# Capcap

Tool quay màn hình siêu nhẹ cho Windows, chạy dạng icon khay hệ thống (system tray). Dùng ffmpeg có sẵn trên máy để encode. Chạy trên .NET Framework 4.8 — đã có sẵn trên mọi máy Windows 10/11, không cần cài thêm runtime.

## Tính năng

- **4 chế độ quay**: Toàn màn hình, Toàn màn hình (ẩn taskbar), Chọn vùng (kéo chuột), Dọc 9:16 bám theo con trỏ chuột.
- **Chụp màn hình tăng tốc GPU** (DXGI) — mượt ở 60fps kể cả full màn hình độ phân giải cao.
- **Con trỏ chuột thật, nét** — đúng hình dạng, phóng to cố định ~2x cho dễ nhìn trong video (vẽ từ ảnh gốc cỡ lớn của bộ con trỏ, không kéo giãn nên không bị vỡ).
- **File xuất ra rất nhẹ** — encode H.264 qua ffmpeg pipe trực tiếp.
- **Âm thanh hiệu ứng click / gõ phím / cuộn chuột** bằng tiếng thu thật (bật mặc định) và **âm thanh hệ thống** (tùy chọn).
- **Đếm ngược 3-2-1** trước khi quay ở mọi chế độ (`Ctrl+End` trong lúc đếm để hủy).
- **Ghi toạ độ chuột** (tuỳ chọn trong menu khay, bật mặc định): xuất kèm `capcap_<giờ>.mouse.json` để phần mềm dựng video làm hiệu ứng bám con trỏ (glow, zoom...). Xem mục *File toạ độ chuột* bên dưới.
- **Nhớ vùng chọn cuối** (ví dụ 608x1080 dọc 9:16) — lần sau chỉ cần click để dùng lại, Enter để dùng đúng vị trí cũ, giữ Shift khi kéo để giữ tỉ lệ.

## Phím tắt

| Phím | Chức năng |
|---|---|
| `Ctrl+Home` | Bắt đầu quay |
| `Ctrl+End` | Dừng quay (hoặc hủy khi đang đếm ngược) |
| `Ctrl+P` | Tạm dừng / tiếp tục quay |
| Bấm đúp vào icon khay hệ thống | Bắt đầu / dừng quay (dự phòng nếu phím tắt bị app khác chiếm) |

> Các phím tắt trên chiếm dụng toàn hệ thống khi app đang chạy (ví dụ Ctrl+Home/Ctrl+End vốn là "về đầu/cuối văn bản" ở nhiều app khác). Nếu bị xung đột với công cụ khác trên máy (AutoHotkey, bộ gõ tiếng Việt...), dùng cách bấm đúp icon khay hệ thống thay thế.

## Tải về

Không muốn build từ mã nguồn? Tải bản build sẵn ở [Releases](https://github.com/hiimmaxx7/capcap/releases) — giải nén và chạy `Capcap.exe`.

## Yêu cầu hệ thống

- Windows 10/11 (đã có sẵn .NET Framework 4.8 — không cần cài thêm gì để chạy).
- `ffmpeg` có trong `PATH` hệ thống — nếu chưa có, chạy `install-ffmpeg.bat` đi kèm để tự tải và thêm vào PATH cho bạn.
- Để build từ mã nguồn: .NET SDK (bản mới, dùng để build target `net48`).

## Build từ mã nguồn

```powershell
dotnet build -c Release
```

File chạy sau khi build: `bin\Release\net48\Capcap.exe` (kèm vài DLL của NAudio cho tính năng ghi âm thanh hệ thống — copy cả thư mục khi phân phối, không chỉ mỗi file .exe).

## Hướng dẫn sử dụng

1. Nếu chưa có ffmpeg, chạy `install-ffmpeg.bat` một lần (tự tải và thêm vào PATH), sau đó khởi động lại `Capcap.exe`.
2. Chạy `Capcap.exe`, icon xuất hiện ở khay hệ thống (góc phải taskbar).
3. Chuột phải vào icon để chọn chế độ quay, FPS (30/60), bật/tắt âm thanh hiệu ứng, âm thanh hệ thống và ghi toạ độ chuột.
4. `Ctrl+Home` để bắt đầu, `Ctrl+End` để dừng — sau khi dừng sẽ hỏi Lưu hay Xóa video.
5. Video lưu tại `Videos\capcap` trong thư mục người dùng, tên file dạng `capcap_yyyyMMdd_HHmmss.mp4` (menu có mục "Mở thư mục lưu video").

## File toạ độ chuột

Khi bật "Ghi toạ độ chuột", mỗi video `capcap_<giờ>.mp4` có thêm `capcap_<giờ>.mouse.json` cùng thư mục (~70KB/phút ở 60fps). Bấm Xóa ở popup sau khi dừng thì xóa cả hai file.

```json
{"fps":60,"w":608,"h":1080,"s":[[0,312,540,0],[0.017,314,541,0],[0.033,320,545,1]]}
```

- `w`, `h`: kích thước video.
- `s`: mỗi phần tử `[t, x, y, down]` — `t` là giây trong video (đã tính cả lúc tạm dừng), `x`/`y` là vị trí con trỏ theo pixel của chính video (đã trừ gốc khung cắt, nên đúng cho cả chế độ Chọn vùng lẫn Dọc 9:16 bám con trỏ), `down` = 1 khi đang giữ nút chuột trái/phải.
- Mỗi khung hình thật ghi một dòng; khung lặp lại để bù khi máy chụp không kịp fps thì không ghi thêm.

## Cấu trúc mã nguồn

| File | Vai trò |
|---|---|
| `Program.cs` | Entry point, chặn chạy nhiều instance |
| `TrayApplicationContext.cs` | Icon khay hệ thống, menu, hotkey, điều phối luồng quay |
| `Recorder.cs` | Engine chụp màn hình + pipe dữ liệu vào ffmpeg, ghi file toạ độ chuột |
| `CursorPainter.cs` | Vẽ con trỏ chuột thật (đúng hình dạng, kích thước theo DPI) |
| `InputSoundLogger.cs` / `InputSoundBank.cs` / `WavBuilder.cs` | Ghi lại thời điểm click/phím/cuộn, nạp các file `Sounds\*.wav` (nhúng trong exe) và trộn thành track hiệu ứng |
| `AppSettings.cs` | Lưu vùng chọn cuối và tuỳ chọn ghi toạ độ chuột vào `%AppData%\Capcap\settings.ini` |
| `SystemAudioCapture.cs` | Ghi âm thanh hệ thống qua WASAPI loopback (NAudio) |
| `DxgiScreenCapture.cs` | Chụp màn hình tăng tốc GPU qua DXGI Desktop Duplication |
| `RegionSelectForm.cs` / `ConfirmStartForm.cs` / `CountdownOverlayForm.cs` / `SaveDiscardForm.cs` | Các popup UI trong luồng quay theo vùng |
| `BorderOverlayForm.cs` | Khung viền báo vùng đang quay |
| `NativeMethods.cs` / `HotkeyWindow.cs` | P/Invoke Win32 API và cửa sổ ẩn nhận hotkey toàn cục |
