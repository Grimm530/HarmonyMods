using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(CodeLock), nameof(CodeLock.OnTryToOpen))]
    internal static class CodeLock_OnTryToOpen_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(CodeLock __instance, BasePlayer player, ref bool __result)
        {
            return HandleUse(__instance, player, ref __result);
        }

        internal static bool HandleUse(BaseLock baseLock, BasePlayer player, ref bool __result)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                object result = plugin.CanUseLockedEntity(player, baseLock);
                if (result is bool b)
                {
                    __result = b;
                    return false;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] CanUseLockedEntity: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(CodeLock), nameof(CodeLock.OnTryToClose))]
    internal static class CodeLock_OnTryToClose_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(CodeLock __instance, BasePlayer player, ref bool __result)
        {
            return CodeLock_OnTryToOpen_Patch.HandleUse(__instance, player, ref __result);
        }
    }

    [HarmonyPatch(typeof(KeyLock), nameof(KeyLock.OnTryToOpen))]
    internal static class KeyLock_OnTryToOpen_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(KeyLock __instance, BasePlayer player, ref bool __result)
        {
            return CodeLock_OnTryToOpen_Patch.HandleUse(__instance, player, ref __result);
        }
    }

    [HarmonyPatch(typeof(KeyLock), nameof(KeyLock.OnTryToClose))]
    internal static class KeyLock_OnTryToClose_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(KeyLock __instance, BasePlayer player, ref bool __result)
        {
            return CodeLock_OnTryToOpen_Patch.HandleUse(__instance, player, ref __result);
        }
    }

    [HarmonyPatch(typeof(CodeLock), "TryUnlock")]
    internal static class CodeLock_TryUnlock_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(CodeLock __instance, BaseEntity.RPCMessage rpc)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                if (plugin.CanUnlock(rpc.player, __instance) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] CanUnlock(CodeLock): " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(CodeLock), "TryLock")]
    internal static class CodeLock_TryLock_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(CodeLock __instance, BaseEntity.RPCMessage rpc)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                if (plugin.CanLock(rpc.player, __instance) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] CanLock(CodeLock): " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(CodeLock), "RPC_ChangeCode")]
    internal static class CodeLock_RPC_ChangeCode_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(CodeLock __instance, BaseEntity.RPCMessage rpc)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.CanChangeCode(rpc.player, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] CanChangeCode: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(KeyLock), "RPC_Unlock")]
    internal static class KeyLock_RPC_Unlock_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(KeyLock __instance, BaseEntity.RPCMessage rpc)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                if (plugin.CanUnlock(rpc.player, __instance) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] CanUnlock(KeyLock): " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(KeyLock), "Lock", typeof(BasePlayer))]
    internal static class KeyLock_Lock_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(KeyLock __instance, BasePlayer player)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                if (plugin.CanLock(player, __instance) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] CanLock(KeyLock): " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(CodeLock), nameof(CodeLock.GetPlayerLockPermission))]
    internal static class CodeLock_GetPlayerLockPermission_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(CodeLock __instance, BasePlayer player, ref bool __result)
        {
            return CodeLock_OnTryToOpen_Patch.HandleUse(__instance, player, ref __result);
        }
    }
}
