namespace GameInventoryApi.DTOs;

public record LoginDto(string Username, string Password, string? AttemptedRole = null);