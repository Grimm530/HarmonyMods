# -*- coding: utf-8 -*-
"""Convert Oxide NpcSpawn.cs into GrimmNPC Harmony mod sources (minimal changes)."""
from __future__ import annotations

import re
from pathlib import Path

SRC = Path(r"c:\!2XRUST\.cursor\Oxide.Plugins.Cant-Use\NpcSpawn.cs")
OUT_DIR = Path(r"c:\!2XRUST\.cursor\HarmonyMods\GrimmNPC")

text = SRC.read_text(encoding="utf-8")

# --- Header / usings ---
old_header = """using System;
using Facepunch;
using Oxide.Core.Plugins;
using Rust;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oxide.Core;
using UnityEngine;
using UnityEngine.AI;
using System.IO;
using HarmonyLib;
using ProtoBuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust.Ai;
using Rust.Ai.Gen2;
using Oxide.Plugins.NpcSpawnExtensionMethods;

namespace Oxide.Plugins
{
    [Info("NpcSpawn", "Grimm530", "3.3.04")]
    internal class NpcSpawn : RustPlugin
    {"""

new_header = """using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Facepunch;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Rust;
using Rust.Ai;
using Rust.Ai.Gen2;
using UnityEngine;
using UnityEngine.AI;
using GrimmNPC.NpcSpawnExtensionMethods;
using OxideCompat = GrimmNPC.OxideCompat;

namespace GrimmNPC
{
    /// <summary>
    /// Harmony port of Oxide NpcSpawn 3.3.04. Logic kept identical; only loader/config/hooks adapted for Harmony.
    /// </summary>
    public class GrimmNPC : IHarmonyModHooks
    {
        public static GrimmNPC Instance { get; private set; }
        public static readonly VersionNumber Version = new VersionNumber(3, 3, 4);
"""

if old_header not in text:
    raise SystemExit("Header block not found — NpcSpawn.cs may have changed")

text = text.replace(old_header, new_header, 1)

# Class / instance renames
text = text.replace("private static NpcSpawn _ins;", "private static GrimmNPC _ins;")
text = text.replace("NpcSpawn _ins", "GrimmNPC _ins")
# Keep CustomScientistNpc / brain names

# Remove Oxide AutoPatch attribute (Harmony PatchAll picks up HarmonyPatch)
text = text.replace("[AutoPatch]\r\n", "")
text = text.replace("[AutoPatch]\n", "")

# DeferToGrimmNpcMod: we ARE GrimmNPC now — never defer
text = text.replace("if (NpcSpawnSwimHarmonyGuard.DeferToGrimmNpcMod())\r\n                    return true;\r\n", "")
text = text.replace("if (NpcSpawnSwimHarmonyGuard.DeferToGrimmNpcMod())\n                    return true;\n", "")
text = text.replace("if (NpcSpawnSwimHarmonyGuard.DeferToGrimmNpcMod())\r\n                    return false;\r\n", "")
text = text.replace("if (NpcSpawnSwimHarmonyGuard.DeferToGrimmNpcMod())\n                    return false;\n", "")

# Extension namespace
text = text.replace(
    "    namespace NpcSpawnExtensionMethods\n",
    "    // Extension methods live in GrimmNPC.NpcSpawnExtensionMethods (same file, nested under GrimmNPC namespace root via closing brace adjustment)\n    // NOTE: kept as nested namespace under GrimmNPC file scope\n",
)

# Fix nested namespace: originally Oxide.Plugins { NpcSpawn...  namespace NpcSpawnExtensionMethods { } }
# After our change we have GrimmNPC { GrimmNPC class ... } then namespace NpcSpawnExtensionMethods
# Change closing so extension methods are GrimmNPC.NpcSpawnExtensionMethods

# PluginReference → fields resolved at runtime
text = text.replace(
    "[PluginReference] private readonly Plugin Kits, Friends, Clans;",
    "private OxideCompat.PluginRef Kits;\n        private OxideCompat.PluginRef Friends;\n        private OxideCompat.PluginRef Clans;",
)

# Interface.Oxide / Interface.CallHook → OxideCompat
text = text.replace("Interface.Oxide.CallHook", "OxideCompat.CallHook")
text = text.replace("Interface.CallHook", "OxideCompat.CallHook")
text = text.replace("Interface.Oxide.LogWarning", "OxideCompat.LogWarning")
text = text.replace("Interface.Oxide.DataDirectory", "OxideCompat.DataDirectory")

# Config overrides → file-based
text = text.replace("protected override void LoadDefaultConfig()", "private void LoadDefaultConfig()")
text = text.replace("protected override void LoadConfig()", "private void LoadConfig()")
text = text.replace("protected override void SaveConfig() => Config.WriteObject(_config);",
                    "private void SaveConfig() => OxideCompat.WriteConfig(_config);")
text = text.replace("base.LoadConfig();\n            _config = Config.ReadObject<PluginConfig>();",
                    "_config = OxideCompat.ReadConfig<PluginConfig>();")
text = text.replace("base.LoadConfig();\r\n            _config = Config.ReadObject<PluginConfig>();",
                    "_config = OxideCompat.ReadConfig<PluginConfig>();")

# Version references on RustPlugin
text = text.replace("_config.PluginVersion = Version;", "_config.PluginVersion = GrimmNPC.Version;")
text = text.replace("if (_config.PluginVersion < Version)", "if (_config.PluginVersion < GrimmNPC.Version)")

# Init / OnServerInitialized / Unload → lifecycle helpers (keep method bodies; wire from OnLoaded)
text = text.replace("private void Init() => _ins = this;",
                    "private void Init()\n        {\n            _ins = this;\n            Instance = this;\n            Kits = OxideCompat.PluginRef.Find(\"Kits\");\n            Friends = OxideCompat.PluginRef.Find(\"Friends\");\n            Clans = OxideCompat.PluginRef.Find(\"Clans\");\n        }")

# Make Oxide hook methods internal so Harmony patches in companion file can call them
for name in [
    "OnEntityKill",
    "OnCorpsePopulate",
    "CanBradleyApcTarget",
    "OnNpcTarget",
    "OnTurretTarget",
    "CanBeTargeted",
    "OnCustomNpcTarget",
    "OnEntityTakeDamage",
    "OnLoseCondition",
]:
    text = text.replace(f"private object {name}", f"internal object {name}")
    text = text.replace(f"private void {name}", f"internal void {name}")

# Commands: strip attributes; registration done in OnLoaded via OxideCompat
text = text.replace("[ChatCommand(\"npccount\")]\n\t\t", "")
text = text.replace("[ChatCommand(\"npccount\")]\r\n\t\t", "")
text = text.replace("[ConsoleCommand(\"npccount\")]\n\t\t", "")
text = text.replace("[ConsoleCommand(\"npccount\")]\r\n\t\t", "")
text = text.replace("[ChatCommand(\"npcdiag\")]\n\t\t", "")
text = text.replace("[ChatCommand(\"npcdiag\")]\r\n\t\t", "")
text = text.replace("[ConsoleCommand(\"npcdiag\")]\n\t\t", "")
text = text.replace("[ConsoleCommand(\"npcdiag\")]\r\n\t\t", "")

# Make command methods internal for registration
text = text.replace("private void CmdNpcCount", "internal void CmdNpcCount")
text = text.replace("private void ConNpcCount", "internal void ConNpcCount")
text = text.replace("private void CmdNpcDiag", "internal void CmdNpcDiag")
text = text.replace("private void ConNpcDiag", "internal void ConNpcDiag")

# Insert IHarmonyModHooks + timer helper before #region Oxide Hooks
lifecycle = '''
        #region Harmony lifecycle
        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Init();
            LoadConfig();
            OxideCompat.EnsureDataFolders();
            OxideCompat.RegisterCommands(this);
            // Delay server-init work until ServerMgr exists (Harmony may load BeforeSceneLoad).
            OxideCompat.RunWhenServerInitialized(() =>
            {
                if (Instance == null) return;
                OnServerInitialized();
            });
            UnityEngine.Debug.Log("[GrimmNPC] Loaded (NpcSpawn Harmony port 3.3.04)");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            OxideCompat.UnregisterCommands();
            Unload();
            Instance = null;
            UnityEngine.Debug.Log("[GrimmNPC] Unloaded");
        }

        /// <summary>Oxide timer.Once replacement.</summary>
        internal OxideCompat.TimerHelper timer => OxideCompat.Timer;

        private void Puts(string message) => UnityEngine.Debug.Log("[GrimmNPC] " + message);
        private void PrintWarning(string message) => UnityEngine.Debug.LogWarning("[GrimmNPC] " + message);
        private void SendReply(BasePlayer player, string message)
        {
            if (player != null && player.IsConnected)
                player.ChatMessage(message);
            else
                Puts(message);
        }
        #endregion Harmony lifecycle

'''

marker = "        #region Oxide Hooks"
if marker not in text:
    raise SystemExit("Oxide Hooks region not found")
text = text.replace(marker, lifecycle + marker, 1)

# Extension methods namespace: file ends with namespace NpcSpawnExtensionMethods inside Oxide.Plugins
# After class closes we need: } namespace GrimmNPC.NpcSpawnExtensionMethods or keep nested
# Current structure after edits:
# namespace GrimmNPC { class GrimmNPC { ... }   namespace NpcSpawnExtensionMethods { } }
# Nested namespace NpcSpawnExtensionMethods under GrimmNPC becomes GrimmNPC.NpcSpawnExtensionMethods — good if using is GrimmNPC.NpcSpawnExtensionMethods

# But we removed "namespace NpcSpawnExtensionMethods" comment incorrectly — restore proper namespace
text = text.replace(
    "    // Extension methods live in GrimmNPC.NpcSpawnExtensionMethods (same file, nested under GrimmNPC namespace root via closing brace adjustment)\n    // NOTE: kept as nested namespace under GrimmNPC file scope\n",
    "    namespace NpcSpawnExtensionMethods\n",
)

# plugins.Exists → PluginRef
text = text.replace("_ins.plugins.Exists(\"Friends\")", "_ins.Friends.Exists")
text = text.replace("_ins.plugins.Exists(\"Clans\")", "_ins.Clans.Exists")
text = text.replace("_ins.Kits != null", "_ins.Kits.Exists")
# Kits usage
text = re.sub(
    r"Kits\.Call\(",
    "Kits.Call(",
    text,
)

out_main = OUT_DIR / "GrimmNPC.cs"
out_main.write_text(text, encoding="utf-8")
print(f"Wrote {out_main} ({out_main.stat().st_size} bytes)")
