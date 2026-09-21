using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class GachaBannerService : IGachaBannerService
{
    private readonly IMongoRepository<GachaBanner> _repository;

    public GachaBannerService(IMongoRepository<GachaBanner> repository)
        => _repository = repository;

    public Task<List<GachaBanner>> GetAllAsync()
        => _repository.GetAllAsync();

    public async Task<List<GachaBanner>> GetActiveAsync()
    {
        var active = await _repository.GetAllByFilterAsync(b => b.IsActive);
        var now = DateTime.UtcNow;
        return active
            .Where(b => (b.StartDate == null || b.StartDate <= now)
                     && (b.ExpiryDate == null || b.ExpiryDate > now))
            .ToList();
    }

    public Task<GachaBanner?> GetByIdAsync(string id)
        => _repository.GetByIdAsync(id);

    public async Task<GachaBanner> CreateAsync(GachaBanner banner)
    {
        await _repository.CreateAsync(banner);
        return banner;
    }

    public Task UpdateAsync(string id, GachaBanner banner)
        => _repository.UpdateAsync(id, banner);

    public async Task SetActiveAsync(string id, bool isActive)
    {
        var banner = await _repository.GetByIdAsync(id);
        if (banner == null) return;
        banner.IsActive = isActive;
        await _repository.UpdateAsync(id, banner);
    }
}
