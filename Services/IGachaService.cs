using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IGachaService
{
    Task<(GachaPullResponseDto? Response, string? Error)> PullAsync(string playerId, string bannerId, PullType pullType);
}
