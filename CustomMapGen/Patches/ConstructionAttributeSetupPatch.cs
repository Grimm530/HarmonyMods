using System;
using HarmonyLib;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Safety net for Construction.AttributeSetup.
    /// Some game versions can throw NullReferenceException here when a prefab has no BaseEntity.
    /// We swallow only that specific failure so map generation can continue.
    /// </summary>
    [HarmonyPatch(typeof(Construction), "AttributeSetup")]
    public static class ConstructionAttributeSetupPatch
    {
        static Exception Finalizer(Exception __exception)
        {
            if (__exception == null)
                return null;

            if (__exception is NullReferenceException)
            {
                UnityEngine.Debug.LogWarning("[ConstructionAttributeSetupPatch] Swallowed NullReferenceException in Construction.AttributeSetup (missing BaseEntity on prefab).");
                return null;
            }

            return __exception;
        }
    }
}
