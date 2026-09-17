# Project Oculus

Tool quay màn hình siêu nhẹ cho Windows, chạy dạng icon khay hệ thống (system tray). Dùng ffmpeg có sẵn trên máy để encode, không cần cài driver ảo hay phần mềm nặng nào khác.

## Tính năng

- **4 chế độ quay**: Toàn màn hình, Toàn màn hình (ẩn taskbar), Chọn vùng (kéo chuột), Dọc 9:16 bám theo con trỏ chuột.
- **Con trỏ chuột thật**: vẽ đúng hình dạng và kích thước con trỏ đang hiển thị thật trên máy (lấy trực tiếp từ Windows, không phải overlay giả theo tọa độ), có tùy chọn phóng to 1x/1.5x/2x/3x để dễ nhìn hơn trong video.
- **File xuất ra rất nhẹ**: encode H.264 qua ffmpeg pipe trực tiếp, không ghi file tạm dạng raw.
- **Âm thanh hiệu ứng khi click / gõ phím / cuộn chuột** (tùy chọn, bật mặc định) — không thu âm thanh hệ thống hay micro.
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

## Yêu cầu hệ thống

- Windows 10/11.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) trở lên (nếu build từ mã nguồn, cần .NET 8 SDK).
- `ffmpeg` có trong `PATH` hệ thống.

## Build từ mã nguồn

```powershell
dotnet build -c Release
```

File chạy sau khi build: `bin\Release\net8.0-windows\Oculus.exe`.

## Sử dụng

1. Chạy `Oculus.exe`, icon xuất hiện ở khay hệ thống (góc phải taskbar).
2. Chuột phải vào icon để chọn chế độ quay, FPS (30/60), cỡ con trỏ, bật/tắt âm thanh.
3. `Ctrl+Home` để bắt đầu, `Ctrl+End` để dừng — sau khi dừng sẽ hỏi Lưu hay Xóa video.
4. Video lưu tại `Videos\ProjectOculus` trong thư mục người dùng (menu có mục "Mở thư mục lưu video").

## Cấu trúc mã nguồn

| File | Vai trò |
|---|---|
| `Program.cs` | Entry point, chặn chạy nhiều instance |
| `TrayApplicationContext.cs` | Icon khay hệ thống, menu, hotkey, điều phối luồng quay |
| `Recorder.cs` | Engine chụp màn hình + pipe dữ liệu vào ffmpeg |
| `CursorPainter.cs` | Vẽ con trỏ chuột thật (đúng hình dạng, kích thước theo DPI) |
| `InputSoundLogger.cs` / `WavBuilder.cs` | Ghi lại thời điểm click/phím/cuộn và tổng hợp track âm thanh |
| `RegionSelectForm.cs` / `ConfirmStartForm.cs` / `CountdownOverlayForm.cs` / `SaveDiscardForm.cs` | Các popup UI trong luồng quay theo vùng |
| `BorderOverlayForm.cs` | Khung viền báo vùng đang quay |
| `NativeMethods.cs` / `HotkeyWindow.cs` | P/Invoke Win32 API và cửa sổ ẩn nhận hotkey toàn cục |
