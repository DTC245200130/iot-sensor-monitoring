using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Sensor.Contracts;
using Sensor.Monitor;

Console.OutputEncoding = Encoding.UTF8;
var address = "http://localhost:5100";
var group = RealtimeProtocol.AllSensorsGroup;
var duration = 0;
try
{
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--group": group = args[++i]; break;
            case "--sensor": group = RealtimeProtocol.SensorGroup(Guid.Parse(args[++i])); break;
            case "--no-group": group = ""; break;
            case "--duration": duration = int.Parse(args[++i]); break;
            case "--help":
                Console.WriteLine("Sensor.Monitor [http://localhost:5100] [--group name | --sensor guid | --no-group] [--duration seconds]");
                return 0;
            default:
                if (!args[i].StartsWith("--")) address = args[i];
                else throw new ArgumentException($"Unknown option: {args[i]}");
                break;
        }
    }
    if (duration < 0 || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
        || (uri.Scheme != "http" && uri.Scheme != "https"))
        throw new ArgumentException("Require an HTTP(S) API URL and duration >= 0.");
}
catch (Exception exception) when (exception is ArgumentException or FormatException
    or IndexOutOfRangeException or OverflowException)
{
    Console.Error.WriteLine($"Invalid arguments: {exception.Message}. Use --help.");
    return 2;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; shutdown.Cancel(); };
await using var monitor = new MonitorSession(new Uri(address), message =>
    Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} {message}"));
try
{
    await monitor.StartAsync(shutdown.Token);
    if (group.Length > 0) await monitor.JoinAsync(group);
    await monitor.Connection.InvokeAsync("Echo", "IoT Monitor week 3", shutdown.Token);
    Console.WriteLine("Commands: /echo text, /all text, /join group, /leave group, /group name text,");
    Console.WriteLine("          /watch sensor-guid, /watchall, /unwatchall, /quit. Ctrl+C to exit.");
    if (duration > 0)
    {
        await Task.Delay(TimeSpan.FromSeconds(duration), shutdown.Token);
        return 0;
    }
    while (!shutdown.IsCancellationRequested)
    {
        var line = await Console.In.ReadLineAsync(shutdown.Token);
        if (line is null || line.Trim() == "/quit") break;
        var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) continue;
        try
        {
            var argument = parts.Length == 2 ? parts[1] : "";
            switch (parts[0])
            {
                case "/echo": await monitor.Connection.InvokeAsync("Echo", argument, shutdown.Token); break;
                case "/all": await monitor.Connection.InvokeAsync("Broadcast", argument, shutdown.Token); break;
                case "/join": await monitor.JoinAsync(argument); break;
                case "/leave": await monitor.LeaveAsync(argument); break;
                case "/watch": await monitor.JoinAsync(RealtimeProtocol.SensorGroup(Guid.Parse(argument))); break;
                case "/watchall": await monitor.JoinAsync(RealtimeProtocol.AllSensorsGroup); break;
                case "/unwatchall": await monitor.LeaveAsync(RealtimeProtocol.AllSensorsGroup); break;
                case "/group":
                    var message = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (message.Length != 2) throw new ArgumentException("Usage: /group name message");
                    await monitor.Connection.InvokeAsync("SendToGroup", message[0], message[1], shutdown.Token);
                    break;
                default: Console.WriteLine("Unknown command. Use /quit to exit."); break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or HubException
            or FormatException or ArgumentException or HttpRequestException)
        {
            Console.Error.WriteLine($"[CommandError] {exception.Message}");
        }
    }
    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return 0; }
catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException
    or HubException or OperationCanceledException)
{
    Console.Error.WriteLine($"[ConnectionError] {exception.Message}. Start API, check URL, then run Monitor again.");
    return 1;
}
