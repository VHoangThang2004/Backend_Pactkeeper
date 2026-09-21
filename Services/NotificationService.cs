using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class NotificationService(IMongoRepository<Notification> repository) : INotificationService
{
    private readonly IMongoRepository<Notification> _repository = repository;

    public async Task<List<Notification>> GetPlayerNotificationsAsync(string playerId)
    {
        var list = await _repository.GetAllByFilterAsync(n => n.PlayerId == playerId);
        return list.OrderByDescending(n => n.CreatedAt).ToList();
    }

    public async Task<bool> MarkAsReadAsync(string playerId, string notificationId)
    {
        var notif = await _repository.GetByIdAsync(notificationId);
        if (notif == null || notif.PlayerId != playerId) return false;

        notif.Read = true;
        await _repository.UpdateAsync(notificationId, notif);
        return true;
    }

    public async Task<bool> MarkAllAsReadAsync(string playerId)
    {
        var list = await _repository.GetAllByFilterAsync(n => n.PlayerId == playerId && !n.Read);
        foreach (var notif in list)
        {
            notif.Read = true;
            await _repository.UpdateAsync(notif.Id, notif);
        }
        return true;
    }

    public async Task<Notification> CreateNotificationAsync(string playerId, string type, string title, string message)
    {
        var notif = new Notification
        {
            PlayerId = playerId,
            Type = type,
            Title = title,
            Message = message,
            Read = false,
            CreatedAt = DateTime.UtcNow
        };
        await _repository.CreateAsync(notif);
        return notif;
    }
}
