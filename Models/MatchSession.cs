using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class MatchSession
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    public int ProcessId { get; set; } = -1;
    public string MatchId { get; set; } = Guid.NewGuid().ToString();
    public string Player1Id { get; set; } = string.Empty;
    public string Player2Id { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string Mode { get; set; } = "pvp";
    public string StoryChapterId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending, active, finished
    public MatchResultData? Result { get; set; } = null;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}