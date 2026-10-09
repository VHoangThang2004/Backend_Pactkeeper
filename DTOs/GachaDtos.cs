using GameInventoryApi.Models;

namespace GameInventoryApi.DTOs;

public record GachaPullRequestDto(string BannerId, PullType PullType);

public record GachaPullResultItemDto(
    RewardDto Reward,
    bool IsDuplicate,
    int CompensationGems  // gems given instead when reward is a duplicate; 0 otherwise
);

public record GachaPullResponseDto(
    List<GachaPullResultItemDto> Results,
    int GemsSpent,
    int GemsRemaining
);

public record GachaHistoryEntryDto(
    string Id,
    string BannerId,
    string BannerName,
    PullType PullType,
    int GemsSpent,
    List<GachaPullResultItemDto> Items,
    DateTime Timestamp
);

public record GachaHistoryResponseDto(
    List<GachaHistoryEntryDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

