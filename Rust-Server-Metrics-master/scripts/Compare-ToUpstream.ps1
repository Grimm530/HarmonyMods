#Requires -Version 5.1
<#
.SYNOPSIS
  Fetches a public GitHub remote and shows how your HEAD differs from that branch.

.PARAMETER Target
  rustymoose — default; compares to rustymoose/main (maintained fork).
  upstream   — compares to upstream/master (archived features-not-bugs).

.EXAMPLE
  .\scripts\Compare-ToUpstream.ps1
  .\scripts\Compare-ToUpstream.ps1 -Target upstream

.NOTES
  Full diff:  git diff <remoteRef> HEAD
  Difftool:   git difftool <remoteRef> HEAD

  https://github.com/features-not-bugs/Rust-ServerMetrics (archived)
  https://github.com/RustyMoose/Rust.ServerMetrics (default compare target)

  Full playbook: RSM-Instructional.md -> "Fork maintenance and upstream updates".
#>
[CmdletBinding()]
param(
    [ValidateSet("rustymoose", "upstream")]
    [string] $Target = "rustymoose"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

if (-not (git remote get-url upstream 2>$null)) {
    Write-Host "Adding remote 'upstream'..." -ForegroundColor Yellow
    git remote add upstream "https://github.com/features-not-bugs/Rust-ServerMetrics.git"
}

if (-not (git remote get-url rustymoose 2>$null)) {
    Write-Host "Adding remote 'rustymoose'..." -ForegroundColor Yellow
    git remote add rustymoose "https://github.com/RustyMoose/Rust.ServerMetrics.git"
}

$ref = switch ($Target) {
    "upstream"   { @{ Fetch = @("upstream", "master"); Ref = "upstream/master" } }
    "rustymoose" { @{ Fetch = @("rustymoose", "main"); Ref = "rustymoose/main" } }
}

Write-Host "Fetching $($ref.Fetch[0]) $($ref.Fetch[1])..." -ForegroundColor Cyan
git fetch $ref.Fetch[0] $ref.Fetch[1]

Write-Host ""
Write-Host "=== Diff stat: your HEAD vs $($ref.Ref) ===" -ForegroundColor Cyan
Write-Host "(+ = you have more / different from remote)" -ForegroundColor Gray
Write-Host ""
git diff $ref.Ref HEAD --stat

Write-Host ""
Write-Host "Commits on remote not in your branch:" -ForegroundColor Cyan
git log --oneline HEAD..$($ref.Ref)

Write-Host ""
Write-Host "Next: full diff  git diff $($ref.Ref) HEAD" -ForegroundColor Yellow
Write-Host "     or GUI      git difftool $($ref.Ref) HEAD" -ForegroundColor Yellow
