using System;
using HarmonyLib;
using UnityEngine;

namespace MinimapHarmony.Patches
{
    /// <summary>
    /// FogImageUpdate / ServerClearFog IL references FileStorage.server.
    /// PatchAll at Harmony load JITs that IL while identity is still
    /// "my_server_identity", which throws TypeInitializationException and
    /// aborts every Minimap patch. Apply these two after identity is set.
    /// </summary>
    public static class DeferredFogPatches
    {
        private static bool _applied;

        public static void Apply()
        {
            if (_applied) return;
            try
            {
                var harmony = new Harmony("com.facepunch.rust_dedicated.Minimap");
                var fog = AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.FogImageUpdate), new[] { typeof(BaseEntity.RPCMessage) });
                if (fog != null)
                    harmony.Patch(fog, postfix: new HarmonyMethod(typeof(BasePlayer_FogImageUpdate_Patch), nameof(BasePlayer_FogImageUpdate_Patch.Postfix)));
                var clear = AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.ServerClearFog), new[] { typeof(bool), typeof(bool) });
                if (clear != null)
                    harmony.Patch(clear, postfix: new HarmonyMethod(typeof(BasePlayer_ServerClearFog_Patch), nameof(BasePlayer_ServerClearFog_Patch.Postfix)));
                _applied = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Deferred fog patches: " + ex.Message);
            }
        }
    }

    public static class BasePlayer_FogImageUpdate_Patch
    {
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnFogOfWarImageUpdate(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnFogOfWarImageUpdate: " + ex.Message);
            }
        }
    }

    public static class BasePlayer_ServerClearFog_Patch
    {
        public static void Postfix(BasePlayer __instance, bool mainland, bool deepSea)
        {
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnClearFogOfWar(__instance, mainland, deepSea);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnClearFogOfWar: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "OnFogOfWarStale")]
    public static class BasePlayer_OnFogOfWarStale_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnFogOfWarStale(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnFogOfWarStale: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(DeepSeaManager), "OpenDeepSea")]
    public static class DeepSeaManager_Open_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(DeepSeaManager __instance)
        {
            try
            {
                if (__instance != null && __instance.IsOpen())
                    MinimapHarmonyMod.Instance?.Plugin?.OnDeepSeaOpened(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnDeepSeaOpened: " + ex.Message);
            }
        }
    }
}
