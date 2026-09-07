using HarmonyLib;
using NexusSelfHost.Patches;
using UnityEngine;

namespace NexusSelfHost
{
    /// <summary>
    /// Ensures self-hosted Nexus API works: the game's NexusConnector sets HttpClient.DefaultRequestHeaders.Authorization
    /// but SendRequestImpl sets the per-request Authorization to null when authToken is omitted, which overrides the default.
    /// This patch forwards the default Bearer token when authToken is null so GET /zone/info (and other calls) get 200.
    /// </summary>
    public class NexusSelfHost : IHarmonyModHooks
    {
        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            NexusSelfHostOptions.Initialize(NexusSelfHostConfig.LoadOrCreate());
            ServerMgr_Initialize_DeferredEnterGamePatch.TryApplyImmediatelyIfServerReady();
            UnityEngine.Debug.Log("[NexusSelfHost] Loaded. Config: HarmonyConfig/NexusSelfHost.json (env overrides when set). Patched NexusConnector.SendRequestImpl (default Bearer), HttpClient.SendAsync (dedupe Authorization), NexusConnector ctor (HTTP timeout), BasePlayer.OnDisconnected -> POST /zone/player/disconnect, PlayerInit persist + blueprint resync, EnterGame blueprint log, NexusServer.TransferEntity ocean/ferry block, TransferHandler empty-entities guard + portal/outpost console routing + terrain-safe spawn, Debug.Log filter for benign Nexus RPC timeout.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args) { }
    }
}
