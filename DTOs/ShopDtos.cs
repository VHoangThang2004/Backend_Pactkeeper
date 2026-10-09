using GameInventoryApi.Models;

namespace GameInventoryApi.DTOs;

public record ShopItemDto(
    string Id,
    RewardDto Reward,
    int RewardTier,
    int Price,
    int? PurchaseLimit
);

public record PlayerShopItemDto(
    string Id,
    RewardDto Reward,
    int RewardTier,
    int Price,
    int? PurchaseLimit,
    int PurchasedCount,
    int? RemainingPurchases
);

public record ShopDto(
    string Id,
    string Name,
    string Description,
    int DisplayOrder,
    List<ShopItemDto> Items,
    PurchaseLimitReset ResetPeriod,
    bool IsActive,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    TimeSpan? DailyStartTime,
    TimeSpan? DailyEndTime,
    DateTime CreatedAt
);

public record PlayerShopDto(
    string Id,
    string Name,
    string Description,
    int DisplayOrder,
    List<PlayerShopItemDto> Items,
    PurchaseLimitReset ResetPeriod,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    TimeSpan? DailyStartTime,
    TimeSpan? DailyEndTime
);

public record CreateShopDto(
    string Name,
    string Description,
    int DisplayOrder,
    List<ShopItemDto> Items,
    PurchaseLimitReset ResetPeriod,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    TimeSpan? DailyStartTime,
    TimeSpan? DailyEndTime
);

public record UpdateShopDto(
    string Name,
    string Description,
    int DisplayOrder,
    List<ShopItemDto> Items,
    PurchaseLimitReset ResetPeriod,
    DateTime? StartDate,
    DateTime? ExpiryDate,
    TimeSpan? DailyStartTime,
    TimeSpan? DailyEndTime
);


public record ShopPurchaseRequestDto(
    string ShopId,
    string ShopItemId,
    int Quantity = 1
);

public record ShopPurchaseResponseDto(
    bool Success,
    RewardDto Reward,
    int Quantity,
    int GemsSpent,
    int GemsRemaining,
    int? RemainingPurchases,
    bool IsDuplicate,
    int CompensationGems
);
