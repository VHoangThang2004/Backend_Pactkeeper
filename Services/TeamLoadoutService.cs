using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class TeamLoadoutService : ITeamLoadoutService
{
    private readonly IMongoRepository<TeamLoadoutDocument> _repository;

    public TeamLoadoutService(IMongoRepository<TeamLoadoutDocument> repository) => _repository = repository;

    public Task<TeamLoadoutDocument?> GetByPlayerIdAsync(string playerId)
        => _repository.GetByFilterAsync(l => l.PlayerId == playerId);

    public async Task SaveAsync(string playerId, List<int> uIds)
    {
        var existing = await _repository.GetByFilterAsync(l => l.PlayerId == playerId);
        if (existing == null)
        {
            await _repository.CreateAsync(new TeamLoadoutDocument
            {
                PlayerId = playerId,
                UIds = uIds,
                LastUpdated = DateTime.UtcNow
            });
        }
        else
        {
            existing.UIds = uIds;
            existing.LastUpdated = DateTime.UtcNow;
            await _repository.UpdateAsync(existing.Id, existing);
        }
    }
}
