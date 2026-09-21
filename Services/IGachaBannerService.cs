using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IGachaBannerService
{
    Task<List<GachaBanner>> GetAllAsync();
    Task<List<GachaBanner>> GetActiveAsync();
    Task<GachaBanner?> GetByIdAsync(string id);
    Task<GachaBanner> CreateAsync(GachaBanner banner);
    Task UpdateAsync(string id, GachaBanner banner);
    Task SetActiveAsync(string id, bool isActive);
}
