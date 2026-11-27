using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class StoryProgressService : IStoryProgressService
{
    private readonly IMongoRepository<StoryProgress> _repository;
    private readonly IChapterConfigService _chapterConfigService;
    private readonly IActionService _actionService;
    private readonly IPlayerProfileService _profileService;

    public StoryProgressService(
        IMongoRepository<StoryProgress> repository,
        IChapterConfigService chapterConfigService,
        IActionService actionService,
        IPlayerProfileService profileService)
    {
        _repository = repository;
        _chapterConfigService = chapterConfigService;
        _actionService = actionService;
        _profileService = profileService;
    }

    private async Task TryAdvanceOnboardingAfterChapter0Async(string playerId, int completedChapterId)
    {
        if (completedChapterId != 0)
            return;

        var profile = await _profileService.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile != null && profile.OnboardingStage == "PendingChapter0")
        {
            // Safeguard against softlock: guarantee enough gems for the 1,500-gem tutorial 10-pull
            if (profile.Gems < 1500)
                profile.Gems = 1600;

            profile.OnboardingStage = "NeedGacha";
            await _profileService.UpdateAsync(profile.Id, profile);
        }
    }

    // Entry point on every login.
    // New player: creates Chapter 0 Scene 0 isCompleted=false.
    // Returning player: returns their single furthest/current StoryProgress document.
    public async Task<StoryProgress> GetCurrentAsync(string playerId)
    {
        var all = await _repository.GetAllByFilterAsync(p => p.PlayerId == playerId);

        if (all.Count > 0)
        {
            var current = all
                .OrderByDescending(p => p.ChapterId)
                .ThenByDescending(p => p.SceneId)
                .First();

            // Clean up any legacy per-chapter duplicate documents so each user strictly has 1 record
            if (all.Count > 1)
            {
                foreach (var stale in all.Where(p => p.Id != current.Id))
                {
                    await _repository.DeleteAsync(stale.Id);
                }
            }

            // Migrate legacy completed-chapter state if AutoAdvance=true and a next chapter exists
            if (current.IsChapterCompleted)
            {
                var curScene = await _chapterConfigService.GetSceneAsync(current.ChapterId, current.SceneId);
                if (curScene == null || curScene.AutoAdvance)
                {
                    var nextChapter = await _chapterConfigService.GetByChapterIdAsync(current.ChapterId + 1);
                    var firstNextScene = nextChapter?.Scenes.OrderBy(s => s.SceneId).FirstOrDefault();
                    if (firstNextScene != null)
                    {
                        current.ChapterId = nextChapter!.ChapterId;
                        current.SceneId = firstNextScene.SceneId;
                        current.IsCompleted = false;
                        current.IsChapterCompleted = false;
                        current.UpdatedAt = DateTime.UtcNow;
                        await _repository.UpdateAsync(current.Id, current);
                    }
                }
            }

            return current;
        }

        // No progress at all — new player, create Chapter 0 Scene 0
        var newProgress = new StoryProgress
        {
            PlayerId = playerId,
            ChapterId = 0,
            SceneId = 0,
            IsCompleted = false,
            IsChapterCompleted = false,
            StartedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.CreateAsync(newProgress);
        return newProgress;
    }

    // Mark current scene as completed.
    // Battle scenes: rejected — server must call this via battle-complete instead.
    public async Task<StoryProgress> CompleteCurrentAsync(string playerId)
    {
        var current = await GetCurrentAsync(playerId);

        current.IsCompleted = true;
        current.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(current.Id, current);
        return current;
    }

    // Complete current scene; if completedScene.AutoAdvance, also move to the next scene
    // (or the first scene of the next chapter). The final state is computed in memory and persisted
    // with ONE UpdateAsync on the player's single StoryProgress document.
    public async Task<AdvanceResult> CompleteAndAdvanceAsync(string playerId, SceneConfig completedScene)
    {
        var current = await GetCurrentAsync(playerId);
        var now = DateTime.UtcNow;

        var chapter = await _chapterConfigService.GetByChapterIdAsync(current.ChapterId);
        var nextScene = chapter?.Scenes
            .Where(s => s.SceneId > current.SceneId)
            .OrderBy(s => s.SceneId)
            .FirstOrDefault();

        current.IsCompleted = true;
        current.UpdatedAt = now;

        if (!completedScene.AutoAdvance)
        {
            if (nextScene == null)
            {
                current.IsChapterCompleted = true;
                current.CompletedAt = now;
            }

            await _repository.UpdateAsync(current.Id, current);

            if (nextScene == null)
            {
                await TryAdvanceOnboardingAfterChapter0Async(playerId, current.ChapterId);
                await _actionService.LogActionAsync(playerId, "StoryProgress", current.Id, new Dictionary<string, object>
                {
                    ["chapterId"] = current.ChapterId,
                    ["sceneId"] = current.SceneId,
                    ["isChapterComplete"] = true,
                });
            }

            return new AdvanceResult(current, completedScene, false);
        }

        if (nextScene == null)
        {
            int completedChapterId = current.ChapterId;
            int completedSceneId = current.SceneId;

            await TryAdvanceOnboardingAfterChapter0Async(playerId, completedChapterId);

            var nextChapter = await _chapterConfigService.GetByChapterIdAsync(completedChapterId + 1);
            var firstNextChapterScene = nextChapter?.Scenes
                .OrderBy(s => s.SceneId)
                .FirstOrDefault();

            if (firstNextChapterScene != null)
            {
                current.ChapterId = nextChapter!.ChapterId;
                current.SceneId = firstNextChapterScene.SceneId;
                current.IsCompleted = false;
                current.IsChapterCompleted = false;
                await _repository.UpdateAsync(current.Id, current);

                await _actionService.LogActionAsync(playerId, "StoryProgress", current.Id, new Dictionary<string, object>
                {
                    ["chapterId"] = completedChapterId,
                    ["sceneId"] = completedSceneId,
                    ["isChapterComplete"] = true,
                });

                return new AdvanceResult(current, firstNextChapterScene, true);
            }

            current.IsChapterCompleted = true;
            current.CompletedAt = now;
            await _repository.UpdateAsync(current.Id, current);

            await _actionService.LogActionAsync(playerId, "StoryProgress", current.Id, new Dictionary<string, object>
            {
                ["chapterId"] = completedChapterId,
                ["sceneId"] = completedSceneId,
                ["isChapterComplete"] = true,
            });

            return new AdvanceResult(current, null, true);
        }

        current.SceneId = nextScene.SceneId;
        current.IsCompleted = false;
        current.IsChapterCompleted = false;
        await _repository.UpdateAsync(current.Id, current);
        return new AdvanceResult(current, nextScene, true);
    }

    // Advance to the next scene.
    // Rejected if current scene not yet completed.
    // Returns null if no next scene exists (chapter fully done).
    public async Task<StoryProgress?> StartNextAsync(string playerId)
    {
        var current = await GetCurrentAsync(playerId);

        // Must be completed
        if (!current.IsCompleted)
            return null;

        // Find next scene in current chapter config
        var chapter = await _chapterConfigService.GetByChapterIdAsync(current.ChapterId);
        var nextScene = chapter?.Scenes
            .Where(s => s.SceneId > current.SceneId)
            .OrderBy(s => s.SceneId)
            .FirstOrDefault();

        if (nextScene == null)
        {
            int completedChapterId = current.ChapterId;
            int completedSceneId = current.SceneId;

            await TryAdvanceOnboardingAfterChapter0Async(playerId, completedChapterId);

            var nextChapter = await _chapterConfigService.GetByChapterIdAsync(completedChapterId + 1);
            var firstNextChapterScene = nextChapter?.Scenes
                .OrderBy(s => s.SceneId)
                .FirstOrDefault();

            if (firstNextChapterScene != null)
            {
                current.ChapterId = nextChapter!.ChapterId;
                current.SceneId = firstNextChapterScene.SceneId;
                current.IsCompleted = false;
                current.IsChapterCompleted = false;
                current.UpdatedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(current.Id, current);
                return current;
            }

            // No more chapters — mark final chapter complete
            if (!current.IsChapterCompleted)
            {
                current.IsChapterCompleted = true;
                current.CompletedAt = DateTime.UtcNow;
                current.UpdatedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(current.Id, current);

                await _actionService.LogActionAsync(playerId, "StoryProgress", current.Id, new Dictionary<string, object>
                {
                    ["chapterId"] = completedChapterId,
                    ["sceneId"] = completedSceneId,
                    ["isChapterComplete"] = true,
                });
            }

            return current;
        }

        // Move to next scene — set isCompleted=false so client runs it immediately
        current.SceneId = nextScene.SceneId;
        current.IsCompleted = false;
        current.IsChapterCompleted = false;
        current.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(current.Id, current);
        return current;
    }

    public async Task<List<StoryProgress>> GetAllAsync(string playerId)
    {
        var current = await GetCurrentAsync(playerId);
        return [current];
    }

    public async Task<bool> IsChapterCompletedAsync(string playerId, int chapterId)
    {
        var current = await GetCurrentAsync(playerId);
        return current.ChapterId > chapterId ||
               (current.ChapterId == chapterId && current.IsChapterCompleted);
    }

    public async Task<int> GetMaxCompletedChapterIdAsync(string playerId)
    {
        var current = await GetCurrentAsync(playerId);
        return current.IsChapterCompleted ? current.ChapterId : current.ChapterId - 1;
    }

    public async Task<(StoryProgress? Progress, string? Error)> PrepareSelectedSceneAsync(
        string playerId, int targetChapterId, int targetSceneId)
    {
        var chapterCfg = await _chapterConfigService.GetByChapterIdAsync(targetChapterId);
        if (chapterCfg == null)
            return (null, $"Chapter {targetChapterId} configuration not found.");

        var sceneCfg = chapterCfg.Scenes.FirstOrDefault(s => s.SceneId == targetSceneId);
        if (sceneCfg == null)
            return (null, $"Scene {targetSceneId} not found in Chapter {targetChapterId}.");

        var current = await GetCurrentAsync(playerId);

        if (targetChapterId > current.ChapterId)
            return (null, $"Chapter {targetChapterId} is still locked.");

        // Replaying an earlier already-completed chapter (or any chapter after all chapters are completed)
        // does not mutate the player's current StoryProgress document.
        if (targetChapterId < current.ChapterId || current.IsChapterCompleted)
            return (current, null);

        int firstSceneId = chapterCfg.Scenes.OrderBy(s => s.SceneId).First().SceneId;
        int firstBattleSceneId = chapterCfg.Scenes
            .Where(s => s.Type == SceneType.Battle)
            .OrderBy(s => s.SceneId)
            .Select(s => (int?)s.SceneId)
            .FirstOrDefault() ?? firstSceneId;

        int rawHighWaterMark = current.IsCompleted
            ? current.SceneId + 1
            : current.SceneId;
        int unlockedSceneHighWaterMark = Math.Max(firstBattleSceneId, rawHighWaterMark);

        if (targetSceneId > unlockedSceneHighWaterMark)
            return (null, $"Scene {targetChapterId}.{targetSceneId} is not unlocked yet.");

        // Only advance current.SceneId forward if the current scene was completed with AutoAdvance=false
        // and the player is starting the next unlocked scene. Replaying an earlier scene (targetSceneId < current.SceneId)
        // leaves current progress untouched.
        if (targetSceneId > current.SceneId)
        {
            current.SceneId = targetSceneId;
            current.IsCompleted = false;
            current.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(current.Id, current);
        }

        return (current, null);
    }
}
