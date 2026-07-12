namespace AlwaysBonus;

/// <summary>
/// Main mod class implementing IHarmonyModHooks for lifecycle.
/// Hits X markers on trees and stars on nodes automatically when enabled.
/// </summary>
public class AlwaysBonusMod : IHarmonyModHooks
{
    public static AlwaysBonusMod Instance { get; private set; }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        AlwaysBonusConfig.LoadConfig();
        UnityEngine.Debug.Log("[AlwaysBonus] Mod loaded. Tree X and node star farming auto-enabled per config.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Instance = null;
    }
}
