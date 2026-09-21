using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ISteamAuthService _steamAuthService;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IConfiguration _configuration;

    public AuthController(
        IAuthService authService,
        ISteamAuthService steamAuthService,
        IGoogleAuthService googleAuthService,
        IConfiguration configuration)
    {
        _authService = authService;
        _steamAuthService = steamAuthService;
        _googleAuthService = googleAuthService;
        _configuration = configuration;
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

    [HttpPost("google")]
    public async Task<ActionResult<AuthResponseDto>> GoogleLogin([FromBody] GoogleLoginDto dto)
    {
        var response = await _googleAuthService.LoginWithGoogleAsync(dto);
        return response != null ? Ok(response) : Unauthorized();
    }

    // PATCH /api/auth/username
    [HttpPatch("username")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult> ChangeUsername([FromBody] ChangeUsernameDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (success, error) = await _authService.ChangeUsernameAsync(playerId, dto.NewUsername);
        return success ? NoContent() : BadRequest(error);
    }

    // GET /api/auth/google-callback
    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("Mã xác thực Google (code) không hợp lệ.");
        }

        try
        {
            using var client = new HttpClient();
            var clientId = _configuration["GoogleSettings:ClientId"];
            var clientSecret = _configuration["GoogleSettings:ClientSecret"];
            var backendPublicUrl = _configuration["ServerConfig:BackendPublicUrl"] ?? "http://localhost:5276";
            var redirectUri = $"{backendPublicUrl}/api/auth/google-callback";

            var values = new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", clientId ?? "" },
                { "client_secret", clientSecret ?? "" },
                { "redirect_uri", redirectUri },
                { "grant_type", "authorization_code" }
            };

            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync("https://oauth2.googleapis.com/token", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[GoogleCallback] Error exchanging code: {errorContent}");
                return BadRequest("Không thể trao đổi mã xác thực với Google.");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseString);

            if (!jsonDoc.RootElement.TryGetProperty("id_token", out var idTokenProp))
            {
                return BadRequest("Không tìm thấy id_token trong phản hồi từ Google.");
            }

            var idToken = idTokenProp.GetString();
            if (string.IsNullOrEmpty(idToken))
            {
                return BadRequest("id_token từ Google trống.");
            }

            var authResponse = await _googleAuthService.LoginWithGoogleAsync(new GoogleLoginDto(idToken));
            if (authResponse == null)
            {
                return BadRequest("Xác thực thông tin tài khoản Google thất bại.");
            }

            // Redirect về app qua Deep Link Custom URL Scheme
            var redirectUrl = $"unnamedsrpg://login-success" +
                              $"?token={Uri.EscapeDataString(authResponse.Token)}" +
                              $"&username={Uri.EscapeDataString(authResponse.Username)}" +
                              $"&playerId={Uri.EscapeDataString(authResponse.PlayerId)}" +
                              $"&role={Uri.EscapeDataString(authResponse.Role)}";


            var state = Request.Query["state"].ToString();
            string finalRedirectUrl;
            if (state == "pc")
            {
                finalRedirectUrl = $"http://localhost:5000/callback" +
                                   $"?token={Uri.EscapeDataString(authResponse.Token)}" +
                                   $"&username={Uri.EscapeDataString(authResponse.Username)}" +
                                   $"&playerId={Uri.EscapeDataString(authResponse.PlayerId)}" +
                                   $"&role={Uri.EscapeDataString(authResponse.Role)}";
            }
            else
            {
                finalRedirectUrl = $"unnamedsrpg://login-success" +
                                   $"?token={Uri.EscapeDataString(authResponse.Token)}" +
                                   $"&username={Uri.EscapeDataString(authResponse.Username)}" +
                                   $"&playerId={Uri.EscapeDataString(authResponse.PlayerId)}" +
                                   $"&role={Uri.EscapeDataString(authResponse.Role)}";
            }
            Console.WriteLine($"[GoogleCallback] Success! Redirecting for user: {authResponse.Username} platform={state}");
            return Redirect(finalRedirectUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GoogleCallback] Exception: {ex.Message}");
            return StatusCode(500, $"Lỗi xử lý callback Google: {ex.Message}");
        }
    }
}
