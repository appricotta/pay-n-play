using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrustlyMiddlewareService.Configuration;
using TrustlyMiddlewareService.Models;

namespace TrustlyMiddlewareService.Services;

public class TrustlyPnpService : PnpServiceBase
{
    private readonly TrustlyApiConfiguration _config;

    public TrustlyPnpService(IOptions<PaymentApiConfiguration> baseConfig, IOptions<TrustlyApiConfiguration> trustlyConfig)
        : base(baseConfig.Value)
    {
        _config = trustlyConfig.Value;
    }

    protected override string PrivateKeyFileName => _config.PrivateKeyFileName;
    protected override string PublicKeyFileName => _config.PublicKeyFileName;
    protected override HashAlgorithmName SignHashAlgorithm => HashAlgorithmName.SHA1;
    protected override HashAlgorithmName VerifyHashAlgorithm => HashAlgorithmName.SHA1;

    public async Task<DepositResponse> Deposite(string email, double amount, string password, string currency, string country, string locale, string failUrl)
    {
        var messageId = GenerateMessageId(password);
        var body = await GetDepositRequestBody(messageId, email, amount, currency, country, locale, failUrl);
        string plaintext = GetPlaintext((string)body.method, (string)body["params"].UUID, ((JObject)body["params"].Data).ToObject<Dictionary<string, object>>()!);
        string signed = Sign(plaintext);
        body["params"].Signature = signed;
        var responseContent = await Post(body);
        dynamic responseData = ((dynamic)JsonConvert.DeserializeObject(responseContent)).result.data;
        return new DepositResponse((string)responseData.url, (string)responseData.orderid, messageId);
    }

    public async Task Response(HttpResponse response, string uuid, string method, string status)
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

    dynamic GetResponseBody(string uuid, string method, string status)
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

    async Task<dynamic> GetDepositRequestBody(string messageId, string email, double amount, string currency, string country, string locale, string failUrl)
    {
        var uuid = Guid.NewGuid().ToString();
        var notificationUrl = _config.NotificationUrl;
        var successUrl = $"https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/success?messageid={messageId}";
        var userId = HashEndUserId(email);
        var json = @$"{{
  ""method"": ""Deposit"",
  ""params"": {{
    ""Signature"": """",
    ""UUID"": ""{uuid}"",
    ""Data"": {{
      ""Username"": ""{_config.Username}"",
      ""Password"": ""{_config.Password}"",
      ""NotificationURL"": ""{notificationUrl}"",
      ""EndUserID"": ""{userId}"",
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

    string GetPlaintext(string method, string uuid, Dictionary<string, object> data)
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

    async Task<string> Post(dynamic body)
    {
        string json = JsonConvert.SerializeObject(body);
        var client = new HttpClient();
        var data = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(_config.ApiBaseUrl, data);
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
