@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK not found. Installing .NET 8 SDK with winget...
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
    echo.
    echo If winget finished, close this window and double-click Build.bat again.
    pause
    exit /b 1
)

echo Building Module Optimiser...
dotnet build -c Release > build-log.txt 2>&1
if errorlevel 1 (
    type build-log.txt
    echo.
    echo Build failed. The errors are saved in build-log.txt.
    pause
    exit /b 1
)

echo Build succeeded. Starting Module Optimiser...
start "" "%~dp0bin\Release\net8.0-windows\ModuleOptimiser.exe"
endlocal
