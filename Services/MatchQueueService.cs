using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class MatchQueueService : IMatchQueueService
{
    private readonly IMongoRepository<MatchQueueEntry> _repository;

    public MatchQueueService(IMongoRepository<MatchQueueEntry> repository)
        => _repository = repository;

    public Task<MatchQueueEntry?> GetByPlayerIdAsync(string playerId)
        => _repository.GetByFilterAsync(e => e.PlayerId == playerId);

    public async Task JoinQueueAsync(string playerId)
    {
        var existing = await GetByPlayerIdAsync(playerId);
        if (existing != null) return; // already in queue

        await _repository.CreateAsync(new MatchQueueEntry { PlayerId = playerId });
        Console.WriteLine($"[Queue] Player {playerId} joined queue.");
    }

    public async Task LeaveQueueAsync(string playerId)
    {
        var existing = await GetByPlayerIdAsync(playerId);
        if (existing == null) return;
        await _repository.DeleteAsync(existing.Id);
        Console.WriteLine($"[Queue] Player {playerId} left queue.");
    }

    public async Task<List<MatchQueueEntry>> GetWaitingPlayersAsync()
        => await _repository.GetAllByFilterAsync(e => e.Status == "waiting");

    public async Task UpdateMatchedAsync(string playerId, string matchId, string serverIp, int serverPort)
    {
        var entry = await GetByPlayerIdAsync(playerId);
        if (entry == null) return;
        entry.Status = "matched";
        entry.MatchId = matchId;
        entry.ServerIp = serverIp;
        entry.ServerPort = serverPort;
        await _repository.UpdateAsync(entry.Id, entry);
    }

    public async Task RemoveByPlayerIdAsync(string playerId)
    {
        var entry = await GetByPlayerIdAsync(playerId);
        if (entry == null) return;
        await _repository.DeleteAsync(entry.Id);
    }
}