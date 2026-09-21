using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IMatchSessionService _matchSessionService;
    private readonly IMatchQueueService _matchQueueService;
    private readonly ServerState _serverState;

    public AdminController(IMatchSessionService matchSessionService, IMatchQueueService matchQueueService, ServerState serverState)
    {
        _matchSessionService = matchSessionService;
        _matchQueueService = matchQueueService;
        _serverState = serverState;
    }
    
    [HttpPost("force-stop-all")]
    public async Task<IActionResult> ForceReset()
    {
        await _matchSessionService.EndAllMatchesAsync();
        await _matchQueueService.ClearAllQueueAsync();
        return Ok(new { message = "All matches ended and queue cleared." });
    }

    [HttpPost("update-login-status")]
    public IActionResult UpdateLoginStatus([FromBody] bool blocked)
    {
        _serverState.LoginBlocked = blocked;
        return Ok(new { message = $"Login {(blocked ? "blocked" : "unblocked")}." });
    }

    [HttpPost("update-queue-status")]
    public IActionResult UpdateQueueStatus([FromBody] bool blocked)
    {
        _serverState.MatchmakingBlocked = blocked;
        return Ok(new { message = $"Queue {(blocked ? "blocked" : "unblocked")}." });
    }
}