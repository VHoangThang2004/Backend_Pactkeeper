using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IMatchSessionService
{
    Task<MatchSession?> GetByMatchIdAsync(string matchId);
    Task<MatchSession?> GetActiveMatchByPlayerIdAsync(string playerId);
    Task<MatchSession> CreateMatchAsync(string player1Id, string player2Id, string serverIp, int port);
    Task UpdateStatusAsync(string matchId, string status);
}