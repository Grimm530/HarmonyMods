@echo off
REM Performance Log Generator Batch File
REM This batch file runs the PowerShell performance monitoring script

echo ========================================
echo Rust Server Performance Log Generator
echo ========================================
echo.

REM Check if PowerShell is available
where pwsh >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    set PWSH_CMD=pwsh
) else (
    set PWSH_CMD=powershell
)

REM Run the PowerShell script with default parameters (10 minutes, 5 second intervals)
REM You can modify these parameters by editing the command below
%PWSH_CMD% -ExecutionPolicy Bypass -File "%~dp0generate-performance-log.ps1" -DurationMinutes 10 -SampleIntervalSeconds 5

echo.
echo Press any key to exit...
pause >nul

