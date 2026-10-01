using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Records every PlaceMonuments folder once, then applies a row only when ShouldChange is true.
    /// Include, exclude, and copy rules run through Prefab.FindPrefabNames while that group is placing.
    /// </summary>
    internal static class MonumentPrefabRules
    {
        internal static MonumentGroupConfig Active;
    }

    [HarmonyPatch(typeof(WorldSetup), nameof(WorldSetup.InitCoroutine))]
    [HarmonyPriority(Priority.Last)]
    public static class WorldSetup_DiscoverMonumentGroups_Patch
    {
        static void Prefix(WorldSetup __instance)
        {
            if (__instance == null || !CustomMapGen.IsCustomMapGenEnabled())
                return;
            if (CustomMapGen.IsLoadingExistingMap)
                return;

            PlaceMonuments[] components = __instance.GetComponentsInChildren<PlaceMonuments>(true);
            if (components == null || components.Length == 0)
                return;

            var discovered = new List<MonumentGroupConfig>(components.Length);
            for (int i = 0; i < components.Length; i++)
            {
                PlaceMonuments component = components[i];
                if (component == null || string.IsNullOrEmpty(component.ResourceFolder))
                    continue;
                discovered.Add(Capture(component));
            }
            CustomMapGen.Instance.RememberMonumentGroups(discovered);
        }

        static MonumentGroupConfig Capture(PlaceMonuments component)
        {
            var group = new MonumentGroupConfig
            {
                ShouldChange = false,
                Generate = true,
                Description = component.Description ?? "",
                Folder = component.ResourceFolder,
                MinWorldSize = component.MinWorldSize,
                TargetCount = component.TargetCount,
                MinDistanceSameType = component.MinDistanceSameType,
                MinDistanceDifferentType = component.MinDistanceDifferentType,
                DistanceSame = component.DistanceSameType.ToString(),
                DistanceDifferent = component.DistanceDifferentType.ToString(),
                Filter = new MonumentGroupFilterConfig()
            };
            SpawnFilter filter = component.Filter;
            if (filter != null)
            {
                group.Filter.BiomeType = NamedFlags(filter.BiomeType);
                group.Filter.SplatType = NamedFlags(filter.SplatType);
                group.Filter.TopologyAny = NamedFlags(filter.TopologyAny);
                group.Filter.TopologyAll = NamedFlags(filter.TopologyAll);
                group.Filter.TopologyNot = NamedFlags(filter.TopologyNot);
            }
            return group;
        }

        static List<string> NamedFlags(Enum value)
        {
            var list = new List<string>();
            if (value == null)
                return list;
            long raw = Convert.ToInt64(value);
            if (raw == 0 || raw == -1)
                return list;
            Array values = Enum.GetValues(value.GetType());
            for (int i = 0; i < values.Length; i++)
            {
                object flag = values.GetValue(i);
                long bits = Convert.ToInt64(flag);
                if (bits == 0 || (raw & bits) != bits)
                    continue;
                list.Add(flag.ToString());
            }
            return list;
        }
    }

    [HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
    [HarmonyPriority(Priority.Last)]
    public static class PlaceMonuments_ApplyGroup_Patch
    {
        static bool Prefix(PlaceMonuments __instance)
        {
            MonumentPrefabRules.Active = null;
            if (__instance == null || !CustomMapGen.IsCustomMapGenEnabled() || CustomMapGen.IsLoadingExistingMap)
                return true;

            var config = CustomMapGen.Instance.GetConfig();
            string folder = __instance.ResourceFolder ?? "";
            if ((config.RemoveUndergroundTunnels || config.RemoveTunnelEntrances)
                && folder.IndexOf("tunnel-entrance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                __instance.MinWorldSize = int.MaxValue;
                UnityEngine.Debug.Log("[CustomMapGen] Tunnel entrances disabled (" + folder + ").");
            }

            MonumentGroupConfig group = FindGroup(config.MonumentGroups, folder);
            if (group == null || !group.ShouldChange)
                return true;
            if (!group.Generate)
            {
                UnityEngine.Debug.Log("[CustomMapGen] Monument group skipped: " + (group.Description ?? folder));
                return false;
            }

            if (Enum.TryParse(group.DistanceSame, true, out PlaceMonuments.DistanceMode distanceSame))
                __instance.DistanceSameType = distanceSame;
            if (Enum.TryParse(group.DistanceDifferent, true, out PlaceMonuments.DistanceMode distanceDifferent))
                __instance.DistanceDifferentType = distanceDifferent;
            __instance.MinDistanceDifferentType = group.MinDistanceDifferentType;
            __instance.MinDistanceSameType = group.MinDistanceSameType;
            __instance.TargetCount = group.TargetCount;
            __instance.MinWorldSize = group.MinWorldSize;
            if (group.Filter != null && group.Filter.Enabled)
                __instance.Filter = BuildFilter(group.Filter);
            if (!string.IsNullOrWhiteSpace(group.OverrideFolder))
            {
                UnityEngine.Debug.Log("[CustomMapGen] " + group.Description + ": folder '" + folder + "' -> '" + group.OverrideFolder + "'");
                __instance.ResourceFolder = group.OverrideFolder;
            }
            if (group.IgnoreWorldSizeMultiplier)
                __instance.TargetCountWorldSizeMultiplier = AnimationCurve.Constant(0f, 100000f, 1f);
            if (group.HasPrefabRules)
                MonumentPrefabRules.Active = group;
            UnityEngine.Debug.Log("[CustomMapGen] Monument group applied: " + (string.IsNullOrEmpty(group.Description) ? folder : group.Description));
            return true;
        }

        static void Finalizer()
        {
            MonumentPrefabRules.Active = null;
        }

        static MonumentGroupConfig FindGroup(List<MonumentGroupConfig> groups, string folder)
        {
            if (groups == null || string.IsNullOrEmpty(folder))
                return null;
            for (int i = 0; i < groups.Count; i++)
            {
                MonumentGroupConfig group = groups[i];
                if (group != null && string.Equals(group.Folder, folder, StringComparison.OrdinalIgnoreCase))
                    return group;
            }
            return null;
        }

        static SpawnFilter BuildFilter(MonumentGroupFilterConfig source)
        {
            var filter = new SpawnFilter();
            SetEnum(filter, "BiomeType", CombineFlags("BiomeType", source.BiomeType, -1));
            SetEnum(filter, "SplatType", CombineFlags("SplatType", source.SplatType, -1));
            SetEnum(filter, "TopologyAny", CombineFlags("TopologyAny", source.TopologyAny, -1));
            SetEnum(filter, "TopologyAll", CombineFlags("TopologyAll", source.TopologyAll, 0));
            SetEnum(filter, "TopologyNot", CombineFlags("TopologyNot", source.TopologyNot, 0));
            return filter;
        }

        static int CombineFlags(string fieldName, List<string> names, int whenEmpty)
        {
            FieldInfo field = typeof(SpawnFilter).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null || names == null || names.Count == 0)
                return whenEmpty;
            int mask = 0;
            bool any = false;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                try
                {
                    object parsed = Enum.Parse(field.FieldType, name.Trim(), true);
                    mask |= Convert.ToInt32(parsed);
                    any = true;
                }
                catch (ArgumentException)
                {
                    UnityEngine.Debug.LogWarning("[CustomMapGen] Unknown " + fieldName + " flag '" + name + "'.");
                }
            }
            return any ? mask : whenEmpty;
        }

        static void SetEnum(SpawnFilter filter, string fieldName, int value)
        {
            FieldInfo field = typeof(SpawnFilter).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
                return;
            field.SetValue(filter, Enum.ToObject(field.FieldType, value));
        }
    }

    [HarmonyPatch(typeof(Prefab), "FindPrefabNames", typeof(string), typeof(bool), typeof(bool))]
    public static class Prefab_FindPrefabNames_GroupRules_Patch
    {
        static void Postfix(ref string[] __result)
        {
            MonumentGroupConfig active = MonumentPrefabRules.Active;
            if (active == null || __result == null || __result.Length == 0)
                return;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < __result.Length; i++)
            {
                string path = __result[i];
                if (string.IsNullOrEmpty(path))
                    continue;
                counts.TryGetValue(path, out int count);
                counts[path] = count + 1;
            }

            var kept = new List<string>();
            foreach (KeyValuePair<string, int> entry in counts)
            {
                string name = FileName(entry.Key);
                if (!IncludeAllows(active.IncludePrefabs, name) || ExcludeHits(active.ExcludePrefabs, name))
                    continue;
                int copies = CopyCount(active.PrefabCopies, name, entry.Value);
                for (int i = 0; i < copies; i++)
                    kept.Add(entry.Key);
            }
            UnityEngine.Debug.Log($"[CustomMapGen] {active.Description}: prefab candidates {__result.Length} -> {kept.Count}");
            __result = kept.ToArray();
        }

        static bool IncludeAllows(List<string> include, string name)
        {
            if (include == null || include.Count == 0)
                return true;
            for (int i = 0; i < include.Count; i++)
            {
                if (!string.IsNullOrEmpty(include[i]) && name.IndexOf(include[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        static bool ExcludeHits(List<string> exclude, string name)
        {
            if (exclude == null)
                return false;
            for (int i = 0; i < exclude.Count; i++)
            {
                if (!string.IsNullOrEmpty(exclude[i]) && name.IndexOf(exclude[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        static int CopyCount(Dictionary<string, int> copies, string name, int fallback)
        {
            if (copies == null)
                return fallback;
            foreach (KeyValuePair<string, int> entry in copies)
            {
                if (!string.IsNullOrEmpty(entry.Key) && name.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return entry.Value < 0 ? 0 : entry.Value;
            }
            return fallback;
        }

        static string FileName(string path)
        {
            int slash = path.LastIndexOf('/');
            string name = slash >= 0 ? path.Substring(slash + 1) : path;
            if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ".prefab".Length);
            return name;
        }
    }
}
