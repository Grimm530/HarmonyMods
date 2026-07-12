$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\CopyPaste4.2.81.cs"
$dst = Join-Path $PSScriptRoot "CopyPaste.cs"
$text = [System.IO.File]::ReadAllText($src)

$text = $text.Replace("using Oxide.Core;`r`n", "")
$text = $text.Replace("using Oxide.Core;`n", "")
$text = $text.Replace("using Oxide.Core.Libraries.Covalence;`r`n", "")
$text = $text.Replace("using Oxide.Core.Libraries.Covalence;`n", "")
$text = $text.Replace("using Oxide.Game.Rust.Libraries.Covalence;`r`n", "")
$text = $text.Replace("using Oxide.Game.Rust.Libraries.Covalence;`n", "")
$text = $text.Replace("namespace Oxide.Plugins", "namespace CopyPasteHarmony")

$newClass = @"
    /// <summary>
    /// CopyPaste 4.2.81 ported for Harmony (no Oxide). Logic matches Oxide plugin; only I/O and hosting differ.
    /// </summary>
    public partial class CopyPaste : CopyPasteBase
"@
$pattern = '(?m)^[ \t]*\[Info\("Copy Paste".*?\r?\n[ \t]*\[Description\("Copy and paste buildings to save them or move them"\)\]\r?\n[ \t]*public class CopyPaste : CovalencePlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find class declaration to replace" }
$text = $newText

$text = $text.Replace(".IPlayer", ".ToIPlayer()")
$text = $text.Replace("Interface.Oxide.DataFileSystem", "Interface.DataFileSystem")

# StringPool is not ready during early Harmony OnLoaded — defer field initializers
$oldPool = 'private readonly uint _floorFramePrefabId = StringPool.Get("assets/prefabs/building core/floor.frame/floor.frame.prefab");
        private readonly uint _floorTriangleFramePrefabId = StringPool.Get("assets/prefabs/building core/floor.triangle.frame/floor.triangle.frame.prefab");'
$newPool = @'
private uint _floorFramePrefabId;
        private uint _floorTriangleFramePrefabId;
'@
# Normalize line endings for match
$oldPoolNorm = $oldPool -replace "`r`n", "`n"
$textNorm = $text -replace "`r`n", "`n"
if ($textNorm.Contains($oldPoolNorm)) {
    $text = $textNorm.Replace($oldPoolNorm, $newPool)
    if ($text -notmatch "`r") { $text = $text -replace "`n", "`r`n" }
} else {
    Write-Host "WARNING: StringPool field initializer pattern not found (may already be deferred)"
}

# Avoid double-replacement if ToIPlayer already applied somehow
$text = $text.Replace(".ToIPlayer()()", ".ToIPlayer()")

$replacements = @{
    '[Command("copy")]' = ''
    '[Command("paste")]' = ''
    '[Command("copylist")]' = ''
    '[Command("pasteback")]' = ''
    '[Command("undo")]' = ''
}
foreach ($k in $replacements.Keys) {
    $text = $text.Replace($k, $replacements[$k])
}

$text = $text.Replace("private void CmdCopy(", "internal void CmdCopy(")
$text = $text.Replace("private void CmdPaste(", "internal void CmdPaste(")
$text = $text.Replace("private void CmdList(", "internal void CmdList(")
$text = $text.Replace("private void CmdPasteBack(", "internal void CmdPasteBack(")
$text = $text.Replace("private void CmdUndo(", "internal void CmdUndo(")

$text = $text.Replace(
    "private HashSet<Dictionary<string, object>> PreLoadData(",
    "public HashSet<Dictionary<string, object>> PreLoadData(")
$text = $text.Replace(
    "private object FindBestHeight(HashSet<Dictionary<string, object>>",
    "public object FindBestHeight(ICollection<Dictionary<string, object>>")
$text = $text.Replace("private PasteData Paste(", "public PasteData Paste(")
$text = $text.Replace("private bool _pasteReady;", "internal bool _pasteReady;")
$text = $text.Replace("private object TryCopyFromSteamId", "public object TryCopyFromSteamId")
$text = $text.Replace("private object TryPasteFromSteamId", "public object TryPasteFromSteamId")
$text = $text.Replace("private object TryPasteFromVector3(", "public object TryPasteFromVector3(")
$text = $text.Replace("private ValueTuple<object, PasteData> TryPasteFromVector3Cancellable", "public ValueTuple<object, PasteData> TryPasteFromVector3Cancellable")
$text = $text.Replace("private string API_GetTrackerId", "public string API_GetTrackerId")

$ctorMarker = "        private readonly List<PasteData> _pendingPastes = new();"
$ctor = @"
        private readonly List<PasteData> _pendingPastes = new();

        public CopyPaste()
        {
            Version = new VersionNumber(4, 2, 81);
        }
"@
if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing" }
$text = $text.Replace($ctorMarker, $ctor)

$harmonyLifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            Init();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public override void HarmonyUnload()
        {
        }

        public bool IsPasteReadyPublic() => _pasteReady;
"@
$initPattern = '(ProcessItemDefinitions\(\);\r?\n\s*\})'
$m = [regex]::Match($text, $initPattern)
if (-not $m.Success) { throw "OnServerInitialized marker missing" }
# Only replace the first occurrence (OnServerInitialized body end)
$text = [regex]::Replace($text, $initPattern, { param($mm) $mm.Groups[1].Value + $harmonyLifecycle }, 1)

# LoadDefaultConfig already protected override - base has protected virtual, override is fine

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"

$checks = @(
    @{ Name = "Oxide.Core"; Pattern = "Oxide\.Core" },
    @{ Name = "CovalencePlugin"; Pattern = "CovalencePlugin" },
    @{ Name = "[Command]"; Pattern = "\[Command" },
    @{ Name = ".IPlayer (bad)"; Pattern = "\.IPlayer\b" },
    @{ Name = "namespace Oxide"; Pattern = "namespace Oxide" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "ToIPlayer"; Pattern = "ToIPlayer" }
)
foreach ($c in $checks) {
    $count = ([regex]::Matches($text, $c.Pattern)).Count
    Write-Host ("{0}: {1}" -f $c.Name, $count)
}
