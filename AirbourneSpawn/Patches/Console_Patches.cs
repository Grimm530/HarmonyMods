using ConVar;
using HarmonyLib;
using ProtoBuf;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    internal static class Hooks
    {
        internal static AirbourneSpawnPlugin Plugin => AirbourneSpawnMod.Instance?.Plugin;

        internal static void Warn(string hook, System.Exception ex) =>
            Debug.LogWarning("[AirbourneSpawn] " + hook + ": " + ex.Message);
    }

    [HarmonyPatch(typeof(Global), nameof(Global.kill))]
    internal static class Global_kill_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg args)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null) return true;
            try
            {
                BasePlayer player = ArgEx.Player(args);
                if (plugin.TryHandleKill(player))
                    return false;
            }
            catch (System.Exception ex) { Hooks.Warn("kill", ex); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Global), nameof(Global.respawn))]
    internal static class Global_respawn_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg args)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null) return true;
            try
            {
                BasePlayer player = ArgEx.Player(args);
                if (plugin.TryHandleRespawn(player))
                    return false;
            }
            catch (System.Exception ex) { Hooks.Warn("respawn", ex); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Global), nameof(Global.respawn_sleepingbag))]
    internal static class Global_respawn_sleepingbag_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg args)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null) return true;
            try
            {
                BasePlayer player = ArgEx.Player(args);
                NetworkableId id = ArgEx.GetEntityID(args, 0);
                if (plugin.TryHandleRespawnBag(player, id))
                    return false;
            }
            catch (System.Exception ex) { Hooks.Warn("respawn_sleepingbag", ex); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Global), nameof(Global.respawn_sleepingbag_remove))]
    internal static class Global_respawn_sleepingbag_remove_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg args)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null) return true;
            try
            {
                BasePlayer player = ArgEx.Player(args);
                NetworkableId id = ArgEx.GetEntityID(args, 0);
                if (plugin.TryBlockRemoveBag(player, id))
                    return false;
            }
            catch (System.Exception ex) { Hooks.Warn("respawn_sleepingbag_remove", ex); }
            return true;
        }
    }
}
