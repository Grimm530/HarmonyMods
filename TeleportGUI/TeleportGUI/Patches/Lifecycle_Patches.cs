using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TeleportGUI.Patches
{
    /// <summary>BasePlayer.OnDisconnected — cancel TPR / delayed TP and destroy CUI.</summary>
    [HarmonyPatch]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))
                   ?? AccessTools.DeclaredMethod(typeof(BasePlayer), "OnDisconnected");
        }

        static bool Prepare() => TargetMethod() != null;

        [HarmonyPostfix]
        static void Postfix(BasePlayer __instance)
        {
            try
            {
                TeleportGUIMod.Instance?.OnPlayerDisconnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }

    /// <summary>SaveRestore.Save(string, bool) — persist TeleportGUI data on server save.</summary>
    [HarmonyPatch]
    internal static class SaveRestore_Save_Patch
    {
        static MethodBase TargetMethod()
        {
            // Prefer IEnumerator Save(string, bool) used by automated save pipeline.
            return AccessTools.Method(typeof(SaveRestore), nameof(SaveRestore.Save), new[] { typeof(string), typeof(bool) })
                   ?? AccessTools.Method(typeof(SaveRestore), "Save", new[] { typeof(string), typeof(bool) })
                   ?? AccessTools.Method(typeof(SaveRestore), nameof(SaveRestore.Save), new[] { typeof(bool) });
        }

        static bool Prepare() => TargetMethod() != null;

        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                TeleportGUIMod.Instance?.OnServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] OnServerSave: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// ServerMgr.Initialize — when world was not loaded from a save (new wipe),
    /// optionally wipe homes then AssignHomeEntities.
    /// </summary>
    [HarmonyPatch]
    internal static class ServerMgr_Initialize_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var found = AccessTools.GetDeclaredMethods(typeof(ServerMgr));
            if (found == null) yield break;
            foreach (var m in found)
            {
                if (m != null && m.Name == nameof(ServerMgr.Initialize))
                    yield return m;
            }
        }

        static bool Prepare()
        {
            foreach (var _ in TargetMethods())
                return true;
            return false;
        }

        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                // Fresh map / wipe: World.LoadedFromSave is false after Initialize.
                if (World.LoadedFromSave)
                {
                    TeleportGUIMod.Instance?.AssignHomeEntities();
                    return;
                }

                TeleportGUIMod.Instance?.OnNewServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] OnNewServerSave: " + ex.Message);
            }
        }
    }

    /// <summary>SleepingBag.OnPlaced(BasePlayer) — create entity-linked home (preferred over BedMade).</summary>
    [HarmonyPatch]
    internal static class SleepingBag_OnPlaced_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SleepingBag), nameof(SleepingBag.OnPlaced), new[] { typeof(BasePlayer) })
                   ?? AccessTools.DeclaredMethod(typeof(SleepingBag), "OnPlaced", new[] { typeof(BasePlayer) });
        }

        static bool Prepare() => TargetMethod() != null;

        [HarmonyPostfix]
        static void Postfix(SleepingBag __instance, BasePlayer player)
        {
            try
            {
                TeleportGUIMod.Instance?.OnSleepingBagPlaced(__instance, player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] OnSleepingBagPlaced: " + ex.Message);
            }
        }
    }

    /// <summary>SleepingBag.Rename(RPCMessage) — update linked home key after rename.</summary>
    [HarmonyPatch]
    internal static class SleepingBag_Rename_Patch
    {
        static MethodBase TargetMethod()
        {
            var rpc = AccessTools.TypeByName("RPCMessage") ?? AccessTools.Inner(typeof(BaseEntity), "RPCMessage");
            // RPCMessage is typically a struct in global/Network scope for Rust.
            Type rpcType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    rpcType = asm.GetType("RPCMessage");
                    if (rpcType != null) break;
                }
                catch { /* ignore */ }
            }

            if (rpcType != null)
            {
                var m = AccessTools.Method(typeof(SleepingBag), nameof(SleepingBag.Rename), new[] { rpcType })
                        ?? AccessTools.DeclaredMethod(typeof(SleepingBag), "Rename", new[] { rpcType });
                if (m != null) return m;
            }

            // Fallback: any DeclaredOnly Rename on SleepingBag.
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(SleepingBag)))
            {
                if (m != null && m.Name == "Rename")
                    return m;
            }

            return null;
        }

        static bool Prepare() => TargetMethod() != null;

        [HarmonyPostfix]
        static void Postfix(SleepingBag __instance)
        {
            try
            {
                TeleportGUIMod.Instance?.OnSleepingBagRenamed(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] OnSleepingBagRenamed: " + ex.Message);
            }
        }
    }

    /// <summary>BaseNetworkable.Kill — remove entity-linked home when a SleepingBag is destroyed.</summary>
    [HarmonyPatch]
    internal static class BaseNetworkable_Kill_SleepingBag_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill),
                       new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) })
                   ?? AccessTools.Method(typeof(BaseNetworkable), "Kill",
                       new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) })
                   ?? AccessTools.Method(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill));
        }

        static bool Prepare() => TargetMethod() != null;

        [HarmonyPrefix]
        static void Prefix(BaseNetworkable __instance)
        {
            try
            {
                if (__instance is SleepingBag bag)
                    TeleportGUIMod.Instance?.OnSleepingBagDestroyed(bag);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] OnSleepingBagDestroyed: " + ex.Message);
            }
        }
    }
}
