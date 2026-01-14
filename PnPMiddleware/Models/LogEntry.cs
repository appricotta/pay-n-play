using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PnPMiddleware.Models;

[BsonIgnoreExtraElements]
public class LogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    public DateTime UtcTimeStamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string RenderedMessage { get; set; } = string.Empty;
    public BsonDocument Properties { get; set; } = new();
    public BsonDocument? Exception { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
}
