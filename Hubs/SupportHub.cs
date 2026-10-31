using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace GameInventoryApi.Hubs;

[Authorize]
public class SupportHub : Hub
{
    private static bool IsStaff(ClaimsPrincipal? user) =>
        user?.IsInRole("Admin") == true ||
        user?.IsInRole("Technical Support") == true ||
        user?.IsInRole("Support") == true ||
        user?.IsInRole("Moderator") == true;

    public override async Task OnConnectedAsync()
    {
        if (IsStaff(Context.User))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }
        else if (Context.User?.IsInRole("Player") == true)
        {
            var playerId = Context.User.FindFirst("PlayerId")?.Value;
            if (!string.IsNullOrEmpty(playerId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, playerId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (IsStaff(Context.User))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Admins");
        }
        else if (Context.User?.IsInRole("Player") == true)
        {
            var playerId = Context.User.FindFirst("PlayerId")?.Value;
            if (!string.IsNullOrEmpty(playerId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, playerId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
