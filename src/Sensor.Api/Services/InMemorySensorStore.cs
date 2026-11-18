using Sensor.Contracts;

namespace Sensor.Api.Services;

// Week 2: volatile storage shared by requests in this API process.
public sealed class InMemorySensorStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SensorDto> _sensors = new();
    private readonly Dictionary<Guid, List<ReadingDto>> _readings = new();

    public SensorDto AddSensor(CreateSensorRequest request)
    {
        var sensor = new SensorDto(Guid.NewGuid(), request.Name!.Trim(), request.Type!.Trim(),
            request.Unit!.Trim(), request.AlertThreshold!.Value, DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _sensors.Add(sensor.Id, sensor);
            _readings.Add(sensor.Id, []);
        }
        return sensor;
    }

    public SensorDto[] GetSensors()
    {
        lock (_gate)
            return _sensors.Values.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToArray();
    }

    public SensorDto? GetSensor(Guid id)
    {
        lock (_gate)
            return _sensors.GetValueOrDefault(id);
    }

    public ReadingDto AddReading(Guid sensorId, CreateReadingRequest request)
    {
        lock (_gate)
        {
            var sensor = _sensors[sensorId];
            var reading = new ReadingDto(Guid.NewGuid(), sensorId,
                request.MeasuredAt!.Value.ToUniversalTime(), DateTimeOffset.UtcNow,
                request.Value!.Value, sensor.AlertThreshold, request.Value.Value > sensor.AlertThreshold);
            _readings[sensorId].Add(reading);
            return reading;
        }
    }

    public ReadingDto? GetLatest(Guid sensorId)
    {
        lock (_gate)
            return OrderedReadings(sensorId).FirstOrDefault();
    }

    public ReadingPage GetHistory(Guid sensorId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take)
    {
        lock (_gate)
        {
            var matches = OrderedReadings(sensorId).Where(r =>
                (!from.HasValue || r.MeasuredAt >= from.Value) && (!to.HasValue || r.MeasuredAt <= to.Value)).ToArray();
            return new ReadingPage(matches.Skip(skip).Take(take).ToArray(), matches.Length, skip, take);
        }
    }

    private IOrderedEnumerable<ReadingDto> OrderedReadings(Guid sensorId) =>
        _readings[sensorId].OrderByDescending(r => r.MeasuredAt).ThenByDescending(r => r.ReceivedAt).ThenBy(r => r.Id);
}
