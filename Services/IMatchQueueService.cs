using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IMatchQueueService
{
    Task<MatchQueueEntry?> GetByPlayerIdAsync(string playerId);
    Task JoinQueueAsync(string playerId);
    Task LeaveQueueAsync(string playerId);
    Task<List<MatchQueueEntry>> GetWaitingPlayersAsync();
    Task UpdateMatchedAsync(string playerId, string matchId, string serverIp, int serverPort);
}