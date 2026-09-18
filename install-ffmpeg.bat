@echo off
setlocal enabledelayedexpansion

where ffmpeg >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo ffmpeg da co san trong PATH. Khong can cai them.
    pause
    exit /b 0
)

echo Chua tim thay ffmpeg. Dang tai ve, vui long doi...

set "FFMPEG_DIR=%LOCALAPPDATA%\Capcap\ffmpeg"
set "FFMPEG_ZIP=%TEMP%\capcap-ffmpeg.zip"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ProgressPreference = 'SilentlyContinue'; try { Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile '%FFMPEG_ZIP%' } catch { exit 1 }"

if not exist "%FFMPEG_ZIP%" (
    echo.
    echo Tai ffmpeg that bai. Kiem tra ket noi mang roi thu lai,
    echo hoac tu tai tai https://ffmpeg.org/download.html va tu them vao PATH.
    pause
    exit /b 1
)

echo Dang giai nen...
if exist "%FFMPEG_DIR%" rmdir /s /q "%FFMPEG_DIR%"
mkdir "%FFMPEG_DIR%" >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Expand-Archive -Path '%FFMPEG_ZIP%' -DestinationPath '%FFMPEG_DIR%' -Force"

del "%FFMPEG_ZIP%" >nul 2>&1

set "FFMPEG_BIN="
for /d %%D in ("%FFMPEG_DIR%\ffmpeg-*") do set "FFMPEG_BIN=%%D\bin"

if not exist "%FFMPEG_BIN%\ffmpeg.exe" (
    echo.
    echo Giai nen xong nhung khong thay ffmpeg.exe. Vui long cai thu cong.
    pause
    exit /b 1
)

echo Dang them "%FFMPEG_BIN%" vao bien PATH cua nguoi dung...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$bin = [Environment]::GetEnvironmentVariable('Path','User'); $add = '%FFMPEG_BIN%'; if (($bin -split ';') -notcontains $add) { [Environment]::SetEnvironmentVariable('Path', ($bin + ';' + $add), 'User') }"

echo.
echo Da cai xong ffmpeg tai: %FFMPEG_BIN%
echo Hay DONG va MO LAI Capcap.exe (hoac dang xuat/dang nhap lai Windows) de PATH co hieu luc.
pause
