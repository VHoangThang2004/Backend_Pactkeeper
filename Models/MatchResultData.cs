namespace GameInventoryApi.Models;

public class AfterMatchPlayerData
{
    public string PlayerId { get; set; } = string.Empty;
    public int UnitsAlive { get; set; }
    public int TotalUnits { get; set; }
    public int UnitsDefeated { get; set; }
}

public class MatchResultData
{
    public string WinnerId { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int TotalInstants { get; set; }
    public bool IsForfeit { get; set; } = false;
    public string? ForfeitPlayerId { get; set; } = null;
    public List<AfterMatchPlayerData> AfterMatchTeamData { get; set; } = [];
}