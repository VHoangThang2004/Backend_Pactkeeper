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
            // Auto-create default profile on first fetch
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
    [HttpPut]
    public async Task<ActionResult> UpdateMyProfile([FromBody] PlayerProfile profile)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null || profile.PlayerId != playerId) return Forbid();

        await _service.UpdateAsync(profile.Id, profile);
        return NoContent();
    }
}