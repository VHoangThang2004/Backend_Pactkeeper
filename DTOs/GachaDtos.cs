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
