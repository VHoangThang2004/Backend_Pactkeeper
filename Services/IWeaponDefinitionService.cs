using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IWeaponDefinitionService
{
    Task<List<WeaponDefinition>> GetAllAsync();
    Task<WeaponDefinition?> GetByIdAsync(string id);
    Task<WeaponDefinition?> GetByWeaponIdAsync(int weaponId);
    Task CreateAsync(WeaponDefinition weapon);
    Task UpdateAsync(string id, WeaponDefinition weapon);
    Task DeleteAsync(string id);
}
