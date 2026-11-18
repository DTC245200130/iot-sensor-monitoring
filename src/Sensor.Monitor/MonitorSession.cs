using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Sensor.Contracts;

namespace Sensor.Monitor;

public sealed class MonitorSession : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, byte> _groups = new(StringComparer.Ordinal);
    private readonly Action<string> _log;
    public HubConnection Connection { get; }

    public MonitorSession(Uri baseUrl, Action<string>? log = null, TimeSpan[]? retryDelays = null)
    {
        _log = log ?? (_ => { });
        Connection = new HubConnectionBuilder()
            .WithUrl(new Uri(baseUrl, RealtimeProtocol.HubPath))
            .WithAutomaticReconnect(retryDelays ?? [TimeSpan.Zero, TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
            .Build();
        Connection.On<string>(RealtimeProtocol.EchoResponse, message => _log($"[Echo] {message}"));
        Connection.On<string>(RealtimeProtocol.BroadcastMessage, message => _log($"[Broadcast] {message}"));
        Connection.On<string>(RealtimeProtocol.GroupMessage, message => _log($"[Group] {message}"));
        Connection.On<string>(RealtimeProtocol.SystemMessage, message => _log($"[System] {message}"));
        Connection.On<ReadingDto>(RealtimeProtocol.ReadingReceived, reading => _log(
            $"[Reading] sensor={reading.SensorId} value={reading.Value} measuredAt={reading.MeasuredAt:O} id={reading.Id}"));
        Connection.On<ReadingDto>(RealtimeProtocol.AlertRaised, reading => _log(
            $"[ALERT] sensor={reading.SensorId} value={reading.Value} > {reading.AlertThreshold} id={reading.Id}"));
        Connection.Reconnecting += error =>
        {
            _log($"[Reconnecting] {error?.Message}");
            return Task.CompletedTask;
        };
        Connection.Closed += error =>
        {
            _log($"[Closed] {error?.Message ?? "Connection stopped"}. Restart Monitor to connect again.");
            return Task.CompletedTask;
        };
        Connection.Reconnected += async connectionId =>
        {
            _log($"[Reconnected] ConnectionId={connectionId}");
            try
            {
                foreach (var group in _groups.Keys)
                    await Connection.InvokeAsync("JoinGroup", group);
                _log("[SubscriptionsRestored] Rejoined groups. Missed events are not replayed; query REST history if needed.");
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or Microsoft.AspNetCore.SignalR.HubException or HttpRequestException or OperationCanceledException)
            {
                _log($"[SubscriptionError] {exception.Message}. Check connection and join groups again.");
            }
        };
    }

    // AutomaticReconnect does not retry the first StartAsync. Caller handles initial failure.
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Connection.StartAsync(cancellationToken);
        _log($"[Connected] ConnectionId={Connection.ConnectionId}");
    }

    public async Task JoinAsync(string group)
    {
        await Connection.InvokeAsync("JoinGroup", group);
        _groups.TryAdd(group, 0);
    }

    public async Task LeaveAsync(string group)
    {
        await Connection.InvokeAsync("LeaveGroup", group);
        _groups.TryRemove(group, out _);
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
