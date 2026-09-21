namespace GameInventoryApi.Models;

public class ServerState
{
    public bool MatchmakingBlocked { get; set; } = false;
    public bool LoginBlocked { get; set; } = false;
}