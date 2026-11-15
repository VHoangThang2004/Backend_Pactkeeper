using System.Text.Json.Serialization;

namespace GameInventoryApi.DTOs;

public record AuthResponseDto(
    string Token,
    string Role,
    string Username,
    string PlayerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<string>? Permissions = null);