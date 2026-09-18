using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class PlayerProfileController : ControllerBase
{
    private readonly IPlayerProfileService _service;

    public PlayerProfileController(IPlayerProfileService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PlayerProfile>> GetMyProfile()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var profile = await _service.GetByFilterAsync(p => p.PlayerId == playerId);

        if (profile == null)
        {
            profile = new PlayerProfile
            {
                PlayerId = playerId,
                Username = User.Identity?.Name ?? "Player",
                Level = 1,
                Experience = 0,
                LastLogin = DateTime.UtcNow
            };
            await _service.CreateAsync(playerId, profile);
        }

        return Ok(profile);
    }

    // GET /api/playerprofile/units
    [HttpGet("units")]
    public async Task<ActionResult<List<UnitConfigDto>>> GetMyUnits()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var units = await _service.GetAllUnitConfigsAsync(playerId);
        return Ok(units);
    }

    // GET /api/playerprofile/unit/{ownedUnitId}
    [HttpGet("unit/{ownedUnitId}")]
    public async Task<ActionResult<UnitConfigDto>> GetMyUnit(string ownedUnitId)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var unit = await _service.GetUnitConfigAsync(playerId, ownedUnitId);
        if (unit == null) return NotFound();
        return Ok(unit);
    }

    // GET /api/playerprofile/weapons
    [HttpGet("weapons")]
    public async Task<ActionResult<List<OwnedWeaponResultDto>>> GetMyWeapons()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var result = await _service.GetAllOwnedWeaponsAsync(playerId);
        return Ok(result);
    }

    // GET /api/playerprofile/trinkets
    [HttpGet("trinkets")]
    public async Task<ActionResult<List<OwnedTrinketResultDto>>> GetMyTrinkets()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var result = await _service.GetAllOwnedTrinketsAsync(playerId);
        return Ok(result);
    }

    // PATCH /api/playerprofile/unit/{ownedUnitId}/movement-skill
    [HttpPatch("unit/{ownedUnitId}/movement-skill")]
    public async Task<ActionResult> UpdateMovementSkill(string ownedUnitId, [FromBody] UpdateSkillDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (success, error) = await _service.UpdateUnitMovementSkillAsync(playerId, ownedUnitId, dto.SkillId);
        return success ? NoContent() : BadRequest(error);
    }

    // PATCH /api/playerprofile/unit/{ownedUnitId}/class-skill
    [HttpPatch("unit/{ownedUnitId}/class-skill")]
    public async Task<ActionResult> UpdateClassSkill(string ownedUnitId, [FromBody] UpdateSkillDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (success, error) = await _service.UpdateUnitClassSkillAsync(playerId, ownedUnitId, dto.SkillId);
        return success ? NoContent() : BadRequest(error);
    }

    // PATCH /api/playerprofile/unit/{ownedUnitId}/weapon
    [HttpPatch("unit/{ownedUnitId}/weapon")]
    public async Task<ActionResult> UpdateWeapon(string ownedUnitId, [FromBody] UpdateUnitWeaponDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (success, error) = await _service.UpdateUnitWeaponAsync(playerId, ownedUnitId, dto.OwnedWeaponId);
        return success ? NoContent() : BadRequest(error);
    }

    // PATCH /api/playerprofile/unit/{ownedUnitId}/trinket
    [HttpPatch("unit/{ownedUnitId}/trinket")]
    public async Task<ActionResult> UpdateTrinket(string ownedUnitId, [FromBody] UpdateUnitTrinketDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (success, error) = await _service.UpdateUnitTrinketAsync(playerId, ownedUnitId, dto.OwnedTrinketId);
        return success ? NoContent() : BadRequest(error);
    }

    // POST /api/playerprofile/weapons/by-class
    [HttpPost("weapons/by-class")]
    public async Task<ActionResult<List<OwnedWeaponResultDto>>> GetWeaponsByClass([FromBody] ClassIdFilterDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var result = await _service.GetOwnedWeaponsByClassIdsAsync(playerId, dto.ClassIds);
        return Ok(result);
    }
}
