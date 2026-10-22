using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IMatchQueueService
{
    Task<MatchQueueEntry?> GetByPlayerIdAsync(string playerId);
    Task JoinQueueAsync(string playerId, string mode = "pvp", int rating = 0);
    Task LeaveQueueAsync(string playerId);
    Task<List<MatchQueueEntry>> GetWaitingPlayersAsync();
    Task UpdateMatchedAsync(string playerId, string matchId, string serverIp, int serverPort);
    Task RemoveByPlayerIdAsync(string playerId);
    Task ClearAllQueueAsync();
}