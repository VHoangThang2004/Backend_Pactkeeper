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

    // GET /api/gacha/history?bannerId={id}&page=1&pageSize=20
    [HttpGet("history")]
    public async Task<ActionResult<GachaHistoryResponseDto>> GetHistory(
        [FromQuery] string? bannerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var result = await gachaService.GetHistoryAsync(playerId, bannerId, page, pageSize);
        return Ok(result);
    }
}

