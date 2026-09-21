using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface IGoogleAuthService
{
    Task<AuthResponseDto?> LoginWithGoogleAsync(GoogleLoginDto dto);
}
