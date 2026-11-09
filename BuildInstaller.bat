@echo off
REM Builds AIMonitor+ v2: publish a self-contained x64 build, then compile the Inno Setup installer.
setlocal
cd /d "%~dp0"

echo Publishing SimpleMikrotikMonitor (Release, win-x64, self-contained)...
if exist publish\win-x64 rd /s /q publish\win-x64
dotnet publish SimpleMikrotikMonitor\SimpleMikrotikMonitor.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
if errorlevel 1 goto :fail

if not exist Output mkdir Output
echo Compiling installer...
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "MikrotikMonitorSetup.iss"
if errorlevel 1 goto :fail

echo.
echo Installer built in the Output folder.
pause
exit /b 0

:fail
echo.
echo Build failed.
pause
exit /b 1
