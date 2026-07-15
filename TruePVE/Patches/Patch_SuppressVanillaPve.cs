// Keep ConVar.Server.pve = true for Steam/browser listing, but skip vanilla's early
// PvP / building reflect so TruePVE RuleSets remain the damage authority.
using ConVar;
using HarmonyLib;
using UnityEngine;
using TPVE = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BasePlayer_Hurt_SuppressVanillaPve
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix(ref bool __state)
        {
            __state = false;
            if (!TPVE.ShouldSuppressVanillaPve()) return;
            if (!Server.pve) return;
            Server.pve = false;
            __state = true;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(bool __state)
        {
            if (__state) Server.pve = true;
        }
    }

    [HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BuildingBlock_Hurt_SuppressVanillaPve
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix(ref bool __state)
        {
            __state = false;
            if (!TPVE.ShouldSuppressVanillaPve()) return;
            if (!Server.pve) return;
            Server.pve = false;
            __state = true;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(bool __state)
        {
            if (__state) Server.pve = true;
        }
    }
}
