# Hệ thống giám sát cảm biến IoT

Bài tập lớn học phần **Phát triển ứng dụng phân tán**, đề tài 06.
Mốc hiện tại: **tuần 3 - SignalR thời gian thực** trên C#/.NET 10.

## Đã thực hiện

- Tuần 1-2: Minimal API, REST, đăng ký cảm biến, tiếp nhận lần đo, latest/lịch sử, Console Client dùng HttpClient.
- Tuần 3: SignalR Hub, Console Monitor, Caller/All/Group, dữ liệu đo và cảnh báo realtime.
- Theo dõi tất cả hoặc từng cảm biến; hủy theo dõi; xử lý Reconnecting/Reconnected/Closed và tự tham gia lại nhóm.
- Kiểm tra dữ liệu đầu vào, trạng thái HTTP và log các thành phần.

Dữ liệu đang lưu trong bộ nhớ và mất khi API khởi động lại. Chưa triển khai RabbitMQ,
Monitor Worker, database, Notification Service độc lập, Web Dashboard hoặc Docker.
Đây là bản tiến độ tuần 3; các thành phần còn lại thuộc các mốc sau.
Mốc tuần được đối chiếu theo bài thực hành 1.2, 2.2 và 3.2 vì chưa có lịch tiến độ riêng của giảng viên.

## Chạy demo tuần 3

Cần .NET SDK **10.x** và kết nối NuGet cho lần restore đầu.
SignalR server có sẵn trong ASP.NET Core; Monitor dùng gói
Microsoft.AspNetCore.SignalR.Client **10.0.14**. Dùng bản vá cùng dòng .NET 10 thay cho
10.0.0 trong bài thực hành vì NuGet báo NU1903 với bản cũ; không tắt kiểm tra NuGet.

Terminal 1 - API:

```powershell
dotnet build IoTSensorMonitoring.slnx
dotnet run --project src/Sensor.Api --no-launch-profile --urls http://localhost:5100
```

Terminal 2 - Monitor nhận dữ liệu/cảnh báo của tất cả cảm biến:

```powershell
dotnet run --project src/Sensor.Monitor -- http://localhost:5100
```

Terminal 3 - một Monitor khác để đối chiếu phạm vi nhận:

```powershell
dotnet run --project src/Sensor.Monitor -- http://localhost:5100 --no-group
```

Terminal 4 - giả lập hai cảm biến gửi dữ liệu:

```powershell
dotnet run --project src/Sensor.Client -- http://localhost:5100
```

Monitor mặc định nhận ReadingReceived và AlertRaised. Monitor --no-group vẫn nhận
Broadcast/System và Echo của chính nó, nhưng không nhận dữ liệu đo khi chưa theo dõi nhóm cảm biến.
Giá trị bằng ngưỡng không cảnh báo; chỉ value > alertThreshold mới phát AlertRaised.

Các lệnh nhập trong Monitor:

| Lệnh | Tác dụng |
|---|---|
| /echo Xin chao | Phản hồi Echo chỉ về chính kết nối gọi |
| /all Thong bao | Broadcast tới mọi monitor đang kết nối |
| /join phong-a | Tham gia nhóm phong-a |
| /group phong-a Noi dung | Gửi tới các monitor trong phong-a |
| /leave phong-a | Rời nhóm |
| /watchall | Theo dõi mọi cảm biến |
| /unwatchall | Bỏ theo dõi mọi cảm biến |
| /watch <sensor-guid> | Theo dõi riêng một cảm biến |
| /leave sensor:<sensor-guid> | Bỏ theo dõi riêng cảm biến đó |
| /quit | Thoát monitor |

Có thể khởi chạy với --sensor <guid>, --group phong-a, hoặc --no-group để chọn nhóm ban đầu.
Mặc định là nhóm sensors:all; các tùy chọn chọn nhóm ban đầu ghi đè nhau theo thứ tự nhập.
--duration 10 giữ kết nối 10 giây rồi thoát, hữu ích khi demo tự động.
Dùng --help để xem cú pháp. Đổi cổng API thì truyền cùng URL cho các client.
Sensor.Client cũng hỗ trợ biến môi trường SENSOR_API_URL nếu không truyền URL.

Dừng API và khởi động lại sớm để quan sát Reconnecting/Reconnected. Monitor tự tham gia lại
các nhóm; dữ liệu trong bộ nhớ API vẫn mất, nên cần chạy lại Sensor.Client để tạo cảm biến mới.
SignalR không phát lại sự kiện bị lỡ. Lần kết nối đầu thất bại cần chạy lại Monitor;
sau khi dùng hết lượt reconnect, Closed xuất hiện và cũng cần chạy lại Monitor.

## Cấu trúc và luồng dữ liệu

```text
src/Sensor.Api/          REST API, SensorHub, kho dữ liệu và danh sách đăng ký nhóm
src/Sensor.Client/       Console giả lập cảm biến qua HTTP
src/Sensor.Monitor/      Console giám sát qua SignalR
src/Sensor.Contracts/    DTO và tên sự kiện dùng chung
tests/Sensor.IntegrationTests/  Kiểm thử tích hợp với hai SignalR client thật
scripts/                Script xác minh tuần 2 và 3
docs/                   API, báo cáo tiến độ, minh chứng kiểm thử
```

```text
Sensor.Client -- HTTP POST --> Sensor.Api --> InMemorySensorStore
                                  |
                                  +-- SignalR /hubs/sensors --> Sensor.Monitor A/B
```

API, Simulator và Monitor là các tiến trình riêng. Ở tuần 3, Sensor.Api xử lý lần đo,
đánh dấu cảnh báo và push trực tiếp qua IHubContext; chưa có bước RabbitMQ/Worker.
Nhóm dùng phân loại người nhận, chưa có xác thực/phân quyền.

## Kiểm chứng

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-week2.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-week3.ps1
```

Script tự chọn cổng trống, chỉ khởi chạy/dừng API thử nghiệm của nó và ghi log/JSON
vào artifacts/. Tuần 2: 32 kiểm tra REST. Tuần 3: 24 kiểm tra tích hợp SignalR,
bao gồm hai kết nối, Console Monitor thật, đúng phạm vi gửi, ngưỡng cảnh báo,
đăng ký chồng nhóm không nhận trùng, reconnect/rejoin và Closed.
Workflow GitHub Actions chạy cả hai script khi push/PR.

- [API REST](docs/api.md)
- [Báo cáo tiến độ tuần 1-2](docs/tien-do-tuan-1-2.md)
- [Báo cáo và hướng dẫn demo tuần 3](docs/tien-do-tuan-3.md)
- [Minh chứng tuần 2](docs/minh-chung/ket-qua-tuan-2.md)
- [Minh chứng tuần 3](docs/minh-chung/ket-qua-tuan-3.md)
