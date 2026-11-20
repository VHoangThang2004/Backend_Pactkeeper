using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlayerController : ControllerBase
{
    private readonly IMongoRepository<User> _userRepository;
    private readonly IMongoRepository<PlayerProfile> _profileRepository;
    private readonly INotificationService _notificationService;
    private readonly IUserPresenceTracker _presenceTracker;
    private readonly IStoryProgressService _storyProgressService;
    private readonly IMongoRepository<StoryProgress> _storyRepository;
    private readonly ITeamLoadoutService _teamLoadoutService;
    private readonly IRewardProgressService _rewardProgressService;
    private readonly IRewardRuleService _rewardRuleService;
    private readonly IUnitDefinitionService _unitDefinitionService;
    private readonly IWeaponDefinitionService _weaponDefinitionService;
    private readonly ITrinketDefinitionService _trinketDefinitionService;

    public PlayerController(
        IMongoRepository<User> userRepository,
        IMongoRepository<PlayerProfile> profileRepository,
        INotificationService notificationService,
        IUserPresenceTracker presenceTracker,
        IStoryProgressService storyProgressService,
        IMongoRepository<StoryProgress> storyRepository,
        ITeamLoadoutService teamLoadoutService,
        IRewardProgressService rewardProgressService,
        IRewardRuleService rewardRuleService,
        IUnitDefinitionService unitDefinitionService,
        IWeaponDefinitionService weaponDefinitionService,
        ITrinketDefinitionService trinketDefinitionService)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _notificationService = notificationService;
        _presenceTracker = presenceTracker;
        _storyProgressService = storyProgressService;
        _storyRepository = storyRepository;
        _teamLoadoutService = teamLoadoutService;
        _rewardProgressService = rewardProgressService;
        _rewardRuleService = rewardRuleService;
        _unitDefinitionService = unitDefinitionService;
        _weaponDefinitionService = weaponDefinitionService;
        _trinketDefinitionService = trinketDefinitionService;
    }

    // GET /api/player/profile/{playerId}
    [HttpGet("profile/{playerId}")]
    public async Task<ActionResult<AdminPlayerProfileDto>> GetPlayerProfile(string playerId)
    {
        var trimmed = playerId.Trim();
        var lower = trimmed.ToLowerInvariant();

        var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == trimmed);
        if (profile == null)
        {
            var profiles = await _profileRepository.GetAllByFilterAsync(p => p.Username != null && p.Username.ToLower() == lower);
            profile = profiles.FirstOrDefault();
        }

        var user = await _userRepository.GetByFilterAsync(u => u.Id == trimmed);
        if (user == null)
        {
            var users = await _userRepository.GetAllByFilterAsync(u => u.Username != null && u.Username.ToLower() == lower);
            user = users.FirstOrDefault();
        }

        if (profile == null && user == null)
        {
            return NotFound(new { message = $"Player profile with ID '{playerId}' was not found." });
        }

        var isBanned = user?.IsBanned ?? profile?.IsBanned ?? false;
        var banReason = user?.BanReason ?? profile?.BanReason;
        var resolvedPlayerId = profile?.PlayerId ?? user?.Id ?? playerId;
        var resolvedLastLogin = profile?.LastLogin ?? user?.LastLoginAt ?? user?.CreatedAt ?? DateTime.UtcNow;
        if (user?.LastLoginAt is DateTime userLastLogin && userLastLogin > resolvedLastLogin)
        {
            resolvedLastLogin = userLastLogin;
        }

        return Ok(new AdminPlayerProfileDto
        {
            Id = profile?.Id ?? user?.Id ?? playerId,
            PlayerId = resolvedPlayerId,
            Username = profile?.Username ?? user?.Username ?? "Unknown Traveler",
            Level = profile?.Level ?? 1,
            Experience = profile?.Experience ?? 0,
            Gold = profile?.Gold ?? 0,
            Gems = profile?.Gems ?? 0,
            IsBanned = isBanned,
            BanReason = banReason,
            LastLogin = resolvedLastLogin,
            IsOnline = _presenceTracker.IsOnline(resolvedPlayerId),
            PresenceStatus = _presenceTracker.GetStatus(resolvedPlayerId),
            OwnedUnitsCount = profile?.OwnedUnits.Count ?? 0,
            OwnedWeaponsCount = profile?.OwnedWeapons.Count ?? 0,
            OwnedTrinketsCount = profile?.OwnedTrinkets.Count ?? 0
        });
    }

    // GET /api/player/all - Admin and Moderator can list all registered players
    [HttpGet("all")]
    [Authorize(Roles = "Admin,Moderator,Technical Support,Support")]
    public async Task<ActionResult<List<AdminPlayerProfileDto>>> GetAllPlayers()
    {
        var profiles = await _profileRepository.GetAllAsync();
        var users = await _userRepository.GetAllAsync();
        var userMap = users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First());

        var list = new List<AdminPlayerProfileDto>();
        var seenIds = new HashSet<string>();

        foreach (var p in profiles)
        {
            seenIds.Add(p.PlayerId);
            userMap.TryGetValue(p.PlayerId, out var u);
            var lastLogin = p.LastLogin;
            if (u?.LastLoginAt is DateTime userLastLogin && userLastLogin > lastLogin)
            {
                lastLogin = userLastLogin;
            }

            list.Add(new AdminPlayerProfileDto
            {
                Id = p.Id,
                PlayerId = p.PlayerId,
                Username = p.Username,
                Level = p.Level,
                Experience = p.Experience,
                Gold = p.Gold,
                Gems = p.Gems,
                IsBanned = p.IsBanned || (u?.IsBanned ?? false),
                BanReason = p.BanReason ?? u?.BanReason,
                LastLogin = lastLogin,
                IsOnline = _presenceTracker.IsOnline(p.PlayerId),
                PresenceStatus = _presenceTracker.GetStatus(p.PlayerId),
                OwnedUnitsCount = p.OwnedUnits.Count,
                OwnedWeaponsCount = p.OwnedWeapons.Count,
                OwnedTrinketsCount = p.OwnedTrinkets.Count
            });
        }

        // Include any user accounts that do not yet have a profile
        foreach (var u in users.Where(u => string.Equals(u.Role, "Player", StringComparison.OrdinalIgnoreCase) && !seenIds.Contains(u.Id)))
        {
            list.Add(new AdminPlayerProfileDto
            {
                Id = u.Id,
                PlayerId = u.Id,
                Username = u.Username,
                Level = 1,
                Experience = 0,
                Gold = 0,
                Gems = 0,
                IsBanned = u.IsBanned,
                BanReason = u.BanReason,
                LastLogin = u.LastLoginAt ?? u.CreatedAt,
                IsOnline = _presenceTracker.IsOnline(u.Id),
                PresenceStatus = _presenceTracker.GetStatus(u.Id),
                OwnedUnitsCount = 0,
                OwnedWeaponsCount = 0,
                OwnedTrinketsCount = 0
            });
        }

        return Ok(list);
    }

    // POST /api/player/adjust-currencies - Admin only
    [HttpPost("adjust-currencies")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdjustCurrencies([FromBody] AdjustCurrenciesDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PlayerId))
            return BadRequest(new { message = "Player ID is required." });

        var targetId = dto.PlayerId.Trim();
        var lower = targetId.ToLowerInvariant();

        var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == targetId);
        if (profile == null)
        {
            var profiles = await _profileRepository.GetAllByFilterAsync(p => p.Username != null && p.Username.ToLower() == lower);
            profile = profiles.FirstOrDefault();
        }

        if (profile == null)
        {
            var user = await _userRepository.GetByFilterAsync(u => u.Id == targetId);
            if (user == null)
            {
                var users = await _userRepository.GetAllByFilterAsync(u => u.Username != null && u.Username.ToLower() == lower);
                user = users.FirstOrDefault();
            }

            if (user == null)
                return NotFound(new { message = $"Player '{dto.PlayerId}' not found." });

            profile = new PlayerProfile
            {
                PlayerId = user.Id,
                Username = user.Username,
                Level = 1,
                Experience = 0,
                Gold = 0,
                Gems = 0,
                LastLogin = DateTime.UtcNow
            };
            await _profileRepository.CreateAsync(profile);
        }

        profile.Gems = Math.Max(0, profile.Gems + dto.Gems);
        profile.Gold = Math.Max(0, profile.Gold + dto.Gold);
        await _profileRepository.UpdateAsync(profile.Id, profile);

        // Notify player of treasury grant/deduction
        try
        {
            var reason = string.IsNullOrWhiteSpace(dto.Reason) ? "Admin Manual Treasury Adjustment" : dto.Reason;
            var changeText = $"{(dto.Gems >= 0 ? "+" : "")}{dto.Gems} Gems, {(dto.Gold >= 0 ? "+" : "")}{dto.Gold} Gold";
            await _notificationService.CreateNotificationAsync(
                profile.PlayerId,
                "info",
                "Treasury Adjustment",
                $"Your treasury balances were adjusted: {changeText}. Reason: {reason}."
            );
        }
        catch
        {
            // Non-critical if notification dispatch fails
        }

        return Ok(new
        {
            message = $"Successfully adjusted currencies for {profile.Username}.",
            playerId = profile.PlayerId,
            gems = profile.Gems,
            gold = profile.Gold
        });
    }

    // POST /api/player/ban - Admin & Moderator
    [HttpPost("ban")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> BanPlayer([FromBody] BanPlayerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PlayerId))
            return BadRequest(new { message = "Player ID is required." });

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { message = "Violation rationale is required to enforce an authoritative disciplinary sanction." });

        var targetId = dto.PlayerId.Trim();
        var lower = targetId.ToLowerInvariant();

        var users = await _userRepository.GetAllByFilterAsync(u => u.Id == targetId || (u.Username != null && u.Username.ToLower() == lower));
        var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == targetId);
        if (profile == null)
        {
            var profiles = await _profileRepository.GetAllByFilterAsync(p => p.Username != null && p.Username.ToLower() == lower);
            profile = profiles.FirstOrDefault();
        }

        if (!users.Any() && profile == null)
            return NotFound(new { message = $"Player '{dto.PlayerId}' not found." });

        var now = DateTime.UtcNow;
        foreach (var u in users)
        {
            u.IsBanned = true;
            u.BanReason = dto.Reason.Trim();
            u.BannedAt = now;
            u.BanDuration = dto.Duration ?? "Permanent";
            await _userRepository.UpdateAsync(u.Id, u);
        }

        if (profile != null)
        {
            profile.IsBanned = true;
            profile.BanReason = dto.Reason.Trim();
            profile.BannedAt = now;
            profile.BanDuration = dto.Duration ?? "Permanent";
            await _profileRepository.UpdateAsync(profile.Id, profile);
        }

        return Ok(new
        {
            message = $"Account sanction successfully enforced for player '{dto.PlayerId}'.",
            playerId = dto.PlayerId,
            reason = dto.Reason.Trim(),
            duration = dto.Duration ?? "Permanent"
        });
    }

    // POST /api/player/unban - Admin & Moderator
    [HttpPost("unban")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> UnbanPlayer([FromBody] UnbanPlayerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PlayerId))
            return BadRequest(new { message = "Player ID is required." });

        var targetId = dto.PlayerId.Trim();
        var lower = targetId.ToLowerInvariant();

        var users = await _userRepository.GetAllByFilterAsync(u => u.Id == targetId || (u.Username != null && u.Username.ToLower() == lower));
        var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == targetId);
        if (profile == null)
        {
            var profiles = await _profileRepository.GetAllByFilterAsync(p => p.Username != null && p.Username.ToLower() == lower);
            profile = profiles.FirstOrDefault();
        }

        if (!users.Any() && profile == null)
            return NotFound(new { message = $"Player '{dto.PlayerId}' not found." });

        foreach (var u in users)
        {
            u.IsBanned = false;
            u.BanReason = null;
            u.BannedAt = null;
            u.BanDuration = null;
            await _userRepository.UpdateAsync(u.Id, u);
        }

        if (profile != null)
        {
            profile.IsBanned = false;
            profile.BanReason = null;
            profile.BannedAt = null;
            profile.BanDuration = null;
            await _profileRepository.UpdateAsync(profile.Id, profile);
        }

        return Ok(new
        {
            message = $"Account access successfully restored for player '{dto.PlayerId}'.",
            playerId = dto.PlayerId
        });
    }

    // GET /api/player/details/{playerId} - Full profile, inventory, pity, story progress, loadout, and quest progress
    [HttpGet("details/{playerId}")]
    [Authorize(Roles = "Admin,Moderator,Technical Support,Support")]
    public async Task<ActionResult<AdminPlayerDetailsDto>> GetPlayerDetails(string playerId)
    {
        var (user, profile, resolvedPlayerId) = await ResolveUserAndProfileAsync(playerId);
        if (user == null && profile == null)
        {
            return NotFound(new { message = $"Player '{playerId}' was not found." });
        }

        var storyList = await _storyProgressService.GetAllAsync(resolvedPlayerId);
        var loadout = await _teamLoadoutService.GetByPlayerIdAsync(resolvedPlayerId);
        var rewardProgress = await _rewardProgressService.GetByPlayerIdAsync(resolvedPlayerId);
        var rules = await _rewardRuleService.GetAllAsync();
        var ruleMap = rules.ToDictionary(r => r.Id);

        var resolvedLastLogin = profile?.LastLogin ?? user?.LastLoginAt ?? user?.CreatedAt ?? DateTime.UtcNow;
        if (user?.LastLoginAt is DateTime userLastLogin && userLastLogin > resolvedLastLogin)
        {
            resolvedLastLogin = userLastLogin;
        }

        return Ok(new AdminPlayerDetailsDto
        {
            Id = profile?.Id ?? user?.Id ?? resolvedPlayerId,
            PlayerId = resolvedPlayerId,
            Username = profile?.Username ?? user?.Username ?? "Unknown Traveler",
            Level = profile?.Level ?? 1,
            Experience = profile?.Experience ?? 0,
            Gold = profile?.Gold ?? 0,
            Gems = profile?.Gems ?? 0,
            IsBanned = (user?.IsBanned ?? false) || (profile?.IsBanned ?? false),
            BanReason = profile?.BanReason ?? user?.BanReason,
            BanDuration = profile?.BanDuration ?? user?.BanDuration,
            LastLogin = resolvedLastLogin,
            IsOnline = _presenceTracker.IsOnline(resolvedPlayerId),
            PresenceStatus = _presenceTracker.GetStatus(resolvedPlayerId),
            OwnedUnitsCount = profile?.OwnedUnits.Count ?? 0,
            OwnedWeaponsCount = profile?.OwnedWeapons.Count ?? 0,
            OwnedTrinketsCount = profile?.OwnedTrinkets.Count ?? 0,
            Rating = profile?.Rating ?? 0,
            RankTier = profile?.RankTier ?? 0,
            RankWins = profile?.RankWins ?? 0,
            RankLosses = profile?.RankLosses ?? 0,
            OwnedUnits = profile?.OwnedUnits ?? [],
            OwnedWeapons = profile?.OwnedWeapons ?? [],
            OwnedTrinkets = profile?.OwnedTrinkets ?? [],
            GachaPityByBanner = profile?.GachaPityByBanner ?? new(),
            StoryProgress = storyList
                .OrderBy(s => s.ChapterId)
                .Select(s => new AdminStoryProgressItemDto
                {
                    ChapterId = s.ChapterId,
                    SceneId = s.SceneId,
                    IsCompleted = s.IsCompleted,
                    IsChapterCompleted = s.IsChapterCompleted,
                    StartedAt = s.StartedAt,
                    UpdatedAt = s.UpdatedAt,
                    CompletedAt = s.CompletedAt
                })
                .ToList(),
            TeamLoadoutUIds = loadout?.UIds ?? [],
            RewardProgress = rewardProgress
                .Select(rp =>
                {
                    ruleMap.TryGetValue(rp.RuleId, out var rule);
                    var ruleTitle = rule != null ? $"{rule.ActionType} ({rule.TargetCount})" : rp.RuleId;
                    return new AdminRewardProgressDto
                    {
                        Id = rp.Id,
                        RuleId = rp.RuleId,
                        RuleTitle = ruleTitle,
                        CurrentCount = rp.CurrentCount,
                        TargetCount = rule?.TargetCount,
                        IsCompleted = rp.IsCompleted,
                        CompletedAt = rp.CompletedAt,
                        IsReceived = rp.IsReceived,
                        ReceivedAt = rp.ReceivedAt
                    };
                })
                .ToList()
        });
    }

    // PUT /api/player/{playerId}/stats - Admin only
    [HttpPut("{playerId}/stats")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdatePlayerStats(string playerId, [FromBody] UpdatePlayerStatsDto dto)
    {
        var (user, profile, resolvedPlayerId) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null)
        {
            return NotFound(new { message = $"Player '{playerId}' not found." });
        }

        if (!string.IsNullOrWhiteSpace(dto.Username))
        {
            var nextUsername = dto.Username.Trim();
            profile.Username = nextUsername;
            if (user != null)
            {
                user.Username = nextUsername;
                await _userRepository.UpdateAsync(user.Id, user);
            }
        }

        profile.Level = Math.Max(1, dto.Level);
        profile.Experience = Math.Max(0, dto.Experience);
        profile.Gold = Math.Max(0, dto.Gold);
        profile.Gems = Math.Max(0, dto.Gems);
        profile.Rating = Math.Max(0, dto.Rating);
        profile.RankTier = Math.Max(0, dto.RankTier);
        profile.RankWins = Math.Max(0, dto.RankWins);
        profile.RankLosses = Math.Max(0, dto.RankLosses);

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return Ok(new { message = $"Updated stats for {profile.Username}.", playerId = resolvedPlayerId });
    }

    // POST /api/player/{playerId}/units - Admin only
    [HttpPost("{playerId}/units")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GrantUnit(string playerId, [FromBody] GrantPlayerUnitDto dto)
    {
        var (_, profile, _) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        var unitDef = await _unitDefinitionService.GetByUIdAsync(dto.UnitDefinitionUId);
        if (unitDef == null)
        {
            return BadRequest(new { message = $"UnitDefinition uId={dto.UnitDefinitionUId} not found." });
        }

        var existing = profile.OwnedUnits.FirstOrDefault(u => u.UnitDefinitionUId == dto.UnitDefinitionUId);
        if (existing != null)
        {
            existing.Grade = Math.Max(existing.Grade, Math.Max(1, dto.Grade));
            foreach (var cid in unitDef.ClassIds)
            {
                if (!existing.UnlockedClassIds.Contains(cid))
                {
                    existing.UnlockedClassIds.Add(cid);
                }
            }
        }
        else
        {
            profile.OwnedUnits.Add(new OwnedUnit
            {
                OwnedUnitId = Guid.NewGuid().ToString(),
                UnitDefinitionUId = dto.UnitDefinitionUId,
                Grade = Math.Max(1, dto.Grade),
                UnlockedClassIds = [.. unitDef.ClassIds],
                EquippedMovementSkillId = -1,
                EquippedOwnedWeaponId = string.Empty,
                EquippedOwnedTrinketId = string.Empty,
                EquippedClassSkillId = -1
            });
        }

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return Ok(new { message = $"Granted unit #{dto.UnitDefinitionUId}." });
    }

    // PATCH /api/player/{playerId}/units/{ownedUnitId} - Admin only
    [HttpPatch("{playerId}/units/{ownedUnitId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateOwnedUnit(string playerId, string ownedUnitId, [FromBody] UpdateOwnedUnitDto dto)
    {
        var (_, profile, _) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        var unit = profile.OwnedUnits.FirstOrDefault(u => u.OwnedUnitId == ownedUnitId);
        if (unit == null) return NotFound(new { message = $"Owned unit '{ownedUnitId}' not found." });

        unit.Grade = Math.Max(1, dto.Grade);
        if (dto.UnlockedClassIds != null)
        {
            unit.UnlockedClassIds = dto.UnlockedClassIds.Distinct().ToList();
        }

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return Ok(new { message = "Updated owned unit." });
    }

    // DELETE /api/player/{playerId}/units/{ownedUnitId} - Admin only
    [HttpDelete("{playerId}/units/{ownedUnitId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveOwnedUnit(string playerId, string ownedUnitId)
    {
        var (_, profile, resolvedPlayerId) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        var unit = profile.OwnedUnits.FirstOrDefault(u => u.OwnedUnitId == ownedUnitId);
        if (unit == null) return NotFound(new { message = $"Owned unit '{ownedUnitId}' not found." });

        profile.OwnedUnits.Remove(unit);
        await _profileRepository.UpdateAsync(profile.Id, profile);

        var loadout = await _teamLoadoutService.GetByPlayerIdAsync(resolvedPlayerId);
        if (loadout != null && loadout.UIds.Contains(unit.UnitDefinitionUId))
        {
            await _teamLoadoutService.SaveAsync(
                resolvedPlayerId,
                loadout.UIds.Where(id => id != unit.UnitDefinitionUId).ToList());
        }

        return NoContent();
    }

    // POST /api/player/{playerId}/weapons - Admin only
    [HttpPost("{playerId}/weapons")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GrantWeapon(string playerId, [FromBody] GrantPlayerWeaponDto dto)
    {
        var (_, profile, _) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        var def = await _weaponDefinitionService.GetByWeaponIdAsync(dto.WeaponDefinitionId);
        if (def == null)
        {
            return BadRequest(new { message = $"WeaponDefinition #{dto.WeaponDefinitionId} not found." });
        }

        profile.OwnedWeapons.Add(new OwnedWeapon
        {
            OwnedWeaponId = Guid.NewGuid().ToString(),
            WeaponDefinitionId = dto.WeaponDefinitionId
        });

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return Ok(new { message = $"Granted weapon #{dto.WeaponDefinitionId}." });
    }

    // DELETE /api/player/{playerId}/weapons/{ownedWeaponId} - Admin only
    [HttpDelete("{playerId}/weapons/{ownedWeaponId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveOwnedWeapon(string playerId, string ownedWeaponId)
    {
        var (_, profile, _) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        var weapon = profile.OwnedWeapons.FirstOrDefault(w => w.OwnedWeaponId == ownedWeaponId);
        if (weapon == null) return NotFound(new { message = $"Owned weapon '{ownedWeaponId}' not found." });

        profile.OwnedWeapons.Remove(weapon);
        foreach (var unit in profile.OwnedUnits.Where(u => u.EquippedOwnedWeaponId == ownedWeaponId))
        {
            unit.EquippedOwnedWeaponId = string.Empty;
        }

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return NoContent();
    }

    // POST /api/player/{playerId}/trinkets - Admin only
    [HttpPost("{playerId}/trinkets")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GrantTrinket(string playerId, [FromBody] GrantPlayerTrinketDto dto)
    {
        var (_, profile, _) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        var def = await _trinketDefinitionService.GetByTrinketIdAsync(dto.TrinketDefinitionId);
        if (def == null)
        {
            return BadRequest(new { message = $"TrinketDefinition #{dto.TrinketDefinitionId} not found." });
        }

        profile.OwnedTrinkets.Add(new OwnedTrinket
        {
            OwnedTrinketId = Guid.NewGuid().ToString(),
            TrinketDefinitionId = dto.TrinketDefinitionId
        });

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return Ok(new { message = $"Granted trinket #{dto.TrinketDefinitionId}." });
    }

    // DELETE /api/player/{playerId}/trinkets/{ownedTrinketId} - Admin only
    [HttpDelete("{playerId}/trinkets/{ownedTrinketId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveOwnedTrinket(string playerId, string ownedTrinketId)
    {
        var (_, profile, _) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        var trinket = profile.OwnedTrinkets.FirstOrDefault(t => t.OwnedTrinketId == ownedTrinketId);
        if (trinket == null) return NotFound(new { message = $"Owned trinket '{ownedTrinketId}' not found." });

        profile.OwnedTrinkets.Remove(trinket);
        foreach (var unit in profile.OwnedUnits.Where(u => u.EquippedOwnedTrinketId == ownedTrinketId))
        {
            unit.EquippedOwnedTrinketId = string.Empty;
        }

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return NoContent();
    }

    // PUT /api/player/{playerId}/pity - Admin only
    [HttpPut("{playerId}/pity")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdatePity(string playerId, [FromBody] UpdatePlayerPityDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.BannerId))
        {
            return BadRequest(new { message = "BannerId is required." });
        }

        var (_, profile, _) = await EnsureProfileForMutationAsync(playerId);
        if (profile == null) return NotFound(new { message = $"Player '{playerId}' not found." });

        profile.GachaPityByBanner[dto.BannerId.Trim()] = new GachaPityState
        {
            PullsSinceRare = Math.Max(0, dto.PullsSinceRare),
            GuaranteedFeatured = dto.GuaranteedFeatured
        };

        await _profileRepository.UpdateAsync(profile.Id, profile);
        return Ok(new { message = $"Updated pity for banner '{dto.BannerId}'." });
    }

    // PUT /api/player/{playerId}/story-progress - Admin only
    [HttpPut("{playerId}/story-progress")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStoryProgress(string playerId, [FromBody] UpdatePlayerStoryProgressDto dto)
    {
        var (user, profile, resolvedPlayerId) = await ResolveUserAndProfileAsync(playerId);
        if (user == null && profile == null)
        {
            return NotFound(new { message = $"Player '{playerId}' not found." });
        }

        var existing = await _storyRepository.GetAllByFilterAsync(s => s.PlayerId == resolvedPlayerId);
        var now = DateTime.UtcNow;

        if (existing.Count > 0)
        {
            var current = existing
                .OrderByDescending(s => s.ChapterId)
                .ThenByDescending(s => s.SceneId)
                .First();

            foreach (var stale in existing.Where(s => s.Id != current.Id))
            {
                await _storyRepository.DeleteAsync(stale.Id);
            }

            current.ChapterId = Math.Max(0, dto.ChapterId);
            current.SceneId = Math.Max(0, dto.SceneId);
            current.IsCompleted = dto.IsCompleted;
            current.IsChapterCompleted = dto.IsChapterCompleted;
            current.UpdatedAt = now;
            current.CompletedAt = dto.IsChapterCompleted ? now : null;
            await _storyRepository.UpdateAsync(current.Id, current);
        }
        else
        {
            await _storyRepository.CreateAsync(new StoryProgress
            {
                PlayerId = resolvedPlayerId,
                ChapterId = Math.Max(0, dto.ChapterId),
                SceneId = Math.Max(0, dto.SceneId),
                IsCompleted = dto.IsCompleted,
                IsChapterCompleted = dto.IsChapterCompleted,
                StartedAt = now,
                UpdatedAt = now,
                CompletedAt = dto.IsChapterCompleted ? now : null
            });
        }

        return Ok(new { message = $"Updated story checkpoint for {resolvedPlayerId} to C{dto.ChapterId} S{dto.SceneId}." });
    }

    private async Task<(User? User, PlayerProfile? Profile, string ResolvedPlayerId)> ResolveUserAndProfileAsync(string playerId)
    {
        var trimmed = playerId.Trim();
        var lower = trimmed.ToLowerInvariant();

        var profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == trimmed);
        if (profile == null)
        {
            var profiles = await _profileRepository.GetAllByFilterAsync(p => p.Username != null && p.Username.ToLower() == lower);
            profile = profiles.FirstOrDefault();
        }

        var user = await _userRepository.GetByFilterAsync(u => u.Id == trimmed);
        if (user == null)
        {
            var users = await _userRepository.GetAllByFilterAsync(u => u.Username != null && u.Username.ToLower() == lower);
            user = users.FirstOrDefault();
        }

        if (profile == null && user != null)
        {
            profile = await _profileRepository.GetByFilterAsync(p => p.PlayerId == user.Id);
        }

        var resolvedId = profile?.PlayerId ?? user?.Id ?? trimmed;
        return (user, profile, resolvedId);
    }

    private async Task<(User? User, PlayerProfile? Profile, string ResolvedPlayerId)> EnsureProfileForMutationAsync(string playerId)
    {
        var (user, profile, resolvedId) = await ResolveUserAndProfileAsync(playerId);
        if (profile != null || user == null)
        {
            return (user, profile, resolvedId);
        }

        profile = new PlayerProfile
        {
            PlayerId = user.Id,
            Username = user.Username,
            Level = 1,
            Experience = 0,
            Gold = 0,
            Gems = 0,
            LastLogin = user.LastLoginAt ?? DateTime.UtcNow
        };
        await _profileRepository.CreateAsync(profile);
        return (user, profile, user.Id);
    }
}
