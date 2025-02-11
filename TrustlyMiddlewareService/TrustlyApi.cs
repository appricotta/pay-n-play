using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System.Net;

namespace TrustlyMiddlewareService;

public class TrustlyApi
{
    public static async Task<DepositResponse> Deposite(string email, double amount, string currency, string country, string locale, string successUrl, string failUrl)
    {
        var body = GetDepositRequestBody(email, amount, currency, country, locale, successUrl, failUrl);
        string plaintext = GetPlaintext((string)body.method, (string)body["params"].UUID, ((JObject)body["params"].Data).ToObject<Dictionary<string, object>>()!);
        string signed = Sign(plaintext);
        body["params"].Signature = signed;
        var responseContent = await Post(body);
        dynamic responseData = ((dynamic)JsonConvert.DeserializeObject(responseContent)).result.data;
        return new DepositResponse((string)responseData.url, (string)responseData.orderid);
    }
    public static async Task Response(HttpResponse response, string uuid, string method, string status)
    {
        dynamic body = GetResponseBody(uuid, method, status);
        string plaintext = GetPlaintext((string)body["result"].method, (string)body["result"].uuid, ((JObject)body["result"].data).ToObject<Dictionary<string, object>>()!);
        string signed = Sign(plaintext);
        body["result"].signature = signed;
        string json = JsonConvert.SerializeObject(body);
        response.Headers.Append("User-Agent", "trustly-api-client/0.0.9");
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.WriteAsync(json);
    }

    static dynamic GetResponseBody(string uuid, string method, string status)
    {
        var json = @$"{{
    ""result"": {{
        ""signature"": """",
        ""uuid"": ""{uuid}"",
        ""method"": ""{method}"",
        ""data"": {{
            ""status"": ""{status}""
        }}
    }},
    ""version"":""1.1""
}}";
        return JsonConvert.DeserializeObject<dynamic>(json)!;
    }

    static dynamic GetDepositRequestBody(string email, double amount, string currency, string country, string locale, string successUrl, string failUrl)
    {
        var uuid = Guid.NewGuid().ToString();
        var notificationUrl = "https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/trustly/notifications";
        var encodedData = Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Concat(currency,email)));
        var messageId = string.Concat(Guid.NewGuid().ToString(), encodedData);
        var json = @$"{{
  ""method"": ""Deposit"",
  ""params"": {{
    ""Signature"": """",
    ""UUID"": ""{uuid}"",
    ""Data"": {{
      ""Username"": ""hitticasino_pnph"",
      ""Password"": ""72546f67-8e45-47e6-93d6-af8bf40b0c9b"",
      ""NotificationURL"": ""{notificationUrl}"",
      ""EndUserID"": ""{email}"",
      ""MessageID"": ""{messageId}"",
      ""Attributes"": {{
        ""Locale"": ""{locale}"",
        ""Country"": ""{country}"",
        ""Currency"": ""{currency}"",
        ""Amount"": ""{amount}"",
        ""Firstname"": ""John"",
        ""Lastname"": ""Doe"",
        ""Email"": ""{email}"",
        ""ShopperStatement"": ""Mobile Incorporated Ltd"",
        ""SuccessURL"": ""{successUrl}"",
        ""FailURL"": ""{failUrl}"",
        ""RequestKYC"": ""1""
      }}
    }}
  }},
  ""version"": ""1.1""
}}";
        return JsonConvert.DeserializeObject<dynamic>(json)!;
    }
    static string GetPlaintext(string method, string uuid, Dictionary<string, object> data)
    {
        var plaintextBuilder = new StringBuilder();
        plaintextBuilder.Append(method);
        plaintextBuilder.Append(uuid);
        foreach (var property in data.OrderBy(x => x.Key))
        {
            plaintextBuilder.Append(property.Key);
            if (property.Value is JObject attributesJObj)
            {
                Dictionary<string, object> attributesDic = attributesJObj.ToObject<Dictionary<string, object>>()!;
                foreach (var attribute in attributesDic.OrderBy(x => x.Key))
                {
                    plaintextBuilder.Append(attribute.Key);
                    plaintextBuilder.Append(attribute.Value);
                }
            }
            else
            {
                plaintextBuilder.Append(property.Value);
            }
        }
        return plaintextBuilder.ToString();
    }

    static string Sign(string plainText)
    {
        var privateKeyText = EmbeddedResource.GetText("private.pem");
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyText);
        byte[] fileBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] signature = rsa.SignData(fileBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    static bool Verify(string hash, string plainText)
    {
        var rsa = RSA.Create();
        var publicKeyText = EmbeddedResource.GetText("trustly_public_test.pem");
        rsa.ImportFromPem(publicKeyText);
        return rsa.VerifyData(Encoding.UTF8.GetBytes(plainText), Convert.FromBase64String(hash), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
    }

    static async Task<string> Post(dynamic body)
    {
        string json = JsonConvert.SerializeObject(body);
        var client = new HttpClient();
        var data = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("https://test.trustly.com/api/1", data);
        string responseContent = await response.Content.ReadAsStringAsync();
        dynamic result = ((dynamic)JsonConvert.DeserializeObject(responseContent)).result;
        string plaintext = GetPlaintext((string)result.method, (string)result.uuid, ((JObject)result.data).ToObject<Dictionary<string, object>>());
        if (Verify((string)result.signature, plaintext))
        {
            return responseContent;
        }

        throw new Exception("Signature is not valid.");
    }
}

public record DepositResponse(string Url, string OrderId);