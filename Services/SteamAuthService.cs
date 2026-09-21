using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GameInventoryApi.Services;

public class SteamAuthService(
    IMongoRepository<User> userRepository,
    IPlayerProfileService profileService,
    IOptions<JwtSettings> jwtSettings,
    IOptions<SteamSettings> steamSettings,
    HttpClient httpClient,
    ServerState serverState) : ISteamAuthService
{
    private readonly IMongoRepository<User> _userRepository = userRepository;
    private readonly IPlayerProfileService _profileService = profileService;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;
    private readonly SteamSettings _steamSettings = steamSettings.Value;
    private readonly HttpClient _httpClient = httpClient;
    private readonly ServerState _serverState = serverState;

    public async Task<AuthResponseDto?> LoginWithSteamAsync(SteamLoginDto dto)
    {
        if (_serverState.LoginBlocked) return null;
        string steamId = await VerifySteamTicketAsync(dto.SteamTicket);
        if (string.IsNullOrEmpty(steamId))
        {
            Console.WriteLine("[SteamAuth] Ticket verification failed.");
            return null;
        }

        Console.WriteLine($"[SteamAuth] Verified SteamId: {steamId}");

        var user = await _userRepository.GetByFilterAsync(u => u.LoginProviders.SteamId == steamId);

        if (user == null)
        {
            string personaName = await GetSteamPersonaNameAsync(steamId);
            string username = string.IsNullOrEmpty(personaName) ? $"Steam_{steamId}" : personaName;

            user = new User
            {
                Username = username,
                Role = "Player",
                LoginProviders = new LoginProviders { SteamId = steamId }
            };
            await _userRepository.CreateAsync(user);
            await _profileService.CreateInitialProfileAsync(user.Id, user.Username);
            Console.WriteLine($"[SteamAuth] Created new user for SteamId {steamId} with name '{username}'");
        }

        return new AuthResponseDto(GenerateJwtToken(user), user.Role, user.Username, user.Id);
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

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("response", out var responseObj)) return string.Empty;
        if (!responseObj.TryGetProperty("params", out var paramsObj)) return string.Empty;
        if (!paramsObj.TryGetProperty("steamid", out var steamIdProp)) return string.Empty;

        return steamIdProp.GetString() ?? string.Empty;
    }

    private async Task<string> GetSteamPersonaNameAsync(string steamId)
    {
        string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/" +
                     $"?key={_steamSettings.WebApiKey}" +
                     $"&steamids={steamId}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return string.Empty;

        string json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("response", out var responseObj)) return string.Empty;
        if (!responseObj.TryGetProperty("players", out var players)) return string.Empty;
        if (players.GetArrayLength() == 0) return string.Empty;

        return players[0].TryGetProperty("personaname", out var name)
            ? name.GetString() ?? string.Empty
            : string.Empty;
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
