using System;
using UnityEngine;

namespace GrimmCoreHarmony
{
    /// <summary>
    /// Core infrastructure: unified Hurt dispatcher, vanilla AI cull, shared custom entity skinID.
    /// Load early as 0GrimmCore.dll (before most Harmony mods alphabetically).
    /// </summary>
    public class GrimmCoreMod : IHarmonyModHooks
    {
        public const string AppDomainApiKey = "GrimmCore_ApiType";
        public const string CustomEntitySkinIdKey = "GrimmCore_CustomEntitySkinId";

        static GrimmCoreMod()
        {
            PublishAppDomainApi();
        }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            PublishAppDomainApi();
            Debug.Log("[GrimmCore] Loaded. Unified Hurt dispatcher + vanilla scientist/scarecrow AI cull active (wildlife not culled). CustomEntitySkinId="
                + GrimmCoreEntityMarkers.CustomEntitySkinId);
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            GrimmCoreHurtDispatcher.ClearAll();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(CustomEntitySkinIdKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtPrefix", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtSideEffect", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtPostfix", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterHurtMod", null); } catch { }
            Debug.Log("[GrimmCore] Unloaded.");
        }

        private static void PublishAppDomainApi()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(GrimmCoreMod));
                AppDomain.CurrentDomain.SetData(CustomEntitySkinIdKey, GrimmCoreEntityMarkers.CustomEntitySkinId);
                AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtPrefix",
                    new Action<string, int, Delegate>(GrimmCoreHurtDispatcher.RegisterPrefixUntyped));
                AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtSideEffect",
                    new Action<string, int, Delegate>(GrimmCoreHurtDispatcher.RegisterSideEffectUntyped));
                AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtPostfix",
                    new Action<string, int, Delegate>(GrimmCoreHurtDispatcher.RegisterPostfixUntyped));
                AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterHurtMod",
                    new Action<string>(GrimmCoreHurtDispatcher.UnregisterMod));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmCore] AppDomain publish failed: " + ex.Message);
            }
        }
    }
}
