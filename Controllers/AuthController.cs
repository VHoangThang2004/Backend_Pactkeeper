using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ISteamAuthService _steamAuthService;

    public AuthController(IAuthService authService, ISteamAuthService steamAuthService)
    {
        _authService = authService;
        _steamAuthService = steamAuthService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        var response = await _authService.LoginAsync(dto);
        return response != null ? Ok(response) : Unauthorized();
    }

    [HttpPost("steam")]
    public async Task<ActionResult<AuthResponseDto>> SteamLogin([FromBody] SteamLoginDto dto)
    {
        var response = await _steamAuthService.LoginWithSteamAsync(dto);
        return response != null ? Ok(response) : Unauthorized();
    }
}
