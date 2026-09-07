using System;
using GrimmCuiHarmony;
using System.IO;
using GrimmCuiHarmony;
using UnityEngine;
using GrimmCuiHarmony;
using Rust;
using GrimmCuiHarmony;

namespace RaidableBasesUI;

/// <summary>
/// Harmony mod entry for RaidableBases UI.
/// Provides CUI for buyable events, cooldowns, delay, lockout, status, teleport.
/// Designed to be called by RaidableBases (Oxide or Harmony) via reflection.
/// Config: HarmonyConfig/RaidableBasesUI.json. Data: HarmonyData/RaidableBasesUI/.
/// </summary>
public class RaidableBasesUIMod : IHarmonyModHooks
{
    public static RaidableBasesUIMod Instance { get; private set; }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
            GrimmCui.RegisterReadyCallback(GrimmCuiRegistration.Register);
        Instance = this;
        RaidableBasesUIConfig.Load();
        RaidableBasesUIHandler.LoadOffsetData();
        EnsureHarmonyDataDir();
        Debug.Log("[RaidableBasesUI] Loaded. Config: HarmonyConfig/RaidableBasesUI.json, Data: HarmonyData/RaidableBasesUI/.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Instance = null;
        Debug.Log("[RaidableBasesUI] Unloaded.");
    }

    private static void EnsureHarmonyDataDir()
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var dir = Path.Combine(root, "HarmonyData", "RaidableBasesUI");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        catch { }
    }

    /// <summary>Forward console command so RaidableBases (if loaded) can handle ui_buyraid &lt;mode&gt;.</summary>
    public static void RunUiBuyraidCommand(string mode)
    {
        try
        {
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "ui_buyraid", mode);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[RaidableBasesUI] RunUiBuyraidCommand: " + ex.Message);
        }
    }
}
