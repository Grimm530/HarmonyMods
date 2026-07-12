using UnityEngine;

namespace DeveloperListOverride
{
    public class DeveloperListOverrideMod : IHarmonyModHooks
    {
        public static DeveloperListOverrideMod Instance { get; private set; }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            DeveloperListOverrideConfig.LoadConfig();
            UnityEngine.Debug.Log("[DeveloperListOverride] Loaded. Add Steam IDs to HarmonyConfig/DeveloperListOverride.json to grant developer (orange name + tools).");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Instance = null;
        }
    }
}
