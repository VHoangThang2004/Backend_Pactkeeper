using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class SupportMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;
    public string Sender { get; set; } = "user"; // "user" or "admin"
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string AttachmentUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
