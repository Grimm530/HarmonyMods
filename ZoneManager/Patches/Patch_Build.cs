using HarmonyLib;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__result == null) return;
            try { ZM.Dispatch_OnEntityBuilt(__instance, __result.gameObject); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnEntityBuilt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.DoUpgradeToGrade))]
    public static class BuildingBlock_DoUpgradeToGrade_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingBlock __instance, BaseEntity.RPCMessage msg)
        {
            if (msg.player == null) return true;
            try
            {
                // Grade is read from the RPC stream by the original; we only cancel when NoUpgrade is set.
                // OnStructureUpgrade uses the player's current target grade via the hook args after the original
                // would parse them — cancel using the player's zone flag through the 3-arg Oxide signature
                // by probing with the block's current grade (plugin ignores the grade value besides identity).
                object result = ZM.Dispatch_OnStructureUpgrade(__instance, msg.player, __instance.grade);
                if (result != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnStructureUpgrade: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.UseItem))]
    public static class Item_UseItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Item __instance, int amountToConsume)
        {
            try { ZM.Dispatch_OnItemUse(__instance, amountToConsume); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnItemUse: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SprayCan), "CreateSpray")]
    public static class SprayCan_CreateSpray_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(SprayCan __instance, BaseEntity.RPCMessage msg)
        {
            try
            {
                Vector3 pos = __instance.transform.position;
                object result = ZM.Dispatch_OnSprayCreate(__instance, pos, __instance.transform.rotation);
                if (result != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnSprayCreate: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Door), "RPC_OpenDoor")]
    public static class Door_RPC_OpenDoor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Door __instance, BaseEntity.RPCMessage rpc)
        {
            try { ZM.Dispatch_OnDoorOpened(__instance, rpc.player); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnDoorOpened: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemCrafter), nameof(ItemCrafter.CanCraft), new[] { typeof(ItemBlueprint), typeof(int), typeof(bool) })]
    public static class ItemCrafter_CanCraft_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ItemCrafter __instance, ItemBlueprint bp, int amount, ref bool __result)
        {
            object result = ZM.Dispatch_CanCraft(__instance, bp, amount);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            if (result != null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
