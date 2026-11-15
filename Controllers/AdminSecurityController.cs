using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameInventoryApi.DTOs;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin,Server")]
public class AdminSecurityController : ControllerBase
{
    private readonly IAdminSecurityService _securityService;

    public AdminSecurityController(IAdminSecurityService securityService)
    {
        _securityService = securityService;
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<List<AdminAccountDto>>> GetAccounts()
    {
        var accounts = await _securityService.GetAdminAccountsAsync();
        return Ok(accounts);
    }

    [HttpPost("accounts")]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAdminAccountRequestDto dto)
    {
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.CreateAdminAccountAsync(dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpPut("accounts/{id}/role")]
    public async Task<IActionResult> UpdateAccountRole(string id, [FromBody] UpdateAdminAccountRoleRequestDto dto)
    {
        var actorId = GetActorId();
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.UpdateAccountRoleAsync(id, actorId, dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpPatch("accounts/{id}/status")]
    public async Task<IActionResult> UpdateAccountStatus(string id, [FromBody] UpdateAdminAccountStatusRequestDto dto)
    {
        var actorId = GetActorId();
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.UpdateAccountStatusAsync(id, actorId, dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpPost("accounts/{id}/reset-password")]
    public async Task<IActionResult> ResetAccountPassword(string id, [FromBody] ResetAdminPasswordRequestDto dto)
    {
        var actorId = GetActorId();
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.ResetAccountPasswordAsync(id, actorId, dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpPost("accounts/{id}/revoke-sessions")]
    public async Task<IActionResult> RevokeAccountSessions(
        string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RevokeSessionsRequestDto? dto = null)
    {
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.RevokeAccountSessionsAsync(id, dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpGet("roles")]
    public async Task<ActionResult<List<AdminRoleDto>>> GetRoles()
    {
        var roles = await _securityService.GetRolesAsync();
        return Ok(roles);
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] CreateAdminRoleRequestDto dto)
    {
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.CreateRoleAsync(dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpPut("roles/{id}/permissions")]
    public async Task<IActionResult> UpdateRolePermissions(string id, [FromBody] UpdateAdminRolePermissionsRequestDto dto)
    {
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.UpdateRolePermissionsAsync(id, dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<List<AdminAuditLogDto>>> GetAuditLogs(
        [FromQuery] string? category = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 200)
    {
        var logs = await _securityService.GetAuditLogsAsync(category, severity, actor, search, limit);
        return Ok(logs);
    }

    [HttpPost("audit-logs")]
    [Authorize(Roles = "Admin,Server,Moderator,Technical Support,Support")]
    public async Task<IActionResult> CreateAuditLog([FromBody] CreateAdminAuditLogRequestDto dto)
    {
        var (actorUsername, actorRole) = GetActorIdentity();
        var result = await _securityService.RecordAuditLogAsync(dto, actorUsername, actorRole);
        return ToActionResult(result);
    }

    [HttpGet("login-history")]
    public async Task<ActionResult<List<AdminLoginHistoryDto>>> GetLoginHistory(
        [FromQuery] string? status = null,
        [FromQuery] string? username = null,
        [FromQuery] int limit = 200)
    {
        var history = await _securityService.GetLoginHistoryAsync(status, username, limit);
        return Ok(history);
    }

    private string GetActorId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? User.FindFirstValue("PlayerId")
        ?? string.Empty;

    private (string Username, string Role) GetActorIdentity()
    {
        var username =
            User.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.Identity?.Name
            ?? "Admin";

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Admin";
        return (username, role);
    }

    private IActionResult ToActionResult<T>(ServiceResult<T> result)
    {
        if (result.Success)
        {
            return result.StatusCode == 201
                ? StatusCode(201, result.Data)
                : Ok(result.Data);
        }

        return StatusCode(result.StatusCode, new { message = result.ErrorMessage ?? "Request failed." });
    }
}
