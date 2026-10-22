using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface IRankingService
{
    int CalculateRankTier(int rating);
    string GetRankName(int tier);
    (int winnerGain, int loserLoss) CalculateRatingDelta(int winnerRating, int loserRating);
    Task<LeaderboardResponseDto> GetLeaderboardAsync(string? currentPlayerId, int limit = 100);
    Task<LeaderboardEntryDto?> GetPlayerRankAsync(string playerId);
}
