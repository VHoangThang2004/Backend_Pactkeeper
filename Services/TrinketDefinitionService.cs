using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class TrinketDefinitionService : ITrinketDefinitionService
{
    private readonly IMongoRepository<TrinketDefinition> _repository;

    public TrinketDefinitionService(IMongoRepository<TrinketDefinition> repository) => _repository = repository;

    public Task<List<TrinketDefinition>> GetAllAsync() => _repository.GetAllAsync();
    public Task<TrinketDefinition?> GetByIdAsync(string id) => _repository.GetByIdAsync(id);
    public Task<TrinketDefinition?> GetByTrinketIdAsync(int trinketId) => _repository.GetByFilterAsync(t => t.TrinketId == trinketId);
    public Task CreateAsync(TrinketDefinition trinket) => _repository.CreateAsync(trinket);
    public Task UpdateAsync(string id, TrinketDefinition trinket) => _repository.UpdateAsync(id, trinket);
    public Task DeleteAsync(string id) => _repository.DeleteAsync(id);
}
