using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public MatchController(
        IMatchSessionService matchService,
        ITeamLoadoutService loadoutService,
        IPlayerProfileService profileService,
        IMatchHistoryService historyService,
        IMatchQueueService queueService,
        MatchmakingService matchmakingService)
    {
        _matchService = matchService;
        _loadoutService = loadoutService;
        _profileService = profileService;
        _historyService = historyService;
        _queueService = queueService;
        _matchmakingService = matchmakingService;
    }

    [HttpGet("current")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<MatchSessionJoinInfoDto>> GetCurrentMatch()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var match = await _matchService.GetActiveMatchByPlayerIdAsync(playerId);
        if (match == null) return NotFound();

        return Ok(new MatchSessionJoinInfoDto(match.MatchId, match.ServerIp, match.ServerPort));
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
                ? new MatchResultDataDto(history.Result.WinnerId, history.Result.DurationSeconds, history.Result.TotalInstants)
                : null,
            history.CompletedAt
        ));
    }

    [HttpGet("history")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<List<MatchSessionDto>>> GetMatchHistory()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var matches = await _historyService.GetByPlayerIdAsync(playerId);
        return Ok(matches.Select(m => new MatchSessionDto(
            m.MatchId, m.Player1Id, m.Player2Id, m.ServerIp, m.ServerPort, m.Status, m.Mode
        )).ToList());
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
            await _historyService.ArchiveAsync(match);
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
