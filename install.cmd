@echo off
rem Установка WinCalendar: сборка + копирование в %LOCALAPPDATA%\WinCalendar +
rem ярлык в меню Пуск + автозапуск. Запускать из распакованного репозитория.
setlocal
cd /d "%~dp0"

echo === WinCalendar installer ===

rem -- сборка, если ещё не собрано --
if not exist "bin\wincal.exe" (
  call build.cmd
  if errorlevel 1 (echo build failed & pause & exit /b 1)
)
if not exist "bin\wincal.exe" (echo bin\wincal.exe not found & pause & exit /b 1)

rem -- остановить запущенный экземпляр --
taskkill /im wincal.exe /f >nul 2>&1

rem -- копирование --
set DEST=%LOCALAPPDATA%\WinCalendar
if not exist "%DEST%" mkdir "%DEST%"
copy /y "bin\wincal.exe" "%DEST%\" >nul
copy /y "bin\System.Data.SQLite.dll" "%DEST%\" >nul
if not exist "%DEST%\x64" mkdir "%DEST%\x64"
if not exist "%DEST%\x86" mkdir "%DEST%\x86"
copy /y "bin\x64\SQLite.Interop.dll" "%DEST%\x64\" >nul
copy /y "bin\x86\SQLite.Interop.dll" "%DEST%\x86\" >nul

rem -- ярлык в меню Пуск --
set SM=%APPDATA%\Microsoft\Windows\Start Menu\Programs
powershell -NoProfile -Command ^
  "$s = (New-Object -ComObject WScript.Shell).CreateShortcut('%SM%\WinCalendar.lnk'); $s.TargetPath = '%DEST%\wincal.exe'; $s.WorkingDirectory = '%DEST%'; $s.Description = 'WinCalendar - события в часах Windows 10'; $s.Save()"
echo Start Menu shortcut created

rem -- автозапуск --
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v WinCalendarAgenda /t REG_SZ /d "\"%DEST%\wincal.exe\"" /f >nul
echo autostart enabled

rem -- запустить --
start "" "%DEST%\wincal.exe"
echo.
echo Installed to %DEST%
echo Config: %%APPDATA%%\WinCalendar\config.json
echo Uninstall: uninstall.cmd in this folder
pause
