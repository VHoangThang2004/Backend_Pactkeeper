using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public record ActiveChatPlayerDto(string PlayerId, string PlayerName, string LatestMessageText, DateTime LatestMessageTime);

public interface ISupportService
{
    Task<List<SupportMessage>> GetChatHistoryAsync(string playerId);
    Task<SupportMessage> SendMessageAsync(string playerId, string sender, string senderName, string text, string attachmentUrl = "");
    Task<List<ActiveChatPlayerDto>> GetActiveChatPlayersAsync();
}
