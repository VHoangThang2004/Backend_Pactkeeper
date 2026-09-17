using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SkillDefinitionController : ControllerBase
{
    private readonly ISkillDefinitionService _service;

    public SkillDefinitionController(ISkillDefinitionService service) => _service = service;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SkillDefinition>>> GetAll() => await _service.GetAllAsync();

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<SkillDefinition>> Get(string id)
    {
        var item = await _service.GetByIdAsync(id);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpGet("skill/{skillId}")]
    [Authorize]
    public async Task<ActionResult<SkillDefinition>> GetBySkillId(int skillId)
    {
        var item = await _service.GetBySkillIdAsync(skillId);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create([FromBody] SkillDefinition skill)
    {
        await _service.CreateAsync(skill);
        return CreatedAtAction(nameof(Get), new { id = skill.Id }, skill);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Update(string id, [FromBody] SkillDefinition skill)
    {
        await _service.UpdateAsync(id, skill);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Delete(string id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}
