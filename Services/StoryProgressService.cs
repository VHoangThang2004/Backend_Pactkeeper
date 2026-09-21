using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class StoryProgressService : IStoryProgressService
{
    private readonly IMongoRepository<StoryProgress> _repository;
    private readonly IChapterConfigService _chapterConfigService;

    public StoryProgressService(
        IMongoRepository<StoryProgress> repository,
        IChapterConfigService chapterConfigService)
    {
        _repository = repository;
        _chapterConfigService = chapterConfigService;
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
}