using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Oxide Convoy PveModeManager parity: registers the stop zone with the shared PveMode
    /// Harmony mod and tracks event owner for markers / cooldown checks.
    /// </summary>
    public static class PveModeManager
    {
        public const string EventShortName = "Convoy";

        private static HashSet<ulong> _pveModeOwners = new HashSet<ulong>();
        private static BasePlayer _owner;
        private static float _lastZoneDeleteTime;
        private static bool _readyCallbackRegistered;
        private static int _boundGeneration = -1;

        public static BasePlayer CurrentOwner => _owner;

        public static bool IsPveModeReady()
        {
            var cfg = GetPveConfig();
            if (cfg == null || !cfg.Enable) return false;
            return TryGetApi(out _);
        }

        /// <summary>
        /// Call from Convoy OnLoaded. Registers a PveMode ready callback so owner hooks rebind
        /// even if Convoy loaded before 0PveMode (same pattern as 0Permissions consumers).
        /// </summary>
        public static void EnsureOwnerCallbackRegistered()
        {
            RegisterOwnerCallbacks();
            if (_readyCallbackRegistered) return;
            try
            {
                // Prefer typed RegisterReadyCallback on PveModeApi when assembly is loaded.
                Type apiType = AppDomain.CurrentDomain.GetData("PveMode_ApiType") as Type;
                if (apiType != null)
                {
                    MethodInfo reg = apiType.GetMethod("RegisterReadyCallback", BindingFlags.Public | BindingFlags.Static);
                    reg?.Invoke(null, new object[] { (Action)OnPveModeReady });
                }
                else
                {
                    // 0PveMode not up yet — park callback on AppDomain list for when it loads.
                    const string key = "PveMode_ReadyCallbacks";
                    var list = AppDomain.CurrentDomain.GetData(key) as List<Action>;
                    if (list == null)
                    {
                        list = new List<Action>();
                        AppDomain.CurrentDomain.SetData(key, list);
                    }
                    lock (list)
                    {
                        list.Remove(OnPveModeReady);
                        list.Add(OnPveModeReady);
                    }
                }
                _readyCallbackRegistered = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Failed to register PveMode ready callback: " + ex.Message);
            }
        }

        private static void OnPveModeReady()
        {
            int gen = 0;
            try
            {
                var data = AppDomain.CurrentDomain.GetData("PveMode_Generation");
                if (data is int i) gen = i;
            }
            catch { }

            if (gen == _boundGeneration && IsPveModeReady()) return;
            _boundGeneration = gen;
            RegisterOwnerCallbacks();
            UnityEngine.Debug.Log("[Convoy] Bound to PveMode gen=" + gen + " ready=" + IsPveModeReady());
        }

        private static void RegisterOwnerCallbacks()
        {
            try
            {
                const string key = "PveMode_OwnerCallbacks";
                var list = AppDomain.CurrentDomain.GetData(key) as List<Action<string, string, BasePlayer>>;
                if (list == null)
                {
                    list = new List<Action<string, string, BasePlayer>>();
                    AppDomain.CurrentDomain.SetData(key, list);
                }
                lock (list)
                {
                    list.Remove(OnOwnerCallback);
                    list.Add(OnOwnerCallback);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Failed to register PveMode owner callback: " + ex.Message);
            }
        }

        private static void OnOwnerCallback(string shortname, string action, BasePlayer player)
        {
            if (!string.Equals(shortname, EventShortName, StringComparison.OrdinalIgnoreCase)) return;
            if (action == "set") OnNewOwnerSet(shortname, player);
            else if (action == "clear") OnOwnerDeleted(shortname, player);
        }

        /// <summary>Called by PveMode reflection (signature must match).</summary>
        public static void OnNewOwnerSet(string shortname, BasePlayer player)
        {
            if (!string.Equals(shortname, EventShortName, StringComparison.OrdinalIgnoreCase)) return;
            _owner = player;
            if (player != null)
            {
                ulong id = (ulong)player.userID;
                if (id != 0) _pveModeOwners.Add(id);
            }
        }

        /// <summary>Called by PveMode reflection (signature must match).</summary>
        public static void OnOwnerDeleted(string shortname, BasePlayer player)
        {
            if (!string.Equals(shortname, EventShortName, StringComparison.OrdinalIgnoreCase)) return;
            _owner = null;
        }

        public static BasePlayer UpdateAndGetEventOwner()
        {
            var ec = EventController.Instance;
            if (ec != null && ec.IsStopped())
                return _owner;

            var cfg = GetPveConfig();
            float timeSince = Time.realtimeSinceStartup - _lastZoneDeleteTime;
            if (cfg != null && timeSince > cfg.TimeExitOwner)
                _owner = null;

            return _owner;
        }

        public static void CreatePveModeZone(Vector3 position, BasePlayer externalOwner)
        {
            if (!IsPveModeReady()) return;
            EnsureOwnerCallbackRegistered();

            var ec = EventController.Instance;
            if (ec == null) return;

            Dictionary<string, object> config = GetPveModeConfigDict();
            if (config == null) return;

            HashSet<ulong> npc = ec.GetEventNpcNetIds();
            HashSet<ulong> bradley = ec.GetAllBradleyNetIds();
            HashSet<ulong> helicopters = new HashSet<ulong>(); // EventHeli not ported
            HashSet<ulong> crates = ec.GetEventCratesNetIds();
            HashSet<ulong> turrets = ec.GetAliveTurretsNetIds();

            BasePlayer playerOwner = GetEventOwner();
            if (playerOwner == null)
                playerOwner = externalOwner;

            float radius = ec.EventConfig?.ZoneRadius > 0f ? ec.EventConfig.ZoneRadius : 50f;

            CallApi("EventAddPveMode", EventShortName, config, position, radius, crates, npc, bradley, helicopters, turrets, _pveModeOwners, playerOwner);
            UnityEngine.Debug.Log("[Convoy] Registered PveMode zone at " + position + " radius=" + radius
                + (playerOwner != null ? " owner=" + playerOwner.displayName : " (no owner yet)"));
        }

        public static void DeletePveModeZone()
        {
            if (!IsPveModeReady()) return;

            _lastZoneDeleteTime = Time.realtimeSinceStartup;

            object ownersObj = CallApi("GetEventOwners", EventShortName);
            if (ownersObj is HashSet<ulong> owners)
                _pveModeOwners = owners ?? new HashSet<ulong>();
            else
                _pveModeOwners = new HashSet<ulong>();

            object ownerIdObj = CallApi("GetEventOwner", EventShortName);
            ulong userId = 0;
            if (ownerIdObj is ulong u) userId = u;
            else if (ownerIdObj != null) ulong.TryParse(ownerIdObj.ToString(), out userId);

            if (userId != 0)
            {
                BasePlayer p = BasePlayer.FindByID(userId);
                if (p != null) OnNewOwnerSet(EventShortName, p);
            }

            CallApi("EventRemovePveMode", EventShortName, false);
        }

        public static void OnEventEnd()
        {
            if (IsPveModeReady())
            {
                var cfg = GetPveConfig();
                double cooldown = cfg?.Cooldown ?? 4000d;
                CallApi("EventAddCooldown", EventShortName, _pveModeOwners, cooldown);
            }

            _lastZoneDeleteTime = 0;
            _pveModeOwners.Clear();
            _owner = null;
        }

        public static bool IsPveModDefaultBlockAction(BasePlayer player)
        {
            if (!IsPveModeReady() || player == null) return false;
            return CallApi("CanActionEventNoMessage", EventShortName, player) != null;
        }

        public static bool IsPveModeBlockInteractByCooldown(BasePlayer player)
        {
            if (!IsPveModeReady() || player == null) return false;
            var cfg = GetPveConfig();
            if (cfg == null) return false;

            BasePlayer eventOwner = GetEventOwner();
            if ((cfg.NoInteractIfCooldownAndNoOwners && eventOwner == null) || cfg.NoDealDamageIfCooldownAndTeamOwner)
            {
                object can = CallApi("CanTimeOwner", EventShortName, (ulong)player.userID, cfg.Cooldown);
                return !(can is bool b && b);
            }
            return false;
        }

        public static bool IsPveModeBlockNoOwnerLooting(BasePlayer player)
        {
            if (!IsPveModeReady() || player == null) return false;
            var cfg = GetPveConfig();
            if (cfg == null || !cfg.CanLootOnlyOwner) return false;

            BasePlayer eventOwner = GetEventOwner();
            if (eventOwner == null) return false;
            return !IsTeam(player, (ulong)eventOwner.userID);
        }

        public static bool IsPlayerHaveCooldown(ulong userId)
        {
            if (!IsPveModeReady()) return false;
            var cfg = GetPveConfig();
            if (cfg == null) return false;
            object can = CallApi("CanTimeOwner", EventShortName, userId, cfg.Cooldown);
            return !(can is bool b && b);
        }

        public static string GetOwnerDisplayNameForMarker()
        {
            var cfg = GetPveConfig();
            if (cfg == null || !cfg.ShowEventOwnerNameOnMap) return null;
            BasePlayer owner = UpdateAndGetEventOwner();
            return owner != null && !owner.IsDestroyed ? owner.displayName : null;
        }

        private static BasePlayer GetEventOwner()
        {
            var cfg = GetPveConfig();
            float timeSince = Time.realtimeSinceStartup - _lastZoneDeleteTime;
            if (_owner != null)
            {
                var ec = EventController.Instance;
                if (ec != null && ec.IsStopped())
                    return _owner;
                if (cfg != null && timeSince < cfg.TimeExitOwner)
                    return _owner;
            }
            return null;
        }

        private static Dictionary<string, object> GetPveModeConfigDict()
        {
            var p = GetPveConfig();
            if (p == null) return null;

            return new Dictionary<string, object>
            {
                ["Damage"] = p.Damage,
                ["ScaleDamage"] = p.ScaleDamage ?? new Dictionary<string, float>(),
                ["LootCrate"] = p.LootCrate,
                ["HackCrate"] = p.HackCrate,
                ["LootNpc"] = p.LootNpc,
                ["DamageNpc"] = p.DamageNpc,
                ["DamageTank"] = p.DamageTank,
                ["DamageHelicopter"] = p.DamageHeli,
                ["DamageTurret"] = p.DamageTurret,
                ["TargetNpc"] = p.TargetNpc,
                ["TargetTank"] = p.TargetTank,
                ["TargetHelicopter"] = p.TargetHeli,
                ["TargetTurret"] = p.TargetTurret,
                ["CanEnter"] = p.CanEnter,
                ["CanEnterCooldownPlayer"] = p.CanEnterCooldownPlayer,
                ["TimeExitOwner"] = p.TimeExitOwner,
                ["AlertTime"] = p.AlertTime,
                ["RestoreUponDeath"] = p.RestoreUponDeath,
                ["CooldownOwner"] = p.Cooldown,
                ["Darkening"] = 0
            };
        }

        private static ConvoyPveModeConfig GetPveConfig()
            => ConvoyMod.Instance?.FullConfig?.SupportedPluginsConfig?.PveMode;

        private static bool TryGetApi(out object apiInstance)
        {
            apiInstance = null;
            try
            {
                var apiType = AppDomain.CurrentDomain.GetData("PveMode_ApiType") as Type;
                if (apiType == null) return false;
                var prop = apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                apiInstance = prop?.GetValue(null);
                return apiInstance != null;
            }
            catch { return false; }
        }

        private static object CallApi(string method, params object[] args)
        {
            if (!TryGetApi(out object api)) return null;
            try
            {
                MethodInfo call = api.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                if (call != null)
                    return call.Invoke(api, new object[] { method, args ?? Array.Empty<object>() });

                // Fallback: direct method on API instance
                MethodInfo mi = api.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
                return mi?.Invoke(api, args);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] PveMode.Call " + method + " failed: " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        private static bool IsTeam(BasePlayer player, ulong targetId)
        {
            if (player == null || targetId == 0) return false;
            if ((ulong)player.userID == targetId) return true;
            if (player.currentTeam == 0) return false;
            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance?.FindTeam(player.currentTeam);
            return team != null && team.members.Contains(targetId);
        }
    }
}
