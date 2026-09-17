using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeaponDefinitionController : ControllerBase
{
    private readonly IWeaponDefinitionService _service;

    public WeaponDefinitionController(IWeaponDefinitionService service) => _service = service;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<WeaponDefinition>>> GetAll() => await _service.GetAllAsync();

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<WeaponDefinition>> Get(string id)
    {
        var item = await _service.GetByIdAsync(id);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpGet("weapon/{weaponId}")]
    [Authorize]
    public async Task<ActionResult<WeaponDefinition>> GetByWeaponId(int weaponId)
    {
        var item = await _service.GetByWeaponIdAsync(weaponId);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create([FromBody] WeaponDefinition weapon)
    {
        await _service.CreateAsync(weapon);
        return CreatedAtAction(nameof(Get), new { id = weapon.Id }, weapon);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Update(string id, [FromBody] WeaponDefinition weapon)
    {
        await _service.UpdateAsync(id, weapon);
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
