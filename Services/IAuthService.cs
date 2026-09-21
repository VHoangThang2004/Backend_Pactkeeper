using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface IAuthService
{
    Task<AuthResponseDto?> LoginAsync(LoginDto dto);
    Task<(bool Success, string? Error)> ChangeUsernameAsync(string playerId, string newUsername);
}
