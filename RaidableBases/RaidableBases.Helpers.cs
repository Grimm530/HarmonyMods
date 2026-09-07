using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Color = UnityEngine.Color;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Helpers

        internal static bool IsRocketLauncher(HeldEntity weapon) => weapon != null && weapon is BaseLauncher && weapon.ShortPrefabName == "rocket_launcher.entity";

        private bool IsLauncherProjectile(BaseLauncher launcher, TimedExplosive projectile)
        {
            ItemDefinition ammoType = launcher.primaryMagazine?.ammoType;
            if (ammoType == null || !_itemModProjectile.TryGetValue(ammoType, out ItemModProjectile projectileMod)) return false;
            return projectileMod != null && projectileMod.GetOverrideProjectile(launcher)?.resourcePath == projectile.PrefabName;
        }

        private static void FixTurretRocket(AutoTurret turret, BaseLauncher launcher, TimedExplosive rocket)
        {
            if (turret == null || launcher == null || launcher.MuzzlePoint == null || !rocket.TryGetComponent(out ServerProjectile projectile))
            {
                return;
            }

            Matrix4x4 muzzle = turret.GetCenterMuzzle();
            if (turret.IsBeingControlled)
            {
                muzzle *= turret.toRCEyesFromPitch;
            }
            Vector3 velocity = projectile.CurrentVelocity;
            float speed = velocity.magnitude;
            if (speed <= 0f)
            {
                speed = projectile.speed * launcher.initialSpeedMultiplier;
                velocity = launcher.MuzzlePoint.forward * speed;
            }
            if (speed <= 0f)
            {
                return;
            }

            Quaternion correction = muzzle.rotation * Quaternion.Inverse(launcher.MuzzlePoint.rotation);
            Vector3 direction = (correction * velocity).normalized;
            Vector3 position = muzzle.GetPosition();
            float launchOffset = 1f;
            if (Physics.Raycast(position, direction, out var hit, launchOffset, projectile.mask))
            {
                launchOffset = Mathf.Max(0f, hit.distance - 0.1f);
            }

            rocket.transform.position = position + direction * launchOffset;
            projectile.ignoreEntity = turret;
            projectile.InitializeVelocity(direction * speed);
        }

        private static bool IsVanished(BasePlayer player) => player != null && (player.limitNetworking || player.isInvisible);

        private class FrameDeadline
        {
            private double ticksPerMillisecond;
            private double min;
            private double max;

            public long Value;

            public FrameDeadline(double max) : this(Math.Min(1d, max), max) { }

            public FrameDeadline(double min, double max)
            {
                ticksPerMillisecond = Stopwatch.Frequency / 1000d;
                this.min = min;
                this.max = max;
                Reset();
            }

            public bool Expired => Value != long.MaxValue && Stopwatch.GetTimestamp() >= Value;

            public void Reset() => Value = min < max ? GetFrameTimestamp(min, max) : GetFrameTimestamp(max);

            private long GetFrameTimestamp(double min, double max) => GetFrameTimestamp(GetScaledFrameMilliseconds(min, max));

            private long GetFrameTimestamp(double milliseconds) => Stopwatch.GetTimestamp() + (long)Math.Ceiling(milliseconds * ticksPerMillisecond);

            private double GetScaledFrameMilliseconds(double min, double max)
            {
                int fpsLimit = ConVar.FPS.limit;
                if (fpsLimit <= 0) return max;

                double frameTime = Performance.report.frameTime;
                if (frameTime <= 0d) return min;

                double scale = Math.Min(1d, (1000d / fpsLimit) / frameTime);
                return Math.Min(max, Math.Max(min, scale * max));
            }
        }

        private const double uiFrameBudgetMilliseconds = 1d;
        private const double markerFrameBudgetMilliseconds = 1d;
        private const double wallFrameBudgetMilliseconds = 1d;
        private const double clutterFrameBudgetMilliseconds = 1d;
        private const double setupFrameBudgetMilliseconds = 2d;
        private const double npcSchedulerFrameBudgetMilliseconds = 1d;
        private const double raidMaintenanceFrameBudgetMilliseconds = 1d;
        private const double undoFrameBudgetMilliseconds = 1d;
        private const double nearbyNotificationFrameBudgetMilliseconds = 1d;
        private const double searchFrameBudgetMilliseconds = 1d;

        private readonly Queue<string> exceptionMessages = new();
        private Timer exceptionMessageTimer;

        protected void QueueExceptionMessage(string message)
        {
            exceptionMessages.Enqueue(message);

            if (exceptionMessageTimer == null || exceptionMessageTimer.Destroyed)
            {
                PrintNextExceptionMessage();
            }
        }

        protected void PrintNextExceptionMessage()
        {
            if (exceptionMessages.Count > 0)
            {
                Puts(exceptionMessages.Dequeue());
            }

            exceptionMessageTimer = exceptionMessages.Count > 0 ? timer.Once(1f, PrintNextExceptionMessage) : null;
        }

        private bool AddDespawnException(BaseEntity entity)
        {
            if (entity.IsKilled() || !Get(entity.transform.position, out var raid)) return false;
            return !raid.RaidEntities.Contains(entity) && raid.DespawnExceptions.Add(entity);
        }

        private static bool IsDeepSeaOpen()
        {
            DeepSeaManager dsm = PointEntity<DeepSeaManager>.ServerInstance;
            if (dsm == null) return false;
            return dsm.IsBusy() || dsm.IsOpen();
        }

        private static void ResetToPool<T>(ref List<T> obj) // why a helper? because usage is cleaner with no name variance, and it has a null check.
        {
            if (obj != null) Pool.FreeUnmanaged(ref obj);
        }

        private static void ResetToPool<T>(ref HashSet<T> obj)
        {
            if (obj != null) Pool.FreeUnmanaged(ref obj);
        }

        private static void ResetToPool<TKey, TValue>(ref Dictionary<TKey, TValue> obj)
        {
            if (obj != null) Pool.FreeUnmanaged(ref obj);
        }

        private static void ResetToPool<T>(ref T obj) where T : class, Pool.IPooled, new()
        {
            if (obj != null) Pool.Free(ref obj);
        }

        public static bool IsCustomEntity(BaseEntity m) => m.PrefabName.StartsWith("assets/custom/");
        public static PooledList<T> DisposableList<T>() => Pool.Get<PooledList<T>>();
        public static PooledHashSet<T> DisposableHashSet<T>() => Pool.Get<PooledHashSet<T>>();
        private static void SafelyKill(BaseEntity entity) => entity.SafelyKill();

        public static void SafelyKillNpc(HumanoidNPC npc)
        {
            if (npc != null)
            {
                ulong userid = npc.userID;
                BasePlayer.bots.Remove(npc);
                npc.SafelyKill();
                BasePlayer.freeBotIds.Remove(userid);
            }
        }

        private bool IsCustomSpawn(Vector3 v)
        {
            if (GridController.Spawns.Exists(x => x.Key != RaidableType.Grid && x.Value?.Spawns?.Exists(s => Vector3.Distance(v, s.Location) < 50f) == true)) return true;
            return Buildings.Profiles.Values.Exists(x => x?.Spawns?.Values?.Exists(s => s?.Spawns?.Exists(spawn => Vector3.Distance(v, spawn.Location) <= x.Options.ProtectionRadius(RaidableType.None)) == true) == true);
        }

        private void CheckPlayersNearEvents()
        {
            if (Raids.Count == 0 || config.EventMessages.Nearby <= 0f)
            {
                return;
            }

            checkPlayersNearEventsCo = ServerMgr.Instance.StartCoroutine(CheckNearCo());
        }

        private IEnumerator CheckNearCo()
        {
            double nearbySqr = config.EventMessages.Nearby * config.EventMessages.Nearby;
            FrameDeadline deadline = new(nearbyNotificationFrameBudgetMilliseconds);
            using var players = BasePlayer.activePlayerList.ToPooledList();
            using var raids = Raids.ToPooledList();

            for (int i = 0; i < players.Count; i++)
            {
                BasePlayer player = players[i];
                if (player == null || player.IsDestroyed || !player.IsConnected)
                {
                    continue;
                }

                Vector3 position = player.transform.position;

                for (int j = 0; j < raids.Count; j++)
                {
                    RaidableBase raid = raids[j];
                    if (raid == null || !raid.IsOpened || raid.IsDespawning || raid.ownerId != 0 || raid.IsPayLocked) continue;
                    if (raid.NotifiedNearby.Contains(player.userID)) continue;
                    float distSqr = (raid.Location - position).sqrMagnitude;
                    if (distSqr < nearbySqr)
                    {
                        raid.NotifiedNearby.Add(player.userID);
                        if (distSqr > raid.ProtectionRadiusSqr(100f)) SendNotification(player, "Near", raid.Options.Mode);
                    }

                    if (deadline.Expired)
                    {
                        yield return null;
                        deadline.Reset();
                    }
                }
            }

            timer.Once(30f, CheckPlayersNearEvents);

            checkPlayersNearEventsCo = null;
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission("raidablebases.infoui", this);
            permission.RegisterPermission("raidablebases.allow", this);
            permission.RegisterPermission("raidablebases.allow.commands", this);
            permission.RegisterPermission("raidablebases.bypassmaxmanualeventlimit", this);
            permission.RegisterPermission("raidablebases.setowner", this);
            permission.RegisterPermission("raidablebases.clearowner", this);
            permission.RegisterPermission("raidablebases.ladder.exclude", this);
            permission.RegisterPermission("raidablebases.durabilitybypass", this);
            permission.RegisterPermission("raidablebases.ddraw", this);
            permission.RegisterPermission("raidablebases.mapteleport", this);
            permission.RegisterPermission("raidablebases.canbypass", this);
            permission.RegisterPermission("raidablebases.lockoutbypass", this);
            permission.RegisterPermission("raidablebases.blockbypass", this);
            permission.RegisterPermission("raidablebases.banned", this);
            permission.RegisterPermission("raidablebases.vipcooldown", this);
            permission.RegisterPermission("raidablebases.despawn.buyraid", this);
            permission.RegisterPermission("raidablebases.notitle", this);
            permission.RegisterPermission("raidablebases.block.fauxadmin", this);
            permission.RegisterPermission("raidablebases.elevators.bypass.building", this);
            permission.RegisterPermission("raidablebases.elevators.bypass.card", this);
            permission.RegisterPermission("raidablebases.time", this);
            permission.RegisterPermission("raidablebases.timebypass", this);
            permission.RegisterPermission("raidablebases.buyraid", this);
            permission.RegisterPermission("raidablebases.buyraid.free", this);
            permission.RegisterPermission("raidablebases.buyraid.banned", this);
            permission.RegisterPermission("raidablebases.buyraid.prefabteleport", this);
            permission.RegisterPermission("raidablebases.buyable.bypass.cooldown", this);
            permission.RegisterPermission("raidablebases.buyable.spawn.filenames", this);
            permission.RegisterPermission("raidablebases.buyable.vip.pve", this);
            permission.RegisterPermission("raidablebases.buyable.vip.pvp", this);
            permission.RegisterPermission("raidablebases.hoggingbypass", this);
            permission.RegisterPermission("raidablebases.block.filenames", this);
            permission.RegisterPermission("raidablebases.keepbackpackplugin", this);
            permission.RegisterPermission("raidablebases.keepbackpackrust", this);
            permission.RegisterPermission("raidablebases.buyraid.pvponly", this);
            permission.RegisterPermission("raidablebases.buyraid.pveonly", this);
            permission.RegisterPermission("raidablebases.invitecommand", this);
            permission.RegisterPermission("raidablebases.limitedannouncements", this);
        }

        public void LoadPlayerData(bool initial)
        {
            try { data = HarmonyDataLayer.ReadObject<StoredData>(Name); } catch (Exception ex) { Puts(ex); }
            data ??= new();
            data.Players ??= new();
            data.BuyableCooldowns ??= new();
            data.Cycle ??= new();
            data.Cycle.Instance = this;
            if (initial && !config.Settings.Management.RequireAllSpawnsPersist)
            {
                data.Cycle._buildings.Clear();
            }
            if (data.protocol == -1)
            {
                data.protocol = Rust.Protocol.save;
            }
            if (data.protocol != Rust.Protocol.save)
            {
                if (config.Settings.Wipe.Protocol)
                {
                    Puts("Protocol change detected; wiping ranked ladder");
                    wiped = true;
                }
                data.protocol = Rust.Protocol.save;
            }
        }

        private void SaveData()
        {
            SavePlayerData();
            UI.SaveOffsetData();
        }

        public void SavePlayerData()
        {
            if (data != null)
            {
                if (RaidableModes.Count > 0) data.BuyableCooldowns.RemoveAll((userid, bi) => !BuyableInfo.HasTimeRemaining(this, userid));
                data.Lockouts.RemoveAll((userid, lo) => !lo.Any());
                data.Players.RemoveAll((useridstring, playerInfo) =>
                {
                    if (playerInfo.IsExpired(config.RankedLadder.Days))
                    {
                        if (ulong.TryParse(useridstring, out var userid))
                        {
                            UI?.Offsets?.Remove(userid);
                        }
                        return true;
                    }
                    return playerInfo.TotalRaids == 0;
                });
                HarmonyDataLayer.WriteObject(Name, data);
            }
        }

        private string GetPlayerData() => JsonConvert.SerializeObject(data.Players);

        internal void StartEntityCleanup()
        {
            IsSpawnerBusy = true;

            var batches = new List<UndoLoopBatch>();
            using var raids = Raids.ToPooledList();
            using var sb = DisposableBuilder.Get();
            string rb = $"{DateTime.Now.ToShortTimeString()} [{nameof(RaidableBases)}] ";
            foreach (var raid in raids)
            {
                if (!IsShuttingDown)
                {
                    string padding = sb.Length != 0 ? rb : string.Empty;
                    string message = mx("Destroyed Raid", null, $"{PositionToGrid(raid.Location, false)} {raid.Location} ({raid.BaseName}: {raid.Options.Mode})");
                    if (!string.IsNullOrEmpty(message)) sb.AppendLine(padding + message); // messages can be blanked out in the language file to disable them.
                    if (raid.IsOpened) TryInvokeMethod(raid.AwardRaiders);
                    UndoLoopBatch batch = new(raid.Options.Setup.DespawnLimit);
                    foreach (var entity in raid.Entities)
                    {
                        if (!entity.IsKilled() && !raid.DespawnExceptions.Contains(entity))
                        {
                            if (TryDisablePickup(entity))
                            {
                                batch.PickupEntities.Add(entity);
                            }
                            else
                            {
                                batch.Entities.Add(entity);
                            }
                            ulong entityId = entity.net?.ID.Value ?? 0uL;
                            if (entityId > batch.NewestEntityId)
                            {
                                batch.NewestEntityId = entityId;
                            }
                        }
                    }
                    if (batch.PickupEntities.Count > 0 || batch.Entities.Count > 0)
                    {
                        batches.Add(batch);
                    }
                    raid.Entities.Clear();
                }

                raid.Despawn();
            }

            if (batches.Count == 0)
            {
                TryInvokeMethod(RemoveHeldEntities);
                TryInvokeMethod(UnsetStatics);
            }
            else UndoLoop(batches);

            if (sb.Length > 0)
            {
                Puts(sb.ToString());
            }
        }

        private static bool TryDisablePickup(BaseEntity entity)
        {
            if (entity.OwnerID.IsSteamId() || !entity.Is(out BaseCombatEntity e) || !e.pickup.enabled)
            {
                return false;
            }
            e.pickup.enabled = false;
            return true;
        }

        private void UnsetStatics()
        {
            UI.DestroyAll();
            EntityToRaid.Clear();
            HtmlTagRegex = null;
            _extensions.Clear();
            harmonyEngine?.Dispose();
            harmonyEngine = null;
        }

        private bool CheckForWipe(bool revoke)
        {
            bool ret = false;

            if (wiped)
            {
                using var raids = DisposableList<int>();

                if (data.Players.Count > 0)
                {
                    if (AssignTreasureHunters())
                    {
                        foreach (var info in data.Players.Values)
                        {
                            if (info.Raids > 0)
                            {
                                raids.Add(info.Raids);
                            }

                            if (config.Settings.Wipe.Current)
                            {
                                info.ResetWipe();
                            }

                            if (config.Settings.Wipe.Lifetime)
                            {
                                info.ResetLifetime();
                            }
                        }
                    }

                    if (raids.Count > 0)
                    {
                        ret = true;

                        var average = raids.Average();

                        data.Players.RemoveAll((userid, playerInfo) => playerInfo.TotalRaids < average);
                    }
                }

                wiped = false;
                data.Lockouts.Clear();
                NextTick(SaveData);

                if (revoke)
                {
                    RevokePermissionsAndGroups(config.Settings.Wipe.Remove);
                }
            }

            return ret;
        }

        private bool IsPocketDimensions(BasePlayer player, BaseEntity e)
        {
            if (e.skinID != 0 && e.ShortPrefabName == "woodbox_deployed" && PocketDimensions != null && player.GetActiveItem() is Item activeItem)
            {
                if (Convert.ToBoolean(PocketDimensions?.Call("CheckIsDimensionalItem", activeItem, true))) return true;
                if (Convert.ToBoolean(PocketDimensions?.Call("CheckIsDimensionalItem", activeItem, false))) return true;
            }
            return false;
        }

        public void BuyableTeleport(BasePlayer player)
        {
            if (player != null && !player.IsDestroyed && player.IsConnected && player.HasPermission("raidablebases.buyraid.prefabteleport"))
            {
                foreach (var raid in Raids)
                {
                    if (raid.Type != RaidableType.Purchased) continue;
                    if (raid.ownerId != player.userID) continue;
                    if (!raid.IsOpened) continue;
                    raid.Teleport(player);
                    break;
                }
            }
        }

        private static float GetObstructionRadius(BuildingOptionsProtectionRadius radii, RaidableType type)
        {
            if (radii.Obstruction > 0)
            {
                return Mathf.Clamp(radii.Obstruction, CELL_SIZE, radii.Get(type));
            }
            return radii.Get(type);
        }

        public PasteData GetPasteData(string baseName)
        {
            if (!_pasteData.TryGetValue(baseName, out var pasteData))
            {
                _pasteData[baseName] = pasteData = new();
            }
            return pasteData;
        }

        private bool IsEventOwner(BasePlayer player, bool isLoading)
        {
            return Raids.Exists(raid => raid.ownerId == player.userID && (config.Settings.Buyable.PreventNew && raid.IsPayLocked || raid.IsOpened || raid.IsDespawning || isLoading && raid.IsLoading || config.Settings.Buyable.PreventHogging && raid.Type == RaidableType.Purchased && raid.IsHogging(player)));
        }

        private bool IsBuyableEventOwner(BasePlayer player)
        {
            if (player == null) return false;
            return Raids.Exists(raid => raid.ownerId == player.userID && raid.Type == RaidableType.Purchased);
        }

        private bool Has(NetworkableId networkableId)
        {
            foreach (var brain in HumanoidBrains.Values)
            {
                if (brain.npc != null && brain.npc.EqualNetID(networkableId))
                {
                    return true;
                }
            }
            return false;
        }

        private bool Has(TriggerBase trigger)
        {
            if (trigger != null)
            {
                foreach (var raid in Raids)
                {
                    if (raid.triggers.ContainsKey(trigger))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool Has(BasePlayer player)
        {
            return player is HumanoidNPC;
        }

        private static bool TryGetNetworkId(BaseEntity entity, out NetworkableId id)
        {
            if (!HasNetworkId(entity))
            {
                id = default;
                return false;
            }
            id = entity.net.ID;
            return id.IsValid;
        }

        private static bool HasNetworkId(BaseEntity entity) => entity.IsValid() && !entity.IsDestroyed;

        private bool Has(BaseEntity entity)
        {
            if (!TryGetNetworkId(entity, out NetworkableId id))
            {
                return false;
            }

            if (entity.skinID == RB_SKIN_ID)
            {
                return true;
            }

            return EntityToRaid.ContainsKey(id);
        }

        public int Get(RaidableType type)
        {
            int count = 0;
            foreach (var sp in Queues.queue)
            {
                if (sp.type == type)
                {
                    count++;
                }
            }
            foreach (var raid in Raids)
            {
                if (raid.Type == type && !raid.IsDespawning)
                {
                    count++;
                }
            }
            return count;
        }

        private bool HasLimit(RaidableType type)
        {
            return type is RaidableType.Maintained or RaidableType.Scheduled or RaidableType.Purchased;
        }

        public int Get(string mode, bool isPurchased)
        {
            int count = 0;
            foreach (var sp in Queues.queue)
            {
                if ((isPurchased == (sp.type == RaidableType.Purchased)) && sp.options.Mode == mode && HasLimit(sp.type))
                {
                    count++;
                }
            }
            foreach (var raid in Raids)
            {
                if ((isPurchased == (raid.Type == RaidableType.Purchased)) && raid.Options.Mode == mode && !raid.IsDespawning && HasLimit(raid.Type))
                {
                    count++;
                }
            }
            return count;
        }

        public bool Get(ulong userID, out HumanoidBrain brain)
        {
            return HumanoidBrains.TryGetValue(userID, out brain) && brain.raid != null ? brain.raid : null;
        }

        private uint GetLootSpawnSlotsPrefabID(ulong userID)
        {
            return HumanoidBrains.TryGetValue(userID, out var brain) ? brain.LootSpawnSlotsPrefabID : 0u;
        }

        public bool Get(Vector3 target, out RaidableBase raid, float f = 0f)
        {
            foreach (var x in Raids)
            {
                if (InRange(x.Location, target, x.ProtectionRadius + f))
                {
                    raid = x;
                    return true;
                }
            }
            raid = null;
            return false;
        }

        public bool Get(BasePlayer victim, HitInfo info, out RaidableBase raid)
        {
            if (Has(victim) && Get(victim.userID, out HumanoidBrain brain))
            {
                raid = brain.raid;
                return true;
            }
            if (GetPVPDelay(victim.userID, true, out DelaySettings ds) && ds.raid != null)
            {
                raid = ds.raid;
                return true;
            }
            if (Get(victim.transform.position, out raid))
            {
                return true;
            }
            if (info != null && info.PointStart != default && Get(info.PointStart, out raid))
            {
                return true;
            }
            raid = null;
            return false;
        }

        public bool Get(BaseEntity entity, ulong playerSteamID, out RaidableBase raid)
        {
            if (!playerSteamID.IsSteamId() && Get(playerSteamID, out HumanoidBrain brain))
            {
                raid = brain.raid;
                return true;
            }
            if (playerSteamID.IsSteamId() && GetPVPDelay(playerSteamID, true, out DelaySettings ds) && ds.raid != null)
            {
                raid = ds.raid;
                return true;
            }
            if (Get(entity.transform.position, out raid))
            {
                return true;
            }
            raid = null;
            return false;
        }

        public bool Get(BaseEntity entity, out RaidableBase raid)
        {
            if (!TryGetNetworkId(entity, out NetworkableId id))
            {
                raid = null;
                return false;
            }

            return EntityToRaid.TryGetValue(id, out raid) && raid != null;
        }

        private bool Get(TriggerBase trigger, out RaidableBase raid)
        {
            if (trigger != null)
            {
                foreach (var x in Raids)
                {
                    if (x.triggers.ContainsKey(trigger))
                    {
                        raid = x;
                        return true;
                    }
                }
            }
            raid = null;
            return false;
        }

        public bool IsTooClose(Vector3 target, float radius)
        {
            foreach (var raid in Raids)
            {
                if (InRange2D(raid.Location, target, radius))
                {
                    return true;
                }
            }
            return false;
        }

        private static void DrawText(BasePlayer player, float duration, Color color, Vector3 from, object text) => player?.SendConsoleCommand("ddraw.text", duration, color, from, $"<size=24>{text}</size>");
        private static void DrawLine(BasePlayer player, float duration, Color color, Vector3 from, Vector3 to) => player?.SendConsoleCommand("ddraw.line", duration, color, from, to);
        private static void DrawSphere(BasePlayer player, float duration, Color color, Vector3 from, float radius) => player?.SendConsoleCommand("ddraw.sphere", duration, color, from, radius);
        private static bool IsContainerKilled(DroppedItemContainer container) => container.IsKilled() || container.inventory == null || container.inventory.itemList == null;
        private static bool IsContainerKilled(RaidableBase.TurretInfo info) => info == null || info.IsKilled();
        private static bool IsContainerKilled(StorageContainer container) => container.IsKilled() || container.inventory == null || container.inventory.itemList == null;
        private static bool IsContainerKilled(ContainerIOEntity container) => container.IsKilled() || container.inventory == null || container.inventory.itemList == null;
        private static bool IsKilled(Item item) => item == null || item.isBroken || !item.IsValid();
        private static bool IsKilled(BaseEntity entity) => entity.IsKilled();

        internal void DestroyProtection()
        {
            if (_elevatorProtection != null)
            {
                UnityEngine.Object.DestroyImmediate(_elevatorProtection);
            }
            if (_turretProtection != null)
            {
                UnityEngine.Object.DestroyImmediate(_turretProtection);
            }
        }

        internal ProtectionProperties GetElevatorProtection()
        {
            if (_elevatorProtection == null)
            {
                _elevatorProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                _elevatorProtection.name = "EventElevatorProtection";
            }
            return _elevatorProtection;
        }

        internal ProtectionProperties GetTurretProtection()
        {
            if (_turretProtection == null)
            {
                _turretProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                _turretProtection.name = "EventTurretProtection";
            }
            return _turretProtection;
        }

        public void UpdateAllMarkers()
        {
            if (markerUpdateCo == null && Raids.Count > 0)
            {
                markerUpdateCo = ServerMgr.Instance.StartCoroutine(UpdateAllMarkersCo());
            }
        }

        private IEnumerator UpdateAllMarkersCo()
        {
            FrameDeadline deadline = new(markerFrameBudgetMilliseconds);
            using var raids = Raids.ToPooledList();

            for (int i = 0; i < raids.Count; i++)
            {
                RaidableBase raid = raids[i];
                if (raid != null && !raid.IsDespawning)
                {
                    raid.UpdateMarker();
                }

                if (deadline.Expired)
                {
                    yield return null;
                    deadline.Reset();
                }
            }

            markerUpdateCo = null;
        }

        private bool IsBusy(out Vector3 pastedLocation)
        {
            foreach (RaidableBase raid in Raids)
            {
                if (raid.IsDespawning || raid.IsLoading)
                {
                    pastedLocation = raid.Location;
                    return true;
                }
            }
            pastedLocation = Vector3.zero;
            return false;
        }

        public static void TryInvokeMethod(Action action)
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                Puts("{0} ERROR: {1}", action.Method.Name, ex);
            }
        }

        private bool IsKillableEntity(BaseEntity entity)
        {
            return entity.PrefabName.Contains("building") || DeployableItems.ContainsKey(entity.PrefabName) || (entity is VendingMachineMapMarker or MapMarkerGenericRadius or SphereEntity or HumanoidNPC);
        }

        private static PooledList<T> FindEntitiesOfType<T>(Vector3 a, float n, int m = -1, QueryTriggerInteraction queryTrigger = QueryTriggerInteraction.Collide) where T : BaseEntity
        {
            PooledList<T> entities = DisposableList<T>();
            Vis.Entities(a, n, entities, m, queryTrigger);
            entities.RemoveAll(IsKilled);
            return entities;
        }

        private void CheckOceanLevel()
        {
            if (OceanLevel != WaterSystem.OceanLevel)
            {
                OceanLevel = WaterSystem.OceanLevel;

                if (GridController.Spawns.TryGetValue(RaidableType.Grid, out var spawns))
                {
                    spawns.TryAddRange(CacheType.Submerged);
                }
            }
        }

        private void UnsubscribeSky() => SetOnSun(false, 0);

        private void SetOnSun(bool state, int retries = 0)
        {
            if (retries >= 3 || !config.Settings.Management.Lights)
            {
                return;
            }

            try
            {
                if (state)
                {
                    TOD_Sky.Instance.Components.Time.OnSunrise += OnSunrise;
                    TOD_Sky.Instance.Components.Time.OnSunset += OnSunset;
                }
                else
                {
                    TOD_Sky.Instance.Components.Time.OnSunrise -= OnSunrise;
                    TOD_Sky.Instance.Components.Time.OnSunset -= OnSunset;
                }
            }
            catch
            {
                timer.Once(10f, () => SetOnSun(state, ++retries));
            }
        }

        public void InitializeSkins()
        {
            foreach (var def in ItemManager.GetItemDefinitions())
            {
                int workbenchLevel = def.Blueprint?.workbenchLevelRequired ?? 0;
                DefinitionToWorkbenchLevel[def] = workbenchLevel;

                if (def.TryGetComponent<ItemModDeployable>(out var imd))
                {
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModDeployable);

                    if (RequiresOwnership(def)) PaidDeployableItems[imd.entityPrefab.resourcePath] = def;
                    DeployableItems[imd.entityPrefab.resourcePath] = def;
                    ItemDefinitions[def] = imd.entityPrefab.resourcePath;

                    if (imd.entityPrefab.GetEntity() is BaseSiegeWeapon siegeWeapon)
                    {
                        AddDefinitionFlag(def, ItemDefinitionFlags.BaseSiegeWeapon);
                        CacheWeaponEntity(siegeWeapon, def, workbenchLevel);

                        if (siegeWeapon is Catapult)
                        {
                            CatapultWeaponShortnames.Add(def.shortname);
                            CatapultWorkbenchLevel = Math.Max(CatapultWorkbenchLevel, workbenchLevel);
                        }
                    }
                }

                if (def.TryGetComponent<ItemModEntity>(out var ime))
                {
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModEntity);

                    if (ime.entityPrefab.GetEntity() is AttackEntity attackEntity)
                    {
                        AddDefinitionFlag(def, ItemDefinitionFlags.AttackEntity);
                        CacheWeaponEntity(attackEntity, def, workbenchLevel);

                        if (attackEntity is ThrownWeapon)
                        {
                            AddDefinitionFlag(def, ItemDefinitionFlags.ThrownWeapon);
                        }
                    }
                }

                if (def.TryGetComponent<ItemModProjectile>(out var imp))
                {
                    _itemModProjectile[def] = imp;
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModProjectile);
                    if (workbenchLevel > MaxConsideredWorkbenchLevel) MaxConsideredWorkbenchLevel = workbenchLevel;
                    bool isCatapultAmmo = imp.IsAmmo(AmmoTypes.CATAPULT_BOULDER);
                    CacheProjectileWorkbenchLevel(imp.projectileObject, def, workbenchLevel, isCatapultAmmo);
                    for (int i = 0; i < imp.SkinOverrides.Length; i++)
                    {
                        CacheProjectileWorkbenchLevel(imp.SkinOverrides[i].OverrideProjectile, def, workbenchLevel, isCatapultAmmo);
                    }
                }

                if (def.TryGetComponent<ItemModCatapultBoulder>(out var boulder))
                {
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModCatapultBoulder);
                    if (workbenchLevel > MaxConsideredWorkbenchLevel) MaxConsideredWorkbenchLevel = workbenchLevel;
                    foreach (var projectileSettings in boulder.projectileSettings)
                    {
                        CacheProjectileWorkbenchLevel(projectileSettings.prefab, def, workbenchLevel, true);
                    }
                }

                if (def.TryGetComponent<ItemModCookable>(out _))
                {
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModCookable);
                }

                if ((def.category == ItemCategory.Food || def.category == ItemCategory.Medical) && def.TryGetComponent<ItemModConsume>(out var con))
                {
                    _itemModConsume[def] = con;
                }
            }
        }

        /// <summary>Soft-start wrapper — yields so harmony.load does not freeze on large item tables.</summary>
        public IEnumerator InitializeSkinsCoroutine()
        {
            const int yieldEvery = 50;
            int count = 0;
            foreach (var def in ItemManager.GetItemDefinitions())
            {
                int workbenchLevel = def.Blueprint?.workbenchLevelRequired ?? 0;
                DefinitionToWorkbenchLevel[def] = workbenchLevel;

                if (def.TryGetComponent<ItemModDeployable>(out var imd))
                {
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModDeployable);
                    if (RequiresOwnership(def)) PaidDeployableItems[imd.entityPrefab.resourcePath] = def;
                    DeployableItems[imd.entityPrefab.resourcePath] = def;
                    ItemDefinitions[def] = imd.entityPrefab.resourcePath;
                    if (imd.entityPrefab.GetEntity() is BaseSiegeWeapon siegeWeapon)
                    {
                        AddDefinitionFlag(def, ItemDefinitionFlags.BaseSiegeWeapon);
                        CacheWeaponEntity(siegeWeapon, def, workbenchLevel);
                        if (siegeWeapon is Catapult)
                        {
                            CatapultWeaponShortnames.Add(def.shortname);
                            CatapultWorkbenchLevel = Math.Max(CatapultWorkbenchLevel, workbenchLevel);
                        }
                    }
                }

                if (def.TryGetComponent<ItemModEntity>(out var ime))
                {
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModEntity);
                    if (ime.entityPrefab.GetEntity() is AttackEntity attackEntity)
                    {
                        AddDefinitionFlag(def, ItemDefinitionFlags.AttackEntity);
                        CacheWeaponEntity(attackEntity, def, workbenchLevel);
                        if (attackEntity is ThrownWeapon) AddDefinitionFlag(def, ItemDefinitionFlags.ThrownWeapon);
                    }
                }

                if (def.TryGetComponent<ItemModProjectile>(out var imp))
                {
                    _itemModProjectile[def] = imp;
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModProjectile);
                    if (workbenchLevel > MaxConsideredWorkbenchLevel) MaxConsideredWorkbenchLevel = workbenchLevel;
                    bool isCatapultAmmo = imp.IsAmmo(AmmoTypes.CATAPULT_BOULDER);
                    CacheProjectileWorkbenchLevel(imp.projectileObject, def, workbenchLevel, isCatapultAmmo);
                    for (int i = 0; i < imp.SkinOverrides.Length; i++)
                        CacheProjectileWorkbenchLevel(imp.SkinOverrides[i].OverrideProjectile, def, workbenchLevel, isCatapultAmmo);
                }

                if (def.TryGetComponent<ItemModCatapultBoulder>(out var boulder))
                {
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModCatapultBoulder);
                    if (workbenchLevel > MaxConsideredWorkbenchLevel) MaxConsideredWorkbenchLevel = workbenchLevel;
                    foreach (var projectileSettings in boulder.projectileSettings)
                        CacheProjectileWorkbenchLevel(projectileSettings.prefab, def, workbenchLevel, true);
                }

                if (def.TryGetComponent<ItemModCookable>(out _))
                    AddDefinitionFlag(def, ItemDefinitionFlags.ItemModCookable);

                if ((def.category == ItemCategory.Food || def.category == ItemCategory.Medical) && def.TryGetComponent<ItemModConsume>(out var con))
                    _itemModConsume[def] = con;

                if (++count >= yieldEvery)
                {
                    count = 0;
                    yield return CoroutineEx.waitForSeconds(0.02f);
                }
            }
        }

        private void CacheWeaponEntity(BaseEntity entity, ItemDefinition def, int workbenchLevel)
        {
            string shortPrefabName = entity.ShortPrefabName;

            if (!AttackEntityToWorkbenchLevel.TryGetValue(shortPrefabName, out int existingLevel) || workbenchLevel > existingLevel)
            {
                AttackEntityToWorkbenchLevel[shortPrefabName] = workbenchLevel;
            }

            if (!WeaponEntityToShortnames.TryGetValue(shortPrefabName, out var shortnames))
            {
                WeaponEntityToShortnames[shortPrefabName] = shortnames = new(StringComparer.OrdinalIgnoreCase);
            }

            shortnames.Add(def.shortname);

            if (workbenchLevel > MaxConsideredWorkbenchLevel)
            {
                MaxConsideredWorkbenchLevel = workbenchLevel;
            }
        }

        private void CacheProjectileWorkbenchLevel(GameObjectRef projectileRef, ItemDefinition def, int workbenchLevel, bool isCatapultAmmo)
        {
            GameObject go = projectileRef.Get();
            if (go == null)
            {
                return;
            }

            if (go.TryGetComponent(out Projectile projectile))
            {
                // Shared projectile prefabs:
                // pistolbullet: ammo.pistol (1), ammo.pistol.hv (2)
                // riflebullet: ammo.rifle (2), ammo.rifle.hv (3)
                // shotgunbullet: ammo.shotgun (2), ammo.grenadelauncher.buckshot (3)
                // Cache the highest level so the fallback check cannot allow higher-tier ammo.
                if (!ProjectileToWorkbenchLevel.TryGetValue(projectile, out int existingLevel) || workbenchLevel > existingLevel)
                {
                    ProjectileToWorkbenchLevel[projectile] = workbenchLevel;
                }

                if (!ProjectileToShortnames.TryGetValue(projectile, out var projectileShortnames))
                {
                    ProjectileToShortnames[projectile] = projectileShortnames = new(StringComparer.OrdinalIgnoreCase);
                }

                projectileShortnames.Add(def.shortname);
            }

            if (go.TryGetComponent(out BaseEntity projectileEntity))
            {
                string shortPrefabName = projectileEntity.ShortPrefabName;

                if (!ProjectileEntityToWorkbenchLevel.TryGetValue(shortPrefabName, out int existingLevel) || workbenchLevel > existingLevel)
                {
                    ProjectileEntityToWorkbenchLevel[shortPrefabName] = workbenchLevel;
                }

                if (!ProjectileEntityToShortnames.TryGetValue(shortPrefabName, out var entityShortnames))
                {
                    ProjectileEntityToShortnames[shortPrefabName] = entityShortnames = new(StringComparer.OrdinalIgnoreCase);
                }

                entityShortnames.Add(def.shortname);

                if (isCatapultAmmo)
                {
                    CatapultProjectilePrefabs.Add(shortPrefabName);
                }
            }
        }

        public static void AdminCommand(BasePlayer player, Action action)
        {
            if (!player.IsAdmin && !player.IsDeveloper && player.IsFlying)
            {
                return; // BasePlayer => FinalizeTick => NoteAdminHack => Ban => Cheat Detected!
            }

            bool isAdmin = player.IsAdmin;

            if (!isAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }
            try
            {
                action();
            }
            finally
            {
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        private PooledHashSet<ulong> GetMembers(BasePlayer player, out bool delay) =>
            GetMembers(player.userID, GetClan(player), out delay);

        private static IClan GetClan(BasePlayer player)
        {
            if (player.clanId == 0)
            {
                return null;
            }
            if (player.serverClan == null)
            {
                ClanManager.ServerInstance?.Backend?.TryGet(player.clanId, out player.serverClan);
            }
            return player.serverClan;
        }

        private static bool TryGetClan(BasePlayer player, out IClan clan)
        {
            clan = GetClan(player);
            return clan != null;
        }

        private PooledHashSet<ulong> GetMembers(ulong userid, IClan clan, out bool delay)
        {
            delay = false;
            PooledHashSet<ulong> members = DisposableHashSet<ulong>();
            members.Add(userid);

            if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(userid, out var team))
            {
                members.UnionWith(team.members);
            }

            if (Clans?.Call("GetClanMembers", userid) is List<string> obj && !obj.IsNullOrEmpty())
            {
                obj.ForEach(member => members.Add(Convert.ToUInt64(member)));
            }

            if (clan != null)
            {
                foreach (var member in clan.Members)
                {
                    members.Add(member.SteamId);
                }
                members.Add(clan.Creator);
            }

            if (members.Count == 1 || config.Settings.Management.Lockout.AllyExploit)
            {
                delay = AddBagMemorialMembers(members, userid);
            }

            return members;
        }

        private bool AddBagMemorialMembers(HashSet<ulong> members, ulong userid)
        {
            int count = members.Count;

            if (SleepingBag.bagsPerPlayer.TryGetValue(userid, out var bags))
            {
                AddBagOwners(members, bags, userid);
            }

            if (bagMemorialService.TryGetValue(userid, out var gotchabitch))
            {
                members.UnionWith(gotchabitch);
            }

            return members.Count > count;
        }

        private void AddBagOwners(HashSet<ulong> members, IEnumerable<SleepingBag> bags, ulong userid)
        {
            foreach (var bag in bags)
            {
                if (bag == null || bag.IsDestroyed) continue;
                var priv = bag.GetBuildingPrivilege(true);
                var building = bag.GetBuilding() ?? priv?.GetBuilding();
                if (priv != null)
                {
                    if (!HasBuildingAccess(priv, building, userid)) continue;
                    if (priv.OwnerID.IsSteamId()) members.Add(priv.OwnerID);
                    members.UnionWith(priv.authorizedPlayers);
                }
                if (building != null && building.HasDecayEntities())
                {
                    foreach (var ent in building.decayEntities)
                    {
                        if (ent == null || !ent.OwnerID.IsSteamId()) continue;
                        members.Add(ent.OwnerID);
                    }
                }
            }
        }

        private bool HasBuildingAccess(BuildingPrivlidge priv, BuildingManager.Building building, ulong userid)
        {
            if (priv.OwnerID == userid || priv.IsAuthed(userid)) return true;
            if (building == null || building.decayEntities == null) return false;
            foreach (var ent in building.decayEntities)
            {
                if (ent == null || !ent.GetSlot(BaseEntity.Slot.Lock).Is(out BaseLock baseLock)) continue;
                if (ent.OwnerID == userid) return true;
                if (!baseLock.Is(out CodeLock codeLock)) continue;
                if (codeLock.whitelistPlayers.Contains(userid)) return true;
                if (codeLock.guestPlayers.Contains(userid)) return true;
            }
            return false;
        }

        private void OnEntityKill(SleepingBag bag)
        {
            if (bag == null || bag.IsDestroyed || !bag.enableSaving || !bag.deployerUserID.IsSteamId()) return;
            if (!bagMemorialService.TryGetValue(bag.deployerUserID, out var members))
            {
                bagMemorialService[bag.deployerUserID] = members = new();
            }
            using var bags = DisposableList<SleepingBag>();
            bags.Add(bag);
            AddBagOwners(members, bags, bag.deployerUserID);
        }

        private Dictionary<ulong, HashSet<ulong>> bagMemorialService = new();
        private uint heli_napalm = 184893264;
        private uint oilfireballsmall = 3550347674;
        private uint rocket_heli = 129320027;
        private uint rocket_heli_napalm = 200672762;

        private void BuildPrefabIds()
        {
            heli_napalm = StringPool.Get("assets/bundled/prefabs/napalm.prefab");
            oilfireballsmall = StringPool.Get("assets/bundled/prefabs/oilfireballsmall.prefab");
            rocket_heli = StringPool.Get("assets/prefabs/npc/patrol helicopter/rocket_heli.prefab");
            rocket_heli_napalm = StringPool.Get("assets/prefabs/npc/patrol helicopter/rocket_heli_napalm.prefab");
        }

        private bool IsHelicopter(HitInfo info, out bool eventHeli)
        {
            eventHeli = false;
            if (info.Initiator != null)
            {
                if (info.Initiator is PatrolHelicopter heli)
                {
                    eventHeli = heli._name != null && !heli._name.Contains("patrolhelicopter");
                    return true;
                }
                if (info.Initiator.prefabID == oilfireballsmall || info.Initiator.prefabID == heli_napalm)
                {
                    return true;
                }
            }
            return info.WeaponPrefab?.prefabID == rocket_heli || info.WeaponPrefab?.prefabID == rocket_heli_napalm;
        }

        public bool IsPasteEngineReady(out string error)
        {
            error = "Raidable Bases could not initialize its internal paste engine.";
            return _pasteEngine != null;
        }

        private bool PlayerInEvent(BasePlayer player)
        {
            return !player.IsKilled() && (HasPVPDelay(player.userID) || EventTerritory(player.transform.position));
        }

        private bool PlayerInEventPVE(BasePlayer player)
        {
            return !player.IsKilled() && !HasPVPDelay(player.userID) && Get(player.transform.position, out var raid) && !raid.AllowPVP;
        }

        private bool PlayerInEventPVP(BasePlayer player)
        {
            return !player.IsKilled() && (HasPVPDelay(player.userID) || Get(player.transform.position, out var raid) && raid.AllowPVP);
        }

        private double GetPVPDelay(ulong userid)
        {
            return userid.IsSteamId() && GetPVPDelay(userid, true, out DelaySettings ds) ? ds.time : 0d;
        }

        private bool GetPVPDelay(ulong userid, bool check, out DelaySettings ds)
        {
            if (!PvpDelay.TryGetValue(userid, out ds))
            {
                return false;
            }
            if (check)
            {
                return ds != null && ds.time > Time.timeAsDouble;
            }
            return ds != null;
        }

        private double GetMaxPVPDelay()
        {
            return config.Settings.Management.PVPDelay;
        }

        [HookMethod("HasPVPDelay")]
        public bool HasPVPDelay(ulong userid)
        {
            return GetPVPDelay(userid) > 0d;
        }

        private void RemovePVPDelay(ulong userid, in DelaySettings ds)
        {
            if (ds != null && ds.Timer != null)
            {
                ds.Timer.Destroy();
            }
            PvpDelay.Remove(userid);
            UnsubscribeDamageHook();
        }

        private void ExpirePVPDelay(ulong userid, DelaySettings ds)
        {
            if (!PvpDelay.TryGetValue(userid, out DelaySettings current) || current != ds)
            {
                return;
            }

            RaidableBase raid = ds.raid;
            BasePlayer target = RelationshipManager.FindByID(userid);

            RemovePVPDelay(userid, ds);

            HarmonyModInterface.CallHook("OnPlayerPvpDelayExpired", raid == null ? ds.hookObjectsFallback : raid.GetDelayHookObjects(target, userid));
        }

        private bool IsBox(BaseEntity entity, bool inherit) =>
            entity is DisplayingBoxStorage || IsBox(entity.ShortPrefabName, inherit);

        private bool IsBox(string prefab, bool inherit)
        {
            switch (prefab)
            {
                case "krieg_storage_vertical":
                case "krieg_storage_horizontal":
                case "abyss_barrel_horizontal":
                case "abyss_barrel_vertical":
                case "medieval.box.wooden.large":
                case "box.wooden.large":
                case "woodbox_deployed":
                case "coffinstorage":
                case "unused_storage_barrel_a":
                case "storage_barrel_b":
                case "storage_barrel_c":
                case "wicker_barrel":
                case "bamboo_barrel":
                case "industrial_storage_horizontal":
                case "industrial_storage_vertical":
                    return true;
                default:
                    if (inherit)
                    {
                        foreach (var sub in config.Settings.Management.Inherit)
                        {
                            if (prefab.Contains(sub)) return true;
                        }
                    }
                    return prefab.StartsWith("component.box.");
            }
        }

        public float GetDistance(RaidableType type)
        {
            return type switch
            {
                RaidableType.Maintained => Mathf.Clamp(config.Settings.Maintained.Distance, CELL_SIZE, 9000f),
                RaidableType.Purchased => Mathf.Clamp(config.Settings.Buyable.Distance, CELL_SIZE, 9000f),
                RaidableType.Scheduled => Mathf.Clamp(config.Settings.Schedule.Distance, CELL_SIZE, 9000f),
                RaidableType.None => Mathf.Max(config.Settings.Maintained.Distance, config.Settings.Buyable.Distance, config.Settings.Schedule.Distance),
                _ => 100f
            };
        }

        private bool IsPVE() => TruePVE != null || SimplePVE != null || NextGenPVE != null || Imperium != null || AegisPVE != null;

        [HookMethod("IsPremium")]
        public bool IsPremium() => true;

        private void UpdateUI()
        {
            if (config.UI.Lockout.Enabled || config.UI.BuyableCooldowns.Enabled)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (config.UI.Lockout.Enabled) UI.UpdateUi(player, UiType.Lockout);
                    if (config.UI.BuyableCooldowns.Enabled) UI.UpdateUi(player, UiType.Cooldown);
                }
            }
        }

        private static bool NullifyDamage(HitInfo info)
        {
            if (info != null && info.damageTypes != null)
            {
                info.damageTypes.Clear();
                info.DidHit = false;
                info.DoHitEffects = false;
            }
            return false;
        }

        public bool MustExclude(RaidableType type, bool allowPVP)
        {
            if (!config.Settings.Maintained.IncludePVE && type == RaidableType.Maintained && !allowPVP)
            {
                return true;
            }

            if (!config.Settings.Maintained.IncludePVP && type == RaidableType.Maintained && allowPVP)
            {
                return true;
            }

            if (!config.Settings.Schedule.IncludePVE && type == RaidableType.Scheduled && !allowPVP)
            {
                return true;
            }

            if (!config.Settings.Schedule.IncludePVP && type == RaidableType.Scheduled && allowPVP)
            {
                return true;
            }

            return false;
        }

        private bool AnyNpcs()
        {
            foreach (var raid in Raids)
            {
                if (raid != null && !raid.IsDespawning && raid.ExtendHookSubscription) return true;
            }
            foreach (var brain in HumanoidBrains.Values)
            {
                if (brain?.npc != null && !brain.npc.IsKilled()) return true;
            }
            return false;
        }

        private string[] GetProfileFiles()
        {
            try
            {
                return HarmonyDataLayer.GetFiles(Path.Combine(Name, "Profiles"));
            }
            catch (UnauthorizedAccessException ex)
            {
                Puts(ex);
                profileErrors.Add("Unauthorized");
            }

            return Array.Empty<string>();
        }

        private string[] GetCopyPasteFiles()
        {
            try
            {
                return HarmonyDataLayer.GetFiles("copypaste", "*.json");
            }
            catch (UnauthorizedAccessException ex)
            {
                Puts(ex);
                profileErrors.Add("Unauthorized");
            }

            return Array.Empty<string>();
        }

        private bool CheckAutoCorrect(IPlayer user, string file, ref string value)
        {
            string other = GetFileNameWithoutExtension(file);
            if (other == value) return true;
            if (!other.Equals(value, StringComparison.OrdinalIgnoreCase)) return false;
            Reply(user, $"Auto-corrected spelling of '{value}' to '{other}'");
            value = other;
            return true;
        }

        private void ConfigAddBase(IPlayer user, string[] args)
        {
            if (args.Length < 2)
            {
                ReplyOrLog(user, "Syntax: rb.config add \"profile name\" [difficulty] [file name or wildcard] ...\nWildcards: * matches any number of characters and ? matches one character.");
                return;
            }

            static bool IsWildcard(string value) => value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;

            static bool WildcardMatches(string value, string pattern)
            {
                int valueIndex = 0;
                int patternIndex = 0;
                int wildcardIndex = -1;
                int wildcardValueIndex = -1;

                while (valueIndex < value.Length)
                {
                    if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
                    {
                        patternIndex++;
                        valueIndex++;
                    }
                    else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                    {
                        wildcardIndex = patternIndex++;
                        wildcardValueIndex = valueIndex;
                    }
                    else if (wildcardIndex >= 0)
                    {
                        patternIndex = wildcardIndex + 1;
                        valueIndex = ++wildcardValueIndex;
                    }
                    else
                    {
                        return false;
                    }
                }

                while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                {
                    patternIndex++;
                }

                return patternIndex == pattern.Length;
            }

            string profileName = args[1];

            foreach (string existingProfileName in Buildings.Profiles.Keys)
            {
                if (existingProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                {
                    profileName = existingProfileName;
                    break;
                }
            }

            string mode = RaidableMode.Random;
            using var selectors = DisposableList<string>();

            for (int i = 2; i < args.Length; i++)
            {
                string selectedMode = GetRaidableModes().Find(x => x.Equals(args[i], StringComparison.OrdinalIgnoreCase)) ?? RaidableMode.Random;

                if (mode == RaidableMode.Random && IsModeValid(selectedMode))
                {
                    mode = selectedMode;
                }
                else
                {
                    selectors.Add(args[i]);
                }
            }

            bool createdProfile = false;

            if (!Buildings.Profiles.TryGetValue(profileName, out var profile))
            {
                Buildings.Profiles[profileName] = profile = new(this);
                profile.ProfileName = profileName;
                createdProfile = true;
            }

            if (createdProfile && IsModeValid(mode))
            {
                profile.Options.Mode = mode;
            }

            string[] copyPastePaths = GetCopyPasteFiles();
            using var copyPasteNames = DisposableList<string>();

            foreach (string path in copyPastePaths)
            {
                string fileName = GetFileNameWithoutExtension(path);

                if (!copyPasteNames.Exists(x => x.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    copyPasteNames.Add(fileName);
                }
            }

            copyPasteNames.Sort(StringComparer.OrdinalIgnoreCase);

            using var requestedFiles = DisposableList<string>();
            using var unmatchedWildcards = DisposableList<string>();

            foreach (string selector in selectors)
            {
                if (IsWildcard(selector))
                {
                    int matchCount = 0;

                    foreach (string fileName in copyPasteNames)
                    {
                        if (WildcardMatches(fileName, selector))
                        {
                            matchCount++;

                            if (!requestedFiles.Exists(x => x.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                            {
                                requestedFiles.Add(fileName);
                            }
                        }
                    }

                    if (matchCount == 0)
                    {
                        unmatchedWildcards.Add(selector);
                    }

                    continue;
                }

                string file = copyPasteNames.Find(x => x.Equals(selector, StringComparison.OrdinalIgnoreCase)) ?? selector;

                if (!file.Equals(selector, StringComparison.Ordinal))
                {
                    ReplyOrLog(user, $"Auto-corrected spelling of '{selector}' to '{file}'");
                }

                if (!requestedFiles.Exists(x => x.Equals(file, StringComparison.OrdinalIgnoreCase)))
                {
                    requestedFiles.Add(file);
                }
            }

            using var addedFiles = DisposableList<string>();
            using var existingFiles = DisposableList<string>();

            foreach (string file in requestedFiles)
            {
                if (file.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (profile.Options.AdditionalBases.ContainsKey(file))
                {
                    existingFiles.Add(file);
                    continue;
                }

                profile.Options.AdditionalBases.Add(file, DefaultBaseOptions());
                addedFiles.Add(file);
            }

            if (createdProfile || addedFiles.Count > 0)
            {
                profile.Options.Enabled = true;
                SaveProfile(profileName, profile.Options);
                Buildings.Profiles[profileName] = profile;
            }

            using var sb = DisposableBuilder.Get();
            sb.AppendLine($"Profile: {profileName}");

            if (createdProfile)
            {
                sb.AppendLine("  Created profile.");

                if (IsModeValid(mode))
                {
                    sb.AppendLine($"  Difficulty: {mode}");
                }
                else
                {
                    sb.AppendLine("  Difficulty is not set.");
                }
            }

            if (addedFiles.Count > 0)
            {
                sb.AppendLine($"  Added {addedFiles.Count} additional base file{(addedFiles.Count == 1 ? string.Empty : "s")}:");

                foreach (string file in addedFiles)
                {
                    sb.AppendLine($"    {file}{(FileExists(file) ? string.Empty : " (file does not currently exist)")}");
                }
            }

            if (existingFiles.Count > 0)
            {
                sb.AppendLine($"  Already configured ({existingFiles.Count}):");

                foreach (string file in existingFiles)
                {
                    sb.AppendLine($"    {file}");
                }
            }

            if (unmatchedWildcards.Count > 0)
            {
                sb.AppendLine($"  Wildcards with no matches ({unmatchedWildcards.Count}):");

                foreach (string wildcard in unmatchedWildcards)
                {
                    sb.AppendLine($"    {wildcard}");
                }
            }

            if (!createdProfile && addedFiles.Count == 0 && existingFiles.Count == 0 && unmatchedWildcards.Count == 0)
            {
                sb.AppendLine("  No additional base files were specified.");
            }

            ReplyOrLog(user, sb.ToString());
        }

        private static string GetFileNameWithoutExtension(string file) => Utility.GetFileNameWithoutExtension(file);

        private void ConfigRemoveBase(IPlayer user, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(user, "RemoveSyntax");
                return;
            }

            int num = 0;
            var profiles = Buildings.Profiles.ToDictionary(k => k.Key, k => k.Value);
            var files = (string.Join(" ", args[0].Equals("remove", StringComparison.CurrentCultureIgnoreCase) ? args.Skip(1) : args)).Replace(", ", " ");
            var split = files.Split(' ');

            using var _sb = DisposableBuilder.Get();
            _sb.AppendLine(mx("RemovingFrom", user.Id, string.Join(" ", files)));

            foreach (var (key, profile) in profiles)
            {
                using var tmp = profile.Options.AdditionalBases.Keys.ToPooledList();

                foreach (var extra in tmp)
                {
                    if (args.Contains("*") && key == args[1] || split.Contains(extra))
                    {
                        _sb.AppendLine(mx("RemovedFrom", user.Id, extra, key));
                        if (profile.Options.AdditionalBases.Remove(extra)) num++;
                        SaveProfile(key, profile.Options);
                    }
                }

                if (split.Contains(key))
                {
                    _sb.AppendLine(mx("RemovedPrimary", user.Id, key));
                    if (Buildings.Profiles.Remove(key)) num++;
                    profile.Options.Enabled = false;
                    SaveProfile(key, profile.Options);
                }
            }

            _sb.AppendLine(mx("RemovedEntries", user.Id, num));
            Reply(user, _sb.ToString());
        }

        private void ConfigSetEnabledWeekday(IPlayer user, string mode, DayOfWeek dayOfWeek, string flag)
        {
            if (!bool.TryParse(flag, out var value))
            {
                Reply(user, $"Invalid flag (true/false): {flag}");
                return;
            }

            Reply(user, $"{mode} is now {(value ? "enabled" : "disabled")} on {dayOfWeek}");

            if (!config.Settings.Management.Dictionary.TryGetValue(en ? $"{mode} Raids Can Spawn On" : $"Дни спавна {mode} рейд-баз", out var ds))
            {
                Reply(user, $"Unable to find mode '{mode}'");
                return;
            }

            if (ds != null)
            {
                switch (dayOfWeek)
                {
                    case DayOfWeek.Monday: ds.Monday = value; break;
                    case DayOfWeek.Tuesday: ds.Tuesday = value; break;
                    case DayOfWeek.Wednesday: ds.Wednesday = value; break;
                    case DayOfWeek.Thursday: ds.Thursday = value; break;
                    case DayOfWeek.Friday: ds.Friday = value; break;
                    case DayOfWeek.Saturday: ds.Saturday = value; break;
                    case DayOfWeek.Sunday: ds.Sunday = value; break;
                }
            }

            if (_saveConfigTimer != null) _saveConfigTimer.Reset();
            else _saveConfigTimer = timer.Once(1f, SaveConfig);
        }

        private Timer _saveConfigTimer, _saveConfigTimer2;

        private void ConfigSetDifficultyLimit(IPlayer user, string mode, int amount, string type)
        {
            if (type.Contains("automated")) config.Settings.Management.Amounts.Set(mode, amount);
            else if (type.Contains("buyable")) config.Settings.Buyable.Limits.Set(mode, amount);
            else return;

            Reply(user, $"{mode} is now limited to {amount} {type} event(s)");

            if (_saveConfigTimer2 != null) _saveConfigTimer2.Reset();
            else _saveConfigTimer2 = timer.Once(1f, SaveConfig);
        }

        private void ConfigListBases(IPlayer user)
        {
            if (Buildings.Profiles.Count == 0)
            {
                if (IsGridLoading())
                {
                    Reply(user, "GridIsLoading");
                }

                ReplyOrLog(user, "No profiles are loaded.");
                return;
            }

            using var sb = DisposableBuilder.Get();
            bool anyPVE = false;
            int existingFileCount = 0;
            int missingFileCount = 0;

            sb.AppendLine("Configured CopyPaste files:");

            foreach (var (profileName, profile) in Buildings.Profiles)
            {
                using var existingFiles = DisposableList<string>();
                using var missingFiles = DisposableList<string>();
                bool profileFileExists = FileExists(profileName);

                if (!profile.Options.AllowPVP)
                {
                    anyPVE = true;
                }

                if (profileFileExists)
                {
                    existingFiles.Add(profileName);
                }

                foreach (string file in profile.Options.AdditionalBases.Keys)
                {
                    if (FileExists(file))
                    {
                        existingFiles.Add(file);
                    }
                    else
                    {
                        missingFiles.Add(file);
                    }
                }

                existingFileCount += existingFiles.Count;
                missingFileCount += missingFiles.Count;

                var found = existingFiles.Count == 0 ? "(none)" : string.Join(", ", existingFiles);
                var missing = missingFiles.Count == 0 ? "(none)" : string.Join(", ", missingFiles);
                var mode = profile.Options.Mode.ToLowerInvariant();
                var type = profile.Options.AllowPVP ? "PVP" : "PVE";
                var state = profile.Options.Enabled ? "enabled" : "disabled";

                sb.AppendLine();
                sb.AppendLine($"Profile: {profileName} ({mode}, {type}, {state})");
                sb.AppendLine($" Found ({existingFiles.Count}): {found}");
                sb.AppendLine($" Missing ({missingFiles.Count}): {missing}");
            }

            sb.AppendLine();
            sb.AppendLine($"Summary: {Buildings.Profiles.Count} profile{(Buildings.Profiles.Count == 1 ? string.Empty : "s")}, {existingFileCount} existing file{(existingFileCount == 1 ? string.Empty : "s")}, {missingFileCount} missing file{(missingFileCount == 1 ? string.Empty : "s")}.");

            if (!anyPVE && !AllowBuyingPVP)
            {
                sb.AppendLine(mx("NoBuyableEventsPVP", user.Id));
            }

            if (existingFileCount == 0)
            {
                sb.AppendLine(mx("NoBuildingsConfigured", user.Id));
            }

            ReplyOrLog(user, sb.ToString());

            if (!IsPasteEngineReady(out var error))
            {
                ReplyOrLog(user, error);
            }
        }

        private bool TryRemoveItems(BaseEntity entity)
        {
            if (entity is IItemContainerEntity ice && ice != null && ice.inventory != null)
            {
                bool clearInventory = entity.OwnerID == 0 && entity switch
                {
                    FlameTurret or FogMachine or GunTrap when !config.Settings.Management.DropLoot.Get(entity) => true,
                    BuildingPrivlidge when !config.Settings.Management.AllowCupboardLoot => true,
                    _ => false
                };
                if (clearInventory)
                {
                    RaidableBase.ClearInventory(ice.inventory);
                    return true;
                }
            }
            return false;
        }

        private void DropOrRemoveItems(StorageContainer container, RaidableBase raid, bool forced, bool kill)
        {
            if (!container.inventory.IsEmpty() && (forced || !TryRemoveItems(container)))
            {
                var drop = DropLoot(container, container.inventory, container is BuildingPrivlidge ? raid.Options.BuoyantPrivilege : raid.Options.BuoyantBox);
                if (drop != null && container.OwnerID == 0uL)
                {
                    drop.buryLeftoverItems = false;
                    if (container switch
                    {
                        GunTrap or FlameTurret => config.Settings.Management.DropLoot.CanDespawnGreyWeaponBag(container),
                        _ => raid.Options.DespawnGreyBoxBags
                    })
                    {
                        raid.SetupEntity(drop);
                    }
                    else raid.DespawnExceptions.Add(drop);
                }
            }

            ItemManager.DoRemoves();

            if (kill && (container is BuildingPrivlidge || IsBox(container, false)))
            {
                container.Invoke(container.SafelyKill, 0.1f);
            }
        }

        private Dictionary<ulong, (string, string)> despawnCooldowns = new();

        protected bool DespawnBase(BasePlayer player, bool isAllowed)
        {
            var raid = isAllowed ? GetNearestBase(player.transform.position) : GetPurchasedBase(player);
            var bypass = isAllowed || player.HasPermission("raidablebases.canbypass");

            if (!bypass && despawnCooldowns.ContainsKey(player.userID))
            {
                SendNotification(player, "CommandNotAllowed");
                return false;
            }

            if (raid == null || raid.IsLoading)
            {
                SendNotification(player, isAllowed ? "DespawnBaseNoneAvailable" : "DespawnBaseNoneOwned");
                return false;
            }

            if (!raid.CanBypass(player) && raid.IsDamaged && config.Settings.Buyable.Refunds.Despawn)
            {
                SendNotification(player, "DespawnBaseDamaged");
                return false;
            }

            if (!raid.CanBypass(player) && raid.IsAnyLooted && config.Settings.Buyable.Refunds.AnyLooted)
            {
                SendNotification(player, "DespawnBaseLooted");
                return false;
            }

            if (raid.IsPayLocked)
            {
                if (raid.payments.owner != null) raid.Refund(raid.payments.owner);
                else if (raid.GetOwner() is BasePlayer owner) raid.Refund(owner);
                else raid.Refund(player);
                raid.IsEligible = !config.Settings.Buyable.Refunds.Ineligible;
            }

            if (raid.AddNearTime <= 0f)
            {
                raid.AddNearTime = 15f;
            }

            string baseName = raid.BaseName;
            string mode = raid.Options.Mode;

            Puts(mx("DespawnedAt", null, player.displayName, $"{PositionToGrid(player.transform.position)} [{baseName}]"));

            raid.Despawn();

            SendNotification(player, "DespawnBaseSuccess");

            if (!bypass && config.Settings.Buyable.Refunds.Cooldown > 0)
            {
                ulong userid = player.userID;
                despawnCooldowns[userid] = (baseName, mode);
                timer.Once(config.Settings.Buyable.Refunds.Cooldown, () => despawnCooldowns.Remove(userid));
            }

            return true;
        }

        private RaidableBase GetPurchasedBase(BasePlayer player)
        {
            for (int i = 0; i < Raids.Count; i++)
            {
                RaidableBase raid = Raids[i];
                if (raid.IsPayLocked && raid.ownerId == player.userID)
                {
                    return raid;
                }
            }

            return null;
        }

        private RaidableBase GetNearestBase(Vector3 target, float radius = 100f)
        {
            float radiusSqr = radius * radius;
            float nearestSqr = float.MaxValue;
            Vector2 targetXZ = target.XZ2D();
            RaidableBase nearest = null;

            for (int i = 0; i < Raids.Count; i++)
            {
                RaidableBase raid = Raids[i];
                float sqrDistance = (raid.Location.XZ2D() - targetXZ).sqrMagnitude;

                if (sqrDistance <= radiusSqr && sqrDistance < nearestSqr)
                {
                    nearestSqr = sqrDistance;
                    nearest = raid;
                }
            }

            return nearest;
        }

        private bool IsTrueDamage(BaseEntity entity, bool isProtectedWeapon)
        {
            if (entity.IsNull())
            {
                return false;
            }

            if (isProtectedWeapon || entity.skinID == 1587601905 || (entity is TeslaCoil or BaseTrap))
            {
                return true;
            }

            return TrueDamage.Contains(entity.ShortPrefabName);
        }

        private Vector3 GetCenterLocation(Vector3 position)
        {
            for (int i = 0; i < Raids.Count; i++)
            {
                if (InRange2D(Raids[i].Location, position, Raids[i].ProtectionRadius))
                {
                    return Raids[i].Location;
                }
            }

            return Vector3.zero;
        }

        private bool HasEventEntity(BaseEntity entity)
        {
            if (entity == null || entity.net == null || entity.IsDestroyed)
            {
                return false;
            }
            if (entity.skinID == RB_SKIN_ID || entity is HumanoidNPC)
            {
                return true;
            }
            return Has(entity);
        }


        [HookMethod("GetAllEventsCount")]
        public int GetAllEventsCount() => Raids.Count;

        [HookMethod("GetActiveEventCount")]
        public int GetActiveEventCount() => Raids.Sum(raid => raid.GetPercentComplete() > 0 ? 1 : 0);

        [HookMethod("GetAllEvents")]
        public List<(Vector3, string, int, bool, string, float, float, float, ulong, BasePlayer, List<BasePlayer>, List<BasePlayer>, HashSet<BaseEntity>, string, DateTime, DateTime, float, int)> GetAllEvents()
        {
            List<(Vector3, string, int, bool, string, float, float, float, ulong, BasePlayer, List<BasePlayer>, List<BasePlayer>, HashSet<BaseEntity>, string, DateTime, DateTime, float, int)> results = new(Raids.Count);

            GetAllEventsNonAlloc(results);

            return results;
        }

        [HookMethod("GetAllEventsNonAlloc")]
        public void GetAllEventsNonAlloc(List<(Vector3, string, int, bool, string, float, float, float, ulong, BasePlayer, List<BasePlayer>, List<BasePlayer>, HashSet<BaseEntity>, string, DateTime, DateTime, float, int)> m)
        {
            if (m == null)
            {
                return;
            }

            m.Clear();

            if (m.Capacity < Raids.Count)
            {
                m.Capacity = Raids.Count;
            }

            for (int i = 0; i < Raids.Count; i++)
            {
                RaidableBase r = Raids[i];

                m.Add((r.Location, r.Options.Mode, r.Options.Level, r.AllowPVP, r.ID, 0f, 0f, 0f, r.ownerId, r.GetOwner(), r.GetRaiders(), r.GetIntruders(), r.Entities, r.BaseName, r.spawnDateTime, r.despawnDateTime, r.ProtectionRadius, r.GetLootAmountCounted()));
            }
        }

        [HookMethod("GetAllDifficulties")]
        public List<(string mode, int level)> GetAllDifficulties()
        {
            return new(Buildings.Profiles.Select(x => (x.Value.Options.Mode, x.Value.Options.Level)));
        }

        [HookMethod("EventTerritory")]
        public bool EventTerritory(Vector3 position, float x = 0f)
        {
            for (int i = 0; i < Raids.Count; i++)
            {
                RaidableBase raid = Raids[i];
                if (InRange(raid.Location, position, raid.ProtectionRadius + x))
                {
                    return true;
                }
            }
            return false;
        }

        [HookMethod("EventTerritoryAny")]
        public bool EventTerritoryAny(Vector3[] positions, float x = 0f)
        {
            for (int j = 0; j < Raids.Count; j++)
            {
                for (int k = 0; k < positions.Length; k++)
                {
                    RaidableBase raid = Raids[j];
                    if (InRange(raid.Location, positions[k], raid.ProtectionRadius + x))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        [HookMethod("EventTerritoryAll")]
        public bool EventTerritoryAll(Vector3[] positions, float x = 0f)
        {
            for (int k = 0; k < positions.Length; k++)
            {
                bool isEventTerritory = false;
                for (int j = 0; j < Raids.Count; j++)
                {
                    RaidableBase raid = Raids[j];
                    if (InRange(raid.Location, positions[k], raid.ProtectionRadius + x))
                    {
                        isEventTerritory = true;
                        break;
                    }
                }
                if (!isEventTerritory)
                {
                    return false;
                }
            }
            return true;
        }

        [HookMethod("GetPlayersFrom")]
        public List<BasePlayer> GetPlayersFrom(Vector3 position, float x = 0f, bool intruders = false)
        {
            for (int i = 0; i < Raids.Count; i++)
            {
                if (InRange2D(Raids[i].Location, position, Raids[i].ProtectionRadius + x))
                {
                    return intruders ? Raids[i].GetIntruders() : Raids[i].GetRaiders();
                }
            }
            return null;
        }

        [HookMethod("GetOwnerFrom")]
        public BasePlayer GetOwnerFrom(Vector3 position, float x = 0f)
        {
            for (int i = 0; i < Raids.Count; i++)
            {
                if (InRange2D(Raids[i].Location, position, Raids[i].ProtectionRadius + x))
                {
                    return Raids[i].GetOwner();
                }
            }
            return null;
        }

        private string SetUiParent(string value, int type)
        {
            return type switch
            {
                0 => UI.BUYABLE_PARENT = value,
                1 => UI.COOLDOWN_PARENT = value,
                2 => UI.DELAY_PARENT = value,
                3 => UI.LOCKOUT_PARENT = value,
                4 => UI.STATUS_PARENT = value,
                5 => UI.ELEVATOR_PARENT = value,
                _ => UI.TELEPORT_PARENT = value
            };
        }

        private Dictionary<string, Dictionary<string, int[]>> GetPlayerAmounts()
        {
            var result = new Dictionary<string, Dictionary<string, int[]>>();

            foreach (var (userid, info) in data.Players)
            {
                var amounts = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

                foreach (var key in info.Modes.Keys)
                {
                    if (!key.StartsWith("Total") && !key.EndsWith("Points"))
                    {
                        int currentCount = info.Modes.GetValueOrDefault(key);
                        int totalCount = info.Modes.GetValueOrDefault("Total" + key);
                        amounts[key] = new int[] { currentCount, totalCount };
                    }
                }

                amounts["Points"] = new int[] { info.Points, info.TotalPoints };
                amounts["Raids"] = new int[] { info.Raids, info.TotalRaids };

                result[userid] = amounts;
            }

            return result;
        }

        private int[] GetPlayerAmount(string userid, string mode)
        {
            if (!data.Players.TryGetValue(userid, out var user))
                return Array.Empty<int>();

            int currentCount = user.Modes.GetValueOrDefault(mode);
            int totalCount = user.Modes.GetValueOrDefault("Total" + mode);

            return new int[] { currentCount, totalCount };
        }

        public static bool InRange2D(Vector3 a, Vector3 b, float distance)
        {
            return (a - b).SqrMagnitude2D() <= distance * distance;
        }

        public static bool InRange(Vector3 a, Vector3 b, float distance)
        {
            return (a - b).sqrMagnitude <= distance * distance;
        }

        public static bool InRange(BaseEntity a, Vector3 b, float distance)
        {
            if (a.IsKilled()) return false;
            return (a.transform.position - b).sqrMagnitude <= distance * distance;
        }

        private void RevokePermissionsAndGroups(IEnumerable<string> revokes)
        {
            if (revokes.Exists())
            {
                foreach (var target in covalence.Players.All)
                {
                    if (target == null) continue;
                    foreach (var revoke in revokes)
                    {
                        if (target.HasPermission(revoke))
                        {
                            permission.RevokeUserPermission(target.Id, revoke);
                        }

                        if (permission.UserHasGroup(target.Id, revoke))
                        {
                            permission.RemoveUserGroup(target.Id, revoke);
                        }
                    }
                }
            }
        }

        private bool AssignTreasureHunters()
        {
            var records = config.RankedLadder.GetRecords();

            if (records.Count == 0)
            {
                return true;
            }

            RevokePermissionsAndGroups(records.Select(record => record.Permission).Union(records.Select(record => record.Group)));

            var players = data.Players.Where(x => IsNormalUser(x.Key));

            if (!players.Exists(entry => entry.Value.Any()))
            {
                return false;
            }

            foreach (var target in covalence.Players.All)
            {
                foreach (var record in records)
                {
                    if (target.HasPermission(record.Permission))
                    {
                        permission.RevokeUserPermission(target.Id, record.Permission);
                    }

                    if (permission.UserHasGroup(target.Id, record.Group))
                    {
                        permission.RemoveUserGroup(target.Id, record.Group);
                    }
                }
            }

            if (config.RankedLadder.Enabled && config.RankedLadder.Amount > 0 && players.Count > 0)
            {
                records.ForEach(record => AssignTreasureHunters(record, players));

                Puts(mx("Log Saved", null, "topraider"));
            }

            return true;
        }

        private bool IsNormalUser(string userid)
        {
            return userid.IsSteamId() && !userid.HasPermission("raidablebases.notitle") && covalence.Players.FindPlayerById(userid) is IPlayer user && !user.IsBanned;
        }

        private void AssignTreasureHunters(RankedRecord record, List<KeyValuePair<string, PlayerInfo>> players)
        {
            using var ladder = DisposableList<(string userid, int score)>();

            foreach (var (userid, info) in players)
            {
                int value = record.Mode == RaidableMode.Points ? info.Points : info.Get(record.Mode);
                (string, int) score = value > 0 && (record.Mode == RaidableMode.Points || config.RankedLadder.Assign.Dictionary.TryGetValue(record.Mode, out var val) && val <= 0) ? new(userid, value) : default;

                if (score.Item2 != 0 && !score.Item1.HasPermission("raidablebases.ladder.exclude") && !score.Item1.HasPermission("raidablebases.notitle"))
                {
                    ladder.Add(score);
                }
            }

            if (ladder.Count == 0)
            {
                return;
            }

            ladder.Sort((x, y) => y.score.CompareTo(x.score));

            for (int i = 0; i < ladder.Count && i < config.RankedLadder.Amount; i++)
            {
                var (userid, score) = ladder[i];
                var user = covalence.Players.FindPlayerById(userid);
                var username = user?.Name ?? ConVar.Admin.GetPlayerName(Convert.ToUInt64(userid));

                permission.GrantUserPermission(userid, record.Permission, this);
                permission.AddUserGroup(userid, record.Group);

                LogToFile("topraider", $"{DateTime.Now} : {mx("Log Stolen", null, username, userid, $"{record.Mode}: {score}")}", this, true);
                Puts(mx("Log Granted", null, username, userid, record.Permission, record.Group));
            }
        }

        private bool CanContinueAutomation(RaidableType type) => GetRaidableModes().Exists(x => CanSpawnDifficultyToday(type, x));

        private static bool IsModeValid(string mode) => mode != RaidableMode.Disabled && mode != RaidableMode.Random && mode != RaidableMode.Points;

        public string PositionToGrid(Vector3 v) => PositionToGrid(v, config.Settings.ShowXZ);

        public string PositionToGrid(Vector3 v, bool showxz) => showxz ? $"{MapHelper.PositionToString(v)} ({v.x:N2} {v.z:N2})" : MapHelper.PositionToString(v);

        public string FormatGridReference(BasePlayer player, Vector3 v)
        {
            using var format = DisposableList<string>();

            if (config.Settings.ShowGrid)
            {
                format.Add(MapHelper.PositionToString(v));
            }

            if (config.Settings.ShowDir && !player.IsKilled())
            {
                format.Add(format.Count > 0 ? $"({GetDirection(player, v)})" : $"{GetDirection(player, v)} ({Mathf.CeilToInt(player.Distance(v))}m)");
            }

            if (config.Settings.ShowXZ)
            {
                format.Add(format.Count > 0 ? $"({v.x:N2} {v.z:N2})" : $"{v.x:N2} {v.z:N2}");
            }

            return format.Count > 0 ? string.Join(" ", format) : $"{v}";
        }

        private string GetDirection(BasePlayer player, Vector3 target)
        {
            Vector3 targetDir = (target - player.eyes.position).normalized;
            float yaw = Quaternion.LookRotation(targetDir).eulerAngles.y;

            return yaw switch
            {
                >= 0 and < 45 => "North",
                >= 45 and < 90 => "North East",
                >= 90 and < 135 => "East",
                >= 135 and < 180 => "South East",
                >= 180 and < 225 => "South",
                >= 225 and < 270 => "South West",
                >= 270 and < 315 => "West",
                >= 315 and < 360 or _ => "North West",
            };
        }

        private string FormatTime(double seconds, string id = null)
        {
            if (seconds < 0)
            {
                return "0s";
            }

            var ts = TimeSpan.FromSeconds(seconds);

            return mx("TimeFormat", id, (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }

        #endregion

    }
}
