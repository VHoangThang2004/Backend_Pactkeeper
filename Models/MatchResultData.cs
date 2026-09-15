namespace GameInventoryApi.Models;

public class MatchResultData
{
    public string WinnerId { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int TotalInstants { get; set; }
}