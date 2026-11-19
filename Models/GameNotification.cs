using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class GameNotification
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Target player ID, or null/empty if this is a global system announcement for all players.
    /// </summary>
    public string? PlayerId { get; set; }

    /// <summary>
    /// Category / type: "System", "Announcement", "Maintenance", "MatchResult", etc.
    /// </summary>
    public string Type { get; set; } = "System";

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Read state for player-specific notifications.
    /// </summary>
    public bool Read { get; set; } = false;

    /// <summary>
    /// Tracks player IDs who read this notification when PlayerId is null (broadcast announcement).
    /// </summary>
    public List<string> ReadByPlayerIds { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }
}
