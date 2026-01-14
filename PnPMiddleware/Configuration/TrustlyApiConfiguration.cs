namespace PnPMiddleware.Configuration;

public class TrustlyApiConfiguration
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string NotificationUrl { get; set; } = string.Empty;
    public string PrivateKeyFileName { get; set; } = string.Empty;
    public string PublicKeyFileName { get; set; } = string.Empty;
}
