using System;
using RustEditStandalone.Commands;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using RustEditStandalone.Features;
using UnityEngine;

namespace RustEditStandalone;

/// <summary>
/// Harmony mod entry — full Oxide.Ext.RustEdit feature parity without Oxide (AutoUpdater omitted).
/// </summary>
public class RustEditStandaloneMod : IHarmonyModHooks
{
    public static RustEditStandaloneMod Instance { get; private set; }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        RustEditConfig.Load();
        RustEditHub.Reset();

        DeployableFeature.Initialize();
        IoFeature.Initialize();
        VendingFeature.Initialize();
        LootFeature.Initialize();
        ResourceFeature.Initialize();
        JunkPileFeature.Initialize();
        DeskKeycardFeature.Initialize();
        DieselFeature.Initialize();
        SpawnFeature.Initialize();
        OceanFeature.Initialize();
        ApcFeature.Initialize();
        VehicleFeature.Initialize();
        NpcFeature.Initialize();
        ExcavatorRotationFeature.Initialize();
        CustomTopologyFeature.Initialize();
        ShopKeeperFeature.Initialize();

        RustEditCommands.Register();
        AppDomain.CurrentDomain.SetData("RustEdit_ApiType", typeof(RustEditApi));

        Debug.Log("[RustEditStandalone] Loaded. Config: HarmonyConfig/RustEdit.json");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        try { RustEditCommands.Unregister(); } catch { }
        try { AppDomain.CurrentDomain.SetData("RustEdit_ApiType", null); } catch { }

        ShopKeeperFeature.Shutdown();
        CustomTopologyFeature.Shutdown();
        ExcavatorRotationFeature.Shutdown();
        NpcFeature.Shutdown();
        VehicleFeature.Shutdown();
        ApcFeature.Shutdown();
        OceanFeature.Shutdown();
        SpawnFeature.Shutdown();
        DieselFeature.Shutdown();
        DeskKeycardFeature.Shutdown();
        JunkPileFeature.Shutdown();
        ResourceFeature.Shutdown();
        LootFeature.Shutdown();
        VendingFeature.Shutdown();
        IoFeature.Shutdown();
        DeployableFeature.Shutdown();
        RustEditHub.Reset();

        Instance = null;
        Debug.Log("[RustEditStandalone] Unloaded.");
    }
}
