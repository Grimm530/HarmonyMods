using System.Collections.Generic;
using HarmonyLib;

namespace LivemapBridge;

/// <summary>
/// Tracks live Bradleys without walking serverEntities every snapshot.
/// Seed once on load; ServerInit postfix catches later spawns; Tick prunes destroyed.
/// </summary>
internal static class LivemapBradleyTracker
{
    static readonly List<BradleyAPC> Live = new List<BradleyAPC>(8);

    public static int Count => Live.Count;

    public static List<BradleyAPC> LiveList => Live;

    public static void Clear()
    {
        Live.Clear();
    }

    public static void Add(BradleyAPC bradley)
    {
        if (bradley == null || bradley.IsDestroyed)
            return;
        if (Live.Contains(bradley))
            return;
        Live.Add(bradley);
    }

    public static void SeedFromWorld()
    {
        Live.Clear();
        if (BaseNetworkable.serverEntities == null)
            return;
        foreach (BaseNetworkable ent in BaseNetworkable.serverEntities)
        {
            if (ent is BradleyAPC bradley && !bradley.IsDestroyed)
                Live.Add(bradley);
        }
    }
}

[HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.ServerInit))]
internal static class LivemapBradleySpawnPatch
{
    [HarmonyPostfix]
    static void Postfix(BradleyAPC __instance)
    {
        LivemapBradleyTracker.Add(__instance);
    }
}
