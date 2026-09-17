using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClassDefinitionController : ControllerBase
{
    private readonly IClassDefinitionService _service;

    public ClassDefinitionController(IClassDefinitionService service) => _service = service;

    // Admin CRUD
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<ClassDefinition>>> GetAll() => await _service.GetAllAsync();

    [HttpGet("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ClassDefinition>> Get(string id)
    {
        var item = await _service.GetByIdAsync(id);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpGet("class/{classId}")]
    [Authorize]
    public async Task<ActionResult<ClassDefinition>> GetByClassId(int classId)
    {
        var item = await _service.GetByClassIdAsync(classId);
        return item != null ? Ok(item) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create([FromBody] ClassDefinition classDef)
    {
        await _service.CreateAsync(classDef);
        return CreatedAtAction(nameof(Get), new { id = classDef.Id }, classDef);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Update(string id, [FromBody] ClassDefinition classDef)
    {
        await _service.UpdateAsync(id, classDef);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Delete(string id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }

    // Player-accessible skill filters
    [HttpPost("movement-skills")]
    [Authorize]
    public async Task<ActionResult<List<ClassSkillResultDto>>> GetMovementSkills([FromBody] ClassIdFilterDto dto)
    {
        var result = await _service.GetMovementSkillsByClassIdsAsync(dto.ClassIds);
        return Ok(result);
    }

    [HttpPost("class-skills")]
    [Authorize]
    public async Task<ActionResult<List<ClassSkillResultDto>>> GetClassSkills([FromBody] ClassIdFilterDto dto)
    {
        var result = await _service.GetClassSkillsByClassIdsAsync(dto.ClassIds);
        return Ok(result);
    }
}
