using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GameInventoryApi.Models;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
    private readonly IMatchSessionService _matchService;
    private readonly ITeamLoadoutService _loadoutService;

    public MatchController(IMatchSessionService matchService, ITeamLoadoutService loadoutService)
    {
        _matchService = matchService;
        _loadoutService = loadoutService;
    }

    // Called by backend/admin to start a match
    [HttpPost("start")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<MatchSessionDto>> StartMatch([FromBody] StartMatchRequestDto dto)
    {
        // For now: static server IP and next available port
        // Later: spawn Unity server process here
        string serverIp = "127.0.0.1";
        int port = 7777;

        var match = await _matchService.CreateMatchAsync(dto.Player1Id, dto.Player2Id, serverIp, port);

        return Ok(new MatchSessionDto(
            match.MatchId,
            match.Player1Id,
            match.Player2Id,
            match.ServerIp,
            match.ServerPort,
            match.Status
        ));
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
}