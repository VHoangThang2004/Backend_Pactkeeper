using GameInventoryApi.Models;

namespace GameInventoryApi.DTOs;

public record RewardDto(RewardType Type, int DefinitionId, int ClassId, int Amount);

public record BannerItemDto(RewardDto Reward, int Weight, bool IsFeatured, int RewardTier);

public record PullOptionDto(PullType PullType, int Price);

public record CreateGachaBannerDto(
    string Name,
    string Description,
    List<BannerItemDto> Items,
    List<PullOptionDto> PullOptions,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    int? PityThreshold,
    string? BannerImageUrl = null
);

public record UpdateGachaBannerDto(
    string Name,
    string Description,
    List<BannerItemDto> Items,
    List<PullOptionDto> PullOptions,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    int? PityThreshold,
    string? BannerImageUrl = null
);

public record DropRateItemDto(
    RewardDto Reward,
    bool IsFeatured,
    int RewardTier,
    double DropRate  // percentage, e.g. 1.72
);

public record BannerDropRatesDto(string BannerId, string BannerName, List<DropRateItemDto> Items);

public record GachaBannerDto(
    string Id,
    string Name,
    string Description,
    List<BannerItemDto> Items,
    List<PullOptionDto> PullOptions,
    bool IsActive,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    int? PityThreshold,
    DateTime CreatedAt,
    string? BannerImageUrl = null
);

public record GachaBannerStatsDto(
    string BannerId,
    string BannerName,
    int TotalPullTransactions,
    int TotalDraws,
    long TotalGemsSpent,
    int UniquePlayers,
    int DuplicateDraws,
    long CompensationGemsAwarded,
    Dictionary<int, int> DrawsByTier
);
