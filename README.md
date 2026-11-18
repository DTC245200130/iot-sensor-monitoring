# Hệ thống giám sát cảm biến IoT

Bài tập lớn học phần **Phát triển ứng dụng phân tán**, đề tài 06.
Mốc hiện tại: **tuần 2 - Minimal API, REST và Console Client** trên C#/.NET 10.

## Phạm vi hiện tại

- Đăng ký, liệt kê và xem chi tiết cảm biến.
- Tiếp nhận dữ liệu đo từ nhiều cảm biến giả lập qua HTTP/JSON.
- Xem dữ liệu mới nhất theo thời điểm đo; lọc lịch sử theo khoảng thời gian, phân trang.
- Kiểm tra đầu vào và trả HTTP 200/201/400/404, có `Location` khi tạo mới.
- Tính trạng thái vượt ngưỡng: `value > alertThreshold`.
- Console Client dùng `HttpClient`, chạy hai luồng giả lập đồng thời và xử lý lỗi kết nối.

Dữ liệu đang lưu trong bộ nhớ tiến trình, mất khi API khởi động lại. Chưa có RabbitMQ,
Monitor Worker, cơ sở dữ liệu, Notification Service, SignalR/Dashboard hay Docker.
Đây là bản tiến độ tuần 2, chưa phải hệ thống hoàn chỉnh theo yêu cầu cuối kỳ.

Mốc tuần 2 được hiểu là vận dụng bài thực hành 1.2 và 2.2 vào đề tài IoT.
Tài liệu được cung cấp chưa có bảng phân công tiến độ tuần riêng của giảng viên.

## Chạy trên máy

Cần .NET SDK **10.x**. Không cần cài package ngoài hoặc cơ sở dữ liệu.
Mở terminal ở thư mục chứa `IoTSensorMonitoring.slnx`:

```powershell
dotnet build IoTSensorMonitoring.slnx
dotnet run --project src/Sensor.Api --no-launch-profile --urls http://localhost:5100
```

Giữ API chạy, mở terminal thứ hai:

```powershell
dotnet run --project src/Sensor.Client -- http://localhost:5100
```

Client tạo hai cảm biến nhiệt độ/độ ẩm, mỗi cảm biến gửi ba lần đo, đọc lại dữ liệu
mới nhất/lịch sử và thử một yêu cầu sai để quan sát HTTP 400.
Chạy client lần nữa sẽ tạo thêm hai cảm biến. Dừng API bằng `Ctrl+C`.

Đổi cổng bằng `--urls`; truyền cùng URL cho client. Client cũng đọc biến môi trường
`SENSOR_API_URL` khi không có tham số URL; mặc định là `http://localhost:5100`.

## Cấu trúc

```text
IoTSensorMonitoring.slnx
src/
  Sensor.Api/          ASP.NET Core Minimal API và kho lưu trong bộ nhớ
  Sensor.Client/       Console App giả lập cảm biến, gọi REST API
  Sensor.Contracts/    DTO request/response dùng chung
scripts/
  verify-week2.ps1     Build, chạy API và kiểm tra HTTP tự động
docs/
  tien-do-tuan-1-2.md  Phạm vi, thiết kế, giải thích và kịch bản báo cáo
  api.md              Đặc tả endpoint và ví dụ JSON
  minh-chung/          Kết quả kiểm thử thực tế
```

Luồng hiện tại: `Sensor.Client -> HTTP/JSON -> Sensor.Api -> InMemorySensorStore`.
Hai chương trình chạy trong hai tiến trình riêng. Contracts là thư viện DTO, không phải service.

## Kiểm chứng

Windows PowerShell hoặc PowerShell 7:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-week2.ps1
```

Script tự chọn cổng trống, build Release, khởi chạy/dừng API của lần kiểm tra và ghi
log/JSON vào `artifacts/`. Có thể chỉ định `-Port 5200` nếu cổng đó chưa được sử dụng.
Các ca bao gồm ngưỡng bằng/vượt, dữ liệu đến sai thứ tự, múi giờ, lọc khoảng thời gian,
20 POST đồng thời, client khi API tắt và việc mất dữ liệu sau restart.

Kết quả đã ghi nhận: **build 0 lỗi/0 cảnh báo, 32 kiểm tra đạt**, cùng các xác nhận
giá trị phản hồi và tính toàn vẹn dữ liệu. Xem [minh chứng](docs/minh-chung/ket-qua-tuan-2.md).
Workflow GitHub Actions sẽ chạy cùng script khi push/PR; chưa có kết quả CI từ xa trước khi đẩy repo.

Xem [API](docs/api.md) và [báo cáo tiến độ](docs/tien-do-tuan-1-2.md) để chuẩn bị demo.
