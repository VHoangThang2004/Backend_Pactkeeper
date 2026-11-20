using GameInventoryApi.Models;

namespace GameInventoryApi.DTOs;

public class AdminPlayerProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public int Gold { get; set; } = 0;
    public int Gems { get; set; } = 0;
    public bool IsBanned { get; set; } = false;
    public string? BanReason { get; set; }
    public DateTime LastLogin { get; set; } = DateTime.UtcNow;
    public bool IsOnline { get; set; } = false;
    public string PresenceStatus { get; set; } = "offline";
    public int OwnedUnitsCount { get; set; }
    public int OwnedWeaponsCount { get; set; }
    public int OwnedTrinketsCount { get; set; }
}

public class AdjustCurrenciesDto
{
    public string PlayerId { get; set; } = string.Empty;
    public int Gems { get; set; }
    public int Gold { get; set; }
    public string? Reason { get; set; }
}

public class BanPlayerDto
{
    public string PlayerId { get; set; } = string.Empty;
    public string? Duration { get; set; }
    public string? Reason { get; set; }
}

public class UnbanPlayerDto
{
    public string PlayerId { get; set; } = string.Empty;
}

public class AdminRewardProgressDto
{
    public string Id { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string? RuleTitle { get; set; }
    public int CurrentCount { get; set; }
    public int? TargetCount { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsReceived { get; set; }
    public DateTime? ReceivedAt { get; set; }
}

public class AdminPlayerDetailsDto : AdminPlayerProfileDto
{
    public int Rating { get; set; }
    public int RankTier { get; set; }
    public int RankWins { get; set; }
    public int RankLosses { get; set; }
    public string? BanDuration { get; set; }
    public List<OwnedUnit> OwnedUnits { get; set; } = [];
    public List<OwnedWeapon> OwnedWeapons { get; set; } = [];
    public List<OwnedTrinket> OwnedTrinkets { get; set; } = [];
    public Dictionary<string, GachaPityState> GachaPityByBanner { get; set; } = new();
    public List<AdminStoryProgressItemDto> StoryProgress { get; set; } = [];
    public List<int> TeamLoadoutUIds { get; set; } = [];
    public List<AdminRewardProgressDto> RewardProgress { get; set; } = [];
}

public class AdminStoryProgressItemDto
{
    public int ChapterId { get; set; }
    public int SceneId { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsChapterCompleted { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class UpdatePlayerStatsDto
{
    public string? Username { get; set; }
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Gold { get; set; }
    public int Gems { get; set; }
    public int Rating { get; set; }
    public int RankTier { get; set; }
    public int RankWins { get; set; }
    public int RankLosses { get; set; }
}

public class GrantPlayerUnitDto
{
    public int UnitDefinitionUId { get; set; }
    public int Grade { get; set; } = 1;
}

public class UpdateOwnedUnitDto
{
    public int Grade { get; set; } = 1;
    public List<int>? UnlockedClassIds { get; set; }
}

public class GrantPlayerWeaponDto
{
    public int WeaponDefinitionId { get; set; }
}

public class GrantPlayerTrinketDto
{
    public int TrinketDefinitionId { get; set; }
}

public class UpdatePlayerPityDto
{
    public string BannerId { get; set; } = string.Empty;
    public int PullsSinceRare { get; set; }
    public bool GuaranteedFeatured { get; set; }
}

public class UpdatePlayerStoryProgressDto
{
    public int ChapterId { get; set; }
    public int SceneId { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsChapterCompleted { get; set; }
}
