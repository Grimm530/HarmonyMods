using System;

namespace InstantBarrel;

/// <summary>
/// Main mod class implementing IHarmonyModHooks for lifecycle.
/// No Oxide dependency: permissions and hooks are config-only; patch game methods directly.
/// </summary>
public class InstantBarrelMod : IHarmonyModHooks
{
    public static InstantBarrelMod Instance { get; private set; }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        InstantBarrelConfig.LoadConfig();
        UnityEngine.Debug.Log("[InstantBarrel] Mod loaded. Barrels and road signs: instant loot (config-only, no Oxide).");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Instance = null;
    }

    /// <summary>
    /// Whether the player is allowed instant barrel loot. Config-only: when RequirePermission is false, everyone gets it.
    /// No Oxide or external permission system; add your own logic here or via config if needed.
    /// </summary>
    public static bool HasPermission(string userIdString)
    {
        if (InstantBarrelConfig.Config == null || !InstantBarrelConfig.Config.RequirePermission)
            return true;
        // No permission system: when RequirePermission is true, allow all (or implement your own check).
        return true;
    }
}
