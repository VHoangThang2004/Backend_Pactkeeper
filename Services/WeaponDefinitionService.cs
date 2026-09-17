using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class WeaponDefinitionService : IWeaponDefinitionService
{
    private readonly IMongoRepository<WeaponDefinition> _repository;

    public WeaponDefinitionService(IMongoRepository<WeaponDefinition> repository) => _repository = repository;

    public Task<List<WeaponDefinition>> GetAllAsync() => _repository.GetAllAsync();
    public Task<WeaponDefinition?> GetByIdAsync(string id) => _repository.GetByIdAsync(id);
    public Task<WeaponDefinition?> GetByWeaponIdAsync(int weaponId) => _repository.GetByFilterAsync(w => w.WeaponId == weaponId);
    public Task CreateAsync(WeaponDefinition weapon) => _repository.CreateAsync(weapon);
    public Task UpdateAsync(string id, WeaponDefinition weapon) => _repository.UpdateAsync(id, weapon);
    public Task DeleteAsync(string id) => _repository.DeleteAsync(id);
}
