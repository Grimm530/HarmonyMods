using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrimmCoreHarmony
{
    /// <summary>
    /// Oxide-style hook bus: subscribe once, CallHook overloads skip work when nobody is listening.
    /// Two-arg invokes never allocate an object[]. First non-null result wins (priority order).
    /// </summary>
    public static class GrimmCoreHookBus
    {
        private sealed class Entry
        {
            public string ModId;
            public int Priority;
            public int Arity;
            public Func<object> Invoke0;
            public Func<object, object> Invoke1;
            public Func<object, object, object> Invoke2;
            public Func<object, object, object, object> Invoke3;
            public Func<object[], object> InvokeN;
        }

        private static readonly Dictionary<string, List<Entry>> Hooks = new Dictionary<string, List<Entry>>(64);
        private static readonly Dictionary<string, Entry[]> Snapshots = new Dictionary<string, Entry[]>(64);

        private static readonly object[][] Pool =
        {
            Array.Empty<object>(),
            new object[1],
            new object[2],
            new object[3],
            new object[4]
        };

        public static void RegisterUntyped(string modId, string hook, int priority, Delegate handler)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(hook) || handler == null) return;
            var entry = Adapt(modId, priority, handler);
            if (entry == null)
            {
                Debug.LogWarning("[GrimmCore] Hook register failed for " + modId + " " + hook + " (handler=" + handler.GetType().FullName + ")");
                return;
            }

            lock (Hooks)
            {
                if (!Hooks.TryGetValue(hook, out var list))
                {
                    list = new List<Entry>(8);
                    Hooks[hook] = list;
                }
                list.RemoveAll(e => e.ModId == modId);
                list.Add(entry);
                Rebuild(hook, list);
            }
        }

        public static void UnregisterMod(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            lock (Hooks)
            {
                var keys = new List<string>(Hooks.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    var hook = keys[i];
                    var list = Hooks[hook];
                    list.RemoveAll(e => e.ModId == modId);
                    if (list.Count == 0)
                    {
                        Hooks.Remove(hook);
                        Snapshots.Remove(hook);
                    }
                    else
                    {
                        Rebuild(hook, list);
                    }
                }
            }
        }

        public static void ClearAll()
        {
            lock (Hooks)
            {
                Hooks.Clear();
                Snapshots.Clear();
            }
        }

        public static bool HasSubscribers(string hook)
        {
            if (string.IsNullOrEmpty(hook)) return false;
            return Snapshots.TryGetValue(hook, out var snap) && snap.Length > 0;
        }

        public static object Call(string hook)
        {
            var snap = GetSnap(hook);
            if (snap == null || snap.Length == 0) return null;
            object last = null;
            for (int i = 0; i < snap.Length; i++)
            {
                object v = InvokeEntry(snap[i], 0, null, null, null, null);
                if (v != null) return v;
                last = v;
            }
            return last;
        }

        public static object Call(string hook, object a)
        {
            var snap = GetSnap(hook);
            if (snap == null || snap.Length == 0) return null;
            for (int i = 0; i < snap.Length; i++)
            {
                object v = InvokeEntry(snap[i], 1, a, null, null, null);
                if (v != null) return v;
            }
            return null;
        }

        public static object Call(string hook, object a, object b)
        {
            var snap = GetSnap(hook);
            if (snap == null || snap.Length == 0) return null;
            for (int i = 0; i < snap.Length; i++)
            {
                object v = InvokeEntry(snap[i], 2, a, b, null, null);
                if (v != null) return v;
            }
            return null;
        }

        public static object Call(string hook, object a, object b, object c)
        {
            var snap = GetSnap(hook);
            if (snap == null || snap.Length == 0) return null;
            for (int i = 0; i < snap.Length; i++)
            {
                object v = InvokeEntry(snap[i], 3, a, b, c, null);
                if (v != null) return v;
            }
            return null;
        }

        public static object Call(string hook, object[] args)
        {
            int n = args?.Length ?? 0;
            if (n == 0) return Call(hook);
            if (n == 1) return Call(hook, args[0]);
            if (n == 2) return Call(hook, args[0], args[1]);
            if (n == 3) return Call(hook, args[0], args[1], args[2]);

            var snap = GetSnap(hook);
            if (snap == null || snap.Length == 0) return null;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i].InvokeN != null ? snap[i].InvokeN(args) : InvokeEntry(snap[i], n, args[0], args[1], n > 2 ? args[2] : null, n > 3 ? args[3] : null); }
                catch (Exception ex)
                {
                    Warn(snap[i].ModId, hook, ex);
                    continue;
                }
                if (v != null) return v;
            }
            return null;
        }

        private static Entry[] GetSnap(string hook)
        {
            if (string.IsNullOrEmpty(hook)) return null;
            return Snapshots.TryGetValue(hook, out var snap) ? snap : null;
        }

        private static object InvokeEntry(Entry e, int arity, object a, object b, object c, object d)
        {
            try
            {
                if (arity == 0 && e.Invoke0 != null) return e.Invoke0();
                if (arity == 1 && e.Invoke1 != null) return e.Invoke1(a);
                if (arity == 2 && e.Invoke2 != null) return e.Invoke2(a, b);
                if (arity == 3 && e.Invoke3 != null) return e.Invoke3(a, b, c);
                if (e.InvokeN != null)
                {
                    object[] arr = arity <= 4 ? Pool[arity] : new object[arity];
                    if (arity > 0) arr[0] = a;
                    if (arity > 1) arr[1] = b;
                    if (arity > 2) arr[2] = c;
                    if (arity > 3) arr[3] = d;
                    try { return e.InvokeN(arr); }
                    finally
                    {
                        if (arity <= 4)
                        {
                            for (int i = 0; i < arity; i++) arr[i] = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Warn(e.ModId, "hook", ex);
            }
            return null;
        }

        private static void Rebuild(string hook, List<Entry> list)
        {
            list.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(a.ModId, b.ModId));
            Snapshots[hook] = list.ToArray();
        }

        private static Entry Adapt(string modId, int priority, Delegate handler)
        {
            var entry = new Entry { ModId = modId, Priority = priority };

            if (handler is Func<object> f0) { entry.Arity = 0; entry.Invoke0 = f0; return entry; }
            if (handler is Func<object, object> f1) { entry.Arity = 1; entry.Invoke1 = f1; return entry; }
            if (handler is Func<object, object, object> f2) { entry.Arity = 2; entry.Invoke2 = f2; return entry; }
            if (handler is Func<object, object, object, object> f3) { entry.Arity = 3; entry.Invoke3 = f3; return entry; }

            if (handler is Func<BaseEntity, HitInfo, object> feh)
            {
                entry.Arity = 2;
                entry.Invoke2 = (a, b) => feh(a as BaseEntity, b as HitInfo);
                return entry;
            }
            if (handler is Func<BaseCombatEntity, HitInfo, object> fch)
            {
                entry.Arity = 2;
                entry.Invoke2 = (a, b) => fch(a as BaseCombatEntity, b as HitInfo);
                return entry;
            }
            if (handler is Func<BaseEntity, BaseEntity, object> fee)
            {
                entry.Arity = 2;
                entry.Invoke2 = (a, b) => fee(a as BaseEntity, b as BaseEntity);
                return entry;
            }
            if (handler is Func<BaseNetworkable, object> fn)
            {
                entry.Arity = 1;
                entry.Invoke1 = a => fn(a as BaseNetworkable);
                return entry;
            }

            var ps = handler.Method.GetParameters();
            int n = ps?.Length ?? 0;
            try
            {
                if (n == 0)
                {
                    var d = (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), handler.Target, handler.Method);
                    entry.Arity = 0;
                    entry.Invoke0 = d;
                    return entry;
                }
                if (n == 2 && typeof(HitInfo).IsAssignableFrom(ps[1].ParameterType))
                {
                    var d = (Func<BaseEntity, HitInfo, object>)Delegate.CreateDelegate(typeof(Func<BaseEntity, HitInfo, object>), handler.Target, handler.Method);
                    entry.Arity = 2;
                    entry.Invoke2 = (a, b) => d(a as BaseEntity, b as HitInfo);
                    return entry;
                }
                if (n == 2)
                {
                    var d = (Func<BaseEntity, BaseEntity, object>)Delegate.CreateDelegate(typeof(Func<BaseEntity, BaseEntity, object>), handler.Target, handler.Method);
                    entry.Arity = 2;
                    entry.Invoke2 = (a, b) => d(a as BaseEntity, b as BaseEntity);
                    return entry;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void Warn(string modId, string hook, Exception ex)
            => Debug.LogWarning("[GrimmCore] Hook " + hook + " " + modId + ": " + ex.Message);
    }
}
