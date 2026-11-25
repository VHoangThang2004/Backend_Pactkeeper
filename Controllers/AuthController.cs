using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
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

    private static readonly ConcurrentDictionary<string, (AuthResponseDto Response, DateTime ExpiresAtUtc)> _pendingLoginCodes = new();

    public record ExchangeCodeRequestDto(string Code);

    private static string IssueOneTimeLoginCode(AuthResponseDto authResponse)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _pendingLoginCodes)
        {
            if (now > kvp.Value.ExpiresAtUtc)
                _pendingLoginCodes.TryRemove(kvp.Key, out _);
        }

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        _pendingLoginCodes[code] = (authResponse, now.AddSeconds(60));
        return code;
    }

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
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();
        var (response, statusCode, errorMessage) = await _authService.LoginWithContextAsync(dto, ipAddress, userAgent);
        if (response != null)
        {
            return Ok(response);
        }

        if (statusCode == 403)
        {
            return StatusCode(403, new { message = errorMessage ?? "Account access is restricted." });
        }

        return Unauthorized(new { message = errorMessage ?? "Invalid credentials." });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Authenticated user claim was not found." });
        }

        var (success, statusCode, message) = await _authService.ChangePasswordAsync(
            userId,
            dto.CurrentPassword,
            dto.NewPassword);

        return success
            ? Ok(new { message })
            : StatusCode(statusCode, new { message });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = GetAuthenticatedUserId() ?? string.Empty;
        var username = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? User.Identity?.Name;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        var (_, message) = await _authService.LogoutAsync(userId, username, role, ipAddress, userAgent);
        return Ok(new { message });
    }

    private string? GetAuthenticatedUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? User.FindFirst("PlayerId")?.Value;

    private string? GetClientIpAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',').FirstOrDefault()?.Trim();
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
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
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string? code = null,
        [FromQuery] string? error = null,
        [FromQuery] string? state = null)
    {
        var resolvedState = !string.IsNullOrEmpty(state) ? state : Request.Query["state"].ToString();

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            if (resolvedState == "pc")
            {
                return Redirect($"http://localhost:5000/callback?error={Uri.EscapeDataString(error ?? "cancelled")}");
            }

            const string cancelledHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>Pactkeeper — Login Cancelled</title>
  <style>
    body {
      margin: 0; height: 100vh; display: flex; align-items: center; justify-content: center;
      background: radial-gradient(circle at top, #2B1D24 0%, #140D10 70%);
      color: #F2E4C8; font-family: 'Segoe UI', system-ui, sans-serif;
    }
    .card {
      background: #1E161C; border: 2px solid #8C5A3C; border-radius: 10px;
      padding: 36px 44px; text-align: center; max-width: 400px;
      box-shadow: 0 18px 45px rgba(0, 0, 0, 0.65);
    }
    h1 { margin: 0 0 8px; font-size: 20px; letter-spacing: 2px; color: #E8C678; text-transform: uppercase; }
    p { margin: 0; font-size: 14px; color: #C9B9A6; line-height: 1.5; }
  </style>
</head>
<body>
  <div class=""card"">
    <h1>Login Cancelled</h1>
    <p>Google authentication was cancelled.<br/>You may safely close this tab and return to Pactkeeper.</p>
  </div>
</body>
</html>";
            return Content(cancelledHtml, "text/html; charset=utf-8");
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

            string finalRedirectUrl;
            if (resolvedState == "pc")
            {
                var oneTimeCode = IssueOneTimeLoginCode(authResponse);
                finalRedirectUrl = $"http://localhost:5000/callback?code={Uri.EscapeDataString(oneTimeCode)}";
            }
            else
            {
                finalRedirectUrl = $"unnamedsrpg://login-success" +
                                   $"?token={Uri.EscapeDataString(authResponse.Token)}" +
                                   $"&username={Uri.EscapeDataString(authResponse.Username)}" +
                                   $"&playerId={Uri.EscapeDataString(authResponse.PlayerId)}" +
                                   $"&role={Uri.EscapeDataString(authResponse.Role)}";
            }
            Console.WriteLine($"[GoogleCallback] Success! Redirecting for user: {authResponse.Username} platform={resolvedState}");
            return Redirect(finalRedirectUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GoogleCallback] Exception: {ex.Message}");
            return StatusCode(500, $"Lỗi xử lý callback Google: {ex.Message}");
        }
    }

    // POST /api/auth/exchange-code
    [HttpPost("exchange-code")]
    [AllowAnonymous]
    public ActionResult<AuthResponseDto> ExchangeCode([FromBody] ExchangeCodeRequestDto? dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Code))
            return Unauthorized();

        if (!_pendingLoginCodes.TryRemove(dto.Code, out var entry) || DateTime.UtcNow > entry.ExpiresAtUtc)
            return Unauthorized();

        return Ok(entry.Response);
    }
}
