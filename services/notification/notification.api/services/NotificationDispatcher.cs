using Microsoft.AspNetCore.SignalR;
using notification.api.hubs;

namespace notification.api.services;

public sealed class NotificationDispatcher
{
    private readonly
        IHubContext<NotificationHub> _hubContext;

    public NotificationDispatcher(
        IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendToUserAsync(string userId,
        object payload)
    {
        await _hubContext
            .Clients
            .Group($"user:{userId}")
            .SendAsync("notification", payload);
    }
}