using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Sensor.Contracts;
using Sensor.Monitor;

Console.OutputEncoding = Encoding.UTF8;
var root = Path.GetFullPath(args.FirstOrDefault() ?? Directory.GetCurrentDirectory());
var artifacts = Path.Combine(root, "artifacts");
Directory.CreateDirectory(artifacts);
var checks = new List<CheckResult>();
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var baseUrl = new Uri($"http://127.0.0.1:{port}");
using var http = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(3) };
await using var api = new TestApi(root, artifacts, baseUrl, http);
var aLog = new ConcurrentQueue<string>();
var bLog = new ConcurrentQueue<string>();
TimeSpan[] retry = [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)];
await using var a = new MonitorSession(baseUrl, aLog.Enqueue, retry);
await using var b = new MonitorSession(baseUrl, bLog.Enqueue, retry);
var aReadings = new ConcurrentQueue<ReadingDto>();
var bReadings = new ConcurrentQueue<ReadingDto>();
var aAlerts = new ConcurrentQueue<ReadingDto>();
var bAlerts = new ConcurrentQueue<ReadingDto>();
a.Connection.On<ReadingDto>(RealtimeProtocol.ReadingReceived, aReadings.Enqueue);
b.Connection.On<ReadingDto>(RealtimeProtocol.ReadingReceived, bReadings.Enqueue);
a.Connection.On<ReadingDto>(RealtimeProtocol.AlertRaised, aAlerts.Enqueue);
b.Connection.On<ReadingDto>(RealtimeProtocol.AlertRaised, bAlerts.Enqueue);
var success = false;
try
{
    await api.Start();
    await a.StartAsync();
    await b.StartAsync();
    await Until(() => aLog.Any(x => x.Contains("connected")));
    Check("TN1: two independent connections", a.Connection.ConnectionId != b.Connection.ConnectionId,
        "Two HubConnections have different ConnectionIds; SystemMessage observed.");

    await b.Connection.InvokeAsync("Echo", "caller-only");
    await Until(() => bLog.Any(x => x == "[Echo] Echo: caller-only"));
    await Barrier(a);
    Check("TN1: Echo reaches Caller only", !aLog.Any(x => x.Contains("caller-only")), "B received EchoResponse; A did not.");
    await b.Connection.InvokeAsync("Broadcast", "broadcast-both");
    await Until(() => aLog.Any(x => x.Contains("broadcast-both")) && bLog.Any(x => x.Contains("broadcast-both")));
    Check("TN2: Broadcast reaches All", true, "A and B received BroadcastMessage.");

    await a.JoinAsync("room-a");
    await b.JoinAsync("room-b");
    await a.Connection.InvokeAsync("SendToGroup", "room-a", "group-a-only");
    await Until(() => aLog.Any(x => x.Contains("group-a-only")));
    await Barrier(b);
    Check("TN3: other group excluded", !bLog.Any(x => x.Contains("group-a-only")), "A in room-a received; B in room-b did not.");
    await b.JoinAsync("room-a");
    await a.Connection.InvokeAsync("SendToGroup", "room-a", "same-group-both");
    await Until(() => aLog.Any(x => x.Contains("same-group-both")) && bLog.Any(x => x.Contains("same-group-both")));
    Check("TN3: same group receives", true, "A and B joined room-a and received GroupMessage.");
    await b.LeaveAsync("room-a");
    await a.Connection.InvokeAsync("SendToGroup", "room-a", "after-leave");
    await Until(() => aLog.Any(x => x.Contains("after-leave")));
    await Barrier(b);
    Check("TN3: LeaveGroup takes effect", !bLog.Any(x => x.Contains("after-leave")), "B no longer receives room-a messages.");
    var rejected = false;
    try { await a.Connection.InvokeAsync("JoinGroup", " "); } catch (HubException) { rejected = true; }
    Check("Invalid group rejected", rejected, "Blank group returns HubException.");
    rejected = false;
    try { await a.Connection.InvokeAsync("Broadcast", " "); } catch (HubException) { rejected = true; }
    Check("Invalid message rejected", rejected, "Blank broadcast returns HubException.");

    var sensor1 = await Register("Temperature", 35);
    var sensor2 = await Register("Humidity", 80);
    await a.JoinAsync(RealtimeProtocol.AllSensorsGroup);
    await b.JoinAsync(RealtimeProtocol.SensorGroup(sensor1.Id));
    var normal = await Reading(sensor1.Id, 35);
    await Until(() => aReadings.Any(r => r.Id == normal.Id) && bReadings.Any(r => r.Id == normal.Id));
    await Barrier(a); await Barrier(b);
    Check("Normal readings realtime; equal threshold no alert", !aAlerts.Any(r => r.Id == normal.Id)
        && !bAlerts.Any(r => r.Id == normal.Id), "Value 35 == threshold 35: ReadingReceived on A/B, no AlertRaised.");
    var alert1 = await Reading(sensor1.Id, 36);
    await Until(() => aAlerts.Any(r => r.Id == alert1.Id) && bAlerts.Any(r => r.Id == alert1.Id));
    Check("Above threshold alerts realtime", alert1.IsAlert && aReadings.Any(r => r.Id == alert1.Id)
        && bReadings.Any(r => r.Id == alert1.Id), "Value 36 > 35: ReadingReceived and AlertRaised on both subscribers.");
    var alert2 = await Reading(sensor2.Id, 81);
    await Until(() => aAlerts.Any(r => r.Id == alert2.Id));
    await Barrier(b);
    Check("Sensor subscription filters reading and alert", !bReadings.Any(r => r.Id == alert2.Id)
        && !bAlerts.Any(r => r.Id == alert2.Id), "A follows all sensors; B follows sensor1 and receives neither event for sensor2.");
    await a.JoinAsync(RealtimeProtocol.SensorGroup(sensor1.Id));
    var overlap = await Reading(sensor1.Id, 37);
    await Until(() => aAlerts.Any(r => r.Id == overlap.Id));
    await Barrier(a);
    Check("Overlapping subscriptions deliver once", aAlerts.Count(r => r.Id == overlap.Id) == 1
        && aReadings.Count(r => r.Id == overlap.Id) == 1, "A in all-sensors and sensor1 groups receives each event once.");
    await b.LeaveAsync(RealtimeProtocol.SensorGroup(sensor1.Id));
    var afterUnsubscribe = await Reading(sensor1.Id, 38);
    await Until(() => aAlerts.Any(r => r.Id == afterUnsubscribe.Id));
    await Barrier(b);
    Check("Unsubscribe stops realtime data", !bReadings.Any(r => r.Id == afterUnsubscribe.Id)
        && !bAlerts.Any(r => r.Id == afterUnsubscribe.Id), "B leaves its sensor group; A continues receiving.");
    using (var invalid = await http.PostAsJsonAsync($"/sensors/{sensor1.Id}/readings", new { value = 99 }))
    {
        await Barrier(a);
        Check("Invalid HTTP data does not broadcast", invalid.StatusCode == HttpStatusCode.BadRequest
            && !aReadings.Any(r => r.Value == 99), "Missing timestamp returns 400 and emits no ReadingReceived.");
    }
    var latest = await http.GetFromJsonAsync<ReadingDto>($"/sensors/{sensor1.Id}/readings/latest");
    Check("REST and realtime reference the same reading", latest?.Id == afterUnsubscribe.Id,
        "Latest REST reading ID matches the event payload.");

    // Exercise the actual Console Monitor process, not only the test connections.
    var monitorStart = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true,
        CreateNoWindow = true, UseShellExecute = false };
    monitorStart.ArgumentList.Add(Path.Combine(root, "src", "Sensor.Monitor", "bin", "Release", "net10.0", "Sensor.Monitor.dll"));
    monitorStart.ArgumentList.Add(baseUrl.ToString());
    monitorStart.ArgumentList.Add("--duration"); monitorStart.ArgumentList.Add("2");
    using (var monitor = Process.Start(monitorStart) ?? throw new Exception("Cannot launch Monitor"))
    {
        var output = monitor.StandardOutput.ReadToEndAsync();
        var error = monitor.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await monitor.WaitForExitAsync(timeout.Token); }
        finally { if (!monitor.HasExited) monitor.Kill(true); }
        var text = await output + await error;
        await File.WriteAllTextAsync(Path.Combine(artifacts, "week3-monitor.log"), text);
        Check("Console Monitor connects and exits cleanly", monitor.ExitCode == 0 && text.Contains("[Connected]")
            && text.Contains("[Echo] Echo: IoT Monitor week 3"), "Real Monitor process --duration 2 completed with exit code 0.");
    }
    await Until(() => aLog.Any(x => x.Contains("disconnected")));
    Check("SystemMessage on disconnect", true, "Existing monitor saw another monitor disconnect.");

    await b.JoinAsync("room-a");
    var oldId = a.Connection.ConnectionId;
    await api.Stop();
    await Until(() => aLog.Any(x => x.StartsWith("[Reconnecting]")) && bLog.Any(x => x.StartsWith("[Reconnecting]")));
    Check("TN4: Reconnecting observed", true, "A and B detect the stopped API.");
    await api.Start();
    await Until(() => aLog.Any(x => x.StartsWith("[SubscriptionsRestored]"))
        && bLog.Any(x => x.StartsWith("[SubscriptionsRestored]")), 15000);
    Check("TN4: automatic reconnect", a.Connection.State == HubConnectionState.Connected
        && a.Connection.ConnectionId != oldId, "Monitor reconnected with a new ConnectionId.");
    await a.Connection.InvokeAsync("SendToGroup", "room-a", "group-after-restart");
    await Until(() => aLog.Any(x => x.Contains("group-after-restart")) && bLog.Any(x => x.Contains("group-after-restart")));
    Check("TN4: groups restored after reconnect", true, "A and B receive room-a message after rejoining automatically.");
    var resetSensors = await http.GetFromJsonAsync<SensorDto[]>("/sensors");
    Check("Restart remains in-memory", resetSensors is { Length: 0 }, "Week-2 storage limitation remains explicit.");
    var replacement = await Register("Restarted sensor", 35);
    var resumed = await Reading(replacement.Id, 40);
    await Until(() => aAlerts.Any(r => r.Id == resumed.Id));
    Check("Live IoT alerts resume after reconnect", true, "A restored all-sensors subscription and received new reading/alert.");

    var closedLog = new ConcurrentQueue<string>();
    await using (var probe = new MonitorSession(baseUrl, closedLog.Enqueue, [TimeSpan.Zero, TimeSpan.FromMilliseconds(100)]))
    {
        await probe.StartAsync();
        await api.Stop();
        await Until(() => closedLog.Any(x => x.StartsWith("[Closed]")), 10000);
        Check("TN4: Closed after retry exhaustion", probe.Connection.State == HubConnectionState.Disconnected,
            "A short retry policy exhausts while the API stays offline; Closed observed.");
    }
    await using (var initial = new MonitorSession(baseUrl))
    {
        var initialFailed = false;
        try { await initial.StartAsync(); } catch (HttpRequestException) { initialFailed = true; }
        Check("Initial StartAsync failure is explicit", initialFailed, "AutomaticReconnect does not hide an initial offline API.");
    }
    success = true;
    Console.WriteLine($"PASS: {checks.Count} SignalR integration checks.");
}
catch (Exception exception)
{
    checks.Add(new("Unexpected test failure", false, exception.ToString()));
    Console.Error.WriteLine(exception);
}
finally
{
    await api.Stop();
    await File.WriteAllLinesAsync(Path.Combine(artifacts, "week3-monitor-a.log"), aLog);
    await File.WriteAllLinesAsync(Path.Combine(artifacts, "week3-monitor-b.log"), bLog);
    await File.WriteAllTextAsync(Path.Combine(artifacts, "week3-results.json"),
        JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
}
return success ? 0 : 1;

void Check(string name, bool passed, string detail)
{
    checks.Add(new(name, passed, detail));
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
    if (!passed) throw new InvalidOperationException(detail);
}

static async Task Until(Func<bool> predicate, int timeoutMs = 8000)
{
    var watch = Stopwatch.StartNew();
    while (!predicate())
    {
        if (watch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Expected realtime event was not received.");
        await Task.Delay(25);
    }
}

static async Task Barrier(MonitorSession session)
{
    // Echo on this same connection proves earlier queued events have been dispatched.
    var marker = "barrier-" + Guid.NewGuid();
    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var subscription = session.Connection.On<string>(RealtimeProtocol.EchoResponse, text =>
    {
        if (text == "Echo: " + marker) done.TrySetResult();
    });
    await session.Connection.InvokeAsync("Echo", marker);
    await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

async Task<SensorDto> Register(string name, decimal threshold)
{
    using var response = await http.PostAsJsonAsync("/sensors", new CreateSensorRequest(name, "test", "C", threshold));
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<SensorDto>() ?? throw new Exception("Empty sensor");
}

async Task<ReadingDto> Reading(Guid sensorId, decimal value)
{
    using var response = await http.PostAsJsonAsync($"/sensors/{sensorId}/readings", new CreateReadingRequest(value, DateTimeOffset.UtcNow));
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<ReadingDto>() ?? throw new Exception("Empty reading");
}

sealed record CheckResult(string Test, bool Passed, string Detail);

sealed class TestApi(string root, string artifacts, Uri baseUrl, HttpClient http) : IAsyncDisposable
{
    private Process? _process;
    private readonly ConcurrentQueue<string> _log = new();
    private int _run;

    public async Task Start()
    {
        var dll = Path.Combine(root, "src", "Sensor.Api", "bin", "Release", "net10.0", "Sensor.Api.dll");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(dll); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(baseUrl.ToString());
        _process = Process.Start(start) ?? throw new Exception("Could not launch API");
        _run++;
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) _log.Enqueue(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _log.Enqueue(e.Data); };
        _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
        for (var i = 0; i < 100; i++)
        {
            if (_process.HasExited) throw new Exception("API exited at startup.");
            try
            {
                using var response = await http.GetAsync("/");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        throw new TimeoutException("API startup timed out.");
    }

    public async Task Stop()
    {
        if (_process is null) return;
        if (!_process.HasExited) _process.Kill(true);
        await _process.WaitForExitAsync();
        _process.WaitForExit();
        _process.Dispose(); _process = null;
        await File.WriteAllLinesAsync(Path.Combine(artifacts, $"week3-api-{_run}.log"), _log);
        _log.Clear();
    }

    public async ValueTask DisposeAsync() => await Stop();
}
