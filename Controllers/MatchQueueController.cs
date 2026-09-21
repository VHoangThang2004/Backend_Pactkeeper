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

    public MatchQueueController(IMatchQueueService queueService, IMatchSessionService matchService)
    {
        _queueService = queueService;
        _matchService = matchService;
    }

    [HttpPost("join")]
    public async Task<ActionResult> JoinQueue()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var ongoing = await _matchService.GetActiveMatchByPlayerIdAsync(playerId);
        if (ongoing != null) return Conflict(new { message = "already_in_match" });

        await _queueService.JoinQueueAsync(playerId);
        return Ok(new { message = "Joined queue." });
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