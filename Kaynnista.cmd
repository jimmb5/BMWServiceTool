@echo off
setlocal
if exist "%~dp0artifacts\app\G30ServiceTool.exe" (
    start "" "%~dp0artifacts\app\G30ServiceTool.exe"
    exit /b 0
)
if exist "%~dp0src\G30ServiceTool.App\bin\Release\net9.0-windows\G30ServiceTool.exe" (
    start "" "%~dp0src\G30ServiceTool.App\bin\Release\net9.0-windows\G30ServiceTool.exe"
    exit /b 0
)
echo Rakenna ohjelma ensin: powershell -File scripts\build.ps1
pause
exit /b 1
