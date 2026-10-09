using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IShopService
{
    Task<List<Shop>> GetAllAsync();
    Task<Shop?> GetByIdAsync(string id);
    Task<Shop> CreateAsync(Shop shop);
    Task UpdateAsync(string id, Shop shop);
    Task SetActiveAsync(string id, bool isActive);
    Task DeleteAsync(string id);

    Task<List<PlayerShopDto>> GetActiveShopsForPlayerAsync(string playerId);
    Task<PlayerShopDto?> GetShopForPlayerAsync(string playerId, string shopId);
    Task<(ShopPurchaseResponseDto? Response, string? Error)> PurchaseItemAsync(
        string playerId, string shopId, string shopItemId, int quantity = 1);
}
