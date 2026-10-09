using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/shop")]
[Authorize]
public class ShopController : ControllerBase
{
    private readonly IShopService _shopService;

    public ShopController(IShopService shopService)
    {
        _shopService = shopService;
    }

    // ==========================================
    // PLAYER ENDPOINTS
    // ==========================================

    // GET /api/shop/active — active and currently open shops for players with purchase limits
    [HttpGet("active")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<List<PlayerShopDto>>> GetActive()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var shops = await _shopService.GetActiveShopsForPlayerAsync(playerId);
        return Ok(shops);
    }

    // GET /api/shop/{id} — active shop details for player
    [HttpGet("{id}")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<PlayerShopDto>> GetShopForPlayer(string id)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var shop = await _shopService.GetShopForPlayerAsync(playerId, id);
        if (shop == null) return NotFound(new { message = "Shop not found or currently closed." });

        return Ok(shop);
    }

    // POST /api/shop/purchase — purchase an item from a shop
    [HttpPost("purchase")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<ShopPurchaseResponseDto>> Purchase([FromBody] ShopPurchaseRequestDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var (response, error) = await _shopService.PurchaseItemAsync(playerId, dto.ShopId, dto.ShopItemId, dto.Quantity);
        if (error != null) return BadRequest(new { message = error });

        return Ok(response);
    }

    // ==========================================
    // ADMIN ENDPOINTS
    // ==========================================

    // GET /api/shop/admin — all shops (including inactive/expired)
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<ShopDto>>> GetAllAdmin()
    {
        var shops = await _shopService.GetAllAsync();
        var sorted = shops.OrderBy(s => s.DisplayOrder).ThenByDescending(s => s.CreatedAt).Select(ToDto).ToList();
        return Ok(sorted);
    }

    // GET /api/shop/admin/{id} — shop by ID
    [HttpGet("admin/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ShopDto>> GetByIdAdmin(string id)
    {
        var shop = await _shopService.GetByIdAsync(id);
        if (shop == null) return NotFound();
        return Ok(ToDto(shop));
    }

    // POST /api/shop — create shop (starts inactive by default)
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ShopDto>> Create([FromBody] CreateShopDto dto)
    {
        var shop = new Shop
        {
            Name = dto.Name,
            Description = dto.Description,
            DisplayOrder = dto.DisplayOrder,
            Items = dto.Items.Select(MapItem).ToList(),
            ResetPeriod = dto.ResetPeriod,
            StartDate = dto.StartDate,
            ExpiryDate = dto.ExpiryDate,
            DailyStartTime = dto.DailyStartTime,
            DailyEndTime = dto.DailyEndTime,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _shopService.CreateAsync(shop);
        return CreatedAtAction(nameof(GetByIdAdmin), new { id = created.Id }, ToDto(created));
    }

    // PUT /api/shop/{id} — update shop configuration
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateShopDto dto)
    {
        var existing = await _shopService.GetByIdAsync(id);
        if (existing == null) return NotFound();

        existing.Name = dto.Name;
        existing.Description = dto.Description;
        existing.DisplayOrder = dto.DisplayOrder;
        existing.Items = dto.Items.Select(MapItem).ToList();
        existing.ResetPeriod = dto.ResetPeriod;
        existing.StartDate = dto.StartDate;
        existing.ExpiryDate = dto.ExpiryDate;
        existing.DailyStartTime = dto.DailyStartTime;
        existing.DailyEndTime = dto.DailyEndTime;

        await _shopService.UpdateAsync(id, existing);
        return NoContent();
    }

    // PATCH /api/shop/{id}/activate — activate shop
    [HttpPatch("{id}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Activate(string id)
    {
        var shop = await _shopService.GetByIdAsync(id);
        if (shop == null) return NotFound();
        await _shopService.SetActiveAsync(id, true);
        return NoContent();
    }

    // PATCH /api/shop/{id}/deactivate — deactivate shop
    [HttpPatch("{id}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var shop = await _shopService.GetByIdAsync(id);
        if (shop == null) return NotFound();
        await _shopService.SetActiveAsync(id, false);
        return NoContent();
    }

    // DELETE /api/shop/{id} — delete shop
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var shop = await _shopService.GetByIdAsync(id);
        if (shop == null) return NotFound();
        await _shopService.DeleteAsync(id);
        return NoContent();
    }

    private static ShopItem MapItem(ShopItemDto i) => new()
    {
        Id = string.IsNullOrEmpty(i.Id) ? Guid.NewGuid().ToString() : i.Id,
        Reward = new Reward
        {
            Type = i.Reward.Type,
            DefinitionId = i.Reward.DefinitionId,
            ClassId = i.Reward.ClassId,
            Amount = i.Reward.Amount
        },
        RewardTier = i.RewardTier,
        Price = i.Price,
        PurchaseLimit = i.PurchaseLimit
    };

    private static ShopDto ToDto(Shop s) => new(
        s.Id,
        s.Name,
        s.Description,
        s.DisplayOrder,
        s.Items.Select(i => new ShopItemDto(
            i.Id,
            new RewardDto(i.Reward.Type, i.Reward.DefinitionId, i.Reward.ClassId, i.Reward.Amount),
            i.RewardTier,
            i.Price,
            i.PurchaseLimit
        )).ToList(),
        s.ResetPeriod,
        s.IsActive,
        s.StartDate,
        s.ExpiryDate,
        s.DailyStartTime,
        s.DailyEndTime,
        s.CreatedAt
    );
}

