using HarmonyLib;
using UnityEngine;
using VQ = Oxide.Plugins.VirtualQuarries;

namespace VirtualQuarriesHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { VQ.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string strFilename, bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = System.AppDomain.CurrentDomain.GetData("VirtualQuarries_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                System.AppDomain.CurrentDomain.SetData("VirtualQuarries_LastWipeId", wipeId);
                if (prev == null) return;
                VQ.Dispatch_OnNewSave();
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnNewSave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.AssignFinishBonus))]
    public static class ResourceDispenser_AssignFinishBonus_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResourceDispenser __instance, BasePlayer player, float fraction)
        {
            // Best-effort: bonus item is already given; plugin records dispenser usage by player.
            try { VQ.Dispatch_OnDispenserBonus(__instance, player, null); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnDispenserBonus: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ThrownWeapon), "SetUpThrownWeapon")]
    public static class ThrownWeapon_SetUpThrownWeapon_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, BaseEntity ent)
        {
            if (__instance == null || ent == null) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try { VQ.Dispatch_OnExplosiveThrown(player, ent, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnExplosiveThrown: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = VQ.Dispatch_OnEntityTakeDamage(__instance, info);
            return result == null;
        }
    }

    [HarmonyPatch(typeof(EngineSwitch), nameof(EngineSwitch.StartEngine))]
    public static class EngineSwitch_StartEngine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(EngineSwitch __instance, BaseEntity.RPCMessage msg)
        {
            var quarry = __instance?.GetParentEntity() as MiningQuarry;
            if (quarry == null) return;
            try { VQ.Dispatch_OnQuarryToggled(quarry, msg.player); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnQuarryToggled start: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(EngineSwitch), nameof(EngineSwitch.StopEngine))]
    public static class EngineSwitch_StopEngine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(EngineSwitch __instance, BaseEntity.RPCMessage msg)
        {
            var quarry = __instance?.GetParentEntity() as MiningQuarry;
            if (quarry == null) return;
            try { VQ.Dispatch_OnQuarryToggled(quarry, msg.player); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnQuarryToggled stop: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ExcavatorArm), nameof(ExcavatorArm.RPC_SetResourceTarget))]
    public static class ExcavatorArm_RPC_SetResourceTarget_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ExcavatorArm __instance, BaseEntity.RPCMessage msg)
        {
            string type = null;
            try { type = msg.read.String(); }
            catch { return true; }
            object result = VQ.Dispatch_OnExcavatorResourceSet(__instance, type, msg.player);
            return result == null;
        }
    }

    [HarmonyPatch(typeof(ExcavatorSignalComputer), nameof(ExcavatorSignalComputer.RequestSupplies))]
    public static class ExcavatorSignalComputer_RequestSupplies_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ExcavatorSignalComputer __instance, BaseEntity.RPCMessage rpc)
        {
            object result = VQ.Dispatch_OnExcavatorSuppliesRequest(__instance, rpc.player);
            return result == null;
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            if (__instance == null || targetEntity == null) return true;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return true;
            if (targetEntity is StorageContainer storage)
            {
                object blocked = VQ.Dispatch_CanLootEntity(player, storage);
                if (blocked != null) { __result = false; return false; }
            }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || __instance == null || targetEntity is not BoxStorage box) return;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return;
            try { VQ.Dispatch_OnLootEntity(player, box); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnLootEntity: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerLoot __instance)
        {
            BasePlayer player = __instance?.baseEntity;
            if (player == null) return;
            if (__instance.entitySource is BoxStorage box)
            {
                try { VQ.Dispatch_OnLootEntityEnd(player, box); }
                catch (System.Exception ex) { Debug.LogWarning("[VirtualQuarries] OnLootEntityEnd: " + ex.Message); }
            }
        }
    }
}
