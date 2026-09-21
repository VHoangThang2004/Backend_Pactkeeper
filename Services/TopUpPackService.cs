using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class TopUpPackService : ITopUpPackService
{
    private readonly IMongoRepository<TopUpPack> _repository;

    public TopUpPackService(IMongoRepository<TopUpPack> repository)
        => _repository = repository;

    public Task<List<TopUpPack>> GetAllAsync()
        => _repository.GetAllAsync();

    public Task<List<TopUpPack>> GetAvailableAsync()
        => _repository.GetAllByFilterAsync(p => p.IsAvailable);

    public Task<TopUpPack?> GetByIdAsync(string id)
        => _repository.GetByIdAsync(id);

    public async Task<TopUpPack> CreateAsync(TopUpPack pack)
    {
        await _repository.CreateAsync(pack);
        return pack;
    }

    public async Task UpdateAvailabilityAsync(string id, bool isAvailable)
    {
        var pack = await _repository.GetByIdAsync(id);
        if (pack == null) return;
        pack.IsAvailable = isAvailable;
        await _repository.UpdateAsync(id, pack);
    }

    public Task DeleteAsync(string id)
        => _repository.DeleteAsync(id);
}
