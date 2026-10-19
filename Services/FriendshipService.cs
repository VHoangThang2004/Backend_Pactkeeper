using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class FriendshipService : IFriendshipService
{
    private readonly IMongoRepository<Friendship> _friendshipRepo;
    private readonly IMongoRepository<PlayerProfile> _profileRepo;
    private readonly IUserPresenceTracker _presenceTracker;

    public FriendshipService(
        IMongoRepository<Friendship> friendshipRepo,
        IMongoRepository<PlayerProfile> profileRepo,
        IUserPresenceTracker presenceTracker)
    {
        _friendshipRepo = friendshipRepo;
        _profileRepo = profileRepo;
        _presenceTracker = presenceTracker;
    }

    public async Task<List<FriendProfileDto>> GetFriendsWithPresenceAsync(string playerId)
    {
        var friendships = await _friendshipRepo.GetAllByFilterAsync(f =>
            (f.PlayerId == playerId || f.FriendId == playerId) && f.Status == "accepted");

        var friendIds = friendships
            .Select(f => f.PlayerId == playerId ? f.FriendId : f.PlayerId)
            .Distinct()
            .ToList();

        if (friendIds.Count == 0) return new List<FriendProfileDto>();

        var profiles = await _profileRepo.GetAllByFilterAsync(p => friendIds.Contains(p.PlayerId));
        var profileMap = profiles.ToDictionary(p => p.PlayerId);

        var result = new List<FriendProfileDto>();
        foreach (var id in friendIds)
        {
            if (profileMap.TryGetValue(id, out var profile))
            {
                var isOnline = _presenceTracker.IsOnline(id);
                var status = _presenceTracker.GetStatus(id);
                result.Add(new FriendProfileDto(
                    profile.PlayerId,
                    profile.Username,
                    profile.Level,
                    isOnline,
                    status
                ));
            }
        }

        // Sort: Online first, then alphabetical by username
        return result
            .OrderByDescending(f => f.IsOnline)
            .ThenBy(f => f.Username)
            .ToList();
    }

    public async Task<List<FriendRequestDto>> GetPendingRequestsAsync(string playerId)
    {
        // Incoming requests where target is playerId and status is pending
        var incoming = await _friendshipRepo.GetAllByFilterAsync(f =>
            f.FriendId == playerId && f.Status == "pending");

        var senderIds = incoming.Select(f => f.PlayerId).Distinct().ToList();
        if (senderIds.Count == 0) return new List<FriendRequestDto>();

        var senders = await _profileRepo.GetAllByFilterAsync(p => senderIds.Contains(p.PlayerId));
        var senderMap = senders.ToDictionary(p => p.PlayerId);

        var result = new List<FriendRequestDto>();
        foreach (var req in incoming)
        {
            if (senderMap.TryGetValue(req.PlayerId, out var sender))
            {
                result.Add(new FriendRequestDto(
                    req.Id,
                    sender.PlayerId,
                    sender.Username,
                    sender.Level,
                    req.CreatedAt
                ));
            }
        }
        return result.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<List<string>> GetFriendPlayerIdsAsync(string playerId)
    {
        var friendships = await _friendshipRepo.GetAllByFilterAsync(f =>
            (f.PlayerId == playerId || f.FriendId == playerId) && f.Status == "accepted");

        return friendships
            .Select(f => f.PlayerId == playerId ? f.FriendId : f.PlayerId)
            .Distinct()
            .ToList();
    }

    public async Task<(bool Success, string Message, Friendship? Friendship)> SendFriendRequestAsync(
        string senderPlayerId, string targetUsernameOrId)
    {
        var targetProfile = await _profileRepo.GetByFilterAsync(p =>
            p.PlayerId == targetUsernameOrId || p.Username == targetUsernameOrId);

        if (targetProfile == null)
            return (false, "player_not_found", null);

        if (targetProfile.PlayerId == senderPlayerId)
            return (false, "cannot_add_self", null);

        // Check if existing relationship
        var existing = await _friendshipRepo.GetByFilterAsync(f =>
            (f.PlayerId == senderPlayerId && f.FriendId == targetProfile.PlayerId) ||
            (f.PlayerId == targetProfile.PlayerId && f.FriendId == senderPlayerId));

        if (existing != null)
        {
            if (existing.Status == "accepted")
                return (false, "already_friends", null);
            if (existing.Status == "pending")
            {
                if (existing.PlayerId == senderPlayerId)
                    return (false, "request_already_sent", null);
                
                // If the other person already sent a request to sender, accept it automatically!
                existing.Status = "accepted";
                existing.UpdatedAt = DateTime.UtcNow;
                await _friendshipRepo.UpdateAsync(existing.Id, existing);
                return (true, "request_accepted", existing);
            }
        }

        var friendship = new Friendship
        {
            PlayerId = senderPlayerId,
            FriendId = targetProfile.PlayerId,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _friendshipRepo.CreateAsync(friendship);
        return (true, "request_sent", friendship);
    }

    public async Task<bool> AcceptFriendRequestAsync(string playerId, string requestId)
    {
        var friendship = await _friendshipRepo.GetByIdAsync(requestId);
        if (friendship == null || friendship.FriendId != playerId || friendship.Status != "pending")
            return false;

        friendship.Status = "accepted";
        friendship.UpdatedAt = DateTime.UtcNow;
        await _friendshipRepo.UpdateAsync(friendship.Id, friendship);
        return true;
    }

    public async Task<bool> DeclineFriendRequestAsync(string playerId, string requestId)
    {
        var friendship = await _friendshipRepo.GetByIdAsync(requestId);
        if (friendship == null || friendship.FriendId != playerId || friendship.Status != "pending")
            return false;

        await _friendshipRepo.DeleteAsync(friendship.Id);
        return true;
    }

    public async Task<bool> RemoveFriendAsync(string playerId, string friendPlayerId)
    {
        var existing = await _friendshipRepo.GetByFilterAsync(f =>
            ((f.PlayerId == playerId && f.FriendId == friendPlayerId) ||
             (f.PlayerId == friendPlayerId && f.FriendId == playerId)) &&
            f.Status == "accepted");

        if (existing == null) return false;

        await _friendshipRepo.DeleteAsync(existing.Id);
        return true;
    }
}
