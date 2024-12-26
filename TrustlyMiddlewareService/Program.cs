using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Serilog;
using TrustlyMiddlewareService;

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
            await TrustlyApi.Response(context.Response, uuid, "kyc", "CONTINUE");
        }
        else
        {
            var client = new HttpClient();
            var stringPayload = JsonConvert.SerializeObject(notification);
            var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
            var redirectResp = await client.PostAsync("https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/trustly/notifications2", httpContent);
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

async Task CreateUser()
{
    var paramsDic = new Dictionary<string, string>
    {
        { "ident", "ma" },
        { "email", "test1@email.com" },
        { "first_name", "testfirstname1" },
        { "last_name", "testlastname1" },
    };
    var plain = string.Concat(string.Concat(paramsDic.OrderBy(x => x.Key).Select(x => string.Concat(x.Key, x.Value))), "8cf2bf68bd066d37bcfaaae26251c365");

    
    byte[] hashmessage = new HMACSHA1().ComputeHash(Encoding.UTF8.GetBytes(plain));
    var t1 = String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2")));

    using (SHA1Managed sha1 = new SHA1Managed())
    {
        var hash2 = sha1.ComputeHash(Encoding.UTF8.GetBytes(plain));
        var sb = new StringBuilder(hash2.Length * 2);

        foreach (byte b in hash2)
        {
            sb.Append(b.ToString("x2")); // x2 is lowercase
        }

        var t2 = sb.ToString().ToLower();
    }

    var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain)));







    paramsDic.Add("sign",hash);


    var p = UrlEncode(paramsDic);

    string url = QueryHelpers.AddQueryString("https://beta.hittikasino.com/registration/api/", paramsDic);
    
    //var client = new HttpClient();
    //var result = await client.PostAsync(url, paramsDic);
    //string resultContent = await result.Content.ReadAsStringAsync();
    
}

static string UrlEncode(IDictionary<string, string> parameters)
{
    var sb = new StringBuilder();
    foreach (var val in parameters)
    {
        // add each parameter to the query string, url-encoding the value.
        sb.AppendFormat("{0}={1}&", val.Key, HttpUtility.UrlEncode(val.Value));
    }
    sb.Remove(sb.Length - 1, 1); // remove last '&'
    return sb.ToString();
}

public record DepositParams(string Email, double Amount, string Currency, string Country, string Locale, string SuccessUrl, string FailUrl);