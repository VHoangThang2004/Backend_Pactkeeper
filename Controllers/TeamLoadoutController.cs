using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class TeamLoadoutController : ControllerBase
{
    private readonly ITeamLoadoutService _service;

    public TeamLoadoutController(ITeamLoadoutService service)
        => _service = service;

    [HttpGet]
    public async Task<ActionResult<TeamLoadoutDto>> GetMyLoadout()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var loadout = await _service.GetByPlayerIdAsync(playerId);
        if (loadout == null) return NotFound();

        return Ok(ToDto(loadout));
    }

    [HttpPost]
    public async Task<ActionResult> SaveMyLoadout([FromBody] TeamLoadoutDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var document = new TeamLoadoutDocument
        {
            PlayerId = playerId,
            Units = dto.Units.Select(u => new UnitLoadoutEntry
            {
                UId = u.UId,
                MovementSkillId = u.MovementSkillId,
                WeaponSkillId = u.WeaponSkillId,
                ClassSkillId = u.ClassSkillId,
                EquipmentSkillId = u.EquipmentSkillId
            }).ToList()
        };

        await _service.SaveAsync(playerId, document);
        return Ok();
    }

    private TeamLoadoutDto ToDto(TeamLoadoutDocument doc) => new TeamLoadoutDto(
        PlayerId: doc.PlayerId,
        Units: doc.Units.Select(u => new UnitLoadoutEntryDto(
            u.UId, u.MovementSkillId, u.WeaponSkillId, u.ClassSkillId, u.EquipmentSkillId
        )).ToList()
    );
}