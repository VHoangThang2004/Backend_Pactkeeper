using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GameInventoryApi.Services;

public class AuthService : IAuthService
{
    private readonly IMongoRepository<User> _userRepository;
    private readonly IMongoRepository<PlayerProfile> _profileRepository;
    private readonly IMongoRepository<AdminRoleDefinition> _roleRepository;
    private readonly IMongoRepository<AdminAuditLog> _auditRepository;
    private readonly IMongoRepository<AdminLoginHistory> _loginHistoryRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ServerState _serverState;

    public AuthService(
        IMongoRepository<User> userRepository,
        IMongoRepository<PlayerProfile> profileRepository,
        IMongoRepository<AdminRoleDefinition> roleRepository,
        IMongoRepository<AdminAuditLog> auditRepository,
        IMongoRepository<AdminLoginHistory> loginHistoryRepository,
        IOptions<JwtSettings> jwtSettings,
        ServerState serverState)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _roleRepository = roleRepository;
        _auditRepository = auditRepository;
        _loginHistoryRepository = loginHistoryRepository;
        _jwtSettings = jwtSettings.Value;
        _serverState = serverState;
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        var (response, _, _) = await LoginWithContextAsync(dto, null, null);
        return response;
    }

    public async Task<(AuthResponseDto? Response, int StatusCode, string? ErrorMessage)> LoginWithContextAsync(
        LoginDto dto,
        string? ipAddress,
        string? userAgent)
    {
        var attemptedUsername = dto.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(attemptedUsername) || string.IsNullOrEmpty(dto.Password))
        {
            return (null, 401, "Username and passcode are required.");
        }

        var user = await _userRepository.GetByFilterAsync(u => u.Username == attemptedUsername);
        if (user == null || !user.HasPassword || !AdminSecurityHelper.VerifyPassword(dto.Password, user.PasswordHash))
        {
            await TryRecordLoginHistoryAsync(new AdminLoginHistory
            {
                Timestamp = DateTime.UtcNow,
                Username = attemptedUsername,
                AttemptedRole = dto.AttemptedRole,
                ResolvedRole = user?.Role,
                Status = "FAILED",
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Reason = user == null ? "Account not found" : "Invalid passcode"
            });
            return (null, 401, "Invalid username or passcode.");
        }

        var isPlayer = string.Equals(AdminSecurityHelper.NormalizeRoleName(user.Role), "Player", StringComparison.OrdinalIgnoreCase);

        if (user.IsBanned)
        {
            if (!isPlayer)
            {
                await TryRecordLoginHistoryAsync(new AdminLoginHistory
                {
                    Timestamp = DateTime.UtcNow,
                    Username = user.Username,
                    AttemptedRole = dto.AttemptedRole,
                    ResolvedRole = user.Role,
                    Status = "FAILED",
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Reason = "Account is banned"
                });
                return (null, 403, "Account access has been suspended.");
            }
            return (null, 401, "Account is banned.");
        }

        var accountStatus = AdminSecurityHelper.NormalizeAccountStatus(user.AccountStatus);
        if (accountStatus != "ACTIVE")
        {
            var reason = $"Account status is {accountStatus}";
            await TryRecordLoginHistoryAsync(new AdminLoginHistory
            {
                Timestamp = DateTime.UtcNow,
                Username = user.Username,
                AttemptedRole = dto.AttemptedRole,
                ResolvedRole = user.Role,
                Status = "FAILED",
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Reason = reason
            });

            if (!isPlayer)
            {
                await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    ActorUsername = user.Username,
                    ActorRole = user.Role,
                    Action = "RESTRICTED_ACCOUNT_LOGIN_BLOCKED",
                    Category = "AUTH",
                    Severity = "HIGH",
                    TargetType = "Account",
                    TargetId = user.Id,
                    TargetLabel = user.Username,
                    Rationale = reason,
                    Status = "FAILED",
                    ErrorMessage = reason
                });
            }

            return (null, 403, $"Account is {accountStatus}. Please contact a High Counsel Admin.");
        }

        List<string>? permissions = null;
        if (!isPlayer)
        {
            var roles = await _roleRepository.GetAllAsync();
            permissions = AdminSecurityHelper.ResolveEffectivePermissions(user, roles);
        }

        var now = DateTime.UtcNow;
        user.LastLoginAt = now;
        user.LastLoginIp = ipAddress ?? user.LastLoginIp;
        await _userRepository.UpdateAsync(user.Id, user);

        if (isPlayer)
        {
            var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == user.Id);
            if (profile != null)
            {
                profile.LastLogin = now;
                await _profileRepository.UpdateAsync(profile.Id, profile);
            }
        }

        if (!isPlayer)
        {
            await TryRecordLoginHistoryAsync(new AdminLoginHistory
            {
                Timestamp = DateTime.UtcNow,
                Username = user.Username,
                AttemptedRole = dto.AttemptedRole,
                ResolvedRole = user.Role,
                Status = "SUCCESS",
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Reason = "Authenticated via POST /api/Auth/login"
            });
        }

        var token = GenerateJwtToken(user, permissions);
        return (new AuthResponseDto(token, user.Role, user.Username, user.Id, permissions), 200, null);
    }

    public async Task<(bool Success, int StatusCode, string Message)> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return (false, 401, "Invalid user identity.");
        }

        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            return (false, 400, "Current passcode and new passcode are required.");
        }

        if (newPassword.Length < 8)
        {
            return (false, 400, "New passcode must be at least 8 characters long.");
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return (false, 400, "New passcode must differ from your current passcode.");
        }

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return (false, 404, "Account not found.");
        }

        if (!user.HasPassword || !AdminSecurityHelper.VerifyPassword(currentPassword, user.PasswordHash))
        {
            return (false, 401, "Current passcode is incorrect.");
        }

        user.PasswordHash = AdminSecurityHelper.HashPassword(newPassword);
        user.HasPassword = true;
        await _userRepository.UpdateAsync(user.Id, user);

        if (!string.Equals(AdminSecurityHelper.NormalizeRoleName(user.Role), "Player", StringComparison.OrdinalIgnoreCase))
        {
            await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
            {
                Timestamp = DateTime.UtcNow,
                ActorUsername = user.Username,
                ActorRole = user.Role,
                Action = "PASSWORD_CHANGED",
                Category = "AUTH",
                Severity = "MEDIUM",
                TargetType = "Account",
                TargetId = user.Id,
                TargetLabel = user.Username,
                Rationale = "Self-service passcode update via POST /api/Auth/change-password",
                Status = "SUCCESS"
            });
        }

        return (true, 200, "Passcode updated successfully.");
    }

    public async Task<(bool Success, string Message)> LogoutAsync(
        string userId,
        string? username,
        string? role,
        string? ipAddress,
        string? userAgent)
    {
        User? user = null;
        if (ObjectId.TryParse(userId, out _))
        {
            user = await _userRepository.GetByIdAsync(userId);
            if (user != null)
            {
                user.TokenVersion += 1;
                await _userRepository.UpdateAsync(user.Id, user);
            }
        }

        var resolvedUsername = user?.Username ?? username ?? "Unknown";
        var resolvedRole = user?.Role ?? role ?? "Admin";

        if (!string.Equals(AdminSecurityHelper.NormalizeRoleName(resolvedRole), "Player", StringComparison.OrdinalIgnoreCase))
        {
            await TryRecordLoginHistoryAsync(new AdminLoginHistory
            {
                Timestamp = DateTime.UtcNow,
                Username = resolvedUsername,
                ResolvedRole = resolvedRole,
                Status = "LOGOUT",
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Reason = "Signed out via POST /api/Auth/logout"
            });

            await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
            {
                Timestamp = DateTime.UtcNow,
                ActorUsername = resolvedUsername,
                ActorRole = resolvedRole,
                Action = "ADMIN_LOGOUT",
                Category = "AUTH",
                Severity = "LOW",
                TargetType = "Session",
                TargetId = user?.Id,
                TargetLabel = resolvedUsername,
                Status = "SUCCESS"
            });
        }

        return (true, "Logged out successfully.");
    }

    private async Task TryRecordLoginHistoryAsync(AdminLoginHistory entry)
    {
        try
        {
            await _loginHistoryRepository.CreateAsync(entry);
        }
        catch
        {
            // Keep login response resilient if telemetry persistence encounters a transient error
        }
    }

    public async Task<(bool Success, string? Error)> ChangeUsernameAsync(string playerId, string newUsername)
    {
        if (string.IsNullOrWhiteSpace(newUsername))
            return (false, "Username cannot be empty.");

        newUsername = newUsername.Trim();

        var existing = await _userRepository.GetByFilterAsync(u => u.Id != playerId && u.Username == newUsername);
        if (existing != null)
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

    private string GenerateJwtToken(User user, List<string>? permissions = null)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("PlayerId", user.Id),
            new("tv", user.TokenVersion.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (permissions != null)
        {
            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permissions", permission));
            }
        }

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
