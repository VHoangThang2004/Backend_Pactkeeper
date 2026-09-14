using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GameInventoryApi.Services;

public class SteamAuthService : ISteamAuthService
{
    private readonly IMongoRepository<User> _userRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly SteamSettings _steamSettings;
    private readonly HttpClient _httpClient;

    public SteamAuthService(
        IMongoRepository<User> userRepository,
        IOptions<JwtSettings> jwtSettings,
        IOptions<SteamSettings> steamSettings,
        HttpClient httpClient)
    {
        _userRepository = userRepository;
        _jwtSettings = jwtSettings.Value;
        _steamSettings = steamSettings.Value;
        _httpClient = httpClient;
    }

    public async Task<AuthResponseDto?> LoginWithSteamAsync(SteamLoginDto dto)
    {
        // Step 1 — Verify ticket with Steam
        string steamId = await VerifySteamTicketAsync(dto.SteamTicket);
        if (string.IsNullOrEmpty(steamId))
        {
            Console.WriteLine("[SteamAuth] Ticket verification failed.");
            return null;
        }

        Console.WriteLine($"[SteamAuth] Verified SteamId: {steamId}");

        // Step 2 — Find or create user
        var user = await _userRepository.GetByFilterAsync(u => u.LoginProviders.SteamId == steamId);

        if (user == null)
        {
            user = new User
            {
                Username = $"Steam_{steamId}",
                Role = "Player",
                LoginProviders = new LoginProviders { SteamId = steamId }
            };
            await _userRepository.CreateAsync(user);
            Console.WriteLine($"[SteamAuth] Created new user for SteamId {steamId}");
        }

        // Step 3 — Issue JWT
        var token = GenerateJwtToken(user);
        return new AuthResponseDto(Token: token, Role: user.Role, Username: user.Username);
    }

    private async Task<string> VerifySteamTicketAsync(string ticket)
    {
        string url = $"https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/" +
                     $"?key={_steamSettings.WebApiKey}" +
                     $"&appid={_steamSettings.AppId}" +
                     $"&ticket={ticket}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return string.Empty;

        string json = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[SteamAuth] Steam response: {json}");

        // Parse steamid from response
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("response", out var responseObj)) return string.Empty;
        if (!responseObj.TryGetProperty("params", out var paramsObj)) return string.Empty;
        if (!paramsObj.TryGetProperty("steamid", out var steamIdProp)) return string.Empty;

        return steamIdProp.GetString() ?? string.Empty;
    }

    private string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("PlayerId", user.Id),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryInMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}