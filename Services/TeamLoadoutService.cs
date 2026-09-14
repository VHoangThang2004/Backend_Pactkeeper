using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class TeamLoadoutService : ITeamLoadoutService
{
    private readonly IMongoRepository<TeamLoadoutDocument> _repository;

    public TeamLoadoutService(IMongoRepository<TeamLoadoutDocument> repository)
    {
        _repository = repository;
    }

    public Task<TeamLoadoutDocument?> GetByPlayerIdAsync(string playerId)
        => _repository.GetByFilterAsync(l => l.PlayerId == playerId);

    public async Task SaveAsync(string playerId, TeamLoadoutDocument loadout)
    {
        var existing = await _repository.GetByFilterAsync(l => l.PlayerId == playerId);
        if (existing == null)
        {
            loadout.PlayerId = playerId;
            loadout.LastUpdated = DateTime.UtcNow;
            await _repository.CreateAsync(loadout);
        }
        else
        {
            loadout.Id = existing.Id;
            loadout.PlayerId = playerId;
            loadout.LastUpdated = DateTime.UtcNow;
            await _repository.UpdateAsync(existing.Id, loadout);
        }
    }
}