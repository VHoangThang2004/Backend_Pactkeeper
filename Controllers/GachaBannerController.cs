using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/gachabanner")]
[Authorize]
public class GachaBannerController : ControllerBase
{
    private readonly IGachaBannerService _service;

    public GachaBannerController(IGachaBannerService service) => _service = service;

    // GET /api/gachabanner/active — active non-expired banners (publicly viewable by Guests too)
    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<ActionResult<List<GachaBannerDto>>> GetActive()
    {
        var banners = await _service.GetActiveAsync();
        return Ok(banners.Select(ToDto).ToList());
    }

    // GET /api/gachabanner — all banners including inactive, admin only
    [HttpGet]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<List<GachaBannerDto>>> GetAll()
    {
        var banners = await _service.GetAllAsync();
        return Ok(banners.Select(ToDto).ToList());
    }

    // GET /api/gachabanner/{id}/droprates — any authenticated user
    [HttpGet("{id}/droprates")]
    public async Task<ActionResult<BannerDropRatesDto>> GetDropRates(string id)
    {
        var banner = await _service.GetByIdAsync(id);
        if (banner == null) return NotFound();

        int totalWeight = banner.Items.Sum(i => Math.Max(0, i.Weight));
        if (banner.Items.Count == 0 || totalWeight <= 0)
        {
            return Ok(new BannerDropRatesDto(banner.Id, banner.Name, []));
        }

        var items = banner.Items
            .Select(i => new DropRateItemDto(
                new RewardDto(i.Reward.Type, i.Reward.DefinitionId, i.Reward.ClassId, i.Reward.Amount),
                i.IsFeatured,
                i.RewardTier,
                Math.Round((double)Math.Max(0, i.Weight) / totalWeight * 100, 2)))
            .OrderByDescending(i => i.DropRate)
            .ToList();

        return Ok(new BannerDropRatesDto(banner.Id, banner.Name, items));
    }

    // GET /api/gachabanner/{id}/stats — admin & moderator telemetry from GachaResults
    [HttpGet("{id}/stats")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<GachaBannerStatsDto>> GetStats(string id)
    {
        var stats = await _service.GetBannerStatsAsync(id);
        if (stats == null) return NotFound();
        return Ok(stats);
    }

    // GET /api/gachabanner/{id} — admin only
    [HttpGet("{id}")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<GachaBannerDto>> GetById(string id)
    {
        var banner = await _service.GetByIdAsync(id);
        if (banner == null) return NotFound();
        return Ok(ToDto(banner));
    }

    // POST /api/gachabanner — admin only; banners start inactive
    [HttpPost]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<GachaBannerDto>> Create([FromBody] CreateGachaBannerDto dto)
    {
        var validationError = await _service.ValidateBannerAsync(
            dto.Name,
            dto.BannerImageUrl,
            dto.Items,
            dto.PullOptions,
            dto.StartDate,
            dto.ExpiryDate,
            dto.PityThreshold);

        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }

        var banner = new GachaBanner
        {
            Name = dto.Name.Trim(),
            Description = (dto.Description ?? string.Empty).Trim(),
            BannerImageUrl = string.IsNullOrWhiteSpace(dto.BannerImageUrl) ? null : dto.BannerImageUrl.Trim(),
            Items = (dto.Items ?? []).Select(MapItem).ToList(),
            PullOptions = dto.PullOptions.Select(MapOption).ToList(),
            StartDate = dto.StartDate,
            ExpiryDate = dto.ExpiryDate,
            PityThreshold = dto.PityThreshold,
            IsActive = false,
        };
        var created = await _service.CreateAsync(banner);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    // PUT /api/gachabanner/{id} — admin only; does not change IsActive
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateGachaBannerDto dto)
    {
        var existing = await _service.GetByIdAsync(id);
        if (existing == null) return NotFound();

        var validationError = await _service.ValidateBannerAsync(
            dto.Name,
            dto.BannerImageUrl,
            dto.Items,
            dto.PullOptions,
            dto.StartDate,
            dto.ExpiryDate,
            dto.PityThreshold);

        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }

        if (existing.IsActive && (dto.Items == null || dto.Items.Count == 0 || dto.Items.Sum(i => Math.Max(0, i.Weight)) <= 0))
        {
            return BadRequest(new { message = "An active banner must have at least one valid reward pool item." });
        }

        existing.Name = dto.Name.Trim();
        existing.Description = (dto.Description ?? string.Empty).Trim();
        existing.BannerImageUrl = string.IsNullOrWhiteSpace(dto.BannerImageUrl) ? null : dto.BannerImageUrl.Trim();
        existing.Items = (dto.Items ?? []).Select(MapItem).ToList();
        existing.PullOptions = dto.PullOptions.Select(MapOption).ToList();
        existing.StartDate = dto.StartDate;
        existing.ExpiryDate = dto.ExpiryDate;
        existing.PityThreshold = dto.PityThreshold;

        await _service.UpdateAsync(id, existing);
        return NoContent();
    }

    // PATCH /api/gachabanner/{id}/activate — admin only
    [HttpPatch("{id}/activate")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> Activate(string id)
    {
        var banner = await _service.GetByIdAsync(id);
        if (banner == null) return NotFound();

        if (banner.Items.Count == 0 || banner.Items.Sum(i => Math.Max(0, i.Weight)) <= 0)
        {
            return BadRequest(new { message = "Cannot activate a banner with an empty or zero-weight reward pool." });
        }

        if (banner.PullOptions.Count == 0 || banner.PullOptions.Any(o => o.Price <= 0))
        {
            return BadRequest(new { message = "Cannot activate a banner without valid pull pricing options." });
        }

        await _service.SetActiveAsync(id, true);
        return NoContent();
    }

    // PATCH /api/gachabanner/{id}/deactivate — admin only
    [HttpPatch("{id}/deactivate")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var banner = await _service.GetByIdAsync(id);
        if (banner == null) return NotFound();
        await _service.SetActiveAsync(id, false);
        return NoContent();
    }

    // DELETE /api/gachabanner/{id} — only deletes unused banners with no GachaResults history
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> Delete(string id)
    {
        var banner = await _service.GetByIdAsync(id);
        if (banner == null) return NotFound();

        var (deleted, hasHistory) = await _service.DeleteIfUnusedAsync(id);
        if (!deleted && hasHistory)
        {
            return Conflict(new { message = "Banner already has player pull history and cannot be deleted. Deactivate it instead." });
        }

        return NoContent();
    }

    private static BannerItem MapItem(BannerItemDto i) => new()
    {
        Reward = new Reward
        {
            Type = i.Reward.Type,
            DefinitionId = i.Reward.Type == RewardType.Gems ? 0 : i.Reward.DefinitionId,
            ClassId = i.Reward.Type == RewardType.Unit ? i.Reward.ClassId : 0,
            Amount = i.Reward.Type == RewardType.Gems ? i.Reward.Amount : 1,
        },
        Weight = i.Weight,
        IsFeatured = i.IsFeatured,
        RewardTier = i.RewardTier,
    };

    private static PullOption MapOption(PullOptionDto o) => new() { PullType = o.PullType, Price = o.Price };

    private static GachaBannerDto ToDto(GachaBanner b) => new(
        b.Id,
        b.Name,
        b.Description,
        b.Items.Select(i => new BannerItemDto(
            new RewardDto(i.Reward.Type, i.Reward.DefinitionId, i.Reward.ClassId, i.Reward.Amount),
            i.Weight,
            i.IsFeatured,
            i.RewardTier)).ToList(),
        b.PullOptions.Select(o => new PullOptionDto(o.PullType, o.Price)).ToList(),
        b.IsActive,
        b.StartDate,
        b.ExpiryDate,
        b.PityThreshold,
        b.CreatedAt,
        b.BannerImageUrl
    );
}
