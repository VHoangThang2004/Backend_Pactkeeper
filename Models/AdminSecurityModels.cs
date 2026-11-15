using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

[BsonIgnoreExtraElements]
public class AdminRoleDefinition
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool IsSystemDefault { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class AdminAuditLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ActorUsername { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = "SECURITY";
    public string Severity { get; set; } = "LOW";
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? TargetLabel { get; set; }
    public string? Rationale { get; set; }
    public string? BeforeStateJson { get; set; }
    public string? AfterStateJson { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string? ErrorMessage { get; set; }
}

[BsonIgnoreExtraElements]
public class AdminLoginHistory
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = string.Empty;
    public string? AttemptedRole { get; set; }
    public string? ResolvedRole { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Reason { get; set; }
}
