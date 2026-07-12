@echo off
echo Building CustomMapGen...
dotnet build CustomMapGen.csproj -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful! Copying DLL to HarmonyMods...
    if not exist "..\..\HarmonyMods" mkdir "..\..\HarmonyMods"
    if exist "bin\Release\net48\CustomMapGen.dll" (
        copy /Y "bin\Release\net48\CustomMapGen.dll" "..\..\HarmonyMods\CustomMapGen.dll"
    ) else if exist "bin\Release\CustomMapGen.dll" (
        copy /Y "bin\Release\CustomMapGen.dll" "..\..\HarmonyMods\CustomMapGen.dll"
    ) else (
        echo ERROR: Could not find built DLL!
        pause
    )
    echo.
    echo CustomMapGen.dll copied to D:\!RustServer\HarmonyMods\CustomMapGen.dll
    echo The mod will load automatically on next server start.
) else (
    echo.
    echo Build failed! Check errors above.
    pause
)
