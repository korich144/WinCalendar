@echo off
rem Сборка лёгкого per-user MSI (WiX 3.11, бинарники скачиваются автоматически).
setlocal
cd /d "%~dp0"
set WIX=third_party\wix

if not exist "%WIX%\candle.exe" (
  echo downloading WiX 3.11 binaries...
  if not exist third_party mkdir third_party
  curl -sL -o third_party\wix.zip "https://github.com/wixtoolset/wix3/releases/download/wix3112rtm/wix311-binaries.zip" || (echo download failed & exit /b 1)
  powershell -NoProfile -Command "Expand-Archive -Force 'C:\Users\Корыч\Projects\win-calendar\third_party\wix.zip' 'C:\Users\Корыч\Projects\win-calendar\third_party\wix'" || (echo extract failed & exit /b 1)
)

if not exist bin\wincal.exe (echo build first: build.cmd & exit /b 1)
if not exist obj mkdir obj

"%WIX%\candle.exe" -nologo -ext "%WIX%\WixUtilExtension.dll" -out obj\WinCalendar.wixobj installer\WinCalendar.wxs
if errorlevel 1 (echo candle failed & exit /b 1)

"%WIX%\light.exe" -nologo -out bin\WinCalendar.msi obj\WinCalendar.wixobj ^
  -ext "%WIX%\WixUtilExtension.dll" ^
  -spdb -sice:ICE64 -sice:ICE80
if errorlevel 1 (echo light failed & exit /b 1)

echo MSI OK: bin\WinCalendar.msi
