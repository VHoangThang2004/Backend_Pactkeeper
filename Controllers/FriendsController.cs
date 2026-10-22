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

    private string? GetPlayerId() =>
        User.FindFirst("PlayerId")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    [HttpGet]
    public async Task<ActionResult<List<FriendProfileDto>>> GetFriends()
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        var friends = await _friendshipService.GetFriendsWithPresenceAsync(playerId);
        Console.WriteLine($"[FriendsController] GetFriends for {playerId}: found {friends.Count} friends. Online count: {friends.Count(f => f.IsOnline)}");
        return Ok(friends);
    }

    [HttpGet("requests")]
    public async Task<ActionResult<List<FriendRequestDto>>> GetPendingRequests()
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        var requests = await _friendshipService.GetPendingRequestsAsync(playerId);
        return Ok(requests);
    }

    [HttpPost("request")]
    public async Task<ActionResult> SendRequest([FromBody] SendFriendRequestDto dto)
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        if (dto == null || string.IsNullOrWhiteSpace(dto.TargetUsernameOrId))
            return BadRequest(new { message = "target_required" });

        Console.WriteLine($"[FriendsController] Player {playerId} sending request to '{dto.TargetUsernameOrId}'");
        var (success, message, friendship) = await _friendshipService.SendFriendRequestAsync(
            playerId, dto.TargetUsernameOrId.Trim());

        Console.WriteLine($"[FriendsController] Result: success={success}, message={message}");

        if (!success)
            return BadRequest(new { message });

        // If mutual request was auto-accepted, notify both players immediately
        if (friendship != null && message == "request_accepted")
        {
            await _lobbyHub.Clients.Group(friendship.PlayerId).SendAsync("FriendRequestAccepted", new { friendPlayerId = friendship.FriendId });
            await _lobbyHub.Clients.Group(friendship.FriendId).SendAsync("FriendRequestAccepted", new { friendPlayerId = friendship.PlayerId });

            if (_presenceTracker.IsOnline(friendship.PlayerId))
            {
                await _lobbyHub.Clients.Group(friendship.FriendId).SendAsync("OnFriendPresenceChanged", new FriendPresenceUpdateDto(
                    friendship.PlayerId,
                    IsOnline: true,
                    Status: _presenceTracker.GetStatus(friendship.PlayerId)
                ));
            }
            if (_presenceTracker.IsOnline(friendship.FriendId))
            {
                await _lobbyHub.Clients.Group(friendship.PlayerId).SendAsync("OnFriendPresenceChanged", new FriendPresenceUpdateDto(
                    friendship.FriendId,
                    IsOnline: true,
                    Status: _presenceTracker.GetStatus(friendship.FriendId)
                ));
            }
        }
        // If target is currently online, notify them of incoming request via SignalR
        else if (friendship != null && _presenceTracker.IsOnline(friendship.FriendId))
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
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        var (success, friendship) = await _friendshipService.AcceptFriendRequestAsync(playerId, requestId);
        if (!success || friendship == null) return BadRequest(new { message = "accept_failed" });

        var senderPlayerId = friendship.PlayerId;
        Console.WriteLine($"[FriendsController] AcceptRequest: Player {playerId} accepted request from {senderPlayerId}");

        // 1. Notify both parties that friendship has been established so their UI re-fetches
        await _lobbyHub.Clients.Group(senderPlayerId).SendAsync("FriendRequestAccepted", new
        {
            friendPlayerId = playerId
        });
        await _lobbyHub.Clients.Group(playerId).SendAsync("FriendRequestAccepted", new
        {
            friendPlayerId = senderPlayerId
        });

        // 2. Broadcast online presence immediately between both newly-connected friends
        bool playerOnline = _presenceTracker.IsOnline(playerId);
        bool senderOnline = _presenceTracker.IsOnline(senderPlayerId);
        Console.WriteLine($"[FriendsController] Presence states: Player({playerId})={playerOnline}, Sender({senderPlayerId})={senderOnline}");

        if (playerOnline)
        {
            await _lobbyHub.Clients.Group(senderPlayerId).SendAsync("OnFriendPresenceChanged", new FriendPresenceUpdateDto(
                playerId,
                IsOnline: true,
                Status: _presenceTracker.GetStatus(playerId)
            ));
        }
        if (senderOnline)
        {
            await _lobbyHub.Clients.Group(playerId).SendAsync("OnFriendPresenceChanged", new FriendPresenceUpdateDto(
                senderPlayerId,
                IsOnline: true,
                Status: _presenceTracker.GetStatus(senderPlayerId)
            ));
        }

        return Ok(new { message = "accepted" });
    }

    [HttpPost("requests/{requestId}/decline")]
    public async Task<ActionResult> DeclineRequest(string requestId)
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        var success = await _friendshipService.DeclineFriendRequestAsync(playerId, requestId);
        if (!success) return BadRequest(new { message = "decline_failed" });

        return Ok(new { message = "declined" });
    }

    [HttpDelete("{friendPlayerId}")]
    public async Task<ActionResult> RemoveFriend(string friendPlayerId)
    {
        var playerId = GetPlayerId();
        if (playerId == null) return Unauthorized();

        var success = await _friendshipService.RemoveFriendAsync(playerId, friendPlayerId);
        if (!success) return NotFound(new { message = "friend_not_found" });

        await _lobbyHub.Clients.Group(friendPlayerId).SendAsync("FriendRemoved", playerId);

        return Ok(new { message = "removed" });
    }

    // -------------------------------------------------------
    // Friendly Match Invite Endpoints
    // -------------------------------------------------------

    [HttpPost("match/invite")]
    public async Task<ActionResult> SendMatchInvite([FromBody] SendMatchInviteDto dto)
    {
        var senderId = GetPlayerId();
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
        var receiverId = GetPlayerId();
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
        var receiverId = GetPlayerId();
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
        var senderId = GetPlayerId();
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

