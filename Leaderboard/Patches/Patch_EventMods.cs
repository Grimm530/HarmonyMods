using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// Records event/raid completions from optional Harmony event mods via reflection
/// (no hard assembly refs). Patched deferred from LeaderboardMod.OnLoaded so target
/// mods can finish loading first.
/// </summary>
internal static class EventRecording
{
    public static void RecordEvent(ulong userId, string eventName, float amount = 1f)
    {
        if (userId == 0 || string.IsNullOrEmpty(eventName)) return;
        if (!SteamIdHelper.IsSteamId(userId)) return;
        LeaderboardMod.Instance?.RecordStat(userId, LootType.Event, eventName, amount);
    }

    public static void RecordRaidableBase(ulong userId, string mode, float amount = 1f)
    {
        if (userId == 0 || string.IsNullOrEmpty(mode)) return;
        if (!SteamIdHelper.IsSteamId(userId)) return;
        LeaderboardMod.Instance?.RecordStat(userId, LootType.RaidableBases, mode.ToLowerInvariant(), amount);
    }
}

public static class EventModPatches
{
    private static bool _applied;

    /// <summary>Call after other Harmony mods have loaded (deferred from OnLoaded).</summary>
    public static void TryApply(HarmonyLib.Harmony harmony)
    {
        if (_applied || harmony == null) return;
        int n = 0;
        n += TryPatch(harmony, FindType("RaidableBases.RaidableBases+RaidableBase", "Oxide.Plugins.RaidableBases+RaidableBase"),
            "AwardRaiders", new HarmonyMethod(typeof(EventModPatches), nameof(Postfix_RaidableBases_AwardRaiders))) ? 1 : 0;
        n += TryPatch(harmony, FindType("Convoy.EventLauncher"),
            "StopEvent", prefix: new HarmonyMethod(typeof(EventModPatches), nameof(Prefix_Convoy_StopEvent))) ? 1 : 0;
        n += TryPatch(harmony, FindType("Oxide.Plugins.ArmoredTrain+EconomyManager"),
            "DefineEventWinner", prefix: new HarmonyMethod(typeof(EventModPatches), nameof(Prefix_ArmoredTrain_DefineEventWinner))) ? 1 : 0;
        n += TryPatch(harmony, FindType("Oxide.Plugins.CustomHelicopterTiers2"),
            "OnEntityDeath",
            postfix: new HarmonyMethod(typeof(EventModPatches), nameof(Postfix_CHT_OnEntityDeath)),
            paramTypes: new[] { typeof(PatrolHelicopter), typeof(HitInfo) }) ? 1 : 0;
        _applied = true;
        UnityEngine.Debug.Log($"[Leaderboard] Event/raid integration patches applied: {n}/4");
    }

    private static Type FindType(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            var t = AccessTools.TypeByName(names[i]);
            if (t != null) return t;
        }
        // Fallback: scan loaded assemblies (HarmonyLoader renames assemblies).
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            catch { continue; }
            if (types == null) continue;
            for (int i = 0; i < types.Length; i++)
            {
                var t = types[i];
                if (t == null) continue;
                for (int n = 0; n < names.Length; n++)
                {
                    if (string.Equals(t.FullName, names[n], StringComparison.Ordinal))
                        return t;
                }
            }
        }
        return null;
    }

    private static bool TryPatch(HarmonyLib.Harmony harmony, Type type, string methodName,
        HarmonyMethod postfix = null, HarmonyMethod prefix = null, Type[] paramTypes = null)
    {
        if (type == null) return false;
        MethodInfo mi = paramTypes != null
            ? AccessTools.Method(type, methodName, paramTypes)
            : AccessTools.Method(type, methodName);
        if (mi == null)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Event patch: {type.FullName}.{methodName} not found");
            return false;
        }
        try
        {
            harmony.Patch(mi, prefix: prefix, postfix: postfix);
            return true;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Event patch {type.Name}.{methodName}: {ex.Message}");
            return false;
        }
    }

    // --- Patch bodies ---

    public static void Postfix_RaidableBases_AwardRaiders(object __instance)
    {
        if (__instance == null || LeaderboardMod.Instance == null) return;
        try
        {
            var t = __instance.GetType();
            var eligibleField = AccessTools.Field(t, "IsEligible");
            if (eligibleField != null && eligibleField.GetValue(__instance) is bool eligible && !eligible)
                return;

            var options = AccessTools.Field(t, "Options")?.GetValue(__instance);
            string mode = null;
            if (options != null)
                mode = AccessTools.Field(options.GetType(), "Mode")?.GetValue(options) as string;
            if (string.IsNullOrWhiteSpace(mode))
                mode = "easy";

            var credited = new HashSet<ulong>();
            var ownerIdObj = AccessTools.Field(t, "ownerId")?.GetValue(__instance);
            if (ownerIdObj is ulong ownerId && SteamIdHelper.IsSteamId(ownerId))
            {
                EventRecording.RecordRaidableBase(ownerId, mode);
                credited.Add(ownerId);
            }

            var getRaiders = AccessTools.Method(t, "GetRaiders", new[] { typeof(bool) })
                             ?? AccessTools.Method(t, "GetRaiders", Type.EmptyTypes);
            if (getRaiders == null) return;
            object listObj = getRaiders.GetParameters().Length == 1
                ? getRaiders.Invoke(__instance, new object[] { true })
                : getRaiders.Invoke(__instance, null);

            if (listObj is not IEnumerable enumerable) return;
            foreach (var item in enumerable)
            {
                if (item is not BasePlayer bp || bp.IsNpc) continue;
                if (!SteamIdHelper.IsSteamId(bp.userID) || !credited.Add(bp.userID)) continue;
                EventRecording.RecordRaidableBase(bp.userID, mode);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] RaidableBases AwardRaiders: {ex.Message}");
        }
    }

    public static void Prefix_Convoy_StopEvent()
    {
        try
        {
            var pve = FindType("Convoy.PveModeManager");
            if (pve == null) return;
            var prop = AccessTools.Property(pve, "CurrentOwner");
            var owner = prop?.GetValue(null, null) as BasePlayer;
            if (owner == null || owner.IsNpc || !SteamIdHelper.IsSteamId(owner.userID)) return;
            EventRecording.RecordEvent(owner.userID, "Convoy");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Convoy StopEvent: {ex.Message}");
        }
    }

    public static void Prefix_ArmoredTrain_DefineEventWinner()
    {
        try
        {
            var t = FindType("Oxide.Plugins.ArmoredTrain+EconomyManager");
            if (t == null) return;
            var field = AccessTools.Field(t, "PlayersBalance");
            if (field?.GetValue(null) is not IDictionary balance || balance.Count == 0) return;

            ulong bestId = 0;
            double bestVal = 0;
            foreach (DictionaryEntry entry in balance)
            {
                var id = Convert.ToUInt64(entry.Key);
                var val = Convert.ToDouble(entry.Value);
                if (val > bestVal && SteamIdHelper.IsSteamId(id))
                {
                    bestVal = val;
                    bestId = id;
                }
            }
            if (bestId != 0 && bestVal > 0)
                EventRecording.RecordEvent(bestId, "ArmoredTrainEvent");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] ArmoredTrain DefineEventWinner: {ex.Message}");
        }
    }

    public static void Postfix_CHT_OnEntityDeath(object __instance, PatrolHelicopter patrolHelicopter, HitInfo hitInfo)
    {
        if (__instance == null || patrolHelicopter == null || LeaderboardMod.Instance == null) return;
        try
        {
            var mgrField = AccessTools.Field(__instance.GetType(), "_tieredHelicopterManager")
                           ?? AccessTools.Field(__instance.GetType(), "tieredHelicopterManager");
            var mgr = mgrField?.GetValue(__instance);
            if (mgr == null) return;

            var getComp = AccessTools.Method(mgr.GetType(), "GetTieredComponentForHelicopter", new[] { typeof(PatrolHelicopter) });
            var comp = getComp?.Invoke(mgr, new object[] { patrolHelicopter });
            if (comp == null) return;

            ulong winnerId = 0;
            var attackersField = AccessTools.Field(comp.GetType(), "_attackerInfos");
            if (attackersField?.GetValue(comp) is IEnumerable attackers)
            {
                float bestDmg = 0f;
                foreach (var info in attackers)
                {
                    if (info == null) continue;
                    var player = AccessTools.Field(info.GetType(), "Player")?.GetValue(info) as BasePlayer
                                 ?? AccessTools.Property(info.GetType(), "Player")?.GetValue(info) as BasePlayer;
                    var dmgObj = AccessTools.Field(info.GetType(), "TotalDamage")?.GetValue(info)
                                 ?? AccessTools.Property(info.GetType(), "TotalDamage")?.GetValue(info);
                    float dmg = dmgObj != null ? Convert.ToSingle(dmgObj) : 0f;
                    if (player != null && !player.IsNpc && SteamIdHelper.IsSteamId(player.userID) && dmg > bestDmg)
                    {
                        bestDmg = dmg;
                        winnerId = player.userID;
                    }
                }
            }

            if (winnerId == 0)
            {
                var caller = AccessTools.Property(comp.GetType(), "CallingPlayer")?.GetValue(comp) as BasePlayer
                             ?? AccessTools.Field(comp.GetType(), "CallingPlayer")?.GetValue(comp) as BasePlayer;
                if (caller != null && !caller.IsNpc && SteamIdHelper.IsSteamId(caller.userID))
                    winnerId = caller.userID;
            }

            if (winnerId == 0)
            {
                var killer = hitInfo?.InitiatorPlayer;
                if (killer != null && !killer.IsNpc && SteamIdHelper.IsSteamId(killer.userID))
                    winnerId = killer.userID;
            }

            if (winnerId != 0)
                EventRecording.RecordEvent(winnerId, "CHT");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] CHT OnEntityDeath: {ex.Message}");
        }
    }
}
