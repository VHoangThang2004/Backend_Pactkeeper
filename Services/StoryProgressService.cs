using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class StoryProgressService : IStoryProgressService
{
    private readonly IMongoRepository<StoryProgress> _repository;
    private readonly IChapterConfigService _chapterConfigService;
    private readonly IActionService _actionService;

    public StoryProgressService(
        IMongoRepository<StoryProgress> repository,
        IChapterConfigService chapterConfigService,
        IActionService actionService)
    {
        _repository = repository;
        _chapterConfigService = chapterConfigService;
        _actionService = actionService;
    }

    // Entry point on every login.
    // New player: creates Chapter 0 Scene 0 isCompleted=false.
    // Returning player: returns their current active scene.
    public async Task<StoryProgress> GetCurrentAsync(string playerId)
    {
        var all = await _repository.GetAllByFilterAsync(p => p.PlayerId == playerId);

        // Return earliest incomplete chapter
        var current = all
            .Where(p => !p.IsChapterCompleted)
            .OrderBy(p => p.ChapterId)
            .FirstOrDefault();

        if (current != null)
            return current;

        // All existing chapters are completed — return the last completed one
        // so callers can read IsChapterCompleted=true without creating a new document
        if (all.Count > 0)
            return all.OrderByDescending(p => p.ChapterId).First();

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
    // (or mark the chapter complete). The final state is computed in memory and persisted
    // with ONE UpdateAsync, so there is no half-done state (completed but not advanced).
    public async Task<AdvanceResult> CompleteAndAdvanceAsync(string playerId, SceneConfig completedScene)
    {
        var current = await GetCurrentAsync(playerId);
        var now = DateTime.UtcNow;

        current.IsCompleted = true;
        current.UpdatedAt = now;

        if (!completedScene.AutoAdvance)
        {
            await _repository.UpdateAsync(current.Id, current);
            return new AdvanceResult(current, completedScene, false);
        }

        var chapter = await _chapterConfigService.GetByChapterIdAsync(current.ChapterId);
        var nextScene = chapter?.Scenes
            .Where(s => s.SceneId > current.SceneId)
            .OrderBy(s => s.SceneId)
            .FirstOrDefault();

        if (nextScene == null)
        {
            current.IsChapterCompleted = true;
            current.CompletedAt = now;
            await _repository.UpdateAsync(current.Id, current);

            await _actionService.LogActionAsync(playerId, "StoryProgress", current.Id, new Dictionary<string, object>
            {
                ["chapterId"] = current.ChapterId,
                ["sceneId"] = current.SceneId,
                ["isChapterComplete"] = true,
            });

            return new AdvanceResult(current, null, true);
        }

        current.SceneId = nextScene.SceneId;
        current.IsCompleted = false;
        await _repository.UpdateAsync(current.Id, current);
        return new AdvanceResult(current, nextScene, true);
    }

    // Advance to the next scene.
    // Rejected if current scene not yet completed.
    // Returns null if no next scene exists (chapter fully done).
    public async Task<StoryProgress?> StartNextAsync(string playerId)
    {
        var current = await GetCurrentAsync(playerId);

        // Must be completed but chapter not yet marked done
        if (!current.IsCompleted || current.IsChapterCompleted)
            return null;

        // Find next scene in chapter config
        var chapter = await _chapterConfigService.GetByChapterIdAsync(current.ChapterId);
        var nextScene = chapter?.Scenes
            .Where(s => s.SceneId > current.SceneId)
            .OrderBy(s => s.SceneId)
            .FirstOrDefault();

        if (nextScene == null)
        {
            // No more scenes — mark chapter complete
            current.IsChapterCompleted = true;
            current.CompletedAt = DateTime.UtcNow;
            current.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(current.Id, current);

            await _actionService.LogActionAsync(playerId, "StoryProgress", current.Id, new Dictionary<string, object>
            {
                ["chapterId"] = current.ChapterId,
                ["sceneId"] = current.SceneId,
                ["isChapterComplete"] = true,
            });

            return current;
        }

        // Move to next scene — set isCompleted=false so client runs it immediately
        current.SceneId = nextScene.SceneId;
        current.IsCompleted = false;
        current.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(current.Id, current);
        return current;
    }

    public async Task<List<StoryProgress>> GetAllAsync(string playerId)
        => await _repository.GetAllByFilterAsync(p => p.PlayerId == playerId);

    public async Task<bool> IsChapterCompletedAsync(string playerId, int chapterId)
    {
        var progress = await _repository.GetByFilterAsync(
            p => p.PlayerId == playerId && p.ChapterId == chapterId);
        return progress?.IsChapterCompleted ?? false;
    }

    public async Task<int> GetMaxCompletedChapterIdAsync(string playerId)
    {
        var all = await _repository.GetAllByFilterAsync(p => p.PlayerId == playerId);
        var completedChapters = all.Where(p => p.IsChapterCompleted).Select(p => p.ChapterId).ToList();
        return completedChapters.Count > 0 ? completedChapters.Max() : -1;
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

        var all = await _repository.GetAllByFilterAsync(p => p.PlayerId == playerId);
        int maxCompletedChapterId = all
            .Where(p => p.IsChapterCompleted)
            .Select(p => (int?)p.ChapterId)
            .Max() ?? -1;

        if (targetChapterId > maxCompletedChapterId + 1)
            return (null, $"Chapter {targetChapterId} is still locked.");

        var existingChapterProg = all.FirstOrDefault(p => p.ChapterId == targetChapterId);

        if (existingChapterProg == null)
        {
            int firstSceneId = chapterCfg.Scenes.OrderBy(s => s.SceneId).First().SceneId;
            if (targetSceneId != firstSceneId)
                return (null, $"Must start Chapter {targetChapterId} from Scene {firstSceneId}.");

            var newChapterProg = new StoryProgress
            {
                PlayerId = playerId,
                ChapterId = targetChapterId,
                SceneId = targetSceneId,
                IsCompleted = false,
                IsChapterCompleted = false,
                StartedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.CreateAsync(newChapterProg);
            return (newChapterProg, null);
        }

        if (existingChapterProg.IsChapterCompleted)
        {
            existingChapterProg.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(existingChapterProg.Id, existingChapterProg);
            return (existingChapterProg, null);
        }

        int unlockedSceneHighWaterMark = existingChapterProg.IsCompleted
            ? existingChapterProg.SceneId + 1
            : existingChapterProg.SceneId;

        if (targetSceneId > unlockedSceneHighWaterMark)
            return (null, $"Scene {targetChapterId}.{targetSceneId} is not unlocked yet.");

        existingChapterProg.SceneId = targetSceneId;
        existingChapterProg.IsCompleted = false;
        existingChapterProg.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(existingChapterProg.Id, existingChapterProg);

        return (existingChapterProg, null);
    }
}
