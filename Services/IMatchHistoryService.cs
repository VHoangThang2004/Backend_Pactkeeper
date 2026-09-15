using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IMatchHistoryService
{
    Task ArchiveAsync(MatchSession match);
    Task<List<MatchHistory>> GetByPlayerIdAsync(string playerId);
}