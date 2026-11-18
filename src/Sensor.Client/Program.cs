using System.Net;
using System.Net.Http.Json;
using System.Text;
using Sensor.Contracts;

Console.OutputEncoding = Encoding.UTF8;
var address = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("SENSOR_API_URL") ?? "http://localhost:5100";
if (!Uri.TryCreate(address, UriKind.Absolute, out var apiUri) || (apiUri.Scheme != "http" && apiUri.Scheme != "https"))
{
    Console.Error.WriteLine("URL API phải bắt đầu bằng http:// hoặc https://.");
    return 2;
}
using var client = new HttpClient { BaseAddress = apiUri, Timeout = TimeSpan.FromSeconds(10) };
try
{
    Console.WriteLine($"Sensor API: {apiUri}");
    var sensors = await client.GetFromJsonAsync<SensorDto[]>("/sensors") ?? [];
    Console.WriteLine($"GET /sensors: {sensors.Length} cảm biến đã đăng ký.");
    var temperature = await Register(new("Nhiệt độ phòng A", "temperature", "°C", 35m));
    var humidity = await Register(new("Độ ẩm phòng A", "humidity", "%", 80m));
    await Task.WhenAll(Simulate(temperature, [28m, 35m, 38.5m]), Simulate(humidity, [60m, 75m, 85m]));
    foreach (var sensor in new[] { temperature, humidity })
    {
        var latest = await client.GetFromJsonAsync<ReadingDto>($"/sensors/{sensor.Id}/readings/latest");
        Console.WriteLine($"Mới nhất {sensor.Name}: {latest!.Value} {sensor.Unit}; cảnh báo={latest.IsAlert}");
        var history = await client.GetFromJsonAsync<ReadingPage>($"/sensors/{sensor.Id}/readings?take=10");
        Console.WriteLine($"Lịch sử {sensor.Name}: {history!.Total} bản ghi.");
    }
    using var invalid = await client.PostAsJsonAsync("/sensors", new CreateSensorRequest(" ", "temperature", "°C", 35m));
    if (invalid.StatusCode != HttpStatusCode.BadRequest)
        throw new InvalidOperationException($"Expected 400, received {(int)invalid.StatusCode}.");
    var error = await invalid.Content.ReadFromJsonAsync<ApiError>();
    Console.WriteLine($"Thử dữ liệu sai: HTTP {(int)invalid.StatusCode} - {error!.Code}: {error.Message}");
    Console.WriteLine("Demo tuần 2 hoàn tất. Dữ liệu trong bộ nhớ sẽ mất khi API khởi động lại.");
    return 0;
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine($"Lỗi HTTP/kết nối: {exception.Message}");
    Console.Error.WriteLine("Kiểm tra API đã chạy và URL/cổng đúng. Mất kết nối khác với phản hồi HTTP 400.");
    return 1;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine("API không phản hồi trong 10 giây.");
    return 1;
}
catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"Phản hồi API không đúng dự kiến: {exception.Message}");
    return 1;
}

async Task<SensorDto> Register(CreateSensorRequest request)
{
    using var response = await client.PostAsJsonAsync("/sensors", request);
    if (response.StatusCode != HttpStatusCode.Created)
        throw new HttpRequestException($"POST /sensors: {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    var sensor = await response.Content.ReadFromJsonAsync<SensorDto>() ?? throw new InvalidOperationException("API returned an empty sensor.");
    Console.WriteLine($"Đăng ký {sensor.Name}: HTTP 201; Location={response.Headers.Location}");
    return sensor;
}

async Task Simulate(SensorDto sensor, decimal[] values)
{
    foreach (var value in values)
    {
        using var response = await client.PostAsJsonAsync($"/sensors/{sensor.Id}/readings", new CreateReadingRequest(value, DateTimeOffset.UtcNow));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new HttpRequestException($"Send reading: {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var reading = await response.Content.ReadFromJsonAsync<ReadingDto>() ?? throw new InvalidOperationException("API returned an empty reading.");
        Console.WriteLine($"  {sensor.Name}: {reading.Value} {sensor.Unit}; cảnh báo={reading.IsAlert}");
        await Task.Delay(100);
    }
}
