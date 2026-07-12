using HarmonyLib;
using UnityEngine;

namespace Rustcord.Patches;

/// <summary>Chinook crate dropped - SetWasDropped called when crate spawns from helicopter.</summary>
[HarmonyPatch(typeof(HackableLockedCrate), "SetWasDropped")]
internal class HackableLockedCrate_SetWasDropped_Patch
{
    [HarmonyPostfix]
    static void Postfix(HackableLockedCrate __instance)
    {
        if (RustcordMod.Instance == null) return;
        var cfg = RustcordConfig.Config;
        if (cfg?.PostSettings?.CrateDrops != true) return;

        var serverName = cfg?.General?.ServerName ?? "";
        var formatted = RustcordMod.FormatCrateDropped(serverName);
        RustcordMod.PostToDiscord(formatted, "log_cratedrop");
    }
}
