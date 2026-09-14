namespace GameInventoryApi.DTOs;

public record PlayerProfileDto(
    string Id,
    string PlayerId,
    string Username,
    int Level,
    int Experience,
    DateTime LastLogin
);