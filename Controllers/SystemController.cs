using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SystemController : ControllerBase
{
    private readonly IGameVersionService _gameVersionService;

    public SystemController(IGameVersionService gameVersionService)
    {
        _gameVersionService = gameVersionService;
    }

    /// <summary>
    /// GET /api/system/version
    /// Public endpoint for game clients, mobile app, and launchers to inspect version metadata.
    /// Returns LatestVersion, MinSupportedVersion, DownloadUrl, PatchNotes, and UpdatedAt.
    /// </summary>
    [HttpGet("version")]
    public async Task<ActionResult<GameVersionDto>> GetVersion()
    {
        var version = await _gameVersionService.GetVersionConfigAsync();
        return Ok(version);
    }
}
