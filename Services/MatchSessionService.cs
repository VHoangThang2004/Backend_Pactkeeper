using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class MatchSessionService : IMatchSessionService
{
    private readonly IMongoRepository<MatchSession> _repository;

    public MatchSessionService(IMongoRepository<MatchSession> repository)
        => _repository = repository;

    public Task<MatchSession?> GetByMatchIdAsync(string matchId)
        => _repository.GetByFilterAsync(m => m.MatchId == matchId);

    public Task<MatchSession?> GetActiveMatchByPlayerIdAsync(string playerId)
        => _repository.GetByFilterAsync(m =>
            (m.Player1Id == playerId || m.Player2Id == playerId) &&
            m.Status == "active");

    public async Task<MatchSession> CreateMatchAsync(string player1Id, string player2Id, string serverIp, int port)
    {
        var match = new MatchSession
        {
            Player1Id = player1Id,
            Player2Id = player2Id,
            ServerIp = serverIp,
            ServerPort = port,
            Status = "pending"
        };
        await _repository.CreateAsync(match);
        return match;
    }

    public async Task UpdateStatusAsync(string matchId, string status)
    {
        var match = await GetByMatchIdAsync(matchId);
        if (match == null) return;
        match.Status = status;
        await _repository.UpdateAsync(match.Id, match);
    }
}