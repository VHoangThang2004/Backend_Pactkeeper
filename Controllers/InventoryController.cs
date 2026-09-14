using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _service;

    public InventoryController(IInventoryService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<InventoryItem>>> GetAll() => await _service.GetAllAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<InventoryItem>> Get(string id)
    {
        var item = await _service.GetByIdAsync(id);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] InventoryItem item)
    {
        await _service.CreateAsync(item);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(string id, [FromBody] InventoryItem item)
    {
        await _service.UpdateAsync(id, item);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}