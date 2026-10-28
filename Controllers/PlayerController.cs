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

    public PlayerController(
        IMongoRepository<User> userRepository,
        IMongoRepository<PlayerProfile> profileRepository,
        INotificationService notificationService)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _notificationService = notificationService;
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

        return Ok(new AdminPlayerProfileDto
        {
            Id = profile?.Id ?? user?.Id ?? playerId,
            PlayerId = profile?.PlayerId ?? user?.Id ?? playerId,
            Username = profile?.Username ?? user?.Username ?? "Unknown Traveler",
            Level = profile?.Level ?? 1,
            Experience = profile?.Experience ?? 0,
            Gold = profile?.Gold ?? 0,
            Gems = profile?.Gems ?? 0,
            IsBanned = isBanned,
            BanReason = banReason,
            LastLogin = profile?.LastLogin ?? DateTime.UtcNow,
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
                LastLogin = p.LastLogin,
                OwnedUnitsCount = p.OwnedUnits.Count,
                OwnedWeaponsCount = p.OwnedWeapons.Count,
                OwnedTrinketsCount = p.OwnedTrinkets.Count
            });
        }

        // Include any user accounts that do not yet have a profile
        foreach (var u in users.Where(u => u.Role == "Player" && !seenIds.Contains(u.Id)))
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
                LastLogin = DateTime.UtcNow,
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
}
