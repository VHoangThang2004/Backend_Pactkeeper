using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class RankingController : ControllerBase
{
    private readonly IRankingService _rankingService;

    public RankingController(IRankingService rankingService)
    {
        _rankingService = rankingService;
    }

    [HttpGet("leaderboard")]
    public async Task<ActionResult<LeaderboardResponseDto>> GetLeaderboard([FromQuery] int limit = 100)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (limit <= 0 || limit > 500) limit = 100;

        var result = await _rankingService.GetLeaderboardAsync(playerId, limit);
        return Ok(result);
    }

    [HttpGet("me")]
    public async Task<ActionResult<LeaderboardEntryDto>> GetMyRank()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var rank = await _rankingService.GetPlayerRankAsync(playerId);
        if (rank == null) return NotFound(new { message = "Profile not found." });

        return Ok(rank);
    }
}
