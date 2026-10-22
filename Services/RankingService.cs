using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class RankingService : IRankingService
{
    private readonly IMongoRepository<PlayerProfile> _profileRepository;

    private static readonly string[] RankTierNames = new[]
    {
        "Beginner",        // 0 (0-99)
        "Master",          // 1 (100-199)
        "Captain",         // 2 (200-299)
        "Pactkeeper",      // 3 (300-399)
        "Warlord",         // 4 (400-499)
        "Grand Commander", // 5 (500-599)
        "Union"            // 6 (600+)
    };

    public RankingService(IMongoRepository<PlayerProfile> profileRepository)
    {
        _profileRepository = profileRepository;
    }

    public int CalculateRankTier(int rating)
    {
        if (rating >= 600) return 6;
        if (rating >= 500) return 5;
        if (rating >= 400) return 4;
        if (rating >= 300) return 3;
        if (rating >= 200) return 2;
        if (rating >= 100) return 1;
        return 0;
    }

    public string GetRankName(int tier)
    {
        if (tier < 0 || tier >= RankTierNames.Length)
            return "Beginner";
        return RankTierNames[tier];
    }

    public (int winnerGain, int loserLoss) CalculateRatingDelta(int winnerRating, int loserRating)
    {
        // Elo expected score for winner
        double expectedWinner = 1.0 / (1.0 + Math.Pow(10.0, (loserRating - winnerRating) / 400.0));

        // Base K-factor 32
        int gain = (int)Math.Round(32.0 * (1.0 - expectedWinner));
        gain = Math.Clamp(gain, 15, 35);

        // Loser loss is slightly softer to encourage ladder climbing (~80% of win gain)
        int loss = (int)Math.Round(gain * 0.8);
        loss = Math.Clamp(loss, 12, 28);

        return (gain, loss);
    }

    public async Task<LeaderboardResponseDto> GetLeaderboardAsync(string? currentPlayerId, int limit = 100)
    {
        var topProfiles = await _profileRepository.GetTopAsync(p => true, p => p.Rating, limit);

        var items = new List<LeaderboardEntryDto>();
        LeaderboardEntryDto? currentPlayerEntry = null;

        for (int i = 0; i < topProfiles.Count; i++)
        {
            var p = topProfiles[i];
            int tier = CalculateRankTier(p.Rating);
            bool isCurrent = !string.IsNullOrEmpty(currentPlayerId) && p.PlayerId == currentPlayerId;

            var entry = new LeaderboardEntryDto(
                Position: i + 1,
                PlayerId: p.PlayerId,
                Username: p.Username,
                Level: p.Level,
                Rating: p.Rating,
                RankTier: tier,
                RankName: GetRankName(tier),
                RankWins: p.RankWins,
                RankLosses: p.RankLosses,
                IsCurrentPlayer: isCurrent
            );

            items.Add(entry);
            if (isCurrent)
            {
                currentPlayerEntry = entry;
            }
        }

        // If current player is not in top limit, fetch their individual rank
        if (currentPlayerEntry == null && !string.IsNullOrEmpty(currentPlayerId))
        {
            currentPlayerEntry = await GetPlayerRankAsync(currentPlayerId);
        }

        return new LeaderboardResponseDto(items, currentPlayerEntry);
    }

    public async Task<LeaderboardEntryDto?> GetPlayerRankAsync(string playerId)
    {
        var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return null;

        // Count how many players have strictly higher rating to determine position
        var higherProfiles = await _profileRepository.GetAllByFilterAsync(p => p.Rating > profile.Rating);
        int position = higherProfiles.Count + 1;

        int tier = CalculateRankTier(profile.Rating);
        return new LeaderboardEntryDto(
            Position: position,
            PlayerId: profile.PlayerId,
            Username: profile.Username,
            Level: profile.Level,
            Rating: profile.Rating,
            RankTier: tier,
            RankName: GetRankName(tier),
            RankWins: profile.RankWins,
            RankLosses: profile.RankLosses,
            IsCurrentPlayer: true
        );
    }
}
