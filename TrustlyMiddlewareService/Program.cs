using System.Text;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Serilog;
using TrustlyMiddlewareService;

//await HittikasinoApi.TryCreateUser("Mateo", "Lundin", "qwe2@qq.q");

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.MapPost("/trustly/deposit", async ([FromBody] DepositParams deposit, ILogger<Program> logger) =>
{
    try
    {
        logger.LogDebug(deposit.ToString());
        return await TrustlyApi.Deposite(deposit.Email, deposit.Amount, deposit.Currency, deposit.Country, deposit.Locale, deposit.SuccessUrl, deposit.FailUrl);
    } catch (Exception ex)
    {
        logger.LogError(ex, null);
        throw;
    }
});
app.MapPost("/trustly/notifications", async ([FromBody] object body, HttpContext context, ILogger<Program> logger) =>
{
    try
    {
        var notification = JsonConvert.DeserializeObject<dynamic>(body.ToString()!)!;
        string method = notification.method;
        logger.LogDebug(body.ToString()!);
        if (method == "kyc")
        {
            var uuid = (string)notification["params"].uuid;
            var data = notification["params"].data;
            var attributes = data.attributes;
            var firstname = (string)attributes.firstname;
            var lastname = (string)attributes.lastname;
            var messageid = (string)data.messageid;
            var encodedEmail = messageid.Substring(36);
            var email = Encoding.ASCII.GetString(Convert.FromBase64String(encodedEmail));
            if (await HittikasinoApi.TryCreateUser(firstname, lastname, email))
            {
                logger.LogDebug(string.Concat("Respond to a KYC notification: CONTINUE"));
                await TrustlyApi.Response(context.Response, uuid, "kyc", "CONTINUE");
            }
            else
            {
                logger.LogDebug(string.Concat("Respond to a KYC notification: FINISH"));
                await TrustlyApi.Response(context.Response, uuid, "kyc", "FINISH");
            }
        }
        else
        {
            var client = new HttpClient();
            var stringPayload = JsonConvert.SerializeObject(notification);
            var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
            //var redirectResp = await client.PostAsync("https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/trustly/notifications2", httpContent);
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
app.MapPost("/trustly/notifications2", async ([FromBody] object body, HttpContext context, ILogger<Program> logger) =>
{
    try
    {
        logger.LogDebug(body.ToString()!);
        var notification = JsonConvert.DeserializeObject<dynamic>(body.ToString()!)!;
        string method = notification.method;
        var uuid = (string)notification["params"].uuid;
        await TrustlyApi.Response(context.Response, uuid, method, "OK");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, null);
        throw;
    }
});

app.Run();



public record DepositParams(string Email, double Amount, string Currency, string Country, string Locale, string SuccessUrl, string FailUrl);