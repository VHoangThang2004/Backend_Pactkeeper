using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

[BsonIgnoreExtraElements]
public class GameVersionConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string LatestVersion { get; set; } = "1.0.0";
    public string MinSupportedVersion { get; set; } = "1.0.0";
    public string DownloadUrl { get; set; } = string.Empty;
    public string PatchNotes { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
