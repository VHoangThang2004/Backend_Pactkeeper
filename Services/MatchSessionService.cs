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

    public async Task<MatchSession> CreateMatchAsync(string player1Id, string player2Id, string serverIp, int port, string mode = "pvp", string mapId = "MD_PVP_001")
    {
        var match = new MatchSession
        {
            Player1Id = player1Id,
            Player2Id = player2Id,
            ServerIp = serverIp,
            ServerPort = port,
            Status = "pending",
            Mode = mode,
            MapId = mapId
        };
        await _repository.CreateAsync(match);
        return match;
    }
    public async Task UpdateStatusAsync(string matchId, string status, MatchResultData? result = null)
    {
        var match = await GetByMatchIdAsync(matchId);
        if (match == null) return;
        match.Status = status;
        if (result != null) match.Result = result;
        await _repository.UpdateAsync(match.Id, match);
    }

    public async Task UpdateProcessIdAsync(string matchId, int processId)
    {
        var match = await GetByMatchIdAsync(matchId);
        if (match == null) return;
        match.ProcessId = processId;
        await _repository.UpdateAsync(match.Id, match);
    }
    public async Task<List<MatchSession>> GetAllByPlayerIdAsync(string playerId)
    {
        return await _repository.GetAllByFilterAsync(m =>
            m.Player1Id == playerId || m.Player2Id == playerId);
    }
    public async Task DeleteAsync(string matchId)
    {
        var match = await GetByMatchIdAsync(matchId);
        if (match == null) return;
        await _repository.DeleteAsync(match.Id);
    }
}