using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace GameInventoryApi.Hubs;

[Authorize]
public class SupportHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("Admin") == true)
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
        if (Context.User?.IsInRole("Admin") == true)
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
