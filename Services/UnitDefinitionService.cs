using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class UnitDefinitionService : IUnitDefinitionService
{
    private readonly IMongoRepository<UnitDefinition> _repository;

    public UnitDefinitionService(IMongoRepository<UnitDefinition> repository) => _repository = repository;

    public Task<List<UnitDefinition>> GetAllAsync() => _repository.GetAllAsync();
    public Task<UnitDefinition?> GetByIdAsync(string id) => _repository.GetByIdAsync(id);
    public Task<UnitDefinition?> GetByUIdAsync(int uId) => _repository.GetByFilterAsync(u => u.UId == uId);
    public Task CreateAsync(UnitDefinition unit) => _repository.CreateAsync(unit);
    public Task UpdateAsync(string id, UnitDefinition unit) => _repository.UpdateAsync(id, unit);
    public Task DeleteAsync(string id) => _repository.DeleteAsync(id);
}
