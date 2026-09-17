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
