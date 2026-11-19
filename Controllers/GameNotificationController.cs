using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/game-notification")]
[Authorize(Roles = "Player")]
public class GameNotificationController(IGameNotificationService gameNotificationService) : ControllerBase
{
    private readonly IGameNotificationService _gameNotificationService = gameNotificationService;

    // GET /api/game-notification
    [HttpGet]
    public async Task<ActionResult<List<GameNotificationDto>>> GetNotifications()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

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
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var success = await _gameNotificationService.MarkAsReadAsync(playerId, id);
        if (!success) return NotFound();

        return Ok();
    }

    // POST /api/game-notification/read-all
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        await _gameNotificationService.MarkAllAsReadAsync(playerId);
        return Ok();
    }

    // POST /api/game-notification (Admin broadcast or target send)
    [HttpPost]
    [Authorize(Roles = "Admin")]
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
