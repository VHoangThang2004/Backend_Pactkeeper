namespace GameInventoryApi.DTOs;

public record UpdateSkillDto(int SkillId);
public record UpdateAvatarDto(int AvatarId);
public record PlayerAvatarDto(string PlayerId, int AvatarId);
public record UpdateSignatureDto(string? Signature);

public record UpdateUnitWeaponDto(string? OwnedWeaponId);

public record UpdateUnitTrinketDto(string OwnedTrinketId);

public record ClassIdFilterDto(List<int> ClassIds);

public record OwnedWeaponResultDto(
    string OwnedWeaponId,
    int WeaponDefinitionId,
    bool IsEquipped);
public record OwnedTrinketResultDto(
    string OwnedTrinketId,
    int TrinketDefinitionId,
    bool IsEquipped);

public record ClassSkillResultDto(int ClassId, int SkillId);
