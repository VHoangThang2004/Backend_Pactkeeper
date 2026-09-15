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

    public MatchQueueController(IMatchQueueService queueService)
        => _queueService = queueService;

    [HttpPost("join")]
    public async Task<ActionResult> JoinQueue()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

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

        if (entry.Status == "matched")
            return Ok(new
            {
                status = "matched",
                matchId = entry.MatchId,
                serverIp = entry.ServerIp,
                serverPort = entry.ServerPort
            });

        return Ok(new { status = "waiting" });
    }
}