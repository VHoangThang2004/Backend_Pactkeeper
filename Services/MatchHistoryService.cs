using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class MatchHistoryService : IMatchHistoryService
{
    private readonly IMongoRepository<MatchHistory> _repository;

    public MatchHistoryService(IMongoRepository<MatchHistory> repository)
        => _repository = repository;

    public async Task ArchiveAsync(MatchSession match)
    {
        var history = new MatchHistory
        {
            MatchId = match.MatchId,
            Player1Id = match.Player1Id,
            Player2Id = match.Player2Id,
            ServerIp = match.ServerIp,
            ServerPort = match.ServerPort,
            Mode = match.Mode,
            MapId = match.MapId,
            Status = match.Status,
            Result = match.Result,
            CreatedAt = match.CreatedAt
        };
        await _repository.CreateAsync(history);
    }

    public Task<List<MatchHistory>> GetByPlayerIdAsync(string playerId)
        => _repository.GetAllByFilterAsync(h =>
            h.Player1Id == playerId || h.Player2Id == playerId);
}