using Microsoft.AspNetCore.SignalR;
using Sensor.Contracts;
using Sensor.Api.Services;

namespace Sensor.Api.Hubs;

// Groups demonstrate delivery scope, not authorization. This milestone has no user accounts.
public sealed class SensorHub(ILogger<SensorHub> logger, SensorSubscriptions subscriptions) : Hub
{
    public Task Echo(string message) => Clients.Caller.SendAsync(
        RealtimeProtocol.EchoResponse, $"Echo: {ValidateMessage(message)}");

    public Task Broadcast(string message) => Clients.All.SendAsync(
        RealtimeProtocol.BroadcastMessage, ValidateMessage(message));

    public async Task JoinGroup(string groupName)
    {
        ValidateGroup(groupName);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        subscriptions.Join(Context.ConnectionId, groupName);
        await Clients.Group(groupName).SendAsync(RealtimeProtocol.GroupMessage,
            $"{ShortId} joined {groupName}");
    }

    public async Task LeaveGroup(string groupName)
    {
        ValidateGroup(groupName);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        subscriptions.Leave(Context.ConnectionId, groupName);
    }

    public Task SendToGroup(string groupName, string message)
    {
        ValidateGroup(groupName);
        return Clients.Group(groupName).SendAsync(RealtimeProtocol.GroupMessage, ValidateMessage(message));
    }

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("Monitor connected: {ConnectionId}", Context.ConnectionId);
        await Clients.All.SendAsync(RealtimeProtocol.SystemMessage, $"{ShortId} connected");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Disconnect(Context.ConnectionId);
        logger.LogInformation("Monitor disconnected: {ConnectionId}", Context.ConnectionId);
        await Clients.All.SendAsync(RealtimeProtocol.SystemMessage, $"{ShortId} disconnected");
        await base.OnDisconnectedAsync(exception);
    }

    private string ShortId => Context.ConnectionId[..Math.Min(8, Context.ConnectionId.Length)];

    private static string ValidateMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
            throw new HubException("Message must contain 1-500 characters.");
        return message.Trim();
    }

    private static void ValidateGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > 100
            || groupName.Any(char.IsControl) || groupName != groupName.Trim())
            throw new HubException("Group must contain 1-100 characters without outer whitespace or control characters.");
    }
}
