using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class MatchSession
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string MatchId { get; set; } = Guid.NewGuid().ToString();
    public string Player1Id { get; set; } = string.Empty;
    public string Player2Id { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string Status { get; set; } = "pending"; // pending, active, finished
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}