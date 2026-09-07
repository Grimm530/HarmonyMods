using System.Collections.Generic;
using UnityEngine;
using HMPlugin = Harmony.Plugins.HitMarkers;

namespace HitMarkersHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "HitMarkers";
        private static readonly Dictionary<ulong, float> HealthBefore = new Dictionary<ulong, float>(64);

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtSideEffect(ModId, 5, CaptureHealth);
            GrimmCoreBridge.RegisterHurtPostfix(ModId, 142, ObserveHurt);
        }

        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            HealthBefore.Clear();
        }

        private static void CaptureHealth(BaseCombatEntity entity, HitInfo info)
        {
            if (entity?.net == null) return;
            HealthBefore[(ulong)entity.net.ID.Value] = entity.Health();
        }

        private static void ObserveHurt(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || entity.net == null) return;
            ulong id = (ulong)entity.net.ID.Value;
            if (!HealthBefore.TryGetValue(id, out float before)) return;
            HealthBefore.Remove(id);
            try { HMPlugin.GetModInstance()?.OnHurtObserved(entity, info, before); }
            catch (System.Exception ex) { Debug.LogWarning("[HitMarkers] Hurt postfix: " + ex.Message); }
        }
    }
}
