using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class FriendlyInviteService : IFriendlyInviteService
{
    private readonly IMongoRepository<FriendlyMatchInvite> _inviteRepo;

    public FriendlyInviteService(IMongoRepository<FriendlyMatchInvite> inviteRepo)
    {
        _inviteRepo = inviteRepo;
    }

    public async Task<FriendlyMatchInvite> CreateInviteAsync(string senderPlayerId, string targetPlayerId)
    {
        // Cancel any stale pending invites between these players
        var oldInvites = await _inviteRepo.GetAllByFilterAsync(i =>
            ((i.SenderPlayerId == senderPlayerId && i.TargetPlayerId == targetPlayerId) ||
             (i.SenderPlayerId == targetPlayerId && i.TargetPlayerId == senderPlayerId)) &&
            i.Status == "pending");

        foreach (var old in oldInvites)
        {
            old.Status = "cancelled";
            await _inviteRepo.UpdateAsync(old.Id, old);
        }

        var invite = new FriendlyMatchInvite
        {
            SenderPlayerId = senderPlayerId,
            TargetPlayerId = targetPlayerId,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(30)
        };

        await _inviteRepo.CreateAsync(invite);
        return invite;
    }

    public async Task<FriendlyMatchInvite?> GetPendingInviteAsync(string inviteId, string targetPlayerId)
    {
        var invite = await _inviteRepo.GetByIdAsync(inviteId);
        if (invite == null) return null;
        if (invite.TargetPlayerId != targetPlayerId) return null;
        if (invite.Status != "pending") return null;
        if (invite.ExpiresAt < DateTime.UtcNow)
        {
            invite.Status = "expired";
            await _inviteRepo.UpdateAsync(invite.Id, invite);
            return null;
        }
        return invite;
    }

    public async Task<FriendlyMatchInvite?> GetInviteByIdAsync(string inviteId)
    {
        return await _inviteRepo.GetByIdAsync(inviteId);
    }

    public async Task MarkAcceptedAsync(string inviteId, string matchId)
    {
        var invite = await _inviteRepo.GetByIdAsync(inviteId);
        if (invite == null) return;
        invite.Status = "accepted";
        invite.MatchId = matchId;
        await _inviteRepo.UpdateAsync(invite.Id, invite);
    }

    public async Task MarkDeclinedAsync(string inviteId)
    {
        var invite = await _inviteRepo.GetByIdAsync(inviteId);
        if (invite == null) return;
        invite.Status = "declined";
        await _inviteRepo.UpdateAsync(invite.Id, invite);
    }

    public async Task<bool> MarkCancelledAsync(string inviteId, string senderPlayerId)
    {
        var invite = await _inviteRepo.GetByIdAsync(inviteId);
        if (invite == null || invite.SenderPlayerId != senderPlayerId) return false;
        invite.Status = "cancelled";
        await _inviteRepo.UpdateAsync(invite.Id, invite);
        return true;
    }

    public async Task<FriendlyMatchInvite?> GetActiveInviteBetweenPlayersAsync(string player1Id, string player2Id)
    {
        return await _inviteRepo.GetByFilterAsync(i =>
            ((i.SenderPlayerId == player1Id && i.TargetPlayerId == player2Id) ||
             (i.SenderPlayerId == player2Id && i.TargetPlayerId == player1Id)) &&
            i.Status == "pending" &&
            i.ExpiresAt > DateTime.UtcNow);
    }
}
