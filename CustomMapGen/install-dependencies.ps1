# Install dependencies for building CustomMapGen (and optionally Cursor extensions).
# Run from any location: .\install-dependencies.ps1
# Or from repo root: .\.cursor\HarmonyMods\CustomMapGen\install-dependencies.ps1

$ErrorActionPreference = "Stop"

# --- 1. .NET SDK (required for dotnet build) ---
$sdks = & dotnet --list-sdks 2>$null
if (-not $sdks) {
    Write-Host "No .NET SDK found. Installing .NET 8 SDK via winget..." -ForegroundColor Yellow
    winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Winget install failed or was skipped. Install manually: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    }
    Write-Host "SDK installed. Close and reopen your terminal (or Cursor), then run build.ps1 again." -ForegroundColor Green
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
}
$sdksNow = & dotnet --list-sdks 2>$null
if ($sdksNow) {
    Write-Host " .NET SDKs: $($sdksNow -join ', ')" -ForegroundColor Cyan
} else {
    Write-Host " .NET SDK still not visible. Restart your terminal/IDE and run this script again." -ForegroundColor Yellow
}

# --- 2. Cursor / VS Code extensions (optional) ---
$extensions = @(
    "ms-dotnettools.csharp",
    "ms-dotnettools.csdevkit"
)
$cli = $null
if (Get-Command cursor -ErrorAction SilentlyContinue) { $cli = "cursor" }
elseif (Get-Command code -ErrorAction SilentlyContinue) { $cli = "code" }
if ($cli) {
    Write-Host "`nInstalling recommended C# extensions via $cli..." -ForegroundColor Cyan
    foreach ($ext in $extensions) {
        & $cli --install-extension $ext --force 2>$null
        if ($LASTEXITCODE -eq 0) { Write-Host "  Installed: $ext" -ForegroundColor Green }
        else { Write-Host "  Skip/fail: $ext" -ForegroundColor Gray }
    }
} else {
    Write-Host "`nCursor/VS Code CLI not in PATH. Open this workspace in Cursor and accept the prompt to install recommended extensions." -ForegroundColor Yellow
}

Write-Host "`nDone. To build CustomMapGen, run: .\build.ps1" -ForegroundColor Green
