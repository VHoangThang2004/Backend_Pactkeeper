using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public enum PurchaseOrderStatus { Pending, Confirmed, Cancelled }

public class PurchaseOrder
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public long OrderCode { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string TopUpPackId { get; set; } = string.Empty;

    // Denormalized from the pack at order time so webhook doesn't need a second lookup
    public int GemsAmount { get; set; }
    public int Amount { get; set; }
    public List<int> WeaponDefinitionIds { get; set; } = [];
    public List<int> TrinketDefinitionIds { get; set; } = [];
    public List<int> UnitDefinitionIds { get; set; } = [];

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
}
