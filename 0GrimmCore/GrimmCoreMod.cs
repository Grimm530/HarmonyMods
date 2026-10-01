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
            Debug.Log("[GrimmCore] Loaded. Unified Hurt/Spawn/Kill/game-hook dispatchers + hook bus. CustomEntitySkinId="
                + GrimmCoreEntityMarkers.CustomEntitySkinId);
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            GrimmCoreHurtDispatcher.ClearAll();
            GrimmCoreSpawnDispatcher.ClearAll();
            GrimmCoreHookBus.ClearAll();
            GrimmCoreGameHooks.ClearAll();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(CustomEntitySkinIdKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtPrefix", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtSideEffect", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHurtPostfix", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterHurtMod", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterSpawnPostfix", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterSpawnMod", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHook", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterHookMod", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_CallHook0", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_CallHook1", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_CallHook2", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_CallHook3", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_CallHookN", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_HasHook", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_RegisterGameHook", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterGameHookMod", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterGameHook", null); } catch { }
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
                AppDomain.CurrentDomain.SetData("GrimmCore_RegisterSpawnPostfix",
                    new Action<string, int, Delegate>(GrimmCoreSpawnDispatcher.RegisterPostfixUntyped));
                AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterSpawnMod",
                    new Action<string>(GrimmCoreSpawnDispatcher.UnregisterMod));
                AppDomain.CurrentDomain.SetData("GrimmCore_RegisterHook",
                    new Action<string, string, int, Delegate>(GrimmCoreHookBus.RegisterUntyped));
                AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterHookMod",
                    new Action<string>(GrimmCoreHookBus.UnregisterMod));
                AppDomain.CurrentDomain.SetData("GrimmCore_CallHook0",
                    new Func<string, object>(GrimmCoreHookBus.Call));
                AppDomain.CurrentDomain.SetData("GrimmCore_CallHook1",
                    new Func<string, object, object>(GrimmCoreHookBus.Call));
                AppDomain.CurrentDomain.SetData("GrimmCore_CallHook2",
                    new Func<string, object, object, object>(GrimmCoreHookBus.Call));
                AppDomain.CurrentDomain.SetData("GrimmCore_CallHook3",
                    new Func<string, object, object, object, object>(GrimmCoreHookBus.Call));
                AppDomain.CurrentDomain.SetData("GrimmCore_CallHookN",
                    new Func<string, object[], object>(GrimmCoreHookBus.Call));
                AppDomain.CurrentDomain.SetData("GrimmCore_HasHook",
                    new Func<string, bool>(GrimmCoreHookBus.HasSubscribers));
                AppDomain.CurrentDomain.SetData("GrimmCore_RegisterGameHook",
                    new Action<string, string, int, Delegate>(GrimmCoreGameHooks.RegisterUntyped));
                AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterGameHookMod",
                    new Action<string>(GrimmCoreGameHooks.UnregisterMod));
                AppDomain.CurrentDomain.SetData("GrimmCore_UnregisterGameHook",
                    new Action<string, string>(GrimmCoreGameHooks.UnregisterHook));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmCore] AppDomain publish failed: " + ex.Message);
            }
        }
    }
}
