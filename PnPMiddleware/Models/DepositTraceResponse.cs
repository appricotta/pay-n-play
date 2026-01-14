namespace PnPMiddleware.Models;

public class DepositTraceResponse
{
    public string MessageId { get; set; } = string.Empty;
    public string? PaymentProvider { get; set; }
    public string? Email { get; set; }
    public string? Currency { get; set; }
    public DateTime? SessionCreatedAt { get; set; }
    public List<TraceEvent> Timeline { get; set; } = new();
}

public class TraceEvent
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Details { get; set; } = new();
    public string? Exception { get; set; }
}
