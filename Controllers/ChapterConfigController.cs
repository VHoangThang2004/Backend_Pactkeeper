using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChapterConfigController : ControllerBase
{
    private readonly IChapterConfigService _chapterService;

    public ChapterConfigController(IChapterConfigService chapterService)
    {
        _chapterService = chapterService;
    }

    // -------------------------------------------------------
    // Public reads — any authenticated user
    // -------------------------------------------------------

    // GET /api/chapterconfig
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<ChapterConfig>>> GetAll()
        => Ok(await _chapterService.GetAllAsync());

    // GET /api/chapterconfig/{chapterId}
    [HttpGet("{chapterId:int}")]
    [Authorize]
    public async Task<ActionResult<ChapterConfig>> GetByChapterId(int chapterId)
    {
        var chapter = await _chapterService.GetByChapterIdAsync(chapterId);
        return chapter != null ? Ok(chapter) : NotFound();
    }

    // GET /api/chapterconfig/{chapterId}/scene/{sceneId}
    // Used by StoryController internally — also accessible for client debug
    [HttpGet("{chapterId:int}/scene/{sceneId:int}")]
    [Authorize]
    public async Task<ActionResult<SceneConfig>> GetScene(int chapterId, int sceneId)
    {
        var scene = await _chapterService.GetSceneAsync(chapterId, sceneId);
        return scene != null ? Ok(scene) : NotFound();
    }

    // -------------------------------------------------------
    // Admin CRUD
    // -------------------------------------------------------

    // POST /api/chapterconfig
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ChapterConfig>> Create([FromBody] ChapterConfig chapter)
    {
        // Guard: chapterId must be unique
        var existing = await _chapterService.GetByChapterIdAsync(chapter.ChapterId);
        if (existing != null)
            return Conflict($"ChapterId {chapter.ChapterId} already exists.");

        var created = await _chapterService.CreateAsync(chapter);
        return CreatedAtAction(nameof(GetByChapterId),
            new { chapterId = created.ChapterId }, created);
    }

    // PUT /api/chapterconfig/{id}
    // Full replace — send the complete ChapterConfig including Scenes list
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Update(string id, [FromBody] ChapterConfig chapter)
    {
        var success = await _chapterService.UpdateAsync(id, chapter);
        return success ? NoContent() : NotFound();
    }

    // DELETE /api/chapterconfig/{id}
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Delete(string id)
    {
        var success = await _chapterService.DeleteAsync(id);
        return success ? NoContent() : NotFound();
    }
}