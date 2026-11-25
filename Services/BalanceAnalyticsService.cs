using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Microsoft.Extensions.Options;

namespace GameInventoryApi.Services;

public class BalanceAnalyticsService : IBalanceAnalyticsService
{
    private readonly IMongoRepository<MatchUnitTelemetry> _telemetryRepo;
    private readonly IMongoRepository<BalanceAiSuggestion> _aiSuggestionRepo;
    private readonly IUnitDefinitionService _unitService;
    private readonly IWeaponDefinitionService _weaponService;
    private readonly ITrinketDefinitionService _trinketService;
    private readonly ISkillDefinitionService _skillService;
    private readonly ILlmService _llmService;
    private readonly LlmSettings _llmSettings;

    public BalanceAnalyticsService(
        IMongoRepository<MatchUnitTelemetry> telemetryRepo,
        IMongoRepository<BalanceAiSuggestion> aiSuggestionRepo,
        IUnitDefinitionService unitService,
        IWeaponDefinitionService weaponService,
        ITrinketDefinitionService trinketService,
        ISkillDefinitionService skillService,
        ILlmService llmService,
        IOptions<LlmSettings> llmSettings)
    {
        _telemetryRepo = telemetryRepo;
        _aiSuggestionRepo = aiSuggestionRepo;
        _unitService = unitService;
        _weaponService = weaponService;
        _trinketService = trinketService;
        _skillService = skillService;
        _llmService = llmService;
        _llmSettings = llmSettings.Value;
    }

    public async Task<MatchUnitTelemetry> RecordTelemetryAsync(MatchUnitTelemetry telemetry)
    {
        if (telemetry.CreatedAt == default)
        {
            telemetry.CreatedAt = DateTime.UtcNow;
        }
        await _telemetryRepo.CreateAsync(telemetry);
        return telemetry;
    }

    public async Task<BalanceAnalyticsResponseDto> GetBalanceMetricsAsync(BalanceAnalyticsFilterDto filter)
    {
        var matches = await _telemetryRepo.GetAllByFilterAsync(t =>
            (string.IsNullOrEmpty(filter.PatchVersion) || t.PatchVersion == filter.PatchVersion) &&
            (string.IsNullOrEmpty(filter.Mode) || t.Mode.ToLower() == filter.Mode.ToLower()) &&
            (!filter.FromDate.HasValue || t.CreatedAt >= filter.FromDate.Value) &&
            (!filter.ToDate.HasValue || t.CreatedAt <= filter.ToDate.Value));

        int totalMatches = matches.Count;

        var response = new BalanceAnalyticsResponseDto
        {
            PatchVersion = filter.PatchVersion,
            Mode = filter.Mode,
            TotalMatches = totalMatches
        };

        if (totalMatches == 0)
        {
            return response;
        }

        // Pre-fetch definition metadata for human-readable labels
        var unitDefs = (await _unitService.GetAllAsync()).ToDictionary(u => u.UId, u => u.UnitName);
        var weaponDefs = (await _weaponService.GetAllAsync()).ToDictionary(w => w.WeaponId, w => w.Name);
        var trinketDefs = (await _trinketService.GetAllAsync()).ToDictionary(t => t.TrinketId, t => t.Name);
        var skillDefs = (await _skillService.GetAllAsync()).ToDictionary(s => s.SkillId, s => $"Skill {s.SkillId}");

        // Aggregation buckets
        var unitStats = new Dictionary<int, EntityStatAccumulator>();
        var weaponStats = new Dictionary<int, EntityStatAccumulator>();
        var trinketStats = new Dictionary<int, EntityStatAccumulator>();
        var skillStats = new Dictionary<int, EntityStatAccumulator>();

        foreach (var match in matches)
        {
            string winnerId = match.WinnerId;

            foreach (var unit in match.Units)
            {
                bool isWinner = !string.IsNullOrEmpty(winnerId) &&
                                string.Equals(unit.PlayerId, winnerId, StringComparison.OrdinalIgnoreCase);

                // Accumulate Unit
                if (unit.UnitUId > 0)
                {
                    Accumulate(unitStats, unit.UnitUId, isWinner, unit.Survived, unit.DamageDealt);
                }

                // Accumulate Weapon
                if (unit.WeaponId > 0)
                {
                    Accumulate(weaponStats, unit.WeaponId, isWinner, unit.Survived, unit.DamageDealt);
                }

                // Accumulate Trinket
                if (unit.TrinketId > 0)
                {
                    Accumulate(trinketStats, unit.TrinketId, isWinner, unit.Survived, unit.DamageDealt);
                }

                // Accumulate Skills
                if (unit.SkillIds != null)
                {
                    foreach (var skillId in unit.SkillIds.Distinct())
                    {
                        if (skillId > 0)
                        {
                            Accumulate(skillStats, skillId, isWinner, unit.Survived, unit.DamageDealt);
                        }
                    }
                }
            }
        }

        double highWinRate = _llmSettings.WinRateHighThreshold;
        double lowWinRate = _llmSettings.WinRateLowThreshold;

        response.Units = BuildMetrics(unitStats, "Unit", totalMatches, id => unitDefs.GetValueOrDefault(id, $"Unit #{id}"), highWinRate, lowWinRate);
        response.Weapons = BuildMetrics(weaponStats, "Weapon", totalMatches, id => weaponDefs.GetValueOrDefault(id, $"Weapon #{id}"), highWinRate, lowWinRate);
        response.Trinkets = BuildMetrics(trinketStats, "Trinket", totalMatches, id => trinketDefs.GetValueOrDefault(id, $"Trinket #{id}"), highWinRate, lowWinRate);
        response.Skills = BuildMetrics(skillStats, "Skill", totalMatches, id => skillDefs.GetValueOrDefault(id, $"Skill #{id}"), highWinRate, lowWinRate);

        return response;
    }

    public async Task<BalanceAiSuggestion> GetOrGenerateAiSuggestionAsync(
        AiSuggestRequestDto request,
        string adminUserId,
        string adminUsername)
    {
        string patchVersion = string.IsNullOrWhiteSpace(request.PatchVersion) ? "latest" : request.PatchVersion;

        // Token saving check: return existing suggestion if one was already generated for this patch & mode
        if (!request.ForceRegenerate)
        {
            var existing = await _aiSuggestionRepo.GetByFilterAsync(s =>
                s.PatchVersion == patchVersion &&
                (string.IsNullOrEmpty(request.Mode) || s.Mode == request.Mode));

            if (existing != null)
            {
                Console.WriteLine($"[BalanceAnalytics] Cache hit: returning existing suggestion for patch '{patchVersion}' (saving LLM tokens).");
                return existing;
            }
        }

        // Fetch balance metrics for the requested patch
        var metrics = await GetBalanceMetricsAsync(new BalanceAnalyticsFilterDto
        {
            PatchVersion = string.Equals(patchVersion, "latest", StringComparison.OrdinalIgnoreCase) ? null : patchVersion,
            Mode = request.Mode
        });

        double highThreshold = request.HighWinRateThreshold ?? _llmSettings.WinRateHighThreshold;
        double lowThreshold = request.LowWinRateThreshold ?? _llmSettings.WinRateLowThreshold;
        int minPickCount = request.MinPickCount ?? _llmSettings.MinPickCount;

        // Collect out-of-average items (winrate > 60% or < 40%)
        var allItems = metrics.Units
            .Concat(metrics.Weapons)
            .Concat(metrics.Trinkets)
            .Concat(metrics.Skills)
            .ToList();

        // If total matches are low, adjust minPickCount so we still catch anomalies
        int effectiveMinPick = metrics.TotalMatches > 0 && metrics.TotalMatches < minPickCount
            ? Math.Max(1, metrics.TotalMatches / 2)
            : minPickCount;

        var anomalies = allItems
            .Where(m => m.PickCount >= effectiveMinPick && (m.WinRate >= highThreshold || m.WinRate <= lowThreshold))
            .Select(m => new BalanceAnomalyItem
            {
                Category = m.Category,
                TargetId = m.Id,
                TargetName = m.Name,
                PickCount = m.PickCount,
                PickRate = m.PickRate,
                WinRate = m.WinRate,
                SurvivalRate = m.SurvivalRate,
                AvgDamage = m.AvgDamage,
                AnomalyType = m.WinRate >= highThreshold ? "OVERPERFORMING" : "UNDERPERFORMING"
            })
            .OrderByDescending(a => a.PickCount)
            .ToList();

        // Ask LLM for suggestions
        var (recommendations, rawResponse) = await _llmService.GetBalancingSuggestionsAsync(
            patchVersion,
            metrics.TotalMatches,
            anomalies);

        var suggestion = new BalanceAiSuggestion
        {
            PatchVersion = patchVersion,
            Mode = request.Mode,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = adminUserId,
            CreatedByUsername = adminUsername,
            TotalMatchesAnalyzed = metrics.TotalMatches,
            Outliers = anomalies,
            Recommendations = recommendations,
            RawLlmResponse = rawResponse
        };

        await _aiSuggestionRepo.CreateAsync(suggestion);
        Console.WriteLine($"[BalanceAnalytics] Saved new balance suggestion for patch '{patchVersion}' with {recommendations.Count} recommendations.");

        return suggestion;
    }

    public async Task<List<BalanceAiSuggestion>> GetSavedAiSuggestionsAsync(string? patchVersion = null)
    {
        var list = string.IsNullOrEmpty(patchVersion)
            ? await _aiSuggestionRepo.GetAllAsync()
            : await _aiSuggestionRepo.GetAllByFilterAsync(s => s.PatchVersion == patchVersion);

        return list.OrderByDescending(s => s.CreatedAt).ToList();
    }

    private static void Accumulate(
        Dictionary<int, EntityStatAccumulator> dict,
        int id,
        bool isWinner,
        bool survived,
        int damage)
    {
        if (!dict.TryGetValue(id, out var acc))
        {
            acc = new EntityStatAccumulator();
            dict[id] = acc;
        }

        acc.PickCount++;
        if (isWinner) acc.WinCount++;
        if (survived) acc.SurvivedCount++;
        acc.TotalDamage += damage;
    }

    private static List<BalanceMetricItemDto> BuildMetrics(
        Dictionary<int, EntityStatAccumulator> dict,
        string category,
        int totalMatches,
        Func<int, string> nameLookup,
        double highThreshold,
        double lowThreshold)
    {
        return dict.Select(kvp =>
        {
            int id = kvp.Key;
            var acc = kvp.Value;
            double pickRate = totalMatches > 0 ? Math.Round((double)acc.PickCount / totalMatches, 4) : 0;
            double winRate = acc.PickCount > 0 ? Math.Round((double)acc.WinCount / acc.PickCount, 4) : 0;
            double survivalRate = acc.PickCount > 0 ? Math.Round((double)acc.SurvivedCount / acc.PickCount, 4) : 0;
            double avgDamage = acc.PickCount > 0 ? Math.Round((double)acc.TotalDamage / acc.PickCount, 1) : 0;

            bool isOutlier = winRate >= highThreshold || winRate <= lowThreshold;

            return new BalanceMetricItemDto
            {
                Id = id,
                Name = nameLookup(id),
                Category = category,
                TotalMatches = totalMatches,
                PickCount = acc.PickCount,
                PickRate = pickRate,
                WinCount = acc.WinCount,
                WinRate = winRate,
                SurvivedCount = acc.SurvivedCount,
                SurvivalRate = survivalRate,
                AvgDamage = avgDamage,
                IsOutlier = isOutlier
            };
        })
        .OrderByDescending(m => m.PickCount)
        .ToList();
    }

    private class EntityStatAccumulator
    {
        public int PickCount { get; set; }
        public int WinCount { get; set; }
        public int SurvivedCount { get; set; }
        public long TotalDamage { get; set; }
    }
}
