namespace TrustlyMiddlewareService.Configuration;

public class TrumoApiConfiguration
{
    public string MerchantId { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string NotificationUrl { get; set; } = string.Empty;
    public string PrivateKeyFileName { get; set; } = string.Empty;
    public string PublicKeyFileName { get; set; } = string.Empty;
}
