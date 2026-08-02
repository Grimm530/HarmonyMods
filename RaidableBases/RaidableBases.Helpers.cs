using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Helpers

        private static bool IsDeepSeaOpen()
        {
            DeepSeaManager dsm = PointEntity<DeepSeaManager>.ServerInstance;
            if (dsm == null) return false;
            return dsm.IsBusy() || dsm.IsOpen();
        }

        public static bool IsCustomEntity(BaseEntity m) => m.PrefabName.StartsWith("assets/custom/");
        public static PooledList<T> DisposableList<T>() => Pool.Get<PooledList<T>>();
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
            foreach (var player in BasePlayer.activePlayerList)
            {
                yield return CoroutineEx.waitForSeconds(0.1f);
                foreach (var raid in Raids)
                {
                    if (!raid.IsOpened || raid.IsDespawning || raid.ownerId != 0 || raid.IsPayLocked) continue;
                    if (raid.NotifiedNearby.Contains(player.userID)) continue;
                    var distSqr = (raid.Location - player.transform.position).sqrMagnitude;
                    if (distSqr < raid.ProtectionRadiusSqr(100f)) continue;
                    if (distSqr < config.EventMessages.Nearby * config.EventMessages.Nearby)
                    {
                        raid.NotifiedNearby.Add(player.userID);
                        Message(player, "Near", raid.Options.Mode);
                    }
                }
            }

            timer.Once(30f, CheckPlayersNearEvents);

            checkPlayersNearEventsCo = null;
        }

        private void RegisterPermissions()
        {
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
            permission.RegisterPermission("raidablebases.config", this);
        }

        public void LoadPlayerData()
        {
            try { data = HarmonyDataLayer.ReadObject<StoredData>(Name); } catch (Exception ex) { Puts(ex); }
            data ??= new();
            data.Players ??= new();
            data.BuyableCooldowns ??= new();
            data.Cycle ??= new();
            data.Cycle.Instance = this;
            if (!config.Settings.Management.RequireAllSpawnsPersist)
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
            var entities = new List<BaseEntity>();
            using var tmp = Raids.ToPooledList();
            foreach (var raid in tmp)
            {
                if (!IsShuttingDown)
                {
                    Puts(mx("Destroyed Raid"), $"{PositionToGrid(raid.Location, false)} {raid.Location} ({raid.BaseName}: {raid.Options.Mode})");
                    if (raid.IsOpened) TryInvokeMethod(raid.AwardRaiders);
                    entities.AddRange(raid.Entities);
                }

                raid.Despawn();
            }
            if (entities.Count == 0)
            {
                TryInvokeMethod(RemoveHeldEntities);
                TryInvokeMethod(UnsetStatics);
            }
            else UndoLoop(entities, despawnLimit);
        }

        private void UnsetStatics()
        {
            UI.DestroyAll();
            HtmlTagRegex = null;
            _extensions.Clear();
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
            if (player.IsOnline() && !player.IsDestroyed && player.HasPermission("raidablebases.buyraid.prefabteleport"))
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

        private bool Has(BaseEntity entity, bool checkList = false)
        {
            if (entity.IsKilled())
            {
                return false;
            }

            if (entity.skinID == RB_SKIN_ID)
            {
                return true;
            }

            foreach (var raid in Raids)
            {
                if (raid.Has(entity, checkList, checkDist: true))
                {
                    return true;
                }
            }

            return false;
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
            return type == RaidableType.Maintained || type == RaidableType.Scheduled || type == RaidableType.Purchased;
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
            if (entity.IsKilled())
            {
                raid = null;
                return false;
            }
            foreach (var x in Raids)
            {
                if (x.Has(entity, false, true))
                {
                    raid = x;
                    return true;
                }
            }
            raid = null;
            return false;
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
            foreach (var raid in Raids)
            {
                raid.UpdateMarker();
            }
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
                if (def.TryGetComponent<ItemModDeployable>(out var imd))
                {
                    if (RequiresOwnership(def)) PaidDeployableItems[imd.entityPrefab.resourcePath] = def;
                    DeployableItems[imd.entityPrefab.resourcePath] = def;
                    ItemDefinitions[def] = imd.entityPrefab.resourcePath;
                }
                if (def.category == ItemCategory.Food || def.category == ItemCategory.Medical)
                {
                    if (def.TryGetComponent<ItemModConsume>(out var con))
                    {
                        _itemModConsume[def] = con;
                    }
                }
            }
        }

        /// <summary>InitializeSkins with yields every 50 items so server FPS stays responsive during harmony.load soft-start.</summary>
        public IEnumerator InitializeSkinsCoroutine()
        {
            const int yieldEvery = 50;
            int count = 0;
            foreach (var def in ItemManager.GetItemDefinitions())
            {
                if (def.TryGetComponent<ItemModDeployable>(out var imd))
                {
                    if (RequiresOwnership(def)) PaidDeployableItems[imd.entityPrefab.resourcePath] = def;
                    DeployableItems[imd.entityPrefab.resourcePath] = def;
                    ItemDefinitions[def] = imd.entityPrefab.resourcePath;
                }
                if (def.category == ItemCategory.Food || def.category == ItemCategory.Medical)
                {
                    if (def.TryGetComponent<ItemModConsume>(out var con))
                    {
                        _itemModConsume[def] = con;
                    }
                }
                if (++count >= yieldEvery)
                {
                    count = 0;
                    yield return CoroutineEx.waitForSeconds(0.02f);
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

        private HashSet<ulong> GetMembers(ulong userid)
        {
            HashSet<ulong> members = new() { userid };

            if (RelationshipManager.ServerInstance != null && RelationshipManager.ServerInstance.playerToTeam.TryGetValue(userid, out var team))
            {
                members.UnionWith(team.members);
            }

            // Vanilla Rust clans (ClanManager)
            if (ConVar.Clan.enabled)
            {
                var player = BasePlayer.FindByID(userid) ?? BasePlayer.FindSleeping(userid);
                IClan nativeClan = player?.serverClan;
                if (nativeClan == null && player != null && player.clanId != 0L && ClanManager.ServerInstance?.Backend != null)
                    ClanManager.ServerInstance.Backend.TryGet(player.clanId, out nativeClan);
                if (nativeClan?.Members != null)
                {
                    foreach (ClanMember member in nativeClan.Members)
                        members.Add(member.SteamId);
                }
            }

            // Optional Oxide Clans plugin
            if (Clans?.Call("GetClanMembers", userid) is List<string> clan && !clan.IsNullOrEmpty())
            {
                clan.ForEach(member => members.Add(Convert.ToUInt64(member)));
            }

            return members;
        }

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
        public new bool IsCopyPasteLoaded(out string error) => base.IsCopyPasteLoaded(out error);

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

        private float GetPVPDelay(ulong userid)
        {
            return userid.IsSteamId() && GetPVPDelay(userid, true, out DelaySettings ds) ? ds.time : 0f;
        }

        private bool GetPVPDelay(ulong userid, bool check, out DelaySettings ds)
        {
            if (!PvpDelay.TryGetValue(userid, out ds))
            {
                return false;
            }
            if (check)
            {
                return ds != null && ds.time > Time.time;
            }
            return ds != null;
        }

        private float GetMaxPVPDelay()
        {
            return config.Settings.Management.PVPDelay;
        }

        [HookMethod("HasPVPDelay")]
        public bool HasPVPDelay(ulong userid)
        {
            return GetPVPDelay(userid) > 0f;
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

        private bool IsBox(BaseEntity entity, bool inherit)
        {
            switch (entity.ShortPrefabName)
            {
                case "krieg_storage_vertical":
                case "krieg_storage_horizontal":
                case "abyss_barrel_horizontal":
                case "abyss_barrel_verticle":
                case "medieval.box.wooden.large":
                case "box.wooden.large":
                case "woodbox_deployed":
                case "coffinstorage":
                case "storage_barrel_a":
                case "storage_barrel_b":
                case "storage_barrel_c":
                case "wicker_barrel":
                case "bamboo_barrel":
                    return true;
                default:
                    if (inherit)
                    {
                        foreach (var sub in config.Settings.Management.Inherit)
                        {
                            if (entity.ShortPrefabName.Contains(sub)) return true;
                        }
                    }
                    return entity is DisplayingBoxStorage;
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

        private bool IsPVE()
        {
            if (TruePVE != null || SimplePVE != null || NextGenPVE != null || Imperium != null)
                return true;
            try
            {
                if (ConVar.Server.pve)
                    return true;
            }
            catch { }
            return false;
        }

        [HookMethod("IsPremium")]
        public bool IsPremium() => true;

        private void UpdateUI()
        {
            if (config.UI.Lockout.Enabled || config.UI.BuyableCooldowns.Enabled)
            {
                BasePlayer.activePlayerList.ForEach(player =>
                {
                    UI.UpdateUi(player, UiType.Lockout);
                    UI.UpdateUi(player, UiType.Cooldown);
                });
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
            foreach (var brain in HumanoidBrains.Values)
            {
                if (brain == null || brain.raid == null) continue;
                if (brain.raid.ExtendHookSubscription || !brain.npc.IsKilled()) return true;
            }
            return false;
        }

        private string[] GetProfileFiles()
        {
            try
            {
                return HarmonyDataLayer.GetProfileFileFullPaths();
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
            Message(user, $"Auto-corrected spelling of '{value}' to '{other}'");
            value = other;
            return true;
        }

        private void ConfigAddBase(IPlayer user, string[] args)
        {
            if (args.Length < 2)
            {
                Message(user, "ConfigAddBaseSyntax");
                return;
            }

            using var _sb = DisposableBuilder.Get();
            List<string> values = new(args);
            values.RemoveAt(0);
            string profileName = values[0];

            foreach (var file in GetProfileFiles())
            {
                if (file.Contains("_empty")) continue;
                if (CheckAutoCorrect(user, file, ref profileName)) break;
            }

            string mode = RaidableMode.Random;

            foreach (string value in values)
            {
                var m = GetRaidableMode(value);

                if (m != RaidableMode.Random)
                {
                    values.Remove(value);
                    mode = m;
                    break;
                }
            }

            values.RemoveAll(v => v.Length == 1);
            bool profileExists = FileExists(profileName);
            if (!profileExists) values.Remove(profileName);
            Message(user, "Adding", string.Join(" ", values));

            if (!Buildings.Profiles.TryGetValue(profileName, out var profile))
            {
                Buildings.Profiles[profileName] = profile = new(this);
                profile.ProfileName = profileName;
                _sb.AppendLine(mx("AddedPrimaryBase", user.Id, profileName));
            }

            if (!profileExists && IsModeValid(mode))
            {
                _sb.AppendLine(mx("DifficultySetTo", user.Id, profile.Options.Mode = mode));
            }

            var copypasteFiles = GetCopyPasteFiles();

            if (args.Contains("*"))
            {
                foreach (var path in copypasteFiles)
                {
                    string value = GetFileNameWithoutExtension(path);
                    if (values.Contains(value) || profile.Options.AdditionalBases.ContainsKey(value))
                    {
                        continue;
                    }
                    if (value.Contains(profile.Options.Mode, StringComparison.OrdinalIgnoreCase))
                    {
                        values.Add(value);
                    }
                }
            }

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                foreach (var cpf in copypasteFiles)
                {
                    if (CheckAutoCorrect(user, cpf, ref value)) break;
                }
                if (!profile.Options.AdditionalBases.ContainsKey(value))
                {
                    profile.Options.AdditionalBases.Add(value, DefaultBaseOptions());
                    _sb.AppendLine(mx("AddedAdditionalBase", user.Id, value));
                }
            }

            if (_sb.Length > 0)
            {
                Message(user, _sb.ToString());
                profile.Options.Enabled = true;
                SaveProfile(profileName, profile.Options);
                Buildings.Profiles[profileName] = profile;
                if (mode == RaidableMode.Disabled)
                {
                    Message(user, "DifficultyNotSet");
                }
            }
            else Message(user, "EntryAlreadyExists");
        }

        private static string GetFileNameWithoutExtension(string file) => Utility.GetFileNameWithoutExtension(file);

        private void ConfigRemoveBase(IPlayer user, string[] args)
        {
            if (args.Length < 2)
            {
                Message(user, "RemoveSyntax");
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
            Message(user, _sb.ToString());
        }

        private void ConfigSetEnabledWeekday(IPlayer user, string mode, DayOfWeek dayOfWeek, string flag)
        {
            if (!bool.TryParse(flag, out var value))
            {
                Message(user, $"Invalid flag (true/false): {flag}");
                return;
            }

            Message(user, $"{mode} is now {(value ? "enabled" : "disabled")} on {dayOfWeek}");

            if (!config.Settings.Management.Dictionary.TryGetValue(en ? $"{mode} Raids Can Spawn On" : $"Дни спавна {mode} рейд-баз", out var ds))
            {
                Message(user, $"Unable to find mode '{mode}'");
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

            Message(user, $"{mode} is now limited to {amount} {type} event(s)");

            if (_saveConfigTimer2 != null) _saveConfigTimer2.Reset();
            else _saveConfigTimer2 = timer.Once(1f, SaveConfig);
        }

        private void ConfigCheckFrames(IPlayer user)
        {
            if (GridController.BadFrameRate)
            {
                Message(user ?? _consolePlayer, $"Server FPS must be above 15 for the plugin to load and function properly.");
            }
        }

        private void ConfigListBases(IPlayer user)
        {
            ConfigCheckFrames(user);
            using var _sb = DisposableBuilder.Get();
            using var _sb2 = DisposableBuilder.Get();
            _sb.AppendLine();

            bool anyPVE = false;
            bool validBase = false;

            if (Buildings.Profiles.Count == 0)
            {
                if (IsGridLoading()) Message(user, "GridIsLoading");
                Message(user, "No profiles are loaded!");
            }

            foreach (var (key, profile) in Buildings.Profiles)
            {
                if (!profile.Options.AllowPVP)
                {
                    anyPVE = true;
                }

                if (FileExists(key))
                {
                    _sb.Append(key);
                    validBase = true;
                }
                else _sb.Append(key).Append(mx("IsProfile", user.Id));

                if (profile.Options.AdditionalBases.Count > 0)
                {
                    foreach (var extra in profile.Options.AdditionalBases.Keys)
                    {
                        if (FileExists(extra))
                        {
                            _sb.Append(extra).Append(", ");
                            validBase = true;
                        }
                        else _sb2.Append(extra).Append(mx("FileDoesNotExist", user.Id));
                    }

                    if (validBase)
                    {
                        _sb.Length -= 2;
                    }

                    _sb.AppendLine();
                    _sb.Append(_sb2);
                    _sb2.Clear();
                }

                _sb.AppendLine();
            }

            if (!anyPVE && !AllowBuyingPVP)
            {
                _sb.AppendLine(mx("NoBuyableEventsPVP", user.Id));
            }

            if (!validBase)
            {
                _sb.AppendLine(mx("NoBuildingsConfigured", user.Id));
            }

            Message(user, _sb.ToString());

            if (!IsCopyPasteLoaded(out var error))
            {
                user.Message(error);
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
                    drop.onlyOwnerLoot = false;
                    drop.playerSteamID = 0;
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
                Message(player, "CommandNotAllowed");
                return false;
            }

            if (raid == null || raid.IsLoading)
            {
                Message(player, isAllowed ? "DespawnBaseNoneAvailable" : "DespawnBaseNoneOwned");
                return false;
            }

            if (!raid.CanBypass(player) && raid.IsDamaged && config.Settings.Buyable.Refunds.Despawn)
            {
                Message(player, "DespawnBaseDamaged");
                return false;
            }

            if (!raid.CanBypass(player) && raid.IsAnyLooted && config.Settings.Buyable.Refunds.AnyLooted)
            {
                Message(player, "DespawnBaseLooted");
                return false;
            }

            if (raid.IsPayLocked)
            {
                if (raid.GetOwner() is BasePlayer owner) raid.Refund(owner);
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

            Message(player, "DespawnBaseSuccess");

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
            return Raids.FirstOrDefault(raid => raid.IsPayLocked && raid.ownerId == player.userID);
        }

        private RaidableBase GetNearestBase(Vector3 target, float radius = 100f)
        {
            return Raids.Where(x => InRange2D(x.Location, target, radius)).OrderByAscending(x => (x.Location - target).sqrMagnitude).FirstOrDefault();
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

                m.Add((r.Location, r.Options.Mode, r.Options.Level, r.AllowPVP, r.ID, 0f, 0f, 0f, r.ownerId, r.GetOwner(), r.GetRaiders(), r.GetIntruders(), r.Entities, r.BaseName, r.spawnDateTime, r.despawnDateTime, r.ProtectionRadius, r.GetLootAmountRemaining()));
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
            List<(string userid, int score)> ladder = new();

            foreach (var (userid, info) in players)
            {
                (string, int) score = info.Get(record.Mode) > 0 && config.RankedLadder.Assign.Dictionary.TryGetValue(record.Mode, out var val) && val == 0 ? new(userid, info.Get(record.Mode)) : default;

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

            using var tmp = ladder.TakePooledList(config.RankedLadder.Amount);

            foreach (var (userid, score) in tmp)
            {
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
            List<string> format = new();

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
