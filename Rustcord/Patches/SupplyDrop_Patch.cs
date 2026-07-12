using HarmonyLib;
using UnityEngine;

namespace Rustcord.Patches;

/// <summary>Supply drop landed - OnCollisionEnter when hits terrain.</summary>
[HarmonyPatch(typeof(SupplyDrop), "OnCollisionEnter")]
internal class SupplyDrop_OnCollisionEnter_Patch
{
    [HarmonyPostfix]
    static void Postfix(SupplyDrop __instance, Collision collision)
    {
        if (collision == null) return;
        // Only when landing on terrain/tugboat (same check as original)
        var flag = ((1 << collision.collider.gameObject.layer) & 0x40A10111) > 0
            || (((1 << collision.collider.gameObject.layer) & 0x8000000) > 0 && collision.GetEntity() is Tugboat);
        if (!flag) return;

        if (RustcordMod.Instance == null) return;
        var cfg = RustcordConfig.Config;
        if (cfg?.PostSettings?.CrateDrops != true) return;

        var serverName = cfg?.General?.ServerName ?? "";
        var formatted = RustcordMod.FormatSupplyDrop(serverName);
        RustcordMod.PostToDiscord(formatted, "log_supplydrop");
    }
}
