using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Microsoft.Extensions.Options;

namespace GameInventoryApi.Services;

public class BalanceAnalyticsService : IBalanceAnalyticsService
{
    private readonly IMongoRepository<MatchUnitTelemetry> _telemetryRepo;
    private readonly IMongoRepository<BalanceAiSuggestion> _aiSuggestionRepo;
    private readonly IMatchHistoryService _historyService;
    private readonly IGameVersionService _gameVersionService;
    private readonly IUnitDefinitionService _unitService;
    private readonly IWeaponDefinitionService _weaponService;
    private readonly ITrinketDefinitionService _trinketService;
    private readonly ISkillDefinitionService _skillService;
    private readonly ILlmService _llmService;
    private readonly LlmSettings _llmSettings;

    public BalanceAnalyticsService(
        IMongoRepository<MatchUnitTelemetry> telemetryRepo,
        IMongoRepository<BalanceAiSuggestion> aiSuggestionRepo,
        IMatchHistoryService historyService,
        IGameVersionService gameVersionService,
        IUnitDefinitionService unitService,
        IWeaponDefinitionService weaponService,
        ITrinketDefinitionService trinketService,
        ISkillDefinitionService skillService,
        ILlmService llmService,
        IOptions<LlmSettings> llmSettings)
    {
        _telemetryRepo = telemetryRepo;
        _aiSuggestionRepo = aiSuggestionRepo;
        _historyService = historyService;
        _gameVersionService = gameVersionService;
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
        var versionConfig = await _gameVersionService.GetVersionConfigAsync();
        string currentPatch = string.IsNullOrWhiteSpace(versionConfig?.LatestVersion)
            ? "1.0.0"
            : versionConfig.LatestVersion.Trim();

        // Pre-fetch definition metadata for human-readable labels and SkillId -> EquipmentId mapping
        var allUnits = await _unitService.GetAllAsync();
        var allWeapons = await _weaponService.GetAllAsync();
        var allTrinkets = await _trinketService.GetAllAsync();
        var allSkills = await _skillService.GetAllAsync();

        var unitDefs = allUnits.ToDictionary(u => u.UId, u => u.UnitName);
        var weaponDefs = allWeapons.ToDictionary(w => w.WeaponId, w => w.Name);
        var trinketDefs = allTrinkets.ToDictionary(t => t.TrinketId, t => t.Name);
        var skillDefs = allSkills.ToDictionary(s => s.SkillId, s => $"Skill {s.SkillId}");

        var weaponBySkill = allWeapons
            .Where(w => w.SkillId > 0)
            .GroupBy(w => w.SkillId)
            .ToDictionary(g => g.Key, g => g.First().WeaponId);

        var trinketBySkill = allTrinkets
            .Where(t => t.SkillId > 0)
            .GroupBy(t => t.SkillId)
            .ToDictionary(g => g.Key, g => g.First().TrinketId);

        // 1. Explicit telemetry records (e.g. POST /api/analytics/telemetry & self-test)
        var telemetryMatches = await _telemetryRepo.GetAllByFilterAsync(t =>
            (string.IsNullOrEmpty(filter.PatchVersion) || t.PatchVersion == filter.PatchVersion) &&
            (string.IsNullOrEmpty(filter.Mode) || t.Mode.ToLower() == filter.Mode.ToLower()) &&
            (!filter.FromDate.HasValue || t.CreatedAt >= filter.FromDate.Value) &&
            (!filter.ToDate.HasValue || t.CreatedAt <= filter.ToDate.Value));

        var matches = new List<MatchUnitTelemetry>(telemetryMatches);
        var recordedMatchIds = new HashSet<string>(
            telemetryMatches.Where(t => !string.IsNullOrWhiteSpace(t.MatchId)).Select(t => t.MatchId),
            StringComparer.OrdinalIgnoreCase);

        // 2. Real completed PvP/Ranked/Friendly matches stored in MatchHistory (Phase 1)
        bool includeCurrentPatchHistory =
            string.IsNullOrWhiteSpace(filter.PatchVersion) ||
            string.Equals(filter.PatchVersion.Trim(), currentPatch, StringComparison.OrdinalIgnoreCase);

        if (includeCurrentPatchHistory)
        {
            var historyMatches = await _historyService.GetAllAsync();
            foreach (var h in historyMatches)
            {
                if (h == null ||
                    !string.Equals(h.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h.Mode, "story", StringComparison.OrdinalIgnoreCase) ||
                    h.Result?.AfterMatchTeamData == null ||
                    h.Result.AfterMatchTeamData.Count == 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(h.MatchId) && recordedMatchIds.Contains(h.MatchId))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(filter.Mode) &&
                    !string.Equals(h.Mode, filter.Mode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var matchTime = h.CompletedAt != default ? h.CompletedAt : h.CreatedAt;
                if (filter.FromDate.HasValue && matchTime < filter.FromDate.Value) continue;
                if (filter.ToDate.HasValue && matchTime > filter.ToDate.Value) continue;

                var converted = ConvertHistoryToTelemetry(h, currentPatch, weaponBySkill, trinketBySkill);
                if (converted != null && converted.Units.Count > 0)
                {
                    matches.Add(converted);
                    if (!string.IsNullOrWhiteSpace(h.MatchId))
                    {
                        recordedMatchIds.Add(h.MatchId);
                    }
                }
            }
        }

        var availablePatches = matches
            .Select(m => m.PatchVersion)
            .Where(p => !string.IsNullOrWhiteSpace(p) && !p.StartsWith("test_patch_", StringComparison.OrdinalIgnoreCase))
            .Append(currentPatch)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p)
            .ToList();

        int totalMatches = matches.Count;

        var response = new BalanceAnalyticsResponseDto
        {
            PatchVersion = filter.PatchVersion,
            AvailablePatchVersions = availablePatches,
            Mode = filter.Mode,
            TotalMatches = totalMatches
        };

        if (totalMatches == 0)
        {
            return response;
        }

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

    private static MatchUnitTelemetry? ConvertHistoryToTelemetry(
        MatchHistory history,
        string patchVersion,
        Dictionary<int, int> weaponBySkill,
        Dictionary<int, int> trinketBySkill)
    {
        if (history.Result?.AfterMatchTeamData == null || history.Result.AfterMatchTeamData.Count == 0)
        {
            return null;
        }

        var unitItems = new List<UnitTelemetryItem>();
        foreach (var team in history.Result.AfterMatchTeamData)
        {
            if (team == null) continue;

            var loadoutByUnitId = (team.Loadout ?? [])
                .GroupBy(l => l.UId)
                .ToDictionary(g => g.Key, g => g.First());

            if (team.Units != null && team.Units.Count > 0)
            {
                foreach (var u in team.Units)
                {
                    loadoutByUnitId.TryGetValue(u.UId, out var loadout);
                    int weaponId = loadout != null && loadout.WeaponSkillId > 0 &&
                                   weaponBySkill.TryGetValue(loadout.WeaponSkillId, out int wId)
                        ? wId
                        : 0;
                    int trinketId = loadout != null && loadout.TrinketSkillId > 0 &&
                                    trinketBySkill.TryGetValue(loadout.TrinketSkillId, out int tId)
                        ? tId
                        : 0;

                    unitItems.Add(new UnitTelemetryItem
                    {
                        PlayerId = team.PlayerId,
                        UnitUId = u.UId,
                        WeaponId = weaponId,
                        TrinketId = trinketId,
                        SkillIds = CollectSkillIds(loadout, u.SkillUses),
                        Survived = u.Alive,
                        DamageDealt = u.DamageDealt,
                        DamageTaken = u.DamageTaken,
                        HealingDone = u.HealingDone,
                        Kills = u.Kills
                    });
                }
            }
            else if (team.Loadout != null && team.Loadout.Count > 0)
            {
                foreach (var loadout in team.Loadout)
                {
                    int weaponId = loadout.WeaponSkillId > 0 &&
                                   weaponBySkill.TryGetValue(loadout.WeaponSkillId, out int wId)
                        ? wId
                        : 0;
                    int trinketId = loadout.TrinketSkillId > 0 &&
                                    trinketBySkill.TryGetValue(loadout.TrinketSkillId, out int tId)
                        ? tId
                        : 0;

                    unitItems.Add(new UnitTelemetryItem
                    {
                        PlayerId = team.PlayerId,
                        UnitUId = loadout.UId,
                        WeaponId = weaponId,
                        TrinketId = trinketId,
                        SkillIds = CollectSkillIds(loadout, null),
                        Survived = team.UnitsAlive > 0,
                        DamageDealt = 0,
                        DamageTaken = 0,
                        HealingDone = 0,
                        Kills = 0
                    });
                }
            }
        }

        if (unitItems.Count == 0) return null;

        return new MatchUnitTelemetry
        {
            MatchId = history.MatchId,
            MatchHistoryId = history.Id,
            PatchVersion = patchVersion,
            Mode = string.IsNullOrWhiteSpace(history.Mode) ? "pvp" : history.Mode.Trim().ToLowerInvariant(),
            WinnerId = history.Result.WinnerId ?? string.Empty,
            CreatedAt = history.CompletedAt != default ? history.CompletedAt : history.CreatedAt,
            Units = unitItems
        };
    }

    private static List<int> CollectSkillIds(LoadoutUnitData? loadout, List<SkillUseData>? skillUses)
    {
        var ids = new HashSet<int>();
        if (loadout != null)
        {
            if (loadout.PassiveSkillId > 0) ids.Add(loadout.PassiveSkillId);
            if (loadout.MovementSkillId > 0) ids.Add(loadout.MovementSkillId);
            if (loadout.WeaponSkillId > 0) ids.Add(loadout.WeaponSkillId);
            if (loadout.ClassSkillId > 0) ids.Add(loadout.ClassSkillId);
            if (loadout.TrinketSkillId > 0) ids.Add(loadout.TrinketSkillId);
        }
        if (skillUses != null)
        {
            foreach (var usage in skillUses)
            {
                if (usage.SkillId > 0) ids.Add(usage.SkillId);
            }
        }
        return ids.ToList();
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
