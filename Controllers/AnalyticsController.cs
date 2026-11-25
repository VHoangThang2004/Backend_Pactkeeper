using System.Security.Claims;
using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IBalanceAnalyticsService _analyticsService;

    public AnalyticsController(IBalanceAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    /// <summary>
    /// GET /api/analytics/balance
    /// Calculates Total Matches, Pick Count, Pick Rate, Win Rate, Survival Rate, Avg Damage
    /// grouped by UnitUId, WeaponId, TrinketId, and SkillId.
    /// Filterable by patchVersion, mode, and fromDate / toDate.
    /// </summary>
    [HttpGet("balance")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<BalanceAnalyticsResponseDto>> GetBalance(
        [FromQuery] string? patchVersion,
        [FromQuery] string? mode,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        var filter = new BalanceAnalyticsFilterDto
        {
            PatchVersion = patchVersion,
            Mode = mode,
            FromDate = fromDate,
            ToDate = toDate
        };

        var result = await _analyticsService.GetBalanceMetricsAsync(filter);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/analytics/ai-suggest
    /// Identifies balance anomalies (win rate > 60% or < 40%) and queries LLM for suggestions.
    /// Suggestions specify changeType (DB_ONLY vs REBUILD_REQUIRED).
    /// Results are persisted to avoid wasting tokens on the same patch unless forceRegenerate=true.
    /// </summary>
    [HttpPost("ai-suggest")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<BalanceAiSuggestion>> GetAiSuggestions([FromBody] AiSuggestRequestDto dto)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("PlayerId")?.Value
            ?? "admin";

        var adminUsername = User.Identity?.Name
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("unique_name")?.Value
            ?? "Admin";

        var suggestion = await _analyticsService.GetOrGenerateAiSuggestionAsync(dto, adminId, adminUsername);
        return Ok(suggestion);
    }

    /// <summary>
    /// GET /api/analytics/ai-suggest
    /// Lists saved AI balance suggestions so all admins can view past recommendations.
    /// </summary>
    [HttpGet("ai-suggest")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<List<BalanceAiSuggestion>>> GetSavedAiSuggestions([FromQuery] string? patchVersion)
    {
        var suggestions = await _analyticsService.GetSavedAiSuggestionsAsync(patchVersion);
        return Ok(suggestions);
    }

    /// <summary>
    /// POST /api/analytics/telemetry
    /// Telemetry ingestion endpoint for game servers and telemetry collectors.
    /// </summary>
    [HttpPost("telemetry")]
    [Authorize(Roles = "Server,Admin,Moderator")]
    public async Task<ActionResult<MatchUnitTelemetry>> IngestTelemetry([FromBody] MatchUnitTelemetry telemetry)
    {
        if (telemetry == null || telemetry.Units == null || telemetry.Units.Count == 0)
        {
            return BadRequest(new { message = "telemetry_units_empty" });
        }

        var saved = await _analyticsService.RecordTelemetryAsync(telemetry);
        return Ok(saved);
    }

    /// <summary>
    /// GET /api/analytics/self-test
    /// Self-verification endpoint to validate formula calculations, outlier thresholds, and token-saving caching.
    /// </summary>
    [HttpGet("self-test")]
    [AllowAnonymous]
    public async Task<IActionResult> SelfTest([FromServices] IGameVersionService versionService)
    {
        var logs = new List<string>();
        try
        {
            // Seed a self-test telemetry batch with known values
            string testPatch = $"test_patch_{Guid.NewGuid():N}";
            for (int i = 1; i <= 10; i++)
            {
                string winner = i <= 6 ? "player1" : "player2";
                var t = new MatchUnitTelemetry
                {
                    MatchId = $"test_m_{i}",
                    MatchHistoryId = $"test_h_{i}",
                    PatchVersion = testPatch,
                    Mode = "pvp",
                    WinnerId = winner,
                    CreatedAt = DateTime.UtcNow,
                    Units = new List<UnitTelemetryItem>
                    {
                        new() { PlayerId = "player1", UnitUId = 1, WeaponId = 1, TrinketId = 1, SkillIds = [1001], Survived = (i <= 5), DamageDealt = 120 },
                        new() { PlayerId = "player2", UnitUId = 2, WeaponId = 2, TrinketId = 2, SkillIds = [1002], Survived = (i > 8), DamageDealt = 40 }
                    }
                };
                await _analyticsService.RecordTelemetryAsync(t);
            }
            logs.Add("Seeded 10 test match telemetry records.");

            // 1. Verify GET calculation
            var filter = new BalanceAnalyticsFilterDto { PatchVersion = testPatch, Mode = "pvp" };
            var metrics = await _analyticsService.GetBalanceMetricsAsync(filter);

            if (metrics.TotalMatches != 10) throw new Exception($"TotalMatches expected 10, got {metrics.TotalMatches}");
            logs.Add($"TotalMatches: {metrics.TotalMatches} (PASS)");

            var u1 = metrics.Units.FirstOrDefault(u => u.Id == 1);
            if (u1 == null || u1.PickCount != 10 || u1.WinRate != 0.6 || !u1.IsOutlier)
                throw new Exception($"Unit 1 metrics mismatch: PickCount={u1?.PickCount}, WinRate={u1?.WinRate}, IsOutlier={u1?.IsOutlier}");
            logs.Add($"Unit 1 (WinRate={u1.WinRate:P0}, PickCount={u1.PickCount}, IsOutlier={u1.IsOutlier}) (PASS)");

            var u2 = metrics.Units.FirstOrDefault(u => u.Id == 2);
            if (u2 == null || u2.PickCount != 10 || u2.WinRate != 0.4 || !u2.IsOutlier)
                throw new Exception($"Unit 2 metrics mismatch: PickCount={u2?.PickCount}, WinRate={u2?.WinRate}, IsOutlier={u2?.IsOutlier}");
            logs.Add($"Unit 2 (WinRate={u2.WinRate:P0}, PickCount={u2.PickCount}, IsOutlier={u2.IsOutlier}) (PASS)");

            // 2. Verify AI Suggest generation and changeType
            var req = new AiSuggestRequestDto { PatchVersion = testPatch, Mode = "pvp" };
            var sug1 = await _analyticsService.GetOrGenerateAiSuggestionAsync(req, "test_admin", "TestAdmin");
            if (sug1.Recommendations.Count == 0) throw new Exception("Expected AI suggestions to be generated.");
            foreach (var rec in sug1.Recommendations)
            {
                if (rec.ChangeType != "DB_ONLY" && rec.ChangeType != "REBUILD_REQUIRED")
                    throw new Exception($"Invalid changeType '{rec.ChangeType}'. Must be DB_ONLY or REBUILD_REQUIRED.");
            }
            logs.Add($"AI Suggestion generated {sug1.Recommendations.Count} recommendations with valid changeType (PASS)");

            // 3. Verify token saving (caching)
            var sug2 = await _analyticsService.GetOrGenerateAiSuggestionAsync(req, "test_admin", "TestAdmin");
            if (sug2.Id != sug1.Id) throw new Exception("Token-saving cache failed! Created new suggestion instead of returning existing one.");
            logs.Add("Token-saving cache returned existing suggestion record (PASS)");

            // 4. Verify Game Version retrieval and update
            var initialVer = await versionService.GetVersionConfigAsync();
            logs.Add($"Initial GameVersion retrieved: {initialVer.LatestVersion} (PASS)");

            var updatedVer = await versionService.UpdateVersionConfigAsync(new UpdateGameVersionDto
            {
                LatestVersion = "1.0.1-test",
                MinSupportedVersion = "1.0.0",
                DownloadUrl = "https://example.com/client.zip",
                PatchNotes = "Self-test patch notes."
            }, "SelfTestAdmin");

            if (updatedVer.LatestVersion != "1.0.1-test") throw new Exception("Version update mismatch");
            var fetchedVer = await versionService.GetVersionConfigAsync();
            if (fetchedVer.LatestVersion != "1.0.1-test") throw new Exception("Fetched updated version mismatch");
            logs.Add($"GameVersion updated and verified: {fetchedVer.LatestVersion} (PASS)");

            return Ok(new { success = true, logs });
        }
        catch (Exception ex)
        {
            logs.Add($"FAIL: {ex.Message}");
            return StatusCode(500, new { success = false, error = ex.Message, logs });
        }
    }
}
