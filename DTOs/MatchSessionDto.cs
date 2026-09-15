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
    int ServerPort
);

public record StartMatchRequestDto(
    string Player1Id,
    string Player2Id
);
public record MatchInfoDto(
    string MatchId,
    string Mode,
    string StoryChapterId
);
public record MatchLoadoutsResponseDto(
    string Player1Id,
    string Player2Id,
    TeamLoadoutDto? Player1Loadout,
    TeamLoadoutDto? Player2Loadout
);
