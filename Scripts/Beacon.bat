@echo off
setlocal enabledelayedexpansion

set SERVICE_NAME=BeaconService
set DISPLAY_NAME=Beacon Consent Service
set DESCRIPTION=Lightweight consent and opt-out service
set EXE_PATH=%~dp0Beacon.exe

if "%1"=="" goto :usage

if /i "%1"=="install" goto :install
if /i "%1"=="uninstall" goto :uninstall
if /i "%1"=="start" goto :start
if /i "%1"=="stop" goto :stop
if /i "%1"=="restart" goto :restart
if /i "%1"=="status" goto :status
if /i "%1"=="test" goto :test
goto :usage

:install
  echo Installing Beacon Service...
  sc create "%SERVICE_NAME%" ^
    binPath= "\"%EXE_PATH%\"" ^
    start= auto ^
    displayname= "%DISPLAY_NAME%"

  if !ERRORLEVEL! EQU 0 (
    sc description "%SERVICE_NAME%" "%DESCRIPTION%"
    echo Service installed successfully
    echo Run '%~nx0 start' to start the service
  ) else (
    echo Installation failed. Run as Administrator.
  )
  goto :eof

:uninstall
  echo Stopping service...
  sc stop "%SERVICE_NAME%" >nul 2>&1
  timeout /t 3 /nobreak >nul

  echo Uninstalling Beacon Service...
  sc delete "%SERVICE_NAME%"

  if !ERRORLEVEL! EQU 0 (
    echo Service uninstalled successfully
  ) else (
    echo Uninstall failed. Run as Administrator.
  )
  goto :eof

:start
  echo Starting Beacon Service...
  sc start "%SERVICE_NAME%"

  if !ERRORLEVEL! EQU 0 (
    echo Service started successfully
  ) else (
    echo Failed to start service
  )
  goto :eof

:stop
  echo Stopping Beacon Service...
  sc stop "%SERVICE_NAME%"

  if !ERRORLEVEL! EQU 0 (
    echo Service stopped successfully
  ) else (
    echo Failed to stop service
  )
  goto :eof

:restart
  call :stop
  timeout /t 3 /nobreak >nul
  call :start
  goto :eof

:status
  echo Beacon Service Status:
  echo ====================
  sc query "%SERVICE_NAME%"
  echo.
  echo Recent logs:
  if exist "%~dp0log\*.log" (
    for /f "delims=" %%f in ('dir /b /o-d "%~dp0log\*.log" 2^>nul') do (
      powershell -Command "Get-Content '%~dp0log\%%f' -Tail 20"
      goto :eof
    )
  ) else (
    echo No logs found
  )
  goto :eof

:test
  echo Running Beacon in console mode...
  echo Press Ctrl+C to stop
  echo.
  "%EXE_PATH%"
  goto :eof

:usage
  echo Beacon Service Manager
  echo ============================
  echo.
  echo Usage: %~nx0 [command]
  echo.
  echo Commands:
  echo   install   - Install as Windows service (requires Admin)
  echo   uninstall - Remove Windows service (requires Admin)
  echo   start     - Start the service
  echo   stop      - Stop the service
  echo   restart   - Restart the service
  echo   status    - Show service status and recent logs
  echo   test      - Run in console mode for testing
  echo.
  goto :eof
