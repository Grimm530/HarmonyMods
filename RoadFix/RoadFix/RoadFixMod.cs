namespace RoadFix;

/// <summary>
/// Minimal fix: (1) re-apply river height after roads fill valleys,
/// (2) rebuild road meshes with snapToTerrain=false so nodes stay at natural height,
/// (3) place custom bridge maps under crossings.
/// Never moves road or rail path nodes.
/// </summary>
public class RoadFixMod : IHarmonyModHooks
{
    public static RoadFixMod Instance { get; private set; }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        RoadFixConfig.LoadConfig();
        RoadFix.Bridge.DeferredBridgeSpawn.Clear();
        var cfg = RoadFixConfig.Config;
        UnityEngine.Debug.Log(
            $"[RoadFix] Loaded. Enabled={cfg?.Enabled} " +
            $"SnapToTerrain={cfg?.RoadsSnapToTerrain} " +
            $"SpawnBridges={cfg?.SpawnCustomBridges} " +
            $"RiverReapply={cfg?.ReapplyRiverHeightAfterRoads} " +
            $"RoadMap={cfg?.RoadBridgeMapPath} RailMap={cfg?.RailBridgeMapPath}");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Instance = null;
    }
}
