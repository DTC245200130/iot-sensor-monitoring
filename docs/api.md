# Sensor API - mốc tuần 2

Base URL mặc định: `http://localhost:5100`. Body JSON dùng tên trường camelCase.
Các ID do server sinh là UUID/GUID; client không tự chọn ID.

| Phương thức | Đường dẫn | Thành công | Lỗi chính |
|---|---|---|---|
| GET | `/` | 200, trạng thái API | |
| POST | `/sensors` | 201, SensorDto và Location | 400 đầu vào sai |
| GET | `/sensors` | 200, mảng cảm biến | |
| GET | `/sensors/{id}` | 200, SensorDto | 404 không tồn tại |
| POST | `/sensors/{id}/readings` | 201, ReadingDto và Location | 400 đầu vào sai; 404 cảm biến không tồn tại |
| GET | `/sensors/{id}/readings/latest` | 200, ReadingDto | 404 cảm biến không tồn tại hoặc chưa có dữ liệu |
| GET | `/sensors/{id}/readings` | 200, ReadingPage | 400 query sai; 404 cảm biến không tồn tại |

## Đăng ký cảm biến

`POST /sensors`:

```json
{
  "name": "Nhiệt độ phòng A",
  "type": "temperature",
  "unit": "°C",
  "alertThreshold": 35
}
```

Tên, loại, đơn vị không được rỗng, giới hạn lần lượt 100/50/20 ký tự; server loại khoảng
trắng đầu/cuối. Ngưỡng là số bắt buộc, cho phép 0 hoặc âm để dùng với nhiều loại cảm biến.
Response có thêm `id` và `createdAt`. Header `Location: /sensors/{id}` trỏ tới API chi tiết.

## Gửi dữ liệu đo

`POST /sensors/{id}/readings`:

```json
{
  "value": 38.5,
  "measuredAt": "2026-11-18T10:00:00+07:00"
}
```

`value` và `measuredAt` bắt buộc. Nên luôn truyền thời điểm ISO 8601 có `Z` hoặc offset.
Giá trị 0/âm hợp lệ. Server chuẩn hóa thời điểm đo về UTC; `receivedAt` là thời điểm server nhận.
Response:

```json
{
  "id": "<reading-guid>",
  "sensorId": "<sensor-guid>",
  "measuredAt": "2026-11-18T03:00:00+00:00",
  "receivedAt": "<server-utc-time>",
  "value": 38.5,
  "alertThreshold": 35,
  "isAlert": true
}
```

Các chuỗi trong dấu `<...>` là ký hiệu minh họa, thay bằng giá trị thực khi gọi API.
`isAlert` chỉ đúng khi **lớn hơn** ngưỡng; bằng ngưỡng vẫn là bình thường.
Ở tuần 2, đây là cờ dữ liệu, chưa phải thông báo realtime.
`Location` trỏ tới lịch sử lọc đúng thời điểm đo, có chứa bản ghi vừa tạo.

## Lịch sử và dữ liệu mới nhất

```text
GET /sensors/{id}/readings?from=2026-11-18T00:00:00Z&to=2026-11-19T00:00:00Z&skip=0&take=100
```

- `from`/`to` tùy chọn, lấy cả hai đầu mút, yêu cầu `from <= to`.
- `skip` mặc định 0, không âm; `take` mặc định 100, từ 1 đến 500.
- Response: `{ "items": [...], "total": 123, "skip": 0, "take": 100 }`.
- `total` là tổng số bản ghi khớp khoảng thời gian trước phân trang.
- Xếp giảm dần theo `measuredAt`, sau đó `receivedAt` và ID để ổn định thứ tự.
- Latest dựa trên thời điểm đo, không dựa vào thứ tự request đến.
- Lịch sử trống trả 200 với `items: []`; latest chưa có dữ liệu trả 404 `READING_NOT_FOUND`.

Khi query dùng offset `+07:00`, mã hóa dấu `+` thành `%2B`; dùng `Z` sẽ dễ thử hơn.

## Lỗi

```json
{ "code": "INVALID_SENSOR", "message": "..." }
```

Các mã: `INVALID_SENSOR`, `INVALID_READING`, `INVALID_QUERY`, `INVALID_REQUEST`,
`SENSOR_NOT_FOUND`, `READING_NOT_FOUND`, `INTERNAL_ERROR`.
GUID sai định dạng không khớp ràng buộc route nên trả 404 không có ApiError từ handler.
Mất kết nối khi API tắt không có HTTP status/body; client bắt ngoại lệ và trả exit code 1.

## Thử nhanh bằng PowerShell

```powershell
$base = 'http://localhost:5100'
$body = @{ name='Phong A'; type='temperature'; unit='C'; alertThreshold=35 } | ConvertTo-Json
$sensor = Invoke-RestMethod -Method Post -Uri "$base/sensors" -ContentType 'application/json' -Body $body
$reading = @{ value=38.5; measuredAt=[DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$base/sensors/$($sensor.id)/readings" -ContentType 'application/json' -Body $reading
Invoke-RestMethod "$base/sensors/$($sensor.id)/readings/latest"
Invoke-RestMethod "$base/sensors/$($sensor.id)/readings?take=10"
```
