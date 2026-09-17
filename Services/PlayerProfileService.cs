using System.Linq.Expressions;
using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class PlayerProfileService : IPlayerProfileService
{
    private readonly IMongoRepository<PlayerProfile> _repository;
    private readonly IMongoRepository<UnitDefinition> _unitRepository;
    private readonly IMongoRepository<WeaponDefinition> _weaponRepository;
    private readonly IMongoRepository<TrinketDefinition> _trinketRepository;
    private readonly IMongoRepository<ClassDefinition> _classRepository;

    public PlayerProfileService(
        IMongoRepository<PlayerProfile> repository,
        IMongoRepository<UnitDefinition> unitRepository,
        IMongoRepository<WeaponDefinition> weaponRepository,
        IMongoRepository<TrinketDefinition> trinketRepository,
        IMongoRepository<ClassDefinition> classRepository)
    {
        _repository = repository;
        _unitRepository = unitRepository;
        _weaponRepository = weaponRepository;
        _trinketRepository = trinketRepository;
        _classRepository = classRepository;
    }

    public Task<PlayerProfile?> GetByFilterAsync(Expression<Func<PlayerProfile, bool>> filter)
        => _repository.GetByFilterAsync(filter);

    public Task UpdateAsync(string id, PlayerProfile profile)
        => _repository.UpdateAsync(id, profile);

    public Task CreateAsync(string playerId, PlayerProfile profile)
        => _repository.CreateAsync(profile);

    public async Task CreateInitialProfileAsync(string playerId, string username)
    {
        var starterUnits = await _unitRepository.GetAllByFilterAsync(u => u.GivenAtRegister);
        var starterWeapons = await _weaponRepository.GetAllByFilterAsync(w => w.GivenAtRegister);

        var profile = new PlayerProfile
        {
            PlayerId = playerId,
            Username = username,
            OwnedUnits = starterUnits.Select(u => new OwnedUnit
            {
                OwnedUnitId = Guid.NewGuid().ToString(),
                UnitDefinitionUId = u.UId,
                Grade = 1,
                UnlockedClassIds = new List<int>(u.ClassIds),
                EquippedMovementSkillId = -1,
                EquippedOwnedWeaponId = string.Empty,
                EquippedOwnedTrinketId = string.Empty,
                EquippedClassSkillId = -1,
            }).ToList(),
            OwnedWeapons = starterWeapons.Select(w => new OwnedWeapon
            {
                OwnedWeaponId = Guid.NewGuid().ToString(),
                WeaponDefinitionId = w.WeaponId,
            }).ToList(),
        };

        await _repository.CreateAsync(profile);
    }

    public async Task<(bool Success, string? Error)> UpdateUnitMovementSkillAsync(
        string playerId, string ownedUnitId, int skillId)
    {
        var profile = await _repository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return (false, "Profile not found.");

        var unit = profile.OwnedUnits.FirstOrDefault(u => u.OwnedUnitId == ownedUnitId);
        if (unit == null) return (false, "Unit not found.");

        if (skillId != -1)
        {
            var classDef = await _classRepository.GetByFilterAsync(c => c.MovementSkillId == skillId);
            if (classDef == null) return (false, "No class has that movement skill.");
            if (!unit.UnlockedClassIds.Contains(classDef.ClassId))
                return (false, $"Class {classDef.ClassId} is not unlocked for this unit.");
        }

        unit.EquippedMovementSkillId = skillId;
        await _repository.UpdateAsync(profile.Id, profile);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateUnitClassSkillAsync(
        string playerId, string ownedUnitId, int skillId)
    {
        var profile = await _repository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return (false, "Profile not found.");

        var unit = profile.OwnedUnits.FirstOrDefault(u => u.OwnedUnitId == ownedUnitId);
        if (unit == null) return (false, "Unit not found.");

        if (skillId != -1)
        {
            var classDef = await _classRepository.GetByFilterAsync(c => c.ClassSkillId == skillId);
            if (classDef == null) return (false, "No class has that class skill.");
            if (!unit.UnlockedClassIds.Contains(classDef.ClassId))
                return (false, $"Class {classDef.ClassId} is not unlocked for this unit.");
        }

        unit.EquippedClassSkillId = skillId;
        await _repository.UpdateAsync(profile.Id, profile);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateUnitWeaponAsync(
        string playerId, string ownedUnitId, string? ownedWeaponId)
    {
        var profile = await _repository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return (false, "Profile not found.");

        var unit = profile.OwnedUnits.FirstOrDefault(u => u.OwnedUnitId == ownedUnitId);
        if (unit == null) return (false, "Unit not found.");

        if (string.IsNullOrEmpty(ownedWeaponId))
        {
            unit.EquippedOwnedWeaponId = string.Empty;
            await _repository.UpdateAsync(profile.Id, profile);
            return (true, null);
        }

        var ownedWeapon = profile.OwnedWeapons.FirstOrDefault(w => w.OwnedWeaponId == ownedWeaponId);
        if (ownedWeapon == null) return (false, "Weapon not owned.");

        var weaponDef = await _weaponRepository.GetByFilterAsync(w => w.WeaponId == ownedWeapon.WeaponDefinitionId);
        if (weaponDef == null) return (false, "Weapon definition not found.");

        if (!unit.UnlockedClassIds.Contains(weaponDef.ClassId))
            return (false, $"Class {weaponDef.ClassId} is not unlocked for this unit.");

        foreach (var other in profile.OwnedUnits.Where(u => u.EquippedOwnedWeaponId == ownedWeaponId))
            other.EquippedOwnedWeaponId = string.Empty;

        unit.EquippedOwnedWeaponId = ownedWeaponId;
        await _repository.UpdateAsync(profile.Id, profile);
        return (true, null);
    }

    public async Task<List<OwnedWeaponResultDto>> GetOwnedWeaponsByClassIdsAsync(
        string playerId, List<int> classIds)
    {
        var profile = await _repository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return [];

        var weaponDefs = await _weaponRepository.GetAllByFilterAsync(w => classIds.Contains(w.ClassId));
        var defIdSet = weaponDefs.Select(w => w.WeaponId).ToHashSet();

        var equippedIds = profile.OwnedUnits
            .Select(u => u.EquippedOwnedWeaponId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        return profile.OwnedWeapons
            .Where(w => defIdSet.Contains(w.WeaponDefinitionId))
            .Select(w => new OwnedWeaponResultDto(
                w.OwnedWeaponId,
                w.WeaponDefinitionId,
                equippedIds.Contains(w.OwnedWeaponId)))
            .ToList();
    }

    public async Task<PlayerLoadoutDataDto?> GetLoadoutDataAsync(string playerId, List<int> uIds)
    {
        var profile = await _repository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return null;

        var units = await BuildUnitConfigsAsync(profile, uIds);
        return new PlayerLoadoutDataDto(playerId, units);
    }

    public async Task<List<UnitConfigDto>> GetAllUnitConfigsAsync(string playerId)
    {
        var profile = await _repository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return [];

        var allUIds = profile.OwnedUnits.Select(u => u.UnitDefinitionUId).ToList();
        return await BuildUnitConfigsAsync(profile, allUIds);
    }

    private async Task<List<UnitConfigDto>> BuildUnitConfigsAsync(PlayerProfile profile, List<int> uIds)
    {
        var unitDefs = await _unitRepository.GetAllByFilterAsync(u => uIds.Contains(u.UId));
        var unitDefMap = unitDefs.ToDictionary(u => u.UId);

        var weaponDefIds = profile.OwnedWeapons.Select(w => w.WeaponDefinitionId).ToList();
        var weaponDefs = await _weaponRepository.GetAllByFilterAsync(w => weaponDefIds.Contains(w.WeaponId));
        var weaponDefMap = weaponDefs.ToDictionary(w => w.WeaponId);

        var trinketDefIds = profile.OwnedTrinkets.Select(t => t.TrinketDefinitionId).ToList();
        var trinketDefs = trinketDefIds.Count > 0
            ? await _trinketRepository.GetAllByFilterAsync(t => trinketDefIds.Contains(t.TrinketId))
            : [];
        var trinketDefMap = trinketDefs.ToDictionary(t => t.TrinketId);

        var ownedWeaponMap = profile.OwnedWeapons.ToDictionary(w => w.OwnedWeaponId);
        var ownedTrinketMap = profile.OwnedTrinkets.ToDictionary(t => t.OwnedTrinketId);

        var result = new List<UnitConfigDto>();

        foreach (var uId in uIds)
        {
            var ownedUnit = profile.OwnedUnits.FirstOrDefault(u => u.UnitDefinitionUId == uId);
            if (ownedUnit == null) continue;

            if (!unitDefMap.TryGetValue(uId, out var unitDef)) continue;

            var gradeStats = unitDef.StatsByGrade.FirstOrDefault(g => g.Grade == ownedUnit.Grade)
                          ?? unitDef.StatsByGrade.LastOrDefault();
            if (gradeStats == null) continue;

            EquippedEquipmentDataDto? weaponData = null;
            if (!string.IsNullOrEmpty(ownedUnit.EquippedOwnedWeaponId)
                && ownedWeaponMap.TryGetValue(ownedUnit.EquippedOwnedWeaponId, out var ownedWeapon)
                && weaponDefMap.TryGetValue(ownedWeapon.WeaponDefinitionId, out var weaponDef))
            {
                weaponData = new EquippedEquipmentDataDto(
                    weaponDef.WeaponId, weaponDef.SkillId,
                    weaponDef.StatModifiers.MaxHP, weaponDef.StatModifiers.MaxSkillPoint,
                    weaponDef.StatModifiers.Speed, weaponDef.StatModifiers.DamageMultiplier,
                    weaponDef.StatModifiers.DamageReduction);
            }

            EquippedEquipmentDataDto? trinketData = null;
            if (!string.IsNullOrEmpty(ownedUnit.EquippedOwnedTrinketId)
                && ownedTrinketMap.TryGetValue(ownedUnit.EquippedOwnedTrinketId, out var ownedTrinket)
                && trinketDefMap.TryGetValue(ownedTrinket.TrinketDefinitionId, out var trinketDef))
            {
                trinketData = new EquippedEquipmentDataDto(
                    trinketDef.TrinketId, trinketDef.SkillId,
                    trinketDef.StatModifiers.MaxHP, trinketDef.StatModifiers.MaxSkillPoint,
                    trinketDef.StatModifiers.Speed, trinketDef.StatModifiers.DamageMultiplier,
                    trinketDef.StatModifiers.DamageReduction);
            }

            result.Add(new UnitConfigDto(
                OwnedUnitId: ownedUnit.OwnedUnitId,
                UId: uId,
                Grade: ownedUnit.Grade,
                PassiveSkillId: unitDef.PassiveSkillId,
                EquippedMovementSkillId: ownedUnit.EquippedMovementSkillId,
                EquippedClassSkillId: ownedUnit.EquippedClassSkillId,
                GradeStats: new UnitGradeStatsDto(
                    gradeStats.MaxHP, gradeStats.MaxSkillPoint, gradeStats.Speed,
                    gradeStats.DamageMultiplier, gradeStats.DamageReduction),
                EquippedWeapon: weaponData,
                EquippedTrinket: trinketData));
        }

        return result;
    }
}
