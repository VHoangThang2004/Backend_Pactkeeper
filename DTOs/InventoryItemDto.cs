namespace GameInventoryApi.DTOs;

public record InventoryItemDto(
    string Id,
    string ItemId,
    string Name,
    int Quantity,
    string PlayerId
);