@echo off
echo Building GrimmNPC...
dotnet build GrimmNPC.csproj -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful! Copying DLL to .cursor\HarmonyMods\GrimmNPC...
    if not exist "..\GrimmNPC" mkdir "..\GrimmNPC"
    if exist "bin\Release\net48\GrimmNPC.dll" (
        copy /Y "bin\Release\net48\GrimmNPC.dll" "..\GrimmNPC\GrimmNPC.dll"
    ) else if exist "bin\Release\GrimmNPC.dll" (
        copy /Y "bin\Release\GrimmNPC.dll" "..\GrimmNPC\GrimmNPC.dll"
    ) else (
        echo ERROR: Could not find built DLL!
        pause
    )
    echo.
    echo GrimmNPC.dll copied to D:\!RustServer\.cursor\HarmonyMods\GrimmNPC\GrimmNPC.dll
    echo The mod will load automatically on next server start.
) else (
    echo.
    echo Build failed! Check errors above.
    pause
)
