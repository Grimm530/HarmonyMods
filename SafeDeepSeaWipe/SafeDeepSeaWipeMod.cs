namespace SafeDeepSeaWipe;

/// <summary>
/// Only kill entities that are actually inside Deep Sea bounds when Deep Sea closes.
/// The game's GetAllDeepSeaEntities adds every entity in DeepSeaGroup and every layer-4
/// visibility group without a position check; entities with stale parent refs can stay
/// in that group and get killed even when physically on the mainland. This mod filters
/// the list so only entities inside DeepSeaBounds are wiped.
/// </summary>
public class SafeDeepSeaWipeMod : IHarmonyModHooks
{
    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        UnityEngine.Debug.Log("[SafeDeepSeaWipe] Loaded. Deep Sea wipe will only kill entities inside Deep Sea bounds.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args) { }
}
