namespace GameInventoryApi.DTOs;

public record LeaderboardEntryDto(
    int Position,
    string PlayerId,
    string Username,
    int Level,
    int Rating,
    int RankTier,
    string RankName,
    int RankWins,
    int RankLosses,
    bool IsCurrentPlayer = false
);

public record LeaderboardResponseDto(
    List<LeaderboardEntryDto> Items,
    LeaderboardEntryDto? CurrentPlayer
);

public record JoinQueueRequestDto(
    string? Mode = "pvp"
);
