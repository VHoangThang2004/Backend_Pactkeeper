using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class InventoryService : IInventoryService
{
    private readonly IMongoRepository<InventoryItem> _repository;

    public InventoryService(IMongoRepository<InventoryItem> repository)
    {
        _repository = repository;
    }

    public Task<List<InventoryItem>> GetAllAsync() => _repository.GetAllAsync();
    public Task<InventoryItem?> GetByIdAsync(string id) => _repository.GetByIdAsync(id);
    public Task CreateAsync(InventoryItem item) => _repository.CreateAsync(item);
    public Task UpdateAsync(string id, InventoryItem item) => _repository.UpdateAsync(id, item);
    public Task DeleteAsync(string id) => _repository.DeleteAsync(id);
}