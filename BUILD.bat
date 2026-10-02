@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title StreamRoute Builder

set "DOTNET_EXE="
set "ISCC_EXE="
set "TOOLS_DIR=%CD%\.build-tools"
set "LOCAL_BUILD_TOOLS=%LOCALAPPDATA%\StreamRouteBuildTools"

if not exist "%TOOLS_DIR%" mkdir "%TOOLS_DIR%" >nul 2>nul
if not exist "%LOCAL_BUILD_TOOLS%" mkdir "%LOCAL_BUILD_TOOLS%" >nul 2>nul

echo.
echo ==========================================
echo          StreamRoute Build Tool
echo ==========================================
echo.

call :FindDotNet
if errorlevel 1 exit /b 1

call :FindInno
if errorlevel 1 exit /b 1

echo [OK] .NET 10 SDK found
"%DOTNET_EXE%" --version

echo [OK] Inno Setup found
echo       %ISCC_EXE%

echo.
echo Cleaning old build output...
if exist "%CD%\build" rmdir /s /q "%CD%\build"
if exist "%CD%\dist" rmdir /s /q "%CD%\dist"
mkdir "%CD%\build\publish" >nul 2>nul
mkdir "%CD%\dist" >nul 2>nul

echo.
echo Restoring packages...
"%DOTNET_EXE%" restore "%CD%\src\StreamRoute\StreamRoute.csproj"
if errorlevel 1 goto :BuildFailed

echo.
echo Publishing StreamRoute for Windows x64...
"%DOTNET_EXE%" publish "%CD%\src\StreamRoute\StreamRoute.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%CD%\build\publish"
if errorlevel 1 goto :BuildFailed

echo.
echo Building one-file installer...
"%ISCC_EXE%" "%CD%\installer\StreamRoute.iss"
if errorlevel 1 goto :BuildFailed

echo.
echo ==========================================
echo                 DONE
echo ==========================================
echo.
echo Your installer is:
echo   %CD%\dist\StreamRoute-Setup.exe
echo.
if exist "%CD%\dist\StreamRoute-Setup.exe" explorer /select,"%CD%\dist\StreamRoute-Setup.exe"
pause
exit /b 0

:FindDotNet
call :LocateDotNet
if defined DOTNET_EXE (
  "%DOTNET_EXE%" --list-sdks 2>nul | findstr /B /C:"10." >nul
  if not errorlevel 1 exit /b 0
)

echo [MISSING] .NET 10 SDK is required to build StreamRoute.
echo.
choice /C YN /N /M "Install .NET 10 SDK automatically? [Y/N]: "
if errorlevel 2 (
  echo Build cancelled. Install the .NET 10 SDK and run BUILD.bat again.
  pause
  exit /b 1
)

where winget >nul 2>nul
if not errorlevel 1 (
  echo Installing .NET 10 SDK with winget...
  winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements
) else (
  echo winget is not available. Using Microsoft's official dotnet-install script instead...
  set "DOTNET_LOCAL=%LOCAL_BUILD_TOOLS%\dotnet"
  if not exist "!DOTNET_LOCAL!" mkdir "!DOTNET_LOCAL!" >nul 2>nul
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop'; $p='%TOOLS_DIR%\dotnet-install.ps1'; Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $p; & $p -Channel 10.0 -InstallDir '!DOTNET_LOCAL!' -NoPath"
  if errorlevel 1 (
    echo Automatic .NET installation failed.
    echo Install the .NET 10 SDK manually, then run BUILD.bat again.
    pause
    exit /b 1
  )
  set "DOTNET_EXE=!DOTNET_LOCAL!\dotnet.exe"
)

call :LocateDotNet
if not defined DOTNET_EXE (
  if exist "%LOCAL_BUILD_TOOLS%\dotnet\dotnet.exe" set "DOTNET_EXE=%LOCAL_BUILD_TOOLS%\dotnet\dotnet.exe"
)
if not defined DOTNET_EXE (
  echo .NET 10 SDK still was not found. Restart Windows or install it manually, then run BUILD.bat again.
  pause
  exit /b 1
)
"%DOTNET_EXE%" --list-sdks 2>nul | findstr /B /C:"10." >nul
if errorlevel 1 (
  echo .NET 10 SDK still was not found. Restart Windows or install it manually, then run BUILD.bat again.
  pause
  exit /b 1
)
exit /b 0

:LocateDotNet
set "DOTNET_EXE="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET_EXE if exist "%LOCAL_BUILD_TOOLS%\dotnet\dotnet.exe" set "DOTNET_EXE=%LOCAL_BUILD_TOOLS%\dotnet\dotnet.exe"
if not defined DOTNET_EXE for /f "delims=" %%i in ('where dotnet 2^>nul') do if not defined DOTNET_EXE set "DOTNET_EXE=%%i"
exit /b 0

:FindInno
call :LocateInno
if defined ISCC_EXE exit /b 0

echo [MISSING] Inno Setup is required to make StreamRoute-Setup.exe.
echo.
choice /C YN /N /M "Install the build tool automatically? [Y/N]: "
if errorlevel 2 (
  echo Build cancelled. Install Inno Setup and run BUILD.bat again.
  pause
  exit /b 1
)

where winget >nul 2>nul
if not errorlevel 1 (
  echo Installing Inno Setup with winget...
  winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
) else (
  echo winget is not available. Downloading the official Inno Setup installer from GitHub...
  set "INNO_INSTALLER=%TOOLS_DIR%\innosetup-6.7.3.exe"
  set "INNO_LOCAL=%LOCAL_BUILD_TOOLS%\Inno Setup 6"

  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop'; $u='https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'; $p='%TOOLS_DIR%\innosetup-6.7.3.exe'; Invoke-WebRequest -UseBasicParsing -Uri $u -OutFile $p; $fi=Get-Item $p; if($fi.Length -lt 1000000){throw ('Download was too small: ' + $fi.Length + ' bytes')}; $fs=[IO.File]::OpenRead($p); try{$b1=$fs.ReadByte();$b2=$fs.ReadByte()}finally{$fs.Dispose()}; if($b1 -ne 77 -or $b2 -ne 90){throw 'Downloaded file is not a Windows EXE'}"
  if errorlevel 1 (
    echo.
    echo Failed to download a valid Inno Setup installer.
    echo Please check your internet connection and run BUILD.bat again.
    pause
    exit /b 1
  )

  echo Installing the local build tool...
  if exist "!INNO_LOCAL!" rmdir /s /q "!INNO_LOCAL!" >nul 2>nul
  start /wait "" "!INNO_INSTALLER!" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CURRENTUSER /DIR="!INNO_LOCAL!"
  if errorlevel 1 (
    echo.
    echo Inno Setup installation failed with exit code !errorlevel!.
    pause
    exit /b 1
  )
)

call :LocateInno
if not defined ISCC_EXE (
  echo.
  echo Inno Setup installed, but ISCC.exe was not found.
  echo Run BUILD.bat one more time. If this repeats, send me this screen.
  pause
  exit /b 1
)
exit /b 0

:LocateInno
set "ISCC_EXE="
if exist "%LOCAL_BUILD_TOOLS%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%LOCAL_BUILD_TOOLS%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not defined ISCC_EXE if exist "%LocalAppData%\Programs\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%LocalAppData%\Programs\Inno Setup 7\ISCC.exe"
exit /b 0

:BuildFailed
echo.
echo ==========================================
echo             BUILD FAILED
echo ==========================================
echo Read the error above. Nothing was installed by StreamRoute itself.
echo.
pause
exit /b 1
