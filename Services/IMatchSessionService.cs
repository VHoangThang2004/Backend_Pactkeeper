using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IMatchSessionService
{
    Task<MatchSession?> GetByMatchIdAsync(string matchId);
    Task<MatchSession?> GetActiveMatchByPlayerIdAsync(string playerId);
    Task<MatchSession> CreateMatchAsync(string player1Id, string player2Id, string serverIp, int port, string mode = "pvp", string mapId = "MD_PVP_001");
    Task<List<MatchSession>> GetAllByPlayerIdAsync(string playerId);

    Task UpdateStatusAsync(string matchId, string status, MatchResultData? result = null);
    Task UpdateProcessIdAsync(string matchId, int processId);
    Task DeleteAsync(string matchId);
}