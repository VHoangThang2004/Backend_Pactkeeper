using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

// Progress: final state after the operation.
// Scene:    config of the scene the player is now on (null if chapter finished).
// Advanced: true if the server moved the player past the completed scene (AutoAdvance).
public record AdvanceResult(StoryProgress Progress, SceneConfig? Scene, bool Advanced);

public interface IStoryProgressService
{
    // Entry point on every login.
    // Returns current active scene. Creates Chapter 0 Scene 0 (isCompleted=false) for new players.
    Task<StoryProgress> GetCurrentAsync(string playerId);

    // Mark current scene as completed.
    // Does NOT advance to next scene — that's /next.
    Task<StoryProgress> CompleteCurrentAsync(string playerId);

    // Complete the current scene and, if completedScene.AutoAdvance, advance to the next
    // scene (or mark the chapter done) in a SINGLE write.
    Task<AdvanceResult> CompleteAndAdvanceAsync(string playerId, SceneConfig completedScene);

    // Advance to the next scene (sets isCompleted=false on it).
    // Fails if current scene is not yet completed.
    // Returns null if chapter is fully done (no next scene).
    Task<StoryProgress?> StartNextAsync(string playerId);

    // All progress records — used for admin/debug.
    Task<List<StoryProgress>> GetAllAsync(string playerId);

    // Check if a chapter is fully completed — used for PvP gate.
    Task<bool> IsChapterCompletedAsync(string playerId, int chapterId);
}