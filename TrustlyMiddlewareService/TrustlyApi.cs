using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System.Net;

namespace TrustlyMiddlewareService;

public class TrustlyApi
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("VjdwUn8zUWOkqaK7mHb4TYiGCVlM9GNe");

    public static async Task<DepositResponse> Deposite(string email, double amount, string password, string currency, string country, string locale, string successUrl, string failUrl)
    {
        var body = GetDepositRequestBody(email, amount, password, currency, country, locale, successUrl, failUrl);
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
    
    static dynamic GetDepositRequestBody(string email, double amount, string password, string currency, string country, string locale, string successUrl, string failUrl)
    {
        var uuid = Guid.NewGuid().ToString();
        var notificationUrl = "https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/trustly/notifications";
        var messageId = SerializeMessageId(email, password, currency);
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

    static string SerializeMessageId(string email, string password, string currency)
    {
        byte[] rawData = EncodeBinary(currency, email, password);
        byte[] encryptedData = Encrypt(rawData);
        string messageId = Convert.ToBase64String(encryptedData);
        return messageId;
    }

    public static (string currency, string email, string password) DeserializeMessageId(string messageId)
    {
        byte[] decodedData = Convert.FromBase64String(messageId);
        byte[] decryptedData = Decrypt(decodedData);
        var (timestamp, randomBytes, currency, email, password) = DecodeBinary(decryptedData);
        return (currency, email, password);
    }

    static byte[] EncodeBinary(string currency, string email, string password)
    {
        byte[] currencyBytes = Encoding.ASCII.GetBytes(currency); // 3 bytes
        byte[] emailBytes = Encoding.UTF8.GetBytes(email);
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        
        byte[] timestampBytes = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        byte[] randomBytes = new byte[4];
        RandomNumberGenerator.Fill(randomBytes);

        return timestampBytes.Concat(randomBytes).Concat(currencyBytes).Concat(emailBytes).Concat(new byte[] { 0x00 }).Concat(passwordBytes).ToArray();
    }

    static (long timestamp, byte[] randomBytes, string currency, string email, string password) DecodeBinary(byte[] data)
    {
        long timestamp = BitConverter.ToInt64(data, 0);
        byte[] randomBytes = data.Skip(8).Take(4).ToArray();
        string currency = Encoding.ASCII.GetString(data, 12, 3);
        int separatorIndex = Array.IndexOf(data, (byte)0, 15);
        string email = Encoding.UTF8.GetString(data, 15, separatorIndex - 15);
        string password = Encoding.UTF8.GetString(data, separatorIndex + 1, data.Length - separatorIndex - 1);
        return (timestamp, randomBytes, currency, email, password);
    }

    static byte[] Encrypt(byte[] plainBytes)
    {
        using Aes aesAlg = Aes.Create();
        aesAlg.Key = Key;
        aesAlg.Mode = CipherMode.ECB;
        aesAlg.Padding = PaddingMode.PKCS7;

        using MemoryStream msEncrypt = new();
        using CryptoStream csEncrypt = new(msEncrypt, aesAlg.CreateEncryptor(), CryptoStreamMode.Write);
        csEncrypt.Write(plainBytes, 0, plainBytes.Length);
        csEncrypt.FlushFinalBlock();

        return msEncrypt.ToArray();
    }
    
    static byte[] Decrypt(byte[] cipherBytes)
    {
        using Aes aesAlg = Aes.Create();
        aesAlg.Key = Key;
        aesAlg.Mode = CipherMode.ECB;
        aesAlg.Padding = PaddingMode.PKCS7;

        using MemoryStream msDecrypt = new(cipherBytes);
        using CryptoStream csDecrypt = new(msDecrypt, aesAlg.CreateDecryptor(), CryptoStreamMode.Read);
        using MemoryStream resultStream = new();
        csDecrypt.CopyTo(resultStream);

        return resultStream.ToArray();
    }
}

public record DepositResponse(string Url, string OrderId);