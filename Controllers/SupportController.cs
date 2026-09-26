using GameInventoryApi.Services;
using GameInventoryApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SupportController(
    ISupportService supportService,
    IPlayerProfileService profileService,
    INotificationService notificationService,
    IHubContext<SupportHub> hubContext) : ControllerBase
{
    private readonly ISupportService _supportService = supportService;
    private readonly IPlayerProfileService _profileService = profileService;
    private readonly INotificationService _notificationService = notificationService;
    private readonly IHubContext<SupportHub> _hubContext = hubContext;

    // GET /api/support/chat
    [HttpGet("chat")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<List<SupportMessageDto>>> GetChatHistory()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var list = await _supportService.GetChatHistoryAsync(playerId);
        var result = list.Select(m => new SupportMessageDto(
            m.Id,
            m.Sender,
            m.SenderName,
            m.Text,
            m.AttachmentUrl,
            m.CreatedAt
        )).ToList();

        return Ok(result);
    }

    // POST /api/support/chat
    [HttpPost("chat")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<SupportMessageDto>> SendMessage([FromBody] CreateSupportMessageRequestDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var profile = await _profileService.GetByFilterAsync(p => p.PlayerId == playerId);
        var username = profile?.Username ?? "Wanderer";

        var m = await _supportService.SendMessageAsync(playerId, "user", username, dto.Text, dto.AttachmentUrl ?? "");
        var messageDto = new SupportMessageDto(
            m.Id,
            m.Sender,
            m.SenderName,
            m.Text,
            m.AttachmentUrl,
            m.CreatedAt
        );

        await _hubContext.Clients.Group("Admins").SendAsync("ReceiveMessage", playerId, messageDto);
        await _hubContext.Clients.Group(playerId).SendAsync("ReceiveMessage", playerId, messageDto);

        return Ok(messageDto);
    }

    // GET /api/support/admin/players
    [HttpGet("admin/players")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<ActiveChatPlayerDto>>> GetActivePlayers()
    {
        var list = await _supportService.GetActiveChatPlayersAsync();
        return Ok(list);
    }

    // GET /api/support/admin/chat/{playerId}
    [HttpGet("admin/chat/{playerId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<SupportMessageDto>>> GetAdminChatHistory(string playerId)
    {
        var list = await _supportService.GetChatHistoryAsync(playerId);
        var result = list.Select(m => new SupportMessageDto(
            m.Id,
            m.Sender,
            m.SenderName,
            m.Text,
            m.AttachmentUrl,
            m.CreatedAt
        )).ToList();

        return Ok(result);
    }

    // POST /api/support/admin/chat/{playerId}
    [HttpPost("admin/chat/{playerId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SupportMessageDto>> AdminReply(string playerId, [FromBody] AdminReplyRequestDto dto)
    {
        var m = await _supportService.SendMessageAsync(playerId, "admin", "Keeper of Records", dto.Text);

        await _notificationService.CreateNotificationAsync(
            playerId,
            "info",
            "Recent Missive from Counsel",
            "The Keeper of Records has sent an answer to your scroll. View the counsel board."
        );

        var messageDto = new SupportMessageDto(
            m.Id,
            m.Sender,
            m.SenderName,
            m.Text,
            m.AttachmentUrl,
            m.CreatedAt
        );

        await _hubContext.Clients.Group(playerId).SendAsync("ReceiveMessage", playerId, messageDto);

        return Ok(messageDto);
    }
}

public record CreateSupportMessageRequestDto(string Text, string? AttachmentUrl);
public record AdminReplyRequestDto(string Text);
public record SupportMessageDto(
    string Id,
    string Sender,
    string SenderName,
    string Text,
    string AttachmentUrl,
    DateTime CreatedAt
);
