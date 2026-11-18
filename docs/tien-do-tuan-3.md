# Báo cáo tiến độ tuần 3 - SignalR thời gian thực

## Phạm vi

Vận dụng bài thực hành 3.2 vào đề tài IoT trên nền REST API tuần 2.
Hub của hệ thống là `/hubs/sensors` thay cho `/hubs/ping` của ví dụ minh họa.
Console Monitor đóng vai trò client giám sát được phép theo yêu cầu chung.
Chưa triển khai RabbitMQ, Docker hoặc Notification Service độc lập.

## Ánh xạ nội dung thực hành

| Nội dung | Hiện thực |
|---|---|
| AddSignalR, MapHub | Sensor.Api/Program.cs đăng ký SignalR và ánh xạ SensorHub |
| Echo, Clients.Caller | SensorHub.Echo phát EchoResponse riêng cho kết nối gọi |
| Broadcast, Clients.All | SensorHub.Broadcast phát BroadcastMessage tới mọi kết nối |
| JoinGroup, SendToGroup | SensorHub dùng Groups.AddToGroupAsync và Clients.Group |
| OnConnected/OnDisconnected | Phát SystemMessage và ghi log ConnectionId |
| HubConnection, On, InvokeAsync | MonitorSession và Console Monitor |
| Reconnecting, Closed | Monitor in trạng thái; hết lượt thử thì cần chạy lại |
| Reconnected, tham gia lại nhóm | MonitorSession lưu tên nhóm mong muốn và gọi lại JoinGroup |
| Ứng dụng vào đề tài | ReadingReceived và AlertRaised theo nhóm cảm biến |

SignalR Client dùng NuGet 10.0.14. Ban đầu thử đúng 10.0.0 theo tài liệu nhưng restore
báo NU1903 cho dependency SignalR.Common; đã chuyển sang bản vá cùng dòng .NET 10,
giữ TreatWarningsAsErrors và không tắt NuGet audit.

## Hợp đồng hub

Route kết nối: `http://localhost:5100/hubs/sensors`.
Không kiểm tra hub bằng GET trên thanh địa chỉ; cần SignalR client để negotiate/kết nối.

| Phương thức hub | Tham số | Sự kiện nhận |
|---|---|---|
| Echo | message | EchoResponse(string), chỉ Caller |
| Broadcast | message | BroadcastMessage(string), All |
| JoinGroup | groupName | GroupMessage(string) thông báo tham gia tới Group |
| SendToGroup | groupName, message | GroupMessage(string), Group |
| LeaveGroup | groupName | Rời nhóm, không có sự kiện riêng |

Message phải có 1-500 ký tự và không toàn khoảng trắng; group có 1-100 ký tự,
không có khoảng trắng đầu/cuối hoặc ký tự điều khiển. Sai đầu vào trả HubException.
Tên phương thức/sự kiện phải khớp cả hai phía. Tên sự kiện được gom tại RealtimeProtocol.

| Sự kiện tự động | Payload | Người nhận |
|---|---|---|
| SystemMessage | string | Các kết nối đang trực tuyến khi có connect/disconnect |
| ReadingReceived | ReadingDto | Monitor theo dõi cảm biến tương ứng hoặc tất cả |
| AlertRaised | ReadingDto | Cùng phạm vi, chỉ khi Value > AlertThreshold |

Nhóm `sensors:all` theo dõi mọi cảm biến. Nhóm `sensor:<guid>` theo dõi một cảm biến;
GUID theo dạng chuẩn D viết thường như API trả về. Monitor tự tạo đúng tên nhóm qua `/watch`.
Nhóm tùy ý như `phong-a` dùng minh họa GroupMessage, không tự nhận dữ liệu cảm biến.
Nhóm chỉ xác định phạm vi gửi; không phải quyền truy cập hay danh tính được xác thực.

## Luồng xử lý dữ liệu đo

1. Simulator gửi POST dữ liệu đo qua HTTP.
2. API kiểm tra dữ liệu và cảm biến, lưu vào InMemorySensorStore.
3. SensorSubscriptions lấy hợp các ConnectionId theo dõi tất cả hoặc cảm biến này.
4. IHubContext gửi ReadingReceived đến các kết nối đó, chỉ một bản cho mỗi kết nối.
5. Nếu vượt ngưỡng, gửi thêm AlertRaised cùng ReadingDto.
6. POST trả 201 cùng payload và Location như tuần 2.

Danh sách đăng ký nằm trong bộ nhớ và được dọn khi disconnect. Cách hợp ConnectionId
tránh phát trùng khi một monitor ở cả nhóm tất cả lẫn nhóm riêng.
SignalR gửi theo khả năng tại thời điểm đó; response 201 xác nhận API tiếp nhận/lưu trong bộ nhớ,
không phải xác nhận rằng mọi monitor đã xử lý thông báo. Chưa có hàng đợi lưu bền vững.

## Kịch bản TN1-TN4 với hai monitor

Chạy API và hai Monitor như README. Để làm phép thử nhóm rõ ràng, mở cả hai với `--no-group`.

**TN1 - Caller:** nhập `/echo Xin chao A` tại A. Chỉ A nhận EchoResponse có tiền tố Echo:.
ConnectionId dùng phân biệt kết nối, không phải tài khoản người dùng.

**TN2 - All:** nhập `/all Thong bao tu A`. A và B đều nhận BroadcastMessage, kể cả khi không tham gia nhóm.
Đây là điểm khác với Echo và với dữ liệu IoT cần theo dõi nhóm.

**TN3 - Group:** A nhập `/join phong-a`; B nhập `/join phong-b`.
A nhập `/group phong-a Chi phong A`: chỉ A nhận. B tham gia phong-a và thử lại: cả hai nhận.
B nhập `/leave phong-a` rồi thử lần nữa: B không nhận. Đổi tên nhóm/nội dung bằng lệnh để đối chiếu trực tiếp.

**Ứng dụng IoT:** A nhập `/watchall`; B giữ `--no-group`. Chạy Sensor.Client:
A nhận Reading/ALERT; B không nhận hai loại sự kiện này. Dùng GET /sensors lấy GUID,
B nhập `/watch <guid>`, chạy POST cho cảm biến đó và cảm biến khác để đối chiếu phạm vi.
Thử giá trị 35 và 36 với ngưỡng 35: cả hai có Reading, chỉ 36 có ALERT.

**TN4 - Mất kết nối:** giữ hai Monitor, dừng API rồi khởi động lại sớm.
Monitor in Reconnecting, Reconnected, SubscriptionsRestored và tiếp tục nhận sự kiện nhóm.
Dữ liệu API mất sau restart; chạy lại Simulator để có cảm biến và lần đo mới.
Nếu theo dõi riêng GUID cũ, phải theo dõi GUID của cảm biến mới để nhận dữ liệu mới.

## Reconnect và giới hạn

- Chính sách Monitor mặc định: thử lại sau 0, 2, 10, 30 giây; không bảo đảm phục hồi nếu API dừng quá lâu.
- Reconnected có ConnectionId mới, Monitor gọi lại JoinGroup cho các nhóm đã tham gia thành công và chưa rời.
- Closed là đóng hẳn sau khi hết lượt thử hoặc khi dừng client; cần chạy lại Monitor nếu muốn kết nối mới.
- WithAutomaticReconnect không xử lý lỗi lần StartAsync đầu. Monitor báo ConnectionError, exit code 1.
- Không tự phát lại dữ liệu bị bỏ lỡ. REST history có thể dùng tra cứu khi API còn dữ liệu;
  API restart thì kho bộ nhớ mất nên cũng không thể khôi phục lịch sử cũ.
- State, nhóm và dữ liệu chỉ phục vụ một tiến trình API; chưa có backplane hay triển khai nhiều instance.

## Kiểm chứng

`scripts/verify-week3.ps1` chạy hai SignalR HubConnection dùng chính MonitorSession,
thêm một Console Monitor riêng, kiểm tra Caller/All/Group, lọc dữ liệu, cảnh báo,
không nhận trùng, unsubscribe, dừng/chạy API, rejoin và Closed.
Phép thử reconnect dùng khoảng thử ngắn 0/1/2/3 giây để chạy kiểm tra nhanh;
phép thử Closed dùng 0/0,1 giây. Monitor thông thường vẫn dùng 0/2/10/30 giây.
Log và JSON thực tế sinh ở artifacts/, bản ghi kết quả chọn lưu trong docs/minh-chung/.

Đã chạy lại bộ kiểm tra REST tuần 2 để phát hiện thay đổi làm hỏng chức năng cũ.
