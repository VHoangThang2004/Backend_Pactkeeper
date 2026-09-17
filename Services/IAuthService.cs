using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface IAuthService
{
    Task<AuthResponseDto?> LoginAsync(LoginDto dto);
}
