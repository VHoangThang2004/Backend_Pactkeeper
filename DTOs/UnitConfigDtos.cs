namespace GameInventoryApi.DTOs;

public record UpdateSkillDto(int SkillId);

public record UpdateUnitWeaponDto(string? OwnedWeaponId);

public record ClassIdFilterDto(List<int> ClassIds);

public record OwnedWeaponResultDto(
    string OwnedWeaponId,
    int WeaponDefinitionId,
    bool IsEquipped);

public record ClassSkillResultDto(int ClassId, int SkillId);
