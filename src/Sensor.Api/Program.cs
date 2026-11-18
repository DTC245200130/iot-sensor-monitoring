using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Sensor.Api.Hubs;
using Sensor.Api.Services;
using Sensor.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Services.AddSingleton<InMemorySensorStore>();
builder.Services.AddSingleton<SensorSubscriptions>();
builder.Services.AddSignalR();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var badRequest = error is BadHttpRequestException;
    context.Response.StatusCode = badRequest ? 400 : 500;
    await context.Response.WriteAsJsonAsync(new ApiError(badRequest ? "INVALID_REQUEST" : "INTERNAL_ERROR",
        badRequest ? "Check the JSON body, content type and query parameters." : "An unexpected server error occurred."));
}));

app.MapGet("/", () => Results.Ok(new
{
    service = "Sensor.Api", status = "running", milestone = "week-3", storage = "in-memory",
    hub = RealtimeProtocol.HubPath
}));

app.MapPost("/sensors", (CreateSensorRequest request, InMemorySensorStore store, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100
        || string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 50
        || string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > 20 || request.AlertThreshold is null)
        return Results.BadRequest(new ApiError("INVALID_SENSOR",
            "Name (1-100), type (1-50), unit (1-20) and alertThreshold are required."));
    var sensor = store.AddSensor(request);
    logger.LogInformation("Registered sensor {SensorId} ({SensorName})", sensor.Id, sensor.Name);
    return Results.Created($"/sensors/{sensor.Id}", sensor);
});

app.MapGet("/sensors", (InMemorySensorStore store) => Results.Ok(store.GetSensors()));
app.MapGet("/sensors/{id:guid}", (Guid id, InMemorySensorStore store) =>
    store.GetSensor(id) is { } sensor ? Results.Ok(sensor) : SensorNotFound());

app.MapPost("/sensors/{id:guid}/readings", async Task<IResult> (Guid id, CreateReadingRequest request,
    InMemorySensorStore store, IHubContext<SensorHub> hub, SensorSubscriptions subscriptions, ILogger<Program> logger) =>
{
    if (store.GetSensor(id) is null) return SensorNotFound();
    if (request.Value is null || request.MeasuredAt is null || request.MeasuredAt == default(DateTimeOffset))
        return Results.BadRequest(new ApiError("INVALID_READING", "Value and a valid measuredAt timestamp are required."));
    var reading = store.AddReading(id, request);
    logger.LogInformation("Received reading {ReadingId} for {SensorId}: {Value}, alert={IsAlert}",
        reading.Id, id, reading.Value, reading.IsAlert);
    // Week 3: synchronous processing plus best-effort realtime delivery; no message broker yet.
    var recipients = hub.Clients.Clients(subscriptions.ForSensor(id));
    await recipients.SendAsync(RealtimeProtocol.ReadingReceived, reading);
    if (reading.IsAlert)
        await recipients.SendAsync(RealtimeProtocol.AlertRaised, reading);
    return Results.Created($"/sensors/{id}/readings?from={Uri.EscapeDataString(reading.MeasuredAt.ToString("O"))}&to={Uri.EscapeDataString(reading.MeasuredAt.ToString("O"))}", reading);
});

app.MapGet("/sensors/{id:guid}/readings/latest", (Guid id, InMemorySensorStore store) =>
{
    if (store.GetSensor(id) is null) return SensorNotFound();
    return store.GetLatest(id) is { } reading ? Results.Ok(reading)
        : Results.NotFound(new ApiError("READING_NOT_FOUND", "This sensor has no readings yet."));
});

app.MapGet("/sensors/{id:guid}/readings", (Guid id, DateTimeOffset? from, DateTimeOffset? to,
    int? skip, int? take, InMemorySensorStore store) =>
{
    if (store.GetSensor(id) is null) return SensorNotFound();
    if ((from.HasValue && to.HasValue && from.Value > to.Value) || skip is < 0 || take is < 1 or > 500)
        return Results.BadRequest(new ApiError("INVALID_QUERY", "Require from <= to, skip >= 0 and 1 <= take <= 500."));
    return Results.Ok(store.GetHistory(id, from, to, skip ?? 0, take ?? 100));
});

app.MapHub<SensorHub>(RealtimeProtocol.HubPath);
app.Run();

static IResult SensorNotFound() => Results.NotFound(new ApiError("SENSOR_NOT_FOUND", "The sensor does not exist."));
