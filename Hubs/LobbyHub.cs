using System.Security.Claims;
using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameInventoryApi.Hubs;

[Authorize]
public class LobbyHub : Hub
{
    public const string AdminsGroup = "Admins";

    private readonly IUserPresenceTracker _presenceTracker;
    private readonly IFriendshipService _friendshipService;
    private readonly IFriendlyInviteService _inviteService;
    private readonly IMatchSessionService _matchService;
    private readonly MatchmakingService _matchmaker;
    private readonly IPlayerProfileService _profileService;

    public LobbyHub(
        IUserPresenceTracker presenceTracker,
        IFriendshipService friendshipService,
        IFriendlyInviteService inviteService,
        IMatchSessionService matchService,
        MatchmakingService matchmaker,
        IPlayerProfileService profileService)
    {
        _presenceTracker = presenceTracker;
        _friendshipService = friendshipService;
        _inviteService = inviteService;
        _matchService = matchService;
        _matchmaker = matchmaker;
        _profileService = profileService;
    }

    private static bool IsStaff(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrWhiteSpace(role)) return false;
        return !string.Equals(AdminSecurityHelper.NormalizeRoleName(role), "Player", StringComparison.OrdinalIgnoreCase);
    }

    public override async Task OnConnectedAsync()
    {
        if (IsStaff(Context.User))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
            var snapshot = _presenceTracker.GetAllOnlineStatuses();
            Console.WriteLine($"[LobbyHub] Staff connected: ConnectionId={Context.ConnectionId}, Role={Context.User?.FindFirst(ClaimTypes.Role)?.Value}. Sending {snapshot.Count} online players snapshot.");
            await Clients.Caller.SendAsync("OnOnlinePlayersSnapshot", snapshot);
            await base.OnConnectedAsync();
            return;
        }

        var playerId = Context.User?.FindFirst("PlayerId")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        Console.WriteLine($"[LobbyHub] OnConnectedAsync: ConnectionId={Context.ConnectionId}, PlayerId={playerId}, Authenticated={Context.User?.Identity?.IsAuthenticated}");

        if (!string.IsNullOrEmpty(playerId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, playerId);

            if (_presenceTracker.AddConnection(playerId, Context.ConnectionId))
            {
                var status = _presenceTracker.GetStatus(playerId);
                var payload = new FriendPresenceUpdateDto(
                    playerId,
                    IsOnline: true,
                    Status: status
                );

                var friendIds = await _friendshipService.GetFriendPlayerIdsAsync(playerId);
                Console.WriteLine($"[LobbyHub] Player {playerId} registered online ({status}). Notifying {friendIds.Count} friends and Admins.");
                if (friendIds.Count > 0)
                {
                    await Clients.Groups(friendIds).SendAsync("OnFriendPresenceChanged", payload);
                }

                await Clients.Group(AdminsGroup).SendAsync("OnPlayerPresenceChanged", payload);
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (IsStaff(Context.User))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, AdminsGroup);
            await base.OnDisconnectedAsync(exception);
            return;
        }

        var playerId = Context.User?.FindFirst("PlayerId")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        Console.WriteLine($"[LobbyHub] OnDisconnectedAsync: ConnectionId={Context.ConnectionId}, PlayerId={playerId}");

        if (!string.IsNullOrEmpty(playerId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, playerId);

            if (_presenceTracker.RemoveConnection(playerId, Context.ConnectionId))
            {
                var offlinePayload = new FriendPresenceUpdateDto(
                    playerId,
                    IsOnline: false,
                    Status: "offline"
                );

                var friendIds = await _friendshipService.GetFriendPlayerIdsAsync(playerId);
                Console.WriteLine($"[LobbyHub] Player {playerId} went offline. Notifying {friendIds.Count} friends and Admins.");
                if (friendIds.Count > 0)
                {
                    await Clients.Groups(friendIds).SendAsync("OnFriendPresenceChanged", offlinePayload);
                }

                await Clients.Group(AdminsGroup).SendAsync("OnPlayerPresenceChanged", offlinePayload);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task<IReadOnlyDictionary<string, string>> GetOnlinePlayers()
    {
        if (!IsStaff(Context.User))
        {
            throw new HubException("Forbidden");
        }
        return Task.FromResult(_presenceTracker.GetAllOnlineStatuses());
    }

    public async Task<string> SendMatchInvite(string targetPlayerId)
    {
        var senderId = Context.User?.FindFirst("PlayerId")?.Value;
        if (string.IsNullOrEmpty(senderId)) throw new HubException("Unauthorized");

        if (senderId == targetPlayerId) throw new HubException("cannot_invite_self");

        if (!_presenceTracker.IsOnline(targetPlayerId))
            throw new HubException("target_offline");

        if (_presenceTracker.IsInMatch(targetPlayerId) || _presenceTracker.IsInMatch(senderId))
            throw new HubException("player_already_in_match");

        var activeMatch = await _matchService.GetActiveMatchByPlayerIdAsync(senderId);
        if (activeMatch != null) throw new HubException("sender_in_match");

        var targetMatch = await _matchService.GetActiveMatchByPlayerIdAsync(targetPlayerId);
        if (targetMatch != null) throw new HubException("target_in_match");

        var invite = await _inviteService.CreateInviteAsync(senderId, targetPlayerId);
        var senderProfile = await _profileService.GetByFilterAsync(p => p.PlayerId == senderId);
        var senderName = senderProfile?.Username ?? "Friend";

        var notification = new MatchInviteNotificationDto(
            invite.Id,
            senderId,
            senderName,
            ExpiresInSeconds: 30
        );

        await Clients.Group(targetPlayerId).SendAsync("ReceiveMatchInvite", notification);
        return invite.Id;
    }

    public async Task AcceptMatchInvite(string inviteId)
    {
        var receiverId = Context.User?.FindFirst("PlayerId")?.Value;
        if (string.IsNullOrEmpty(receiverId)) throw new HubException("Unauthorized");

        var invite = await _inviteService.GetPendingInviteAsync(inviteId, receiverId);
        if (invite == null) throw new HubException("invite_not_found_or_expired");

        // Verify ongoing match status
        var senderMatch = await _matchService.GetActiveMatchByPlayerIdAsync(invite.SenderPlayerId);
        var receiverMatch = await _matchService.GetActiveMatchByPlayerIdAsync(receiverId);
        if (senderMatch != null || receiverMatch != null)
            throw new HubException("player_already_in_match");

        var match = await _matchmaker.CreateFriendlyMatchAsync(_matchService, invite.SenderPlayerId, receiverId);
        if (match == null)
            throw new HubException("no_servers_available");

        await _inviteService.MarkAcceptedAsync(inviteId, match.MatchId);

        _presenceTracker.SetInMatch(invite.SenderPlayerId, true);
        _presenceTracker.SetInMatch(receiverId, true);

        await Clients.Group(AdminsGroup).SendAsync("OnPlayerPresenceChanged", new FriendPresenceUpdateDto(
            invite.SenderPlayerId,
            IsOnline: true,
            Status: "in_match"
        ));
        await Clients.Group(AdminsGroup).SendAsync("OnPlayerPresenceChanged", new FriendPresenceUpdateDto(
            receiverId,
            IsOnline: true,
            Status: "in_match"
        ));

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

        // Notify both players simultaneously
        await Clients.Group(invite.SenderPlayerId).SendAsync("MatchReady", joinInfo);
        await Clients.Group(receiverId).SendAsync("MatchReady", joinInfo);
    }

    public async Task DeclineMatchInvite(string inviteId)
    {
        var receiverId = Context.User?.FindFirst("PlayerId")?.Value;
        if (string.IsNullOrEmpty(receiverId)) throw new HubException("Unauthorized");

        var invite = await _inviteService.GetInviteByIdAsync(inviteId);
        if (invite != null && invite.TargetPlayerId == receiverId)
        {
            await _inviteService.MarkDeclinedAsync(inviteId);
            await Clients.Group(invite.SenderPlayerId).SendAsync("MatchInviteDeclined", inviteId);
        }
    }

    public async Task CancelMatchInvite(string inviteId)
    {
        var senderId = Context.User?.FindFirst("PlayerId")?.Value;
        if (string.IsNullOrEmpty(senderId)) throw new HubException("Unauthorized");

        var invite = await _inviteService.GetInviteByIdAsync(inviteId);
        if (invite != null && invite.SenderPlayerId == senderId)
        {
            await _inviteService.MarkCancelledAsync(inviteId, senderId);
            await Clients.Group(invite.TargetPlayerId).SendAsync("MatchInviteCancelled", inviteId);
        }
    }
}
