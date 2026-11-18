namespace Sensor.Contracts;

public static class RealtimeProtocol
{
    public const string HubPath = "/hubs/sensors";
    public const string AllSensorsGroup = "sensors:all";
    public const string EchoResponse = "EchoResponse";
    public const string BroadcastMessage = "BroadcastMessage";
    public const string GroupMessage = "GroupMessage";
    public const string SystemMessage = "SystemMessage";
    public const string ReadingReceived = "ReadingReceived";
    public const string AlertRaised = "AlertRaised";
    public static string SensorGroup(Guid sensorId) => $"sensor:{sensorId:D}";
}
