using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayOS;
using PayOS.Models.V2.PaymentRequests;
using System.Text.Json;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Player")]
public class PaymentController(
    IConfiguration configuration,
    ITopUpPackService topUpPackService,
    IPurchaseOrderService orderService,
    IPlayerProfileService profileService,
    INotificationService notificationService) : ControllerBase
{
    private readonly PayOSClient? _payOSClient = InitClient(configuration);

    private static PayOSClient? InitClient(IConfiguration config)
    {
        var s = config.GetSection("PayOS");
        var clientId = s["ClientId"];
        var apiKey = s["ApiKey"];
        var checksumKey = s["ChecksumKey"];
        if (string.IsNullOrEmpty(clientId) || clientId == "YOUR_PAYOS_CLIENT_ID" ||
            string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_PAYOS_API_KEY" ||
            string.IsNullOrEmpty(checksumKey) || checksumKey == "YOUR_PAYOS_CHECKSUM_KEY")
            return null;
        try { return new PayOSClient(clientId, apiKey, checksumKey); }
        catch (Exception ex) { Console.WriteLine($"[Payment-Init] {ex.Message}"); return null; }
    }

    // POST /api/payment/create-order
    [HttpPost("create-order")]
    public async Task<ActionResult<CreateOrderResponseDto>> CreateOrder([FromBody] CreateOrderRequestDto dto)
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var pack = await topUpPackService.GetByIdAsync(dto.PackId);
        if (pack == null || !pack.IsAvailable) return BadRequest("Pack not found or unavailable.");

        long orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string checkoutUrl;

        if (_payOSClient != null)
        {
            try
            {
                var rawDesc = $"{pack.Name} ({pack.GemsAmount} gems)";
                var request = new CreatePaymentLinkRequest
                {
                    OrderCode = orderCode,
                    Amount = pack.PriceVnd,
                    Description = rawDesc.Length > 25 ? rawDesc[..25] : rawDesc,
                    CancelUrl = "http://srpg-backend.duckdns.org:5276/cancel.html",
                    ReturnUrl = "http://srpg-backend.duckdns.org:5276/success.html"
                };
                var result = await _payOSClient.PaymentRequests.CreateAsync(request);
                checkoutUrl = result.CheckoutUrl;
                Console.WriteLine($"[Payment] Order {orderCode} for {playerId}, pack {pack.Name}, {pack.PriceVnd} VND");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Payment] PayOS SDK error: {ex.Message}. Using mock.");
                checkoutUrl = $"https://checkout.payos.vn/oauth/iframe?id=mock_{orderCode}&amount={pack.PriceVnd}";
            }
        }
        else
        {
            checkoutUrl = $"https://checkout.payos.vn/oauth/iframe?id=mock_{orderCode}&amount={pack.PriceVnd}";
            Console.WriteLine($"[Payment-Mock] Order {orderCode} for {playerId}, pack {pack.Name}");
        }

        await orderService.CreateOrderAsync(playerId, pack, orderCode);

        return Ok(new CreateOrderResponseDto(orderCode, checkoutUrl, "PENDING"));
    }

    // GET /api/payment/history — player's own transaction history
    [HttpGet("history")]
    public async Task<ActionResult<List<TransactionHistoryDto>>> GetHistory()
    {
        var playerId = User.FindFirst("PlayerId")?.Value;
        if (playerId == null) return Unauthorized();

        var orders = await orderService.GetByPlayerIdAsync(playerId);
        var result = orders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new TransactionHistoryDto(
                o.OrderCode,
                o.Amount,
                o.GemsAmount,
                o.UnitDefinitionIds,
                o.WeaponDefinitionIds,
                o.TrinketDefinitionIds,
                o.Status.ToString(),
                o.CreatedAt,
                o.ConfirmedAt))
            .ToList();

        return Ok(result);
    }

    // POST /api/payment/webhook  — called by PayOS, no player token
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymentWebhook([FromBody] JsonElement body)
    {
        try
        {
            var code = body.GetProperty("code").GetString();
            var data = body.GetProperty("data");
            var orderCode = data.GetProperty("orderCode").GetInt64();

            if (code == "00")
            {
                var order = await orderService.GetByOrderCodeAsync(orderCode);
                if (order == null)
                {
                    Console.WriteLine($"[Webhook] Order {orderCode} not found.");
                    return Ok();
                }

                var confirmed = await orderService.ConfirmOrderAsync(orderCode);
                if (confirmed)
                {
                    if (order.GemsAmount > 0)
                        await profileService.AddGemsAsync(order.PlayerId, order.GemsAmount);

                    await profileService.GrantItemsAsync(
                        order.PlayerId,
                        order.UnitDefinitionIds,
                        order.WeaponDefinitionIds,
                        order.TrinketDefinitionIds);

                    Console.WriteLine($"[Webhook] Order {orderCode} confirmed → {order.PlayerId}: +{order.GemsAmount} gems, {order.UnitDefinitionIds.Count} units, {order.WeaponDefinitionIds.Count} weapons, {order.TrinketDefinitionIds.Count} trinkets");

                    await notificationService.CreateNotificationAsync(
                        order.PlayerId,
                        "success",
                        "Tribute Accepted",
                        $"Your offering of {order.Amount:N0} VND has been received. {order.GemsAmount} Gems added to your treasury."
                    );
                }
                else
                {
                    Console.WriteLine($"[Webhook] Order {orderCode} already processed, skipping.");
                }
            }
            else
            {
                var order = await orderService.GetByOrderCodeAsync(orderCode);
                await orderService.CancelOrderAsync(orderCode);
                Console.WriteLine($"[Webhook] Order {orderCode} cancelled (code={code}).");

                if (order != null)
                {
                    await notificationService.CreateNotificationAsync(
                        order.PlayerId,
                        "error",
                        "Pact Failed",
                        $"The magical transaction for {order.Amount:N0} VND was disrupted. Please try another tribute method."
                    );
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Webhook] Error: {ex.Message}");
            return BadRequest();
        }

        return Ok();
    }
}

public record CreateOrderRequestDto(string PackId);
public record CreateOrderResponseDto(long OrderCode, string CheckoutUrl, string Status);
public record TransactionHistoryDto(
    long OrderCode,
    int Amount,
    int GemsAmount,
    List<int> UnitDefinitionIds,
    List<int> WeaponDefinitionIds,
    List<int> TrinketDefinitionIds,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt
);
