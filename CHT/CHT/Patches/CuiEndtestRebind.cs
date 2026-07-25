using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CHT.Patches
{
    /// <summary>
    /// When CHT loads after Shop, Facepunch's unpatch/repatch of cui.endtest can drop Shop's
    /// buy-button prefix. Re-apply it under Shop's own Harmony id if missing (no double-patch).
    /// </summary>
    public static class CuiEndtestRebind
    {
        private static readonly (string typeName, string harmonyId, string label)[] ForeignPrefixes =
        {
            ("ShopHarmony.Patches.Cui_Endtest_Patch", "com.facepunch.rust_dedicated.Shop", "Shop"),
            ("KitsHarmony.Patches.Cui_Endtest_Patch", "com.facepunch.rust_dedicated.Kits", "Kits"),
        };

        public static void EnsureForeignPrefixes()
        {
            try
            {
                var target = AccessTools.Method(typeof(global::cui), nameof(global::cui.endtest));
                if (target == null)
                {
                    Debug.LogWarning("[CHT] cui.endtest rebind: target method not found.");
                    return;
                }

                foreach (var entry in ForeignPrefixes)
                    EnsureOne(target, entry.typeName, entry.harmonyId, entry.label);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CHT] cui.endtest rebind failed: " + ex.Message);
            }
        }

        private static void EnsureOne(MethodInfo target, string typeName, string harmonyId, string label)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) return;

            var prefix = AccessTools.Method(type, "Prefix");
            if (prefix == null) return;

            if (HasPrefix(target, prefix))
                return;

            var harmony = new HarmonyLib.Harmony(harmonyId);
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            Debug.Log($"[CHT] Re-bound {label} cui.endtest handler (was missing after CHT load).");
        }

        private static bool HasPrefix(MethodInfo target, MethodInfo prefix)
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(target);
            if (info?.Prefixes == null) return false;

            foreach (var p in info.Prefixes)
            {
                if (p?.PatchMethod == null) continue;
                if (p.PatchMethod == prefix) return true;
                if (p.PatchMethod.DeclaringType == prefix.DeclaringType &&
                    string.Equals(p.PatchMethod.Name, prefix.Name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
