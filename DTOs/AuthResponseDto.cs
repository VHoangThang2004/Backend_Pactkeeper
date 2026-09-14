namespace GameInventoryApi.DTOs;

public record AuthResponseDto(string Token, string Role, string Username);