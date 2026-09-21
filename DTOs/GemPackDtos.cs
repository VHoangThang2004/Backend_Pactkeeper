namespace GameInventoryApi.DTOs;

public record TopUpPackDto(
    string Id,
    string Name,
    int PriceVnd,
    int GemsAmount,
    List<int> WeaponDefinitionIds,
    List<int> TrinketDefinitionIds,
    List<int> UnitDefinitionIds,
    bool IsAvailable,
    List<string>? WeaponNames = null,
    List<string>? TrinketNames = null,
    List<string>? UnitNames = null
);

public record CreateTopUpPackDto(
    string Name,
    int PriceVnd,
    int GemsAmount,
    List<int> WeaponDefinitionIds,
    List<int> TrinketDefinitionIds,
    List<int> UnitDefinitionIds
);

public record UpdateTopUpPackAvailabilityDto(bool IsAvailable);
