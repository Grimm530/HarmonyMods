using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace Convoy
{
    /// <summary>
    /// Launches/stops the convoy event. Port of the Oxide Convoy EventLauncher (simplified: no PreStart/PVE/Discord).
    /// </summary>
    public static class EventLauncher
    {
        private static EventController _controller;

        public static bool IsEventActive()
        {
            return _controller != null;
        }

        public static bool DelayStartEvent(BasePlayer activator = null, string presetName = null)
        {
            if (IsEventActive())
            {
                Reply(activator, "The convoy event is already active.");
                return false;
            }

            var cfg = ConvoyMod.Instance?.FullConfig;
            if (cfg == null)
            {
                Reply(activator, "Convoy config not loaded.");
                return false;
            }

            ConvoyEventConfig eventConfig = DefineEventConfig(cfg, presetName);
            if (eventConfig == null)
            {
                Reply(activator, "No suitable convoy preset found in config.");
                return false;
            }

            return StartEvent(eventConfig, activator);
        }

        private static bool StartEvent(ConvoyEventConfig eventConfig, BasePlayer activator = null)
        {
            ConvoyPathManager.GenerateNewPath();
            if (ConvoyPathManager.CurrentPath == null || ConvoyPathManager.CurrentPath.StartPathPoint == null)
            {
                UnityEngine.Debug.LogWarning("[Convoy] StartEvent: no route found (need roads with Route Settings). Event aborted.");
                Reply(activator, "No road route found. Check Route Settings / map roads.");
                return false;
            }

            var go = new GameObject("ConvoyEventController");
            _controller = go.AddComponent<EventController>();
            _controller.Init(eventConfig);
            Reply(activator, "Convoy '" + (eventConfig.DisplayName ?? eventConfig.PresetName) + "' spawning...");
            return true;
        }

        public static void StopEvent()
        {
            bool wasActive = IsEventActive();
            if (_controller != null)
            {
                _controller.DeleteController();
                _controller = null;
            }
            ConvoyGrimmNpc.ClearAll();
            ConvoyState.Clear();
            if (wasActive)
            {
                PveModeManager.OnEventEnd();
                ConvoyNotifyStub.SendMessageToAll("Finish", ConvoyMod.Instance?.FullConfig?.Prefix ?? "[Convoy]");
            }
        }

        public static EventController Active => _controller;

        private static ConvoyEventConfig DefineEventConfig(ConvoyPluginConfig cfg, string presetName)
        {
            if (cfg.EventConfigs == null || cfg.EventConfigs.Count == 0) return null;

            if (!string.IsNullOrEmpty(presetName))
                return cfg.EventConfigs.FirstOrDefault(x => string.Equals(x.PresetName, presetName, StringComparison.OrdinalIgnoreCase));

            var suitable = cfg.EventConfigs.Where(x => x != null && x.Chance > 0 && x.IsAutoStart && x.VehiclesOrder != null && x.VehiclesOrder.Count > 0).ToList();
            if (suitable.Count == 0)
                suitable = cfg.EventConfigs.Where(x => x != null && x.VehiclesOrder != null && x.VehiclesOrder.Count > 0).ToList();
            if (suitable.Count == 0) return null;

            float sum = suitable.Sum(x => Mathf.Max(0.0001f, x.Chance));
            float roll = UnityEngine.Random.Range(0f, sum);
            foreach (var e in suitable)
            {
                roll -= Mathf.Max(0.0001f, e.Chance);
                if (roll <= 0) return e;
            }
            return suitable[0];
        }

        private static void Reply(BasePlayer player, string message)
        {
            string prefix = ConvoyMod.Instance?.Config?.Prefix ?? "[Convoy]";
            if (player != null && player.IsConnected)
            {
                // Command replies: tip when GameTips enabled, else chat.
                var tip = ConvoyMod.Instance?.FullConfig?.NotifyConfig?.GameTipConfig;
                if (tip != null && tip.IsEnabled)
                    ConvoyNotifyStub.ShowGameTip(player, prefix + " " + message, tip.Style);
                else
                    player.ChatMessage(prefix + " " + message);
            }
            else
                UnityEngine.Debug.Log("[Convoy] " + message);
        }
    }

    /// <summary>Linear drivable route built from the pathfinding EventPath graph (single polyline, ping-pong for dead-end roads).</summary>
    public class ConvoyRoute
    {
        public readonly List<Vector3> Points = new List<Vector3>();
        private float[] _cum;
        public float TotalLength { get; private set; }
        public bool IsRing { get; private set; }

        public static ConvoyRoute Build(EventPath path)
        {
            if (path == null || path.Points.Count == 0) return null;
            var route = new ConvoyRoute { IsRing = path.IsRoundRoad };

            var start = path.StartPathPoint ?? path.Points[0];
            var visited = new HashSet<PathPoint>();
            PathPoint prev = null;
            PathPoint cur = start;
            int guard = 0;
            while (cur != null && guard++ < 20000)
            {
                route.Points.Add(cur.Position);
                visited.Add(cur);

                PathPoint next = null;
                foreach (var c in cur.ConnectedPoints)
                {
                    if (c == null || c == prev) continue;
                    if (visited.Contains(c)) continue;
                    next = c;
                    break;
                }
                prev = cur;
                cur = next;
            }

            if (route.Points.Count < 2)
            {
                foreach (var p in path.Points)
                    if (route.Points.Count == 0 || route.Points[route.Points.Count - 1] != p.Position)
                        route.Points.Add(p.Position);
            }

            route.Recalculate();
            return route.Points.Count >= 2 ? route : null;
        }

        private void Recalculate()
        {
            _cum = new float[Points.Count];
            _cum[0] = 0f;
            for (int i = 1; i < Points.Count; i++)
                _cum[i] = _cum[i - 1] + Vector3.Distance(Points[i - 1], Points[i]);
            TotalLength = _cum[Points.Count - 1];
        }

        private static float Mod(float a, float m)
        {
            if (m <= 0f) return 0f;
            return ((a % m) + m) % m;
        }

        public void Sample(float distance, out Vector3 pos, out Vector3 dir)
        {
            if (Points.Count == 1 || TotalLength <= 0f)
            {
                pos = Points[0];
                dir = Vector3.forward;
                return;
            }

            bool reverse = false;
            if (IsRing)
            {
                distance = Mod(distance, TotalLength);
            }
            else
            {
                float m = Mod(distance, TotalLength * 2f);
                if (m > TotalLength) { m = TotalLength * 2f - m; reverse = true; }
                distance = m;
            }

            int i = 0;
            for (; i < _cum.Length - 1; i++)
                if (distance <= _cum[i + 1]) break;
            if (i >= Points.Count - 1) i = Points.Count - 2;

            float segLen = _cum[i + 1] - _cum[i];
            float t = segLen > 0.0001f ? (distance - _cum[i]) / segLen : 0f;
            pos = Vector3.Lerp(Points[i], Points[i + 1], t);
            dir = (Points[i + 1] - Points[i]);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward; else dir.Normalize();
            if (reverse) dir = -dir;
        }
    }

    /// <summary>
    /// Runs a single convoy event: spawns vehicles/NPCs/turrets/crates, drives the convoy along the route
    /// (kinematic on-rails movement), handles aggro/stop-on-attack, map marker, event timer and cleanup.
    /// </summary>
    public class EventController : MonoBehaviour
    {
        private class ConvoyVehicle
        {
            public BaseEntity Entity;
            public float HeightOffset;
            public Rigidbody Body;
            public float BradleyBuildingDamageScale = -1f;
        }

        private class NpcSlot
        {
            public ScientistNPC Npc;
            public BaseMountable Seat;
            public BaseEntity Vehicle;
            public string PresetName;
            public ConvoyNpcConfig Config;
            public bool AllowDismount = true;
            public bool IsRoaming;
        }

        public ConvoyEventConfig EventConfig;

        private ConvoyRoute _route;
        private readonly List<ConvoyVehicle> _vehicles = new List<ConvoyVehicle>();
        private readonly HashSet<AutoTurret> _turrets = new HashSet<AutoTurret>();
        private readonly HashSet<SamSite> _samSites = new HashSet<SamSite>();
        private readonly HashSet<BaseEntity> _crates = new HashSet<BaseEntity>();
        private readonly List<NpcSlot> _npcSlots = new List<NpcSlot>();

        private const float Spacing = 9f;
        private const float Speed = 6f;

        private float _leadDistance;
        private bool _moving;
        private bool _fullySpawned;
        private bool _isStopped;
        private int _eventTime;
        private int _aggressiveTime;
        private int _stopTime;
        private bool _isEventLooted;
        private readonly HashSet<ulong> _lootedContainersUids = new HashSet<ulong>();
        private BasePlayer _lastAttacker;

        private float _netTimer;
        private BaseEntity _radiusMarker;
        private BaseEntity _vendingMarker;

        private Coroutine _spawnCo;
        private Coroutine _eventCo;

        public static EventController Instance { get; private set; }

        /// <summary>True while the convoy is rolling (not stopped / not finished spawning).</summary>
        public bool IsMovingNow => _fullySpawned && _moving && !_isStopped;

        private ConvoyPluginConfig Cfg => ConvoyMod.Instance?.FullConfig;

        public void Init(ConvoyEventConfig eventConfig)
        {
            Instance = this;
            EventConfig = eventConfig;
            _route = ConvoyRoute.Build(ConvoyPathManager.CurrentPath);
            if (_route == null)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Could not build a route from the path. Aborting.");
                EventLauncher.StopEvent();
                return;
            }
            _spawnCo = ServerMgr.Instance.StartCoroutine(SpawnCoroutine());
        }

        private Vector3 StartPos => ConvoyPathManager.CurrentPath.StartPathPoint.Position;
        private Vector3 SpawnDir
        {
            get
            {
                var d = ConvoyPathManager.CurrentPath.SpawnRotation;
                return d.sqrMagnitude < 0.001f ? Vector3.forward : d;
            }
        }

        private IEnumerator SpawnCoroutine()
        {
            var cfg = Cfg;
            Quaternion rot = Quaternion.LookRotation(SpawnDir);

            foreach (string preset in EventConfig.VehiclesOrder)
            {
                var vehicle = SpawnConvoyVehicle(preset, StartPos + Vector3.up * 0.5f, rot);
                if (vehicle != null)
                    _vehicles.Add(vehicle);
                yield return CoroutineExWait(0.2f);
            }

            if (_vehicles.Count == 0)
            {
                UnityEngine.Debug.LogWarning("[Convoy] No vehicles spawned (check Convoy Presets / vehicle configs). Aborting.");
                EventLauncher.StopEvent();
                yield break;
            }

            yield return CoroutineExWait(1f);
            OnSpawnFinished();
        }

        private void OnSpawnFinished()
        {
            _spawnCo = null;
            _fullySpawned = true;
            _moving = true;
            _eventTime = EventConfig.EventTime > 0 ? EventConfig.EventTime : 3600;
            SwitchAggressive(IsAggressive());
            CreateMarkers();
            ConvoyPathManager.OnSpawnFinish();
            _eventCo = ServerMgr.Instance.StartCoroutine(EventCoroutine());

            string grid = GridToString(StartPos);
            int npcCount = 0;
            foreach (var s in _npcSlots)
                if (s?.Npc != null && !s.Npc.IsDestroyed) npcCount++;
            UnityEngine.Debug.Log($"[Convoy] Convoy '{EventConfig.DisplayName}' spawned at {grid} with {_vehicles.Count} vehicles, {npcCount} NPCs. Moving.");
            string prefix = Cfg?.Prefix ?? "[Convoy]";
            ConvoyNotifyStub.SendMessageToAll("EventStart", prefix, EventConfig.DisplayName ?? "Convoy", grid);
            if (Cfg?.MainConfig?.EnableStartStopLogs == true)
                ConvoyNotifyStub.PrintLogMessage("EventStart_Log", EventConfig.PresetName);
        }

        private ConvoyVehicle SpawnConvoyVehicle(string presetName, Vector3 pos, Quaternion rot)
        {
            var cfg = Cfg;

            // Modular car — spawn with modules like Oxide ModularCarManager
            var modular = cfg.ModularCarConfigs?.FirstOrDefault(x => x.PresetName == presetName);
            if (modular != null)
            {
                var car = SpawnModularCar(pos, rot, modular);
                return FinishVehicle(car, modular, 0.4f);
            }

            var bradley = cfg.BradleyConfigs?.FirstOrDefault(x => x.PresetName == presetName);
            if (bradley != null)
            {
                var apc = ConvoyBuild.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab", pos, rot, 755446, false) as BradleyAPC;
                if (apc != null)
                {
                    apc.ScientistSpawns.Clear();
                    apc.Spawn();
                    ConvoyBuild.UpdateEntityMaxHealth(apc, bradley.Hp > 0 ? bradley.Hp : 1000f);
                    apc.moveForceMax = 0f;
                }
                var spawned = FinishVehicle(apc, bradley, 1.0f);
                if (spawned != null)
                    spawned.BradleyBuildingDamageScale = bradley.BradleyBuildingDamageScale;
                return spawned;
            }

            var vendor = cfg.TravelingVendorConfigs?.FirstOrDefault(x => x.PresetName == presetName);
            if (vendor != null)
            {
                var e = ConvoyBuild.SpawnRegularEntity("assets/prefabs/npc/travelling vendor/travellingvendor.prefab", pos, rot);
                var tv = e as TravellingVendor;
                if (tv != null) ConvoyBuild.PrepareTravellingVendor(tv, vendor);
                return FinishVehicle(e, vendor, 0.5f);
            }

            var sedan = cfg.SedanConfigs?.FirstOrDefault(x => x.PresetName == presetName);
            if (sedan != null)
            {
                var e = ConvoyBuild.SpawnRegularEntity("assets/content/vehicles/sedan_a/sedantest.entity.prefab", pos, rot) as BaseCombatEntity;
                if (e != null) ConvoyBuild.UpdateEntityMaxHealth(e, sedan.Hp > 0 ? sedan.Hp : 500f);
                return FinishVehicle(e, sedan, 0.4f);
            }

            var bike = cfg.BikeConfigs?.FirstOrDefault(x => x.PresetName == presetName);
            if (bike != null)
            {
                var e = ConvoyBuild.SpawnRegularEntity(bike.PrefabName, pos, rot) as BaseCombatEntity;
                if (e != null && bike.Hp > 0) ConvoyBuild.UpdateEntityMaxHealth(e, bike.Hp);
                return FinishVehicle(e, bike, 0.3f);
            }

            var karuza = cfg.KaruzaCarConfigs?.FirstOrDefault(x => x.PresetName == presetName);
            if (karuza != null)
            {
                var e = ConvoyBuild.SpawnRegularEntity(karuza.PrefabName, pos, rot);
                return FinishVehicle(e, karuza, 0.4f);
            }

            UnityEngine.Debug.LogWarning("[Convoy] Vehicle preset not found in any config: " + presetName);
            return null;
        }

        private ConvoyVehicle FinishVehicle(BaseEntity entity, ConvoyVehicleConfig vehicleConfig, float heightOffset)
        {
            if (entity == null || entity.IsDestroyed) return null;

            entity.enableSaving = false;
            ConvoyBuild.SetKinematic(entity, true);
            if (entity.net != null)
                ConvoyState.RegisterConvoyCrate((ulong)entity.net.ID.Value); // reuse convoy-entity set (checked via IsConvoyEntity)

            SpawnTurrets(vehicleConfig, entity);
            SpawnSamSites(vehicleConfig, entity);
            SpawnCrates(vehicleConfig, entity);
            SpawnVehicleNpcs(vehicleConfig, entity);

            return new ConvoyVehicle { Entity = entity, HeightOffset = heightOffset, Body = ConvoyBuild.GetRigidbody(entity) };
        }

        private void SpawnTurrets(ConvoyVehicleConfig vc, BaseEntity parent)
        {
            if (vc.TurretLocations == null) return;
            foreach (var loc in vc.TurretLocations)
            {
                var tc = Cfg.TurretConfigs?.FirstOrDefault(x => x.PresetName == loc.PresetName);
                if (tc == null) continue;
                var turret = ConvoyBuild.SpawnChildEntity(parent, "assets/prefabs/npc/autoturret/autoturret_deployed.prefab", loc.Position.ToVector3(), loc.Rotation.ToVector3()) as AutoTurret;
                if (turret == null) continue;
                ConvoyBuild.UpdateEntityMaxHealth(turret, tc.Hp > 0 ? tc.Hp : 500f);
                if (!string.IsNullOrEmpty(tc.ShortNameWeapon))
                {
                    var w = ItemManager.CreateByName(tc.ShortNameWeapon);
                    if (w != null && !w.MoveToContainer(turret.inventory)) w.Remove();
                }
                if (!string.IsNullOrEmpty(tc.ShortNameAmmo) && tc.CountAmmo > 0)
                {
                    var a = ItemManager.CreateByName(tc.ShortNameAmmo, tc.CountAmmo);
                    if (a != null && !a.MoveToContainer(turret.inventory)) a.Remove();
                }
                turret.isLootable = false;
                turret.dropFloats = false;
                turret.dropsLoot = Cfg.MainConfig?.IsTurretDropWeapon ?? false;
                turret.SetFlag(BaseEntity.Flags.Busy, true);
                turret.SetFlag(BaseEntity.Flags.Locked, true);
                if (tc.TargetLossRange > 0) turret.sightRange = tc.TargetLossRange;
                // Oxide TurretOptimizer: players-only targeting (prevents shooting Bradley / convoy vehicles).
                ConvoyTurretOptimizer.Attach(turret, tc.TargetDetectionRange == 0 ? 30f : tc.TargetDetectionRange);
                turret.SendNetworkUpdate();
                _turrets.Add(turret);
                if (turret.net != null) ConvoyState.RegisterConvoyCrate((ulong)turret.net.ID.Value);
            }
        }

        private void SpawnSamSites(ConvoyVehicleConfig vc, BaseEntity parent)
        {
            if (vc.SamSiteLocations == null) return;
            foreach (var loc in vc.SamSiteLocations)
            {
                var sc = Cfg.SamsiteConfigs?.FirstOrDefault(x => x.PresetName == loc.PresetName);
                if (sc == null) continue;
                var sam = ConvoyBuild.SpawnChildEntity(parent, "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab", loc.Position.ToVector3(), loc.Rotation.ToVector3()) as SamSite;
                if (sam == null) continue;
                ConvoyBuild.UpdateEntityMaxHealth(sam, sc.Hp > 0 ? sc.Hp : 1000f);
                if (sc.CountAmmo > 0)
                {
                    var a = ItemManager.CreateByName("ammo.rocket.sam", sc.CountAmmo);
                    if (a != null && !a.MoveToContainer(sam.inventory)) a.Remove();
                }
                sam.isLootable = false;
                sam.dropFloats = false;
                sam.dropsLoot = false;
                sam.inventory.SetLocked(true);
                sam.SetFlag(BaseEntity.Flags.Locked, true);
                sam.SetFlag(BaseEntity.Flags.Busy, true);
                _samSites.Add(sam);
                if (sam.net != null) ConvoyState.RegisterConvoyCrate((ulong)sam.net.ID.Value);
            }
        }

        private void SpawnCrates(ConvoyVehicleConfig vc, BaseEntity parent)
        {
            if (vc.CrateLocations == null) return;
            foreach (var loc in vc.CrateLocations)
            {
                var cc = Cfg.CrateConfigs?.FirstOrDefault(x => x.PresetName == loc.PresetName);
                if (cc == null || string.IsNullOrEmpty(cc.PrefabName)) continue;
                var crate = ConvoyBuild.SpawnChildEntity(parent, cc.PrefabName, loc.Position.ToVector3(), loc.Rotation.ToVector3(), cc.Skin);
                if (crate == null) continue;
                _crates.Add(crate);
                if (crate.net != null) ConvoyState.RegisterConvoyCrate((ulong)crate.net.ID.Value);
                // HackableLockedCrate keeps its default hack time / map marker (private fields vary by build).
            }
        }

        private BaseEntity SpawnModularCar(Vector3 pos, Quaternion rot, ConvoyModularCarConfig modular)
        {
            var modules = modular.Modules ?? new List<string>();
            int doubleMods = 0;
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] != null && modules[i].Contains("2mod")) doubleMods++;
            int len = doubleMods + modules.Count;
            if (len < 2) len = 2;
            if (len > 4) len = 4;

            string prefab = $"assets/content/vehicles/modularcar/{len}module_car_spawned.entity.prefab";
            var car = ConvoyBuild.CreateEntity(prefab, pos, rot, 0, false) as ModularCar;
            if (car == null) return null;

            // Disable random spawn modules via reflection (field name varies by game build).
            try
            {
                var spawnSettings = car.GetType().GetField("spawnSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(car);
                if (spawnSettings != null)
                {
                    var useField = spawnSettings.GetType().GetField("useSpawnSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    useField?.SetValue(spawnSettings, false);
                }
            }
            catch { }

            car.Spawn();

            int lastModule = -1;
            for (int socket = 0; socket < car.TotalSockets; socket++)
            {
                int idx = lastModule + 1;
                if (idx >= modules.Count) break;
                lastModule = idx;
                string shortname = modules[idx];
                if (string.IsNullOrEmpty(shortname)) continue;
                Item moduleItem = ItemManager.CreateByName(shortname);
                if (moduleItem == null) continue;
                if (!car.TryAddModule(moduleItem, socket))
                {
                    moduleItem.Remove();
                    continue;
                }
                if (shortname.Contains("2mod"))
                    socket++;
            }

            car.SetFlag(BaseEntity.Flags.Locked, true);
            car.SetFlag(BaseEntity.Flags.Busy, true);
            return car;
        }

        private void SpawnVehicleNpcs(ConvoyVehicleConfig vc, BaseEntity parent)
        {
            if (parent == null) return;

            if (!string.IsNullOrEmpty(vc.NpcPresetName) && vc.NumberOfNpc > 0)
            {
                var npcCfg = Cfg.NpcConfigs?.FirstOrDefault(x => x.PresetName == vc.NpcPresetName);
                if (npcCfg == null)
                    UnityEngine.Debug.LogWarning("[Convoy] NPC preset not found: " + vc.NpcPresetName);
                else
                {
                    var vehicle = parent as BaseVehicle;
                    if (vehicle != null)
                    {
                        int count = 0;
                        foreach (var mp in vehicle.allMountPoints)
                        {
                            if (mp == null || mp.mountable == null) continue;
                            var npc = ConvoyGrimmNpc.SpawnNpc(npcCfg, parent.transform.position + Vector3.up, parent.transform.rotation, mounted: true);
                            if (npc == null) continue;
                            // Oxide: native vehicle seats are not marked isMobile / ignoreVehicleParent
                            mp.mountable.AttemptMount(npc, false);
                            SyncMountedNpcLook(npc, mp.mountable);
                            AddNpcSlot(npc, mp.mountable, parent, vc.NpcPresetName, npcCfg, allowDismount: true);
                            count++;
                            if (count >= vc.NumberOfNpc) break;
                        }
                    }
                }
            }

            if (vc.AdditionalNpc == null) return;
            foreach (var pose in vc.AdditionalNpc)
            {
                if (pose == null || !pose.IsEnable) continue;
                string preset = !string.IsNullOrEmpty(pose.NpcPresetName) ? pose.NpcPresetName : vc.NpcPresetName;
                if (string.IsNullOrEmpty(preset)) continue;
                var poseCfg = Cfg.NpcConfigs?.FirstOrDefault(x => x.PresetName == preset);
                if (poseCfg == null)
                {
                    UnityEngine.Debug.LogWarning("[Convoy] Additional NPC preset not found: " + preset);
                    continue;
                }

                Vector3 localPos = pose.Position.ToVector3();
                Vector3 localRot = pose.Rotation.ToVector3();
                Vector3 worldPos = parent.transform.TransformPoint(localPos);
                Quaternion worldRot = parent.transform.rotation * Quaternion.Euler(localRot);

                BaseMountable seat = null;
                if (!string.IsNullOrEmpty(pose.SeatPrefab))
                    seat = ConvoyBuild.SpawnChildEntity(parent, pose.SeatPrefab, localPos, localRot) as BaseMountable;

                var npc = ConvoyGrimmNpc.SpawnNpc(poseCfg, worldPos, worldRot, mounted: true);
                if (npc == null) continue;
                if (seat != null)
                {
                    ConfigureConvoySeat(seat, parent as BaseVehicle);
                    seat.AttemptMount(npc, false);
                    SyncMountedNpcLook(npc, seat);
                }
                AddNpcSlot(npc, seat, parent, preset, poseCfg, allowDismount: pose.IsDismount);
            }
        }

        private void AddNpcSlot(ScientistNPC npc, BaseMountable seat, BaseEntity vehicle, string preset, ConvoyNpcConfig cfg, bool allowDismount)
        {
            _npcSlots.Add(new NpcSlot
            {
                Npc = npc,
                Seat = seat,
                Vehicle = vehicle,
                PresetName = preset,
                Config = cfg,
                AllowDismount = allowDismount,
                IsRoaming = false
            });
            if (npc?.net != null)
                ConvoyState.RegisterNpcPreset((ulong)npc.net.ID.Value, preset);
        }

        private static void ConfigureConvoySeat(BaseMountable seat, BaseVehicle vehicle)
        {
            if (seat == null) return;
            try
            {
                seat.isMobile = true;
                seat.ignoreVehicleParent = true;
                // Never assign an empty dismount array — stock code then fails and Suicide-kills the NPC.
                if (vehicle != null && vehicle.dismountPositions != null && vehicle.dismountPositions.Length > 0)
                    seat.dismountPositions = vehicle.dismountPositions;
            }
            catch { }
        }

        /// <summary>
        /// Oxide Convoy parity: NPC network facing uses viewAngles (world euler). When the vehicle
        /// transform is driven kinematically, those angles go stale and NPCs appear to face backwards.
        /// Re-apply mountAnchor rotation while passive (when aggressive, AI aims at targets).
        /// </summary>
        private static void SyncMountedNpcLook(ScientistNPC npc, BaseMountable seat)
        {
            if (npc == null || npc.IsDestroyed || seat == null || seat.IsDestroyed) return;
            if (!npc.isMounted) return;
            try
            {
                Transform anchor = seat.mountAnchor != null ? seat.mountAnchor : seat.transform;
                Quaternion rot = anchor.rotation;
                npc.OverrideViewAngles(rot.eulerAngles);
                npc.ServerRotation = rot;
                if (npc.eyes != null)
                    npc.eyes.NetworkUpdate(rot);
                npc.SendNetworkUpdate();
            }
            catch { }
        }

        private void UpdateAllMountedNpcLookRotation()
        {
            foreach (var slot in _npcSlots)
            {
                if (slot == null || slot.IsRoaming) continue;
                var npc = slot.Npc;
                if (npc == null || npc.IsDestroyed || !npc.isMounted) continue;
                var seat = slot.Seat != null && !slot.Seat.IsDestroyed ? slot.Seat : npc.GetMounted();
                SyncMountedNpcLook(npc, seat);
            }
        }

        // -------- Movement --------

        private void FixedUpdate()
        {
            if (!_fullySpawned || _route == null) return;

            if (_moving && !_isStopped)
                _leadDistance += Speed * Time.fixedDeltaTime;

            for (int i = 0; i < _vehicles.Count; i++)
            {
                var v = _vehicles[i];
                if (v?.Entity == null || v.Entity.IsDestroyed) continue;

                // Keep the vehicle on rails (some vehicle scripts try to re-enable physics).
                if (v.Body != null && !v.Body.isKinematic)
                    v.Body.isKinematic = true;

                float d = _leadDistance - i * Spacing;
                if (d < 0f) d = 0f;

                _route.Sample(d, out Vector3 pos, out Vector3 dir);
                pos.y += v.HeightOffset;
                v.Entity.transform.position = pos;
                if (dir.sqrMagnitude > 0.0001f)
                    v.Entity.transform.rotation = Quaternion.LookRotation(dir);
            }

            _netTimer += Time.fixedDeltaTime;
            if (_netTimer >= 0.1f)
            {
                _netTimer = 0f;
                foreach (var v in _vehicles)
                {
                    if (v?.Entity != null && !v.Entity.IsDestroyed)
                        v.Entity.SendNetworkUpdate();
                }
                // Keep mounted NPC facing aligned with seat while the convoy turns on rails.
                if (!IsAggressive())
                    UpdateAllMountedNpcLookRotation();
            }
        }

        // -------- Event loop / behavior --------

        private IEnumerator EventCoroutine()
        {
            while (_eventTime > 0)
            {
                // Oxide: only resume after StopTime if unmounted NPCs are still alive and crates are not all looted.
                if (_isStopped && !_isEventLooted)
                {
                    _stopTime--;
                    if (_stopTime == 0)
                    {
                        int roaming = GetUnmountedLiveNpcCount();
                        if (roaming > 0)
                            SwitchMoving(true);
                        else
                            UnityEngine.Debug.Log("[Convoy] Stop timer expired with no roaming NPCs left — staying stopped for loot.");
                    }
                }

                if (!_isStopped && _aggressiveTime > 0)
                {
                    _aggressiveTime--;
                    if (_aggressiveTime <= 0)
                        SwitchAggressive(false);
                }

                UpdateMarkers();
                UpdateConvoyState();

                // Oxide: UpdateAllMountedNpcLookRotation when not aggressive
                if (!IsAggressive())
                    UpdateAllMountedNpcLookRotation();

                var timeNotify = Cfg?.NotifyConfig?.TimeNotifications;
                if (timeNotify != null && timeNotify.Contains(_eventTime) && !_isEventLooted)
                {
                    string prefix = Cfg?.Prefix ?? "[Convoy]";
                    ConvoyNotifyStub.SendMessageToAll("RemainTime", prefix, EventConfig.DisplayName ?? "Convoy", _eventTime);
                }

                if (_eventTime % 30 == 0 && EventConfig != null && EventConfig.EventTime - _eventTime > 30)
                    EventPassingCheck();

                _eventTime--;
                yield return CoroutineExWait(1f);
            }

            EventLauncher.StopEvent();
        }

        /// <summary>Oxide NpcSpawnManager.GetEventNpcCount: living event NPCs that are on foot.</summary>
        private int GetUnmountedLiveNpcCount()
        {
            int n = 0;
            for (int i = 0; i < _npcSlots.Count; i++)
            {
                ScientistNPC npc = _npcSlots[i]?.Npc;
                if (npc == null || npc.IsDestroyed) continue;
                if (npc.isMounted) continue;
                n++;
            }
            return n;
        }

        public void OnEventCrateLooted(BaseEntity crate)
        {
            if (crate?.net == null) return;
            _lootedContainersUids.Add((ulong)crate.net.ID.Value);
            EventPassingCheck();
        }

        /// <summary>
        /// Oxide EventPassingCheck: once every crate is opened or destroyed, freeze the convoy
        /// and despawn after Main Setting → Time to destroy the convoy after opening all the crates.
        /// </summary>
        public void EventPassingCheck()
        {
            if (_isEventLooted) return;
            if (_crates.Count == 0) return;

            int unlooted = 0;
            foreach (var c in _crates)
            {
                if (c == null || c.IsDestroyed || c.net == null) continue;
                if (_lootedContainersUids.Contains((ulong)c.net.ID.Value)) continue;
                unlooted++;
            }

            if (unlooted > 0) return;

            _isEventLooted = true;
            SwitchMoving(false);
            int endTime = Cfg?.MainConfig?.EndAfterLootTime ?? 300;
            if (endTime < 0) endTime = 0;
            _eventTime = endTime;
            string prefix = Cfg?.Prefix ?? "[Convoy]";
            ConvoyNotifyStub.SendMessageToAll("Looted", prefix, EventConfig?.DisplayName ?? "Convoy");
            UnityEngine.Debug.Log("[Convoy] All crates opened or destroyed — convoy will despawn in " + endTime + "s.");
        }

        private void UpdateConvoyState()
        {
            ConvoyState.IsMoving = _moving && !_isStopped;
            ConvoyState.NpcsAlive = ConvoyGrimmNpc.LiveNpcCount > 0;
        }

        public bool IsStopped() => _isStopped;
        public bool IsFullySpawned() => _fullySpawned;

        public bool IsAggressive()
        {
            var bc = Cfg?.BehaviorConfig;
            if (bc == null) return true;
            return bc.AggressiveTime < 0 || _aggressiveTime > 0 || (bc.IsStopConvoyAggressive && _isStopped);
        }

        /// <summary>Called from the Hurt patch when a player damages a convoy entity.</summary>
        public void OnConvoyAttacked(BasePlayer attacker)
        {
            if (!_fullySpawned) return;

            if (attacker != null && !(attacker is NPCPlayer))
                _lastAttacker = attacker;

            // Announce only when transitioning into stop/aggro (Oxide parity — avoid spam every hit).
            var bc = Cfg?.BehaviorConfig;
            bool shouldAnnounce = attacker != null
                && ((_aggressiveTime <= 0 && bc != null && bc.AggressiveTime > 0) || !_isStopped);

            SwitchAggressive(true);
            SwitchMoving(false);
            string who = attacker != null ? attacker.displayName : "?";
            UnityEngine.Debug.Log("[Convoy] Attacked by " + who + " — convoy stopped, NPCs dismounting.");

            // Immediate aggro for any NPC still mounted (CombatStationary) before respawn finishes.
            if (_lastAttacker != null)
                AggroAllNpc(_lastAttacker);

            if (shouldAnnounce)
            {
                string prefix = Cfg?.Prefix ?? "[Convoy]";
                ConvoyNotifyStub.SendMessageToAll("ConvoyAttacked", prefix, who);
            }
        }

        public void SwitchAggressive(bool aggressive)
        {
            var bc = Cfg?.BehaviorConfig;
            if (aggressive && bc != null)
                _aggressiveTime = bc.AggressiveTime;

            bool enableTurrets = aggressive || (bc != null && bc.AggressiveTime <= 0);

            foreach (var t in _turrets)
            {
                if (t == null || t.IsDestroyed) continue;
                if (enableTurrets && t.IsPowered()) continue;
                t.UpdateFromInput(enableTurrets ? 10 : 0, 0);
            }
            foreach (var s in _samSites)
            {
                if (s == null || s.IsDestroyed) continue;
                s.UpdateFromInput(enableTurrets ? 100 : 0, 0);
            }
        }

        public void SwitchMoving(bool moving)
        {
            if (!_fullySpawned) return;
            var bc = Cfg?.BehaviorConfig;

            if (moving)
            {
                if (_isStopped)
                {
                    MountAllNpc();
                    ConvoyZoneController.TryDeleteZone();
                    _isStopped = false;
                    _stopTime = 0;
                    _moving = true;
                    UnityEngine.Debug.Log("[Convoy] Resuming movement.");
                }
            }
            else
            {
                if (!_isStopped)
                {
                    // Mark stopped BEFORE dismount/respawn so a RoamAllNpc failure cannot leave the convoy driving.
                    _stopTime = bc?.StopTime ?? 80;
                    if (_stopTime <= 0) _stopTime = 80;
                    _isStopped = true;
                    _moving = false;
                    UpdateConvoyState();
                    try
                    {
                        RoamAllNpc();
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning("[Convoy] RoamAllNpc failed: " + ex.Message);
                    }
                    // Oxide ZoneController.CreateZone — border spheres + optional PveMode ownership zone.
                    BasePlayer zoneOwner = null;
                    var pve = Cfg?.SupportedPluginsConfig?.PveMode;
                    if (pve != null && pve.Enable && pve.OwnerIsStopper && _lastAttacker != null
                        && !PveModeManager.IsPlayerHaveCooldown((ulong)_lastAttacker.userID))
                        zoneOwner = _lastAttacker;
                    ConvoyZoneController.CreateZone(zoneOwner);
                }
                else
                {
                    // Already stopped — refresh stop timer (keep fighting).
                    _stopTime = bc?.StopTime ?? 80;
                    if (_stopTime <= 0) _stopTime = 80;
                }
            }

            UpdateConvoyState();
        }

        /// <summary>
        /// Oxide RoamAllNpc parity: kill mounted NPCs and respawn them on foot with full Chase/Combat states.
        /// In-place DismountPlayer left them mounted / CombatStationary-only with ChaseRange=0.
        /// </summary>
        private void RoamAllNpc()
        {
            Convoy.Patches.ConvoyDismountGuard.AllowCombatDismount = true;
            try
            {
                for (int i = 0; i < _npcSlots.Count; i++)
                {
                    NpcSlot slot = _npcSlots[i];
                    if (slot == null || slot.IsRoaming || !slot.AllowDismount) continue;
                    ScientistNPC npc = slot.Npc;
                    if (npc == null || npc.IsDestroyed) continue;

                    Vector3 from = npc.transform.position;
                    if (!TryFindGroundNear(from, out Vector3 ground))
                        ground = from + Vector3.up * 0.35f;

                    float healthFrac = Mathf.Clamp01(npc.healthFraction);
                    Quaternion rot = npc.transform.rotation;

                    try
                    {
                        var mount = npc.GetMounted() ?? slot.Seat;
                        if (mount != null)
                            mount.DismountPlayer(npc, true);
                        npc.EnsureDismounted();
                    }
                    catch { }

                    if (npc.net != null)
                    {
                        ulong oldId = (ulong)npc.net.ID.Value;
                        ConvoyState.UnregisterNpc(oldId);
                        ConvoyGrimmNpc.Unregister(oldId);
                    }

                    try { npc.Kill(); } catch { }

                    ScientistNPC replacement = ConvoyGrimmNpc.SpawnNpc(slot.Config, ground, rot, mounted: false);
                    if (replacement == null)
                    {
                        // Spawn failed — if Kill left us with no NPC, log; do not leave slot pointing at destroyed entity.
                        UnityEngine.Debug.LogWarning("[Convoy] Failed to respawn dismounted NPC for preset " + slot.PresetName);
                        if (npc != null && !npc.IsDestroyed)
                        {
                            try
                            {
                                npc.MovePosition(ground);
                                npc.transform.position = ground;
                                npc.ServerPosition = ground;
                                ConvoyGrimmNpc.EnableGroundCombat(npc, slot.Config);
                                slot.IsRoaming = true;
                                if (_lastAttacker != null && !_lastAttacker.IsDestroyed)
                                    ConvoyGrimmNpc.ForceAggro(npc, _lastAttacker);
                            }
                            catch { slot.Npc = null; }
                        }
                        else
                            slot.Npc = null;
                        continue;
                    }

                    try
                    {
                        float maxHp = replacement.MaxHealth();
                        if (maxHp > 0f)
                            replacement.SetHealth(maxHp * healthFrac);
                    }
                    catch { }

                    slot.Npc = replacement;
                    slot.IsRoaming = true;
                    if (replacement.net != null)
                        ConvoyState.RegisterNpcPreset((ulong)replacement.net.ID.Value, slot.PresetName);

                    if (_lastAttacker != null && !_lastAttacker.IsDestroyed)
                        ConvoyGrimmNpc.ForceAggro(replacement, _lastAttacker);
                }
            }
            finally
            {
                Convoy.Patches.ConvoyDismountGuard.AllowCombatDismount = false;
            }
        }

        private void AggroAllNpc(BasePlayer attacker)
        {
            if (attacker == null || attacker.IsDestroyed) return;
            for (int i = 0; i < _npcSlots.Count; i++)
            {
                NpcSlot slot = _npcSlots[i];
                if (slot?.Npc == null || slot.Npc.IsDestroyed) continue;
                ConvoyGrimmNpc.ForceAggro(slot.Npc, attacker);
            }
        }

        /// <summary>
        /// Remount living roaming NPCs when the convoy starts moving again.
        /// Oxide parity: do not spawn replacements for NPCs the player already killed.
        /// Movement is kinematic, so dead drivers are not replaced either.
        /// </summary>
        private void MountAllNpc()
        {
            int remounted = 0;
            int skippedDead = 0;
            foreach (var slot in _npcSlots)
            {
                if (slot == null || !slot.IsRoaming) continue;
                if (slot.Seat == null || slot.Seat.IsDestroyed)
                {
                    slot.IsRoaming = false;
                    continue;
                }

                var npc = slot.Npc;
                if (npc == null || npc.IsDestroyed)
                {
                    skippedDead++;
                    slot.Npc = null;
                    slot.IsRoaming = false;
                    continue;
                }

                ConvoyGrimmNpc.PauseForMount(npc);
                try
                {
                    npc.EnsureDismounted();
                }
                catch { }

                ConfigureConvoySeat(slot.Seat, slot.Vehicle as BaseVehicle);
                slot.Seat.AttemptMount(npc, false);
                SyncMountedNpcLook(npc, slot.Seat);
                slot.IsRoaming = false;
                remounted++;
            }

            if (skippedDead > 0)
                UnityEngine.Debug.Log("[Convoy] Remount skipped " + skippedDead + " dead NPC slot(s); remounted " + remounted + " survivor(s).");
        }

        private static bool TryFindGroundNear(Vector3 origin, out Vector3 ground)
        {
            ground = origin;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(origin, out hit, 8f, NavMesh.AllAreas))
            {
                ground = hit.position;
                return true;
            }

            // Side-step off the road centerline and sample again.
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                Vector3 probe = origin + new Vector3(Mathf.Cos(ang) * 3f, 2f, Mathf.Sin(ang) * 3f);
                if (NavMesh.SamplePosition(probe, out hit, 6f, NavMesh.AllAreas))
                {
                    ground = hit.position;
                    return true;
                }
            }

            if (TerrainMeta.HeightMap != null)
            {
                ground = origin;
                ground.y = TerrainMeta.HeightMap.GetHeight(origin) + 0.2f;
                return true;
            }
            return false;
        }

        /// <summary>True if this entity (or its vehicle parent/module) belongs to the active convoy.</summary>
        public bool IsEventCombatTarget(BaseEntity entity)
        {
            if (entity?.net == null) return false;
            ulong netId = (ulong)entity.net.ID.Value;
            if (ConvoyState.IsConvoyEntity(netId)) return true;
            if (IsConvoyVehicle(netId)) return true;

            var module = entity as BaseVehicleModule;
            if (module != null)
            {
                var veh = module.Vehicle;
                if (veh?.net != null && IsConvoyVehicle((ulong)veh.net.ID.Value))
                    return true;
            }

            var parent = entity.GetParentEntity();
            if (parent?.net != null)
            {
                ulong pid = (ulong)parent.net.ID.Value;
                if (IsConvoyVehicle(pid) || ConvoyState.IsConvoyEntity(pid))
                    return true;
            }

            var npc = entity as ScientistNPC;
            if (npc != null && ConvoyGrimmNpc.IsConvoyNpc(netId))
                return true;

            return false;
        }

        // -------- Markers --------

        private void CreateMarkers()
        {
            var mc = Cfg?.MarkerConfig;
            if (mc == null || !mc.Enable) return;
            Vector3 pos = GetEventPosition();

            if (mc.UseRingMarker)
            {
                var radius = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", pos) as MapMarkerGenericRadius;
                if (radius != null)
                {
                    radius.enableSaving = false;
                    radius.Spawn();
                    radius.radius = mc.Radius > 0 ? mc.Radius : 0.2f;
                    radius.alpha = mc.Alpha;
                    if (mc.Color1 != null) radius.color1 = new Color(mc.Color1.R, mc.Color1.G, mc.Color1.B);
                    if (mc.Color2 != null) radius.color2 = new Color(mc.Color2.R, mc.Color2.G, mc.Color2.B);
                    radius.SendUpdate();
                    _radiusMarker = radius;
                }
            }

            if (mc.UseShopMarker)
            {
                var vending = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", pos) as VendingMachineMapMarker;
                if (vending != null)
                {
                    vending.enableSaving = false;
                    vending.Spawn();
                    vending.markerShopName = EventConfig.DisplayName ?? "Convoy";
                    _vendingMarker = vending;
                }
            }
        }

        private void UpdateMarkers()
        {
            Vector3 pos = GetEventPosition();
            if (_radiusMarker != null && !_radiusMarker.IsDestroyed)
            {
                _radiusMarker.transform.position = pos;
                (_radiusMarker as MapMarkerGenericRadius)?.SendUpdate();
                _radiusMarker.SendNetworkUpdate();
            }
            if (_vendingMarker != null && !_vendingMarker.IsDestroyed)
            {
                _vendingMarker.transform.position = pos;
                var vm = _vendingMarker as VendingMachineMapMarker;
                if (vm != null)
                {
                    string name = EventConfig.DisplayName ?? "Convoy";
                    string ownerName = PveModeManager.GetOwnerDisplayNameForMarker();
                    if (!string.IsNullOrEmpty(ownerName))
                        vm.markerShopName = $"{name} ({FormatTime(_eventTime)}) [{ownerName}]";
                    else
                        vm.markerShopName = $"{name} ({FormatTime(_eventTime)})";
                }
                _vendingMarker.SendNetworkUpdate();
            }
        }

        public Vector3 GetEventPosition()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var v in _vehicles)
            {
                if (v?.Entity != null && !v.Entity.IsDestroyed)
                {
                    sum += v.Entity.transform.position;
                    n++;
                }
            }
            return n > 0 ? sum / n : StartPos;
        }

        public bool IsConvoyVehicle(ulong netId)
        {
            foreach (var v in _vehicles)
                if (v?.Entity?.net != null && (ulong)v.Entity.net.ID.Value == netId)
                    return true;
            return false;
        }

        public float GetBradleyBuildingDamageScale(ulong bradleyNetId)
        {
            foreach (var v in _vehicles)
            {
                if (v?.Entity?.net != null && (ulong)v.Entity.net.ID.Value == bradleyNetId)
                    return v.BradleyBuildingDamageScale;
            }
            return -1f;
        }

        // -------- Cleanup --------

        public void DeleteController()
        {
            if (_eventCo != null) ServerMgr.Instance.StopCoroutine(_eventCo);
            if (_spawnCo != null) ServerMgr.Instance.StopCoroutine(_spawnCo);

            ConvoyZoneController.TryDeleteZone();
            KillConvoy();

            if (_radiusMarker != null && !_radiusMarker.IsDestroyed) _radiusMarker.Kill();
            if (_vendingMarker != null && !_vendingMarker.IsDestroyed) _vendingMarker.Kill();

            if (Instance == this) Instance = null;
            if (gameObject != null) Destroy(gameObject);
        }

        public bool IsEventTurret(ulong netId)
        {
            foreach (var t in _turrets)
            {
                if (t != null && t.net != null && (ulong)t.net.ID.Value == netId)
                    return true;
            }
            return false;
        }

        public HashSet<ulong> GetEventCratesNetIds()
        {
            var ids = new HashSet<ulong>();
            foreach (var c in _crates)
                if (c != null && !c.IsDestroyed && c.net != null)
                    ids.Add((ulong)c.net.ID.Value);
            return ids;
        }

        public HashSet<ulong> GetAliveTurretsNetIds()
        {
            var ids = new HashSet<ulong>();
            foreach (var t in _turrets)
                if (t != null && !t.IsDestroyed && t.net != null)
                    ids.Add((ulong)t.net.ID.Value);
            foreach (var s in _samSites)
                if (s != null && !s.IsDestroyed && s.net != null)
                    ids.Add((ulong)s.net.ID.Value);
            return ids;
        }

        public HashSet<ulong> GetAllBradleyNetIds()
        {
            var ids = new HashSet<ulong>();
            foreach (var v in _vehicles)
            {
                if (v?.Entity is BradleyAPC && v.Entity.net != null && !v.Entity.IsDestroyed)
                    ids.Add((ulong)v.Entity.net.ID.Value);
            }
            return ids;
        }

        public HashSet<ulong> GetEventNpcNetIds()
        {
            var ids = new HashSet<ulong>();
            foreach (var slot in _npcSlots)
            {
                var npc = slot?.Npc;
                if (npc != null && !npc.IsDestroyed && npc.net != null)
                    ids.Add((ulong)npc.net.ID.Value);
            }
            return ids;
        }

        private void KillConvoy()
        {
            Convoy.Patches.ConvoyDismountGuard.AllowCombatDismount = true;
            try
            {
                foreach (var slot in _npcSlots)
                {
                    var npc = slot?.Npc;
                    if (npc == null || npc.IsDestroyed) continue;
                    try
                    {
                        var mount = npc.GetMounted();
                        if (mount != null)
                            mount.DismountPlayer(npc, true);
                        npc.EnsureDismounted();
                    }
                    catch { }
                }
            }
            finally
            {
                Convoy.Patches.ConvoyDismountGuard.AllowCombatDismount = false;
            }

            foreach (var slot in _npcSlots)
            {
                var npc = slot?.Npc;
                if (npc != null && !npc.IsDestroyed)
                {
                    ConvoyGrimmNpc.Unregister(npc);
                    npc.Kill();
                }
            }
            _npcSlots.Clear();

            foreach (var t in _turrets)
                if (t != null && !t.IsDestroyed) t.Kill();
            _turrets.Clear();

            foreach (var s in _samSites)
                if (s != null && !s.IsDestroyed) s.Kill();
            _samSites.Clear();

            foreach (var c in _crates)
                if (c != null && !c.IsDestroyed) c.Kill();
            _crates.Clear();

            foreach (var v in _vehicles)
                if (v?.Entity != null && !v.Entity.IsDestroyed) v.Entity.Kill();
            _vehicles.Clear();
        }

        // -------- Helpers --------

        private static WaitForSeconds CoroutineExWait(float seconds) => new WaitForSeconds(seconds);

        private static string FormatTime(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int m = seconds / 60;
            int s = seconds % 60;
            return $"{m:00}:{s:00}";
        }

        private static string GridToString(Vector3 pos)
        {
            try
            {
                if (TerrainMeta.Size.x <= 0) return "?";
                float worldSize = TerrainMeta.Size.x;
                float offset = worldSize / 2f;
                int gx = Mathf.FloorToInt((pos.x + offset) / 146.3f);
                int gz = Mathf.FloorToInt((worldSize - (pos.z + offset)) / 146.3f);
                string col = "";
                int n = gx;
                do { col = (char)('A' + (n % 26)) + col; n = n / 26 - 1; } while (n >= 0);
                return col + gz;
            }
            catch { return "?"; }
        }
    }
}
