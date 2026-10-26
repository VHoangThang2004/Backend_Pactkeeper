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
    // Optional query params (?chapterId=X&sceneId=Y) guard against double-advancing
    // when the headless server's /progress/battle-complete already completed the scene.
    // -------------------------------------------------------
    [HttpPost("progress/complete")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<CurrentProgressDto>> CompleteScene(
        [FromQuery] int? chapterId = null,
        [FromQuery] int? sceneId = null)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var current = await _storyService.GetCurrentAsync(playerId);

        // Idempotency guard: if the caller specifies (chapterId, sceneId) and the server's
        // /battle-complete already advanced progress past that scene (or completed the chapter),
        // return the current state without completing the NEW scene by mistake.
        if (chapterId.HasValue && sceneId.HasValue)
        {
            bool alreadyPastTarget =
                current.IsChapterCompleted ||
                current.ChapterId != chapterId.Value ||
                current.SceneId != sceneId.Value;

            if (alreadyPastTarget)
            {
                await TerminateStoryMatchesAsync(playerId, $"cleanup completed C{chapterId.Value}_S{sceneId.Value}");
                var curScene = current.IsChapterCompleted
                    ? null
                    : await _chapterConfigService.GetSceneAsync(current.ChapterId, current.SceneId);

                return Ok(new CurrentProgressDto(
                    current.ChapterId,
                    current.SceneId,
                    current.IsCompleted,
                    current.IsChapterCompleted,
                    curScene?.Type.ToString() ?? "None",
                    curScene?.AutoResume ?? false,
                    true
                ));
            }
        }

        if (current.IsCompleted)
        {
            var curScene = await _chapterConfigService.GetSceneAsync(current.ChapterId, current.SceneId);
            if (chapterId.HasValue && sceneId.HasValue)
            {
                await TerminateStoryMatchesAsync(playerId, $"cleanup completed C{current.ChapterId}_S{current.SceneId}");
                return Ok(new CurrentProgressDto(
                    current.ChapterId,
                    current.SceneId,
                    current.IsCompleted,
                    current.IsChapterCompleted,
                    curScene?.Type.ToString() ?? "None",
                    curScene?.AutoResume ?? false,
                    false
                ));
            }
            return BadRequest("Scene already completed. Call /next to continue.");
        }

        var scene = await _chapterConfigService.GetSceneAsync(
            current.ChapterId, current.SceneId);

        if (scene == null)
            return NotFound("Scene config not found.");

        // Tutorial battles (Ch0..Ch3) are completed by the client (BattleTutorialStarter).
        if (scene.Type == SceneType.Battle && current.ChapterId > 3)
            return BadRequest("Battle scenes are completed by the server, not the client.");

        var advanceResult = await _storyService.CompleteAndAdvanceAsync(playerId, scene);

        if (scene.Type == SceneType.Battle)
        {
            await TerminateStoryMatchesAsync(playerId, $"completed tutorial battle C{current.ChapterId}_S{current.SceneId}");
        }

        // Auto-advances server-side if scene.AutoAdvance
        return Ok(ToDto(advanceResult));
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
    // POST /api/story/match/select
    // Called when the player manually selects a Chapter/Scene from 10_StoryProgress Dropdown.
    // Strictly enforces autoResume = false, terminates any active story server, and spawns
    // a fresh headless server for the exact (ChapterId, SceneId) requested.
    // -------------------------------------------------------
    [HttpPost("match/select")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<StoryMatchSelectResponseDto>> StartSelectedStoryMatch(
        [FromBody] SelectStoryMatchRequestDto request)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        bool effectiveAutoResume = false;

        var (progress, error) = await _storyService.PrepareSelectedSceneAsync(
            playerId, request.ChapterId, request.SceneId);

        if (progress == null)
            return BadRequest(error ?? "Cannot select the requested story scene.");

        var scene = await _chapterConfigService.GetSceneAsync(
            request.ChapterId, request.SceneId);

        if (scene == null)
            return NotFound("Scene config not found.");

        int maxCompletedChapterId = await _storyService.GetMaxCompletedChapterIdAsync(playerId);
        bool canSkipOrExit = request.ChapterId <= maxCompletedChapterId || progress.IsChapterCompleted;

        if (scene.Type != SceneType.Battle)
        {
            await TerminateStoryMatchesAsync(playerId, $"switch to non-battle C{request.ChapterId}_S{request.SceneId}");
            return Ok(new StoryMatchSelectResponseDto(
                string.Empty,
                string.Empty,
                0,
                request.ChapterId,
                request.SceneId,
                scene.Type.ToString(),
                effectiveAutoResume,
                canSkipOrExit
            ));
        }

        var existing = await _matchService.GetActiveMatchByPlayerIdAsync(playerId);
        if (existing != null && existing.Mode != "story")
            return Conflict("Player is already in an ongoing non-story match.");

        var killed = await TerminateStoryMatchesAsync(playerId,
            $"manual select C{request.ChapterId}_S{request.SceneId} (autoResume=false)");
        if (killed > 0)
        {
            Console.WriteLine($"[Story] Terminated {killed} stale story match(es) for {playerId} " +
                              $"before starting C{request.ChapterId}_S{request.SceneId} (autoResume=false).");
        }

        var match = await _matchmakingService.CreateStoryMatchAsync(
            _matchService, playerId, request.ChapterId, request.SceneId, scene.MapId);

        if (match == null)
            return StatusCode(503, "No server ports available.");

        return Ok(new StoryMatchSelectResponseDto(
            match.MatchId,
            match.ServerIp,
            match.ServerPort,
            request.ChapterId,
            request.SceneId,
            scene.Type.ToString(),
            effectiveAutoResume,
            canSkipOrExit
        ));
    }

    // -------------------------------------------------------
    // POST /api/story/terminate
    // Immediately terminates the Dedicated Server process when clicking "Exit to Map".
    // -------------------------------------------------------
    [HttpPost("terminate")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult> TerminateStoryMatch()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var killed = await TerminateStoryMatchesAsync(playerId, "Player exited replay to Story Map");
        return Ok(new { terminated = killed });
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
public record SelectStoryMatchRequestDto(int ChapterId, int SceneId, bool AutoResume = false);
public record StoryMatchSelectResponseDto(
    string MatchId,
    string ServerIp,
    int ServerPort,
    int ChapterId,
    int SceneId,
    string SceneType,
    bool AutoResume,
    bool CanSkipOrExit
);
