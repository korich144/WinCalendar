@echo off
rem Build smoke test with the system C# compiler (.NET Framework 4.8)
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WMD=%WINDIR%\System32\WinMetadata
set GAC=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL
if not exist bin mkdir bin
%CSC% /nologo /target:exe /out:bin\smoke.exe ^
  /r:"%GAC%\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll" ^
  /r:"%GAC%\System.Threading.Tasks\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Threading.Tasks.dll" ^
  /r:"%WMD%\Windows.Foundation.winmd" ^
  /r:"%WMD%\Windows.ApplicationModel.winmd" ^
  /r:"%WMD%\Windows.UI.winmd" ^
  /r:libs\System.Data.SQLite.dll ^
  src\smoke\Program.cs src\Ics.cs src\Config.cs src\Rt.cs src\StoreSync.cs src\Thunderbird.cs src\Thunderbird.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK
bin\smoke.exe
