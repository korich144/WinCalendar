@echo off
rem Удаление WinCalendar (конфиг и состояние в %APPDATA%\WinCalendar не трогаются;
rem чтобы стереть и их, удалите папку %APPDATA%\WinCalendar вручную).
setlocal
echo === WinCalendar uninstaller ===
taskkill /im wincal.exe /f >nul 2>&1

set DEST=%LOCALAPPDATA%\WinCalendar
if exist "%DEST%" rmdir /s /q "%DEST%"
echo program folder removed

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v WinCalendarAgenda /f >nul 2>&1
echo autostart removed

del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\WinCalendar.lnk" >nul 2>&1
echo start menu shortcut removed

echo.
echo Done. Config data left in %%APPDATA%%\WinCalendar (delete manually if needed).
pause
