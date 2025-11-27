using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrustlyMiddlewareService.Configuration;
using TrustlyMiddlewareService.Models;

namespace TrustlyMiddlewareService.Services;

public class TrumoPnpService : PnpServiceBase
{
    private readonly TrumoApiConfiguration _config;

    public TrumoPnpService(IOptions<PaymentApiConfiguration> baseConfig, IOptions<TrumoApiConfiguration> trumoConfig)
        : base(baseConfig.Value)
    {
        _config = trumoConfig.Value;
    }

    protected override string PrivateKeyFileName => _config.PrivateKeyFileName;
    protected override string PublicKeyFileName => _config.PublicKeyFileName;
    protected override HashAlgorithmName SignHashAlgorithm => HashAlgorithmName.SHA256;
    protected override HashAlgorithmName VerifyHashAlgorithm => HashAlgorithmName.SHA256;

    public async Task<DepositResponse> Deposite(string email, double amount, string password, string currency, string country, string locale, string failUrl)
    {
        var messageId = GenerateMessageId(password);
        var body = await GetDepositRequestBody(messageId, amount, currency, country, locale, failUrl);
        string plaintext = GetPlaintext("/v1/deposit", (string)body.UUID, ((JObject)body["data"]).ToObject<Dictionary<string, object>>()!);
        string signed = Sign(plaintext);
        body["signature"] = signed;
        var responseContent = await Post("deposit", body);
        dynamic responseData = ((dynamic)JsonConvert.DeserializeObject(responseContent)).data.orderDetails;
        return new DepositResponse((string)responseData.url, (string)responseData.trumoOrderID, messageId);
    }

    public async Task<DepositResponse> Login(string email, string password, string country, string locale, string successUrl, string failUrl)
    {
        var messageId = GenerateMessageId(password);
        var body = await GetLoginRequestBody(messageId, email, country, locale, successUrl, failUrl);
        string plaintext = GetPlaintext("/v1/login", (string)body.UUID, ((JObject)body["data"]).ToObject<Dictionary<string, object>>()!);
        string signed = Sign(plaintext);
        body["signature"] = signed;
        var responseContent = await Post("login", body);
        dynamic responseData = ((dynamic)JsonConvert.DeserializeObject(responseContent)).data.orderDetails;
        return new DepositResponse((string)responseData.url, (string)responseData.trumoOrderID, messageId);
    }

    public async Task<string> Withdraw(string merchantPayerId, string trumoPayerId, string merchantOrderId, string amount, string currency, string trumoBankAccountId)
    {
        var body = GetWithdrawalRequestBody(merchantPayerId, trumoPayerId, merchantOrderId, amount, currency, trumoBankAccountId);
        string plaintext = GetPlaintext("/v1/withdraw", (string)body.UUID, ((JObject)body["data"]).ToObject<Dictionary<string, object>>()!);
        string signed = Sign(plaintext);
        body["signature"] = signed;
        var responseContent = await Post("withdraw", body);
        dynamic responseData = ((dynamic)JsonConvert.DeserializeObject(responseContent)).data.orderDetails;
        return (string)responseData.trumoOrderID;
    }

    public async Task<string> Refund(string merchantPayerId, string trumoPayerId, string merchantOrderId, string trumoOrderId, string amount, string currency)
    {
        var body = GetRefundRequestBody(merchantPayerId, trumoPayerId, merchantOrderId, trumoOrderId, amount, currency);
        string plaintext = GetPlaintext("/v1/refund", (string)body.UUID, ((JObject)body["data"]).ToObject<Dictionary<string, object>>()!);
        string signed = Sign(plaintext);
        body["signature"] = signed;
        var responseContent = await Post("refund", body);
        dynamic responseData = ((dynamic)JsonConvert.DeserializeObject(responseContent)).data.orderDetails;
        return (string)responseData.trumoOrderID;
    }

    // Notification Response: PayerDetails with "proceed" decision
    public async Task RespondToPayerDetailsNotification(
        HttpResponse response,
        string uuid,
        string merchantPayerId,
        string trumoPayerId,
        string merchantOrderId,
        string trumoOrderId,
        string decision = "proceed",
        string? minAmount = null,
        string? maxAmount = null)
    {
        var responseData = new Dictionary<string, object>
        {
            ["UUID"] = uuid,
            ["type"] = "payerDetails",
            ["data"] = new Dictionary<string, object>
            {
                ["response"] = decision,
                ["payerDetails"] = new Dictionary<string, object>
                {
                    ["merchantPayerID"] = merchantPayerId,
                    ["trumoPayerID"] = trumoPayerId
                },
                ["orderDetails"] = new Dictionary<string, object>
                {
                    ["merchantOrderID"] = merchantOrderId,
                    ["trumoOrderID"] = trumoOrderId
                }
            }
        };

        // Add amount limits if proceeding with limit
        if (decision == "proceedWithLimit" && responseData["data"] is Dictionary<string, object> dataDict)
        {
            var orderDetails = (Dictionary<string, object>)dataDict["orderDetails"];
            if (minAmount != null) orderDetails["minAmount"] = minAmount;
            if (maxAmount != null) orderDetails["maxAmount"] = maxAmount;
        }

        string json = JsonConvert.SerializeObject(responseData);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json";
        await response.WriteAsync(json);
    }

    // Notification Response: BankAccount with "processed" response
    public async Task RespondToBankAccountNotification(
        HttpResponse response,
        string uuid,
        string merchantPayerId,
        string trumoPayerId,
        string merchantOrderId,
        string trumoOrderId)
    {
        var responseData = new Dictionary<string, object>
        {
            ["UUID"] = uuid,
            ["type"] = "bankAccount",
            ["data"] = new Dictionary<string, object>
            {
                ["response"] = "processed",
                ["payerDetails"] = new Dictionary<string, object>
                {
                    ["merchantPayerID"] = merchantPayerId,
                    ["trumoPayerID"] = trumoPayerId
                },
                ["orderDetails"] = new Dictionary<string, object>
                {
                    ["merchantOrderID"] = merchantOrderId,
                    ["trumoOrderID"] = trumoOrderId
                }
            }
        };

        string json = JsonConvert.SerializeObject(responseData);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json";
        await response.WriteAsync(json);
    }

    // Notification Response: OrderStatus with "processed" response
    public async Task RespondToOrderStatusNotification(
        HttpResponse response,
        string uuid,
        string merchantPayerId,
        string trumoPayerId,
        string merchantOrderId,
        string trumoOrderId)
    {
        var responseData = new Dictionary<string, object>
        {
            ["UUID"] = uuid,
            ["type"] = "orderStatus",
            ["data"] = new Dictionary<string, object>
            {
                ["response"] = "processed",
                ["payerDetails"] = new Dictionary<string, object>
                {
                    ["merchantPayerID"] = merchantPayerId,
                    ["trumoPayerID"] = trumoPayerId
                },
                ["orderDetails"] = new Dictionary<string, object>
                {
                    ["merchantOrderID"] = merchantOrderId,
                    ["trumoOrderID"] = trumoOrderId
                }
            }
        };

        string json = JsonConvert.SerializeObject(responseData);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json";
        await response.WriteAsync(json);
    }

    async Task<dynamic> GetDepositRequestBody(string messageId, double amount, string currency, string country, string locale, string failUrl)
    {
        var uuid = Guid.NewGuid().ToString();
        var notificationUrl = _config.NotificationUrl;
        //var notificationUrl = "https://webhook.site/e93e8e0c-8efd-4cc0-9056-a10638fa05de";
        var successUrl = $"https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/success?messageid={messageId}";
        var json = @$"{{
  ""signature"": """",
  ""UUID"": ""{uuid}"",
  ""data"": {{
    ""notificationURL"": ""{notificationUrl}"",
    ""successURL"": ""{successUrl}"",
    ""failureURL"": ""{failUrl}"",
    ""orderDetails"": {{
      ""merchantOrderID"": ""{messageId}"",
      ""amount"": ""{amount:F2}"",
      ""currency"": ""{currency}"",
      ""country"": ""{country}"",
      ""locale"": ""{locale}""
    }}
  }}
}}";
        return JsonConvert.DeserializeObject<dynamic>(json)!;
    }

    async Task<dynamic> GetLoginRequestBody(string messageId, string email, string country, string locale, string successUrl, string failUrl)
    {
        var uuid = Guid.NewGuid().ToString();
        var notificationUrl = _config.NotificationUrl;
        var json = @$"{{
  ""signature"": """",
  ""UUID"": ""{uuid}"",
  ""data"": {{
    ""notificationURL"": ""{notificationUrl}"",
    ""successURL"": ""{successUrl}"",
    ""failureURL"": ""{failUrl}"",
    ""orderDetails"": {{
      ""merchantOrderID"": ""{messageId}"",
      ""country"": ""{country}"",
      ""locale"": ""{locale}""
    }}
  }}
}}";
        return JsonConvert.DeserializeObject<dynamic>(json)!;
    }

    dynamic GetWithdrawalRequestBody(string merchantPayerId, string trumoPayerId, string merchantOrderId, string amount, string currency, string trumoBankAccountId)
    {
        var uuid = Guid.NewGuid().ToString();
        var notificationUrl = _config.NotificationUrl;
        var json = @$"{{
  ""signature"": """",
  ""UUID"": ""{uuid}"",
  ""data"": {{
    ""notificationURL"": ""{notificationUrl}"",
    ""payerDetails"": {{
      ""merchantPayerID"": ""{merchantPayerId}"",
      ""trumoPayerID"": ""{trumoPayerId}""
    }},
    ""orderDetails"": {{
      ""merchantOrderID"": ""{merchantOrderId}"",
      ""amount"": ""{amount}"",
      ""currency"": ""{currency}""
    }},
    ""bankAccountDetails"": {{
      ""trumoBankAccountID"": ""{trumoBankAccountId}""
    }}
  }}
}}";
        return JsonConvert.DeserializeObject<dynamic>(json)!;
    }

    dynamic GetRefundRequestBody(string merchantPayerId, string trumoPayerId, string merchantOrderId, string trumoOrderId, string amount, string currency)
    {
        var uuid = Guid.NewGuid().ToString();
        var notificationUrl = _config.NotificationUrl;
        var json = @$"{{
  ""signature"": """",
  ""UUID"": ""{uuid}"",
  ""data"": {{
    ""notificationURL"": ""{notificationUrl}"",
    ""payerDetails"": {{
      ""merchantPayerID"": ""{merchantPayerId}"",
      ""trumoPayerID"": ""{trumoPayerId}""
    }},
    ""orderDetails"": {{
      ""merchantOrderID"": ""{merchantOrderId}"",
      ""trumoOrderID"": ""{trumoOrderId}"",
      ""amount"": ""{amount}"",
      ""currency"": ""{currency}""
    }}
  }}
}}";
        return JsonConvert.DeserializeObject<dynamic>(json)!;
    }

    string GetPlaintext(string urlPath, string uuid, Dictionary<string, object> data)
    {
        var plaintextBuilder = new StringBuilder();
        plaintextBuilder.Append(urlPath);
        plaintextBuilder.Append(uuid);

        // Recursively flatten and serialize the data
        SerializeDataRecursive(data, plaintextBuilder);

        return plaintextBuilder.ToString();
    }

    void SerializeDataRecursive(Dictionary<string, object> data, StringBuilder builder)
    {
        foreach (var property in data.OrderBy(x => x.Key))
        {
            builder.Append(property.Key);

            if (property.Value is JObject nestedJObj)
            {
                Dictionary<string, object> nestedDic = nestedJObj.ToObject<Dictionary<string, object>>()!;
                SerializeDataRecursive(nestedDic, builder);
            }
            else if (property.Value is JArray arrayJObj)
            {
                foreach (var item in arrayJObj)
                {
                    if (item is JObject itemObj)
                    {
                        Dictionary<string, object> itemDic = itemObj.ToObject<Dictionary<string, object>>()!;
                        SerializeDataRecursive(itemDic, builder);
                    }
                    else
                    {
                        builder.Append(item);
                    }
                }
            }
            else
            {
                builder.Append(property.Value);
            }
        }
    }

    async Task<string> Post(string method, dynamic body)
    {
        string json = JsonConvert.SerializeObject(body);
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-Merchant-ID", _config.MerchantId);
        var data = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{_config.ApiBaseUrl}/{method}", data);
        string responseContent = await response.Content.ReadAsStringAsync();
        // Note: As of Trumo API v1.4, signature is removed from API responses
        // So we don't verify the signature anymore
        return responseContent;
    }
}
