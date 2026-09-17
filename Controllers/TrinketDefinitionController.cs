using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrinketDefinitionController : ControllerBase
{
    private readonly ITrinketDefinitionService _service;

    public TrinketDefinitionController(ITrinketDefinitionService service) => _service = service;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<TrinketDefinition>>> GetAll() => await _service.GetAllAsync();

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<TrinketDefinition>> Get(string id)
    {
        var item = await _service.GetByIdAsync(id);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpGet("trinket/{trinketId}")]
    [Authorize]
    public async Task<ActionResult<TrinketDefinition>> GetByTrinketId(int trinketId)
    {
        var item = await _service.GetByTrinketIdAsync(trinketId);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create([FromBody] TrinketDefinition trinket)
    {
        await _service.CreateAsync(trinket);
        return CreatedAtAction(nameof(Get), new { id = trinket.Id }, trinket);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Update(string id, [FromBody] TrinketDefinition trinket)
    {
        await _service.UpdateAsync(id, trinket);
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
