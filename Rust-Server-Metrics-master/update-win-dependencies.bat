@echo off
echo Setting up Windows dependencies from local Rust server...
if not exist "raw-deps\windows\RustDedicated_Data\Managed" mkdir "raw-deps\windows\RustDedicated_Data\Managed"
xcopy /E /I /Y "D:\!RustServer\RustDedicated_Data\Managed\*" "raw-deps\windows\RustDedicated_Data\Managed\"
pwsh scripts\unprivate-dependencies.ps1 -outputPath "deps/windows/" -inputPath "raw-deps/windows/RustDedicated_Data/Managed"
PAUSE