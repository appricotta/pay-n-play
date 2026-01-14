using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace PnPMiddleware.Services
{
    public class CarousellerService
    {
        private readonly ILogger<CarousellerService> _logger;

        public CarousellerService(ILogger<CarousellerService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> KeyObtain(KeyValuePair<string, string> transactionParam, string currency, string firstName, string lastName, string email, string siteLogin, DateOnly? dob, string? country, string? city, string? street, string? zip, string messageId)
        {
            _logger.LogInformation("Carouseller KeyObtain request for MessageId {MessageId}, SiteLogin {SiteLogin}, Currency {Currency}", messageId, siteLogin, currency);

            var paramsDic = GetParams(transactionParam, currency, firstName, lastName, email, siteLogin, dob, country, city, street, zip);
            var plain = GetPlaintext(paramsDic);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain))).ToLower();
            paramsDic.Add("signature", hash);
            var url = BuildQueryString("https://a.papaya.ninja/api/authlink/obtain/", paramsDic);
            _logger.LogDebug("Carouseller KeyObtain request URL for MessageId {MessageId}: {Url}", messageId, url);

            var client = new HttpClient();
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await client.SendAsync(requestMessage);

            _logger.LogInformation("Carouseller KeyObtain response for MessageId {MessageId}, StatusCode {StatusCode}", messageId, (int)response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Carouseller KeyObtain response for MessageId {MessageId}: {ResponseContent}", messageId, responseContent);

            var responseObj = JsonConvert.DeserializeObject<dynamic>(responseContent)!;
            bool success = responseObj.success == true;

            return success;
        }

        private static Dictionary<string, string?> GetParams(KeyValuePair<string, string> transactionParam, string currency, string firstName, string lastName, string email, string siteLogin, DateOnly? dob, string? country, string? city, string? street, string? zip)
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
                { transactionParam.Key, transactionParam.Value }
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
            return plaintextBuilder.ToString();
        }

        private static string BuildQueryString(string baseUrl, Dictionary<string, string?> parameters)
        {
            var queryParams = new List<string>();

            foreach (var kvp in parameters)
            {
                if (kvp.Value == null) continue;

                string encodedKey = Uri.EscapeDataString(kvp.Key);
                string encodedValue;

                if (kvp.Key == "trumo_uuid")
                {
                    var parts = kvp.Value.Split(':');
                    encodedValue = string.Join(":", parts.Select(p => Uri.EscapeDataString(p)));
                }
                else
                {
                    // Normal encoding for all other parameters
                    encodedValue = Uri.EscapeDataString(kvp.Value);
                }

                queryParams.Add($"{encodedKey}={encodedValue}");
            }

            return $"{baseUrl}?{string.Join("&", queryParams)}";
        }
    }
}
