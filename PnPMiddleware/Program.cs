using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using Serilog;
using System.Net;
using System.Text;
using PnPMiddleware.Configuration;
using PnPMiddleware.Repositories;
using PnPMiddleware.Services;
using PnPMiddleware.Models;
using MongoDB.Bson;

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
builder.Services.AddSingleton<CasinoService>();
builder.Services.AddSingleton<CarousellerService>();
builder.Services.AddSingleton<TrustlySessionRepository, TrustlySessionRepository>();
builder.Services.AddSingleton<LogRepository, LogRepository>();

// Configure payment API options
builder.Services.Configure<PaymentApiConfiguration>(builder.Configuration.GetSection("PaymentApi"));
builder.Services.Configure<TrumoApiConfiguration>(builder.Configuration.GetSection("TrumoApi"));
builder.Services.Configure<TrustlyApiConfiguration>(builder.Configuration.GetSection("TrustlyApi"));

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

app.MapPost("/trumo/deposit", async ([FromBody] DepositParams deposit, ILogger<Program> logger, TrustlySessionRepository sessionRepository, HttpContext context, TrumoPnpService trumoApi) =>
{
    try
    {
        var origin = context.Request.Headers.Origin.ToString();
        // If Origin header is not available, try Referer
        if (string.IsNullOrEmpty(origin))
        {
            origin = context.Request.Headers.Referer.ToString();
        }
        var depositResponse = await trumoApi.Deposite(deposit.Email, deposit.Amount, deposit.Password, deposit.Currency, deposit.Country, deposit.Locale, deposit.FailUrl);
        logger.LogInformation("Trumo deposit request processed for MessageId {MessageId}, Currency {Currency}, Amount {Amount}", depositResponse.MessageId, deposit.Currency, deposit.Amount);
        logger.LogDebug("Trumo deposit details for MessageId {MessageId}: {@Deposit}", depositResponse.MessageId, deposit);
        await sessionRepository.CreateSessionAsync("Trumo", depositResponse.MessageId, deposit.Email, deposit.Currency, deposit.PartnerId, origin);
        return depositResponse;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process Trumo deposit request");
        throw;
    }
});

app.MapPost("/trumo/notifications", async (HttpContext context, ILogger<Program> logger, CasinoService hittikasinoApi, CarousellerService carousellerApi, TrustlySessionRepository sessionRepository, TrumoPnpService trumoApi) =>
{
    try
    {
        context.Request.EnableBuffering();

        string requestBody;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        var notification = JsonConvert.DeserializeObject<dynamic>(requestBody)!;
        string notificationType = notification.type;
        string uuid = notification.UUID;

        var data = notification.data;
        var orderDetails = data?.orderDetails;
        string? merchantOrderId = orderDetails?.merchantOrderID;
        logger.LogInformation("Trumo PayerDetails notification received for MessageId {MessageId}", merchantOrderId);
        logger.LogDebug("Trumo notification body for MessageId {MessageId}: {RequestBody}", merchantOrderId, requestBody);

        if (notificationType == "payerDetails")
        {
            try
            {
                var payerDetails = data!.payerDetails;

                string merchantPayerId = payerDetails.merchantPayerID;
                string trumoPayerId = payerDetails.trumoPayerID;
                string trumoOrderId = orderDetails!.trumoOrderID;

                
                // Get session data from MongoDB
                var sessionData = await sessionRepository.GetSessionAsync(merchantOrderId!);

                if (sessionData != null)
                {
                    var casinoDomain = sessionData.RequestOrigin ?? "https://hittikasino.com";
                    string email = sessionData.Email;
                    string password = trumoApi.GetPasswordFromMessageId(merchantOrderId!);

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

                    var createUserResponse = await hittikasinoApi.TryCreateUser(casinoDomain, firstname, lastname, email, password, dob, country, city, street, zipcode, sessionData.PartnerId, merchantOrderId!);

                    if (createUserResponse.Exists && createUserResponse.UserId != null
                        && await carousellerApi.KeyObtain(new KeyValuePair<string, string>("trumo_uuid", $"{merchantOrderId}:{trumoOrderId}"), sessionData.Currency, firstname, lastname, email, createUserResponse.UserId, dob, country, city, street, zipcode, merchantOrderId))
                    {
                        logger.LogInformation("PayerDetails notification approved for MessageId {MessageId}", merchantOrderId);
                        if (createUserResponse.SuccessLoginUrl != null)
                        {
                            await sessionRepository.UpdateSuccessLoginUrlAsync(merchantOrderId, createUserResponse.SuccessLoginUrl);
                        }
                        await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, merchantPayerId, trumoPayerId, merchantOrderId, trumoOrderId, "proceed");
                    }
                    else
                    {
                        logger.LogWarning("PayerDetails notification cancelled for MessageId {MessageId}, user creation or key obtain failed", merchantOrderId);
                        await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, merchantPayerId, trumoPayerId, merchantOrderId!, trumoOrderId, "cancel");
                    }
                }
                else
                {
                    logger.LogWarning("Session data not found for MessageId {MessageId}, PayerDetails notification cancelled", merchantOrderId);
                    await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, merchantPayerId, trumoPayerId, merchantOrderId!, trumoOrderId, "cancel");
                }
            }
            catch (Exception ex)
            {
                var payerDetails = data!.payerDetails;
                var errorTrumoOrderId = (string?)orderDetails?.trumoOrderID;
                logger.LogError(ex, "Error in PayerDetails notification for MessageId {MessageId}. Response: cancel", merchantOrderId);
                await trumoApi.RespondToPayerDetailsNotification(context.Response, uuid, (string)payerDetails.merchantPayerID, (string)payerDetails.trumoPayerID, merchantOrderId ?? "", errorTrumoOrderId ?? "", "cancel");
            }
        }
        else if (notificationType == "orderStatus" && notification.data.orderDetails.type == "deposit" && notification.data.orderDetails.status == "initiated")
        {
            var payerDetails = data!.payerDetails;

            string merchantPayerId = payerDetails.merchantPayerID;
            string trumoPayerId = payerDetails.trumoPayerID;
            string trumoOrderId = orderDetails!.trumoOrderID;
            await trumoApi.RespondToOrderStatusNotification(context.Response, uuid, merchantPayerId, trumoPayerId, merchantOrderId!, trumoOrderId);
            logger.LogInformation("OrderStatus notification processed for MessageId {MessageId}", merchantOrderId);
        }
        else
        {
            logger.LogInformation("Trumo notification redirected to papaya.ninja for MessageId {MessageId}", merchantOrderId);
            var client = new HttpClient();
            var stringPayload = JsonConvert.SerializeObject(notification);
            var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
            var redirectResp = await client.PostAsync("https://a.papaya.ninja/gate/trumo/", httpContent);
            var content = await redirectResp.Content.ReadAsStringAsync();
            logger.LogDebug("Redirected response content for MessageId {MessageId}: {Content}", merchantOrderId, content);
            await context.Response.WriteAsync(content);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in /trumo/notifications endpoint");
        throw;
    }
});

app.MapGet("/success", async (string messageid, ILogger<Program> logger, TrustlySessionRepository sessionRepository, HttpContext context) =>
{
    try
    {
        var sessionData = await sessionRepository.GetSessionAsync(messageid);
        logger.LogInformation("Success redirect for messageId {MessageId}, currency {Currency}", messageid, sessionData?.Currency);
        logger.LogDebug("Success redirect session data: {@SessionData}", sessionData);

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

        //context.Response.Redirect(decoded);

        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync($@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1'>
            <title>Ohjataan...</title>
            <style>
                body {{ 
                    font-family: -apple-system, sans-serif; 
                    text-align: center; 
                    padding-top: 50px; 
                    background-color: #fff;
                    color: #333;
                }}
                .spinner {{
                    margin: 0 auto 20px;
                    width: 40px;
                    height: 40px;
                    border: 4px solid #f3f3f3;
                    border-top: 4px solid #007bff;
                    border-radius: 50%;
                    animation: s 1s linear infinite;
                }}
                @keyframes s {{ 0% {{ transform: rotate(0deg); }} 100% {{ transform: rotate(360deg); }} }}
                .btn {{
                    display: inline-block; 
                    padding: 12px 30px; 
                    background-color: #007bff;
                    color: white; 
                    text-decoration: none; 
                    border-radius: 6px; 
                    font-weight: bold;
                    margin-top: 20px;
                }}
            </style>
        </head>
        <body>
            <div class='spinner'></div>
            <h3>Maksu onnistui</h3>
            <a id='lnk' href='{decoded}' target='_top' class='btn'>Jatkaa</a>

            <script>
                var u = '{decoded}';
                window.onload = function() {{
                    try {{ window.parent.postMessage({{ type: 'PAYMENT_SUCCESS', url: u }}, '*'); }} catch(e){{}}
                    try {{ if (window.top) window.top.location.href = u; }} catch(e){{}}
                    setTimeout(function() {{ try {{ document.getElementById('lnk').click(); }} catch(e){{}} }}, 100);
                }};
            </script>
        </body>
        </html>");

    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in /success endpoint");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync("Internal server error");
    }
});

app.MapPost("/trustly/deposit", async ([FromBody] DepositParams deposit, ILogger<Program> logger, TrustlySessionRepository sessionRepository, HttpContext context, TrustlyPnpService trustlyApi) =>
{
    try
    {
        var origin = context.Request.Headers.Origin.ToString();
        // If Origin header is not available, try Referer
        if (string.IsNullOrEmpty(origin))
        {
            origin = context.Request.Headers.Referer.ToString();
        }
        var depositResponse = await trustlyApi.Deposite(deposit.Email, deposit.Amount, deposit.Password, deposit.Currency, deposit.Country, deposit.Locale, deposit.FailUrl);
        logger.LogInformation("Trustly deposit request processed for MessageId {MessageId}, Currency {Currency}, Amount {Amount}", depositResponse.MessageId, deposit.Currency, deposit.Amount);
        logger.LogDebug("Trustly deposit details for MessageId {MessageId}: {@Deposit}", depositResponse.MessageId, deposit);
        await sessionRepository.CreateSessionAsync("Trustly", depositResponse.MessageId, deposit.Email, deposit.Currency, deposit.PartnerId, origin);
        return depositResponse;
    } catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process Trustly deposit request");
        throw;
    }
});

app.MapPost("/trustly/notifications", async ([FromBody] object body, HttpContext context, ILogger<Program> logger, CasinoService hittikasinoApi, CarousellerService carousellerApi, TrustlySessionRepository sessionRepository, TrustlyPnpService trustlyApi) =>
{
    try
    {
        var notification = JsonConvert.DeserializeObject<dynamic>(body.ToString()!)!;
        string method = notification.method;
        var data = notification["params"].data;
        var messageid = (string?)data.messageid;

        logger.LogInformation("Trustly {Method} notification received for MessageId {MessageId}", method, messageid);
        logger.LogDebug("Trustly notification body for MessageId {MessageId}: {@Body}", messageid, body);

        if (method == "kyc")
        {
            var uuid = (string)notification["params"].uuid;
            try
            {
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
                        password = trustlyApi.GetPasswordFromMessageId(messageid);
                    }
                    else
                    {
                        throw new Exception($"Session data not found for messageId: {messageid}");
                    }

                    var casinoDomain = sessionData.RequestOrigin ?? "https://hittikasino.com";
                    //email = "test19@gmail.com";
                    var createUserResponse = await hittikasinoApi.TryCreateUser(casinoDomain, firstname, lastname, email, password, dob, country, city, street, zipcode, partnerId, messageid);
                    if (createUserResponse.Exists && createUserResponse.UserId != null
                        && await carousellerApi.KeyObtain(new KeyValuePair<string, string>("trustly_uuid", orderid), currency, firstname, lastname, email, createUserResponse.UserId, dob, country, city, street, zipcode, messageid))
                    {
                        logger.LogInformation("KYC notification approved for messageId {MessageId}", messageid);
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
                        logger.LogWarning("KYC notification rejected for messageId {MessageId}, user creation or key obtain failed", messageid);
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
                    logger.LogWarning("KYC notification aborted for messageId {MessageId}, hasAbortMessage {HasAbortMessage}",
                        messageid, !string.IsNullOrEmpty(abortmessage));
                    logger.LogDebug("KYC notification abort message: {AbortMessage}", abortmessage);
                    await trustlyApi.Response(context.Response, uuid, "kyc", "FINISH");
                }
            } catch (Exception ex)
            {
                var errorMessageId = (string?)notification["params"]?.data?.messageid;
                logger.LogError(ex, "Error in KYC notification for MessageId {MessageId}. Response: FINISH", errorMessageId);
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
            logger.LogInformation("Trustly notification redirected to papaya.ninja for MessageId {MessageId}", messageid);
            logger.LogDebug("Redirected response content: {Content}", content);
            await context.Response.WriteAsync(content);
        }
    } catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process Trustly notification");
        throw;
    }
});

// Deposit Trace API endpoint
app.MapGet("/api/deposit/trace", async (
    string? messageId,
    ILogger<Program> logger,
    LogRepository logRepository,
    TrustlySessionRepository sessionRepository) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return Results.BadRequest(new { error = "messageId parameter is required" });
        }

        logger.LogInformation("Deposit trace requested for MessageId {MessageId}", messageId);

        // Get logs from MongoDB
        var logs = await logRepository.GetLogsByMessageIdAsync(messageId);

        if (!logs.Any())
        {
            logger.LogWarning("No logs found for MessageId {MessageId}", messageId);
            return Results.NotFound(new { error = "No logs found for the provided messageId" });
        }

        // Get session data if available
        var sessionData = await sessionRepository.GetSessionAsync(messageId);

        // Build response
        var response = new DepositTraceResponse
        {
            MessageId = messageId,
            PaymentProvider = sessionData?.PaymentProvider,
            Email = sessionData?.Email,
            Currency = sessionData?.Currency,
            SessionCreatedAt = sessionData?.CreatedAt,
            Timeline = logs.Select(log => new TraceEvent
            {
                Timestamp = log.UtcTimeStamp,
                Level = log.Level,
                Message = log.RenderedMessage,
                Details = ExtractProperties(log.Properties),
                Exception = ExtractException(log.Exception)
            }).ToList()
        };

        logger.LogInformation("Deposit trace returned {EventCount} events for MessageId {MessageId}",
            response.Timeline.Count, messageId);

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving deposit trace for MessageId {MessageId}", messageId);
        return Results.Problem("An error occurred while retrieving the deposit trace");
    }
})
.WithName("GetDepositTrace")
.WithOpenApi();

app.Run();

static Dictionary<string, object> ExtractProperties(BsonDocument properties)
{
    var result = new Dictionary<string, object>();

    foreach (var element in properties.Elements)
    {
        // Skip system properties
        if (element.Name.StartsWith("_") || element.Name == "SourceContext")
            continue;

        // Convert BsonValue to appropriate C# type
        result[element.Name] = BsonTypeMapper.MapToDotNetValue(element.Value);
    }

    return result;
}

static string? ExtractException(BsonDocument? exception)
{
    if (exception == null)
        return null;

    // Serilog stores exception as a document with Message, StackTraceString, etc.
    var message = exception.Contains("Message") ? exception["Message"].AsString : null;
    var stackTrace = exception.Contains("StackTraceString") ? exception["StackTraceString"].AsString : null;

    if (message != null && stackTrace != null)
        return $"{message}\n{stackTrace}";

    return message ?? exception.ToString();
}

public record DepositParams(string Email, double Amount, string Password, string Currency, string Country, string Locale, string FailUrl, string PartnerId);