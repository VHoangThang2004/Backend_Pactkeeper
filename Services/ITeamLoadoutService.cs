using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface ITeamLoadoutService
{
    Task<TeamLoadoutDocument?> GetByPlayerIdAsync(string playerId);
    Task SaveAsync(string playerId, TeamLoadoutDocument loadout);
}