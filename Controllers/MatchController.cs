using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GameInventoryApi.Models;
using Microsoft.Extensions.Options;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
    private readonly IMatchSessionService _matchService;
    private readonly ITeamLoadoutService _loadoutService;
    private readonly IMatchHistoryService _historyService;
    private readonly IMatchQueueService _queueService;
    private readonly MatchmakingService _matchmakingService;

    public MatchController(IMatchSessionService matchService, ITeamLoadoutService loadoutService, IMatchHistoryService historyService, IMatchQueueService queueService, MatchmakingService matchmakingService)
    {
        _matchService = matchService;
        _loadoutService = loadoutService;
        _historyService = historyService;
        _queueService = queueService;
        _matchmakingService = matchmakingService;
    }

    // Called by client to get their current active match
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
    public async Task<ActionResult> GetMatchLoadouts(string matchId)
    {
        var match = await _matchService.GetByMatchIdAsync(matchId);
        if (match == null) return NotFound();

        var loadout1 = await _loadoutService.GetByPlayerIdAsync(match.Player1Id);
        var loadout2 = await _loadoutService.GetByPlayerIdAsync(match.Player2Id);

        return Ok(new MatchLoadoutsResponseDto(
            match.Player1Id,
            match.Player2Id,
            loadout1 != null ? ToLoadoutDto(loadout1) : null,
            loadout2 != null ? ToLoadoutDto(loadout2) : null
        ));
    }

    private TeamLoadoutDto ToLoadoutDto(TeamLoadoutDocument doc) => new TeamLoadoutDto(
        doc.PlayerId,
        doc.Units.Select(u => new UnitLoadoutEntryDto(
            u.UId, u.MovementSkillId, u.WeaponSkillId, u.ClassSkillId, u.EquipmentSkillId
        )).ToList()
    );

    [HttpGet("{matchId}/info")]
    public async Task<ActionResult<MatchInfoDto>> GetMatchInfo(string matchId)
    {
        var match = await _matchService.GetByMatchIdAsync(matchId);
        if (match == null) return NotFound();

        return Ok(new MatchInfoDto(match.MatchId, match.Mode, match.MapId));
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
    public async Task<ActionResult> UpdateMatchStatus(string matchId, [FromBody] MatchStatusUpdateDto dto)
    {
        var match = await _matchService.GetByMatchIdAsync(matchId);
        if (match == null) return NotFound();

        await _matchService.UpdateStatusAsync(matchId, dto.Status, dto.Result);

        if (dto.Status == "cancelled" || dto.Status == "completed")
        {
            // Archive to history
            match.Status = dto.Status;
            match.Result = dto.Result;
            await _historyService.ArchiveAsync(match);

            // Delete from active matches
            await _matchService.DeleteAsync(matchId);
            // Clean up queue entries for both players
            await _queueService.RemoveByPlayerIdAsync(match.Player1Id);
            await _queueService.RemoveByPlayerIdAsync(match.Player2Id);

            // Kill process
            if (match.ProcessId > 0)
            {
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(match.ProcessId);
                    process.Kill();
                    // inject MatchmakingService and call:
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