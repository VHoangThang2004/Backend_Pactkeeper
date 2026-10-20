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

public record MatchLogResponseDto(
    string MatchId,
    string Logs,
    string? LogPath = null,
    int TotalLines = 0,
    string? Status = null,
    DateTime? RetrievedAt = null
);

public record MatchLogFileInfo(
    string MatchId,
    string FileName,
    long FileSizeBytes,
    DateTime LastModifiedUtc,
    DateTime CreatedAtUtc
);


