using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface ITopUpPackService
{
    Task<List<TopUpPack>> GetAllAsync();
    Task<List<TopUpPack>> GetAvailableAsync();
    Task<TopUpPack?> GetByIdAsync(string id);
    Task<TopUpPack> CreateAsync(TopUpPack pack);
    Task<TopUpPack?> UpdateAsync(string id, TopUpPack updated);
    Task UpdateAvailabilityAsync(string id, bool isAvailable);
    Task DeleteAsync(string id);
}
