using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class UnitDefinition
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public int UId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public int PassiveSkillId { get; set; } = -1;
    public List<int> ClassIds { get; set; } = new();
    public bool GivenAtRegister { get; set; } = false;
    public List<UnitGradeStats> StatsByGrade { get; set; } = new();
}

public class UnitGradeStats
{
    public int Grade { get; set; }
    public int MaxHP { get; set; }
    public int Speed { get; set; }
    public int MaxSkillPoint { get; set; }
    public int DamageMultiplier { get; set; } = 100;
    public int DamageReduction { get; set; } = 0;
}
