using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GrimmCuiHarmony
{
    /// <summary>
    /// Shared CUI foundation for Grimm Harmony mods. Loads first (0GrimmCUI.dll).
    /// Provides Harmony-compatible CuiHelper, centralized cui.endtest routing, and drag RPC routing.
    /// </summary>
    public class GrimmCUIMod : IHarmonyModHooks
    {
        public static GrimmCUIMod Instance { get; private set; }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            GrimmCui.ResetRegistrations();
            GrimmCui.BumpGeneration();
            Debug.Log("[0GrimmCUI] Loaded. Mods should register handlers via GrimmCui.Register* in OnLoaded.");
            InvokeReadyCallbacks();
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            GrimmCui.ResetRegistrations();
            try { AppDomain.CurrentDomain.SetData(GrimmCui.AppDomainApiKey, null); } catch { }
            Instance = null;
        }

        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            var list = GetOrCreateReadyCallbacks();
            lock (list)
            {
                if (!list.Contains(callback))
                    list.Add(callback);
            }

            if (Instance != null)
            {
                try { callback(); }
                catch (Exception ex) { Debug.LogWarning("[0GrimmCUI] Ready callback (immediate): " + ex.Message); }
            }
        }

        public static void UnregisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                if (AppDomain.CurrentDomain.GetData(GrimmCui.AppDomainReadyCallbacksKey) is List<Action> list)
                {
                    lock (list) list.Remove(callback);
                }
            }
            catch { }
        }

        private static List<Action> GetOrCreateReadyCallbacks()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(GrimmCui.AppDomainReadyCallbacksKey) is List<Action> existing)
                    return existing;
            }
            catch { }

            var created = new List<Action>();
            try { AppDomain.CurrentDomain.SetData(GrimmCui.AppDomainReadyCallbacksKey, created); } catch { }
            return created;
        }

        private static void InvokeReadyCallbacks()
        {
            List<Action> snapshot;
            try
            {
                if (!(AppDomain.CurrentDomain.GetData(GrimmCui.AppDomainReadyCallbacksKey) is List<Action> list) || list.Count == 0)
                    return;
                lock (list) snapshot = new List<Action>(list);
            }
            catch { return; }

            Debug.Log($"[0GrimmCUI] Invoking {snapshot.Count} ready callback(s) for consumer re-register.");
            foreach (Action cb in snapshot)
            {
                try { cb(); }
                catch (Exception ex) { Debug.LogWarning("[0GrimmCUI] Ready callback: " + ex.Message); }
            }
        }
    }
}
