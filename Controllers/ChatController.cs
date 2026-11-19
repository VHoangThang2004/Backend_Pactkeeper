using GameInventoryApi.DTOs;
using GameInventoryApi.Hubs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class ChatController : ControllerBase
{
    private readonly IChatMessageService _chatService;
    private readonly IHubContext<LobbyHub> _lobbyHub;

    public ChatController(
        IChatMessageService chatService,
        IHubContext<LobbyHub> lobbyHub)
    {
        _chatService = chatService;
        _lobbyHub = lobbyHub;
    }

    private string? GetPlayerId() =>
        User.FindFirst("PlayerId")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    // GET /api/chat/conversations
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ChatConversationDto>>> GetConversations()
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        var convos = await _chatService.GetConversationsAsync(playerId);
        return Ok(convos);
    }

    // GET /api/chat/messages/{friendPlayerId}
    [HttpGet("messages/{friendPlayerId}")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(string friendPlayerId)
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        try
        {
            var messages = await _chatService.GetMessagesBetweenAsync(playerId, friendPlayerId);
            return Ok(messages);
        }
        catch (InvalidOperationException ex) when (ex.Message == "not_friends")
        {
            return BadRequest(new { message = "not_friends" });
        }
    }

    // POST /api/chat/messages
    [HttpPost("messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage([FromBody] SendMessageRequestDto dto)
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        if (dto == null || string.IsNullOrWhiteSpace(dto.ReceiverId) || string.IsNullOrWhiteSpace(dto.Text))
        {
            return BadRequest(new { message = "invalid_payload" });
        }

        try
        {
            var message = await _chatService.SendMessageAsync(playerId, dto.ReceiverId, dto.Text);

            // Broadcast real-time direct message to recipient's group
            await _lobbyHub.Clients.Group(dto.ReceiverId).SendAsync("ReceiveDirectMessage", message);

            return Ok(message);
        }
        catch (InvalidOperationException ex) when (ex.Message == "not_friends")
        {
            return BadRequest(new { message = "not_friends" });
        }
    }

    // POST /api/chat/messages/{friendPlayerId}/read
    [HttpPost("messages/{friendPlayerId}/read")]
    public async Task<IActionResult> MarkAsRead(string friendPlayerId)
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        await _chatService.MarkConversationAsReadAsync(playerId, friendPlayerId);
        return Ok();
    }

    // GET /api/chat/unread-count
    [HttpGet("unread-count")]
    public async Task<ActionResult<ChatUnreadCountDto>> GetTotalUnreadCount()
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        var count = await _chatService.GetTotalUnreadCountAsync(playerId);
        return Ok(new ChatUnreadCountDto(count));
    }
}
