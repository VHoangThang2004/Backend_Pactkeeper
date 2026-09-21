using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/topuppack")]
[Authorize]
public class TopUpPackController : ControllerBase
{
    private readonly ITopUpPackService _service;
    private readonly IUnitDefinitionService _unitService;
    private readonly IWeaponDefinitionService _weaponService;
    private readonly ITrinketDefinitionService _trinketService;

    public TopUpPackController(
        ITopUpPackService service,
        IUnitDefinitionService unitService,
        IWeaponDefinitionService weaponService,
        ITrinketDefinitionService trinketService)
    {
        _service = service;
        _unitService = unitService;
        _weaponService = weaponService;
        _trinketService = trinketService;
    }

    // GET /api/topuppack  — available packs for the shop
    [HttpGet]
    public async Task<ActionResult<List<TopUpPackDto>>> GetAvailable()
    {
        var packs = await _service.GetAvailableAsync();
        
        var units = await _unitService.GetAllAsync();
        var weapons = await _weaponService.GetAllAsync();
        var trinkets = await _trinketService.GetAllAsync();

        var unitMap = units.ToDictionary(u => u.UId, u => u.UnitName);
        var weaponMap = weapons.ToDictionary(w => w.WeaponId, w => w.Name);
        var trinketMap = trinkets.ToDictionary(t => t.TrinketId, t => t.Name);

        var dtos = packs.Select(p => ToDto(p, unitMap, weaponMap, trinketMap)).ToList();
        return Ok(dtos);
    }

    // GET /api/topuppack/all  — all packs including unavailable, admin only
    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<TopUpPackDto>>> GetAll()
    {
        var packs = await _service.GetAllAsync();
        
        var units = await _unitService.GetAllAsync();
        var weapons = await _weaponService.GetAllAsync();
        var trinkets = await _trinketService.GetAllAsync();

        var unitMap = units.ToDictionary(u => u.UId, u => u.UnitName);
        var weaponMap = weapons.ToDictionary(w => w.WeaponId, w => w.Name);
        var trinketMap = trinkets.ToDictionary(t => t.TrinketId, t => t.Name);

        var dtos = packs.Select(p => ToDto(p, unitMap, weaponMap, trinketMap)).ToList();
        return Ok(dtos);
    }

    // GET /api/topuppack/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<TopUpPackDto>> GetById(string id)
    {
        var pack = await _service.GetByIdAsync(id);
        if (pack == null) return NotFound();

        var units = await _unitService.GetAllAsync();
        var weapons = await _weaponService.GetAllAsync();
        var trinkets = await _trinketService.GetAllAsync();

        var unitMap = units.ToDictionary(u => u.UId, u => u.UnitName);
        var weaponMap = weapons.ToDictionary(w => w.WeaponId, w => w.Name);
        var trinketMap = trinkets.ToDictionary(t => t.TrinketId, t => t.Name);

        return Ok(ToDto(pack, unitMap, weaponMap, trinketMap));
    }

    // POST /api/topuppack
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TopUpPackDto>> Create([FromBody] CreateTopUpPackDto dto)
    {
        var pack = new TopUpPack
        {
            Name = dto.Name,
            PriceVnd = dto.PriceVnd,
            GemsAmount = dto.GemsAmount,
            WeaponDefinitionIds = dto.WeaponDefinitionIds,
            TrinketDefinitionIds = dto.TrinketDefinitionIds,
            UnitDefinitionIds = dto.UnitDefinitionIds,
            IsAvailable = true,
        };
        var created = await _service.CreateAsync(pack);
        
        var units = await _unitService.GetAllAsync();
        var weapons = await _weaponService.GetAllAsync();
        var trinkets = await _trinketService.GetAllAsync();

        var unitMap = units.ToDictionary(u => u.UId, u => u.UnitName);
        var weaponMap = weapons.ToDictionary(w => w.WeaponId, w => w.Name);
        var trinketMap = trinkets.ToDictionary(t => t.TrinketId, t => t.Name);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created, unitMap, weaponMap, trinketMap));
    }

    // PATCH /api/topuppack/{id}/availability
    [HttpPatch("{id}/availability")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateAvailability(string id, [FromBody] UpdateTopUpPackAvailabilityDto dto)
    {
        var pack = await _service.GetByIdAsync(id);
        if (pack == null) return NotFound();
        await _service.UpdateAvailabilityAsync(id, dto.IsAvailable);
        return NoContent();
    }

    // DELETE /api/topuppack/{id}
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var pack = await _service.GetByIdAsync(id);
        if (pack == null) return NotFound();
        await _service.DeleteAsync(id);
        return NoContent();
    }

    private static TopUpPackDto ToDto(TopUpPack p, Dictionary<int, string> unitMap, Dictionary<int, string> weaponMap, Dictionary<int, string> trinketMap)
    {
        var weaponNames = p.WeaponDefinitionIds.Select(id => weaponMap.TryGetValue(id, out var name) ? name : $"Weapon #{id}").ToList();
        var trinketNames = p.TrinketDefinitionIds.Select(id => trinketMap.TryGetValue(id, out var name) ? name : $"Trinket #{id}").ToList();
        var unitNames = p.UnitDefinitionIds.Select(id => unitMap.TryGetValue(id, out var name) ? name : $"Unit #{id}").ToList();

        return new TopUpPackDto(
            p.Id,
            p.Name,
            p.PriceVnd,
            p.GemsAmount,
            p.WeaponDefinitionIds,
            p.TrinketDefinitionIds,
            p.UnitDefinitionIds,
            p.IsAvailable,
            weaponNames,
            trinketNames,
            unitNames
        );
    }
}
