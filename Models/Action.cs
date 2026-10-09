using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class Action
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public Dictionary<string, object> Facts { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
