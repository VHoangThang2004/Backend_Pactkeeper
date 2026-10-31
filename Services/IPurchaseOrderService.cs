using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IPurchaseOrderService
{
    Task<PurchaseOrder> CreateOrderAsync(string playerId, TopUpPack pack, long orderCode, string? checkoutUrl = null);
    Task<PurchaseOrder?> GetByOrderCodeAsync(long orderCode);
    Task<List<PurchaseOrder>> GetByPlayerIdAsync(string playerId);
    Task<List<PurchaseOrder>> GetAllOrdersAsync();
    Task<bool> ConfirmOrderAsync(long orderCode);
    Task CancelOrderAsync(long orderCode);
}
