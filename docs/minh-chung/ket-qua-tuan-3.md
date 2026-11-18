# Minh chứng kiểm thử tuần 3

Ngày kiểm tra: 18/11/2026. Windows, .NET SDK 10.0.403, SignalR Client 10.0.14.

Lệnh: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-week3.ps1`.
Build Release: 0 lỗi, 0 cảnh báo. Kết quả: **24/24 kiểm tra SignalR đạt**.
Đã chạy lại `scripts/verify-week2.ps1`: **32/32 kiểm tra REST đạt**, cùng các xác nhận giá trị và dữ liệu đồng thời của script.

| Kiểm tra | Kết quả thực tế |
|---|---|
| TN1: two independent connections | Đạt. Two HubConnections have different ConnectionIds; SystemMessage observed. |
| TN1: Echo reaches Caller only | Đạt. B received EchoResponse; A did not. |
| TN2: Broadcast reaches All | Đạt. A and B received BroadcastMessage. |
| TN3: other group excluded | Đạt. A in room-a received; B in room-b did not. |
| TN3: same group receives | Đạt. A and B joined room-a and received GroupMessage. |
| TN3: LeaveGroup takes effect | Đạt. B no longer receives room-a messages. |
| Invalid group rejected | Đạt. Blank group returns HubException. |
| Invalid message rejected | Đạt. Blank broadcast returns HubException. |
| Normal readings realtime; equal threshold no alert | Đạt. Value 35 == threshold 35: ReadingReceived on A/B, no AlertRaised. |
| Above threshold alerts realtime | Đạt. Value 36 > 35: ReadingReceived and AlertRaised on both subscribers. |
| Sensor subscription filters reading and alert | Đạt. A follows all sensors; B follows sensor1 and receives neither event for sensor2. |
| Overlapping subscriptions deliver once | Đạt. A in all-sensors and sensor1 groups receives each event once. |
| Unsubscribe stops realtime data | Đạt. B leaves its sensor group; A continues receiving. |
| Invalid HTTP data does not broadcast | Đạt. Missing timestamp returns 400 and emits no ReadingReceived. |
| REST and realtime reference the same reading | Đạt. Latest REST reading ID matches the event payload. |
| Console Monitor connects and exits cleanly | Đạt. Real Monitor process --duration 2 completed with exit code 0. |
| SystemMessage on disconnect | Đạt. Existing monitor saw another monitor disconnect. |
| TN4: Reconnecting observed | Đạt. A and B detect the stopped API. |
| TN4: automatic reconnect | Đạt. Monitor reconnected with a new ConnectionId. |
| TN4: groups restored after reconnect | Đạt. A and B receive room-a message after rejoining automatically. |
| Restart remains in-memory | Đạt. Week-2 storage limitation remains explicit. |
| Live IoT alerts resume after reconnect | Đạt. A restored all-sensors subscription and received new reading/alert. |
| TN4: Closed after retry exhaustion | Đạt. A short retry policy exhausts while the API stays offline; Closed observed. |
| Initial StartAsync failure is explicit | Đạt. AutomaticReconnect does not hide an initial offline API. |

Các test dùng server thật và hai HubConnection thuộc MonitorSession; có thêm một Console Monitor chạy thành tiến trình riêng. Cổng kiểm tra do hệ điều hành cấp. API bị dừng/khởi chạy lại trong test và chỉ các tiến trình test sở hữu bị dừng.

Phép thử reconnect/rejoin dùng lịch 0/1/2/3 giây. Phép thử Closed dùng 0/0,1 giây. Monitor thông thường dùng 0/2/10/30 giây. Sự kiện bị lỡ không được phát lại và dữ liệu trong bộ nhớ mất khi API restart.

Chi tiết: [week3-results.json](week3-results.json). Log mỗi lần chạy tự sinh trong `artifacts/week3-*.log`; không commit log tạm. Kết quả trên là kiểm thử tại máy; CI từ xa được kiểm tra riêng sau push.
