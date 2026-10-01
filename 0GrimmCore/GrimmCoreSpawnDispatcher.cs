using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrimmCoreHarmony
{
    public delegate void SpawnPostfixHandler(BaseNetworkable entity);

    /// <summary>
    /// Single ordered handler list for BaseNetworkable.Spawn. One Harmony postfix owns the patch.
    /// </summary>
    public static class GrimmCoreSpawnDispatcher
    {
        private sealed class Entry
        {
            public string ModId;
            public int Priority;
            public SpawnPostfixHandler Handler;
        }

        private static readonly List<Entry> Entries = new List<Entry>(32);
        private static SpawnPostfixHandler[] _snapshot = Array.Empty<SpawnPostfixHandler>();
        private static string[] _modSnapshot = Array.Empty<string>();

        public static void RegisterPostfix(string modId, int priority, SpawnPostfixHandler handler)
        {
            if (string.IsNullOrEmpty(modId) || handler == null) return;
            lock (Entries)
            {
                Entries.RemoveAll(e => e.ModId == modId);
                Entries.Add(new Entry { ModId = modId, Priority = priority, Handler = handler });
                RebuildSnapshot();
            }
        }

        public static void RegisterPostfixUntyped(string modId, int priority, Delegate handler)
        {
            if (TryAdapt(handler, out SpawnPostfixHandler typed))
                RegisterPostfix(modId, priority, typed);
            else
                Debug.LogWarning("[GrimmCore] Spawn register failed for " + modId + " (handler=" + (handler == null ? "null" : handler.GetType().FullName) + ")");
        }

        public static void UnregisterMod(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            lock (Entries)
            {
                Entries.RemoveAll(e => e.ModId == modId);
                RebuildSnapshot();
            }
        }

        public static void ClearAll()
        {
            lock (Entries)
            {
                Entries.Clear();
                RebuildSnapshot();
            }
        }

        public static void InvokePostfix(BaseNetworkable entity)
        {
            if (entity == null) return;
            var snapshot = _snapshot;
            var mods = _modSnapshot;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i](entity); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[GrimmCore] Spawn " + (i < mods.Length ? mods[i] : "?") + ": " + ex.Message);
                }
            }
        }

        private static bool TryAdapt(Delegate handler, out SpawnPostfixHandler typed)
        {
            typed = null;
            if (handler == null) return false;
            if (handler is SpawnPostfixHandler exact)
            {
                typed = exact;
                return true;
            }
            try
            {
                typed = (SpawnPostfixHandler)Delegate.CreateDelegate(typeof(SpawnPostfixHandler), handler.Target, handler.Method);
                return typed != null;
            }
            catch
            {
                return false;
            }
        }

        private static void RebuildSnapshot()
        {
            Entries.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(a.ModId, b.ModId));
            var handlers = new SpawnPostfixHandler[Entries.Count];
            var mods = new string[Entries.Count];
            for (int i = 0; i < Entries.Count; i++)
            {
                handlers[i] = Entries[i].Handler;
                mods[i] = Entries[i].ModId;
            }
            _snapshot = handlers;
            _modSnapshot = mods;
        }
    }
}
