using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface ITrinketDefinitionService
{
    Task<List<TrinketDefinition>> GetAllAsync();
    Task<TrinketDefinition?> GetByIdAsync(string id);
    Task<TrinketDefinition?> GetByTrinketIdAsync(int trinketId);
    Task CreateAsync(TrinketDefinition trinket);
    Task UpdateAsync(string id, TrinketDefinition trinket);
    Task DeleteAsync(string id);
}
