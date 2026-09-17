using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class TeamLoadoutController : ControllerBase
{
    private readonly ITeamLoadoutService _loadoutService;
    private readonly IPlayerProfileService _profileService;

    public TeamLoadoutController(ITeamLoadoutService loadoutService, IPlayerProfileService profileService)
    {
        _loadoutService = loadoutService;
        _profileService = profileService;
    }

    [HttpGet]
    public async Task<ActionResult<List<int>>> GetMyLoadout()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var loadout = await _loadoutService.GetByPlayerIdAsync(playerId);
        return Ok(loadout?.UIds ?? []);
    }

    [HttpPut]
    public async Task<ActionResult> SaveMyLoadout([FromBody] SaveLoadoutDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var profile = await _profileService.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile == null) return NotFound("Profile not found.");

        var ownedUIds = profile.OwnedUnits.Select(u => u.UnitDefinitionUId).ToHashSet();
        var invalid = dto.UIds.Where(id => !ownedUIds.Contains(id)).ToList();
        if (invalid.Count > 0)
            return BadRequest($"Units not owned: {string.Join(", ", invalid)}");

        await _loadoutService.SaveAsync(playerId, dto.UIds);
        return NoContent();
    }
}
