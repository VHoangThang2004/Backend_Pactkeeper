using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface ILlmService
{
    Task<(List<BalanceRecommendationItem> Recommendations, string RawResponse)> GetBalancingSuggestionsAsync(
        string patchVersion,
        int totalMatches,
        List<BalanceAnomalyItem> anomalies);
}
