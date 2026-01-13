using MongoDB.Bson.Serialization.Attributes;

namespace PnPMiddleware.Models;

public class DepositSessionData
{
    [BsonId]
    public string MessageId { get; set; } = string.Empty;
    public string PaymentProvider { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? PartnerId { get; set; }
    public string? SuccessLoginUrl { get; set; }
    public string? RequestOrigin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24); // Session expires in 24 hours
} 