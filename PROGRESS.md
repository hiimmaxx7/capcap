# PROGRESS.md — Capcap

Nhật ký phát triển. Đọc file này trước khi đổi hành vi quay/UI để biết quyết định nào đã chốt và vì sao.

## Kiến trúc hiện tại

- **.NET Framework 4.8** (đổi từ .NET 8 — xem lý do bên dưới), WinForms, chạy dưới dạng tray icon (`TrayApplicationContext`), không cửa sổ chính.
- Quay màn hình: `Recorder` dùng GDI `CopyFromScreen` để chụp từng khung hình, vẽ con trỏ thật lên (`CursorPainter`), rồi pipe raw frame (`bgr24`) vào `ffmpeg` qua stdin để encode H.264 (`libx264 -preset ultrafast -tune zerolatency`).
- Kiến trúc producer/consumer: luồng chụp (`CaptureLoop`) và luồng ghi vào ffmpeg (`WriterLoop`) tách biệt qua `BlockingCollection<byte[]>` + buffer pool — encode chậm không được phép làm treo nhịp chụp.
- Âm thanh hiệu ứng (click/phím/cuộn) không phải thu mic — chỉ log timestamp qua low-level hook (`InputSoundLogger`) rồi tổng hợp sóng sin nhân tạo (`WavBuilder`).
- Âm thanh hệ thống (tùy chọn, tắt mặc định): `SystemAudioCapture` dùng NAudio `WasapiLoopbackCapture`, ghi ra WAV riêng.
- Mux cuối cùng: 0, 1 hoặc 2 track âm thanh (hiệu ứng + hệ thống) được gộp vào video bằng một lệnh ffmpeg pass thứ 2 — nếu có 2 track thì dùng `-filter_complex amix` để trộn, không chỉ giữ lại track đầu.

## Các quyết định đã chốt (đừng đổi lại nếu không có lý do mới)

- **Hotkey: `Ctrl+Home` (bắt đầu) / `Ctrl+End` (dừng) / `Ctrl+P` (tạm dừng)**, đăng ký qua `RegisterHotKey`. Đã thử double-tap Home/End và `Ctrl+Alt+R` trước đó — bỏ vì `Ctrl+Alt+R` bị xung đột với AutoHotkey/EVKey trên máy người dùng (không tự phát hiện được lỗi này, hotkey chỉ âm thầm không bắn). Bấm đúp icon khay hệ thống luôn là phương án dự phòng không phụ thuộc hotkey.
- **Chặn chạy nhiều instance** (`Mutex` trong `Program.cs`) — lý do: 2 tiến trình cùng chạy khiến chỉ 1 cái đăng ký được hotkey, gây cảm giác "đổi cài đặt không có tác dụng" rất khó debug.
- **FPS mặc định 60**, chỉ còn 2 lựa chọn 30/60 (đã bỏ 10/15/24 theo yêu cầu).
- **Âm thanh click/phím/cuộn: bật mặc định**, biên độ tick đã tăng ~3.5x so với bản đầu vì người dùng phản hồi quá nhỏ.
- **Cỡ con trỏ: mặc định 2x** thật (không phải kích thước gốc OS). Lý do: có cố gắng tự tính đúng kích thước theo DPI màn hình (`GetSystemMetricsForDpi`) nhưng không kiểm chứng được trên máy người dùng là có tác dụng hay không, nên thêm hẳn tùy chọn nhân hệ số thủ công (1x/1.5x/2x/3x) làm phương án chắc chắn có hiệu quả.
- **Dead-zone cho chế độ Dọc 9:16 bám con trỏ**: chỉ pan khi con trỏ vào trong khoảng 1/6 bề rộng khung tính từ mép trái/phải; ở giữa đứng yên hoàn toàn. Khi pan thì dùng easing kết hợp: hệ số tỉ lệ (êm khi gần đích) + **sàn tốc độ tối thiểu** (đóng hết 1 chiều rộng khung trong ≤0.3s) để không bị "ì" khi con trỏ nhảy xa — đã tự chỉnh 2 lần vì lần đầu (easing thuần, tau=0.4s) quá chậm bắt kịp khi con trỏ trượt hẳn ra ngoài khung.
- **Viền báo vùng đang quay**: dùng `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` để loại cửa sổ viền khỏi mọi kiểu chụp màn hình — KHÔNG dùng mẹo vẽ lệch ra ngoài vài px (đã thử, vẫn lọt vào video khi viền phải di chuyển nhanh theo chế độ bám chuột).
- **Đồng bộ âm thanh với video**: hai nguồn lệch đã tìm ra và sửa:
  1. Độ trễ khởi động (ffmpeg + thread spin-up) trước khung hình đầu tiên → bù bằng `_firstFrameOffsetSec`, dịch toàn bộ mốc thời gian sự kiện âm thanh theo offset này.
  2. Khi máy chụp không kịp fps mục tiêu, code cũ **âm thầm bỏ khung hình** → video bị nén ngắn hơn thời lượng quay thật (giống tua nhanh), làm âm thanh lệch dần theo thời gian. Đã sửa bằng cách **lặp lại khung hình gần nhất** để bù đủ số khung cần có, đảm bảo thời lượng video luôn khớp thời gian thực (đã đo: quay 4.72s thực tế ra video 4.23s, so với trước khi sửa 3.2s thực tế chỉ ra 0.9s video).
- **Popup Lưu/Xóa sau khi dừng quay** — luôn hỏi trước khi giữ file lại trong `Videos\capcap`.
- **Tên dự án: Capcap** (đổi từ Project Oculus) — chơi chữ "cap" = capture, lặp "cap cap" nghe như tiếng vịt kêu "cạp cạp", khớp với icon Psyduck đang dùng.
- **Đếm ngược 3-2-1** chỉ áp dụng cho luồng "Chọn vùng" (kéo chọn → popup xác nhận → đếm ngược → mới quay thật), không áp dụng cho các chế độ khác khi bấm Ctrl+Home.
- **Đổi target framework sang net48** — .NET Framework 4.8 có sẵn trên mọi máy Windows 10/11, người dùng không cần cài .NET Desktop Runtime riêng như .NET 8 trước đây. Đánh đổi: phải tự thay `Application.SetHighDpiMode` (không tồn tại ở .NET Framework WinForms) bằng khai báo `dpiAwareness` trong `app.manifest`; `Math.Clamp` không có ở net48 (dùng Min/Max thủ công); literal `"..."u8` cần `Span<byte>` nên đổi sang `Encoding.ASCII.GetBytes`; so sánh `IntPtr == int` (wParam trong hook) cần `.ToInt32()` tường minh vì .NET Framework không có operator tiện lợi như .NET 8.
- **Tên file video: `capcap_yyyyMMdd_HHmmss.mp4`** (đổi từ `ghi_...`).
- **Ghi âm thanh hệ thống dùng NAudio** (`WasapiLoopbackCapture`), không tự viết WASAPI qua P/Invoke — đánh đổi chấp nhận được: build không còn ra đúng 1 file .exe nữa mà kèm ~10 DLL nhỏ của NAudio (~1MB tổng), nhưng tránh rủi ro tự implement COM interop WASAPI (rất dễ sai định dạng/timing). Chưa test được nội dung âm thanh thực tế trong sandbox này vì **máy test không có thiết bị phát âm thanh nào** (`GetDefaultAudioEndpoint` báo lỗi "Element not found") — đã thêm thông báo lỗi tiếng Việt rõ ràng cho đúng trường hợp này, nhưng cần người dùng tự xác nhận tính năng hoạt động đúng trên máy thật của họ.

## Đã cân nhắc nhưng KHÔNG làm

- **DXGI Desktop Duplication / Windows.Graphics.Capture** thay cho GDI `CopyFromScreen`: nhanh/nhẹ hơn nhiều nhưng tăng độ phức tạp đáng kể cho một tool "siêu nhẹ". Chưa cần thiết trừ khi người dùng báo capture vẫn không theo kịp fps trên máy thật của họ (môi trường test hiện tại là sandbox ảo hóa, capture chậm bất thường — không chắc phản ánh đúng máy thật).
- **Giới hạn thời lượng quay tối đa (auto-stop sau N giây)**: từng hiểu nhầm yêu cầu "mặc định 30s" theo hướng này — thực ra ý người dùng là "mặc định 30fps" (đã hỏi lại và xác nhận). Không có tính năng giới hạn thời lượng.

## Việc còn để ngỏ / có thể cần theo dõi tiếp

- Chưa test được trên máy thật của người dùng xem GDI capture có theo kịp 60fps ở độ phân giải cao hay không — cơ chế "lặp khung hình bù" đã bảo vệ tính đúng thời lượng, nhưng nếu capture quá chậm thì video sẽ có nhiều đoạn khung hình lặp (nhìn hơi khựng) thay vì mượt thật sự.
- Ctrl+Home/Ctrl+End/Ctrl+P chiếm dụng phím tắt toàn hệ thống (trùng chức năng mặc định ở nhiều app khác: về đầu/cuối văn bản, In). Người dùng đã được nhắc, chưa yêu cầu đổi.
- **Ghi âm thanh hệ thống chưa test được nội dung thực tế** (sandbox không có thiết bị âm thanh) — cần người dùng thử trên máy thật, bật tùy chọn "Ghi âm thanh hệ thống", phát nhạc/video và kiểm tra file ra có tiếng không, đặc biệt khi bật đồng thời với âm thanh hiệu ứng (kiểm tra amix trộn đúng, không bên nào bị át).
