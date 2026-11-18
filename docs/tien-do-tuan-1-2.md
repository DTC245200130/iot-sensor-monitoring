# Báo cáo tiến độ tuần 1-2

Đây là bản ghi mốc tuần 1-2 tại commit `b454aff`. Phần SignalR bổ sung sau mốc này
được trình bày tại [báo cáo tuần 3](tien-do-tuan-3.md) và README hiện tại.

## Căn cứ và phạm vi

Đề tài 06 yêu cầu hệ thống giám sát cảm biến IoT. Các bài thực hành được cung cấp:
bài 1.2 hướng dẫn Minimal API, DI và DTO; bài 2.2 hướng dẫn REST API và Console Client
qua HttpClient. Chưa có tài liệu xác định đầu việc riêng theo tuần cho bài tập lớn,
nên mốc hiện tại được chọn là áp dụng kiến thức hai bài này vào đề tài.
Đây không phải bài nộp Catalog/BasicWebApi chép nguyên mẫu.

Docker nằm ở bài 8.2, RabbitMQ ở bài 6.2-7.2, SignalR ở bài 3.2.
Các phần đó chưa nằm trong bản tiến độ này. Yêu cầu chung cuối kỳ vẫn cần được hoàn thành ở các mốc sau.

## Tuần 1: nền tảng Minimal API

- Tạo solution `IoTSensorMonitoring.slnx`, project API dùng `Microsoft.NET.Sdk.Web`, target `net10.0`.
- Đăng ký `InMemorySensorStore` với DI Singleton, ánh xạ route và chạy server.
- `GET /` trả tên service, trạng thái, mốc và cơ chế lưu hiện tại.
- Tách DTO đầu vào/đầu ra; dùng `Results.Ok`, `Created`, `BadRequest`, `NotFound`.
- Ghi log đăng ký cảm biến, tiếp nhận dữ liệu đo.

## Tuần 2: REST và client-server

- Đăng ký/liệt kê/đọc cảm biến, tiếp nhận lần đo, latest và lịch sử có query thời gian.
- Server sinh GUID, Trim chuỗi, kiểm tra trường bắt buộc, trả JSON lỗi.
- Console Client dùng `GetFromJsonAsync`, `PostAsJsonAsync`, `ReadFromJsonAsync`, kiểm tra status.
- Hai cảm biến chạy đồng thời, mỗi cảm biến gửi ba mẫu; thể hiện cả giá trị bình thường và vượt ngưỡng.
- Kiểm chứng lỗi 400, 404, route sai kiểu, mất kết nối, và dữ liệu mất sau restart.
- Script kiểm tra tái chạy được và kết quả thực tế trong `docs/minh-chung/`.

## Kiến trúc hiện tại

```text
Sensor.Client (Console, HttpClient)
    | REST / HTTP + JSON
    v
Sensor.Api (ASP.NET Core Minimal API)
    | DI Singleton
    v
InMemorySensorStore (Dictionary + List, lock)
```

API và client là hai tiến trình độc lập. `Sensor.Contracts` chứa DTO dùng chung.
Kho lưu dùng lock để cập nhật và đọc snapshot an toàn khi nhận nhiều request; đây là
giải pháp cho một tiến trình API, chưa hỗ trợ nhiều bản sao API chia sẻ dữ liệu.

Thiết kế dữ liệu:

| Kiểu | Trường chính | Vai trò |
|---|---|---|
| SensorDto | Id, Name, Type, Unit, AlertThreshold, CreatedAt | Thông tin cảm biến |
| ReadingDto | Id, SensorId, MeasuredAt, ReceivedAt, Value, AlertThreshold, IsAlert | Dữ liệu đo và ngưỡng tại lúc tiếp nhận |
| CreateSensorRequest | Name, Type, Unit, AlertThreshold | Dữ liệu client được phép gửi |
| CreateReadingRequest | Value, MeasuredAt | Mẫu đo của client |
| ReadingPage | Items, Total, Skip, Take | Kết quả lịch sử có phân trang |
| ApiError | Code, Message | Phản hồi lỗi cho client |

## Giải thích khi trình bày

**Route, query và body:** ID lấy từ route `{id:guid}`; from/to/skip/take lấy từ query;
POST nhận DTO từ JSON body. ID sai định dạng không khớp route, khác với GUID hợp lệ nhưng không tìm thấy.

**201 và Location:** tạo tài nguyên mới trả 201. Đăng ký cảm biến trả Location đến API
chi tiết; tạo lần đo trả Location đến lịch sử tại thời điểm đo. Client có thể GET để đọc lại.

**DTO:** request chỉ chứa trường client được phép cung cấp. Response bổ sung ID,
thời gian server nhận và trạng thái cảnh báo do server xác định.

**Singleton:** cùng một kho dùng trong suốt vòng đời tiến trình. Restart API tạo kho mới,
vì vậy toàn bộ dữ liệu cũ mất; chưa có lưu trữ bền vững.

**Lỗi HTTP và mất kết nối:** đầu vào sai trả HTTP 400 cùng ApiError. API tắt gây
HttpRequestException, không có response 400; client hiển thị thông báo và exit code 1.

**Build và Run:** build biên dịch mã thành assembly; run khởi chạy chương trình.
Khi thay source cần build và chạy lại để chương trình dùng phiên bản mới.

## Kịch bản demo

1. Build solution và khởi chạy API ở cổng 5100.
2. Gọi `GET /`, `GET /sensors` để thấy danh sách rỗng.
3. Chạy Console Client: hai cảm biến gửi dữ liệu, hiển thị giá trị vượt ngưỡng và lịch sử.
4. Gọi latest/lịch sử trực tiếp; thử POST thiếu trường và GUID không tồn tại để thấy 400/404.
5. Dừng API rồi chạy client: quan sát lỗi kết nối.
6. Chạy lại API, GET danh sách: rỗng vì đang dùng bộ nhớ.

## Phần cần làm tiếp

Chuyển dữ liệu đo qua RabbitMQ; Worker lưu database và kiểm tra ngưỡng; phát sự kiện
cảnh báo; Notification Service và Dashboard realtime; đóng gói Docker; kiểm chứng phục hồi
khi service tạm dừng. Các phần này là yêu cầu của hệ thống hoàn chỉnh, chưa được đánh dấu hoàn thành.
