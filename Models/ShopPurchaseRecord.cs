using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

[BsonIgnoreExtraElements]
public class ShopPurchaseRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;
    public string ShopId { get; set; } = string.Empty;
    public string ShopItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime LastPurchasedAt { get; set; } = DateTime.UtcNow;
}
