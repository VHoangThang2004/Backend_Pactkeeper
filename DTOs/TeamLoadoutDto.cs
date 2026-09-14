namespace GameInventoryApi.DTOs;

public record TeamLoadoutDto(
    string PlayerId,
    List<UnitLoadoutEntryDto> Units
);

public record UnitLoadoutEntryDto(
    int UId,
    int MovementSkillId,
    int WeaponSkillId,
    int ClassSkillId,
    int EquipmentSkillId
);