using GameInventoryApi.DTOs;
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
    Task<(bool Deleted, bool HasHistory)> DeleteIfUnusedAsync(string id);
    Task<GachaBannerStatsDto?> GetBannerStatsAsync(string id);
    Task<string?> ValidateBannerAsync(
        string? name,
        string? bannerImageUrl,
        List<BannerItemDto>? items,
        List<PullOptionDto>? pullOptions,
        DateTime? startDate,
        DateTime? expiryDate,
        int? pityThreshold);
}
