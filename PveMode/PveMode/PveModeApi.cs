using System;
using System.Collections.Generic;
using UnityEngine;

namespace PveModeHarmony
{
    /// <summary>
    /// Public API surface for PveMode. DLL/mod name is <c>0PveMode</c> (loads early);
    /// AppDomain keys stay <c>PveMode_*</c> for consumers (same pattern as 0Permissions / Permissions_*).
    /// </summary>
    public class PveModeApi
    {
        public const string AppDomainApiKey = "PveMode_ApiType";
        public const string AppDomainGenerationKey = "PveMode_Generation";
        public const string AppDomainReadyCallbacksKey = "PveMode_ReadyCallbacks";
        public const string AppDomainDamageKey = "PveMode_CanEntityTakeDamage";
        public const string AppDomainTargetKey = "PveMode_CanEntityBeTargeted";

        public static PveModeApi Instance { get; private set; }

        internal static void Activate()
        {
            Instance = new PveModeApi();
            try
            {
                BumpGenerationAndPublishApi();
                AppDomain.CurrentDomain.SetData(AppDomainDamageKey, (Func<BaseEntity, HitInfo, object>)PveModeManager.CanEntityTakeDamageApi);
                AppDomain.CurrentDomain.SetData(AppDomainTargetKey, (Func<BaseEntity, BaseEntity, object>)PveModeManager.CanEntityBeTargetedApi);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Failed to publish API: " + ex.Message);
            }
            InvokeReadyCallbacks();
        }

        internal static void Deactivate()
        {
            Instance = null;
            try
            {
                // Keep PveMode_Generation and ready callbacks so consumers rebind on next load.
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainDamageKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainTargetKey, null);
            }
            catch { }
        }

        // ---- Generation / ready callbacks (consumer rebind failsafe) ----

        public static int GetGeneration()
        {
            try
            {
                var data = AppDomain.CurrentDomain.GetData(AppDomainGenerationKey);
                if (data is int i) return i;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Consumers (Convoy, ArmoredTrain, …) register here. Runs immediately if PveMode is already up,
        /// and again on each 0PveMode load/reload.
        /// </summary>
        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            var list = GetOrCreateReadyCallbacks();
            lock (list)
            {
                if (!list.Contains(callback))
                    list.Add(callback);
            }
            if (Instance != null)
            {
                try { callback(); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PveMode] Ready callback (immediate): " + ex.Message);
                }
            }
        }

        public static void UnregisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> list)
                {
                    lock (list)
                        list.Remove(callback);
                }
            }
            catch { }
        }

        private static void BumpGenerationAndPublishApi()
        {
            int gen = GetGeneration() + 1;
            try { AppDomain.CurrentDomain.SetData(AppDomainGenerationKey, gen); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(PveModeApi)); } catch { }
            Debug.Log("[PveMode] Published API gen=" + gen + " (mod DLL name: 0PveMode).");
        }

        private static List<Action> GetOrCreateReadyCallbacks()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> existing)
                    return existing;
            }
            catch { }

            var created = new List<Action>();
            try { AppDomain.CurrentDomain.SetData(AppDomainReadyCallbacksKey, created); } catch { }
            return created;
        }

        private static void InvokeReadyCallbacks()
        {
            List<Action> snapshot;
            try
            {
                if (!(AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> list) || list.Count == 0)
                    return;
                lock (list)
                    snapshot = new List<Action>(list);
            }
            catch
            {
                return;
            }

            Debug.Log("[PveMode] Invoking " + snapshot.Count + " ready callback(s) for consumer rebind.");
            foreach (var cb in snapshot)
            {
                try { cb(); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PveMode] Ready callback failed: " + ex.Message);
                }
            }
        }

        // ---- Strongly-typed API ----

        public void EventAddPveMode(string shortname, Dictionary<string, object> config, Vector3 position, float radius,
            HashSet<ulong> crates, HashSet<ulong> npc, HashSet<ulong> tanks, HashSet<ulong> helicopters,
            HashSet<ulong> turrets, HashSet<ulong> owners, BasePlayer owner = null)
            => PveModeManager.EventAdd(shortname, config, position, radius, crates, npc, tanks, helicopters, turrets, owners, owner);

        public void EventRemovePveMode(string shortname, bool addCooldownOwners = true)
            => PveModeManager.EventRemove(shortname, addCooldownOwners);

        public void EventAddCooldown(string shortname, HashSet<ulong> owners, double cooldown)
            => PveModeManager.EventAddCooldown(shortname, owners, cooldown);

        public void EventAddCrates(string shortname, HashSet<ulong> crates) => PveModeManager.EventAddCrates(shortname, crates);
        public void EventAddScientists(string shortname, HashSet<ulong> npc) => PveModeManager.EventAddScientists(shortname, npc);
        public void EventAddTanks(string shortname, HashSet<ulong> tanks) => PveModeManager.EventAddTanks(shortname, tanks);
        public void EventAddHelicopters(string shortname, HashSet<ulong> helicopters) => PveModeManager.EventAddHelicopters(shortname, helicopters);
        public void EventAddTurrets(string shortname, HashSet<ulong> turrets) => PveModeManager.EventAddTurrets(shortname, turrets);

        public void SetEventOwner(string shortname, BasePlayer player) => PveModeManager.SetEventOwner(shortname, player);
        public ulong GetEventOwner(string shortname) => PveModeManager.GetEventOwner(shortname);
        public HashSet<ulong> GetEventOwners(string shortname) => PveModeManager.GetEventOwners(shortname);

        public object CanActionEvent(string shortname, BasePlayer player) => PveModeManager.CanActionEvent(shortname, player);
        public object CanActionEventNoMessage(string shortname, BasePlayer player) => PveModeManager.CanActionEventNoMessage(shortname, player);
        public bool CanTimeOwner(string shortname, ulong steamId, double cooldown) => PveModeManager.CanTimeOwner(shortname, steamId, cooldown);

        public bool IsPlayerInEventZone(ulong id) => PveModeManager.IsPlayerInEventZone(id);
        public HashSet<string> GetEventsPlayer(ulong id) => PveModeManager.GetEventsPlayer(id);
        public Dictionary<string, double> GetTimesPlayer(ulong id) => PveModeManager.GetTimesPlayer(id);

        public void ScientistAddPveMode(ScientistNPC npc) => PveModeManager.ScientistAdd(npc);
        public void ScientistRemovePveMode(ScientistNPC npc) => PveModeManager.ScientistRemove(npc);
        public void CrateAddScientistPveMode(ulong crateId, ulong scientistId) => PveModeManager.CrateAddScientist(crateId, scientistId);

        public object Call(string method, object[] args)
        {
            args = args ?? Array.Empty<object>();
            try
            {
                switch (method)
                {
                    case "EventAddPveMode":
                        EventAddPveMode(
                            (string)args[0],
                            args[1] as Dictionary<string, object>,
                            (Vector3)args[2],
                            Convert.ToSingle(args[3]),
                            args[4] as HashSet<ulong>,
                            args[5] as HashSet<ulong>,
                            args[6] as HashSet<ulong>,
                            args[7] as HashSet<ulong>,
                            args[8] as HashSet<ulong>,
                            args[9] as HashSet<ulong>,
                            args.Length > 10 ? args[10] as BasePlayer : null);
                        return null;

                    case "EventRemovePveMode":
                        EventRemovePveMode((string)args[0], args.Length > 1 ? Convert.ToBoolean(args[1]) : true);
                        return null;

                    case "EventAddCooldown":
                        EventAddCooldown((string)args[0], args[1] as HashSet<ulong>, Convert.ToDouble(args[2]));
                        return null;

                    case "EventAddCrates":
                        EventAddCrates((string)args[0], args[1] as HashSet<ulong>);
                        return null;

                    case "EventAddScientists":
                        EventAddScientists((string)args[0], args[1] as HashSet<ulong>);
                        return null;

                    case "EventAddTanks":
                        EventAddTanks((string)args[0], args[1] as HashSet<ulong>);
                        return null;

                    case "EventAddHelicopters":
                        EventAddHelicopters((string)args[0], args[1] as HashSet<ulong>);
                        return null;

                    case "EventAddTurrets":
                        EventAddTurrets((string)args[0], args[1] as HashSet<ulong>);
                        return null;

                    case "SetEventOwner":
                        SetEventOwner((string)args[0], args[1] as BasePlayer);
                        return null;

                    case "GetEventOwner":
                        return GetEventOwner((string)args[0]);

                    case "GetEventOwners":
                        return GetEventOwners((string)args[0]);

                    case "CanActionEvent":
                        return CanActionEvent((string)args[0], args[1] as BasePlayer);

                    case "CanActionEventNoMessage":
                        return CanActionEventNoMessage((string)args[0], args[1] as BasePlayer);

                    case "CanTimeOwner":
                        return CanTimeOwner((string)args[0], Convert.ToUInt64(args[1]), Convert.ToDouble(args[2]));

                    case "IsPlayerInEventZone":
                        return IsPlayerInEventZone(Convert.ToUInt64(args[0]));

                    case "GetEventsPlayer":
                        return GetEventsPlayer(Convert.ToUInt64(args[0]));

                    case "GetTimesPlayer":
                        return GetTimesPlayer(Convert.ToUInt64(args[0]));

                    case "ScientistAddPveMode":
                        ScientistAddPveMode(args[0] as ScientistNPC);
                        return null;

                    case "ScientistRemovePveMode":
                        ScientistRemovePveMode(args[0] as ScientistNPC);
                        return null;

                    case "CrateAddScientistPveMode":
                        CrateAddScientistPveMode(Convert.ToUInt64(args[0]), Convert.ToUInt64(args[1]));
                        return null;

                    case "RegisterReadyCallback":
                        if (args.Length > 0 && args[0] is Action a) RegisterReadyCallback(a);
                        return null;

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Api.Call(" + method + ") failed: " + ex.Message);
                return null;
            }
        }
    }
}
