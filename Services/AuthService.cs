using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GameInventoryApi.Services;

public class AuthService : IAuthService
{
    private readonly IMongoRepository<User> _userRepository;
    private readonly IMongoRepository<PlayerProfile> _profileRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ServerState _serverState;

    public AuthService(
        IMongoRepository<User> userRepository,
        IMongoRepository<PlayerProfile> profileRepository,
        IOptions<JwtSettings> jwtSettings,
        ServerState serverState)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _jwtSettings = jwtSettings.Value;
        _serverState = serverState;
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        // if (_serverState.LoginBlocked) return null; // devmode login is not blocked
        var user = await _userRepository.GetByFilterAsync(u => u.Username == dto.Username);
        if (user == null || !user.HasPassword || user.PasswordHash != dto.Password)
            return null;

        return new AuthResponseDto(GenerateJwtToken(user), user.Role, user.Username, user.Id);
    }

    public async Task<(bool Success, string? Error)> ChangeUsernameAsync(string playerId, string newUsername)
    {
        if (string.IsNullOrWhiteSpace(newUsername))
            return (false, "Username cannot be empty.");

        var existing = await _userRepository.GetByFilterAsync(u => u.Username == newUsername);
        if (existing != null && existing.Id != playerId)
            return (false, "Username is already taken.");

        var user = await _userRepository.GetByIdAsync(playerId);
        if (user == null) return (false, "User not found.");

        user.Username = newUsername;
        await _userRepository.UpdateAsync(playerId, user);

        var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == playerId);
        if (profile != null)
        {
            profile.Username = newUsername;
            await _profileRepository.UpdateAsync(profile.Id, profile);
        }

        return (true, null);
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
