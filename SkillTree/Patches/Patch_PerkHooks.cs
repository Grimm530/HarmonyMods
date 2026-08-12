// Missing perk hooks — Dispatch_* existed but had no Harmony callers (dead perks).
// Timing mirrors Oxide CallHook sites in Assembly-CSharp.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    internal static class CallHookReplace
    {
        /// <summary>Replace Interface.CallHook after ldstr <paramref name="hookName"/> with <paramref name="replacement"/>.</summary>
        public static List<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, string hookName, MethodInfo replacement)
        {
            var list = new List<CodeInstruction>(instructions);
            if (replacement == null) return list;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldstr || list[i].operand as string != hookName) continue;
                for (int j = i + 1; j < Math.Min(i + 20, list.Count); j++)
                {
                    if ((list[j].opcode == OpCodes.Call || list[j].opcode == OpCodes.Callvirt) &&
                        list[j].operand is MethodInfo mi && mi.Name == "CallHook")
                    {
                        list[j] = new CodeInstruction(OpCodes.Call, replacement).WithLabels(list[j].labels);
                        return list;
                    }
                }
                break;
            }
            Debug.LogWarning($"[SkillTree] CallHookReplace: did not find '{hookName}' — perk may stay dead until Rust IL is re-checked.");
            return list;
        }
    }

    // ---- Free_Bullet_Chance -----------------------------------------------

    [HarmonyPatch(typeof(BaseProjectile), "CLProject")]
    public static class BaseProjectile_CLProject_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnWeaponFired",
                AccessTools.Method(typeof(BaseProjectile_CLProject_Patch), nameof(CallHookShim)));

        // Oxide: CallHook("OnWeaponFired", projectile, player, mod, projectileShoot)
        public static object CallHookShim(string hook, object projectile, object player, object mod, object shoot)
        {
            try
            {
                STPlugin.Dispatch_OnWeaponFired(
                    projectile as BaseProjectile,
                    player as BasePlayer,
                    mod as ItemModProjectile,
                    shoot as ProtoBuf.ProjectileShoot);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponFired: " + ex.Message); }
            return null;
        }
    }

    // ---- Extended_Mag -----------------------------------------------------

    [HarmonyPatch(typeof(BaseProjectile), "StartReload")]
    public static class BaseProjectile_StartReload_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnWeaponReload",
                AccessTools.Method(typeof(BaseProjectile_StartReload_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object weapon, object player)
        {
            try { return STPlugin.Dispatch_OnWeaponReload(weapon as BaseProjectile, player as BasePlayer); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponReload: " + ex.Message); return null; }
        }
    }

    [HarmonyPatch(typeof(BaseProjectile), nameof(BaseProjectile.DelayedModsChanged))]
    public static class BaseProjectile_DelayedModsChanged_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnWeaponModChange",
                AccessTools.Method(typeof(BaseProjectile_DelayedModsChanged_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object weapon, object player)
        {
            try { return STPlugin.Dispatch_OnWeaponModChange(weapon as BaseProjectile, player as BasePlayer); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponModChange: " + ex.Message); return null; }
        }
    }

    // ---- Research_Refund --------------------------------------------------

    [HarmonyPatch(typeof(ResearchTable), nameof(ResearchTable.ScrapForResearch), new[] { typeof(Item) })]
    public static class ResearchTable_ScrapForResearch_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item item, ref int __result)
        {
            object r = null;
            try { r = STPlugin.Dispatch_OnResearchCostDetermine(item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnResearchCostDetermine: " + ex.Message); }
            if (r is int cost) { __result = cost; return false; }
            return true;
        }
    }

    // ---- Lock_Picker ------------------------------------------------------

    [HarmonyPatch(typeof(CodeLock), nameof(CodeLock.OnTryToOpen))]
    public static class CodeLock_OnTryToOpen_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(CodeLock __instance, BasePlayer player, ref bool __result)
        {
            object r = null;
            try { r = STPlugin.Dispatch_CanUseLockedEntity(player, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanUseLockedEntity: " + ex.Message); }
            if (r is bool b) { __result = b; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(CodeLock), nameof(CodeLock.OnTryToClose))]
    public static class CodeLock_OnTryToClose_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(CodeLock __instance, BasePlayer player, ref bool __result)
        {
            object r = null;
            try { r = STPlugin.Dispatch_CanUseLockedEntity(player, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanUseLockedEntity(Close): " + ex.Message); }
            if (r is bool b) { __result = b; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(KeyLock), nameof(KeyLock.OnTryToOpen))]
    public static class KeyLock_OnTryToOpen_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(KeyLock __instance, BasePlayer player, ref bool __result)
        {
            object r = null;
            try { r = STPlugin.Dispatch_CanUseLockedEntity(player, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanUseLockedEntity(Key): " + ex.Message); }
            if (r is bool b) { __result = b; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(KeyLock), nameof(KeyLock.OnTryToClose))]
    public static class KeyLock_OnTryToClose_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(KeyLock __instance, BasePlayer player, ref bool __result)
        {
            object r = null;
            try { r = STPlugin.Dispatch_CanUseLockedEntity(player, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanUseLockedEntity(KeyClose): " + ex.Message); }
            if (r is bool b) { __result = b; return false; }
            return true;
        }
    }

    // ---- Recycler_Speed / Efficiency --------------------------------------
    // SkillTree early-outs when recycler.IsOn(); safe to call on every toggle.

    [HarmonyPatch(typeof(Recycler), "SVSwitch")]
    public static class Recycler_SVSwitch_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Recycler __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return;
            try { STPlugin.Dispatch_OnRecyclerToggle(__instance, msg.player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnRecyclerToggle: " + ex.Message); }
        }
    }

    // ---- Fishing: Extra_Fish / Fishing_Luck / fish XP / tension reset -----

    [HarmonyPatch(typeof(BaseFishingRod), "CatchProcessBudgeted")]
    public static class BaseFishingRod_CatchProcessBudgeted_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "CanCatchFish",
                AccessTools.Method(typeof(BaseFishingRod_CatchProcessBudgeted_Patch), nameof(CanCatchShim)));

        public static object CanCatchShim(string hook, object player, object rod, object fish)
        {
            try
            {
                STPlugin.Dispatch_CanCatchFish(player as BasePlayer, rod as BaseFishingRod, fish as Item);
                STPlugin.Dispatch_OnFishCatch(fish as Item, rod as BaseFishingRod, player as BasePlayer);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] FishCatch: " + ex.Message); }
            return null;
        }
    }

    [HarmonyPatch(typeof(BaseFishingRod), "Server_Cancel", new[] { typeof(BaseFishingRod.FailReason) })]
    public static class BaseFishingRod_Server_Cancel_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnFishingStopped",
                AccessTools.Method(typeof(BaseFishingRod_Server_Cancel_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object rod, object reason)
        {
            try
            {
                if (rod is BaseFishingRod r && reason is BaseFishingRod.FailReason fr)
                    STPlugin.Dispatch_OnFishingStopped(r, fr);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnFishingStopped: " + ex.Message); }
            return null;
        }
    }

    // ---- Woodcutting_Hotspot ----------------------------------------------

    [HarmonyPatch(typeof(TreeEntity), nameof(TreeEntity.DidHitMarker))]
    public static class TreeEntity_DidHitMarker_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(TreeEntity __instance, HitInfo info, ref bool __result)
        {
            object r = null;
            try { r = STPlugin.Dispatch_OnTreeMarkerHit(__instance, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnTreeMarkerHit: " + ex.Message); }
            if (r is bool b) { __result = b; return false; }
            return true;
        }
    }

    // ---- Food/tea: Rationer, Iron_Stomach, Extra_Food_Water, Tea_*, etc. --

    [HarmonyPatch(typeof(ItemModConsume), nameof(ItemModConsume.DoAction))]
    public static class ItemModConsume_DoAction_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnPlayerAddModifiers",
                AccessTools.Method(typeof(ItemModConsume_DoAction_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object player, object item, object consumable)
        {
            try { return STPlugin.Dispatch_OnPlayerAddModifiers(player as BasePlayer, item as Item, consumable as ItemModConsumable); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerAddModifiers: " + ex.Message); return null; }
        }
    }

    // ---- Rocket_Velocity --------------------------------------------------

    [HarmonyPatch(typeof(BaseLauncher), "SV_Launch")]
    public static class BaseLauncher_SV_Launch_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnRocketLaunched",
                AccessTools.Method(typeof(BaseLauncher_SV_Launch_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object player, object entity)
        {
            try
            {
                if (player is BasePlayer p && entity is TimedExplosive t)
                    STPlugin.Dispatch_OnRocketLaunched(p, t);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnRocketLaunched: " + ex.Message); }
            return null;
        }
    }

    // ---- Dudless_Explosive ------------------------------------------------

    [HarmonyPatch(typeof(DudTimedExplosive), nameof(DudTimedExplosive.Explode))]
    public static class DudTimedExplosive_Explode_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnExplosiveDud",
                AccessTools.Method(typeof(DudTimedExplosive_Explode_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object dud)
        {
            try { return STPlugin.Dispatch_OnExplosiveDud(dud as DudTimedExplosive); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnExplosiveDud: " + ex.Message); return null; }
        }
    }

    // ---- Loot_Pickup magnet (bonus scrap) ---------------------------------

    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.DropBonusItems))]
    public static class LootContainer_DropBonusItems_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnBonusItemDropped",
                AccessTools.Method(typeof(LootContainer_DropBonusItems_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object item, object player, object container)
        {
            try { STPlugin.Dispatch_OnBonusItemDropped(item as Item, player as BasePlayer); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnBonusItemDropped: " + ex.Message); }
            return null;
        }
    }

    // ---- Metal detector dig XP --------------------------------------------

    [HarmonyPatch(typeof(BaseMetalDetector), "RPC_RequestFlag")]
    public static class BaseMetalDetector_RPC_RequestFlag_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnMetalDetectorFlagRequest",
                AccessTools.Method(typeof(BaseMetalDetector_RPC_RequestFlag_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object detector, object pos, object player)
        {
            try
            {
                if (detector is BaseMetalDetector d && pos is Vector3 v && player is BasePlayer p)
                    STPlugin.Dispatch_OnMetalDetectorFlagRequest(d, v, p);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMetalDetectorFlagRequest: " + ex.Message); }
            return null;
        }
    }

    // ---- Flyhack / Roadrunner ---------------------------------------------

    [HarmonyPatch(typeof(AntiHack), nameof(AntiHack.AddViolation))]
    public static class AntiHack_AddViolation_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer ply, AntiHackType type, float amount)
        {
            object r = null;
            try { r = STPlugin.Dispatch_OnPlayerViolation(ply, type, amount); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerViolation: " + ex.Message); }
            return r == null;
        }
    }

    // ---- Bear ultimate anti-target ----------------------------------------

    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.GetBestTarget))]
    public static class HumanNPC_GetBestTarget_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(HumanNPC __instance, ref BaseEntity __result)
        {
            if (__result is not BasePlayer player || __instance is not ScientistNPC npc) return;
            object r = null;
            try { r = STPlugin.Dispatch_OnNpcTarget(npc, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnNpcTarget: " + ex.Message); }
            if (r != null) __result = null;
        }
    }

    // ---- Build_Craft_Ultimate card bypass ---------------------------------

    [HarmonyPatch(typeof(CardReader), nameof(CardReader.ServerCardSwiped))]
    public static class CardReader_ServerCardSwiped_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            CallHookReplace.Replace(instructions, "OnCardSwipe",
                AccessTools.Method(typeof(CardReader_ServerCardSwiped_Patch), nameof(CallHookShim)));

        public static object CallHookShim(string hook, object reader, object card, object player)
        {
            try { return STPlugin.Dispatch_OnCardSwipe(reader as CardReader, card as Keycard, player as BasePlayer); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnCardSwipe: " + ex.Message); return null; }
        }
    }
}
