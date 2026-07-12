param (
    [Parameter(Mandatory = $true)][string]$outputPath,
    [Parameter(Mandatory = $true)][string]$inputPath
)

$cwd = Get-Location
$publicDepsPath = Join-Path -Path $cwd -ChildPath $outputPath
$rawDepsPath = Join-Path -Path $cwd -ChildPath $inputPath

if ((Test-Path $publicDepsPath) -eq $false) {
    New-Item $publicDepsPath -ItemType Directory
}

function IsAssemblyFileNeedingPublicizing {
    param (
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$File
    )
    
    if ($File.Name.Contains("Apex")) { return $true }
    if ($File.Name.Contains("Assembly-CSharp")) { return $true }
    if ($File.Name.Contains("Facepunch")) { return $true }
    if ($File.Name -eq "NewAssembly.dll") { return $true }
    if ($File.Name.Contains("Rust")) { return $true }

    return $false
}

Write-Host 
Write-Host "#############################"
Write-Host "Deleting existing public deps"
Write-Host "#############################"
Write-Host

$files = Get-ChildItem -Path "$publicDepsPath"
foreach ($file in $files) {
    $file.Delete()
}

# Only get files, not directories
$rawFiles = Get-ChildItem -Path $rawDepsPath -File

$filesToCopy = New-Object Collections.Generic.List[System.IO.FileInfo]
$filesToPublicize = New-Object Collections.Generic.List[System.IO.FileInfo]

foreach ($file in $rawFiles) {
    if ($file.Name -like "System.") { continue }

    if (IsAssemblyFileNeedingPublicizing -file $file) {
        $filesToPublicize.Add($file);
        continue;
    }
    
    $filesToCopy.Add($file);
}

Write-Host
Write-Host "######################################################"
Write-Host "Copying assembly files that do not require publicizing"
Write-Host "######################################################"
Write-Host

foreach ($file in $filesToCopy) {
    Write-Host "Copying $($file.Name)"
    $destination = Join-Path -Path $publicDepsPath -ChildPath $file.Name;
    Copy-Item -Path $file.FullName -Destination $destination;
}

Write-Host
Write-Host "#############################################"
Write-Host "Publicizing assembly files and saving to disk"
Write-Host "#############################################"

foreach ($file in $filesToPublicize) {
    Write-Host "Processing $($file.Name)"
    $filePath = """$rawDepsPath/" + $file.Name + """"
    $fileDest = """$publicDepsPath/" + $file.Name + """"
    # Prefer script dir so it works whether batch is run from project root or workspace root
    $publicizerDir = if (Test-Path (Join-Path $PSScriptRoot "AssemblyPublicizer")) { $PSScriptRoot } else { $cwd }
    $publicizerExe = Join-Path $publicizerDir "AssemblyPublicizer\AssemblyPublicizer.exe"
    if (-not (Test-Path $publicizerExe)) {
        $publicizerExe = Join-Path $publicizerDir "AssemblyPublicizer\assembly-publicizer.exe"
    }
    if (-not (Test-Path $publicizerExe)) {
        Write-Host "ERROR: AssemblyPublicizer not found. Looked in: $publicizerDir\AssemblyPublicizer\" -ForegroundColor Red
        Write-Host "Install with: dotnet tool install BepInEx.AssemblyPublicizer.Cli --version 0.4.3 --tool-path `"$PSScriptRoot\AssemblyPublicizer`"" -ForegroundColor Yellow
        continue
    }
    if ($IsLinux) {
        Start-Process -FilePath "/usr/bin/mono" -WorkingDirectory $rawDepsPath -ArgumentList @($publicizerExe, "-i", $filePath, "-o", $fileDest) -Wait -NoNewWindow
    } else {
        Start-Process -FilePath $publicizerExe -WorkingDirectory $rawDepsPath -ArgumentList @("-i", $filePath, "-o", $fileDest) -Wait -NoNewWindow
    }
}

Write-Host
Write-Host
Write-Host "Done"