using System.Collections.Generic;
using System.Text;
using Rust.Ai.Gen2;
using UnityEngine;

namespace CowFix;

internal static class Population
{
    private const ulong MinSteamId = 76561197960265728UL;

    public static bool IsPlayerOwned(LivestockAnimal animal)
    {
        if (animal == null || !animal.TryGetHomeCupboard(out BuildingPrivlidge cupboard) || cupboard == null)
            return false;
        if (cupboard.OwnerID > MinSteamId)
            return true;
        if (cupboard.authorizedPlayers == null)
            return false;
        foreach (ulong userId in cupboard.authorizedPlayers)
        {
            if (userId > MinSteamId)
                return true;
        }
        return false;
    }

    public static string Report()
    {
        Dictionary<string, int> penned = new Dictionary<string, int>();
        Dictionary<string, int> unowned = new Dictionary<string, int>();
        int pennedTotal = 0;
        int unownedTotal = 0;

        foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
        {
            if (networkable is not LivestockAnimal animal || animal.IsDestroyed || animal.IsDead())
                continue;
            string name = string.IsNullOrEmpty(animal.ShortPrefabName) ? animal.PrefabName : animal.ShortPrefabName;
            if (IsPlayerOwned(animal))
            {
                Add(penned, name);
                pennedTotal++;
            }
            else
            {
                Add(unowned, name);
                unownedTotal++;
            }
        }

        StringBuilder report = new StringBuilder();
        report.Append("Livestock: ").Append(pennedTotal).Append(" penned, ").Append(unownedTotal).Append(" with no player tool cupboard.");
        AppendCounts(report, penned, unowned);
        return report.ToString();
    }

    public static int PurgeUnowned()
    {
        List<LivestockAnimal> kill = new List<LivestockAnimal>();
        foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
        {
            if (networkable is not LivestockAnimal animal || animal.IsDestroyed || animal.IsDead())
                continue;
            if (!IsPlayerOwned(animal))
                kill.Add(animal);
        }

        for (int i = 0; i < kill.Count; i++)
        {
            if (kill[i] != null && !kill[i].IsDestroyed)
                kill[i].Kill();
        }
        return kill.Count;
    }

    private static void Add(Dictionary<string, int> counts, string name)
    {
        counts.TryGetValue(name, out int count);
        counts[name] = count + 1;
    }

    private static void AppendCounts(StringBuilder report, Dictionary<string, int> penned, Dictionary<string, int> unowned)
    {
        HashSet<string> names = new HashSet<string>(penned.Keys);
        foreach (string name in unowned.Keys)
            names.Add(name);

        foreach (string name in names)
        {
            penned.TryGetValue(name, out int kept);
            unowned.TryGetValue(name, out int wild);
            report.Append("\n  ").Append(name).Append(": ").Append(kept).Append(" penned, ").Append(wild).Append(" unowned");
        }
    }
}
