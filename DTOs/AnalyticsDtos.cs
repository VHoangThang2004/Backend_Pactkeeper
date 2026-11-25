namespace GameInventoryApi.DTOs;

public class BalanceAnalyticsFilterDto
{
    public string? PatchVersion { get; set; }
    public string? Mode { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class BalanceMetricItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // "Unit", "Weapon", "Trinket", "Skill"
    public int TotalMatches { get; set; }
    public int PickCount { get; set; }
    public double PickRate { get; set; }
    public int WinCount { get; set; }
    public double WinRate { get; set; }
    public int SurvivedCount { get; set; }
    public double SurvivalRate { get; set; }
    public double AvgDamage { get; set; }
    public bool IsOutlier { get; set; }
}

public class BalanceAnalyticsResponseDto
{
    public string? PatchVersion { get; set; }
    public string? Mode { get; set; }
    public int TotalMatches { get; set; }
    public List<BalanceMetricItemDto> Units { get; set; } = [];
    public List<BalanceMetricItemDto> Weapons { get; set; } = [];
    public List<BalanceMetricItemDto> Trinkets { get; set; } = [];
    public List<BalanceMetricItemDto> Skills { get; set; } = [];
}

public class AiSuggestRequestDto
{
    public string PatchVersion { get; set; } = string.Empty;
    public string? Mode { get; set; }
    public bool ForceRegenerate { get; set; } = false;
    public double? HighWinRateThreshold { get; set; }
    public double? LowWinRateThreshold { get; set; }
    public int? MinPickCount { get; set; }
}
