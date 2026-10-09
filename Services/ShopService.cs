using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class ShopService : IShopService
{
    private readonly IMongoRepository<Shop> _shopRepository;
    private readonly IMongoRepository<ShopPurchaseRecord> _purchaseRecordRepository;
    private readonly IPlayerProfileService _profileService;
    private readonly IActionService _actionService;

    public ShopService(
        IMongoRepository<Shop> shopRepository,
        IMongoRepository<ShopPurchaseRecord> purchaseRecordRepository,
        IPlayerProfileService profileService,
        IActionService actionService)
    {
        _shopRepository = shopRepository;
        _purchaseRecordRepository = purchaseRecordRepository;
        _profileService = profileService;
        _actionService = actionService;
    }

    public Task<List<Shop>> GetAllAsync()
        => _shopRepository.GetAllAsync();

    public Task<Shop?> GetByIdAsync(string id)
        => _shopRepository.GetByIdAsync(id);

    public async Task<Shop> CreateAsync(Shop shop)
    {
        await _shopRepository.CreateAsync(shop);
        return shop;
    }

    public Task UpdateAsync(string id, Shop shop)
        => _shopRepository.UpdateAsync(id, shop);

    public async Task SetActiveAsync(string id, bool isActive)
    {
        var shop = await _shopRepository.GetByIdAsync(id);
        if (shop == null) return;
        shop.IsActive = isActive;
        await _shopRepository.UpdateAsync(id, shop);
    }

    public Task DeleteAsync(string id)
        => _shopRepository.DeleteAsync(id);

    public async Task<List<PlayerShopDto>> GetActiveShopsForPlayerAsync(string playerId)
    {
        var allShops = await _shopRepository.GetAllByFilterAsync(s => s.IsActive);
        var now = DateTime.UtcNow;

        var openShops = allShops
            .Where(s => IsShopOpenNow(s, now))
            .OrderBy(s => s.DisplayOrder)
            .ThenByDescending(s => s.CreatedAt)
            .ToList();

        var playerPurchases = await _purchaseRecordRepository.GetAllByFilterAsync(r => r.PlayerId == playerId);
        var purchaseMap = playerPurchases.ToDictionary(r => $"{r.ShopId}:{r.ShopItemId}");

        return openShops.Select(s => MapToPlayerShopDto(s, purchaseMap, now)).ToList();
    }

    public async Task<PlayerShopDto?> GetShopForPlayerAsync(string playerId, string shopId)
    {
        var shop = await _shopRepository.GetByIdAsync(shopId);
        if (shop == null || !shop.IsActive) return null;

        var now = DateTime.UtcNow;
        if (!IsShopOpenNow(shop, now)) return null;

        var playerPurchases = await _purchaseRecordRepository.GetAllByFilterAsync(
            r => r.PlayerId == playerId && r.ShopId == shopId);
        var purchaseMap = playerPurchases.ToDictionary(r => $"{r.ShopId}:{r.ShopItemId}");

        return MapToPlayerShopDto(shop, purchaseMap, now);
    }

    public async Task<(ShopPurchaseResponseDto? Response, string? Error)> PurchaseItemAsync(
        string playerId, string shopId, string shopItemId, int quantity = 1)
    {
        if (quantity <= 0)
            return (null, "Quantity must be at least 1.");

        var shop = await _shopRepository.GetByIdAsync(shopId);
        if (shop == null || !shop.IsActive)
            return (null, "Shop not found or inactive.");

        var now = DateTime.UtcNow;
        if (!IsShopOpenNow(shop, now))
            return (null, "Shop is currently closed.");

        var item = shop.Items.FirstOrDefault(i => i.Id == shopItemId);
        if (item == null)
            return (null, "Item not found in this shop.");

        // Check purchase quota / limit
        var recordKey = $"{shopId}:{shopItemId}";
        var record = await _purchaseRecordRepository.GetByFilterAsync(
            r => r.PlayerId == playerId && r.ShopId == shopId && r.ShopItemId == shopItemId);

        int effectivePurchased = GetEffectivePurchasedCount(shop.ResetPeriod, record, now);
        if (item.PurchaseLimit.HasValue && (effectivePurchased + quantity > item.PurchaseLimit.Value))
        {
            int remaining = Math.Max(0, item.PurchaseLimit.Value - effectivePurchased);
            return (null, $"Purchase limit reached. Remaining: {remaining}, requested: {quantity}.");
        }

        // Check player profile & gem balance
        var profile = await _profileService.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null)
            return (null, "Player profile not found.");

        int totalCost = item.Price * quantity;
        if (profile.Gems < totalCost)
            return (null, $"Not enough gems. Required: {totalCost}, have: {profile.Gems}.");

        // Deduct gems
        profile.Gems -= totalCost;

        // Apply item rewards
        bool anyDuplicate = false;
        int totalCompensationGems = 0;

        for (int i = 0; i < quantity; i++)
        {
            var (isDup, compGems) = PlayerProfileService.ApplyReward(profile, item.Reward, item.RewardTier);
            if (isDup)
            {
                anyDuplicate = true;
                totalCompensationGems += compGems;
            }
        }

        // Persist profile
        await _profileService.UpdateAsync(profile.Id, profile);

        // Update purchase record
        if (record == null)
        {
            record = new ShopPurchaseRecord
            {
                PlayerId = playerId,
                ShopId = shopId,
                ShopItemId = shopItemId,
                Quantity = quantity,
                LastPurchasedAt = now
            };
            await _purchaseRecordRepository.CreateAsync(record);
        }
        else
        {
            // If reset window passed, start fresh with new quantity, otherwise accumulate
            record.Quantity = effectivePurchased + quantity;
            record.LastPurchasedAt = now;
            await _purchaseRecordRepository.UpdateAsync(record.Id, record);
        }

        await _actionService.LogActionAsync(playerId, "ShopPurchase", shop.Id, new Dictionary<string, object>
        {
            ["shopId"] = shopId,
            ["shopItemId"] = shopItemId,
            ["quantity"] = quantity,
            ["gemsSpent"] = totalCost,
        });

        Console.WriteLine($"[Shop] {playerId} bought {quantity}x item '{shopItemId}' from '{shop.Name}' for {totalCost} gems, remaining {profile.Gems}");

        int? remainingPurchases = item.PurchaseLimit.HasValue
            ? Math.Max(0, item.PurchaseLimit.Value - (effectivePurchased + quantity))
            : null;

        var rewardDto = new RewardDto(item.Reward.Type, item.Reward.DefinitionId, item.Reward.ClassId, item.Reward.Amount);
        var response = new ShopPurchaseResponseDto(
            Success: true,
            Reward: rewardDto,
            Quantity: quantity,
            GemsSpent: totalCost,
            GemsRemaining: profile.Gems,
            RemainingPurchases: remainingPurchases,
            IsDuplicate: anyDuplicate,
            CompensationGems: totalCompensationGems
        );

        return (response, null);
    }

    public static bool IsShopOpenNow(Shop shop, DateTime now)
    {
        if (!shop.IsActive) return false;

        if (shop.StartDate.HasValue && shop.StartDate > now)
            return false;
        if (shop.ExpiryDate.HasValue && shop.ExpiryDate <= now)
            return false;

        if (shop.DailyStartTime.HasValue && shop.DailyEndTime.HasValue)
        {
            var timeOfDay = now.TimeOfDay;
            bool inWindow = shop.DailyStartTime.Value <= shop.DailyEndTime.Value
                ? (timeOfDay >= shop.DailyStartTime.Value && timeOfDay < shop.DailyEndTime.Value)
                : (timeOfDay >= shop.DailyStartTime.Value || timeOfDay < shop.DailyEndTime.Value);

            if (!inWindow) return false;
        }

        return true;
    }

    private static int GetEffectivePurchasedCount(PurchaseLimitReset resetPeriod, ShopPurchaseRecord? record, DateTime now)
    {
        if (record == null) return 0;
        return resetPeriod switch
        {
            PurchaseLimitReset.Lifetime => record.Quantity,
            PurchaseLimitReset.Daily => record.LastPurchasedAt.Date == now.Date ? record.Quantity : 0,
            PurchaseLimitReset.Weekly => IsSameUtcWeek(record.LastPurchasedAt, now) ? record.Quantity : 0,
            _ => record.Quantity
        };
    }

    private static bool IsSameUtcWeek(DateTime dt1, DateTime dt2)
    {
        var monday1 = dt1.Date.AddDays(-(int)(dt1.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)dt1.DayOfWeek - 1));
        var monday2 = dt2.Date.AddDays(-(int)(dt2.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)dt2.DayOfWeek - 1));
        return monday1 == monday2;
    }

    private static PlayerShopDto MapToPlayerShopDto(
        Shop shop, Dictionary<string, ShopPurchaseRecord> purchaseMap, DateTime now)
    {
        var items = shop.Items.Select(item =>
        {
            purchaseMap.TryGetValue($"{shop.Id}:{item.Id}", out var rec);
            int purchasedCount = GetEffectivePurchasedCount(shop.ResetPeriod, rec, now);
            int? remaining = item.PurchaseLimit.HasValue
                ? Math.Max(0, item.PurchaseLimit.Value - purchasedCount)
                : null;

            return new PlayerShopItemDto(
                item.Id,
                new RewardDto(item.Reward.Type, item.Reward.DefinitionId, item.Reward.ClassId, item.Reward.Amount),
                item.RewardTier,
                item.Price,
                item.PurchaseLimit,
                purchasedCount,
                remaining
            );
        }).ToList();

        return new PlayerShopDto(
            shop.Id,
            shop.Name,
            shop.Description,
            shop.DisplayOrder,
            items,
            shop.ResetPeriod,
            shop.StartDate,
            shop.ExpiryDate,
            shop.DailyStartTime,
            shop.DailyEndTime
        );
    }
}

