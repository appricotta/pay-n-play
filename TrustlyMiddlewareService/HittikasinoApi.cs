using Microsoft.AspNetCore.WebUtilities;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System;
using System.Net;

namespace TrustlyMiddlewareService;

public class HittikasinoApi
{
    private readonly ILogger<HittikasinoApi> _logger;
    private const string _ident = "ptz";
    private const string _secret = "f7416c9ce56839cdad1db0e3daf37161";

    public HittikasinoApi(ILogger<HittikasinoApi> logger)
    {
        _logger = logger;
    }

    public async Task<CreateUserResponse> TryCreateUser(string firstName, string lastName, string email, string password, DateOnly? dob, string? country, string? city, string? street, string? zip, string? partnerId = null)
    {
        var checkUserResponse = await CheckUser(firstName, lastName, email, password, dob, country, city, street, zip);
        if (checkUserResponse.Exists == true || (checkUserResponse.Valid == true && checkUserResponse.Errors == 0))
        {
            return await CreateUser(firstName, lastName, email, password, dob, country, city, street, zip, partnerId);
        }
        return new CreateUserResponse(false, null, null);

    }

    private async Task<CreateUserResponse> CreateUser(string firstName, string lastName, string email, string password, DateOnly? dob, string? country, string? city, string? street, string? zip, string? partnerId = null)
    {
        var paramsDic = GetParams(firstName, lastName, email, password, dob, country, city, street, zip);
        paramsDic.Add("user_id","on");
        paramsDic.Add("av_check", "true");
        
        // Add partner parameter if provided
        if (!string.IsNullOrEmpty(partnerId))
        {
            paramsDic.Add("partner", partnerId);
        }
        
        var plain = string.Concat(paramsDic["email"], _secret);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain))).ToLower();
        var registrationUrl = QueryHelpers.AddQueryString(String.Concat("https://hittikasino.com/a/pr/", _ident, "/", hash, "/"), paramsDic);
        _logger.LogDebug($"Create user request: {registrationUrl}");
        var authStr = Convert.ToBase64String(Encoding.UTF8.GetBytes($"123:123"));
        var client = CreateHttpClient();
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, registrationUrl);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", authStr);
        requestMessage.Headers.Add("User-Agent", "TrustlyMiddlewareService");
        var response = await client.SendAsync(requestMessage);
        string responseContent = await response.Content.ReadAsStringAsync();
        _logger.LogDebug($"Create user. Response code: {response.StatusCode}. Response content: {responseContent}");
        var responseObj = JsonConvert.DeserializeObject<dynamic>(responseContent);
        return new CreateUserResponse(true, (string)responseObj.user_id, (string)responseObj.autologin);
    }

    private static HttpClient CreateHttpClient()
    {
        var proxy = new WebProxy("brd.superproxy.io:33335")
        {
            Credentials = new NetworkCredential("brd-customer-hl_b6fd6f46-zone-datacenter_proxy1-country-fi", "s20y2zou472i")
        };
        var httpClientHandler = new HttpClientHandler
        {
            Proxy = proxy,
            UseProxy = true
        };
        var client = new HttpClient(httpClientHandler);
        return client;
    }

    private async Task<CheckUserResponse> CheckUser(string firstName, string lastName, string email, string password, DateOnly? dob, string? country, string? city, string? street, string? zip)
    {
        try
        {
            var paramsDic = GetParams(firstName, lastName, email, password, dob, country, city, street, zip);
            var plain = string.Concat(string.Concat(paramsDic.OrderBy(x => x.Key).Select(x => string.Concat(x.Key, x.Value))), _secret);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain))).ToLower();
            paramsDic.Add("sign", hash);
            string url = QueryHelpers.AddQueryString("https://hittikasino.com/registration/api/", paramsDic);
            _logger.LogDebug($"Check user request: {url}");
            var client = CreateHttpClient();
            var authStr = Convert.ToBase64String(Encoding.UTF8.GetBytes($"123:123"));
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", authStr);
            var response = await client.SendAsync(requestMessage);
            string responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug($"Check user response: {responseContent}");
            var responseObj = JsonConvert.DeserializeObject<dynamic>(responseContent);
            return new CheckUserResponse((bool?)responseObj.exists, (bool?)responseObj.valid, (int)responseObj.error);
        } catch (Exception ex)
        {
            _logger.LogError(ex, $"Check user response error: {ex.Message}");
            return new CheckUserResponse(false, false, 1);
        }
    }

    private static Dictionary<string, string?> GetParams(string firstName, string lastName, string email, string password, DateOnly? dob, string? country, string? city, string? street, string? zip)
    {
        var paramsDic = new Dictionary<string, string?>
        {
            { "ident", _ident },
            { "email", email },
            { "first_name", firstName },
            { "last_name", lastName },
            { "password", password },
        };
        if (dob.HasValue)
        {
            paramsDic.Add("birth_day", dob.Value.Day.ToString());
            paramsDic.Add("birth_month", dob.Value.Month.ToString());
            paramsDic.Add("birth_year", dob.Value.Year.ToString());
        }

        if (country != null)
        {
            paramsDic.Add("address_country", country);
        }
        if (city != null)
        {
            paramsDic.Add("address_city", city);
        }
        if (street != null)
        {
            paramsDic.Add("address_street", street);
        }
        if (zip != null)
        {
            paramsDic.Add("address_zip", zip);
        }
        return paramsDic;
    }

    record CheckUserResponse(bool? Exists, bool? Valid, int Errors);

    public record CreateUserResponse(bool Exists, string? UserId, string? SuccessLoginUrl);
}