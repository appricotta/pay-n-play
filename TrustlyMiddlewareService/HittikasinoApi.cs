using Microsoft.AspNetCore.WebUtilities;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace TrustlyMiddlewareService;

public class HittikasinoApi
{
    public static async Task<bool> TryCreateUser(string firstName, string lastName, string email)
    {
        var checkUserResponse = await CheckUser(firstName, lastName, email);
        if (checkUserResponse.Exists == true)
        {
            return true;
        }

        if (checkUserResponse.Valid == true && checkUserResponse.Errors == 0)
        {
            await CreateUser(firstName, lastName, email);
            return true;
        }

        return false;

    }

    private static async Task CreateUser(string firstName, string lastName, string email)
    {
        var paramsDic = GetParams(firstName, lastName, email);
        var plain = string.Concat(paramsDic["email"], "8cf2bf68bd066d37bcfaaae26251c365");
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain))).ToLower();
        var registrationUrl = QueryHelpers.AddQueryString(String.Concat("https://beta.hittikasino.com/a/pr/ma/", hash, "/"), paramsDic);
        var authStr = Convert.ToBase64String(Encoding.UTF8.GetBytes($"123:123"));
        var client = new HttpClient();
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, registrationUrl);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", authStr);
        requestMessage.Headers.Add("User-Agent", "TrustlyMiddlewareService");
        await client.SendAsync(requestMessage);
    }

    private static async Task<CheckUserResponse> CheckUser(string firstName, string lastName, string email)
    {
        try
        {
            var paramsDic = GetParams(firstName, lastName, email);
            var plain = string.Concat(string.Concat(paramsDic.OrderBy(x => x.Key).Select(x => string.Concat(x.Key, x.Value))), "8cf2bf68bd066d37bcfaaae26251c365");
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain))).ToLower();
            paramsDic.Add("sign", hash);
            string url = QueryHelpers.AddQueryString("https://beta.hittikasino.com/registration/api/", paramsDic);
            var client = new HttpClient();
            var authStr = Convert.ToBase64String(Encoding.UTF8.GetBytes($"123:123"));
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", authStr);
            var response = await client.SendAsync(requestMessage);
            string responseContent = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<dynamic>(responseContent);
            return new CheckUserResponse((bool?)responseObj.exists, (bool?)responseObj.valid, (int)responseObj.error);
        } catch
        {
            return new CheckUserResponse(false, false, 1);
        }
    }

    private static Dictionary<string, string> GetParams(string firstName, string lastName, string email)
    {
        var paramsDic = new Dictionary<string, string>
        {
            { "ident", "ma" },
            { "email", email },
            { "first_name", firstName },
            { "last_name", lastName },
        };
        return paramsDic;
    }

    record CheckUserResponse(bool? Exists, bool? Valid, int Errors);
}