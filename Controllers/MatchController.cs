using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
    private readonly IMatchSessionService _matchService;
    private readonly ITeamLoadoutService _loadoutService;
    private readonly IPlayerProfileService _profileService;
    private readonly IMatchHistoryService _historyService;
    private readonly IMatchQueueService _queueService;
    private readonly MatchmakingService _matchmakingService;
    private readonly IActionService _actionService;
    private readonly IUserPresenceTracker _presenceTracker;
    private readonly ServerConfig _serverConfig;

    public MatchController(
        IMatchSessionService matchService,
        ITeamLoadoutService loadoutService,
        IPlayerProfileService profileService,
        IMatchHistoryService historyService,
        IMatchQueueService queueService,
        MatchmakingService matchmakingService,
        IActionService actionService,
        IUserPresenceTracker presenceTracker,
        IOptions<ServerConfig> serverConfig)
    {
        _matchService = matchService;
        _loadoutService = loadoutService;
        _profileService = profileService;
        _historyService = historyService;
        _queueService = queueService;
        _matchmakingService = matchmakingService;
        _actionService = actionService;
        _presenceTracker = presenceTracker;
        _serverConfig = serverConfig.Value;
    }

    [HttpGet("current")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<MatchSessionJoinInfoDto>> GetCurrentMatch()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var match = await _matchService.GetActiveMatchByPlayerIdAsync(playerId);
        if (match == null) return NotFound(new { message = "no_ongoing_match" });

        var p1 = await _profileService.GetByFilterAsync(p => p.PlayerId == match.Player1Id);
        var p2 = await _profileService.GetByFilterAsync(p => p.PlayerId == match.Player2Id);

        return Ok(new MatchSessionJoinInfoDto(
            match.MatchId,
            match.ServerIp,
            match.ServerPort,
            match.Mode,
            p1?.Username ?? match.Player1Id,
            p2?.Username ?? match.Player2Id
        ));
    }

    [HttpGet("{matchId}/loadouts")]
    [Authorize(Roles = "Server")]
    public async Task<ActionResult<MatchLoadoutDataDto>> GetMatchLoadouts(string matchId)
    {
        var match = await _matchService.GetByMatchIdAsync(matchId);
        if (match == null) return NotFound();

        var loadout1 = await _loadoutService.GetByPlayerIdAsync(match.Player1Id);
        var loadout2 = await _loadoutService.GetByPlayerIdAsync(match.Player2Id);

        var data1 = await _profileService.GetLoadoutDataAsync(match.Player1Id, loadout1?.UIds ?? []);
        var data2 = await _profileService.GetLoadoutDataAsync(match.Player2Id, loadout2?.UIds ?? []);

        return Ok(new MatchLoadoutDataDto(
            data1 ?? new PlayerLoadoutDataDto(match.Player1Id, []),
            data2 ?? new PlayerLoadoutDataDto(match.Player2Id, [])
        ));
    }

    [HttpGet("{matchId}/info")]
    public async Task<ActionResult<MatchInfoDto>> GetMatchInfo(string matchId)
    {
        var match = await _matchService.GetByMatchIdAsync(matchId);
        if (match == null) return NotFound();

        return Ok(new MatchInfoDto(match.MatchId, match.Mode, match.MapId));
    }

    [HttpGet("history/{matchId}")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<MatchHistoryDto>> GetMatchHistoryById(string matchId)
    {
        var history = await _historyService.GetByMatchIdAsync(matchId);
        if (history == null) return NotFound();

        return Ok(new MatchHistoryDto(
            history.MatchId,
            history.Player1Id,
            history.Player2Id,
            history.Status,
            history.Result != null
                ? new MatchResultDataDto(
                    history.Result.WinnerId,
                    history.Result.DurationSeconds,
                    history.Result.TotalInstants,
                    [..history.Result.AfterMatchTeamData
                        .Select(p => new AfterMatchPlayerDataDto(p.PlayerId, p.UnitsAlive, p.TotalUnits))])
                : null,
            history.CompletedAt
        ));
    }

    [HttpGet("history")]
    [Authorize(Roles = "Player,Admin")]
    public async Task<ActionResult<List<MatchSessionDto>>> GetMatchHistory()
    {
        if (User.IsInRole("Admin"))
        {
            var activeMatches = await _matchService.GetAllAsync();
            var historyMatches = await _historyService.GetAllAsync();

            var list = new List<MatchSessionDto>();
            if (activeMatches != null)
            {
                list.AddRange(activeMatches.Select(m => new MatchSessionDto(
                    m.MatchId, m.Player1Id, m.Player2Id, m.ServerIp, m.ServerPort, m.Status, m.Mode
                )));
            }
            if (historyMatches != null)
            {
                list.AddRange(historyMatches.Select(m => new MatchSessionDto(
                    m.MatchId, m.Player1Id, m.Player2Id, m.ServerIp, m.ServerPort, m.Status, m.Mode
                )));
            }
            return Ok(list);
        }

        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var matches = await _historyService.GetByPlayerIdAsync(playerId);
        return Ok(matches.Select(m => new MatchSessionDto(
            m.MatchId, m.Player1Id, m.Player2Id, m.ServerIp, m.ServerPort, m.Status, m.Mode
        )).ToList());
    }

    [HttpGet("log-files")]
    [Authorize(Roles = "Admin,Server")]
    public ActionResult<List<MatchLogFileInfo>> GetAvailableLogFiles()
    {
        var localPath = _serverConfig.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
        {
            return Ok(new List<MatchLogFileInfo>());
        }

        try
        {
            var dir = new DirectoryInfo(localPath);
            var files = dir.GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(50)
                .Select(f => new MatchLogFileInfo(
                    MatchId: Path.GetFileNameWithoutExtension(f.Name),
                    FileName: f.Name,
                    FileSizeBytes: f.Length,
                    LastModifiedUtc: f.LastWriteTimeUtc,
                    CreatedAtUtc: f.CreationTimeUtc
                ))
                .ToList();

            return Ok(files);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = $"Error listing log files: {ex.Message}" });
        }
    }

    [HttpGet("{matchId}/logs")]
    [Authorize(Roles = "Admin,Server")]
    public async Task<ActionResult<MatchLogResponseDto>> GetMatchLogs(string matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId))
        {
            return BadRequest(new { message = "matchId is required." });
        }

        var cleanMatchId = matchId.Trim();
        // Prevent Path Traversal
        if (cleanMatchId.Contains("..") || cleanMatchId.Contains('/') || cleanMatchId.Contains('\\') ||
            cleanMatchId.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
        {
            return BadRequest(new { message = "Invalid matchId character format." });
        }

        var localPath = _serverConfig.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "ServerConfig.LocalPath is not configured on server." });
        }

        var fileName = $"{cleanMatchId}.log";
        var fullPath = Path.GetFullPath(Path.Combine(localPath, fileName));
        var canonicalBasePath = Path.GetFullPath(localPath);

        if (!fullPath.StartsWith(canonicalBasePath, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Path traversal detected." });
        }

        if (!System.IO.File.Exists(fullPath))
        {
            var session = await _matchService.GetByMatchIdAsync(cleanMatchId);
            var history = session == null ? await _historyService.GetByMatchIdAsync(cleanMatchId) : null;
            var currentStatus = session?.Status ?? history?.Status ?? "not_found";

            return NotFound(new {
                message = $"No server log file found for match '{cleanMatchId}'.",
                matchId = cleanMatchId,
                status = currentStatus,
                logPath = fileName
            });
        }

        try
        {
            string content;
            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                content = await sr.ReadToEndAsync();
            }

            var totalLines = content.Split('\n').Length;
            var session = await _matchService.GetByMatchIdAsync(cleanMatchId);
            var history = session == null ? await _historyService.GetByMatchIdAsync(cleanMatchId) : null;
            var status = session?.Status ?? history?.Status ?? "active";

            return Ok(new MatchLogResponseDto(
                MatchId: cleanMatchId,
                Logs: content,
                LogPath: fileName,
                TotalLines: totalLines,
                Status: status,
                RetrievedAt: DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = $"Error reading log file: {ex.Message}" });
        }
    }

    [HttpPatch("{matchId}/status")]
    [Authorize(Roles = "Server")]
    public async Task<ActionResult> UpdateMatchStatus(string matchId, [FromBody] MatchStatusUpdateDto dto)
    {
        var match = await _matchService.GetByMatchIdAsync(matchId);
        if (match == null) return NotFound();

        await _matchService.UpdateStatusAsync(matchId, dto.Status, dto.Result);

        if (dto.Status == "cancelled" || dto.Status == "completed")
        {
            match.Status = dto.Status;
            match.Result = dto.Result;

            string? player1Id = null, player2Id = null;
            Dictionary<string, object>? facts1 = null, facts2 = null;

            if (dto.Status == "completed" && dto.Result?.AfterMatchTeamData.Count == 2)
            {
                var p1Data = dto.Result.AfterMatchTeamData.FirstOrDefault(p => p.PlayerId == match.Player1Id);
                var p2Data = dto.Result.AfterMatchTeamData.FirstOrDefault(p => p.PlayerId == match.Player2Id);

                if (p1Data != null && p2Data != null)
                {
                    int p1UnitsDefeated = p2Data.TotalUnits - p2Data.UnitsAlive;
                    int p2UnitsDefeated = p1Data.TotalUnits - p1Data.UnitsAlive;

                    // Only award gems in standard pvp mode
                    if (match.Mode == "pvp")
                    {
                        int p1Gems = (p1UnitsDefeated + p1Data.UnitsAlive) * 10;
                        int p2Gems = (p2UnitsDefeated + p2Data.UnitsAlive) * 10;
                        await _profileService.AddGemsAsync(match.Player1Id, p1Gems);
                        await _profileService.AddGemsAsync(match.Player2Id, p2Gems);
                    }

                    player1Id = match.Player1Id;
                    player2Id = match.Player2Id;
                    facts1 = new Dictionary<string, object>
                    {
                        ["won"] = dto.Result.WinnerId == match.Player1Id,
                        ["unitsAlive"] = p1Data.UnitsAlive,
                        ["totalUnits"] = p1Data.TotalUnits,
                        ["unitsDefeated"] = p1UnitsDefeated,
                        ["durationSeconds"] = dto.Result.DurationSeconds,
                        ["instants"] = dto.Result.TotalInstants,
                    };
                    facts2 = new Dictionary<string, object>
                    {
                        ["won"] = dto.Result.WinnerId == match.Player2Id,
                        ["unitsAlive"] = p2Data.UnitsAlive,
                        ["totalUnits"] = p2Data.TotalUnits,
                        ["unitsDefeated"] = p2UnitsDefeated,
                        ["durationSeconds"] = dto.Result.DurationSeconds,
                        ["instants"] = dto.Result.TotalInstants,
                    };
                }
            }

            var history = await _historyService.ArchiveAsync(match);

            // Only log quest/reward actions in standard pvp mode
            if (match.Mode == "pvp" && player1Id != null && facts1 != null && facts2 != null)
            {
                await _actionService.LogActionAsync(player1Id, "MatchResult", history.Id, facts1);
                await _actionService.LogActionAsync(player2Id!, "MatchResult", history.Id, facts2);
            }
            _presenceTracker.SetInMatch(match.Player1Id, false);
            _presenceTracker.SetInMatch(match.Player2Id, false);
            await _matchService.DeleteAsync(matchId);
            await _queueService.RemoveByPlayerIdAsync(match.Player1Id);
            await _queueService.RemoveByPlayerIdAsync(match.Player2Id);

            if (match.ProcessId > 0)
            {
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(match.ProcessId);
                    process.Kill();
                    _matchmakingService.ReleasePort(match.ServerPort);
                    Console.WriteLine($"[Match] Killed process {match.ProcessId} for match {matchId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Match] Failed to kill process {match.ProcessId}: {ex.Message}");
                }
            }
        }

        return Ok();
    }
}
