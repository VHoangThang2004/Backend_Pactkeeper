namespace GameInventoryApi.DTOs;

public record MatchSessionDto(
    string MatchId,
    string Player1Id,
    string Player2Id,
    string ServerIp,
    int ServerPort,
    string Status,
    string Mode
);

public record MatchSessionJoinInfoDto(
    string MatchId,
    string ServerIp,
    int ServerPort,
    string Mode,
    string Player1Name,
    string Player2Name
);

public record StartMatchRequestDto(
    string Player1Id,
    string Player2Id
);
public record MatchInfoDto(
    string MatchId,
    string Mode,
    string MapId
);

public record AfterMatchPlayerDataDto(
    string PlayerId,
    int UnitsAlive,
    int TotalUnits
);

public record MatchResultDataDto(
    string WinnerId,
    int DurationSeconds,
    int TotalInstants,
    List<AfterMatchPlayerDataDto> AfterMatchTeamData
);

public record MatchHistoryDto(
    string MatchId,
    string Player1Id,
    string Player2Id,
    string Status,
    MatchResultDataDto? Result,
    DateTime CompletedAt
);
