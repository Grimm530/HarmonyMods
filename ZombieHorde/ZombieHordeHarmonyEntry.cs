using UnityEngine;

namespace ZombieHorde
{
    public class ZombieHordeHarmonyEntry : IHarmonyModHooks
    {
        public static ZombieHordeHarmonyEntry Instance { get; private set; }
        private ZombieHordePlugin _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            _plugin = new ZombieHordePlugin();
            ZombieHordePlugin.Instance = _plugin;
            _plugin.Init();
            Debug.Log("[ZombieHorde] Harmony mod loaded (v0.6.351). Requires GrimmNPC.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            _plugin?.Shutdown();
            _plugin = null;
            ZombieHordePlugin.Instance = null;
            Instance = null;
            Debug.Log("[ZombieHorde] Harmony mod unloaded.");
        }
    }
}
