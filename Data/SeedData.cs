using GameInventoryApi.Models;
using MongoDB.Driver;

namespace GameInventoryApi.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IMongoDatabase database)
    {
        var users = database.GetCollection<User>("Users");
        var inventory = database.GetCollection<InventoryItem>("InventoryItems");
        var profiles = database.GetCollection<PlayerProfile>("PlayerProfiles");

        // Skip if already seeded
        var count = await users.CountDocumentsAsync(_ => true);
        if (count > 0)
        {
            Console.WriteLine("[Seed] Already seeded, skipping.");
            return;
        }

        await users.DeleteManyAsync(_ => true);
        await inventory.DeleteManyAsync(_ => true);
        await profiles.DeleteManyAsync(_ => true);

        var admin = new User { Username = "admin", PasswordHash = "admin123", Role = "Admin", HasPassword = true };
        await users.InsertOneAsync(admin);

        var player1 = new User { Username = "player1", PasswordHash = "player123", Role = "Player", HasPassword = true };
        await users.InsertOneAsync(player1);

        var player2 = new User { Username = "player2", PasswordHash = "player123", Role = "Player", HasPassword = true };
        await users.InsertOneAsync(player2);

        await inventory.InsertOneAsync(new InventoryItem
        {
            ItemId = "sword_001",
            Name = "Iron Sword",
            Quantity = 1,
            PlayerId = player1.Id
        });

        await profiles.InsertOneAsync(new PlayerProfile
        {
            PlayerId = player1.Id,
            Username = "player1",
            Level = 10,
            Experience = 2450
        });
    }
}