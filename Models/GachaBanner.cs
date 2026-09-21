using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace GameInventoryApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PullType { Single, Five, Ten }

public class BannerItem
{
    public Reward Reward { get; set; } = new();
    public int Weight { get; set; }       // relative weight for weighted random draw
    public bool IsFeatured { get; set; }  // rate-up / highlighted item on the banner UI
    public int RewardTier { get; set; }   // 1=weapons/trinkets (no stats)  2=weapons/trinkets (with stats)  3=units
}

public class PullOption
{
    public PullType PullType { get; set; }
    public int Price { get; set; }  // cost in gems
}

public class GachaBanner
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<BannerItem> Items { get; set; } = [];
    public List<PullOption> PullOptions { get; set; } = [];
    public bool IsActive { get; set; } = false;
    public DateTime? StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? PityThreshold { get; set; }  // guaranteed featured item pull every N pulls; null = no pity
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
