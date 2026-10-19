using System.Collections.Concurrent;

namespace GameInventoryApi.Services;

public class UserPresenceTracker : IUserPresenceTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _onlineUsers = new();
    private readonly ConcurrentDictionary<string, bool> _inMatchUsers = new();

    public bool AddConnection(string playerId, string connectionId)
    {
        lock (_onlineUsers)
        {
            if (!_onlineUsers.TryGetValue(playerId, out var connections))
            {
                connections = new HashSet<string>();
                _onlineUsers[playerId] = connections;
            }

            bool wasOffline = connections.Count == 0;
            connections.Add(connectionId);
            return wasOffline;
        }
    }

    public bool RemoveConnection(string playerId, string connectionId)
    {
        lock (_onlineUsers)
        {
            if (_onlineUsers.TryGetValue(playerId, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    _onlineUsers.TryRemove(playerId, out _);
                    _inMatchUsers.TryRemove(playerId, out _);
                    return true;
                }
            }
            return false;
        }
    }

    public bool IsOnline(string playerId) => _onlineUsers.ContainsKey(playerId);

    public void SetInMatch(string playerId, bool inMatch)
    {
        if (inMatch)
            _inMatchUsers[playerId] = true;
        else
            _inMatchUsers.TryRemove(playerId, out _);
    }

    public bool IsInMatch(string playerId) => _inMatchUsers.ContainsKey(playerId);

    public string GetStatus(string playerId)
    {
        if (!IsOnline(playerId)) return "offline";
        if (IsInMatch(playerId)) return "in_match";
        return "idle";
    }
}
