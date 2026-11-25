using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

[BsonIgnoreExtraElements]
public class BalanceAiSuggestion
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PatchVersion { get; set; } = string.Empty;
    public string? Mode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByUsername { get; set; } = string.Empty;

    public int TotalMatchesAnalyzed { get; set; }
    public List<BalanceAnomalyItem> Outliers { get; set; } = [];
    public List<BalanceRecommendationItem> Recommendations { get; set; } = [];
    public string RawLlmResponse { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
public class BalanceAnomalyItem
{
    public string Category { get; set; } = string.Empty; // "Unit", "Weapon", "Trinket", "Skill"
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public int PickCount { get; set; }
    public double PickRate { get; set; }
    public double WinRate { get; set; }
    public double SurvivalRate { get; set; }
    public double AvgDamage { get; set; }
    public string AnomalyType { get; set; } = string.Empty; // "OVERPERFORMING" | "UNDERPERFORMING"
}

[BsonIgnoreExtraElements]
public class BalanceRecommendationItem
{
    public string ChangeType { get; set; } = "DB_ONLY"; // "DB_ONLY" | "REBUILD_REQUIRED"
    public string Category { get; set; } = string.Empty; // "Unit", "Weapon", "Trinket", "Skill"
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public double CurrentWinRate { get; set; }
    public string IssueDescription { get; set; } = string.Empty;
    public string RecommendedChange { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}
