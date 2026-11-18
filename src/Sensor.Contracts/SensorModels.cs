namespace Sensor.Contracts;

public sealed record CreateSensorRequest(string? Name, string? Type, string? Unit, decimal? AlertThreshold);
public sealed record SensorDto(Guid Id, string Name, string Type, string Unit, decimal AlertThreshold, DateTimeOffset CreatedAt);
public sealed record CreateReadingRequest(decimal? Value, DateTimeOffset? MeasuredAt);
public sealed record ReadingDto(Guid Id, Guid SensorId, DateTimeOffset MeasuredAt, DateTimeOffset ReceivedAt, decimal Value, decimal AlertThreshold, bool IsAlert);
public sealed record ReadingPage(IReadOnlyList<ReadingDto> Items, int Total, int Skip, int Take);
public sealed record ApiError(string Code, string Message);
