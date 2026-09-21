using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface INotificationService
{
    Task<List<Notification>> GetPlayerNotificationsAsync(string playerId);
    Task<bool> MarkAsReadAsync(string playerId, string notificationId);
    Task<bool> MarkAllAsReadAsync(string playerId);
    Task<Notification> CreateNotificationAsync(string playerId, string type, string title, string message);
}
