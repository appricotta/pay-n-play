using System.Security.Cryptography;
using MongoDB.Driver;
using TrustlyMiddlewareService.Models;

namespace TrustlyMiddlewareService.Repositories;

public interface ITrustlySessionRepository
{
    Task<string> CreateSessionAsync(string messageId, string email, string currency, string? partnerId = null, string? requestOrigin = null);
    Task<TrustlySessionData?> GetSessionAsync(string messageId);
    Task DeleteSessionAsync(string messageId);
    Task UpdateSuccessLoginUrlAsync(string messageId, string successLoginUrl);
}

public class TrustlySessionRepository : ITrustlySessionRepository
{
    private readonly IMongoCollection<TrustlySessionData> _collection;
    
    public TrustlySessionRepository(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDB");
        var mongoUrl = new MongoUrl(connectionString);
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(mongoUrl.DatabaseName);
        _collection = database.GetCollection<TrustlySessionData>("Deposit");
        
        // Create TTL index for automatic cleanup of expired sessions
        var ttlIndexKeysDefinition = Builders<TrustlySessionData>.IndexKeys.Ascending(x => x.ExpiresAt);
        var ttlIndexModel = new CreateIndexModel<TrustlySessionData>(ttlIndexKeysDefinition, new CreateIndexOptions { ExpireAfter = TimeSpan.Zero });
        _collection.Indexes.CreateOne(ttlIndexModel);
    }
    
    public async Task<string> CreateSessionAsync(string messageId, string email, string currency, string? partnerId = null, string? requestOrigin = null)
    {
        var sessionData = new TrustlySessionData
        {
            MessageId = messageId,
            Email = email,
            Currency = currency,
            PartnerId = partnerId,
            RequestOrigin = requestOrigin,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        
        await _collection.InsertOneAsync(sessionData);
        return sessionData.MessageId;
    }
    
    public async Task<TrustlySessionData?> GetSessionAsync(string messageId)
    {
        var filter = Builders<TrustlySessionData>.Filter.Eq(x => x.MessageId, messageId);
        return await _collection.Find(filter).FirstOrDefaultAsync();
    }
    
    public async Task DeleteSessionAsync(string messageId)
    {
        var filter = Builders<TrustlySessionData>.Filter.Eq(x => x.MessageId, messageId);
        await _collection.DeleteOneAsync(filter);
    }
    
    public async Task UpdateSuccessLoginUrlAsync(string messageId, string successLoginUrl)
    {
        var filter = Builders<TrustlySessionData>.Filter.Eq(x => x.MessageId, messageId);
        var update = Builders<TrustlySessionData>.Update.Set(x => x.SuccessLoginUrl, successLoginUrl);
        await _collection.UpdateOneAsync(filter, update);
    }
} 