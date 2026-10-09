using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class GachaResultItem
{
    public Reward Reward { get; set; } = new();
    public int RewardTier { get; set; }
    public bool IsDuplicate { get; set; }
    public int CompensationGems { get; set; }
}

[BsonIgnoreExtraElements]
public class GachaResult
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;
    public string BannerId { get; set; } = string.Empty;
    public string BannerName { get; set; } = string.Empty;
    public PullType PullType { get; set; } = PullType.Single;
    public int GemsSpent { get; set; }
    public List<GachaResultItem> ItemsWon { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

