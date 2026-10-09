using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IRewardProgressService
{
    Task<List<RewardProgress>> GetByPlayerIdAsync(string playerId);
    Task<(bool Success, string? Error)> ClaimAsync(string playerId, string progressId);
}
