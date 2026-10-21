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

        // Attach scene config so client knows type and autoResume
        var scene = await _chapterConfigService.GetSceneAsync(
            progress.ChapterId, progress.SceneId);

        return Ok(new CurrentProgressDto(
            progress.ChapterId,
            progress.SceneId,
            progress.IsCompleted,
            progress.IsChapterCompleted,
            scene?.Type.ToString() ?? "Unknown",
            scene?.AutoResume ?? false,
            false
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

        // Chapter 0 tutorial battles are completed by the client (BattleTutorialStarter).
        // Competitive Battle scenes (Ch1+) must only be completed by the game server.
        if (scene.Type == SceneType.Battle && current.ChapterId != 0)
            return BadRequest("Battle scenes are completed by the server, not the client.");

        // Auto-advances server-side if scene.AutoAdvance
        return Ok(ToDto(await _storyService.CompleteAndAdvanceAsync(playerId, scene)));
    }

    // -------------------------------------------------------
    // POST /api/story/progress/next
    // No body. Starts the next scene (isCompleted=false on it).
    // Rejected if current scene not yet completed.
    // Used for manual advance when the completed scene has AutoAdvance=false.
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
                "None", false, false));

        var nextScene = await _chapterConfigService.GetSceneAsync(
            next.ChapterId, next.SceneId);

        return Ok(new CurrentProgressDto(
            next.ChapterId,
            next.SceneId,
            next.IsCompleted,
            next.IsChapterCompleted,
            nextScene?.Type.ToString() ?? "Unknown",
            nextScene?.AutoResume ?? false,
            false
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

        // Complete the battle scene (auto-advances server-side if scene.AutoAdvance)
        return Ok(ToDto(await _storyService.CompleteAndAdvanceAsync(dto.PlayerId, scene)));
    }
    
    // Pure mapping: AdvanceResult -> DTO. No business logic here.
    private static CurrentProgressDto ToDto(AdvanceResult r) => new(
        r.Progress.ChapterId,
        r.Progress.SceneId,
        r.Progress.IsCompleted,
        r.Progress.IsChapterCompleted,
        r.Scene?.Type.ToString() ?? "None",
        r.Scene?.AutoResume ?? false,
        r.Advanced
    );

    // -------------------------------------------------------
    // POST /api/story/match
    // No body. Spawns a battle server for the current battle scene.
    // Rejected if current scene is not a Battle or already completed.
    // Story/PvE battles are NOT resumable: the plot config (PlotSequencer) only lives
    // in the Unity client and always restarts from node 0 when the scene is loaded.
    // Reconnecting to an old server would restore stale unit positions / instant /
    // used skills that no longer match the plot nodes. So any leftover story match
    // for this player is terminated and a fresh server is spawned for the scene.
    // -------------------------------------------------------
    [HttpPost("match")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<StoryMatchResponseDto>> StartStoryMatch()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

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

        // A live PvP / friendly match must not be killed from the story flow
        var existing = await _matchService.GetActiveMatchByPlayerIdAsync(playerId);
        if (existing != null && existing.Mode != "story")
            return Conflict("Player is already in an ongoing non-story match.");

        // Discard every stale story match → scene restarts from a clean state
        var killed = await TerminateStoryMatchesAsync(playerId,
            $"restart C{current.ChapterId}_S{current.SceneId}");
        if (killed > 0)
            Console.WriteLine($"[Story] Discarded {killed} stale story match(es) for {playerId} " +
                              $"— restarting C{current.ChapterId}_S{current.SceneId} fresh.");

        var match = await _matchmakingService.CreateStoryMatchAsync(
            _matchService, playerId, current.ChapterId, current.SceneId, scene.MapId);

        if (match == null)
            return StatusCode(503, "No server ports available.");

        return Ok(new StoryMatchResponseDto(match.MatchId, match.ServerIp, match.ServerPort));
    }

    // -------------------------------------------------------
    // POST /api/story/match/abandon
    // No body. Client calls this when the player leaves a story battle scene.
    // Kills the story server so nothing (units, instant, skills) survives the exit.
    // Story progress itself is untouched → the same scene restarts next time.
    // -------------------------------------------------------
    [HttpPost("match/abandon")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult> AbandonStoryMatch()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var killed = await TerminateStoryMatchesAsync(playerId, "player left story scene");
        return Ok(new { terminated = killed });
    }

    private async Task<int> TerminateStoryMatchesAsync(string playerId, string reason)
    {
        var all = await _matchService.GetAllByPlayerIdAsync(playerId);
        var stale = all.Where(m => m.Mode == "story" &&
                                   (m.Status == "pending" || m.Status == "active"))
                       .ToList();

        foreach (var m in stale)
            await _matchService.TerminateMatchAsync(m, reason);

        return stale.Count;
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
    bool AutoResume, // Client uses this in the main menu to load the scene directly.
    bool AdvancedFromPrev // (optional) true if this response is the result of an auto-advance
);
public record StoryProgressDto(int ChapterId, int SceneId, bool IsCompleted, bool IsChapterCompleted);
public record BattleCompleteDto(string PlayerId);
public record StoryMatchResponseDto(string MatchId, string ServerIp, int ServerPort);