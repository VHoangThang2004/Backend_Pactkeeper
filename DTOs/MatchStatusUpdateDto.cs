using GameInventoryApi.Models;

namespace GameInventoryApi.DTOs;

public record MatchStatusUpdateDto(
    string Status,
    MatchResultData? Result = null
);