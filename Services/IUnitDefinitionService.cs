using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IUnitDefinitionService
{
    Task<List<UnitDefinition>> GetAllAsync();
    Task<UnitDefinition?> GetByIdAsync(string id);
    Task<UnitDefinition?> GetByUIdAsync(int uId);
    Task CreateAsync(UnitDefinition unit);
    Task UpdateAsync(string id, UnitDefinition unit);
    Task DeleteAsync(string id);
}
