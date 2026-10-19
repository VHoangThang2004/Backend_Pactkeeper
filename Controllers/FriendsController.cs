using GameInventoryApi.DTOs;
using GameInventoryApi.Hubs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class FriendsController : ControllerBase
{
    private readonly IFriendshipService _friendshipService;
    private readonly IUserPresenceTracker _presenceTracker;
    private readonly IFriendlyInviteService _inviteService;
    private readonly IMatchSessionService _matchService;
    private readonly MatchmakingService _matchmaker;
    private readonly IPlayerProfileService _profileService;
    private readonly IHubContext<LobbyHub> _lobbyHub;

    public FriendsController(
        IFriendshipService friendshipService,
        IUserPresenceTracker presenceTracker,
        IFriendlyInviteService inviteService,
        IMatchSessionService matchService,
        MatchmakingService matchmaker,
        IPlayerProfileService profileService,
        IHubContext<LobbyHub> lobbyHub)
    {
        _friendshipService = friendshipService;
        _presenceTracker = presenceTracker;
        _inviteService = inviteService;
        _matchService = matchService;
        _matchmaker = matchmaker;
        _profileService = profileService;
        _lobbyHub = lobbyHub;
    }

    [HttpGet]
    public async Task<ActionResult<List<FriendProfileDto>>> GetFriends()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var friends = await _friendshipService.GetFriendsWithPresenceAsync(playerId);
        return Ok(friends);
    }

    [HttpGet("requests")]
    public async Task<ActionResult<List<FriendRequestDto>>> GetPendingRequests()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var requests = await _friendshipService.GetPendingRequestsAsync(playerId);
        return Ok(requests);
    }

    [HttpPost("request")]
    public async Task<ActionResult> SendRequest([FromBody] SendFriendRequestDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(dto.TargetUsernameOrId))
            return BadRequest(new { message = "target_required" });

        var (success, message, friendship) = await _friendshipService.SendFriendRequestAsync(
            playerId, dto.TargetUsernameOrId.Trim());

        if (!success)
            return BadRequest(new { message });

        // If target is currently online, notify them via SignalR
        if (friendship != null && _presenceTracker.IsOnline(friendship.FriendId))
        {
            await _lobbyHub.Clients.Group(friendship.FriendId).SendAsync("ReceiveFriendRequest", new
            {
                requestId = friendship.Id,
                senderPlayerId = playerId
            });
        }

        return Ok(new { message });
    }

    [HttpPost("requests/{requestId}/accept")]
    public async Task<ActionResult> AcceptRequest(string requestId)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var success = await _friendshipService.AcceptFriendRequestAsync(playerId, requestId);
        if (!success) return BadRequest(new { message = "accept_failed" });

        return Ok(new { message = "accepted" });
    }

    [HttpPost("requests/{requestId}/decline")]
    public async Task<ActionResult> DeclineRequest(string requestId)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var success = await _friendshipService.DeclineFriendRequestAsync(playerId, requestId);
        if (!success) return BadRequest(new { message = "decline_failed" });

        return Ok(new { message = "declined" });
    }

    [HttpDelete("{friendPlayerId}")]
    public async Task<ActionResult> RemoveFriend(string friendPlayerId)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var success = await _friendshipService.RemoveFriendAsync(playerId, friendPlayerId);
        if (!success) return NotFound(new { message = "friend_not_found" });

        return Ok(new { message = "removed" });
    }

    // -------------------------------------------------------
    // Friendly Match Invite Endpoints
    // -------------------------------------------------------

    [HttpPost("match/invite")]
    public async Task<ActionResult> SendMatchInvite([FromBody] SendMatchInviteDto dto)
    {
        var senderId = User.FindFirst("PlayerId")?.Value;
        if (senderId == null) return Unauthorized();

        if (senderId == dto.TargetPlayerId)
            return BadRequest(new { message = "cannot_invite_self" });

        if (!_presenceTracker.IsOnline(dto.TargetPlayerId))
            return BadRequest(new { message = "target_offline" });

        if (_presenceTracker.IsInMatch(dto.TargetPlayerId) || _presenceTracker.IsInMatch(senderId))
            return Conflict(new { message = "player_already_in_match" });

        var activeMatch = await _matchService.GetActiveMatchByPlayerIdAsync(senderId);
        if (activeMatch != null) return Conflict(new { message = "sender_in_match" });

        var targetMatch = await _matchService.GetActiveMatchByPlayerIdAsync(dto.TargetPlayerId);
        if (targetMatch != null) return Conflict(new { message = "target_in_match" });

        var invite = await _inviteService.CreateInviteAsync(senderId, dto.TargetPlayerId);
        var senderProfile = await _profileService.GetByFilterAsync(p => p.PlayerId == senderId);
        var senderName = senderProfile?.Username ?? "Friend";

        var notification = new MatchInviteNotificationDto(
            invite.Id,
            senderId,
            senderName,
            ExpiresInSeconds: 30
        );

        await _lobbyHub.Clients.Group(dto.TargetPlayerId).SendAsync("ReceiveMatchInvite", notification);
        return Ok(new { inviteId = invite.Id });
    }

    [HttpPost("match/invite/{inviteId}/accept")]
    public async Task<ActionResult<MatchSessionJoinInfoDto>> AcceptMatchInvite(string inviteId)
    {
        var receiverId = User.FindFirst("PlayerId")?.Value;
        if (receiverId == null) return Unauthorized();

        var invite = await _inviteService.GetPendingInviteAsync(inviteId, receiverId);
        if (invite == null)
            return BadRequest(new { message = "invite_not_found_or_expired" });

        var senderMatch = await _matchService.GetActiveMatchByPlayerIdAsync(invite.SenderPlayerId);
        var receiverMatch = await _matchService.GetActiveMatchByPlayerIdAsync(receiverId);
        if (senderMatch != null || receiverMatch != null)
            return Conflict(new { message = "player_already_in_match" });

        var match = await _matchmaker.CreateFriendlyMatchAsync(_matchService, invite.SenderPlayerId, receiverId);
        if (match == null)
            return StatusCode(503, new { message = "no_servers_available" });

        await _inviteService.MarkAcceptedAsync(inviteId, match.MatchId);

        _presenceTracker.SetInMatch(invite.SenderPlayerId, true);
        _presenceTracker.SetInMatch(receiverId, true);

        var p1 = await _profileService.GetByFilterAsync(p => p.PlayerId == match.Player1Id);
        var p2 = await _profileService.GetByFilterAsync(p => p.PlayerId == match.Player2Id);

        var joinInfo = new MatchSessionJoinInfoDto(
            match.MatchId,
            match.ServerIp,
            match.ServerPort,
            match.Mode,
            p1?.Username ?? match.Player1Id,
            p2?.Username ?? match.Player2Id
        );

        await _lobbyHub.Clients.Group(invite.SenderPlayerId).SendAsync("MatchReady", joinInfo);
        await _lobbyHub.Clients.Group(receiverId).SendAsync("MatchReady", joinInfo);

        return Ok(joinInfo);
    }

    [HttpPost("match/invite/{inviteId}/decline")]
    public async Task<ActionResult> DeclineMatchInvite(string inviteId)
    {
        var receiverId = User.FindFirst("PlayerId")?.Value;
        if (receiverId == null) return Unauthorized();

        var invite = await _inviteService.GetInviteByIdAsync(inviteId);
        if (invite != null && invite.TargetPlayerId == receiverId)
        {
            await _inviteService.MarkDeclinedAsync(inviteId);
            await _lobbyHub.Clients.Group(invite.SenderPlayerId).SendAsync("MatchInviteDeclined", inviteId);
        }

        return Ok(new { message = "declined" });
    }

    [HttpPost("match/invite/{inviteId}/cancel")]
    public async Task<ActionResult> CancelMatchInvite(string inviteId)
    {
        var senderId = User.FindFirst("PlayerId")?.Value;
        if (senderId == null) return Unauthorized();

        var invite = await _inviteService.GetInviteByIdAsync(inviteId);
        if (invite != null && invite.SenderPlayerId == senderId)
        {
            await _inviteService.MarkCancelledAsync(inviteId, senderId);
            await _lobbyHub.Clients.Group(invite.TargetPlayerId).SendAsync("MatchInviteCancelled", inviteId);
        }

        return Ok(new { message = "cancelled" });
    }

    [HttpGet("match/invite/{inviteId}/status")]
    public async Task<ActionResult> GetMatchInviteStatus(string inviteId)
    {
        var invite = await _inviteService.GetInviteByIdAsync(inviteId);
        if (invite == null) return NotFound();

        return Ok(new
        {
            inviteId = invite.Id,
            status = invite.Status,
            matchId = invite.MatchId
        });
    }
}

