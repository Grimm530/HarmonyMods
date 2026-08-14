using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DynamicCupShareHarmony
{
    /// <summary>
    /// Reflective Clans / Friends bridges plus vanilla ClanManager fallback.
    /// No compile-time Oxide or third-party assembly references.
    /// </summary>
    public static class SocialBridges
    {
        public static readonly ClansBridge Clans = new ClansBridge();
        public static readonly FriendsBridge Friends = new FriendsBridge();

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
            private Type _type;
            private object _instance;
            private double _lastRebind;

            protected abstract string[] TypeNames { get; }

            protected void EnsureBound()
            {
                double now = Time.realtimeSinceStartup;
                if (_type != null && now - _lastRebind < 30d) return;
                _lastRebind = now;
                _type = ResolveType(TypeNames);
                _instance = ResolveInstance(_type);
            }

            public bool IsLoaded
            {
                get
                {
                    EnsureBound();
                    return _type != null;
                }
            }

            protected MethodInfo Method(string name, params Type[] argTypes)
            {
                EnsureBound();
                if (_type == null) return null;
                try
                {
                    return _type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, argTypes, null);
                }
                catch { return null; }
            }

            protected object Invoke(MethodInfo mi, params object[] args)
            {
                if (mi == null) return null;
                try
                {
                    return mi.Invoke(mi.IsStatic ? null : _instance, args);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DynamicCupShare] social bridge invoke '" + mi.Name + "' failed: " + ex.Message);
                    return null;
                }
            }
        }

        public sealed class FriendsBridge : Bridge
        {
            protected override string[] TypeNames => new[]
            {
                "FriendsHarmony.FriendsMod",
                "Friends",
                "Oxide.Plugins.Friends"
            };

            public bool HasFriend(ulong owner, ulong player)
            {
                if (!IsLoaded) return false;
                object r = Invoke(
                    Method("HasFriend", typeof(ulong), typeof(ulong))
                    ?? Method("AreFriends", typeof(ulong), typeof(ulong))
                    ?? Method("HasFriend", typeof(string), typeof(string))
                    ?? Method("AreFriends", typeof(string), typeof(string)),
                    UseString(owner, player) ? (object)owner.ToString() : owner,
                    UseString(owner, player) ? (object)player.ToString() : player);
                return r is bool b && b;
            }

            public bool AreMutualFriends(ulong owner, ulong player)
            {
                if (!IsLoaded) return false;
                MethodInfo mutual = Method("AreFriends", typeof(ulong), typeof(ulong))
                    ?? Method("AreFriends", typeof(string), typeof(string));
                if (mutual != null)
                {
                    object r = mutual.GetParameters().Length > 0 && mutual.GetParameters()[0].ParameterType == typeof(string)
                        ? Invoke(mutual, owner.ToString(), player.ToString())
                        : Invoke(mutual, owner, player);
                    if (r is bool b) return b;
                }
                return HasFriend(owner, player) && HasFriend(player, owner);
            }

            public ulong[] GetFriends(ulong playerId)
            {
                if (!IsLoaded) return Array.Empty<ulong>();
                object r = Invoke(
                    Method("GetFriends", typeof(ulong)) ?? Method("GetFriends", typeof(string)),
                    Method("GetFriends", typeof(ulong)) != null ? (object)playerId : playerId.ToString());
                if (r is ulong[] arr) return arr;
                if (r is IList list)
                {
                    var result = new List<ulong>(list.Count);
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (TryToUlong(list[i], out ulong id))
                            result.Add(id);
                    }
                    return result.ToArray();
                }
                return Array.Empty<ulong>();
            }

            private bool UseString(ulong a, ulong b)
            {
                return Method("HasFriend", typeof(ulong), typeof(ulong)) == null
                    && Method("AreFriends", typeof(ulong), typeof(ulong)) == null;
            }
        }

        public sealed class ClansBridge : Bridge
        {
            protected override string[] TypeNames => new[]
            {
                "ClansHarmony.ClansMod",
                "Clans",
                "Oxide.Plugins.Clans"
            };

            public bool PluginLoaded => IsLoaded;

            public bool IsAvailable => IsLoaded || NativeClansEnabled();

            public bool IsClanMember(ulong owner, ulong player)
            {
                if (IsLoaded)
                {
                    object r = Invoke(
                        Method("IsClanMember", typeof(ulong), typeof(ulong))
                        ?? Method("IsClanMember", typeof(string), typeof(string)),
                        Method("IsClanMember", typeof(ulong), typeof(ulong)) != null ? (object)owner : owner.ToString(),
                        Method("IsClanMember", typeof(ulong), typeof(ulong)) != null ? (object)player : player.ToString());
                    if (r is bool b) return b;
                }
                return NativeSameClan(owner, player);
            }

            public bool IsMemberOrAlly(ulong owner, ulong player)
            {
                if (IsLoaded)
                {
                    object r = Invoke(
                        Method("IsMemberOrAlly", typeof(ulong), typeof(ulong))
                        ?? Method("IsMemberOrAlly", typeof(string), typeof(string)),
                        Method("IsMemberOrAlly", typeof(ulong), typeof(ulong)) != null ? (object)owner : owner.ToString(),
                        Method("IsMemberOrAlly", typeof(ulong), typeof(ulong)) != null ? (object)player : player.ToString());
                    if (r is bool b) return b;
                }
                return NativeSameClan(owner, player);
            }

            public string GetClanOf(ulong playerId)
            {
                if (IsLoaded)
                {
                    object r = Invoke(
                        Method("GetClanOf", typeof(ulong)) ?? Method("GetClanOf", typeof(string)),
                        Method("GetClanOf", typeof(ulong)) != null ? (object)playerId : playerId.ToString());
                    if (r is string s && !string.IsNullOrEmpty(s))
                        return s;
                }
                return NativeClanTag(playerId);
            }

            public void GetMembers(ulong playerId, List<ulong> list)
            {
                if (list == null) return;

                if (IsLoaded)
                {
                    string tag = GetClanOf(playerId);
                    if (!string.IsNullOrEmpty(tag))
                    {
                        object clan = Invoke(Method("GetClan", typeof(string)), tag);
                        if (TryAddMembersFromClanObject(clan, list) && list.Count > 0)
                            return;
                    }

                    object members = Invoke(
                        Method("GetClanMembers", typeof(ulong)) ?? Method("GetClanMembers", typeof(string)),
                        Method("GetClanMembers", typeof(ulong)) != null ? (object)playerId : playerId.ToString());
                    if (TryAddMembersFromClanObject(members, list) && list.Count > 0)
                        return;
                }

                AddNativeMembers(playerId, list);
            }

            public void GetMembersByTag(string tag, List<ulong> list)
            {
                if (list == null || string.IsNullOrEmpty(tag) || !IsLoaded) return;
                object clan = Invoke(Method("GetClan", typeof(string)), tag);
                TryAddMembersFromClanObject(clan, list);
            }

            private static bool TryAddMembersFromClanObject(object clan, List<ulong> list)
            {
                if (clan == null) return false;

                if (clan is JObject jObj)
                {
                    JArray members = jObj["members"] as JArray;
                    if (members == null) return false;
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (TryToUlong(members[i], out ulong id))
                            list.Add(id);
                    }
                    return true;
                }

                if (clan is IDictionary dict)
                {
                    object members = dict.Contains("members") ? dict["members"] : null;
                    return AddEnumerable(members, list);
                }

                if (clan is IEnumerable enumerable && clan is not string)
                    return AddEnumerable(enumerable, list);

                try
                {
                    object members = clan.GetType().GetProperty("members", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                        ?.GetValue(clan);
                    if (members != null)
                        return AddEnumerable(members, list);
                }
                catch { }

                return false;
            }

            private static bool AddEnumerable(object members, List<ulong> list)
            {
                if (members is not IEnumerable enumerable || members is string)
                    return false;
                bool any = false;
                foreach (object item in enumerable)
                {
                    if (TryToUlong(item, out ulong id))
                    {
                        list.Add(id);
                        any = true;
                    }
                }
                return any;
            }
        }

        internal static bool TryToUlong(object value, out ulong id)
        {
            id = 0UL;
            if (value == null) return false;
            if (value is ulong u) { id = u; return true; }
            if (value is long l && l >= 0) { id = (ulong)l; return true; }
            if (value is JValue jv) return TryToUlong(jv.Value, out id);
            return ulong.TryParse(value.ToString(), out id);
        }

        private static bool NativeClansEnabled()
        {
            try { return ConVar.Clan.enabled && ClanManager.ServerInstance != null; }
            catch { return false; }
        }

        private static bool NativeSameClan(ulong owner, ulong player)
        {
            if (!NativeClansEnabled() || owner == 0UL || player == 0UL)
                return false;
            if (owner == player) return true;

            BasePlayer a = BasePlayer.FindByID(owner) ?? BasePlayer.FindSleeping(owner);
            BasePlayer b = BasePlayer.FindByID(player) ?? BasePlayer.FindSleeping(player);
            if (a != null && b != null && a.clanId != 0L && a.clanId == b.clanId)
                return true;

            if (a?.serverClan?.Members != null)
            {
                foreach (var member in a.serverClan.Members)
                {
                    if (member.SteamId == player) return true;
                }
            }
            if (b?.serverClan?.Members != null)
            {
                foreach (var member in b.serverClan.Members)
                {
                    if (member.SteamId == owner) return true;
                }
            }
            return false;
        }

        private static string NativeClanTag(ulong playerId)
        {
            BasePlayer player = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
            if (player == null || player.clanId == 0L) return null;
            try
            {
                IClan clan = player.serverClan;
                if (clan == null && ClanManager.ServerInstance?.Backend != null)
                    ClanManager.ServerInstance.Backend.TryGet(player.clanId, out clan);
                if (clan != null && !string.IsNullOrEmpty(clan.Name))
                    return clan.Name;
            }
            catch { }
            return player.clanId.ToString();
        }

        private static void AddNativeMembers(ulong playerId, List<ulong> list)
        {
            if (!NativeClansEnabled()) return;
            BasePlayer player = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
            IClan clan = player?.serverClan;
            if (clan == null && player != null && player.clanId != 0L && ClanManager.ServerInstance?.Backend != null)
                ClanManager.ServerInstance.Backend.TryGet(player.clanId, out clan);
            if (clan?.Members == null) return;
            foreach (var member in clan.Members)
                list.Add(member.SteamId);
        }
    }
}
