using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GameInventoryApi.Services;

public class GoogleAuthService(
    IMongoRepository<User> userRepository,
    IPlayerProfileService profileService,
    IOptions<JwtSettings> jwtSettings,
    IOptions<GoogleSettings> googleSettings,
    ServerState serverState
    ) : IGoogleAuthService
{
    private readonly IMongoRepository<User> _userRepository = userRepository;
    private readonly IPlayerProfileService _profileService = profileService;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;
    private readonly GoogleSettings _googleSettings = googleSettings.Value;
    private readonly ServerState _serverState = serverState;

    public async Task<AuthResponseDto?> LoginWithGoogleAsync(GoogleLoginDto dto)
    {
        if (_serverState.LoginBlocked) return null;

        string googleId;
        string email = string.Empty;
        string name = string.Empty;

        // Cho phép bypass khi test offline với mock token
        if (dto.IdToken.StartsWith("mock_"))
        {
            googleId = dto.IdToken.Replace("mock_", "");
            email = $"{googleId}@mock.com";
            name = $"G_Mock_{googleId.Substring(Math.Max(0, googleId.Length - 5))}";
            Console.WriteLine($"[GoogleAuth] Mock login bypass. GoogleId: {googleId}");
        }
        else
        {
            try
            {
                var payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleSettings.ClientId }
                });

                googleId = payload.Subject;
                email = payload.Email;
                name = payload.Name ?? payload.Email;
                Console.WriteLine($"[GoogleAuth] Verified GoogleId: {googleId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GoogleAuth] Token validation failed: {ex.Message}");
                return null;
            }
        }

        var user = await _userRepository.GetByFilterAsync(u => u.LoginProviders.GoogleId == googleId);

        if (user == null)
        {
            user = new User
            {
                Username = string.IsNullOrEmpty(name) ? $"Google_{googleId.Substring(0, Math.Min(6, googleId.Length))}" : name,
                Role = "Player",
                LoginProviders = new LoginProviders { GoogleId = googleId }
            };
            await _userRepository.CreateAsync(user);
            await _profileService.CreateInitialProfileAsync(user.Id, user.Username);
            Console.WriteLine($"[GoogleAuth] Created new user for GoogleId {googleId}");
        }

        return new AuthResponseDto(GenerateJwtToken(user), user.Role, user.Username, user.Id);
    }

    private string GenerateJwtToken(User user)
    {
        var secret = string.IsNullOrEmpty(_jwtSettings.SecretKey) ? "default-super-secret-key-1234567890123456" : _jwtSettings.SecretKey;
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
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
