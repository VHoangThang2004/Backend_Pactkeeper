using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IGameNotificationService
{
    Task<List<GameNotification>> GetPlayerGameNotificationsAsync(string playerId);
    Task<bool> MarkAsReadAsync(string playerId, string notificationId);
    Task<bool> MarkAllAsReadAsync(string playerId);
    Task<GameNotification> CreateGameNotificationAsync(string? playerId, string type, string title, string message, DateTime? expiresAt = null);
}
