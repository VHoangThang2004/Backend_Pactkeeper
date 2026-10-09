using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class RewardRuleService : IRewardRuleService
{
    private readonly IMongoRepository<RewardRule> _repository;

    public RewardRuleService(IMongoRepository<RewardRule> repository)
        => _repository = repository;

    public Task<List<RewardRule>> GetAllAsync()
        => _repository.GetAllAsync();

    public Task<List<RewardRule>> GetActiveAsync()
        => _repository.GetAllByFilterAsync(r => r.IsActive);

    public Task<RewardRule?> GetByIdAsync(string id)
        => _repository.GetByIdAsync(id);

    public async Task<RewardRule> CreateAsync(RewardRule rule)
    {
        await _repository.CreateAsync(rule);
        return rule;
    }

    public Task UpdateAsync(string id, RewardRule rule)
        => _repository.UpdateAsync(id, rule);

    public async Task SetActiveAsync(string id, bool isActive)
    {
        var rule = await _repository.GetByIdAsync(id);
        if (rule == null) return;
        rule.IsActive = isActive;
        await _repository.UpdateAsync(id, rule);
    }
}
