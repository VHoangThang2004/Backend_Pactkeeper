using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IMatchSessionService
{
    Task<MatchSession?> GetByMatchIdAsync(string matchId);
    Task<MatchSession?> GetActiveMatchByPlayerIdAsync(string playerId);
    Task<MatchSession> CreateMatchAsync(string player1Id, string player2Id, string serverIp, int port, string mode = "pvp", string mapId = "MD_PVP_001");
    Task<List<MatchSession>> GetAllByPlayerIdAsync(string playerId);
    Task<List<MatchSession>> GetAllAsync();

    Task UpdateStatusAsync(string matchId, string status, MatchResultData? result = null);
    Task UpdateProcessIdAsync(string matchId, int processId);
    // Atomically moves a live (pending/active) match to "finishing"; null if it was already claimed or ended
    Task<MatchSession?> TryClaimForFinishAsync(string matchId);
    Task DeleteAsync(string matchId);
    Task EndAllMatchesAsync();

    // Kill the server process (if alive), delete the session record and free its port.
    // Used to discard a stale story match so the scene restarts from a clean state.
    Task TerminateMatchAsync(MatchSession match, string reason);
}