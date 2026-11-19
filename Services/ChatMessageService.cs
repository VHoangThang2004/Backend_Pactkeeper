using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class ChatMessageService : IChatMessageService
{
    private readonly IMongoRepository<ChatMessage> _messageRepository;
    private readonly IFriendshipService _friendshipService;
    private readonly IPlayerProfileService _profileService;

    public ChatMessageService(
        IMongoRepository<ChatMessage> messageRepository,
        IFriendshipService friendshipService,
        IPlayerProfileService profileService)
    {
        _messageRepository = messageRepository;
        _friendshipService = friendshipService;
        _profileService = profileService;
    }

    public async Task<List<ChatConversationDto>> GetConversationsAsync(string playerId)
    {
        var friends = await _friendshipService.GetFriendsWithPresenceAsync(playerId);
        if (friends.Count == 0) return new List<ChatConversationDto>();

        var allMyMessages = await _messageRepository.GetAllByFilterAsync(m =>
            m.SenderId == playerId || m.ReceiverId == playerId);

        var conversations = new List<ChatConversationDto>();

        foreach (var friend in friends)
        {
            var conversationMessages = allMyMessages
                .Where(m => (m.SenderId == playerId && m.ReceiverId == friend.PlayerId) ||
                            (m.SenderId == friend.PlayerId && m.ReceiverId == playerId))
                .OrderByDescending(m => m.CreatedAt)
                .ToList();

            var latest = conversationMessages.FirstOrDefault();
            var unreadCount = conversationMessages.Count(m => m.SenderId == friend.PlayerId && !m.IsRead);

            conversations.Add(new ChatConversationDto(
                FriendPlayerId: friend.PlayerId,
                FriendUsername: friend.Username,
                FriendLevel: friend.Level,
                IsOnline: friend.IsOnline,
                Status: friend.Status,
                LatestMessageText: latest?.Text ?? string.Empty,
                LatestMessageTime: latest?.CreatedAt,
                UnreadCount: unreadCount
            ));
        }

        return conversations
            .OrderByDescending(c => c.LatestMessageTime.HasValue)
            .ThenByDescending(c => c.LatestMessageTime)
            .ThenByDescending(c => c.IsOnline)
            .ThenBy(c => c.FriendUsername)
            .ToList();
    }

    public async Task<List<ChatMessageDto>> GetMessagesBetweenAsync(string playerId1, string playerId2, int limit = 50)
    {
        // Enforce that both players are mutual friends
        var friendIds = await _friendshipService.GetFriendPlayerIdsAsync(playerId1);
        if (!friendIds.Contains(playerId2))
        {
            throw new InvalidOperationException("not_friends");
        }

        var messages = await _messageRepository.GetAllByFilterAsync(m =>
            (m.SenderId == playerId1 && m.ReceiverId == playerId2) ||
            (m.SenderId == playerId2 && m.ReceiverId == playerId1));

        return messages
            .OrderBy(m => m.CreatedAt)
            .TakeLast(limit)
            .Select(m => new ChatMessageDto(
                m.Id,
                m.SenderId,
                m.SenderUsername,
                m.ReceiverId,
                m.Text,
                m.IsRead,
                m.CreatedAt
            ))
            .ToList();
    }

    public async Task<ChatMessageDto> SendMessageAsync(string senderId, string receiverId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Message cannot be empty", nameof(text));

        // Enforce mutual friendship
        var friendIds = await _friendshipService.GetFriendPlayerIdsAsync(senderId);
        if (!friendIds.Contains(receiverId))
        {
            throw new InvalidOperationException("not_friends");
        }

        var senderProfile = await _profileService.GetByFilterAsync(p => p.PlayerId == senderId);
        var senderUsername = senderProfile?.Username ?? "Player";

        var message = new ChatMessage
        {
            SenderId = senderId,
            SenderUsername = senderUsername,
            ReceiverId = receiverId,
            Text = text.Trim(),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await _messageRepository.CreateAsync(message);

        return new ChatMessageDto(
            message.Id,
            message.SenderId,
            message.SenderUsername,
            message.ReceiverId,
            message.Text,
            message.IsRead,
            message.CreatedAt
        );
    }

    public async Task<bool> MarkConversationAsReadAsync(string currentUserId, string friendPlayerId)
    {
        var unread = await _messageRepository.GetAllByFilterAsync(m =>
            m.SenderId == friendPlayerId && m.ReceiverId == currentUserId && !m.IsRead);

        foreach (var msg in unread)
        {
            msg.IsRead = true;
            await _messageRepository.UpdateAsync(msg.Id, msg);
        }

        return true;
    }

    public async Task<int> GetTotalUnreadCountAsync(string playerId)
    {
        var unread = await _messageRepository.GetAllByFilterAsync(m =>
            m.ReceiverId == playerId && !m.IsRead);

        return unread.Count;
    }
}
