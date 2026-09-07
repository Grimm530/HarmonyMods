# Remove Oxide namespace/name references from Harmony mod sources.
# Oxide.* compat shims become Harmony.* / Grimm.Chaos.* — Harmony-only runtime required.

$root = Split-Path $PSScriptRoot -Parent
$excludeDirPattern = '(\\|^)(GrimmNPC\\OXIDE PLUGINS|RaidableBases_3\.1\.7_legacy|GrimmNPC2|GrimmNPCOrigional|obj\\|bin\\)'

$replacements = @(
    @{ From = 'Harmony.Core.Libraries.Covalence'; To = 'Harmony.Core.Libraries.Covalence' },
    @{ From = 'Harmony.Core.Configuration'; To = 'Harmony.Core.Configuration' },
    @{ From = 'Harmony.Core.Libraries'; To = 'Harmony.Core.Libraries' },
    @{ From = 'Harmony.Core.Plugins'; To = 'Harmony.Core.Plugins' },
    @{ From = 'Harmony.Core'; To = 'Harmony.Core' },
    @{ From = 'Harmony.Plugins'; To = 'Harmony.Plugins' },
    @{ From = 'Grimm.Chaos.TextMeshPro.Fonts'; To = 'Grimm.Chaos.TextMeshPro.Fonts' },
    @{ From = 'Grimm.Chaos.TextMeshPro'; To = 'Grimm.Chaos.TextMeshPro' },
    @{ From = 'Grimm.Chaos.Collections'; To = 'Grimm.Chaos.Collections' },
    @{ From = 'Grimm.Chaos.Discord'; To = 'Grimm.Chaos.Discord' },
    @{ From = 'Grimm.Chaos.Data'; To = 'Grimm.Chaos.Data' },
    @{ From = 'Grimm.Chaos.Map'; To = 'Grimm.Chaos.Map' },
    @{ From = 'Grimm.Chaos'; To = 'Grimm.Chaos' },
    @{ From = 'Game.Rust.Cui'; To = 'Game.Rust.Cui' },
    @{ From = 'HarmonyModInterface.Mods'; To = 'HarmonyModInterface.Mods' },
    @{ From = 'HarmonyModInterface.CallHook'; To = 'HarmonyModHarmonyModInterface.CallHook' },
    @{ From = 'HarmonyModInterface.Call('; To = 'HarmonyModHarmonyModInterface.Call(' },
    @{ From = 'HarmonyModInterface.NextTick'; To = 'HarmonyModHarmonyModInterface.NextTick' },
    @{ From = 'HarmonyModInterface.GetMod'; To = 'HarmonyModHarmonyModInterface.GetMod' },
    @{ From = 'public static readonly HarmonyModRuntime Mods'; To = 'public static readonly HarmonyModRuntime Mods' },
    @{ From = 'public class HarmonyModRuntime'; To = 'public class HarmonyModRuntime' },
    @{ From = 'public static HarmonyModRuntime GetMod() => Mods'; To = 'public static HarmonyModRuntime GetMod() => Mods' },
    @{ From = 'public static HarmonyModRuntime GetMod() => Mods'; To = 'public static HarmonyModRuntime GetMod() => Mods' },
    @{ From = 'public static class HarmonyModInterface'; To = 'public static class HarmonyModInterface' },
    @{ From = 'RootModManager'; To = 'RootModManager' },
    @{ From = 'ModManager'; To = 'ModManager' },
    @{ From = 'ModDirectory'; To = 'ModDirectory' },
    @{ From = 'HarmonyCompat'; To = 'HarmonyCompat' },
    @{ From = 'HarmonyHooksPatches'; To = 'HarmonyHooksPatches' },
    @{ From = 'HarmonyHooks'; To = 'HarmonyHooks' },
    @{ From = 'Harmony'; To = 'Harmony' },
    @{ From = 'Harmony'; To = 'Harmony' },
    @{ From = '(Harmony port)'; To = '(Harmony port)' },
    @{ From = 'Harmony port'; To = 'Harmony port' },
    @{ From = 'RustCui'; To = 'RustCui' },
    @{ From = 'Harmony-compatible'; To = 'Harmony-compatible' },
    @{ From = 'Harmony-like'; To = 'Harmony-like' },
    @{ From = 'Harmony mod'; To = 'Harmony mod' },
    @{ From = 'Harmony Mod'; To = 'Harmony Mod' },
    @{ From = 'Harmony hook'; To = 'Harmony hook' },
    @{ From = 'Harmony Hook'; To = 'Harmony Hook' },
    @{ From = 'Legacy -> Harmony'; To = 'Legacy -> Harmony' },
    @{ From = 'Legacy->Harmony'; To = 'Legacy->Harmony' },
    @{ From = ' under Harmony'; To = ' under Harmony' },
    @{ From = 'Harmony-only'; To = 'Harmony-only' },
    @{ From = 'Harmony-only'; To = 'Harmony-only' },
    @{ From = 'without legacy plugin host'; To = 'without legacy plugin host' },
    @{ From = 'convert-from-legacy'; To = 'convert-from-legacy' }
)

$extensions = @('*.cs', '*.csproj', '*.md', '*.ps1', '*.json')
$changed = 0

Get-ChildItem -Path $root -Recurse -Include $extensions -File | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length)
    if ($rel -match $excludeDirPattern) { return }

    $text = [IO.File]::ReadAllText($_.FullName)
    $orig = $text
    foreach ($r in $replacements) {
        $text = $text.Replace($r.From, $r.To)
    }
    if ($text -ne $orig) {
        [IO.File]::WriteAllText($_.FullName, $text)
        $changed++
    }
}

# Rename Oxide* compat files
Get-ChildItem -Path $root -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length)
    if ($rel -match $excludeDirPattern) { return }
    $newName = $_.Name `
        -replace '^HarmonyCompatExtras\.cs$', 'HarmonyCompatExtras.cs' `
        -replace '^HarmonyCompatStubs\.cs$', 'HarmonyCompatStubs.cs' `
        -replace '^HarmonyCompat\.cs$', 'HarmonyCompat.cs' `
        -replace '^HarmonyHooksPatches\.cs$', 'HarmonyHooksPatches.cs'
    if ($newName -ne $_.Name) {
        $dest = Join-Path $_.DirectoryName $newName
        if (-not (Test-Path $dest)) {
            Move-Item -LiteralPath $_.FullName -Destination $dest
            Write-Host "Renamed: $rel -> $newName"
        }
    }
}

Write-Host "Updated $changed files under $root"
