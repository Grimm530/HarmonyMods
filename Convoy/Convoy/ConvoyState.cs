using System.Collections.Generic;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Shared state: convoy crates, NPC preset per entity, convoy state (moving, NPCs/Bradley/Heli alive), event lock (team + 15min timeout).
    /// ConvoyMod sets state; patches and applier use this.
    /// </summary>
    public static class ConvoyState
    {
        private static readonly HashSet<ulong> ConvoyCrateNetIds = new HashSet<ulong>();
        private static readonly Dictionary<ulong, string> NpcPresetByNetId = new Dictionary<ulong, string>();
        private static readonly HashSet<ulong> ConvoyCorpseNetIds = new HashSet<ulong>();
        private static readonly List<ulong> PendingConvoyNpcDeaths = new List<ulong>();
        private static readonly object Lock = new object();

        /// <summary>Per-team damage dealt to convoy entities this event. Cleared on Clear().</summary>
        private static readonly Dictionary<ulong, float> TeamDamage = new Dictionary<ulong, float>();

        /// <summary>Event locked to this team (0 = not locked). Only this team can loot/hack convoy entities.</summary>
        public static ulong LockedTeamId { get; private set; }

        /// <summary>Last time the locked team dealt damage. If no damage for EventLockUnlockAfterSeconds, event unlocks.</summary>
        public static float LastLockDamageTime { get; private set; }

        public static bool IsMoving { get; set; }
        public static bool NpcsAlive { get; set; }
        public static bool BradleyAlive { get; set; }
        public static bool HeliAlive { get; set; }

        public static void RegisterConvoyCrate(ulong netId)
        {
            lock (Lock) { ConvoyCrateNetIds.Add(netId); }
        }

        public static void UnregisterConvoyCrate(ulong netId)
        {
            lock (Lock) { ConvoyCrateNetIds.Remove(netId); }
        }

        public static bool IsConvoyCrate(ulong netId)
        {
            lock (Lock) { return ConvoyCrateNetIds.Contains(netId); }
        }

        public static void RegisterNpcPreset(ulong npcNetId, string presetName)
        {
            if (npcNetId == 0 || string.IsNullOrEmpty(presetName)) return;
            lock (Lock) { NpcPresetByNetId[npcNetId] = presetName; }
        }

        public static void UnregisterNpc(ulong npcNetId)
        {
            lock (Lock) { NpcPresetByNetId.Remove(npcNetId); }
        }

        public static string GetNpcPresetName(ulong npcNetId)
        {
            lock (Lock)
            {
                return NpcPresetByNetId.TryGetValue(npcNetId, out var name) ? name : null;
            }
        }

        public static bool IsConvoyCorpse(ulong netId)
        {
            lock (Lock) { return ConvoyCorpseNetIds.Contains(netId); }
        }

        public static void RegisterConvoyCorpse(ulong corpseNetId)
        {
            if (corpseNetId == 0) return;
            lock (Lock) { ConvoyCorpseNetIds.Add(corpseNetId); }
        }

        /// <summary>Call when a convoy NPC is about to die (Die postfix); next LootableCorpse spawn will be registered.</summary>
        public static void NotifyConvoyNpcDeath(ulong npcNetId)
        {
            if (npcNetId == 0) return;
            lock (Lock) { PendingConvoyNpcDeaths.Add(npcNetId); }
        }

        /// <summary>Call from BaseNetworkable.Spawn postfix when a LootableCorpse spawns; associates with a pending convoy NPC death.</summary>
        public static void TryRegisterConvoyCorpse(ulong corpseNetId)
        {
            lock (Lock)
            {
                if (PendingConvoyNpcDeaths.Count == 0) return;
                PendingConvoyNpcDeaths.RemoveAt(0);
                ConvoyCorpseNetIds.Add(corpseNetId);
            }
        }

        /// <summary>Record damage from a player's team to a convoy entity. Returns true if event just became locked to this team.</summary>
        public static bool RecordDamage(ulong teamId, float damage, float damageThreshold, out float totalAfter)
        {
            totalAfter = 0f;
            if (teamId == 0 || damage <= 0f) return false;
            lock (Lock)
            {
                if (!TeamDamage.TryGetValue(teamId, out var total))
                    total = 0f;
                total += damage;
                TeamDamage[teamId] = total;
                totalAfter = total;
                if (LockedTeamId == 0 && damageThreshold > 0f && total >= damageThreshold)
                {
                    LockedTeamId = teamId;
                    LastLockDamageTime = Time.time;
                    return true;
                }
                if (LockedTeamId == teamId)
                    LastLockDamageTime = Time.time;
            }
            return false;
        }

        /// <summary>If event is locked and last damage was more than unlockSeconds ago, clear lock. Call before any lock check.</summary>
        public static void EnsureLockExpiry(float unlockSeconds)
        {
            if (LockedTeamId == 0) return;
            if (Time.time - LastLockDamageTime >= unlockSeconds)
            {
                lock (Lock)
                {
                    if (Time.time - LastLockDamageTime >= unlockSeconds)
                    {
                        LockedTeamId = 0;
                    }
                }
            }
        }

        /// <summary>True if the player is in the team that has the event lock.</summary>
        public static bool IsLockedToPlayerTeam(BasePlayer player)
        {
            if (player == null || LockedTeamId == 0) return false;
            return player.currentTeam != 0 && player.currentTeam == LockedTeamId;
        }

        /// <summary>True if entity (by netId) is a convoy crate, convoy NPC, or convoy corpse.</summary>
        public static bool IsConvoyEntity(ulong netId)
        {
            lock (Lock)
            {
                return ConvoyCrateNetIds.Contains(netId)
                    || NpcPresetByNetId.ContainsKey(netId)
                    || ConvoyCorpseNetIds.Contains(netId);
            }
        }

        public static void SetConvoyState(bool moving, bool npcsAlive, bool bradleyAlive, bool heliAlive)
        {
            IsMoving = moving;
            NpcsAlive = npcsAlive;
            BradleyAlive = bradleyAlive;
            HeliAlive = heliAlive;
        }

        public static void Clear()
        {
            lock (Lock)
            {
                ConvoyCrateNetIds.Clear();
                NpcPresetByNetId.Clear();
                ConvoyCorpseNetIds.Clear();
                PendingConvoyNpcDeaths.Clear();
                TeamDamage.Clear();
            }
            LockedTeamId = 0;
            LastLockDamageTime = 0f;
            IsMoving = false;
            NpcsAlive = false;
            BradleyAlive = false;
            HeliAlive = false;
        }
    }
}
