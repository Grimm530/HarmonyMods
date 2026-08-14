using HarmonyLib;

namespace RustLeagueHarmony.Patches
{
    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.AttemptDismount))]
    internal static class BaseMountable_AttemptDismount_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseMountable __instance, BasePlayer player)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || __instance == null || player == null) return true;
            return !plugin.TryBlockDismount(player, __instance);
        }
    }

    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.RPC_WantsDismount))]
    internal static class BaseMountable_RPC_WantsDismount_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseMountable __instance, BaseEntity.RPCMessage msg)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return true;
            var player = msg.player;
            if (player == null) return true;
            if (plugin.TryHandleDismountFailed(player, __instance))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(ModularCarSeat), nameof(ModularCarSeat.CanSwapToThis))]
    internal static class ModularCarSeat_CanSwapToThis_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ModularCarSeat __instance, BasePlayer player, ref bool __result)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || __instance == null || player == null) return true;
            if (!plugin.TryBlockSeatSwap(player, __instance)) return true;
            __result = false;
            return false;
        }
    }
}
