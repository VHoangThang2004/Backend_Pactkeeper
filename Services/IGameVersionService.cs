using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public interface IGameVersionService
{
    Task<GameVersionDto> GetVersionConfigAsync();
    Task<GameVersionDto> UpdateVersionConfigAsync(UpdateGameVersionDto dto, string? adminUsername);
}
