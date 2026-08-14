// Perk hooks — Prefix/Postfix at Oxide CallHook timing.
// This dedicated server has no Oxide CallHook strings in game IL, so transpilers never match.
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
        public static List<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, string hookName, MethodInfo replacement, bool warn = true)
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
            if (warn)
                Debug.LogWarning($"[SkillTree] CallHookReplace: did not find '{hookName}' — perk may stay dead until Rust IL is re-checked.");
            return list;
        }
    }

    // ---- Free_Bullet_Chance -----------------------------------------------

    [HarmonyPatch(typeof(BaseProjectile), "CLProject")]
    public static class BaseProjectile_CLProject_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseProjectile __instance, out int __state)
        {
            __state = __instance?.primaryMagazine?.contents ?? 0;
        }

        [HarmonyPostfix]
        public static void Postfix(BaseProjectile __instance, int __state)
        {
            if (__instance == null || __instance.primaryMagazine == null) return;
            if (__instance.primaryMagazine.contents >= __state) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try
            {
                var mod = __instance.primaryMagazine.ammoType?.GetComponent<ItemModProjectile>();
                STPlugin.Dispatch_OnWeaponFired(__instance, player, mod, null);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponFired: " + ex.Message); }
        }
    }

    // ---- Extended_Mag -----------------------------------------------------

    [HarmonyPatch(typeof(BaseProjectile), "StartReload")]
    public static class BaseProjectile_StartReload_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseProjectile __instance, BaseEntity.RPCMessage msg)
        {
            var player = msg.player;
            if (player == null) return true;
            try
            {
                if (STPlugin.Dispatch_OnWeaponReload(__instance, player) != null)
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponReload: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseProjectile), nameof(BaseProjectile.DelayedModsChanged))]
    public static class BaseProjectile_DelayedModsChanged_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseProjectile __instance)
        {
            try
            {
                if (STPlugin.Dispatch_OnWeaponModChange(__instance, __instance.GetOwnerPlayer()) != null)
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponModChange: " + ex.Message); }
            return true;
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

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.GiveItem), new[] { typeof(Item), typeof(BaseEntity.GiveItemReason), typeof(GiveItemOptions) })]
    public static class BasePlayer_GiveItem_Fish_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, Item item, BaseEntity.GiveItemReason reason)
        {
            if (reason != BaseEntity.GiveItemReason.Crafted || item == null || __instance == null) return;
            var rod = __instance.GetHeldEntity() as BaseFishingRod;
            if (rod == null || rod.CurrentState != BaseFishingRod.CatchState.Caught) return;
            try
            {
                STPlugin.Dispatch_CanCatchFish(__instance, rod, item);
                STPlugin.Dispatch_OnFishCatch(item, rod, __instance);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] FishCatch: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseFishingRod), "Server_Cancel", new[] { typeof(BaseFishingRod.FailReason) })]
    public static class BaseFishingRod_Server_Cancel_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseFishingRod __instance, BaseFishingRod.FailReason reason)
        {
            try { STPlugin.Dispatch_OnFishingStopped(__instance, reason); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnFishingStopped: " + ex.Message); }
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
        [HarmonyPostfix]
        public static void Postfix(ItemModConsume __instance, Item item, BasePlayer player)
        {
            if (player == null || item == null || __instance == null) return;
            try { STPlugin.Dispatch_OnPlayerAddModifiers(player, item, __instance.GetConsumable()); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerAddModifiers: " + ex.Message); }
        }
    }

    // ---- Rocket_Velocity --------------------------------------------------

    [HarmonyPatch(typeof(BaseLauncher), nameof(BaseLauncher.ProjectileLaunched_Server))]
    public static class BaseLauncher_ProjectileLaunched_Server_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseLauncher __instance, ServerProjectile justLaunched)
        {
            var player = __instance?.GetOwnerPlayer();
            var explosive = justLaunched?.baseEntity as TimedExplosive;
            if (player == null || explosive == null) return;
            try { STPlugin.Dispatch_OnRocketLaunched(player, explosive); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnRocketLaunched: " + ex.Message); }
        }
    }

    // ---- Dudless_Explosive ------------------------------------------------

    [HarmonyPatch(typeof(DudTimedExplosive), nameof(DudTimedExplosive.Explode))]
    public static class DudTimedExplosive_Explode_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(DudTimedExplosive __instance, out float __state)
        {
            __state = -1f;
            if (__instance == null) return;
            if (__instance.creatorEntity != null && __instance.creatorEntity.IsNpc) return;
            try
            {
                if (STPlugin.Dispatch_OnExplosiveDud(__instance) == null) return;
                __state = __instance.dudChance;
                __instance.dudChance = 0f;
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnExplosiveDud: " + ex.Message); }
        }

        [HarmonyPostfix]
        public static void Postfix(DudTimedExplosive __instance, float __state)
        {
            if (__state >= 0f && __instance != null)
                __instance.dudChance = __state;
        }
    }

    // ---- Loot_Pickup magnet (bonus scrap) ---------------------------------

    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.DropBonusItems))]
    public static class LootContainer_DropBonusItems_Patch
    {
        [ThreadStatic] internal static BasePlayer BonusPlayer;

        [HarmonyPrefix]
        public static void Prefix(BaseEntity initiator)
        {
            BonusPlayer = initiator as BasePlayer;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BonusPlayer = null;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.Drop), new[] { typeof(Vector3), typeof(Vector3), typeof(Quaternion) })]
    public static class Item_Drop_Bonus_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Item __instance)
        {
            var player = LootContainer_DropBonusItems_Patch.BonusPlayer;
            if (player == null || __instance == null) return;
            try { STPlugin.Dispatch_OnBonusItemDropped(__instance, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnBonusItemDropped: " + ex.Message); }
        }
    }

    // ---- Metal detector dig XP --------------------------------------------

    [HarmonyPatch(typeof(BaseMetalDetector), "RPC_RequestFlag")]
    public static class BaseMetalDetector_RPC_RequestFlag_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseMetalDetector __instance, BaseEntity.RPCMessage rpc)
        {
            var player = rpc.player;
            if (__instance == null || player == null) return;
            try { STPlugin.Dispatch_OnMetalDetectorFlagRequest(__instance, __instance.GetDetectionPoint(), player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMetalDetectorFlagRequest: " + ex.Message); }
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
        [HarmonyPrefix]
        public static bool Prefix(CardReader __instance, BaseEntity.RPCMessage msg)
        {
            var player = msg.player;
            if (player == null || __instance == null) return true;
            var card = player.GetHeldEntity() as Keycard;
            if (card == null) return true;
            try
            {
                if (STPlugin.Dispatch_OnCardSwipe(__instance, card, player) != null)
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnCardSwipe: " + ex.Message); }
            return true;
        }
    }
}
