# Minh chứng kiểm thử tuần 2

Ngày kiểm tra: 18/11/2026. Môi trường: Windows, .NET SDK 10.0.403.

Lệnh: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-week2.ps1`.
Build Release thành công, 0 lỗi và 0 cảnh báo. **32/32 kiểm tra đạt**; các xác nhận giá trị phản hồi trong script cũng đã qua.

| Kiểm tra | Phương thức | Mong đợi | Thực tế | Kết quả |
|---|---|---|---|---|
| API status | GET | 200 | 200 | Đạt |
| Initially empty list | GET | 200 | 200 | Đạt |
| Register sensor | POST | 201 | 201 | Đạt |
| Follow Location | GET | 200 | 200 | Đạt |
| Unknown sensor | GET | 404 | 404 | Đạt |
| Route constraint | GET | 404 | 404 | Đạt |
| Invalid sensor | POST | 400 | 400 | Đạt |
| Missing threshold | POST | 400 | 400 | Đạt |
| Malformed JSON | POST | 400 | 400 | Đạt |
| Empty sensor body | POST | 400 | 400 | Đạt |
| No latest reading | GET | 404 | 404 | Đạt |
| Empty history | GET | 200 | 200 | Đạt |
| Unknown sensor reading | POST | 404 | 404 | Đạt |
| Missing value | POST | 400 | 400 | Đạt |
| Missing timestamp | POST | 400 | 400 | Đạt |
| Invalid timestamp | POST | 400 | 400 | Đạt |
| Above threshold | POST | 201 | 201 | Đạt |
| Follow reading Location | GET | 200 | 200 | Đạt |
| Equal threshold | POST | 201 | 201 | Đạt |
| Zero is valid | POST | 201 | 201 | Đạt |
| Latest by measurement time | GET | 200 | 200 | Đạt |
| Inclusive time range | GET | 200 | 200 | Đạt |
| History pagination | GET | 200 | 200 | Đạt |
| Reversed range | GET | 400 | 400 | Đạt |
| Invalid page size | GET | 400 | 400 | Đạt |
| Invalid query binding | GET | 400 | 400 | Đạt |
| 20 concurrent readings retained | GET | 200 | 200 | Đạt |
| Two demo sensors registered | GET | 200 | 200 | Đạt |
| API stopped | GET | connection failure | connection failure | Đạt |
| Client handles offline API | CLI | 1 | 1 | Đạt |
| Memory resets after restart | GET | 200 | 200 | Đạt |
| Old sensor gone after restart | GET | 404 | 404 | Đạt |

Các xác nhận bổ sung: Trim tên; Location đọc lại được; 36 > 35 có cảnh báo, 35 = 35 không cảnh báo; giá trị 0 hợp lệ; latest chọn theo thời điểm đo dù dữ liệu đến sai thứ tự; quy đổi +07:00/UTC và lấy đủ hai mốc biên; phân trang; 20 POST đồng thời không mất dữ liệu và ID không trùng; hai cảm biến giả lập gửi đủ 3 mẫu mỗi cảm biến.

Sau khi dừng API, request thất bại ở tầng kết nối và client trả exit code 1. Khởi động lại API trả danh sách rỗng, đúng với cơ chế lưu trong bộ nhớ của mốc này.

Dữ liệu status/body/Location thực tế: [week2-results.json](week2-results.json). Log từng lần chạy được tái tạo trong `artifacts/`; không commit log tạm. CI GitHub chưa chạy trước khi push repository.
