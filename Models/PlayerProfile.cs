using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class PlayerProfile
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public DateTime LastLogin { get; set; } = DateTime.UtcNow;
}