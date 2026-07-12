@echo off
echo Setting up all dependencies from local Rust server...
echo.
echo Setting up Windows dependencies...
if not exist "raw-deps\windows\RustDedicated_Data\Managed" mkdir "raw-deps\windows\RustDedicated_Data\Managed"
xcopy /E /I /Y "D:\!RustServer\RustDedicated_Data\Managed\*" "raw-deps\windows\RustDedicated_Data\Managed\"
pwsh scripts\unprivate-dependencies.ps1 -outputPath "deps/windows/" -inputPath "raw-deps/windows/RustDedicated_Data/Managed"
echo.
echo Setting up Linux dependencies...
if not exist "raw-deps\linux\RustDedicated_Data\Managed" mkdir "raw-deps\linux\RustDedicated_Data\Managed"
xcopy /E /I /Y "D:\!RustServer\RustDedicated_Data\Managed\*" "raw-deps\linux\RustDedicated_Data\Managed\"
pwsh scripts\unprivate-dependencies.ps1 -outputPath "deps/linux/" -inputPath "raw-deps/linux/RustDedicated_Data/Managed"
echo.
echo Done!
PAUSE