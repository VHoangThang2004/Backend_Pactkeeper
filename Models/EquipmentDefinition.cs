using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class WeaponDefinition
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public int WeaponId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ClassId { get; set; }
    public int SkillId { get; set; }
    public EquipmentStatModifiers StatModifiers { get; set; } = new();
    public bool GivenAtRegister { get; set; } = false;
}

public class TrinketDefinition
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public int TrinketId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SkillId { get; set; }
    public EquipmentStatModifiers StatModifiers { get; set; } = new();
    public bool GivenAtRegister { get; set; } = false;
}

public class EquipmentStatModifiers
{
    public int MaxHP { get; set; }
    public int Speed { get; set; }
    public int MaxSkillPoint { get; set; }
    public float DamageMultiplier { get; set; }
    public float DamageReduction { get; set; }
}
