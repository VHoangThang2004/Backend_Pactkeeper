using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/rewardprogress")]
[Authorize(Roles = "Player")]
public class RewardProgressController(IRewardProgressService service) : ControllerBase
{
    // GET /api/rewardprogress — the player's own progress on every rule they've touched
    [HttpGet]
    public async Task<ActionResult<List<RewardProgressDto>>> GetMine()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var list = await service.GetByPlayerIdAsync(playerId);
        return Ok(list.Select(ToDto).ToList());
    }

    // POST /api/rewardprogress/{id}/claim — grants the reward for a completed, unclaimed objective
    [HttpPost("{id}/claim")]
    public async Task<IActionResult> Claim(string id)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (success, error) = await service.ClaimAsync(playerId, id);
        if (!success) return BadRequest(new { message = error });

        return Ok();
    }

    private static RewardProgressDto ToDto(RewardProgress p) => new(
        p.Id, p.RuleId, p.CurrentCount, p.IsCompleted, p.CompletedAt, p.IsReceived, p.ReceivedAt);
}
