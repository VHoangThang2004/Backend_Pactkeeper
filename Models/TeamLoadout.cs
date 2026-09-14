using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class TeamLoadoutDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;

    public List<UnitLoadoutEntry> Units { get; set; } = new List<UnitLoadoutEntry>();

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class UnitLoadoutEntry
{
    public int UId { get; set; }
    public int MovementSkillId { get; set; }
    public int WeaponSkillId { get; set; }
    public int ClassSkillId { get; set; }
    public int EquipmentSkillId { get; set; }
}