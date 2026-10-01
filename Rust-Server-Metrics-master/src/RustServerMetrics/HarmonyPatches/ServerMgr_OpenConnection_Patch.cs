using System;
using HarmonyLib;
using UnityEngine;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.OpenConnection))]
    public class ServerMgr_OpenConnection_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Must not throw: Bootstrap.StartServer calls OpenConnection while Time.timeScale is 0
            // (pausewhileloading). An exception here aborts that coroutine before it restores
            // timeScale, which freezes Time.time and leaves the console at 0fps 0gc.
            try
            {
                SingletonComponent<MetricsLogger>.Instance?.OnServerStarted();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ServerMetrics]: OnServerStarted failed (startup will continue): " + ex);
            }
        }
    }
}
