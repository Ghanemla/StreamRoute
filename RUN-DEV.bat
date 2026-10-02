@echo off
setlocal
cd /d "%~dp0"

set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" set "DOTNET_EXE=dotnet"

"%DOTNET_EXE%" --list-sdks 2>nul | findstr /B /C:"10." >nul
if errorlevel 1 (
  echo .NET 10 SDK is required. Run BUILD.bat once and let it install the build tools.
  pause
  exit /b 1
)

"%DOTNET_EXE%" run --project "%CD%\src\StreamRoute\StreamRoute.csproj" -c Debug
if errorlevel 1 pause
