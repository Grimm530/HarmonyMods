using System.Collections.Generic;
using UnityEngine;

namespace ServerIdentityGraph
{
    /// <summary>
    /// Captures in-game team + clan. BattleMetrics already has names, IPs, and alts.
    /// Clan name/members come from vanilla Facepunch IClan (same roster idea as teams, plus a name).
    /// </summary>
    internal static class IdentityCollector
    {
        internal static bool Ready { get; set; }

        internal static void Record(BasePlayer player)
        {
            if (!Ready || player == null || player.IsNpc || player.IsDestroyed)
                return;

            ulong steamId = player.userID;
            if (steamId == 0)
                return;

            string now = IdentityStore.NowIso();
            lock (IdentityStore.Gate)
            {
                var record = IdentityStore.GetOrLoad(steamId);
                record.Name = player.displayName ?? record.Name;
                record.LastSeen = now;
                record.Team = BuildTeam(steamId, now);
                record.Clan = BuildClan(player, now);
                IdentityStore.MarkDirty(steamId);
            }
        }

        internal static void RecordAllOnline()
        {
            var list = BasePlayer.activePlayerList;
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
                Record(list[i]);
        }

        internal static void RecordTeam(RelationshipManager.PlayerTeam team, ulong playerId)
        {
            if (!Ready || team == null || playerId == 0)
                return;

            string now = IdentityStore.NowIso();
            lock (IdentityStore.Gate)
            {
                var record = IdentityStore.GetOrLoad(playerId);
                record.LastSeen = now;
                record.Team = FromTeam(team, now);
                IdentityStore.MarkDirty(playerId);
            }
        }

        static TeamSighting BuildTeam(ulong steamId, string now)
        {
            try
            {
                var team = RelationshipManager.ServerInstance?.FindPlayersTeam(steamId);
                return team == null ? null : FromTeam(team, now);
            }
            catch
            {
                return null;
            }
        }

        static TeamSighting FromTeam(RelationshipManager.PlayerTeam team, string now)
        {
            if (team == null)
                return null;

            var members = CopyMembers(team.members);
            if (members.Count == 0)
                return null;

            return new TeamSighting
            {
                TeamId = team.teamID.ToString(),
                Leader = MakeMember(team.teamLeader),
                Members = members,
                SeenAt = now
            };
        }

        static ClanSighting BuildClan(BasePlayer player, string now)
        {
            if (player == null)
                return null;

            IClan clan = player.serverClan;
            long clanId = player.clanId;
            if (clan == null && clanId != 0L)
            {
                try
                {
                    ClanManager.ServerInstance?.Backend?.TryGet(clanId, out clan);
                }
                catch
                {
                    clan = null;
                }
            }

            if (clan == null)
                return null;

            var members = new List<MemberSighting>();
            try
            {
                var clanMembers = clan.Members;
                if (clanMembers != null)
                {
                    foreach (ClanMember member in clanMembers)
                    {
                        if (member.SteamId != 0)
                            members.Add(MakeMember(member.SteamId));
                    }
                }
            }
            catch
            {
                // ignore member enumeration failures
            }

            return new ClanSighting
            {
                ClanId = clan.ClanId.ToString(),
                Name = clan.Name ?? "",
                Members = members,
                SeenAt = now
            };
        }

        static List<MemberSighting> CopyMembers(List<ulong> ids)
        {
            var result = new List<MemberSighting>();
            if (ids == null)
                return result;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] != 0)
                    result.Add(MakeMember(ids[i]));
            }
            return result;
        }

        static MemberSighting MakeMember(ulong steamId)
        {
            return new MemberSighting
            {
                SteamId = steamId.ToString(),
                Name = NameOf(steamId)
            };
        }

        static string NameOf(ulong steamId)
        {
            if (steamId == 0)
                return "";
            try
            {
                var online = BasePlayer.FindByID(steamId);
                if (online != null && !string.IsNullOrEmpty(online.displayName))
                    return online.displayName;
                var sleeping = BasePlayer.FindSleeping(steamId);
                if (sleeping != null && !string.IsNullOrEmpty(sleeping.displayName))
                    return sleeping.displayName;
                var persisted = SingletonComponent<ServerMgr>.Instance?.persistance?.GetPlayerName(steamId);
                if (!string.IsNullOrEmpty(persisted))
                    return persisted;
            }
            catch
            {
                // ignore
            }
            return "";
        }
    }
}
