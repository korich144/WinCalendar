@echo off
rem Сборка wincal.exe системным компилятором C# (.NET Framework 4.8, без SDK)
setlocal
cd /d "%~dp0"
if not exist "libs\System.Data.SQLite.dll" call get-libs.cmd
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WMD=%WINDIR%\System32\WinMetadata
set GAC=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL
if not exist bin mkdir bin
%CSC% /nologo /codepage:65001 /optimize+ /target:winexe /out:bin\wincal.exe ^
  /r:"%GAC%\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll" ^
  /r:"%GAC%\System.Threading.Tasks\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Threading.Tasks.dll" ^
  /r:"%WMD%\Windows.Foundation.winmd" ^
  /r:"%WMD%\Windows.ApplicationModel.winmd" ^
  /r:"%WMD%\Windows.UI.winmd" ^
  /r:System.Windows.Forms.dll ^
  /r:System.Drawing.dll ^
  /r:System.Web.Extensions.dll ^
  /r:libs\System.Data.SQLite.dll ^
  src\Rt.cs src\Config.cs src\Ics.cs src\StoreSync.cs src\Thunderbird.cs src\SettingsForm.cs src\Program.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
copy /y libs\System.Data.SQLite.dll bin\ >nul
if not exist bin\x64 mkdir bin\x64
if not exist bin\x86 mkdir bin\x86
copy /y libs\x64\SQLite.Interop.dll bin\x64\ >nul
copy /y libs\x86\SQLite.Interop.dll bin\x86\ >nul
echo BUILD OK: bin\wincal.exe
