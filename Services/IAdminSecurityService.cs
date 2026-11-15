using GameInventoryApi.DTOs;

namespace GameInventoryApi.Services;

public record ServiceResult<T>(
    bool Success,
    int StatusCode,
    T? Data = default,
    string? ErrorMessage = null);

public interface IAdminSecurityService
{
    Task<List<AdminAccountDto>> GetAdminAccountsAsync();
    Task<ServiceResult<AdminAccountDto>> CreateAdminAccountAsync(
        CreateAdminAccountRequestDto dto,
        string actorUsername,
        string actorRole);
    Task<ServiceResult<AdminAccountDto>> UpdateAccountRoleAsync(
        string accountId,
        string actorId,
        UpdateAdminAccountRoleRequestDto dto,
        string actorUsername,
        string actorRole);
    Task<ServiceResult<AdminAccountDto>> UpdateAccountStatusAsync(
        string accountId,
        string actorId,
        UpdateAdminAccountStatusRequestDto dto,
        string actorUsername,
        string actorRole);
    Task<ServiceResult<object>> ResetAccountPasswordAsync(
        string accountId,
        string actorId,
        ResetAdminPasswordRequestDto dto,
        string actorUsername,
        string actorRole);
    Task<ServiceResult<object>> RevokeAccountSessionsAsync(
        string accountId,
        RevokeSessionsRequestDto? dto,
        string actorUsername,
        string actorRole);

    Task<List<AdminRoleDto>> GetRolesAsync();
    Task<ServiceResult<AdminRoleDto>> CreateRoleAsync(
        CreateAdminRoleRequestDto dto,
        string actorUsername,
        string actorRole);
    Task<ServiceResult<AdminRoleDto>> UpdateRolePermissionsAsync(
        string roleId,
        UpdateAdminRolePermissionsRequestDto dto,
        string actorUsername,
        string actorRole);

    Task<List<AdminAuditLogDto>> GetAuditLogsAsync(
        string? category,
        string? severity,
        string? actor,
        string? search,
        int limit = 200);
    Task<ServiceResult<AdminAuditLogDto>> RecordAuditLogAsync(
        CreateAdminAuditLogRequestDto dto,
        string? authenticatedUsername,
        string? authenticatedRole);

    Task<List<AdminLoginHistoryDto>> GetLoginHistoryAsync(
        string? status,
        string? username,
        int limit = 200);
}
