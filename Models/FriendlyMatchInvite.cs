using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class FriendlyMatchInvite
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string SenderPlayerId { get; set; } = string.Empty;
    public string TargetPlayerId { get; set; } = string.Empty;

    // "pending", "accepted", "declined", "expired", "cancelled"
    public string Status { get; set; } = "pending";

    public string? MatchId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddSeconds(30);
}
