namespace GameInventoryApi.Services;

public interface IUserPresenceTracker
{
    bool AddConnection(string playerId, string connectionId);
    bool RemoveConnection(string playerId, string connectionId);
    bool IsOnline(string playerId);
    void SetInMatch(string playerId, bool inMatch);
    bool IsInMatch(string playerId);
    string GetStatus(string playerId);
}
