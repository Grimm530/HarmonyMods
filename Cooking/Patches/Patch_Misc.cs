using HarmonyLib;
using Network;
using UnityEngine;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    [HarmonyPatch(typeof(Item), "LoseCondition", new[] { typeof(float) })]
    public static class Item_LoseCondition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item __instance, ref float amount)
        {
            try { CookingPlugin.Dispatch_OnLoseCondition(__instance, ref amount); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnLoseCondition: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseMountable), "MountPlayer", new[] { typeof(BasePlayer) })]
    public static class BaseMountable_MountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { CookingPlugin.Dispatch_OnEntityMounted(__instance, player); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnEntityMounted: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseMountable), "DismountPlayer", new[] { typeof(BasePlayer), typeof(bool) })]
    public static class BaseMountable_DismountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { CookingPlugin.Dispatch_OnEntityDismounted(__instance, player); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnEntityDismounted: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BuildingBlock), "PayForUpgrade", new[] { typeof(ConstructionGrade), typeof(BasePlayer) })]
    public static class BuildingBlock_PayForUpgrade_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingBlock __instance, ConstructionGrade g, BasePlayer player)
        {
            object r = CookingPlugin.Dispatch_OnPayForUpgrade(player, __instance, g);
            return r == null;
        }
    }

    [HarmonyPatch(typeof(RepairBench), nameof(RepairBench.RepairAnItem))]
    public static class RepairBench_RepairAnItem_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item itemToRepair, BasePlayer player)
        {
            if (player == null || itemToRepair == null) return;
            try { CookingPlugin.Dispatch_OnItemRepair(player, itemToRepair); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnItemRepair: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemCrafter), nameof(ItemCrafter.FinishCrafting))]
    public static class ItemCrafter_FinishCrafting_Patch
    {
        [HarmonyTranspiler]
        public static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(
            System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> instructions,
            System.Reflection.MethodBase originalMethod)
        {
            return YieldHookInjector.InjectAfterItemCreate(
                instructions,
                originalMethod,
                HarmonyLib.AccessTools.Method(typeof(ItemCrafter_FinishCrafting_Patch), nameof(Hook)),
                includePlayerArg: true);
        }

        public static void Hook(ItemCrafter crafter, ItemCraftTask task, Item item)
        {
            if (crafter == null || task == null || item == null) return;
            try { CookingPlugin.Dispatch_OnItemCraftFinished(task, item, crafter); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnItemCraftFinished: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) })]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (__instance == null) return;
            try { CookingPlugin.Dispatch_OnEntityKill(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseMelee), "DoAttackShared")]
    public static class BaseMelee_DoAttackShared_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMelee __instance, HitInfo info)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null || info == null) return;
            try { CookingPlugin.Dispatch_OnMeleeAttack(player, info); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnMeleeAttack: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.BecomeWounded))]
    public static class BasePlayer_BecomeWounded_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, HitInfo info)
        {
            object r = CookingPlugin.Dispatch_OnPlayerWound(__instance, info);
            return r == null;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.ShouldDropActiveItem))]
    public static class BasePlayer_ShouldDropActiveItem_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, ref bool __result)
        {
            object r = CookingPlugin.Dispatch_CanDropActiveItem(__instance);
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnHealthChanged))]
    public static class BasePlayer_OnHealthChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, float oldvalue, float newvalue)
        {
            try { CookingPlugin.Dispatch_OnPlayerHealthChange(__instance, oldvalue, newvalue); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnPlayerHealthChange: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ServerMgr), "OnPlayerVoice")]
    public static class ServerMgr_OnPlayerVoice_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Message packet)
        {
            try
            {
                var player = NetworkPacketEx.Player(packet);
                if (player == null) return true;
                object r = CookingPlugin.Dispatch_OnPlayerVoice(player, System.Array.Empty<byte>());
                if (r != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnPlayerVoice: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(ResearchTable), nameof(ResearchTable.ScrapForResearch), new[] { typeof(Item) })]
    public static class ResearchTable_ScrapForResearch_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item item, ref int __result)
        {
            object r = CookingPlugin.Dispatch_OnResearchCostDetermine(item);
            if (r is int i)
            {
                __result = i;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.VisibilityTest))]
    public static class BradleyAPC_VisibilityTest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BradleyAPC __instance, BaseEntity ent, ref bool __result)
        {
            if (ent is not BasePlayer player) return true;
            object r = CookingPlugin.Dispatch_CanBradleyApcTarget(__instance, player);
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TreeEntity), nameof(TreeEntity.DidHitMarker))]
    public static class TreeEntity_DidHitMarker_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(TreeEntity __instance, HitInfo info, ref bool __result)
        {
            object r = CookingPlugin.Dispatch_OnTreeMarkerHit(__instance, info);
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(NPCTalking), nameof(NPCTalking.Server_BeginTalking), new[] { typeof(BasePlayer) })]
    public static class NPCTalking_Server_BeginTalking_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(NPCTalking __instance, BasePlayer ply)
        {
            if (__instance is not NPCSimpleMissionProvider npc) return true;
            var conv = __instance.GetConversationFor(ply);
            object r = CookingPlugin.Dispatch_OnNpcConversationStart(npc, ply, conv);
            return r == null;
        }
    }

    [HarmonyPatch(typeof(BaseFishingRod), "CatchProcessBudgeted")]
    public static class BaseFishingRod_CatchProcessBudgeted_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseFishingRod __instance)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return;
            try { CookingPlugin.Dispatch_OnFishCatch(null, __instance, player); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnFishCatch: " + ex.Message); }
        }
    }
}
