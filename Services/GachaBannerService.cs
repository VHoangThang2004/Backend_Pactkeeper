using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class GachaBannerService : IGachaBannerService
{
    private readonly IMongoRepository<GachaBanner> _repository;
    private readonly IMongoRepository<GachaResult> _gachaResultRepository;
    private readonly IMongoRepository<UnitDefinition> _unitRepository;
    private readonly IMongoRepository<WeaponDefinition> _weaponRepository;
    private readonly IMongoRepository<TrinketDefinition> _trinketRepository;
    private readonly IMongoRepository<ClassDefinition> _classRepository;

    public GachaBannerService(
        IMongoRepository<GachaBanner> repository,
        IMongoRepository<GachaResult> gachaResultRepository,
        IMongoRepository<UnitDefinition> unitRepository,
        IMongoRepository<WeaponDefinition> weaponRepository,
        IMongoRepository<TrinketDefinition> trinketRepository,
        IMongoRepository<ClassDefinition> classRepository)
    {
        _repository = repository;
        _gachaResultRepository = gachaResultRepository;
        _unitRepository = unitRepository;
        _weaponRepository = weaponRepository;
        _trinketRepository = trinketRepository;
        _classRepository = classRepository;
    }

    public async Task<List<GachaBanner>> GetAllAsync()
    {
        var banners = await _repository.GetAllAsync();
        return banners
            .OrderByDescending(b => b.IsActive)
            .ThenByDescending(b => b.CreatedAt)
            .ToList();
    }

    public async Task<List<GachaBanner>> GetActiveAsync()
    {
        var active = await _repository.GetAllByFilterAsync(b => b.IsActive);
        var now = DateTime.UtcNow;
        return active
            .Where(b => (b.StartDate == null || b.StartDate <= now)
                     && (b.ExpiryDate == null || b.ExpiryDate > now))
            .ToList();
    }

    public Task<GachaBanner?> GetByIdAsync(string id)
        => _repository.GetByIdAsync(id);

    public async Task<GachaBanner> CreateAsync(GachaBanner banner)
    {
        await _repository.CreateAsync(banner);
        return banner;
    }

    public Task UpdateAsync(string id, GachaBanner banner)
        => _repository.UpdateAsync(id, banner);

    public async Task SetActiveAsync(string id, bool isActive)
    {
        var banner = await _repository.GetByIdAsync(id);
        if (banner == null) return;
        banner.IsActive = isActive;
        await _repository.UpdateAsync(id, banner);
    }

    public async Task<(bool Deleted, bool HasHistory)> DeleteIfUnusedAsync(string id)
    {
        var history = await _gachaResultRepository.GetTopAsync(r => r.BannerId == id, r => r.Timestamp, 1);
        if (history.Count > 0)
        {
            return (false, true);
        }

        await _repository.DeleteAsync(id);
        return (true, false);
    }

    public async Task<GachaBannerStatsDto?> GetBannerStatsAsync(string id)
    {
        var banner = await _repository.GetByIdAsync(id);
        if (banner == null) return null;

        var results = await _gachaResultRepository.GetAllByFilterAsync(r => r.BannerId == id);
        var drawsByTier = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0 };

        int totalDraws = 0;
        long totalGemsSpent = 0;
        int duplicateDraws = 0;
        long compensationGemsAwarded = 0;
        var players = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in results)
        {
            if (!string.IsNullOrEmpty(entry.PlayerId))
                players.Add(entry.PlayerId);

            totalGemsSpent += entry.GemsSpent;
            foreach (var item in entry.ItemsWon)
            {
                totalDraws++;
                if (item.IsDuplicate)
                {
                    duplicateDraws++;
                    compensationGemsAwarded += item.CompensationGems;
                }

                var tier = item.RewardTier > 0 ? item.RewardTier : 1;
                drawsByTier[tier] = drawsByTier.GetValueOrDefault(tier) + 1;
            }
        }

        return new GachaBannerStatsDto(
            BannerId: banner.Id,
            BannerName: banner.Name,
            TotalPullTransactions: results.Count,
            TotalDraws: totalDraws,
            TotalGemsSpent: totalGemsSpent,
            UniquePlayers: players.Count,
            DuplicateDraws: duplicateDraws,
            CompensationGemsAwarded: compensationGemsAwarded,
            DrawsByTier: drawsByTier
        );
    }

    public async Task<string?> ValidateBannerAsync(
        string? name,
        string? bannerImageUrl,
        List<BannerItemDto>? items,
        List<PullOptionDto>? pullOptions,
        DateTime? startDate,
        DateTime? expiryDate,
        int? pityThreshold)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Banner title is required.";

        if (!string.IsNullOrWhiteSpace(bannerImageUrl))
        {
            var trimmedUrl = bannerImageUrl.Trim();
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return "Banner Artwork URL must be a valid http:// or https:// URL.";
            }
        }

        if (pullOptions == null || pullOptions.Count == 0)
            return "At least one pull pricing option is required.";

        var seenPullTypes = new HashSet<PullType>();
        foreach (var opt in pullOptions)
        {
            if (!seenPullTypes.Add(opt.PullType))
                return $"Duplicate pull option type '{opt.PullType}' is not allowed.";
            if (opt.Price <= 0)
                return $"Price for pull option '{opt.PullType}' must be greater than 0 Gems.";
        }

        if (startDate.HasValue && expiryDate.HasValue && expiryDate.Value <= startDate.Value)
            return "Expiry date must be later than start date.";

        if (pityThreshold.HasValue && pityThreshold.Value < 1)
            return "Hard pity threshold must be at least 1 pull.";

        if (items == null || items.Count == 0)
            return null;

        var unitsTask = _unitRepository.GetAllAsync();
        var weaponsTask = _weaponRepository.GetAllAsync();
        var trinketsTask = _trinketRepository.GetAllAsync();
        var classesTask = _classRepository.GetAllAsync();
        await Task.WhenAll(unitsTask, weaponsTask, trinketsTask, classesTask);

        var unitByUid = unitsTask.Result
            .GroupBy(u => u.UId)
            .ToDictionary(g => g.Key, g => g.First());
        var weaponIds = weaponsTask.Result.Select(w => w.WeaponId).ToHashSet();
        var trinketIds = trinketsTask.Result.Select(t => t.TrinketId).ToHashSet();
        var classIds = classesTask.Result.Select(c => c.ClassId).ToHashSet();

        var seenRewards = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int entryNum = i + 1;

            if (item.Weight <= 0)
                return $"Pool entry #{entryNum} must have Weight greater than 0.";
            if (item.RewardTier < 1 || item.RewardTier > 3)
                return $"Pool entry #{entryNum} must specify RewardTier 1, 2, or 3.";

            switch (item.Reward.Type)
            {
                case RewardType.Gems:
                    if (item.Reward.Amount <= 0)
                        return $"Pool entry #{entryNum} (Gems) must specify Amount greater than 0.";
                    break;

                case RewardType.Unit:
                    if (item.Reward.DefinitionId <= 0 || !unitByUid.TryGetValue(item.Reward.DefinitionId, out var unitDef))
                        return $"Pool entry #{entryNum}: Unit #{item.Reward.DefinitionId} was not found in UnitDefinitions.";
                    if (item.Reward.ClassId <= 0)
                        return $"Pool entry #{entryNum}: Unit '{unitDef.UnitName}' must specify a valid ClassId (> 0).";
                    if (classIds.Count > 0 && !classIds.Contains(item.Reward.ClassId))
                        return $"Pool entry #{entryNum}: Class #{item.Reward.ClassId} was not found in ClassDefinitions.";
                    if (unitDef.ClassIds.Count > 0 && !unitDef.ClassIds.Contains(item.Reward.ClassId))
                        return $"Pool entry #{entryNum}: Class #{item.Reward.ClassId} is not valid for Unit '{unitDef.UnitName}'.";
                    break;

                case RewardType.Weapon:
                    if (item.Reward.DefinitionId <= 0 || !weaponIds.Contains(item.Reward.DefinitionId))
                        return $"Pool entry #{entryNum}: Weapon #{item.Reward.DefinitionId} was not found in WeaponDefinitions.";
                    break;

                case RewardType.Trinket:
                    if (item.Reward.DefinitionId <= 0 || !trinketIds.Contains(item.Reward.DefinitionId))
                        return $"Pool entry #{entryNum}: Trinket #{item.Reward.DefinitionId} was not found in TrinketDefinitions.";
                    break;

                default:
                    return $"Pool entry #{entryNum} has unsupported reward type '{item.Reward.Type}'.";
            }

            string dedupKey = item.Reward.Type switch
            {
                RewardType.Gems => $"Gems:{item.Reward.Amount}",
                RewardType.Unit => $"Unit:{item.Reward.DefinitionId}:{item.Reward.ClassId}",
                _ => $"{item.Reward.Type}:{item.Reward.DefinitionId}",
            };

            if (!seenRewards.Add(dedupKey))
                return $"Pool entry #{entryNum} duplicates an existing reward ({dedupKey}). Merge weights instead of repeating entries.";
        }

        return null;
    }
}
