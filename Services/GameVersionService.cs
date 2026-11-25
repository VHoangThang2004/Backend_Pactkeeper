using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class GameVersionService : IGameVersionService
{
    private readonly IMongoRepository<GameVersionConfig> _versionRepo;
    private readonly IMongoRepository<AdminAuditLog> _auditRepo;

    public GameVersionService(
        IMongoRepository<GameVersionConfig> versionRepo,
        IMongoRepository<AdminAuditLog> auditRepo)
    {
        _versionRepo = versionRepo;
        _auditRepo = auditRepo;
    }

    public async Task<GameVersionDto> GetVersionConfigAsync()
    {
        var all = await _versionRepo.GetAllAsync();
        var config = all.FirstOrDefault();

        if (config == null)
        {
            // Seed a default config if none exists yet
            config = new GameVersionConfig
            {
                LatestVersion = "1.0.0",
                MinSupportedVersion = "1.0.0",
                DownloadUrl = string.Empty,
                PatchNotes = "Initial release.",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "System"
            };
            await _versionRepo.CreateAsync(config);
        }

        return new GameVersionDto
        {
            LatestVersion = config.LatestVersion,
            MinSupportedVersion = config.MinSupportedVersion,
            DownloadUrl = config.DownloadUrl,
            PatchNotes = config.PatchNotes,
            UpdatedAt = config.UpdatedAt
        };
    }

    public async Task<GameVersionDto> UpdateVersionConfigAsync(UpdateGameVersionDto dto, string? adminUsername)
    {
        var all = await _versionRepo.GetAllAsync();
        var config = all.FirstOrDefault();

        var now = DateTime.UtcNow;
        string actor = string.IsNullOrWhiteSpace(adminUsername) ? "Admin" : adminUsername;

        if (config == null)
        {
            config = new GameVersionConfig
            {
                LatestVersion = dto.LatestVersion,
                MinSupportedVersion = dto.MinSupportedVersion,
                DownloadUrl = dto.DownloadUrl ?? string.Empty,
                PatchNotes = dto.PatchNotes ?? string.Empty,
                UpdatedAt = now,
                UpdatedBy = actor
            };
            await _versionRepo.CreateAsync(config);
        }
        else
        {
            config.LatestVersion = dto.LatestVersion;
            config.MinSupportedVersion = dto.MinSupportedVersion;
            config.DownloadUrl = dto.DownloadUrl ?? string.Empty;
            config.PatchNotes = dto.PatchNotes ?? string.Empty;
            config.UpdatedAt = now;
            config.UpdatedBy = actor;
            await _versionRepo.UpdateAsync(config.Id, config);
        }

        // Record AdminAuditLog
        await AdminSecurityHelper.TryRecordAuditAsync(_auditRepo, new AdminAuditLog
        {
            Category = "Deployment",
            Action = "UpdateGameVersion",
            TargetId = config.Id,
            TargetType = "GameVersionConfig",
            TargetLabel = $"v{dto.LatestVersion}",
            ActorUsername = actor,
            ActorRole = "Admin",
            Rationale = $"Updated GameVersion to {dto.LatestVersion} (Min: {dto.MinSupportedVersion}, Download: {dto.DownloadUrl})",
            Timestamp = now
        });

        return new GameVersionDto
        {
            LatestVersion = config.LatestVersion,
            MinSupportedVersion = config.MinSupportedVersion,
            DownloadUrl = config.DownloadUrl,
            PatchNotes = config.PatchNotes,
            UpdatedAt = config.UpdatedAt
        };
    }
}
