$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\PlayerSkins.cs"
$dst = Join-Path $PSScriptRoot "PlayerSkinsPlugin.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# --- Usings: strip Oxide runtime, keep Chaos / Facepunch / etc. ---
foreach ($using in @(
    "using Oxide.Core;",
    "using Oxide.Core.Libraries;",
    "using Oxide.Core.Plugins;"
)) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}

if ($text -notmatch "(?m)^using PlayerSkinsHarmony;\r?\n") {
    $text = [regex]::Replace($text, "(?m)(^using UIAnchor = Oxide\.Ext\.Chaos\.UIFramework\.Anchor;\r?\n)", '$1using PlayerSkinsHarmony;' + "`r`n")
}

# --- Namespace ---
$text = $text.Replace("namespace Oxide.Plugins", "namespace PlayerSkinsHarmony")

# --- Class declaration ---
$newClass = @"
    /// <summary>PlayerSkins 3.0.141 Harmony port</summary>
    public class PlayerSkinsPlugin
"@
$pattern = '(?m)^[ \t]*\[Info\("PlayerSkins", "Grimm530", "3\.0\.141"\)\]\r?\n[ \t]*class PlayerSkins : ChaosPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find PlayerSkins class declaration to replace" }
$text = $newText

# --- Permission attributes ---
$text = $text.Replace("[Chaos.Permission]", "[Permission]")

# --- Static instance type ---
$text = $text.Replace("private static PlayerSkins s_Instance;", "private static PlayerSkinsPlugin s_Instance;")

# --- Title property (after s_Instance field) ---
$text = $text.Replace(
    "private static PlayerSkinsPlugin s_Instance;",
    @"
private static PlayerSkinsPlugin s_Instance;

        public string Title => "PlayerSkins";
"@)

# --- Strip console command attributes (ccmd* registered in mod) ---
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\("playerskins\.skins"\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\("playerskins\.setprice"\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\("playerskins\.giveskin"\)\]\r?\n', "")

# --- Hooks / commands: private -> internal for Harmony patches ---
$internalMethods = @(
    "OnServerInitialized", "Unload", "OnServerSave",
    "OnLootEntityEnd", "OnPlayerDeath",
    "CanAcceptItem", "CanMoveItem",
    "OnItemAddedToContainer", "OnItemRemovedFromContainer", "OnActiveItemChanged",
    "OnUseNPC", "OnItemCraftFinished",
    "cmdAddSkin", "cmdSkin", "cmdReSkin", "cmdSkinShop",
    "ccmdSkinManager", "ccmdSetSkinPrice", "ccmdGiveSkin"
)
foreach ($method in $internalMethods) {
    $text = [regex]::Replace($text,
        "(?m)^(\s*)private (void|object|bool) " + [regex]::Escape($method) + "\(",
        '$1internal $2 ' + $method + '(')
}

# --- RegisterChatCommands: empty body, remove call from OnServerInitialized ---
$text = [regex]::Replace($text, '(?m)^\s*RegisterChatCommands\(\);\r?\n', "")
$text = [regex]::Replace($text,
    '(?ms)(private void RegisterChatCommands\(\)\s*\{).*?(\n        \})',
    '$1$2')
$text = [regex]::Replace($text,
    '(?ms)(private void RegisterChatCommands\(\)\s*\{)\s*(\n        \})',
    '$1$2')

# --- Permissions bridge ---
$text = [regex]::Replace($text, 'permission\.RegisterPermission\(([^,]+),\s*this\)', 'PermissionsBridge.RegisterPermission($1)')

# --- Lang bridge ---
$text = $text.Replace("lang.RegisterMessages(m_Messages, this)", "PlayerSkinsHost.Instance.Lang.RegisterMessages(m_Messages)")

# --- Configuration: ChaosPlugin overrides -> standalone ---
$text = $text.Replace(
    "private ConfigData Configuration => ConfigurationData as ConfigData;",
    @"
private ConfigData m_Configuration;

        private ConfigData Configuration => m_Configuration;
"@)

$text = $text.Replace("protected override void OnConfigurationUpdated", "private void OnConfigurationUpdated")
$text = $text.Replace("ConfigData baseConfigData = GenerateDefaultConfiguration<ConfigData>();", "ConfigData baseConfigData = GenerateDefaultConfiguration();")
$text = $text.Replace("(ConfigurationData as ConfigData)", "m_Configuration")

$text = [regex]::Replace($text, '(?m)^[ \t]*protected override ConfigurationFile OnLoadConfig\(ref ConfigurationFile configurationFile\).*?\r?\n', "")

$text = [regex]::Replace($text,
    '(?ms)protected override T GenerateDefaultConfiguration<T>\(\)\s*\{',
    'private static ConfigData GenerateDefaultConfiguration()' + "`r`n        {")
$text = [regex]::Replace($text, '(?m)\}\s+as T;\s*$', '};', 1)

$text = [regex]::Replace($text, '(?m)^[ \t]*protected override void PopulatePhrases\(\)\{\}\r?\n', "")

# SaveConfiguration wrapper (SaveConfig implemented in host compat)
$text = [regex]::Replace($text,
    '(?m)(private ConfigData Configuration => m_Configuration;\s*\r?\n)',
    '$1        private void SaveConfiguration() => SaveConfig();' + "`r`n")

# --- Loaded(): extract body, remove method ---
$loadedMatch = [regex]::Match($text, '(?ms)        private void Loaded\(\)\s*\{(.*?)\n        \}\s*\n')
if (-not $loadedMatch.Success) { throw "Loaded() method not found" }
$loadedBody = $loadedMatch.Groups[1].Value
$loadedBody = [regex]::Replace($loadedBody, '(?m)^\s*s_Instance = this;\s*\r?\n', "")
$loadedBody = $loadedBody.TrimEnd()

$text = [regex]::Replace($text, '(?ms)        private void Loaded\(\)\s*\{.*?\n        \}\s*\n', "", 1)

# --- Harmony lifecycle (after Unload) ---
$harmonyLifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide Loaded / OnServerInitialized / Unload) ----
        public void HarmonyInit()
        {
            s_Instance = this;
            LoadConfig();
            m_Configuration = Configuration;
$loadedBody
            PlayerSkinsHost.Instance?.ReloadLanguage();
        }

        public void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public void HarmonyUnload()
        {
            Unload();
        }
"@

$classEnd = '(?ms)(\r?\n        #endregion \r?\n)(    \}\s*\r?\n\}\s*)$'
if (-not [regex]::IsMatch($text, $classEnd)) { throw "Class closing marker missing" }
$text = [regex]::Replace($text, $classEnd, { param($m) $m.Groups[1].Value + $harmonyLifecycle + "`r`n" + $m.Groups[2].Value }, 1)

[System.IO.File]::WriteAllText($dst, $text)
$lineCount = ($text -split "`n").Count
Write-Host "Wrote $dst ($lineCount lines)"

$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core[^.]" },
    @{ Name = "ChaosPlugin"; Pattern = "ChaosPlugin" },
    @{ Name = "[Info("; Pattern = '\[Info\("PlayerSkins"' },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "cmd.AddChatCommand"; Pattern = "cmd\.AddChatCommand" },
    @{ Name = "RegisterChatCommands() call"; Pattern = "RegisterChatCommands\(\);" },
    @{ Name = "permission.RegisterPermission"; Pattern = "permission\.RegisterPermission" },
    @{ Name = "lang.RegisterMessages"; Pattern = "lang\.RegisterMessages" },
    @{ Name = "[Chaos.Permission]"; Pattern = "\[Chaos\.Permission\]" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "class PlayerSkinsPlugin"; Pattern = "class PlayerSkinsPlugin" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "PermissionsBridge"; Pattern = "PermissionsBridge\.RegisterPermission" },
    @{ Name = "PlayerSkinsHost.Lang"; Pattern = "PlayerSkinsHost\.Instance\.Lang" },
    @{ Name = "Loaded()"; Pattern = "void Loaded\(" },
    @{ Name = "ConfigurationData"; Pattern = "ConfigurationData" },
    @{ Name = "protected override"; Pattern = "protected override" }
)
foreach ($c in $checks) {
    $count = ([regex]::Matches($text, $c.Pattern)).Count
    Write-Host ("{0}: {1}" -f $c.Name, $count)
}
