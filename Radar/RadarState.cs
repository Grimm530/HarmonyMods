using System;
using System.Collections.Generic;
using UnityEngine;

namespace Radar;

public enum RadarEntityType
{
    Players,
    Sleepers,
    Dead,
    Bags,
    TC,
    Stash,
    Backpack,
    Box,
    Loot,
    Npc,
    Ore,
    Trap,
    Turret,
    Col,
    Airdrop,
    CCTV,
    MLRS,
    Prefab
}

public class RadarState
{
    public const float MinRange = 50f;
    public const float MaxRange = 800f;
    public const float RangeStep = 50f;
    public const float DistanceStep100 = 100f;
    public const float MinRefreshInterval = 0.1f;
    public const float MaxRefreshInterval = 5f;

    public bool Enabled;
    public float ViewDistance = 300f;
    /// <summary>Seconds between radar scans. Lower = faster refresh.</summary>
    public float RefreshInterval = 0.5f;
    public bool MoveModeActive;

    /// <summary>Pixel offset min for panel (anchor 0.75 0 = 25% from right). Default moved further right.</summary>
    public string UiAnchorMin = "-120 20";
    /// <summary>Pixel offset max for panel (height half of original).</summary>
    public string UiAnchorMax = "100 115";

    // All off by default; toggle on the ones you want.
    private readonly HashSet<RadarEntityType> _enabled = new HashSet<RadarEntityType>();

    public RadarState()
    {
        var cfg = RadarConfig.Config;
        if (cfg != null)
        {
            var settings = cfg.Settings;
            if (settings != null && settings.DefaultDistance > 0f)
            {
                ViewDistance = Mathf.Clamp(settings.DefaultDistance, MinRange, MaxRange);
            }

            var gui = cfg.GUI;
            if (gui != null)
            {
                if (!string.IsNullOrEmpty(gui.OffsetMin) && !LooksLikeNormalized(gui.OffsetMin))
                    UiAnchorMin = gui.OffsetMin;
                if (!string.IsNullOrEmpty(gui.OffsetMax) && !LooksLikeNormalized(gui.OffsetMax))
                    UiAnchorMax = gui.OffsetMax;
            }
        }
    }

    private static bool LooksLikeNormalized(string s)
    {
        var parts = s?.Split(' ');
        if (parts == null || parts.Length < 2) return false;
        return float.TryParse(parts[0], out float a) && float.TryParse(parts[1], out float b) && a >= 0f && a <= 1f && b >= 0f && b <= 1f;
    }

    public bool IsEnabled(RadarEntityType t) => _enabled.Contains(t);

    public bool IsAllEnabled()
    {
        foreach (RadarEntityType t in Enum.GetValues(typeof(RadarEntityType)))
            if (!_enabled.Contains(t)) return false;
        return true;
    }

    public void SetAll(bool on)
    {
        _enabled.Clear();
        if (on)
        {
            foreach (RadarEntityType t in Enum.GetValues(typeof(RadarEntityType)))
                _enabled.Add(t);
        }
    }
    public void Toggle(RadarEntityType t)
    {
        if (_enabled.Contains(t)) _enabled.Remove(t);
        else _enabled.Add(t);
    }

    public void IncreaseRange()
    {
        ViewDistance = Mathf.Min(MaxRange, ViewDistance + RangeStep);
    }

    public void DecreaseRange()
    {
        ViewDistance = Mathf.Max(MinRange, ViewDistance - RangeStep);
    }

    public void IncreaseDistance100()
    {
        ViewDistance = Mathf.Min(MaxRange, ViewDistance + DistanceStep100);
    }

    public void DecreaseDistance100()
    {
        ViewDistance = Mathf.Max(MinRange, ViewDistance - DistanceStep100);
    }

    /// <summary>Faster refresh: decrease interval by 0.1.</summary>
    public void IncreaseRefreshRate()
    {
        RefreshInterval = Mathf.Max(MinRefreshInterval, RefreshInterval - 0.1f);
    }

    /// <summary>Slower refresh: increase interval by 0.1.</summary>
    public void DecreaseRefreshRate()
    {
        RefreshInterval = Mathf.Min(MaxRefreshInterval, RefreshInterval + 0.1f);
    }
}
