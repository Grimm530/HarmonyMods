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
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Hooks

        private void UnsubscribeHooks()
        {
            if (IsUnloading)
            {
                return;
            }

            Unsubscribe(nameof(OnCustomLootNPC));
            Unsubscribe(nameof(CanBGrade));
            Unsubscribe(nameof(CanDoubleJump));
            Unsubscribe(nameof(OnLifeSupportSavingLife));
            Unsubscribe(nameof(CanRevivePlayer));
            Unsubscribe(nameof(OnRestoreUponDeath));
            Unsubscribe(nameof(CanPopulateLoot));
            Unsubscribe(nameof(ShouldBLPopulate_NPC));
            Unsubscribe(nameof(OnNpcKits));
            Unsubscribe(nameof(CanTeleport));
            Unsubscribe(nameof(canTeleport));
            Unsubscribe(nameof(canRemove));
            Unsubscribe(nameof(CanEntityBeTargeted));
            Unsubscribe(nameof(CanEntityTrapTrigger));
            Unsubscribe(nameof(CanOpenBackpack));
            Unsubscribe(nameof(CanBePenalized));
            Unsubscribe(nameof(OnBaseRepair));
            Unsubscribe(nameof(OnClanMemberJoined));
            Unsubscribe(nameof(CanGainXp));
            Unsubscribe(nameof(OnNeverWear));

            Unsubscribe(nameof(OnLoseCondition));
            Unsubscribe(nameof(OnNearbyTurretsScan));
            Unsubscribe(nameof(OnInterferenceUpdate));
            Unsubscribe(nameof(OnMlrsFire));
            Unsubscribe(nameof(OnTeamAcceptInvite));
            Unsubscribe(nameof(OnElevatorMove));
            Unsubscribe(nameof(OnElevatorCall));
            Unsubscribe(nameof(OnButtonPress));
            Unsubscribe(nameof(OnElevatorButtonPress));
            Unsubscribe(nameof(OnSamSiteTargetScan));
            Unsubscribe(nameof(OnPlayerCommand));
            Unsubscribe(nameof(OnServerCommand));
            Unsubscribe(nameof(OnTrapTrigger));
            Unsubscribe(nameof(OnEntityBuilt));
            Unsubscribe(nameof(OnStructureUpgrade));
            Unsubscribe(nameof(OnEntityGroundMissing));
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnLootEntityEnd));
            Unsubscribe(nameof(OnExplosiveFuseSet));
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(CanPickupEntity));
            Unsubscribe(nameof(OnPlayerLand));
            Unsubscribe(nameof(OnPlayerRespawn));
            Unsubscribe(nameof(OnPlayerDeath));
            Unsubscribe(nameof(OnBackpackDrop));
            Unsubscribe(nameof(OnPlayerDropActiveItem));
            Unsubscribe(nameof(OnEntityEnter));
            Unsubscribe(nameof(OnNpcDuck));
            Unsubscribe(nameof(OnNpcDestinationSet));
            Unsubscribe(nameof(OnCupboardAuthorize));
            Unsubscribe(nameof(OnActiveItemChanged));
            Unsubscribe(nameof(OnFireBallSpread));
            Unsubscribe(nameof(OnFireBallDamage));
            Unsubscribe(nameof(OnCupboardProtectionCalculated));

            UnsubscribeDamageHook();
        }

        private void OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)
        {
            // Vanish handles marker teleport for limitNetworking (vanished) admins.
            if (player.limitNetworking)
                return;
            if (player.IsAlive() && player.HasPermission("raidablebases.mapteleport") && !player.isMounted)
            {
                float y = GetSpawnHeight(note.worldPosition);
                if (player.IsFlying) y = Mathf.Max(y, player.transform.position.y);
                player.Teleport(note.worldPosition.WithY(y));
                if (config.Settings.DestroyMarker)
                {
                    player.State.pointsOfInterest?.Remove(note);
                    note.Dispose();
                    player.DirtyPlayerState();
                    player.SendMarkersToClient();
                }
            }
        }

        private void OnNewSave(string filename)
        {
            if (config.Settings.Wipe.Map)
            {
                Puts("New map detected; wiping ranked ladder");
                wiped = true;
            }
        }

        internal void InitHarmony() => Init();
        internal void UnloadHarmony() => Unload();
        internal void InitMinimal()
        {
            LoadConfig();
            Kits = RaidableBasesHost.Instance?.Kits ?? new KitsPluginStub();
            KitsAPI.Init();
        }
        internal void InitRest()
        {
            if (InstallationError) return;
            _messages = new(this);
            HtmlTagRegex = new("<.*?>", RegexOptions.Compiled);
            _configPresetController = new(this);
            _pasteEngine = new(this);
            _targetInfo = new(this);
            _pasteEngine.Initialize();
            harmonyEngine ??= new HarmonyEngine(this);
            Automated = new(this, config.Settings.Maintained.Enabled, config.Settings.Schedule.Enabled);
            UndoComparer.DeployableItems = DeployableItems;
            UndoComparer.IsBox = IsBox;
            SpawnsController.Instance = this;
            UI = new() { Instance = this };
            UI.LoadOffsetData();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                UI.DestroyAllUi(player);
            }
            IsUnloading = false;
            Buildings = new();
            GridController.Instance = this;
            IsSpawnerBusy = true;
            RegisterPermissions();
            buyableEnabled = config.Settings.Buyable.Max > 0;
            Unsubscribe(nameof(OnMapMarkerAdded));
            Unsubscribe(nameof(OnPlayerSleepEnded));
            Unsubscribe(nameof(CanBuild));
            Unsubscribe(nameof(CanLootEntity));
            Unsubscribe(nameof(OnEntitySpawned));
            UnsubscribeHooks();
            SpawnsController.Initialize();
            Queues = new(this);
        }
        private void Init()
        {
            LoadConfig();
            if (InstallationError) return;
            InitRest();
        }

        private void OnServerShutdown()
        {
            IsShuttingDown = true;
            IsUnloading = true;
        }

        private void Unload()
        {
            if (InstallationError) return;
            IsUnloading = true;
            IsSpawnerBusy = true;
            _configPresetController?.Dispose();
            _pasteEngine?.Dispose();
            _targetInfo?.Dispose();
            _messages?.Dispose();
            TryInvokeMethod(ClearPlayerDelayExclusions);
            SaveData();
            UI?.DestroyAll();
            TryInvokeMethod(StopLoadCoroutines);
            TryInvokeMethod(UnsubscribeSky);
            TryInvokeMethod(StartEntityCleanup);
            DestroyProtection();
        }

        internal void SetUnloadingState(bool unloading, bool spawnerBusy)
        {
            IsUnloading = unloading;
            IsSpawnerBusy = spawnerBusy;
        }

        internal IEnumerator RunUnloadStepsAsync()
        {
            if (InstallationError) yield break;
            _configPresetController?.Dispose();
            _pasteEngine?.Dispose();
            _targetInfo?.Dispose();
            _messages?.Dispose();
            SaveData();
            yield return null;
            UI?.DestroyAll();
            yield return null;
            UnsubscribeSky();
            yield return null;
            StartEntityCleanup();
            yield return null;
            DestroyProtection();
        }

        internal void RunUnloadStepsSync()
        {
            if (InstallationError) return;
            SaveData();
            TryInvokeMethod(UnsubscribeSky);
            TryInvokeMethod(StartEntityCleanup);
            DestroyProtection();
        }

        public void OnServerInitializedHarmony() => OnServerInitialized(true);

        public void StartSoftInitCoroutine(System.Action onComplete = null)
        {
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.StartCoroutine(OnServerInitializedSoftStartCoroutine(onComplete));
            else
            {
                OnServerInitialized(true);
                onComplete?.Invoke();
            }
        }

        public IEnumerator OnServerInitializedSoftStartCoroutine(System.Action onComplete = null)
        {
            yield return null;
            if (InstallationError || IsUnloading || RaidableBasesHost.Instance == null) yield break;
            if (Queues != null) { onComplete?.Invoke(); yield break; }
            InitRest();
            yield return null;
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            SpawnsController.instruction0 = CoroutineEx.waitForSeconds(0.0025f);
            if (!string.IsNullOrWhiteSpace(config.Settings.EditCommand)) AddCovalenceCommand(config.Settings.EditCommand, nameof(CommandEdit));
            if (!string.IsNullOrWhiteSpace(config.Settings.BuyCommand)) AddCovalenceCommand(config.Settings.BuyCommand, nameof(CommandBuyRaid));
            if (!string.IsNullOrWhiteSpace(config.Settings.EventCommand)) AddCovalenceCommand(config.Settings.EventCommand, nameof(CommandRaidBase));
            if (!string.IsNullOrWhiteSpace(config.Settings.HunterCommand)) AddCovalenceCommand(config.Settings.HunterCommand, nameof(CommandRaidHunter));
            if (!string.IsNullOrWhiteSpace(config.Settings.ConsoleCommand)) AddCovalenceCommand(config.Settings.ConsoleCommand, nameof(CommandRaidBase));
            AddCovalenceCommand("rb.reloadconfig", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadprofiles", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadtables", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.config", nameof(CommandConfig), "raidablebases.config");
            AddCovalenceCommand("rb.populate", nameof(CommandPopulate), "raidablebases.config");
            AddCovalenceCommand("rb.toggle", nameof(CommandToggle), "raidablebases.config");
            AddCovalenceCommand("rb.difficulty", nameof(CommandDifficulty), "raidablebases.config");
            CommandRegistry.RegisterAttributedConsoleCommands(this);
            yield return null;
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            LoadPlayerData(true);
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            yield return InitializeSkinsCoroutine();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            Initialize();
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, _messages.ProcessQueue);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
            onComplete?.Invoke();
            Puts("Soft-start complete.");
        }

        private void OnServerInitialized(bool initial)
        {
            if (InstallationError) return;
            SpawnsController.instruction0 = CoroutineEx.waitForSeconds(0.0025f);
            if (!string.IsNullOrWhiteSpace(config.Settings.EditCommand)) AddCovalenceCommand(config.Settings.EditCommand, nameof(CommandEdit));
            if (!string.IsNullOrWhiteSpace(config.Settings.BuyCommand)) AddCovalenceCommand(config.Settings.BuyCommand, nameof(CommandBuyRaid));
            if (!string.IsNullOrWhiteSpace(config.Settings.EventCommand)) AddCovalenceCommand(config.Settings.EventCommand, nameof(CommandRaidBase));
            if (!string.IsNullOrWhiteSpace(config.Settings.HunterCommand)) AddCovalenceCommand(config.Settings.HunterCommand, nameof(CommandRaidHunter));
            if (!string.IsNullOrWhiteSpace(config.Settings.ConsoleCommand)) AddCovalenceCommand(config.Settings.ConsoleCommand, nameof(CommandRaidBase));
            AddCovalenceCommand("rb.reloadconfig", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadprofiles", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadtables", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.config", nameof(CommandConfig), "raidablebases.config");
            AddCovalenceCommand("rb.populate", nameof(CommandPopulate), "raidablebases.config");
            AddCovalenceCommand("rb.toggle", nameof(CommandToggle), "raidablebases.config");
            AddCovalenceCommand("rb.difficulty", nameof(CommandDifficulty), "raidablebases.config");
            CommandRegistry.RegisterAttributedConsoleCommands(this);
            LoadPlayerData(initial);
            InitializeSkins();
            Initialize();
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, _messages.ProcessQueue);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
        }

        private void EnsureSchedulers(bool npcEnabled)
        {
            if (npcEnabled && npcSchedulerCo == null)
            {
                npcSchedulerCo = ServerMgr.Instance.StartCoroutine(NpcSchedulerCoroutine());
            }

            if (raidMaintenanceCo == null)
            {
                raidMaintenanceCo = ServerMgr.Instance.StartCoroutine(RaidMaintenanceCoroutine());
            }
        }

        private IEnumerator NpcSchedulerCoroutine()
        {
            WaitForSeconds instruction = CoroutineEx.waitForSeconds(0.1f);
            FrameDeadline deadline = new(npcSchedulerFrameBudgetMilliseconds);

            try
            {
                while (!IsUnloading && Raids.Count > 0)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    using var brains = HumanoidBrains.Values.ToPooledList();

                    for (int i = 0; i < brains.Count; i++)
                    {
                        HumanoidBrain brain = brains[i];
                        if (brain == null || brain.CannotSchedule())
                        {
                            continue;
                        }

                        brain.TickEquipment(now);

                        if (deadline.Expired)
                        {
                            yield return null;
                            now = Time.realtimeSinceStartupAsDouble;
                            deadline.Reset();
                        }

                        if (brain == null || brain.CannotSchedule())
                        {
                            continue;
                        }

                        if (now >= brain.nextAttackThinkTime)
                        {
                            brain.nextAttackThinkTime = now + 1d;
                            brain.TryToAttack();

                            if (deadline.Expired)
                            {
                                yield return null;
                                now = Time.realtimeSinceStartupAsDouble;
                                deadline.Reset();
                            }
                        }

                        if (brain == null || brain.CannotSchedule())
                        {
                            continue;
                        }

                        if (brain.UsesBaseNavigation)
                        {
                            brain.TickBaseNavigation(now);
                        }
                        else if (!brain.isStationary && now >= brain.nextRoamThinkTime)
                        {
                            brain.nextRoamThinkTime = now + UnityEngine.Random.Range(6f, 7f);
                            brain.TryToRoam();
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            now = Time.realtimeSinceStartupAsDouble;
                            deadline.Reset();
                        }
                    }

                    yield return instruction;
                }
            }
            finally
            {
                npcSchedulerCo = null;
            }
        }

        private IEnumerator RaidMaintenanceCoroutine()
        {
            WaitForSeconds instruction = CoroutineEx.waitForSeconds(0.05f);
            FrameDeadline deadline = new(raidMaintenanceFrameBudgetMilliseconds);

            try
            {
                while (!IsUnloading && Raids.Count > 0)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    using var raids = Raids.ToPooledList();

                    for (int i = 0; i < raids.Count; i++)
                    {
                        RaidableBase raid = raids[i];
                        if (raid == null || raid.IsDespawning || !raid.protectionStarted || now < raid.nextProtectorTime)
                        {
                            continue;
                        }

                        if (raid.intruders.Count > 0)
                        {
                            raid.UpdateLootAmountCounted();
                        }

                        while (!raid.RunProtector(deadline.Value))
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        now = Time.realtimeSinceStartupAsDouble;

                        if (!raid.IsDespawning)
                        {
                            raid.nextProtectorTime = now + 1d;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                            now = Time.realtimeSinceStartupAsDouble;
                        }
                    }

                    yield return instruction;
                    deadline.Reset();
                }
            }
            finally
            {
                raidMaintenanceCo = null;
            }
        }

        private void OnSunrise()
        {
            Raids.ForEach(raid => raid.ToggleLights());
        }

        private void OnSunset()
        {
            Raids.ForEach(raid => raid.ToggleLights());
        }

        private object OnLifeSupportSavingLife(BasePlayer player)
        {
            return EventTerritory(player.transform.position) || HasPVPDelay(player.userID) ? true : (object)null;
        }

        private object CanDoubleJump(BasePlayer player)
        {
            return EventTerritory(player.transform.position) || HasPVPDelay(player.userID) ? true : (object)null;
        }

        private object OnRestoreUponDeath(BasePlayer player)
        {
            return Get(player, null, out var raid) && (raid.AllowPVP ? config.Settings.Management.BlockRestorePVP : config.Settings.Management.BlockRestorePVE) ? true : (object)null;
        }

        private object CanRevivePlayer(BasePlayer player, Vector3 pos)
        {
            return Get(pos, out var raid) && (raid.AllowPVP ? config.Settings.Management.BlockRevivePVP : config.Settings.Management.BlockRevivePVE) ? true : (object)null;
        }

        private object OnCustomLootNPC(NetworkableId networkableId)
        {
            return Has(networkableId) ? true : (object)null;
        }

        private object CanPopulateLoot(BaseEntity entity, LootableCorpse corpse)
        {
            return corpse != null && corpse.skinID == RB_SKIN_ID ? true : (object)null;
        }

        private object ShouldBLPopulate_NPC(ulong playerSteamID)
        {
            return playerSteamID >= 514922525 && playerSteamID <= BotIdCounter ? true : (object)null;
        }

        private object OnNpcKits(ulong targetId)
        {
            return HumanoidBrains.ContainsKey(targetId) ? true : (object)null;
        }

        private object OnReflectDamage(BasePlayer victim, BasePlayer attacker)
        {
            return PlayerInEvent(victim) || PlayerInEvent(attacker) ? true : (object)null;
        }

        private object CanBGrade(BasePlayer player, int playerGrade, BuildingBlock block, Planner planner)
        {
            return PlayerInEvent(player) ? 0 : (object)null;
        }

        private object canRemove(BasePlayer player)
        {
            return !player.IsFlying && EventTerritory(player.transform.position) ? mx("CannotRemove", player.UserIDString) : null;
        }

        private object canTeleport(BasePlayer player)
        {
            return !player.IsFlying && (EventTerritory(player.transform.position) || HasPVPDelay(player.userID)) ? m("CannotTeleport", player.UserIDString) : null;
        }

        private object CanTeleport(BasePlayer player, Vector3 to)
        {
            return !player.IsFlying && (EventTerritoryAny(new Vector3[2] { to, player.transform.position }) || HasPVPDelay(player.userID)) ? m("CannotTeleport", player.UserIDString) : null;
        }

        private object OnBaseRepair(BuildingManager.Building building, BasePlayer player)
        {
            return EventTerritory(player.transform.position) ? false : (object)null;
        }

        private object CanGainXp(BasePlayer player, double amount, string pluginName)
        {
            if (pluginName == Name)
            {
                foreach (var raid in Raids)
                {
                    if (raid.IsParticipant(player))
                    {
                        return true;
                    }
                }
            }
            return null;
        }

        private object OnRaidingUltimateTargetAcquire(BasePlayer player, Vector3 targetPoint)
        {
            return !Get(targetPoint, out var raid) || raid.Options.MLRS ? (object)null : true;
        }

        private void OnClanMemberJoined(ulong userid, string tag)
        {
            var player = BasePlayer.FindByID(userid);
            if (player == null) return;
            RaidableBase raid = null;
            foreach (var other in Raids)
            {
                if (other.ownerId == player.userID && other.IsAllyHogging(player))
                {
                    raid = other;
                    break;
                }
            }
            if (raid == null) return;
            Clans?.Call("cmdChatClan", player, "clan", new string[1] { "leave" });
        }

        private object OnTeamAcceptInvite(RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            if (player == null) return null;
            RaidableBase raid = null;
            foreach (var other in Raids)
            {
                if (other.ownerId == player.userID && other.IsAllyHogging(player))
                {
                    raid = other;
                    break;
                }
            }
            if (raid == null) return null;
            playerTeam.RejectInvite(player);
            return true;
        }

        private object OnNeverWear(Item item, float amount)
        {
            var player = item?.parentItem?.GetOwnerPlayer() ?? item?.GetOwnerPlayer();

            if (player == null || !player.IsHuman() || player.HasPermission("raidablebases.durabilitybypass"))
            {
                return null;
            }

            if (!Get(player.transform.position, out var raid) || !raid.Options.EnforceConditionLoss)
            {
                return null;
            }

            return amount;
        }

        private void OnDeletedDynamicPVP(string zoneId, string eventName)
        {
            SpawnsController.ManagedZones.Remove(zoneId);
        }

        private void OnCreatedDynamicPVP(string zoneId, string eventName, Vector3 position, float duration)
        {
            if (ZoneManager != null)
            {
                SpawnsController.AddZone(zoneId);
            }
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (item == null || item.instanceData != null && item.instanceData.dataFloat > 0f)
            {
                return;
            }

            var player = item?.parentItem?.GetOwnerPlayer() ?? item?.GetOwnerPlayer();

            if (player == null || !player.userID.IsSteamId() || player.HasPermission("raidablebases.durabilitybypass"))
            {
                return;
            }

            if (!Get(player.transform.position, out var raid) || !raid.Options.EnforceConditionLoss)
            {
                return;
            }

            var uid = item.uid;

            if (!raid.conditions.TryGetValue(uid, out var condition))
            {
                raid.conditions[uid] = condition = item.condition;
            }

            float _previous = condition - amount;

            raid.Invoke(() =>
            {
                if (raid == null)
                {
                    return;
                }

                if (IsKilled(item))
                {
                    raid.conditions.Remove(uid);
                    return;
                }

                if (_previous < item.condition)
                {
                    item.condition = _previous;
                }

                if (item.condition <= 0f && item.condition < condition)
                {
                    item.OnBroken();
                    raid.conditions.Remove(uid);
                }
                else raid.conditions[uid] = item.condition;
            }, 0.0625f);
        }

        private object OnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade, ulong skin)
        {
            if (!Get(block.transform.position, out var raid))
            {
                return null;
            }

            if (block.OwnerID == 0uL && !block.enableSaving)
            {
                return config.Settings.Management.AllowUpgrade ? (object)null : true;
            }

            return grade switch
            {
                BuildingGrade.Enum.Wood when raid.Options.BuildingRestrictions.Wooden => true,
                BuildingGrade.Enum.Stone when raid.Options.BuildingRestrictions.Stone => true,
                BuildingGrade.Enum.Metal when raid.Options.BuildingRestrictions.Metal => true,
                BuildingGrade.Enum.TopTier when raid.Options.BuildingRestrictions.HQM => true,
                _ => null
            };
        }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var e = go.ToBaseEntity();

            if (e == null || !Get(e.transform.position, out var raid, 0.6f))
            {
                return;
            }

            var player = planner.GetOwnerPlayer();

            if (player == null || IsPocketDimensions(player, e))
            {
                return;
            }

            if (raid.Options.Mounts.Siege && !raid.Options.Siege.Only)
            {
                if (e is BaseSiegeWeapon || e is ConstructableEntity)
                {
                    raid.Eject(e, raid.Location, raid.ProtectionRadius, true);
                    return;
                }
            }

            if (raid.Options.BuildingRestrictions.Any() && e is BuildingBlock block)
            {
                var grade = block.grade;

                block.Invoke(() =>
                {
                    if (raid == null || block.IsDestroyed)
                    {
                        return;
                    }

                    if (block.grade == grade || OnStructureUpgrade(block, player, block.grade, block.skinID) == null)
                    {
                        AddPlayerEntity(e, raid);
                        return;
                    }

                    foreach (var ia in block.BuildCost().Items)
                    {
                        player.GiveItem(ItemManager.Create(ia.itemDef, (int)ia.amount));
                    }

                    block.SafelyKill();
                }, 0.1f);
            }
            else if (raid.IsFoundation(e) && raid.NearFoundation(e.transform.position))
            {
                SendNotification(player, "TooCloseToABuilding");
                e.Invoke(e.SafelyKill, 0.1f);
            }
            else AddPlayerEntity(e, raid);
        }

        private void AddPlayerEntity(BaseEntity e, RaidableBase raid)
        {
            if (raid.AllowPVP && e is AutoTurret)
            {
                e.skinID = RB_SKIN_ID;
            }

            raid.BuiltList.Add(e);
            raid.SetupEntity(e, false);

            if (e is ConstructableEntity || e.PrefabName.Contains("assets/prefabs/deployable/"))
            {
                if (config.Settings.Management.KeepDeployables)
                {
                    raid.DestroyGroundCheck(e);
                }
                else
                {
                    raid.AddEntity(e);
                }
            }
            else if (!config.Settings.Management.KeepStructures)
            {
                raid.AddEntity(e);
            }
        }

        private object OnElevatorButtonPress(ElevatorLift e, BasePlayer player, Elevator.Direction Direction, bool FullTravel)
        {
            if (e == null)
            {
                return null;
            }
            var parent = e.HasParent() ? e.GetParentEntity() : e.owner;
            if (parent == null || parent.net == null || parent.IsDestroyed)
            {
                return null;
            }
            if (!Get(parent.transform.position, out var raid) || !raid.Elevators.TryGetValue(parent.net.ID, out var elevator))
            {
                return null;
            }
            if (elevator.IsBMG())
            {
                if (elevator.CanUseElevator(player))
                {
                    elevator.BMG.GoToFloor(Direction, FullTravel);
                    return null;
                }
                return true;
            }
            if (elevator.IsVanilla() && !elevator.CanUseElevator(player))
            {
                return true;
            }
            return null;
        }

        private void OnButtonPress(PressButton button, BasePlayer player)
        {
            if (button == null || button.OwnerID != 0 || button.IsDestroyed)
            {
                return;
            }
            var buttonPos = button.transform.position;
            if (!Get(buttonPos, out var raid) || raid.Elevators.Count == 0 || !raid.buttons.Contains(button))
            {
                return;
            }
            foreach (var ele in raid.Elevators.Values)
            {
                if (ele.IsVanilla())
                {
                    if (raid.Options.Elevators.RequiresPower && !button.IsPowered())
                    {
                        continue;
                    }
                    if (InRange2D(buttonPos, ele.Elevator.transform.position, 3f) && Mathf.Abs(buttonPos.y - ele.Elevator.GetWorldSpaceFloorPosition(ele.Elevator.Floor).y) <= 1.5f && ele.CanUseElevator(player))
                    {
                        ele.Elevator.CallElevator();
                    }
                }
                else if (ele.IsBMG())
                {
                    if (BMGELEVATOR.GetElevatorLift(ele.BMG._elevator, out var lift) && InRange(buttonPos, lift.transform.position, 3f) && ele.CanUseElevator(player))
                    {
                        ele.BMG.GoToFloor(Elevator.Direction.Up, false, Mathf.CeilToInt(buttonPos.y));
                    }
                }
            }
        }

        private object OnElevatorMove(Elevator elevator, int targetFloor)
        {
            if (elevator.IsValid() && !elevator.IsDestroyed && Get(elevator.transform.position, out var raid) && raid.Elevators.TryGetValue(elevator.net.ID, out var ele) && ele.IsBMG()) return true;
            return null;
        }

        private object OnElevatorCall(Elevator elevator, Elevator fromElevator) => OnElevatorMove(elevator, 0);

        private string GetTypeName(BaseEntity entity)
        {
            string key = entity.Is(out BasePlayer player) ? player.UserIDString : entity.PrefabName;
            if (!TypeNameLookup.TryGetValue(key, out string name))
            {
                TypeNameLookup[key] = name = entity.GetType().Name;
            }
            return name;
        }

        private bool IsProtectedScientist(BasePlayer player, BaseEntity entity)
        {
            if (Has(player))
            {
                return false;
            }
            NPCPlayer npc = player as NPCPlayer;
            if (npc == null || string.IsNullOrEmpty(npc.UserIDString))
            {
                return false;
            }
            string name = GetTypeName(player);
            if (!name.Contains("CustomScientist", CompareOptions.OrdinalIgnoreCase) && !name.Contains("Better", CompareOptions.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!Get(npc.transform.position, out var raid) || !raid.Options.NPC.IgnorePlayerTrapsTurrets || !InRange(raid.Location, npc.spawnPos, raid.ProtectionRadius))
            {
                return false;
            }
            if (entity.Is(out AutoTurret turret) && turret.OwnerID == 0 && turret.skinID == RB_SKIN_ID)
            {
                turret.authorizedPlayers.Add(player.userID);
            }
            if (entity is StorageContainer && !raid.priv.IsKilled())
            {
                raid.priv.authorizedPlayers.Add(player.userID);
            }
            return true;
        }

        private object OnNpcDuck(HumanoidNPC npc) => true;

        private object OnNpcDestinationSet(HumanoidNPC npc, Vector3 newDestination)
        {
            if (npc == null || !npc.NavAgent || !npc.NavAgent.enabled || !npc.NavAgent.isOnNavMesh)
            {
                return null;
            }

            if (!HumanoidBrains.TryGetValue(npc.userID, out var brain) || brain.CanRoam(newDestination))
            {
                return null;
            }

            return true;
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (!player.IsKilled() && player.IsHuman() && Get(player.transform.position, out var raid))
            {
                raid.StopUsingWeapon(player);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (!ReferenceEquals(player, null))
            {
                _targetInfo?.Hide(player, false);
                _configPresetController?.ForgetImportUi(player.userID);
                UI.RemoveUser(player.userID);
            }

            if (player == null || !player.IsDead())
            {
                return;
            }

            foreach (var raid in Raids)
            {
                if (!raid.raiders.TryGetValue(player.userID, out var ri) || ri.PreEnter)
                {
                    continue;
                }

                raid.HandlePlayerExiting(player);
                break;
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !player.IsHuman())
                return;
            player.Invoke(() =>
            {
                if (player.IsDestroyed)
                {
                    return;
                }

                if (data.Players.TryGetValue(player.UserIDString, out var info))
                {
                    info.Name = player.displayName.ToFriendlyJson();
                }

                UI.PrivateEvents.Remove(player.userID);
                UI.PublicEvents.Remove(player.userID);
                UI.DestroyUi(player, UiType.Status);

                if (GetPVPDelay(player.userID, false, out DelaySettings ds))
                {
                    RemovePVPDelay(player.userID, ds);

                    if (config.UI.Delay.Enabled)
                    {
                        UI.DestroyUi(player, UiType.Delay);
                    }
                }

                if (config.UI.Lockout.Enabled)
                {
                    UI.UpdateUi(player, UiType.Lockout);
                }

                if (!Get(player.transform.position, out var raid, 5f))
                {
                    return;
                }

                //if (raid.IsUnderground(player.transform.position))
                //{
                //    raid.OnPlayerExit(player);
                //    raid.intruders.Remove(player.userID);
                //    raid.enteredEntities.Remove(player);
                //    return;
                //}

                if (raid.IsUnderground(player.transform.position))
                {
                    raid.HandlePlayerUnderground(player);
                    return;
                }

                if (!config.Settings.Management.AllowTeleport && !raid.TeleportExceptions.Remove(player.userID) && !raid.CanBypass(player) && !raid.CanRespawnAt(player) && raid.Type != RaidableType.None && !raid.WasConnected(player))
                {
                    SendNotification(player, "CannotTeleport");
                    //raid.intruders.Remove(player.userID);
                    raid.HandlePlayerExiting(player);
                    raid.RemovePlayer(player, raid.Location, raid.ProtectionRadius, raid.Type, true);
                }
                else
                {
                    if (!raid.intruders.Contains(player.userID))
                    {
                        raid.enteredEntities.Remove(player);
                    }
                    raid.HandlePlayerEntering(player);
                }
            }, 0.015f);
        }

        private object OnPlayerLand(BasePlayer player, float amount)
        {
            return player == null || !Get(player.transform.position, out var raid) || !raid.IsDespawning ? (object)null : true;
        }

        private object OnPlayerRespawn(BasePlayer player, BasePlayer.SpawnPoint spawnPoint)
        {
            if (player == null || !player.IsHuman() || !Get(spawnPoint.pos, out var raid, M_RADIUS))
            {
                return null;
            }

            for (int i = 0; i < 3; i++)
            {
                BasePlayer.SpawnPoint replacement = ServerMgr.FindSpawnPoint(player);

                if (!EventTerritory(replacement.pos, M_RADIUS))
                {
                    return replacement;
                }
            }

            spawnPoint.pos = raid.GetEjectLocation(spawnPoint.pos, 100f, raid.Location, raid.ProtectionRadius * 2f, towardsZero: true, setHeight: true);
            return spawnPoint;
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null && info != null)
            {
                player = info.HitEntity as BasePlayer;
            }

            if (player == null)
            {
                return;
            }

            if (!Get(player, info, out var raid))
            {
                return;
            }

            if (!player.IsHuman())
            {
                if (!Has(player) || !HumanoidBrains.TryGetValue(player.userID, out var brain))
                {
                    return;
                }

                brain.DisableShouldThink();

                var attacker = info?.Initiator as BasePlayer;

                if (config.Settings.Management.UseOwners && attacker != null && raid.AddLooter(attacker) && !raid.ownerId.IsSteamId())
                {
                    raid.TrySetOwner(attacker, player, info, false);
                }

                if (!raid.IsEngaged && raid.EngageOnNpcDeath && attacker != null && attacker.IsHuman() && !attacker.IsFlying && !IsVanished(attacker))
                {
                    raid.IsEngaged = true;
                }

                raid.CheckDespawn();
                raid.CreateSpheres();
            }
            else
            {
                if (CanDropPlayerBackpack(player, raid))
                {
                    Backpacks?.Call("API_DropBackpack", player);
                }

                //if (!raid.intruders.Contains(player.userID))
                //{
                //    raid.OnPlayerExited(player);
                //} 

                raid.HandlePlayerExiting(player);
                raid.HandleTurretSight(player);
            }
        }

        private object OnBackpackDrop(Item backpack, PlayerInventory inv)
        {
            if (backpack == null || inv == null || inv.baseEntity == null) return null;
            BasePlayer player = inv.baseEntity;
            if (!player.IsHuman() || !Get(player, player.userID, out var raid)) return null;
            if (raid.CanDropRustBackpack(player.userID))
            {
                backpack.RemoveFromContainer();
                backpack.Drop(player.GetDropPosition() + new Vector3(0f, 0.035f), player.GetDropVelocity());
                return null;
            }
            return true;
        }

        private void DropRustBackpack(PlayerCorpse corpse)
        {
            if (corpse?.containers != null)
            {
                var position = corpse.GetDropPosition() + new Vector3(0f, 0.035f);
                var velocity = corpse.GetDropVelocity();
                foreach (var container in corpse.containers)
                {
                    if (container != null && container.itemList != null)
                    {
                        for (int i = container.itemList.Count - 1; i >= 0; i--)
                        {
                            Item item = container.itemList[i];
                            if (item != null && item.IsBackpack() && item.contents != null && !item.contents.itemList.IsNullOrEmpty())
                            {
                                if (PreventLooting != null) item.RemoveFromContainer();
                                item.Drop(position, velocity);
                            }
                        }
                    }
                }
            }
        }

        private void DropRustBackpack(DroppedItemContainer backpack)
        {
            if (backpack?.inventory?.itemList != null)
            {
                var position = backpack.GetDropPosition() + new Vector3(0f, 0.035f);
                var velocity = backpack.GetDropVelocity();
                for (int i = backpack.inventory.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = backpack.inventory.itemList[i];
                    if (item != null && item.IsBackpack() && item.contents != null && !item.contents.itemList.IsNullOrEmpty())
                    {
                        if (PreventLooting != null) item.RemoveFromContainer();
                        item.Drop(position, velocity);
                    }
                }
            }
        }

        private object OnPlayerDropActiveItem(BasePlayer player, Item item)
        {
            return EventTerritory(player.transform.position) ? true : (object)null;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsKilled() && !player.HasPermission("raidablebases.allow.commands"))
            {
                List<string> commands =
                    Get(player.transform.position, out var raid) ? raid.BlacklistedCommands :
                    config.Settings.Management.PVPDelayPersists && GetPVPDelay(player.userID, true, out var ds) && ds.raid != null ? ds.raid.BlacklistedCommands : null;
                if (commands != null && commands.Exists(value => command.EndsWith(value, StringComparison.OrdinalIgnoreCase)))
                {
                    SendNotification(player, "CommandNotAllowed");
                    return true;
                }
            }
            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            return OnPlayerCommand(arg.Player(), arg.cmd.FullName, Array.Empty<string>());
        }

        private object OnExplosiveFuseSet(TimedExplosive explosive, float fuseLength)
        {
            if (!(explosive.creatorEntity is HumanoidNPC npc) || !HumanoidBrains.TryGetValue(npc.userID, out var brain) || !brain.Settings.PlayCatch || !brain.ValidTarget)
            {
                return null;
            }

            return brain.ServerPosition.Distance(brain.AttackPosition) * 0.1275f;
        }

        private void OnEntityDeath(BuildingPrivlidge priv, HitInfo info)
        {
            if (!Get(priv, out var raid) || raid.priv != priv)
            {
                return;
            }

            if (!raid.IsEngaged && raid.EngageOnBaseDamage)
            {
                raid.IsEngaged = true;
            }

            if (!raid.IsDespawning && config.Settings.Management.AllowCupboardLoot)
            {
                DropOrRemoveItems(priv, raid, true, false);
                raid.UpdateLootAmountCounted();
            }

            if (raid.Options.RequiresCupboardAccess)
            {
                OnCupboardAuthorize(priv, null);
            }

            if (raid.GetInitiatorPlayer(info, DamageType.Generic, priv, out var attacker))
            {
                raid.GetRaider(attacker).HasDestroyed = true;
            }

            raid.OnBuildingPrivilegeDestroyed();
        }

        private void OnEntityKill(StorageContainer container)
        {
            if (container is BuildingPrivlidge priv)
            {
                OnEntityDeath(priv, null);
            }
            if (container != null)
            {
                EntityHandler(container, null);
            }
        }

        private void OnEntityDeath(StorageContainer container, HitInfo info) => EntityHandler(container, info);

        //private void OnEntityKill(BuildingBlock block) => OnEntityDeath(block, new HitInfo(block.lastAttacker, block, DamageType.Explosion, 9999f)); // ent kill testing
        private void OnEntityKill(Fridge io) => OnEntityDeath(io, null);

        private void OnEntityDeath(StabilityEntity entity, HitInfo info)
        {
            if (!Get(entity.transform.position, out var raid) || raid.IsDespawning)
            {
                return;
            }

            raid.InvalidateBaseRoute(entity);

            if (info == null || !raid.GetInitiatorPlayer(info, DamageType.Generic, entity, out var attacker))
            {
                return;
            }

            if (raid.AddLooter(attacker))
            {
                raid.AddMember(attacker.userID);

                raid.TrySetOwner(attacker, entity, info, false);

                raid.GetRaider(attacker).HasDestroyed = true;
            }

            if (raid.CanSetPVPDelay(attacker))
            {
                raid.TrySetPVPDelay(attacker, false, false, "AttackableFromOutside");
            }

            raid.CheckDespawn();

            if (raid.IsDamaged)
            {
                return;
            }

            if (entity is BuildingBlock or Door)
            {
                raid.IsDamaged = true;
            }
        }

        private object OnEntityGroundMissing(StorageContainer container)
        {
            return Get(container, out var raid) && !raid.CanHurtBox(container) ? true : (object)null;
        }

        //private void OnEntityKill(IOEntity io) => OnEntityDeath(io, null);

        private void OnEntityDeath(IOEntity io, HitInfo info)
        {
            if (io.IsKilled() || !config.Settings.Management.DropLoot.Get(io))
            {
                return;
            }
            if (!Get(io, out var raid) || raid.IsDespawning || raid.IsLoading)
            {
                return;
            }
            if (io is Fridge fridge && raid.fridges.Remove(fridge))
            {
                BaseEntity drop = DropLoot(io, fridge.inventory, raid.Options.BuoyantBox);
                if (raid.Options.DespawnGreyBoxBags) raid.SetupEntity(drop);
            }
            else if (io is AutoTurret turret && raid.TryRemoveOrConfirmTurret(turret))
            {
                BaseEntity drop = DropLoot(io, turret.inventory, raid.Options.BuoyantBox);
                if (config.Settings.Management.DropLoot.DespawnGreyWeaponBags) raid.SetupEntity(drop);
            }
            else if (io is SamSite samsite && raid.samsites.Remove(samsite))
            {
                BaseEntity drop = DropLoot(io, samsite.inventory, raid.Options.BuoyantBox);
                if (config.Settings.Management.DropLoot.DespawnGreyWeaponBags) raid.SetupEntity(drop);
            }
        }

        private void EntityHandler(StorageContainer container, HitInfo info)
        {
            if (!Get(container, out var raid) || raid.IsDespawning || raid.IsLoading)
            {
                return;
            }

            if (!raid.IsEngaged && raid.EngageOnBaseDamage)
            {
                raid.IsEngaged = true;
            }

            DropOrRemoveItems(container, raid, false, false);

            if (raid._containers.Remove(container))
            {
                HarmonyModInterface.CallHook("OnRaidableLootDestroyed", raid.Location, raid.ProtectionRadius, raid.UpdateLootAmountCounted(), container, raid.Options.Level);
            }

            if (!raid.IsAnyLooted && info != null)
            {
                raid.IsAnyLooted = info.Initiator is BasePlayer || info.damageTypes.Has(DamageType.Heat);
            }

            if (IsLootingWeapon(info) && raid.GetInitiatorPlayer(info, DamageType.Generic, container, out var attacker) && raid.AddLooter(attacker))
            {
                raid.GetRaider(attacker).HasDestroyed = true;
            }

            if (raid.IsOpened && (IsBox(container, true) || container is BuildingPrivlidge))
            {
                raid.TryToEnd();
            }

            if (!Raids.Exists(x => x._containers.Count > 0))
            {
                Unsubscribe(nameof(OnEntityKill));
                Unsubscribe(nameof(OnEntityGroundMissing));
            }
        }

        private static bool IsLootingWeapon(HitInfo info)
        {
            if (info == null || info.damageTypes == null)
            {
                return false;
            }

            return info.damageTypes.Has(DamageType.Explosion) || info.damageTypes.Has(DamageType.Heat) || info.damageTypes.IsMeleeType() || info.WeaponPrefab is TimedExplosive;
        }

        private void OnCupboardAuthorize(BuildingPrivlidge priv, BasePlayer player)
        {
            bool isHookNeeded = false;

            foreach (var raid in Raids)
            {
                if (!raid.IsAuthed && raid.Options.RequiresCupboardAccess && raid.priv == priv)
                {
                    raid.IsAuthed = true;

                    if (config.EventMessages.AnnounceRaidUnlock)
                    {
                        foreach (var target in BasePlayer.activePlayerList)
                        {
                            if (!raid.IsRaider(target) && target.HasPermission("raidablebases.limitedannouncements")) continue;
                            raid.NotifyUnlessSmart(target, "OnRaidFinished", FormatGridReference(target, raid.Location));
                        }
                    }
                }

                if (!raid.IsAuthed)
                {
                    isHookNeeded = true;
                }
            }

            if (!isHookNeeded)
            {
                Unsubscribe(nameof(OnCupboardAuthorize));
            }
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (!Get(entity, out var raid))
            {
                return null;
            }

            if (player != null && player.IsConnected)
            {
                if (entity is BaseLadder || player.userID == entity.OwnerID)
                {
                    return true;
                }
                if (!raid.AddLooter(player))
                {
                    return raid.CanBypass(player);
                }
            }

            if (raid.IsPickupBlacklisted(entity.ShortPrefabName) || entity.Is(out DroppedItem di) && di.item != null && raid.IsPickupBlacklisted(di.item.info.shortname))
            {
                return false;
            }

            if (!raid.Options.AllowPickup && entity.OwnerID == 0 && !raid.IsPickupAllowed(entity.ShortPrefabName))
            {
                return false;
            }

            if (entity.OwnerID == 0uL)
            {
                if (TryRemoveItems(entity))
                {
                    ItemManager.DoRemoves();
                }
                if (config.BlockPaidContent && DeployableItems.TryGetValue(entity.PrefabName, out var def))
                {
                    if (entity.skinID == RB_SKIN_ID) entity.skinID = 0;
                    if (RequiresOwnership(def, 0) && !HasUnlocked(player, def)) return false;
                    if (RequiresOwnership(def, entity.skinID) && !HasUnlocked(player, entity.skinID))
                    {
                        entity.skinID = 0;
                        entity.SendNetworkUpdateImmediate();
                    }
                    return null;
                }
            }

            if (entity.skinID == RB_SKIN_ID)
            {
                entity.skinID = 0;
            }

            return null;
        }

        private void OnFireBallSpread(FireBall fire, BaseEntity spread)
        {
            if (!spread.IsKilled() && Get(spread.transform.position, out var raid) && !raid.Options.Eco.CanSpread(spread))
            {
                spread.DelayedSafeKill();
            }
        }

        private void OnFireBallDamage(FireBall fire, BaseCombatEntity target, HitInfo info)
        {
            if (info != null && (info.Initiator == null || info.Initiator is FireBall) && !fire.IsKilled() && EventTerritory(fire.transform.position))
            {
                info.Initiator = fire.creatorEntity;
            }
        }

        private object CanMlrsTargetLocation(MLRS mlrs, BasePlayer player)
        {
            return Get(mlrs.TrueHitPos, out var raid, 25f) ? raid.Options.MLRS : (object)null;
        }

        private object OnMlrsFire(MLRS mlrs, BasePlayer player)
        {
            if (!Get(mlrs.TrueHitPos, out var raid, 25f) || raid.Options.MLRS) return null;
            SendNotification(player, "MLRS Target Denied");
            return true;
        }

        private object OnNearbyTurretsScan(AutoTurret turret)
        {
            return OnInterferenceUpdate(turret);
        }

        private object OnInterferenceUpdate(AutoTurret turret)
        {
            if (turret == null || turret.IsDestroyed) return null;
            if (!Get(turret.transform.position, out var raid)) return null;
            if (turret.skinID == RB_SKIN_ID || !turret.enableSaving) return true;
            return turret.OwnerID.IsSteamId() ? (object)true : null;
        }

        private void OnEntitySpawned(TimedExplosive te)
        {
            if (te.IsKilled())
            {
                return;
            }
            var rocket = te as MLRSRocket;
            if (rocket != null)
            {
                OnEntitySpawnedMLRS(rocket);
                return;
            }
            if (te.creatorEntity != null || !Get(te.transform.position, out var raid))
            {
                return;
            }

            Vector3 position = te.transform.position;
            AutoTurret nearestTurret = null;
            AutoTurret muzzleTurret = null;
            BaseLauncher baseLauncher = null;
            float nearestSqrDistance = 9f;
            float nearestMuzzleSqrDistance = 9f;

            foreach (var info in raid.turrets.Values)
            {
                if (info.IsKilled())
                {
                    continue;
                }

                var turret = info.turret;
                float sqrDistance = (turret.transform.position - position).sqrMagnitude;
                if (sqrDistance < nearestSqrDistance)
                {
                    nearestSqrDistance = sqrDistance;
                    nearestTurret = turret;
                }

                if (!turret.GetAttachedWeapon().Is(out BaseLauncher launcher) || launcher.MuzzlePoint == null || !IsLauncherProjectile(launcher, te))
                {
                    continue;
                }

                float muzzleSqrDistance = (launcher.MuzzlePoint.position - position).sqrMagnitude;
                if (muzzleSqrDistance < nearestMuzzleSqrDistance)
                {
                    nearestMuzzleSqrDistance = muzzleSqrDistance;
                    muzzleTurret = turret;
                    baseLauncher = launcher;
                }
            }

            AutoTurret source = muzzleTurret ?? nearestTurret;
            if (source == null)
            {
                return;
            }

            te.creatorEntity = source;
            if (IsRocketLauncher(baseLauncher))
            {
                FixTurretRocket(muzzleTurret, baseLauncher, te);
            }
        }

        private void OnEntitySpawnedMLRS(MLRSRocket rocket)
        {
            using var systems = FindEntitiesOfType<MLRS>(rocket.transform.position, 15f);
            if (systems.Count == 0 || !Get(systems[0].TrueHitPos, out var raid))
            {
                return;
            }
            BasePlayer owner = systems[0].rocketOwnerRef.Get(true) as BasePlayer;
            if (!raid.Options.MLRS)
            {
                if (owner != null) SendNotification(owner, "MLRS Target Denied");
                else raid.Notify("MLRS Target Denied");
                rocket.Invoke(rocket.SafelyKill, 0.1f);
                rocket.playerDamage?.Clear();
                rocket.damageTypes?.Clear();
            }
            else if (owner != null)
            {
                rocket.creatorEntity = owner;
                rocket.OwnerID = owner.userID;
            }
        }

        private void OnEntitySpawned(FireBall fire)
        {
            if (fire.IsKilled() || !Get(fire.transform.position, out var raid))
            {
                return;
            }
            if (raid.Options.Eco.Enabled && !raid.Options.Eco.CanSpread(fire))
            {
                fire.DelayedSafeKill();
            }
            else if (config.Settings.Management.PreventFireFromSpreading && fire.ShortPrefabName == "flamethrower_fireball" && fire.creatorEntity is BasePlayer player && !player.userID.IsSteamId())
            {
                fire.DelayedSafeKill();
            }
            else if (raid.cached_attacker != null && !(fire.creatorEntity is BasePlayer) && Time.timeAsDouble - raid.cached_attack_time < 1d && raid.raiders.ContainsKey(raid.cached_attacker_id))
            {
                fire.creatorEntity = raid.cached_attacker;
            }
            raid.cached_attacker = null;
            raid.cached_attacker_id = 0;
            raid.cached_attack_time = 0;
        }

        private List<ulong> NpcCorpse = new();
        private void OnEntitySpawned(DroppedItemContainer backpack)
        {
            if (backpack.IsKilled())
            {
                return;
            }
            backpack.Invoke(() =>
            {
                if (IsUnloading || backpack.IsDestroyed || !Get(backpack, backpack.playerSteamID, out var raid))
                {
                    return;
                }
                if (backpack.ShortPrefabName == "item_drop" || backpack.ShortPrefabName == "item_drop_buoyant")
                {
                    backpack.buryLeftoverItems = false;
                    return;
                }
                if (backpack.playerSteamID.IsSteamId())
                {
                    if (raid.CanDropRustBackpack(backpack.playerSteamID))
                    {
                        DropRustBackpack(backpack);
                    }
                    if (raid.CanDropBackpack(backpack.playerSteamID))
                    {
                        backpack.playerSteamID = 0;
                    }
                }
                else if (NpcCorpse.Remove(backpack.playerSteamID))
                {
                    backpack.skinID = RB_SKIN_ID;
                    raid.SetupEntity(backpack);
                }
            }, 0.1f);
        }

        private void OnEntitySpawned(BaseLock entity)
        {
            if (entity.IsKilled() || !Get(entity.transform.position, out var raid) || raid.IsLoading)
            {
                return;
            }
            if (entity.GetParentEntity() is StorageContainer parent && raid._containers.Contains(parent))
            {
                entity.DelayedSafeKill();
            }
        }

        private void OnEntitySpawned(PlayerCorpse corpse)
        {
            if (corpse.IsKilled() || !Get(corpse, corpse.playerSteamID, out var raid))
            {
                return;
            }

            ulong playerSteamID = corpse.playerSteamID;
            if (playerSteamID.IsSteamId())
            {
                if (HarmonyModInterface.CallHook("OnRaidablePlayerCorpseCreate", new object[] { corpse, raid.Location, raid.AllowPVP, raid.Options.Level, raid.GetOwner(), raid.GetRaiders(), raid.BaseName, raid.PlayersLootable }) != null)
                {
                    return;
                }

                if ((raid.Options.EjectBackpacks || raid.EjectBackpacksPVE) && !playerSteamID.HasPermission("reviveplayer.use"))
                {
                    if (corpse.containers.IsNullOrEmpty())
                    {
                        goto done;
                    }

                    var container = GameManager.server.CreateEntity("assets/prefabs/misc/item drop/item_drop_backpack.prefab", corpse.transform.position) as DroppedItemContainer;
                    container.maxItemCount = 48;
                    container.lootPanelName = "generic_resizable";
                    container.playerName = corpse.playerName;
                    container.playerSteamID = playerSteamID;
                    container.Spawn();

                    if (IsContainerKilled(container))
                    {
                        goto done;
                    }

                    container.TakeFrom(corpse.containers, 0f);
                    corpse.Invoke(corpse.SafelyKill, 0.0625f);

                    var player = RustCore.FindPlayerById(playerSteamID);
                    var backpack = raid.AddBackpack(container, playerSteamID, player);
                    bool canEjectBackpack = HarmonyModInterface.CallHook("OnRaidableBaseBackpackEject", new object[] { container, playerSteamID, raid.Location, raid.AllowPVP, raid.Options.Level, raid.GetOwner(), raid.GetRaiders(), raid.BaseName, raid.PlayersLootable }) == null;

                    if (canEjectBackpack && raid.EjectBackpack(backpack, raid.EjectBackpacksPVE))
                    {
                        raid.backpacks.Remove(backpack);
                        ResetToPool(ref backpack);
                    }

                    if (raid.PlayersLootable)
                    {
                        container.playerSteamID = 0;
                    }

                    return;
                }

            done:

                if (raid.CanDropRustBackpack(playerSteamID))
                {
                    DropRustBackpack(corpse);
                }

                if (raid.PlayersLootable)
                {
                    corpse.playerSteamID = 0;
                }
            }
        }

        private object CanBuild(BasePlayer player, Vector3 buildPos)
        {
            foreach (var profile in Buildings.Profiles.Values)
            {
                if (!profile.Options.CustomSpawns.PreventBuilding)
                {
                    continue;
                }
                foreach (var spawns in profile.Spawns)
                {
                    if (!spawns.Value.CanBuild(buildPos, profile.Options.ProtectionRadius(spawns.Key)))
                    {
                        SendNotification(player, "Building is blocked for spawns!");
                        return false;
                    }
                }
            }
            return null;
        }

        private object CanBuild(Planner planner, Construction construction, Construction.Target target)
        {
            var buildPos = target.entity && target.entity.transform && target.socket ? target.GetWorldPosition() : target.position;
            if (!Get(buildPos, out var raid, Mathf.Clamp(construction.bounds.size.Max() * 0.85f, 2.4f, 4f)))
            {
                return CanBuild(target.player, buildPos);
            }

            if (target.player != null && !InRange(raid.Location, target.player.transform.position, raid.ProtectionRadius - 0.6f))
            {
                SendNotification(target.player, "Building is blocked!");
                return false;
            }

            if (!raid.Options.AllowBuildingPriviledges && CupboardPrefabIDs.Contains(construction.prefabID))
            {
                SendNotification(target.player, "Cupboards are blocked!");
                return false;
            }
            else if (construction.prefabID == 2150203378)
            {
                if (!config.Settings.Management.AllowLadders || raid.Options.RequiresCupboardAccessLadders && !raid.CanBuild(target.player))
                {
                    SendNotification(target.player, "Ladders are blocked!");
                    return false;
                }
                if (raid.raiders.TryGetValue(target.player.userID, out var ri) && ri.Input != null)
                {
                    ri.Input.Restart();
                    ri.Input.TryPlace(ConstructionType.Ladder);
                }
            }
            else if (construction.fullName.Contains("/barricades/barricade."))
            {
                if (raid.Options.AllowBarricades)
                {
                    if (raid.raiders.TryGetValue(target.player.userID, out var ri) && ri.Input != null)
                    {
                        ri.Input.Restart();
                        ri.Input.TryPlace(ConstructionType.Barricade);
                    }
                }
                else
                {
                    SendNotification(target.player, "Barricades are blocked!");
                    return false;
                }
            }
            else if (!raid.Options.AllowBuilding)
            {
                var value = GetFileNameWithoutExtension(construction.fullName);
                if (value != "explosivesiegedeployable" && !raid.Options.AllowedBuildingBlockExceptions.Exists(value.Contains))
                {
                    SendNotification(target.player, "Building is blocked!");
                    return false;
                }
            }

            return null;
        }

        [HookMethod("AddLootToDifficultyProfile")]
        public bool AddLootToDifficultyProfile(string mode, List<object[]> lootObjects)
        {
            if (lootObjects == null || lootObjects.Count < 1 || !Buildings.DifficultyLootLists.TryGetValue(mode, out var lootList))
            {
                return false;
            }

            bool success = false;
            foreach (var obj in lootObjects)
            {
                if (!(obj[0] is string shortname)) continue;
                int amountMin = obj.Length > 1 && obj[1] is int v1 ? v1 : 1;
                int amountMax = obj.Length > 2 && obj[2] is int v2 ? v2 : 1;
                ulong skin = obj.Length > 3 && obj[3] is ulong v3 ? v3 : 0;
                float probability = obj.Length > 4 && obj[4] is float v4 ? v4 : 1.0f;
                string displayName = obj.Length > 5 && obj[5] is string v5 ? v5 : null;
                int stackSize = obj.Length > 6 && obj[6] is int v6 ? v6 : -1;
                string text = obj.Length > 7 && obj[7] is string v7 ? v7 : null;

                LootItem ti = new(shortname, amountMin, amountMax, skin, false, probability, stackSize, displayName, text);
                ti.InitializeArmorSlots();
                lootList.Add(ti);
                success = true;
            }

            return success;
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            if (player == null || IsVanished(player) || container == null || container.inventory == null || container.OwnerID.IsSteamId() || !Get(container, out var raid))
            {
                return;
            }

            if (player.userID.IsSteamId())
            {
                raid.IsAnyLooted = true;
            }

            bool kill = config.BlockPaidContent && config.DestroyLootedContainer && container.inventory.IsEmpty() && PaidDeployableItems.TryGetValue(container.PrefabName, out var def) && RequiresOwnership(def, container.skinID);
            if (kill)
            {
                container.Invoke(container.SafelyKill, 0.1f);
            }

            if (raid.Options.DropTimeAfterLooting <= 0 || (raid.Options.DropOnlyBoxesAndPrivileges && !IsBox(container, true) && !(container is BuildingPrivlidge)))
            {
                raid.TryToEnd();
                return;
            }

            if (container.inventory.IsEmpty() && IsBox(container, false))
            {
                if (!kill) container.Invoke(container.SafelyKill, 0.1f);
            }
            else container.Invoke(() => DropOrRemoveItems(container, raid, false, true), raid.Options.DropTimeAfterLooting);

            raid.TryToEnd();
        }

        private void OnLootEntityEnd(BasePlayer player, ContainerIOEntity container)
        {
            if (config.BlockPaidContent && config.DestroyLootedContainer && !container.IsKilled() && container.inventory.IsEmpty() && PaidDeployableItems.TryGetValue(container.PrefabName, out var def) && RequiresOwnership(def, container.skinID) && Has(container))
            {
                container.Invoke(container.SafelyKill, 0.1f);
            }
        }

        private object CanLootDroppedItemContainer(BasePlayer player, BaseEntity entity) => entity switch
        {
            _ when entity.skinID != RB_SKIN_ID || !entity.OwnerID.IsSteamId() || entity.OwnerID == player.userID || IsVanished(player) => null,
            _ when RelationshipManager.ServerInstance.playerToTeam.TryGetValue(entity.OwnerID, out var team) && team.members.Contains(player.userID) => null,
            _ when Convert.ToBoolean(Clans?.Call("IsClanMember", entity.OwnerID.ToString(), player.UserIDString)) => null,
            _ when Convert.ToBoolean(Friends?.Call("AreFriends", entity.OwnerID.ToString(), player.UserIDString)) => null,
            _ => ((Func<object>)(() => { SendNotification(player, "You do not own this loot!"); return true; }))(),
        };

        private object CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity.IsKilled()) return null;
            if (CanLootDroppedItemContainer(player, entity) != null) return true;
            return Get(entity.transform.position, out var raid) ? raid.CanLootEntityInternal(player, entity) : (object)null;
        }

        private object CanBePenalized(BasePlayer player)
        {
            return Get(player, null, out var raid) && (raid.AllowPVP && !raid.Options.PenalizePVP || !raid.AllowPVP && !raid.Options.PenalizePVE) ? false : (object)null;
        }

        private object CanOpenBackpack(BasePlayer looter, ulong backpackOwnerID)
        {
            if (!Get(looter.transform.position, out var raid))
            {
                return null;
            }

            if (!raid.AllowPVP && !config.Settings.Management.BackpacksOpenPVE || raid.AllowPVP && !config.Settings.Management.BackpacksOpenPVP)
            {
                return lang.GetMessage("NotAllowed", this, looter.UserIDString);
            }

            return null;
        }

        private bool CanDropPlayerBackpack(BasePlayer player, RaidableBase raid)
        {
            if (GetPVPDelay(player.userID, true, out DelaySettings ds) && ds.raid != null && ds.raid.CanDropBackpack(player.userID))
            {
                return true;
            }

            return InRange(raid.Location, player.transform.position, raid.ProtectionRadius) && raid.CanDropBackpack(player.userID);
        }

        private bool ShouldIgnoreFlyingPlayer(BasePlayer player)
        {
            if (!config.Settings.Management.IgnoreFlying || !player.IsFlying) return false;
            Transform t = player.transform; // if this is null, your server is fucked and needs a restart
            return t != null && EventTerritory(t.position);
        }

        private static bool IsDangerousEvent(BaseEntity entity) => entity is StorageContainer && !entity.enableSaving && entity.OwnerID == 0;

        private static bool IsSputnik(BaseEntity entity) => entity != null && entity.ShortPrefabName == "large.rechargable.battery.deployed" && entity.OwnerID == 0 && !entity.enableSaving;

        private bool IsEventEntity(BaseEntity entity, float dist, float protectionRadius) => !entity.OwnerID.IsSteamId() && dist <= protectionRadius || IsAbandonedEntity(entity);

        private bool IsAbandonedEntity(BaseEntity entity) => AbandonedBases != null && Convert.ToBoolean(AbandonedBases?.Call("isAbandoned", entity));

        private bool IsArmoredTrain(BaseEntity entity) => entity.OwnerID == 0uL && entity.Is(out AutoTurret turret) && !turret.isLootable && !turret.dropFloats && turret.parentEntity.IsSet();

        private static bool IsEventDrone(BaseEntity entity) => entity.OwnerID == 335576777746;

        private bool IsSentryTargetingNpc(BasePlayer player, BaseEntity entity) => entity is NPCAutoTurret && player.skinID != RB_SKIN_ID && !player.userID.IsSteamId();

        private bool IgnorePlayer(BasePlayer player, BaseEntity entity) => IsVanished(player) || IsSentryTargetingNpc(player, entity) || IsArmoredTrain(entity);

        private bool IsPositionInSpace(Vector3 a, Vector3 b, float r) => Space != null && a.y - b.y > r + M_RADIUS;

        private object OnEntityEnter(TriggerBase trigger, Drone drone)
        {
            if (drone == null || drone.IsDestroyed || !Get(trigger, out var raid)) return null;
            if (drone is DeliveryDrone) return true;
            return !InRange(drone.transform.position, raid.Location, raid.Options.SamSite.Range) ? true : (object)null;
        }

        private object OnEntityEnter(TriggerBase trigger, BasePlayer player)
        {
            if (trigger == null || player.IsKilled()) return null;
            if (ShouldIgnoreFlyingPlayer(player)) return true;
            if (Has(player) && (Has(trigger) || (Get(player.userID, out HumanoidBrain brain) && brain.raid.Options.NPC.IgnorePlayerTrapsTurrets))) return true;
            BaseEntity entity = Get(trigger, out var raid) ? raid.triggers[trigger] : (trigger is TriggerParent p ? p.Entity : trigger.gameObject.ToBaseEntity());
            if (IsProtectedScientist(player, entity)) return true;
            return CanEntityBeTargetedInternal(player, entity, IsPVE()) is true or null ? (object)null : true;
        }

        private bool _subscribeOnEntityEnterHopper = true;
        private object OnEntityEnter(TriggerEnterTimer trigger, BaseEntity target)
        {
            if (!_subscribeOnEntityEnterHopper || trigger == null) return null;
            Hopper hopper = trigger.gameObject.ToBaseEntity() as Hopper;
            return CanEntityBeTargetedInternal(target, hopper) is bool val && !val ? true : (object)null;
        }

        private object CanEntityBeTargeted(BaseEntity target, Hopper hopper)
        {
            _subscribeOnEntityEnterHopper = false;
            return CanEntityBeTargetedInternal(target, hopper) is bool val ? val : (object)null;
        }

        private object CanEntityBeTargetedInternal(BaseEntity target, Hopper hopper)
        {
            if (target.IsKilled() || hopper.IsKilled())
            {
                return null;
            }

            if (!Get(target.transform.position, out var raid) && !Get(hopper.transform.position, out raid))
            {
                return null;
            }

            if (hopper.OwnerID == 0 && raid.Has(hopper))
            {
                return true;
            }

            if (hopper.OwnerID != 0 && !InRange(hopper.transform.position, raid.Location, raid.ProtectionRadius))
            {
                return false;
            }

            DroppedItem di = target as DroppedItem;
            if (di != null)
            {
                return raid.AllowPVP || di.DroppedBy == 0 || di.DroppedBy == hopper.OwnerID || raid.raiders.TryGetValue(di.DroppedBy, out var raider) && raid.IsAlly(raider, hopper.OwnerID);
            }

            PlayerCorpse corpse = target as PlayerCorpse;
            if (corpse != null)
            {
                return raid.AllowPVP || corpse.playerSteamID == hopper.OwnerID || raid.raiders.TryGetValue(corpse.playerSteamID, out var raider) && raid.IsAlly(raider, hopper.OwnerID);
            }

            return null;
        }

        private object CanEntityBeTargeted(BasePlayer player, BaseEntity entity)
        {
            if (player.IsKilled()) return null;
            return CanEntityBeTargetedInternal(player, entity, false);
        }

        private object CanEntityBeTargetedInternal(BasePlayer player, BaseEntity entity, bool earlyExit)
        {
            if (entity.IsKilled() || IgnorePlayer(player, entity))
            {
                return null;
            }

            if (!Get(player.transform.position, out var raid) && !Get(entity.transform.position, out raid))
            {
                return null;
            }

            if (earlyExit && (!raid.Options.BlockOutsideDamageToPlayersInside && !raid.Options.NPC.BlockOutsideDamageToNpcsInside))
            {
                return null;
            }

            if (Has(player))
            {
                if (entity.skinID == 3358068268)
                {
                    return null;
                }
                AutoTurret turret = entity as AutoTurret;
                if (entity.OwnerID.IsSteamId() ? raid.Options.NPC.IgnorePlayerTrapsTurrets : raid.Options.NPC.IgnoreTrapsTurrets)
                {
                    if (turret != null)
                    {
                        turret.SetNoTarget();
                        return null;
                    }
                    return false;
                }
                if (raid.Options.NPC.BlockOutsideDamageToNpcsInside && Has(player) && CanBlockOutsideDamage(raid, entity) && InRange(player.transform.position, raid.Location, raid.ProtectionRadius))
                {
                    if (turret != null)
                    {
                        turret.SetNoTarget();
                        return null;
                    }
                    return false;
                }
                return entity.OwnerID.IsSteamId() ? !raid.Options.NPC.IgnorePlayerTrapsTurrets : !Has(entity);
            }

            if (player.IsHuman())
            {
                AutoTurret turret = entity as AutoTurret;
                if (raid.Options.BlockOutsideDamageToPlayersInside && entity.skinID != RB_SKIN_ID && CanBlockOutsideDamage(raid, entity))
                {
                    if (turret != null)
                    {
                        turret.SetNoTarget();
                        return null;
                    }
                    return false;
                }
                if (turret != null)
                {
                    var success = raid.OnTurretTarget(turret, player);
                    if (success == DamageResult.None) return null;
                    if (success == DamageResult.Blocked) return false;
                }
                return entity.skinID == RB_SKIN_ID || entity is BaseDetector || HasPVPDelay(player.userID);
            }

            return IsEventDrone(entity) ? (object)null : entity.OwnerID.IsSteamId() ? !raid.Options.NPC.IgnorePlayerTrapsTurrets : !raid.Options.NPC.IgnoreTrapsTurrets;
        }

        private object CanEntityBeTargeted(BaseEntity entity, SamSite ss)
        {
            if (entity.IsKilled() || ss.IsKilled())
            {
                return null;
            }

            if (Get(ss.transform.position, out var raid) && !IsPositionInSpace(entity.transform.position, raid.Location, raid.ProtectionRadius))
            {
                if (raid.IsLoading || entity.skinID == RB_SKIN_ID && ss.skinID == RB_SKIN_ID)
                {
                    return false;
                }
                return (entity.transform.position - ss.transform.position).sqrMagnitude <= raid.Options.SamSite.Range * raid.Options.SamSite.Range;
            }

            return null;
        }

        private object OnSamSiteTargetScan(SamSite ss, List<SamSite.ISamSiteTarget> obj)
        {
            if (ss.IsKilled())
            {
                return null;
            }
            var a = ss.transform.position;
            if (!Get(a, out var raid))
            {
                return null;
            }
            if (!raid.IsLoading)
            {
                var sqrDistance = raid.Options.SamSite.Range * raid.Options.SamSite.Range;
                foreach (SamSite.ISamSiteTarget server in SamSite.ISamSiteTarget.serverList)
                {
                    if (server == null)
                    {
                        continue;
                    }
                    BaseEntity entity = server as BaseEntity;
                    if (entity == null || entity.IsDestroyed)
                    {
                        continue;
                    }
                    var b = server.CenterPoint();
                    var isValidTarget = server is MLRSRocket || (entity.skinID != RB_SKIN_ID && !ss.IsInDefenderMode() && !IsPositionInSpace(b, raid.Location, raid.ProtectionRadius));
                    if (isValidTarget && (a - b).sqrMagnitude <= sqrDistance)
                    {
                        obj.Add(server);
                    }
                }
                if (raid.Options.SamSite.Repair > 0f && ss.staticRespawn && obj.Count > 0f)
                {
                    ss.staticRespawn = false;
                    ss.Invoke(() => ss.staticRespawn = true, 0.1f);
                }
            }

            return true;
        }

        private object OnTrapTrigger(BaseTrap trap, GameObject go)
        {
            var player = go.GetComponent<BasePlayer>();
            var success = CanEntityTrapTrigger(trap, player);

            return success is bool val && !val ? true : (object)null;
        }

        private object CanEntityTrapTrigger(BaseTrap trap, BasePlayer player)
        {
            if (player == null || IsVanished(player))
            {
                return null;
            }

            if (Has(player))
            {
                return false;
            }

            if (!Get(trap, out var raid))
            {
                return null;
            }

            if (raid.Options.RearmBearTraps && trap is BearTrap)
            {
                trap.Invoke(trap.Arm, 0.1f);
            }

            return true;
        }

        private void OnCupboardProtectionCalculated(BuildingPrivlidge priv, float cachedProtectedMinutes)
        {
            if (priv.OwnerID == 0 && Has(priv))
            {
                priv.cachedProtectedMinutes = 1500;
            }
        }

        private static bool BlockDamage(HitInfo info)
        {
            if (info.Weapon is BlowPipeWeapon)
            {
                info.HitEntity = null;
            }

            return NullifyDamage(info);
        }

        public struct DamageContext
        {
            public BaseCombatEntity Entity;
            public HitInfo Info;
            public DamageType DamageType;
            public RaidableBase Raid;
            public BasePlayer Attacker;
            public bool IsHuman;

            public DamageContext(BaseCombatEntity entity, HitInfo info)
            {
                Entity = entity;
                Info = info;
                DamageType = info.damageTypes.GetMajorityDamageType();
                Raid = null;
                Attacker = null;
                IsHuman = false;
            }
        }

        private object CanRaidWindowBlockDamage(BaseCombatEntity victim, HitInfo info)
        {
            return Has(victim) ? false : null;
        }

        private object CanEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || entity == null || entity.IsDestroyed || entity.OwnerID == 1337422)
            {
                return null;
            }

            if (info.Initiator?.OwnerID is 1309 or 13099 or 8002738255 or 335576777746)
            {
                return null;
            }

            DamageContext context = new(entity, info);
            DamageResult result = entity.Is(out BasePlayer victim) ? EvaluatePlayerDamage(victim, ref context) : EvaluateEntityDamage(ref context);

            if (result == DamageResult.None)
            {
                return null;
            }

            if (result == DamageResult.Blocked)
            {
                return BlockDamage(context.Info);
            }

            if (context.Raid != null)
            {
                bool raidEntity = context.Entity is not BasePlayer && Has(context.Entity);
                bool checkItemRestrictions = raidEntity && (context.IsHuman || context.Attacker == null);

                if (checkItemRestrictions && context.Raid.Options.RestrictByAcceptedItems() && !context.Raid.IsWeaponAllowedByAcceptedItems(context.Attacker, context.Info))
                {
                    if (context.IsHuman)
                    {
                        NotifyOnce(context.Attacker, "ItemNotAccepted");
                    }

                    return NullifyDamage(context.Info);
                }

                if (checkItemRestrictions && context.Raid.Options.RestrictByWorkbenchLevel(MaxConsideredWorkbenchLevel) && !context.Raid.IsWeaponAllowedByWorkbenchLevel(context.Attacker, context.Info))
                {
                    if (context.IsHuman)
                    {
                        NotifyOnce(context.Attacker, "WorkbenchLevelReq", context.Raid.Options.WorkbenchLevel);
                    }

                    return NullifyDamage(context.Info);
                }

                if (context.IsHuman && context.DamageType != DamageType.Heat)
                {
                    context.Raid.CreateSpheres();
                    context.Raid.GetRaider(context.Attacker).lastActiveTime = Time.timeAsDouble;
                }
            }

            return true;
        }

        protected void UnsubscribeDamageHook()
        {
            if (Raids.Count > 0 || config == null || (config.Settings.Management.PVPDelayPersists && PvpDelay.Count > 0))
            {
                return;
            }

            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(CanEntityTakeDamage));
        }

        private void SubscribeDamageHook()
        {
            if (IsPVE())
            {
                Unsubscribe(nameof(OnEntityTakeDamage));
                Subscribe(nameof(CanEntityTakeDamage));
            }
            else
            {
                Unsubscribe(nameof(CanEntityTakeDamage));
                Subscribe(nameof(OnEntityTakeDamage));
            }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info) => CanEntityTakeDamage(entity, info);

        private DamageResult EvaluatePlayerDamage(BasePlayer victim, ref DamageContext context)
        {
            BaseEntity initiator = context.Info.Initiator;
            bool got = Get(victim, context.Info, out var raid);
            context.Raid = raid;

            if (!got || raid.IsDespawning)
            {
                if (config.Settings.Management.PVPDelayPersists && initiator is BasePlayer other && HasPVPDelay(other.userID) && HasPVPDelay(victim.userID))
                {
                    return DamageResult.Allowed;
                }

                return DamageResult.None;
            }

            bool hasVictim = Has(victim);

            if (context.Info.WeaponPrefab is MLRSRocket)
            {
                bool allowed = (raid.AllowPVP || hasVictim) && raid.Options.MLRS && initiator?.OwnerID != 13099;
                return allowed ? DamageResult.Allowed : DamageResult.Blocked;
            }

            if (IsHelicopter(context.Info, out bool eventHelicopter))
            {
                return eventHelicopter ? DamageResult.None : DamageResult.Allowed;
            }

            if (hasVictim && initiator?.OwnerID == 0uL && !initiator.enableSaving && Has(initiator))
            {
                context.Info.damageTypes.Clear();
                return DamageResult.None;
            }

            if (IsTrueDamage(initiator, raid.IsProtectedWeapon(initiator)))
            {
                return EvaluateTrueDamage(raid, context.Info, initiator, victim, hasVictim);
            }

            if (!raid.GetInitiatorPlayer(context.Info, context.DamageType, victim, out var attacker))
            {
                return hasVictim ? DamageResult.Blocked : DamageResult.None;
            }

            context.Attacker = attacker;
            return EvaluateAttackerDamage(attacker, victim, ref context);
        }

        private DamageResult EvaluateTrueDamage(RaidableBase raid, HitInfo info, BaseEntity weapon, BasePlayer victim, bool raidVictim)
        {
            if (victim is ScientistNPC && !raidVictim)
            {
                return DamageResult.None;
            }

            if (raidVictim && raid.Options.NPC.BlockOutsideDamageToNpcsInside && CanBlockOutsideDamage(raid, weapon) && InRange(victim.transform.position, raid.Location, raid.ProtectionRadius))
            {
                return DamageResult.Blocked;
            }

            if (weapon is not AutoTurret turret)
            {
                return DamageResult.Allowed;
            }

            float min, max;
            if (victim.userID.IsSteamId())
            {
                min = raid.Options.AutoTurret.Min;
                max = raid.Options.AutoTurret.Max;
            }
            else
            {
                min = raid.Options.AutoTurret.NpcMin;
                max = raid.Options.AutoTurret.NpcMax;
            }

            if (min != 1f || max != 1f)
            {
                info.damageTypes.Scale(DamageType.Bullet, UnityEngine.Random.Range(min, max));
            }

            bool ignorePlayerTurret = raidVictim && raid.Options.NPC.IgnorePlayerTrapsTurrets && weapon.OwnerID.IsSteamId();
            bool isRaidTurret = raidVictim && weapon.OwnerID == 0uL && weapon.skinID == RB_SKIN_ID;

            if (ignorePlayerTurret || isRaidTurret)
            {
                if (turret.target == victim)
                {
                    turret.SetNoTarget();
                    return DamageResult.None;
                }

                return DamageResult.Blocked;
            }

            if (weapon.OwnerID.IsSteamId())
            {
                if (!victim.IsHuman())
                {
                    return DamageResult.Allowed;
                }

                if (InRange2D(weapon.transform.position, raid.Location, raid.ProtectionRadius))
                {
                    return raid.AllowPVP ? DamageResult.Allowed : DamageResult.Blocked;
                }
            }

            return raid.OnTurretTarget(turret, victim);
        }

        private DamageResult EvaluateAttackerDamage(BasePlayer attacker, BasePlayer victim, ref DamageContext context)
        {
            RaidableBase raid = context.Raid;
            context.IsHuman = attacker.IsHuman();

            bool raidAttacker = Has(attacker);
            bool raidVictim = Has(victim);

            if (!context.IsHuman && raidAttacker && raidVictim)
            {
                return DamageResult.Blocked;
            }

            if (attacker.userID == victim.userID)
            {
                return raid.Options.AllowSelfDamage ? DamageResult.Allowed : DamageResult.Blocked;
            }

            if (HasPVPDelay(victim.userID))
            {
                if (!raid.Options.AllowFriendlyFire && raid.IsAlly(attacker, victim))
                {
                    return DamageResult.Blocked;
                }

                if (EventTerritory(attacker.transform.position))
                {
                    raid.SetPVPDelay(attacker, context.DamageType == DamageType.Heat);
                    return DamageResult.Allowed;
                }

                if (config.Settings.Management.PVPDelayAnywhere && HasPVPDelay(attacker.userID))
                {
                    return DamageResult.Allowed;
                }
            }

            if (config.Settings.Management.PVPDelayDamageInside && HasPVPDelay(attacker.userID) && InRange2D(raid.Location, victim.transform.position, raid.ProtectionRadius))
            {
                return DamageResult.Allowed;
            }

            if (context.IsHuman)
            {
                return victim.IsHuman()
                    ? EvaluatePvpDamage(raid, victim, attacker, context.Info, context.DamageType)
                    : EvaluateHumanToNpcDamage(raid, victim, attacker, context.Info, raidVictim);
            }

            return raidAttacker ? EvaluateRaidNpcDamage(raid, victim, attacker, context.Info, context.DamageType, raidVictim) : DamageResult.None;
        }

        private DamageResult EvaluateHumanToNpcDamage(RaidableBase raid, BasePlayer victim, BasePlayer attacker, HitInfo info, bool raidVictim)
        {
            if (!raidVictim || !HumanoidBrains.TryGetValue(victim.userID, out var brain))
            {
                return DamageResult.Allowed;
            }

            if (config.Settings.Management.BlockMounts)
            {
                if (raid.IsMounted(attacker, raid.Options.Siege.Only || !config.Settings.Management.BlockSiegeMounts))
                {
                    return DamageResult.Blocked;
                }

                BaseEntity parent = attacker.HasParent() ? attacker.GetParentEntity() : null;

                if (parent is BaseHelicopter || parent is HotAirBalloon)
                {
                    return DamageResult.Blocked;
                }
            }

            if (raid.Options.NPC.BlockOutsideDamageToNpcsInside && brain.AttackTarget != attacker && CanBlockOutsideDamage(raid, attacker) && InRange(victim.transform.position, raid.Location, raid.ProtectionRadius))
            {
                return DamageResult.Blocked;
            }

            if (!raid.Options.NPC.CanLeave && raid.Options.NPC.BlockOutsideDamageOnLeave && !InRange(attacker.transform.position, raid.Location, raid.ProtectionRadius) && InRange(victim.transform.position, raid.Location, raid.ProtectionRadius))
            {
                brain.Forget();

                if (!victim.IsDead())
                {
                    victim.Heal(victim.MaxHealth());
                }

                return DamageResult.Blocked;
            }

            ApplyMaxEffectiveRangeMultiplier(raid.Options.NPC.PlayerMaxEffectiveRange, raid.SqrProtectionRadius, attacker.transform.position, info, brain);

            if (victim.HasPlayerFlag(BasePlayer.PlayerFlags.Sleeping))
            {
                if (raid.Options.NPC.Inside.Sleepers.IsUnwakeable)
                {
                    return DamageResult.Allowed;
                }

                brain.SetSleeping(false);
            }

            brain.SetTarget(attacker);
            return DamageResult.Allowed;
        }

        private DamageResult EvaluatePvpDamage(RaidableBase raid, BasePlayer victim, BasePlayer attacker, HitInfo info, DamageType damageType)
        {
            if (playerDelayExclusions.Count > 1 && HasDelayExclusion(victim.userID) && HasDelayExclusion(attacker.userID))
            {
                return DamageResult.Allowed;
            }

            if (raid.HasLockout(attacker, damageType != DamageType.Heat))
            {
                return DamageResult.Blocked;
            }

            if (raid.Options.BlockOutsideDamageToPlayersInside && CanBlockOutsideDamage(raid, attacker) && info.WeaponPrefab is not MLRSRocket)
            {
                if (config.EventMessages.NoDamageFromOutsideToPlayersInside && damageType != DamageType.Heat)
                {
                    NotifyOnce(attacker, "NoDamageFromOutsideToPlayersInside");
                }

                return DamageResult.Blocked;
            }

            if (IsPVE() &&
                (!InRange(attacker.transform.position, raid.Location, raid.ProtectionRadius) ||
                !InRange(victim.transform.position, raid.Location, raid.ProtectionRadius)))
            {
                return DamageResult.Blocked;
            }

            if (raid.IsAlly(attacker, victim))
            {
                return raid.Options.AllowFriendlyFire ? DamageResult.Allowed : DamageResult.Blocked;
            }

            if (!raid.AllowPVP)
            {
                return DamageResult.Blocked;
            }

            raid.SetPVPDelay(attacker, damageType == DamageType.Heat);
            return DamageResult.Allowed;
        }

        private DamageResult EvaluateRaidNpcDamage(RaidableBase raid, BasePlayer victim, BasePlayer attacker, HitInfo info, DamageType damageType, bool raidVictim)
        {
            if (!HumanoidBrains.TryGetValue(attacker.userID, out var brain))
            {
                return DamageResult.Allowed;
            }

            if (raidVictim)
            {
                return DamageResult.Blocked;
            }

            if (raid.Options.BlockNpcDamageToPlayersOutside && CanBlockOutsideDamage(raid, victim))
            {
                return victim.userID.IsSteamId() ? DamageResult.Blocked : DamageResult.None;
            }

            if (brain.attackType == HumanoidBrain.AttackType.BaseProjectile && brain.baseProjectile != null && UnityEngine.Random.Range(0f, 100f) > raid.Options.NPC.Accuracy.Get(brain))
            {
                return victim.userID.IsSteamId() ? DamageResult.Blocked : DamageResult.None;
            }

            ApplyMaxEffectiveRangeMultiplier(raid.Options.NPC.NpcMaxEffectiveRange, raid.SqrProtectionRadius, victim.transform.position, info, brain);

            if (damageType == DamageType.Explosion)
            {
                info.UseProtection = false;
            }

            var opt = raid.Options.NPC.Multipliers;
            var m = brain.attackType switch { HumanoidBrain.AttackType.BaseProjectile => opt.ProjectileDamageMultiplier, HumanoidBrain.AttackType.Explosive => opt.ExplosiveDamageMultiplier, HumanoidBrain.AttackType.Melee => opt.MeleeDamageMultiplier, _ => 1f };
            if (m != 1f) info.damageTypes.ScaleAll(m);

            return DamageResult.Allowed;
        }

        private DamageResult EvaluateEntityDamage(ref DamageContext context)
        {
            BaseCombatEntity entity = context.Entity;
            HitInfo info = context.Info;

            if (info.Initiator is SamSite ss)
            {
                return ss.skinID == RB_SKIN_ID ? DamageResult.Allowed : DamageResult.None;
            }

            if (!Get(entity.transform.position, out var raid))
            {
                return DamageResult.None;
            }

            context.Raid = raid;

            if (!CanEventTurretDamage(info, raid, entity))
            {
                return DamageResult.None;
            }

            if (IsHelicopter(info, out bool eventHelicopter))
            {
                if (config.Settings.Management.BlockHelicopterDamage && entity.OwnerID == 0uL)
                {
                    info.damageTypes.Clear();
                }

                return eventHelicopter ? DamageResult.None : DamageResult.Allowed;
            }

            bool hasAttacker = raid.GetInitiatorPlayer(info, context.DamageType, entity, out var attacker);
            context.Attacker = attacker;
            context.IsHuman = hasAttacker && attacker.IsHuman();

            if (raid.IsDespawning)
            {
                return hasAttacker ? DamageResult.None : DamageResult.Allowed;
            }

            if (context.IsHuman && entity.OwnerID == 0uL && raid.Type != RaidableType.None)
            {
                raid.IsEngaged = true;
                raid.CheckDespawn();
            }

            if (info.Initiator != null && info.Initiator.skinID == RB_SKIN_ID && entity.skinID == RB_SKIN_ID)
            {
                info.damageTypes.Clear();
                return DamageResult.None;
            }

            ApplyRaidEntityDamageMultipliers(ref context, hasAttacker);

            if (entity.Is(out BearTrap trap))
            {
                if (raid.Options.BearTrapsImmuneToExplosives && info.WeaponPrefab is TimedExplosive)
                {
                    info.damageTypes.Clear();
                }

                if (raid.Options.RearmBearTraps)
                {
                    trap.Invoke(trap.Arm, 0.1f);
                }
            }

            if (raid.IsDamageBlocked(entity) || (!raid.Options.MLRS && info.WeaponPrefab is MLRSRocket))
            {
                return DamageResult.Blocked;
            }

            if (context.DamageType == DamageType.Decay)
            {
                if (entity.OwnerID == 0uL && !entity.enableSaving && Has(entity))
                {
                    return DamageResult.Blocked;
                }
            }
            else if (raid.IsLoading || entity is DroppedItemContainer)
            {
                return DamageResult.Blocked;
            }

            if (entity.IsNpc || entity is PlayerCorpse)
            {
                return DamageResult.Allowed;
            }

            if (entity.Is(out BuildingBlock block))
            {
                DamageResult result = EvaluateBuildingBlockDamage(block, raid);

                if (result != DamageResult.None)
                {
                    return result;
                }
            }
            else if (raid.IsMountable(entity))
            {
                DamageResult result = EvaluateMountableDamage(entity, info, raid, context.IsHuman, attacker);

                if (result != DamageResult.None)
                {
                    return result;
                }
            }

            if (!entity.IsValid())
            {
                return DamageResult.None;
            }

            bool builtEntity = raid.BuiltList.Contains(entity);

            if (!builtEntity && !raid.Has(entity))
            {
                return DamageResult.None;
            }

            if (info.WeaponPrefab is TimedExplosive && info.WeaponPrefab.ShortPrefabName == "torpedostraight")
            {
                info.damageTypes.ScaleAll(UnityEngine.Random.Range(raid.Options.Water.TorpedoMin, raid.Options.Water.TorpedoMax));
            }

            if (!attacker.IsValid())
            {
                BaseEntity initiator = info.Initiator;
                bool allowUnknownAttacker = initiator.IsNull() || (initiator.OwnerID == 0uL && Has(initiator)) || IsLootingWeapon(info);

                return allowUnknownAttacker ? DamageResult.Allowed : DamageResult.None;
            }

            if (!context.IsHuman)
            {
                return EvaluateNonHumanDamageToEntity(entity, raid, attacker, info);
            }

            return EvaluateHumanDamageToEntity(entity, raid, attacker, info, context.DamageType, builtEntity);
        }

        private bool CanEventTurretDamage(HitInfo info, RaidableBase raid, BaseCombatEntity entity)
        {
            if (entity.OwnerID != 0uL || entity.enableSaving || info.Initiator.IsKilled() || info.Initiator.skinID != RB_SKIN_ID)
            {
                return true;
            }

            if (info.Initiator is not AutoTurret)
            {
                return true;
            }

            if (entity.Is(out BuildingBlock block) && block.grade == BuildingGrade.Enum.Twigs)
            {
                // Do not redirect turret damage through twig onto the player — that
                // made raid turrets effectively shoot through walls.
                if (raid.Options.TurretsHurtTwig)
                {
                    return true;
                }
            }

            info.damageTypes.Clear();
            return false;
        }

        private void ApplyMaxEffectiveRangeMultiplier(float maxEffectiveRange, float sqrProtectionRadius, Vector3 position, HitInfo info, HumanoidBrain brain)
        {
            if (!(maxEffectiveRange > 0f))
            {
                return;
            }

            float distanceSquared = (position - brain.ServerPosition).sqrMagnitude;
            if (!(distanceSquared > sqrProtectionRadius))
            {
                return;
            }

            bool outsideEffectiveRange = distanceSquared > maxEffectiveRange * maxEffectiveRange;
            info.damageTypes.ScaleAll(outsideEffectiveRange ? 0f : 1f - Mathf.Sqrt(distanceSquared) / maxEffectiveRange);
        }

        private void ApplyRaidEntityDamageMultipliers(ref DamageContext context, bool hasAttacker)
        {
            RaidableBase raid = context.Raid;

            if (hasAttacker ? context.IsHuman : context.DamageType == DamageType.Heat)
            {
                if (raid.PlayerDamageMultiplier.Count > 0)
                {
                    foreach (var multiplier in raid.PlayerDamageMultiplier)
                    {
                        context.Info.damageTypes.Scale(multiplier.index, multiplier.amount);
                    }
                }

                if (raid.Options.PlayerDamageMultiplierTC != 1f && context.Info.HitEntity is BuildingPrivlidge)
                {
                    context.Info.damageTypes.ScaleAll(raid.Options.PlayerDamageMultiplierTC);
                }
            }

            if (!raid.Options.Siege.Disabled)
            {
                raid.Options.Siege.Scale(context);
            }
        }

        private DamageResult EvaluateBuildingBlockDamage(BuildingBlock block, RaidableBase raid)
        {
            if (raid.Options.Setup.FoundationsImmune || (raid.Options.Setup.FoundationsImmuneForcedHeight && raid.Options.Setup.ForcedHeight != -1))
            {
                if (raid.foundations.Count > 0 && raid.IsFoundation(block))
                {
                    return DamageResult.Blocked;
                }

                if (raid.FloorsAreFoundations && block.ShortPrefabName.StartsWith("floor") && block.transform.position.y - raid.Location.y <= 3f)
                {
                    return DamageResult.Blocked;
                }
            }

            if (block.OwnerID == 0uL)
            {
                if (raid.Options.TwigImmune && block.grade == BuildingGrade.Enum.Twigs)
                {
                    return DamageResult.Blocked;
                }

                if (raid.Options.BlocksImmune)
                {
                    return block.grade == BuildingGrade.Enum.Twigs ? DamageResult.Allowed : DamageResult.Blocked;
                }
            }

            return block.grade == BuildingGrade.Enum.Twigs ? DamageResult.Allowed : DamageResult.None;
        }

        private DamageResult EvaluateMountableDamage(BaseEntity entity, HitInfo info, RaidableBase raid, bool isHuman, BasePlayer attacker)
        {
            if (config.Settings.Management.MiniCollision && entity is Minicopter && entity == info.Initiator)
            {
                return DamageResult.Blocked;
            }

            if (isHuman && !ExcludedMountsExists(entity.ShortPrefabName))
            {
                if (entity.Is(out BaseMountable mountable))
                {
                    BaseVehicle vehicle = mountable.HasParent() ? mountable.VehicleParent() : mountable as BaseVehicle;

                    if (vehicle != null && vehicle.GetDriver() == attacker)
                    {
                        return config.Settings.Management.MountDamageFromPlayers ? DamageResult.Allowed : DamageResult.Blocked;
                    }
                }

                if (!config.Settings.Management.MountDamageFromPlayers)
                {
                    NotifyOnce(attacker, "NoMountedDamageTo");
                    return DamageResult.Blocked;
                }

                if (config.Settings.Management.BlockMounts &&
                    raid.IsMounted(attacker, raid.Options.Siege.Only || !config.Settings.Management.BlockSiegeMounts))
                {
                    NotifyOnce(attacker, "NoMountedDamageFrom");
                    return DamageResult.Blocked;
                }

                if (raid.Options.BlockOutsideDamageToBaseInside && CanBlockOutsideDamage(raid, attacker) && info.WeaponPrefab is not MLRSRocket)
                {
                    NotifyOnce(attacker, "NoDamageFromOutsideToBaseInside");
                    return DamageResult.Blocked;
                }
            }

            if (info.Initiator == entity)
            {
                bool allowDamage = config.Settings.Management.MountDamageFromPlayers || entity is BatteringRam or BatteringRamHead;
                return allowDamage ? DamageResult.Allowed : DamageResult.Blocked;
            }

            return DamageResult.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ExcludedMountsExists(string prefabName)
        {
            foreach (string prefix in ExcludedMounts)
            {
                if (prefabName.StartsWith(prefix))
                {
                    return true;
                }
            }

            return false;
        }

        private DamageResult EvaluateNonHumanDamageToEntity(BaseCombatEntity entity, RaidableBase raid, BasePlayer attacker, HitInfo info)
        {
            if (entity.OwnerID == 0uL && !raid.Options.RaidingNpcs && !Has(attacker))
            {
                info.damageTypes.Clear();
                return DamageResult.None;
            }

            if (entity.OwnerID == 0uL && Has(attacker) && (info.damageTypes.Has(DamageType.Explosion) || info.WeaponPrefab is TimedExplosive) && entity is not BasePlayer)
            {
                return DamageResult.Blocked;
            }

            return DamageResult.Allowed;
        }

        private DamageResult EvaluateHumanDamageToEntity(BaseCombatEntity entity, RaidableBase raid, BasePlayer attacker, HitInfo info, DamageType damageType, bool builtEntity)
        {
            if (info.IsProjectile())
            {
                raid.cached_attacker = attacker;
                raid.cached_attack_time = Time.timeAsDouble;
                raid.cached_attacker_id = attacker.userID;
            }

            entity.lastAttacker = attacker;
            attacker.lastDealtDamageTime = Time.time;

            if (raid.Options.Eco.Enabled && !raid.IsEcoTool(attacker, info))
            {
                NotifyOnce(attacker, "EcoOnly");
                return DamageResult.Blocked;
            }

            if (raid.Options.Siege.Only && !raid.Options.Siege.IsSiegeTool(attacker, info, damageType))
            {
                NotifyOnce(attacker, "PrimitiveOnly");
                return DamageResult.Blocked;
            }

            if (config.Settings.Management.BlockMounts && raid.IsMounted(attacker, raid.Options.Siege.Only || !config.Settings.Management.BlockSiegeMounts))
            {
                NotifyOnce(attacker, "NoMountedDamageFrom");
                return DamageResult.Blocked;
            }

            if (raid.Options.BlockOutsideDamageToBaseInside && CanBlockOutsideDamage(raid, attacker) && info.WeaponPrefab is not MLRSRocket)
            {
                NotifyOnce(attacker, "NoDamageFromOutsideToBaseInside");
                return DamageResult.Blocked;
            }

            if (raid.ID.Length == 17 && IsBox(entity, false) && (attacker.UserIDString == raid.ID || raid.IsAlly(attacker, Convert.ToUInt64(raid.ID)))) // intentionally coded this way to make it difficult to understand (private plugin uses raid.ID)
            {
                return DamageResult.Blocked;
            }

            if (raid.ownerId.IsSteamId() && raid.CanEjectEnemy() && !raid.IsAlly(attacker))
            {
                NotifyOnce(attacker, "NoDamageToEnemyBase");
                return DamageResult.Blocked;
            }

            if (raid.HasLockout(attacker, damageType != DamageType.Heat))
            {
                return DamageResult.Blocked;
            }

            if (raid.Options.AutoTurret.AutoAdjust && entity.skinID == RB_SKIN_ID && entity.Is(out AutoTurret turret) && turret.sightRange < raid.Options.AutoTurret.SightRange * 2f)
            {
                raid.SetupSightRange(turret, raid.Options.AutoTurret.SightRange, 2);
            }

            if (damageType == DamageType.Explosion && !raid.Options.ExplosionModifier.Equals(100f))
            {
                info.damageTypes.Scale(damageType, raid.Options.ExplosionModifier / 100f);
            }

            if (builtEntity)
            {
                return DamageResult.Allowed;
            }

            if (raid.IsOpened && IsLootingWeapon(info) && raid.AddLooter(attacker, info))
            {
                if (damageType == DamageType.Explosion && info.WeaponPrefab is TimedExplosive)
                {
                    raid.GetRaider(attacker).HasDestroyed = true;
                }

                raid.TrySetOwner(attacker, entity, info, damageType == DamageType.Heat);
            }

            if (!raid.CanHurtBox(entity))
            {
                if (damageType != DamageType.Heat)
                {
                    NotifyOnce(attacker, "NoDamageToBoxes");
                }

                return DamageResult.Blocked;
            }

            if (raid.Options.MLRS && info.WeaponPrefab is MLRSRocket)
            {
                raid.GetRaider(attacker).lastActiveTime = Time.timeAsDouble;
            }

            return DamageResult.Allowed;
        }

        public bool CanBlockOutsideDamage(RaidableBase raid, BaseEntity attacker)
        {
            return !InRange(attacker.transform.position, raid.Location, Mathf.Max(raid.ProtectionRadius, raid.Options.ArenaWalls.Radius));
        }

        private readonly Dictionary<ulong, List<PlayerExclusion>> playerDelayExclusions = new();

        private class PlayerExclusion : Pool.IPooled
        {
            public object plugin;
            public double time;
            public bool IsExpired => Time.timeAsDouble > time;
            public void EnterPool()
            {
                plugin = null;
                time = 0f;
            }
            public void LeavePool()
            {
            }
        }

        private void ExcludePlayer(ulong userid, float maxDelayLength, object plugin)
        {
            if (plugin == null)
            {
                return;
            }
            if (!playerDelayExclusions.TryGetValue(userid, out var exclusions))
            {
                playerDelayExclusions[userid] = exclusions = Pool.Get<List<PlayerExclusion>>();
            }
            var exclusion = exclusions.Find(x => x.plugin == plugin);
            if (maxDelayLength <= 0f)
            {
                if (exclusion != null)
                {
                    exclusions.Remove(exclusion);
                    exclusion.plugin = null;
                    exclusion.time = 0d;
                    Pool.Free(ref exclusion);
                }
                if (exclusions.Count == 0)
                {
                    playerDelayExclusions.Remove(userid);
                    Pool.FreeUnmanaged(ref exclusions);
                }
            }
            else
            {
                if (exclusion == null)
                {
                    exclusion = Pool.Get<PlayerExclusion>();
                    exclusions.Add(exclusion);
                }
                exclusion.plugin = plugin;
                exclusion.time = Time.timeAsDouble + maxDelayLength;
            }
        }

        private bool HasDelayExclusion(ulong userid)
        {
            if (playerDelayExclusions.TryGetValue(userid, out var exclusions))
            {
                for (int i = 0; i < exclusions.Count; i++)
                {
                    var exclusion = exclusions[i];
                    if (!exclusion.IsExpired)
                    {
                        return true;
                    }
                    exclusions.RemoveAt(i);
                    exclusion.plugin = null;
                    exclusion.time = 0f;
                    Pool.Free(ref exclusion);
                    i--;
                }
                if (exclusions.Count == 0)
                {
                    playerDelayExclusions.Remove(userid);
                    Pool.FreeUnmanaged(ref exclusions);
                }
            }
            return false;
        }

        protected void ClearPlayerDelayExclusions()
        {
            foreach (var exclusions in playerDelayExclusions.Values)
            {
                for (int i = 0; i < exclusions.Count; i++)
                {
                    PlayerExclusion exclusion = exclusions[i];
                    Pool.Free(ref exclusion);
                }

                List<PlayerExclusion> obj = exclusions;
                Pool.FreeUnmanaged(ref obj);
            }

            playerDelayExclusions.Clear();
        }

        #endregion Hooks

    }
}
