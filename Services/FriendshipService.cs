using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class FriendshipService : IFriendshipService
{
    private readonly IMongoRepository<Friendship> _friendshipRepo;
    private readonly IMongoRepository<PlayerProfile> _profileRepo;
    private readonly IMongoRepository<User> _userRepo;
    private readonly IUserPresenceTracker _presenceTracker;

    public FriendshipService(
        IMongoRepository<Friendship> friendshipRepo,
        IMongoRepository<PlayerProfile> profileRepo,
        IMongoRepository<User> userRepo,
        IUserPresenceTracker presenceTracker)
    {
        _friendshipRepo = friendshipRepo;
        _profileRepo = profileRepo;
        _userRepo = userRepo;
        _presenceTracker = presenceTracker;
    }

    private async Task<(string PlayerId, string Username, int Level)?> ResolvePlayerAsync(string usernameOrId)
    {
        var trimmed = usernameOrId.Trim();
        var lower = trimmed.ToLower();

        // 1. Try PlayerProfile by PlayerId or case-insensitive Username
        var profile = await _profileRepo.GetByFilterAsync(p => p.PlayerId == trimmed);
        if (profile == null)
        {
            var profiles = await _profileRepo.GetAllByFilterAsync(p => p.Username != null && p.Username.ToLower() == lower);
            profile = profiles.FirstOrDefault();
        }

        if (profile != null)
        {
            return (profile.PlayerId, profile.Username, profile.Level);
        }

        // 2. Fallback to User collection by Id or case-insensitive Username
        var user = await _userRepo.GetByFilterAsync(u => u.Id == trimmed);
        if (user == null)
        {
            var users = await _userRepo.GetAllByFilterAsync(u => u.Username != null && u.Username.ToLower() == lower);
            user = users.FirstOrDefault();
        }

        if (user != null)
        {
            return (user.Id, user.Username, 1);
        }

        return null;
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
        var profileMap = profiles.GroupBy(p => p.PlayerId).ToDictionary(g => g.Key, g => g.First());

        var users = await _userRepo.GetAllByFilterAsync(u => friendIds.Contains(u.Id));
        var userMap = users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First());

        var result = new List<FriendProfileDto>();
        foreach (var id in friendIds)
        {
            string username = "Unknown";
            int level = 1;

            if (profileMap.TryGetValue(id, out var profile))
            {
                username = profile.Username;
                level = profile.Level;
            }
            else if (userMap.TryGetValue(id, out var user))
            {
                username = user.Username;
            }

            var isOnline = _presenceTracker.IsOnline(id);
            var status = _presenceTracker.GetStatus(id);

            result.Add(new FriendProfileDto(
                id,
                username,
                level,
                isOnline,
                status
            ));
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

        var users = await _userRepo.GetAllByFilterAsync(u => senderIds.Contains(u.Id));
        var userMap = users.ToDictionary(u => u.Id);

        var result = new List<FriendRequestDto>();
        foreach (var req in incoming)
        {
            string username = "Unknown";
            int level = 1;

            if (senderMap.TryGetValue(req.PlayerId, out var sender))
            {
                username = sender.Username;
                level = sender.Level;
            }
            else if (userMap.TryGetValue(req.PlayerId, out var user))
            {
                username = user.Username;
            }

            result.Add(new FriendRequestDto(
                req.Id,
                req.PlayerId,
                username,
                level,
                req.CreatedAt
            ));
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
        var target = await ResolvePlayerAsync(targetUsernameOrId);
        if (target == null)
        {
            Console.WriteLine($"[Friendship] Target '{targetUsernameOrId}' not found for sender {senderPlayerId}");
            return (false, "player_not_found", null);
        }

        var targetPlayerId = target.Value.PlayerId;

        if (targetPlayerId == senderPlayerId)
            return (false, "cannot_add_self", null);

        // Check if existing relationship
        var existing = await _friendshipRepo.GetByFilterAsync(f =>
            (f.PlayerId == senderPlayerId && f.FriendId == targetPlayerId) ||
            (f.PlayerId == targetPlayerId && f.FriendId == senderPlayerId));

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
                Console.WriteLine($"[Friendship] Mutual request auto-accepted between {senderPlayerId} and {targetPlayerId}");
                return (true, "request_accepted", existing);
            }
        }

        var friendship = new Friendship
        {
            PlayerId = senderPlayerId,
            FriendId = targetPlayerId,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _friendshipRepo.CreateAsync(friendship);
        Console.WriteLine($"[Friendship] Request created: {senderPlayerId} -> {targetPlayerId} ({target.Value.Username})");
        return (true, "request_sent", friendship);
    }

    public async Task<(bool Success, Friendship? Friendship)> AcceptFriendRequestAsync(string playerId, string requestId)
    {
        var friendship = await _friendshipRepo.GetByIdAsync(requestId);
        if (friendship == null || friendship.FriendId != playerId || friendship.Status != "pending")
            return (false, null);

        friendship.Status = "accepted";
        friendship.UpdatedAt = DateTime.UtcNow;
        await _friendshipRepo.UpdateAsync(friendship.Id, friendship);
        Console.WriteLine($"[Friendship] Request {requestId} accepted by {playerId}");
        return (true, friendship);
    }

    public async Task<bool> DeclineFriendRequestAsync(string playerId, string requestId)
    {
        var friendship = await _friendshipRepo.GetByIdAsync(requestId);
        if (friendship == null || friendship.FriendId != playerId || friendship.Status != "pending")
            return false;

        await _friendshipRepo.DeleteAsync(friendship.Id);
        Console.WriteLine($"[Friendship] Request {requestId} declined by {playerId}");
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
        Console.WriteLine($"[Friendship] Friendship removed between {playerId} and {friendPlayerId}");
        return true;
    }
}
