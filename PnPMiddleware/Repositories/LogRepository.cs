using MongoDB.Driver;
using PnPMiddleware.Models;

namespace PnPMiddleware.Repositories;

public class LogRepository
{
    private readonly IMongoCollection<LogEntry> _collection;

    public LogRepository(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDBLogs");
        var mongoUrl = new MongoUrl(connectionString);
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(mongoUrl.DatabaseName);
        _collection = database.GetCollection<LogEntry>("AppLog");
    }

    public async Task<List<LogEntry>> GetLogsByMessageIdAsync(string messageId)
    {
        // Query logs where Properties.MessageId equals the provided messageId
        var filter = Builders<LogEntry>.Filter.Eq("Properties.MessageId", messageId);

        // Sort by timestamp ascending (chronological order)
        var sort = Builders<LogEntry>.Sort.Ascending(x => x.UtcTimeStamp);

        return await _collection
            .Find(filter)
            .Sort(sort)
            .ToListAsync();
    }
}
