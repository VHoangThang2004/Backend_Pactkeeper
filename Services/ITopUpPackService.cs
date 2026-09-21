using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface ITopUpPackService
{
    Task<List<TopUpPack>> GetAllAsync();
    Task<List<TopUpPack>> GetAvailableAsync();
    Task<TopUpPack?> GetByIdAsync(string id);
    Task<TopUpPack> CreateAsync(TopUpPack pack);
    Task UpdateAvailabilityAsync(string id, bool isAvailable);
    Task DeleteAsync(string id);
}
