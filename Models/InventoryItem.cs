using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class InventoryItem
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}