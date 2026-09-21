using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class TopUpPack
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int PriceVnd { get; set; }
    public int GemsAmount { get; set; }
    public List<int> WeaponDefinitionIds { get; set; } = [];
    public List<int> TrinketDefinitionIds { get; set; } = [];
    public List<int> UnitDefinitionIds { get; set; } = [];
    public bool IsAvailable { get; set; } = true;
}
