using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface IAuthService
{
    Task<AuthResponseDto?> LoginAsync(LoginDto dto);
    Task<(AuthResponseDto? Response, int StatusCode, string? ErrorMessage)> LoginWithContextAsync(
        LoginDto dto,
        string? ipAddress,
        string? userAgent);
    Task<(bool Success, int StatusCode, string Message)> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword);
    Task<(bool Success, string Message)> LogoutAsync(
        string userId,
        string? username,
        string? role,
        string? ipAddress,
        string? userAgent);
    Task<(bool Success, string? Error)> ChangeUsernameAsync(string playerId, string newUsername);
}
