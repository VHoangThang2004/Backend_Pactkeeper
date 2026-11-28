using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public class CreateGameNotificationRequestDto
{
    public string? PlayerId { get; set; }
    public string Type { get; set; } = "System";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    [JsonConverter(typeof(FlexibleNullableDateTimeConverter))]
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Parses nullable UTC timestamps flexibly (treats blank strings as null, accepts space separators
/// and offsets without colons) and emits an actionable error message on invalid calendar dates.
/// </summary>
public sealed class FlexibleNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("expiresAt must be an ISO-8601 date/time string or null.");

        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(
                raw.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.UtcDateTime;
        }

        throw new JsonException(
            $"Invalid expiresAt value '{raw}'. Use a valid ISO-8601 date/time (e.g. '2026-12-01T12:00:00Z') or null.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
