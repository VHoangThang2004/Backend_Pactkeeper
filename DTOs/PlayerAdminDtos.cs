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
