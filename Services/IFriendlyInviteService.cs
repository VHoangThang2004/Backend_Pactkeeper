using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IFriendlyInviteService
{
    Task<FriendlyMatchInvite> CreateInviteAsync(string senderPlayerId, string targetPlayerId);
    Task<FriendlyMatchInvite?> GetPendingInviteAsync(string inviteId, string targetPlayerId);
    Task<FriendlyMatchInvite?> GetInviteByIdAsync(string inviteId);
    Task MarkAcceptedAsync(string inviteId, string matchId);
    Task MarkDeclinedAsync(string inviteId);
    Task<bool> MarkCancelledAsync(string inviteId, string senderPlayerId);
    Task<FriendlyMatchInvite?> GetActiveInviteBetweenPlayersAsync(string player1Id, string player2Id);
}
