using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IBalanceAnalyticsService
{
    Task<BalanceAnalyticsResponseDto> GetBalanceMetricsAsync(BalanceAnalyticsFilterDto filter);
    Task<MatchUnitTelemetry> RecordTelemetryAsync(MatchUnitTelemetry telemetry);
    Task<BalanceAiSuggestion> GetOrGenerateAiSuggestionAsync(AiSuggestRequestDto request, string adminUserId, string adminUsername);
    Task<List<BalanceAiSuggestion>> GetSavedAiSuggestionsAsync(string? patchVersion = null);
}
