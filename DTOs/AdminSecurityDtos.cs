using System.Text.Json;

namespace GameInventoryApi.DTOs;

public record ChangePasswordDto(string CurrentPassword, string NewPassword);

public record AdminAccountDto(
    string Id,
    string Username,
    string? Email,
    string Role,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    string? LastLoginIp,
    List<string> CustomPermissions,
    List<string> Permissions);

public record CreateAdminAccountRequestDto(
    string Username,
    string Password,
    string? Email,
    string Role,
    string? Rationale = null,
    List<string>? CustomPermissions = null);

public record UpdateAdminAccountRoleRequestDto(
    string Role,
    string? Rationale = null);

public record UpdateAdminAccountStatusRequestDto(
    string Status,
    string? Rationale = null);

public record ResetAdminPasswordRequestDto(
    string NewPassword,
    string? Rationale = null);

public record RevokeSessionsRequestDto(string? Rationale = null);

public record AdminRoleDto(
    string Id,
    string Name,
    string Description,
    List<string> Permissions,
    bool IsSystemDefault,
    int AccountCount,
    DateTime UpdatedAt);

public record CreateAdminRoleRequestDto(
    string Name,
    string Description,
    List<string> Permissions,
    string? Rationale = null);

public record UpdateAdminRolePermissionsRequestDto(
    List<string> Permissions,
    string? Description = null,
    string? Rationale = null);

public record AdminAuditLogDto(
    string Id,
    DateTime Timestamp,
    string ActorUsername,
    string ActorRole,
    string Action,
    string Category,
    string Severity,
    string? TargetType,
    string? TargetId,
    string? TargetLabel,
    string? Rationale,
    JsonElement? BeforeState,
    JsonElement? AfterState,
    string Status,
    string? ErrorMessage);

public record CreateAdminAuditLogRequestDto(
    string Action,
    string? Category = null,
    string? Severity = null,
    string? TargetType = null,
    string? TargetId = null,
    string? TargetLabel = null,
    string? Rationale = null,
    JsonElement? BeforeState = null,
    JsonElement? AfterState = null,
    string? Status = null,
    string? ErrorMessage = null,
    string? ActorUsername = null,
    string? ActorRole = null);

public record AdminLoginHistoryDto(
    string Id,
    DateTime Timestamp,
    string Username,
    string? AttemptedRole,
    string? ResolvedRole,
    string Status,
    string? IpAddress,
    string? UserAgent,
    string? Reason);
