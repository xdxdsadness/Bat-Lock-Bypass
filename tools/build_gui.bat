@echo off
rem Rebuilds the GUI exe from BatLockBypass.cs. Run after editing the source.
rem Uses the C# compiler built into Windows (.NET Framework 4.x).

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

"%CSC%" /nologo /target:winexe /platform:anycpu ^
  /out:"%~dp0..\Bat Lock Bypass.exe" ^
  /win32icon:"%~dp0..\src\app.ico" ^
  /win32manifest:"%~dp0..\src\app.manifest" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  "%~dp0..\src\BatLockBypass.cs"

if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)
echo BUILD OK: %~dp0..\Bat Lock Bypass.exe
