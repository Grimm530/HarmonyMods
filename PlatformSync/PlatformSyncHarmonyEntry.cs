using UnityEngine;

namespace PlatformSync
{
    /// <summary>
    /// Harmony mod entry: Platform Sync server plugin port (1.1.01).
    /// Config: HarmonyConfig/PlatformSync.json
    /// Data: HarmonyData/PlatformSync/
    /// </summary>
    public class PlatformSyncHarmonyEntry : IHarmonyModHooks
    {
        public static PlatformSyncHarmonyEntry Instance { get; private set; }
        private PlatformSyncPlugin _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            _plugin = new PlatformSyncPlugin();
            _plugin.Init();
            Debug.Log("[PlatformSync] Harmony mod loaded.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            _plugin?.Shutdown();
            _plugin = null;
            Instance = null;
            Debug.Log("[PlatformSync] Harmony mod unloaded.");
        }
    }
}
