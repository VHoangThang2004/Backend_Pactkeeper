using System.Security.Claims;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/game-notification")]
[Authorize]
public class GameNotificationController(IGameNotificationService gameNotificationService) : ControllerBase
{
    private readonly IGameNotificationService _gameNotificationService = gameNotificationService;

    private string? GetPlayerId() =>
        User.FindFirst("PlayerId")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    // GET /api/game-notification
    [HttpGet]
    [Authorize(Roles = "Player,Admin")]
    public async Task<ActionResult<List<GameNotificationDto>>> GetNotifications()
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized(new { message = "No PlayerId claim found in token. Please log in as a Player and authorize in Swagger." });

        var list = await _gameNotificationService.GetPlayerGameNotificationsAsync(playerId);
        var result = list.Select(n =>
        {
            var isRead = n.PlayerId == playerId ? n.Read : n.ReadByPlayerIds.Contains(playerId);
            return new GameNotificationDto(
                n.Id,
                n.Type,
                n.Title,
                n.Message,
                isRead,
                isRead,
                n.CreatedAt
            );
        }).ToList();

        return Ok(result);
    }

    // POST /api/game-notification/{id}/read
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id)
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized(new { message = "No PlayerId claim found in token." });

        var success = await _gameNotificationService.MarkAsReadAsync(playerId, id);
        if (!success) return NotFound();

        return Ok();
    }

    // POST /api/game-notification/read-all
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized(new { message = "No PlayerId claim found in token." });

        await _gameNotificationService.MarkAllAsReadAsync(playerId);
        return Ok();
    }

    // POST /api/game-notification (Admin broadcast or target send)
    [HttpPost]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<GameNotificationDto>> CreateNotification([FromBody] CreateGameNotificationRequestDto dto)
    {
        var notif = await _gameNotificationService.CreateGameNotificationAsync(
            dto.PlayerId,
            dto.Type,
            dto.Title,
            dto.Message,
            dto.ExpiresAt
        );

        return Ok(new GameNotificationDto(
            notif.Id,
            notif.Type,
            notif.Title,
            notif.Message,
            notif.Read,
            notif.Read,
            notif.CreatedAt
        ));
    }
}

public record GameNotificationDto(
    string Id,
    string Type,
    string Title,
    string Message,
    bool Read,
    bool IsRead,
    DateTime CreatedAt
);

public record CreateGameNotificationRequestDto(
    string? PlayerId,
    string Type,
    string Title,
    string Message,
    DateTime? ExpiresAt
);
