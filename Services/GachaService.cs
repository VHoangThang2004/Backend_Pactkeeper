using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class GachaService(
    IGachaBannerService bannerService,
    IPlayerProfileService profileService,
    IMongoRepository<GachaResult> gachaResultRepository,
    IActionService actionService) : IGachaService
{
    private static int PullCount(PullType pullType) => pullType switch
    {
        PullType.Single => 1,
        PullType.Five   => 5,
        PullType.Ten    => 10,
        _ => 1
    };

    public async Task<(GachaPullResponseDto? Response, string? Error)> PullAsync(
        string playerId, string bannerId, PullType pullType)
    {
        // Validate banner
        var banner = await bannerService.GetByIdAsync(bannerId);
        if (banner == null || !banner.IsActive)
            return (null, "Banner not found or inactive.");

        var now = DateTime.UtcNow;
        if (banner.StartDate.HasValue && banner.StartDate > now)
            return (null, "Banner has not started yet.");
        if (banner.ExpiryDate.HasValue && banner.ExpiryDate <= now)
            return (null, "Banner has expired.");
        if (banner.Items.Count == 0)
            return (null, "Banner has no items configured.");

        // Validate pull option
        var option = banner.PullOptions.FirstOrDefault(o => o.PullType == pullType);
        if (option == null)
            return (null, $"Pull type '{pullType}' is not available on this banner.");

        // Load player profile
        var profile = await profileService.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null)
            return (null, "Player profile not found.");

        bool isTutorialFixedPull = profile.OnboardingStage == "NeedGacha";
        if (isTutorialFixedPull && pullType != PullType.Ten)
            return (null, "Please perform a 10x Summon to complete the tutorial.");

        // Check gems
        if (profile.Gems < option.Price)
            return (null, $"Not enough gems. Required: {option.Price}, have: {profile.Gems}.");

        // Deduct cost upfront
        profile.Gems -= option.Price;

        profile.GachaPityByBanner ??= new Dictionary<string, GachaPityState>();
        if (!profile.GachaPityByBanner.TryGetValue(bannerId, out var pityState))
        {
            pityState = new GachaPityState();
            profile.GachaPityByBanner[bannerId] = pityState;
        }

        var results = new List<GachaPullResultItemDto>();
        var itemsWon = new List<GachaResultItem>();
        int rarestTier = 0;

        if (isTutorialFixedPull)
        {
            // Deterministic 10-item onboarding pull using valid SeedData IDs:
            // 1 new Tier-3 Unit (Druid), 5 Tier-2 class Weapons (+stats for all 5 starter classes),
            // 3 Tier-2 Trinkets (+stats), and 1 Tier-1 Trinket (High Heels).
            var fixedTutorialRewards = new (Reward Reward, int Tier)[]
            {
                (new Reward { Type = RewardType.Unit,    DefinitionId = 6,  ClassId = 6, Amount = 1 }, 3), // Druid
                (new Reward { Type = RewardType.Weapon,  DefinitionId = 7,  Amount = 1 }, 2), // Sharp Dagger (Assassin +5 Spd)
                (new Reward { Type = RewardType.Weapon,  DefinitionId = 8,  Amount = 1 }, 2), // Kite Shield (Tank +1 HP)
                (new Reward { Type = RewardType.Weapon,  DefinitionId = 9,  Amount = 1 }, 2), // War Axe (Warrior +1 HP)
                (new Reward { Type = RewardType.Weapon,  DefinitionId = 10, Amount = 1 }, 2), // Compound Bow (Archer +5 Spd)
                (new Reward { Type = RewardType.Weapon,  DefinitionId = 11, Amount = 1 }, 2), // Sacred Tome (Physician +1 SP)
                (new Reward { Type = RewardType.Trinket, DefinitionId = 5,  Amount = 1 }, 2), // Steel Heels (+5 Spd)
                (new Reward { Type = RewardType.Trinket, DefinitionId = 6,  Amount = 1 }, 2), // Jeweled Monocle (+1 SP)
                (new Reward { Type = RewardType.Trinket, DefinitionId = 7,  Amount = 1 }, 2), // Iron Boots (+1 HP)
                (new Reward { Type = RewardType.Trinket, DefinitionId = 1,  Amount = 1 }, 1), // High Heels
            };

            foreach (var (reward, tier) in fixedTutorialRewards)
            {
                var (isDuplicate, compensationGems) = PlayerProfileService.ApplyReward(profile, reward, tier);
                results.Add(new GachaPullResultItemDto(
                    new RewardDto(reward.Type, reward.DefinitionId, reward.ClassId, reward.Amount),
                    isDuplicate,
                    compensationGems));
                itemsWon.Add(new GachaResultItem
                {
                    Reward = reward,
                    RewardTier = tier,
                    IsDuplicate = isDuplicate,
                    CompensationGems = compensationGems
                });
                if (tier > rarestTier) rarestTier = tier;
            }

            pityState.PullsSinceRare = 0;
            profile.OnboardingStage = "NeedUnitConfig";
        }
        else
        {
            // Perform draws with hard-pity & 50/50 featured tracking
            int totalWeight = banner.Items.Sum(i => Math.Max(0, i.Weight));
            if (totalWeight <= 0)
            {
                return (null, "Banner reward pool weights must total more than 0.");
            }

            var highTierItems = banner.Items.Where(i => i.IsFeatured || i.RewardTier >= 3).ToList();
            if (highTierItems.Count == 0)
            {
                int maxTier = banner.Items.Max(i => i.RewardTier);
                highTierItems = banner.Items.Where(i => i.RewardTier == maxTier).ToList();
            }
            var featuredItems = banner.Items.Where(i => i.IsFeatured).ToList();
            var nonFeaturedHighTier = highTierItems.Where(i => !i.IsFeatured).ToList();

            for (int i = 0; i < PullCount(pullType); i++)
            {
                pityState.PullsSinceRare++;

                BannerItem item;
                bool pityTriggered = banner.PityThreshold is int threshold
                    && threshold > 0
                    && pityState.PullsSinceRare >= threshold
                    && highTierItems.Count > 0;

                if (pityTriggered)
                {
                    if (featuredItems.Count > 0)
                    {
                        if (pityState.GuaranteedFeatured || nonFeaturedHighTier.Count == 0)
                        {
                            item = DrawWeightedItem(featuredItems);
                            pityState.GuaranteedFeatured = false;
                        }
                        else if (Random.Shared.Next(2) == 0)
                        {
                            item = DrawWeightedItem(featuredItems);
                            pityState.GuaranteedFeatured = false;
                        }
                        else
                        {
                            item = DrawWeightedItem(nonFeaturedHighTier);
                            pityState.GuaranteedFeatured = true;
                        }
                    }
                    else
                    {
                        item = DrawWeightedItem(highTierItems);
                    }

                    pityState.PullsSinceRare = 0;
                }
                else
                {
                    item = DrawWeightedItem(banner.Items);
                    if (item.IsFeatured || item.RewardTier >= 3)
                    {
                        pityState.PullsSinceRare = 0;
                        if (featuredItems.Count > 0)
                        {
                            pityState.GuaranteedFeatured = !item.IsFeatured;
                        }
                    }
                }

                var (isDuplicate, compensationGems) = PlayerProfileService.ApplyReward(profile, item.Reward, item.RewardTier);
                results.Add(new GachaPullResultItemDto(
                    new RewardDto(item.Reward.Type, item.Reward.DefinitionId, item.Reward.ClassId, item.Reward.Amount),
                    isDuplicate,
                    compensationGems));
                itemsWon.Add(new GachaResultItem
                {
                    Reward = item.Reward,
                    RewardTier = item.RewardTier,
                    IsDuplicate = isDuplicate,
                    CompensationGems = compensationGems
                });
                if (item.RewardTier > rarestTier) rarestTier = item.RewardTier;
            }
        }

        // Persist updated profile (gems already modified in-place by ApplyReward)
        await profileService.UpdateAsync(profile.Id, profile);

        var gachaResult = new GachaResult
        {
            PlayerId = playerId,
            BannerId = bannerId,
            BannerName = banner.Name,
            PullType = pullType,
            GemsSpent = option.Price,
            ItemsWon = itemsWon,
            Timestamp = DateTime.UtcNow,
        };
        await gachaResultRepository.CreateAsync(gachaResult);

        await actionService.LogActionAsync(playerId, "GachaPull", gachaResult.Id, new Dictionary<string, object>
        {
            ["bannerId"] = bannerId,
            ["pullCount"] = PullCount(pullType),
            ["rarestTier"] = rarestTier.ToString(),
        });

        Console.WriteLine($"[Gacha] {playerId} pulled {PullCount(pullType)}x on '{banner.Name}', spent {option.Price} gems, remaining {profile.Gems}");

        return (new GachaPullResponseDto(results, option.Price, profile.Gems), null);
    }

    public async Task<GachaHistoryResponseDto> GetHistoryAsync(
        string playerId, string? bannerId = null, int page = 1, int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var allResults = string.IsNullOrEmpty(bannerId)
            ? await gachaResultRepository.GetAllByFilterAsync(r => r.PlayerId == playerId)
            : await gachaResultRepository.GetAllByFilterAsync(r => r.PlayerId == playerId && r.BannerId == bannerId);

        var sorted = allResults.OrderByDescending(r => r.Timestamp).ToList();
        int totalCount = sorted.Count;
        int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        var paged = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new GachaHistoryEntryDto(
                r.Id,
                r.BannerId,
                r.BannerName,
                r.PullType,
                r.GemsSpent,
                r.ItemsWon.Select(i => new GachaPullResultItemDto(
                    new RewardDto(i.Reward.Type, i.Reward.DefinitionId, i.Reward.ClassId, i.Reward.Amount),
                    i.IsDuplicate,
                    i.CompensationGems)).ToList(),
                r.Timestamp
            ))
            .ToList();

        return new GachaHistoryResponseDto(paged, totalCount, page, pageSize, totalPages);
    }

    private static BannerItem DrawWeightedItem(IReadOnlyList<BannerItem> items)
    {
        int totalWeight = items.Sum(i => Math.Max(0, i.Weight));
        if (totalWeight <= 0)
        {
            return items[Random.Shared.Next(0, items.Count)];
        }

        int roll = Random.Shared.Next(0, totalWeight);
        int cumulative = 0;
        foreach (var item in items)
        {
            cumulative += Math.Max(0, item.Weight);
            if (roll < cumulative) return item;
        }
        return items[^1];
    }
}

