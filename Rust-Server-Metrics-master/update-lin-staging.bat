@echo off
echo WARNING: Staging branch requires Steam download - using local dependencies instead
echo Setting up Linux dependencies from local Rust server...
if not exist "raw-deps\linux\RustDedicated_Data\Managed" mkdir "raw-deps\linux\RustDedicated_Data\Managed"
xcopy /E /I /Y "D:\!RustServer\RustDedicated_Data\Managed\*" "raw-deps\linux\RustDedicated_Data\Managed\"
pwsh scripts\unprivate-dependencies.ps1 -outputPath "deps/linux/" -inputPath "raw-deps/linux/RustDedicated_Data/Managed"
PAUSE