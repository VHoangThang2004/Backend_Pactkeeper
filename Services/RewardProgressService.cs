using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class RewardProgressService(
    IMongoRepository<RewardProgress> progressRepository,
    IMongoRepository<RewardRule> ruleRepository,
    IMongoRepository<StoryProgress> storyRepository,
    IChapterConfigService chapterConfigService,
    IPlayerProfileService profileService) : IRewardProgressService
{
    public async Task<List<RewardProgress>> GetByPlayerIdAsync(string playerId)
    {
        var progressList = await progressRepository.GetAllByFilterAsync(p => p.PlayerId == playerId);

        // Reconcile StoryProgress reward rules against the player's actual StoryProgress watermark
        // so chapters completed earlier (before new RewardRules were seeded or via Admin checkpoint update)
        // are accurately reflected as Completed (1 / 1) in 13_Quest.
        var storyRecords = await storyRepository.GetAllByFilterAsync(s => s.PlayerId == playerId);
        if (storyRecords.Count > 0)
        {
            var currentStory = storyRecords
                .OrderByDescending(s => s.ChapterId)
                .ThenByDescending(s => s.SceneId)
                .First();

            int maxCompletedChapterId = currentStory.ChapterId - 1;
            if (currentStory.IsChapterCompleted)
            {
                maxCompletedChapterId = currentStory.ChapterId;
            }
            else if (currentStory.IsCompleted)
            {
                var chapterCfg = await chapterConfigService.GetByChapterIdAsync(currentStory.ChapterId);
                if (chapterCfg?.Scenes != null && !chapterCfg.Scenes.Any(s => s.SceneId > currentStory.SceneId))
                {
                    maxCompletedChapterId = currentStory.ChapterId;
                }
            }

            if (maxCompletedChapterId >= 0)
            {
                var storyRules = await ruleRepository.GetAllByFilterAsync(
                    r => r.ActionType == "StoryProgress" && r.IsActive);

                foreach (var rule in storyRules)
                {
                    var chCond = rule.Conditions?.FirstOrDefault(c =>
                        string.Equals(c.Field, "chapterId", StringComparison.OrdinalIgnoreCase) && c.Op == "==");

                    if (chCond == null || !int.TryParse(chCond.Value, out int ruleChapterId))
                        continue;

                    if (ruleChapterId > maxCompletedChapterId)
                        continue;

                    int target = Math.Max(1, rule.TargetCount);
                    var existing = progressList.FirstOrDefault(p => p.RuleId == rule.Id);
                    if (existing == null)
                    {
                        var backfilled = new RewardProgress
                        {
                            PlayerId = playerId,
                            RuleId = rule.Id,
                            CurrentCount = target,
                            IsCompleted = true,
                            CompletedAt = currentStory.CompletedAt ?? currentStory.UpdatedAt,
                            IsReceived = false,
                        };
                        await progressRepository.CreateAsync(backfilled);
                        progressList.Add(backfilled);
                    }
                    else if (!existing.IsCompleted)
                    {
                        existing.CurrentCount = Math.Max(existing.CurrentCount, target);
                        existing.IsCompleted = true;
                        existing.CompletedAt ??= currentStory.CompletedAt ?? currentStory.UpdatedAt;
                        await progressRepository.UpdateAsync(existing.Id, existing);
                    }
                }
            }
        }

        return progressList;
    }

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
