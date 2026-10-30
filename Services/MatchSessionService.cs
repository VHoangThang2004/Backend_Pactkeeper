using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using MongoDB.Driver;

namespace GameInventoryApi.Services;

public class MatchSessionService : IMatchSessionService
{
    private readonly IMongoRepository<MatchSession> _repository;
    private readonly MatchmakingService _matchmakingService;
    private readonly IMatchQueueService _queueService;
    private readonly IUserPresenceTracker _presenceTracker;

    public MatchSessionService(IMongoRepository<MatchSession> repository, MatchmakingService matchmakingService,
        IMatchQueueService queueService, IUserPresenceTracker presenceTracker)
    {
        _repository = repository;
        _matchmakingService = matchmakingService;
        _queueService = queueService;
        _presenceTracker = presenceTracker;
    }
    public Task<MatchSession?> GetByMatchIdAsync(string matchId)
        => _repository.GetByFilterAsync(m => m.MatchId == matchId);

    public Task<MatchSession?> GetActiveMatchByPlayerIdAsync(string playerId)
        => _repository.GetByFilterAsync(m =>
            (m.Player1Id == playerId || m.Player2Id == playerId) &&
            (m.Status == "pending" || m.Status == "active"));

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

    public Task<MatchSession?> TryClaimForFinishAsync(string matchId)
        => _repository.FindOneAndUpdateAsync(
            m => m.MatchId == matchId && (m.Status == "pending" || m.Status == "active"),
            Builders<MatchSession>.Update.Set(m => m.Status, "finishing"));

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
    public Task<List<MatchSession>> GetAllAsync() => _repository.GetAllAsync();
    public async Task DeleteAsync(string matchId)
    {
        var match = await GetByMatchIdAsync(matchId);
        if (match == null) return;
        await _repository.DeleteAsync(match.Id);
    }

    public async Task TerminateMatchAsync(MatchSession match, string reason)
    {
        // Free the players first: a leftover "matched" queue entry blocks re-queueing
        foreach (var playerId in new[] { match.Player1Id, match.Player2Id })
        {
            _presenceTracker.SetInMatch(playerId, false);
            await _queueService.RemoveByPlayerIdAsync(playerId);
        }

        if (!_matchmakingService.StopMatchServer(match.ProcessId, match.MatchId))
        {
            // Its server still holds the port: keep the record (so the port stays reserved, even across a
            // backend restart) as "stopping" — the reaper retries the kill every loop.
            await _repository.FindOneAndUpdateAsync(m => m.Id == match.Id,
                Builders<MatchSession>.Update.Set(m => m.Status, "stopping"));
            Console.WriteLine($"[Match] Could not stop server PID={match.ProcessId} for match {match.MatchId} " +
                              $"— port {match.ServerPort} kept, will retry. reason={reason}");
            return;
        }

        await _repository.DeleteAsync(match.Id);
        _matchmakingService.ReleasePort(match.ServerPort);

        Console.WriteLine($"[Match] Terminated match {match.MatchId} mode={match.Mode} " +
                          $"port={match.ServerPort} PID={match.ProcessId} reason={reason}");
    }
    public async Task EndAllMatchesAsync()
    {
        foreach (var session in await _repository.GetAllAsync())
            await TerminateMatchAsync(session, "admin ended all matches");
    }
}