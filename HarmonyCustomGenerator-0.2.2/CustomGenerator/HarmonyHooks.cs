using Rust.Ai;
using UnityEngine;
namespace CustomGenerator
{
    internal class HarmonyModHooks : IHarmonyModHooks
    {
        void IHarmonyModHooks.OnLoaded(OnHarmonyModLoadedArgs args) {
            if (AiManager.nav_disable)
            {
                AiManager.nav_disable = false;
                Debug.Log("[CustomGenerator] Re-enabled AI NavMesh on load (nav_disable is for CGen map baking only).");
            }
            if (!AiManager.nav_wait)
            {
                AiManager.nav_wait = true;
                Debug.Log("[CustomGenerator] Restored aimanager.nav_wait=true on load (live servers must wait for navmesh).");
            }
            Debug.Log("[Harmony] Loaded: CustomGenerator");
        }

        void IHarmonyModHooks.OnUnloaded(OnHarmonyModUnloadedArgs args) {
            Debug.Log("[Harmony] Unloaded: CustomGenerator");
        }
    }
}
