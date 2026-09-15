using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

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
}

public class LoginProviders
{
    public string SteamId { get; set; } = string.Empty;
    public string GoogleId { get; set; } = string.Empty;
}