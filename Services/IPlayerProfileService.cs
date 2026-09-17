using System.Linq.Expressions;
using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IPlayerProfileService
{
    Task<PlayerProfile?> GetByFilterAsync(Expression<Func<PlayerProfile, bool>> filter);
    Task UpdateAsync(string id, PlayerProfile profile);
    Task CreateAsync(string playerId, PlayerProfile profile);
    Task CreateInitialProfileAsync(string playerId, string username);

    Task<(bool Success, string? Error)> UpdateUnitMovementSkillAsync(string playerId, string ownedUnitId, int skillId);
    Task<(bool Success, string? Error)> UpdateUnitClassSkillAsync(string playerId, string ownedUnitId, int skillId);
    Task<(bool Success, string? Error)> UpdateUnitWeaponAsync(string playerId, string ownedUnitId, string? ownedWeaponId);
    Task<List<OwnedWeaponResultDto>> GetOwnedWeaponsByClassIdsAsync(string playerId, List<int> classIds);

    Task<PlayerLoadoutDataDto?> GetLoadoutDataAsync(string playerId, List<int> uIds);
    Task<List<UnitConfigDto>> GetAllUnitConfigsAsync(string playerId);
}
