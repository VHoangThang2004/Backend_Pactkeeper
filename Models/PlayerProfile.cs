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

    public List<OwnedUnit> OwnedUnits { get; set; } = new();
    public List<OwnedWeapon> OwnedWeapons { get; set; } = new();
    public List<OwnedTrinket> OwnedTrinkets { get; set; } = new();
}

public class OwnedUnit
{
    public string OwnedUnitId { get; set; } = string.Empty;
    public int UnitDefinitionUId { get; set; }
    public int Grade { get; set; } = 1;
    public List<int> UnlockedClassIds { get; set; } = new();
    public int EquippedMovementSkillId { get; set; } = -1;
    public string EquippedOwnedWeaponId { get; set; } = string.Empty;
    public string EquippedOwnedTrinketId { get; set; } = string.Empty;
    public int EquippedClassSkillId { get; set; } = -1;
}

public class OwnedWeapon
{
    public string OwnedWeaponId { get; set; } = string.Empty;
    public int WeaponDefinitionId { get; set; }
}

public class OwnedTrinket
{
    public string OwnedTrinketId { get; set; } = string.Empty;
    public int TrinketDefinitionId { get; set; }
}