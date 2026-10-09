using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IGachaService
{
    Task<(GachaPullResponseDto? Response, string? Error)> PullAsync(string playerId, string bannerId, PullType pullType);
    Task<GachaHistoryResponseDto> GetHistoryAsync(string playerId, string? bannerId = null, int page = 1, int pageSize = 20);
}
