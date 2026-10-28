using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

[BsonIgnoreExtraElements]
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Player";
    public bool HasPassword { get; set; } = false;
    public LoginProviders LoginProviders { get; set; } = new LoginProviders();

    public bool IsBanned { get; set; } = false;
    public string? BanReason { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BanDuration { get; set; }
}

[BsonIgnoreExtraElements]
public class LoginProviders
{
    public string SteamId { get; set; } = string.Empty;
    public string GoogleId { get; set; } = string.Empty;
}
