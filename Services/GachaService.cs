using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public class GachaService(IGachaBannerService bannerService, IPlayerProfileService profileService) : IGachaService
{
    // Gems awarded instead of a duplicate item, by tier
    private static int DuplicateCompensation(int tier) => tier switch
    {
        1 => 30,
        2 => 80,
        3 => 150,
        _ => 30
    };

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

        // Check gems
        if (profile.Gems < option.Price)
            return (null, $"Not enough gems. Required: {option.Price}, have: {profile.Gems}.");

        // Deduct cost upfront
        profile.Gems -= option.Price;

        // Perform draws
        int totalWeight = banner.Items.Sum(i => i.Weight);
        var results = new List<GachaPullResultItemDto>();

        for (int i = 0; i < PullCount(pullType); i++)
        {
            var item = DrawItem(banner.Items, totalWeight);
            var (isDuplicate, compensationGems) = ApplyReward(item, profile);
            results.Add(new GachaPullResultItemDto(
                new RewardDto(item.Reward.Type, item.Reward.DefinitionId, item.Reward.ClassId, item.Reward.Amount),
                isDuplicate,
                compensationGems));
        }

        // Persist updated profile (gems already modified in-place by ApplyReward)
        await profileService.UpdateAsync(profile.Id, profile);

        Console.WriteLine($"[Gacha] {playerId} pulled {PullCount(pullType)}x on '{banner.Name}', spent {option.Price} gems, remaining {profile.Gems}");

        return (new GachaPullResponseDto(results, option.Price, profile.Gems), null);
    }

    private static BannerItem DrawItem(List<BannerItem> items, int totalWeight)
    {
        int roll = Random.Shared.Next(0, totalWeight);
        int cumulative = 0;
        foreach (var item in items)
        {
            cumulative += item.Weight;
            if (roll < cumulative) return item;
        }
        return items[^1];
    }

    // Modifies profile in-place and returns duplicate info
    private static (bool isDuplicate, int compensationGems) ApplyReward(BannerItem item, PlayerProfile profile)
    {
        var reward = item.Reward;

        switch (reward.Type)
        {
            case RewardType.Gems:
                profile.Gems += reward.Amount;
                return (false, 0);

            case RewardType.Weapon:
                if (profile.OwnedWeapons.Any(w => w.WeaponDefinitionId == reward.DefinitionId))
                {
                    int comp = DuplicateCompensation(item.RewardTier);
                    profile.Gems += comp;
                    return (true, comp);
                }
                profile.OwnedWeapons.Add(new OwnedWeapon
                {
                    OwnedWeaponId = Guid.NewGuid().ToString(),
                    WeaponDefinitionId = reward.DefinitionId,
                });
                return (false, 0);

            case RewardType.Trinket:
                if (profile.OwnedTrinkets.Any(t => t.TrinketDefinitionId == reward.DefinitionId))
                {
                    int comp = DuplicateCompensation(item.RewardTier);
                    profile.Gems += comp;
                    return (true, comp);
                }
                profile.OwnedTrinkets.Add(new OwnedTrinket
                {
                    OwnedTrinketId = Guid.NewGuid().ToString(),
                    TrinketDefinitionId = reward.DefinitionId,
                });
                return (false, 0);

            case RewardType.Unit:
                var owned = profile.OwnedUnits.FirstOrDefault(u => u.UnitDefinitionUId == reward.DefinitionId);
                if (owned != null)
                {
                    if (owned.UnlockedClassIds.Contains(reward.ClassId))
                    {
                        // Class already unlocked — duplicate
                        int comp = DuplicateCompensation(item.RewardTier);
                        profile.Gems += comp;
                        return (true, comp);
                    }
                    // Unit owned but class not yet unlocked
                    owned.UnlockedClassIds.Add(reward.ClassId);
                    return (false, 0);
                }
                // Brand new unit
                profile.OwnedUnits.Add(new OwnedUnit
                {
                    OwnedUnitId = Guid.NewGuid().ToString(),
                    UnitDefinitionUId = reward.DefinitionId,
                    Grade = 1,
                    UnlockedClassIds = [reward.ClassId],
                    EquippedMovementSkillId = -1,
                    EquippedOwnedWeaponId = string.Empty,
                    EquippedOwnedTrinketId = string.Empty,
                    EquippedClassSkillId = -1,
                });
                return (false, 0);

            default:
                return (false, 0);
        }
    }
}
