using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface IChatMessageService
{
    Task<List<ChatConversationDto>> GetConversationsAsync(string playerId);
    Task<List<ChatMessageDto>> GetMessagesBetweenAsync(string playerId1, string playerId2, int limit = 50);
    Task<ChatMessageDto> SendMessageAsync(string senderId, string receiverId, string text);
    Task<bool> MarkConversationAsReadAsync(string currentUserId, string friendPlayerId);
    Task<int> GetTotalUnreadCountAsync(string playerId);
}
