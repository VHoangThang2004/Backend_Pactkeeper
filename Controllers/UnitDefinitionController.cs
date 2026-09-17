using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UnitDefinitionController : ControllerBase
{
    private readonly IUnitDefinitionService _service;

    public UnitDefinitionController(IUnitDefinitionService service) => _service = service;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<UnitDefinition>>> GetAll() => await _service.GetAllAsync();

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<UnitDefinition>> Get(string id)
    {
        var item = await _service.GetByIdAsync(id);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpGet("uid/{uId}")]
    [Authorize]
    public async Task<ActionResult<UnitDefinition>> GetByUId(int uId)
    {
        var item = await _service.GetByUIdAsync(uId);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create([FromBody] UnitDefinition unit)
    {
        await _service.CreateAsync(unit);
        return CreatedAtAction(nameof(Get), new { id = unit.Id }, unit);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Update(string id, [FromBody] UnitDefinition unit)
    {
        await _service.UpdateAsync(id, unit);
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
