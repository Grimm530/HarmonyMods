# Rust Server Metrics Build Script
# Builds RustServerMetrics.dll for Windows or Linux and copies to HarmonyMods directory

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Rust Server Metrics Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Non-interactive: use defaults when -NonInteractive or CI env is set
$nonInteractive = $args -contains "-NonInteractive" -or $env:CI -eq "1"
# Ask which platform to build for
Write-Host "Which platform is your Rust server running on?" -ForegroundColor Yellow
Write-Host "  1) Windows" -ForegroundColor Gray
Write-Host "  2) Linux" -ForegroundColor Gray
$platformChoice = if ($nonInteractive) { "1" } else { Read-Host "Enter choice (1 or 2, default: 1)" }
if ([string]::IsNullOrWhiteSpace($platformChoice)) { $platformChoice = "1" }

if ($platformChoice -eq "2" -or $platformChoice -eq "L" -or $platformChoice -eq "l") {
    $Configuration = "Linux"
    $depsScriptName = "update-lin-dependencies.bat"
} else {
    $Configuration = "Windows"
    $depsScriptName = "update-win-dependencies.bat"
}

Write-Host ""
Write-Host "Building for: $Configuration" -ForegroundColor Cyan
Write-Host ""

# Configuration
$SolutionPath = Join-Path $PSScriptRoot "RustServerMetrics.sln"
$ProjectPath = Join-Path $PSScriptRoot "src\RustServerMetrics\RustServerMetrics.csproj"
$OutputPath = Join-Path $PSScriptRoot "src\RustServerMetrics\bin\$Configuration\net48\RustServerMetrics.dll"
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$TargetPath = Join-Path $workspaceRoot "HarmonyMods\RustServerMetrics.dll"
$Platform = "Any CPU"

# Check if solution exists
if (-not (Test-Path $SolutionPath)) {
    Write-Host "ERROR: Solution file not found at: $SolutionPath" -ForegroundColor Red
    exit 1
}

# Check if MSBuild is available
$msbuildPath = $null
$msbuildPaths = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\MSBuild\15.0\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe",
    "${env:ProgramFiles}\dotnet\dotnet.exe"
)

foreach ($path in $msbuildPaths) {
    if (Test-Path $path) {
        $msbuildPath = $path
        break
    }
}

if ($null -eq $msbuildPath) {
    Write-Host "ERROR: MSBuild not found. Please install Visual Studio or .NET SDK." -ForegroundColor Red
    Write-Host "Searched paths:" -ForegroundColor Yellow
    foreach ($path in $msbuildPaths) {
        Write-Host "  - $path" -ForegroundColor Gray
    }
    exit 1
}

Write-Host "Using MSBuild: $msbuildPath" -ForegroundColor Gray
Write-Host ""

# Optional: Update dependencies (platform-specific)
$updateDeps = if ($nonInteractive) { "N" } else { Read-Host "Update dependencies before building? (y/N)" }
if ($updateDeps -eq "y" -or $updateDeps -eq "Y") {
    Write-Host ""
    Write-Host "Updating $Configuration dependencies..." -ForegroundColor Cyan
    
    # Check if Rust server path exists
    $rustServerPath = "D:\!RustServer\RustDedicated_Data\Managed"
    if (-not (Test-Path $rustServerPath)) {
        Write-Host "WARNING: Rust server path not found: $rustServerPath" -ForegroundColor Yellow
        Write-Host "Skipping dependency update. Build may fail if dependencies are outdated." -ForegroundColor Yellow
    } else {
        # Use platform-specific dependency update script
        $depsScript = Join-Path $PSScriptRoot $depsScriptName
        if (Test-Path $depsScript) {
            # Run without PAUSE at the end (non-interactive)
            # Also need to bypass PowerShell execution policy for unprivate-dependencies.ps1
            $depsContent = Get-Content $depsScript -Raw
            $depsContent = $depsContent -replace "PAUSE", "REM PAUSE"
            
            # FIX: PowerShell execution policy and path issues
            # Problem: Batch files call "pwsh scripts\unprivate-dependencies.ps1" which fails due to:
            #   1. Execution policy restrictions (SecurityError)
            #   2. Relative paths not resolving correctly when called from temp script
            # Solution: Replace with absolute path and -ExecutionPolicy Bypass flag
            # Pattern: "pwsh scripts\unprivate-dependencies.ps1 -args" 
            #    -> "pwsh -ExecutionPolicy Bypass -File "fullpath\scripts\unprivate-dependencies.ps1" -args"
            $scriptPath = Join-Path $PSScriptRoot "scripts\unprivate-dependencies.ps1"
            # Regex matches: (pwsh|powershell) + whitespace + scripts\ + scriptname.ps1
            $depsContent = $depsContent -replace '(pwsh|powershell)\s+scripts\\([^\s]+\.ps1)', "`$1 -ExecutionPolicy Bypass -File `"$scriptPath`""
            $tempScript = Join-Path $env:TEMP "update-deps-$(Get-Random).bat"
            $depsContent | Set-Content $tempScript
            & cmd /c $tempScript
            $exitCode = $LASTEXITCODE
            Remove-Item $tempScript -Force -ErrorAction SilentlyContinue
            
            if ($exitCode -ne 0) {
                Write-Host "WARNING: Dependency update may have failed. Continuing with build..." -ForegroundColor Yellow
            } else {
                Write-Host "Dependencies updated successfully." -ForegroundColor Green
            }
        } else {
            # Fallback to update-all-dependencies.bat if platform-specific script doesn't exist
            $depsScript = Join-Path $PSScriptRoot "update-all-dependencies.bat"
            if (Test-Path $depsScript) {
                Write-Host "Platform-specific script not found, using update-all-dependencies.bat..." -ForegroundColor Yellow
                $depsContent = Get-Content $depsScript -Raw
                $depsContent = $depsContent -replace "PAUSE", "REM PAUSE"
                # Fix PowerShell execution policy issue and use absolute paths
                $scriptPath = Join-Path $PSScriptRoot "scripts\unprivate-dependencies.ps1"
                # Match: pwsh/powershell + whitespace + scripts\ + scriptname.ps1
                $depsContent = $depsContent -replace '(pwsh|powershell)\s+scripts\\([^\s]+\.ps1)', "`$1 -ExecutionPolicy Bypass -File `"$scriptPath`""
                $tempScript = Join-Path $env:TEMP "update-deps-$(Get-Random).bat"
                $depsContent | Set-Content $tempScript
                & cmd /c $tempScript
                $exitCode = $LASTEXITCODE
                Remove-Item $tempScript -Force -ErrorAction SilentlyContinue
                
                if ($exitCode -ne 0) {
                    Write-Host "WARNING: Dependency update may have failed. Continuing with build..." -ForegroundColor Yellow
                } else {
                    Write-Host "Dependencies updated successfully." -ForegroundColor Green
                }
            } else {
                Write-Host "WARNING: Dependency update scripts not found. Skipping dependency update." -ForegroundColor Yellow
            }
        }
    }
    Write-Host ""
}

# Clean previous build
Write-Host "Cleaning previous build..." -ForegroundColor Cyan
$binPath = Join-Path $PSScriptRoot "src\RustServerMetrics\bin"
$objPath = Join-Path $PSScriptRoot "src\RustServerMetrics\obj"
if (Test-Path $binPath) {
    Remove-Item $binPath -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $objPath) {
    Remove-Item $objPath -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "Clean complete." -ForegroundColor Green
Write-Host ""

# Build the solution
Write-Host "Building RustServerMetrics..." -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
Write-Host "  Platform: $Platform" -ForegroundColor Gray
Write-Host ""

# Build using solution configuration format: Configuration|Platform
# FIX: Build configuration issues
# Problem: MSBuild was defaulting to Linux configuration even when Windows was selected
#    - Solution files with custom configurations (Windows/Linux) need special handling
#    - Just passing /p:Configuration wasn't sufficient for solution-level configs
# Solution: Use both solution-level and project-level properties:
#    1. /p:SolutionConfigurationPlatforms="Windows|Any CPU" (quoted, for solution file)
#    2. /p:Configuration=Windows (for project-level override)
#    3. /p:Platform=Any CPU (for project-level override)
# This ensures the correct configuration is used throughout the build process
$solutionConfig = "$Configuration|$Platform"
$buildArgs = @(
    $SolutionPath,
    "/p:SolutionConfigurationPlatforms=`"$solutionConfig`"",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/t:Build",
    "/v:minimal",
    "/nologo"
)

# For .NET Framework projects, we need MSBuild.exe, not dotnet.exe
if ($msbuildPath -like "*dotnet.exe") {
    Write-Host "WARNING: dotnet.exe detected, but this project requires MSBuild.exe for .NET Framework 4.8" -ForegroundColor Yellow
    Write-Host "Please install Visual Studio Build Tools or use MSBuild from Visual Studio." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Attempting to find MSBuild.exe..." -ForegroundColor Yellow
    
    # Try to find MSBuild via vswhere (Visual Studio Installer)
    $vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswherePath) {
        $vsPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($vsPath) {
            $vsMsbuildPath = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $vsMsbuildPath) {
                $msbuildPath = $vsMsbuildPath
                Write-Host "Found MSBuild: $msbuildPath" -ForegroundColor Green
            }
        }
    }
    
    if ($msbuildPath -like "*dotnet.exe") {
        Write-Host "ERROR: Could not find MSBuild.exe. This project requires Visual Studio MSBuild." -ForegroundColor Red
        exit 1
    }
}

# Restore NuGet packages first
Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
$restoreArgs = @(
    $SolutionPath,
    "/t:Restore",
    "/v:minimal",
    "/nologo"
)
& $msbuildPath $restoreArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "WARNING: NuGet restore may have failed, but continuing with build..." -ForegroundColor Yellow
}
Write-Host ""

# Build the solution
& $msbuildPath $buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Build successful!" -ForegroundColor Green
Write-Host ""

# Check if output file exists
if (-not (Test-Path $OutputPath)) {
    Write-Host "ERROR: Output file not found at: $OutputPath" -ForegroundColor Red
    Write-Host "Build may have succeeded but DLL was not generated." -ForegroundColor Yellow
    exit 1
}

Write-Host "Output file: $OutputPath" -ForegroundColor Gray
$fileInfo = Get-Item $OutputPath
Write-Host "  Size: $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
Write-Host ""

# Copy to target location
Write-Host "Copying to target location..." -ForegroundColor Cyan
$targetDir = Split-Path $TargetPath -Parent

if (-not (Test-Path $targetDir)) {
    Write-Host "Creating target directory: $targetDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

# Backup existing file if it exists
if (Test-Path $TargetPath) {
    $backupPath = $TargetPath + ".backup"
    Write-Host "Backing up existing file to: $backupPath" -ForegroundColor Gray
    Copy-Item $TargetPath $backupPath -Force
}

# Copy the DLL
Copy-Item $OutputPath $TargetPath -Force
Write-Host "Copied to: $TargetPath" -ForegroundColor Green
Write-Host ""

# Verify copy
if (Test-Path $TargetPath) {
    $targetInfo = Get-Item $TargetPath
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Build and deployment complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "File Details:" -ForegroundColor Cyan
    Write-Host "  Location: $TargetPath" -ForegroundColor Gray
    Write-Host "  Size: $([math]::Round($targetInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
    Write-Host "  Modified: $($targetInfo.LastWriteTime)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "The mod is ready to use. Restart your Rust server to load the new version." -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "ERROR: Failed to copy file to target location!" -ForegroundColor Red
    exit 1
}
