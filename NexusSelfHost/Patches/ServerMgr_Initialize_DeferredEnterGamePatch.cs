using System;
using System.Reflection;
using HarmonyLib;
using NexusSelfHost;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// After <c>ServerMgr.Initialize</c>, patches <c>BasePlayer</c> methods that must not be JIT'd during
    /// <c>BeforeSceneLoad</c> (can run <c>FileStorage</c> static ctor and fail with Sqlite error 14).
    /// See HARMONY_MODS_GUIDE.md (FileStorage / troubleshooting).
    /// </summary>
    [HarmonyPatch]
    internal static class ServerMgr_Initialize_DeferredEnterGamePatch
    {
        private const string HarmonyId = "com.facepunch.rust_dedicated.NexusSelfHost";
        private static bool _applied;
        private static readonly object ApplyLock = new object();

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ServerMgr");
            return t != null ? AccessTools.Method(t, "Initialize") : null;
        }

        static void Postfix()
        {
            EnsureApplied("ServerMgr.Initialize postfix");
        }

        internal static void TryApplyImmediatelyIfServerReady()
        {
            if (_applied)
                return;

            if (!IsServerReadyForDeferredBasePlayerPatches())
                return;

            EnsureApplied("runtime harmony.load");
        }

        private static void EnsureApplied(string reason)
        {
            if (_applied)
                return;

            lock (ApplyLock)
            {
                if (_applied)
                    return;

                ApplyDeferredBasePlayerPatches(reason);
                _applied = true;
            }
        }

        private static void ApplyDeferredBasePlayerPatches(string reason)
        {
            var bpType = AccessTools.TypeByName("BasePlayer");
            if (bpType == null)
            {
                Debug.LogWarning("[NexusSelfHost] Deferred BasePlayer patches: BasePlayer type not found.");
                return;
            }

            try
            {
                var harmony = new Harmony(HarmonyId);

                var playerInit = AccessTools.Method(bpType, "PlayerInit");
                if (playerInit != null)
                {
                    harmony.Patch(
                        playerInit,
                        prefix: new HarmonyMethod(typeof(BasePlayer_PlayerInit_InvalidatePersistCachePatch), nameof(BasePlayer_PlayerInit_InvalidatePersistCachePatch.Prefix)),
                        postfix: new HarmonyMethod(typeof(BasePlayer_PlayerInit_InvalidatePersistCachePatch), nameof(BasePlayer_PlayerInit_InvalidatePersistCachePatch.Postfix)));
                    Debug.Log("[NexusSelfHost] PlayerInit persist invalidate + blueprint snapshot resync applied via " + reason +
                              " (opt out: NEXUS_SKIP_PERSIST_CACHE_INVALIDATE=1 / NEXUS_SKIP_BLUEPRINT_SNAPSHOT_RESYNC=1).");
                }
                else
                    Debug.LogWarning("[NexusSelfHost] Deferred patch: BasePlayer.PlayerInit not found.");

                var enterGame = AccessTools.Method(bpType, "EnterGame");
                if (enterGame != null)
                {
                    harmony.Patch(
                        enterGame,
                        prefix: new HarmonyMethod(typeof(BasePlayer_EnterGame_NexusBlueprintLog_Patch), nameof(BasePlayer_EnterGame_NexusBlueprintLog_Patch.Prefix)),
                        postfix: new HarmonyMethod(typeof(BasePlayer_EnterGame_NexusBlueprintLog_Patch), nameof(BasePlayer_EnterGame_NexusBlueprintLog_Patch.Postfix)));
                    Debug.Log("[NexusSelfHost] EnterGame patch applied via " + reason + ": blueprint log + transferred-spawn rescue.");
                }
                else
                    Debug.LogWarning("[NexusSelfHost] Deferred EnterGame patch: BasePlayer.EnterGame not found.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[NexusSelfHost] Deferred BasePlayer patches failed: " + ex);
            }
        }

        private static bool IsServerReadyForDeferredBasePlayerPatches()
        {
            var serverMgrType = AccessTools.TypeByName("ServerMgr");
            if (serverMgrType == null)
                return false;

            var serverMgr = UnityEngine.Object.FindObjectOfType(serverMgrType);
            if (serverMgr == null)
                return false;

            var persistence = AccessTools.Field(serverMgrType, "persistance")?.GetValue(serverMgr);
            if (persistence != null)
                return true;

            var runFrameUpdate = AccessTools.Property(serverMgrType, "runFrameUpdate")?.GetValue(serverMgr);
            return runFrameUpdate is bool b && b;
        }
    }
}
