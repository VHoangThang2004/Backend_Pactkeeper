using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class RewardRule
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string ActionType { get; set; } = string.Empty;
    public List<RewardCondition> Conditions { get; set; } = new();
    public string? AccumulateField { get; set; }
    public int TargetCount { get; set; }
    public Reward Reward { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class RewardCondition
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = string.Empty;   // "==", "!=", ">", ">=", "<", "<="
    public string Value { get; set; } = string.Empty;
}
