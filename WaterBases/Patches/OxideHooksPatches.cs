using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WaterBasesHarmony.Patches
{
    internal static class CallHookBridge
    {
        public static IEnumerable<CodeInstruction> ReplaceNamedHook(
            IEnumerable<CodeInstruction> instructions,
            string hookName,
            MethodInfo replacement)
        {
            var codes = new List<CodeInstruction>(instructions);
            if (replacement == null) return codes;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldstr || codes[i].operand is not string s || s != hookName)
                    continue;

                for (int j = i + 1; j < Math.Min(i + 20, codes.Count); j++)
                {
                    if ((codes[j].opcode == OpCodes.Call || codes[j].opcode == OpCodes.Callvirt) &&
                        codes[j].operand is MethodInfo mi && mi.Name == "CallHook")
                    {
                        codes[j] = new CodeInstruction(OpCodes.Call, replacement)
                        {
                            labels = codes[j].labels,
                            blocks = codes[j].blocks
                        };
                        break;
                    }
                }
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__instance == null || __result == null) return;
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnEntityBuilt(__instance, __result.gameObject); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnEntityBuilt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is not SimpleShark shark) return;
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnEntitySpawned(shark); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnEntitySpawned: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (__instance is not SimpleShark shark) return;
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnEntityKill(shark); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
    public static class BasePlayer_Die_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, HitInfo info)
        {
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnPlayerDeath(__instance, info); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnPlayerDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.UpdateActiveItem), typeof(ItemId))]
    public static class BasePlayer_UpdateActiveItem_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, out Item __state)
        {
            __state = __instance?.GetActiveItem();
        }

        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, Item __state)
        {
            if (__instance == null) return;
            var newItem = __instance.GetActiveItem();
            if (__state == newItem) return;
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnActiveItemChanged(__instance, __state, newItem); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnActiveItemChanged: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Insert), typeof(Item))]
    public static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result) return;
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnItemAddedToContainer(__instance, item); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnItemAddedToContainer: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Remove), typeof(Item))]
    public static class ItemContainer_Remove_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemContainer __instance, Item item)
        {
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnItemRemovedFromContainer(__instance, item); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnItemRemovedFromContainer: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseMelee), "DoAttackShared", new[] { typeof(HitInfo) })]
    public static class BaseMelee_DoAttackShared_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseMelee __instance, HitInfo info)
        {
            if (info == null) return true;
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return true;
            try
            {
                var result = WaterBasesMod.Instance?.Plugin?.HarmonyOnHammerHit(player, info);
                if (result != null) return false;
            }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnHammerHit: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.CanChangeToGrade))]
    public static class BuildingBlock_CanChangeToGrade_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingBlock __instance, BuildingGrade.Enum iGrade, ulong iSkin, BasePlayer player, ref bool __result)
        {
            if (__instance == null || player == null) return true;
            try
            {
                var result = WaterBasesMod.Instance?.Plugin?.HarmonyOnStructureUpgrade(__instance, player, iGrade);
                if (result != null)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnStructureUpgrade: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(RepairBench), nameof(RepairBench.ChangeSkin))]
    public static class RepairBench_ChangeSkin_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return CallHookBridge.ReplaceNamedHook(
                instructions,
                "OnItemSkinChange",
                AccessTools.Method(typeof(RepairBench_ChangeSkin_Patch), nameof(Hook)));
        }

        public static object Hook(string name, object[] args)
        {
            try
            {
                if (args != null && args.Length >= 4)
                {
                    return WaterBasesMod.Instance?.Plugin?.HarmonyOnItemSkinChange(
                        Convert.ToInt32(args[0]),
                        args[1] as Item,
                        args[2] as RepairBench,
                        args[3] as BasePlayer);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnItemSkinChange: " + ex.Message); }
            return null;
        }
    }

    [HarmonyPatch(typeof(Recycler), "RecycleThink")]
    public static class Recycler_RecycleThink_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return CallHookBridge.ReplaceNamedHook(
                instructions,
                "OnItemRecycle",
                AccessTools.Method(typeof(Recycler_RecycleThink_Patch), nameof(Hook)));
        }

        public static object Hook(string name, object[] args)
        {
            try
            {
                if (args != null && args.Length >= 2)
                {
                    return WaterBasesMod.Instance?.Plugin?.HarmonyOnItemRecycle(args[0] as Item, args[1] as Recycler);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnItemRecycle: " + ex.Message); }
            return null;
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), "GiveResourceFromItem")]
    public static class ResourceDispenser_GiveResourceFromItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResourceDispenser __instance, BasePlayer entity)
        {
            if (entity == null || __instance == null) return;
            try { WaterBasesMod.Instance?.Plugin?.HarmonyOnDispenserGather(__instance, entity, null); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] OnDispenserGather: " + ex.Message); }
        }
    }
}
