using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class RewardProgressService(
    IMongoRepository<RewardProgress> progressRepository,
    IMongoRepository<RewardRule> ruleRepository,
    IPlayerProfileService profileService) : IRewardProgressService
{
    public Task<List<RewardProgress>> GetByPlayerIdAsync(string playerId)
        => progressRepository.GetAllByFilterAsync(p => p.PlayerId == playerId);

    public async Task<(bool Success, string? Error)> ClaimAsync(string playerId, string progressId)
    {
        var progress = await progressRepository.GetByIdAsync(progressId);
        if (progress == null || progress.PlayerId != playerId)
            return (false, "Reward progress not found.");

        if (!progress.IsCompleted)
            return (false, "Objective not yet completed.");

        if (progress.IsReceived)
            return (false, "Reward already claimed.");

        var rule = await ruleRepository.GetByIdAsync(progress.RuleId);
        if (rule == null)
            return (false, "Reward rule no longer exists.");

        var profile = await profileService.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null)
            return (false, "Player profile not found.");

        PlayerProfileService.ApplyReward(profile, rule.Reward);
        await profileService.UpdateAsync(profile.Id, profile);

        progress.IsReceived = true;
        progress.ReceivedAt = DateTime.UtcNow;
        await progressRepository.UpdateAsync(progress.Id, progress);

        return (true, null);
    }
}
