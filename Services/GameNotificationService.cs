using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class GameNotificationService(IMongoRepository<GameNotification> repository) : IGameNotificationService
{
    private readonly IMongoRepository<GameNotification> _repository = repository;

    public async Task<List<GameNotification>> GetPlayerGameNotificationsAsync(string playerId)
    {
        var now = DateTime.UtcNow;
        var list = await _repository.GetAllByFilterAsync(n =>
            n.PlayerId == playerId || n.PlayerId == null || n.PlayerId == "" || n.PlayerId == "*");

        return list
            .Where(n => n.ExpiresAt == null || n.ExpiresAt > now)
            .OrderByDescending(n => n.CreatedAt)
            .ToList();
    }

    public async Task<bool> MarkAsReadAsync(string playerId, string notificationId)
    {
        var notif = await _repository.GetByIdAsync(notificationId);
        if (notif == null) return false;

        if (notif.PlayerId == playerId)
        {
            notif.Read = true;
            await _repository.UpdateAsync(notificationId, notif);
            return true;
        }

        if (string.IsNullOrEmpty(notif.PlayerId) || notif.PlayerId == "*")
        {
            if (!notif.ReadByPlayerIds.Contains(playerId))
            {
                notif.ReadByPlayerIds.Add(playerId);
                await _repository.UpdateAsync(notificationId, notif);
            }
            return true;
        }

        return false;
    }

    public async Task<bool> MarkAllAsReadAsync(string playerId)
    {
        var now = DateTime.UtcNow;
        var playerNotifs = await _repository.GetAllByFilterAsync(n =>
            n.PlayerId == playerId && !n.Read);

        foreach (var notif in playerNotifs.Where(n => n.ExpiresAt == null || n.ExpiresAt > now))
        {
            notif.Read = true;
            await _repository.UpdateAsync(notif.Id, notif);
        }

        var globalNotifs = await _repository.GetAllByFilterAsync(n =>
            (n.PlayerId == null || n.PlayerId == "" || n.PlayerId == "*")
            && !n.ReadByPlayerIds.Contains(playerId));

        foreach (var notif in globalNotifs.Where(n => n.ExpiresAt == null || n.ExpiresAt > now))
        {
            notif.ReadByPlayerIds.Add(playerId);
            await _repository.UpdateAsync(notif.Id, notif);
        }

        return true;
    }

    public async Task<GameNotification> CreateGameNotificationAsync(
        string? playerId,
        string type,
        string title,
        string message,
        DateTime? expiresAt = null)
    {
        var notif = new GameNotification
        {
            PlayerId = playerId,
            Type = type,
            Title = title,
            Message = message,
            Read = false,
            ReadByPlayerIds = [],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        await _repository.CreateAsync(notif);
        return notif;
    }
}
