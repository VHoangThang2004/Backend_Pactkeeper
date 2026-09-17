using GameInventoryApi.Models;
using MongoDB.Driver;

namespace GameInventoryApi.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IMongoDatabase database)
    {
        await SeedLibraryAsync(database);
        await SeedUsersAsync(database);
    }

    private static async Task SeedLibraryAsync(IMongoDatabase database)
    {
        var skills = database.GetCollection<SkillDefinition>("SkillDefinitions");
        if (await skills.CountDocumentsAsync(_ => true) > 0)
        {
            Console.WriteLine("[Seed] Library already seeded, skipping.");
            return;
        }

        await SeedSkillsAsync(skills);
        await SeedClassesAsync(database);
        await SeedUnitsAsync(database);
        await SeedWeaponsAsync(database);

        Console.WriteLine("[Seed] Library seeded.");
    }

    private static async Task SeedUsersAsync(IMongoDatabase database)
    {
        var users = database.GetCollection<User>("Users");
        if (await users.CountDocumentsAsync(_ => true) > 0)
        {
            Console.WriteLine("[Seed] Users already seeded, skipping.");
            return;
        }

        var profiles = database.GetCollection<PlayerProfile>("PlayerProfiles");
        await users.DeleteManyAsync(_ => true);
        await profiles.DeleteManyAsync(_ => true);

        var admin = new User { Username = "admin", PasswordHash = "admin123", Role = "Admin", HasPassword = true };
        await users.InsertOneAsync(admin);

        var server = new User { Username = "gameserver", PasswordHash = "server-secret-changeme", Role = "Server", HasPassword = true };
        await users.InsertOneAsync(server);

        var player1 = new User { Username = "player1", PasswordHash = "player123", Role = "Player", HasPassword = true };
        await users.InsertOneAsync(player1);
        await SeedPlayerProfileAsync(database, player1.Id, player1.Username);

        var player2 = new User { Username = "player2", PasswordHash = "player123", Role = "Player", HasPassword = true };
        await users.InsertOneAsync(player2);
        await SeedPlayerProfileAsync(database, player2.Id, player2.Username);

        Console.WriteLine("[Seed] Users seeded.");
    }

    private static async Task SeedPlayerProfileAsync(IMongoDatabase database, string playerId, string username)
    {
        var unitDefs = await database.GetCollection<UnitDefinition>("UnitDefinitions")
            .Find(u => u.GivenAtRegister).ToListAsync();

        var weaponDefs = await database.GetCollection<WeaponDefinition>("WeaponDefinitions")
            .Find(w => w.GivenAtRegister).ToListAsync();

        var profile = new PlayerProfile
        {
            PlayerId = playerId,
            Username = username,
            OwnedUnits = unitDefs.Select(u => new OwnedUnit
            {
                OwnedUnitId = Guid.NewGuid().ToString(),
                UnitDefinitionUId = u.UId,
                Grade = 1,
                UnlockedClassIds = [..u.ClassIds],
                EquippedMovementSkillId = -1,
                EquippedOwnedWeaponId = string.Empty,
                EquippedOwnedTrinketId = string.Empty,
                EquippedClassSkillId = -1,
            }).ToList(),
            OwnedWeapons = weaponDefs.Select(w => new OwnedWeapon
            {
                OwnedWeaponId = Guid.NewGuid().ToString(),
                WeaponDefinitionId = w.WeaponId,
            }).ToList(),
        };

        await database.GetCollection<PlayerProfile>("PlayerProfiles").InsertOneAsync(profile);
    }

    private static async Task SeedSkillsAsync(IMongoCollection<SkillDefinition> skills)
    {
        var entries = new List<SkillDefinition>();
        for (int i = 1001; i <= 1006; i++) entries.Add(new SkillDefinition { SkillId = i });
        for (int i = 2001; i <= 2006; i++) entries.Add(new SkillDefinition { SkillId = i });
        for (int i = 3001; i <= 3006; i++) entries.Add(new SkillDefinition { SkillId = i });
        for (int i = 4001; i <= 4006; i++) entries.Add(new SkillDefinition { SkillId = i });
        for (int i = 5001; i <= 5006; i++) entries.Add(new SkillDefinition { SkillId = i });
        await skills.InsertManyAsync(entries);
    }

    private static async Task SeedClassesAsync(IMongoDatabase database)
    {
        var classes = database.GetCollection<ClassDefinition>("ClassDefinitions");
        await classes.InsertManyAsync(new[]
        {
            new ClassDefinition { ClassId = 1, Name = "Assassin",  MovementSkillId = 1001, ClassSkillId = 3001 },
            new ClassDefinition { ClassId = 2, Name = "Tank",      MovementSkillId = 1002, ClassSkillId = 3002 },
            new ClassDefinition { ClassId = 3, Name = "Warrior",   MovementSkillId = 1003, ClassSkillId = 3003 },
            new ClassDefinition { ClassId = 4, Name = "Archer",    MovementSkillId = 1004, ClassSkillId = 3004 },
            new ClassDefinition { ClassId = 5, Name = "Physician", MovementSkillId = 1005, ClassSkillId = 3005 },
        });
    }

    private static async Task SeedUnitsAsync(IMongoDatabase database)
    {
        var units = database.GetCollection<UnitDefinition>("UnitDefinitions");
        await units.InsertManyAsync(new[]
        {
            new UnitDefinition
            {
                UId = 1, UnitName = "Assassin", PassiveSkillId = 5001, ClassIds = [1], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 1, MaxSkillPoint = 1, Speed = 140, DamageMultiplier = 1f, DamageReduction = 1f },
                    new() { Grade = 2, MaxHP = 3, MaxSkillPoint = 4, Speed = 150, DamageMultiplier = 1f, DamageReduction = 1f },
                ]
            },
            new UnitDefinition
            {
                UId = 2, UnitName = "Tank", PassiveSkillId = 5002, ClassIds = [2], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 2, MaxSkillPoint = 1, Speed = 100, DamageMultiplier = 1f, DamageReduction = 1f },
                    new() { Grade = 2, MaxHP = 5, MaxSkillPoint = 3, Speed = 110, DamageMultiplier = 1f, DamageReduction = 1f },
                ]
            },
            new UnitDefinition
            {
                UId = 3, UnitName = "Warrior", PassiveSkillId = 5003, ClassIds = [3], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 2, MaxSkillPoint = 1, Speed = 110, DamageMultiplier = 1f, DamageReduction = 1f },
                    new() { Grade = 2, MaxHP = 4, MaxSkillPoint = 3, Speed = 120, DamageMultiplier = 1f, DamageReduction = 1f },
                ]
            },
            new UnitDefinition
            {
                UId = 4, UnitName = "Archer", PassiveSkillId = 5004, ClassIds = [4], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 1, MaxSkillPoint = 2, Speed = 120, DamageMultiplier = 1f, DamageReduction = 1f },
                    new() { Grade = 2, MaxHP = 3, MaxSkillPoint = 4, Speed = 130, DamageMultiplier = 1f, DamageReduction = 1f },
                ]
            },
            new UnitDefinition
            {
                UId = 5, UnitName = "Physician", PassiveSkillId = 5005, ClassIds = [5], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 1, MaxSkillPoint = 2, Speed = 110, DamageMultiplier = 1f, DamageReduction = 1f },
                    new() { Grade = 2, MaxHP = 3, MaxSkillPoint = 5, Speed = 120, DamageMultiplier = 1f, DamageReduction = 1f },
                ]
            },
        });
    }

    private static async Task SeedWeaponsAsync(IMongoDatabase database)
    {
        var weapons = database.GetCollection<WeaponDefinition>("WeaponDefinitions");
        await weapons.InsertManyAsync(new[]
        {
            new WeaponDefinition { WeaponId = 1, Name = "Dagger",         ClassId = 1, SkillId = 2001, StatModifiers = new(), GivenAtRegister = true },
            new WeaponDefinition { WeaponId = 2, Name = "Sword & Shield", ClassId = 2, SkillId = 2002, StatModifiers = new(), GivenAtRegister = true },
            new WeaponDefinition { WeaponId = 3, Name = "Axe",            ClassId = 3, SkillId = 2003, StatModifiers = new(), GivenAtRegister = true },
            new WeaponDefinition { WeaponId = 4, Name = "CrossBow",       ClassId = 4, SkillId = 2004, StatModifiers = new(), GivenAtRegister = true },
            new WeaponDefinition { WeaponId = 5, Name = "Holy Book",      ClassId = 5, SkillId = 2005, StatModifiers = new(), GivenAtRegister = true },
        });
    }
}
