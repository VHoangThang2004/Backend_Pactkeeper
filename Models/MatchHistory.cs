using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class MatchHistory
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string Player1Id { get; set; } = string.Empty;
    public string Player2Id { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string Mode { get; set; } = "pvp";
    public string MapId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public MatchResultData? Result { get; set; } = null;
    public DateTime CreatedAt { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}