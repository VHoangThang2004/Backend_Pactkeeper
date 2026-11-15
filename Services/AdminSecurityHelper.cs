using System.Security.Cryptography;
using System.Text.Json;
using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

/// <summary>
/// Shared cryptographic password hashing, RBAC catalog defaults, and audit/telemetry helpers.
/// </summary>
public static class AdminSecurityHelper
{
    private const string HashPrefix = "pbkdf2";
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    public static readonly string[] AllPermissions =
    [
        "dashboard.view",
        "server.killswitch",
        "players.view",
        "players.ban",
        "treasury.adjust",
        "matches.view",
        "matches.logs",
        "gacha.manage",
        "content.manage",
        "payments.view",
        "payments.manage",
        "support.chat",
        "security.accounts",
        "security.roles",
        "security.audit"
    ];

    public static readonly string[] AdminCriticalPermissions =
    [
        "security.accounts",
        "security.roles",
        "security.audit"
    ];

    public static readonly HashSet<string> ValidAccountStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACTIVE",
        "LOCKED",
        "DISABLED"
    };

    public static readonly HashSet<string> ValidAuditCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUTH",
        "SERVER",
        "PLAYER",
        "TREASURY",
        "GACHA",
        "CONTENT",
        "PAYMENT",
        "SECURITY"
    };

    public static readonly HashSet<string> ValidAuditSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "CRITICAL",
        "HIGH",
        "MEDIUM",
        "LOW"
    };

    public static IReadOnlyList<AdminRoleDefinition> GetDefaultRoleDefinitions() =>
    [
        new AdminRoleDefinition
        {
            Name = "Admin",
            Description = "High Counsel sovereign authority across treasury, accounts, security governance, and emergency controls.",
            Permissions = [.. AllPermissions],
            IsSystemDefault = true,
            UpdatedAt = DateTime.UtcNow
        },
        new AdminRoleDefinition
        {
            Name = "Moderator",
            Description = "Realm Game Master supervising PvP matches, Gacha banners, SRPG definitions, and player disciplinary actions.",
            Permissions =
            [
                "players.view",
                "players.ban",
                "matches.view",
                "matches.logs",
                "gacha.manage",
                "content.manage"
            ],
            IsSystemDefault = true,
            UpdatedAt = DateTime.UtcNow
        },
        new AdminRoleDefinition
        {
            Name = "Technical Support",
            Description = "Infrastructure & live support specialist monitoring dedicated nodes, VPS combat logs, emergency overrides, and live player chat.",
            Permissions =
            [
                "dashboard.view",
                "server.killswitch",
                "matches.view",
                "matches.logs",
                "support.chat"
            ],
            IsSystemDefault = true,
            UpdatedAt = DateTime.UtcNow
        }
    ];

    public static string NormalizeRoleName(string? rawRole)
    {
        var trimmed = (rawRole ?? string.Empty).Trim();
        var lower = trimmed.ToLowerInvariant();
        return lower switch
        {
            "admin" => "Admin",
            "server" => "Server",
            "moderator" or "mod" => "Moderator",
            "technical support" or "technicalsupport" or "techsupport" or "support" or "cskh" => "Technical Support",
            "player" => "Player",
            _ => trimmed
        };
    }

    public static string NormalizeAccountStatus(string? rawStatus)
    {
        var upper = (rawStatus ?? string.Empty).Trim().ToUpperInvariant();
        return ValidAccountStatuses.Contains(upper) ? upper : "ACTIVE";
    }

    public static List<string> SanitizePermissions(IEnumerable<string>? permissions)
    {
        if (permissions == null) return [];
        var validSet = new HashSet<string>(AllPermissions, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in permissions)
        {
            var trimmed = raw?.Trim();
            if (string.IsNullOrEmpty(trimmed) || !validSet.Contains(trimmed)) continue;
            var canonical = AllPermissions.First(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
            if (seen.Add(canonical))
            {
                result.Add(canonical);
            }
        }

        return result;
    }

    public static List<string> GetDefaultPermissionsForRole(string? role)
    {
        var normalized = NormalizeRoleName(role);
        if (string.Equals(normalized, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Server", StringComparison.OrdinalIgnoreCase))
        {
            return [.. AllPermissions];
        }

        var defaultRole = GetDefaultRoleDefinitions()
            .FirstOrDefault(r => string.Equals(r.Name, normalized, StringComparison.OrdinalIgnoreCase));

        return defaultRole != null ? [.. defaultRole.Permissions] : [];
    }

    /// <summary>
    /// Resolves effective portal permissions from the stored role definition merged with per-account overrides.
    /// </summary>
    public static List<string> ResolveEffectivePermissions(User user, IReadOnlyList<AdminRoleDefinition> roles)
    {
        var normalizedRole = NormalizeRoleName(user.Role);
        if (string.Equals(normalizedRole, "Player", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedRole, "Server", StringComparison.OrdinalIgnoreCase))
        {
            return [.. AllPermissions];
        }

        var matchedRole = roles.FirstOrDefault(r =>
            string.Equals(NormalizeRoleName(r.Name), normalizedRole, StringComparison.OrdinalIgnoreCase));

        var basePermissions = matchedRole != null
            ? SanitizePermissions(matchedRole.Permissions)
            : GetDefaultPermissionsForRole(normalizedRole);

        var merged = new HashSet<string>(basePermissions, StringComparer.OrdinalIgnoreCase);
        foreach (var custom in SanitizePermissions(user.CustomPermissions))
        {
            merged.Add(custom);
        }

        return AllPermissions.Where(merged.Contains).ToList();
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);

        return $"{HashPrefix}${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies PBKDF2-SHA256 hashes while preserving legacy plaintext password verification for seeded accounts.
    /// </summary>
    public static bool VerifyPassword(string inputPassword, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(inputPassword))
        {
            return false;
        }

        if (!storedHash.StartsWith($"{HashPrefix}$", StringComparison.Ordinal))
        {
            return string.Equals(storedHash, inputPassword, StringComparison.Ordinal);
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                inputPassword,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string? SerializeState(JsonElement? state)
    {
        if (!state.HasValue ||
            state.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return state.Value.GetRawText();
    }

    public static string? SerializeObjectState(object? state)
    {
        return state == null ? null : JsonSerializer.Serialize(state);
    }

    public static JsonElement? DeserializeState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static AdminAuditLogDto MapAuditLogToDto(AdminAuditLog log) => new(
        log.Id,
        log.Timestamp,
        log.ActorUsername,
        log.ActorRole,
        log.Action,
        log.Category,
        log.Severity,
        log.TargetType,
        log.TargetId,
        log.TargetLabel,
        log.Rationale,
        DeserializeState(log.BeforeStateJson),
        DeserializeState(log.AfterStateJson),
        log.Status,
        log.ErrorMessage);

    public static async Task<AdminAuditLog> RecordAuditWithDedupeAsync(
        IMongoRepository<AdminAuditLog> auditRepo,
        AdminAuditLog log)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-5);
        var recent = await auditRepo.GetAllByFilterAsync(l =>
            l.Action == log.Action &&
            l.ActorUsername == log.ActorUsername &&
            l.Timestamp >= cutoff);

        var match = recent.FirstOrDefault(l =>
            string.Equals(l.Status, log.Status, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(l.TargetLabel ?? string.Empty, log.TargetLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(l.TargetId ?? string.Empty, log.TargetId ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(log.Action, "ADMIN_LOGOUT", StringComparison.OrdinalIgnoreCase)));

        if (match != null)
        {
            return match;
        }

        await auditRepo.CreateAsync(log);
        return log;
    }

    public static async Task TryRecordAuditAsync(
        IMongoRepository<AdminAuditLog> auditRepo,
        AdminAuditLog log)
    {
        try
        {
            await RecordAuditWithDedupeAsync(auditRepo, log);
        }
        catch
        {
            // Audit persistence must not crash primary workflow responses if transient storage error occurs
        }
    }
}
