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

        // Check gems
        if (profile.Gems < option.Price)
            return (null, $"Not enough gems. Required: {option.Price}, have: {profile.Gems}.");

        // Deduct cost upfront
        profile.Gems -= option.Price;

        // Perform draws
        int totalWeight = banner.Items.Sum(i => i.Weight);
        var results = new List<GachaPullResultItemDto>();
        var itemsWon = new List<GachaResultItem>();
        int rarestTier = 0;

        for (int i = 0; i < PullCount(pullType); i++)
        {
            var item = DrawItem(banner.Items, totalWeight);
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
}

