using GameInventoryApi.Models;
using MongoDB.Driver;

namespace GameInventoryApi.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IMongoDatabase database)
    {
        await SeedLibraryAsync(database);
        await SeedUsersAsync(database);
        await SeedTopUpPacksAsync(database);
        await SeedGachaBannersAsync(database);
        await SeedChapterConfigsAsync(database);
    }

    private static async Task SeedChapterConfigsAsync(IMongoDatabase database)
    {
        var chapters = database.GetCollection<ChapterConfig>("ChapterConfigs");
        if (await chapters.CountDocumentsAsync(_ => true) > 0)
            return;

        await chapters.InsertManyAsync(new[]
        {
            new ChapterConfig
            {
                ChapterId = 0,
                Title = "Chapter 0 — The First Pact",
                MapId = "MD_PVP_001",
                Scenes = new List<SceneConfig>
                {
                    new SceneConfig
                    {
                        SceneId = 0,
                        Type = SceneType.Cutscene,
                        Description = "Name input screen — player signs their name",
                        AutoNext=true
                    },
                    new SceneConfig
                    {
                        SceneId = 1,
                        Type = SceneType.Battle,
                        Description = "Tutorial battle — learn movement and skills",
                        AutoNext=true
                    },
                    new SceneConfig
                    {
                        SceneId = 2,
                        Type = SceneType.Dialogue,
                        Description = "Main menu walkthrough tooltips",
                        AutoNext=false
                    },
                }
            }
        });

        Console.WriteLine("[Seed] ChapterConfigs seeded.");
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
        await SeedTrinketsAsync(database);

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

        var trinketDefs = await database.GetCollection<TrinketDefinition>("TrinketDefinitions")
            .Find(t => t.GivenAtRegister).ToListAsync();

        var profile = new PlayerProfile
        {
            PlayerId = playerId,
            Username = username,
            OwnedUnits = unitDefs.Select(u => new OwnedUnit
            {
                OwnedUnitId = Guid.NewGuid().ToString(),
                UnitDefinitionUId = u.UId,
                Grade = 2,
                UnlockedClassIds = [.. u.ClassIds],
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
            OwnedTrinkets = trinketDefs.Select(t => new OwnedTrinket
            {
                OwnedTrinketId = Guid.NewGuid().ToString(),
                TrinketDefinitionId = t.TrinketId,
            }).ToList(),
        };

        await database.GetCollection<PlayerProfile>("PlayerProfiles").InsertOneAsync(profile);
    }

    private static async Task SeedTopUpPacksAsync(IMongoDatabase database)
    {
        var col = database.GetCollection<TopUpPack>("TopUpPacks");
        if (await col.CountDocumentsAsync(_ => true) > 0)
        {
            Console.WriteLine("[Seed] TopUpPacks already seeded, skipping.");
            return;
        }

        await col.InsertManyAsync(new[]
        {
            new TopUpPack
            {
                Name = "Gem Starter",
                PriceVnd = 20_000,
                GemsAmount = 200,
                WeaponDefinitionIds = [],
                TrinketDefinitionIds = [],
                UnitDefinitionIds = [],
                IsAvailable = true,
            },
            new TopUpPack
            {
                Name = "Gem Pouch",
                PriceVnd = 50_000,
                GemsAmount = 550,
                WeaponDefinitionIds = [],
                TrinketDefinitionIds = [],
                UnitDefinitionIds = [],
                IsAvailable = true,
            },
            new TopUpPack
            {
                Name = "Gem Chest",
                PriceVnd = 100_000,
                GemsAmount = 1_200,
                WeaponDefinitionIds = [],
                TrinketDefinitionIds = [],
                UnitDefinitionIds = [],
                IsAvailable = true,
            },
            new TopUpPack
            {
                Name = "Assassin Bundle",
                PriceVnd = 25_000,
                GemsAmount = 50,
                WeaponDefinitionIds = [1],
                TrinketDefinitionIds = [1],
                UnitDefinitionIds = [],
                IsAvailable = true,
            },
            new TopUpPack
            {
                Name = "Full Arsenal",
                PriceVnd = 200_000,
                GemsAmount = 1_000,
                WeaponDefinitionIds = [1, 2, 3, 4, 5, 6],
                TrinketDefinitionIds = [1],
                UnitDefinitionIds = [],
                IsAvailable = true,
            },
        });

        Console.WriteLine("[Seed] TopUpPacks seeded.");
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
            new ClassDefinition { ClassId = 6, Name = "Druid",     MovementSkillId = 1006, ClassSkillId = 3006 },
        });
    }

    private static async Task SeedUnitsAsync(IMongoDatabase database)
    {
        var units = database.GetCollection<UnitDefinition>("UnitDefinitions");
        await units.InsertManyAsync(new[]
        {
            new UnitDefinition
            {
                UId = 1, UnitName = "Assassin", PassiveSkillId = -1, ClassIds = [1], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 1, MaxSkillPoint = 3, Speed = 135, DamageMultiplier = 100, DamageReduction = 0 },
                    new() { Grade = 2, MaxHP = 3, MaxSkillPoint = 4, Speed = 140, DamageMultiplier = 100, DamageReduction = 0 },
                ]
            },
            new UnitDefinition
            {
                UId = 2, UnitName = "Tank", PassiveSkillId = -1, ClassIds = [2], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 3, MaxSkillPoint = 2, Speed = 100, DamageMultiplier = 100, DamageReduction = 0 },
                    new() { Grade = 2, MaxHP = 5, MaxSkillPoint = 3, Speed = 110, DamageMultiplier = 100, DamageReduction = 0 },
                ]
            },
            new UnitDefinition
            {
                UId = 3, UnitName = "Warrior", PassiveSkillId = -1, ClassIds = [3], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 2, MaxSkillPoint = 2, Speed = 110, DamageMultiplier = 100, DamageReduction = 0 },
                    new() { Grade = 2, MaxHP = 4, MaxSkillPoint = 3, Speed = 120, DamageMultiplier = 100, DamageReduction = 0 },
                ]
            },
            new UnitDefinition
            {
                UId = 4, UnitName = "Archer", PassiveSkillId = -1, ClassIds = [4], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 1, MaxSkillPoint = 2, Speed = 120, DamageMultiplier = 100, DamageReduction = 0 },
                    new() { Grade = 2, MaxHP = 3, MaxSkillPoint = 3, Speed = 130, DamageMultiplier = 100, DamageReduction = 0 },
                ]
            },
            new UnitDefinition
            {
                UId = 5, UnitName = "Physician", PassiveSkillId = -1, ClassIds = [5], GivenAtRegister = true,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 1, MaxSkillPoint = 3, Speed = 110, DamageMultiplier = 100, DamageReduction = 0 },
                    new() { Grade = 2, MaxHP = 3, MaxSkillPoint = 5, Speed = 120, DamageMultiplier = 100, DamageReduction = 0 },
                ]
            },
            new UnitDefinition
            {
                UId = 6, UnitName = "Druid", PassiveSkillId = -1, ClassIds = [6], GivenAtRegister = false,
                StatsByGrade =
                [
                    new() { Grade = 1, MaxHP = 3, MaxSkillPoint = 2, Speed = 120, DamageMultiplier = 100, DamageReduction = 0 },
                    new() { Grade = 2, MaxHP = 4, MaxSkillPoint = 3, Speed = 130, DamageMultiplier = 100, DamageReduction = 0 },
                ]
            },
        });
    }

    private static async Task SeedWeaponsAsync(IMongoDatabase database)
    {
        var weapons = database.GetCollection<WeaponDefinition>("WeaponDefinitions");
        await weapons.InsertManyAsync(new[]
        {
            // Tier 1 — no stat modifiers, given at register
            new WeaponDefinition { WeaponId = 1, Name = "Dagger",          ClassId = 1, SkillId = 2001, StatModifiers = new(), GivenAtRegister = true  },
            new WeaponDefinition { WeaponId = 2, Name = "Sword & Shield",  ClassId = 2, SkillId = 2002, StatModifiers = new(), GivenAtRegister = true  },
            new WeaponDefinition { WeaponId = 3, Name = "Axe",             ClassId = 3, SkillId = 2003, StatModifiers = new(), GivenAtRegister = true  },
            new WeaponDefinition { WeaponId = 4, Name = "CrossBow",        ClassId = 4, SkillId = 2004, StatModifiers = new(), GivenAtRegister = true  },
            new WeaponDefinition { WeaponId = 5, Name = "Holy Book",       ClassId = 5, SkillId = 2005, StatModifiers = new(), GivenAtRegister = true  },
            new WeaponDefinition { WeaponId = 6, Name = "Staff of the Druid", ClassId = 6, SkillId = 2006, StatModifiers = new(), GivenAtRegister = true  },
            // Tier 2 — with stat modifiers, gacha only
            new WeaponDefinition { WeaponId = 7,  Name = "Sharp Dagger",   ClassId = 1, SkillId = 2001, StatModifiers = new() { Speed = 5 },           GivenAtRegister = false },
            new WeaponDefinition { WeaponId = 8,  Name = "Kite Shield",    ClassId = 2, SkillId = 2002, StatModifiers = new() { MaxHP = 1 },            GivenAtRegister = false },
            new WeaponDefinition { WeaponId = 9,  Name = "War Axe",        ClassId = 3, SkillId = 2003, StatModifiers = new() { MaxHP = 1 },            GivenAtRegister = false },
            new WeaponDefinition { WeaponId = 10, Name = "Compound Bow",   ClassId = 4, SkillId = 2004, StatModifiers = new() { Speed = 5 },           GivenAtRegister = false },
            new WeaponDefinition { WeaponId = 11, Name = "Sacred Tome",    ClassId = 5, SkillId = 2005, StatModifiers = new() { MaxSkillPoint = 1 },   GivenAtRegister = false },
            new WeaponDefinition { WeaponId = 12, Name = "Ancient Staff",  ClassId = 6, SkillId = 2006, StatModifiers = new() { MaxHP = 1 },            GivenAtRegister = false },
        });
    }

    private static async Task SeedTrinketsAsync(IMongoDatabase database)
    {
        var trinkets = database.GetCollection<TrinketDefinition>("TrinketDefinitions");
        await trinkets.InsertManyAsync(new[]
        {
            // Tier 1 — no stat modifiers
            new TrinketDefinition { TrinketId = 1, Name = "High Heels",    SkillId = 4001, StatModifiers = new(), GivenAtRegister = false },
            new TrinketDefinition { TrinketId = 2, Name = "Monocle",       SkillId = 4002, StatModifiers = new(), GivenAtRegister = true  },
            new TrinketDefinition { TrinketId = 3, Name = "Strong Boots",  SkillId = 4003, StatModifiers = new(), GivenAtRegister = true  },
            new TrinketDefinition { TrinketId = 4, Name = "Spyglass",      SkillId = 4004, StatModifiers = new(), GivenAtRegister = true  },
            // Tier 2 — with stat modifiers, gacha only
            new TrinketDefinition { TrinketId = 5, Name = "Steel Heels",      SkillId = 4001, StatModifiers = new() { Speed = 5 },         GivenAtRegister = false },
            new TrinketDefinition { TrinketId = 6, Name = "Jeweled Monocle",  SkillId = 4002, StatModifiers = new() { MaxSkillPoint = 1 }, GivenAtRegister = false },
            new TrinketDefinition { TrinketId = 7, Name = "Iron Boots",       SkillId = 4003, StatModifiers = new() { MaxHP = 1 },         GivenAtRegister = false },
            new TrinketDefinition { TrinketId = 8, Name = "Brass Spyglass",   SkillId = 4004, StatModifiers = new() { Speed = 5 },         GivenAtRegister = false },
        });
    }

    private static async Task SeedGachaBannersAsync(IMongoDatabase database)
    {
        var col = database.GetCollection<GachaBanner>("GachaBanners");
        if (await col.CountDocumentsAsync(_ => true) > 0)
        {
            Console.WriteLine("[Seed] GachaBanners already seeded, skipping.");
            return;
        }

        // RewardTier: 1=weapons/trinkets no stat  2=weapons/trinkets with stat  3=units
        // Weight is relative — higher = more common

        var standardBanner = new GachaBanner
        {
            Name = "Standard Summon",
            Description = "A permanent banner with all available units and equipment.",
            IsActive = true,
            StartDate = null,
            ExpiryDate = null,
            PityThreshold = 80,
            Items =
            [
                // Tier 3 — units (rarest); ClassId must be in UnitDefinition.ClassIds
                new() { Reward = new() { Type = RewardType.Unit, DefinitionId = 1, ClassId = 1, Amount = 1 }, Weight = 5,  IsFeatured = false, RewardTier = 3 }, // Assassin  → class Assassin(1)
                new() { Reward = new() { Type = RewardType.Unit, DefinitionId = 2, ClassId = 2, Amount = 1 }, Weight = 5,  IsFeatured = false, RewardTier = 3 }, // Tank      → class Tank(2)
                new() { Reward = new() { Type = RewardType.Unit, DefinitionId = 3, ClassId = 3, Amount = 1 }, Weight = 5,  IsFeatured = false, RewardTier = 3 }, // Warrior   → class Warrior(3)
                new() { Reward = new() { Type = RewardType.Unit, DefinitionId = 4, ClassId = 4, Amount = 1 }, Weight = 5,  IsFeatured = false, RewardTier = 3 }, // Archer    → class Archer(4)
                new() { Reward = new() { Type = RewardType.Unit, DefinitionId = 5, ClassId = 5, Amount = 1 }, Weight = 5,  IsFeatured = false, RewardTier = 3 }, // Physician → class Physician(5)
                new() { Reward = new() { Type = RewardType.Unit, DefinitionId = 6, ClassId = 6, Amount = 1 }, Weight = 5,  IsFeatured = false, RewardTier = 3 }, // Druid     → class Druid(6)

                // Tier 2 — weapons with stats (uncommon)
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 7,  Amount = 1 }, Weight = 15, IsFeatured = false, RewardTier = 2 }, // Sharp Dagger   (+5 Spd)
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 8,  Amount = 1 }, Weight = 15, IsFeatured = false, RewardTier = 2 }, // Kite Shield    (+1 HP)
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 9,  Amount = 1 }, Weight = 15, IsFeatured = false, RewardTier = 2 }, // War Axe        (+1 HP)
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 10, Amount = 1 }, Weight = 15, IsFeatured = false, RewardTier = 2 }, // Compound Bow   (+5 Spd)
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 11, Amount = 1 }, Weight = 15, IsFeatured = false, RewardTier = 2 }, // Sacred Tome    (+1 SP)
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 12, Amount = 1 }, Weight = 15, IsFeatured = false, RewardTier = 2 }, // Ancient Staff  (+1 HP)

                // Tier 2 — trinkets with stats (uncommon)
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 5, Amount = 1 }, Weight = 20, IsFeatured = false, RewardTier = 2 }, // Steel Heels     (+5 Spd)
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 6, Amount = 1 }, Weight = 20, IsFeatured = false, RewardTier = 2 }, // Jeweled Monocle (+1 SP)
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 7, Amount = 1 }, Weight = 20, IsFeatured = false, RewardTier = 2 }, // Iron Boots      (+1 HP)
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 8, Amount = 1 }, Weight = 20, IsFeatured = false, RewardTier = 2 }, // Brass Spyglass  (+5 Spd)

                // Tier 1 — weapons no stats (common)
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 1, Amount = 1 }, Weight = 25, IsFeatured = false, RewardTier = 1 }, // Dagger
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 2, Amount = 1 }, Weight = 25, IsFeatured = false, RewardTier = 1 }, // Sword & Shield
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 3, Amount = 1 }, Weight = 25, IsFeatured = false, RewardTier = 1 }, // Axe
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 4, Amount = 1 }, Weight = 25, IsFeatured = false, RewardTier = 1 }, // CrossBow
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 5, Amount = 1 }, Weight = 25, IsFeatured = false, RewardTier = 1 }, // Holy Book
                new() { Reward = new() { Type = RewardType.Weapon, DefinitionId = 6, Amount = 1 }, Weight = 25, IsFeatured = false, RewardTier = 1 }, // Staff of the Druid

                // Tier 1 — trinkets no stats (common)
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 1, Amount = 1 }, Weight = 40, IsFeatured = false, RewardTier = 1 }, // High Heels
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 2, Amount = 1 }, Weight = 40, IsFeatured = false, RewardTier = 1 }, // Monocle
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 3, Amount = 1 }, Weight = 40, IsFeatured = false, RewardTier = 1 }, // Strong Boots
                new() { Reward = new() { Type = RewardType.Trinket, DefinitionId = 4, Amount = 1 }, Weight = 40, IsFeatured = false, RewardTier = 1 }, // Spyglass

                // Tier 1 — gem fillers (most common)
                new() { Reward = new() { Type = RewardType.Gems, DefinitionId = 0, Amount = 50  }, Weight = 200, IsFeatured = false, RewardTier = 1 },
                new() { Reward = new() { Type = RewardType.Gems, DefinitionId = 0, Amount = 100 }, Weight = 80,  IsFeatured = false, RewardTier = 1 },
            ],
            PullOptions =
            [
                new() { PullType = PullType.Single, Price = 160  },
                new() { PullType = PullType.Ten,    Price = 1500 },
            ],
        };

        await col.InsertOneAsync(standardBanner);
        Console.WriteLine("[Seed] GachaBanners seeded.");
    }
}