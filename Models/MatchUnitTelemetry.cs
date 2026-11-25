using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

[BsonIgnoreExtraElements]
public class MatchUnitTelemetry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string MatchId { get; set; } = string.Empty;
    public string MatchHistoryId { get; set; } = string.Empty;
    public string PatchVersion { get; set; } = "1.0.0";
    public string Mode { get; set; } = "pvp";
    public string WinnerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<UnitTelemetryItem> Units { get; set; } = [];
}

[BsonIgnoreExtraElements]
public class UnitTelemetryItem
{
    public string PlayerId { get; set; } = string.Empty;
    public int UnitUId { get; set; }
    public int WeaponId { get; set; }
    public int TrinketId { get; set; }
    public List<int> SkillIds { get; set; } = [];
    public bool Survived { get; set; }
    public bool Alive
    {
        get => Survived;
        set => Survived = value;
    }
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int HealingDone { get; set; }
    public int Kills { get; set; }
}
