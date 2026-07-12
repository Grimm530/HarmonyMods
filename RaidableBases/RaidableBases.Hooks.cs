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
        /// <summary>Load config only (used from OnLoaded so load returns immediately).</summary>
        internal void InitMinimal()
        {
            LoadConfig();
            // Bind Kits Harmony stub so profile Scientist/Murderer Kits resolve via KitsAPI.
            Kits = RaidableBasesHost.Instance?.Kits ?? new KitsPluginStub();
            KitsAPI.Init();
        }
        /// <summary>Rest of init after config (run from deferred soft-start coroutine to avoid load freeze).</summary>
        internal void InitRest()
        {
            if (InstallationError) return;
            HtmlTagRegex = new("<.*?>", RegexOptions.Compiled);
            Automated = new(this, config.Settings.Maintained.Enabled, config.Settings.Schedule.Enabled);
            UndoComparer.DeployableItems = DeployableItems;
            UndoComparer.IsBox = IsBox;
            SpawnsController.Instance = this;
            UI = new() { Instance = this };
            UI.LoadOffsetData();
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
            SaveData();
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

        /// <summary>Unload steps with yields so entry can run unload without freezing.</summary>
        internal IEnumerator RunUnloadStepsAsync()
        {
            if (InstallationError) yield break;
            SaveData();
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

        /// <summary>Start server init as a soft-start coroutine (yields between steps so server stays responsive on harmony.load).</summary>
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

        /// <summary>Soft start: run server init as coroutine with yields between heavy steps.</summary>
        public IEnumerator OnServerInitializedSoftStartCoroutine(System.Action onComplete = null)
        {
            yield return null;
            if (InstallationError || IsUnloading || RaidableBasesHost.Instance == null) yield break;
            // Avoid double soft-start overlapping on watchdog retry.
            if (Queues != null)
            {
                onComplete?.Invoke();
                yield break;
            }
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
            Puts("Commands registered (chat + console): buyraid/rbe/rb/rbevent + ui_buyraid");
            yield return null;
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            LoadPlayerData();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            yield return InitializeSkinsCoroutine();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            if (config.Settings.Buyable.Cooldowns == null)
            {
                config.Settings.Buyable.Cooldowns = new();
                data.BuyableCooldowns.Clear();
                SaveConfig();
            }
            if (config.Settings.TeleportMarker)
                Subscribe(nameof(OnMapMarkerAdded));
            else
                Unsubscribe(nameof(OnMapMarkerAdded));
            Subscribe(nameof(OnPlayerSleepEnded));
            GridController.LoadSpawns();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            if (ZoneManager != null)
                SpawnsController.SetupZones(true);
            Skins.Clear();
            CreateDefaultFiles();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            SetOnSun(true);
            GridController.SetupGrid();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, CheckNotifications);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
            onComplete?.Invoke();
            Puts("Soft-start complete - maintained/scheduled automation will run when grid finishes.");
        }

        private void OnServerInitialized(bool isStartup)
        {
            if (InstallationError)
            {
                return;
            }
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
            Puts("Commands registered (chat + console): buyraid/rbe/rb/rbevent + ui_buyraid");
            LoadPlayerData();
            InitializeSkins();
            Initialize();
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, CheckNotifications);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
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

        private object OnClanMemberJoined(ulong userid, string tag)
        {
            var player = BasePlayer.FindByID(userid);
            if (player == null) return null;
            var raid = Raids.FirstOrDefault(other => other.ownerId == player.userID && other.IsAllyHogging(player));
            if (raid == null) return null;
            // Oxide Clans (optional) + block native ClanManager accept when hogging.
            Clans?.Call("cmdChatClan", player, "clan", new string[1] { "leave" });
            return true;
        }

        private object OnTeamAcceptInvite(RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            if (player == null) return null;
            var raid = Raids.FirstOrDefault(other => other.ownerId == player.userID && other.IsAllyHogging(player));
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

                    foreach (var ia in block.BuildCost())
                    {
                        player.GiveItem(ItemManager.Create(ia.itemDef, (int)ia.amount));
                    }

                    block.SafelyKill();
                }, 0.1f);
            }
            else if (raid.IsFoundation(e) && raid.NearFoundation(e.transform.position))
            {
                Message(player, "TooCloseToABuilding");
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
            var parent = e.IsValid() && e.HasParent() ? e.GetParentEntity() : e.owner;
            if (!parent.IsNetworked() || !Get(parent.transform.position, out var raid) || !raid.Elevators.TryGetValue(parent.net.ID, out var elevator))
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
            if (elevator.IsNetworked() && Get(elevator.transform.position, out var raid) && raid.Elevators.TryGetValue(elevator.net.ID, out var ele) && ele.IsBMG()) return true;
            return null;
        }

        private object OnElevatorCall(Elevator elevator, Elevator fromElevator) => OnElevatorMove(elevator, 0);

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
            if (!TypeNameLookup.TryGetValue(player.UserIDString, out string name))
            {
                TypeNameLookup[player.UserIDString] = name = player.GetType().Name;
            }
            if (!name.Contains("CustomScientist", CompareOptions.OrdinalIgnoreCase) && !name.Contains("14922524", CompareOptions.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!Get(npc.transform.position, out var raid) || !raid.Options.NPC.IgnorePlayerTrapsTurrets || !InRange(raid.Location, npc.spawnPos, raid.ProtectionRadius))
            {
                return false;
            }
            if (entity is AutoTurret turret && turret.OwnerID == 0 && turret.skinID == RB_SKIN_ID)
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

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null)
                return;
            player.Invoke(() =>
            {
                if (player.IsDestroyed || !player.IsHuman())
                {
                    return;
                }

                if (data.Players.TryGetValue(player.UserIDString, out var info))
                {
                    info.Name = player.displayName.ToFriendlyJson();
                }

                UI.PrivateEvents.Remove(player.userID);
                UI.PublicEvents.Remove(player.userID);

                if (GetPVPDelay(player.userID, false, out DelaySettings ds))
                {
                    if (config.UI.Delay.Enabled)
                    {
                        RemovePVPDelay(player.userID, ds);
                        UI.DestroyUi(player, UiType.Delay);
                    }
                    ds.Destroy();
                }

                if (config.UI.Lockout.Enabled)
                {
                    UI.UpdateUi(player, UiType.Lockout);
                }

                if (config.UI.Status.Enabled)
                {
                    UI.UpdateUi(player, UiType.Status);
                }

                if (!Get(player.transform.position, out var raid, 5f))
                {
                    return;
                }

                if (raid.IsUnderground(player.transform.position))
                {
                    raid.intruders.Remove(player.userID);
                    raid.enteredEntities.Remove(player);
                    return;
                }

                if (!config.Settings.Management.AllowTeleport && !raid.TeleportExceptions.Remove(player.userID) && !raid.CanBypass(player) && !raid.CanRespawnAt(player) && raid.Type != RaidableType.None && !raid.WasConnected(player))
                {
                    Message(player, "CannotTeleport");
                    raid.intruders.Remove(player.userID);
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

                if (!raid.IsEngaged && raid.EngageOnNpcDeath && attacker != null && attacker.IsHuman() && !attacker.limitNetworking && !attacker.IsFlying)
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

                if (!raid.intruders.Contains(player.userID))
                {
                    raid.OnPlayerExited(player);
                }

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
                    Message(player, "CommandNotAllowed");
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

        private void OnEntityDeath(StabilityEntity entity, HitInfo info)
        {
            if (info == null || !Get(entity.transform.position, out var raid) || raid.IsDespawning || !raid.GetInitiatorPlayer(info, DamageType.Generic, entity, out var attacker))
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
            else if (io is AutoTurret turret && raid.turrets.Remove(turret))
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
                Interface.CallHook("OnRaidableLootDestroyed", raid.Location, raid.ProtectionRadius, raid.GetLootAmountRemaining(), container, raid.Options.Level);
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
                            raid.QueueNotification(target, "OnRaidFinished", FormatGridReference(target, raid.Location));
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

            if (player.IsNetworked())
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

            if (raid.IsPickupBlacklisted(entity.ShortPrefabName) || entity is DroppedItem di && di.item != null && raid.IsPickupBlacklisted(di.item.info.shortname))
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
            Message(player, "MLRS Target Denied");
            return true;
        }

        private object OnNearbyTurretsScan(AutoTurret turret) => OnInterferenceUpdate(turret);

        // Oxide signature: OnNearbyTurretsScan(AutoTurret, List<AutoTurret>)
        private object OnNearbyTurretsScan(AutoTurret turret, List<AutoTurret> list) => OnInterferenceUpdate(turret);

        private object OnInterferenceUpdate(AutoTurret turret)
        {
            if (turret == null || turret.IsDestroyed) return null;
            if (IsRaidDefenseSkin(turret.skinID)) return true;
            if (!Get(turret.transform.position, out var raid)) return null;
            return raid.BuiltList.Contains(turret) ? (object)true : null;
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
            if (te.creatorEntity == null && Get(te.transform.position, out var raid) && raid.UsableByTurret)
            {
                var pos = te.transform.position;
                foreach (var turret in raid.turrets)
                {
                    if (!turret.IsKilled() && InRange(turret.transform.position, pos, 3f))
                    {
                        te.creatorEntity = turret;
                        break;
                    }
                }
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
                if (owner != null) Message(owner, "MLRS Target Denied");
                else raid.Message("MLRS Target Denied");
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
            else if (raid.cached_attacker != null && !(fire.creatorEntity is BasePlayer) && Time.time - raid.cached_attack_time < 1f && raid.raiders.ContainsKey(raid.cached_attacker_id))
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
                if (Interface.CallHook("OnRaidablePlayerCorpseCreate", new object[] { corpse, raid.Location, raid.AllowPVP, raid.Options.Level, raid.GetOwner(), raid.GetRaiders(), raid.BaseName, raid.PlayersLootable }) != null)
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

                    if (container.IsKilled())
                    {
                        goto done;
                    }

                    container.TakeFrom(corpse.containers, 0f);
                    corpse.Invoke(corpse.SafelyKill, 0.0625f);
                    
                    var player = RustCore.FindPlayerById(playerSteamID);
                    var backpack = raid.AddBackpack(container, playerSteamID, player);
                    bool canEjectBackpack = Interface.CallHook("OnRaidableBaseBackpackEject", new object[] { container, playerSteamID, raid.Location, raid.AllowPVP, raid.Options.Level, raid.GetOwner(), raid.GetRaiders(), raid.BaseName, raid.PlayersLootable }) == null;

                    if (canEjectBackpack && raid.EjectBackpack(backpack, raid.EjectBackpacksPVE))
                    {
                        raid.backpacks.Remove(backpack);
                        backpack.ResetToPool();
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
                        Message(player, "Building is blocked for spawns!");
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
                Message(target.player, "Building is blocked!");
                return false;
            }

            if (!raid.Options.AllowBuildingPriviledges && CupboardPrefabIDs.Contains(construction.prefabID))
            {
                Message(target.player, "Cupboards are blocked!");
                return false;
            }
            else if (construction.prefabID == 2150203378)
            {
                if (!config.Settings.Management.AllowLadders || raid.Options.RequiresCupboardAccessLadders && !raid.CanBuild(target.player))
                {
                    Message(target.player, "Ladders are blocked!");
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
                    Message(target.player, "Barricades are blocked!");
                    return false;
                }
            }
            else if (!raid.Options.AllowBuilding)
            {
                var value = GetFileNameWithoutExtension(construction.fullName);
                if (value != "explosivesiegedeployable" && !raid.Options.AllowedBuildingBlockExceptions.Exists(value.Contains))
                {
                    Message(target.player, "Building is blocked!");
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
            if (player == null || player.limitNetworking || container == null || container.inventory == null || container.OwnerID.IsSteamId() || !Get(container, out var raid))
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
            if (config.BlockPaidContent && config.DestroyLootedContainer && container.inventory.IsEmpty() && PaidDeployableItems.TryGetValue(container.PrefabName, out var def) && Has(container) && RequiresOwnership(def, container.skinID))
            {
                container.Invoke(container.SafelyKill, 0.1f);
            }
        }

        private object CanLootDroppedItemContainer(BasePlayer player, BaseEntity entity) => entity switch
        {
            _ when entity.skinID != RB_SKIN_ID || !entity.OwnerID.IsSteamId() || entity.OwnerID == player.userID => null,
            _ when RelationshipManager.ServerInstance != null && RelationshipManager.ServerInstance.playerToTeam.TryGetValue(entity.OwnerID, out var team) && team.members.Contains(player.userID) => null,
            _ when ConVar.Clan.enabled && player.clanId != 0L && (BasePlayer.FindByID(entity.OwnerID) ?? BasePlayer.FindSleeping(entity.OwnerID)) is BasePlayer owner && owner.clanId == player.clanId => null,
            _ when Convert.ToBoolean(Clans?.Call("IsClanMember", entity.OwnerID.ToString(), player.UserIDString)) => null,
            _ when Convert.ToBoolean(Friends?.Call("AreFriends", entity.OwnerID.ToString(), player.UserIDString)) => null,
            _ => ((Func<object>)(() => { Message(player, "You do not own this loot!"); return true; }))(),
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

        private bool IsArmoredTrain(BaseEntity entity) => entity.OwnerID == 0uL && entity is AutoTurret turret && !turret.isLootable && !turret.dropFloats && turret.parentEntity.IsSet();

        private static bool IsEventDrone(BaseEntity entity) => entity.OwnerID == 335576777746;

        private bool IsSentryTargetingNpc(BasePlayer player, BaseEntity entity) => entity is NPCAutoTurret && player.skinID != RB_SKIN_ID && !player.userID.IsSteamId();

        private bool IgnorePlayer(BasePlayer player, BaseEntity entity) => player.limitNetworking || IsSentryTargetingNpc(player, entity) || IsArmoredTrain(entity);

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
            // Oxide: keep raid NPCs out of event trap/turret triggers when Ignore* is set.
            if (Has(player) && (Has(trigger) || (Get(player.userID, out HumanoidBrain brain) && brain.raid.Options.NPC.IgnorePlayerTrapsTurrets))) return true;
            BaseEntity entity = trigger is TriggerParent p ? p.Entity : trigger.gameObject.ToBaseEntity();
            if (IsProtectedScientist(player, entity)) return true;
            // Exact Oxide semantics: true/null → allow enter; false → cancel enter.
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

            if (hopper.OwnerID == 0 && raid.Has(hopper, false))
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
                return raid.AllowPVP || di.DroppedBy == 0 || di.DroppedBy == hopper.OwnerID || raid.IsAlly(di.DroppedBy, hopper.OwnerID);
            }

            PlayerCorpse corpse = target as PlayerCorpse;
            if (corpse != null)
            {
                return raid.AllowPVP || corpse.playerSteamID == hopper.OwnerID || raid.IsAlly(corpse.playerSteamID, hopper.OwnerID);
            }

            return null;
        }

        private object CanEntityBeTargeted(BasePlayer player, BaseEntity entity)
        {
            if (player.IsKilled()) return null;
            return CanEntityBeTargetedInternal(player, entity, false);
        }

        private static bool IsRaidDefenseSkin(ulong skin) => skin == RB_SKIN_ID || skin == 14922524UL;

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
                // Oxide: raid-skinned defenses always target players (skin 14922524 / our RB_SKIN_ID).
                if (IsRaidDefenseSkin(entity.skinID))
                    return true;

                AutoTurret turret = entity as AutoTurret;
                if (raid.Options.BlockOutsideDamageToPlayersInside && !IsRaidDefenseSkin(entity.skinID) && CanBlockOutsideDamage(raid, entity))
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
                return IsRaidDefenseSkin(entity.skinID) || entity is BaseDetector || HasPVPDelay(player.userID);
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
            if (player == null || player.limitNetworking)
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

        private object CanEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || entity == null || entity.IsDestroyed || entity.OwnerID == 1337422)
            {
                return null;
            }

            if (info.Initiator != null)
            {
                switch (info.Initiator.OwnerID)
                {
                    case 1309:
                    case 13099:
                    case 8002738255:
                    case 335576777746:
                        return null;
                }
            }

            DamageType damageType = info.damageTypes.GetMajorityDamageType();
            DamageResult success = entity is BasePlayer player ?
                HandlePlayerDamage(player, info, damageType, out var raid, out var attacker, out var isHuman) :
                HandleEntityDamage(entity, info, damageType, out raid, out attacker, out isHuman);

            if (success == DamageResult.None)
            {
                return null;
            }

            if (success == DamageResult.Blocked)
            {
                if (info.Weapon is BlowPipeWeapon)
                {
                    info.HitEntity = null;
                }
                return NullifyDamage(info);
            }

            if (isHuman && damageType != DamageType.Heat && raid != null)
            {
                raid.CreateSpheres();
                raid.GetRaider(attacker).lastActiveTime = Time.time;
            }

            return true;
        }

        protected void UnsubscribeDamageHook()
        {
            if (Raids.Count > 0 || config == null || config.Settings.Management.PVPDelayPersists && PvpDelay.Count > 0)
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

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info) => CanEntityTakeDamage(entity, info);

        private DamageResult HandlePlayerDamage(BasePlayer victim, HitInfo info, DamageType damageType, out RaidableBase raid, out BasePlayer attacker, out bool isHuman)
        {
            BaseEntity weapon = info.Initiator;
            attacker = null;
            isHuman = false;

            if (!Get(victim, info, out raid) || raid.IsDespawning)
            {
                if (config.Settings.Management.PVPDelayPersists && weapon is BasePlayer attacker2 && HasPVPDelay(attacker2.userID) && HasPVPDelay(victim.userID))
                {
                    return DamageResult.Allowed;
                }
                return DamageResult.None;
            }

            if (info.WeaponPrefab is MLRSRocket)
            {
                return ((raid.AllowPVP || Has(victim)) && raid.Options.MLRS && (weapon?.OwnerID != 13099)) ? DamageResult.Allowed : DamageResult.Blocked;
            }

            if (IsHelicopter(info, out var eventHeli))
            {
                return eventHeli ? DamageResult.None : DamageResult.Allowed;
            }

            if (Has(victim) && weapon != null && weapon.OwnerID == 0uL && !weapon.enableSaving && Has(weapon))
            {
                info.damageTypes.Clear();
                return DamageResult.None;
            }

            if (IsTrueDamage(weapon, raid.IsProtectedWeapon(weapon)))
            {
                return HandleTrueDamage(raid, info, weapon, victim);
            }

            if (raid.GetInitiatorPlayer(info, damageType, victim, out attacker))
            {
                return HandleAttacker(attacker, victim, info, damageType, raid, out isHuman);
            }

            return Has(victim) ? DamageResult.Blocked : DamageResult.None;
        }

        private DamageResult HandleTrueDamage(RaidableBase raid, HitInfo info, BaseEntity weapon, BasePlayer victim)
        {
            if (victim is ScientistNPC && !Has(victim))
            {
                return DamageResult.None;
            }

            if (raid.Options.NPC.BlockOutsideDamageToNpcsInside && Has(victim) && CanBlockOutsideDamage(raid, weapon) && InRange(victim.transform.position, raid.Location, raid.ProtectionRadius))
            {
                return DamageResult.Blocked;
            }

            AutoTurret turret = weapon as AutoTurret;
            if (turret != null)
            {
                var (min, max) = victim.userID.IsSteamId() ? (raid.Options.AutoTurret.Min, raid.Options.AutoTurret.Max) : (raid.Options.AutoTurret.NpcMin, raid.Options.AutoTurret.NpcMax);

                if (min != 1 || max != 1)
                {
                    info.damageTypes.Scale(DamageType.Bullet, UnityEngine.Random.Range(min, max));
                }

                if (Has(victim) && (raid.Options.NPC.IgnorePlayerTrapsTurrets && weapon.OwnerID.IsSteamId() || weapon.OwnerID == 0uL && weapon.skinID == RB_SKIN_ID))
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

            // GunTrap / FlameTurret: same IgnoreTrapsTurrets intent as CanEntityBeTargeted (Oxide relies on OnEntityEnter).
            if (Has(victim) && weapon is GunTrap or FlameTurret)
            {
                if (weapon.OwnerID.IsSteamId() ? raid.Options.NPC.IgnorePlayerTrapsTurrets : raid.Options.NPC.IgnoreTrapsTurrets)
                {
                    return DamageResult.Blocked;
                }
            }

            return DamageResult.Allowed;
        }

        private DamageResult HandleAttacker(BasePlayer attacker, BasePlayer victim, HitInfo info, DamageType damageType, RaidableBase raid, out bool isHuman)
        {
            isHuman = attacker.IsHuman();
            if (!isHuman && Has(attacker) && Has(victim))
            {
                return DamageResult.Blocked;
            }

            if (attacker.userID == victim.userID)
            {
                return raid.Options.AllowSelfDamage ? DamageResult.Allowed : DamageResult.Blocked;
            }

            if (HasPVPDelay(victim.userID))
            {
                if (!raid.Options.AllowFriendlyFire && raid.IsAlly(attacker.userID, victim.userID))
                {
                    return DamageResult.Blocked;
                }

                if (EventTerritory(attacker.transform.position))
                {
                    raid.SetPVPDelay(attacker, damageType == DamageType.Heat);
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

            if (isHuman && !victim.IsHuman())
            {
                return HandleNpcVictim(raid, victim, attacker, info);
            }

            if (isHuman && victim.IsHuman())
            {
                return HandlePVPDamage(raid, victim, attacker, info, damageType);
            }

            if (Has(attacker))
            {
                return HandleNpcAttacker(raid, victim, attacker, info, damageType);
            }

            return DamageResult.None;
        }

        private DamageResult HandleNpcVictim(RaidableBase raid, BasePlayer victim, BasePlayer attacker, HitInfo info)
        {
            if (!Has(victim) || !HumanoidBrains.TryGetValue(victim.userID, out var brain))
            {
                return DamageResult.Allowed;
            }

            if (config.Settings.Management.BlockMounts)
            {
                if (raid.IsMounted(attacker, raid.Options.Siege.Only || !config.Settings.Management.BlockSiegeMounts))
                {
                    return DamageResult.Blocked;
                }

                var parent = attacker.HasParent() ? attacker.GetParentEntity() : null;

                if (parent is BaseHelicopter || parent is HotAirBalloon)
                {
                    return DamageResult.Blocked;
                }
            }

            if (raid.Options.NPC.BlockOutsideDamageToNpcsInside && brain.AttackTarget != attacker && CanBlockOutsideDamage(raid, attacker) && InRange(victim.transform.position, raid.Location, raid.ProtectionRadius))
            {
                // Still mark agro so NPCs react once the player steps inside.
                brain.SetTarget(attacker, converge: false);
                return DamageResult.Blocked;
            }

            if (!raid.Options.NPC.CanLeave && raid.Options.NPC.BlockOutsideDamageOnLeave && !InRange(attacker.transform.position, raid.Location, raid.ProtectionRadius) && InRange(victim.transform.position, raid.Location, raid.ProtectionRadius))
            {
                // Remember shooter, but heal/forget roam so outside snipe doesn't soft-kill NPCs.
                brain.SetTarget(attacker, converge: false);
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

            // Damage agro: force target even if Vanish left limitNetworking stuck; skip converge-networking gate.
            brain.SetTarget(attacker, converge: false);

            return DamageResult.Allowed;
        }

        private DamageResult HandlePVPDamage(RaidableBase raid, BasePlayer victim, BasePlayer attacker, HitInfo info, DamageType damageType)
        {
            if (playerDelayExclusions.Count > 1 && HasDelayExclusion(victim.userID) && HasDelayExclusion(attacker.userID))
            {
                return DamageResult.Allowed;
            }

            if (raid.HasLockout(attacker, damageType != DamageType.Heat))
            {
                return DamageResult.Blocked;
            }

            if (raid.Options.BlockOutsideDamageToPlayersInside && CanBlockOutsideDamage(raid, attacker) && !(info.WeaponPrefab is MLRSRocket))
            {
                if (config.EventMessages.NoDamageFromOutsideToPlayersInside && damageType != DamageType.Heat)
                {
                    TryMessage(attacker, "NoDamageFromOutsideToPlayersInside");
                }
                return DamageResult.Blocked;
            }

            if (IsPVE() && (!InRange(attacker.transform.position, raid.Location, raid.ProtectionRadius) || !InRange(victim.transform.position, raid.Location, raid.ProtectionRadius)))
            {
                return DamageResult.Blocked;
            }

            if (raid.IsAlly(attacker.userID, victim.userID))
            {
                return raid.Options.AllowFriendlyFire ? DamageResult.Allowed : DamageResult.Blocked;
            }

            if (raid.AllowPVP)
            {
                raid.SetPVPDelay(attacker, damageType == DamageType.Heat);
                return DamageResult.Allowed;
            }

            return DamageResult.Blocked;
        }

        private DamageResult HandleNpcAttacker(RaidableBase raid, BasePlayer victim, BasePlayer attacker, HitInfo info, DamageType damageType)
        {
            if (!Has(attacker) || !HumanoidBrains.TryGetValue(attacker.userID, out var brain))
            {
                return DamageResult.Allowed;
            }

            if (Has(victim))
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

            switch (brain.attackType)
            {
                case HumanoidBrain.AttackType.BaseProjectile:
                    info.damageTypes.ScaleAll(raid.Options.NPC.Multipliers.ProjectileDamageMultiplier);
                    break;
                case HumanoidBrain.AttackType.Explosive:
                    info.damageTypes.ScaleAll(raid.Options.NPC.Multipliers.ExplosiveDamageMultiplier);
                    break;
                case HumanoidBrain.AttackType.Melee:
                    info.damageTypes.ScaleAll(raid.Options.NPC.Multipliers.MeleeDamageMultiplier);
                    break;
            }

            return DamageResult.Allowed;
        }

        private DamageResult HandleEntityDamage(BaseCombatEntity entity, HitInfo info, DamageType damageType, out RaidableBase raid, out BasePlayer attacker, out bool isHuman)
        {
            raid = null;
            attacker = null;
            isHuman = false;

            if (info.Initiator is SamSite ss)
            {
                return ss.skinID == RB_SKIN_ID ? DamageResult.Allowed : DamageResult.None;
            }

            if (!Get(entity.transform.position, out raid) || !ValidateEventTurretDamage(info, raid, entity))
            {
                return DamageResult.None;
            }

            if (IsHelicopter(info, out bool eventHeli))
            {
                HandleHelicopterDamage(entity, info);
                return eventHeli ? DamageResult.None : DamageResult.Allowed;
            }

            bool isAttacker = raid.GetInitiatorPlayer(info, damageType, entity, out attacker);
            isHuman = isAttacker && attacker.IsHuman();

            if (raid.IsDespawning)
            {
                return !isAttacker ? DamageResult.Allowed : DamageResult.None;
            }

            if (HandleOwnerlessEntities(entity, info, raid, isHuman) == DamageResult.None)
            {
                return DamageResult.None;
            }

            ApplyPlayerDamageMultipliers(info, raid, damageType, isAttacker, isHuman, attacker);

            HandleSpecificEntities(entity, info, raid);

            if (ShouldBlockDamage(entity, info, damageType, raid))
            {
                return DamageResult.Blocked;
            }

            if (ShouldBlockDueToLoadingOrDecay(entity, damageType, raid))
            {
                return DamageResult.Blocked;
            }

            if (entity.IsNpc || entity is PlayerCorpse)
            {
                return DamageResult.Allowed;
            }

            if (entity is BuildingBlock block)
            {
                DamageResult handleBuildingResult = HandleBuildingBlock(block, raid);
                if (handleBuildingResult != DamageResult.None)
                {
                    return handleBuildingResult;
                }
            }
            else if (raid.IsMountable(entity))
            {
                DamageResult handleMountableResult = HandleMountable(entity, info, raid, isHuman, attacker);
                if (handleMountableResult != DamageResult.None)
                {
                    return handleMountableResult;
                }
            }

            if (!entity.IsValid())
            {
                return DamageResult.None;
            }

            bool checkList = raid.BuiltList.Contains(entity);

            if (!checkList && !raid.Has(entity, false))
            {
                return DamageResult.None;
            }

            if (info.WeaponPrefab is TimedExplosive && info.WeaponPrefab.ShortPrefabName == "torpedostraight")
            {
                ScaleTorpedoDamage(info, raid);
            }

            if (!attacker.IsNetworked())
            {
                return ValidateUnknownAttacker(info, raid, entity) ? DamageResult.Allowed : DamageResult.None;
            }

            if (!isHuman)
            {
                return HandleNonHumanAttacker(entity, raid, attacker, info, damageType);
            }

            if (info.IsProjectile())
            {
                raid.cached_attacker = attacker;
                raid.cached_attack_time = Time.time;
                raid.cached_attacker_id = attacker.userID;
            }

            UpdateAttackerInfo(entity, attacker);

            if (HandleEcoAndMountDamage(raid, attacker, info, damageType) == DamageResult.Blocked)
            {
                return DamageResult.Blocked;
            }

            if (raid.Options.BlockOutsideDamageToBaseInside && CanBlockOutsideDamage(raid, attacker) && !(info.WeaponPrefab is MLRSRocket))
            {
                TryMessage(attacker, "NoDamageFromOutsideToBaseInside");
                return DamageResult.Blocked;
            }

            if (HandleRaidAndTurretConditions(entity, raid, attacker, info, damageType) == DamageResult.Blocked)
            {
                return DamageResult.Blocked;
            }

            if (!checkList && FinalizeRaidChecks(entity, info, raid, attacker, damageType) == DamageResult.Blocked)
            {
                return DamageResult.Blocked;
            }

            return DamageResult.Allowed;
        }

        private bool ValidateEventTurretDamage(HitInfo info, RaidableBase raid, BaseCombatEntity entity)
        {
            if (entity.OwnerID != 0uL || entity.enableSaving || info.Initiator.IsKilled() || info.Initiator.skinID != RB_SKIN_ID)
            {
                return true;
            }
            AutoTurret turret = info.Initiator as AutoTurret;
            if (turret != null)
            {
                BuildingBlock block = entity as BuildingBlock;
                if (block != null && block.grade == BuildingGrade.Enum.Twigs)
                {
                    BasePlayer target = turret.target as BasePlayer;
                    if (target != null && raid.intruders.Contains(target.userID))
                    {
                        turret.target.Hurt(info);
                    }
                    if (raid.Options.TurretsHurtTwig)
                    {
                        return true;
                    }
                }
                info.damageTypes.Clear();
                return false;
            }
            return true;
        }

        private void HandleHelicopterDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (config.Settings.Management.BlockHelicopterDamage && entity.OwnerID == 0uL)
            {
                info.damageTypes.Clear();
            }
        }

        private DamageResult HandleOwnerlessEntities(BaseCombatEntity entity, HitInfo info, RaidableBase raid, bool isHuman)
        {
            if (isHuman && entity.OwnerID == 0uL && raid.Type != RaidableType.None)
            {
                raid.IsEngaged = true;
                raid.CheckDespawn();
            }
            if (info.Initiator != null && info.Initiator.skinID == RB_SKIN_ID && entity.skinID == RB_SKIN_ID)
            {
                info.damageTypes.Clear();
                return DamageResult.None;
            }
            return DamageResult.Allowed;
        }

        private void ApplyMaxEffectiveRangeMultiplier(float maxEffectiveRange, float sqrProtectionRadius, Vector3 a, HitInfo info, HumanoidBrain brain)
        {
            if (maxEffectiveRange > 0f)
            {
                float distanceSq = (a - brain.ServerPosition).sqrMagnitude;

                if (distanceSq > sqrProtectionRadius)
                {
                    bool flag = distanceSq > maxEffectiveRange * maxEffectiveRange;

                    info.damageTypes.ScaleAll(flag ? 0f : 1f - (Mathf.Sqrt(distanceSq) / maxEffectiveRange));
                }
            }
        }

        private void ApplyPlayerDamageMultipliers(HitInfo info, RaidableBase raid, DamageType damageType, bool isAttacker, bool isHuman, BasePlayer attacker)
        {
            if (isAttacker ? isHuman : damageType == DamageType.Heat)
            {
                if (raid.PlayerDamageMultiplier.Count > 0)
                {
                    foreach (var m in raid.PlayerDamageMultiplier)
                    {
                        info.damageTypes.Scale(m.index, m.amount);
                    }
                }
                if (raid.Options.PlayerDamageMultiplierTC != 1f && info.HitEntity is BuildingPrivlidge)
                {
                    info.damageTypes.ScaleAll(raid.Options.PlayerDamageMultiplierTC);
                }
            }
            if (!raid.Options.Siege.Disabled)
            {
                raid.Options.Siege.Scale(attacker, info, isHuman);
            }
        }

        private void HandleSpecificEntities(BaseCombatEntity entity, HitInfo info, RaidableBase raid)
        {
            if (entity is BearTrap trap && trap != null)
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
        }

        private bool ShouldBlockDamage(BaseCombatEntity entity, HitInfo info, DamageType damageType, RaidableBase raid)
        {
            return raid.IsDamageBlocked(entity) || (!raid.Options.MLRS && info.WeaponPrefab is MLRSRocket);
        }

        private bool ShouldBlockDueToLoadingOrDecay(BaseCombatEntity entity, DamageType damageType, RaidableBase raid)
        {
            if (damageType == DamageType.Decay)
            {
                return entity.OwnerID == 0uL && !entity.enableSaving && raid.Has(entity, false);
            }
            return raid.IsLoading || entity is DroppedItemContainer;
        }

        private DamageResult HandleBuildingBlock(BuildingBlock block, RaidableBase raid)
        {
            if (raid.Options.Setup.FoundationsImmune || raid.Options.Setup.FoundationsImmuneForcedHeight && raid.Options.Setup.ForcedHeight != -1)
            {
                if (raid.foundations.Count > 0 && block.ShortPrefabName.StartsWith("foundation"))
                {
                    return DamageResult.Blocked;
                }

                if (raid.floors == null && block.ShortPrefabName.StartsWith("floor") && block.transform.position.y - raid.Location.y <= 3f)
                {
                    return DamageResult.Blocked;
                }
            }

            if (block.OwnerID == 0)
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

            if (block.grade == BuildingGrade.Enum.Twigs)
            {
                return DamageResult.Allowed;
            }

            return DamageResult.None;
        }

        private DamageResult HandleMountable(BaseEntity entity, HitInfo info, RaidableBase raid, bool isHuman, BasePlayer attacker)
        {
            if (config.Settings.Management.MiniCollision && entity is Minicopter && entity == info.Initiator)
            {
                return DamageResult.Blocked;
            }

            if (isHuman && !ExcludedMountsExists(entity.ShortPrefabName))
            {
                BaseMountable mountable = entity as BaseMountable;
                if (mountable != null)
                {
                    BaseVehicle vehicle = mountable.HasParent() ? mountable.VehicleParent() : mountable as BaseVehicle;

                    if (vehicle != null && vehicle.GetDriver() == attacker)
                    {
                        return config.Settings.Management.MountDamageFromPlayers ? DamageResult.Allowed : DamageResult.Blocked;
                    }
                }
                if (!config.Settings.Management.MountDamageFromPlayers)
                {
                    TryMessage(attacker, "NoMountedDamageTo");
                    return DamageResult.Blocked;
                }
                if (config.Settings.Management.BlockMounts && raid.IsMounted(attacker, raid.Options.Siege.Only || !config.Settings.Management.BlockSiegeMounts))
                {
                    TryMessage(attacker, "NoMountedDamageFrom");
                    return DamageResult.Blocked;
                }
                if (raid.Options.BlockOutsideDamageToBaseInside && CanBlockOutsideDamage(raid, attacker) && !(info.WeaponPrefab is MLRSRocket))
                {
                    TryMessage(attacker, "NoDamageFromOutsideToBaseInside");
                    return DamageResult.Blocked;
                }
            }

            if (info.Initiator == entity)
            {
                return config.Settings.Management.MountDamageFromPlayers || (entity is BatteringRam or BatteringRamHead) ? DamageResult.Allowed : DamageResult.Blocked;
            }

            return DamageResult.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ExcludedMountsExists(string prefabName)
        {
            foreach (var prefix in ExcludedMounts)
            {
                if (prefabName.StartsWith(prefix))
                {
                    return true;
                }
            }
            return false;
        }

        private void ScaleTorpedoDamage(HitInfo info, RaidableBase raid)
        {
            info.damageTypes.ScaleAll(UnityEngine.Random.Range(raid.Options.Water.TorpedoMin, raid.Options.Water.TorpedoMax));
        }

        private bool ValidateUnknownAttacker(HitInfo info, RaidableBase raid, BaseCombatEntity entity)
        {
            BaseEntity initiator = info.Initiator;
            return initiator.IsNull() || (initiator.OwnerID == 0uL && Has(initiator)) || IsLootingWeapon(info);
        }

        private DamageResult HandleNonHumanAttacker(BaseCombatEntity entity, RaidableBase raid, BasePlayer attacker, HitInfo info, DamageType damageType)
        {
            if (entity.OwnerID == 0uL && !raid.Options.RaidingNpcs && !Has(attacker))
            {
                info.damageTypes.Clear();
                return DamageResult.None;
            }

            if (info.damageTypes.Has(DamageType.Explosion) || info.WeaponPrefab is TimedExplosive)
            {
                if (entity.OwnerID == 0uL && !(entity is BasePlayer) && Has(attacker))
                {
                    return DamageResult.Blocked;
                }

                //return raid.Has(entity) ? DamageResult.Allowed : DamageResult.Blocked;
                //return (entity.OwnerID == 0uL || raid.BuiltList.Contains(entity)) ? DamageResult.Allowed : DamageResult.Blocked;
            }

            return DamageResult.Allowed;
        }

        private void UpdateAttackerInfo(BaseCombatEntity entity, BasePlayer attacker)
        {
            entity.lastAttacker = attacker;
            attacker.lastDealtDamageTime = Time.time;
        }

        private DamageResult HandleEcoAndMountDamage(RaidableBase raid, BasePlayer attacker, HitInfo info, DamageType damageType)
        {
            if (raid.Options.Eco.Enabled && !raid.IsEcoTool(attacker, info))
            {
                TryMessage(attacker, "EcoOnly");
                return DamageResult.Blocked;
            }

            if (raid.Options.Siege.Only && !raid.Options.Siege.IsSiegeTool(attacker, info, damageType))
            {
                TryMessage(attacker, "PrimitiveOnly");
                return DamageResult.Blocked;
            }

            if (config.Settings.Management.BlockMounts && raid.IsMounted(attacker, raid.Options.Siege.Only || !config.Settings.Management.BlockSiegeMounts))
            {
                TryMessage(attacker, "NoMountedDamageFrom");
                return DamageResult.Blocked;
            }

            return DamageResult.Allowed;
        }

        public bool CanBlockOutsideDamage(RaidableBase raid, BaseEntity attacker)
        {
            return !InRange(attacker.transform.position, raid.Location, Mathf.Max(raid.ProtectionRadius, raid.Options.ArenaWalls.Radius));
        }

        private DamageResult HandleRaidAndTurretConditions(BaseCombatEntity entity, RaidableBase raid, BasePlayer attacker, HitInfo info, DamageType damageType)
        {
            if (raid.ID.IsSteamId() && IsBox(entity, false) && (attacker.UserIDString == raid.ID || raid.IsAlly(attacker.userID, Convert.ToUInt64(raid.ID))))
            {
                return DamageResult.Blocked;
            }

            if (raid.ownerId.IsSteamId() && raid.CanEjectEnemy() && !raid.IsAlly(attacker))
            {
                TryMessage(attacker, "NoDamageToEnemyBase");
                return DamageResult.Blocked;
            }

            if (raid.HasLockout(attacker, damageType != DamageType.Heat))
            {
                return DamageResult.Blocked;
            }

            if (raid.Options.AutoTurret.AutoAdjust && entity.skinID == RB_SKIN_ID && entity is AutoTurret turret && turret.sightRange < raid.Options.AutoTurret.SightRange * 2)
            {
                raid.SetupSightRange(turret, raid.Options.AutoTurret.SightRange, 2);
            }

            if (damageType == DamageType.Explosion && !raid.Options.ExplosionModifier.Equals(100f))
            {
                info.damageTypes.Scale(damageType, raid.Options.ExplosionModifier / 100f);
            }

            return DamageResult.None;
        }

        private DamageResult FinalizeRaidChecks(BaseCombatEntity entity, HitInfo info, RaidableBase raid, BasePlayer attacker, DamageType damageType)
        {
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
                    TryMessage(attacker, "NoDamageToBoxes");
                }
                return DamageResult.Blocked;
            }

            if (raid.Options.MLRS && info.WeaponPrefab is MLRSRocket)
            {
                raid.GetRaider(attacker).lastActiveTime = Time.time;
            }

            return DamageResult.None;
        }

        private readonly Dictionary<ulong, List<PlayerExclusion>> playerDelayExclusions = new();

        private class PlayerExclusion : Pool.IPooled
        {
            public object plugin;
            public float time;
            public bool IsExpired => Time.time > time;
            public void EnterPool()
            {
                plugin = null;
                time = 0f;
            }
            public void LeavePool()
            {
                plugin = null;
                time = 0f;
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
                    exclusion.time = 0f;
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
                exclusion.time = Time.time + maxDelayLength;
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
                    Pool.Free(ref exclusions);
                }
            }
            return false;
        }

        #endregion Hooks

    }
}
