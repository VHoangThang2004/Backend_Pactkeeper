using GameInventoryApi.Services;
using GameInventoryApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using GameInventoryApi.Hubs;
using PayOS;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;
using System.Text.Json;

namespace GameInventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentController(
    IConfiguration configuration,
    ITopUpPackService topUpPackService,
    IPurchaseOrderService orderService,
    IPlayerProfileService profileService,
    INotificationService notificationService,
    IActionService actionService,
    IHubContext<SupportHub> hubContext) : ControllerBase
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
    [Authorize(Roles = "Player")]
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
                    CancelUrl = dto.CancelUrl ?? "https://pactkeeper-web.vercel.app/payment/cancel",
                    ReturnUrl = dto.ReturnUrl ?? "https://pactkeeper-web.vercel.app/payment/success",
                    ExpiredAt = (int)DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()
                };
                var result = await _payOSClient.PaymentRequests.CreateAsync(request);
                checkoutUrl = result.CheckoutUrl;
                Console.WriteLine($"[Payment] Order {orderCode} for {playerId}, pack {pack.Name}, {pack.PriceVnd} VND, ExpiredAt=+15m");
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

        await orderService.CreateOrderAsync(playerId, pack, orderCode, checkoutUrl);

        return Ok(new CreateOrderResponseDto(orderCode, checkoutUrl, "PENDING"));
    }

    // GET /api/payment/history — player's own transaction history (or all transactions if Admin)
    [HttpGet("history")]
    public async Task<ActionResult<List<TransactionHistoryDto>>> GetHistory()
    {
        var isAdmin = User.IsInRole("Admin");
        List<PurchaseOrder> orders;

        if (isAdmin)
        {
            orders = await orderService.GetAllOrdersAsync();
        }
        else
        {
            var playerId = User.FindFirst("PlayerId")?.Value;
            if (playerId == null) return Unauthorized();
            orders = await orderService.GetByPlayerIdAsync(playerId);
        }

        // Active Reconciliation & 15-Minute Expiration Verification
        var pendingOrders = orders.Where(o => o.Status == PurchaseOrderStatus.Pending).ToList();
        if (pendingOrders.Count > 0)
        {
            bool anyChanged = false;
            foreach (var po in pendingOrders)
            {
                // Check if order exceeded standard PayOS payment window (15 minutes)
                if (DateTime.UtcNow - po.CreatedAt > TimeSpan.FromMinutes(15))
                {
                    if (_payOSClient != null)
                    {
                        try { await _payOSClient.PaymentRequests.CancelAsync(po.OrderCode, "EXPIRED: Payment window exceeded 15 minutes"); }
                        catch { /* ignore if already cancelled or expired on PayOS */ }
                    }
                    await HandleOrderCancellationAsync(po.OrderCode, "EXPIRED (exceeded 15 minutes)");
                    anyChanged = true;
                }
                else if (_payOSClient != null)
                {
                    await SyncOrderFromPayOSAsync(po);
                    anyChanged = true;
                }
            }

            // Reload fresh orders after reconciliation
            if (anyChanged)
            {
                if (isAdmin)
                {
                    orders = await orderService.GetAllOrdersAsync();
                }
                else
                {
                    var playerId = User.FindFirst("PlayerId")?.Value;
                    if (playerId != null)
                        orders = await orderService.GetByPlayerIdAsync(playerId);
                }
            }
        }

        var result = orders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new TransactionHistoryDto(
                o.OrderCode,
                o.Amount,
                o.GemsAmount,
                o.UnitDefinitionIds,
                o.WeaponDefinitionIds,
                o.TrinketDefinitionIds,
                o.Status == PurchaseOrderStatus.Confirmed ? "PAID" : (o.Status == PurchaseOrderStatus.Cancelled ? "CANCELLED" : "PENDING"),
                o.CreatedAt,
                o.ConfirmedAt,
                o.PlayerId,
                o.TopUpPackId,
                o.Id,
                o.CheckoutUrl))
            .ToList();

        return Ok(result);
    }

    // GET /api/payment/admin/orders — all transactions for Admin
    [HttpGet("admin/orders")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<TransactionHistoryDto>>> GetAdminOrders()
    {
        var orders = await orderService.GetAllOrdersAsync();
        var result = orders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new TransactionHistoryDto(
                o.OrderCode,
                o.Amount,
                o.GemsAmount,
                o.UnitDefinitionIds,
                o.WeaponDefinitionIds,
                o.TrinketDefinitionIds,
                o.Status == PurchaseOrderStatus.Confirmed ? "PAID" : (o.Status == PurchaseOrderStatus.Cancelled ? "CANCELLED" : "PENDING"),
                o.CreatedAt,
                o.ConfirmedAt,
                o.PlayerId,
                o.TopUpPackId,
                o.Id,
                o.CheckoutUrl))
            .ToList();

        return Ok(result);
    }

    // POST /api/payment/cancel-order/{orderCode} — Cancel a pending order immediately
    [HttpPost("cancel-order/{orderCode}")]
    public async Task<IActionResult> CancelOrder(long orderCode)
    {
        var order = await orderService.GetByOrderCodeAsync(orderCode);
        if (order == null) return NotFound(new { message = "Order not found." });

        var playerId = User.FindFirst("PlayerId")?.Value;
        var isAdmin = User.IsInRole("Admin");

        if (!isAdmin && order.PlayerId != playerId)
        {
            return Forbid();
        }

        if (order.Status != PurchaseOrderStatus.Pending)
        {
            return BadRequest(new { message = $"Order is already {order.Status.ToString().ToLower()}." });
        }

        if (_payOSClient != null)
        {
            try
            {
                await _payOSClient.PaymentRequests.CancelAsync(orderCode, "Cancelled by user");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Payment-Cancel] PayOS Cancel error for {orderCode}: {ex.Message}");
            }
        }

        await HandleOrderCancellationAsync(orderCode, "Cancelled by user");

        return Ok(new { status = "CANCELLED", message = "Order cancelled successfully." });
    }

    // POST /api/payment/sync/{orderCode} — called by SDK frontend or client to actively reconcile an order
    [HttpPost("sync/{orderCode}")]
    [AllowAnonymous]
    public async Task<IActionResult> SyncOrder(long orderCode, [FromQuery] string? status)
    {
        var order = await orderService.GetByOrderCodeAsync(orderCode);
        if (order == null) return NotFound(new { message = "Order not found." });

        if (order.Status == PurchaseOrderStatus.Confirmed)
        {
            return Ok(new { status = "PAID", message = "Order already confirmed." });
        }
        if (order.Status == PurchaseOrderStatus.Cancelled)
        {
            return Ok(new { status = "CANCELLED", message = "Order already cancelled." });
        }

        // Check if order exceeded standard 15-minute window
        if (order.Status == PurchaseOrderStatus.Pending && DateTime.UtcNow - order.CreatedAt > TimeSpan.FromMinutes(15))
        {
            if (_payOSClient != null)
            {
                try { await _payOSClient.PaymentRequests.CancelAsync(orderCode, "EXPIRED: Payment window exceeded 15 minutes"); }
                catch { }
            }
            await HandleOrderCancellationAsync(orderCode, "EXPIRED (exceeded 15 minutes)");
            return Ok(new { status = "CANCELLED", message = "Order expired (exceeded 15 minutes)." });
        }

        if (_payOSClient != null)
        {
            try
            {
                var info = await _payOSClient.PaymentRequests.GetAsync(orderCode);
                var payOSStatus = info.Status.ToString().ToUpperInvariant();
                if (payOSStatus == "PAID")
                {
                    await HandleOrderConfirmationAsync(orderCode);
                    return Ok(new { status = "PAID", message = "Order successfully verified and confirmed." });
                }
                else if (payOSStatus == "CANCELLED" || payOSStatus == "EXPIRED")
                {
                    await HandleOrderCancellationAsync(orderCode, payOSStatus);
                    return Ok(new { status = "CANCELLED", message = "Order was cancelled or expired." });
                }
                return Ok(new { status = "PENDING", message = "Order is still pending on PayOS." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] PayOS GetAsync error: {ex.Message}");
            }
        }
        else
        {
            // Mock fallback when testing without live PayOS credentials
            if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                await HandleOrderConfirmationAsync(orderCode);
                return Ok(new { status = "PAID", message = "[Mock] Order confirmed in mock mode." });
            }
            else if (string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                await HandleOrderCancellationAsync(orderCode, "Cancelled in mock mode");
                return Ok(new { status = "CANCELLED", message = "[Mock] Order cancelled in mock mode." });
            }
        }

        return Ok(new { status = order.Status.ToString().ToUpperInvariant(), message = "Order status unchanged." });
    }

    // POST /api/payment/webhook — called by PayOS, no player token
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymentWebhook([FromBody] Webhook webhook)
    {
        // Anonymous endpoint: only trust a body whose signature matches our PayOS ChecksumKey
        if (_payOSClient == null) return BadRequest();
        WebhookData data;
        try { data = await _payOSClient.Webhooks.VerifyAsync(webhook); }
        catch (Exception ex)
        {
            Console.WriteLine($"[Webhook] Rejected unverified webhook: {ex.Message}");
            return BadRequest();
        }

        try
        {
            var code = webhook.Code;
            var orderCode = data.OrderCode;

            if (code == "00")
            {
                var success = await HandleOrderConfirmationAsync(orderCode);
                if (!success)
                {
                    Console.WriteLine($"[Webhook] Order {orderCode} already processed or not found.");
                }
            }
            else
            {
                await HandleOrderCancellationAsync(orderCode, $"webhook code={code}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Webhook] Error: {ex.Message}");
            return BadRequest();
        }

        return Ok();
    }

    private async Task<bool> HandleOrderConfirmationAsync(long orderCode)
    {
        var order = await orderService.GetByOrderCodeAsync(orderCode);
        if (order == null) return false;

        var confirmed = await orderService.ConfirmOrderAsync(orderCode);
        if (!confirmed) return false;

        if (order.GemsAmount > 0)
            await profileService.AddGemsAsync(order.PlayerId, order.GemsAmount);

        await profileService.GrantItemsAsync(
            order.PlayerId,
            order.UnitDefinitionIds,
            order.WeaponDefinitionIds,
            order.TrinketDefinitionIds);

        Console.WriteLine($"[Payment-Confirmed] Order {orderCode} confirmed → {order.PlayerId}: +{order.GemsAmount} gems, {order.UnitDefinitionIds.Count} units, {order.WeaponDefinitionIds.Count} weapons, {order.TrinketDefinitionIds.Count} trinkets");

        await actionService.LogActionAsync(order.PlayerId, "Purchase", order.Id, new Dictionary<string, object>
        {
            ["packId"] = order.TopUpPackId,
            ["gemsAmount"] = order.GemsAmount,
        });

        await notificationService.CreateNotificationAsync(
            order.PlayerId,
            "success",
            "Tribute Accepted",
            $"Your offering of {order.Amount:N0} VND has been received. {order.GemsAmount} Gems added to your treasury."
        );

        await hubContext.Clients.Group(order.PlayerId).SendAsync("ReceiveProfileUpdate");
        return true;
    }

    private async Task HandleOrderCancellationAsync(long orderCode, string reason = "")
    {
        var order = await orderService.GetByOrderCodeAsync(orderCode);
        if (order == null || order.Status != PurchaseOrderStatus.Pending) return;

        await orderService.CancelOrderAsync(orderCode);
        Console.WriteLine($"[Payment-Cancelled] Order {orderCode} cancelled ({reason}).");

        await notificationService.CreateNotificationAsync(
            order.PlayerId,
            "error",
            "Pact Failed",
            $"The magical transaction for {order.Amount:N0} VND was disrupted or cancelled. Please try another tribute method."
        );
    }

    private async Task SyncOrderFromPayOSAsync(PurchaseOrder order)
    {
        if (order.Status != PurchaseOrderStatus.Pending || _payOSClient == null) return;

        try
        {
            var paymentInfo = await _payOSClient.PaymentRequests.GetAsync(order.OrderCode);
            if (paymentInfo != null)
            {
                var status = paymentInfo.Status.ToString().ToUpperInvariant();
                if (status == "PAID")
                {
                    await HandleOrderConfirmationAsync(order.OrderCode);
                }
                else if (status == "CANCELLED" || status == "EXPIRED")
                {
                    await HandleOrderCancellationAsync(order.OrderCode, status);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PayOS-Sync] Order {order.OrderCode} sync check error: {ex.Message}");
        }
    }
}

public record CreateOrderRequestDto(string PackId, string? ReturnUrl, string? CancelUrl);
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
    DateTime? ConfirmedAt,
    string PlayerId,
    string TopUpPackId,
    string Id,
    string? CheckoutUrl = null
);
