using System;
using System.Reflection;
using UnityEngine;

namespace PveModeHarmony
{
    /// <summary>
    /// Best-effort reflective bridges to optional Friends/Clans systems, resolved purely through
    /// the current AppDomain (no hard assembly dependency). Rust teams are the primary/required
    /// relationship check (see PveModeManager.IsTeam); these are soft extras that no-op when the
    /// target mod/plugin isn't loaded.
    /// </summary>
    public static class PveModeSocial
    {
        public static readonly FriendsBridge Friends = new FriendsBridge();
        public static readonly ClansBridge Clans = new ClansBridge();

        internal static Type ResolveType(params string[] typeNames)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string name in typeNames)
                {
                    try
                    {
                        Type t = asm.GetType(name, false);
                        if (t != null) return t;
                    }
                    catch { }
                }
            }
            return null;
        }

        internal static object ResolveInstance(Type type)
        {
            if (type == null) return null;
            try
            {
                object inst = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (inst != null) return inst;
            }
            catch { }
            return null;
        }

        public abstract class Bridge
        {
            private Type _type;
            private object _instance;
            private double _lastRebind;

            protected abstract string[] TypeNames { get; }

            private void EnsureBound()
            {
                double now = Time.realtimeSinceStartup;
                if (_type != null && now - _lastRebind < 30d) return;
                _lastRebind = now;
                _type = ResolveType(TypeNames);
                _instance = ResolveInstance(_type);
            }

            public bool IsLoaded { get { EnsureBound(); return _type != null; } }

            protected MethodInfo Method(string name, params Type[] argTypes)
            {
                EnsureBound();
                if (_type == null) return null;
                try { return _type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, argTypes, null); }
                catch { return null; }
            }

            protected object Invoke(MethodInfo mi, params object[] args)
            {
                if (mi == null) return null;
                try { return mi.Invoke(mi.IsStatic ? null : _instance, args); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PveMode] social bridge invoke '" + mi.Name + "' failed: " + ex.Message);
                    return null;
                }
            }
        }

        public sealed class FriendsBridge : Bridge
        {
            protected override string[] TypeNames => new[] { "Friends", "FriendsHarmony.FriendsMod", "Oxide.Plugins.Friends" };

            public bool AreFriends(ulong a, ulong b)
            {
                if (!IsLoaded) return false;
                object r = Invoke(Method("AreFriends", typeof(ulong), typeof(ulong)) ?? Method("AreFriends", typeof(string), typeof(string)),
                    a, b);
                return r is bool res && res;
            }
        }

        public sealed class ClansBridge : Bridge
        {
            protected override string[] TypeNames => new[] { "Clans", "ClansHarmony.ClansMod", "Oxide.Plugins.Clans" };

            public bool IsClanMember(ulong a, ulong b)
            {
                if (!IsLoaded) return false;
                object r = Invoke(Method("IsMemberOrAlly", typeof(string), typeof(string)), a.ToString(), b.ToString());
                return r is bool res && res;
            }
        }
    }
}
