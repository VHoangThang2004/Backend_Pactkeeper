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
    // Body (optional): { "chapterId": 2, "sceneId": 1 }. Client says "I finished this scene."
    // Only advances Current Progress if the completed (chapterId, sceneId) matches current!
    // -------------------------------------------------------
    [HttpPost("progress/complete")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<CurrentProgressDto>> CompleteScene(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CompleteSceneRequestDto? dto = null)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var current = await _storyService.GetCurrentAsync(playerId);
        var currentScene = await _chapterConfigService.GetSceneAsync(
            current.ChapterId, current.SceneId);

        int? targetChapterId = dto?.ChapterId;
        int? targetSceneId = dto?.SceneId;

        if (!targetChapterId.HasValue || !targetSceneId.HasValue)
        {
            var (matchChapterId, matchSceneId) = await TryResolveActiveStoryMatchSceneAsync(playerId);
            targetChapterId ??= matchChapterId;
            targetSceneId ??= matchSceneId;
        }

        // Guard: Never advance Current Progress if the scene that just finished is NOT the player's Current Progress
        // (e.g. replaying C2_S1 while Current Progress is C2_S2, or idempotent call after battle-complete already advanced C2_S1 -> C2_S2).
        if (targetChapterId.HasValue && targetSceneId.HasValue &&
            (targetChapterId.Value != current.ChapterId || targetSceneId.Value != current.SceneId))
        {
            Console.WriteLine($"[Story] Ignored progress/complete for C{targetChapterId}_S{targetSceneId} " +
                              $"because current progress is C{current.ChapterId}_S{current.SceneId} (completed={current.IsCompleted}).");
            await TerminateStoryMatchesAsync(playerId, $"finished non-current/already-advanced scene C{targetChapterId}_S{targetSceneId}");
            return Ok(ToDto(new AdvanceResult(current, currentScene, false)));
        }

        if (current.IsCompleted || current.IsChapterCompleted)
        {
            if (targetChapterId.HasValue && targetSceneId.HasValue &&
                targetChapterId.Value == current.ChapterId && targetSceneId.Value == current.SceneId)
            {
                await TerminateStoryMatchesAsync(playerId, $"idempotent complete C{current.ChapterId}_S{current.SceneId}");
                return Ok(ToDto(new AdvanceResult(current, currentScene, false)));
            }
            return BadRequest("Scene already completed. Call /next to continue.");
        }

        if (currentScene == null)
            return NotFound("Scene config not found.");

        // Tutorial battles (Ch0..Ch3) are completed by the client (BattleTutorialStarter).
        if (currentScene.Type == SceneType.Battle && current.ChapterId > 3)
            return BadRequest("Battle scenes are completed by the server, not the client.");

        var advanceResult = await _storyService.CompleteAndAdvanceAsync(playerId, currentScene);

        if (currentScene.Type == SceneType.Battle)
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
    // Body: { "playerId": "...", "chapterId": 2, "sceneId": 1 }
    // -------------------------------------------------------
    [HttpPost("progress/battle-complete")]
    [Authorize(Roles = "Server")]
    public async Task<ActionResult<CurrentProgressDto>> BattleComplete(
        [FromBody] BattleCompleteDto dto)
    {
        var current = await _storyService.GetCurrentAsync(dto.PlayerId);
        var currentScene = await _chapterConfigService.GetSceneAsync(
            current.ChapterId, current.SceneId);

        int? battleChapterId = dto.ChapterId;
        int? battleSceneId = dto.SceneId;

        // Fallback for older server binaries that only send { "playerId" }:
        // inspect the active/latest story MatchSession for this player.
        if (!battleChapterId.HasValue || !battleSceneId.HasValue ||
            (battleChapterId.Value == 0 && battleSceneId.Value == 0))
        {
            var (matchChapterId, matchSceneId) = await TryResolveActiveStoryMatchSceneAsync(dto.PlayerId);
            if (matchChapterId.HasValue && matchSceneId.HasValue)
            {
                battleChapterId = matchChapterId;
                battleSceneId = matchSceneId;
            }
        }

        // Guard: If the battle server that just finished was running a Replay scene (e.g. C2_S1)
        // while Current Progress is at a later scene (e.g. C2_S2), NEVER advance Current Progress!
        if (battleChapterId.HasValue && battleSceneId.HasValue &&
            (battleChapterId.Value != current.ChapterId || battleSceneId.Value != current.SceneId))
        {
            Console.WriteLine($"[Story] Ignored battle-complete for replay/non-current scene C{battleChapterId}_S{battleSceneId} " +
                              $"(player={dto.PlayerId}, current progress is C{current.ChapterId}_S{current.SceneId}).");
            return Ok(ToDto(new AdvanceResult(current, currentScene, false)));
        }

        if (current.IsCompleted || current.IsChapterCompleted)
        {
            Console.WriteLine($"[Story] Ignored battle-complete for C{current.ChapterId}_S{current.SceneId} — already marked complete.");
            return Ok(ToDto(new AdvanceResult(current, currentScene, false)));
        }

        if (currentScene == null)
            return NotFound("Scene config not found.");

        if (currentScene.Type != SceneType.Battle)
            return BadRequest("Current scene is not a battle.");

        // Complete the battle scene (auto-advances server-side if scene.AutoAdvance)
        return Ok(ToDto(await _storyService.CompleteAndAdvanceAsync(dto.PlayerId, currentScene)));
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

    private async Task<(int? ChapterId, int? SceneId)> TryResolveActiveStoryMatchSceneAsync(string playerId)
    {
        var all = await _matchService.GetAllByPlayerIdAsync(playerId);
        var storyMatch = all
            .Where(m => m.Mode == "story" && (m.Status == "pending" || m.Status == "active" || m.Status == "finishing"))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault()
            ?? all.Where(m => m.Mode == "story")
                  .OrderByDescending(m => m.CreatedAt)
                  .FirstOrDefault();

        if (storyMatch == null)
            return (null, null);

        if (storyMatch.ChapterId.HasValue && storyMatch.SceneId.HasValue)
            return (storyMatch.ChapterId.Value, storyMatch.SceneId.Value);

        if (!string.IsNullOrEmpty(storyMatch.MapId))
        {
            // Parse MapId format like "MD_PVE_C2S1" or "C2_S1"
            var match = System.Text.RegularExpressions.Regex.Match(
                storyMatch.MapId, @"C(\d+)_?S(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out int parsedCh) &&
                int.TryParse(match.Groups[2].Value, out int parsedSc))
            {
                return (parsedCh, parsedSc);
            }
        }

        return (null, null);
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
public record CompleteSceneRequestDto(int? ChapterId = null, int? SceneId = null);
public record BattleCompleteDto(string PlayerId, int? ChapterId = null, int? SceneId = null);
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
