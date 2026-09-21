using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/gacha")]
[Authorize(Roles = "Player")]
public class GachaController(IGachaService gachaService) : ControllerBase
{
    // POST /api/gacha/pull
    [HttpPost("pull")]
    public async Task<ActionResult<GachaPullResponseDto>> Pull([FromBody] GachaPullRequestDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (response, error) = await gachaService.PullAsync(playerId, dto.BannerId, dto.PullType);
        if (error != null) return BadRequest(new { message = error });

        return Ok(response);
    }
}
