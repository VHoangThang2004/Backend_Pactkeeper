using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class NotificationController(INotificationService notificationService) : ControllerBase
{
    private readonly INotificationService _notificationService = notificationService;

    // GET /api/notification
    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> GetNotifications()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var list = await _notificationService.GetPlayerNotificationsAsync(playerId);
        var result = list.Select(n => new NotificationDto(
            n.Id,
            n.Type,
            n.Title,
            n.Message,
            n.Read,
            n.CreatedAt
        )).ToList();

        return Ok(result);
    }

    // POST /api/notification/{id}/read
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var success = await _notificationService.MarkAsReadAsync(playerId, id);
        if (!success) return NotFound();

        return Ok();
    }

    // POST /api/notification/read-all
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        await _notificationService.MarkAllAsReadAsync(playerId);
        return Ok();
    }
}

public record NotificationDto(
    string Id,
    string Type,
    string Title,
    string Message,
    bool Read,
    DateTime CreatedAt
);
