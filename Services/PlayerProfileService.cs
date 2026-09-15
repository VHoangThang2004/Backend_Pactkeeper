using System.Linq.Expressions;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class PlayerProfileService : IPlayerProfileService
{
    private readonly IMongoRepository<PlayerProfile> _repository;

    public PlayerProfileService(IMongoRepository<PlayerProfile> repository)
    {
        _repository = repository;
    }

    public Task<PlayerProfile?> GetByFilterAsync(Expression<Func<PlayerProfile, bool>> filter)
        => _repository.GetByFilterAsync(filter);

    public Task UpdateAsync(string id, PlayerProfile profile)
        => _repository.UpdateAsync(id, profile);

    public Task CreateAsync(string playerId, PlayerProfile profile)
=> _repository.CreateAsync(profile);
}