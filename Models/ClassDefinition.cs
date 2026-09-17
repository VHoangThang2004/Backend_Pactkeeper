using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class ClassDefinition
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public int ClassId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MovementSkillId { get; set; }
    public int ClassSkillId { get; set; }
}
