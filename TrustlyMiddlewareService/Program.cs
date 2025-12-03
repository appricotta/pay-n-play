using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using Serilog;
using System.Net;
using System.Text;
using System.Transactions;
using TrustlyMiddlewareService;
using TrustlyMiddlewareService.Configuration;
using TrustlyMiddlewareService.Repositories;
using TrustlyMiddlewareService.Services;

//await CasinoApi.TryCreateUser("Mateo", "Lundin", "qwe2@qq.q");
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .Build();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSerilog(lc => lc.ReadFrom.Configuration(builder.Configuration));
builder.Services.AddSingleton<CasinoApi>();
builder.Services.AddSingleton<CarousellerApi>();
builder.Services.AddSingleton<ITrustlySessionRepository, TrustlySessionRepository>();

// Configure payment API options
builder.Services.Configure<PaymentApiConfiguration>(
    builder.Configuration.GetSection("PaymentApi"));
builder.Services.Configure<TrumoApiConfiguration>(
    builder.Configuration.GetSection("TrumoApi"));
builder.Services.Configure<TrustlyApiConfiguration>(
    builder.Configuration.GetSection("TrustlyApi"));

// Register API services
builder.Services.AddScoped<TrumoPnpService>();
builder.Services.AddScoped<TrustlyPnpService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.UseFileServer(new FileServerOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "test")),
    RequestPath = "/test",
    EnableDefaultFiles = true
});



app.MapPost("/trumo/deposit", async ([FromBody] DepositParams deposit, ILogger<Program> logger, ITrustlySessionRepository sessionRepository, HttpContext context, TrumoPnpService trumoApi) =>
{
    try
    {
        logger.LogDebug(deposit.ToString());
        var origin = context.Request.Headers.Origin.ToString();
        // If Origin header is not available, try Referer
        if (string.IsNullOrEmpty(origin))
        {
            origin = context.Request.Headers.Referer.ToString();
        }
        var depositResponse = await trumoApi.Deposite(deposit.Email, deposit.Amount, deposit.Password, deposit.Currency, deposit.Country, deposit.Locale, deposit.FailUrl);
        await sessionRepository.CreateSessionAsync("Trumo", depositResponse.MessageId, deposit.Email, deposit.Currency, deposit.PartnerId, origin);
        return depositResponse;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, null);
        throw;
    }
});

app.MapPost("/trumo/notifications", async ([FromBody] object body, HttpContext context, ILogger<Program> logger, CasinoApi hittikasinoApi, CarousellerApi carousellerApi, ITrustlySessionRepository sessionRepository, TrumoPnpService trumoApi) =>
{
    try
    {
        var notification = JsonConvert.DeserializeObject<dynamic>(body.ToString()!)!;
        string notificationType = notification.type;
        string uuid = notification.UUID;
        logger.LogDebug($"Trumo notification received: {notificationType}. Body: {body.ToString()}");

        if (notificationType == "payerDetails")
        {
            try
            {
                var data = notification.data;
                var payerDetails = data.payerDetails;
                var orderDetails = data.orderDetails;

                string merchantPayerId = payerDetails.merchantPayerID;
                string trumoPayerId = payerDetails.trumoPayerID;
                string merchantOrderId = orderDetails.merchantOrderID;
                string trumoOrderId = orderDetails.trumoOrderID;

                // Get session data from MongoDB
                var sessionData = await sessionRepository.GetSessionAsync(merchantOrderId);

                if (sessionData != null)
                {
                    var casinoDomain = sessionData.RequestOrigin ?? "https://hittikasino.com";
                    string email = sessionData.Email;
                    string password = trumoApi.DeserializeMessageId(merchantOrderId);

                    // Extract KYC details from notification
                    var kycDetails = data.payerDetails;
                    string firstname = kycDetails.firstName;
                    string lastname = kycDetails.lastName;
                    string? birthDate = kycDetails.birthDate;
                    DateOnly? dob = birthDate != null ? DateOnly.Parse(birthDate) : null;
                    string? country = kycDetails.country;
                    string? city = kycDetails.city;
                    string? street = kycDetails.street;
                    string? zipcode = kycDetails.zipcode;

                    var createUserResponse = await hittikasinoApi.TryCreateUser(casinoDomain, firstname, lastname, email, password, dob, country, city, street, zipcode, sessionData.PartnerId);

                    if (createUserResponse.Exists && createUserResponse.UserId != null
                        && await carousellerApi.KeyObtain(new KeyValuePair<string, string>("trumo_uuid", $"{merchantOrderId}:{trumoOrderId}"), sessionData.Currency, firstname, lastname, email, createUserResponse.UserId, dob, country, city, street, zipcode))
                    {
                        logger.LogDebug("Response to PayerDetails notification: proceed");
                        if (createUserResponse.SuccessLoginUrl != null)
                        {
                            await sessionRepository.UpdateSuccessLoginUrlAsync(merchantOrderId, createUserResponse.SuccessLoginUrl);
                        }
                        await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, merchantPayerId, trumoPayerId, merchantOrderId, trumoOrderId, "proceed");
                    }
                    else
                    {
                        logger.LogDebug("Response to PayerDetails notification: cancel");
                        await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, merchantPayerId, trumoPayerId, merchantOrderId, trumoOrderId, "cancel");
                    }
                }
                else
                {
                    logger.LogWarning($"Session data not found for merchantOrderId: {merchantOrderId}. Response: cancel");
                    await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, merchantPayerId, trumoPayerId, merchantOrderId, trumoOrderId, "cancel");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in PayerDetails notification. Response: cancel");
                var data = notification.data;
                var payerDetails = data.payerDetails;
                var orderDetails = data.orderDetails;
                await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, (string)payerDetails.merchantPayerID, (string)payerDetails.trumoPayerID, (string)orderDetails.merchantOrderID, (string)orderDetails.trumoOrderID, "cancel");
            }
        }
        else
        {
            var client = new HttpClient();
            var stringPayload = JsonConvert.SerializeObject(notification);
            var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
            var redirectResp = await client.PostAsync("https://a.papaya.ninja/gate/trumo/", httpContent);
            var content = await redirectResp.Content.ReadAsStringAsync();
            logger.LogDebug(string.Concat("Redirected response content: ", content));
            await context.Response.WriteAsync(content);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in /trumo/notifications endpoint");
        throw;
    }
});

app.MapGet("/success", async (string messageid, ILogger<Program> logger, ITrustlySessionRepository sessionRepository, HttpContext context) =>
{
    try
    {
        var sessionData = await sessionRepository.GetSessionAsync(messageid);
        logger.LogDebug(string.Concat("Success redirect. SessionData: ", sessionData == null ? "null" : JsonConvert.SerializeObject(sessionData)));
        
        if (sessionData == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Session data not found");
            return;
        }
        
        if (string.IsNullOrEmpty(sessionData.SuccessLoginUrl))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("SuccessLoginUrl is missing");
            return;
        }

        var decoded = WebUtility.HtmlDecode(sessionData.SuccessLoginUrl);
        context.Response.Redirect(decoded);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in /success endpoint");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync("Internal server error");
    }
});

app.MapPost("/trustly/deposit", async ([FromBody] DepositParams deposit, ILogger<Program> logger, ITrustlySessionRepository sessionRepository, HttpContext context, TrustlyPnpService trustlyApi) =>
{
    try
    {
        logger.LogDebug(deposit.ToString());
        var origin = context.Request.Headers.Origin.ToString();
        // If Origin header is not available, try Referer
        if (string.IsNullOrEmpty(origin))
        {
            origin = context.Request.Headers.Referer.ToString();
        }
        var depositResponse =  await trustlyApi.Deposite(deposit.Email, deposit.Amount, deposit.Password, deposit.Currency, deposit.Country, deposit.Locale, deposit.FailUrl);
        await sessionRepository.CreateSessionAsync("Trustly", depositResponse.MessageId, deposit.Email, deposit.Currency, deposit.PartnerId, origin);
        return depositResponse;
    } catch (Exception ex)
    {
        logger.LogError(ex, null);
        throw;
    }
});

app.MapPost("/trustly/login", async ([FromBody] DepositParams deposit, ILogger<Program> logger, ITrustlySessionRepository sessionRepository, TrustlyPnpService trustlyApi) =>
{
    try
    {
        logger.LogDebug(deposit.ToString());
        var depositResponse = await trustlyApi.Deposite(deposit.Email, deposit.Amount, deposit.Password, deposit.Currency, deposit.Country, deposit.Locale, deposit.FailUrl);
        await sessionRepository.CreateSessionAsync("Trustly", depositResponse.MessageId, deposit.Email, deposit.Currency, deposit.PartnerId);
        return depositResponse;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, null);
        throw;
    }
});
app.MapPost("/trustly/notifications", async ([FromBody] object body, HttpContext context, ILogger<Program> logger, CasinoApi hittikasinoApi, CarousellerApi carousellerApi, ITrustlySessionRepository sessionRepository, TrustlyPnpService trustlyApi) =>
{
    try
    {
        var notification = JsonConvert.DeserializeObject<dynamic>(body.ToString()!)!;
        string method = notification.method;
        logger.LogDebug(body.ToString()!);
        if (method == "kyc")
        {
            var uuid = (string)notification["params"].uuid;
            try
            {
                var data = notification["params"].data;
                var abortmessage = (string?)data.abortmessage;
                if (abortmessage == null)
                {
                    var attributes = data.attributes;
                    var firstname = (string)attributes.firstname;
                    var lastname = (string)attributes.lastname;
                    var dob = (string?)attributes.dob is { } dobStr ? DateOnly.Parse(dobStr) : (DateOnly?)null;
                    var street = (string?)attributes.street;
                    var zipcode = (string?)attributes.zipcode;
                    var city = (string?)attributes.city;
                    var country = (string?)attributes.country;
                    var messageid = (string)data.messageid;
                    var orderid = (string)data.orderid;

                    // Try to get session data from MongoDB first
                    var sessionData = await sessionRepository.GetSessionAsync(messageid);
                    string currency, email, password;
                    string? partnerId;
                    
                    if (sessionData != null)
                    {
                        // Use data from MongoDB
                        currency = sessionData.Currency;
                        email = sessionData.Email;
                        partnerId = sessionData.PartnerId;
                        // Get password from encrypted messageId
                        password = trustlyApi.DeserializeMessageId(messageid);
                    }
                    else
                    {
                        throw new Exception($"Session data not found for messageId: {messageid}");
                    }

                    var casinoDomain = sessionData.RequestOrigin ?? "https://hittikasino.com";
                    //email = "test19@gmail.com";
                    var createUserResponse = await hittikasinoApi.TryCreateUser(casinoDomain, firstname, lastname, email, password, dob, country, city, street, zipcode, partnerId);
                    if (createUserResponse.Exists && createUserResponse.UserId != null
                        && await carousellerApi.KeyObtain(new KeyValuePair<string, string>("trustly_uuid", orderid), currency, firstname, lastname, email, createUserResponse.UserId, dob, country, city, street, zipcode))
                    {
                        logger.LogDebug(string.Concat("Response to a KYC notification: CONTINUE"));
                        if (createUserResponse.SuccessLoginUrl != null)
                        {
                            await sessionRepository.UpdateSuccessLoginUrlAsync(messageid, createUserResponse.SuccessLoginUrl);
                        }

                        await trustlyApi.Response(context.Response, uuid, "kyc", "CONTINUE");
                        // Clean up session data after successful processing
                        //if (sessionData != null)
                        //{
                        //    await sessionRepository.DeleteSessionAsync(messageid);
                        //}
                    }
                    else
                    {
                        logger.LogDebug(string.Concat("Response to a KYC notification: FINISH"));
                        await trustlyApi.Response(context.Response, uuid, "kyc", "FINISH");
                        // Clean up session data after processing
                        //if (sessionData != null)
                        //{
                        //    await sessionRepository.DeleteSessionAsync(messageid);
                        //}
                    }
                }
                else
                {
                    logger.LogDebug(string.Concat("Abortmessage in KYC notification: ", abortmessage, ". Response: FINISH"));
                    await trustlyApi.Response(context.Response, uuid, "kyc", "FINISH");
                }
            } catch (Exception ex)
            {
                logger.LogError(ex, "Error in KYC notification. Response: FINISH");
                await trustlyApi.Response(context.Response, uuid, "kyc", "FINISH");
            }
        }
        else
        {
            var client = new HttpClient();
            var stringPayload = JsonConvert.SerializeObject(notification);
            var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
            var redirectResp = await client.PostAsync("https://a.papaya.ninja/trustly/gate/mobinc/", httpContent);
            var content = await redirectResp.Content.ReadAsStringAsync();
            logger.LogDebug(string.Concat("Redirected response content: ", content));
            await context.Response.WriteAsync(content);
        }
    } catch (Exception ex)
    {
        logger.LogError(ex, null);
        throw;
    }
});

app.Run();



public record DepositParams(string Email, double Amount, string Password, string Currency, string Country, string Locale, string FailUrl, string PartnerId);
public record LoginParams(string Email, double Amount, string Password, string Currency, string Country, string Locale, string FailUrl);