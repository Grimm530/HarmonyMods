using System;
using HarmonyLib;
using UnityEngine;

namespace RustVehiclesHarmony.Patches
{
    internal static class Hooks
    {
        internal static RustVehicles Plugin => RustVehiclesHarmonyMod.Instance?.Plugin;

        internal static void Warn(string hook, Exception ex) =>
            Debug.LogWarning("[RustVehicles] " + hook + ": " + ex.Message);
    }

    // ---------------------------------------------------------------- always-on lifecycle

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    internal static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnEntityDeath(__instance, info); }
            catch (Exception ex) { Hooks.Warn("OnEntityDeath", ex); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            if (__instance is not BaseCombatEntity entity) return;
            try { plugin.OnEntityKill(entity); }
            catch (Exception ex) { Hooks.Warn("OnEntityKill", ex); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (Exception ex) { Hooks.Warn("OnPlayerDisconnected", ex); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(bool))]
    internal static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            var plugin = Hooks.Plugin;
            if (plugin == null) return;
            try { plugin.OnServerSave(); }
            catch (Exception ex) { Hooks.Warn("OnServerSave", ex); }
        }
    }

    /// <summary>Oxide OnNewSave — fire when WipeId changes after a successful Load.</summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    internal static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = AppDomain.CurrentDomain.GetData("RustVehicles_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("RustVehicles_LastWipeId", wipeId);
                if (prev == null) return;

                var plugin = Hooks.Plugin;
                if (plugin == null) return;
                plugin.OnNewSave();
                Debug.Log("[RustVehicles] OK: Wipe detected — OnNewSave.");
            }
            catch (Exception ex) { Hooks.Warn("OnNewSave", ex); }
        }
    }

    // ---------------------------------------------------------------- always-on vehicle helpers

    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.SetTarget))]
    internal static class AutoTurret_SetTarget_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(AutoTurret __instance, ref BaseCombatEntity targ)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null || targ == null) return;
            try
            {
                if (plugin.OnTurretTarget(__instance, targ) != null)
                    targ = null;
            }
            catch (Exception ex) { Hooks.Warn("OnTurretTarget", ex); }
        }
    }

    [HarmonyPatch(typeof(ElectricSwitch), "RPC_Switch", new[] { typeof(BaseEntity.RPCMessage) })]
    internal static class ElectricSwitch_RPC_Switch_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ElectricSwitch __instance, BaseEntity.RPCMessage msg)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnSwitchToggled(__instance, msg.player); }
            catch (Exception ex) { Hooks.Warn("OnSwitchToggled", ex); }
        }
    }

    /// <summary>Oxide OnServerCommand for inventory.lighttoggle (minicopter search light).</summary>
    [HarmonyPatch(typeof(ConsoleSystem), nameof(ConsoleSystem.Run), new[] { typeof(ConsoleSystem.Option), typeof(string), typeof(object[]) })]
    internal static class ConsoleSystem_Run_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Option options, string strCommand, object[] args)
        {
            if (string.IsNullOrEmpty(strCommand)) return true;
            if (!strCommand.Equals("inventory.lighttoggle", StringComparison.OrdinalIgnoreCase) &&
                !strCommand.Equals("global.inventory.lighttoggle", StringComparison.OrdinalIgnoreCase))
                return true;

            var plugin = Hooks.Plugin;
            if (plugin == null) return true;
            if (options.Connection == null) return true;

            try
            {
                var realArg = new ConsoleSystem.Arg(options, "inventory.lighttoggle");
                if (plugin.OnServerCommand(realArg) != null)
                    return false;
            }
            catch (Exception ex) { Hooks.Warn("OnServerCommand", ex); }
            return true;
        }
    }

    /// <summary>Oxide OnEntityReskin — block reskinning licensed vehicles.</summary>
    [HarmonyPatch(typeof(SprayCan), nameof(SprayCan.ValidateReskin))]
    internal static class SprayCan_ValidateReskin_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BasePlayer player, BaseEntity targetEnt, int targetSkin, ref bool __result)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || targetEnt == null || player == null) return true;
            try
            {
                ItemSkinDirectory.Skin skin = default;
                skin.id = targetSkin;
                if (plugin.OnEntityReskin(targetEnt, skin, player) != null)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("OnEntityReskin", ex); }
            return true;
        }
    }

    // ---------------------------------------------------------------- subscribed hooks

    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.AttemptMount), new[] { typeof(BasePlayer), typeof(bool) })]
    internal static class BaseMountable_AttemptMount_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseMountable __instance, BasePlayer player)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("CanMountEntity")) return true;
            if (__instance == null || player == null) return true;
            try
            {
                if (plugin.CanMountEntity(player, __instance) != null)
                    return false;
            }
            catch (Exception ex) { Hooks.Warn("CanMountEntity", ex); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    internal static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntityTakeDamage")) return;
            if (__instance == null || info == null) return;
            try { plugin.OnEntityTakeDamage(__instance, info); }
            catch (Exception ex) { Hooks.Warn("OnEntityTakeDamage", ex); }
        }
    }

    [HarmonyPatch(typeof(TriggerHurtNotChild), nameof(TriggerHurtNotChild.OnEntityEnter), new[] { typeof(BaseEntity) })]
    internal static class TriggerHurtNotChild_OnEntityEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TriggerHurtNotChild __instance, BaseEntity ent)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntityEnter")) return true;
            if (__instance == null || ent is not BasePlayer player) return true;
            try
            {
                if (plugin.OnEntityEnter(__instance, player) != null)
                    return false;
            }
            catch (Exception ex) { Hooks.Warn("OnEntityEnter(TriggerHurtNotChild)", ex); }
            return true;
        }
    }

    /// <summary>
    /// TriggerHurt does not override OnEntityEnter — it uses TriggerBase.OnEntityEnter.
    /// Narrow to TriggerHurt only so we do not compete with every TriggerBase consumer.
    /// </summary>
    [HarmonyPatch(typeof(TriggerBase), nameof(TriggerBase.OnEntityEnter), new[] { typeof(BaseEntity) })]
    internal static class TriggerHurt_OnEntityEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TriggerBase __instance, BaseEntity ent)
        {
            if (__instance is not TriggerHurt triggerHurt) return true;
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntityEnter")) return true;
            if (ent is not BasePlayer player) return true;
            try
            {
                if (plugin.OnEntityEnter(triggerHurt, player) != null)
                    return false;
            }
            catch (Exception ex) { Hooks.Warn("OnEntityEnter(TriggerHurt)", ex); }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    internal static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("CanLootEntity")) return true;
            if (__instance == null || targetEntity == null) return true;
            var player = __instance.GetComponent<BasePlayer>() ?? __instance.entitySource as BasePlayer;
            // PlayerLoot lives on the player
            try
            {
                player ??= __instance.baseEntity as BasePlayer;
            }
            catch { }
            if (player == null)
            {
                try
                {
                    foreach (var p in BasePlayer.activePlayerList)
                    {
                        if (p != null && p.inventory?.loot == __instance) { player = p; break; }
                    }
                }
                catch { }
            }
            if (player == null) return true;

            try
            {
                object result = null;
                if (targetEntity is RidableHorse horse)
                    result = plugin.CanLootEntity(player, horse);
                else if (targetEntity is StorageContainer container)
                    result = plugin.CanLootEntity(player, container);

                if (result != null)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanLootEntity", ex); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntitySpawned")) return;
            if (__instance == null) return;
            try
            {
                switch (__instance)
                {
                    case Tugboat tug: plugin.OnEntitySpawned(tug); break;
                    case BaseSubmarine sub: plugin.OnEntitySpawned(sub); break;
                    case MotorRowboat row: plugin.OnEntitySpawned(row); break;
                    case Minicopter mini: plugin.OnEntitySpawned(mini); break;
                    case AttackHelicopter atk: plugin.OnEntitySpawned(atk); break;
                }
            }
            catch (Exception ex) { Hooks.Warn("OnEntitySpawned", ex); }
        }
    }

    [HarmonyPatch(typeof(RidableHorse), nameof(RidableHorse.SERVER_Claim))]
    internal static class RidableHorse_SERVER_Claim_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(RidableHorse __instance, BaseEntity.RPCMessage msg)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnRidableAnimalClaimed")) return;
            if (__instance == null || msg.player == null) return;
            try { plugin.OnRidableAnimalClaimed(__instance, msg.player); }
            catch (Exception ex) { Hooks.Warn("OnRidableAnimalClaimed", ex); }
        }
    }

    [HarmonyPatch(typeof(BaseMountable), "DismountPlayer", new[] { typeof(BasePlayer), typeof(bool) })]
    internal static class BaseMountable_DismountPlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntityDismounted")) return;
            if (__instance == null) return;
            try { plugin.OnEntityDismounted(__instance, player); }
            catch (Exception ex) { Hooks.Warn("OnEntityDismounted", ex); }
        }
    }

    [HarmonyPatch(typeof(PlayerHelicopter), nameof(PlayerHelicopter.TryStartEngine))]
    internal static class PlayerHelicopter_TryStartEngine_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerHelicopter __instance, BasePlayer player)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEngineStarted")) return;
            if (__instance == null) return;
            try { plugin.OnEngineStarted(__instance, player); }
            catch (Exception ex) { Hooks.Warn("OnEngineStarted", ex); }
        }
    }

    [HarmonyPatch(typeof(BaseVehicle), nameof(BaseVehicle.DoPushAction))]
    internal static class BaseVehicle_DoPushAction_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseVehicle __instance, BasePlayer player)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnVehiclePush")) return true;
            if (__instance == null || player == null) return true;
            try
            {
                if (plugin.OnVehiclePush(__instance, player) != null)
                    return false;
            }
            catch (Exception ex) { Hooks.Warn("OnVehiclePush", ex); }
            return true;
        }
    }
}
