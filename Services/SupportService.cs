using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class SupportService(IMongoRepository<SupportMessage> repository) : ISupportService
{
    private readonly IMongoRepository<SupportMessage> _repository = repository;

    public async Task<List<SupportMessage>> GetChatHistoryAsync(string playerId)
    {
        var messages = await _repository.GetAllByFilterAsync(m => m.PlayerId == playerId);
        return messages.OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<SupportMessage> SendMessageAsync(string playerId, string sender, string senderName, string text, string attachmentUrl = "")
    {
        var message = new SupportMessage
        {
            PlayerId = playerId,
            Sender = sender,
            SenderName = senderName,
            Text = text,
            AttachmentUrl = attachmentUrl,
            CreatedAt = DateTime.UtcNow
        };
        await _repository.CreateAsync(message);
        return message;
    }

    public async Task<List<ActiveChatPlayerDto>> GetActiveChatPlayersAsync()
    {
        var allMessages = await _repository.GetAllAsync();
        
        var grouped = allMessages
            .GroupBy(m => m.PlayerId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(m => m.CreatedAt).First();
                var userName = g.FirstOrDefault(m => m.Sender == "user")?.SenderName ?? "Wanderer";
                return new ActiveChatPlayerDto(
                    latest.PlayerId,
                    userName,
                    latest.Text,
                    latest.CreatedAt
                );
            })
            .OrderByDescending(dto => dto.LatestMessageTime)
            .ToList();

        return grouped;
    }
}
