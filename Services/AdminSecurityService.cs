using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using MongoDB.Bson;

namespace GameInventoryApi.Services;

public class AdminSecurityService : IAdminSecurityService
{
    private readonly IMongoRepository<User> _userRepository;
    private readonly IMongoRepository<AdminRoleDefinition> _roleRepository;
    private readonly IMongoRepository<AdminAuditLog> _auditRepository;
    private readonly IMongoRepository<AdminLoginHistory> _loginHistoryRepository;

    public AdminSecurityService(
        IMongoRepository<User> userRepository,
        IMongoRepository<AdminRoleDefinition> roleRepository,
        IMongoRepository<AdminAuditLog> auditRepository,
        IMongoRepository<AdminLoginHistory> loginHistoryRepository)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _auditRepository = auditRepository;
        _loginHistoryRepository = loginHistoryRepository;
    }

    public async Task<List<AdminAccountDto>> GetAdminAccountsAsync()
    {
        var users = await _userRepository.GetAllAsync();
        var roles = await EnsureAndGetRolesAsync();

        return users
            .Where(u => !IsPlayerAccount(u))
            .OrderByDescending(u => string.Equals(AdminSecurityHelper.NormalizeRoleName(u.Role), "Admin", StringComparison.OrdinalIgnoreCase))
            .ThenBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(u => MapAccountToDto(u, roles))
            .ToList();
    }

    public async Task<ServiceResult<AdminAccountDto>> CreateAdminAccountAsync(
        CreateAdminAccountRequestDto dto,
        string actorUsername,
        string actorRole)
    {
        var username = dto.Username?.Trim() ?? string.Empty;
        if (username.Length < 3 || username.Length > 50 || username.Any(char.IsWhiteSpace))
        {
            return new(false, 400, ErrorMessage: "Username must be 3-50 characters with no whitespace.");
        }

        if (string.IsNullOrEmpty(dto.Password) || dto.Password.Length < 8)
        {
            return new(false, 400, ErrorMessage: "Initial passcode must be at least 8 characters.");
        }

        var roles = await EnsureAndGetRolesAsync();
        var normalizedRole = AdminSecurityHelper.NormalizeRoleName(dto.Role);
        if (string.IsNullOrEmpty(normalizedRole) ||
            string.Equals(normalizedRole, "Player", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, 400, ErrorMessage: "Cannot create a Player account through High Counsel provisioning.");
        }

        var matchedRole = roles.FirstOrDefault(r =>
            string.Equals(AdminSecurityHelper.NormalizeRoleName(r.Name), normalizedRole, StringComparison.OrdinalIgnoreCase));

        var canonicalRole = matchedRole?.Name
            ?? (string.Equals(normalizedRole, "Server", StringComparison.OrdinalIgnoreCase) ? "Server" : null);

        if (canonicalRole == null)
        {
            return new(false, 400, ErrorMessage: $"Role '{dto.Role}' does not exist.");
        }

        var existingUsers = await _userRepository.GetAllAsync();
        if (existingUsers.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            return new(false, 409, ErrorMessage: $"Account username '{username}' already exists.");
        }

        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        var customPerms = AdminSecurityHelper.SanitizePermissions(dto.CustomPermissions);
        var newUser = new User
        {
            Username = username,
            Email = email,
            PasswordHash = AdminSecurityHelper.HashPassword(dto.Password),
            Role = canonicalRole,
            HasPassword = true,
            AccountStatus = "ACTIVE",
            TokenVersion = 0,
            CreatedAt = DateTime.UtcNow,
            CustomPermissions = customPerms
        };

        await _userRepository.CreateAsync(newUser);

        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorUsername,
            ActorRole = actorRole,
            Action = "ADMIN_ACCOUNT_CREATED",
            Category = "SECURITY",
            Severity = "HIGH",
            TargetType = "Account",
            TargetId = newUser.Id,
            TargetLabel = newUser.Username,
            Rationale = dto.Rationale?.Trim(),
            AfterStateJson = AdminSecurityHelper.SerializeObjectState(new
            {
                username = newUser.Username,
                email = newUser.Email,
                role = newUser.Role,
                status = newUser.AccountStatus
            }),
            Status = "SUCCESS"
        });

        return new(true, 201, MapAccountToDto(newUser, roles));
    }

    public async Task<ServiceResult<AdminAccountDto>> UpdateAccountRoleAsync(
        string accountId,
        string actorId,
        UpdateAdminAccountRoleRequestDto dto,
        string actorUsername,
        string actorRole)
    {
        var target = await FindStaffUserByIdAsync(accountId);
        if (target == null)
        {
            return new(false, 404, ErrorMessage: $"Admin account '{accountId}' was not found.");
        }

        var roles = await EnsureAndGetRolesAsync();
        var normalizedNewRole = AdminSecurityHelper.NormalizeRoleName(dto.Role);
        if (string.IsNullOrEmpty(normalizedNewRole) ||
            string.Equals(normalizedNewRole, "Player", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, 400, ErrorMessage: "Target role cannot be empty or Player.");
        }

        var matchedRole = roles.FirstOrDefault(r =>
            string.Equals(AdminSecurityHelper.NormalizeRoleName(r.Name), normalizedNewRole, StringComparison.OrdinalIgnoreCase));

        var canonicalRole = matchedRole?.Name
            ?? (string.Equals(normalizedNewRole, "Server", StringComparison.OrdinalIgnoreCase) ? "Server" : null);

        if (canonicalRole == null)
        {
            return new(false, 400, ErrorMessage: $"Role '{dto.Role}' does not exist.");
        }

        var oldRole = target.Role;
        if (target.Id == actorId && !string.Equals(oldRole, canonicalRole, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, 400, ErrorMessage: "Cannot change your own role during an active session.");
        }

        if (IsHighCounselAdmin(oldRole) && !IsHighCounselAdmin(canonicalRole))
        {
            var otherActiveAdmins = await CountOtherActiveAdminsAsync(target.Id);
            if (otherActiveAdmins == 0)
            {
                return new(false, 400, ErrorMessage: "Cannot demote the last active High Counsel Admin account.");
            }
        }

        target.Role = canonicalRole;
        target.TokenVersion += 1;
        await _userRepository.UpdateAsync(target.Id, target);

        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorUsername,
            ActorRole = actorRole,
            Action = "ADMIN_ROLE_ASSIGNED",
            Category = "SECURITY",
            Severity = "CRITICAL",
            TargetType = "Account",
            TargetId = target.Id,
            TargetLabel = target.Username,
            Rationale = dto.Rationale?.Trim(),
            BeforeStateJson = AdminSecurityHelper.SerializeObjectState(new { role = oldRole }),
            AfterStateJson = AdminSecurityHelper.SerializeObjectState(new { role = target.Role }),
            Status = "SUCCESS"
        });

        return new(true, 200, MapAccountToDto(target, roles));
    }

    public async Task<ServiceResult<AdminAccountDto>> UpdateAccountStatusAsync(
        string accountId,
        string actorId,
        UpdateAdminAccountStatusRequestDto dto,
        string actorUsername,
        string actorRole)
    {
        var target = await FindStaffUserByIdAsync(accountId);
        if (target == null)
        {
            return new(false, 404, ErrorMessage: $"Admin account '{accountId}' was not found.");
        }

        var requestedUpper = (dto.Status ?? string.Empty).Trim().ToUpperInvariant();
        if (!AdminSecurityHelper.ValidAccountStatuses.Contains(requestedUpper))
        {
            return new(false, 400, ErrorMessage: "Status must be ACTIVE, LOCKED, or DISABLED.");
        }

        if (target.Id == actorId && requestedUpper != "ACTIVE")
        {
            return new(false, 400, ErrorMessage: "Cannot lock or disable your own currently active account.");
        }

        if (IsHighCounselAdmin(target.Role) && requestedUpper != "ACTIVE")
        {
            var otherActiveAdmins = await CountOtherActiveAdminsAsync(target.Id);
            if (otherActiveAdmins == 0)
            {
                return new(false, 400, ErrorMessage: "Cannot lock or disable the last active High Counsel Admin account.");
            }
        }

        var oldStatus = AdminSecurityHelper.NormalizeAccountStatus(target.AccountStatus);
        target.AccountStatus = requestedUpper;
        if (requestedUpper != "ACTIVE")
        {
            // Force immediate token invalidation when an account is locked or disabled
            target.TokenVersion += 1;
        }

        await _userRepository.UpdateAsync(target.Id, target);
        var roles = await EnsureAndGetRolesAsync();

        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorUsername,
            ActorRole = actorRole,
            Action = "ADMIN_ACCOUNT_STATUS_CHANGED",
            Category = "SECURITY",
            Severity = requestedUpper == "ACTIVE" ? "HIGH" : "CRITICAL",
            TargetType = "Account",
            TargetId = target.Id,
            TargetLabel = target.Username,
            Rationale = dto.Rationale?.Trim(),
            BeforeStateJson = AdminSecurityHelper.SerializeObjectState(new { status = oldStatus }),
            AfterStateJson = AdminSecurityHelper.SerializeObjectState(new { status = requestedUpper }),
            Status = "SUCCESS"
        });

        return new(true, 200, MapAccountToDto(target, roles));
    }

    public async Task<ServiceResult<object>> ResetAccountPasswordAsync(
        string accountId,
        string actorId,
        ResetAdminPasswordRequestDto dto,
        string actorUsername,
        string actorRole)
    {
        var target = await FindStaffUserByIdAsync(accountId);
        if (target == null)
        {
            return new(false, 404, ErrorMessage: $"Admin account '{accountId}' was not found.");
        }

        if (string.IsNullOrEmpty(dto.NewPassword) || dto.NewPassword.Length < 8)
        {
            return new(false, 400, ErrorMessage: "Temporary passcode must be at least 8 characters.");
        }

        target.PasswordHash = AdminSecurityHelper.HashPassword(dto.NewPassword);
        target.HasPassword = true;
        if (target.Id != actorId)
        {
            target.TokenVersion += 1;
        }

        await _userRepository.UpdateAsync(target.Id, target);

        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorUsername,
            ActorRole = actorRole,
            Action = "ADMIN_PASSWORD_RESET",
            Category = "SECURITY",
            Severity = "CRITICAL",
            TargetType = "Account",
            TargetId = target.Id,
            TargetLabel = target.Username,
            Rationale = dto.Rationale?.Trim(),
            AfterStateJson = AdminSecurityHelper.SerializeObjectState(new
            {
                sessionsRevoked = target.Id != actorId,
                tokenVersion = target.TokenVersion
            }),
            Status = "SUCCESS"
        });

        return new(true, 200, new { message = $"Passcode reset completed for '{target.Username}'." });
    }

    public async Task<ServiceResult<object>> RevokeAccountSessionsAsync(
        string accountId,
        RevokeSessionsRequestDto? dto,
        string actorUsername,
        string actorRole)
    {
        var target = await FindStaffUserByIdAsync(accountId);
        if (target == null)
        {
            return new(false, 404, ErrorMessage: $"Admin account '{accountId}' was not found.");
        }

        var previousVersion = target.TokenVersion;
        target.TokenVersion += 1;
        await _userRepository.UpdateAsync(target.Id, target);

        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorUsername,
            ActorRole = actorRole,
            Action = "ADMIN_SESSIONS_REVOKED",
            Category = "SECURITY",
            Severity = "HIGH",
            TargetType = "Account",
            TargetId = target.Id,
            TargetLabel = target.Username,
            Rationale = dto?.Rationale?.Trim(),
            BeforeStateJson = AdminSecurityHelper.SerializeObjectState(new { tokenVersion = previousVersion }),
            AfterStateJson = AdminSecurityHelper.SerializeObjectState(new { tokenVersion = target.TokenVersion }),
            Status = "SUCCESS"
        });

        return new(true, 200, new
        {
            message = $"All active sessions revoked for '{target.Username}'.",
            tokenVersion = target.TokenVersion
        });
    }

    public async Task<List<AdminRoleDto>> GetRolesAsync()
    {
        var roles = await EnsureAndGetRolesAsync();
        var users = await _userRepository.GetAllAsync();
        var staffUsers = users.Where(u => !IsPlayerAccount(u)).ToList();

        return roles
            .OrderByDescending(r => r.IsSystemDefault)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => MapRoleToDto(r, staffUsers))
            .ToList();
    }

    public async Task<ServiceResult<AdminRoleDto>> CreateRoleAsync(
        CreateAdminRoleRequestDto dto,
        string actorUsername,
        string actorRole)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length < 2 || name.Length > 50)
        {
            return new(false, 400, ErrorMessage: "Role name must be 2-50 characters.");
        }

        var normalized = AdminSecurityHelper.NormalizeRoleName(name);
        if (string.Equals(normalized, "Player", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Server", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, 400, ErrorMessage: $"Role name '{name}' is reserved.");
        }

        var existingRoles = await EnsureAndGetRolesAsync();
        if (existingRoles.Any(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(AdminSecurityHelper.NormalizeRoleName(r.Name), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return new(false, 409, ErrorMessage: $"Role '{name}' already exists.");
        }

        var sanitizedPerms = AdminSecurityHelper.SanitizePermissions(dto.Permissions);
        if (sanitizedPerms.Count == 0)
        {
            return new(false, 400, ErrorMessage: "Select at least one valid permission for the custom role.");
        }

        var created = new AdminRoleDefinition
        {
            Name = name,
            Description = dto.Description?.Trim() ?? string.Empty,
            Permissions = sanitizedPerms,
            IsSystemDefault = false,
            UpdatedAt = DateTime.UtcNow
        };

        await _roleRepository.CreateAsync(created);

        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorUsername,
            ActorRole = actorRole,
            Action = "ROLE_CREATED",
            Category = "SECURITY",
            Severity = "HIGH",
            TargetType = "Role",
            TargetId = created.Id,
            TargetLabel = created.Name,
            Rationale = dto.Rationale?.Trim(),
            AfterStateJson = AdminSecurityHelper.SerializeObjectState(new
            {
                name = created.Name,
                permissions = created.Permissions
            }),
            Status = "SUCCESS"
        });

        return new(true, 201, new AdminRoleDto(
            created.Id,
            created.Name,
            created.Description,
            created.Permissions,
            created.IsSystemDefault,
            0,
            created.UpdatedAt));
    }

    public async Task<ServiceResult<AdminRoleDto>> UpdateRolePermissionsAsync(
        string roleId,
        UpdateAdminRolePermissionsRequestDto dto,
        string actorUsername,
        string actorRole)
    {
        var roles = await EnsureAndGetRolesAsync();
        var role = FindRoleByIdOrAlias(roles, roleId);
        if (role == null)
        {
            return new(false, 404, ErrorMessage: $"Role '{roleId}' was not found.");
        }

        var sanitized = AdminSecurityHelper.SanitizePermissions(dto.Permissions);
        if (string.Equals(AdminSecurityHelper.NormalizeRoleName(role.Name), "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var mergedAdmin = new HashSet<string>(sanitized, StringComparer.OrdinalIgnoreCase);
            foreach (var required in AdminSecurityHelper.AdminCriticalPermissions)
            {
                mergedAdmin.Add(required);
            }
            sanitized = AdminSecurityHelper.AllPermissions.Where(mergedAdmin.Contains).ToList();
        }

        var oldPermissions = new List<string>(role.Permissions);
        role.Permissions = sanitized;
        if (!string.IsNullOrWhiteSpace(dto.Description))
        {
            role.Description = dto.Description.Trim();
        }
        role.UpdatedAt = DateTime.UtcNow;

        await _roleRepository.UpdateAsync(role.Id, role);

        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepository, new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorUsername,
            ActorRole = actorRole,
            Action = "ROLE_PERMISSIONS_UPDATED",
            Category = "SECURITY",
            Severity = "CRITICAL",
            TargetType = "Role",
            TargetId = role.Id,
            TargetLabel = role.Name,
            Rationale = dto.Rationale?.Trim(),
            BeforeStateJson = AdminSecurityHelper.SerializeObjectState(new { permissions = oldPermissions }),
            AfterStateJson = AdminSecurityHelper.SerializeObjectState(new { permissions = role.Permissions }),
            Status = "SUCCESS"
        });

        var users = await _userRepository.GetAllAsync();
        var staffUsers = users.Where(u => !IsPlayerAccount(u)).ToList();
        return new(true, 200, MapRoleToDto(role, staffUsers));
    }

    public async Task<List<AdminAuditLogDto>> GetAuditLogsAsync(
        string? category,
        string? severity,
        string? actor,
        string? search,
        int limit = 200)
    {
        var allLogs = await _auditRepository.GetAllAsync();
        IEnumerable<AdminAuditLog> query = allLogs;

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            query = query.Where(l => string.Equals(l.Category, cat, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            var sev = severity.Trim();
            query = query.Where(l => string.Equals(l.Severity, sev, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(actor))
        {
            var actorTerm = actor.Trim();
            query = query.Where(l =>
                !string.IsNullOrEmpty(l.ActorUsername) &&
                l.ActorUsername.Contains(actorTerm, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l =>
                (!string.IsNullOrEmpty(l.Action) && l.Action.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(l.ActorUsername) && l.ActorUsername.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(l.TargetLabel) && l.TargetLabel.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(l.TargetId) && l.TargetId.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(l.Rationale) && l.Rationale.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var capped = Math.Clamp(limit, 1, 500);
        return query
            .OrderByDescending(l => l.Timestamp)
            .Take(capped)
            .Select(AdminSecurityHelper.MapAuditLogToDto)
            .ToList();
    }

    public async Task<ServiceResult<AdminAuditLogDto>> RecordAuditLogAsync(
        CreateAdminAuditLogRequestDto dto,
        string? authenticatedUsername,
        string? authenticatedRole)
    {
        if (string.IsNullOrWhiteSpace(dto.Action))
        {
            return new(false, 400, ErrorMessage: "Audit action is required.");
        }

        var categoryUpper = (dto.Category ?? string.Empty).Trim().ToUpperInvariant();
        var category = AdminSecurityHelper.ValidAuditCategories.Contains(categoryUpper)
            ? categoryUpper
            : "SECURITY";

        var severityUpper = (dto.Severity ?? string.Empty).Trim().ToUpperInvariant();
        var severity = AdminSecurityHelper.ValidAuditSeverities.Contains(severityUpper)
            ? severityUpper
            : "MEDIUM";

        var statusUpper = string.Equals(dto.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
            ? "FAILED"
            : "SUCCESS";

        var actorName = !string.IsNullOrWhiteSpace(authenticatedUsername)
            ? authenticatedUsername.Trim()
            : (!string.IsNullOrWhiteSpace(dto.ActorUsername) ? dto.ActorUsername.Trim() : "Unknown Admin");

        var actorRoleValue = !string.IsNullOrWhiteSpace(authenticatedRole)
            ? authenticatedRole.Trim()
            : (!string.IsNullOrWhiteSpace(dto.ActorRole) ? dto.ActorRole.Trim() : "Admin");

        var entity = new AdminAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actorName,
            ActorRole = actorRoleValue,
            Action = dto.Action.Trim(),
            Category = category,
            Severity = severity,
            TargetType = string.IsNullOrWhiteSpace(dto.TargetType) ? null : dto.TargetType.Trim(),
            TargetId = string.IsNullOrWhiteSpace(dto.TargetId) ? null : dto.TargetId.Trim(),
            TargetLabel = string.IsNullOrWhiteSpace(dto.TargetLabel) ? null : dto.TargetLabel.Trim(),
            Rationale = string.IsNullOrWhiteSpace(dto.Rationale) ? null : dto.Rationale.Trim(),
            BeforeStateJson = AdminSecurityHelper.SerializeState(dto.BeforeState),
            AfterStateJson = AdminSecurityHelper.SerializeState(dto.AfterState),
            Status = statusUpper,
            ErrorMessage = string.IsNullOrWhiteSpace(dto.ErrorMessage) ? null : dto.ErrorMessage.Trim()
        };

        var saved = await AdminSecurityHelper.RecordAuditWithDedupeAsync(_auditRepository, entity);
        return new(true, 200, AdminSecurityHelper.MapAuditLogToDto(saved));
    }

    public async Task<List<AdminLoginHistoryDto>> GetLoginHistoryAsync(
        string? status,
        string? username,
        int limit = 200)
    {
        var all = await _loginHistoryRepository.GetAllAsync();
        IEnumerable<AdminLoginHistory> query = all;

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusTrim = status.Trim();
            query = query.Where(h => string.Equals(h.Status, statusTrim, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var userTrim = username.Trim();
            query = query.Where(h =>
                !string.IsNullOrEmpty(h.Username) &&
                h.Username.Contains(userTrim, StringComparison.OrdinalIgnoreCase));
        }

        var capped = Math.Clamp(limit, 1, 500);
        return query
            .OrderByDescending(h => h.Timestamp)
            .Take(capped)
            .Select(h => new AdminLoginHistoryDto(
                h.Id,
                h.Timestamp,
                h.Username,
                h.AttemptedRole,
                h.ResolvedRole,
                h.Status,
                h.IpAddress,
                h.UserAgent,
                h.Reason))
            .ToList();
    }

    private async Task<List<AdminRoleDefinition>> EnsureAndGetRolesAsync()
    {
        var roles = await _roleRepository.GetAllAsync();
        if (roles.Count > 0) return roles;

        foreach (var def in AdminSecurityHelper.GetDefaultRoleDefinitions())
        {
            await _roleRepository.CreateAsync(def);
        }

        return await _roleRepository.GetAllAsync();
    }

    private async Task<User?> FindStaffUserByIdAsync(string accountId)
    {
        if (!ObjectId.TryParse(accountId, out _)) return null;
        var user = await _userRepository.GetByIdAsync(accountId);
        if (user == null || IsPlayerAccount(user)) return null;
        return user;
    }

    private async Task<int> CountOtherActiveAdminsAsync(string excludedUserId)
    {
        var allUsers = await _userRepository.GetAllAsync();
        return allUsers.Count(u =>
            u.Id != excludedUserId &&
            !u.IsBanned &&
            IsHighCounselAdmin(u.Role) &&
            AdminSecurityHelper.NormalizeAccountStatus(u.AccountStatus) == "ACTIVE");
    }

    private static bool IsPlayerAccount(User user) =>
        string.Equals(AdminSecurityHelper.NormalizeRoleName(user.Role), "Player", StringComparison.OrdinalIgnoreCase);

    private static bool IsHighCounselAdmin(string? role) =>
        string.Equals(AdminSecurityHelper.NormalizeRoleName(role), "Admin", StringComparison.OrdinalIgnoreCase);

    private static AdminRoleDefinition? FindRoleByIdOrAlias(List<AdminRoleDefinition> roles, string roleId)
    {
        var byId = roles.FirstOrDefault(r => string.Equals(r.Id, roleId, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;

        var aliasName = roleId.Trim().ToLowerInvariant() switch
        {
            "role-admin" => "Admin",
            "role-moderator" => "Moderator",
            "role-tech-support" => "Technical Support",
            _ => roleId.Trim()
        };

        return roles.FirstOrDefault(r =>
            string.Equals(r.Name, aliasName, StringComparison.OrdinalIgnoreCase));
    }

    private static AdminAccountDto MapAccountToDto(User user, IReadOnlyList<AdminRoleDefinition> roles) => new(
        user.Id,
        user.Username,
        user.Email,
        user.Role,
        AdminSecurityHelper.NormalizeAccountStatus(user.AccountStatus),
        user.CreatedAt == default ? DateTime.UtcNow : user.CreatedAt,
        user.LastLoginAt,
        user.LastLoginIp,
        AdminSecurityHelper.SanitizePermissions(user.CustomPermissions),
        AdminSecurityHelper.ResolveEffectivePermissions(user, roles));

    private static AdminRoleDto MapRoleToDto(AdminRoleDefinition role, List<User> staffUsers)
    {
        var normalizedTarget = AdminSecurityHelper.NormalizeRoleName(role.Name);
        var count = staffUsers.Count(u =>
            string.Equals(AdminSecurityHelper.NormalizeRoleName(u.Role), normalizedTarget, StringComparison.OrdinalIgnoreCase));

        return new AdminRoleDto(
            role.Id,
            role.Name,
            role.Description,
            role.Permissions,
            role.IsSystemDefault,
            count,
            role.UpdatedAt);
    }
}
