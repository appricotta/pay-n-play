using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System;

namespace TrustlyMiddlewareService
{
    public class CarousellerApi
    {
        private readonly ILogger<CarousellerApi> _logger;

        public CarousellerApi(ILogger<CarousellerApi> logger)
        {
            _logger = logger;
        }

        public async Task<bool> KeyObtain(string transactionId, string currency, string firstName, string lastName, string email, string siteLogin, DateOnly? dob, string? country, string? city, string? street, string? zip)
        {
            var paramsDic = GetParams(transactionId, currency, firstName, lastName, email, siteLogin, dob, country, city, street, zip);
            var plain = GetPlaintext(paramsDic);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain))).ToLower();
            paramsDic.Add("signature", hash);
            var url = QueryHelpers.AddQueryString("https://a.papaya.ninja/api/authlink/obtain/", paramsDic);
            _logger.LogDebug($"KeyObtain request: {url}");
            var client = new HttpClient();
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await client.SendAsync(requestMessage);
            string responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug($"KeyObtain response: {responseContent}");
            var responseObj = JsonConvert.DeserializeObject<dynamic>(responseContent)!;
            return responseObj.success == true;
        }

        private static Dictionary<string, string?> GetParams(string transactionId, string currency, string firstName, string lastName, string email, string siteLogin, DateOnly? dob, string? country, string? city, string? street, string? zip)
        {
            var paramsDic = new Dictionary<string, string?>
            {
                { "site_id", "96" },
                { "site_login", siteLogin },
                { "user_email", email },
                { "customer_ip", "8.8.8.8" },
                { "user_name", string.Concat(firstName, " ", lastName) },
                { "first_name", firstName },
                { "last_name", lastName },
                { "currency", currency.ToUpper() },
                { "trustly_uuid", transactionId }
            };
            if (dob.HasValue)
            {
                paramsDic.Add("birthdate", dob.Value.ToString("dd-MM-yyyy"));
            }

            if (country != null)
            {
                paramsDic.Add("user_country", country);
            }
            if (city != null)
            {
                paramsDic.Add("user_city", city);
            }
            if (street != null)
            {
                paramsDic.Add("user_address", street);
            }
            if (zip != null)
            {
                paramsDic.Add("user_postal", zip);
            }
            return paramsDic;
        }

        static string GetPlaintext(Dictionary<string, string?> data)
        {
            var plaintextBuilder = new StringBuilder();
            foreach (var property in data.OrderBy(x => x.Key))
            {
                plaintextBuilder.Append(property.Key);
                plaintextBuilder.Append(":");
                plaintextBuilder.Append(property.Value);
                plaintextBuilder.Append(";");
            }
            plaintextBuilder.Append("SEF3235kjkhg48uiw43edwt657asRnnsQWh8sl");
            //SEF3235kjkhg48uiw43edwt657asRnnsQWh8sl
            return plaintextBuilder.ToString();
        }
    }
}
