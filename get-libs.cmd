@echo off
rem Загрузка SQLite-библиотек (System.Data.SQLite, public domain) с NuGet.
rem Вызывается build.cmd автоматически, если libs\ пуст.
setlocal
cd /d "%~dp0"
if exist "libs\System.Data.SQLite.dll" if exist "libs\x64\SQLite.Interop.dll" if exist "libs\x86\SQLite.Interop.dll" (
  echo libs already present
  exit /b 0
)
set VER=1.0.119
set URL=https://api.nuget.org/v3-flatcontainer/stub.system.data.sqlite.core.netframework/%VER%/stub.system.data.sqlite.core.netframework.%VER%.nupkg
set ZIP=%TEMP%\sds.%VER%.nupkg
echo downloading %URL%
if not exist libs mkdir libs
curl -sL -o "%ZIP%" "%URL%" || (echo download failed & exit /b 1)
set ZIPZIP=%TEMP%\sds.%VER%.zip
copy /y "%ZIP%" "%ZIPZIP%" >nul
if exist "%TEMP%\sds-extract" rmdir /s /q "%TEMP%\sds-extract"
powershell -NoProfile -Command "Expand-Archive -Force '%ZIPZIP%' '%TEMP%\sds-extract'" || (echo extract failed & exit /b 1)
copy /y "%TEMP%\sds-extract\lib\net46\System.Data.SQLite.dll" libs\ >nul
if not exist libs\x64 mkdir libs\x64
if not exist libs\x86 mkdir libs\x86
copy /y "%TEMP%\sds-extract\build\net46\x64\SQLite.Interop.dll" libs\x64\ >nul
copy /y "%TEMP%\sds-extract\build\net46\x86\SQLite.Interop.dll" libs\x86\ >nul
if exist "libs\System.Data.SQLite.dll" (echo libs OK) else (echo extraction failed & exit /b 1)
