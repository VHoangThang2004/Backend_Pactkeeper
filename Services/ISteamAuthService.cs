using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface ISteamAuthService
{
    Task<AuthResponseDto?> LoginWithSteamAsync(SteamLoginDto dto);
}