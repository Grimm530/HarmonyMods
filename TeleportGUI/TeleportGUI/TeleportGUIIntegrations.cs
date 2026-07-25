using System;
using System.Reflection;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>
    /// Best-effort reflective bridges to optional third-party systems, resolved purely through the
    /// current AppDomain (no Oxide references, no hard assembly dependencies). Each bridge degrades to
    /// a no-op / "not loaded" state when the target type is absent so the mod stays functional.
    ///
    /// Mirrors the optional integrations the Oxide TeleportGUI relies on:
    /// Economics, ServerRewards, Clans, Friends, RaidBlock and ZoneManager.
    /// </summary>
    public static class TeleportGUIIntegrations
    {
        private const string LogTag = "[TeleportGUI]";

        public static readonly EconomicsBridge Economics = new EconomicsBridge();
        public static readonly ServerRewardsBridge ServerRewards = new ServerRewardsBridge();
        public static readonly ClansBridge Clans = new ClansBridge();
        public static readonly FriendsBridge Friends = new FriendsBridge();
        public static readonly RaidBlockBridge RaidBlock = new RaidBlockBridge();
        public static readonly ZoneManagerBridge ZoneManager = new ZoneManagerBridge();

        public static void Initialize()
        {
            Economics.Rebind();
            ServerRewards.Rebind();
            Clans.Rebind();
            Friends.Rebind();
            RaidBlock.Rebind();
            ZoneManager.Rebind();
        }

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
            try
            {
                object inst = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (inst != null) return inst;
            }
            catch { }
            return null;
        }

        public abstract class Bridge
        {
            protected Type Type;
            protected object Instance;
            public bool IsLoaded => Type != null;

            protected abstract string[] TypeNames { get; }

            public void Rebind()
            {
                Type = ResolveType(TypeNames);
                Instance = ResolveInstance(Type);
            }

            protected MethodInfo Method(string name, params Type[] argTypes)
            {
                if (Type == null) return null;
                try
                {
                    return Type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, argTypes, null);
                }
                catch { return null; }
            }

            protected object Invoke(MethodInfo mi, params object[] args)
            {
                if (mi == null) return null;
                try
                {
                    object target = mi.IsStatic ? null : Instance;
                    return mi.Invoke(target, args);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogTag} integration invoke '{mi.Name}' failed: {ex.Message}");
                    return null;
                }
            }
        }

        public sealed class EconomicsBridge : Bridge
        {
            protected override string[] TypeNames => new[]
            {
                "EconomicsHarmony.EconomicsHarmonyMod",
                "Economics",
                "EconomicsHarmony.EconomicsMod",
                "Oxide.Plugins.Economics"
            };

            public double Balance(ulong userId)
            {
                if (!IsLoaded) return 0;
                object r = Invoke(Method("Balance", typeof(ulong)) ?? Method("Balance", typeof(string)), Arg(userId));
                return r is double d ? d : (r is int i ? i : 0);
            }

            public bool Withdraw(ulong userId, double amount)
            {
                if (!IsLoaded) return false;
                object r = Invoke(Method("Withdraw", typeof(ulong), typeof(double)) ?? Method("Withdraw", typeof(string), typeof(double)), Arg(userId), amount);
                return r is bool b ? b : true;
            }

            public void Deposit(ulong userId, double amount)
            {
                if (!IsLoaded) return;
                Invoke(Method("Deposit", typeof(ulong), typeof(double)) ?? Method("Deposit", typeof(string), typeof(double)), Arg(userId), amount);
            }

            private object Arg(ulong userId) =>
                (Method("Balance", typeof(string)) != null && Method("Balance", typeof(ulong)) == null) ? (object)userId.ToString() : userId;
        }

        public sealed class ServerRewardsBridge : Bridge
        {
            protected override string[] TypeNames => new[] { "ServerRewards", "ServerRewardsHarmony.ServerRewardsMod", "Oxide.Plugins.ServerRewards" };

            public int CheckPoints(ulong userId)
            {
                if (!IsLoaded) return 0;
                object r = Invoke(Method("CheckPoints", typeof(ulong)), userId);
                return r is int i ? i : 0;
            }

            public void TakePoints(ulong userId, int amount)
            {
                if (!IsLoaded) return;
                Invoke(Method("TakePoints", typeof(ulong), typeof(int)), userId, amount);
            }

            public void AddPoints(ulong userId, int amount)
            {
                if (!IsLoaded) return;
                Invoke(Method("AddPoints", typeof(ulong), typeof(int)), userId, amount);
            }
        }

        public sealed class ClansBridge : Bridge
        {
            protected override string[] TypeNames => new[] { "Clans", "ClansHarmony.ClansMod", "Oxide.Plugins.Clans" };

            public bool IsClanMember(ulong a, ulong b)
            {
                if (!IsLoaded) return false;
                object r = Invoke(Method("IsClanMember", typeof(ulong), typeof(ulong)) ?? Method("IsClanMember", typeof(string), typeof(string)),
                    a, b);
                return r is bool res && res;
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

        public sealed class RaidBlockBridge : Bridge
        {
            protected override string[] TypeNames => new[] { "NoEscape", "RaidBlock", "RaidBlockHarmony.RaidBlockMod", "Oxide.Plugins.NoEscape" };

            public bool IsRaidBlocked(BasePlayer player)
            {
                if (!IsLoaded || player == null) return false;
                object r = Invoke(Method("IsRaidBlocked", typeof(BasePlayer)) ?? Method("IsEscapeBlocked", typeof(BasePlayer)), player);
                return r is bool res && res;
            }
        }

        public sealed class ZoneManagerBridge : Bridge
        {
            protected override string[] TypeNames => new[] { "ZoneManager", "ZoneManagerHarmony.ZoneManagerMod", "Oxide.Plugins.ZoneManager" };

            public bool PlayerHasFlag(BasePlayer player, string flag)
            {
                if (!IsLoaded || player == null) return false;
                object r = Invoke(Method("PlayerHasFlag", typeof(BasePlayer), typeof(string)), player, flag);
                return r is bool res && res;
            }
        }
    }
}
