using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class GachaResult
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;
    public string BannerId { get; set; } = string.Empty;
    public List<Reward> ItemsWon { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
