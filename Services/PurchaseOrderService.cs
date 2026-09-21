using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IMongoRepository<PurchaseOrder> _repository;

    public PurchaseOrderService(IMongoRepository<PurchaseOrder> repository)
        => _repository = repository;

    public async Task<PurchaseOrder> CreateOrderAsync(string playerId, TopUpPack pack, long orderCode)
    {
        var order = new PurchaseOrder
        {
            OrderCode = orderCode,
            PlayerId = playerId,
            TopUpPackId = pack.Id,
            GemsAmount = pack.GemsAmount,
            Amount = pack.PriceVnd,
            WeaponDefinitionIds = pack.WeaponDefinitionIds,
            TrinketDefinitionIds = pack.TrinketDefinitionIds,
            UnitDefinitionIds = pack.UnitDefinitionIds,
            Status = PurchaseOrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        await _repository.CreateAsync(order);
        return order;
    }

    public Task<PurchaseOrder?> GetByOrderCodeAsync(long orderCode)
        => _repository.GetByFilterAsync(o => o.OrderCode == orderCode);

    public Task<List<PurchaseOrder>> GetByPlayerIdAsync(string playerId)
        => _repository.GetAllByFilterAsync(o => o.PlayerId == playerId);

    public async Task<bool> ConfirmOrderAsync(long orderCode)
    {
        var order = await _repository.GetByFilterAsync(o => o.OrderCode == orderCode);
        if (order == null || order.Status != PurchaseOrderStatus.Pending) return false;

        order.Status = PurchaseOrderStatus.Confirmed;
        order.ConfirmedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(order.Id, order);
        return true;
    }

    public async Task CancelOrderAsync(long orderCode)
    {
        var order = await _repository.GetByFilterAsync(o => o.OrderCode == orderCode);
        if (order == null || order.Status != PurchaseOrderStatus.Pending) return;

        order.Status = PurchaseOrderStatus.Cancelled;
        await _repository.UpdateAsync(order.Id, order);
    }
}
