using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly IMatchSessionService _matchSessionService;
    private readonly IMatchQueueService _matchQueueService;
    private readonly ServerState _serverState;
    private readonly IUserPresenceTracker _presenceTracker;
    private readonly IGameVersionService _gameVersionService;

    public AdminController(
        IMatchSessionService matchSessionService,
        IMatchQueueService matchQueueService,
        ServerState serverState,
        IUserPresenceTracker presenceTracker,
        IGameVersionService gameVersionService)
    {
        _matchSessionService = matchSessionService;
        _matchQueueService = matchQueueService;
        _serverState = serverState;
        _presenceTracker = presenceTracker;
        _gameVersionService = gameVersionService;
    }

    [HttpGet("online-players")]
    [Authorize(Roles = "Admin,Moderator,Technical Support,Support")]
    public IActionResult GetOnlinePlayers()
    {
        var onlinePlayers = _presenceTracker.GetAllOnlineStatuses();
        return Ok(new
        {
            onlinePlayers,
            count = onlinePlayers.Count
        });
    }

    [HttpGet("system-state")]
    [Authorize(Roles = "Admin,Technical Support,Support")]
    public IActionResult GetSystemState()
    {
        return Ok(new
        {
            loginBlocked = _serverState.LoginBlocked,
            matchmakingBlocked = _serverState.MatchmakingBlocked
        });
    }
    
    [HttpPost("force-stop-all")]
    [Authorize(Roles = "Admin,Technical Support,Support")]
    public async Task<IActionResult> ForceReset()
    {
        await _matchSessionService.EndAllMatchesAsync();
        await _matchQueueService.ClearAllQueueAsync();
        return Ok(new { message = "All matches ended and queue cleared." });
    }

    [HttpPost("update-login-status")]
    [Authorize(Roles = "Admin,Technical Support,Support")]
    public IActionResult UpdateLoginStatus([FromBody] bool blocked)
    {
        _serverState.LoginBlocked = blocked;
        return Ok(new { message = $"Login {(blocked ? "blocked" : "unblocked")}." });
    }

    [HttpPost("update-queue-status")]
    [Authorize(Roles = "Admin,Technical Support,Support")]
    public IActionResult UpdateQueueStatus([FromBody] bool blocked)
    {
        _serverState.MatchmakingBlocked = blocked;
        return Ok(new { message = $"Queue {(blocked ? "blocked" : "unblocked")}." });
    }

    /// <summary>
    /// PUT /api/admin/game-version
    /// Updates live game version, minimum supported version, download link, and patch notes.
    /// </summary>
    [HttpPut("game-version")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GameVersionDto>> UpdateGameVersion([FromBody] UpdateGameVersionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.LatestVersion) || string.IsNullOrWhiteSpace(dto.MinSupportedVersion))
        {
            return BadRequest(new { message = "latest_version_and_min_supported_version_required" });
        }

        var adminUsername = User.Identity?.Name
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? User.FindFirst("unique_name")?.Value
            ?? "Admin";

        var updated = await _gameVersionService.UpdateVersionConfigAsync(dto, adminUsername);
        return Ok(updated);
    }
}
