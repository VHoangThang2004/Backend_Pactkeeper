using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/rewardrule")]
[Authorize]
public class RewardRuleController : ControllerBase
{
    private readonly IRewardRuleService _service;

    public RewardRuleController(IRewardRuleService service) => _service = service;

    // GET /api/rewardrule/active — any authenticated user; the player-facing mission list
    [HttpGet("active")]
    public async Task<ActionResult<List<RewardRuleDto>>> GetActive()
    {
        var rules = await _service.GetActiveAsync();
        return Ok(rules.Select(ToDto).ToList());
    }

    // GET /api/rewardrule — all rules including inactive, admin only
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<RewardRuleDto>>> GetAll()
    {
        var rules = await _service.GetAllAsync();
        return Ok(rules.Select(ToDto).ToList());
    }

    // GET /api/rewardrule/{id} — admin only
    [HttpGet("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RewardRuleDto>> GetById(string id)
    {
        var rule = await _service.GetByIdAsync(id);
        if (rule == null) return NotFound();
        return Ok(ToDto(rule));
    }

    // POST /api/rewardrule — admin only; rules start active
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RewardRuleDto>> Create([FromBody] CreateRewardRuleDto dto)
    {
        var rule = new RewardRule
        {
            ActionType = dto.ActionType,
            Conditions = dto.Conditions.Select(MapCondition).ToList(),
            AccumulateField = dto.AccumulateField,
            TargetCount = dto.TargetCount,
            Reward = MapReward(dto.Reward),
            IsActive = true,
        };
        var created = await _service.CreateAsync(rule);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    // PUT /api/rewardrule/{id} — admin only; does not change IsActive
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateRewardRuleDto dto)
    {
        var existing = await _service.GetByIdAsync(id);
        if (existing == null) return NotFound();

        existing.ActionType = dto.ActionType;
        existing.Conditions = dto.Conditions.Select(MapCondition).ToList();
        existing.AccumulateField = dto.AccumulateField;
        existing.TargetCount = dto.TargetCount;
        existing.Reward = MapReward(dto.Reward);

        await _service.UpdateAsync(id, existing);
        return NoContent();
    }

    // PATCH /api/rewardrule/{id}/activate — admin only
    [HttpPatch("{id}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Activate(string id)
    {
        var rule = await _service.GetByIdAsync(id);
        if (rule == null) return NotFound();
        await _service.SetActiveAsync(id, true);
        return NoContent();
    }

    // PATCH /api/rewardrule/{id}/deactivate — admin only
    [HttpPatch("{id}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var rule = await _service.GetByIdAsync(id);
        if (rule == null) return NotFound();
        await _service.SetActiveAsync(id, false);
        return NoContent();
    }

    private static RewardCondition MapCondition(RewardConditionDto c) => new()
    {
        Field = c.Field,
        Op = c.Op,
        Value = c.Value,
    };

    private static Reward MapReward(RewardDto r) => new()
    {
        Type = r.Type,
        DefinitionId = r.DefinitionId,
        ClassId = r.ClassId,
        Amount = r.Amount,
    };

    private static RewardRuleDto ToDto(RewardRule r) => new(
        r.Id,
        r.ActionType,
        r.Conditions.Select(c => new RewardConditionDto(c.Field, c.Op, c.Value)).ToList(),
        r.AccumulateField,
        r.TargetCount,
        new RewardDto(r.Reward.Type, r.Reward.DefinitionId, r.Reward.ClassId, r.Reward.Amount),
        r.IsActive
    );
}
