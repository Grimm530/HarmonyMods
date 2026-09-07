using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrimmCoreHarmony
{
    public delegate bool? HurtPrefixHandler(BaseCombatEntity entity, HitInfo info);
    public delegate void HurtSideEffectHandler(BaseCombatEntity entity, HitInfo info);
    public delegate void HurtPostfixHandler(BaseCombatEntity entity, HitInfo info);

    /// <summary>
    /// Single ordered handler list for BaseCombatEntity.Hurt. One Harmony prefix owns the patch.
    /// </summary>
    public static class GrimmCoreHurtDispatcher
    {
        private sealed class SideEffectEntry
        {
            public string ModId;
            public int Priority;
            public HurtSideEffectHandler Handler;
        }

        private sealed class PrefixEntry
        {
            public string ModId;
            public int Priority;
            public HurtPrefixHandler Handler;
        }

        private sealed class PostfixEntry
        {
            public string ModId;
            public int Priority;
            public HurtPostfixHandler Handler;
        }

        private static readonly List<SideEffectEntry> SideEffects = new List<SideEffectEntry>(8);
        private static readonly List<PrefixEntry> Prefixes = new List<PrefixEntry>(32);
        private static readonly List<PostfixEntry> Postfixes = new List<PostfixEntry>(16);
        private static SideEffectEntry[] _sideSnapshot = Array.Empty<SideEffectEntry>();
        private static PrefixEntry[] _prefixSnapshot = Array.Empty<PrefixEntry>();
        private static PostfixEntry[] _postfixSnapshot = Array.Empty<PostfixEntry>();

        public static void RegisterPrefix(string modId, int priority, HurtPrefixHandler handler)
        {
            if (string.IsNullOrEmpty(modId) || handler == null) return;
            lock (SideEffects)
            {
                Prefixes.RemoveAll(e => e.ModId == modId);
                Prefixes.Add(new PrefixEntry { ModId = modId, Priority = priority, Handler = handler });
                RebuildSnapshots();
            }
        }

        public static void RegisterSideEffect(string modId, int priority, HurtSideEffectHandler handler)
        {
            if (string.IsNullOrEmpty(modId) || handler == null) return;
            lock (SideEffects)
            {
                SideEffects.RemoveAll(e => e.ModId == modId);
                SideEffects.Add(new SideEffectEntry { ModId = modId, Priority = priority, Handler = handler });
                RebuildSnapshots();
            }
        }

        public static void RegisterPostfix(string modId, int priority, HurtPostfixHandler handler)
        {
            if (string.IsNullOrEmpty(modId) || handler == null) return;
            lock (SideEffects)
            {
                Postfixes.RemoveAll(e => e.ModId == modId);
                Postfixes.Add(new PostfixEntry { ModId = modId, Priority = priority, Handler = handler });
                RebuildSnapshots();
            }
        }

        public static void UnregisterMod(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            lock (SideEffects)
            {
                SideEffects.RemoveAll(e => e.ModId == modId);
                Prefixes.RemoveAll(e => e.ModId == modId);
                Postfixes.RemoveAll(e => e.ModId == modId);
                RebuildSnapshots();
            }
        }

        public static void ClearAll()
        {
            lock (SideEffects)
            {
                SideEffects.Clear();
                Prefixes.Clear();
                Postfixes.Clear();
                RebuildSnapshots();
            }
        }

        /// <summary>
        /// AppDomain registration from linked GrimmCoreBridge copies. Those define their own
        /// GrimmCoreHurt*Handler delegates (same signature, different type identity), so a strict
        /// <c>is HurtPrefixHandler</c> check silently dropped every foreign mod — including TruePVE,
        /// which left PvP open while Patch_SuppressVanillaPve disabled stock server.pve reflect.
        /// </summary>
        public static void RegisterPrefixUntyped(string modId, int priority, Delegate handler)
        {
            if (TryAdapt(handler, out HurtPrefixHandler typed))
                RegisterPrefix(modId, priority, typed);
            else
                WarnRegister(modId, "prefix", handler);
        }

        public static void RegisterSideEffectUntyped(string modId, int priority, Delegate handler)
        {
            if (TryAdapt(handler, out HurtSideEffectHandler typed))
                RegisterSideEffect(modId, priority, typed);
            else
                WarnRegister(modId, "side", handler);
        }

        public static void RegisterPostfixUntyped(string modId, int priority, Delegate handler)
        {
            if (TryAdapt(handler, out HurtPostfixHandler typed))
                RegisterPostfix(modId, priority, typed);
            else
                WarnRegister(modId, "postfix", handler);
        }

        private static bool TryAdapt<T>(Delegate handler, out T typed) where T : class
        {
            typed = null;
            if (handler == null) return false;
            if (handler is T exact)
            {
                typed = exact;
                return true;
            }
            try
            {
                // Rebind MethodInfo onto our delegate type (cross-assembly bridge delegates).
                typed = (T)(object)Delegate.CreateDelegate(typeof(T), handler.Target, handler.Method);
                return typed != null;
            }
            catch
            {
                return false;
            }
        }

        private static void WarnRegister(string modId, string kind, Delegate handler)
        {
            string got = handler == null ? "null" : handler.GetType().FullName;
            Debug.LogWarning("[GrimmCore] Hurt " + kind + " register failed for " + modId + " (handler=" + got + ")");
        }

        /// <summary>Harmony prefix: false = block Hurt, true = continue to original + postfixes.</summary>
        public static bool InvokePrefix(BaseCombatEntity entity, HitInfo info, ref bool blocked)
        {
            blocked = false;
            if (entity == null || info == null)
                return true;

            var side = _sideSnapshot;
            for (int i = 0; i < side.Length; i++)
            {
                try { side[i].Handler(entity, info); }
                catch (Exception ex) { Warn(side[i].ModId, "side", ex); }
            }

            var prefix = _prefixSnapshot;
            for (int i = 0; i < prefix.Length; i++)
            {
                bool? result;
                try { result = prefix[i].Handler(entity, info); }
                catch (Exception ex)
                {
                    Warn(prefix[i].ModId, "prefix", ex);
                    continue;
                }

                if (result == true)
                {
                    blocked = true;
                    return false;
                }
            }

            return true;
        }

        public static void InvokePostfix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;
            var postfix = _postfixSnapshot;
            for (int i = 0; i < postfix.Length; i++)
            {
                try { postfix[i].Handler(entity, info); }
                catch (Exception ex) { Warn(postfix[i].ModId, "postfix", ex); }
            }
        }

        private static void RebuildSnapshots()
        {
            SideEffects.Sort(CompareSide);
            Prefixes.Sort(ComparePrefix);
            Postfixes.Sort(ComparePostfix);
            _sideSnapshot = SideEffects.ToArray();
            _prefixSnapshot = Prefixes.ToArray();
            _postfixSnapshot = Postfixes.ToArray();
        }

        private static int CompareSide(SideEffectEntry a, SideEffectEntry b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(a.ModId, b.ModId);
        private static int ComparePrefix(PrefixEntry a, PrefixEntry b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(a.ModId, b.ModId);
        private static int ComparePostfix(PostfixEntry a, PostfixEntry b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(a.ModId, b.ModId);

        private static void Warn(string modId, string phase, Exception ex)
            => Debug.LogWarning("[GrimmCore] Hurt " + phase + " " + modId + ": " + ex.Message);
    }
}
