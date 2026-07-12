using HarmonyLib;
using UnityEngine;

namespace Rustcord.Patches;

/// <summary>Postfix on BaseCombatEntity.Die - PvP death.</summary>
[HarmonyPatch(typeof(BaseCombatEntity), "Die")]
internal class BaseCombatEntity_Die_Patch
{
    [HarmonyPostfix]
    static void Postfix(BaseCombatEntity __instance, HitInfo info)
    {
        if (RustcordMod.Instance == null) return;
        if (__instance == null) return;
        var cfg = RustcordConfig.Config;
        if (cfg?.PostSettings?.Deaths != true) return;

        var victim = __instance as BasePlayer;
        if (victim == null || victim.IsNpc) return;
        var killer = info?.InitiatorPlayer as BasePlayer;
        if (killer == null || killer.IsNpc) return;

        var serverName = cfg?.General?.ServerName ?? "";
        var formatted = RustcordMod.FormatDeath(serverName, killer.displayName ?? "?", victim.displayName ?? "?");
        RustcordMod.PostToDiscord(formatted, "death_pvp");
    }
}
