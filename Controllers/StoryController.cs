using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StoryController : ControllerBase
{
    private readonly IStoryProgressService _storyService;
    private readonly IMatchSessionService _matchService;
    private readonly MatchmakingService _matchmakingService;
    private readonly IChapterConfigService _chapterConfigService;

    public StoryController(
        IStoryProgressService storyService,
        IMatchSessionService matchService,
        MatchmakingService matchmakingService,
        IChapterConfigService chapterConfigService)
    {
        _storyService = storyService;
        _matchService = matchService;
        _matchmakingService = matchmakingService;
        _chapterConfigService = chapterConfigService;
    }

    // -------------------------------------------------------
    // GET /api/story/progress/current
    // Called on every main menu load.
    // New player → creates Chapter 0 Scene 0 isCompleted=false → client runs it immediately.
    // Returning player → returns their current scene state.
    // isChapterCompleted=true → all story done, show full main menu.
    // -------------------------------------------------------
    [HttpGet("progress/current")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<CurrentProgressDto>> GetCurrent()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var progress = await _storyService.GetCurrentAsync(playerId);

        // Attach scene config so client knows type and autoNext
        var scene = await _chapterConfigService.GetSceneAsync(
            progress.ChapterId, progress.SceneId);

        return Ok(new CurrentProgressDto(
            progress.ChapterId,
            progress.SceneId,
            progress.IsCompleted,
            progress.IsChapterCompleted,
            scene?.Type.ToString() ?? "Unknown",
            scene?.AutoNext ?? false
        ));
    }

    // -------------------------------------------------------
    // POST /api/story/progress/complete
    // No body. Client says "I finished this scene."
    // Rejected if current scene is a Battle — only server can complete those.
    // -------------------------------------------------------
    [HttpPost("progress/complete")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<CurrentProgressDto>> CompleteScene()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var current = await _storyService.GetCurrentAsync(playerId);

        if (current.IsCompleted)
            return BadRequest("Scene already completed. Call /next to continue.");

        // Guard: Battle scenes can only be completed by the server
        var scene = await _chapterConfigService.GetSceneAsync(
            current.ChapterId, current.SceneId);

        if (scene == null)
            return NotFound("Scene config not found.");

        if (scene.Type == SceneType.Battle)
            return BadRequest("Battle scenes are completed by the server, not the client.");

        var updated = await _storyService.CompleteCurrentAsync(playerId);

        return Ok(new CurrentProgressDto(
            updated.ChapterId,
            updated.SceneId,
            updated.IsCompleted,
            updated.IsChapterCompleted,
            scene.Type.ToString(),
            scene.AutoNext
        ));
    }

    // -------------------------------------------------------
    // POST /api/story/progress/next
    // No body. Starts the next scene (isCompleted=false on it).
    // Rejected if current scene not yet completed.
    // Client calls this immediately if autoNext=true, or waits for player input.
    // -------------------------------------------------------
    [HttpPost("progress/next")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<CurrentProgressDto>> StartNext()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var current = await _storyService.GetCurrentAsync(playerId);

        if (!current.IsCompleted)
            return BadRequest("Current scene not yet completed.");

        var next = await _storyService.StartNextAsync(playerId);

        if (next == null)
            return BadRequest("Current scene not yet completed.");

        // Chapter fully done
        if (next.IsChapterCompleted)
            return Ok(new CurrentProgressDto(
                next.ChapterId, next.SceneId,
                next.IsCompleted, next.IsChapterCompleted,
                "None", false));

        var nextScene = await _chapterConfigService.GetSceneAsync(
            next.ChapterId, next.SceneId);

        return Ok(new CurrentProgressDto(
            next.ChapterId,
            next.SceneId,
            next.IsCompleted,
            next.IsChapterCompleted,
            nextScene?.Type.ToString() ?? "Unknown",
            nextScene?.AutoNext ?? false
        ));
    }

    // -------------------------------------------------------
    // POST /api/story/progress/battle-complete
    // Called by headless server after player wins a battle.
    // Body: { "playerId": "..." }
    // -------------------------------------------------------
    [HttpPost("progress/battle-complete")]
    [Authorize(Roles = "Server")]
    public async Task<ActionResult<CurrentProgressDto>> BattleComplete(
        [FromBody] BattleCompleteDto dto)
    {
        var current = await _storyService.GetCurrentAsync(dto.PlayerId);

        var scene = await _chapterConfigService.GetSceneAsync(
            current.ChapterId, current.SceneId);

        if (scene == null)
            return NotFound("Scene config not found.");

        if (scene.Type != SceneType.Battle)
            return BadRequest("Current scene is not a battle.");

        if (current.IsCompleted)
            return BadRequest("Battle already marked complete.");

        // Complete the battle scene
        await _storyService.CompleteCurrentAsync(dto.PlayerId);

        // If autoNext, immediately advance to next scene
        CurrentProgressDto response;
        if (scene.AutoNext)
        {
            var next = await _storyService.StartNextAsync(dto.PlayerId);
            var nextScene = next != null && !next.IsChapterCompleted
                ? await _chapterConfigService.GetSceneAsync(next.ChapterId, next.SceneId)
                : null;

            response = new CurrentProgressDto(
                next?.ChapterId ?? current.ChapterId,
                next?.SceneId ?? current.SceneId,
                next?.IsCompleted ?? true,
                next?.IsChapterCompleted ?? false,
                nextScene?.Type.ToString() ?? "None",
                nextScene?.AutoNext ?? false
            );
        }
        else
        {
            response = new CurrentProgressDto(
                current.ChapterId, current.SceneId,
                true, false,
                scene.Type.ToString(), false
            );
        }

        return Ok(response);
    }

    // -------------------------------------------------------
    // POST /api/story/match
    // No body. Spawns a battle server for the current battle scene.
    // Rejected if current scene is not a Battle or already completed.
    // -------------------------------------------------------
    [HttpPost("match")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<StoryMatchResponseDto>> StartStoryMatch()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        // Return existing active story match
        var existing = await _matchService.GetActiveMatchByPlayerIdAsync(playerId);
        if (existing != null)
            return Ok(new StoryMatchResponseDto(
                existing.MatchId, existing.ServerIp, existing.ServerPort));

        var current = await _storyService.GetCurrentAsync(playerId);

        if (current.IsCompleted)
            return BadRequest("Current scene already completed. Call /next first.");

        if (current.IsChapterCompleted)
            return BadRequest("Chapter already completed.");

        var scene = await _chapterConfigService.GetSceneAsync(
            current.ChapterId, current.SceneId);

        if (scene == null)
            return NotFound("Scene config not found.");

        if (scene.Type != SceneType.Battle)
            return BadRequest("Current scene is not a battle.");

        var match = await _matchmakingService.CreateStoryMatchAsync(
            _matchService, playerId, current.ChapterId, current.SceneId);

        if (match == null)
            return StatusCode(503, "No server ports available.");

        return Ok(new StoryMatchResponseDto(match.MatchId, match.ServerIp, match.ServerPort));
    }

    // -------------------------------------------------------
    // GET /api/story/progress/all  — all chapters, for admin/debug
    // -------------------------------------------------------
    [HttpGet("progress/all")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<List<StoryProgressDto>>> GetAll()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var all = await _storyService.GetAllAsync(playerId);
        return Ok(all.Select(p =>
            new StoryProgressDto(p.ChapterId, p.SceneId, p.IsCompleted, p.IsChapterCompleted))
            .ToList());
    }
}

public record CurrentProgressDto(
    int ChapterId,
    int SceneId,
    bool IsCompleted,
    bool IsChapterCompleted,
    string SceneType,       // "Cutscene" | "Dialogue" | "Battle" | "None"
    bool AutoNext           // client calls /next immediately if true
);
public record StoryProgressDto(int ChapterId, int SceneId, bool IsCompleted, bool IsChapterCompleted);
public record BattleCompleteDto(string PlayerId);
public record StoryMatchResponseDto(string MatchId, string ServerIp, int ServerPort);