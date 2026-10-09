using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace GameInventoryApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PurchaseLimitReset
{
    Lifetime,
    Daily,
    Weekly
}

public class ShopItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Reward Reward { get; set; } = new();
    public int RewardTier { get; set; } = 1;                          // 1=weapons/trinkets (no stats), 2=with stats, 3=units
    public int Price { get; set; }                                   // Cost in Gems
    public int? PurchaseLimit { get; set; }                          // Max quantity per player; null = unlimited
}

[BsonIgnoreExtraElements]
public class Shop
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } = 0;                       // Lower number = higher display priority
    public List<ShopItem> Items { get; set; } = [];
    public PurchaseLimitReset ResetPeriod { get; set; } = PurchaseLimitReset.Lifetime;
    public bool IsActive { get; set; } = false;

    // Overall seasonal / campaign date validity
    public DateTime? StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    // Recurring daily active time window (UTC)
    public TimeSpan? DailyStartTime { get; set; }                   // e.g. 18:00:00
    public TimeSpan? DailyEndTime { get; set; }                     // e.g. 22:00:00

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

