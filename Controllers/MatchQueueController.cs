using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/match/queue")]
[Authorize(Roles = "Player")]
public class MatchQueueController : ControllerBase
{
    private readonly IMatchQueueService _queueService;
    private readonly IMatchSessionService _matchService;
    private readonly IPlayerProfileService _profileService;

    public MatchQueueController(
        IMatchQueueService queueService,
        IMatchSessionService matchService,
        IPlayerProfileService profileService)
    {
        _queueService = queueService;
        _matchService = matchService;
        _profileService = profileService;
    }

    [HttpPost("join")]
    public async Task<ActionResult> JoinQueue([FromBody] JoinQueueRequestDto? dto = null)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var ongoing = await _matchService.GetActiveMatchByPlayerIdAsync(playerId);
        if (ongoing != null) return Conflict(new { message = "already_in_match" });

        string mode = dto?.Mode?.ToLowerInvariant() ?? "pvp";
        int rating = 0;

        if (mode == "ranked")
        {
            var profile = await _profileService.GetByFilterAsync(p => p.PlayerId == playerId);
            rating = profile?.Rating ?? 0;
        }

        await _queueService.JoinQueueAsync(playerId, mode, rating);
        return Ok(new { message = "Joined queue.", mode });
    }

    [HttpDelete("leave")]
    public async Task<ActionResult> LeaveQueue()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        await _queueService.LeaveQueueAsync(playerId);
        return Ok(new { message = "Left queue." });
    }

    [HttpGet("status")]
    public async Task<ActionResult> GetQueueStatus()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var entry = await _queueService.GetByPlayerIdAsync(playerId);
        if (entry == null) return NotFound(new { status = "not_in_queue" });

        return Ok(new { status = entry.Status });
    }
}