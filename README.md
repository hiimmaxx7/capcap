# Capcap

Tool quay màn hình siêu nhẹ cho Windows, chạy dạng icon khay hệ thống (system tray). Dùng ffmpeg có sẵn trên máy để encode. Chạy trên .NET Framework 4.8 — đã có sẵn trên mọi máy Windows 10/11, không cần cài thêm runtime.

Tên "Capcap": **cap** = capture (quay/chụp màn hình), lặp lại **cap cap** nghe như tiếng vịt kêu "cạp cạp" — khớp với icon Psyduck của app.

## Tính năng

- **4 chế độ quay**: Toàn màn hình, Toàn màn hình (ẩn taskbar), Chọn vùng (kéo chuột), Dọc 9:16 bám theo con trỏ chuột.
- **Chụp màn hình tăng tốc GPU** (DXGI Desktop Duplication) — mượt ở 60fps kể cả full màn hình độ phân giải cao; tự động lùi về chụp thường (GDI) nếu máy không hỗ trợ.
- **Con trỏ chuột thật**: vẽ đúng hình dạng và kích thước con trỏ đang hiển thị thật trên máy (lấy trực tiếp từ Windows, không phải overlay giả theo tọa độ), có tùy chọn phóng to 1x/1.5x/2x/3x để dễ nhìn hơn trong video.
- **File xuất ra rất nhẹ**: encode H.264 qua ffmpeg pipe trực tiếp, không ghi file tạm dạng raw.
- **Âm thanh hiệu ứng khi click / gõ phím / cuộn chuột** (tùy chọn, bật mặc định) — tổng hợp tick nhân tạo, không thu mic.
- **Ghi âm thanh hệ thống** (tùy chọn, tắt mặc định) — thu những gì đang phát qua loa máy (WASAPI loopback), trộn cùng track hiệu ứng nếu cả hai cùng bật.
- **Khung viền báo vùng đang quay** (trừ chế độ Toàn màn hình) — không lọt vào video nhờ `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.
- **Đếm ngược 3-2-1** trước khi quay theo vùng đã chọn, có popup xác nhận trước.
- **Tạm dừng / tiếp tục quay** không mất đồng bộ âm thanh.
- **Hỏi Lưu / Xóa** sau khi dừng quay.
- Chỉ chạy **một instance** cùng lúc (tự phát hiện nếu app đã chạy rồi).

## Phím tắt

| Phím | Chức năng |
|---|---|
| `Ctrl+Home` | Bắt đầu quay |
| `Ctrl+End` | Dừng quay |
| `Ctrl+P` | Tạm dừng / tiếp tục quay |
| Bấm đúp vào icon khay hệ thống | Bắt đầu / dừng quay (dự phòng nếu phím tắt bị app khác chiếm) |

> Các phím tắt trên chiếm dụng toàn hệ thống khi app đang chạy (ví dụ Ctrl+Home/Ctrl+End vốn là "về đầu/cuối văn bản" ở nhiều app khác). Nếu bị xung đột với công cụ khác trên máy (AutoHotkey, bộ gõ tiếng Việt...), dùng cách bấm đúp icon khay hệ thống thay thế.

## Tải về

Không muốn build từ mã nguồn? Tải bản build sẵn ở [Releases](https://github.com/hiimmaxx7/capcap/releases) — giải nén và chạy `Capcap.exe`.

## Yêu cầu hệ thống

- Windows 10/11 (đã có sẵn .NET Framework 4.8 — không cần cài thêm gì để chạy).
- `ffmpeg` có trong `PATH` hệ thống.
- Để build từ mã nguồn: .NET SDK (bản mới, dùng để build target `net48`).

## Build từ mã nguồn

```powershell
dotnet build -c Release
```

File chạy sau khi build: `bin\Release\net48\Capcap.exe` (kèm vài DLL của NAudio cho tính năng ghi âm thanh hệ thống — copy cả thư mục khi phân phối, không chỉ mỗi file .exe).

## Sử dụng

1. Chạy `Capcap.exe`, icon xuất hiện ở khay hệ thống (góc phải taskbar).
2. Chuột phải vào icon để chọn chế độ quay, FPS (30/60), cỡ con trỏ, bật/tắt âm thanh hiệu ứng và âm thanh hệ thống.
3. `Ctrl+Home` để bắt đầu, `Ctrl+End` để dừng — sau khi dừng sẽ hỏi Lưu hay Xóa video.
4. Video lưu tại `Videos\capcap` trong thư mục người dùng, tên file dạng `capcap_yyyyMMdd_HHmmss.mp4` (menu có mục "Mở thư mục lưu video").

## Cấu trúc mã nguồn

| File | Vai trò |
|---|---|
| `Program.cs` | Entry point, chặn chạy nhiều instance |
| `TrayApplicationContext.cs` | Icon khay hệ thống, menu, hotkey, điều phối luồng quay |
| `Recorder.cs` | Engine chụp màn hình + pipe dữ liệu vào ffmpeg |
| `CursorPainter.cs` | Vẽ con trỏ chuột thật (đúng hình dạng, kích thước theo DPI) |
| `InputSoundLogger.cs` / `WavBuilder.cs` | Ghi lại thời điểm click/phím/cuộn và tổng hợp track hiệu ứng âm thanh |
| `SystemAudioCapture.cs` | Ghi âm thanh hệ thống qua WASAPI loopback (NAudio) |
| `DxgiScreenCapture.cs` | Chụp màn hình tăng tốc GPU qua DXGI Desktop Duplication |
| `RegionSelectForm.cs` / `ConfirmStartForm.cs` / `CountdownOverlayForm.cs` / `SaveDiscardForm.cs` | Các popup UI trong luồng quay theo vùng |
| `BorderOverlayForm.cs` | Khung viền báo vùng đang quay |
| `NativeMethods.cs` / `HotkeyWindow.cs` | P/Invoke Win32 API và cửa sổ ẩn nhận hotkey toàn cục |
