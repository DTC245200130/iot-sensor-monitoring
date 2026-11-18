using Sensor.Contracts;

namespace Sensor.Api.Services;

// Union membership before publishing so an all-sensors + specific-sensor subscriber
// receives only one copy of each reading/alert, even when groups overlap.
public sealed class SensorSubscriptions
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _groupsByConnection = new(StringComparer.Ordinal);

    public void Join(string connectionId, string group)
    {
        lock (_gate)
        {
            if (!_groupsByConnection.TryGetValue(connectionId, out var groups))
                _groupsByConnection[connectionId] = groups = new(StringComparer.Ordinal);
            groups.Add(group);
        }
    }

    public void Leave(string connectionId, string group)
    {
        lock (_gate)
        {
            if (!_groupsByConnection.TryGetValue(connectionId, out var groups)) return;
            groups.Remove(group);
            if (groups.Count == 0) _groupsByConnection.Remove(connectionId);
        }
    }

    public void Disconnect(string connectionId)
    {
        lock (_gate) _groupsByConnection.Remove(connectionId);
    }

    public string[] ForSensor(Guid sensorId)
    {
        var group = RealtimeProtocol.SensorGroup(sensorId);
        lock (_gate)
            return _groupsByConnection.Where(entry => entry.Value.Contains(RealtimeProtocol.AllSensorsGroup)
                || entry.Value.Contains(group)).Select(entry => entry.Key).ToArray();
    }
}
