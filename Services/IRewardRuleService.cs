using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IRewardRuleService
{
    Task<List<RewardRule>> GetAllAsync();
    Task<List<RewardRule>> GetActiveAsync();
    Task<RewardRule?> GetByIdAsync(string id);
    Task<RewardRule> CreateAsync(RewardRule rule);
    Task UpdateAsync(string id, RewardRule rule);
    Task SetActiveAsync(string id, bool isActive);
}
