using System;
using HarmonyLib;
using UnityEngine;
using UL = Oxide.Plugins.UpLifted;

namespace UpLiftedHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || (!message.StartsWith("/") && !message.StartsWith("\\")))
                return true;
            var player = arg.Player();
            if (player == null) return true;
            try
            {
                if (UpLiftedMod.Instance != null && UpLiftedMod.Instance.OnChatCommand(player, message))
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] Chat.say: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;
            if (!string.Equals(a[0].ToString(), UpLiftedMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;
            var mod = UpLiftedMod.Instance;
            if (mod == null) return true;
            try { mod.HandleCuiEndtest(args, a); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] cui.endtest UPLIFTED: " + ex); }
            return false;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { UL.Dispatch_OnPlayerConnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.StartSleeping))]
    public static class BasePlayer_StartSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { UL.Dispatch_OnPlayerSleep(__instance); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnPlayerSleep: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { UL.Dispatch_OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnServerSave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseMelee), nameof(BaseMelee.DoAttackShared))]
    public static class BaseMelee_DoAttackShared_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMelee __instance, HitInfo info)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return;
            try { UL.Dispatch_OnMeleeAttack(player, info); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnMeleeAttack: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ConsoleSystem), nameof(ConsoleSystem.Run), new[] { typeof(ConsoleSystem.Option), typeof(string), typeof(object[]) })]
    public static class ConsoleSystem_Run_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Option options, string strCommand, object[] args)
        {
            if (string.IsNullOrEmpty(strCommand)) return true;
            bool interesting =
                strCommand.IndexOf("entid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                strCommand.IndexOf("sethome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                strCommand.IndexOf("home", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!interesting) return true;
            try
            {
                var arg = new ConsoleSystem.Arg(options, strCommand);
                if (UL.Dispatch_OnServerCommand(arg) != null) return false;
                if (UL.Dispatch_OnPlayerCommand(arg) != null) return false;
            }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] ConsoleSystem.Run: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Planner __instance, Construction.Target target, Construction component)
        {
            object r = UL.Dispatch_CanBuild(__instance, component, target);
            return r == null;
        }
    }

    [HarmonyPatch(typeof(Deployer), nameof(Deployer.DoDeploy_Slot))]
    public static class Deployer_DoDeploy_Slot_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Deployer __instance, Deployable deployable, Ray ray, NetworkableId entityID)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return true;
            object r = UL.Dispatch_CanDeployItem(player, __instance, entityID);
            return r == null;
        }
    }

    [HarmonyPatch(typeof(Door), "RPC_KnockDoor")]
    public static class Door_RPC_KnockDoor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Door __instance, BaseEntity.RPCMessage rpc)
        {
            try { UL.Dispatch_OnDoorKnocked(__instance, rpc.player); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnDoorKnocked: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            return UL.Dispatch_OnEntityTakeDamage(__instance, info) == null;
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            try { UL.Dispatch_OnEntityKill(__instance); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PressButton), nameof(PressButton.RPC_Press))]
    public static class PressButton_RPC_Press_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PressButton __instance, BaseEntity.RPCMessage msg)
        {
            try { UL.Dispatch_OnButtonPress(__instance, msg.player); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnButtonPress: " + ex.Message); }
        }
    }
}
