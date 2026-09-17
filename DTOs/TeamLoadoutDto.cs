namespace GameInventoryApi.DTOs;

public record SaveLoadoutDto(List<int> UIds);

// Stats for the unit's current grade only
public record UnitGradeStatsDto(
    int MaxHP,
    int MaxSkillPoint,
    int Speed,
    float DamageMultiplier,
    float DamageReduction);

// Snapshot of an equipped weapon or trinket (null if slot is empty)
public record EquippedEquipmentDataDto(
    int DefinitionId,
    int SkillId,
    int MaxHP,
    int MaxSkillPoint,
    int Speed,
    float DamageMultiplier,
    float DamageReduction);

// Full raw config for one unit — consumer calculates final stats
public record UnitConfigDto(
    string OwnedUnitId,
    int UId,
    int Grade,
    int PassiveSkillId,
    int EquippedMovementSkillId,
    int EquippedClassSkillId,
    UnitGradeStatsDto GradeStats,
    EquippedEquipmentDataDto? EquippedWeapon,
    EquippedEquipmentDataDto? EquippedTrinket);

public record PlayerLoadoutDataDto(string PlayerId, List<UnitConfigDto> Units);

public record MatchLoadoutDataDto(PlayerLoadoutDataDto Player1, PlayerLoadoutDataDto Player2);
