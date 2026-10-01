using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrimmCoreHarmony
{
    /// <summary>
    /// Ordered typed handler list. Snapshot is a raw delegate array so hot invokes are a loop + call.
    /// </summary>
    internal sealed class GrimmCoreHookList<TDelegate> where TDelegate : Delegate
    {
        private sealed class Entry
        {
            public string ModId;
            public int Priority;
            public TDelegate Handler;
        }

        private readonly List<Entry> _entries = new List<Entry>(8);
        public TDelegate[] Snapshot = Array.Empty<TDelegate>();
        public string[] Mods = Array.Empty<string>();

        public void Set(string modId, int priority, TDelegate handler)
        {
            if (string.IsNullOrEmpty(modId) || handler == null) return;
            lock (_entries)
            {
                _entries.RemoveAll(e => e.ModId == modId);
                _entries.Add(new Entry { ModId = modId, Priority = priority, Handler = handler });
                Rebuild();
            }
        }

        public void Remove(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            lock (_entries)
            {
                if (_entries.RemoveAll(e => e.ModId == modId) > 0)
                    Rebuild();
            }
        }

        public void RemoveHook(string modId)
        {
            Remove(modId);
        }

        public void Clear()
        {
            lock (_entries)
            {
                _entries.Clear();
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _entries.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(a.ModId, b.ModId));
            var handlers = new TDelegate[_entries.Count];
            var mods = new string[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
            {
                handlers[i] = _entries[i].Handler;
                mods[i] = _entries[i].ModId;
            }
            Snapshot = handlers;
            Mods = mods;
        }

        public static void Warn(string[] mods, int i, Exception ex)
            => Debug.LogWarning("[GrimmCore] GameHook " + (i < mods.Length ? mods[i] : "?") + ": " + ex.Message);
    }
}
