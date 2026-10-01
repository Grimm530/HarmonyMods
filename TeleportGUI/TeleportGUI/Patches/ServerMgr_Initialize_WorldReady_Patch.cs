using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TeleportGUI.Patches
{
    /// <summary>
    /// ServerMgr.Initialize postfix — monument prefabs exist after the world is ready.
    /// Harmony mods load BeforeSceneLoad, so Outpost/Bandit warps are generated here (and via the wait coroutine).
    /// </summary>
    [HarmonyPatch]
    internal static class ServerMgr_Initialize_WorldReady_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var found = AccessTools.GetDeclaredMethods(typeof(ServerMgr));
            if (found == null) yield break;
            foreach (var m in found)
            {
                if (m != null && m.Name == nameof(ServerMgr.Initialize))
                    yield return m;
            }
        }

        static bool Prepare()
        {
            foreach (var _ in TargetMethods())
                return true;
            return false;
        }

        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                TeleportGUIMod.Instance?.NotifyWorldReady();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] NotifyWorldReady: " + ex.Message);
            }
        }
    }
}
