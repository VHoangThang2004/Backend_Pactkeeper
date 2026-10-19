using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IFriendshipService
{
    Task<List<FriendProfileDto>> GetFriendsWithPresenceAsync(string playerId);
    Task<List<FriendRequestDto>> GetPendingRequestsAsync(string playerId);
    Task<List<string>> GetFriendPlayerIdsAsync(string playerId);
    Task<(bool Success, string Message, Friendship? Friendship)> SendFriendRequestAsync(string senderPlayerId, string targetUsernameOrId);
    Task<bool> AcceptFriendRequestAsync(string playerId, string requestId);
    Task<bool> DeclineFriendRequestAsync(string playerId, string requestId);
    Task<bool> RemoveFriendAsync(string playerId, string friendPlayerId);
}
