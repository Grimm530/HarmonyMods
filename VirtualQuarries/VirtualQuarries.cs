using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using Facepunch;
using Network;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;
using VLB;
using Image = UnityEngine.UI.Image;
using VirtualQuarriesCuiShim;

namespace Oxide.Plugins
{
    [Info("VirtualQuarries", "ThePitereq", "2.6.0")]
    public partial class VirtualQuarries : RustPlugin
    {
        public static CUI.Handler CuiHandler = new();
        private const string ExcavatorSignalTipUi = "VirtualQuarries_ExcavatorSignalTip";
        private const string SupplyDropPrefab = "assets/prefabs/misc/supply drop/supply_drop.prefab";

        [PluginReference]
        private readonly Plugin
            PopUpAPI, //POPUPS - REQUIRED
            ServerRewards, Economics, IQEconomic, BankSystem, ShoppyStock, //CURRENCY PLUGINS - REQUIRED IF PURCHASES FOR CURRENCY ENABLED
            RedeemStorageAPI; //QUARRY REFUND - REQUIRED IF REFUNDING ENABLED

        private static VirtualQuarries _plugin;
        internal static bool PendingNewSave;

        private readonly List<ulong> adminModes = new();
        private readonly Dictionary<ulong, UiCache> cache = new();
        private readonly Dictionary<ulong, ExcavatorArm> outputPilesLinks = new();
        private readonly Dictionary<ulong, int> outputPileSlots = new();
        private readonly Dictionary<ulong, ExcavatorArm> computerLinks = new();
        private readonly List<ExcavatorArm> excavatorArms = new();
        private readonly Dictionary<ulong, float> commandCooldowns = new();
        private readonly Dictionary<ulong, float> excavatorSignalPopupCooldowns = new();
        private readonly Dictionary<ulong, float> excavatorPinnedTipUntil = new();
        private readonly Dictionary<ulong, (int, bool)> awaitingConnections = new();
        private readonly List<Vector3> giantExcPositions = new();
        private static DateTime lastOperation = DateTime.Now;
        private readonly List<ulong> anyLinkedQuarryOutputs = new();

        private class UiCache
        {
            public bool isUsingPlugin = true;
            public int quarryId = -1;
            public string quarrySearch = string.Empty;
            public string userSearch = string.Empty;
            public string surveyKey = string.Empty;
            public int userManageQuarryId = -1;
            public bool userManageRemove = false;
            public QuarryViewType viewType = QuarryViewType.All;
            public readonly List<string> autoRollDisabledResources = new();
            public readonly Dictionary<string, float> autoRollValues = new();
            public readonly CachedSurvey cachedSurvey = new();
            public int autoRollMinResources = 0;
            public ulong lastLootedLinkedStorage = 0;
        }

        private enum QuarryViewType
        {
            All,
            Owned,
            Shared
        }

        private class CachedSurvey
        {
            public string profile = string.Empty;
            public readonly List<QuarryResource> resources = new();
        }

        #region Init

        private void Init()
        {
            _plugin = this;
            config = Config.ReadObject<PluginConfig>();
            if (config == null)
            {
                LoadDefaultConfig();
                config = Config.ReadObject<PluginConfig>() ?? new PluginConfig();
            }
            config.staticQuarryUpgrades ??= new();
            config.excavatorUpgrades ??= new();
            config.excavatorResources ??= new();
            config.quarryProfiles ??= new();
            config.surveys ??= new();
            config.excavatorFuelItems ??= new();
            config.staticFuelItems ??= new();
            if (config.staticQuarryUpgrades.Count == 0)
            {
                PrintWarning("There was no static quarry upgrades (maybe update from older version?). Generating base one...");
                config.staticQuarryUpgrades.Add(new() { capacity = 18 });
            }
            if (config.excavatorUpgrades.Count == 0)
            {
                PrintWarning("There was no excavator quarry upgrades (maybe update from older version?). Generating base one...");
                config.excavatorUpgrades.Add(new() { capacity = 18 });
            }                
            if (config.excavatorResources.Count < 4)
            {
                PrintWarning("You don't have all 4 resources required to run excavator properly! Generating new ones...");
                config.excavatorResources = new()
                {
                    { "Stone", new() {
                        new() { shortname = "stones", amount = 5000 }
                    } },
                    { "Metal", new() {
                        new() { shortname = "metal.fragments", amount = 2500 }
                    } },
                    { "Sulfur", new() {
                        new() { shortname = "sulfur.ore", amount = 1000 }
                    } },
                    { "HQM", new() {
                        new() { shortname = "hq.metal.ore", amount = 50 }
                    } }
                };
            }
            Config.WriteObject(config);
            foreach (var u in config.staticQuarryUpgrades)
                u.requiredItems ??= new List<RequiredItem>();
            foreach (var u in config.excavatorUpgrades)
                u.requiredItems ??= new List<RequiredItem>();
            LoadData();
            LoadMessages();
        }

        private void OnServerInitialized()
        {
            TryWipeFromMapVoterSignal();
            if (config.economyPlugin == 1 && Economics == null)
                PrintWarning("Economics plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 2 && ServerRewards == null)
                PrintWarning("ServerRewards plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 3 && IQEconomic == null)
                PrintWarning("IQEconomic plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 4 && BankSystem == null)
                PrintWarning("BankSystem plugin not found! You will not be able to upgrade your quarries with server currency!");
            else if (config.economyPlugin == 5 && ShoppyStock == null)
                PrintWarning("ShoppyStock plugin not found! You will not be able to upgrade your quarries with server currency!");
            if (PopUpAPI == null)
                PrintWarning("PopUpAPI plugin not found! The Pop-Up messages will not display!");
            else
                GeneratePopUpConfig();
            if (RedeemStorageAPI == null)
                PrintWarning("RedeemStorageAPI plugin not found! Refunded items from removed quarries will go to inventories instead of redeem inventory!");
            if (config.surveyThrow)
                Unsubscribe(nameof(OnExplosiveThrown));
            if (config.dispenserProfiles.Count == 0)
                Unsubscribe(nameof(OnDispenserBonus));
            if (!config.staticQuarries)
                Unsubscribe(nameof(OnQuarryToggled));
            if (!config.excavatorQuarry)
            {
                Unsubscribe(nameof(OnExcavatorSuppliesRequest));
                Unsubscribe(nameof(OnExcavatorResourceSet));
            }
            if (!config.staticQuarries && !config.excavatorQuarry)
                Unsubscribe(nameof(CanLootEntity));
            bool anyLink = false;
            foreach (var profile in config.quarryProfiles.Values)
                if (profile.enableInputLink || profile.enableOutputLink)
                {
                    anyLink = true;
                    break;
                }
            if (!anyLink)
                Unsubscribe(nameof(OnLootEntity));
            permission.RegisterPermission("virtualquarries.admin", this);
            if (config.requirePermission)
                permission.RegisterPermission("virtualquarries.use", this);
            if (config.sharingRequirePermission)
                permission.RegisterPermission("virtualquarries.share", this);
            if (config.quarryPerm)
                permission.RegisterPermission("virtualquarries.static.quarry", this);
            if (config.pumpJackPerm)
                permission.RegisterPermission("virtualquarries.static.pumpjack", this);
            foreach (var permissionKey in config.permissions.Keys)
                permission.RegisterPermission(permissionKey, this);
            foreach (var permissionKey in config.surveys.Values)
                if (permissionKey.permission != string.Empty)
                    permission.RegisterPermission(permissionKey.permission, this);
            foreach (var permissionKey in config.quarryProfiles.Values)
            {
                if (permissionKey.permission != string.Empty)
                    permission.RegisterPermission(permissionKey.permission, this);
                if (permissionKey.allowQuickPerm != string.Empty)
                    permission.RegisterPermission(permissionKey.allowQuickPerm, this);
                foreach (var resourceKey in permissionKey.resources.Values)
                    if (resourceKey.permission != string.Empty)
                        permission.RegisterPermission(resourceKey.permission, this);
                foreach (var upgradeKey in permissionKey.upgrades)
                    if (upgradeKey.requiredPerm != string.Empty)
                        permission.RegisterPermission(upgradeKey.requiredPerm, this);
            }
            if (config.storeContainers)
                timer.Every(config.containerSaveInterval, SaveContainers);
            foreach (var command in config.commandList)
                cmd.AddChatCommand(command, this, nameof(VirtualQuarriesCommand));
            cmd.AddChatCommand("vqtip", this, nameof(VirtualQuarriesTipCommand));
            cmd.AddConsoleCommand(Community.Protect("QuarriesUI"), this, nameof(QuarriesConsoleCommand));
            timer.Once(1f, SetupQuarries);
            foreach (var player in BasePlayer.activePlayerList)
            {
                playerCache[player.userID] = player.displayName;
                if (config.offlineQuarryOwnerKickTime == 0) continue;
                DateTime now = DateTime.Now;
                foreach (var quarry in data.quarries)
                {
                    if (quarry.Value.owner != player.userID) continue;
                    quarry.Value.lastOwnerOnline = now;
                }
            }
            if (config.offlineQuarryOwnerKickTime > 0)
                CheckQuarryKicks();
            if (config.excavatorQuarry)
                timer.Every(1f, CheckExcavatorSignalLookPopups);
            if (config.excavatorQuarry)
            {
                foreach (var monument in TerrainMeta.Path.Landmarks)
                {
                    if (!monument.displayPhrase.IsValid()) continue;
                    if (monument.displayPhrase.english == "Giant Excavator Pit")
                        giantExcPositions.Add(monument.transform.position);
                }

                foreach (var computer in BaseNetworkable.serverEntities.OfType<ExcavatorSignalComputer>())
                {
                    bool validDist = false;
                    foreach (var giantExcPosition in giantExcPositions)
                    {
                        if (Vector3.Distance(giantExcPosition, computer.transform.position) > 200) continue;
                        List<ExcavatorArm> arms = Pool.Get<List<ExcavatorArm>>();
                        arms.Clear();
                        Vis.Entities(giantExcPosition, 100, arms);
                        if (arms.Count == 0)
                        {
                            Pool.FreeUnmanaged(ref arms);
                            continue;
                        }
                        computerLinks[computer.net.ID.Value] = arms[0];
                        Pool.FreeUnmanaged(ref arms);
                        validDist = true;
                        break;
                    }
                    if (!validDist) continue;
                    computer.requiresPowerToCharge = false;
                    computer.chargePower = float.MaxValue / 2f;
                    computer.SetFlag(BaseEntity.Flags.Reserved7, true);
                    computer.SetFlag(BaseEntity.Flags.Reserved8, true);
                    computer.maxNumSuppliesCalled = -1;
                }
            }
        }

        private void GeneratePopUpConfig()
        {
            JObject popUpConfig = new JObject()
            {
                { "key", "UpperCenter" },
                { "anchor", "0.5 1" },
                { "name", "Legacy" },
                { "parent", "Overall" },
                { "background_enabled", true },
                { "background_color", "0.153 0.141 0.114 1" },
                { "background_fadeIn", 0.5f },
                { "background_fadeOut", 0.5f },
                { "background_offsetMax", "180 0" },
                { "background_offsetMin", "-180 -70" },
                { "background_smooth", false },
                { "background_url", "" },
                { "background_additionalObjectCount", 1 },
                { "background_detail_0_color", "0.196 0.18 0.153 1" },
                { "background_detail_0_offsetMax", "354 70" },
                { "background_detail_0_offsetMin","6 6" },
                { "background_detail_0_smooth", false },
                { "background_detail_0_url", "" },
                { "text_anchor", "MiddleCenter" },
                { "text_color", ColDb.LightGray },
                { "text_fadeIn", 0.5f },
                { "text_fadeOut", 0.5f },
                { "text_font", "RobotoCondensed-Bold.ttf" },
                { "text_offsetMax", "170 0" },
                { "text_offsetMin", "-170 -64" },
                { "text_outlineColor", "0 0 0 0" },
                { "text_outlineSize", "0 0" }
            };
            PopUpAPI?.Call("AddNewPopUpSchema", Name, popUpConfig);
        }

        private void SetupQuarries()
        {
            if (config.staticQuarries && !config.disableRunningEffect)
            {
                foreach (var quarry in BaseNetworkable.serverEntities.OfType<MiningQuarry>())
                {
                    if (quarry == null || quarry.IsDestroyed) continue;
                    if (quarry.staticType == MiningQuarry.QuarryType.None && quarry.OwnerID != 0) continue;
                    try
                    {
                        quarry.SetOn(false);
                        quarry.CancelInvoke();
                        var engineSwitch = quarry.engineSwitchPrefab?.instance;
                        if (engineSwitch != null && !engineSwitch.IsDestroyed)
                            engineSwitch.SetFlag(BaseEntity.Flags.On, true);
                        quarry.SetFlag(BaseEntity.Flags.On, true);
                        quarry.SendNetworkUpdate();
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"Static quarry setup {quarry.net?.ID.Value}: {ex.Message}");
                    }
                }
            }
            if (config.excavatorQuarry)
            {
                LinkExcavatorWorldEntities();
                foreach (var arm in excavatorArms)
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(arm.transform.position, pos) > 400) continue;
                        arm.StopMining();
                        arm.SetFlag(BaseEntity.Flags.Reserved8, true);
                        if (!config.disableExcavatorRunningEffect)
                            arm.SetFlag(BaseEntity.Flags.On, true);
                        break;
                    }
                }
                foreach (var engin in BaseNetworkable.serverEntities.OfType<DieselEngine>())
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(engin.transform.position, pos) > 250) continue;
                        engin.cachedFuelTime = 0;
                        engin.SetFlag(BaseEntity.Flags.On, false);
                        break;
                    }
                }
            }
            foreach (var quarry in data.quarries)
            {
                try
                {
                    bool isVirtual = quarry.Value.quarryType == QuarryType.Virtual;
                    QuarryProfile qp = null;
                    if (isVirtual && (config.quarryProfiles == null || !config.quarryProfiles.TryGetValue(quarry.Value.profile, out qp) || qp == null))
                    {
                        PrintWarning($"Quarry {quarry.Key} profile '{quarry.Value.profile}' is missing; skipping setup.");
                        continue;
                    }
                    BoxStorage storage = GetQuarryBox(quarry.Key, BoxType.Core);
                    if (!storage)
                    {
                        PrintWarning($"Quarry {quarry.Key} core storage could not be spawned; skipping setup.");
                        continue;
                    }
                    GameObject.Destroy(storage.GetComponent<GroundWatch>());
                    GameObject.Destroy(storage.GetComponent<DestroyOnGroundMissing>());
                    VirtualQuarry vQuarry = storage.GetOrAddComponent<VirtualQuarry>();
                    vQuarry.SetupQuarry(quarry.Key);
                    BoxStorage fuelStorage = GetQuarryBox(quarry.Key, BoxType.Fuel);
                    if (isVirtual && qp.enableInputLink)
                        if (fuelStorage && fuelStorage.transform.position.y < -300)
                            quarry.Value.fuelNetId = 0;
                    if (quarry.Value.redirectNetId != 0 && !anyLinkedQuarryOutputs.Contains(quarry.Value.redirectNetId))
                        anyLinkedQuarryOutputs.Add(quarry.Value.redirectNetId);
                    if (!isVirtual || !qp.enableInputLink)
                    {
                        List<UpgradeConfig> upgrades;
                        if (isVirtual)
                            upgrades = qp.upgrades;
                        else if (quarry.Value.quarryType == QuarryType.Static)
                            upgrades = config.staticQuarryUpgrades;
                        else
                            upgrades = config.excavatorUpgrades;
                        if (upgrades != null && quarry.Value.level < upgrades.Count && storage.inventory != null)
                        {
                            UpgradeConfig uc = upgrades[quarry.Value.level];
                            storage.inventory.capacity = uc.capacity;
                        }
                    }
                    if (!isVirtual || !qp.enableInputLink)
                    {
                        if (!fuelStorage)
                        {
                            PrintWarning($"Quarry {quarry.Key} fuel storage could not be spawned; skipping fuel setup.");
                            continue;
                        }
                        GameObject.Destroy(fuelStorage.GetComponent<GroundWatch>());
                        GameObject.Destroy(fuelStorage.GetComponent<DestroyOnGroundMissing>());
                        List<UpgradeConfig> upgrades;
                        if (isVirtual)
                            upgrades = qp.upgrades;
                        else if (quarry.Value.quarryType == QuarryType.Static)
                            upgrades = config.staticQuarryUpgrades;
                        else
                            upgrades = config.excavatorUpgrades;
                        if (upgrades != null && quarry.Value.level < upgrades.Count && fuelStorage.inventory != null)
                        {
                            UpgradeConfig uc = upgrades[quarry.Value.level];
                            fuelStorage.inventory.capacity = uc.fuelCapacity;
                        }
                        else if (upgrades == null || quarry.Value.level >= upgrades.Count)
                            PrintWarning($"Quarry with ID {quarry.Key} have more upgrades than is added to config! You need to add more levels or remove this quarry from data, or plugin will print errors!");
                    }
                    if (quarry.Value.quarryType == QuarryType.Excavator)
                    {
                        BoxStorage output2 = GetQuarryBox(quarry.Key, BoxType.Output2);
                        if (output2)
                        {
                            GameObject.Destroy(output2.GetComponent<GroundWatch>());
                            GameObject.Destroy(output2.GetComponent<DestroyOnGroundMissing>());
                            if (config.excavatorUpgrades != null && quarry.Value.level < config.excavatorUpgrades.Count && output2.inventory != null)
                                output2.inventory.capacity = config.excavatorUpgrades[quarry.Value.level].capacity;
                            if (output2.inventory != null)
                                output2.inventory.canAcceptItem = (_, _, _) => false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    PrintWarning($"Quarry {quarry.Key} setup failed: {ex.Message}");
                }
            }
        }

        private void Unload()
        {
            if (config.storeContainers)
                SaveContainers();
            if (config.excavatorQuarry)
            {
                foreach (var arm in BaseNetworkable.serverEntities.OfType<ExcavatorArm>())
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(arm.transform.position, pos) > 250) continue;
                        arm.SetFlag(BaseEntity.Flags.Reserved8, false);
                        if (!config.disableExcavatorRunningEffect)
                            arm.SetFlag(BaseEntity.Flags.On, false);
                        break;
                    }
                }
                foreach (var engin in BaseNetworkable.serverEntities.OfType<DieselEngine>())
                {
                    foreach (var pos in giantExcPositions)
                    {
                        if (Vector3.Distance(engin.transform.position, pos) > 250) continue;
                        engin.cachedFuelTime = 0;
                        engin.SetFlag(BaseEntity.Flags.On, false);
                        break;
                    }
                }
            }
            if (config.staticQuarries)
            {
                foreach (var quarry in BaseNetworkable.serverEntities.OfType<MiningQuarry>())
                {
                    if (quarry.staticType == MiningQuarry.QuarryType.None && quarry.OwnerID != 0) continue;
                    quarry.SetOn(false);
                    quarry.CancelInvoke();
                    quarry.engineSwitchPrefab.instance.SetFlag(BaseEntity.Flags.On, false);
                    quarry.SetFlag(BaseEntity.Flags.On, false);
                    quarry.SendNetworkUpdate();
                }
            }
            SaveData();
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Main");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_New");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_NewCursor");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Back");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Players");
                CuiHelper.DestroyUi(player, "VirtualQuarriesUI_Levels");
                CuiHelper.DestroyUi(player, "QuarriesUI");
                CuiHelper.DestroyUi(player, "QuarriesInventoriesUI");
                CuiHelper.DestroyUi(player, ExcavatorSignalTipUi);
            }
            foreach (var netId in data.quarries.Values)
            {
                BoxStorage quarry = BaseNetworkable.serverEntities.Find(new NetworkableId(netId.netId)) as BoxStorage;
                if (quarry)
                    GameObject.Destroy(quarry.GetComponent<VirtualQuarry>());
            }
            SaveData();
        }

        private void OnNewSave()
        {
            PendingNewSave = true;
            ApplyWipeIfNeeded();
        }

        private void TryWipeFromMapVoterSignal()
        {
            ApplyWipeIfNeeded();
        }

        private void StampWipeIdentity()
        {
            if (data == null) data = new PluginData();
            data.protocol = Rust.Protocol.save;
            data.wipeId = SaveRestore.WipeId ?? "";
        }

        private void WipePluginData(string reason)
        {
            previousGatheredDispensers = new(data?.gatheredDispensers ?? new Dictionary<ulong, Dictionary<string, int>>());
            data = new PluginData();
            StampWipeIdentity();
            playerCache = new Dictionary<ulong, string>();
            if (!config.storeContainers)
                storageCache = new Dictionary<int, StorageData>();
            SaveData();
            SavePrevDispensers();
            if (!config.storeContainers)
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}/storageCache", storageCache);
            Puts($"Server wipe detected ({reason}). Plugin data has been wiped.");
        }

        private void ApplyWipeIfNeeded()
        {
            if (config == null || data == null) return;

            int currentProtocol = Rust.Protocol.save;
            string currentWipeId = SaveRestore.WipeId ?? "";
            var statePath = Path.Combine(Environment.CurrentDirectory, "HarmonyData", Name, "last_wipe_signal.txt");
            bool mapVoter = WipeSignal.ShouldWipe(statePath);
            bool pending = PendingNewSave;
            bool protocolChanged = data.protocol != currentProtocol;
            bool wipeIdChanged = !string.IsNullOrEmpty(data.wipeId)
                && !string.IsNullOrEmpty(currentWipeId)
                && !string.Equals(data.wipeId, currentWipeId, StringComparison.Ordinal);

            if (!config.wipeData)
            {
                PendingNewSave = false;
                StampWipeIdentity();
                return;
            }

            if (config.wipeDataForceOnly)
            {
                DateTime now = DateTime.Now;
                bool forceThursday = now.Day < 8 && now.DayOfWeek == DayOfWeek.Thursday;
                if (!forceThursday && !mapVoter)
                {
                    PendingNewSave = false;
                    StampWipeIdentity();
                    return;
                }
            }

            if (!mapVoter && !pending && !protocolChanged && !wipeIdChanged)
            {
                if (data.protocol == 0 || string.IsNullOrEmpty(data.wipeId))
                {
                    StampWipeIdentity();
                    SaveData();
                }
                return;
            }

            string reason = pending ? "new map"
                : protocolChanged ? $"protocol {data.protocol} -> {currentProtocol}"
                : wipeIdChanged ? "wipe id change"
                : "MapVoter wipe signal";
            PendingNewSave = false;
            WipePluginData(reason);
            WipeSignal.MarkWiped(statePath);
        }

        #endregion

        #region RUST Hooks

        private static readonly HashSet<ulong> assignedBonuses = new();

        private void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            bool checkedValid = false;
            bool isValid = false;

            foreach (var profile in config.dispenserProfiles)
            {
                if (profile.Value.Contains(dispenser.baseEntity.ShortPrefabName))
                {
                    if (!checkedValid)
                    {
                        isValid = assignedBonuses.Add(dispenser.baseEntity.net.ID.Value);
                        data.gatheredDispensers.TryAdd(player.userID, new());
                    }
                    checkedValid = true;
                    if (!isValid) break;
                    data.gatheredDispensers[player.userID].TryAdd(profile.Key, 0);
                    data.gatheredDispensers[player.userID][profile.Key]++;
                }
            }

        }

        private void OnPlayerConnected(BasePlayer player)
        {
            playerCache[player.userID] = player.displayName;
            if (config.offlineQuarryOwnerKickTime == 0) return;
            DateTime now = DateTime.Now;
            foreach (var quarry in data.quarries)
            {
                if (quarry.Value.owner != player.userID) continue;
                quarry.Value.lastOwnerOnline = now;
            }
        }

        private void OnLootEntityEnd(BasePlayer player, BoxStorage _)
        {
            CuiHelper.DestroyUi(player, "QuarriesInventoriesUI");
            if (cache.TryGetValue(player.userID, out var uc) && uc.isUsingPlugin)
            {
                if (uc.lastLootedLinkedStorage == 0)
                    DrawVirtualQuarriesUI(player);
                uc.lastLootedLinkedStorage = 0;
            }
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon item)
        {
            if (item.skinID == 0 && item.ShortPrefabName == "survey_charge")
            {
                entity.Kill();
                Item survey = ItemManager.CreateByName("surveycharge");
                NextTick(() => player.GiveItem(survey));
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoSurveyThrow", player.UserIDString, config.commandList[0]));
            }
        }

        private object OnEntityTakeDamage(BoxStorage entity)
        {
            if (entity.transform.position.y < -390) return config.returnValue;
            return null;
        }

        private void OnQuarryToggled(MiningQuarry quarry, BasePlayer player)
        {
            quarry.SetOn(false);
            quarry.CancelInvoke();
            quarry.engineSwitchPrefab.instance.SetFlag(BaseEntity.Flags.On, true);
            quarry.SetFlag(BaseEntity.Flags.On, true);
            quarry.SendNetworkUpdate();
        }

        private object OnExcavatorResourceSet(ExcavatorArm arm, string type, BasePlayer player)
        {
            bool valid = false;
            foreach (var pos in giantExcPositions)
            {
                if (Vector3.Distance(arm.transform.position, pos) > 250) continue;
                valid = true;
                break;
            }
            if (!valid) return null;
            if (config.excavatorPerm && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.excavator"))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionQuarry", player.UserIDString));
                return false;
            }
            int quarryId = -1;
            foreach (var quarry in data.quarries)
            {
                if (quarry.Value.quarryType == QuarryType.Excavator && quarry.Value.owner == player.userID)
                {
                    quarryId = quarry.Key;
                    break;
                }
            }
            if (quarryId == -1)
                quarryId = CreateNewExcavatorQuarry(player, arm);
            BoxStorage storage = GetQuarryBox(quarryId, BoxType.Core);
            if (!storage)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ErrorOccured", player.UserIDString));
                return false;
            }
            VirtualQuarry vq = storage.GetComponent<VirtualQuarry>();
            vq.SwitchExcavatorType(type);
            vq.SwitchEngine(false);
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("SwitchedResource", player.UserIDString, type));
            if (!arm.HasFlag(BaseEntity.Flags.On) && !config.disableExcavatorRunningEffect)
            {
                arm.SetFlag(BaseEntity.Flags.On, true);
                //ExcavatorServerEffects.SetMining(true, true);
            }
            return false;
        }

        private object OnExcavatorSuppliesRequest(ExcavatorSignalComputer comp, BasePlayer player)
        {
            if (!TryGetExcavatorArmForEntity(comp, out ExcavatorArm arm)) return null;
            if (config.excavatorSupplyCallTime == 0)
            {
                NotifyExcavatorPlayer(player, Lang("NoAirDrops", player.UserIDString));
                return false;
            }
            int quarryId = GetPlayerExcavatorQuarryId(player.userID);
            if (quarryId == -1)
            {
                TimeSpan ts = TimeSpan.FromSeconds(config.excavatorSupplyCallTime + config.excavatorTick);
                NotifyExcavatorPlayer(player, Lang("NotEnoughMined", player.UserIDString, ts.ToString("hh'h 'mm'm'")));
                return false;
            }
            QuarryData qd = data.quarries[quarryId];
            VirtualQuarry vq = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.netId)).GetComponent<VirtualQuarry>();
            if (!vq || vq.quarryRunTime < config.excavatorSupplyCallTime)
            {
                float remaining = Mathf.Max(0f, config.excavatorSupplyCallTime - (vq ? vq.quarryRunTime : 0f));
                TimeSpan ts = TimeSpan.FromSeconds(remaining + config.excavatorTick);
                NotifyExcavatorPlayer(player, Lang("NotEnoughMined", player.UserIDString, ts.ToString("hh'h 'mm'm'")));
                return false;
            }
            if (Interface.CallHook("OnCustomVirtualQuarrySupplyDropCalled", arm, comp, player) != null) return false;
            if (!SpawnExcavatorSupplyDrop(comp, player, out Vector3 dropPos))
            {
                NotifyExcavatorPlayer(player, Lang("ErrorOccured", player.UserIDString));
                return false;
            }
            string grid = MapHelper.PositionToString(dropPos);
            NotifyExcavatorPlayer(player, Lang("SupplyDropCalled", player.UserIDString, grid), Lang("ExcavatorSignalTipDropping", player.UserIDString, grid));
            if (config.consoleLogs)
                Puts($"Excavator supply drop for {player.displayName} ({player.userID}) landing at {grid} ({dropPos})");
            vq.quarryRunTime = 0;
            return false;
        }

        private static Vector3 GetExcavatorDropPosition(ExcavatorSignalComputer computer)
        {
            Vector3 dropPos = computer.transform.position;
            if (computer.dropPoints != null && computer.dropPoints.Length > 0)
            {
                Transform point = computer.dropPoints[UnityEngine.Random.Range(0, computer.dropPoints.Length)];
                if (point)
                    dropPos = point.position;
            }
            dropPos.x += UnityEngine.Random.Range(-3f, 3f);
            dropPos.z += UnityEngine.Random.Range(-3f, 3f);
            float terrain = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(dropPos) : dropPos.y;
            dropPos.y = terrain;
            return dropPos;
        }

        private static bool SpawnExcavatorSupplyDrop(ExcavatorSignalComputer computer, BasePlayer player, out Vector3 dropPos)
        {
            dropPos = Vector3.zero;
            if (!computer) return false;
            dropPos = GetExcavatorDropPosition(computer);
            // BetterAirDrop on this server uses InstantSupplyDrops + InstantSupplyDropFallsFromSky:
            // spawn the crate in the sky at the target, no cargo plane. CargoPlane.InitDropPosition
            // before Spawn was ignored, so the crate never appeared at the excavator.
            Vector3 spawnPos = dropPos;
            spawnPos.y = dropPos.y + 250f;
            SupplyDrop crate = GameManager.server.CreateEntity(SupplyDropPrefab, spawnPos) as SupplyDrop;
            if (!crate)
                return false;
            if (player && player.userID != 0UL)
                crate.OwnerID = player.userID;
            crate.globalBroadcast = true;
            crate.Spawn();
            computer.SetFlag(BaseEntity.Flags.Reserved9, true);
            computer.Invoke(computer.StopTransmitting, 5f);
            computer.SendNetworkUpdate();
            return true;
        }

        private void LinkExcavatorWorldEntities()
        {
            excavatorArms.Clear();
            outputPilesLinks.Clear();
            foreach (var arm in BaseNetworkable.serverEntities.OfType<ExcavatorArm>())
            {
                if (!arm) continue;
                if (giantExcPositions.Count > 0 && !IsNearGiantExcavator(arm.transform.position, 400f))
                    continue;
                excavatorArms.Add(arm);
                if (arm.outputPiles == null) continue;
                foreach (var pile in arm.outputPiles)
                {
                    if (pile && pile.net != null)
                        outputPilesLinks[pile.net.ID.Value] = arm;
                }
            }
            foreach (var pile in BaseNetworkable.serverEntities.OfType<ExcavatorOutputPile>())
            {
                if (!pile || pile.net == null) continue;
                ExcavatorArm arm = FindNearestExcavatorArm(pile.transform.position, 500f);
                if (arm)
                    outputPilesLinks[pile.net.ID.Value] = arm;
            }
            foreach (var computer in BaseNetworkable.serverEntities.OfType<ExcavatorSignalComputer>())
            {
                if (!computer || computer.net == null) continue;
                ExcavatorArm arm = FindNearestExcavatorArm(computer.transform.position, 400f);
                if (arm)
                    computerLinks[computer.net.ID.Value] = arm;
            }
            AssignExcavatorOutputSlots();
        }

        private void AssignExcavatorOutputSlots()
        {
            outputPileSlots.Clear();
            Dictionary<ulong, List<ExcavatorOutputPile>> pilesByArm = new();
            foreach (var pair in outputPilesLinks)
            {
                if (!pair.Value || pair.Value.IsDestroyed) continue;
                ExcavatorOutputPile pile = BaseNetworkable.serverEntities.Find(new NetworkableId(pair.Key)) as ExcavatorOutputPile;
                if (!pile) continue;
                ulong armId = pair.Value.net.ID.Value;
                if (!pilesByArm.TryGetValue(armId, out List<ExcavatorOutputPile> piles))
                {
                    piles = new List<ExcavatorOutputPile>();
                    pilesByArm[armId] = piles;
                }
                piles.Add(pile);
            }
            foreach (var piles in pilesByArm.Values)
            {
                piles.Sort((a, b) => a.net.ID.Value.CompareTo(b.net.ID.Value));
                for (int i = 0; i < piles.Count; i++)
                    outputPileSlots[piles[i].net.ID.Value] = i;
            }
        }

        private int GetExcavatorOutputSlot(StorageContainer worldPile)
        {
            if (!worldPile || worldPile.net == null) return 0;
            if (outputPileSlots.TryGetValue(worldPile.net.ID.Value, out int slot))
                return slot;
            AssignExcavatorOutputSlots();
            return outputPileSlots.TryGetValue(worldPile.net.ID.Value, out slot) ? slot : 0;
        }

        private bool IsNearGiantExcavator(Vector3 pos, float maxDist)
        {
            foreach (var monumentPos in giantExcPositions)
            {
                if (Vector3.Distance(pos, monumentPos) <= maxDist)
                    return true;
            }
            return giantExcPositions.Count == 0;
        }

        private ExcavatorArm FindNearestExcavatorArm(Vector3 pos, float maxDist)
        {
            if (excavatorArms.Count == 0)
            {
                foreach (var arm in BaseNetworkable.serverEntities.OfType<ExcavatorArm>())
                {
                    if (arm)
                        excavatorArms.Add(arm);
                }
            }
            ExcavatorArm best = null;
            float bestDist = maxDist;
            foreach (var arm in excavatorArms)
            {
                if (!arm || arm.IsDestroyed) continue;
                float dist = Vector3.Distance(pos, arm.transform.position);
                if (dist >= bestDist) continue;
                bestDist = dist;
                best = arm;
            }
            return best;
        }

        private bool TryGetExcavatorArmForEntity(BaseEntity entity, out ExcavatorArm arm)
        {
            arm = null;
            if (!entity || entity.net == null) return false;
            if (entity is ExcavatorSignalComputer && computerLinks.TryGetValue(entity.net.ID.Value, out arm) && arm && !arm.IsDestroyed)
                return true;
            if (entity is ExcavatorOutputPile && outputPilesLinks.TryGetValue(entity.net.ID.Value, out arm) && arm && !arm.IsDestroyed)
                return true;
            arm = FindNearestExcavatorArm(entity.transform.position, 500f);
            if (!arm) return false;
            if (entity is ExcavatorSignalComputer)
                computerLinks[entity.net.ID.Value] = arm;
            else if (entity is ExcavatorOutputPile)
                outputPilesLinks[entity.net.ID.Value] = arm;
            return true;
        }

        private int GetPlayerExcavatorQuarryId(ulong playerId)
        {
            foreach (var quarry in data.quarries)
            {
                if (quarry.Value.quarryType == QuarryType.Excavator && quarry.Value.owner == playerId)
                    return quarry.Key;
            }
            return -1;
        }

        private static bool TryGetLookedAtSignalComputer(BasePlayer player, out ExcavatorSignalComputer computer)
        {
            computer = null;
            if (!player || !player.eyes) return false;
            if (!GamePhysics.Trace(player.eyes.HeadRay(), 0f, out RaycastHit hit, 6f, 1218652417, QueryTriggerInteraction.UseGlobal, player))
                return false;

            BaseEntity hitEntity = RaycastHitEx.GetEntity(hit);
            computer = hitEntity as ExcavatorSignalComputer ?? hitEntity?.GetParentEntity() as ExcavatorSignalComputer;
            if (computer) return true;

            List<ExcavatorSignalComputer> nearbyComputers = Pool.Get<List<ExcavatorSignalComputer>>();
            nearbyComputers.Clear();
            Vis.Entities(hit.point, 1.8f, nearbyComputers);
            if (nearbyComputers.Count > 0)
                computer = nearbyComputers[0];
            Pool.FreeUnmanaged(ref nearbyComputers);
            return computer;
        }

        private static string FormatExcavatorTime(float seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
            if (ts.TotalHours >= 1)
                return ts.ToString(@"hh\:mm\:ss");
            return ts.ToString(@"mm\:ss");
        }

        private static void HideExcavatorSignalGameTip(BasePlayer player)
        {
            if (!player) return;
            CuiHelper.DestroyUi(player, ExcavatorSignalTipUi);
        }

        private static void ShowExcavatorSignalGameTip(BasePlayer player, string message)
        {
            if (!player) return;
            CuiHelper.DestroyUi(player, ExcavatorSignalTipUi);
            CuiElementContainer container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.68 0.23 0.18 0.95" },
                RectTransform = { AnchorMin = "0.44 0.40", AnchorMax = "0.56 0.50" }
            }, "Overlay", ExcavatorSignalTipUi);
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = message,
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, ExcavatorSignalTipUi);
            CuiHelper.AddUi(player, container);
        }

        private static string ClearColorAndSize(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            message = message.Replace("</color>", string.Empty).Replace("</size>", string.Empty);
            while (message.Contains("<color="))
            {
                int index = message.IndexOf("<color=", StringComparison.Ordinal);
                int end = message.IndexOf('>', index);
                if (index < 0 || end < 0) break;
                message = message.Remove(index, end - index + 1);
            }
            while (message.Contains("<size="))
            {
                int index = message.IndexOf("<size=", StringComparison.Ordinal);
                int end = message.IndexOf('>', index);
                if (index < 0 || end < 0) break;
                message = message.Remove(index, end - index + 1);
            }
            return message;
        }

        private void NotifyExcavatorPlayer(BasePlayer player, string popupMessage, string cuiMessage = null, float pinSeconds = 12f)
        {
            if (!player) return;
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, popupMessage);
            ShowExcavatorSignalGameTip(player, cuiMessage ?? popupMessage);
            excavatorPinnedTipUntil[player.userID] = Time.realtimeSinceStartup + pinSeconds;
            string toast = ClearColorAndSize(popupMessage);
            if (string.IsNullOrWhiteSpace(toast)) return;

            bool toastSent = false;
            try
            {
                player.ShowToast(GameTip.Styles.Blue_Long, new Translate.Phrase("virtualquarries.tip", toast), false);
                toastSent = true;
            }
            catch
            {
                try
                {
                    player.SendConsoleCommand("gametip.showtoast_translated", (int)GameTip.Styles.Blue_Long, "virtualquarries.tip", toast, false, System.Array.Empty<string>());
                    toastSent = true;
                }
                catch { }
            }
            if (!toastSent)
            {
                player.SendConsoleCommand("gametip.hidegametip");
                player.SendConsoleCommand("gametip.showgametip", toast);
            }
            player.ChatMessage(toast);
        }

        private void VirtualQuarriesTipCommand(BasePlayer player, string _, string[] __)
        {
            if (!player) return;
            ShowExcavatorSignalGameTip(player, "<color=#ffffff>Supply Ready In</color>\n<color=#ff5f5f>09:59</color>");
        }

        private void CheckExcavatorSignalLookPopups()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (!player || player.IsDead() || player.IsSleeping())
                {
                    HideExcavatorSignalGameTip(player);
                    continue;
                }
                float now = Time.realtimeSinceStartup;
                if (excavatorPinnedTipUntil.TryGetValue(player.userID, out float pinnedUntil) && now < pinnedUntil)
                    continue;
                if (!TryGetLookedAtSignalComputer(player, out ExcavatorSignalComputer comp))
                {
                    HideExcavatorSignalGameTip(player);
                    continue;
                }
                if (!computerLinks.ContainsKey(comp.net.ID.Value))
                {
                    HideExcavatorSignalGameTip(player);
                    continue;
                }

                if (excavatorSignalPopupCooldowns.TryGetValue(player.userID, out float lastShown) && now - lastShown < 1.9f)
                    continue;

                int quarryId = GetPlayerExcavatorQuarryId(player.userID);
                if (quarryId == -1)
                {
                    ShowExcavatorSignalGameTip(player, Lang("ExcavatorSignalTipMissing", player.UserIDString));
                    excavatorSignalPopupCooldowns[player.userID] = now;
                    continue;
                }

                if (!data.quarries.TryGetValue(quarryId, out QuarryData qd))
                    continue;

                BaseNetworkable net = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.netId));
                VirtualQuarry vq = net ? net.GetComponent<VirtualQuarry>() : null;
                if (!vq) continue;

                float needed = config.excavatorSupplyCallTime;
                float remaining = Mathf.Max(0f, needed - vq.quarryRunTime);
                if (remaining <= 0f)
                    ShowExcavatorSignalGameTip(player, Lang("ExcavatorSignalTipReady", player.UserIDString));
                else
                    ShowExcavatorSignalGameTip(player, Lang("ExcavatorSignalTipLeft", player.UserIDString, FormatExcavatorTime(remaining)));

                excavatorSignalPopupCooldowns[player.userID] = now;
            }
        }

        private bool TryGetExcavatorComputer(ExcavatorArm arm, out ExcavatorSignalComputer computer)
        {
            foreach (var pair in computerLinks)
            {
                if (!pair.Value || pair.Value.net.ID.Value != arm.net.ID.Value) continue;
                computer = BaseNetworkable.serverEntities.Find(new NetworkableId(pair.Key)) as ExcavatorSignalComputer;
                return computer;
            }
            computer = null;
            return false;
        }

        private static bool IsSignalComputerReadyToClaim(ExcavatorSignalComputer computer)
        {
            if (!computer) return false;
            return computer.HasFlag(BaseEntity.Flags.Reserved7) || computer.chargePower >= computer.GetChargeNeededForSupplies();
        }

        private void OnLootEntity(BasePlayer player, BoxStorage storage)
        {
            if (awaitingConnections.TryGetValue(player.userID, out var awaiting))
            {
                int quarryId = awaiting.Item1;
                if (!data.quarries.TryGetValue(quarryId, out QuarryData qd)) return;
                QuarryProfile qp = config.quarryProfiles[qd.profile];
                if (!qp.enableInputLink && !qp.enableOutputLink) return;
                bool isFuel = awaiting.Item2;
                if (isFuel)
                    qd.fuelNetId = storage.net.ID.Value;
                else
                    qd.redirectNetId = storage.net.ID.Value;
                VirtualQuarry vq = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.netId)).GetComponent<VirtualQuarry>();
                vq.SetupQuarry(quarryId);
                if (!anyLinkedQuarryOutputs.Contains(storage.net.ID.Value))
                    anyLinkedQuarryOutputs.Add(storage.net.ID.Value);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("QuarryLinkedSuccessfully", player.UserIDString));
            }
            if (anyLinkedQuarryOutputs.Contains(storage.net.ID.Value))
                DrawLinkedQuarriesUI(player, storage.net.ID.Value);
        }

        private object CanLootEntity(BasePlayer player, StorageContainer storage)
        {
            if (!storage) return null;
            if (config.excavatorQuarry)
            {
                bool isInput = storage is DieselEngine;
                bool isOutput = storage is ExcavatorOutputPile;
                if (isInput || isOutput)
                {
                    if (!TryGetExcavatorArmForEntity(storage, out ExcavatorArm outputArm))
                        return null;
                    if (config.excavatorPerm && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.excavator"))
                    {
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionExcavator", player.UserIDString));
                        return false;
                    }
                    int quarryId = -1;
                    foreach (var quarry in data.quarries)
                    {
                        if (quarry.Value.quarryType == QuarryType.Excavator && quarry.Value.owner == player.userID)
                        {
                            quarryId = quarry.Key;
                            break;
                        }
                    }
                    if (quarryId == -1)
                    {
                        if (TryGetExcavatorComputer(outputArm, out ExcavatorSignalComputer computer) && !IsSignalComputerReadyToClaim(computer))
                        {
                            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ExcavatorClaimNotReady", player.UserIDString));
                            return false;
                        }
                        quarryId = CreateNewExcavatorQuarry(player, outputArm);
                    }
                    OpenStaticQuarry(player, quarryId, isInput, storage);
                    return false;
                }
            }
            if (config.staticQuarries && storage is ResourceExtractorFuelStorage quarryStorage)
            {
                MiningQuarry quarry = quarryStorage.GetParentEntity() as MiningQuarry;
                if (!quarry) return null;
                if (quarry.staticType == MiningQuarry.QuarryType.None && quarry.OwnerID != 0) return null;
                if (config.quarryPerm && quarry.ShortPrefabName == "mininquarry_static" && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.quarry"))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionQuarry", player.UserIDString));
                    return false;
                }
                if (config.pumpJackPerm && quarry.ShortPrefabName == "pumpjack-static" && !permission.UserHasPermission(player.UserIDString, "virtualquarries.static.pumpjack"))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermissionPumpJack", player.UserIDString));
                    return false;
                }
                int quarryId = -1;
                foreach (var dataQuarry in data.quarries)
                {
                    if (dataQuarry.Value.quarryType == QuarryType.Static && dataQuarry.Value.owner == player.userID && dataQuarry.Value.staticNetId == quarry.net.ID.Value)
                    {
                        quarryId = dataQuarry.Key;
                        break;
                    }
                }
                if (quarryId == -1)
                    quarryId = CreateNewStaticQuarry(player, quarry);
                OpenStaticQuarry(player, quarryId, quarryStorage.ShortPrefabName == "fuelstorage");
                return false;
            }
            return null;
        }

        #endregion

        #region Commands

        private void QuarriesConsoleCommand(ConsoleSystem.Arg arg)
        {
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
            BasePlayer player = arg.Player();
            if (config.commandCooldown > 0)
            {
                if (commandCooldowns.TryGetValue(player.userID, out float lastUsage) && Time.realtimeSinceStartup - lastUsage < config.commandCooldown) return;
                commandCooldowns[player.userID] = Time.realtimeSinceStartup;
            }
            if (config.requirePermission && !permission.UserHasPermission(player.UserIDString, "virtualquarries.use"))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermission", player.UserIDString));
                return;
            }
            UiCache uc = cache[player.userID];
            switch (arg.GetString(0))
            {
                case "close":
                    CuiHelper.DestroyUi(player, "QuarriesUI");
                    uc.isUsingPlugin = false;
                    break;
                case "closeSurveySelect":
                    CuiHelper.DestroyUi(player, "QuarriesUI_RollSelect");
                    break;
                case "closeQuarryRoll":
                    CuiHelper.DestroyUi(player, "QuarriesUI_QuarryRoll");
                    break;
                case "closePlayers":
                    CuiHelper.DestroyUi(player, "QuarriesUI_UsersUI");
                    break;
                case "closeStorage":
                    DrawVirtualQuarriesUI(player);
                    player.EndLooting();
                    CuiHelper.DestroyUi(player, "QuarriesInventoriesUI");
                    break;
                case "quarry":
                    int id = int.Parse(cmdArgs[1]);
                    int oldId = uc.quarryId;
                    if (uc.quarryId == id)
                        uc.quarryId = -1;
                    else
                        uc.quarryId = id;
                    RedrawQuarryDetails(player, oldId, id);
                    break;
                case "addNew":
                    TryAddNewQuarry(player);
                    break;
                case "viewOwned":
                    if (uc.viewType == QuarryViewType.Owned)
                        uc.viewType = QuarryViewType.All;
                    else
                        uc.viewType = QuarryViewType.Owned;
                    UpdateSortButtons(player);
                    break;
                case "viewShared":
                    if (uc.viewType == QuarryViewType.Shared)
                        uc.viewType = QuarryViewType.All;
                    else
                        uc.viewType = QuarryViewType.Shared;
                    UpdateSortButtons(player);
                    break;
                case "adminView":
                    if (!permission.UserHasPermission(player.UserIDString, "virtualquarries.admin")) return;
                    if (adminModes.Contains(player.userID))
                        adminModes.Remove(player.userID);
                    else
                        adminModes.Add(player.userID);
                    UpdateAdminButton(player);
                    break;
                case "removeAll":
                    uc.userManageQuarryId = -1;
                    uc.userManageRemove = true;
                    OpenUserManagementPanel(player);
                    break;
                case "addAll":
                    if (config.sharingRequirePermission && !permission.UserHasPermission(player.UserIDString, "virtualquarries.share"))
                    {
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoAccessToShare", player.UserIDString));
                        return;
                    }
                    uc.userManageQuarryId = -1;
                    uc.userManageRemove = false;
                    OpenUserManagementPanel(player);
                    break;
                case "giveAccess":
                    if (config.sharingRequirePermission && !permission.UserHasPermission(player.UserIDString, "virtualquarries.share"))
                    {
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoAccessToShare", player.UserIDString));
                        return;
                    }
                    uc.userManageQuarryId = uc.quarryId;
                    uc.userManageRemove = false;
                    OpenUserManagementPanel(player);
                    break;
                case "removeAccess":
                    uc.userManageQuarryId = uc.quarryId;
                    uc.userManageRemove = true;
                    OpenUserManagementPanel(player);
                    break;
                case "selectSurvey":
                    string surveyKey = cmdArgs[1];
                    CuiHelper.DestroyUi(player, "QuarriesUI_RollSelect");
                    OpenQuarryRoll(player, surveyKey);
                    break;
                case "toggleAuto":
                    string resourceKey = cmdArgs[1];
                    SwitchAutoSurveyResource(player, resourceKey);
                    break;
                case "searchPlayer":
                    string searchPhrase = cmdArgs.Length == 1 ? string.Empty : string.Join(' ', cmdArgs.Skip(1));
                    uc.userSearch = searchPhrase;
                    SearchPlayers(player);
                    break;
                case "setTargetRoll":
                    resourceKey = cmdArgs[1];
                    float minAmount = 0;
                    if (cmdArgs.Length > 2 && !float.TryParse(cmdArgs[2], out minAmount))
                    {
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("InvalidAmountInput", player.UserIDString, cmdArgs[2]));
                        return;
                    }
                    UpdateAutoSurveyResourceAmount(player, resourceKey, minAmount);
                    break;
                case "setMinResources":
                    int count = 1;
                    if (cmdArgs.Length > 1 && !int.TryParse(cmdArgs[1], out count))
                    {
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("InvalidAmountInput", player.UserIDString, cmdArgs[1]));
                        return;
                    }
                    ChangeAutoSurveyResourceCount(player, count);
                    break;
                case "trySearch":
                    TryQuarrySearch(player);
                    break;
                case "bulkSearch":
                    TryBulkQuarrySearch(player);
                    break;
                case "removeQuarry":
                    DrawQuarryRemovePopUp(player);
                    break;
                case "authList":
                    ShowQuarryAccessList(player);
                    break;
                case "confirmRemove":
                    RemoveQuarry(player);
                    break;
                case "backRemove":
                    CuiHelper.DestroyUi(player, "QuarriesUI_RemovePopUp");
                    break;
                case "openOutput":
                    OpenQuarry(player, false);
                    break;
                case "openInput":
                    OpenQuarry(player, true);
                    break;
                case "place":
                    TryPlaceQuarry(player);
                    break;
                case "toggle":
                    TryToggleQuarry(player);
                    break;
                case "buyItems":
                    TryUpgradeQuarry(player, 0);
                    break;
                case "buyCurrency":
                    TryUpgradeQuarry(player, 1);
                    break;
                case "buyBoth":
                    TryUpgradeQuarry(player, 2);
                    break;
                case "player":
                    ulong userId = ulong.Parse(cmdArgs[1]);
                    ManageQuarryPlayer(player, userId);
                    break;
                case "collectResources":
                    QuickCollectResources(player);
                    break;
                case "linkOutput":
                    UpdateQuarryConnection(player, uc.quarryId, true, false);
                    break;
                case "linkInput":
                    UpdateQuarryConnection(player, uc.quarryId, true, true);
                    break;
                case "unlinkOutput":
                    UpdateQuarryConnection(player, uc.quarryId, false, false);
                    break;
                case "unlinkInput":
                    UpdateQuarryConnection(player, uc.quarryId, false, true);
                    break;
                case "searchQuarry":
                    searchPhrase = cmdArgs.Length == 1 ? string.Empty : string.Join(' ', cmdArgs.Skip(1));
                    uc.quarrySearch = searchPhrase;
                    UpdateSortButtons(player);
                    break;
                case "openUpgrade":
                    int quarryId = int.Parse(cmdArgs[1]);
                    uc.quarryId = quarryId;
                    OpenQuarryStoragePanel(player, quarryId);
                    break;
                case "closeUpgrade":
                    if (uc.lastLootedLinkedStorage == 0) return;
                    DrawLinkedQuarriesUI(player, uc.lastLootedLinkedStorage);
                    break;
            }
        }

        private void UpdateQuarryConnection(BasePlayer player, int quarryId, bool add, bool input)
        {
            if (add)
            {
                awaitingConnections[player.userID] = (quarryId, input);
                CuiHelper.DestroyUi(player, "QuarriesUI");
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("QuarryLinkStarted", player.UserIDString));
                timer.Once(30f, () =>
                {
                    if (awaitingConnections.TryGetValue(player.userID, out var connData) && connData.Item1 == quarryId)
                        awaitingConnections.Remove(player.userID);
                });
            }
            else
            {
                QuarryData qd = data.quarries[quarryId];
                VirtualQuarry vq = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.netId)).GetComponent<VirtualQuarry>();
                if (input)
                    qd.fuelNetId = 0;
                else
                    qd.redirectNetId = 0;
                vq.SetupQuarry(quarryId);
                RedrawQuarryDetails(player, quarryId, quarryId);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("QuarryUnlinked", player.UserIDString));
            }
        }

        private void VirtualQuarriesCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (config.requirePermission && !permission.UserHasPermission(player.UserIDString, "virtualquarries.use"))
            {
                Mess(player, "NoPermission");
                return;
            }
            DrawVirtualQuarriesUI(player);
        }

        #endregion

        #region Function Methods

        private int CreateNewQuarry(BasePlayer player)
        {
            UiCache uc = cache[player.userID];
            int quarryId = ++data.quarryCount;
            data.quarries[quarryId] = new QuarryData()
            {
                owner = player.userID,
                resources = new(uc.cachedSurvey.resources),
                profile = uc.cachedSurvey.profile,
            };
            VirtualQuarry vq = SpawnQuarryStorage(quarryId, false);
            if (!config.quarryProfiles[uc.cachedSurvey.profile].enableInputLink)
                SpawnQuarryStorage(quarryId, true);
            vq.SetupQuarry(quarryId);
            return quarryId;
        }

        private int CreateNewStaticQuarry(BasePlayer player, MiningQuarry quarry)
        {
            int quarryId = ++data.quarryCount;
            data.quarries[quarryId] = new QuarryData()
            {
                owner = player.userID,
                quarryType = QuarryType.Static,
                staticNetId = quarry.net.ID.Value
            };
            VirtualQuarry vq = SpawnQuarryStorage(quarryId, false);
            SpawnQuarryStorage(quarryId, true);
            vq.SetupQuarry(quarryId);
            return quarryId;
        }

        private int CreateNewExcavatorQuarry(BasePlayer player, ExcavatorArm arm)
        {
            int quarryId = ++data.quarryCount;
            data.quarries[quarryId] = new QuarryData()
            {
                owner = player.userID,
                quarryType = QuarryType.Excavator,
                staticNetId = arm.net.ID.Value
            };
            VirtualQuarry vq = SpawnQuarryStorage(quarryId, false);
            SpawnQuarryStorage(quarryId, true);
            SpawnQuarryStorage(quarryId, BoxType.Output2);
            vq.SetupQuarry(quarryId);
            return quarryId;
        }

        private void ShowQuarryAccessList(BasePlayer player)
        {
            UiCache uc = cache[player.userID];
            QuarryData qd = data.quarries[uc.quarryId];
            if (qd.owner != player.userID) return;
            if (qd.authPlayers.Count == 0)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("AccessListNoAdded", player.UserIDString), 12);
                return;
            }
            StringBuilder sb = Pool.Get<StringBuilder>();
            sb.Clear().Append(Lang("AccessListStart", player.UserIDString));
            foreach (var playerId in qd.authPlayers)
                sb.Append("\n - <color=#5c81ed>").Append(playerCache[playerId]).Append("</color>");
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, sb.ToString(), 12);
            Pool.FreeUnmanaged(ref sb);
        }

        private void TryUpgradeQuarry(BasePlayer player, int upgradeType) // 0 - Items, 1 - Currency, 2 - Both
        {
            UiCache uc = cache[player.userID];
            QuarryData qd = data.quarries[uc.quarryId];
            if (qd.owner != player.userID && !qd.authPlayers.Contains(player.userID)) return;
            bool isVirtual = qd.quarryType == QuarryType.Virtual;
            QuarryProfile qp = null;
            if (isVirtual && !config.quarryProfiles.TryGetValue(qd.profile, out qp))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("InvalidSurveyResult", player.UserIDString));
                return;
            }
            List<UpgradeConfig> upgrades;
            if (isVirtual)
                upgrades = qp.upgrades;
            else if (qd.quarryType == QuarryType.Static)
                upgrades = config.staticQuarryUpgrades;
            else
                upgrades = config.excavatorUpgrades;
            if (qd.level >= upgrades.Count - 1) return;
            UpgradeConfig upgCfg = upgrades[qd.level + 1];
            if (upgCfg.requiredPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, upgCfg.requiredPerm))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoPermToUpgrade", player.UserIDString));
                return;
            }
            if (upgradeType == 1)
            {
                if (upgCfg.requiredRp == 0) return;
                ulong userId = player.userID.Get();
                if (config.economyPlugin == 1)
                {
                    if (Economics.Call<double>("Balance", userId) >= upgCfg.requiredRp)
                    {
                        Economics.Call("Withdraw", userId, (double)upgCfg.requiredRp);
                        UpgradeQuarry(player, upgradeType);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 2)
                {
                    if (ServerRewards.Call<int>("CheckPoints", userId) >= upgCfg.requiredRp)
                    {
                        ServerRewards.Call("TakePoints", userId, upgCfg.requiredRp);
                        UpgradeQuarry(player, upgradeType);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 3)
                {
                    if (IQEconomic.Call<int>("API_GET_BALANCE", userId) >= upgCfg.requiredRp)
                    {
                        IQEconomic.Call("API_REMOVE_BALANCE", userId, upgCfg.requiredRp);
                        UpgradeQuarry(player, upgradeType);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 4)
                {
                    if (BankSystem.Call<int>("Balance", userId) >= upgCfg.requiredRp)
                    {
                        BankSystem.Call("Withdraw", userId, upgCfg.requiredRp);
                        UpgradeQuarry(player, upgradeType);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
                else if (config.economyPlugin == 5)
                {
                    if (ShoppyStock.Call<int>("GetCurrencyAmount", config.economyCurrency, userId) >= upgCfg.requiredRp)
                    {
                        ShoppyStock.Call("TakeCurrency", config.economyCurrency, userId, upgCfg.requiredRp);
                        UpgradeQuarry(player, upgradeType);
                    }
                    else
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughCurrency", player.UserIDString));
                }
            }
            else
            {
                DateTime now = DateTime.Now;
                if ((now - lastOperation).TotalSeconds < 1) return;
                if (!TakeItems(player, upgCfg.requiredItems))
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotEnoughItems", player.UserIDString));
                else
                    UpgradeQuarry(player, upgradeType);
                lastOperation = now;
            }
        }

        private void UpgradeQuarry(BasePlayer player, int upgradeType) // 0 - Items, 1 - Currency, 2 - Both
        {
            UiCache uc = cache[player.userID];
            QuarryData qd = data.quarries[uc.quarryId];
            qd.level++;
            bool typeBool = upgradeType == 1;
            qd.upgradedForRp.TryAdd(qd.level, typeBool);
            bool isVirtual = qd.quarryType == QuarryType.Virtual;
            QuarryProfile qp = null;
            List<UpgradeConfig> upgrades;
            if (isVirtual)
            {
                if (!config.quarryProfiles.TryGetValue(qd.profile, out qp))
                {
                    PrintWarning($"VirtualQuarries: Missing profile '{qd.profile}' for quarry {uc.quarryId} during upgrade.");
                    return;
                }
                upgrades = qp.upgrades;
            }
            else if (qd.quarryType == QuarryType.Static)
            {
                upgrades = config.staticQuarryUpgrades;
            }
            else
            {
                upgrades = config.excavatorUpgrades;
            }

            if (qd.level < 0 || qd.level >= upgrades.Count)
            {
                PrintWarning($"VirtualQuarries: Upgrade level {qd.level} out of range for quarry {uc.quarryId}.");
                return;
            }

            UpgradeConfig thisUpgrade = upgrades[qd.level];
            BoxStorage storage = GetQuarryBox(uc.quarryId, BoxType.Core);
            if (!storage) return;
            if (isVirtual && !qp.enableOutputLink)
            {
                storage.inventory.capacity = thisUpgrade.capacity;
                storage.SendNetworkUpdate();
            }
            if (isVirtual && !qp.enableInputLink)
            {
                BoxStorage fuelStorage = GetQuarryBox(uc.quarryId, BoxType.Fuel);
                if (fuelStorage)
                {
                    fuelStorage.inventory.capacity = thisUpgrade.fuelCapacity;
                    fuelStorage.SendNetworkUpdate();
                }
            }
            if (qd.quarryType == QuarryType.Excavator)
            {
                storage.inventory.capacity = thisUpgrade.capacity;
                storage.SendNetworkUpdate();
                BoxStorage output2 = GetQuarryBox(uc.quarryId, BoxType.Output2);
                if (output2)
                {
                    output2.inventory.capacity = thisUpgrade.capacity;
                    output2.SendNetworkUpdate();
                }
                BoxStorage excavatorFuel = GetQuarryBox(uc.quarryId, BoxType.Fuel);
                if (excavatorFuel)
                {
                    excavatorFuel.inventory.capacity = thisUpgrade.fuelCapacity;
                    excavatorFuel.SendNetworkUpdate();
                }
            }
            bool anyNewResource = false;
            if (isVirtual && thisUpgrade.additionalResources.Count > 0)
            {
                foreach (var resourceKey in thisUpgrade.additionalResources)
                {
                    if (!qp.resources.TryGetValue(resourceKey, out var resource)) continue;
                    if (resource.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, resource.permission)) continue;
                    qd.resources.Add(new() { configKey = resourceKey, work = Core.Random.Range(resource.outputMin, resource.outputMax) });
                    anyNewResource = true;
                }
            }
            if (anyNewResource)
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NewResourceDug", player.UserIDString));
            storage.GetComponent<VirtualQuarry>().SetupQuarry(uc.quarryId);
            SendEffect(player, "assets/prefabs/deployable/quarry/effects/mining-quarry-deploy.prefab");
            SendEffect(player, "assets/bundled/prefabs/fx/build/promote_toptier.prefab");
            Interface.CallHook("OnQuarryUpgraded", player, qd.level, qd.profile);
            if (isVirtual && !qp.enableOutputLink)
                OpenQuarry(player, false, true);
            else
                OpenQuarryStoragePanel(player, uc.quarryId);
            if (config.consoleLogs)
                Puts($"Player {player.displayName} ({player.userID}) upgraded his quarry with ID {uc.quarryId} to level {qd.level + 1}.");
        }

        private void ManageQuarryPlayer(BasePlayer player, ulong userId)
        {
            UiCache uc = cache[player.userID];
            bool allQuarries = uc.userManageQuarryId == -1;
            bool removeUser = uc.userManageRemove;
            if (!removeUser && config.shareClanOnly && (player.Team == null || !player.Team.members.Contains(userId)))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("OnlyTeamShare", player.UserIDString));
                return;
            }
            CuiHelper.DestroyUi(player, "QuarriesUI_UsersUI");
            if (allQuarries)
            {
                foreach (var quarry in data.quarries.Values)
                {
                    if (quarry.owner == player.userID)
                    {
                        if (removeUser)
                            quarry.authPlayers.Remove(userId);
                        else if (!removeUser && !quarry.authPlayers.Contains(userId))
                            quarry.authPlayers.Add(userId);
                    }
                }
                if (removeUser)
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("RemovedFromAllQuarries", player.UserIDString, playerCache[userId]));
                else
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("SharedAllQuarries", player.UserIDString, playerCache[userId]));
                return;
            }
            QuarryData qd = data.quarries[uc.userManageQuarryId];
            if (removeUser)
            {
                qd.authPlayers.Remove(userId);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("UserRemoved", player.UserIDString, playerCache[userId]));
            }
            else
            {
                qd.authPlayers.Add(userId);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("UserAdded", player.UserIDString, playerCache[userId]));
            }
        }

        private void QuickCollectResources(BasePlayer player)
        {
            UiCache uc = cache[player.userID];
            QuarryData qd = data.quarries[uc.quarryId];
            if (qd.owner != player.userID && !qd.authPlayers.Contains(player.userID)) return;
            BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.netId)) as BoxStorage;
            if (!storage) return;
            int itemCount = 0;
            foreach (var item in storage.inventory.itemList.ToArray())
            {
                if (!item.MoveToContainer(player.inventory.containerMain))
                    if (!item.MoveToContainer(player.inventory.containerBelt))
                        break;
                itemCount++;
            }
            if (qd.quarryType == QuarryType.Excavator)
            {
                BoxStorage output2 = GetQuarryBox(uc.quarryId, BoxType.Output2);
                if (output2)
                {
                    foreach (var item in output2.inventory.itemList.ToArray())
                    {
                        if (!item.MoveToContainer(player.inventory.containerMain))
                            if (!item.MoveToContainer(player.inventory.containerBelt))
                                break;
                        itemCount++;
                    }
                }
            }
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("MovedItemsToYourInventory", player.UserIDString, itemCount));
        }


        private void RemoveQuarry(BasePlayer player)
        {
            UiCache uc = cache[player.userID];
            if (!data.quarries.TryGetValue(uc.quarryId, out var qd))
            {
                Puts($"[VirtualQuarries ERROR] Player {player.displayName} ({player.UserIDString}) is trying to remove quarry more than once!");
                return;
            }
            if (qd.owner != player.userID) return;
            QuarryProfile qp = config.quarryProfiles[qd.profile];
            BoxStorage quarry = GetQuarryBox(uc.quarryId, BoxType.Core);
            ulong userId = player.userID.Get();
            bool isRedeem = RedeemStorageAPI != null;
            if (!qp.enableOutputLink)
                foreach (var item in quarry.inventory.itemList.ToList())
                {
                    if (isRedeem)
                        RedeemStorageAPI?.Call("AddItem", userId, config.redeemInventoryName, item);
                    else if (!item.MoveToContainer(player.inventory.containerMain))
                        if (!item.MoveToContainer(player.inventory.containerBelt))
                            item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                }
            BoxStorage quarryFuel = GetQuarryBox(uc.quarryId, BoxType.Fuel);
            if (!qp.enableInputLink)
                foreach (var item in quarryFuel.inventory.itemList.ToList())
                {
                    if (isRedeem)
                        RedeemStorageAPI?.Call("AddItem", userId, config.redeemInventoryName, item);
                    else if (!item.MoveToContainer(player.inventory.containerMain))
                        if (!item.MoveToContainer(player.inventory.containerBelt))
                            item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                }
            if (config.refundRemove)
            {
                foreach (var refundItem in qp.requiredItems)
                {
                    Item item = ItemManager.CreateByName(refundItem.shortname, refundItem.amount, refundItem.skin);
                    if (!string.IsNullOrEmpty(refundItem.displayName))
                        item.name = refundItem.displayName;
                    {
                        if (isRedeem)
                            RedeemStorageAPI?.Call("AddItem", userId, config.redeemInventoryName, item);
                        else if (!item.MoveToContainer(player.inventory.containerMain))
                            if (!item.MoveToContainer(player.inventory.containerBelt))
                                item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                    }
                }
                foreach (var resource in qd.resources)
                {
                    if (!qp.resources.TryGetValue(resource.configKey, out var value)) continue;
                    foreach (var refundItem in value.additionalItems)
                    {
                        Item item = ItemManager.CreateByName(refundItem.shortname, refundItem.amount, refundItem.skin);
                        if (!string.IsNullOrEmpty(refundItem.displayName))
                            item.name = refundItem.displayName;
                        {
                            if (isRedeem)
                                RedeemStorageAPI?.Call("AddItem", userId, config.redeemInventoryName, item);
                            else if (!item.MoveToContainer(player.inventory.containerMain))
                                if (!item.MoveToContainer(player.inventory.containerBelt))
                                    item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                        }
                    }
                }
            }
            if (config.refundUpgrades)
            {
                foreach (var upgrade in qd.upgradedForRp)
                {
                    if (qp.upgrades.Count - 1 >= upgrade.Key)
                    {
                        UpgradeConfig upgCfg = qp.upgrades[upgrade.Key];
                        if (upgrade.Value)
                        {
                            if (config.economyPlugin == 1)
                                Economics?.Call("Deposit", userId, (double)upgCfg.requiredRp);
                            else if (config.economyPlugin == 2)
                                ServerRewards?.Call("AddPoints", userId, upgCfg.requiredRp);
                            else if (config.economyPlugin == 4)
                                BankSystem?.Call("Deposit", userId, upgCfg.requiredRp);
                            else if (config.economyPlugin == 5)
                                ShoppyStock?.Call("GiveCurrency", config.economyCurrency, userId, upgCfg.requiredRp);
                        }
                        else
                        {
                            foreach (var refundItem in upgCfg.requiredItems)
                            {
                                Item item = ItemManager.CreateByName(refundItem.shortname, refundItem.amount, refundItem.skin);
                                if (!string.IsNullOrEmpty(refundItem.displayName))
                                    item.name = refundItem.displayName;
                                if (isRedeem)
                                    RedeemStorageAPI?.Call("AddItem", userId, config.redeemInventoryName, item);
                                else if (!item.MoveToContainer(player.inventory.containerMain))
                                    if (!item.MoveToContainer(player.inventory.containerBelt))
                                        item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                            }
                        }
                    }
                }
            }
            Interface.CallHook("OnQuarryRemoved", player, qd.profile);
            quarry.Kill();
            if (!qp.enableInputLink)
                quarryFuel.Kill();
            if (qd.quarryType == QuarryType.Excavator)
            {
                BoxStorage output2 = GetQuarryBox(uc.quarryId, BoxType.Output2);
                if (output2)
                    output2.Kill();
            }
            if (config.consoleLogs)
                Puts($"Player {player.displayName} ({player.userID}) removed quarry with ID {uc.quarryId} with level {qd.level}.");
            data.quarries.Remove(uc.quarryId);
            UpdateNewQuarryRecord(player, -1);
            CuiHelper.DestroyUi(player, "QuarriesUI_RemovePopUp");
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("QuarryRemoved", player.UserIDString));
        }

        // Merges all matching limit-permission entries (max per key) so default + VIP does not leave the lower cap.
        private Dictionary<string, int> GetMergedMiningLimits(BasePlayer player)
        {
            Dictionary<string, int> merged = null;
            foreach (var configPerm in config.permissions)
            {
                if (!permission.UserHasPermission(player.UserIDString, configPerm.Key))
                    continue;
                if (merged == null)
                {
                    merged = new Dictionary<string, int>(configPerm.Value);
                    continue;
                }
                foreach (var kv in configPerm.Value)
                {
                    if (!merged.TryGetValue(kv.Key, out int existing) || kv.Value > existing)
                        merged[kv.Key] = kv.Value;
                }
            }
            return merged;
        }

        private void TryPlaceQuarry(BasePlayer player)
        {
            UiCache uc = cache[player.userID];
            if (uc.cachedSurvey.resources.Count == 0) return;
            if (string.IsNullOrEmpty(uc.cachedSurvey.profile) || uc.cachedSurvey.profile == "-" ||
                !config.quarryProfiles.ContainsKey(uc.cachedSurvey.profile))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("InvalidSurveyResult", player.UserIDString));
                return;
            }
            Dictionary<string, int> playerPerm = GetMergedMiningLimits(player);
            if (playerPerm == null)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotAllowedToPlace", player.UserIDString));
                return;
            }
            Dictionary<string, int> quarryCount = Pool.Get<Dictionary<string, int>>();
            quarryCount.Clear();
            foreach (var quarry in data.quarries.Values)
            {
                if (quarry.owner != player.userID) continue;
                if (string.IsNullOrEmpty(quarry.profile)) continue;
                quarryCount.TryAdd(quarry.profile, 0);
                quarryCount[quarry.profile]++;
            }
            if (playerPerm.TryGetValue("*", out int value))
            {
                int summedQuarries = quarryCount.Sum(x => x.Value);
                if (summedQuarries >= value)
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("TooManyQuarries", player.UserIDString));
                    return;
                }
            }
            if (quarryCount.TryGetValue(uc.cachedSurvey.profile, out int placedCount) && playerPerm.TryGetValue(uc.cachedSurvey.profile, out int profileLimit) && profileLimit <= placedCount)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("TooManyQuarries", player.UserIDString));
                return;
            }
            Pool.FreeUnmanaged(ref quarryCount);
            List<RequiredItem> reqItems = Pool.Get<List<RequiredItem>>();
            reqItems.Clear();
            QuarryProfile qp = config.quarryProfiles[uc.cachedSurvey.profile];
            reqItems.AddRange(qp.requiredItems);
            foreach (var res in uc.cachedSurvey.resources)
                reqItems.AddRange(qp.resources[res.configKey].additionalItems);
            if (!TakeItems(player, reqItems))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoRequiredItems", player.UserIDString));
                Pool.FreeUnmanaged(ref reqItems);
                return;
            }
            Pool.FreeUnmanaged(ref reqItems);
            SendEffect(player, "assets/prefabs/deployable/quarry/effects/mining-quarry-deploy.prefab");
            Interface.CallHook("OnQuarryPlaced", player, uc.cachedSurvey.profile);
            int newId = CreateNewQuarry(player);
            CuiHelper.DestroyUi(player, "QuarriesUI_QuarryRoll");
            UpdateNewQuarryRecord(player, newId);
            uc.cachedSurvey.resources.Clear();
            uc.cachedSurvey.profile = string.Empty;
        }

        private void SaveContainers()
        {
            if (config.consoleLogs)
                Puts("Saving quarry containers...");
            int containerCount = 0;
            foreach (var quarry in data.quarries)
            {
                BoxStorage resources = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.Value.netId)) as BoxStorage;
                if (!resources) continue;
                SaveItems(resources, quarry.Key, "resource");
                if (resources.transform.childCount == 0)
                {
                    Puts($"An error occured while trying to save quarry no. {quarry.Key}. Make sure you don't have any plugin that destroys some entities on map like custom decay plugin or offline players purge.");
                    continue;
                }
                BoxStorage fuel = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.Value.fuelNetId)) as BoxStorage;
                if (!fuel) continue;
                SaveItems(fuel, quarry.Key, "fuel");
                if (quarry.Value.quarryType == QuarryType.Excavator && quarry.Value.outputNetId2 != 0)
                {
                    BoxStorage output2 = BaseNetworkable.serverEntities.Find(new NetworkableId(quarry.Value.outputNetId2)) as BoxStorage;
                    if (output2)
                        SaveItems(output2, quarry.Key, "resource2");
                }
                containerCount++;
            }
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/storageCache", storageCache);
            if (config.consoleLogs)
                Puts($"Successfully saved {containerCount} quarry containers!");

        }

        private void SaveItems(BoxStorage storage, int quarryId, string dataType)
        {
            storageCache.TryAdd(quarryId, new StorageData());
            if (dataType == "resource")
            {
                storageCache[quarryId].resource = new List<RequiredItem>();
                foreach (var item in storage.inventory.itemList.ToList())
                {
                    string name = string.IsNullOrEmpty(item.name) ? null : item.name;
                    storageCache[quarryId].resource.Add(new RequiredItem()
                    {
                        shortname = item.info.shortname,
                        skin = item.skin,
                        amount = item.amount,
                        displayName = name
                    });
                }
            }
            else if (dataType == "resource2")
            {
                storageCache[quarryId].resource2 = new List<RequiredItem>();
                foreach (var item in storage.inventory.itemList.ToList())
                {
                    string name = string.IsNullOrEmpty(item.name) ? null : item.name;
                    storageCache[quarryId].resource2.Add(new RequiredItem()
                    {
                        shortname = item.info.shortname,
                        skin = item.skin,
                        amount = item.amount,
                        displayName = name
                    });
                }
            }
            else
            {
                storageCache[quarryId].fuel = new List<RequiredItem>();
                foreach (var item in storage.inventory.itemList.ToList())
                {
                    string name = string.IsNullOrEmpty(item.name) ? null : item.name;
                    storageCache[quarryId].fuel.Add(new RequiredItem()
                    {
                        shortname = item.info.shortname,
                        skin = item.skin,
                        amount = item.amount,
                        displayName = name
                    });
                }
            }
        }

        private enum BoxType
        {
            Core,
            Fuel,
            Output2
        }

        private static ulong GetBoxNetId(QuarryData qd, BoxType type)
        {
            if (type == BoxType.Fuel) return qd.fuelNetId;
            if (type == BoxType.Output2) return qd.outputNetId2;
            return qd.netId;
        }

        private BoxStorage GetQuarryBox(int quarryId, BoxType type)
        {
            if (!data.quarries.TryGetValue(quarryId, out var qd) || qd == null) return null;
            bool isLink = false;
            if (qd.quarryType == QuarryType.Virtual && type == BoxType.Fuel &&
                config.quarryProfiles != null && config.quarryProfiles.TryGetValue(qd.profile, out var linkProfile) && linkProfile != null)
                isLink = linkProfile.enableInputLink;
            if (type == BoxType.Output2 && qd.quarryType != QuarryType.Excavator) return null;
            ulong netId = GetBoxNetId(qd, type);
            BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as BoxStorage;
            if (storage) return storage;
            if (isLink) return null;
            SpawnQuarryStorage(quarryId, type, setup: type != BoxType.Output2);
            storage = BaseNetworkable.serverEntities.Find(new NetworkableId(GetBoxNetId(data.quarries[quarryId], type))) as BoxStorage;
            if (storage) return storage;
            return null;
        }

        private void CheckQuarryKicks()
        {
            DateTime now = DateTime.Now;
            int cleared = 0;
            foreach (var quarry in data.quarries.Values)
            {
                if (quarry.lastOwnerOnline == DateTime.MinValue) continue;
                if ((now - quarry.lastOwnerOnline).TotalDays > config.offlineQuarryOwnerKickTime)
                {
                    quarry.authPlayers.Clear();
                    cleared++;
                }
            }
            if (cleared > 0)
                Puts($"Cleared authorization in {cleared} quarries due to long inactivity of the quarry owner.");
        }

        private static readonly Dictionary<MiningQuarry.QuarryType, int> convertType = new()
        {
            { MiningQuarry.QuarryType.None, 0 },
            { MiningQuarry.QuarryType.Basic, 0 },
            { MiningQuarry.QuarryType.Sulfur, 1 },
            { MiningQuarry.QuarryType.HQM, 2 }
        };

        //0 - Metal, 1 - Sulfur, 2 - HQM, 3 - Pump Jack, 4 - Excavator 
        private static bool IsStaticResourceOutput(Item item, int quarryType)
        {
            if (item.GetOwnerPlayer()) return false;
            switch (quarryType)
            {
                case 0:
                    foreach (var res in config.staticMetalOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 1:
                    foreach (var res in config.staticSulfurOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 2:
                    foreach (var res in config.staticHqmOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 3:
                    foreach (var res in config.staticPumpJackOutput)
                        if (item.info.shortname == res.shortname && item.skin == res.skin) return true;
                    break;
                case 4:
                    foreach (var res in config.excavatorResources)
                        foreach (var res2 in res.Value)
                            if (item.info.shortname == res2.shortname && item.skin == res2.skin) return true;
                    break;
            }
            return false;
        }

        private float GetFuelCount(BoxStorage storage, QuarryProfile profile)
        {
            float fuelAmount = 0;
            foreach (var fuel in profile.fuelItems)
                foreach (var item in storage.inventory.itemList)
                    if (item.info.shortname == fuel.shortname && item.skin == fuel.skin)
                        fuelAmount += item.amount / fuel.amount;
            return fuelAmount;
        }

        private int GetPlayerItemCount(BasePlayer player, RequiredItem reqItem)
        {
            int amount = 0;
            foreach (var item in player.inventory.containerMain.itemList)
                if (item.skin == reqItem.skin && item.info.shortname == reqItem.shortname)
                    amount += item.amount;
            foreach (var item in player.inventory.containerBelt.itemList)
                if (item.skin == reqItem.skin && item.info.shortname == reqItem.shortname)
                    amount += item.amount;
            return amount;
        }

        private int ThrowsRequired(float p, string surveyKey)
        {
            if (p <= 0f) return int.MaxValue;
            if (p >= 1f) return 1;
            float throws = Mathf.Log(1f - config.surveys[surveyKey].rollProbabilityValue) / Mathf.Log(1f - p);
            return throws >= int.MaxValue ? int.MaxValue : Mathf.CeilToInt(throws);
        }

        private int GetRequiredSurveyCountCombo(BasePlayer player, string surveyKey, List<string> forbiddenKeys, int minHits = -1)
        {
            UiCache uc = cache[player.userID];
            int resolvedHits = minHits <= 0 ? 1 : minHits;

            float pGate = config.surveys[surveyKey].resourceChance / 100f;
            int totalProfileChance = GetTotalProfileChance(player, surveyKey);
            if (totalProfileChance == 0) return int.MaxValue;

            float pSuccess = 0f;
            float quarryMult = 60f / config.quarryTick;

            foreach (var profileKV in config.quarryProfiles)
            {
                var profile = profileKV.Value;
                if (!IsProfileEligible(player, profile, surveyKey)) continue;

                float pProfile = (float)profile.chance / totalProfileChance;

                bool impossible = false;
                int alwaysCount = 0;
                List<float> alwaysHitProbs = Pool.Get<List<float>>();
                foreach (var resourceKV in profile.resources)
                {
                    var res = resourceKV.Value;
                    if (!res.alwaysInclude || !IsResourceEligible(player, res)) continue;
                    if (uc.autoRollDisabledResources.Contains(resourceKV.Key)) { impossible = true; break; }
                    alwaysCount++;
                    if (uc.autoRollDisabledResources.Contains(resourceKV.Key)) continue;
                    alwaysHitProbs.Add(PAtLeastTargetFloat(res.outputMin * quarryMult, res.outputMax * quarryMult,
                        GetTarget(uc, resourceKV.Key, res)));
                }
                if (impossible)
                {
                    Pool.FreeUnmanaged(ref alwaysHitProbs);
                    continue;
                }

                var pool = BuildPool(player, uc, profile);
                int poolTotalChance = 0;
                foreach (var e in pool) poolTotalChance += e.chance;

                int minLoop = Mathf.Max(0, profile.minPerNode - alwaysCount);
                int maxLoop = Mathf.Min(Mathf.Max(0, profile.maxPerNode - alwaysCount), pool.Count);
                minLoop = Mathf.Min(minLoop, maxLoop);
                int loopRange = profile.maxPerNode - profile.minPerNode + 1;

                for (int k = minLoop; k <= maxLoop; k++)
                {
                    pSuccess += pProfile / loopRange * ComboDP(
                        pool, uc.autoRollDisabledResources,
                        alwaysHitProbs, resolvedHits, k, poolTotalChance);
                }
                Pool.FreeUnmanaged(ref alwaysHitProbs);
            }

            return ThrowsRequired(pGate * pSuccess, surveyKey);
        }

        private int GetEnabledResourceCount(BasePlayer player, string surveyKey)
        {
            UiCache uc = cache[player.userID];
            int best = -1, bestCount = 0;
            foreach (var profileKV in config.quarryProfiles)
            {
                var profile = profileKV.Value;
                if (!IsProfileEligible(player, profile, surveyKey)) continue;
                if (profile.chance <= best) continue;

                int alwaysEnabled = 0;
                foreach (var resourceKV in profile.resources)
                {
                    var res = resourceKV.Value;
                    if (!res.alwaysInclude || !IsResourceEligible(player, res)) continue;
                    if (!uc.autoRollDisabledResources.Contains(resourceKV.Key)) alwaysEnabled++;
                }

                int poolEnabled = 0;
                foreach (var resourceKV in profile.resources)
                {
                    var res = resourceKV.Value;
                    if (res.alwaysInclude || !IsResourceEligible(player, res)) continue;
                    if (!uc.autoRollDisabledResources.Contains(resourceKV.Key)) poolEnabled++;
                }

                int drawSlots = profile.maxPerNode - alwaysEnabled;
                int count = alwaysEnabled + Mathf.Min(poolEnabled, Mathf.Max(0, drawSlots));
                if (count == 0) continue;
                best = profile.chance;
                bestCount = count;
            }
            return bestCount;
        }

        private float ComboDP(
            List<(string key, int chance, float pOutputOk)> pool,
            List<string> disabledKeys,
            List<float> alwaysHitProbs,
            int minHits, int k, int totalChance)
        {
            int n = pool.Count;

            int forbiddenMask = 0;
            for (int i = 0; i < n; i++)
                if (disabledKeys.Contains(pool[i].key)) forbiddenMask |= (1 << i);

            int maxPoolHits = 0;
            for (int i = 0; i < n; i++)
                if ((forbiddenMask & (1 << i)) == 0 &&
                    !disabledKeys.Contains(pool[i].key) && pool[i].pOutputOk > 0f) maxPoolHits++;
            if (alwaysHitProbs.Count + maxPoolHits < minHits) return 0f;

            float[] dp = new float[1 << n];
            dp[0] = 1f;
            for (int draw = 0; draw < k; draw++)
            {
                float[] next = new float[1 << n];
                for (int mask = 0; mask < (1 << n); mask++)
                {
                    if (dp[mask] == 0f) continue;
                    int remChance = totalChance;
                    for (int i = 0; i < n; i++)
                        if ((mask & (1 << i)) != 0) remChance -= pool[i].chance;
                    if (remChance <= 0) continue;
                    for (int i = 0; i < n; i++)
                    {
                        if ((mask & (1 << i)) != 0 || (forbiddenMask & (1 << i)) != 0) continue;
                        next[mask | (1 << i)] += dp[mask] * pool[i].chance / (remChance + 1);
                    }
                }
                dp = next;
            }

            int maxAlways = alwaysHitProbs.Count;
            float[] alwaysDist = new float[maxAlways + 1];
            alwaysDist[0] = 1f;
            foreach (float pH in alwaysHitProbs)
            {
                float[] next = new float[maxAlways + 1];
                for (int h = 0; h <= maxAlways; h++)
                {
                    if (alwaysDist[h] == 0f) continue;
                    next[h] += alwaysDist[h] * (1f - pH);
                    if (h < maxAlways) next[h + 1] += alwaysDist[h] * pH;
                }
                alwaysDist = next;
            }

            float result = 0f;
            for (int mask = 0; mask < (1 << n); mask++)
            {
                if (dp[mask] == 0f || (mask & forbiddenMask) != 0) continue;

                int drawnCount = 0;
                for (int i = 0; i < n; i++) if ((mask & (1 << i)) != 0) drawnCount++;

                float[] poolDist = new float[drawnCount + 1];
                poolDist[0] = 1f;
                int slot = 0;
                for (int i = 0; i < n; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    float pH = (!disabledKeys.Contains(pool[i].key) && pool[i].pOutputOk > 0f)
                        ? pool[i].pOutputOk : 0f;
                    float[] next = new float[drawnCount + 1];
                    for (int h = 0; h <= slot; h++)
                    {
                        if (poolDist[h] == 0f) continue;
                        next[h] += poolDist[h] * (1f - pH);
                        if (h < drawnCount) next[h + 1] += poolDist[h] * pH;
                    }
                    poolDist = next;
                    slot++;
                }

                for (int ph = 0; ph <= drawnCount; ph++)
                {
                    if (poolDist[ph] == 0f) continue;
                    int needed = minHits - ph;
                    for (int ah = Mathf.Max(0, needed); ah <= maxAlways; ah++)
                        result += dp[mask] * poolDist[ph] * alwaysDist[ah];
                }
            }
            return result;
        }

        public float GetTargetSelectionWeight(BasePlayer player, string surveyKey, string profileKey, string resourceKey)
        {
            UiCache uc = cache[player.userID];
            if (!config.quarryProfiles.TryGetValue(profileKey, out var profile)) return 0f;
            if (!profile.resources.TryGetValue(resourceKey, out var res)) return 0f;
            if (!IsResourceEligible(player, res)) return 0f;
            if (uc.autoRollDisabledResources.Contains(resourceKey)) return 0f;

            float pOut = PAtLeastTargetFloat(res.outputMin, res.outputMax, GetTarget(uc, resourceKey, res));
            float baseWeight = res.alwaysInclude ? (res.chance + 1000f) : res.chance;
            return baseWeight * pOut;
        }

        private int CountAlways(BasePlayer player, UiCache uc, QuarryProfile profile,
            Dictionary<string, float> pPerResource, float pProfile)
        {
            int count = 0;
            foreach (var resourceKV in profile.resources)
            {
                var res = resourceKV.Value;
                if (!res.alwaysInclude || !IsResourceEligible(player, res)) continue;
                count++;
                if (uc.autoRollDisabledResources.Contains(resourceKV.Key)) continue;
                float pOut = PAtLeastTargetFloat(res.outputMin, res.outputMax,
                    GetTarget(uc, resourceKV.Key, res));
                AddToDict(pPerResource, resourceKV.Key, pProfile * pOut);
            }
            return count;
        }

        private int GetTotalProfileChance(BasePlayer player, string surveyKey)
        {
            int total = 0;
            foreach (var profile in config.quarryProfiles.Values)
                if (IsProfileEligible(player, profile, surveyKey)) total += profile.chance;
            return total;
        }

        private bool IsProfileEligible(BasePlayer player, QuarryProfile profile, string surveyKey)
            => profile.surveyType == surveyKey &&
               (profile.permission == string.Empty ||
                permission.UserHasPermission(player.UserIDString, profile.permission));

        private bool IsResourceEligible(BasePlayer player, ResourceConfig res)
            => res.permission == string.Empty ||
               permission.UserHasPermission(player.UserIDString, res.permission);

        private List<(string key, int chance, float pOutputOk)> BuildPool(
            BasePlayer player, UiCache uc, QuarryProfile profile)
        {
            var pool = new List<(string key, int chance, float pOutputOk)>();
            foreach (var resourceKV in profile.resources)
            {
                var res = resourceKV.Value;
                if (res.alwaysInclude || !IsResourceEligible(player, res)) continue;
                float pOut = uc.autoRollDisabledResources.Contains(resourceKV.Key) ? 0f
                    : PAtLeastTargetFloat(res.outputMin, res.outputMax, GetTarget(uc, resourceKV.Key, res));
                pool.Add((resourceKV.Key, res.chance, pOut));
            }
            return pool;
        }

        private float PAtLeastTargetFloat(float min, float max, float target)
        {
            if (max <= min) return 0f;
            return Mathf.Clamp01((max - Mathf.Clamp(target, min, max)) / (max - min));
        }

        private float GetTarget(UiCache uc, string key, ResourceConfig res)
        {
            return uc.autoRollValues.TryGetValue(key, out float t) ? t / (60f / config.quarryTick) : (res.outputMin + (res.outputMax - res.outputMin) / 2f) * (60f / config.quarryTick);
        }

        private void AddToDict(Dictionary<string, float> dict, string key, float value)
        {
            if (!dict.TryAdd(key, value))
                dict[key] += value;
        }

        private float ExactPSelected(
            List<(string key, int chance, float pOutputOk)> pool,
            string targetKey, int k, int totalChance)
        {
            int n = pool.Count;
            float[] dp = new float[1 << n];
            dp[0] = 1f;
            for (int draw = 0; draw < k; draw++)
            {
                float[] next = new float[1 << n];
                for (int mask = 0; mask < (1 << n); mask++)
                {
                    if (dp[mask] == 0f) continue;
                    int remChance = totalChance;
                    for (int i = 0; i < n; i++)
                        if ((mask & (1 << i)) != 0) remChance -= pool[i].chance;
                    if (remChance <= 0) continue;
                    for (int i = 0; i < n; i++)
                    {
                        if ((mask & (1 << i)) != 0) continue;
                        next[mask | (1 << i)] += dp[mask] * pool[i].chance / (remChance + 1);
                    }
                }
                dp = next;
            }
            int tIdx = pool.FindIndex(r => r.key == targetKey);
            if (tIdx < 0) return 0f;
            float p = 0f;
            for (int mask = 0; mask < (1 << n); mask++)
                if ((mask & (1 << tIdx)) != 0) p += dp[mask];
            return p;
        }

        private float PoissonPSelected(int rChance, int k, int totalChance) => 1f - Mathf.Exp(-k * (float)rChance / totalChance);

        private void SendEffect(BasePlayer player, string path)
        {
            List<Connection> connections = Pool.Get<List<Connection>>();
            connections.Clear();
            connections.Add(player.Connection);
            Effect.server.Run(path, player.eyes.position, targets: connections);
            Pool.FreeUnmanaged(ref connections);
        }

        private void OpenQuarry(BasePlayer player, bool input, bool reopen = false)
        {
            UiCache uc = cache[player.userID];
            QuarryData qd = data.quarries[uc.quarryId];
            if (!adminModes.Contains(player.userID) && qd.owner != player.userID && !qd.authPlayers.Contains(player.userID)) return;
            BoxStorage storage = GetQuarryBox(uc.quarryId, input ? BoxType.Fuel : BoxType.Core);
            if (!storage) return;
            if (config.lockAccessNoPerm)
            {
                Dictionary<string, int> playerPerm = GetMergedMiningLimits(player);
                if (playerPerm == null || !playerPerm.TryGetValue(qd.profile, out int limit))
                {
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoLongerProfilePerm", player.UserIDString));
                    return;
                }
                if (config.checkQuarryAmount)
                {
                    int quarryCount = 0;
                    foreach (var quarryCheck in data.quarries)
                    {
                        if (quarryCheck.Value.owner == player.userID && quarryCheck.Value.profile == qd.profile)
                            quarryCount++;
                    }
                    if (quarryCount > limit)
                    {
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("TooManyQuarriesPermMissing", player.UserIDString));
                        return;
                    }
                }
            }
            uc.isUsingPlugin = false;
            player.EndLooting();
            uc.isUsingPlugin = true;
            bool isVirtual = qd.quarryType == QuarryType.Virtual;
            QuarryProfile qp = isVirtual ? config.quarryProfiles[qd.profile] : null;
            List<UpgradeConfig> upgrades;
            if (isVirtual)
                upgrades = qp.upgrades;
            else if (qd.quarryType == QuarryType.Static)
                upgrades = config.staticQuarryUpgrades;
            else
                upgrades = config.excavatorUpgrades;
            if (qd.level < upgrades.Count)
                storage.inventory.capacity = input ? upgrades[qd.level].fuelCapacity : upgrades[qd.level].capacity;
            storage.inventory.canAcceptItem = (_, item, _) => input && IsFuelItem(item, qd.profile);
            if (input)
                OpenQuarryFuelPanel(player, uc.quarryId);
            else
                OpenQuarryStoragePanel(player, uc.quarryId);
            timer.Once(reopen ? 0.3f : 0, () =>
            {
                player.inventory.loot.AddContainer(storage.inventory);
                player.inventory.loot.entitySource = storage;
                player.inventory.loot.PositionChecks = false;
                player.inventory.loot.MarkDirty();
                player.inventory.loot.SendImmediate();
                CuiHelper.DestroyUi(player, "QuarriesUI");
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
                Interface.CallHook("OnLootEntity", player, storage);
            });
        }

        private void OpenStaticQuarry(BasePlayer player, int quarryId, bool input, StorageContainer worldStorage = null)
        {
            cache.TryAdd(player.userID, new());
            UiCache uc = cache[player.userID];
            uc.quarryId = quarryId;
            uc.isUsingPlugin = false;
            QuarryData qd = data.quarries[quarryId];
            if (qd.owner != player.userID) return;
            BoxType boxType = BoxType.Core;
            if (input)
                boxType = BoxType.Fuel;
            else if (qd.quarryType == QuarryType.Excavator && GetExcavatorOutputSlot(worldStorage) > 0)
                boxType = BoxType.Output2;
            BoxStorage storage = GetQuarryBox(quarryId, boxType);
            if (!storage) return;
            VirtualQuarry vq = GetOrCreateVirtualQuarry(quarryId);
            player.EndLooting();
            bool isVirtual = qd.quarryType == QuarryType.Virtual;
            QuarryProfile qp = isVirtual ? config.quarryProfiles[qd.profile] : null;
            List<UpgradeConfig> upgrades;
            if (isVirtual)
                upgrades = qp.upgrades;
            else if (qd.quarryType == QuarryType.Static)
                upgrades = config.staticQuarryUpgrades;
            else
                upgrades = config.excavatorUpgrades;
            if (qd.level < upgrades.Count)
                storage.inventory.capacity = input ? upgrades[qd.level].fuelCapacity : upgrades[qd.level].capacity;
            storage.inventory.canAcceptItem = (_, item, _) => input && IsStaticFuelItem(item, qd.quarryType);
            if (input)
            {
                OpenQuarryFuelPanel(player, quarryId);
                if (vq && storage.inventory.itemList.Count > 0)
                    vq.OnFuelAdded();
            }
            else
                OpenQuarryStoragePanel(player, quarryId);
            player.inventory.loot.AddContainer(storage.inventory);
            player.inventory.loot.entitySource = storage;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.inventory.loot.SendImmediate();
            player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
            Interface.CallHook("OnLootEntity", player, storage);
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("PrivateInventoryInfo", player.UserIDString));
        }

        private VirtualQuarry SpawnQuarryStorage(int quarryId, bool input, bool restore = false, bool setup = false)
        {
            return SpawnQuarryStorage(quarryId, input ? BoxType.Fuel : BoxType.Core, restore, setup);
        }

        private VirtualQuarry SpawnQuarryStorage(int quarryId, BoxType type, bool restore = false, bool setup = false)
        {
            float startX = -World.Size / 1.5f;
            float startZ = World.Size / 1.5f;
            Vector3 newLocation = new Vector3(startX + Core.Random.Range(-50, 50), -400, startZ + Core.Random.Range(-50, 50));
            BoxStorage storage = GameManager.server.CreateEntity(config.storagePrefab, newLocation) as BoxStorage;
            if (!storage)
            {
                Puts($"Prefab {config.storagePrefab} is not valid an BoxStorage prefab. Try different entity!");
                return null;
            }
            GameObject.Destroy(storage.GetComponent<GroundWatch>());
            GameObject.Destroy(storage.GetComponent<DestroyOnGroundMissing>());
            storage.Spawn();
            QuarryData qd = data.quarries[quarryId];
            if (type == BoxType.Fuel)
                qd.fuelNetId = storage.net.ID.Value;
            else if (type == BoxType.Output2)
                qd.outputNetId2 = storage.net.ID.Value;
            else
                qd.netId = storage.net.ID.Value;
            bool isVirtual = qd.quarryType == QuarryType.Virtual;
            QuarryProfile qp = null;
            if (isVirtual)
                config.quarryProfiles?.TryGetValue(qd.profile, out qp);
            List<UpgradeConfig> upgrades;
            if (isVirtual)
                upgrades = qp?.upgrades;
            else if (qd.quarryType == QuarryType.Static)
                upgrades = config.staticQuarryUpgrades;
            else
                upgrades = config.excavatorUpgrades;
            if (upgrades != null && qd.level < upgrades.Count && storage.inventory != null)
            {
                UpgradeConfig uc = upgrades[qd.level];
                storage.inventory.capacity = type == BoxType.Fuel ? uc.fuelCapacity : uc.capacity;
            }
            else if (upgrades == null || qd.level >= upgrades.Count)
                PrintWarning($"Quarry with ID {quarryId} have more upgrades than is added to config! You need to add more levels or remove this quarry from data, or plugin will print errors!");
            VirtualQuarry qr = null;
            if (type == BoxType.Core)
                qr = storage.gameObject.AddComponent<VirtualQuarry>();
            if (setup)
            {
                if (type == BoxType.Core)
                {
                    VirtualQuarry vQuarry = storage.GetComponent<VirtualQuarry>();
                    vQuarry?.SetupQuarry(quarryId);
                }
                else if (type == BoxType.Fuel)
                {
                    var coreEnt = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.netId));
                    var vq = coreEnt ? coreEnt.GetComponent<VirtualQuarry>() : null;
                    vq?.SetupQuarry(quarryId);
                }
            }
            if (config.storeContainers && restore && storageCache.TryGetValue(quarryId, out var sc))
            {
                List<RequiredItem> restoreList = type == BoxType.Fuel ? sc.fuel : type == BoxType.Output2 ? sc.resource2 : sc.resource;
                if (restoreList != null)
                {
                    foreach (var item in restoreList)
                    {
                        Item restoreItem = ItemManager.CreateByName(item.shortname, item.amount, item.skin);
                        if (!string.IsNullOrEmpty(item.displayName))
                            restoreItem.name = item.displayName;
                        restoreItem.MoveToContainer(storage.inventory);
                    }
                }
            }
            if (type != BoxType.Fuel)
                storage.inventory.canAcceptItem = (_, _, _) => false;
            return qr;
        }

        private bool IsFuelItem(Item item, string profile)
        {
            foreach (var fuelItem in config.quarryProfiles[profile].fuelItems)
                if (item.info.shortname == fuelItem.shortname && item.skin == fuelItem.skin)
                    return true;
            return false;
        }

        private VirtualQuarry GetOrCreateVirtualQuarry(int quarryId)
        {
            BoxStorage core = GetQuarryBox(quarryId, BoxType.Core);
            if (!core) return null;
            VirtualQuarry vq = core.GetComponent<VirtualQuarry>();
            if (!vq)
            {
                vq = core.gameObject.AddComponent<VirtualQuarry>();
                vq.SetupQuarry(quarryId);
            }
            return vq;
        }

        private bool IsStaticFuelItem(Item item, QuarryType qt)
        {
            List<FuelItem> fuelItems = qt == QuarryType.Static ? config.staticFuelItems : config.excavatorFuelItems;
            foreach (var fuelItem in fuelItems)
                if (item.info.shortname == fuelItem.shortname && item.skin == fuelItem.skin)
                    return true;
            return false;
        }

        private static bool TakeItems(BasePlayer player, List<RequiredItem> items)
        {
            foreach (var requiredItem in items)
            {
                bool haveRequired = false;
                int inventoryAmount = 0;
                foreach (var item in player.inventory.containerMain.itemList)
                {
                    if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                    {
                        inventoryAmount += item.amount;
                        if (inventoryAmount >= requiredItem.amount)
                        {
                            haveRequired = true;
                            break;
                        }
                    }
                }
                if (!haveRequired)
                {
                    foreach (var item in player.inventory.containerBelt.itemList)
                    {
                        if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                        {
                            inventoryAmount += item.amount;
                            if (inventoryAmount >= requiredItem.amount)
                            {
                                haveRequired = true;
                                break;
                            }
                        }
                    }
                }
                if (!haveRequired)
                    return false;
            }
            foreach (var requiredItem in items)
            {
                int itemsToTake = requiredItem.amount;
                List<Item> invItems = Pool.Get<List<Item>>();
                invItems.AddRange(player.inventory.containerMain.itemList);
                invItems.AddRange(player.inventory.containerBelt.itemList);
                foreach (var item in invItems)
                {
                    if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                    {
                        if (item.amount > itemsToTake)
                        {
                            item.amount -= itemsToTake;
                            itemsToTake = 0;
                            item.MarkDirty();
                            break;
                        }
                        if (item.amount <= itemsToTake)
                        {
                            itemsToTake -= item.amount;
                            item.GetHeldEntity()?.Kill();
                            item.Remove();
                        }
                    }
                    if (itemsToTake <= 0) break;
                }
                Pool.FreeUnmanaged(ref invItems);
                if (itemsToTake > 0)
                    return false;
            }
            return true;
        }


        private static readonly char[] amountSuffixes = new[] { 'k', 'm', 'b', 't' };

        private static string FormatNumber(float amount, StringBuilder sb)
        {
            if (amount == 0) return "0";
            if (amount < 1) return amount.ToString("0.###");
            if (amount < 10) return amount.ToString("0.##");
            if (amount < 100) return amount.ToString("0.#");
            if (amount < 1000) return amount.ToString("0");
            int index = -1;
            while (amount >= 1000 && index < amountSuffixes.Length - 1)
            {
                amount /= 1000f;
                index++;
            }
            sb.Clear();
            if (amount < 10)
                sb.Append(amount.ToString("0.##"));
            else if (amount < 100)
                sb.Append(amount.ToString("0.#"));
            else
                sb.Append(amount.ToString("0"));
            sb.Append(amountSuffixes[index]);
            string output = sb.ToString();
            return output;
        }

        private void TryToggleQuarry(BasePlayer player)
        {
            UiCache uc = cache[player.userID];
            QuarryData qd = data.quarries[uc.quarryId];
            if (!adminModes.Contains(player.userID) && qd.owner != player.userID && !qd.authPlayers.Contains(player.userID)) return;
            BoxStorage quarry = GetQuarryBox(uc.quarryId, BoxType.Core);
            if (!quarry)
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ErrorOccured", player.UserIDString));
                return;
            }
            VirtualQuarry virtualQuarry = quarry.GetComponent<VirtualQuarry>();
            if (!virtualQuarry)
            {
                Puts($"[VirtualQuarries ERROR] Player {player.displayName} ({player.UserIDString}) is trying to open quarry that somehow doesn't have VirtualQuarry module!");
                return;
            }
            bool wasRunning = qd.isRunning;
            virtualQuarry.SwitchEngine();
            //ShowQuarryMenuUI(player, 1, quarryId);
            if (!wasRunning && !qd.isRunning)
            {
                UpdateCantStartToggle(player);
                return;
            }
            UpdateQuarryToggle(player, qd.isRunning);
            if (!wasRunning && config.startSound.Length > 0)
                SendEffect(player, config.startSound);
            else if (wasRunning && config.stopSound.Length > 0)
                SendEffect(player, config.stopSound);
        }

        #endregion

        #region UI V2

        private static readonly LuiScrollbar defaultScroll = new()
        {
            autoHide = true,
            size = 4,
            handleColor = ColDb.LTD15,
            highlightColor = ColDb.LTD20,
            pressedColor = ColDb.LTD10,
            trackColor = ColDb.BlackTrans10
        };

        private static readonly LuiScrollbar invisScroll = new()
        {
            autoHide = true,
            size = 1,
            handleColor = ColDb.Transparent,
            highlightColor = ColDb.Transparent,
            pressedColor = ColDb.Transparent,
            trackColor = ColDb.Transparent
        };

        private void DrawVirtualQuarriesUI(BasePlayer player)
        {
            cache.TryAdd(player.userID, new());
            UiCache uc = cache[player.userID];
            uc.isUsingPlugin = true;
            if (uc.quarryId != -1 && data.quarries[uc.quarryId].quarryType != QuarryType.Virtual)
                uc.quarryId = -1;
            using CUI cui = new CUI(CuiHandler);
            LUI.LuiContainer quarryUi = cui.v2.CreateParent(CUI.ClientPanels.HudMenu, LuiPosition.Full, "QuarriesUI").SetDestroy("QuarriesUI").AddCursor();

            //Element: Background Blur
            LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(quarryUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

            //Element: Background Darker
            LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(quarryUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Center Anchor
            LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(quarryUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);

            //Element: Main Panel
            LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(-550, -310, 550, 310), ColDb.DarkGray);
            mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Top Panel
            LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, LuiPosition.None, new LuiOffset(0, 578, 1100, 620), ColDb.LTD5);
            topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Title Text
            cui.v2.CreateText(topPanel, LuiPosition.None, new LuiOffset(10, 0, 810, 42), 24, ColDb.LightGray, Lang("VirtualQuarriesMenu", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Close Button
            LUI.LuiContainer closeButton = cui.v2.CreateButton(topPanel, LuiPosition.None, new LuiOffset(1064, 6, 1094, 36), "QuarriesUI close", ColDb.RedBg);
            closeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Close Button Icon
            cui.v2.CreateSprite(closeButton, LuiPosition.None, new LuiOffset(6, 6, 24, 24), "assets/icons/close.png", ColDb.RedText);

            //Element: Bottom Section
            LUI.LuiContainer bottomSection = cui.v2.CreateEmptyContainer(mainPanel, "QuarriesUI_BottomSection", true).SetOffset(new LuiOffset(0, 0, 1100, 578));

            //Element: Quarries Background
            LUI.LuiContainer quarriesBackground = cui.v2.CreatePanel(bottomSection, LuiPosition.None, new LuiOffset(0, 0, 700, 578), ColDb.BlackTrans20, "QuarriesUI_QuarryBackground");
            quarriesBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Quarry List Title
            cui.v2.CreateText(quarriesBackground, LuiPosition.None, new LuiOffset(13, 528, 384, 562), 25, ColDb.LightGray, Lang("QuarryList", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Search Panel
            LUI.LuiContainer searchPanel = cui.v2.CreatePanel(quarriesBackground, LuiPosition.None, new LuiOffset(384, 528, 684, 562), ColDb.LTD5);
            searchPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Search Input
            string search = uc.quarrySearch.Length > 0 ? uc.quarrySearch : Lang("SearchQuarry", player.UserIDString);
            cui.v2.CreateInput(searchPanel, LuiPosition.None, new LuiOffset(9, 0, 272, 34), ColDb.LTD80, search, 13, "QuarriesUI searchQuarry", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleLeft, "QuarriesUI_SearchInput").SetInputKeyboard(false, true);

            //Element: Search Icon
            cui.v2.CreateSprite(searchPanel, LuiPosition.None, new LuiOffset(272, 6, 294, 28), "assets/content/ui/gameui/camera/icon-zoom.png", ColDb.LTD30);

            StringBuilder sb = Pool.Get<StringBuilder>();

            DrawQuarries(player, cui, sb);

            //Element: View Owned Button
            string color = uc.viewType == QuarryViewType.Owned ? ColDb.GreenBg : ColDb.LTD10;
            LUI.LuiContainer viewOwnedButton = cui.v2.CreateButton(quarriesBackground, LuiPosition.None, new LuiOffset(16, 16, 146, 48), "QuarriesUI viewOwned", color, name: "QuarriesUI_ViewOwnedButton");
            viewOwnedButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: View Owned Button Text
            color = uc.viewType == QuarryViewType.Owned ? ColDb.GreenText : ColDb.LTD80;
            LUI.LuiContainer viewOwnedButtonText = cui.v2.CreateText(viewOwnedButton, LuiPosition.Full, LuiOffset.None, 16, color, Lang("ViewOwned", player.UserIDString), TextAnchor.MiddleCenter, "QuarriesUI_ViewOwnedButtonText");

            //Element: View Shared Button
            color = uc.viewType == QuarryViewType.Shared ? ColDb.GreenBg : ColDb.LTD10;
            LUI.LuiContainer viewSharedButton = cui.v2.CreateButton(quarriesBackground, LuiPosition.None, new LuiOffset(162, 16, 292, 48), "QuarriesUI viewShared", color, name: "QuarriesUI_ViewSharedButton");
            viewSharedButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: View Shared Button Text
            color = uc.viewType == QuarryViewType.Shared ? ColDb.GreenText : ColDb.LTD80;
            LUI.LuiContainer viewSharedButtonText = cui.v2.CreateText(viewSharedButton, LuiPosition.Full, LuiOffset.None, 16, color, Lang("ViewShared", player.UserIDString), TextAnchor.MiddleCenter, "QuarriesUI_ViewSharedButtonText");

            if (permission.UserHasPermission(player.UserIDString, "virtualquarries.admin"))
            {
                //Element: Admin View Button
                LUI.LuiContainer adminViewButton = cui.v2.CreateButton(quarriesBackground, LuiPosition.None, new LuiOffset(308, 16, 392, 48), "QuarriesUI adminView", ColDb.LTD10);
                adminViewButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Admin View Button Text
                LUI.LuiContainer adminViewButtonText = cui.v2.CreateText(adminViewButton, LuiPosition.None, new LuiOffset(0, 15, 84, 31), 12, ColDb.LTD80, Lang("AdminView", player.UserIDString), TextAnchor.UpperCenter);

                //Element: Admin Mode Text
                bool isAdminMode = adminModes.Contains(player.userID);
                string text = isAdminMode ? "ON" : "OFF";
                color = isAdminMode ? ColDb.GreenBg : ColDb.RedBg;
                LUI.LuiContainer adminModeText = cui.v2.CreateText(adminViewButton, LuiPosition.None, new LuiOffset(0, 0, 84, 27), 16, color, text, TextAnchor.LowerCenter, "QuarriesUI_AdminModeText");

            }
            //Element: Remove From All Button
            LUI.LuiContainer removeFromAllButton = cui.v2.CreateButton(quarriesBackground, LuiPosition.None, new LuiOffset(408, 16, 538, 48), "QuarriesUI removeAll", ColDb.RedBg);
            removeFromAllButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Remove From All Button Text
            LUI.LuiContainer removeFromAllButtonText = cui.v2.CreateText(removeFromAllButton, LuiPosition.Full, LuiOffset.None, 16, ColDb.RedText, Lang("RemoveFromAll", player.UserIDString), TextAnchor.MiddleCenter);

            //Element: Add To All Button
            LUI.LuiContainer addToAllButton = cui.v2.CreateButton(quarriesBackground, LuiPosition.None, new LuiOffset(554, 16, 684, 48), "QuarriesUI addAll", ColDb.GreenBg);
            addToAllButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Add To All Button Text
            LUI.LuiContainer addToAllButtonText = cui.v2.CreateText(addToAllButton, LuiPosition.Full, LuiOffset.None, 16, ColDb.GreenText, Lang("AddToAll", player.UserIDString), TextAnchor.MiddleCenter);

            DrawQuarryDetails(player, cui, sb);

            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void DrawQuarries(BasePlayer player, CUI cui, StringBuilder sb)
        {
            List<int> quarriesToDisplay = Pool.Get<List<int>>();
            UiCache uc = cache[player.userID];
            quarriesToDisplay.Clear();
            bool hasAdminMode = adminModes.Contains(player.userID);
            bool isSearch = uc.quarrySearch.Length > 0;
            foreach (var quarry in data.quarries)
            {
                if (quarry.Value.quarryType != QuarryType.Virtual) continue;
                if (!hasAdminMode && quarry.Value.owner != player.userID && !quarry.Value.authPlayers.Contains(player.userID)) continue;
                if (uc.viewType == QuarryViewType.Owned && quarry.Value.owner != player.userID) continue;
                if (uc.viewType == QuarryViewType.Shared && quarry.Value.owner == player.userID) continue;
                if (isSearch)
                {
                    if (playerCache[quarry.Value.owner].Contains(uc.quarrySearch, CompareOptions.OrdinalIgnoreCase))
                        quarriesToDisplay.Add(quarry.Key);
                    else if (Lang(config.quarryProfiles[quarry.Value.profile].titleTranslation, player.UserIDString).Contains(uc.quarrySearch, CompareOptions.OrdinalIgnoreCase))
                        quarriesToDisplay.Add(quarry.Key);
                    else
                    {
                        foreach (var resource in quarry.Value.resources)
                        {
                            sb.Clear().Append("ItemName_").Append(resource.configKey);
                            if (Lang(sb.ToString(), player.UserIDString).Contains(uc.quarrySearch, CompareOptions.OrdinalIgnoreCase))
                            {
                                quarriesToDisplay.Add(quarry.Key);
                                break;
                            }
                        }
                    }
                }
                else
                    quarriesToDisplay.Add(quarry.Key);
            }

            int scrollHeight = 456;
            if (quarriesToDisplay.Count > 70)
                scrollHeight = 8 + Mathf.CeilToInt(quarriesToDisplay.Count / 10f) * 66 - 8;
            //Element: Quarries Scroll
            LUI.LuiContainer quarriesScroll = cui.v2.CreateScrollView("QuarriesUI_QuarryBackground", LuiPosition.None, new LuiOffset(0, 64, 694, 520), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, defaultScroll, default, "QuarriesUI_QuarriesScroll").SetDestroy("QuarriesUI_QuarriesScroll");
            quarriesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
            cui.v2.CreatePanel(quarriesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

            scrollHeight -= 58;
            int quarryCount = 0;
            int xPos;
            foreach (var quarry in quarriesToDisplay)
            {
                QuarryData qd = data.quarries[quarry];
                xPos = 25 + quarryCount * 66;
                //Element: Quarry Button
                string command = sb.Clear().Append("QuarriesUI quarry ").Append(quarry).ToString();
                string name = sb.Clear().Append("QuarriesUI_Quarry_").Append(quarry).ToString();
                string color = uc.quarryId == quarry ? ColDb.LTD10 : ColDb.LTD5;

                LUI.LuiContainer quarryButton = cui.v2.CreateButton(quarriesScroll, LuiPosition.None, new LuiOffset(xPos, scrollHeight, xPos + 50, scrollHeight + 50), command, color, true, name);
                quarryButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Quarry Button Outline
                name = sb.Append("_Outline").ToString();
                color = qd.isRunning ? ColDb.GreenBg : ColDb.RedBg;
                cui.v2.CreateSprite(quarryButton, LuiPosition.None, new LuiOffset(-2, -2, 52, 52), "assets/content/ui/ui.box.tga", color, name);

                //Element: Quarry Icon
                QuarryProfile qp = config.quarryProfiles[qd.profile];
                LUI.LuiContainer quarryIcon = cui.v2.CreateItemIcon(quarryButton, LuiPosition.None, new LuiOffset(7, 7, 43, 43), qp.icon.shortname, qp.icon.skin, ColDb.WhiteTrans80);
                quarryIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Quarry Share Icon
                bool shared = qd.owner != player.userID && qd.authPlayers.Contains(player.userID);
                if (shared || (hasAdminMode && qd.owner != player.userID))
                {
                    color = shared ? ColDb.GreenBg : ColDb.RedBg;
                    cui.v2.CreateSprite(quarryButton, LuiPosition.None, new LuiOffset(29, 32, 47, 50), "assets/icons/clan.png", color);
                }

                if (qp.enableUpgrades)
                {
                    //Element: Quarry Level
                    sb.Clear().Append("Lv. ").Append(qd.level + 1);
                    cui.v2.CreateText(quarryButton, LuiPosition.None, new LuiOffset(4, 3, 49, 19), 11, ColDb.LTD80, sb.ToString(), TextAnchor.LowerLeft);
                }
                quarryCount++;
                if (quarryCount >= 10)
                {
                    quarryCount = 0;
                    scrollHeight -= 66;
                }
            }
            xPos = 25 + quarryCount * 66;
            //Element: Quarry Add Button
            LUI.LuiContainer quarryAddButton = cui.v2.CreateButton(quarriesScroll, LuiPosition.None, new LuiOffset(xPos, scrollHeight, xPos + 50, scrollHeight + 50), "QuarriesUI addNew", ColDb.LTD5);
            quarryAddButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Quarry Add Outline
            cui.v2.CreateSprite(quarryAddButton, LuiPosition.None, new LuiOffset(-2, -2, 52, 52), "assets/content/ui/ui.box.tga", ColDb.GreenBg);

            //Element: Quarry Add Icon
            cui.v2.CreateSprite(quarryAddButton, LuiPosition.None, new LuiOffset(8, 8, 42, 42), "assets/icons/add.png", ColDb.GreenBg);
            Pool.FreeUnmanaged(ref quarriesToDisplay);
        }

        private void RedrawQuarryDetails(BasePlayer player, int oldId, int newId)
        {
            using CUI cui = new CUI(CuiHandler);
            StringBuilder sb = Pool.Get<StringBuilder>();
            if (oldId != -1 && newId != oldId)
            {
                string name = sb.Clear().Append("QuarriesUI_Quarry_").Append(oldId).ToString();
                cui.v2.UpdateColor(name, ColDb.LTD5);
            }
            if (newId != -1 && newId != oldId)
            {
                string name = sb.Clear().Append("QuarriesUI_Quarry_").Append(newId).ToString();
                cui.v2.UpdateColor(name, ColDb.LTD10);
            }
            DrawQuarryDetails(player, cui, sb);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void DrawQuarryDetails(BasePlayer player, CUI cui, StringBuilder sb)
        {
            UiCache uc = cache[player.userID];

            //Element: Quarry Details Section
            LUI.LuiContainer quarryDetailsSection = cui.v2.CreateEmptyContainer("QuarriesUI_BottomSection", "QuarriesUI_QuarryDetailsSection", true).SetOffset(new LuiOffset(700, 0, 1100, 578)).SetDestroy("QuarriesUI_QuarryDetailsSection");

            if (!data.quarries.ContainsKey(uc.quarryId))
                uc.quarryId = -1;
            if (uc.quarryId == -1)
            {
                Dictionary<string, int> playerPerm = GetMergedMiningLimits(player);
                if (playerPerm != null)
                {
                    sb.Clear().Append(Lang("NoQuarrySelected", player.UserIDString));
                    foreach (var perm in playerPerm)
                    {
                        if (perm.Key == "*")
                        {
                            sb.Append(Lang("PermTranslation", player.UserIDString,
                                Lang("AllQuarries", player.UserIDString), perm.Value));
                            continue;
                        }
                        if (!config.quarryProfiles.ContainsKey(perm.Key))
                            continue;
                        QuarryProfile permQp = config.quarryProfiles[perm.Key];
                        if (permQp.permission.Length == 0 || permission.UserHasPermission(player.UserIDString, permQp.permission))
                            sb.Append(Lang("PermTranslation", player.UserIDString, Lang(permQp.titleTranslation, player.UserIDString), perm.Value));
                    }
                }
                else
                    sb.Append(Lang("NoQuarrySelectedAccessOnly", player.UserIDString));
                //Element: No Quarry Select
                cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(16, 16, 384, 562), 25, ColDb.LightGray, sb.ToString(), TextAnchor.MiddleCenter);
                return;
            }
            QuarryData qd = data.quarries[uc.quarryId];
            QuarryProfile qp = config.quarryProfiles[qd.profile];

            //Element: Quarry Details Title
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(16, 528, 387, 562), 25, ColDb.LightGray, Lang("QuarryDetails", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Quarry Icon Background
            LUI.LuiContainer quarryIconBackground = cui.v2.CreatePanel(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 384, 164, 524), ColDb.BlackTrans20);
            quarryIconBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Quarry Icon
            LUI.LuiContainer selQuarryIcon = cui.v2.CreateItemIcon(quarryIconBackground, LuiPosition.None, new LuiOffset(8, 8, 132, 132), qp.icon.shortname, qp.icon.skin, ColDb.WhiteTrans80);
            selQuarryIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Quarry Name
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(172, 492, 400, 524), 25, ColDb.LightGray, Lang(qp.titleTranslation, player.UserIDString), TextAnchor.UpperLeft);

            //Element: Quarry Status
            string text = qd.isRunning ? Lang("Running", player.UserIDString) : Lang("Stopped", player.UserIDString);
            string color = qd.isRunning ? ColDb.GreenBg : ColDb.RedBg;
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(174, 474, 400, 496), 15, color, text, TextAnchor.UpperLeft, "QuarriesUI_QuarryRunning");

            if (qp.enableUpgrades)
            {
                //Element: Quarry Level Title
                cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(174, 449, 400, 471), 18, ColDb.LTD80, Lang("QuarryLevel", player.UserIDString), TextAnchor.UpperLeft);

                //Element: Quarry Level
                sb.Clear().Append("Lv. ").Append(qd.level + 1);
                cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(174, 429, 400, 451), 16, ColDb.LTD60, sb.ToString(), TextAnchor.UpperLeft);
            }

            //Element: Fuel Remaining Title
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(174, 408, 400, 430), 18, ColDb.LTD80, Lang("FuelRemaining", player.UserIDString), TextAnchor.UpperLeft);

            //Element: Fuel Remaining
            DrawFuelRequirement(player, cui, qd, qp);

            //Element: Quarry Resources Title
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 344, 400, 376), 25, ColDb.LightGray, Lang("QuarryResources", player.UserIDString), TextAnchor.UpperLeft);

            //Element: Quarry Resources Background
            LUI.LuiContainer quarryResourcesBackground = cui.v2.CreatePanel(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 242, 376, 342), ColDb.BlackTrans20);
            quarryResourcesBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Quarry Resources Scroll
            int scrollHeight = 4 + qd.resources.Count * 30;
            if (scrollHeight < 100)
                scrollHeight = 100;
            LUI.LuiContainer quarryResourcesScroll = cui.v2.CreateScrollView(quarryResourcesBackground, LuiPosition.None, new LuiOffset(0, 0, 352, 100), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, defaultScroll, default);
            quarryResourcesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
            cui.v2.CreatePanel(quarryResourcesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
            scrollHeight -= 34;
            foreach (var resource in qd.resources)
            {
                ResourceConfig rc = qp.resources[resource.configKey];
                //Element: Quarry Resource Section
                LUI.LuiContainer quarryResourceSection = cui.v2.CreateEmptyContainer(quarryResourcesScroll, add: true).SetOffset(new LuiOffset(0, scrollHeight, 352, scrollHeight + 30));

                //Element: Resource Icon
                LUI.LuiContainer resourceIcon = cui.v2.CreateItemIcon(quarryResourceSection, LuiPosition.None, new LuiOffset(8, 3, 32, 27), rc.shortname, rc.skin, ColDb.WhiteTrans80);
                resourceIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Resource Name
                string itemName = Lang(sb.Clear().Append("ItemName_").Append(resource.configKey).ToString(), player.UserIDString);
                cui.v2.CreateText(quarryResourceSection, LuiPosition.None, new LuiOffset(38, 0, 278, 30), 16, ColDb.LTD70, itemName, TextAnchor.MiddleLeft);

                //Element: Resource Output
                float output = 60f / config.quarryTick * resource.work * qp.upgrades[qd.level].multiplier;
                string amountString = output < 1 ? output.ToString("0.###") : output.ToString("0.##");
                sb.Clear().Append(amountString).Append("/m");
                cui.v2.CreateText(quarryResourceSection, LuiPosition.None, new LuiOffset(260, 0, 340, 30), 16, ColDb.GreenBg, sb.ToString(), TextAnchor.MiddleRight);
                scrollHeight -= 30;
            }
            //Element: Quarry Controls Title
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 201, 400, 233), 25, ColDb.LightGray, Lang("QuarryControls", player.UserIDString), TextAnchor.UpperLeft);

            //Element: Quarry Toggle Button
            color = qd.isRunning ? ColDb.RedBg : ColDb.GreenBg;
            LUI.LuiContainer quarryToggleButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 155, 196, 195), "QuarriesUI toggle", color, name: "QuarriesUI_ToggleButton");
            quarryToggleButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Toggle Icon
            color = qd.isRunning ? ColDb.RedText : ColDb.GreenText;
            cui.v2.CreateSprite(quarryToggleButton, LuiPosition.None, new LuiOffset(9, 9, 31, 31), "assets/icons/key.png", color, "QuarriesUI_ToggleIcon");

            //Element: Toggle Text
            text = qd.isRunning ? Lang("StopQuarry", player.UserIDString) : Lang("TryStartQuarry", player.UserIDString);
            cui.v2.CreateText(quarryToggleButton, LuiPosition.None, new LuiOffset(40, 0, 172, 40), 13, color, text, TextAnchor.MiddleLeft, "QuarriesUI_ToggleText");

            //Element: Toggle Status Title
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(205, 172, 395, 195), 18, ColDb.RedBg, "", TextAnchor.LowerLeft, "QuarriesUI_CantStartTitle");

            //Element: Toggle Status Desc
            cui.v2.CreateText(quarryDetailsSection, LuiPosition.None, new LuiOffset(205, 155, 395, 173), 14, ColDb.RedText, "", TextAnchor.UpperLeft, "QuarriesUI_CantStartMessage");

            if (qp.enableOutputLink)
            {
                BoxStorage linkedStorage = qd.redirectNetId == 0 ? null : BaseNetworkable.serverEntities.Find(new NetworkableId(qd.redirectNetId)) as BoxStorage;
                if (!linkedStorage)
                {
                    //Element: Open Resource Container Button
                    LUI.LuiContainer openResourceContainerButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 107, 196, 147), "QuarriesUI linkOutput", ColDb.LTD10);
                    openResourceContainerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Container Icon
                    cui.v2.CreateSprite(openResourceContainerButton, LuiPosition.None, new LuiOffset(9, 9, 31, 31), "assets/icons/open.png", ColDb.LTD80);

                    //Element: Container Text
                    cui.v2.CreateText(openResourceContainerButton, LuiPosition.None, new LuiOffset(40, 0, 172, 40), 13, ColDb.LTD80, Lang("LinkOutputInventory", player.UserIDString), TextAnchor.MiddleLeft);
                }
                else
                {
                    //Element: Linked Storage Panel
                    LUI.LuiContainer linkedStoragePanel = cui.v2.CreatePanel(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 107, 196, 147), ColDb.BlackTrans20);
                    linkedStoragePanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Output Set Title
                    cui.v2.CreateText(linkedStoragePanel, LuiPosition.None, new LuiOffset(9, -1, 119, 34), 11, ColDb.LTD60, Lang("QuarryOutputSet", player.UserIDString), TextAnchor.UpperLeft);

                    //Element: Output Position
                    string grid = MapHelper.PositionToString(linkedStorage.transform.position);
                    cui.v2.CreateText(linkedStoragePanel, LuiPosition.None, new LuiOffset(9, 0, 59, 21), 15, ColDb.LTD80, grid, TextAnchor.UpperLeft);

                    //Element: Unlink Output Button
                    LUI.LuiContainer unlinkOutputButton = cui.v2.CreateButton(linkedStoragePanel, LuiPosition.None, new LuiOffset(141, 9, 163, 31), "QuarriesUI unlinkOutput", ColDb.RedBgTrans);
                    unlinkOutputButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Unlink Output Icon
                    LUI.LuiContainer unlinkOutputIcon = cui.v2.CreateSprite(unlinkOutputButton, LuiPosition.None, new LuiOffset(4, 4, 18, 18), "assets/icons/link_break.png", ColDb.RedText);
                }
            }
            else
            {
                //Element: Open Resource Container Button
                LUI.LuiContainer openResourceContainerButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 107, 196, 147), "QuarriesUI openOutput", ColDb.LTD10);
                openResourceContainerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Container Icon
                cui.v2.CreateSprite(openResourceContainerButton, LuiPosition.None, new LuiOffset(9, 9, 31, 31), "assets/icons/open.png", ColDb.LTD80);

                //Element: Container Text
                cui.v2.CreateText(openResourceContainerButton, LuiPosition.None, new LuiOffset(40, 0, 172, 40), 13, ColDb.LTD80, Lang("OpenOutputInventory", player.UserIDString), TextAnchor.MiddleLeft);

                if (qp.allowQuickTake && (qp.allowQuickPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, qp.allowQuickPerm)))
                {
                    //Element: Collect Resources Button
                    LUI.LuiContainer collectResourcesButton = cui.v2.CreateButton(openResourceContainerButton, LuiPosition.None, new LuiOffset(138, 6, 166, 34), "QuarriesUI collectResources", ColDb.LTD20);
                    collectResourcesButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Collect Resources Button Icon
                    cui.v2.CreateSprite(collectResourcesButton, LuiPosition.None, new LuiOffset(2, 2, 26, 26), "assets/icons/pickup.png", ColDb.LTD60);

                }
            }

            if (qp.enableInputLink)
            {
                BoxStorage linkedStorage = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.fuelNetId)) as BoxStorage;
                if (!linkedStorage)
                {
                    //Element: Open Fuel Container Button
                    LUI.LuiContainer openFuelContainerButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(204, 107, 376, 147), "QuarriesUI linkInput", ColDb.LTD10);
                    openFuelContainerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Fuel Icon
                    cui.v2.CreateSprite(openFuelContainerButton, LuiPosition.None, new LuiOffset(9, 9, 31, 31), "assets/icons/bleeding.png", ColDb.LTD80);

                    //Element: Fuel Text
                    cui.v2.CreateText(openFuelContainerButton, LuiPosition.None, new LuiOffset(40, 0, 172, 40), 13, ColDb.LTD80, Lang("LinkInputInventory", player.UserIDString), TextAnchor.MiddleLeft);
                }
                else
                {
                    //Element: Linked Fuel Panel
                    LUI.LuiContainer linkedFuelPanel = cui.v2.CreatePanel(quarryDetailsSection, LuiPosition.None, new LuiOffset(204, 107, 376, 147), ColDb.BlackTrans20);
                    linkedFuelPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Input Set Title
                    cui.v2.CreateText(linkedFuelPanel, LuiPosition.None, new LuiOffset(9, -1, 119, 34), 11, ColDb.LTD60, Lang("QuarryInputSet", player.UserIDString), TextAnchor.UpperLeft);

                    //Element: Input Position
                    string grid = MapHelper.PositionToString(linkedStorage.transform.position);
                    cui.v2.CreateText(linkedFuelPanel, LuiPosition.None, new LuiOffset(9, 0, 59, 21), 15, ColDb.LTD80, grid, TextAnchor.UpperLeft);

                    //Element: Unlink Input Button
                    LUI.LuiContainer unlinkInputButton = cui.v2.CreateButton(linkedFuelPanel, LuiPosition.None, new LuiOffset(141, 9, 163, 31), "QuarriesUI unlinkInput", ColDb.RedBgTrans);
                    unlinkInputButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Unlink Input Icon
                    LUI.LuiContainer unlinkInputIcon = cui.v2.CreateSprite(unlinkInputButton, LuiPosition.None, new LuiOffset(4, 4, 18, 18), "assets/icons/link_break.png", ColDb.RedText);
                }
            }
            else
            {
                //Element: Open Fuel Container Button
                LUI.LuiContainer openFuelContainerButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(204, 107, 376, 147), "QuarriesUI openInput", ColDb.LTD10);
                openFuelContainerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Fuel Icon
                cui.v2.CreateSprite(openFuelContainerButton, LuiPosition.None, new LuiOffset(9, 9, 31, 31), "assets/icons/bleeding.png", ColDb.LTD80);

                //Element: Fuel Text
                cui.v2.CreateText(openFuelContainerButton, LuiPosition.None, new LuiOffset(40, 0, 172, 40), 13, ColDb.LTD80, Lang("OpenInputInventory", player.UserIDString), TextAnchor.MiddleLeft);
            }

            if (qd.owner != player.userID)
            {
                //Element: Quarry Other Owner Panel
                LUI.LuiContainer quarryOtherOwnerPanel = cui.v2.CreatePanel(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 25, 376, 99), ColDb.BlackTrans20);
                quarryOtherOwnerPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Owned By Title
                LUI.LuiContainer ownedByTitle = cui.v2.CreateText(quarryOtherOwnerPanel, LuiPosition.None, new LuiOffset(0, 41, 352, 61), 15, ColDb.LTD60, Lang("OwnedBy", player.UserIDString), TextAnchor.LowerCenter);

                //Element: Owned By Nickname
                LUI.LuiContainer ownedByNickname = cui.v2.CreateText(quarryOtherOwnerPanel, LuiPosition.None, new LuiOffset(0, 13, 352, 41), 22, ColDb.LTD80, playerCache[qd.owner], TextAnchor.UpperCenter);
            }
            else
            {
                //Element: Give Access Button
                LUI.LuiContainer giveAccessButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 59, 196, 99), "QuarriesUI giveAccess", ColDb.LTD10);
                giveAccessButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Give Access Icon
                LUI.LuiContainer giveAccessIcon = cui.v2.CreateSprite(giveAccessButton, LuiPosition.None, new LuiOffset(9, 9, 31, 31), "assets/icons/fun.png", ColDb.LTD40);

                //Element: Give Access Plus
                LUI.LuiContainer giveAccessPlus = cui.v2.CreateSprite(giveAccessButton, LuiPosition.None, new LuiOffset(21, 21, 33, 33), "assets/icons/add.png", ColDb.LTD80);

                //Element: Give Access Text
                LUI.LuiContainer giveAccessText = cui.v2.CreateText(giveAccessButton, LuiPosition.None, new LuiOffset(40, 0, 172, 40), 13, ColDb.LTD80, Lang("GiveAccess", player.UserIDString), TextAnchor.MiddleLeft);

                //Element: Remove Access Button
                LUI.LuiContainer removeAccessButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(204, 59, 376, 99), "QuarriesUI removeAccess", ColDb.LTD10);
                removeAccessButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Remove Access Icon
                LUI.LuiContainer removeAccessIcon = cui.v2.CreateSprite(removeAccessButton, LuiPosition.None, new LuiOffset(9, 9, 31, 31), "assets/icons/fun.png", ColDb.LTD40);

                //Element: Remove Access Minus
                LUI.LuiContainer removeAccessMinus = cui.v2.CreateSprite(removeAccessButton, LuiPosition.None, new LuiOffset(21, 21, 33, 33), "assets/icons/subtract.png", ColDb.LTD80);

                //Element: Remove Access Text
                LUI.LuiContainer removeAccessText = cui.v2.CreateText(removeAccessButton, LuiPosition.None, new LuiOffset(40, 0, 172, 40), 13, ColDb.LTD80, Lang("RevokeAccess", player.UserIDString), TextAnchor.MiddleLeft);

                //Element: Auth List Button
                LUI.LuiContainer authListButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(24, 25, 196, 51), "QuarriesUI authList", ColDb.LTD10);
                authListButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Auth List Button Text
                LUI.LuiContainer authListButtonText = cui.v2.CreateText(authListButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.LTD80, Lang("AuthList", player.UserIDString), TextAnchor.MiddleCenter);

                //Element: Remove Quarry Button
                LUI.LuiContainer removeQuarryButton = cui.v2.CreateButton(quarryDetailsSection, LuiPosition.None, new LuiOffset(204, 25, 376, 51), "QuarriesUI removeQuarry", ColDb.RedBg);
                removeQuarryButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Remove Quarry Button Text
                LUI.LuiContainer removeQuarryButtonText = cui.v2.CreateText(removeQuarryButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.RedText, Lang("RemoveQuarry", player.UserIDString), TextAnchor.MiddleCenter);
            }
        }

        private void DrawFuelRequirement(BasePlayer player, CUI cui, QuarryData qd, QuarryProfile qp)
        {
            string color = qd.isRunning ? ColDb.GreenBg : ColDb.RedBg;
            float fuelCount = 0;
            BoxStorage storage = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.fuelNetId)) as BoxStorage;
            VirtualQuarry vq = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.netId)).GetComponent<VirtualQuarry>();
            if (storage)
                fuelCount = GetFuelCount(storage, qp);
            float removeSeconds = vq && qd.isRunning ? Time.time - vq.lastFuelTakeTime : 0;
            int remainingTime = Mathf.FloorToInt(fuelCount * config.quarryTick / qp.upgrades[qd.level].fuelMultiplier - removeSeconds);
            LUI.LuiContainer nextWave = cui.v2.CreateCountdown("QuarriesUI_QuarryDetailsSection", LuiPosition.None, new LuiOffset(174, 388, 400, 410), 16, color, "%TIME_LEFT%", TextAnchor.UpperLeft, 0, 0, name: "QuarriesUI_FuelCounter").SetDestroy("QuarriesUI_FuelCounter");
            int minNumber = qd.isRunning ? 0 : remainingTime;
            nextWave.SetCountdown(remainingTime, minNumber, numberFormat: "dd'd 'hh'h 'mm'm 'ss's'");
            nextWave.SetCountdownTimerFormat(TimerFormat.Custom);
            nextWave.SetCountdownDestroy(false);
        }

        private void UpdateSortButtons(BasePlayer player)
        {
            using CUI cui = new CUI(CuiHandler);
            StringBuilder sb = Pool.Get<StringBuilder>();
            UiCache uc = cache[player.userID];
            string color = uc.viewType == QuarryViewType.Owned ? ColDb.GreenBg : ColDb.LTD10;
            cui.v2.Update("QuarriesUI_ViewOwnedButton").SetButtonColors(color);
            color = uc.viewType == QuarryViewType.Owned ? ColDb.GreenText : ColDb.LTD80;
            cui.v2.Update("QuarriesUI_ViewOwnedButtonText").SetTextColor(color);
            color = uc.viewType == QuarryViewType.Shared ? ColDb.GreenBg : ColDb.LTD10;
            cui.v2.Update("QuarriesUI_ViewSharedButton").SetButtonColors(color);
            color = uc.viewType == QuarryViewType.Shared ? ColDb.GreenText : ColDb.LTD80;
            cui.v2.Update("QuarriesUI_ViewSharedButtonText").SetTextColor(color);
            DrawQuarries(player, cui, sb);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void UpdateNewQuarryRecord(BasePlayer player, int newId)
        {
            using CUI cui = new CUI(CuiHandler);
            StringBuilder sb = Pool.Get<StringBuilder>();
            UiCache uc = cache[player.userID];
            uc.quarryId = newId;
            DrawQuarries(player, cui, sb);
            RedrawQuarryDetails(player, -1, -1);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void UpdateAdminButton(BasePlayer player)
        {
            using CUI cui = new CUI(CuiHandler);
            StringBuilder sb = Pool.Get<StringBuilder>();
            bool isAdminMode = adminModes.Contains(player.userID);
            string text = isAdminMode ? "ON" : "OFF";
            string color = isAdminMode ? ColDb.GreenBg : ColDb.RedBg;
            cui.v2.UpdateText("QuarriesUI_AdminModeText", text, 0, color);
            DrawQuarries(player, cui, sb);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void TryAddNewQuarry(BasePlayer player)
        {
            List<string> surveyTypes = Pool.Get<List<string>>();
            surveyTypes.Clear();
            Dictionary<string, int> playerPerm = GetMergedMiningLimits(player);
            if (playerPerm != null)
            {
                foreach (var quarry in playerPerm.Keys)
                {
                    if (quarry == "*" || !config.quarryProfiles.ContainsKey(quarry))
                        continue;
                    QuarryProfile qp = config.quarryProfiles[quarry];
                    SurveyConfig sc = config.surveys[qp.surveyType];
                    if (qp.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, qp.permission)) continue;
                    if (sc.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, sc.permission)) continue;
                    if (!surveyTypes.Contains(qp.surveyType))
                        surveyTypes.Add(qp.surveyType);
                }
            }
            if (surveyTypes.Count == 0)
            {
                Pool.FreeUnmanaged(ref surveyTypes);
                return;
            }
            if (surveyTypes.Count == 1)
                OpenQuarryRoll(player, surveyTypes[0]);
            else
                OpenQuarrySurveySelect(player, surveyTypes);
            Pool.FreeUnmanaged(ref surveyTypes);
        }

        private void OpenQuarrySurveySelect(BasePlayer player, List<string> surveyKeys)
        {
            using CUI cui = new CUI(CuiHandler);
            LUI.LuiContainer rollSelectUi = cui.v2.CreateParent("QuarriesUI", LuiPosition.Full, "QuarriesUI_RollSelect").SetDestroy("QuarriesUI_RollSelect");
            //Element: Background Blur
            LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(rollSelectUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

            //Element: Background Darker
            LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(rollSelectUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Center Anchor
            LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(rollSelectUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);

            //Element: Main Panel
            LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(-180, -250, 180, 250), ColDb.DarkGray);
            mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Top Panel
            LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, LuiPosition.None, new LuiOffset(0, 464, 360, 500), ColDb.LTD5);
            topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Title Text
            LUI.LuiContainer titleText = cui.v2.CreateText(topPanel, LuiPosition.None, new LuiOffset(10, 0, 310, 36), 20, ColDb.LightGray, Lang("QuarrySourceSearch", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Close Button
            LUI.LuiContainer closeButton = cui.v2.CreateButton(topPanel, LuiPosition.None, new LuiOffset(329, 5, 355, 31), "QuarriesUI closeSurveySelect", ColDb.RedBg);
            closeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Close Button Icon
            LUI.LuiContainer closeButtonIcon = cui.v2.CreateSprite(closeButton, LuiPosition.None, new LuiOffset(5, 5, 21, 21), "assets/icons/close.png", ColDb.RedText);

            //Element: Bottom Section
            LUI.LuiContainer bottomSection = cui.v2.CreateEmptyContainer(mainPanel.name).SetOffset(new LuiOffset(0, 0, 360, 464));
            cui.v2.elements.Add(bottomSection);

            //Element: Hint Panel
            LUI.LuiContainer hintPanel = cui.v2.CreatePanel(bottomSection, LuiPosition.None, new LuiOffset(16, 406, 344, 448), ColDb.BlackTrans20);
            hintPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Hint Icon
            LUI.LuiContainer hintIcon = cui.v2.CreateSprite(hintPanel, LuiPosition.None, new LuiOffset(11, 11, 31, 31), "assets/icons/info.png", ColDb.LTD60);

            //Element: Hint Text
            LUI.LuiContainer hintText = cui.v2.CreateText(hintPanel, LuiPosition.None, new LuiOffset(40, 0, 322, 42), 13, ColDb.LTD60, Lang("SelectQuarrySurveyHint", player.UserIDString), TextAnchor.MiddleLeft);
            hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

            //Element: Charges Background
            LUI.LuiContainer chargesBackground = cui.v2.CreatePanel(bottomSection, LuiPosition.None, new LuiOffset(50, 24, 310, 394), ColDb.BlackTrans20);
            chargesBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Charges Scroll
            List<string> surveyTypes = Pool.Get<List<string>>();
            surveyTypes.Clear();
            Dictionary<string, int> playerPerm = GetMergedMiningLimits(player);
            if (playerPerm != null)
            {
                foreach (var quarry in playerPerm.Keys)
                {
                    if (quarry == "*" || !config.quarryProfiles.ContainsKey(quarry))
                        continue;
                    QuarryProfile qp = config.quarryProfiles[quarry];
                    SurveyConfig sc = config.surveys[qp.surveyType];
                    if (qp.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, qp.permission)) continue;
                    if (sc.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, sc.permission)) continue;
                    if (!surveyTypes.Contains(qp.surveyType))
                        surveyTypes.Add(qp.surveyType);
                }
            }
            int scrollHeight = 24 + surveyTypes.Count * 54 - 8;
            if (scrollHeight < 370)
                scrollHeight = 370;
            LUI.LuiContainer chargesScroll = cui.v2.CreateScrollView(chargesBackground, LuiPosition.None, new LuiOffset(0, 0, 260, 370), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, defaultScroll, default);
            chargesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
            cui.v2.CreatePanel(chargesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
            StringBuilder sb = Pool.Get<StringBuilder>();

            scrollHeight -= 58;
            foreach (var charge in surveyTypes)
            {
                SurveyConfig sc = config.surveys[charge];
                sb.Clear().Append("QuarriesUI selectSurvey ").Append(charge);
                //Element: Charge Button
                LUI.LuiContainer chargeButton = cui.v2.CreateButton(chargesScroll, LuiPosition.None, new LuiOffset(12, scrollHeight, 248, scrollHeight + 46), sb.ToString(), ColDb.LTD5);
                chargeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Charge Icon
                LUI.LuiContainer chargeIcon = cui.v2.CreateItemIcon(chargeButton, LuiPosition.None, new LuiOffset(6, 6, 40, 40), sc.surveyItem.shortname, sc.surveyItem.skin, ColDb.WhiteTrans80);
                chargeIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Charge Name
                ItemDefinition def = ItemManager.FindItemDefinition(sc.surveyItem.shortname);
                string itemName = !string.IsNullOrEmpty(sc.surveyItem.displayName) ? sc.surveyItem.displayName : def.displayName.english;
                LUI.LuiContainer chargeName = cui.v2.CreateText(chargeButton, LuiPosition.None, new LuiOffset(44, 22, 236, 40), 15, ColDb.LTD80, itemName, TextAnchor.UpperLeft);

                int startWidth = 46;
                foreach (var profile in config.quarryProfiles.Values)
                {
                    if (profile.surveyType == charge)
                    {
                        //Element: Charge Usage
                        LUI.LuiContainer chargeUsage = cui.v2.CreateItemIcon(chargeButton, LuiPosition.None, new LuiOffset(startWidth, 6, startWidth + 16, 22), profile.icon.shortname, profile.icon.skin, ColDb.WhiteTrans80);
                        chargeUsage.SetMaterial("assets/content/ui/namefontmaterial.mat");
                        startWidth += 19;
                    }
                }
                scrollHeight -= 54;
            }
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref surveyKeys);
            Pool.FreeUnmanaged(ref sb);
        }

        private void OpenQuarryRoll(BasePlayer player, string surveyKey)
        {
            using CUI cui = new CUI(CuiHandler);

            UiCache uc = cache[player.userID];
            uc.surveyKey = surveyKey;
            LUI.LuiContainer rollUi = cui.v2.CreateParent("QuarriesUI", LuiPosition.Full, "QuarriesUI_QuarryRoll").SetDestroy("QuarriesUI_QuarryRoll");
            //Element: Background Blur
            LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(rollUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

            //Element: Background Darker
            LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(rollUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Center Anchor
            LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(rollUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);
            centerAnchor.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Main Search Panel
            LUI.LuiContainer mainSearchPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(-180, -250, 180, 250), ColDb.DarkGray);
            mainSearchPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Main Top Panel
            LUI.LuiContainer mainTopPanel = cui.v2.CreatePanel(mainSearchPanel, LuiPosition.None, new LuiOffset(0, 464, 360, 500), ColDb.LTD5);
            mainTopPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Main Title Text
            LUI.LuiContainer mainTitleText = cui.v2.CreateText(mainTopPanel, LuiPosition.None, new LuiOffset(10, 0, 310, 36), 20, ColDb.LightGray, Lang("QuarrySourceSearch", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Close Button
            LUI.LuiContainer closeButton = cui.v2.CreateButton(mainTopPanel, LuiPosition.None, new LuiOffset(329, 5, 355, 31), "QuarriesUI closeQuarryRoll", ColDb.RedBg);
            closeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Close Button Icon
            LUI.LuiContainer closeButtonIcon = cui.v2.CreateSprite(closeButton, LuiPosition.None, new LuiOffset(5, 5, 21, 21), "assets/icons/close.png", ColDb.RedText);

            //Element: Main Bottom Section
            LUI.LuiContainer mainBottomSection = cui.v2.CreateEmptyContainer(mainSearchPanel.name).SetOffset(new LuiOffset(0, 0, 360, 464));
            cui.v2.elements.Add(mainBottomSection);

            //Element: Found Resources Panel
            LUI.LuiContainer foundResourcesPanel = cui.v2.CreatePanel(mainBottomSection, LuiPosition.None, new LuiOffset(16, 288, 344, 448), ColDb.BlackTrans20, "QuarryRollUI_FoundResourcesPanel");
            foundResourcesPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Found Title
            LUI.LuiContainer foundTitle = cui.v2.CreateText(foundResourcesPanel, LuiPosition.None, new LuiOffset(8, 136, 208, 156), 15, ColDb.LTD60, Lang("FoundResource", player.UserIDString), TextAnchor.LowerLeft);

            //Element: Output Title
            LUI.LuiContainer outputTitle = cui.v2.CreateText(foundResourcesPanel, LuiPosition.None, new LuiOffset(120, 136, 320, 156), 12, ColDb.LTD60, Lang("OutputPerMinute", player.UserIDString), TextAnchor.LowerRight);

            StringBuilder sb = Pool.Get<StringBuilder>();
            SurveyConfig sc = config.surveys[surveyKey];

            //Element: Survey Item Background
            LUI.LuiContainer surveyItemBackground = cui.v2.CreatePanel(mainBottomSection, LuiPosition.None, new LuiOffset(32, 230, 82, 280), ColDb.BlackTrans20);
            surveyItemBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Survey Item Icon
            LUI.LuiContainer surveyItemIcon = cui.v2.CreateItemIcon(surveyItemBackground, LuiPosition.None, new LuiOffset(6, 6, 44, 44), sc.surveyItem.shortname, sc.surveyItem.skin, ColDb.WhiteTrans80);
            surveyItemIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

            if (sc.surveyItem.amount > 0)
            {
                //Element: Survey Item Amount
                sb.Clear().Append('x').Append(sc.surveyItem.amount);
                cui.v2.CreateText(surveyItemBackground, LuiPosition.None, new LuiOffset(0, 2, 47, 50), 15, ColDb.LTD80, sb.ToString(), TextAnchor.LowerRight);
            }

            //Element: Survey Item name
            cui.v2.CreateText(mainBottomSection, LuiPosition.None, new LuiOffset(88, 252, 217, 270), 14, ColDb.LightGray, Lang(sc.surveyTranslation, player.UserIDString), TextAnchor.UpperLeft);

            //Element: Survey Item Title
            cui.v2.CreateText(mainBottomSection, LuiPosition.None, new LuiOffset(88, 238, 217, 253), 11, ColDb.LTD60, Lang("RequiredItem", player.UserIDString), TextAnchor.UpperLeft);

            //Element: Try Search Button
            LUI.LuiContainer trySearchButton = cui.v2.CreateButton(mainBottomSection, LuiPosition.None, new LuiOffset(208, 242, 328, 268), "QuarriesUI trySearch", ColDb.GreenBg, name: "QuarryRollUI_TrySearchButton");
            trySearchButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Try Search Button Text
            LUI.LuiContainer trySearchButtonText = cui.v2.CreateText(trySearchButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, string.Empty, TextAnchor.MiddleCenter, "QuarryRollUI_TrySearchButtonText");

            //Element: Requirements Panel
            LUI.LuiContainer requirementsPanel = cui.v2.CreatePanel(mainBottomSection, LuiPosition.None, new LuiOffset(16, 62, 344, 222), ColDb.BlackTrans20, "QuarryRollUI_RequirementsPanel");
            requirementsPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Required Item Title
            cui.v2.CreateText(requirementsPanel, LuiPosition.None, new LuiOffset(8, 136, 208, 156), 15, ColDb.LTD60, Lang("RequiredItem", player.UserIDString), TextAnchor.LowerLeft);

            //Element: Required Amount Title
            cui.v2.CreateText(requirementsPanel, LuiPosition.None, new LuiOffset(120, 136, 320, 156), 12, ColDb.LTD60, Lang("Amount", player.UserIDString), TextAnchor.LowerRight);

            //Element: Place Quarry Button
            LUI.LuiContainer placeQuarryButton = cui.v2.CreateButton(mainBottomSection, LuiPosition.None, new LuiOffset(90, 16, 270, 46), string.Empty, ColDb.LTD10, true, "QuarriesUI_PlaceQuarryButton");
            placeQuarryButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Place Quarry Button Text
            LUI.LuiContainer placeQuarryButtonText = cui.v2.CreateText(placeQuarryButton, LuiPosition.Full, LuiOffset.None, 20, ColDb.LTD60, string.Empty, TextAnchor.MiddleCenter, "QuarriesUI_PlaceQuarryButtonText");
            if (sc.allowAutoFinder && (sc.autoFinderPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, sc.autoFinderPerm)))
            {
                //Element: Auto Finder Panel
                LUI.LuiContainer autoFinderPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(212, -192, 572, 158), ColDb.DarkGray);
                autoFinderPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Finder Top Panel
                LUI.LuiContainer finderTopPanel = cui.v2.CreatePanel(autoFinderPanel, LuiPosition.None, new LuiOffset(0, 314, 360, 350), ColDb.LTD5);
                finderTopPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Finder Title Text
                cui.v2.CreateText(finderTopPanel, LuiPosition.None, new LuiOffset(10, 0, 310, 36), 20, ColDb.LightGray, Lang("QuarryAutoFinder", player.UserIDString), TextAnchor.MiddleLeft);

                //Element: Finder Bottom Section
                LUI.LuiContainer finderBottomSection = cui.v2.CreateEmptyContainer(autoFinderPanel, add: true).SetOffset(new LuiOffset(0, 0, 360, 314));

                //Element: Resource Select Panel
                LUI.LuiContainer resourceSelectPanel = cui.v2.CreatePanel(finderBottomSection, LuiPosition.None, new LuiOffset(16, 138, 344, 298), ColDb.BlackTrans20);
                resourceSelectPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Min Output Background
                LUI.LuiContainer minOutputBackground = cui.v2.CreatePanel(resourceSelectPanel, LuiPosition.None, new LuiOffset(170, 0, 246, 160), ColDb.LTD5);
                minOutputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Resource Title
                LUI.LuiContainer resourceTitle = cui.v2.CreateText(resourceSelectPanel, LuiPosition.None, new LuiOffset(8, 136, 208, 156), 15, ColDb.LTD60, Lang("ResourceName", player.UserIDString), TextAnchor.LowerLeft);

                //Element: Min Output Title
                LUI.LuiContainer minOutputTitle = cui.v2.CreateText(resourceSelectPanel, LuiPosition.None, new LuiOffset(170, 136, 246, 156), 12, ColDb.LTD60, Lang("MinOutput", player.UserIDString), TextAnchor.LowerCenter);

                //Element: Max Output Title
                LUI.LuiContainer maxOutputTitle = cui.v2.CreateText(resourceSelectPanel, LuiPosition.None, new LuiOffset(246, 136, 322, 156), 12, ColDb.LTD60, Lang("MaxOutput", player.UserIDString), TextAnchor.LowerCenter);

                //Element: Resources Scroll
                Dictionary<string, ResourceConfig> validResources = Pool.Get<Dictionary<string, ResourceConfig>>();
                validResources.Clear();
                foreach (var profile in config.quarryProfiles.Values)
                {
                    if (profile.surveyType != surveyKey) continue;
                    if (profile.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, profile.permission)) continue;
                    foreach (var resource in profile.resources)
                        if (resource.Value.permission.Length == 0 || permission.UserHasPermission(player.UserIDString, resource.Value.permission))
                            validResources[resource.Key] = resource.Value;
                }

                int scrollHeight = 4 + validResources.Count * 26;
                if (scrollHeight < 136)
                    scrollHeight = 136;
                LUI.LuiContainer resourcesScroll = cui.v2.CreateScrollView(resourceSelectPanel, LuiPosition.None, new LuiOffset(0, 0, 328, 136), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, invisScroll, default);
                resourcesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
                cui.v2.CreatePanel(resourcesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

                scrollHeight -= 30;
                foreach (var resource in validResources)
                {
                    //Element: Resource Record Section
                    LUI.LuiContainer resourceRecordSection = cui.v2.CreateEmptyContainer(resourcesScroll, add: true).SetOffset(new LuiOffset(0, scrollHeight, 328, scrollHeight + 36));

                    //Element: Resource Record Icon
                    LUI.LuiContainer resourceRecordIcon = cui.v2.CreateItemIcon(resourceRecordSection, LuiPosition.None, new LuiOffset(14, 3, 34, 23), resource.Value.shortname, resource.Value.skin, ColDb.WhiteTrans80);
                    resourceRecordIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Resource Toggle Button
                    sb.Clear().Append("QuarriesUI toggleAuto ").Append(resource.Key);
                    LUI.LuiContainer resourceToggleButton = cui.v2.CreateButton(resourceRecordSection, LuiPosition.None, new LuiOffset(12, 1, 36, 25), sb.ToString(), ColDb.Transparent);
                    resourceToggleButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Resource Toggle Button Icon
                    sb.Clear().Append("QuarryRollUI_ResourceToggleButtonIcon_").Append(resource.Key);
                    bool isDisabled = uc.autoRollDisabledResources.Contains(resource.Key);
                    string icon = isDisabled ? "assets/icons/close.png" : "assets/icons/check.png";
                    string color = isDisabled ? ColDb.RedBg : ColDb.GreenBg;
                    cui.v2.CreateSprite(resourceToggleButton, LuiPosition.None, new LuiOffset(10, 0, 24, 14), icon, color, sb.ToString());

                    //Element: Resource Record Name
                    ItemDefinition def = ItemManager.FindItemDefinition(resource.Value.shortname);
                    string itemName = resource.Value.name.Length > 0 ? resource.Value.name : def.displayName.english;
                    color = isDisabled ? ColDb.LTD60 : ColDb.LightGray;
                    sb.Clear().Append("QuarryRollUI_ResourceRecordName_").Append(resource.Key);
                    cui.v2.CreateText(resourceRecordSection, LuiPosition.None, new LuiOffset(40, 0, 170, 26), 13, color, itemName, TextAnchor.MiddleLeft, sb.ToString());

                    //Element: Resource Min Input
                    float minInput = uc.autoRollValues.TryGetValue(resource.Key, out float val) ? val : (resource.Value.outputMin + (resource.Value.outputMax - resource.Value.outputMin) / 2) * (60f / config.quarryTick);
                    var command = sb.Clear().Append("QuarriesUI setTargetRoll ").Append(resource.Key).ToString();
                    string name = sb.Clear().Append("QuarryRollUI_ResourceMinInput_").Append(resource.Key).ToString();
                    cui.v2.CreateInput(resourceRecordSection, LuiPosition.None, new LuiOffset(170, 0, 246, 26), ColDb.GreenBg, minInput.ToString(CultureInfo.InvariantCulture), 13, command, 0, true, CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor.MiddleCenter, name).SetInputKeyboard(false, true);

                    //Element: Resource Min Mark
                    LUI.LuiContainer resourceMinMark = cui.v2.CreateSprite(resourceRecordSection, LuiPosition.None, new LuiOffset(186, 3, 230, 5), "assets/content/ui/dotted_line_horizontal.png", ColDb.LTD20);
                    resourceMinMark.SetImageType(Image.Type.Tiled);

                    //Element: Resource Max
                    float maxOutput = resource.Value.outputMax * (60f / config.quarryTick);
                    cui.v2.CreateText(resourceRecordSection, LuiPosition.None, new LuiOffset(246, 0, 322, 26), 13, ColDb.LTD80, maxOutput.ToString(CultureInfo.InvariantCulture), TextAnchor.MiddleCenter);
                    scrollHeight -= 26;
                }
                Pool.FreeUnmanaged(ref validResources);


                //Element: Minimal Resource Title
                LUI.LuiContainer minimalResourceTitle = cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(16, 112, 258, 130), 14, ColDb.LightGray, Lang("MinimalResourcesTitle", player.UserIDString), TextAnchor.UpperLeft);

                //Element: Minimal Resource Desc
                LUI.LuiContainer minimalResourceDesc = cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(16, 102, 258, 117), 11, ColDb.LTD70, Lang("MinimalResourcesHint", player.UserIDString), TextAnchor.LowerLeft);
                minimalResourceDesc.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

                //Element: Minimal Resource Input Background
                LUI.LuiContainer minimalResourceInputBackground = cui.v2.CreatePanel(finderBottomSection, LuiPosition.None, new LuiOffset(258, 102, 328, 130), ColDb.LTD5);
                minimalResourceInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Minimal Resource Input
                string minResources = uc.autoRollMinResources > 1 ? uc.autoRollMinResources.ToString() : "1";
                cui.v2.CreateInput(minimalResourceInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, minResources, 15, "QuarriesUI setMinResources", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter, "QuarryRollUI_MinimalResourceInput").SetInputKeyboard(false, true);

                //Element: Required Item Title
                cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(16, 69, 216, 91), 16, ColDb.LTD80, Lang("RequiredItemAmount", player.UserIDString), TextAnchor.UpperLeft);

                //Element: Required Item Info
                LUI.LuiContainer requiredItemInfo = cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(20, 54, 216, 72), 12, ColDb.LTD60, Lang("BasedOnYouNeed", player.UserIDString), TextAnchor.UpperLeft);

                //Element: Req 1 Icon
                LUI.LuiContainer req1Icon = cui.v2.CreateItemIcon(finderBottomSection, LuiPosition.None, new LuiOffset(26, 38, 42, 54), sc.surveyItem.shortname, sc.surveyItem.skin, ColDb.LTD80);
                req1Icon.SetMaterial("assets/content/ui/namefontmaterial.mat");



                //Element: Req 1 Text
                int surveysRequired = GetRequiredSurveyCountCombo(player, uc.surveyKey, uc.autoRollDisabledResources, uc.autoRollMinResources);
                int reqToStart = Mathf.CeilToInt(surveysRequired * config.surveys[uc.surveyKey].minReqPercentage / 100f);
                sb.Clear().Append(reqToStart).Append(" - ").Append(Lang("ReqToStart", player.UserIDString));
                LUI.LuiContainer req1Text = cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(42, 38, 202, 54), 12, ColDb.LTD80, sb.ToString(), TextAnchor.MiddleLeft, "QuarryRollUI_Req1Text");

                //Element: Req 2 Icon
                LUI.LuiContainer req2Icon = cui.v2.CreateItemIcon(finderBottomSection, LuiPosition.None, new LuiOffset(26, 20, 42, 36), sc.surveyItem.shortname, sc.surveyItem.skin, ColDb.LTD80);
                req2Icon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Req 2 Text
                sb.Clear().Append(surveysRequired).Append(" - ").Append(Lang("ReqForGuarantee", player.UserIDString));
                LUI.LuiContainer req2Text = cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(42, 20, 202, 36), 12, ColDb.LTD80, sb.ToString(), TextAnchor.MiddleLeft, "QuarryRollUI_Req2Text");

                //Element: You Have Title
                LUI.LuiContainer youHaveTitle = cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(208, 75, 328, 95), 13, ColDb.LTD60, Lang("YouHave", player.UserIDString), TextAnchor.LowerCenter);

                //Element: You Have Icon 1
                LUI.LuiContainer youHaveIcon1 = cui.v2.CreateItemIcon(finderBottomSection, LuiPosition.None, new LuiOffset(220, 57, 236, 73), sc.surveyItem.shortname, sc.surveyItem.skin, ColDb.LTD80);
                youHaveIcon1.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: You Have Icon 2
                LUI.LuiContainer youHaveIcon2 = cui.v2.CreateItemIcon(finderBottomSection, LuiPosition.None, new LuiOffset(300, 57, 316, 73), sc.surveyItem.shortname, sc.surveyItem.skin, ColDb.LTD80);
                youHaveIcon2.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: You Have Amount Text
                int surveyCount = GetPlayerItemCount(player, sc.surveyItem);
                LUI.LuiContainer youHaveAmountText = cui.v2.CreateText(finderBottomSection, LuiPosition.None, new LuiOffset(236, 55, 300, 75), 15, ColDb.LightGray, surveyCount.ToString(), TextAnchor.MiddleCenter, "QuarriesUI_YouHaveCount");

                if (surveyCount >= reqToStart)
                {
                    //Element: Search Button
                    LUI.LuiContainer searchButton = cui.v2.CreateButton(finderBottomSection, LuiPosition.None, new LuiOffset(208, 20, 328, 52), "QuarriesUI bulkSearch", ColDb.GreenBg, name: "QuarryRollUI_SearchButton");
                    searchButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Search Button Text
                    LUI.LuiContainer searchButtonText = cui.v2.CreateText(searchButton, LuiPosition.None, new LuiOffset(0, 10, 120, 30), 15, ColDb.GreenText, Lang("Search", player.UserIDString), TextAnchor.UpperCenter, "QuarryRollUI_SearchButtonText");

                    //Element: Search Button Chance Text
                    float chance = surveyCount >= surveysRequired ? 100 : (float)surveyCount / surveysRequired * 100f;
                    LUI.LuiContainer searchButtonChanceText = cui.v2.CreateText(searchButton, LuiPosition.None, new LuiOffset(0, 2, 120, 16), 10, ColDb.GreenText, Lang("RollChance", player.UserIDString, chance.ToString("0.#")), TextAnchor.LowerCenter, "QuarryRollUI_SearchButtonChanceText");
                }
                else
                {
                    //Element: Search Button
                    LUI.LuiContainer searchButton = cui.v2.CreateButton(finderBottomSection, LuiPosition.None, new LuiOffset(208, 20, 328, 52), "", ColDb.RedBg, name: "QuarryRollUI_SearchButton");
                    searchButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Search Button Text
                    LUI.LuiContainer searchButtonText = cui.v2.CreateText(searchButton, LuiPosition.None, new LuiOffset(0, 10, 120, 30), 15, ColDb.RedText, Lang("NotEnough", player.UserIDString), TextAnchor.UpperCenter, "QuarryRollUI_SearchButtonText");

                    //Element: Search Button Chance Text
                    LUI.LuiContainer searchButtonChanceText = cui.v2.CreateText(searchButton, LuiPosition.None, new LuiOffset(0, 2, 120, 16), 10, ColDb.RedText, Lang("ZeroPercent", player.UserIDString), TextAnchor.LowerCenter, "QuarryRollUI_SearchButtonChanceText");
                }
            }

            UpdateRolledSurvey(player, cui, sb);

            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void SwitchAutoSurveyResource(BasePlayer player, string resourceKey)
        {
            UiCache uc = cache[player.userID];
            bool wasBlocked = uc.autoRollDisabledResources.Contains(resourceKey);
            if (wasBlocked)
                uc.autoRollDisabledResources.Remove(resourceKey);
            else
                uc.autoRollDisabledResources.Add(resourceKey);

            using CUI cui = new CUI(CuiHandler);
            StringBuilder sb = Pool.Get<StringBuilder>();
            sb.Clear().Append("QuarryRollUI_ResourceToggleButtonIcon_").Append(resourceKey);
            string icon = !wasBlocked ? "assets/icons/close.png" : "assets/icons/check.png";
            string color = !wasBlocked ? ColDb.RedBg : ColDb.GreenBg;
            cui.v2.Update(sb.ToString()).SetSprite(icon, color);
            UpdateRollRequirements(player, cui, sb, uc);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void UpdateAutoSurveyResourceAmount(BasePlayer player, string resourceKey, float amount)
        {
            UiCache uc = cache[player.userID];
            if (uc.autoRollValues.TryGetValue(resourceKey, out float oldAmount) && oldAmount == amount) return;
            uc.autoRollValues[resourceKey] = amount;
            StringBuilder sb = Pool.Get<StringBuilder>();
            using CUI cui = new CUI(CuiHandler);
            UpdateRollRequirements(player, cui, sb, uc);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void ChangeAutoSurveyResourceCount(BasePlayer player, int count)
        {
            UiCache uc = cache[player.userID];
            if (uc.autoRollMinResources == count) return;
            uc.autoRollMinResources = count;
            StringBuilder sb = Pool.Get<StringBuilder>();
            using CUI cui = new CUI(CuiHandler);
            UpdateRollRequirements(player, cui, sb, uc);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void UpdateRollRequirements(BasePlayer player, CUI cui, StringBuilder sb, UiCache uc)
        {
            int surveysRequired = GetRequiredSurveyCountCombo(player, uc.surveyKey, uc.autoRollDisabledResources, uc.autoRollMinResources);
            bool invalid = surveysRequired == int.MaxValue;
            SurveyConfig sc = config.surveys[uc.surveyKey];
            int reqToStart = Mathf.CeilToInt(surveysRequired * sc.minReqPercentage / 100f);
            sb.Clear().Append(invalid ? "\u221e" : reqToStart).Append(" - ").Append(Lang("ReqToStart", player.UserIDString));
            cui.v2.UpdateText("QuarryRollUI_Req1Text", sb.ToString());
            sb.Clear().Append(invalid ? "\u221e" : surveysRequired).Append(" - ").Append(Lang("ReqForGuarantee", player.UserIDString));
            cui.v2.UpdateText("QuarryRollUI_Req2Text", sb.ToString());
            int surveyCount = GetPlayerItemCount(player, sc.surveyItem);

            if (!invalid && surveyCount >= reqToStart)
            {
                //Element: Search Button
                cui.v2.Update("QuarryRollUI_SearchButton").SetButton(Community.Protect("QuarriesUI bulkSearch"), ColDb.GreenBg);

                //Element: Search Button Text
                cui.v2.UpdateText("QuarryRollUI_SearchButtonText", Lang("Search", player.UserIDString), 0, ColDb.GreenText);

                //Element: Search Button Chance Text
                float chance = surveyCount >= surveysRequired ? 100 : (float)surveyCount / surveysRequired * 100f;
                cui.v2.UpdateText("QuarryRollUI_SearchButtonChanceText", Lang("RollChance", player.UserIDString, chance.ToString("0.#")), 0, ColDb.GreenText);
            }
            else if (invalid)
            {
                //Element: Search Button
                cui.v2.Update("QuarryRollUI_SearchButton").SetButton("", ColDb.RedBg);

                //Element: Search Button Text
                cui.v2.UpdateText("QuarryRollUI_SearchButtonText", Lang("NotPossible", player.UserIDString), 0, ColDb.RedText);

                //Element: Search Button Chance Text
                cui.v2.UpdateText("QuarryRollUI_SearchButtonChanceText", Lang("ZeroPercent", player.UserIDString), 0, ColDb.RedText);
            }
            else
            {
                //Element: Search Button
                cui.v2.Update("QuarryRollUI_SearchButton").SetButton("", ColDb.RedBg);

                //Element: Search Button Text
                cui.v2.UpdateText("QuarryRollUI_SearchButtonText", Lang("NotEnough", player.UserIDString), 0, ColDb.RedText);

                //Element: Search Button Chance Text
                cui.v2.UpdateText("QuarryRollUI_SearchButtonChanceText", Lang("ZeroPercent", player.UserIDString), 0, ColDb.RedText);
            }
        }



        private void TryQuarrySearch(BasePlayer player, bool ignoreItems = false)
        {
            UiCache uc = cache[player.userID];
            SurveyConfig sc = config.surveys[uc.surveyKey];
            if (sc.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, sc.permission)) return;
            List<RequiredItem> requiredSurvey = Pool.Get<List<RequiredItem>>();
            requiredSurvey.Clear();
            requiredSurvey.Add(sc.surveyItem);
            if (!ignoreItems && !TakeItems(player, requiredSurvey))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoRequiredSurvey", player.UserIDString));
                Pool.FreeUnmanaged(ref requiredSurvey);
                return;
            }
            Pool.FreeUnmanaged(ref requiredSurvey);
            if (sc.effectPath.Length > 0)
                SendEffect(player, sc.effectPath);
            uc.cachedSurvey.resources.Clear();
            if (Core.Random.Range(0f, 100f) > sc.resourceChance)
            {
                uc.cachedSurvey.profile = "-";
                using CUI cui = new CUI(CuiHandler);
                StringBuilder sb = Pool.Get<StringBuilder>();
                UpdateRolledSurvey(player, cui, sb);
                cui.v2.SendUi(player);
                Pool.FreeUnmanaged(ref sb);
                return;
            }
            int totalProfileChance = 0;
            foreach (var profile in config.quarryProfiles.Values)
            {
                if (profile.surveyType != uc.surveyKey) continue;
                if (profile.permission.Length == 0 || permission.UserHasPermission(player.UserIDString, profile.permission))
                    totalProfileChance += profile.chance;
            }
            if (totalProfileChance <= 0)
            {
                uc.cachedSurvey.profile = "-";
                using CUI cuiNoProfiles = new CUI(CuiHandler);
                StringBuilder sbNoProfiles = Pool.Get<StringBuilder>();
                UpdateRolledSurvey(player, cuiNoProfiles, sbNoProfiles);
                cuiNoProfiles.v2.SendUi(player);
                Pool.FreeUnmanaged(ref sbNoProfiles);
                return;
            }
            int rolledProfile = Core.Random.Range(0, totalProfileChance + 1);
            int sumChance = 0;
            foreach (var profile in config.quarryProfiles)
            {
                if (profile.Value.surveyType != uc.surveyKey) continue;
                if (profile.Value.permission.Length == 0 || permission.UserHasPermission(player.UserIDString, profile.Value.permission))
                {
                    sumChance += profile.Value.chance;
                    if (sumChance >= rolledProfile)
                    {
                        uc.cachedSurvey.profile = profile.Key;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(uc.cachedSurvey.profile) || !config.quarryProfiles.ContainsKey(uc.cachedSurvey.profile))
            {
                uc.cachedSurvey.resources.Clear();
                uc.cachedSurvey.profile = "-";
                using CUI cuiBadRoll = new CUI(CuiHandler);
                StringBuilder sbBadRoll = Pool.Get<StringBuilder>();
                UpdateRolledSurvey(player, cuiBadRoll, sbBadRoll);
                cuiBadRoll.v2.SendUi(player);
                Pool.FreeUnmanaged(ref sbBadRoll);
                return;
            }
            QuarryProfile qp = config.quarryProfiles[uc.cachedSurvey.profile];
            List<string> rolledResources = Pool.Get<List<string>>();
            rolledResources.Clear();
            foreach (var resource in qp.resources)
            {
                if (!resource.Value.alwaysInclude || rolledResources.Contains(resource.Key)) continue;
                if (resource.Value.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                uc.cachedSurvey.resources.Add(new() { configKey = resource.Key, work = Core.Random.Range(resource.Value.outputMin, resource.Value.outputMax) });
                rolledResources.Add(resource.Key);
            }
            int maxLimit = qp.maxPerNode + 1 - rolledResources.Count;
            int loopCount = maxLimit >= qp.minPerNode ? Core.Random.Range(qp.minPerNode, qp.maxPerNode + 1 - rolledResources.Count) : 0;
            for (int i = 0; i < loopCount; i++)
            {
                int chance = 0;
                foreach (var resource in qp.resources)
                {
                    if (resource.Value.alwaysInclude || rolledResources.Contains(resource.Key)) continue;
                    if (resource.Value.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    chance += resource.Value.chance;
                }
                int rolledItem = Core.Random.Range(0, chance + 1);
                chance = 0;
                foreach (var resource in qp.resources)
                {
                    if (resource.Value.alwaysInclude || rolledResources.Contains(resource.Key)) continue;
                    if (resource.Value.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    chance += resource.Value.chance;
                    if (chance >= rolledItem)
                    {
                        uc.cachedSurvey.resources.Add(new() { configKey = resource.Key, work = Core.Random.Range(resource.Value.outputMin, resource.Value.outputMax) });
                        rolledResources.Add(resource.Key);
                        break;
                    }
                }
            }
            Pool.FreeUnmanaged(ref rolledResources);
            Interface.CallHook("OnCustomSurveyThrow", player, uc.cachedSurvey.profile);
            using CUI cui2 = new CUI(CuiHandler);
            StringBuilder sb2 = Pool.Get<StringBuilder>();
            UpdateRolledSurvey(player, cui2, sb2);
            if (ignoreItems)
            {
                cui2.v2.Update("QuarryRollUI_SearchButton").SetButton("", ColDb.RedBg);
                cui2.v2.UpdateText("QuarryRollUI_SearchButtonText", Lang("NotEnough", player.UserIDString), 0, ColDb.RedText);
                cui2.v2.UpdateText("QuarryRollUI_SearchButtonChanceText", Lang("ZeroPercent", player.UserIDString), 0, ColDb.RedText);
                cui2.v2.UpdateText("QuarriesUI_YouHaveCount", "0");
            }
            cui2.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb2);
        }

        private void TryBulkQuarrySearch(BasePlayer player)
        {
            UiCache uc = cache[player.userID];
            int surveysRequired = GetRequiredSurveyCountCombo(player, uc.surveyKey, uc.autoRollDisabledResources,
                uc.autoRollMinResources);
            if (surveysRequired == int.MaxValue) return;
            int customTarget = uc.autoRollMinResources < 1 ? 1 : uc.autoRollMinResources;
            SurveyConfig sc = config.surveys[uc.surveyKey];
            int reqToStart = Mathf.CeilToInt(surveysRequired * sc.minReqPercentage / 100f);
            int surveyCount = GetPlayerItemCount(player, sc.surveyItem);
            if (surveyCount < reqToStart) return;
            int itemsToTake = surveyCount < surveysRequired ? surveyCount : surveysRequired;
            float chance = surveyCount >= surveysRequired ? 100 : (float)surveyCount / surveysRequired * 100f;
            List<RequiredItem> requiredSurvey = Pool.Get<List<RequiredItem>>();
            requiredSurvey.Clear();
            RequiredItem reqItem = new(sc.surveyItem) { amount = itemsToTake };
            requiredSurvey.Add(reqItem);
            if (!TakeItems(player, requiredSurvey))
            {
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NoRequiredSurvey", player.UserIDString));
                Pool.FreeUnmanaged(ref requiredSurvey);
                return;
            }
            Pool.FreeUnmanaged(ref requiredSurvey);
            if (Core.Random.Range(0f, 100f) > chance)
            {
                TryQuarrySearch(player, true);
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("SearchFailed", player.UserIDString));
                return;
            }
            float totalProfileWeight = 0f;
            var profileWeights = Pool.Get<List<(string key, float weight)>>();
            profileWeights.Clear();
            foreach (var profile in config.quarryProfiles)
            {
                if (profile.Value.surveyType != uc.surveyKey) continue;
                if (profile.Value.permission.Length > 0 &&
                    !permission.UserHasPermission(player.UserIDString, profile.Value.permission)) continue;
                float w = GetProfileSelectionWeight(player, uc.surveyKey, profile.Key, customTarget);
                if (w <= 0f) continue;
                profileWeights.Add((profile.Key, w));
                totalProfileWeight += w;
            }

            string selectedProfile = string.Empty;
            if (totalProfileWeight > 0f)
            {
                float rolledWeight = Core.Random.Range(0f, totalProfileWeight);
                float cumulative = 0f;
                foreach (var (key, weight) in profileWeights)
                {
                    cumulative += weight;
                    if (cumulative >= rolledWeight)
                    {
                        selectedProfile = key;
                        break;
                    }
                }
            }
            Pool.FreeUnmanaged(ref profileWeights);
            if (selectedProfile.Length == 0) return;
            if (sc.effectPath.Length > 0)
                SendEffect(player, sc.effectPath);
            uc.cachedSurvey.profile = selectedProfile;
            uc.cachedSurvey.resources.Clear();
            QuarryProfile qp = config.quarryProfiles[selectedProfile];

            List<string> rolledTargetBonuses = Pool.Get<List<string>>();
            rolledTargetBonuses.Clear();
            List<string> rolledOtherBonuses = new(); //HAD TO TO MAKE IT LIKE THAT, IDK WHY WHEN I POOLED BOTH LISTS THEY'VE BEEN THE SAME LOL
            //rolledOtherBonuses.Clear();
            for (int i = 0; i < customTarget; i++)
            {
                float validWeightSum = 0f;
                foreach (var resource in qp.resources)
                {
                    if (uc.autoRollDisabledResources.Contains(resource.Key)) continue;
                    if (rolledTargetBonuses.Contains(resource.Key)) continue;
                    if (resource.Value.permission.Length > 0 &&
                        !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    validWeightSum += GetTargetSelectionWeight(player, uc.surveyKey, selectedProfile, resource.Key);
                }

                float rolledChance = Core.Random.Range(0f, validWeightSum);
                float currentSum = 0f;
                foreach (var resource in qp.resources)
                {
                    if (uc.autoRollDisabledResources.Contains(resource.Key)) continue;
                    if (rolledTargetBonuses.Contains(resource.Key)) continue;
                    if (resource.Value.permission.Length > 0 &&
                        !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    currentSum += GetTargetSelectionWeight(player, uc.surveyKey, selectedProfile, resource.Key);
                    if (currentSum >= rolledChance)
                    {
                        rolledTargetBonuses.Add(resource.Key);
                        break;
                    }
                }
            }
            foreach (var resource in qp.resources)
            {
                if (!resource.Value.alwaysInclude) continue;
                if (rolledTargetBonuses.Contains(resource.Key)) continue;
                if (resource.Value.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                rolledOtherBonuses.Add(resource.Key);
            }
            int remainingToRoll = Core.Random.Range(qp.minPerNode, qp.maxPerNode + 1) - rolledTargetBonuses.Count - rolledOtherBonuses.Count;
            for (int i = 0; i < remainingToRoll; i++)
            {
                int validPossibleWeightSum = 0;
                foreach (var resource in qp.resources)
                {
                    if (resource.Value.alwaysInclude) continue;
                    if (rolledTargetBonuses.Contains(resource.Key)) continue;
                    if (rolledOtherBonuses.Contains(resource.Key)) continue;
                    if (resource.Value.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    validPossibleWeightSum += resource.Value.chance;
                }
                int rolledChance = Core.Random.Range(0, validPossibleWeightSum + 1);
                int currentSum = 0;
                foreach (var resource in qp.resources)
                {
                    if (resource.Value.alwaysInclude) continue;
                    if (rolledTargetBonuses.Contains(resource.Key)) continue;
                    if (rolledOtherBonuses.Contains(resource.Key)) continue;
                    if (resource.Value.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, resource.Value.permission)) continue;
                    currentSum += resource.Value.chance;
                    if (currentSum >= rolledChance)
                    {
                        rolledOtherBonuses.Add(resource.Key);
                        break;
                    }
                }
            }
            float fixedQuarryTick = 60f / config.quarryTick;
            foreach (var resource in qp.resources)
            {
                if (rolledTargetBonuses.Contains(resource.Key))
                {
                    float minInput = resource.Value.outputMin;
                    if (uc.autoRollValues.TryGetValue(resource.Key, out float val))
                    {
                        val /= fixedQuarryTick;
                        if (val > resource.Value.outputMax) //SECURITY CHECK, SHOULDNT HAPPEN ANYTIME
                        {  
                            minInput = resource.Value.outputMin;
                            Puts($"Player {player.displayName} ({player.userID}) somehow rolled unreachable value ({val} for quarry {selectedProfile}). This is security check that prevent's it. Please report it to plugin developer!");
                        }
                        if (val > resource.Value.outputMin) 
                            minInput = val;
                    }
                    else
                        minInput = resource.Value.outputMin + (resource.Value.outputMax - resource.Value.outputMin) / 2 * fixedQuarryTick;
                    uc.cachedSurvey.resources.Add(new() { configKey = resource.Key, work = NextLinearDescending(minInput, resource.Value.outputMax) });
                }
                else if (rolledOtherBonuses.Contains(resource.Key))
                    uc.cachedSurvey.resources.Add(new() { configKey = resource.Key, work = NextLinearDescending(resource.Value.outputMin, resource.Value.outputMax) });
            }
            Pool.FreeUnmanaged(ref rolledTargetBonuses);
            //Pool.FreeUnmanaged(ref rolledOtherBonuses);
            Interface.CallHook("OnCustomSurveyBulkThrow", player, uc.cachedSurvey.profile, itemsToTake);
            using CUI cui = new CUI(CuiHandler);
            StringBuilder sb = Pool.Get<StringBuilder>();
            UpdateRolledSurvey(player, cui, sb);
            surveyCount -= itemsToTake;
            cui.v2.UpdateText("QuarriesUI_YouHaveCount", surveyCount.ToString());
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("SearchSuccessful", player.UserIDString));
        }
        
        public float GetProfileSelectionWeight(BasePlayer player, string surveyKey, string profileKey, int customTarget)
        {
            UiCache uc = cache[player.userID];
            if (!config.quarryProfiles.TryGetValue(profileKey, out var profile)) return 0f;

            var weights = Pool.Get<List<float>>();
            foreach (var resourceKV in profile.resources)
            {
                var res = resourceKV.Value;
                if (!IsResourceEligible(player, res)) continue;
                if (uc.autoRollDisabledResources.Contains(resourceKV.Key)) continue;
                float pOut = PAtLeastTargetFloat(res.outputMin, res.outputMax,
                    GetTarget(uc, resourceKV.Key, res));
                float baseWeight = res.alwaysInclude ? (res.chance + 1000f) : res.chance;
                weights.Add(baseWeight * pOut);
            }

            if (weights.Count < customTarget)
            {
                Pool.FreeUnmanaged(ref weights);
                return 0f;
            }

            weights.Sort((a, b) => b.CompareTo(a));
            float pCombo = 1f;
            for (int i = 0; i < customTarget; i++)
                pCombo *= weights[i];

            Pool.FreeUnmanaged(ref weights);
            return profile.chance * pCombo;
        }

        public static float NextLinearDescending(float min, float max)
        {
            if (min >= max) return min;

            float u = Core.Random.Range(0f, 1f);   // float in [0, 1)
            float skewed = 1f - Mathf.Sqrt(u);     // skewed toward 0

            float range = max - min;
            float result = min + skewed * range;

            return Mathf.Clamp(result, min, max);  // clamp to max, not max-1
        }

        private void UpdateRolledSurvey(BasePlayer player, CUI cui, StringBuilder sb)
        {
            UiCache uc = cache[player.userID];

            //Element: Found Resources Scroll
            bool hasSurvey = uc.cachedSurvey.profile.Length > 1;
            bool hasThrownBefore = uc.cachedSurvey.profile.Length > 0;
            int scrollHeight = hasSurvey ? 4 + uc.cachedSurvey.resources.Count * 26 : 136;
            if (scrollHeight < 136)
                scrollHeight = 136;
            LUI.LuiContainer foundResourcesScroll = cui.v2.CreateScrollView("QuarryRollUI_FoundResourcesPanel", LuiPosition.None, new LuiOffset(0, 0, 328, 136), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, invisScroll, default, "QuarryRollUI_FoundResourcesScroll").SetDestroy("QuarryRollUI_FoundResourcesScroll");
            foundResourcesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
            cui.v2.CreatePanel(foundResourcesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

            if (hasSurvey)
            {
                QuarryProfile qp = config.quarryProfiles[uc.cachedSurvey.profile];
                scrollHeight -= 30;
                foreach (var res in uc.cachedSurvey.resources)
                {
                    //Element: Resource Section
                    ResourceConfig rc = qp.resources[res.configKey];
                    LUI.LuiContainer resourceSection = cui.v2.CreateEmptyContainer(foundResourcesScroll.name).SetOffset(new LuiOffset(0, scrollHeight, 328, scrollHeight + 26));
                    cui.v2.elements.Add(resourceSection);

                    //Element: Resource Icon
                    LUI.LuiContainer resourceIcon = cui.v2.CreateItemIcon(resourceSection, LuiPosition.None, new LuiOffset(14, 3, 34, 23), rc.shortname, rc.skin, ColDb.WhiteTrans80);
                    resourceIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Resource Name
                    string itemName = Lang(sb.Clear().Append("ItemName_").Append(res.configKey).ToString(), player.UserIDString);
                    LUI.LuiContainer resourceName = cui.v2.CreateText(resourceSection, LuiPosition.None, new LuiOffset(40, 0, 230, 26), 13, ColDb.LightGray, itemName, TextAnchor.MiddleLeft);

                    //Element: Resource Amount
                    float amount = 60f / config.quarryTick * res.work;
                    string amountString = amount < 1 ? amount.ToString("0.###") : amount.ToString("0.##");
                    LUI.LuiContainer resourceAmount = cui.v2.CreateText(resourceSection, LuiPosition.None, new LuiOffset(229, 0, 319, 26), 13, ColDb.GreenBg, amountString, TextAnchor.MiddleRight);
                    scrollHeight -= 26;
                }
            }


            //Element: Try Search Button Text
            string text = hasThrownBefore ? Lang("TryAgain", player.UserIDString) : Lang("StartSearching", player.UserIDString);
            cui.v2.UpdateText("QuarryRollUI_TrySearchButtonText", text);

            int requiredItems = 0;
            if (hasSurvey)
            {
                QuarryProfile qp = config.quarryProfiles[uc.cachedSurvey.profile];
                requiredItems += qp.requiredItems.Count;
                foreach (var res in uc.cachedSurvey.resources)
                    requiredItems += qp.resources[res.configKey].additionalItems.Count;
            }

            scrollHeight = 4 + requiredItems * 26;
            if (scrollHeight < 136)
                scrollHeight = 136;
            LUI.LuiContainer requiredScroll = cui.v2.CreateScrollView("QuarryRollUI_RequirementsPanel", LuiPosition.None, new LuiOffset(0, 0, 328, 136), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, invisScroll, default, "QuarryRollUI_RequiredScroll").SetDestroy("QuarryRollUI_RequiredScroll");
            requiredScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
            cui.v2.CreatePanel(requiredScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

            if (requiredItems > 0)
            {
                scrollHeight -= 30;
                QuarryProfile qp = config.quarryProfiles[uc.cachedSurvey.profile];
                foreach (var reqItem in qp.requiredItems)
                {
                    //Element: Required Section
                    LUI.LuiContainer requiredSection = cui.v2.CreateEmptyContainer(requiredScroll, add: true).SetOffset(new LuiOffset(0, scrollHeight, 328, scrollHeight + 26));

                    //Element: Required Icon
                    LUI.LuiContainer requiredIcon = cui.v2.CreateItemIcon(requiredSection, LuiPosition.None, new LuiOffset(14, 3, 34, 23), reqItem.shortname, reqItem.skin, ColDb.WhiteTrans80);
                    requiredIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Required Name
                    ItemDefinition def = ItemManager.FindItemDefinition(reqItem.shortname);
                    string itemName = !string.IsNullOrEmpty(reqItem.displayName) ? reqItem.displayName : def.displayName.english;
                    cui.v2.CreateText(requiredSection, LuiPosition.None, new LuiOffset(40, 0, 230, 26), 13, ColDb.LightGray, itemName, TextAnchor.MiddleLeft);

                    //Element: Required Amount
                    sb.Clear().Append('x').Append(reqItem.amount);
                    cui.v2.CreateText(requiredSection, LuiPosition.None, new LuiOffset(229, 0, 319, 26), 13, ColDb.LightGray, sb.ToString(), TextAnchor.MiddleRight);
                    scrollHeight -= 26;
                }
                foreach (var res in uc.cachedSurvey.resources)
                {
                    foreach (var reqItem in qp.resources[res.configKey].additionalItems)
                    {
                        //Element: Required Section
                        LUI.LuiContainer requiredSection = cui.v2.CreateEmptyContainer(requiredScroll, add: true).SetOffset(new LuiOffset(0, scrollHeight, 328, scrollHeight + 26));

                        //Element: Required Icon
                        LUI.LuiContainer requiredIcon = cui.v2.CreateItemIcon(requiredSection, LuiPosition.None, new LuiOffset(14, 3, 34, 23), reqItem.shortname, reqItem.skin, ColDb.WhiteTrans80);
                        requiredIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                        //Element: Required Name
                        ItemDefinition def = ItemManager.FindItemDefinition(reqItem.shortname);
                        string itemName = reqItem.displayName.Length > 0 ? reqItem.displayName : def.displayName.english;
                        cui.v2.CreateText(requiredSection, LuiPosition.None, new LuiOffset(40, 0, 230, 26), 13, ColDb.LightGray, itemName, TextAnchor.MiddleLeft);

                        //Element: Required Amount
                        sb.Clear().Append('x').Append(reqItem.amount);
                        cui.v2.CreateText(requiredSection, LuiPosition.None, new LuiOffset(229, 0, 319, 26), 13, ColDb.LightGray, sb.ToString(), TextAnchor.MiddleRight);
                        scrollHeight -= 26;
                    }
                }
            }

            string command = hasSurvey ? "QuarriesUI place" : string.Empty;
            string color = hasSurvey ? ColDb.GreenBg : ColDb.LTD10;
            cui.v2.Update("QuarriesUI_PlaceQuarryButton").SetButton(Community.Protect(command), color);

            text = hasSurvey ? Lang("PlaceQuarry", player.UserIDString) : Lang("RollFirst", player.UserIDString);
            color = hasSurvey ? ColDb.GreenText : ColDb.LTD60;
            cui.v2.UpdateText("QuarriesUI_PlaceQuarryButtonText", text, 0, color);
        }

        private void DrawQuarryRemovePopUp(BasePlayer player)
        {
            using CUI cui = new CUI(CuiHandler);
            LUI.LuiContainer confirmUi = cui.v2.CreateParent("QuarriesUI", LuiPosition.Full, "QuarriesUI_RemovePopUp").SetDestroy("QuarriesUI_RemovePopUp");
            //Element: Background Blur
            LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(confirmUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

            //Element: Background Darker
            LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(confirmUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Center Anchor
            LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(confirmUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);

            //Element: Main Panel
            LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(-180, -80, 180, 80), ColDb.DarkGray);
            mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Top Panel
            LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, LuiPosition.None, new LuiOffset(0, 124, 360, 160), ColDb.LTD5);
            topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Title Text
            LUI.LuiContainer titleText = cui.v2.CreateText(topPanel, LuiPosition.None, new LuiOffset(10, 0, 360, 36), 20, ColDb.LightGray, Lang("QuarryRemoveTitle", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Bottom Section
            LUI.LuiContainer bottomSection = cui.v2.CreateEmptyContainer(mainPanel, add: true).SetOffset(new LuiOffset(0, 0, 360, 124));

            //Element: Hint Panel
            LUI.LuiContainer hintPanel = cui.v2.CreatePanel(bottomSection, LuiPosition.None, new LuiOffset(16, 50, 344, 112), ColDb.BlackTrans20);
            hintPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Hint Icon
            LUI.LuiContainer hintIcon = cui.v2.CreateSprite(hintPanel, LuiPosition.None, new LuiOffset(11, 21, 31, 41), "assets/icons/info.png", ColDb.LTD60);

            //Element: Hint Text
            LUI.LuiContainer hintText = cui.v2.CreateText(hintPanel, LuiPosition.None, new LuiOffset(40, 0, 322, 62), 11, ColDb.LTD60, Lang("QuarryRemoveDescription", player.UserIDString), TextAnchor.MiddleLeft);
            hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

            //Element: Confirm Button
            LUI.LuiContainer confirmButton = cui.v2.CreateButton(bottomSection, LuiPosition.None, new LuiOffset(26, 16, 166, 38), "QuarriesUI confirmRemove", ColDb.RedBg);
            confirmButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Confirm Button Text
            LUI.LuiContainer confirmButtonText = cui.v2.CreateText(confirmButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.RedText, Lang("ConfirmRemove", player.UserIDString), TextAnchor.MiddleCenter);

            //Element: Cancel Button
            LUI.LuiContainer cancelButton = cui.v2.CreateButton(bottomSection, LuiPosition.None, new LuiOffset(194, 16, 334, 38), "QuarriesUI backRemove", ColDb.LTD10);
            cancelButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Cancel Button Text
            LUI.LuiContainer cancelButtonText = cui.v2.CreateText(cancelButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.LTD80, Lang("CancelGoBack", player.UserIDString), TextAnchor.MiddleCenter);
            cui.v2.SendUi(player);
        }

        private void OpenUserManagementPanel(BasePlayer player)
        {
            using CUI cui = new CUI(CuiHandler);

            UiCache uc = cache[player.userID];
            LUI.LuiContainer usersUi = cui.v2.CreateParent("QuarriesUI", LuiPosition.Full, "QuarriesUI_UsersUI").SetDestroy("QuarriesUI_UsersUI");
            //Element: Background Blur
            LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(usersUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

            //Element: Background Darker
            LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(usersUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
            backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Center Anchor
            LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(usersUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);

            //Element: Main Panel
            LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(-180, -250, 180, 250), ColDb.DarkGray);
            mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Top Panel
            LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, LuiPosition.None, new LuiOffset(0, 464, 360, 500), ColDb.LTD5);
            topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Title Text
            LUI.LuiContainer titleText = cui.v2.CreateText(topPanel, LuiPosition.None, new LuiOffset(10, 0, 310, 36), 20, ColDb.LightGray, Lang("PlayerManagement", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Close Button
            LUI.LuiContainer closeButton = cui.v2.CreateButton(topPanel, LuiPosition.None, new LuiOffset(329, 5, 355, 31), "QuarriesUI closePlayers", ColDb.RedBg);
            closeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Close Button Icon
            LUI.LuiContainer closeButtonIcon = cui.v2.CreateSprite(closeButton, LuiPosition.None, new LuiOffset(5, 5, 21, 21), "assets/icons/close.png", ColDb.RedText);

            //Element: Bottom Section
            LUI.LuiContainer bottomSection = cui.v2.CreateEmptyContainer(mainPanel.name).SetOffset(new LuiOffset(0, 0, 360, 464));
            cui.v2.elements.Add(bottomSection);

            //Element: Hint Panel
            LUI.LuiContainer hintPanel = cui.v2.CreatePanel(bottomSection, LuiPosition.None, new LuiOffset(16, 406, 344, 448), ColDb.BlackTrans20);
            hintPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Hint Icon
            LUI.LuiContainer hintIcon = cui.v2.CreateSprite(hintPanel, LuiPosition.None, new LuiOffset(11, 11, 31, 31), "assets/icons/info.png", ColDb.LTD60);

            //Element: Hint Text
            string hintString = string.Empty;
            if (uc.userManageQuarryId == -1 && uc.userManageRemove)
                hintString = Lang("AllQuarriesUserRemoveHint", player.UserIDString);
            else if (uc.userManageQuarryId == -1 && !uc.userManageRemove)
                hintString = Lang("AllQuarriesUserAddHint", player.UserIDString);
            if (uc.userManageQuarryId != -1 && uc.userManageRemove)
                hintString = Lang("QuarryUserRemoveHint", player.UserIDString);
            else if (uc.userManageQuarryId != -1 && !uc.userManageRemove)
                hintString = Lang("QuarryUserAddHint", player.UserIDString);
            LUI.LuiContainer hintText = cui.v2.CreateText(hintPanel, LuiPosition.None, new LuiOffset(40, 0, 322, 42), 12, ColDb.LTD60, hintString, TextAnchor.MiddleLeft);
            hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

            //Element: Search Panel
            LUI.LuiContainer searchPanel = cui.v2.CreatePanel(bottomSection, LuiPosition.None, new LuiOffset(60, 370, 300, 398), ColDb.BlackTrans20);
            searchPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Search Input
            string searchText = uc.userSearch.Length > 0 ? uc.userSearch : Lang("SearchPlayer", player.UserIDString);
            cui.v2.CreateInput(searchPanel, LuiPosition.None, new LuiOffset(6, 0, 206, 28), ColDb.LightGray, searchText, 13, "QuarriesUI searchPlayer", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleLeft).SetInputKeyboard(false, true);

            //Element: Search Icon
            LUI.LuiContainer searchIcon = cui.v2.CreateSprite(searchPanel, LuiPosition.None, new LuiOffset(218, 6, 234, 22), "assets/content/ui/gameui/camera/icon-zoom.png", ColDb.LTD30);

            //Element: Nicknames Background
            LUI.LuiContainer nicknamesBackground = cui.v2.CreatePanel(bottomSection, LuiPosition.None, new LuiOffset(50, 24, 310, 362), ColDb.BlackTrans20, "QuarryUsersUI_NicknamesBackground");
            nicknamesBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

            DrawNicknames(player, cui);
            cui.v2.SendUi(player);
        }

        private void SearchPlayers(BasePlayer player)
        {
            using CUI cui = new CUI(CuiHandler);
            DrawNicknames(player, cui);
            cui.v2.SendUi(player);
        }

        private void DrawNicknames(BasePlayer player, CUI cui)
        {
            StringBuilder sb = Pool.Get<StringBuilder>();
            UiCache uc = cache[player.userID];
            //Element: Nicknames Scroll
            bool isSearching = uc.userSearch.Length > 0;
            List<ulong> foundPlayers = Pool.Get<List<ulong>>();
            List<ulong> foundOnlinePlayers = Pool.Get<List<ulong>>();
            List<ulong> foundRemovePlayers = Pool.Get<List<ulong>>();
            foundPlayers.Clear();
            foundOnlinePlayers.Clear();
            foundRemovePlayers.Clear();
            if (uc.userManageRemove)
            {
                foreach (var quarry in data.quarries)
                {
                    if (quarry.Value.owner != player.userID) continue;
                    foreach (var added in quarry.Value.authPlayers)
                        if (!foundRemovePlayers.Contains(added))
                            foundRemovePlayers.Add(added);
                }
            }
            foreach (var oPlayer in BasePlayer.activePlayerList)
            {
                if (!isSearching)
                {
                    if (uc.userManageRemove)
                    {
                        if (foundRemovePlayers.Contains(oPlayer.userID))
                        {
                            foundPlayers.Add(oPlayer.userID);
                            foundOnlinePlayers.Add(oPlayer.userID);
                        }
                        continue;
                    }
                    foundPlayers.Add(oPlayer.userID);
                    foundOnlinePlayers.Add(oPlayer.userID);
                }
                else if (oPlayer.displayName.Contains(uc.userSearch, CompareOptions.OrdinalIgnoreCase))
                {
                    if (uc.userManageRemove)
                    {
                        if (foundRemovePlayers.Contains(oPlayer.userID))
                        {
                            foundPlayers.Add(oPlayer.userID);
                            foundOnlinePlayers.Add(oPlayer.userID);
                        }
                        continue;
                    }
                    foundPlayers.Add(oPlayer.userID);
                    foundOnlinePlayers.Add(oPlayer.userID);
                }
            }
            if (isSearching)
            {
                int found = 0;
                foreach (var offPlayer in playerCache)
                {
                    if (foundPlayers.Contains(offPlayer.Key)) continue;
                    if (offPlayer.Value.Contains(uc.userSearch, CompareOptions.OrdinalIgnoreCase))
                    {
                        if (uc.userManageRemove)
                        {
                            if (foundRemovePlayers.Contains(offPlayer.Key))
                                foundPlayers.Add(offPlayer.Key);
                            continue;
                        }
                        foundPlayers.Add(offPlayer.Key);
                        found++;
                        if (found > 50)
                            break;
                    }
                }
            }
            foundPlayers.Remove(player.userID);
            int scrollHeight = 32 + foundPlayers.Count * 32 - 8;
            if (scrollHeight < 338)
                scrollHeight = 338;
            LUI.LuiContainer nicknamesScroll = cui.v2.CreateScrollView("QuarryUsersUI_NicknamesBackground", LuiPosition.None, new LuiOffset(0, 0, 260, 338), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, defaultScroll, default, "QuarryUsersUI_NicknamesScroll").SetDestroy("QuarryUsersUI_NicknamesScroll");
            nicknamesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
            cui.v2.CreatePanel(nicknamesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
            scrollHeight -= 40;
            foreach (var foundPlayer in foundPlayers)
            {
                bool isOnline = foundOnlinePlayers.Contains(foundPlayer);
                string color = isOnline ? ColDb.GreenBg : ColDb.LTD10;
                sb.Clear().Append("QuarriesUI player ").Append(foundPlayer);
                //Element: Player Button
                LUI.LuiContainer playerButton = cui.v2.CreateButton(nicknamesScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 244, scrollHeight + 24), sb.ToString(), color);
                playerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Nickname
                color = isOnline ? ColDb.GreenText : ColDb.LTD80;
                cui.v2.CreateText(playerButton, LuiPosition.Full, LuiOffset.None, 15, color, playerCache[foundPlayer], TextAnchor.MiddleCenter);

                scrollHeight -= 32;
            }
            Pool.FreeUnmanaged(ref sb);
            Pool.FreeUnmanaged(ref foundPlayers);
            Pool.FreeUnmanaged(ref foundOnlinePlayers);
            Pool.FreeUnmanaged(ref foundRemovePlayers);
        }

        private void OpenQuarryFuelPanel(BasePlayer player, int quarryId)
        {
            using CUI cui = new CUI(CuiHandler);

            LUI.LuiContainer fuelUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.Full, "QuarriesInventoriesUI").SetDestroy("QuarriesInventoriesUI");

            //Element: Lower Anchor
            LUI.LuiContainer lowerAnchor = cui.v2.CreatePanel(fuelUi, LuiPosition.LowerCenter, LuiOffset.None, ColDb.Transparent, "QuarriesInventoriesUI_Anchor");

            //Element: Background Blur
            LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(lowerAnchor, LuiPosition.None, new LuiOffset(191, 232, 571, 292), ColDb.BlackTrans20);
            backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

            //Element: Background Panel
            LUI.LuiContainer backgroundPanel = cui.v2.CreatePanel(lowerAnchor, LuiPosition.None, new LuiOffset(191, 232, 571, 292), ColDb.LightGrayTransRust);
            backgroundPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Fuel Types Title
            LUI.LuiContainer fuelTypesTitle = cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(0, 65, 380, 93), 20, ColDb.LightGray, Lang("SupportedFuel", player.UserIDString), TextAnchor.LowerLeft);

            QuarryData qd = data.quarries[quarryId];
            int startX = 8;
            int counter = 0;
            StringBuilder sb = Pool.Get<StringBuilder>();
            if (qd.quarryType == QuarryType.Virtual)
            {
                QuarryProfile qp = config.quarryProfiles[qd.profile];
                foreach (var fuelType in qp.fuelItems)
                {
                    counter++;

                    //Element: Fuel Type Section
                    LUI.LuiContainer fuelTypeSection = cui.v2.CreateEmptyContainer(backgroundPanel.name).SetOffset(new LuiOffset(startX, 10, startX + 120, 50));
                    cui.v2.elements.Add(fuelTypeSection);

                    //Element: Fuel Icon
                    LUI.LuiContainer fuelIcon = cui.v2.CreateItemIcon(fuelTypeSection, LuiPosition.None, new LuiOffset(0, 0, 40, 40), fuelType.shortname, fuelType.skin, ColDb.WhiteTrans80);
                    fuelIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Fuel Name
                    ItemDefinition def = ItemManager.FindItemDefinition(fuelType.shortname);
                    string itemName = !string.IsNullOrEmpty(fuelType.displayName) ? fuelType.displayName : def.displayName.english;
                    cui.v2.CreateText(fuelTypeSection, LuiPosition.None, new LuiOffset(42, 21, 120, 37), 10, ColDb.LTD80, itemName);

                    //Element: Fuel Efficiency
                    int fuelPerHour = Mathf.CeilToInt(60f / config.quarryTick * fuelType.amount * 60f * qp.upgrades[qd.level].fuelMultiplier);
                    int fuelPerDay = fuelPerHour * 24;
                    string fuelHourString = FormatNumber(fuelPerHour, sb);
                    string fuelDayString = FormatNumber(fuelPerDay, sb);
                    sb.Clear().Append(fuelHourString).Append(" / h\n").Append(fuelDayString).Append(" / d");
                    cui.v2.CreateText(fuelTypeSection, LuiPosition.None, new LuiOffset(44, -1, 120, 25), 10, ColDb.LightGray, sb.ToString());
                    startX += 122;
                    if (counter >= 3) break;
                }
                DrawBackButtons(player, cui, qp.upgrades[qd.level].fuelCapacity);
            }
            else
            {
                bool isStatic = qd.quarryType == QuarryType.Static;
                var upgrades = isStatic ? config.staticQuarryUpgrades : config.excavatorUpgrades;
                foreach (var fuelType in isStatic ? config.staticFuelItems : config.excavatorFuelItems)
                {
                    counter++;

                    //Element: Fuel Type Section
                    LUI.LuiContainer fuelTypeSection = cui.v2.CreateEmptyContainer(backgroundPanel.name).SetOffset(new LuiOffset(startX, 10, startX + 120, 50));
                    cui.v2.elements.Add(fuelTypeSection);

                    //Element: Fuel Icon
                    LUI.LuiContainer fuelIcon = cui.v2.CreateItemIcon(fuelTypeSection, LuiPosition.None, new LuiOffset(0, 0, 40, 40), fuelType.shortname, fuelType.skin, ColDb.WhiteTrans80);
                    fuelIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Fuel Name
                    ItemDefinition def = ItemManager.FindItemDefinition(fuelType.shortname);
                    string itemName = !string.IsNullOrEmpty(fuelType.displayName) ? fuelType.displayName : def.displayName.english;
                    cui.v2.CreateText(fuelTypeSection, LuiPosition.None, new LuiOffset(42, 21, 120, 37), 10, ColDb.LTD80, itemName);

                    //Element: Fuel Efficiency
                    int fuelPerHour = Mathf.CeilToInt(60f / config.quarryTick * fuelType.amount * 60f * upgrades[qd.level].fuelMultiplier);
                    int fuelPerDay = fuelPerHour * 24;
                    string fuelHourString = FormatNumber(fuelPerHour, sb);
                    string fuelDayString = FormatNumber(fuelPerDay, sb);
                    sb.Clear().Append(fuelHourString).Append(" / h\n").Append(fuelDayString).Append(" / d");
                    cui.v2.CreateText(fuelTypeSection, LuiPosition.None, new LuiOffset(44, -1, 120, 25), 10, ColDb.LightGray, sb.ToString());
                    startX += 122;
                    if (counter >= 3) break;
                }
            }
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void OpenQuarryStoragePanel(BasePlayer player, int quarryId)
        {
            using CUI cui = new CUI(CuiHandler);


            StringBuilder sb = Pool.Get<StringBuilder>();
            LUI.LuiContainer lootUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.Full, "QuarriesInventoriesUI").SetDestroy("QuarriesInventoriesUI");
            //Element: Lower Anchor
            LUI.LuiContainer lowerAnchor = cui.v2.CreatePanel(lootUi, LuiPosition.LowerCenter, LuiOffset.None, ColDb.Transparent, "QuarriesInventoriesUI_Anchor");

            QuarryData qd = data.quarries[quarryId];
            bool isVirtual = qd.quarryType == QuarryType.Virtual;
            QuarryProfile qp = isVirtual ? config.quarryProfiles[qd.profile] : null;
            List<UpgradeConfig> upgrades;
            if (isVirtual)
                upgrades = qp.upgrades;
            else if (qd.quarryType == QuarryType.Static)
                upgrades = config.staticQuarryUpgrades;
            else
                upgrades = config.excavatorUpgrades;

            if ((isVirtual && qp.enableUpgrades) || (!isVirtual && upgrades.Count > 1))
            {
                UpgradeConfig upg = upgrades[qd.level];
                int upgradeAnchorX = -198;
                int upgradeAnchorY = 416;
                if (upg.capacity <= 24)
                {
                    upgradeAnchorX = 192;
                    int inventoryRows = Mathf.CeilToInt(upg.capacity / 6f) - 1;
                    upgradeAnchorY = 233 + 62 * inventoryRows;
                }
                //Element: Background Blur
                LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(lowerAnchor, LuiPosition.None, new LuiOffset(upgradeAnchorX, upgradeAnchorY, upgradeAnchorX + 380, upgradeAnchorY + 212), ColDb.BlackTrans20);
                backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

                //Element: Background Panel
                LUI.LuiContainer backgroundPanel = cui.v2.CreatePanel(lowerAnchor, LuiPosition.None, new LuiOffset(upgradeAnchorX, upgradeAnchorY, upgradeAnchorX + 380, upgradeAnchorY + 212), ColDb.LightGrayTransRust);
                backgroundPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Quarry Upgrades Title
                cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(0, 217, 380, 245), 20, ColDb.LightGray, Lang("QuarryUpgrades", player.UserIDString), TextAnchor.LowerLeft);

                //Element: Now Title
                cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(220, 217, 300, 245), 15, ColDb.LightGray, Lang("Now", player.UserIDString), TextAnchor.LowerCenter);
                bool maxLevel = qd.level + 1 == upgrades.Count;

                if (!maxLevel)
                {
                    //Element: After Title
                    cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(300, 217, 380, 245), 15, ColDb.LightGray, Lang("After", player.UserIDString), TextAnchor.LowerCenter);
                }

                //Element: Quarry Details Panel
                LUI.LuiContainer quarryDetailsPanel = cui.v2.CreatePanel(backgroundPanel, LuiPosition.None, new LuiOffset(0, 120, 380, 212), ColDb.LightGrayTransRust);
                quarryDetailsPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Quarry Icon
                string iconShortname;
                ulong iconSkin;
                if (isVirtual)
                {
                    iconShortname = qp.icon.shortname;
                    iconSkin = qp.icon.skin;
                }
                else if (qd.quarryType == QuarryType.Static)
                {
                    MiningQuarry qr = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.staticNetId)) as MiningQuarry;
                    iconShortname = qr.ShortPrefabName == "pumpjack-static" ? "mining.pumpjack" : "mining.quarry";
                    iconSkin = 0;
                }
                else
                {
                    iconShortname = "mining.quarry";
                    iconSkin = 3681904754;
                }
                LUI.LuiContainer quarryIcon = cui.v2.CreateItemIcon(quarryDetailsPanel, LuiPosition.None, new LuiOffset(16, 16, 76, 76), iconShortname, iconSkin, ColDb.WhiteTrans80);
                quarryIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Quarry Bonus Names
                cui.v2.CreateText(quarryDetailsPanel, LuiPosition.None, new LuiOffset(30, 0, 216, 92), 12, ColDb.LTD80, Lang("QuarryLevelBonuses", player.UserIDString), TextAnchor.MiddleRight);
                //Element: Now Bonuses
                sb.Clear().Append(qd.level + 1).Append('\n').Append(upg.capacity).Append("\nx").Append(upg.multiplier).Append("\nx").Append(upg.fuelMultiplier);
                cui.v2.CreateText(quarryDetailsPanel, LuiPosition.None, new LuiOffset(220, 0, 300, 92), 12, ColDb.RedText, sb.ToString(), TextAnchor.MiddleCenter);

                if (!maxLevel)
                {
                    //Element: Arrow Icon
                    cui.v2.CreateSprite(quarryDetailsPanel, LuiPosition.None, new LuiOffset(290, 36, 310, 56), "assets/icons/chevron_right.png", ColDb.LTD60);

                    //Element: After Bonuses
                    int nextLevel = qd.level + 1;
                    upg = upgrades[nextLevel];
                    sb.Clear().Append(nextLevel + 1).Append('\n').Append(upg.capacity).Append("\nx").Append(upg.multiplier).Append("\nx").Append(upg.fuelMultiplier);
                    cui.v2.CreateText(quarryDetailsPanel, LuiPosition.None, new LuiOffset(300, 0, 380, 92), 12, ColDb.GreenText, sb.ToString(), TextAnchor.MiddleCenter);
                }

                if (isVirtual && qp.enableOutputLink)
                {
                    //Element: Go Back Button
                    LUI.LuiContainer goBackButton = cui.v2.CreateButton(backgroundPanel, LuiPosition.None, new LuiOffset(278, 100, 368, 115), "QuarriesUI closeUpgrade", ColDb.RedBg);
                    goBackButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Go Back Button Text
                    cui.v2.CreateText(goBackButton, LuiPosition.Full, LuiOffset.None, 10, ColDb.RedText, Lang("GoBackToList", player.UserIDString), TextAnchor.MiddleCenter);
                }

                if (maxLevel)
                {
                    //Element: Level Maxed
                    cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(8, 0, 372, 120), 20, ColDb.LightGray, Lang("LevelMaxed", player.UserIDString), TextAnchor.MiddleCenter);
                }
                else
                {
                    //Element: Requirements Title
                    cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(8, 94, 158, 116), 15, ColDb.LightGray, Lang("Requirements", player.UserIDString), TextAnchor.UpperLeft);

                    int startX = 8;

                    if (upg.requiredItems.Count > 0)
                    {
                        //Element: Required Items Section
                        LUI.LuiContainer requiredItemsSection = cui.v2.CreateEmptyContainer(backgroundPanel, add: true).SetOffset(new LuiOffset(startX, 49, startX + 228, 93));
                        int internalX = 8;
                        int itemCount = 0;
                        foreach (var item in upg.requiredItems)
                        {
                            itemCount++;
                            //Element: Item 1 Icon 
                            LUI.LuiContainer item1Icon = cui.v2.CreateItemIcon(requiredItemsSection, LuiPosition.None, new LuiOffset(internalX, 4, internalX + 36, 40), item.shortname, item.skin, ColDb.WhiteTrans80);
                            item1Icon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                            //Element: Item 1 Amount
                            string itemAmount = FormatNumber(item.amount, sb);
                            sb.Clear().Append('x').Append(itemAmount);
                            LUI.LuiContainer item1Amount = cui.v2.CreateText(item1Icon, LuiPosition.None, new LuiOffset(0, -1, 38, 15), 12, ColDb.LightGray, sb.ToString(), TextAnchor.LowerRight);
                            item1Amount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
                            internalX += 44;
                            if (itemCount >= 5) break;

                        }
                        startX += internalX;
                        if (upg.requiredRp > 0)
                        {
                            string text = upg.requireBoth ? Lang("And", player.UserIDString) : Lang("Or", player.UserIDString);
                            //Element: Or Text
                            cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(startX, 46, startX + 44, 96), 20, ColDb.LTD60, text, TextAnchor.MiddleCenter);
                            startX += 44;
                        }
                    }
                    if (upg.requiredRp > 0)
                    {
                        string text = upg.requiredItems.Count > 1 ? Lang("CurrencyFormat", player.UserIDString, upg.requiredRp) : sb.Clear().Append(Lang("ReqCurrency", player.UserIDString)).Append(Lang("CurrencyFormat", player.UserIDString, upg.requiredRp)).ToString();
                        //Element: Currency Text
                        cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(startX, 46, startX + 196, 96), 20, ColDb.LightGray,text, TextAnchor.MiddleLeft);
                    }
                    //Element: Buy Panel
                    LUI.LuiContainer buyPanel = cui.v2.CreatePanel(backgroundPanel, LuiPosition.None, new LuiOffset(0, 8, 380, 38), ColDb.LightGrayTransRust);
                    buyPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Buy Icon
                    cui.v2.CreateSprite(buyPanel, LuiPosition.None, new LuiOffset(7, 6, 25, 24), "assets/icons/store.png", ColDb.LTD60);

                    bool haveItems = upg.requiredItems.Count > 0;
                    bool haveRp = upg.requiredRp > 0;
                    bool twoOptions = !upg.requireBoth && haveItems && haveRp;

                    if (twoOptions)
                    {
                        //Element: Buy Items Button
                        LUI.LuiContainer buyItemsButton = cui.v2.CreateButton(buyPanel, LuiPosition.None, new LuiOffset(33, 0, 183, 30), "QuarriesUI buyItems", ColDb.GreenBg);
                        buyItemsButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                        //Element: Buy Items Button Text
                        cui.v2.CreateText(buyItemsButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.GreenText, Lang("UpgradeItems", player.UserIDString), TextAnchor.MiddleCenter);

                        //Element: Buy Or Text
                        cui.v2.CreateText(buyPanel, LuiPosition.None, new LuiOffset(183, 0, 218, 30), 15, ColDb.LTD60, Lang("Or", player.UserIDString), TextAnchor.MiddleCenter);

                        //Element: Buy Currency Button
                        LUI.LuiContainer buyCurrencyButton = cui.v2.CreateButton(buyPanel, LuiPosition.None, new LuiOffset(218, 0, 368, 30), "QuarriesUI buyCurrency", ColDb.GreenBg);
                        buyCurrencyButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                        //Element: Buy Currency Button Text
                        cui.v2.CreateText(buyCurrencyButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.GreenText, Lang("UpgradeCurrency", player.UserIDString), TextAnchor.MiddleCenter);
                    }
                    else
                    {
                        if (haveItems && haveRp)
                        {
                            //Element: Buy Both Button
                            LUI.LuiContainer buyBothButton = cui.v2.CreateButton(buyPanel, LuiPosition.None, new LuiOffset(33, 0, 368, 30), "QuarriesUI buyBoth", ColDb.GreenBg);
                            buyBothButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                            //Element: Buy Both Button Text
                            cui.v2.CreateText(buyBothButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.GreenText, Lang("UpgradeBoth", player.UserIDString), TextAnchor.MiddleCenter);
                        }
                        else if (haveItems)
                        {
                            //Element: Buy Both Button
                            LUI.LuiContainer buyBothButton = cui.v2.CreateButton(buyPanel, LuiPosition.None, new LuiOffset(33, 0, 368, 30), "QuarriesUI buyItems", ColDb.GreenBg);
                            buyBothButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                            //Element: Buy Both Button Text
                            cui.v2.CreateText(buyBothButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.GreenText, Lang("UpgradeItems", player.UserIDString), TextAnchor.MiddleCenter);
                        }
                        else if (haveRp)
                        {
                            //Element: Buy Both Button
                            LUI.LuiContainer buyBothButton = cui.v2.CreateButton(buyPanel, LuiPosition.None, new LuiOffset(33, 0, 368, 30), "QuarriesUI buyCurrency", ColDb.GreenBg);
                            buyBothButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                            //Element: Buy Both Button Text
                            cui.v2.CreateText(buyBothButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.GreenText, Lang("UpgradeCurrency", player.UserIDString), TextAnchor.MiddleCenter);
                        }
                    }
                    if (isVirtual && upg.requiredDispensers > 0)
                    {
                        int destroyed = 0;
                        int oldDestroyed = 0;
                        bool hasData = data.gatheredDispensers.TryGetValue(player.userID, out var dDispensers);
                        bool hasOldData = previousGatheredDispensers.TryGetValue(player.userID, out var oldDispensers);
                        bool hasUserData = hasData && dDispensers.TryGetValue(qp.dispenserProfile, out destroyed);
                        bool hadOldUserData = hasOldData && oldDispensers.TryGetValue(qp.dispenserProfile, out oldDestroyed);
                        bool hasThisDispenser = hasData && isVirtual && (hasUserData || hadOldUserData);
                        int maxDestroyed = Mathf.Max(destroyed, oldDestroyed);
                        int toDestroy = !hasData || !hasThisDispenser ? upg.requiredDispensers : upg.requiredDispensers - maxDestroyed;
                        if (toDestroy > 0)
                        {
                            //Element: Locked Blur
                            LUI.LuiContainer lockedBlur = cui.v2.CreatePanel(buyPanel, LuiPosition.None, new LuiOffset(0, 0, 380, 30), ColDb.BlackTrans20);
                            lockedBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

                            //Element: Locked Background
                            LUI.LuiContainer lockedBackground = cui.v2.CreatePanel(lockedBlur, LuiPosition.Full, LuiOffset.None, ColDb.LightGrayTransRust);
                            lockedBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

                            //Element: Locked Text
                            sb.Clear().Append("NeedToDestroy_").Append(qp.dispenserProfile);
                            cui.v2.CreateText(lockedBlur, LuiPosition.None, new LuiOffset(0, 0, 380, 30), 15, ColDb.White, Lang(sb.ToString(), player.UserIDString, toDestroy), TextAnchor.MiddleCenter);
                        }
                    }
                }
            }
            if (isVirtual && !qp.enableOutputLink)
            {
                int capacity = config.exitButtonLoc == 3 ? qp.upgrades[qd.level].capacity : 0;
                DrawBackButtons(player, cui, capacity);
            }

            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void DrawBackButtons(BasePlayer player, CUI cui, int capacity)
        {
            if (config.exitButtonLoc == 1)
            {
                //Element: Go Back First Button
                LUI.LuiContainer goBackFirstButton = cui.v2.CreateButton("QuarriesInventoriesUI_Anchor", LuiPosition.None, new LuiOffset(226, 27, 386, 87), "QuarriesUI closeStorage", ColDb.RedBg);
                goBackFirstButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Go Back First Text
                cui.v2.CreateText(goBackFirstButton, LuiPosition.None, new LuiOffset(54, 0, 160, 60), 18, ColDb.RedText, Lang("GoBackToMain2Lines", player.UserIDString), TextAnchor.MiddleLeft);

                //Element: Go Back First Icon
                cui.v2.CreateSprite(goBackFirstButton, LuiPosition.None, new LuiOffset(9, 13, 43, 47), "assets/icons/enter.png", ColDb.RedText);
            }
            else if (config.exitButtonLoc == 2)
            {
                //Element: Go Back Second Button
                LUI.LuiContainer goBackSecondButton = cui.v2.CreateButton("QuarriesInventoriesUI_Anchor", LuiPosition.None, new LuiOffset(570, 110, 628, 168), "QuarriesUI closeStorage", ColDb.RedBg);
                goBackSecondButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Go Back Second Text
                cui.v2.CreateText(goBackSecondButton, LuiPosition.None, new LuiOffset(2, 0, 56, 58), 12, ColDb.RedText, Lang("GoBackToMain3Lines", player.UserIDString), TextAnchor.MiddleCenter);
            }
            else if (config.exitButtonLoc == 3)
            {
                int startPos = 176;
                int slots = 6;

                while (slots < capacity)
                {
                    startPos += 63;
                    slots += 6;
                }
                //Element: Go Back Third Button
                LUI.LuiContainer goBackThirdButton = cui.v2.CreateButton("QuarriesInventoriesUI_Anchor", LuiPosition.None, new LuiOffset(472, startPos, 572, startPos + 23), "QuarriesUI closeStorage", ColDb.RedBg);
                goBackThirdButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Go Back Third Text
                cui.v2.CreateText(goBackThirdButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.RedText, Lang("GoBack", player.UserIDString), TextAnchor.MiddleCenter);
            }
        }

        private void UpdateCantStartToggle(BasePlayer player)
        {
            using CUI cui = new CUI(CuiHandler);
            cui.v2.UpdateText("QuarriesUI_CantStartTitle", Lang("CantStartQuarry", player.UserIDString));
            cui.v2.UpdateText("QuarriesUI_CantStartMessage", Lang("NotEnoughFuel", player.UserIDString));
            cui.v2.SendUi(player);
        }

        private void UpdateQuarryToggle(BasePlayer player, bool isRunning)
        {
            using CUI cui = new CUI(CuiHandler);
            UiCache uc = cache[player.userID];
            StringBuilder sb = Pool.Get<StringBuilder>();
            cui.v2.UpdateText("QuarriesUI_CantStartTitle", "");
            cui.v2.UpdateText("QuarriesUI_CantStartMessage", "");
            string text = isRunning ? Lang("Running", player.UserIDString) : Lang("Stopped", player.UserIDString);
            string color = isRunning ? ColDb.GreenBg : ColDb.RedBg;
            cui.v2.UpdateText("QuarriesUI_QuarryRunning", text, 0, color);
            QuarryData qd = data.quarries[uc.quarryId];
            DrawFuelRequirement(player, cui, qd, config.quarryProfiles[qd.profile]);
            color = isRunning ? ColDb.RedBg : ColDb.GreenBg;
            cui.v2.Update("QuarriesUI_ToggleButton").SetButtonColors(color);
            color = isRunning ? ColDb.RedText : ColDb.GreenText;
            cui.v2.UpdateColor("QuarriesUI_ToggleIcon", color);
            text = isRunning ? Lang("StopQuarry", player.UserIDString) : Lang("TryStartQuarry", player.UserIDString);
            cui.v2.Update("QuarriesUI_ToggleText").SetText(text, 0, color, TextAnchor.MiddleLeft);
            sb.Clear().Append("QuarriesUI_Quarry_").Append(uc.quarryId).Append("_Outline");
            color = isRunning ? ColDb.GreenBg : ColDb.RedBg;
            cui.v2.UpdateColor(sb.ToString(), color);
            cui.v2.SendUi(player);
            Pool.FreeUnmanaged(ref sb);
        }

        private void DrawLinkedQuarriesUI(BasePlayer player, ulong storageNetId)
        {
            cache.TryAdd(player.userID, new());
            cache[player.userID].lastLootedLinkedStorage = storageNetId;
            using CUI cui = new CUI(CuiHandler);

            LUI.LuiContainer sourceUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.Full, "QuarriesInventoriesUI").SetDestroy("QuarriesInventoriesUI");

            //Element: Lower Anchor
            LUI.LuiContainer lowerAnchor = cui.v2.CreatePanel(sourceUi, LuiPosition.LowerCenter, LuiOffset.None, ColDb.Transparent);

            //Element: Background Blur
            LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(lowerAnchor, LuiPosition.None, new LuiOffset(-198, 416, 182, 628), ColDb.BlackTrans20);
            backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

            //Element: Background Panel
            LUI.LuiContainer backgroundPanel = cui.v2.CreatePanel(lowerAnchor, LuiPosition.None, new LuiOffset(-198, 416, 182, 628), ColDb.LightGrayTransRust);
            backgroundPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Linked Quarries Title
            cui.v2.CreateText(backgroundPanel, LuiPosition.None, new LuiOffset(0, 217, 380, 245), 20, ColDb.LightGray, Lang("LinkedQuarries", player.UserIDString), TextAnchor.LowerLeft);
            List<int> connectedQuarries = Pool.Get<List<int>>();
            connectedQuarries.Clear();
            foreach (var quarry in data.quarries)
                if (quarry.Value.redirectNetId == storageNetId)
                    connectedQuarries.Add(quarry.Key);

            //Element: Quarries Scroll
            int scrollHeight = connectedQuarries.Count * 34 + 2;
            if (scrollHeight < 212)
                scrollHeight = 212;

            LUI.LuiContainer quarriesScroll = cui.v2.CreateScrollView(backgroundPanel, LuiPosition.None, new LuiOffset(0, 0, 380, 212), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, defaultScroll, default);
            quarriesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
            cui.v2.CreatePanel(quarriesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

            StringBuilder sb = Pool.Get<StringBuilder>();
            string upgLang = Lang("Upgrade", player.UserIDString);
            string lvlLang = Lang("Level", player.UserIDString);
            foreach (var quarry in connectedQuarries)
            {
                scrollHeight -= 34;
                QuarryData qd = data.quarries[quarry];
                QuarryProfile qp = config.quarryProfiles[qd.profile];
                //Element: Quarry Section
                LUI.LuiContainer quarrySection = cui.v2.CreateEmptyContainer(quarriesScroll, add: true).SetOffset(new LuiOffset(0, scrollHeight, 380, scrollHeight + 32));

                //Element: Quarry Icon
                LUI.LuiContainer quarryIcon = cui.v2.CreateItemIcon(quarrySection, LuiPosition.None, new LuiOffset(8, 6, 28, 26), qp.icon.shortname, qp.icon.skin, ColDb.WhiteTrans80);
                quarryIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                if (qd.owner != player.userID)
                {
                    //Element: Quarry Share Icon
                    cui.v2.CreateSprite(quarrySection, LuiPosition.None, new LuiOffset(20, 19, 30, 29), "assets/icons/clan.png", ColDb.GreenText);
                }

                //Element: Quarry Name
                cui.v2.CreateText(quarrySection, LuiPosition.None, new LuiOffset(34, 10, 200, 30), 15, ColDb.LTD80, Lang(qp.titleTranslation, player.UserIDString), TextAnchor.UpperLeft);

                int startX = 34;
                foreach (var res in qd.resources)
                {
                    ResourceConfig rc = qp.resources[res.configKey];
                    //Element: Quarry Resource Icon 1
                    LUI.LuiContainer quarryResourceIcon1 = cui.v2.CreateItemIcon(quarrySection, LuiPosition.None, new LuiOffset(startX, 2, startX + 12, 14), rc.shortname, rc.skin, ColDb.WhiteTrans80);
                    quarryResourceIcon1.SetMaterial("assets/content/ui/namefontmaterial.mat");
                    startX += 14;
                }
                if (qp.enableUpgrades && (qd.owner == player.userID || qd.authPlayers.Contains(player.userID)))
                {
                    //Element: Quarry Level
                    sb.Clear().Append(lvlLang).Append(qd.level + 1);
                    cui.v2.CreateText(quarrySection, LuiPosition.None, new LuiOffset(202, 0, 292, 32), 15, ColDb.LTD80, sb.ToString(), TextAnchor.MiddleRight);

                    //Element: Upgrade Button
                    sb.Clear().Append("QuarriesUI openUpgrade ").Append(quarry);
                    LUI.LuiContainer upgradeButton = cui.v2.CreateButton(quarrySection, LuiPosition.None, new LuiOffset(298, 6, 368, 26), sb.ToString(), ColDb.GreenBg);
                    upgradeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                    //Element: Upgrade Button Text
                    cui.v2.CreateText(upgradeButton, LuiPosition.Full, LuiOffset.None, 12, ColDb.GreenText, upgLang, TextAnchor.MiddleCenter);
                }
            }
            Pool.FreeUnmanaged(ref connectedQuarries);
            Pool.FreeUnmanaged(ref sb);

            cui.v2.SendUi(player);
        }

        private class ColDb
        {
            public const string Transparent = "1 1 1 0";
            public const string White = "1 1 1 1";
            public const string WhiteTrans80 = "1 1 1 0.8";
            public const string BlackTrans10 = "0 0 0 0.102";
            public const string BlackTrans20 = "0 0 0 0.2";
            public const string GreenBg = "0.365 0.447 0.224 1";
            public const string GreenText = "0.82 1 0.494 1";
            public const string RedBgTrans = "0.667 0.278 0.204 0.549";
            public const string RedBg = "0.667 0.278 0.204 1";
            public const string RedText = "1 0.647 0.58 1";
            public const string DarkGray = "0.153 0.141 0.114 1";
            public const string LTD5 = "0.196 0.18 0.153 1";
            public const string LTD10 = "0.239 0.224 0.196 1";
            public const string LTD15 = "0.282 0.263 0.235 1";
            public const string LTD20 = "0.325 0.306 0.275 1";
            public const string LTD30 = "0.412 0.388 0.357 1";
            public const string LTD40 = "0.498 0.471 0.439 1";
            public const string LTD60 = "0.667 0.635 0.6 1";
            public const string LTD70 = "0.753 0.718 0.678 1";
            public const string LTD80 = "0.839 0.8 0.761 1";
            public const string LightGray = "0.969 0.922 0.882 1";
            public const string LightGrayTransRust = "0.969 0.922 0.882 0.039";
        }

        #endregion

        private class VirtualQuarry : FacepunchBehaviour
        {

            //ALL QUARRIES
            private VqType quarryType = VqType.Default;
            private BoxStorage storage = null;
            private BoxStorage fuelStorage = null;
            private BoxStorage outputStorage2 = null;
            private readonly List<OutputInfo> output = new();
            private readonly Dictionary<string, float> nonIntOutput = new();
            private int dataId = 0;
            private QuarryData qd = null;
            public float lastFuelTakeTime = 0;

            //DEFAULT
            private BoxStorage redirectStorage = null;
            private bool isRedirect = false;

            //STATIC
            private MiningQuarry staticQuarry = null;

            //EXCAVATOR
            private string mineType = "";
            public float quarryRunTime = 0;

            private class OutputInfo
            {
                public string configKey;
                public ItemDefinition def;
                public ulong skin;
                public string name;
                public float amount;
            }

            private enum VqType
            {
                Default,
                Static,
                Excavator
            }

            private void Awake()
            {
                storage = GetComponent<BoxStorage>();
                if (storage == null) return;

                //OLD IMPLEMENTATION NEED TO KEEP TO MAKE OLD QUARRIES WORK
                if (storage.ChildCount > 0)
                    fuelStorage = storage.children[0]?.GetComponent<BoxStorage>();
            }

            public void SetupQuarry(int quarryId)
            {
                dataId = quarryId;
                qd = data.quarries[dataId];
                QuarryProfile profile = null;
                bool hasProfile = qd.quarryType != QuarryType.Virtual || (config.quarryProfiles != null && config.quarryProfiles.TryGetValue(qd.profile, out profile) && profile != null);
                bool requireInputLinking = qd.quarryType == QuarryType.Virtual && hasProfile && profile.enableInputLink;
                if (!fuelStorage)
                    fuelStorage = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.fuelNetId)) as BoxStorage;
                else if (!requireInputLinking)
                    //OLD IMPLEMENTATION NEED TO KEEP TO MAKE OLD QUARRIES WORK
                    qd.fuelNetId = fuelStorage.net.ID.Value;
                bool requireOutputLinking = qd.quarryType == QuarryType.Virtual && hasProfile && profile.enableOutputLink;
                if (requireOutputLinking)
                {
                    isRedirect = true;
                    redirectStorage = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.redirectNetId)) as BoxStorage;
                }

                if (qd.quarryType == QuarryType.Excavator)
                {
                    mineType = qd.excResource;
                    quarryType = VqType.Excavator;
                    outputStorage2 = _plugin.GetQuarryBox(dataId, BoxType.Output2);
                }
                else if (qd.quarryType == QuarryType.Static)
                    quarryType = VqType.Static;
                if (quarryType == VqType.Static)
                    staticQuarry = BaseNetworkable.serverEntities.Find(new NetworkableId(qd.staticNetId)) as MiningQuarry;
                CancelInvoke(MineResources);
                if (quarryType == VqType.Default && (config.quarryProfiles == null || !config.quarryProfiles.ContainsKey(qd.profile)))
                {
                    _plugin.Puts($"Profile {qd.profile} is missing from the configuration, but is found in data. Quarry with ID {dataId} will not work!");
                    return;
                }
                ConfigureOutput();
                nonIntOutput.TryAdd("fuel", 0);
                HookFuelListener();
                if (qd.quarryType == QuarryType.Virtual && !qd.isRunning) return;
                float tickRate = config.quarryTick;
                if (quarryType == VqType.Static)
                    tickRate = config.staticQuarryTick;
                else if (quarryType == VqType.Excavator)
                    tickRate = config.excavatorTick;
                CancelInvoke(MineResources);
                InvokeRepeating(MineResources, tickRate, tickRate);
                MineResources();
            }

            private BoxStorage hookedFuelStorage;

            private void HookFuelListener()
            {
                if (!fuelStorage || fuelStorage.inventory == null) return;
                if (hookedFuelStorage == fuelStorage) return;
                if (hookedFuelStorage && hookedFuelStorage.inventory != null)
                {
                    hookedFuelStorage.inventory.onItemAddedRemoved -= OnFuelStorageItemChanged;
                    hookedFuelStorage.inventory.onItemAddedToStack -= OnFuelStorageItemStacked;
                }
                fuelStorage.inventory.onItemAddedRemoved += OnFuelStorageItemChanged;
                fuelStorage.inventory.onItemAddedToStack += OnFuelStorageItemStacked;
                hookedFuelStorage = fuelStorage;
            }

            private void OnFuelStorageItemChanged(Item item, bool added)
            {
                if (added)
                    OnFuelAdded();
            }

            private void OnFuelStorageItemStacked(Item item, int amount)
            {
                OnFuelAdded();
            }

            public void OnFuelAdded()
            {
                CancelInvoke(StartEngineFromFuel);
                Invoke(StartEngineFromFuel, 0.1f);
            }

            private void StartEngineFromFuel() => SwitchEngine(false);

            public void SwitchEngine(bool canDisable = true)
            {
                if (canDisable && qd.isRunning)
                {
                    qd.isRunning = false;
                    CancelInvoke(MineResources);
                }
                else
                {
                    if (!canDisable && qd.isRunning) return;
                    if (!CalculateFuel(false) || (quarryType == VqType.Excavator && mineType.Length == 0))
                    {
                        qd.isRunning = false;
                        CancelInvoke(MineResources);
                        return;
                    }
                    qd.isRunning = true;
                    float tickRate = config.quarryTick;
                    if (quarryType == VqType.Static)
                        tickRate = config.staticQuarryTick;
                    else if (quarryType == VqType.Excavator)
                        tickRate = config.excavatorTick;
                    CancelInvoke(MineResources);
                    InvokeRepeating(MineResources, tickRate, tickRate);
                    MineResources();
                }
            }

            public void SwitchExcavatorType(string type)
            {
                mineType = type;
                qd.excResource = type;
                ConfigureOutput();
            }

            private void ConfigureOutput()
            {
                output.Clear();
                if (quarryType == VqType.Default)
                {
                    QuarryProfile qp = config.quarryProfiles[qd.profile];
                    Dictionary<string, ResourceConfig> configResources = qp.resources;
                    List<UpgradeConfig> levels = qp.upgrades;
                    if (qd.level > levels.Count - 1)
                        qd.level = levels.Count - 1;
                    foreach (var resource in qd.resources)
                    {
                        if (!configResources.TryGetValue(resource.configKey, out var rc)) continue;
                        ItemDefinition def = ItemManager.FindItemDefinition(rc.shortname);
                        if (!def) continue;
                        output.Add(new() { configKey = resource.configKey, def = def, skin = rc.skin, name = rc.name, amount = resource.work * levels[qd.level].multiplier });
                    }
                }
                else if (quarryType == VqType.Static)
                {
                    List<StaticQuarryOutput> quarryOutput = null;
                    if (staticQuarry.ShortPrefabName == "pumpjack-static")
                    {
                        staticQuarry.staticType = MiningQuarry.QuarryType.Basic;
                        quarryOutput = config.staticPumpJackOutput;
                    }
                    else if (staticQuarry.staticType == MiningQuarry.QuarryType.Sulfur)
                        quarryOutput = config.staticSulfurOutput;
                    else if (staticQuarry.staticType == MiningQuarry.QuarryType.HQM)
                        quarryOutput = config.staticHqmOutput;
                    else if (staticQuarry.staticType == MiningQuarry.QuarryType.Basic)
                        quarryOutput = config.staticMetalOutput;
                    if (quarryOutput != null)
                        foreach (var resource in quarryOutput)
                        {
                            ItemDefinition def = ItemManager.FindItemDefinition(resource.shortname);
                            if (!def) continue;
                            output.Add(new() { configKey = $"{resource.shortname}_{resource.skin}", def = def, skin = resource.skin, name = resource.displayName, amount = resource.amount });
                        }
                }
                else if (quarryType == VqType.Excavator && mineType.Length > 0)
                    foreach (var resource in config.excavatorResources[mineType])
                    {
                        ItemDefinition def = ItemManager.FindItemDefinition(resource.shortname);
                        if (!def) continue;
                        output.Add(new() { configKey = $"{resource.shortname}_{resource.skin}", def = def, skin = resource.skin, name = resource.displayName, amount = resource.amount });
                    }
            }

            private void MineResources()
            {
                if (!CalculateFuel() || (isRedirect && (!redirectStorage || redirectStorage.IsDestroyed)))
                {
                    qd.isRunning = false;
                    CancelInvoke(MineResources);
                    return;
                }
                if (quarryType == VqType.Excavator)
                    quarryRunTime += config.excavatorTick;
                foreach (var resource in output)
                {
                    nonIntOutput.TryAdd(resource.configKey, 0);
                    int amount = Mathf.FloorToInt(resource.amount);
                    nonIntOutput[resource.configKey] += resource.amount % 1;
                    if (nonIntOutput[resource.configKey] > 1)
                    {
                        nonIntOutput[resource.configKey]--;
                        amount++;
                    }
                    if (amount == 0) continue;
                    if (isRedirect)
                    {
                        Item item = ItemManager.Create(resource.def, amount, resource.skin);
                        if (!string.IsNullOrEmpty(resource.name))
                            item.name = resource.name;
                        if (!item.MoveToContainer(redirectStorage.inventory))
                        {
                            qd.isRunning = false;
                            CancelInvoke(MineResources);
                            return;
                        }
                        continue;
                    }
                    if (quarryType == VqType.Excavator && outputStorage2)
                    {
                        int amount2 = amount / 2;
                        int amount1 = amount - amount2;
                        if (amount1 > 0 && !TryDepositMinedItem(storage, resource, amount1))
                        {
                            qd.isRunning = false;
                            CancelInvoke(MineResources);
                            return;
                        }
                        if (amount2 > 0 && !TryDepositMinedItem(outputStorage2, resource, amount2))
                        {
                            qd.isRunning = false;
                            CancelInvoke(MineResources);
                            return;
                        }
                        continue;
                    }
                    Item single = ItemManager.Create(resource.def, amount, resource.skin);
                    if (!string.IsNullOrEmpty(resource.name))
                        single.name = resource.name;
                    storage.inventory.canAcceptItem = (_, _, _) => true;
                    bool movedItem = single.MoveToContainer(storage.inventory);
                    storage.inventory.canAcceptItem = (_, _, _) => false;
                    if (!movedItem)
                    {
                        qd.isRunning = false;
                        CancelInvoke(MineResources);
                        return;
                    }
                }
            }

            private static bool TryDepositMinedItem(BoxStorage box, OutputInfo resource, int amount)
            {
                if (!box || box.IsDestroyed || box.inventory == null) return false;
                Item item = ItemManager.Create(resource.def, amount, resource.skin);
                if (!string.IsNullOrEmpty(resource.name))
                    item.name = resource.name;
                box.inventory.canAcceptItem = (_, _, _) => true;
                bool moved = item.MoveToContainer(box.inventory);
                box.inventory.canAcceptItem = (_, _, _) => false;
                if (!moved)
                    item.Remove();
                return moved;
            }

            private bool CalculateFuel(bool takeFuel = true)
            {
                if (!fuelStorage) return false;
                if (quarryType == VqType.Default && !config.quarryProfiles.ContainsKey(qd.profile))
                {
                    _plugin.Puts($"Profile {qd.profile} is missing from the configuration, but is found in data. Quarry with ID {dataId} will not work!");
                    return false;
                }
                if (quarryType == VqType.Excavator && mineType.Length == 0) return false;
                List<FuelItem> requiredFuel;
                if (quarryType == VqType.Default)
                    requiredFuel = config.quarryProfiles[qd.profile].fuelItems;
                else if (quarryType == VqType.Static)
                    requiredFuel = config.staticFuelItems;
                else
                    requiredFuel = config.excavatorFuelItems;
                float levelMult = 1;
                if (quarryType == VqType.Default)
                    levelMult = config.quarryProfiles[qd.profile].upgrades[qd.level].fuelMultiplier;
                else if (quarryType == VqType.Static)
                    levelMult = config.staticQuarryUpgrades[qd.level].fuelMultiplier;
                else if (quarryType == VqType.Excavator)
                    levelMult = config.excavatorUpgrades[qd.level].fuelMultiplier;
                if (TakeFuel(fuelStorage, requiredFuel, levelMult, takeFuel)) return true;
                return false;
            }

            private bool TakeFuel(BoxStorage storage, List<FuelItem> fuel, float notRoundedAmount, bool takeFuel = true)
            {
                if (nonIntOutput["fuel"] >= notRoundedAmount)
                {
                    if (takeFuel)
                        nonIntOutput["fuel"] -= notRoundedAmount;
                    return true;
                }
                lastFuelTakeTime = Time.time;
                int amount = Mathf.CeilToInt(notRoundedAmount);
                bool haveRequired = false;
                int inventoryAmount = 0;
                foreach (var fuelType in fuel)
                {
                    foreach (var item in storage.inventory.itemList)
                    {
                        if (item.skin == fuelType.skin && item.info.shortname == fuelType.shortname)
                        {
                            inventoryAmount += item.amount;
                            if (inventoryAmount >= amount)
                            {
                                haveRequired = true;
                                break;
                            }
                        }
                    }
                    if (haveRequired) break;
                }
                if (!haveRequired) return false;
                int itemsToTake = Mathf.CeilToInt(notRoundedAmount);
                float remainingFuel = itemsToTake - notRoundedAmount;
                if (takeFuel)
                {
                    foreach (var fuelType in fuel)
                    {
                        foreach (var item in storage.inventory.itemList.ToList())
                        {
                            if (itemsToTake <= 0) break;
                            if (item.skin == fuelType.skin && item.info.shortname == fuelType.shortname)
                            {
                                if (item.amount > itemsToTake)
                                {
                                    nonIntOutput["fuel"] += remainingFuel;
                                    item.amount -= itemsToTake;
                                    item.MarkDirty();
                                    break;
                                }
                                if (item.amount == itemsToTake)
                                {
                                    nonIntOutput["fuel"] += remainingFuel;
                                    item.GetHeldEntity()?.Kill();
                                    item.RemoveFromContainer();
                                    item.Remove();
                                    break;
                                }
                                itemsToTake -= item.amount;
                                item.GetHeldEntity()?.Kill();
                                item.RemoveFromContainer();
                                item.Remove();
                            }
                        }
                        if (itemsToTake <= 0) break;
                    }
                }
                //ItemManager.DoRemoves();
                return true;
            }

            private void OnDestroy()
            {
                CancelInvoke();
                if (hookedFuelStorage && hookedFuelStorage.inventory != null)
                {
                    hookedFuelStorage.inventory.onItemAddedRemoved -= OnFuelStorageItemChanged;
                    hookedFuelStorage.inventory.onItemAddedToStack -= OnFuelStorageItemStacked;
                }
            }
        }

        private void LoadMessages()
        {
            Dictionary<string, string> translations = new Dictionary<string, string>()
            {
                ["NoSurveyThrow"] = "You can't throw survey charges!\nUse <color=#5c81ed>/{0}</color> instead.",
                ["NoPermission"] = "You don't have access to <color=#5c81ed>Virtual Quarries</color>!",
                ["InvalidAmountInput"] = "Number <color=#5c81ed>{0}</color> is not valid number!",
                ["AccessListStart"] = "All players added to this quarry:",
                ["AccessListNoAdded"] = "\n<color=#5c81ed>No one is added to this quarry!</color>",
                ["NoPermToUpgrade"] = "You don't have required permissions to upgrade your quarry further!",
                ["NotEnoughCurrency"] = "You don't have required amount of <color=#5c81ed>currency</color> to upgrade this quarry!",
                ["NotEnoughItems"] = "You don't have required amount of <color=#5c81ed>items</color> to upgrade this quarry!",
                ["NewResourceDug"] = "During upgrading your quarry you found a <color=#5c81ed>new resource deposit</color>!",
                ["OnlyTeamShare"] = "You can share quarries only with your <color=#5c81ed>teammates</color>!",
                ["RemovedFromAllQuarries"] = "Player <color=#5c81ed>{0}</color> lost access to all of your quarries!",
                ["SharedAllQuarries"] = "Player <color=#5c81ed>{0}</color> gained access to all of your quarries!",
                ["UserRemoved"] = "Player <color=#5c81ed>{0}</color> gained access to your quarry!",
                ["UserAdded"] = "Player <color=#5c81ed>{0}</color> list access to your quarry!",
                ["MovedItemsToYourInventory"] = "Successfully moved <color=#5c81ed>{0}</color> items from quarry to your inventory!",
                ["QuarryRemoved"] = "You've <color=#5c81ed>successfully</color> removed your Virtual Quarry!",
                ["NotAllowedToPlace"] = "You are <color=#5c81ed>not allowed</color> to place new quarries!",
                ["TooManyQuarries"] = "You've reached your limit of placed <color=#5c81ed>quarries of this type</color>!",
                ["NoRequiredItems"] = "You don't have <color=#5c81ed>required items</color> to place this quarry!",
                ["InvalidSurveyResult"] = "Your survey result is not valid to place a quarry. Try <color=#5c81ed>searching again</color>.",
                ["VirtualQuarriesMenu"] = "VIRTUAL QUARRIES MANAGEMENT MENU",
                ["QuarryList"] = "YOUR QUARRIES",
                ["SearchQuarry"] = "Search quarry by user, resource, type...",
                ["ViewOwned"] = "VIEW OWNED",
                ["ViewShared"] = "VIEW SHARED",
                ["AdminView"] = "ADMIN VIEW",
                ["RemoveFromAll"] = "REM. FROM ALL",
                ["AddToAll"] = "ADD TO ALL",
                ["NoQuarrySelected"] = "<size=36>NO QUARRY SELECTED</size>\n\nSELECT <color=#5D7239>ONE OF YOUR QUARRIES</color> OR <color=#5D7239>PLACE NEW ONE</color> FOR QUARRY DETAILS!\n\nYOUR QUARRY TYPE LIMITS:",
                ["NoQuarrySelectedAccessOnly"] = "<size=36>NO QUARRY SELECTED</size>\n\nSELECT <color=#5D7239>ONE OF YOUR QUARRIES</color> FOR QUARRY DETAILS!\n\nYOU ARE <color=#bbd988>NOT ALLOWED</color> TO PLACE NEW QUARRIES!",
                ["PermTranslation"] = "\n<color=#5D7239>{0}</color> - {1}",
                ["AllQuarries"] = "ALL QUARRIES",
                ["QuarryDetails"] = "QUARRY DETAILS",
                ["Stopped"] = "STOPPED",
                ["Running"] = "RUNNING",
                ["QuarryLevel"] = "QUARRY LEVEL",
                ["FuelRemaining"] = "FUEL REMAINING",
                ["QuarryResources"] = "QUARRY RESOURCES",
                ["QuarryControls"] = "QUARRY CONTROLS",
                ["TryStartQuarry"] = "TRY START\nQUARRY ENGINE",
                ["StopQuarry"] = "TURN OFF\nQUARRY ENGINE",
                ["LinkOutputInventory"] = "LINK QUARRY\nOUTPUT INVENTORY",
                ["QuarryOutputSet"] = "QUARRY OUTPUT SET",
                ["OpenOutputInventory"] = "OPEN OUTPUT\nINVENTORY",
                ["LinkInputInventory"] = "LINK QUARRY\nINPUT CONTAINER",
                ["QuarryInputSet"] = "QUARRY INPUT SET",
                ["OpenInputInventory"] = "OPEN FUEL\nCONTAINER",
                ["OwnedBy"] = "QUARRY OWNED BY",
                ["GiveAccess"] = "GIVE ACCESS\nTO QUARRY",
                ["RevokeAccess"] = "REVOKE ACCESS\nTO QUARRY",
                ["AuthList"] = "AUTHORIZED PLAYERS",
                ["RemoveQuarry"] = "REMOVE QUARRY",
                ["QuarrySourceSearch"] = "QUARRY SOURCE SEARCH",
                ["SelectQuarrySurveyHint"] = "Select which survey type you want to use to search for quarry.\nEach survey can give different quarry ouput type.",
                ["FoundResource"] = "FOUND RESOURCE",
                ["OutputPerMinute"] = "OUTPUT PER MINUTE",
                ["RequiredItem"] = "REQUIRED ITEM",
                ["Amount"] = "AMOUNT",
                ["QuarryAutoFinder"] = "QUARRY AUTO FINDER",
                ["ResourceName"] = "RESOURCE NAME",
                ["MinOutput"] = "MIN. OUTPUT",
                ["MaxOutput"] = "MAX. OUTPUT",
                ["MinimalResourcesTitle"] = "MINIMAL RESOURCES COUNT",
                ["MinimalResourcesHint"] = "How many from selected resources must hit.",
                ["RequiredItemAmount"] = "REQUIRED SURVEY COUNT",
                ["BasedOnYouNeed"] = "Based on selected options, you need:",
                ["ReqToStart"] = "TO TRY SEARCHING",
                ["ReqForGuarantee"] = "FOR 100% CHANCE",
                ["YouHave"] = "YOU HAVE",
                ["Search"] = "SEARCH",
                ["RollChance"] = "[{0}% CHANCE]",
                ["NotEnough"] = "NOT ENOUGH",
                ["ZeroPercent"] = "[0% CHANCE]",
                ["NotPossible"] = "NOT POSSIBLE",
                ["NoRequiredSurvey"] = "You don't have required <color=#5c81ed>survey charge type</color> to find new resource deposit!",
                ["SearchFailed"] = "Unfortunately, you didn't found your desired <color=#5c81ed>resource deposit</color>...",
                ["SearchSuccessful"] = "You've successfully found your desired <color=#5c81ed>resource deposit</color>!",
                ["TryAgain"] = "TRY AGAIN",
                ["StartSearching"] = "SEARCH",
                ["PlaceQuarry"] = "PLACE QUARRY",
                ["RollFirst"] = "ROLL FIRST",
                ["QuarryRemoveTitle"] = "QUARRY REMOVAL CONFIRMATION",
                ["QuarryRemoveDescription"] = "You're about to remove your virtual quarry.\nThis action can't be undone!",
                ["ConfirmRemove"] = "CONFIRM & REMOVE",
                ["CancelGoBack"] = "CANCEL & GO BACK",
                ["PlayerManagement"] = "QUARRY PLAYER MANAGEMENT",
                ["AllQuarriesUserRemoveHint"] = "Select player that you want to revoke access to your quarries.\nUser will lose access to all of your quarries!",
                ["AllQuarriesUserAddHint"] = "Select player that you want to give access to your quarries.\nUser will gain access to of your owned quarries!",
                ["QuarryUserRemoveHint"] = "Select player that you want to revoke access to your quarry.\nUser will lose access to only this one quarry!",
                ["QuarryUserAddHint"] = "Select player that you want to give access to your quarry.\nUser will gain access to this one quarry!",
                ["SearchPlayer"] = "Search player...",
                ["NoLongerProfilePerm"] = "You no longer have permission to this quarry!\n<color=#5c81ed>Renew your permission</color> or <color=#5c81ed>remove quarry</color> to continue!",
                ["TooManyQuarriesPermMissing"] = "You no longer have permission to this amount of this type quarries!\n<color=#5c81ed>Renew your permission</color> or <color=#5c81ed>decrease amount of quarries</color> to continue!",
                ["SupportedFuel"] = "SUPPORTED FUEL TYPES",
                ["QuarryUpgrades"] = "QUARRY UPGRADES",
                ["Now"] = "NOW",
                ["After"] = "AFTER",
                ["QuarryLevelBonuses"] = "CURRENT LEVEL:\nOUTPUT CAPACITY:\nOUTPUT MULTIPLIER:\nFUEL USAGE MULTIPLIER:",
                ["GoBackToList"] = "GO BACK TO LIST",
                ["LevelMaxed"] = "<size=28><color=#D1FF7E>QUARRY LEVEL MAXED</color></size>\nYou can't upgrade this quarry further!",
                ["Requirements"] = "REQUIREMENTS",
                ["Or"] = "OR",
                ["And"] = "AND",
                ["ReqCurrency"] = "<color=#D1FF7E>CURRENCY: </color>",
                ["CurrencyFormat"] = "{0} RP",
                ["UpgradeItems"] = "UPGRADE FOR ITEMS",
                ["UpgradeCurrency"] = "UPGRADE FOR CURRENCY",
                ["UpgradeBoth"] = "UPGRADE QUARRY LEVEL",
                ["GoBackToMain2Lines"] = "GO BACK TO\nMAIN PAGE",
                ["GoBackToMain3Lines"] = "GO BACK\nTO MAIN\nPAGE",
                ["GoBack"] = "GO BACK",
                ["ErrorOccured"] = "An error has occured! Please contact <color=#5c81ed>Administrator</color>!",
                ["CantStartQuarry"] = "CAN'T START QUARRY",
                ["NotEnoughFuel"] = "NOT ENOUGH FUEL!",
                ["NoPermissionQuarry"] = "You don't have access to <color=#5c81ed>Quarries</color>!",
                ["NoPermissionPumpJack"] = "You don't have access to <color=#5c81ed>Pump Jacks</color>!",
                ["NoPermissionExcavator"] = "You don't have access to <color=#5c81ed>Giant Excavator</color>!",
                ["ExcavatorClaimNotReady"] = "You can claim this excavator when the signal computer reaches <color=#5c81ed>100%</color>.",
                ["ExcavatorSignalTipMissing"] = "<color=#ffffff>Supply Ready In</color>\n<color=#ff5f5f>Claim Excavator First</color>",
                ["ExcavatorSignalTipReady"] = "<color=#ffffff>Supply Ready In</color>\n<color=#7CFF7C>READY</color>",
                ["ExcavatorSignalTipLeft"] = "<color=#ffffff>Supply Ready In</color>\n<color=#ff5f5f>{0}</color>",
                ["ExcavatorSignalTipDropping"] = "<color=#ffffff>Dropping At</color>\n<color=#7CFF7C>{0}</color>",
                ["NoAccessToShare"] = "You don't have permission to <color=#5c81ed>share quarries</color>!",
                ["NoAirDrops"] = "Excavator supply drops has been <color=#5c81ed>disabled</color>!",
                ["NotEnoughMined"] = "You need to have excavator turned on for at least <color=#5c81ed>{0}</color> more in order to call supply drop!",
                ["SupplyDropCalled"] = "Excavator supply drop is falling at <color=#5c81ed>{0}</color>!",
                ["QuarryLinkStarted"] = "Quarry storage linking enabled. Open storage that you want to link to this quarry. You have <color=#5c81ed>30 seconds</color>. After that time, action will be canceled.",
                ["QuarryUnlinked"] = "Quarry storage unlinked successfully!",
                ["QuarryLinkedSuccessfully"] = "Quarry storage has been linked successfully!",
                ["SwitchedResource"] = "You've switched excavator resource to <color=#5c81ed>{0}</color>!",
                ["PrivateInventoryInfo"] = "This inventory and items are visible <color=#5c81ed>only</color> for you!",
                ["LinkedQuarries"] = "LINKED QUARRIES",
                ["Level"] = "LEVEL ",
                ["Upgrade"] = "UPGRADE"

            };
            foreach (var profile in config.quarryProfiles.Values)
            {
                if (!string.IsNullOrEmpty(profile.dispenserProfile))
                    translations.TryAdd($"NeedToDestroy_{profile.dispenserProfile}", "You need to break <color=#AA4734>{0} Rocks</color> to unlock this upgrade.");
                if (profile.fuelItems != null)
                {
                    foreach (var fuelItem in profile.fuelItems)
                    {
                        ItemDefinition def = ItemManager.FindItemDefinition(fuelItem.shortname);
                        if (!def) continue;
                        if (fuelItem.skin == 0)
                            translations.TryAdd($"Fuel_{fuelItem.skin}", def.displayName.english);
                        else
                            translations.TryAdd($"Fuel_{fuelItem.shortname}", def.displayName.english);
                    }
                }
                if (profile.resources == null) continue;
                foreach (var res in profile.resources)
                {
                    ItemDefinition def = ItemManager.FindItemDefinition(res.Value.shortname);
                    if (!def) continue;
                    translations.TryAdd($"ItemName_{res.Key}", def.displayName.english);
                }
                if (profile.titleTranslation == "QuarryTitle")
                    translations.TryAdd(profile.titleTranslation, "Mining Quarry");
                else if (profile.titleTranslation == "PumpjackTitle")
                    translations.TryAdd(profile.titleTranslation, "Mining Pumpjack");
                else
                    translations.TryAdd(profile.titleTranslation, profile.titleTranslation);
            }
            foreach (var fuelItem in config.excavatorFuelItems)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(fuelItem.shortname);
                if (!def) continue;
                if (fuelItem.skin == 0)
                    translations.TryAdd($"Fuel_{fuelItem.skin}", def.displayName.english);
                else
                    translations.TryAdd($"Fuel_{fuelItem.shortname}", def.displayName.english);
            }
            foreach (var fuelItem in config.staticFuelItems)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(fuelItem.shortname);
                if (!def) continue;
                if (fuelItem.skin == 0)
                    translations.TryAdd($"Fuel_{fuelItem.skin}", def.displayName.english);
                else
                    translations.TryAdd($"Fuel_{fuelItem.shortname}", def.displayName.english);
            }
            foreach (var survey in config.surveys.Values)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(survey.surveyItem.shortname);
                if (!def) continue;
                translations.TryAdd(survey.surveyTranslation, def.displayName.english);
            }
            lang.RegisterMessages(translations, this);
        }

        private void Mess(BasePlayer player, string key, params object[] args) => SendReply(player, Lang(key, player.UserIDString, args));

        private string Lang(string key, string id = null, params object[] args)
        {
            if (args.Length == 0)
                return lang.GetMessage(key, this, id);
            return string.Format(lang.GetMessage(key, this, id), args);
        }

        private static PluginConfig config;
        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(config = new()
            {
                commandList = new()
                {
                    "qr",
                    "quarry",
                    "quarries",
                    "vq",
                    "virtualquarry",
                    "virtualquarries"
                },
                permissions = new()
                {
                    { "virtualquarries.default", new() {
                        { "quarry", 3 },
                        { "pumpjack", 1 }
                    } },
                    { "virtualquarries.vip", new() {
                        { "quarry", 3 },
                        { "pumpjack", 2 }
                    } }
                },
                surveys = new()
                {
                    { "survey", new() { surveyTranslation = "SurveyCharge" } }
                },
                staticMetalOutput = new()
                {
                    new() { shortname = "stones", amount = 150 },
                    new() { shortname = "metal.ore", amount = 22.5f }
                },
                staticSulfurOutput = new()
                {
                    new() { shortname = "sulfur.ore", amount = 22.5f }
                },
                staticHqmOutput = new()
                {
                    new() { shortname = "hq.metal.ore", amount = 1.5f }
                },
                staticPumpJackOutput = new()
                {
                    new() { shortname = "crude.oil", amount = 6 }
                },
                staticQuarryUpgrades = new()
                {
                    new() { capacity = 18 },
                    new() { capacity = 18, multiplier = 1.2f, requiredItems = new() { new() { shortname = "wood", amount = 7000, skin = 0 }, new() { shortname = "stones", amount = 5000, skin = 0 } }, requiredRp = 6000 },
                },
                excavatorUpgrades = new()
                {
                    new() { capacity = 18 },
                    new() { capacity = 18, multiplier = 1.2f, requiredItems = new() { new() { shortname = "wood", amount = 7000, skin = 0 }, new() { shortname = "stones", amount = 5000, skin = 0 } }, requiredRp = 6000 },
                },
                dispenserProfiles = new()
                {
                    { "ores", new()
                    {
                        "metal-ore",
                        "sulfur-ore",
                        "stone-ore",
                    } },
                    { "oak-trees", new()
                    {
                        "oak_a",
                        "oak_a_tundra",
                        "oak_b",
                        "oak_b_tundra",
                        "oak_c",
                        "oak_d",
                        "oak_e",
                        "oak_e_tundra",
                        "oak_f",
                        "oak_f_tundra",
                    } }
                },
                excavatorResources = new()
                {
                    { "Stone", new() {
                        new() { shortname = "stones", amount = 5000 }
                    } },
                    { "Metal", new() {
                        new() { shortname = "metal.fragments", amount = 2500 }
                    } },
                    { "Sulfur", new() {
                        new() { shortname = "sulfur.ore", amount = 1000 }
                    } },
                    { "HQM", new() {
                        new() { shortname = "hq.metal.ore", amount = 50 }
                    } }
                },
                quarryProfiles = new()
                {
                    { "quarry", new()
                        {
                            chance = 25,
                            titleTranslation = "QuarryTitle",
                            icon = new() { shortname = "mining.quarry" },
                            surveyType = "survey",
                            requiredItems = new()
                            {
                                new() { shortname = "mining.quarry", amount = 1, skin = 0 }
                            },
                            resources = new()
                            {
                                { "stone", new() { chance = 0, alwaysInclude = true, shortname = "stones", outputMax = 300f, outputMin = 150f } },
                                { "metal", new() { chance = 50, alwaysInclude = false, shortname = "metal.ore", outputMax = 45f, outputMin = 22.5f, permission = "virtualquarries.metal" } },
                                { "sulfur", new() { chance = 50, alwaysInclude = false, shortname = "sulfur.ore", outputMax = 30.5f, outputMin = 15.0f } },
                                { "hq", new() { chance = 10, alwaysInclude = false, shortname = "hq.metal.ore", outputMax = 2.0f, outputMin = 0.3f } },
                                { "scrap", new() { chance = 5, alwaysInclude = false, shortname = "scrap", outputMax = 1.0f, outputMin = 0.1f, permission = "virtualquarries.scrap" , additionalItems = new() { new() { shortname = "wood", amount = 7000, skin = 0 } } } }
                            },
                            upgrades = new()
                            {
                                new() { capacity = 6, multiplier = 1, requiredItems = new(), requiredRp = 0 },
                                new() { capacity = 9, multiplier = 1.2f, requiredItems = new() { new() { shortname = "wood", amount = 7000, skin = 0 }, new() { shortname = "stones", amount = 5000, skin = 0 } }, requiredRp = 6000, additionalResources = new() { "scrap" } },
                            },
                        }
                    },
                    { "pumpjack", new()
                        {
                            permission = "virtualquarries.pumpjack",
                            titleTranslation = "PumpjackTitle",
                            icon = new() { shortname = "mining.pumpjack" },
                            surveyType = "survey",
                            requiredItems = new()
                            {
                                new() { shortname = "mining.pumpjack", amount = 1, skin = 0 }
                            },
                            resources = new()
                            {
                                { "crude", new() { chance = 0, alwaysInclude = true, shortname = "crude.oil", outputMax = 3.0f, outputMin = 0.8f } },
                            },
                            upgrades = new()
                            {
                                new() { capacity = 6, multiplier = 1, requiredItems = new(), requiredRp = 0 },
                                new() { capacity = 9, multiplier = 1.2f, requiredItems = new() { new() { shortname = "wood", amount = 14000, skin = 0 }, new() { shortname = "stones", amount = 10000, skin = 0 } }, requiredRp = 12000 },
                            },
                        }
                    }
                }
            }, true);
        }

        private class PluginConfig
        {
            [JsonProperty("Command List")]
            public List<string> commandList = new();

            [JsonProperty("UI Action Cooldown (in seconds, 0 to disable)")]
            public float commandCooldown = 0f;

            [JsonProperty("Enable Console Logs")]
            public bool consoleLogs = true;

            [JsonProperty("PopUpAPI - Preset Name")]
            public string popUpPreset = "UpperCenter";

            [JsonProperty("RedeemStorageAPI - Storage Name")]
            public string redeemInventoryName = "default";

            [JsonProperty("Require Permission For Use")]
            public bool requirePermission = false;

            [JsonProperty("Lock Access To Quarry Profiles If Lost Permission")]
            public bool lockAccessNoPerm = false;

            [JsonProperty("Check For Quarry Amount Permission (option above must be set to true to work)")]
            public bool checkQuarryAmount = false;

            [JsonProperty("OnEntityTakeDamage Return Value")]
            public bool returnValue = false;

            [JsonProperty("Mining Quarry/Pump Jack Limit Permissions")]
            public Dictionary<string, Dictionary<string, int>> permissions = new();

            [JsonProperty("Sharing - Require Permission")]
            public bool sharingRequirePermission = false;

            [JsonProperty("Sharing - Remove Members If Owner Offline More Than X Days (0, to disable)")]
            public int offlineQuarryOwnerKickTime = 0;

            [JsonProperty("Sharing - Share Only To Teammates")]
            public bool shareClanOnly = false;

            [JsonProperty("Data - Enable Data Wipe On Server Wipe")]
            public bool wipeData = true;

            [JsonProperty("Data - Wipe Data Only On Force Wipe")]
            public bool wipeDataForceOnly = false;

            [JsonProperty("Data - Store Container Data In File And Restore On Server Wipe")]
            public bool storeContainers = false;

            [JsonProperty("Data - Store Container Interval (in seconds)")]
            public int containerSaveInterval = 1800;

            [JsonProperty("Quarry Tick (how often quarries dig resources, in seconds)")]
            public float quarryTick = 60f;

            [JsonProperty("Static Quarry Tick (how often quarries dig resources, in seconds)")]
            public float staticQuarryTick = 60f;

            [JsonProperty("Excavator Quarry Tick (how often quarries dig resources, in seconds)")]
            public float excavatorTick = 60f;

            [JsonProperty("Storage Prefab")]
            public string storagePrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";

            [JsonProperty("Sound - Start Sound")]
            public string startSound = "assets/prefabs/npc/autoturret/effects/online.prefab";

            [JsonProperty("Sound - Stop Sound")]
            public string stopSound = "assets/prefabs/npc/autoturret/effects/offline.prefab";

            [JsonProperty("Survey Charge - Allow Throwing Survey Charges")]
            public bool surveyThrow = false;

            [JsonProperty("Survey Charget Types")]
            public Dictionary<string, SurveyConfig> surveys = new();

            [JsonProperty("Upgrades - Used Economy Plugin (0 - None, See Website For More Info)")]
            public int economyPlugin = 0;

            [JsonProperty("Upgrades - Economy Currency (If Economy Plugin Is 5 - ShoppyStock)")]
            public string economyCurrency = "rp";

            [JsonProperty("Removing Quarries - Refund Items")]
            public bool refundRemove = true;

            [JsonProperty("Removing Quarries - Refund Upgrades")]
            public bool refundUpgrades = false;

            [JsonProperty("Go Back Button - Position (1-3)")]
            public int exitButtonLoc = 1;

            [JsonProperty("Static Quarries - Enable")]
            public bool staticQuarries = false;

            [JsonProperty("Static Quarries - Disable Running Effect")]
            public bool disableRunningEffect = false;

            [JsonProperty("Excavator Quarry - Enable")]
            public bool excavatorQuarry = false;

            [JsonProperty("Excavator Quarry - Disable Running Effect")]
            public bool disableExcavatorRunningEffect = false;

            [JsonProperty("Static Quarries - Quarry Requires Permission")]
            public bool quarryPerm = false;

            [JsonProperty("Static Quarries - Pump Jack Requires Permission")]
            public bool pumpJackPerm = false;

            [JsonProperty("Static Quarries - Excavator Requires Permission")]
            public bool excavatorPerm = false;

            [JsonProperty("Excavator Quarry - Fuel Item", NullValueHandling = NullValueHandling.Ignore)]
            public FuelItem _oldExcavatorFuel = null;

            [JsonProperty("Static Quarries - Fuel Item", NullValueHandling = NullValueHandling.Ignore)]
            public FuelItem _oldStaticFuel = null;

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context)
            {
                staticFuelItems ??= new List<FuelItem>();
                excavatorFuelItems ??= new List<FuelItem>();
                if (_oldStaticFuel != null && staticFuelItems.Count == 0)
                {
                    staticFuelItems.Add(_oldStaticFuel);
                    _oldStaticFuel = null;
                }
                if (staticFuelItems.Count == 0)
                    staticFuelItems.Add(new() { shortname = "lowgradefuel" });
                if (_oldExcavatorFuel != null && excavatorFuelItems.Count == 0)
                {
                    excavatorFuelItems.Add(_oldExcavatorFuel);
                    _oldStaticFuel = null;
                }
                if (excavatorFuelItems.Count == 0)
                    excavatorFuelItems.Add(new() { shortname = "lowgradefuel" });
            }

            [JsonProperty("Static Quarries - Fuel Items")]
            public List<FuelItem> staticFuelItems = new();

            [JsonProperty("Excavator Quarry - Fuel Items")]
            public List<FuelItem> excavatorFuelItems = new();

            [JsonProperty("Static Quarries - Metal Quarry Output")]
            public List<StaticQuarryOutput> staticMetalOutput = new();

            [JsonProperty("Static Quarries - Sulfur Quarry Output")]
            public List<StaticQuarryOutput> staticSulfurOutput = new();

            [JsonProperty("Static Quarries - HQM Quarry Output")]
            public List<StaticQuarryOutput> staticHqmOutput = new();

            [JsonProperty("Static Quarries - Pump Jack Output")]
            public List<StaticQuarryOutput> staticPumpJackOutput = new();

            [JsonProperty("Static Quarries - Quarry & Pumpjack Upgrades")]
            public List<UpgradeConfig> staticQuarryUpgrades = new();

            [JsonProperty("Static Quarries - Excavator Upgrades")]
            public List<UpgradeConfig> excavatorUpgrades = new();

            [JsonProperty("Static Quarries - Excavator Outputs")]
            public Dictionary<string, List<StaticQuarryOutput>> excavatorResources = new();

            [JsonProperty("Excavator Quarry - How Long Does Quarry Need To Run To Be Able To Call Supply (in seconds)")]
            public float excavatorSupplyCallTime = 3600;

            [JsonProperty("Dispenser Profiles (profile key and list of short prefab names)")]
            public Dictionary<string, List<string>> dispenserProfiles = new();

            [JsonProperty("Quarry Profiles")]
            public Dictionary<string, QuarryProfile> quarryProfiles = new();
        }

        private class SurveyConfig
        {
            [JsonProperty("Effect Path")]
            public string effectPath = "assets/bundled/prefabs/fx/survey_explosion.prefab";

            [JsonProperty("Required Permission (empty, if not required)")]
            public string permission = string.Empty;

            [JsonProperty("Allow Auto Finder")]
            public bool allowAutoFinder = false;

            [JsonProperty("Auto Finder Required Permission (not required, if empty)")]
            public string autoFinderPerm = "";

            [JsonProperty("Auto Finder Min. Survey Req. Percentage To Start (0-100)")]
            public float minReqPercentage = 25f;

            [JsonProperty("Rolling Real Probability Value (advanced)")]
            public float rollProbabilityValue = 0.95f;

            [JsonProperty("Chance For Resources (0-100)")]
            public float resourceChance = 75;

            [JsonProperty("Displayed Survey Title Translation Key")]
            public string surveyTranslation = string.Empty;

            [JsonProperty("Required Item")]
            public RequiredItem surveyItem = new() { shortname = "surveycharge" };
        }

        private class QuarryProfile
        {
            [JsonProperty("Required Permission (empty, if not required)")]
            public string permission = string.Empty;

            [JsonProperty("Displayed Icon")]
            public RequiredItem icon = new() { shortname = "mining.quarry" };

            [JsonProperty("Survey Type")]
            public string surveyType = "";

            [JsonProperty("Enable Output Linking Requirement")]
            public bool enableOutputLink = false;

            [JsonProperty("Enable Input Linking Requirement")]
            public bool enableInputLink = false;

            [JsonProperty("Displayed Quarry Title Translation Key")]
            public string titleTranslation = string.Empty;

            [JsonProperty("Chance")]
            public int chance = 5;

            [JsonProperty("Minimal Resources Per Node")]
            public int minPerNode = 1;

            [JsonProperty("Maximal Resources Per Node")]
            public int maxPerNode = 2;

            [JsonProperty("Fuel Required Per Tick", NullValueHandling = NullValueHandling.Ignore)]
            private FuelItem _oldFuelItem = null;

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context)
            {
                fuelItems ??= new List<FuelItem>();
                if (_oldFuelItem != null && fuelItems.Count == 0)
                {
                    fuelItems.Add(_oldFuelItem);
                    _oldFuelItem = null;
                }
                if (fuelItems.Count == 0)
                    fuelItems.Add(new() { shortname = "lowgradefuel" });
            }

            [JsonProperty("Fuels Supported Required Per Tick")]
            public List<FuelItem> fuelItems = new();

            [JsonProperty("Allow Quick Resource Collect Button")]
            public bool allowQuickTake = true;

            [JsonProperty("Quick Resource Collect Required Perm (not required, if empty)")]
            public string allowQuickPerm = string.Empty;

            [JsonProperty("Enable Upgrades")]
            public bool enableUpgrades = true;

            [JsonProperty("Upgrade Requirement Dispenser Profile Name")]
            public string dispenserProfile = string.Empty;

            [JsonProperty("Items Required To Place")]
            public List<RequiredItem> requiredItems = new();

            [JsonProperty("Resources")]
            public Dictionary<string, ResourceConfig> resources = new();

            [JsonProperty("Upgrades")]
            public List<UpgradeConfig> upgrades = new();
        }

        private class ResourceConfig
        {
            [JsonProperty("Output Item - Shortname")]
            public string shortname;

            [JsonProperty("Output Item - Skin")]
            public ulong skin = 0;

            [JsonProperty("Output Item - Display Name")]
            public string name = string.Empty;

            [JsonProperty("Include Always")]
            public bool alwaysInclude = false;

            [JsonProperty("Required Permission (empty if not required)")]
            public string permission = string.Empty;

            [JsonProperty("Chance")]
            public int chance;

            [JsonProperty("Minimal Output Per Tick")]
            public float outputMin;

            [JsonProperty("Maximal Output Per Tick")]
            public float outputMax;

            [JsonProperty("Additional Items Required To Place")]
            public List<RequiredItem> additionalItems = new();
        }

        private class UpgradeConfig
        {
            [JsonProperty("Required Items")]
            public List<RequiredItem> requiredItems = new();

            [JsonProperty("Required Permission To Upgrade")]
            public string requiredPerm = "";

            [JsonProperty("Required Currency (0 to disable)")]
            public int requiredRp = 0;

            [JsonProperty("Required Destroyed Resource Dispensers To Upgrade (0 to disable)")]
            public int requiredDispensers = 0;

            [JsonProperty("Require Both Items And Currency")]
            public bool requireBoth = false;

            [JsonProperty("Fuel Storage Capacity")]
            public int fuelCapacity = 6;

            [JsonProperty("Capacity")]
            public int capacity = 6;

            [JsonProperty("Gather Multiplier")]
            public float multiplier = 1;

            [JsonProperty("Fuel Usage Multiplier")]
            public float fuelMultiplier = 1;

            [JsonProperty("Additional Resources (Resources keys)")]
            public List<string> additionalResources = new();
        }

        private class StaticQuarryOutput
        {
            [JsonProperty("Shortname")]
            public string shortname;

            [JsonProperty("Skin")]
            public ulong skin = 0;

            [JsonProperty("Amount Per Tick")]
            public float amount = 1;

            [JsonProperty("Display Name")]
            public string displayName = "";
        }

        private class FuelItem
        {
            [JsonProperty("Shortname")]
            public string shortname;

            [JsonProperty("Skin")]
            public ulong skin = 0;

            [JsonProperty("Amount")]
            public float amount = 1;

            [JsonProperty("Display Name")]
            public string displayName;
        }

        private class RequiredItem
        {
            [JsonProperty("Shortname")]
            public string shortname;

            [JsonProperty("Skin")]
            public ulong skin = 0;

            [JsonProperty("Amount")]
            public int amount = 1;

            [JsonProperty("Display Name")]
            public string displayName;

            public RequiredItem() { }

            public RequiredItem(RequiredItem item)
            {
                shortname = item.shortname;
                skin = item.skin;
                amount = item.amount;
                displayName = item.displayName;
            }
        }

        private static PluginData data;

        [JsonProperty("Player Cache")]
        private Dictionary<ulong, string> playerCache = new();

        [JsonProperty("Storage Cache")]
        private Dictionary<int, StorageData> storageCache = new();

        [JsonProperty("Previous Wipe Dispensers")]
        public Dictionary<ulong, Dictionary<string, int>> previousGatheredDispensers = new();

        private class PluginData
        {
            [JsonProperty("Quarry Count")]
            public int quarryCount = 0;

            [JsonProperty("Quarries")]
            public Dictionary<int, QuarryData> quarries = new();

            [JsonProperty("Static Quarries")]
            public Dictionary<ulong, Dictionary<ulong, ulong>> _oldStaticQuarries = null;

            [JsonProperty("Protocol")]
            public int protocol;

            [JsonProperty("Wipe Id")]
            public string wipeId = "";

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context)
            {
                if (_oldStaticQuarries != null)
                {
                    ExcavatorArm arm = null;
                    foreach (var miningArm in BaseNetworkable.serverEntities.OfType<ExcavatorArm>())
                    {
                        arm = miningArm;
                        break;
                    }
                    foreach (var quarry in _oldStaticQuarries)
                    {
                        if (quarry.Value == null) continue;
                        ulong validNetId;
                        QuarryType qt = QuarryType.Static;
                        if (quarry.Key == uint.MaxValue)
                        {
                            if (!arm) continue;
                            validNetId = arm.net.ID.Value;
                            qt = QuarryType.Excavator;
                        }
                        else
                            validNetId = quarry.Key;
                        foreach (var staticQr in quarry.Value)
                        {
                            BoxStorage outputStorage = BaseNetworkable.serverEntities.Find(new NetworkableId(staticQr.Value)) as BoxStorage;
                            if (!outputStorage || outputStorage.children == null || outputStorage.children.Count < 1) continue;
                            BoxStorage fuelStorage = outputStorage.children[0] as BoxStorage;
                            if (!fuelStorage) continue;
                            // Use instance fields: static `data` is not assigned until after deserialization completes.
                            int quarryId = ++quarryCount;
                            quarries[quarryId] = new QuarryData()
                            {
                                owner = staticQr.Key,
                                quarryType = qt,
                                staticNetId = validNetId,
                                netId = staticQr.Value,
                                fuelNetId = fuelStorage.net.ID.Value
                            };
                        }
                    }
                    _oldStaticQuarries = null;
                }
            }

            [JsonProperty("Gathered Dispensers")]
            public Dictionary<ulong, Dictionary<string, int>> gatheredDispensers = new();
        }

        private enum QuarryType
        {
            Virtual,
            Static,
            Excavator
        }

        private class QuarryData
        {
            [JsonProperty("Quarry Type")]
            public QuarryType quarryType = QuarryType.Virtual;

            [JsonProperty("Quarry Network ID")]
            public ulong netId;

            [JsonProperty("Quarry Fuel Network ID")]
            public ulong fuelNetId = 0;

            [JsonProperty("Excavator Output 2 Network ID")]
            public ulong outputNetId2 = 0;

            [JsonProperty("Quarry Output Redirect ID")]
            public ulong redirectNetId = 0;

            [JsonProperty("Static Quarry Network ID")]
            public ulong staticNetId = 0;

            [JsonProperty("Excavator Resource")]
            public string excResource = string.Empty;

            [JsonProperty("Quarry Owner")]
            public ulong owner;

            [JsonProperty("Profile")]
            public string profile = string.Empty;

            [JsonProperty("Authorized Players")]
            public List<ulong> authPlayers = new();

            [JsonProperty("Quarry Level")]
            public int level = 0;

            [JsonProperty("Last Owner Online")]
            public DateTime lastOwnerOnline = DateTime.MinValue;

            [JsonProperty("Quarry Upgrade Types")]
            public Dictionary<int, bool> upgradedForRp = new();

            [JsonProperty("Is Running")]
            public bool isRunning = false;

            [JsonProperty("Resources")]
            public List<QuarryResource> resources = new();
        }

        private class QuarryResource
        {
            [JsonProperty("Config Resource Key")]
            public string configKey;

            [JsonProperty("Output Per Tick")]
            public float work;
        }

        private class StorageData
        {
            [JsonProperty("Resource Storage")]
            public List<RequiredItem> resource = new();

            [JsonProperty("Resource Storage 2")]
            public List<RequiredItem> resource2 = new();

            [JsonProperty("Fuel Storage")]
            public List<RequiredItem> fuel = new();
        }

        private void LoadData()
        {
            data = Interface.Oxide.DataFileSystem.ReadObject<PluginData>($"{Name}/quarryData") ?? new PluginData();
            data.quarries ??= new Dictionary<int, QuarryData>();
            foreach (var q in data.quarries.Values)
                if (q.profile == null)
                    q.profile = string.Empty;
            playerCache = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, string>>($"{Name}/playerCache") ?? new Dictionary<ulong, string>();
            previousGatheredDispensers = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, Dictionary<string, int>>>($"{Name}/oldDispensers") ?? new Dictionary<ulong, Dictionary<string, int>>();
            if (config.storeContainers)
                storageCache = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, StorageData>>($"{Name}/storageCache");
            ApplyWipeIfNeeded();
            timer.Every(Core.Random.Range(500, 700), SaveData);
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/quarryData", data);
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/playerCache", playerCache);
        }

        private void SavePrevDispensers()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/oldDispensers", previousGatheredDispensers);
        }
    }
}

// --- Inlined CUI/LUI shim (MIT, derived from github.com/ThePitereq/Oxide.Ext.CarbonAliases) ---
// Namespace VirtualQuarriesCuiShim: Oxide.CSharp treats "using Oxide.Ext.*" as a required extension DLL reference.

namespace VirtualQuarriesCuiShim
{
    public class Community
    {
        public static string Protect(string command) => command;
    }
}

namespace VirtualQuarriesCuiShim
{
using Oxide.Core;

    public class BaseModule
    {
        public static T GetModule<T>()
        {
            object obj;
            switch (typeof(T).Name)
            {
                case nameof(ImageDatabaseModule):
                    var plugin = Interface.GetMod().RootPluginManager.GetPlugin("ImageLibrary");
                    if (plugin == null)
                    {
                        Interface.Oxide.LogWarning("ImageLibrary plugin not found! UI building will print errors!");
                        return default;
                    }

                    obj = new ImageDatabaseModule(plugin);
                    break;

                default:
                    Interface.Oxide.LogWarning($"Module {nameof(T)} not supported! This may cause issues!");
                    return default;
            }

            return obj is T module ? module : default;
        }
    }
}

namespace VirtualQuarriesCuiShim
{
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using Pool = Facepunch.Pool;

    public class ImageDatabaseModule
    {
        private static Plugin ImageLibrary;

        public ImageDatabaseModule(Plugin plugin = null) {
            ImageLibrary = plugin;
        }

        public void QueueBatch(bool @override, IEnumerable<string> urls) => QueueBatch(@override, null, urls);
        
        public void QueueBatch(bool @override, Action<List<ImageQueueResult>> onComplete, IEnumerable<string> urls)
        {
            if (ImageLibrary == null) return;

            Dictionary<string, string> images = Pool.Get<Dictionary<string, string>>();
            images.Clear();
            foreach (string url in urls)
            {
                if (images.ContainsKey(url)) continue;
                images.Add(url, url);
            }
            ImageLibrary.Call("ImportImageList", "CarbonAliasesRequest", images, 0UL, @override, () => onComplete?.Invoke(null));
            Pool.FreeUnmanaged(ref images);
        }

        public class ImageQueueResult
        {
            
        }

        public void Queue(Dictionary<string, string> urls)
        {
            if (ImageLibrary == null) return;

            foreach (var kv in urls)
                ImageLibrary.Call<bool>("AddImage", kv.Value, kv.Key, 0uL);
        }
        public void DeleteImage(string url)
        {
            ImageLibrary?.Call("RemoveImage", url);
        }
        
        public void AddMap(string key, string url)
        {
            ImageLibrary?.Call<bool>("AddImage", url, key, 0uL);
        }
        public void RemoveMap(string key, string url)
        {

        }
        
        public bool HasImage(string key)
        {
            if (ImageLibrary == null) return false;

            return ImageLibrary.Call<bool>("HasImage", key, 0UL);
        }
        
        public uint GetImage(string key, float scale = 0, bool silent = false)
        {
            if (ImageLibrary == null) return 0;
            
            return Convert.ToUInt32(ImageLibrary.Call<string>("GetImage", key));
        }
        public string GetImageString(string key, float scale = 0, bool silent = false)
        {
            if (ImageLibrary == null) return null;

            return ImageLibrary.Call<string>("GetImage", key);
        }

        public void DeleteImage(string url, float scale = 0)
        {

        }
    }
}

namespace VirtualQuarriesCuiShim
{
using Facepunch.Extend;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

    public class CUI : IDisposable
    {
        public Handler Manager { get; private set; }
        
        public LUI v2 { get; }
        
        public ImageDatabaseModule ImageDatabase { get; }

        internal int _currentId = 0; 

        public enum ClientPanels
        {
            Overall,
            Overlay,
            OverlayNonScaled,
            Hud,
            HudMenu,
            Under,
            UnderNonScaled,
            Inventory,
            TechTree,
            Crafting,
            Contacts,
            Clans,
            Map
        }

        public string GetClientPanel(ClientPanels panel)
        {
            return panel switch
            {
                ClientPanels.Overall => "Overall",
                ClientPanels.OverlayNonScaled => "OverlayNonScaled",
                ClientPanels.Hud => "Hud",
                ClientPanels.HudMenu => "Hud.Menu",
                ClientPanels.Under => "Under",
                ClientPanels.UnderNonScaled => "UnderNonScaled",
                ClientPanels.Inventory => "Inventory",
                ClientPanels.TechTree => "TechTree",
                ClientPanels.Crafting => "Crafting",
                ClientPanels.Contacts => "Contacts",
                ClientPanels.Clans => "Clans",
                ClientPanels.Map => "Map",
                _ => "Overlay",
            };
        }

        public CUI(Handler manager)
        {
            Manager = manager;
            v2 = new LUI(this);
            ImageDatabase = BaseModule.GetModule<ImageDatabaseModule>();
        }

        #region Update

        public Handler.UpdatePool UpdatePool() => new Handler.UpdatePool();

        internal string AppendId()
        {
            _currentId++;
            return $"CarbonAliasesID_{_currentId}";
        }

        internal static string ProcessColor(string color)
        {
            if (color.StartsWith("#")) return CUI.HexToRustColor(color);

            return color;
        }

        #endregion



        #region Methods

        public CuiElementContainer CreateContainer(string panel, string color = "0 0 0 0", float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, ClientPanels parent = ClientPanels.Overlay, string destroyUi = null, float rotation = 0)
        {
            return CreateContainerParent(panel, color, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, GetClientPanel(parent), destroyUi, rotation);
        }
        
        public CuiElementContainer CreateContainerParent(string panel, string color = "0 0 0 0", float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string parentName = "Overlay", string destroyUi = null, float rotation = 0)
        {
            CuiElementContainer container = new CuiElementContainer();
            CuiElement element = new CuiElement
            {
                Name = panel,
                Parent = parentName,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = ProcessColor(color),
                        FadeIn = fadeIn
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"{xMin} {yMin}",
                        AnchorMax = $"{xMax} {yMax}",
                        OffsetMin = $"{OxMin} {OyMin}",
                        OffsetMax = $"{OxMax} {OyMax}",
                        Rotation = rotation,
                    }
                },
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
            };
            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());
            container.Add(element);
            return container;
        }

        public Pair<string, CuiElement> CreatePanel(CuiElementContainer container, string parent, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, bool blur = false, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (string.IsNullOrEmpty(id))
                id = AppendId();
            CuiElement element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            CuiImageComponent imageComponent = new CuiImageComponent();
            if (material != null) imageComponent.Material = material;
            imageComponent.Color = color;
            imageComponent.FadeIn = fadeIn;
            if (blur) imageComponent.Material = "assets/content/ui/uibackgroundblur.mat";
            element.Components.Add(imageComponent);
            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());
            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }
            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }

        public Pair<string, CuiElement> CreateText(CuiElementContainer container, string parent, string color, string text, int size, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, VerticalWrapMode verticalOverflow = VerticalWrapMode.Overflow, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var label = new CuiTextComponent();
            label.Text = string.IsNullOrEmpty(text) ? string.Empty : text;
            label.FontSize = size;
            label.Font = GetFont(font);
            label.Align = align;
            label.Color = ProcessColor(color);
            label.FadeIn = fadeIn;
            label.VerticalOverflow = verticalOverflow;
            element.Components.Add(label);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }
        public Pair<string, CuiElement, CuiElement> CreateProtectedButton(CuiElementContainer container, string parent, string color, string textColor, string text, int size, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0) => CreateButton(container, parent, color, textColor, text, size, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, update, rotation);

        public Pair<string, CuiElement, CuiElement> CreateButton(CuiElementContainer container, string parent, string color, string textColor, string text, int size, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var button = new CuiButtonComponent();
            if (material != null) button.Material = material;
            button.FadeIn = fadeIn;
            button.Color = ProcessColor(color);
            button.Command = command;
            element.Components.Add(button);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());



            if (!update) container?.Add(element);

            var textElement = (CuiElement)null;

            if (!string.IsNullOrEmpty(text))
            {
                textElement = new CuiElement
                {
                    Parent = id,
                    Name = AppendId(),
                    Components =
                    {
                        new CuiRectTransformComponent
                        {

                            AnchorMin = "0.02 0",
                            AnchorMax = "0.98 1"
                        }
                    },
                    FadeOut = fadeOut,
                    DestroyUi = destroyUi,
                    Update = update
                }; ;

                var ptext = new CuiTextComponent();
                ptext.Text = text;
                ptext.FontSize = size;
                ptext.Align = align;
                ptext.Color = ProcessColor(textColor);
                ptext.Font = GetFont(font);
                textElement.Components.Add(ptext);

                container.Add(textElement);
            }

            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }

            return new Pair<string, CuiElement, CuiElement>(id, element, textElement);
        }
        public Pair<string, CuiElement> CreateInputField(CuiElementContainer container, string parent, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0) => CreateInputField(container, parent, color, text, size, characterLimit, readOnly, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, autoFocus, hudMenuInput, InputField.LineType.SingleLine, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, update, rotation);

        public Pair<string, CuiElement> CreateInputField(CuiElementContainer container, string parent, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, InputField.LineType lineType = InputField.LineType.SingleLine, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var inputField = new CuiInputFieldComponent();
            inputField.Color = ProcessColor(color);
            inputField.Text = string.IsNullOrEmpty(text) ? string.Empty : text;
            inputField.FontSize = size;
            inputField.Font = GetFont(font);
            inputField.Align = align;
            inputField.CharsLimit = characterLimit;
            inputField.ReadOnly = readOnly;
            inputField.Command = command;
            inputField.Autofocus = autoFocus;
            inputField.HudMenuInput = hudMenuInput;
            inputField.LineType = lineType;
            element.Components.Add(inputField);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }
        public Pair<string, CuiElement> CreateProtectedInputField(CuiElementContainer container, string parent, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            return CreateInputField(container, parent, color, text, size, characterLimit, readOnly, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, autoFocus, hudMenuInput, InputField.LineType.SingleLine, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, update, rotation);
        }

        public Pair<string, CuiElement> CreateProtectedInputField(CuiElementContainer container, string parent, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, InputField.LineType lineType = InputField.LineType.SingleLine, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            return CreateInputField(container, parent, color, text, size, characterLimit, readOnly, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, autoFocus, hudMenuInput, lineType, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, update, rotation);
        }

        public Pair<string, CuiElement> CreateImage(CuiElementContainer container, string parent, uint png, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var rawImage = new CuiRawImageComponent();
            if (material != null) rawImage.Material = material;
            rawImage.Png = png.ToString();
            rawImage.FadeIn = fadeIn;
            rawImage.Color = ProcessColor(color);
            element.Components.Add(rawImage);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }

        public Pair<string, CuiElement> CreateImage(CuiElementContainer container, string parent, string url, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var rawImage = new CuiRawImageComponent();
            if (material != null) rawImage.Material = material;
            rawImage.Url = url;
            rawImage.FadeIn = fadeIn;
            rawImage.Color = ProcessColor(color);
            element.Components.Add(rawImage);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }

        public Pair<string, CuiElement> CreateSprite(CuiElementContainer container, string parent, string sprite, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var rawImage = new CuiRawImageComponent();
            if (material != null) rawImage.Material = material;
            rawImage.Sprite = sprite;
            rawImage.FadeIn = fadeIn;
            rawImage.Color = ProcessColor(color);
            element.Components.Add(rawImage);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }

        public Pair<string, CuiElement> CreateSimpleImage(CuiElementContainer container, string parent, string png, string sprite, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0, string slice = null)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var simpleImage = new CuiImageComponent();
            simpleImage.Png = png;
            simpleImage.Sprite = sprite;
            simpleImage.FadeIn = fadeIn;
            simpleImage.Slice = slice;
            simpleImage.Color = ProcessColor(color);
            if (material != null) simpleImage.Material = material;
            element.Components.Add(simpleImage);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }

        public Pair<string, CuiElement> CreateItemImage(CuiElementContainer container, string parent, int itemID, ulong skinID, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update,
            };

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                element.Components.Add(rect);
            }

            var rawImage = new CuiImageComponent();
            if (material != null) rawImage.Material = material;
            rawImage.ItemId = itemID;
            rawImage.SkinId = skinID;
            rawImage.FadeIn = fadeIn;
            rawImage.Color = ProcessColor(color);
            element.Components.Add(rawImage);

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (outlineColor != null)
            {
                CuiOutlineComponent outline = new CuiOutlineComponent();
                outline.Color = ProcessColor(outlineColor);
                outline.Distance = outlineDistance;
                outline.UseGraphicAlpha = outlineUseGraphicAlpha;
                element.Components.Add(outline);
            }

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }

        public Pair<string, CuiElement> CreateClientImage(CuiElementContainer container, string parent, string url, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0)
        {
            return CreateImage(container, parent, url, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, update, rotation);
        }

        public Pair<string, CuiElement> CreateScrollView(CuiElementContainer container, string parent,bool vertical, bool horizontal, ScrollRect.MovementType movementType, float elasticity, bool inertia, float decelerationRate, float scrollSensitivity, out CuiRectTransform contentTransformComponent, out CuiScrollbar horizontalScrollBar, out CuiScrollbar verticalScrollBar, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string id = null, string destroyUi = null, bool update = false, float rotation = 0, float pivotX = 0.5f, float pivotY = 0.5f, float scrollPosHorizontal = 0, float scrollPosVertical = 0)
        {
            if (id == null) id = AppendId();
            var element = new CuiElement
            {
                Parent = parent,
                Name = id,
                FadeOut = fadeOut,
                DestroyUi = destroyUi,
                Update = update,
            };

            var scrollview = new CuiScrollViewComponent();
            scrollview.Vertical = vertical;
            scrollview.Horizontal = horizontal;
            scrollview.MovementType = movementType;
            scrollview.Elasticity = elasticity;
            scrollview.Inertia = inertia;
            scrollview.DecelerationRate = decelerationRate;
            scrollview.ScrollSensitivity = scrollSensitivity;
            scrollview.ContentTransform = new CuiRectTransform();
            contentTransformComponent = scrollview.ContentTransform;
            scrollview.HorizontalScrollbar = new CuiScrollbar();
            horizontalScrollBar = scrollview.HorizontalScrollbar;
            scrollview.VerticalScrollbar = new CuiScrollbar();
            verticalScrollBar = scrollview.VerticalScrollbar;
            scrollview.HorizontalNormalizedPosition = scrollPosHorizontal;
            scrollview.VerticalNormalizedPosition = scrollPosVertical;

            element.Components.Add(scrollview);

            if (!update || (update && (xMin != 0 || xMax != 1 || yMin != 0 || yMax != 1)))
            {
                var rect = new CuiRectTransformComponent();
                rect.AnchorMin = $"{xMin} {yMin}";
                rect.AnchorMax = $"{xMax} {yMax}";
                rect.OffsetMin = $"{OxMin} {OyMin}";
                rect.OffsetMax = $"{OxMax} {OyMax}";
                rect.Rotation = rotation;
                rect.Pivot = $"{pivotX} {pivotY}";
                element.Components.Add(rect);
            }

            if (needsCursor) element.Components.Add(new CuiNeedsCursorComponent());
            if (needsKeyboard) element.Components.Add(new CuiNeedsKeyboardComponent());

            if (!update) container?.Add(element);
            return new Pair<string, CuiElement>(id, element);
        }

        public static string RustToHexColor(string rustColor, float? alpha = null, bool includeAlpha = true)
        {
            var colors = rustColor.Split(' ');
            var color = new Color(colors[0].ToFloat(), colors[1].ToFloat(), colors[2].ToFloat(), includeAlpha ? alpha ?? (colors.Length > 2 ? colors[3].ToFloat() : 1f) : 1);
            var result = includeAlpha ? ColorUtility.ToHtmlStringRGBA(color) : ColorUtility.ToHtmlStringRGB(color);
            Array.Clear(colors, 0, colors.Length);
            return $"#{result}";
        }

        public static string HexToRustColor(string hexColor, float? alpha = null, bool includeAlpha = true)
        {
            if (!ColorUtility.TryParseHtmlString(hexColor, out var color))
            {
                return $"1 1 1{(includeAlpha ? $" {alpha.GetValueOrDefault(1)}" : "")}";
            }

            return $"{color.r} {color.g} {color.b}{(includeAlpha ? $" {alpha ?? color.a}" : "")}";
        }
        
        public string GetImage(string keyOrUrl)
        {
            return ImageDatabase.GetImageString(keyOrUrl);
        }
        
        public bool HasImage(string url)
        {
            return ImageDatabase.HasImage(url);
        }
        
        public void QueueImages(IEnumerable<string> urls)
        {
            ImageDatabase.QueueBatch(false, urls);
        }
        
        public void ClearImages(IEnumerable<string> urls)
        {
            foreach (var url in urls)
            {
                ImageDatabase.DeleteImage(url);
            }
        }

        public string GetFont(CUI.Handler.FontTypes type)
        {
            return type switch
            {
                CUI.Handler.FontTypes.RobotoCondensedBold => "robotocondensed-bold.ttf",
                CUI.Handler.FontTypes.RobotoCondensedRegular => "robotocondensed-regular.ttf",
                CUI.Handler.FontTypes.PermanentMarker => "permanentmarker.ttf",
                CUI.Handler.FontTypes.DroidSansMono => "droidsansmono.ttf",
                CUI.Handler.FontTypes.NotoSansArabicBold => "_nonenglish/notosanscjksc-bold.otf",
                CUI.Handler.FontTypes.Poxel => "poxel.otf",
                CUI.Handler.FontTypes.LCD => "lcd.ttf",
                CUI.Handler.FontTypes.NoToEmoji => "_nonenglish/notoemoji-regular.ttf",
                CUI.Handler.FontTypes.PressStart => "pressstart2p-regular.ttf",
                _ => "robotocondensed-regular.ttf"
            };
        }

        #endregion

        #region Send

        public void Send(CuiElementContainer container, BasePlayer player)
        {
            CuiHelper.AddUi(player, container);
        }
        public void Destroy(string name, BasePlayer player)
        {
            CuiHelper.DestroyUi(player, name);
        }

        #endregion

        public struct Pair<T1, T2>
        {
            public T1 Id;
            public T2 Element;

            public Pair(T1 id, T2 element)
            {
                Id = id;
                Element = element;
            }

            public static implicit operator string(Pair<T1, T2> value)
            {
                return value.Id.ToString();
            }
        }
        public struct Pair<T1, T2, T3>
        {
            public T1 Id;
            public T2 Element1;
            public T3 Element2;

            public Pair(T1 id, T2 element1, T3 element2)
            {
                Id = id;
                Element1 = element1;
                Element2 = element2;
            }

            public static implicit operator string(Pair<T1, T2, T3> value)
            {
                return value.Id.ToString();
            }
        }

        public void Dispose()
        {
        }

        public class Handler
        {
            internal string Identifier { get; set; }

            public Handler()
            {
                Identifier = "CarbonAliasesID";
            }

            #region Properties

            internal int _currentId { get; set; }

            #endregion

            #region Pooling

            internal string AppendId()
            {
                _currentId++;
                return $"{Identifier}_{_currentId}";
            }

            #endregion


            #region Classes
            
            public enum FontTypes
            {
                RobotoCondensedBold,
                RobotoCondensedRegular,
                PermanentMarker,
                DroidSansMono,
                NotoSansArabicBold,
                Poxel,
                LCD,
                NoToEmoji,
                PressStart
            }

            #endregion

            #region Network

            public void Send(CuiElementContainer container, BasePlayer player)
            {
                CuiHelper.AddUi(player, container);
            }
            public void SendUpdate(Pair<string, CuiElement> pair, BasePlayer player)
            {
                pair.SendUpdate(player);
            }
            public void Destroy(string name, BasePlayer player)
            {
                CuiHelper.DestroyUi(player, name);
            }

            #endregion

            public class UpdatePool : CuiElementContainer, IDisposable
            {
                internal bool _hasDisposed;

                public void Add(Pair<string, CuiElement> pair)
                {
                    if (pair.Element != null)
                    {
                        if (!pair.Element.Update)
                        {
                            return;
                        }
                        else Add(pair.Element);
                    }
                }
                public void Add(Pair<string, CuiElement, CuiElement> pair)
                {
                    if (pair.Element1 != null)
                    {
                        if (!pair.Element1.Update)
                        {
                            return;
                        }
                        else Add(pair.Element1);
                    }

                    if (pair.Element2 != null)
                    {
                        if (!pair.Element2.Update)
                        {
                            return;
                        }
                        else Add(pair.Element2);
                    }
                }

                public void Send(BasePlayer player)
                {
                    CuiHelper.AddUi(player, this);

                    Dispose();
                }

                public void Dispose()
                {
                    if (_hasDisposed) return;

                    Clear();

                    _hasDisposed = true;
                }
            }
        }
    }

public static class CUIStatics
{

    internal static string ProcessColor(string color)
    {
        if (color.StartsWith("#")) return CUI.HexToRustColor(color);

        return color;
    }

    public static CUI.Pair<string, CuiElement> UpdatePanel(this CUI cui, string id, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, bool blur = false, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreatePanel(null, null, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, blur, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateText(this CUI cui, string id, string color, string text, int size, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, VerticalWrapMode verticalOverflow = VerticalWrapMode.Overflow, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateText(null, null, color, text, size, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, align, font, verticalOverflow, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement, CuiElement> UpdateButton(this CUI cui, string id, string color, string textColor, string text, int size, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateButton(null, null, color, textColor, text, size, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement, CuiElement> UpdateProtectedButton(this CUI cui, string id, string color, string textColor, string text, int size, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateProtectedButton(null, null, color, textColor, text, size, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateInputField(this CUI cui, string id, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateInputField(null, null, color, text, size, characterLimit, readOnly, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, autoFocus, hudMenuInput, InputField.LineType.SingleLine, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateInputField(this CUI cui, string id, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, InputField.LineType lineType = InputField.LineType.SingleLine, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateInputField(null, null, color, text, size, characterLimit, readOnly, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, autoFocus, hudMenuInput, lineType, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateProtectedInputField(this CUI cui, string id, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateProtectedInputField(null, null, color, text, size, characterLimit, readOnly, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, autoFocus, hudMenuInput, InputField.LineType.SingleLine, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateProtectedInputField(this CUI cui, string id, string color, string text, int size, int characterLimit, bool readOnly, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, string command = null, TextAnchor align = TextAnchor.MiddleCenter, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedRegular, bool autoFocus = false, bool hudMenuInput = false, InputField.LineType lineType = InputField.LineType.SingleLine, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateProtectedInputField(null, null, color, text, size, characterLimit, readOnly, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, command, align, font, autoFocus, hudMenuInput, lineType, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateImage(this CUI cui, string id, uint png, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateImage(null, null, png, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateImage(this CUI cui, string id, string url, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateImage(null, null, url, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation); ;
    }
    public static CUI.Pair<string, CuiElement> UpdateImage(this CUI cui, string id, string url, float scale, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateImage(null, null, url, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateSprite(this CUI cui, string id, string sprite, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateSprite(null, null, sprite, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateItemImage(this CUI cui, string id, int itemID, ulong skinID, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateItemImage(null, null, itemID, skinID, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateClientImage(this CUI cui, string id, string url, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0)
    {
        return cui.CreateClientImage(null, null, url, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation);
    }
    public static CUI.Pair<string, CuiElement> UpdateSimpleImage(this CUI cui, string id, string png, string sprite, string color, string material = null, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string outlineColor = null, string outlineDistance = null, bool outlineUseGraphicAlpha = false, string destroyUi = null, float rotation = 0, string slice = null)
    {
        return cui.CreateSimpleImage(null, null, png, sprite, color, material, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, outlineColor, outlineDistance, outlineUseGraphicAlpha, id, destroyUi, true, rotation, slice);
    }
    public static CUI.Pair<string, CuiElement> UpdateScrollView(this CUI cui, string id, bool vertical, bool horizontal, ScrollRect.MovementType movementType, float elasticity, bool inertia, float decelerationRate, float scrollSensitivity, out CuiRectTransform contentTransformComponent, out CuiScrollbar horizontalScrollBar, out CuiScrollbar verticalScrollBar, float xMin = 0f, float xMax = 1f, float yMin = 0f, float yMax = 1f, float OxMin = 0f, float OxMax = 0f, float OyMin = 0f, float OyMax = 0f, float fadeIn = 0f, float fadeOut = 0f, bool needsCursor = false, bool needsKeyboard = false, string destroyUi = null, float rotation = 0, float pivotX = 0.5f, float pivotY = 0.5f, float scrollPosHorizontal = 0, float scrollPosVertical = 0)
    {
        return cui.CreateScrollView(null, null, vertical, horizontal, movementType, elasticity, inertia, decelerationRate, scrollSensitivity, out contentTransformComponent, out horizontalScrollBar, out verticalScrollBar, xMin, xMax, yMin, yMax, OxMin, OxMax, OyMin, OyMax, fadeIn, fadeOut, needsCursor, needsKeyboard, id, destroyUi, true, rotation, pivotX, pivotY, scrollPosHorizontal, scrollPosVertical);
    }

    public static void SendUpdate(this CUI.Pair<string, CuiElement> pair, BasePlayer player)
    {
        var elements = Facepunch.Pool.Get<List<CuiElement>>();
        elements.Add(pair.Element);

        CuiHelper.AddUi(player, elements);

        Facepunch.Pool.FreeUnmanaged(ref elements);
    }
}
}

namespace VirtualQuarriesCuiShim
{
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

public class LUIBuilder
{
    private LUI lui { get; set; }

    private string GetFieldName(LuiCompType type)
    {
	    return type switch
	    {
		    LuiCompType.Text => "UnityEngine.UI.Text",
		    LuiCompType.Image => "UnityEngine.UI.Image",
		    LuiCompType.RawImage => "UnityEngine.UI.RawImage",
		    LuiCompType.Button => "UnityEngine.UI.Button",
		    LuiCompType.Outline => "UnityEngine.UI.Outline",
		    LuiCompType.InputField => "UnityEngine.UI.InputField",
		    LuiCompType.NeedsCursor => "NeedsCursor",
		    LuiCompType.RectTransform => "RectTransform",
		    LuiCompType.Countdown => "Countdown",
		    LuiCompType.HorizontalLayoutGroup => "UnityEngine.UI.HorizontalLayoutGroup",
		    LuiCompType.VerticalLayoutGroup => "UnityEngine.UI.VerticalLayoutGroup",
		    LuiCompType.GridLayoutGroup => "UnityEngine.UI.GridLayoutGroup",
		    LuiCompType.ContentSizeFitter => "UnityEngine.UI.ContentSizeFitter",
		    LuiCompType.LayoutElement => "UnityEngine.UI.LayoutElement",
		    LuiCompType.Draggable => "Draggable",
		    LuiCompType.Slot  => "Slot",
		    LuiCompType.NeedsKeyboard => "NeedsKeyboard",
		    LuiCompType.ScrollView => "UnityEngine.UI.ScrollView",
		    _ => "UnityEngine.UI.Image"
	    };
    }
    
    private static readonly char[] charPreset = new char[32]; //Max 4 values with 8 chars as no ui x/y should be bigger than 1280

    public static string VectorToString(Vector2 vector) => $"{vector.x} {vector.y}";
    
    public LUIBuilder(LUI _lui)
    {
        lui = _lui;
        elements.Clear();
        foreach (var element in lui.elements)
        {
            var el = new LuiContainerCarbonAliases()
            {
                name = element.name,
                destroyUi = element.destroyUi,
                update = element.update,
                fadeOut = element.fadeOut,
                parent = element.parent
            };
            foreach (LuiCompBase component in element.luiComponents)
            {
	            Dictionary<string, object> compBuilder = new();
	            compBuilder.Add("type", GetFieldName(component.type));
	            if (!component.enabled)
	            {
		            compBuilder.Add("enabled", false);
	            }
	            if (component.fadeIn > 0)
	            {
		            compBuilder.Add("fadeIn", component.fadeIn);
	            }
	            if (component.placeholderParentId != null)
	            {
		            compBuilder.Add("placeholderParentId", component.placeholderParentId);
	            }
	            switch (component.type)
                {
                    case LuiCompType.Text:
	                    LuiTextComp text = component as LuiTextComp;
	                    
	                    if (text.text != null)
	                    {
		                    
		                    compBuilder.Add("text", text.text);
	                    }
	                    if (text.fontSize > 0)
	                    {
		                    
		                    compBuilder.Add("fontSize", text.fontSize);
	                    }
	                    if (text.font != null)
	                    {
		                    
		                    compBuilder.Add("font", text.font);
	                    }
	                    if (text.align != null)
	                    {
		                    
		                    compBuilder.Add("align", text.align);
	                    }
	                    if (text.color != null)
	                    {
		                    
		                    compBuilder.Add("color", text.color);
	                    }
	                    if (text.verticalOverflow != null)
	                    {
		                    
		                    compBuilder.Add("verticalOverflow", text.verticalOverflow);
	                    }
	                    break;
                    case LuiCompType.Image:
	                    LuiImageComp image = component as LuiImageComp;
	                    
	                    if (image.sprite != null)
	                    {
		                    
		                    compBuilder.Add("sprite", image.sprite);
	                    }
	                    if (image.material != null)
	                    {
		                    compBuilder.Add("material", image.material);
	                    }
	                    if (image.color != null)
	                    {
		                    compBuilder.Add("color", image.color);
	                    }
	                    if (image.imageType != null)
	                    {
		                    compBuilder.Add("imagetype", image.imageType);
	                    }
	                    if (image.fillCenter)
	                    {
		                    
		                    compBuilder.Add("fillCenter", true);
	                    }
	                    if (image.png != null)
	                    {
		                    compBuilder.Add("png", image.png);
	                    }
	                    if (image.slice != null)
	                    {
		                    
		                    compBuilder.Add("slice", image.slice);
	                    }
	                    if (image.itemid != 0)
	                    {
		                    compBuilder.Add("itemid", image.itemid);
	                    }
	                    if (image.skinid != 0)
	                    {
		                    compBuilder.Add("skinid", image.skinid);
	                    }
	                    break;
                    case LuiCompType.RawImage:
	                    LuiRawImageComp rawImage = component as LuiRawImageComp;
	                    
	                    if (rawImage.sprite != null)
	                    {
		                    compBuilder.Add("sprite", rawImage.sprite);
	                    }
	                    if (rawImage.color != null)
	                    {
		                    compBuilder.Add("color", rawImage.color);
	                    }
	                    if (rawImage.material != null)
	                    {
		                    compBuilder.Add("material", rawImage.material);
	                    }
	                    if (rawImage.url != null)
	                    {
		                    compBuilder.Add("url", rawImage.url);
	                    }
	                    if (rawImage.png != null)
	                    {
		                    compBuilder.Add("png", rawImage.png);
	                    }
	                    if (rawImage.steamid != null)
	                    {
		                    compBuilder.Add("steamid", rawImage.steamid);
	                    }
	                    break;
                    case LuiCompType.Button:
	                    LuiButtonComp button = component as LuiButtonComp;
	                    
	                    if (button.command != null)
	                    {
		                    compBuilder.Add("command", button.command);
	                    }
	                    if (button.close != null)
	                    {
		                    compBuilder.Add("close", button.close);
	                    }
	                    if (button.sprite != null)
	                    {
		                    compBuilder.Add("sprite", button.sprite);
	                    }
	                    if (button.material != null)
	                    {
		                    compBuilder.Add("material", button.material);
	                    }
	                    if (button.color != null)
	                    {
		                    compBuilder.Add("color", button.color);
	                    }
	                    if (button.imageType != null)
	                    {
		                    compBuilder.Add("imagetype", button.imageType);
	                    }
	                    if (button.normalColor != null)
	                    {
		                    compBuilder.Add("normalColor", button.normalColor);
	                    }
	                    if (button.highlightedColor != null)
	                    {
		                    compBuilder.Add("highlightedColor", button.highlightedColor);
	                    }
	                    if (button.pressedColor != null)
	                    {
		                    compBuilder.Add("pressedColor", button.pressedColor);
	                    }
	                    if (button.selectedColor != null)
	                    {
		                    compBuilder.Add("selectedColor", button.selectedColor);
	                    }
	                    if (button.disabledColor != null)
	                    {
		                    compBuilder.Add("disabledColor", button.disabledColor);
	                    }
	                    if (button.colorMultiplier != -1)
	                    {
		                    compBuilder.Add("colorMultiplier", button.colorMultiplier);
	                    }
	                    if (button.fadeDuration != -1)
	                    {
		                    compBuilder.Add("fadeDuration", button.fadeDuration);
	                    }
	                    break;
                    case LuiCompType.Outline:
	                    LuiOutlineComp outline = component as LuiOutlineComp;
	                    
	                    if (outline.color != null)
	                    {
		                    
		                    compBuilder.Add("color", outline.color);
	                    }
	                    if (outline.distance != default)
	                    {
		                    
		                    compBuilder.Add("distance", outline.distance);
	                    }
	                    if (outline.useGraphicAlpha)
	                    {
		                    
		                    compBuilder.Add("useGraphicAlpha", true);
	                    }
	                    break;
                    case LuiCompType.InputField:
	                    LuiInputComp input = component as LuiInputComp;
	                    
	                    if (input.fontSize > 0)
	                    {
		                    
		                    compBuilder.Add("fontSize", input.fontSize);
	                    }
	                    if (input.font != null)
	                    {
		                    
		                    compBuilder.Add("font", input.font);
	                    }
	                    if (input.align != null)
	                    {
		                    
		                    compBuilder.Add("align", input.align);
	                    }
	                    if (input.color != null)
	                    {
		                    
		                    compBuilder.Add("color", input.color);
	                    }
	                    if (input.characterLimit > 0)
	                    {
		                    
		                    compBuilder.Add("characterLimit", input.characterLimit);
	                    }
	                    if (input.command != null)
	                    {
		                    
		                    compBuilder.Add("command", input.command);
	                    }
	                    if (input.text != null)
	                    {
		                    
		                    compBuilder.Add("text", input.text);
	                    }
	                    if (input.readOnly)
	                    {
		                    
		                    compBuilder.Add("readOnly", true);
	                    }
	                    if (input.placeholderId != null)
	                    {
		                    
		                    compBuilder.Add("placeholderId", input.placeholderId);
	                    }
	                    if (input.lineType != null)
	                    {
		                    
		                    compBuilder.Add("lineType", input.lineType);
	                    }
	                    if (input.password)
	                    {
		                    
		                    compBuilder.Add("password", true);
	                    }
	                    if (input.needsKeyboard)
	                    {
		                    
		                    compBuilder.Add("needsKeyboard", true);
	                    }
	                    if (input.hudMenuInput)
	                    {
		                    
		                    compBuilder.Add("hudMenuInput", true);
	                    }
	                    if (input.autofocus)
	                    {
		                    
		                    compBuilder.Add("autofocus", true);
	                    }
	                    break;
                    case LuiCompType.RectTransform:
                        LuiRectTransformComp rect = component as LuiRectTransformComp;
                        
                        if (rect.anchor != LuiPosition.Full)
                        {
	                        
	                        compBuilder.Add("anchormin", VectorToString(rect.anchor.anchorMin));
	                        
	                        compBuilder.Add("anchormax", VectorToString(rect.anchor.anchorMax));
                        }
                         //Always adding offset, as RUST UI have weird one pixel offset by default, idk who came to this idea lol.
                        compBuilder.Add("offsetmin", VectorToString(rect.offset.offsetMin));
                        
                        compBuilder.Add("offsetmax", VectorToString(rect.offset.offsetMax));
                        if (rect.rotation != 0)
                        {
	                        
	                        compBuilder.Add("rotation", rect.rotation);
                        }
                        if (rect.setParent != null)
                        {
	                        
	                        compBuilder.Add("setParent", rect.setParent);
                        }
                        if (rect.setTransformIndex != -1)
                        {
	                        
	                        compBuilder.Add("setTransformIndex", rect.setTransformIndex);
                        }
                        break;
                    case LuiCompType.Countdown:
	                    LuiCountdownComp countdown = component as LuiCountdownComp;
	                    
	                    if (countdown.endTime != -1)
	                    {
		                    
		                    compBuilder.Add("endTime", countdown.endTime);
	                    }
	                    if (countdown.startTime != -1)
	                    {
		                    
		                    compBuilder.Add("startTime", countdown.startTime);
	                    }
	                    if (countdown.step > 0)
	                    {
		                    
		                    compBuilder.Add("step", countdown.step);
	                    }
	                    if (countdown.interval > 0)
	                    {
		                    
		                    compBuilder.Add("interval", countdown.interval);
	                    }
	                    if (countdown.timerFormat != null)
	                    {
		                    
		                    compBuilder.Add("timerFormat", countdown.timerFormat);
	                    }
	                    if (countdown.numberFormat != null)
	                    {
		                    
		                    compBuilder.Add("numberFormat", countdown.numberFormat);
	                    }
	                    if (!countdown.destroyIfDone)
	                    {
		                    
		                    compBuilder.Add("destroyIfDone", false);
	                    }
	                    if (countdown.command != null)
	                    {
		                    
		                    compBuilder.Add("command", countdown.command);
	                    }
	                    break;
                    case LuiCompType.HorizontalLayoutGroup:
	                    LuiHorizontalLayoutGroupComp horizontalLayoutGroup = component as LuiHorizontalLayoutGroupComp;
	                    if (horizontalLayoutGroup.spacing != 0)
	                    {
		                    
		                    compBuilder.Add("spacing", horizontalLayoutGroup.spacing);
	                    }
	                    if (horizontalLayoutGroup.childAlignment != null)
	                    {
		                    
		                    compBuilder.Add("childAlignment", horizontalLayoutGroup.childAlignment);
	                    }
	                    if (!horizontalLayoutGroup.childForceExpandWidth)
	                    {
		                    
		                    compBuilder.Add("childForceExpandWidth", false);
	                    }
	                    if (!horizontalLayoutGroup.childForceExpandHeight)
	                    {
		                    
		                    compBuilder.Add("childForceExpandHeight", false);
	                    }
	                    if (horizontalLayoutGroup.childControlWidth)
	                    {
		                    
		                    compBuilder.Add("childControlWidth", true);
	                    }
	                    if (horizontalLayoutGroup.childControlHeight)
	                    {
		                    
		                    compBuilder.Add("childControlHeight", true);
	                    }
	                    if (horizontalLayoutGroup.childScaleWidth)
	                    {
		                    
		                    compBuilder.Add("childScaleWidth", true);
	                    }
	                    if (horizontalLayoutGroup.childScaleHeight)
	                    {
		                    
		                    compBuilder.Add("childScaleHeight", true);
	                    }
	                    if (horizontalLayoutGroup.padding != null)
	                    {
		                    
		                    compBuilder.Add("padding", horizontalLayoutGroup.padding);
	                    }
	                    break;
                    case LuiCompType.VerticalLayoutGroup:
	                    LuiVerticalLayoutGroupComp verticalLayoutGroup = component as LuiVerticalLayoutGroupComp;
	                    if (verticalLayoutGroup.spacing != 0)
	                    {
		                    
		                    compBuilder.Add("spacing", verticalLayoutGroup.spacing);
	                    }
	                    if (verticalLayoutGroup.childAlignment != null)
	                    {
		                    
		                    compBuilder.Add("childAlignment", verticalLayoutGroup.childAlignment);
	                    }
	                    if (!verticalLayoutGroup.childForceExpandWidth)
	                    {
		                    
		                    compBuilder.Add("childForceExpandWidth", false);
	                    }
	                    if (!verticalLayoutGroup.childForceExpandHeight)
	                    {
		                    
		                    compBuilder.Add("childForceExpandHeight", false);
	                    }
	                    if (verticalLayoutGroup.childControlWidth)
	                    {
		                    
		                    compBuilder.Add("childControlWidth", true);
	                    }
	                    if (verticalLayoutGroup.childControlHeight)
	                    {
		                    
		                    compBuilder.Add("childControlHeight", true);
	                    }
	                    if (verticalLayoutGroup.childScaleWidth)
	                    {
		                    
		                    compBuilder.Add("childScaleWidth", true);
	                    }
	                    if (verticalLayoutGroup.childScaleHeight)
	                    {
		                    
		                    compBuilder.Add("childScaleHeight", true);
	                    }
	                    if (verticalLayoutGroup.padding != null)
	                    {
		                    
		                    compBuilder.Add("padding", verticalLayoutGroup.padding);
	                    }
	                    break;
                    case LuiCompType.GridLayoutGroup:
	                    LuiGridLayoutGroupComp gridLayoutGroup = component as LuiGridLayoutGroupComp;
	                    if (gridLayoutGroup.cellSize != new Vector2(100, 100))
	                    {
		                    
		                    compBuilder.Add("cellSize", gridLayoutGroup.cellSize);
	                    }
	                    if (gridLayoutGroup.spacing != default)
	                    {
		                    
		                    compBuilder.Add("spacing", gridLayoutGroup.spacing);
	                    }
	                    if (gridLayoutGroup.startCorner != null)
	                    {
		                    
		                    compBuilder.Add("startCorner", gridLayoutGroup.startCorner);
	                    }
	                    if (gridLayoutGroup.startAxis != null)
	                    {
		                    
		                    compBuilder.Add("startAxis", gridLayoutGroup.startAxis);
	                    }
	                    if (gridLayoutGroup.childAlignment != null)
	                    {
		                    
		                    compBuilder.Add("childAlignment", gridLayoutGroup.childAlignment);
	                    }
	                    if (gridLayoutGroup.constraint != null)
	                    {
		                    
		                    compBuilder.Add("constraint", gridLayoutGroup.constraint);
	                    }
	                    if (gridLayoutGroup.constraintCount != 0)
	                    {
		                    
		                    compBuilder.Add("constraintCount", gridLayoutGroup.constraintCount);
	                    }
	                    if (gridLayoutGroup.padding != null)
	                    {
		                    
		                    compBuilder.Add("padding", gridLayoutGroup.padding);
	                    }
	                    break;
                    case LuiCompType.ContentSizeFitter:
	                    LuiContentSizeFitterComp contentSizeFitter = component as LuiContentSizeFitterComp;
	                    if (contentSizeFitter.horizontalFit != null)
	                    {
		                    
		                    compBuilder.Add("horizontalFit", contentSizeFitter.horizontalFit);
	                    }
	                    if (contentSizeFitter.verticalFit != null)
	                    {
		                    
		                    compBuilder.Add("verticalFit", contentSizeFitter.verticalFit);
	                    }
	                    break;
                    case LuiCompType.LayoutElement:
	                    LuiLayoutElementComp layoutElement = component as LuiLayoutElementComp;
	                    if (layoutElement.preferredWidth != -1)
	                    {
		                    
		                    compBuilder.Add("preferredWidth", layoutElement.preferredWidth);
	                    }
	                    if (layoutElement.preferredHeight != -1)
	                    {
		                    
		                    compBuilder.Add("preferredHeight", layoutElement.preferredHeight);
	                    }
	                    if (layoutElement.minWidth != 0)
	                    {
		                    
		                    compBuilder.Add("minWidth", layoutElement.minWidth);
	                    }
	                    if (layoutElement.minHeight != 0)
	                    {
		                    
		                    compBuilder.Add("minHeight", layoutElement.minHeight);
	                    }
	                    if (layoutElement.flexibleWidth != 0)
	                    {
		                    
		                    compBuilder.Add("flexibleWidth", layoutElement.flexibleWidth);
	                    }
	                    if (layoutElement.flexibleHeight != 0)
	                    {
		                    
		                    compBuilder.Add("flexibleHeight", layoutElement.flexibleHeight);
	                    }
	                    if (layoutElement.ignoreLayout)
	                    {
		                    
		                    compBuilder.Add("ignoreLayout", true);
	                    }
	                    break;
                    case LuiCompType.Draggable:
	                    LuiDraggableComp draggable = component as LuiDraggableComp;
	                    
	                    if (draggable.limitToParent)
	                    {
		                    
		                    compBuilder.Add("limitToParent", true);
	                    }
	                    if (draggable.maxDistance > 0)
	                    {
		                    
		                    compBuilder.Add("maxDistance", draggable.maxDistance);
	                    }
	                    if (draggable.allowSwapping)
	                    {
		                    
		                    compBuilder.Add("allowSwapping", true);
	                    }
	                    if (!draggable.dropAnywhere)
	                    {
		                    
		                    compBuilder.Add("dropAnywhere", false);
	                    }
	                    if (draggable.dragAlpha != -1)
	                    {
		                    
		                    compBuilder.Add("dragAlpha", draggable.dragAlpha);
	                    }
	                    if (draggable.parentLimitIndex != -1)
	                    {
		                    
		                    compBuilder.Add("parentLimitIndex", draggable.parentLimitIndex);
	                    }
	                    if (draggable.filter != null)
	                    {
		                    
		                    compBuilder.Add("filter", draggable.filter);
	                    }
	                    if (draggable.parentPadding != default)
	                    {
		                    
		                    compBuilder.Add("parentPadding", draggable.parentPadding);
	                    }
	                    if (draggable.anchorOffset != default)
	                    {
		                    
		                    compBuilder.Add("anchorOffset", draggable.anchorOffset);
	                    }
	                    if (draggable.keepOnTop)
	                    {
		                    
		                    compBuilder.Add("anchorOffset", draggable.keepOnTop);
	                    }
	                    if (draggable.positionRPC != null)
	                    {
		                    
		                    compBuilder.Add("positionRPC", draggable.positionRPC);
	                    }
	                    if (draggable.moveToAnchor)
	                    {
		                    
		                    compBuilder.Add("moveToAnchor", draggable.moveToAnchor);
	                    }
	                    if (draggable.rebuildAnchor)
	                    {
		                    
		                    compBuilder.Add("rebuildAnchor", draggable.rebuildAnchor);
	                    }
	                    break;
                    case LuiCompType.Slot:
	                    LuiSlotComp slot = component as LuiSlotComp;
	                    
	                    if (slot.filter != null)
	                    {
		                    
		                    compBuilder.Add("filter", slot.filter);
	                    }
	                    break;
                    case LuiCompType.ScrollView:
	                    LuiScrollComp scroll = component as LuiScrollComp;
	                    
	                    bool changeAnchor = scroll.anchor != LuiPosition.Full;
	                    bool changeOffset = scroll.offset != LuiOffset.None;
	                    if (changeAnchor || changeOffset || scroll.pivot != new Vector2(0.5f, 0.5f))
	                    {
		                    
		                    Dictionary<string, string> transform = new();
		                    if (changeAnchor)
		                    {
			                    transform.Add("anchormin", VectorToString(scroll.anchor.anchorMin));
			                    
			                    transform.Add("anchormax", VectorToString(scroll.anchor.anchorMax));
		                    }
		                    if (changeOffset)
		                    {
			                    if (changeAnchor)
				                    
				                    transform.Add("offsetmin", VectorToString(scroll.offset.offsetMin));
			                    
			                    transform.Add("offsetmax", VectorToString(scroll.offset.offsetMax));
		                    }
		                    if (scroll.pivot != new Vector2(0.5f, 0.5f))
		                    {
			                    
			                    compBuilder.Add("pivot", scroll.pivot);
		                    }
		                    compBuilder.Add("contentTransform", transform);
	                    }
	                    if (scroll.horizontal)
	                    {
		                    
		                    compBuilder.Add("horizontal", true);
	                    }
	                    if (scroll.vertical)
	                    {
		                    
		                    compBuilder.Add("vertical", true);
	                    }
	                    if (scroll.movementType != null)
	                    {
		                    
		                    compBuilder.Add("movementType", scroll.movementType);
	                    }
	                    if (scroll.elasticity != -1)
	                    {
		                    
		                    compBuilder.Add("elasticity", scroll.elasticity);
	                    }
	                    if (scroll.inertia)
	                    {
		                    
		                    compBuilder.Add("inertia", true);
	                    }
	                    if (scroll.decelerationRate != -1)
	                    {
		                    
		                    compBuilder.Add("decelerationRate", scroll.decelerationRate);
	                    }
	                    if (scroll.scrollSensitivity != -1)
	                    {
		                    
		                    compBuilder.Add("scrollSensitivity", scroll.scrollSensitivity);
	                    }
	                    if (scroll.horizontal)
	                    {
		                    compBuilder.Add("horizontalScrollbar", WriteScrollBar(scroll.horizontalScrollbar));

	                    }
	                    if (scroll.vertical)
	                    {
		                    compBuilder.Add("verticalScrollbar", WriteScrollBar(scroll.verticalScrollbar));
	                    }
	                    if (scroll.horizontalNormalizedPosition != 0)
	                    {
		                    
		                    compBuilder.Add("horizontalNormalizedPosition", scroll.horizontalNormalizedPosition);
	                    }
	                    if (scroll.verticalNormalizedPosition != 0)
	                    {
		                    
		                    compBuilder.Add("verticalNormalizedPosition", scroll.verticalNormalizedPosition);
	                    }
	                    break;
                }
	            el.components.Add(compBuilder);
            }
            elements.Add(el);
        }
    }

    public byte[] GetMergedBytes()
    {
	    string stringJson = JsonConvert.SerializeObject(elements, Formatting.None, _cuiSettings).Replace("\\n", "\n");
	    return Encoding.UTF8.GetBytes(stringJson);
    }
    
    private Dictionary<string, object> WriteScrollBar(LuiScrollbar scroll)
    {
	    Dictionary<string, object> elements = new();
	    elements.Add("enabled", !scroll.disabled); //Adding so I don't need to check for first coma.
	    if (scroll.invert)
	    {
		    
		    elements.Add("invert", true);
	    }
	    if (scroll.autoHide)
	    {
		    
		    elements.Add("autoHide", true);
	    }
	    if (scroll.handleSprite != null)
	    {
		    
		    elements.Add("handleSprite", scroll.handleSprite);
	    }
	    if (scroll.size != 0)
	    {
		    
		    elements.Add("size", scroll.size);
	    }
	    if (scroll.handleColor != null)
	    {
		    
		    elements.Add("handleColor", scroll.handleColor);
	    }
	    if (scroll.highlightColor != null)
	    {
		    
		    elements.Add("highlightColor", scroll.highlightColor);
	    }
	    if (scroll.pressedColor != null)
	    {
		    
		    elements.Add("pressedColor", scroll.pressedColor);
	    }
	    if (scroll.trackSprite != null)
	    {
		    
		    elements.Add("trackSprite", scroll.trackSprite);
	    }
	    if (scroll.trackColor != null)
	    {
		    
		    elements.Add("trackColor", scroll.trackColor);
	    }

	    return elements;
    }

    public List<LuiContainerCarbonAliases> elements = new();

    public class LuiContainerCarbonAliases
    {
        public string name;
        public string parent;
        public List<Dictionary<string, object>> components = new();
        public string destroyUi;
        public float fadeOut;
        public bool update;
    }
    
    private static JsonSerializerSettings _cuiSettings = new()
    {
        DefaultValueHandling = DefaultValueHandling.Ignore
    };

    public string GetJsonString()
    {
        return JsonConvert.SerializeObject(elements, Formatting.None, _cuiSettings).Replace("\\n", "\n");
    }
}
}

namespace VirtualQuarriesCuiShim
{
using System;
using System.Collections;
using System.Collections.Generic;
using Network;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

public class LUI : IDisposable
{
	public readonly List<LuiContainer> elements = new();
	
	private ImageDatabaseModule imgDb;

	private readonly CUI _parent;

	/// <summary>
	/// Boolean that changes default generation of element names.
	/// With this option disabled, you cannot create UI hierarchy without manual name input.
	/// </summary>
	public bool generateNames = true;

	public LUI(CUI cui)
	{
		_parent = cui;
		imgDb = BaseModule.GetModule<ImageDatabaseModule>();
	}

	/// <summary>
	/// Name of last created container. Used for easy new element creation to last parent without creating variable for that.
	/// </summary>
	public string lastName = string.Empty;

	#region Core Panel

	public LuiContainer CreateParent(CUI.ClientPanels parent, LuiPosition position, string name = "") => CreateParent(_parent.GetClientPanel(parent), position, name);

	public LuiContainer CreateParent(LuiContainer container, LuiPosition position, string name = "") => CreateParent(container.name, position, name);
	public LuiContainer CreateParent(string parent, LuiPosition position, string name = "")
	{
		LuiContainer cont = new();
		cont.parent = parent;
		if (name != string.Empty)
			cont.name = name;
		else if (generateNames)
			cont.name = _parent.AppendId();
		cont.SetAnchors(position);
		elements.Add(cont);
		return cont;
	}

	#endregion

	#region Updates

	public LuiContainer UpdatePosition(string name, LuiPosition pos)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		cont.SetAnchors(pos);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer UpdatePosition(string name, LuiOffset off)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		cont.SetOffset(off);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer UpdatePosition(string name, LuiPosition pos, LuiOffset off)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		cont.SetAnchorAndOffset(pos, off);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer UpdateRotation(string name, float rotation)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		cont.SetRotation(rotation);
		elements.Add(cont);
		return cont;
	}

	/// <summary>
	/// Creates update container without any fields assigned.
	/// </summary>
	public LuiContainer Update(string name)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		elements.Add(cont);
		return cont;
	}

	public LuiContainer UpdateColor(string name, string color)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		cont.SetColor(color);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer UpdateText(string name, string text, int fontSize = 0, string color = null)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		cont.SetText(text, fontSize, color, update: true);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer UpdateButtonCommand(string name, string command, bool isProtected = true)
	{
		LuiContainer cont = new();
		cont.name = name;
		cont.update = true;
		cont.SetButton(command);
		elements.Add(cont);
		return cont;
	}

	#endregion

	#region Panel Creation

	public LuiContainer CreateEmptyContainer(LuiContainer container, string name = "", bool add = false) => CreateEmptyContainer(container.name, name, add);

	
	/// <summary>
	/// Creates empty container without anything. Shouldn't be used outside LUI library, but in rare cases might be useful.
	/// </summary>
	public LuiContainer CreateEmptyContainer(string parent, string name = "", bool add = false)
	{
		LuiContainer cont = new();
		cont.parent = parent;
		if (name != string.Empty)
			cont.name = name;
		else if (generateNames)
		{
			string newName = _parent.Manager.AppendId();
			lastName = newName;
			cont.name = newName;
		}
		if (add)
			elements.Add(cont);
		return cont;
	}

	public LuiContainer CreatePanel(LuiContainer container, LuiPosition position, LuiOffset offset, string color, string name = "") => CreatePanel(container.name, position, offset, color, name);
	public LuiContainer CreatePanel(LuiContainer container, LuiOffset offset, string color, string name = "") => CreatePanel(container.name, LuiPosition.None, offset, color, name);
	public LuiContainer CreatePanel(string parent, LuiOffset offset, string color, string name = "") => CreatePanel(parent, LuiPosition.None, offset, color, name);

	public LuiContainer CreatePanel(string parent, LuiPosition position, LuiOffset offset, string color, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetColor(color);
		elements.Add(cont);
		return cont;
	}
	public LuiContainer CreateText(LuiContainer container, LuiPosition position, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment = TextAnchor.UpperLeft, string name = "") => CreateText(container.name, position, offset, fontSize, color, text, alignment, name);
	public LuiContainer CreateText(LuiContainer container, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment = TextAnchor.UpperLeft, string name = "") => CreateText(container.name, LuiPosition.None, offset, fontSize, color, text, alignment, name);
	public LuiContainer CreateText(string parent, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment = TextAnchor.UpperLeft, string name = "") => CreateText(parent, LuiPosition.None, offset, fontSize, color, text, alignment, name);

	public LuiContainer CreateText(string parent, LuiPosition position, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment = TextAnchor.MiddleCenter, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetText(text, fontSize, color, alignment);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateSprite(LuiContainer container, LuiPosition position, LuiOffset offset, string sprite, string color = LuiColors.White, string name = "") => CreateSprite(container.name, position, offset, sprite, color, name);
	public LuiContainer CreateSprite(LuiContainer container, LuiOffset offset, string sprite, string color = LuiColors.White, string name = "") => CreateSprite(container.name, LuiPosition.None, offset, sprite, color, name);
	public LuiContainer CreateSprite(string parent, LuiOffset offset, string sprite, string color = LuiColors.White, string name = "") => CreateSprite(parent, LuiPosition.None, offset, sprite, color, name);

	public LuiContainer CreateSprite(string parent, LuiPosition position, LuiOffset offset, string sprite, string color = LuiColors.White, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetSprite(sprite, color);
		elements.Add(cont);

		return cont;
	}

	public LuiContainer CreateImage(LuiContainer container, LuiPosition position, LuiOffset offset, string png, string color = LuiColors.White, string name = "") => CreateImage(container.name, position, offset, png, color, name);
	public LuiContainer CreateImage(LuiContainer container, LuiOffset offset, string png, string color = LuiColors.White, string name = "") => CreateImage(container.name, LuiPosition.None, offset, png, color, name);
	public LuiContainer CreateImage(string parent, LuiOffset offset, string png, string color = LuiColors.White, string name = "") => CreateImage(parent, LuiPosition.None, offset, png, color, name);

	public LuiContainer CreateImage(string parent, LuiPosition position, LuiOffset offset, string png, string color = LuiColors.White, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetImage(png, color);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateImageFromDb(LuiContainer container, LuiPosition position, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "") => CreateImageFromDb(container.name, position, offset, dbName, color, name);
	public LuiContainer CreateImageFromDb(LuiContainer container, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "") => CreateImageFromDb(container.name, LuiPosition.None, offset, dbName, color, name);
	public LuiContainer CreateImageFromDb(string parent, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "") => CreateImageFromDb(parent, LuiPosition.None, offset, dbName, color, name);

	public LuiContainer CreateImageFromDb(string parent, LuiPosition position, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		if (imgDb.HasImage(dbName))
		{
			cont.SetAnchorAndOffset(position, offset);
			cont.SetImage(imgDb.GetImageString(dbName), color);
		}
		else
		{
			Interface.Oxide.LogWarning($"[LUI] You're trying to image from ImageDatabase '{dbName}' which doesn't exist. Ignoring.");
			return null;
		}
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateRawImageFromDb(LuiContainer container, LuiPosition position, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "") => CreateImageFromDb(container.name, position, offset, dbName, color, name);
	public LuiContainer CreateRawImageFromDb(LuiContainer container, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "") => CreateImageFromDb(container.name, LuiPosition.None, offset, dbName, color, name);
	public LuiContainer CreateRawImageFromDb(string parent, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "") => CreateImageFromDb(parent, LuiPosition.None, offset, dbName, color, name);

	public LuiContainer CreateRawImageFromDb(string parent, LuiPosition position, LuiOffset offset, string dbName, string color = LuiColors.White, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		if (imgDb.HasImage(dbName))
		{
			cont.SetAnchorAndOffset(position, offset);
			cont.SetRawImage(imgDb.GetImageString(dbName), color);
		}
		else
		{
			Interface.Oxide.LogWarning($"[LUI] You're trying to image from ImageDatabase '{dbName}' which doesn't exist. Ignoring.");
			return null;
		}
		elements.Add(cont);
		return cont;
	}


	public LuiContainer CreateUrlImage(LuiContainer container, LuiPosition position, LuiOffset offset, string url, string color = LuiColors.White, string name = "") => CreateUrlImage(container.name, position, offset, url, color, name);
	public LuiContainer CreateUrlImage(LuiContainer container, LuiOffset offset, string url, string color = LuiColors.White, string name = "") => CreateUrlImage(container.name, LuiPosition.None, offset, url, color, name);
	public LuiContainer CreateUrlImage(string parent, LuiOffset offset, string url, string color = LuiColors.White, string name = "") => CreateUrlImage(parent, LuiPosition.None, offset, url, color, name);

	public LuiContainer CreateUrlImage(string parent, LuiPosition position, LuiOffset offset, string url, string color = LuiColors.White, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetUrlImage(url, color);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateItemIcon(LuiContainer container, LuiPosition position, LuiOffset offset, string shortname, ulong skinId = 0, string color = "", string name = "") => CreateItemIcon(container.name, position, offset, shortname, skinId, color, name);
	public LuiContainer CreateItemIcon(LuiContainer container, LuiOffset offset, string shortname, ulong skinId = 0, string color = "", string name = "") => CreateItemIcon(container.name, LuiPosition.None, offset, shortname, skinId, color, name);
	public LuiContainer CreateItemIcon(string parent, LuiOffset offset, string shortname, ulong skinId = 0, string color = "", string name = "") => CreateItemIcon(parent, LuiPosition.None, offset, shortname, skinId, color, name);

	public LuiContainer CreateItemIcon(string parent, LuiPosition position, LuiOffset offset, string shortname, ulong skinId = 0, string color = "", string name = "")
	{
		ItemDefinition def = ItemManager.FindItemDefinition(shortname);
		if (def)
			return CreateItemIcon(parent, position, offset, def.itemid, skinId, color, name);
		Interface.Oxide.LogWarning($"[LUI] We couldn't find '{shortname}' as valid item shortname. Ignoring.");
		return null;
	}

	public LuiContainer CreateItemIcon(LuiContainer container, LuiPosition position, LuiOffset offset, int itemId, ulong skinId = 0, string color = "", string name = "") => CreateItemIcon(container.name, position, offset, itemId, skinId, color, name);
	public LuiContainer CreateItemIcon(LuiContainer container, LuiOffset offset, int itemId, ulong skinId = 0, string color = "", string name = "") => CreateItemIcon(container.name, LuiPosition.None, offset, itemId, skinId, color, name);
	public LuiContainer CreateItemIcon(string parent, LuiOffset offset, int itemId, ulong skinId = 0, string color = "", string name = "") => CreateItemIcon(parent, LuiPosition.None, offset, itemId, skinId, color, name);

	public LuiContainer CreateItemIcon(string parent, LuiPosition position, LuiOffset offset, int itemId, ulong skinId = 0, string color = "", string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetItemIcon(itemId, skinId);
		if (color != string.Empty)
			cont.SetColor(color);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateButton(LuiContainer container, LuiPosition position, LuiOffset offset, string command, string color, bool isProtected = true, string name = "") => CreateButton(container.name, position, offset, command, color, isProtected, name);
	public LuiContainer CreateButton(LuiContainer container, LuiOffset offset, string command, string color, bool isProtected = true, string name = "") => CreateButton(container.name, LuiPosition.None, offset, command, color, isProtected, name);
	public LuiContainer CreateButton(string parent, LuiOffset offset, string command, string color, bool isProtected = true, string name = "") => CreateButton(parent, LuiPosition.None, offset, command, color, isProtected, name);

	public LuiContainer CreateButton(string parent, LuiPosition position, LuiOffset offset, string command, string color, bool isProtected = true, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetButton(isProtected ? Community.Protect(command) : command, color);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateInput(LuiContainer container, LuiPosition position, LuiOffset offset, string color, string text, int fontSize, string command, int charLimit = 0, bool isProtected = true, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor alignment = TextAnchor.UpperLeft, string name = "") => CreateInput(container.name, position, offset, color, text, fontSize, command, charLimit, isProtected, font, alignment, name);
	public LuiContainer CreateInput(LuiContainer container, LuiOffset offset, string color, string text, int fontSize, string command, int charLimit = 0, bool isProtected = true, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor alignment = TextAnchor.UpperLeft, string name = "") => CreateInput(container.name, LuiPosition.None, offset, color, text, fontSize, command, charLimit, isProtected, font, alignment, name);
	public LuiContainer CreateInput(string parent, LuiOffset offset, string color, string text, int fontSize, string command, int charLimit = 0, bool isProtected = true, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor alignment = TextAnchor.UpperLeft, string name = "") => CreateInput(parent, LuiPosition.None, offset, color, text, fontSize, command, charLimit, isProtected, font, alignment, name);

	public LuiContainer CreateInput(string parent, LuiPosition position, LuiOffset offset, string color, string text, int fontSize, string command, int charLimit = 0, bool isProtected = true, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor alignment = TextAnchor.MiddleCenter, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetInput(color, text, fontSize, isProtected ? Community.Protect(command) : command, charLimit, font, alignment);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateCountdown(LuiContainer container, LuiPosition position, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment, float startTime, float endTime, float step = 1, float interval = 1, string command = null, bool isProtected = true, string name = "") => CreateCountdown(container.name, position, offset, fontSize, color, text, alignment, startTime, endTime, step, interval, command, isProtected, name);
	public LuiContainer CreateCountdown(LuiContainer container, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment, float startTime, float endTime, float step = 1, float interval = 1, string command = null, bool isProtected = true, string name = "") => CreateCountdown(container.name, LuiPosition.None, offset, fontSize, color, text, alignment, startTime, endTime, step, interval, command, isProtected, name);
	public LuiContainer CreateCountdown(string parent, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment, float startTime, float endTime, float step = 1, float interval = 1, string command = null, bool isProtected = true, string name = "") => CreateCountdown(parent, LuiPosition.None, offset, fontSize, color, text, alignment, startTime, endTime, step, interval, command, isProtected, name);

	public LuiContainer CreateCountdown(string parent, LuiPosition position, LuiOffset offset, int fontSize, string color, string text, TextAnchor alignment, float startTime, float endTime, float step = 1, float interval = 1, string command = null, bool isProtected = true, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetText(text, fontSize, color, alignment);
		cont.SetCountdown(startTime, endTime, step, interval, isProtected ? Community.Protect(command) : command);
		elements.Add(cont);
		return cont;
	}
	
	public LuiContainer CreateHorizontalLayoutGroup(LuiContainer container, LuiPosition position, LuiOffset offset, float spacing = 0, string name = "") => CreateHorizontalLayoutGroup(container.name, position, offset, spacing, name);
	public LuiContainer CreateHorizontalLayoutGroup(LuiContainer container, LuiOffset offset, float spacing = 0, string name = "") => CreateHorizontalLayoutGroup(container.name, LuiPosition.None, offset, spacing, name);
	public LuiContainer CreateHorizontalLayoutGroup(string parent, LuiOffset offset, float spacing = 0, string name = "") => CreateHorizontalLayoutGroup(parent, LuiPosition.None, offset, spacing, name);

	public LuiContainer CreateHorizontalLayoutGroup(string parent, LuiPosition position, LuiOffset offset, float spacing = 0, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetHorizontalLayoutSpacing(spacing);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateVerticalLayoutGroup(LuiContainer container, LuiPosition position, LuiOffset offset, float spacing = 0, string name = "") => CreateVerticalLayoutGroup(container.name, position, offset, spacing, name);
	public LuiContainer CreateVerticalLayoutGroup(LuiContainer container, LuiOffset offset, float spacing = 0, string name = "") => CreateVerticalLayoutGroup(container.name, LuiPosition.None, offset, spacing, name);
	public LuiContainer CreateVerticalLayoutGroup(string parent, LuiOffset offset, float spacing = 0, string name = "") => CreateVerticalLayoutGroup(parent, LuiPosition.None, offset, spacing, name);

	public LuiContainer CreateVerticalLayoutGroup(string parent, LuiPosition position, LuiOffset offset, float spacing = 0, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetVerticalLayoutSpacing(spacing);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateGridLayoutGroup(LuiContainer container, LuiPosition position, LuiOffset offset, Vector2 cellSize, string name = "") => CreateGridLayoutGroup(container.name, position, offset, cellSize, name);
	public LuiContainer CreateGridLayoutGroup(LuiContainer container, LuiOffset offset, Vector2 cellSize, string name = "") => CreateGridLayoutGroup(container.name, LuiPosition.None, offset, cellSize, name);
	public LuiContainer CreateGridLayoutGroup(string parent, LuiOffset offset, Vector2 cellSize, string name = "") => CreateGridLayoutGroup(parent, LuiPosition.None, offset, cellSize, name);

	public LuiContainer CreateGridLayoutGroup(string parent, LuiPosition position, LuiOffset offset, Vector2 cellSize, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetCellSize(cellSize);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateContentFitter(LuiContainer container, LuiPosition position, LuiOffset offset, ContentSizeFitter.FitMode horizontal, ContentSizeFitter.FitMode vertical, string name = "") => CreateContentFitter(container.name, position, offset, horizontal, vertical, name);
	public LuiContainer CreateContentFitter(LuiContainer container, LuiOffset offset, ContentSizeFitter.FitMode horizontal, ContentSizeFitter.FitMode vertical, string name = "") => CreateContentFitter(container.name, LuiPosition.None, offset, horizontal, vertical, name);
	public LuiContainer CreateContentFitter(string parent, LuiOffset offset, ContentSizeFitter.FitMode horizontal, ContentSizeFitter.FitMode vertical, string name = "") => CreateContentFitter(parent, LuiPosition.None, offset, horizontal, vertical, name);

	public LuiContainer CreateContentFitter(string parent, LuiPosition position, LuiOffset offset, ContentSizeFitter.FitMode horizontal, ContentSizeFitter.FitMode vertical, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetFitMode(horizontal, vertical);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateLayoutElement(LuiContainer container, LuiPosition position, LuiOffset offset, float minWidth, float minHeight, string name = "") => CreateLayoutElement(container.name, position, offset, minWidth, minHeight, name);
	public LuiContainer CreateLayoutElement(LuiContainer container, LuiOffset offset, float minWidth, float minHeight, string name = "") => CreateLayoutElement(container.name, LuiPosition.None, offset, minWidth, minHeight, name);
	public LuiContainer CreateLayoutElement(string parent, LuiOffset offset, float minWidth, float minHeight, string name = "") => CreateLayoutElement(parent, LuiPosition.None, offset, minWidth, minHeight, name);

	public LuiContainer CreateLayoutElement(string parent, LuiPosition position, LuiOffset offset, float minWidth, float minHeight, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetMinimalSize(minWidth, minHeight);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateDraggable(LuiContainer container, LuiPosition position, LuiOffset offset, string color, string filter = null, bool dropAnywhere = true, bool keepOnTop = false, bool limitToParent = false, float maxDistance = -1f, bool allowSwapping = false, string name = "") => CreateDraggable(container.name, position, offset, color, filter, dropAnywhere, keepOnTop, limitToParent, maxDistance, allowSwapping, name);
	public LuiContainer CreateDraggable(LuiContainer container, LuiOffset offset, string color, string filter = null, bool dropAnywhere = true, bool keepOnTop = false, bool limitToParent = false, float maxDistance = -1f, bool allowSwapping = false, string name = "") => CreateDraggable(container.name, LuiPosition.None, offset, color, filter, dropAnywhere, keepOnTop, limitToParent, maxDistance, allowSwapping, name);
	public LuiContainer CreateDraggable(string parent, LuiOffset offset, string color, string filter = null, bool dropAnywhere = true, bool keepOnTop = false, bool limitToParent = false, float maxDistance = -1f, bool allowSwapping = false, string name = "") => CreateDraggable(parent, LuiPosition.None, offset, color, filter, dropAnywhere, keepOnTop, limitToParent, maxDistance, allowSwapping, name);

	public LuiContainer CreateDraggable(string parent, LuiPosition position, LuiOffset offset, string color, string filter = null, bool dropAnywhere = true, bool keepOnTop = false, bool limitToParent = false, float maxDistance = -1f, bool allowSwapping = false, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetColor(color);
		cont.SetDraggable(filter, dropAnywhere, keepOnTop, limitToParent, maxDistance, allowSwapping);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateSlot(LuiContainer container, LuiPosition position, LuiOffset offset, string filter = null, string name = "") => CreateSlot(container.name, position, offset, filter, name);
	public LuiContainer CreateSlot(LuiContainer container, LuiOffset offset, string filter = null, string name = "") => CreateSlot(container.name, LuiPosition.None, offset, filter, name);
	public LuiContainer CreateSlot(string parent, LuiOffset offset, string filter = null, string name = "") => CreateSlot(parent, LuiPosition.None, offset, filter, name);

	public LuiContainer CreateSlot(string parent, LuiPosition position, LuiOffset offset, string filter = null, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetSlot(filter);
		elements.Add(cont);
		return cont;
	}

	public LuiContainer CreateScrollView(LuiContainer container, LuiPosition position, LuiOffset offset, bool vertical, bool horizontal, ScrollRect.MovementType movementType = ScrollRect.MovementType.Clamped, float elasticity = 0, bool inertia = false, float decelerationRate = 0, float scrollSensitivity = 0, LuiScrollbar verticalScrollOptions = default, LuiScrollbar horizontalScrollOptions = default, string name = "") => CreateScrollView(container.name, position, offset, vertical, horizontal, movementType, elasticity, inertia, decelerationRate, scrollSensitivity, verticalScrollOptions, horizontalScrollOptions, name);
	public LuiContainer CreateScrollView(LuiContainer container, LuiOffset offset, bool vertical, bool horizontal, ScrollRect.MovementType movementType = ScrollRect.MovementType.Clamped, float elasticity = 0, bool inertia = false, float decelerationRate = 0, float scrollSensitivity = 0, LuiScrollbar verticalScrollOptions = default, LuiScrollbar horizontalScrollOptions = default, string name = "") => CreateScrollView(container.name, LuiPosition.None, offset, vertical, horizontal, movementType, elasticity, inertia, decelerationRate, scrollSensitivity, verticalScrollOptions, horizontalScrollOptions, name);
	public LuiContainer CreateScrollView(string parent, LuiOffset offset, bool vertical, bool horizontal, ScrollRect.MovementType movementType = ScrollRect.MovementType.Clamped, float elasticity = 0, bool inertia = false, float decelerationRate = 0, float scrollSensitivity = 0, LuiScrollbar verticalScrollOptions = default, LuiScrollbar horizontalScrollOptions = default, string name = "") => CreateScrollView(parent, LuiPosition.None, offset, vertical, horizontal, movementType, elasticity, inertia, decelerationRate, scrollSensitivity, verticalScrollOptions, horizontalScrollOptions, name);

	public LuiContainer CreateScrollView(string parent, LuiPosition position, LuiOffset offset, bool vertical, bool horizontal, ScrollRect.MovementType movementType = ScrollRect.MovementType.Clamped, float elasticity = 0, bool inertia = false, float decelerationRate = 0, float scrollSensitivity = 0, LuiScrollbar verticalScrollOptions = default, LuiScrollbar horizontalScrollOptions = default, string name = "")
	{
		LuiContainer cont = CreateEmptyContainer(parent, name);
		cont.SetAnchorAndOffset(position, offset);
		cont.SetScrollView(vertical, horizontal, movementType, elasticity, inertia, decelerationRate, scrollSensitivity, verticalScrollOptions, horizontalScrollOptions);
		elements.Add(cont);
		return cont;
	}

	#endregion
	/// <summary>
	/// Gets the built UI in bytes. Useful when redrawing whole UI, and we want to minimalize delay between destroy and draw.
	/// </summary>
	public byte[] GetUiBytes()
	{
		LUIBuilder cbi = new LUIBuilder(this);
		return cbi.GetMergedBytes();
	}
	
	/// <summary>
	/// Builds and sends UI to player.
	/// </summary>
	public void SendUi(BasePlayer player) => SendJson(new SendInfo(player.net.connection));

	/// <summary>
	/// Builds and sends UI to player. Preffered SendUi(BasePlayer) over this method.
	/// </summary>
	public void SendUiJson(BasePlayer player) => SendJson(new SendInfo(player.net.connection));
	
	/// <summary>
	/// Sends already buit UI to player. Need to run GetUiBytes() in order to get the bytes before.
	/// </summary>
	public void SendUiBytes(BasePlayer player, byte[] bytes) => SendBytes(new SendInfo(player.Connection), bytes);


	/// <summary>
	/// Returns string JSON of currently builded UI.
	/// </summary>
	public string ToJson()
	{
		LUIBuilder cbi = new LUIBuilder(this);
		return cbi.GetJsonString();
	}

	private void Send(SendInfo send)
	{
		LUIBuilder cbi = new LUIBuilder(this);
		NetWrite write = Net.sv.StartWrite();
		write.PacketID(Message.Type.RPCMessage);
		write.EntityID(CommunityEntity.ServerInstance.net.ID);
		write.UInt32(StringPool.Get("AddUI"));
		write.String(cbi.GetJsonString());
		write.Send(send);
	}

	private void SendBytes(SendInfo send, byte[] bytes)
	{
		NetWrite write = Net.sv.StartWrite();
		write.PacketID(Message.Type.RPCMessage);
		write.EntityID(CommunityEntity.ServerInstance.net.ID);
		write.UInt32(StringPool.Get("AddUI"));
		write.BytesWithSize(bytes);
		write.Send(send);
	}

	private void SendJson(SendInfo send)
	{
		LUIBuilder cbi = new LUIBuilder(this);
		NetWrite write = Net.sv.StartWrite();
		write.PacketID(Message.Type.RPCMessage);
		write.EntityID(CommunityEntity.ServerInstance.net.ID);
		write.UInt32(StringPool.Get("AddUI"));
		write.String(cbi.GetJsonString());
		write.Send(send);
	}

	public void Dispose()
	{
		elements.Clear();
	}
	
	public static string GetFont(CUI.Handler.FontTypes type)
	{
		return type switch
		{
			CUI.Handler.FontTypes.RobotoCondensedBold => "robotocondensed-bold.ttf",
			CUI.Handler.FontTypes.RobotoCondensedRegular => "robotocondensed-regular.ttf",
			CUI.Handler.FontTypes.PermanentMarker => "permanentmarker.ttf",
			CUI.Handler.FontTypes.DroidSansMono => "droidsansmono.ttf",
			CUI.Handler.FontTypes.NotoSansArabicBold => "_nonenglish/notosanscjksc-bold.otf",
			CUI.Handler.FontTypes.Poxel => "poxel.otf",
			CUI.Handler.FontTypes.LCD => "lcd.ttf",
			CUI.Handler.FontTypes.NoToEmoji => "_nonenglish/notoemoji-regular.ttf",
			CUI.Handler.FontTypes.PressStart => "pressstart2p-regular.ttf",
			_ => "robotocondensed-regular.ttf"
		};
	}

	public static string GetAlign(TextAnchor anchor)
	{
		return anchor switch
		{
			TextAnchor.UpperLeft => nameof(TextAnchor.UpperLeft),
			TextAnchor.UpperCenter => nameof(TextAnchor.UpperCenter),
			TextAnchor.UpperRight => nameof(TextAnchor.UpperRight),
			TextAnchor.MiddleLeft => nameof(TextAnchor.MiddleLeft),
			TextAnchor.MiddleCenter => nameof(TextAnchor.MiddleCenter),
			TextAnchor.MiddleRight => nameof(TextAnchor.MiddleRight),
			TextAnchor.LowerLeft => nameof(TextAnchor.LowerLeft),
			TextAnchor.LowerCenter => nameof(TextAnchor.LowerCenter),
			TextAnchor.LowerRight => nameof(TextAnchor.LowerRight),
			_ => nameof(TextAnchor.UpperLeft)
		};
	}

	public static string GetImageType(UnityEngine.UI.Image.Type imgType)
	{
		return imgType switch
		{
			Image.Type.Simple => nameof(Image.Type.Simple),
			Image.Type.Sliced => nameof(Image.Type.Sliced),
			Image.Type.Tiled => nameof(Image.Type.Tiled),
			Image.Type.Filled => nameof(Image.Type.Filled),
			_ => nameof(Image.Type.Simple)
		};
	}

	public static string GetWrapMode(VerticalWrapMode mode)
	{
		return mode switch
		{
			VerticalWrapMode.Truncate => nameof(VerticalWrapMode.Truncate),
			VerticalWrapMode.Overflow => nameof(VerticalWrapMode.Overflow),
			_ => nameof(VerticalWrapMode.Truncate)
		};
	}

	public static string GetLineType(InputField.LineType lineType)
	{
		return lineType switch
		{
			InputField.LineType.SingleLine => nameof(InputField.LineType.SingleLine),
			InputField.LineType.MultiLineSubmit => nameof(InputField.LineType.MultiLineSubmit),
			InputField.LineType.MultiLineNewline => nameof(InputField.LineType.MultiLineNewline),
			_ => nameof(InputField.LineType.SingleLine)
		};
	}

	public static string GetMovementType(ScrollRect.MovementType movementType)
	{
		return movementType switch
		{
			ScrollRect.MovementType.Unrestricted => nameof(ScrollRect.MovementType.Unrestricted),
			ScrollRect.MovementType.Elastic => nameof(ScrollRect.MovementType.Elastic),
			ScrollRect.MovementType.Clamped => nameof(ScrollRect.MovementType.Clamped),
			_ => nameof(ScrollRect.MovementType.Unrestricted)
		};
	}

	public static string GetTimerFormat(TimerFormat format)
	{
		return format switch
		{
			TimerFormat.None => nameof(TimerFormat.None),
			TimerFormat.SecondsHundreth => nameof(TimerFormat.SecondsHundreth),
			TimerFormat.MinutesSeconds => nameof(TimerFormat.MinutesSeconds),
			TimerFormat.MinutesSecondsHundreth => nameof(TimerFormat.MinutesSecondsHundreth),
			TimerFormat.HoursMinutes => nameof(TimerFormat.HoursMinutes),
			TimerFormat.HoursMinutesSeconds => nameof(TimerFormat.HoursMinutesSeconds),
			TimerFormat.HoursMinutesSecondsMilliseconds => nameof(TimerFormat.HoursMinutesSecondsMilliseconds),
			TimerFormat.HoursMinutesSecondsTenths => nameof(TimerFormat.HoursMinutesSecondsTenths),
			TimerFormat.DaysHoursMinutes => nameof(TimerFormat.DaysHoursMinutes),
			TimerFormat.DaysHoursMinutesSeconds => nameof(TimerFormat.DaysHoursMinutesSeconds),
			TimerFormat.Custom => nameof(TimerFormat.Custom),
			_ => nameof(TimerFormat.None)
		};
	}
	
	public static string GetCorner(GridLayoutGroup.Corner corner)
	{
		return corner switch
		{
			GridLayoutGroup.Corner.UpperLeft => nameof(GridLayoutGroup.Corner.UpperLeft),
			GridLayoutGroup.Corner.UpperRight => nameof(GridLayoutGroup.Corner.UpperRight),
			GridLayoutGroup.Corner.LowerLeft => nameof(GridLayoutGroup.Corner.LowerLeft),
			GridLayoutGroup.Corner.LowerRight => nameof(GridLayoutGroup.Corner.LowerRight),
			_ => nameof(GridLayoutGroup.Corner.UpperLeft)
		};
	}

	public static string GetAxis(GridLayoutGroup.Axis axis)
	{
		return axis switch
		{
			GridLayoutGroup.Axis.Horizontal => nameof(GridLayoutGroup.Axis.Horizontal),
			GridLayoutGroup.Axis.Vertical => nameof(GridLayoutGroup.Axis.Vertical),
			_ => nameof(GridLayoutGroup.Axis.Vertical)
		};
	}

	public static string GetConstraint(GridLayoutGroup.Constraint constraint)
	{
		return constraint switch
		{
			GridLayoutGroup.Constraint.Flexible => nameof(GridLayoutGroup.Constraint.Flexible),
			GridLayoutGroup.Constraint.FixedColumnCount => nameof(GridLayoutGroup.Constraint.FixedColumnCount),
			GridLayoutGroup.Constraint.FixedRowCount => nameof(GridLayoutGroup.Constraint.FixedRowCount),
			_ => nameof(GridLayoutGroup.Constraint.FixedRowCount)
		};
	}

	public static string GetFitMode(ContentSizeFitter.FitMode mode)
	{
		return mode switch
		{
			ContentSizeFitter.FitMode.Unconstrained => nameof(ContentSizeFitter.FitMode.Unconstrained),
			ContentSizeFitter.FitMode.MinSize => nameof(ContentSizeFitter.FitMode.MinSize),
			ContentSizeFitter.FitMode.PreferredSize => nameof(ContentSizeFitter.FitMode.PreferredSize),
			_ => nameof(ContentSizeFitter.FitMode.Unconstrained)
		};
	}

	public static string GetSendType(CommunityEntity.DraggablePositionSendType type)
	{
		return type switch
		{
			CommunityEntity.DraggablePositionSendType.NormalizedScreen => nameof(CommunityEntity.DraggablePositionSendType.NormalizedScreen),
			CommunityEntity.DraggablePositionSendType.NormalizedParent => nameof(CommunityEntity.DraggablePositionSendType.NormalizedParent),
			CommunityEntity.DraggablePositionSendType.Relative => nameof(CommunityEntity.DraggablePositionSendType.Relative),
			CommunityEntity.DraggablePositionSendType.RelativeAnchor => nameof(CommunityEntity.DraggablePositionSendType.RelativeAnchor),
			_ => nameof(CommunityEntity.DraggablePositionSendType.NormalizedScreen)
		};
	}

	public class LuiContainer
	{
		public string name;
		public string parent;
		public LuiComponentDictionary luiComponents = new();
		public string destroyUi;
		public float fadeOut;
		public bool update;

		#region Container Methods - Global

		public LuiContainer SetDestroy(string name)
		{
			destroyUi = name;
			return this;
		}

		public LuiContainer SetFadeOut(float time)
		{
			fadeOut = time;
			return this;
		}

		public LuiContainer SetName(string newName)
		{
			name = newName;
			return this;
		}

	    /// <summary>
	    /// Updates or creates new component in current element.
		/// Recommended to use only when there is no built-in method for that.
	    /// </summary>
		public T UpdateComp<T>() where T : LuiCompBase, new()
		{
			//if (!update)
			//	Logger.Warn($"[LUI] You're trying to create update in element '{name}' (of parent '{parent}') which doesn't allow updates. Ignoring.");
			if (luiComponents.TryGetValue<T>(GetLuiCompType(typeof(T)), out var component))
			{
				return component;
			}
			component = new T();
			luiComponents.Add(component.type, component);
			return component;
		}

		public void SetEnabled<T>(bool enabled = true) where T : LuiCompBase
		{
			//if (!update)
			//{
			//	Logger.Warn($"[LUI] You're trying to create update in element '{name}' (of parent '{parent}') which doesn't allow updates. Ignoring.");
			//	return;
			//}
			if (luiComponents.TryGetValue<T>(GetLuiCompType(typeof(T)), out var component))
			{
				component.enabled = enabled;
			}
			else
			{
				Interface.Oxide.LogWarning($"[LUI] You're trying to switch state of component '{typeof(T)}' but it isn't present. Ignoring.");
			}
		}

		public void SetFadeIn<T>(float fadeIn) where T : LuiCompBase
		{
			if (luiComponents.TryGetValue<T>(GetLuiCompType(typeof(T)), out var component))
			{
				component.fadeIn = fadeIn;
			}
			else
			{
				Interface.Oxide.LogWarning($"[LUI] You're trying to switch fadeIn of component '{typeof(T)}' but it isn't present. Ignoring.");
			}
		}

		public void SetPlaceholderParentId<T>(string placeholderParentId) where T : LuiCompBase
		{
			if (luiComponents.TryGetValue<T>(GetLuiCompType(typeof(T)), out var component))
			{
				component.placeholderParentId = placeholderParentId;
			}
			else
			{
				Interface.Oxide.LogWarning($"[LUI] You're trying to switch placeholderParentId of component '{typeof(T)}' but it isn't present. Ignoring.");
			}
		}

		public static LuiCompType GetLuiCompType(Type type)
		{
			return type switch
			{
				not null when type == typeof(LuiTextComp) => LuiCompType.Text,
				not null when type == typeof(LuiImageComp) => LuiCompType.Image,
				not null when type == typeof(LuiRawImageComp) => LuiCompType.RawImage,
				not null when type == typeof(LuiButtonComp) => LuiCompType.Button,
				not null when type == typeof(LuiOutlineComp) => LuiCompType.Outline,
				not null when type == typeof(LuiInputComp) => LuiCompType.InputField,
				not null when type == typeof(LuiCursorComp) => LuiCompType.NeedsCursor,
				not null when type == typeof(LuiRectTransformComp) => LuiCompType.RectTransform,
				not null when type == typeof(LuiCountdownComp) => LuiCompType.Countdown,
				not null when type == typeof(LuiHorizontalLayoutGroupComp) => LuiCompType.HorizontalLayoutGroup,
				not null when type == typeof(LuiVerticalLayoutGroupComp) => LuiCompType.VerticalLayoutGroup,
				not null when type == typeof(LuiGridLayoutGroupComp) => LuiCompType.GridLayoutGroup,
				not null when type == typeof(LuiContentSizeFitterComp) => LuiCompType.ContentSizeFitter,
				not null when type == typeof(LuiLayoutElementComp) => LuiCompType.LayoutElement,
				not null when type == typeof(LuiDraggableComp) => LuiCompType.Draggable,
				not null when type == typeof(LuiSlotComp) => LuiCompType.Slot,
				not null when type == typeof(LuiKeyboardComp) => LuiCompType.NeedsKeyboard,
				not null when type == typeof(LuiScrollComp) => LuiCompType.ScrollView,
				_ => LuiCompType.Image
			};
		}

		#endregion

		#region Container Methods - LuiTextComp

		public LuiContainer SetText(string input, int fontSize = 0, string color = null, TextAnchor alignment = TextAnchor.MiddleCenter, bool update = false)
		{
			if (luiComponents.TryGetValue<LuiTextComp>(LuiCompType.Text, out var text))
			{
				text.text = input;
				if (fontSize > 0)
					text.fontSize = fontSize;
				if (color != null)
					text.color = color;
				if (!update)
					text.align = GetAlign(alignment);
			}
			else
			{
				text = new();
				text.text = input;
				if (fontSize > 0)
					text.fontSize = fontSize;
				if (color != null)
					text.color = color;
				if (!update)
					text.align = GetAlign(alignment);
				luiComponents.Add(text.type, text);
			}
			return this;
		}

		public LuiContainer SetTextColor(string color)
		{
			if (luiComponents.TryGetValue<LuiTextComp>(LuiCompType.Text, out var text))
			{
				text.color = color;
			}
			else
			{
				text = new();
				text.color = color;
				luiComponents.Add(text.type, text);
			}
			return this;
		}

		public LuiContainer SetTextFont(CUI.Handler.FontTypes font)
		{
			if (luiComponents.TryGetValue<LuiTextComp>(LuiCompType.Text, out var text))
			{
				text.font = GetFont(font);
			}
			else
			{
				text = new();
				text.font = GetFont(font);
				luiComponents.Add(text.type, text);
			}
			return this;
		}

		public LuiContainer SetTextAlign(TextAnchor align)
		{
			if (luiComponents.TryGetValue<LuiTextComp>(LuiCompType.Text, out var text))
			{
				text.align = GetAlign(align);
			}
			else
			{
				text = new();
				text.align = GetAlign(align);
				luiComponents.Add(text.type, text);
			}
			return this;
		}

		public LuiContainer SetTextOverflow(VerticalWrapMode verticalOverflow)
		{
			if (luiComponents.TryGetValue<LuiTextComp>(LuiCompType.Text, out var text))
			{
				text.verticalOverflow = GetWrapMode(verticalOverflow);
			}
			else
			{
				text = new();
				text.verticalOverflow = GetWrapMode(verticalOverflow);
				luiComponents.Add(text.type, text);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiImageComp

		public LuiContainer SetColor(string color)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				img.color = color;
			}
			else
			{
				img = new();
				img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetMaterial(string material)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				img.material = material;
			}
			else
			{
				img = new();
				img.material = material;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetImageType(UnityEngine.UI.Image.Type imageType)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				img.imageType = GetImageType(imageType);
			}
			else
			{
				img = new();
				img.imageType = GetImageType(imageType);
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetSprite(string sprite = null, string color = null, UnityEngine.UI.Image.Type imageType = Image.Type.Simple)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				if (sprite != null)
				{
					img.sprite = sprite;
					img.imageType = GetImageType(imageType);
				}
				if (color != null)
					img.color = color;
			}
			else
			{
				img = new();
				if (sprite != null)
				{
					img.sprite = sprite;
					img.imageType = GetImageType(imageType);
				}
				if (color != null)
					img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetImage(string png = null, string color = null)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				if (png != null)
					img.png = png;
				if (color != null)
					img.color = color;
			}
			else
			{
				img = new();
				if (png != null)
					img.png = png;
				if (color != null)
					img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetFillCenter(bool fill)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				img.fillCenter = fill;
			}
			else
			{
				img = new();
				img.fillCenter = fill;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetImageSlice(string sliceValue)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				img.slice = sliceValue;
			}
			else
			{
				img = new();
				img.slice = sliceValue;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetItemIcon(int itemid, ulong skinid)
		{
			if (luiComponents.TryGetValue<LuiImageComp>(LuiCompType.Image, out var img))
			{
				img.itemid = itemid;
				img.skinid = skinid;
			}
			else
			{
				img = new();
				img.itemid = itemid;
				img.skinid = skinid;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiRawImageComp

		public LuiContainer SetUrlImage(string url = null, string color = null)
		{
			if (luiComponents.TryGetValue<LuiRawImageComp>(LuiCompType.RawImage, out var img))
			{
				if (url != null)
					img.url = url;
				if (color != null)
					img.color = color;
			}
			else
			{
				img = new();
				if (url != null)
					img.url = url;
				if (color != null)
					img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetRawImage(string png = null, string color = null)
		{
			if (luiComponents.TryGetValue<LuiRawImageComp>(LuiCompType.RawImage, out var img))
			{
				if (png != null)
					img.png = png;
				if (color != null)
					img.color = color;
			}
			else
			{
				img = new();
				if (png != null)
					img.png = png;
				if (color != null)
					img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetSteamIcon(string steamid, string color = null)
		{
			if (luiComponents.TryGetValue<LuiRawImageComp>(LuiCompType.RawImage, out var img))
			{
				img.steamid = steamid;
				if (color != null)
					img.color = color;
			}
			else
			{
				img = new();
				img.steamid = steamid;
				if (color != null)
					img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetRawSprite(string sprite, string color = null)
		{
			if (luiComponents.TryGetValue<LuiRawImageComp>(LuiCompType.RawImage, out var img))
			{
				img.sprite = sprite;
				if (color != null)
					img.color = color;
			}
			else
			{
				img = new();
				img.sprite = sprite;
				if (color != null)
					img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		public LuiContainer SetRawMaterial(string material, string color = null)
		{
			if (luiComponents.TryGetValue<LuiRawImageComp>(LuiCompType.RawImage, out var img))
			{
				img.material = material;
				if (color != null)
					img.color = color;
			}
			else
			{
				img = new();
				img.material = material;
				if (color != null)
					img.color = color;
				luiComponents.Add(img.type, img);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiButtonComp

		public LuiContainer SetButton(string command = null, string color = null)
		{
			if (luiComponents.TryGetValue<LuiButtonComp>(LuiCompType.Button, out var button))
			{
				if (command != null)
					button.command = command;
				if (color != null)
					button.color = color;
			}
			else
			{
				button = new();
				if (command != null)
					button.command = command;
				if (color != null)
					button.color = color;
				luiComponents.Add(button.type, button);
			}
			return this;
		}

		public LuiContainer SetButtonColors(string color = null, string normalColor = null, string highlightedColor = null, string pressedColor = null, string selectedColor = null, string disabledColor = null, float colorMultiplier = -1, float fadeDuration = -1)
		{
			if (luiComponents.TryGetValue<LuiButtonComp>(LuiCompType.Button, out var button))
			{
				if (color != null)
					button.color = color;
				if (normalColor != null)
					button.normalColor = normalColor;
				if (highlightedColor != null)
					button.highlightedColor = highlightedColor;
				if (pressedColor != null)
					button.pressedColor = pressedColor;
				if (selectedColor != null)
					button.selectedColor = selectedColor;
				if (disabledColor != null)
					button.disabledColor = disabledColor;
				if (colorMultiplier != -1)
					button.colorMultiplier = colorMultiplier;
				if (fadeDuration != -1)
					button.fadeDuration = fadeDuration;
			}
			else
			{
				button = new();
				if (color != null)
					button.color = color;
				if (normalColor != null)
					button.normalColor = normalColor;
				if (highlightedColor != null)
					button.highlightedColor = highlightedColor;
				if (pressedColor != null)
					button.pressedColor = pressedColor;
				if (selectedColor != null)
					button.selectedColor = selectedColor;
				if (disabledColor != null)
					button.disabledColor = disabledColor;
				if (colorMultiplier != -1)
					button.colorMultiplier = colorMultiplier;
				if (fadeDuration != -1)
					button.fadeDuration = fadeDuration;
				luiComponents.Add(button.type, button);
			}
			return this;
		}

		public LuiContainer SetButtonMaterial(string material)
		{
			if (luiComponents.TryGetValue<LuiButtonComp>(LuiCompType.Button, out var button))
			{
				button.material = material;
			}
			else
			{
				button = new();
				button.material = material;
				luiComponents.Add(button.type, button);
			}
			return this;
		}

		public LuiContainer SetButtonSprite(string sprite,  UnityEngine.UI.Image.Type imageType = Image.Type.Simple)
		{
			if (luiComponents.TryGetValue<LuiButtonComp>(LuiCompType.Button, out var button))
			{
				button.sprite = sprite;
				button.imageType = GetImageType(imageType);
			}
			else
			{
				button = new();
				button.sprite = sprite;
				button.imageType = GetImageType(imageType);
				luiComponents.Add(button.type, button);
			}
			return this;
		}

		public LuiContainer SetButtonClose(string close)
		{
			if (luiComponents.TryGetValue<LuiButtonComp>(LuiCompType.Button, out var button))
			{
				button.close = close;
			}
			else
			{
				button = new();
				button.close = close;
				luiComponents.Add(button.type, button);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiOutlineComp

		public LuiContainer SetOutline(string color, Vector2 distance, bool useGraphicAlpha = false)
		{
			if (luiComponents.TryGetValue<LuiOutlineComp>(LuiCompType.Outline, out var outline))
			{
				outline.color = color;
				outline.distance = distance;
				outline.useGraphicAlpha = useGraphicAlpha;
			}
			else
			{
				outline = new();
				outline.color = color;
				outline.distance = distance;
				outline.useGraphicAlpha = useGraphicAlpha;
				luiComponents.Add(outline.type, outline);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiInputComp

		public LuiContainer SetInput(string color = null, string text = null, int fontSize = 0, string command = null, int charLimit = 0, CUI.Handler.FontTypes font = CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor alignment = TextAnchor.MiddleCenter, bool update = false)
		{
			if (luiComponents.TryGetValue<LuiInputComp>(LuiCompType.InputField, out var input))
			{
				if (color != null)
					input.color = color;
				if (text != null)
					input.text = text;
				if (fontSize > 0)
					input.fontSize = fontSize;
				if (command != null)
					input.command = command;
				if (charLimit > 0)
					input.characterLimit = charLimit;
				if (!update)
				{
					input.align = GetAlign(alignment);
					input.font = GetFont(font);
				}
			}
			else
			{
				input = new();
				if (color != null)
					input.color = color;
				if (text != null)
					input.text = text;
				if (fontSize > 0)
					input.fontSize = fontSize;
				if (command != null)
					input.command = command;
				if (charLimit > 0)
					input.characterLimit = charLimit;
				if (!update)
				{
					input.align = GetAlign(alignment);
					input.font = GetFont(font);
				}
				luiComponents.Add(input.type, input);
			}
			return this;
		}

		public LuiContainer SetInputReadOnly(bool readOnly)
		{
			if (luiComponents.TryGetValue<LuiInputComp>(LuiCompType.InputField, out var input))
			{
				input.readOnly = readOnly;
			}
			else
			{
				input = new();
				input.readOnly = readOnly;
				luiComponents.Add(input.type, input);
			}
			return this;
		}

		public LuiContainer SetInputPassword(bool password)
		{
			if (luiComponents.TryGetValue<LuiInputComp>(LuiCompType.InputField, out var input))
			{
				input.password = password;
			}
			else
			{
				input = new();
				input.password = password;
				luiComponents.Add(input.type, input);
			}
			return this;
		}

		public LuiContainer SetInputAutoFocus(bool autofocus)
		{
			if (luiComponents.TryGetValue<LuiInputComp>(LuiCompType.InputField, out var input))
			{
				input.autofocus = autofocus;
			}
			else
			{
				input = new();
				input.autofocus = autofocus;
				luiComponents.Add(input.type, input);
			}
			return this;
		}

		public LuiContainer SetInputKeyboard(bool needsKeyboard = false, bool hudMenuInput = false)
		{
			if (luiComponents.TryGetValue<LuiInputComp>(LuiCompType.InputField, out var input))
			{
				input.needsKeyboard = needsKeyboard;
				input.hudMenuInput = hudMenuInput;
			}
			else
			{
				input = new();
				input.needsKeyboard = needsKeyboard;
				input.hudMenuInput = hudMenuInput;
				luiComponents.Add(input.type, input);
			}
			return this;
		}

		public LuiContainer SetInputLineType(UnityEngine.UI.InputField.LineType lineType)
		{
			if (luiComponents.TryGetValue<LuiInputComp>(LuiCompType.InputField, out var input))
			{
				input.lineType = GetLineType(lineType);
			}
			else
			{
				input = new();
				input.lineType = GetLineType(lineType);
				luiComponents.Add(input.type, input);
			}
			return this;
		}

		public LuiContainer SetInputPlaceholder(string placeholderId)
		{
			if (luiComponents.TryGetValue<LuiInputComp>(LuiCompType.InputField, out var input))
			{
				input.placeholderId = placeholderId;
			}
			else
			{
				input = new();
				input.placeholderId = placeholderId;
				luiComponents.Add(input.type, input);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiCursorComp

		public LuiContainer AddCursor()
		{
			if (!luiComponents.TryGetValue<LuiCursorComp>(LuiCompType.Button, out var cursor))
			{
				cursor = new();
				luiComponents.Add(cursor.type, cursor);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiRectTransformComp

		public LuiContainer SetAnchors(LuiPosition pos)
		{
			if (luiComponents.TryGetValue<LuiRectTransformComp>(LuiCompType.RectTransform, out var rect))
			{
				rect.anchor = pos;
			}
			else
			{
				rect = new();
				rect.anchor = pos;
				luiComponents.Add(rect.type, rect);
			}
			return this;
		}

		public LuiContainer SetOffset(LuiOffset off)
		{
			if (luiComponents.TryGetValue<LuiRectTransformComp>(LuiCompType.RectTransform, out var rect))
			{
				rect.offset = off;
			}
			else
			{
				rect = new();
				rect.offset = off;
				luiComponents.Add(rect.type, rect);
			}
			return this;
		}

		public LuiContainer SetRotation(float rotation)
		{
			if (luiComponents.TryGetValue<LuiRectTransformComp>(LuiCompType.RectTransform, out var rect))
			{
				rect.rotation = rotation;
			}
			else
			{
				rect = new();
				rect.rotation = rotation;
				luiComponents.Add(rect.type, rect);
			}
			return this;
		}

		public LuiContainer SetAnchorAndOffset(LuiPosition pos, LuiOffset off)
		{
			if (luiComponents.TryGetValue<LuiRectTransformComp>(LuiCompType.RectTransform, out var rect))
			{
				rect.anchor = pos;
				rect.offset = off;
			}
			else
			{
				rect = new();
				rect.anchor = pos;
				rect.offset = off;
				luiComponents.Add(rect.type, rect);
			}
			return this;
		}

		public LuiContainer SetRectParent(string setParent)
		{
			if (luiComponents.TryGetValue<LuiRectTransformComp>(LuiCompType.RectTransform, out var rect))
			{
				rect.setParent = setParent;
			}
			else
			{
				rect = new();
				rect.setParent = setParent;
				luiComponents.Add(rect.type, rect);
			}
			return this;
		}

		public LuiContainer SetRectIndex(int setTransformIndex)
		{
			if (luiComponents.TryGetValue<LuiRectTransformComp>(LuiCompType.RectTransform, out var rect))
			{
				rect.setTransformIndex = setTransformIndex;
			}
			else
			{
				rect = new();
				rect.setTransformIndex = setTransformIndex;
				luiComponents.Add(rect.type, rect);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiCountdownComp

		public LuiContainer SetCountdown(float startTime, float endTime, float step = 1, float interval = 1, string command = null, string numberFormat = null)
		{
			if (luiComponents.TryGetValue<LuiCountdownComp>(LuiCompType.Countdown, out var countdown))
			{
				countdown.startTime = startTime;
				countdown.endTime = endTime;
				if (step != 1)
					countdown.step = step;
				if (interval != 1)
					countdown.interval = interval;
				if (command != null)
					countdown.command = command;
				if (numberFormat != null)
					countdown.numberFormat = numberFormat;
			}
			else
			{
				countdown = new();
				countdown.startTime = startTime;
				countdown.endTime = endTime;
				if (step != 1)
					countdown.step = step;
				if (interval != 1)
					countdown.interval = interval;
				if (command != null)
					countdown.command = command;
				if (numberFormat != null)
					countdown.numberFormat = numberFormat;
				luiComponents.Add(countdown.type, countdown);
			}
			return this;
		}

		public LuiContainer SetCountdownDestroy(bool destroy)
		{
			if (luiComponents.TryGetValue<LuiCountdownComp>(LuiCompType.Countdown, out var countdown))
			{
				countdown.destroyIfDone = destroy;
			}
			else
			{
				countdown = new();
				countdown.destroyIfDone = destroy;
				luiComponents.Add(countdown.type, countdown);
			}
			return this;
		}

		public LuiContainer SetCountdownTimerFormat(TimerFormat format)
		{
			if (luiComponents.TryGetValue<LuiCountdownComp>(LuiCompType.Countdown, out var countdown))
			{
				countdown.timerFormat = GetTimerFormat(format);
			}
			else
			{
				countdown = new();
				countdown.timerFormat = GetTimerFormat(format);
				luiComponents.Add(countdown.type, countdown);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiHorizontalLayoutGroupComp

		public LuiContainer SetHorizontalLayoutSpacing(float spacing)
		{
			if (luiComponents.TryGetValue<LuiHorizontalLayoutGroupComp>(LuiCompType.HorizontalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.spacing = spacing;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.spacing = spacing;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetHorizontalLayoutAlignment(TextAnchor anchor)
		{
			if (luiComponents.TryGetValue<LuiHorizontalLayoutGroupComp>(LuiCompType.HorizontalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childAlignment = GetAlign(anchor);
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childAlignment = GetAlign(anchor);
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetHorizontalLayoutForceExpand(bool width, bool height)
		{
			if (luiComponents.TryGetValue<LuiHorizontalLayoutGroupComp>(LuiCompType.HorizontalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childForceExpandWidth = width;
				layoutGroup.childForceExpandHeight = height;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childForceExpandWidth = width;
				layoutGroup.childForceExpandHeight = height;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetHorizontalLayoutControl(bool width, bool height)
		{
			if (luiComponents.TryGetValue<LuiHorizontalLayoutGroupComp>(LuiCompType.HorizontalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childControlWidth = width;
				layoutGroup.childControlHeight = height;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childControlWidth = width;
				layoutGroup.childControlHeight = height;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetHorizontalLayoutScale(bool width, bool height)
		{
			if (luiComponents.TryGetValue<LuiHorizontalLayoutGroupComp>(LuiCompType.HorizontalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childScaleWidth = width;
				layoutGroup.childScaleHeight = height;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childScaleWidth = width;
				layoutGroup.childScaleHeight = height;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetHorizontalLayoutPadding(string padding)
		{
			if (luiComponents.TryGetValue<LuiHorizontalLayoutGroupComp>(LuiCompType.HorizontalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.padding = padding;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.padding = padding;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		#endregion


		#region Container Methods - LuiVerticalLayoutGroupComp

		public LuiContainer SetVerticalLayoutSpacing(float spacing)
		{
			if (luiComponents.TryGetValue<LuiVerticalLayoutGroupComp>(LuiCompType.VerticalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.spacing = spacing;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.spacing = spacing;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetVerticalLayoutAlignment(TextAnchor anchor)
		{
			if (luiComponents.TryGetValue<LuiVerticalLayoutGroupComp>(LuiCompType.VerticalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childAlignment = GetAlign(anchor);
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childAlignment = GetAlign(anchor);
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetVerticalLayoutForceExpand(bool width, bool height)
		{
			if (luiComponents.TryGetValue<LuiVerticalLayoutGroupComp>(LuiCompType.VerticalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childForceExpandWidth = width;
				layoutGroup.childForceExpandHeight = height;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childForceExpandWidth = width;
				layoutGroup.childForceExpandHeight = height;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetVerticalLayoutControl(bool width, bool height)
		{
			if (luiComponents.TryGetValue<LuiVerticalLayoutGroupComp>(LuiCompType.VerticalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childControlWidth = width;
				layoutGroup.childControlHeight = height;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childControlWidth = width;
				layoutGroup.childControlHeight = height;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetVerticalLayoutScale(bool width, bool height)
		{
			if (luiComponents.TryGetValue<LuiVerticalLayoutGroupComp>(LuiCompType.VerticalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childScaleWidth = width;
				layoutGroup.childScaleHeight = height;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childScaleWidth = width;
				layoutGroup.childScaleHeight = height;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetVerticalLayoutPadding(string padding)
		{
			if (luiComponents.TryGetValue<LuiVerticalLayoutGroupComp>(LuiCompType.VerticalLayoutGroup, out var layoutGroup))
			{
				layoutGroup.padding = padding;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.padding = padding;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiGridLayoutGroupComp

		public LuiContainer SetCellSize(Vector2 size)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.cellSize = size;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.cellSize = size;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetCellSpacing(Vector2 spacing)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.spacing = spacing;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.spacing = spacing;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetStartCorner(GridLayoutGroup.Corner corner)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.startCorner = GetCorner(corner);
			}
			else
			{
				layoutGroup = new();
				layoutGroup.startCorner = GetCorner(corner);
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetStartAxis(GridLayoutGroup.Axis axis)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.startAxis = GetAxis(axis);
			}
			else
			{
				layoutGroup = new();
				layoutGroup.startAxis = GetAxis(axis);
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetChildAlign(TextAnchor align)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.childAlignment = GetAlign(align);
			}
			else
			{
				layoutGroup = new();
				layoutGroup.childAlignment = GetAlign(align);
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetContraint(GridLayoutGroup.Constraint constraint)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.constraint = GetConstraint(constraint);
			}
			else
			{
				layoutGroup = new();
				layoutGroup.constraint = GetConstraint(constraint);
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetContraintCount(int count)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.constraintCount = count;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.constraintCount = count;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		public LuiContainer SetGridLayoutPadding(string padding)
		{
			if (luiComponents.TryGetValue<LuiGridLayoutGroupComp>(LuiCompType.GridLayoutGroup, out var layoutGroup))
			{
				layoutGroup.padding = padding;
			}
			else
			{
				layoutGroup = new();
				layoutGroup.padding = padding;
				luiComponents.Add(layoutGroup.type, layoutGroup);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiContentSizeFitterComp

		public LuiContainer SetFitMode(ContentSizeFitter.FitMode horizontalFit, ContentSizeFitter.FitMode verticalFit)
		{
			if (luiComponents.TryGetValue<LuiContentSizeFitterComp>(LuiCompType.ContentSizeFitter, out var fitterComp))
			{
				fitterComp.horizontalFit = GetFitMode(horizontalFit);
				fitterComp.verticalFit = GetFitMode(verticalFit);
			}
			else
			{
				fitterComp = new();
				fitterComp.horizontalFit = GetFitMode(horizontalFit);
				fitterComp.verticalFit = GetFitMode(verticalFit);
				luiComponents.Add(fitterComp.type, fitterComp);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiLayoutElementComp

		public LuiContainer SetPrefferedSize(float width = -1f, float height = -1f)
		{
			if (luiComponents.TryGetValue<LuiLayoutElementComp>(LuiCompType.LayoutElement, out var layoutComp))
			{
				if (width != -1f)
					layoutComp.preferredWidth = width;
				if (height != -1f)
					layoutComp.preferredHeight = height;
			}
			else
			{
				layoutComp = new();
				if (width != -1f)
					layoutComp.preferredWidth = width;
				if (height != -1f)
					layoutComp.preferredHeight = height;
				luiComponents.Add(layoutComp.type, layoutComp);
			}
			return this;
		}

		public LuiContainer SetMinimalSize(float width = -1f, float height = -1f)
		{
			if (luiComponents.TryGetValue<LuiLayoutElementComp>(LuiCompType.LayoutElement, out var layoutComp))
			{
				if (width != -1f)
					layoutComp.minWidth = width;
				if (height != -1f)
					layoutComp.minHeight = height;
			}
			else
			{
				layoutComp = new();
				if (width != -1f)
					layoutComp.minWidth = width;
				if (height != -1f)
					layoutComp.minHeight = height;
				luiComponents.Add(layoutComp.type, layoutComp);
			}
			return this;
		}

		public LuiContainer SetFlexible(float width = -1f, float height = -1f)
		{
			if (luiComponents.TryGetValue<LuiLayoutElementComp>(LuiCompType.LayoutElement, out var layoutComp))
			{
				if (width != -1f)
					layoutComp.flexibleWidth = width;
				if (height != -1f)
					layoutComp.flexibleHeight = height;
			}
			else
			{
				layoutComp = new();
				if (width != -1f)
					layoutComp.flexibleWidth = width;
				if (height != -1f)
					layoutComp.flexibleHeight = height;
				luiComponents.Add(layoutComp.type, layoutComp);
			}
			return this;
		}

		public LuiContainer SetIgnoreLayout(bool ignore)
		{
			if (luiComponents.TryGetValue<LuiLayoutElementComp>(LuiCompType.LayoutElement, out var layoutComp))
			{
				layoutComp.ignoreLayout = ignore;
			}
			else
			{
				layoutComp = new();
				layoutComp.ignoreLayout = ignore;
				luiComponents.Add(layoutComp.type, layoutComp);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiDraggableComp

		public LuiContainer SetDraggable(string filter = null, bool dropAnywhere = true, bool keepOnTop = false, bool limitToParent = false, float maxDistance = -1f, bool allowSwapping = false)
		{
			if (luiComponents.TryGetValue<LuiDraggableComp>(LuiCompType.Draggable, out var drag))
			{
				if (filter != null)
					drag.filter = filter;
				if (drag.maxDistance != -1)
					drag.maxDistance = maxDistance;
				drag.dropAnywhere = dropAnywhere;
				drag.keepOnTop = keepOnTop;
				drag.limitToParent = limitToParent;
				drag.allowSwapping = allowSwapping;
			}
			else
			{
				drag = new();
				if (filter != null)
					drag.filter = filter;
				if (drag.maxDistance != -1)
					drag.maxDistance = maxDistance;
				drag.dropAnywhere = dropAnywhere;
				drag.keepOnTop = keepOnTop;
				drag.limitToParent = limitToParent;
				drag.allowSwapping = allowSwapping;
				luiComponents.Add(drag.type, drag);
			}
			return this;
		}

		public LuiContainer SetDragAlpha(float alpha)
		{
			if (luiComponents.TryGetValue<LuiDraggableComp>(LuiCompType.Draggable, out var drag))
			{
				drag.dragAlpha = alpha;
			}
			else
			{
				drag = new();
				drag.dragAlpha = alpha;
				luiComponents.Add(drag.type, drag);
			}
			return this;
		}

		public LuiContainer SetParentLimitIndex(int index)
		{
			if (luiComponents.TryGetValue<LuiDraggableComp>(LuiCompType.Draggable, out var drag))
			{
				drag.parentLimitIndex = index;
			}
			else
			{
				drag = new();
				drag.parentLimitIndex = index;
				luiComponents.Add(drag.type, drag);
			}
			return this;
		}

		public LuiContainer SetDraggableParentPadding(Vector2 padding)
		{
			if (luiComponents.TryGetValue<LuiDraggableComp>(LuiCompType.Draggable, out var drag))
			{
				drag.parentPadding = padding;
			}
			else
			{
				drag = new();
				drag.parentPadding = padding;
				luiComponents.Add(drag.type, drag);
			}
			return this;
		}

		public LuiContainer SetDraggableAnchorOffset(Vector2 offset)
		{
			if (luiComponents.TryGetValue<LuiDraggableComp>(LuiCompType.Draggable, out var drag))
			{
				drag.anchorOffset = offset;
			}
			else
			{
				drag = new();
				drag.anchorOffset = offset;
				luiComponents.Add(drag.type, drag);
			}
			return this;
		}

		public LuiContainer SetDraggableRPC(CommunityEntity.DraggablePositionSendType posSendType)
		{
			if (luiComponents.TryGetValue<LuiDraggableComp>(LuiCompType.Draggable, out var drag))
			{
				drag.positionRPC = GetSendType(posSendType);
			}
			else
			{
				drag = new();
				drag.positionRPC = GetSendType(posSendType);
				luiComponents.Add(drag.type, drag);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiSlotComp

		public LuiContainer SetSlot(string filter = null)
		{
			if (luiComponents.TryGetValue<LuiSlotComp>(LuiCompType.Slot, out var slot))
			{
				if (filter != null)
					slot.filter = filter;
			}
			else
			{
				slot = new();
				if (filter != null)
					slot.filter = filter;
				luiComponents.Add(slot.type, slot);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiKeyboardComp

		public LuiContainer AddKeyboard()
		{
			if (!luiComponents.TryGetValue<LuiKeyboardComp>(LuiCompType.Button, out var keyboard))
			{
				keyboard = new();
				luiComponents.Add(keyboard.type, keyboard);
			}
			return this;
		}

		#endregion

		#region Container Methods - LuiScrollComp

		public LuiContainer SetScrollView(bool vertical, bool horizontal, ScrollRect.MovementType movementType = ScrollRect.MovementType.Clamped, float elasticity = 0, bool inertia = false, float decelerationRate = 0, float scrollSensitivity = 0, LuiScrollbar verticalScrollOptions = default, LuiScrollbar horizontalScrollOptions = default, bool update = false)
		{
			if (luiComponents.TryGetValue<LuiScrollComp>(LuiCompType.ScrollView, out var scroll))
			{
				if (!update)
				{
					scroll.vertical = vertical;
					scroll.horizontal = horizontal;
					scroll.movementType = GetMovementType(movementType);
					scroll.inertia = inertia;
				}
				if (elasticity != 0)
					scroll.elasticity = elasticity;
				if (decelerationRate != 0)
					scroll.decelerationRate = decelerationRate;
				if (scrollSensitivity != 0)
					scroll.scrollSensitivity = scrollSensitivity;
				scroll.verticalScrollbar = verticalScrollOptions;
				scroll.horizontalScrollbar = horizontalScrollOptions;
			}
			else
			{
				scroll = new();
				if (!update)
				{
					scroll.vertical = vertical;
					scroll.horizontal = horizontal;
					scroll.movementType = GetMovementType(movementType);
					scroll.inertia = inertia;
				}
				if (elasticity != 0)
					scroll.elasticity = elasticity;
				if (decelerationRate != 0)
					scroll.decelerationRate = decelerationRate;
				if (scrollSensitivity != 0)
					scroll.scrollSensitivity = scrollSensitivity;
				scroll.verticalScrollbar = verticalScrollOptions;
				scroll.horizontalScrollbar = horizontalScrollOptions;
				luiComponents.Add(scroll.type, scroll);
			}
			return this;
		}

		public LuiContainer SetScrollContent(LuiPosition pos, LuiOffset offset)
		{
			if (luiComponents.TryGetValue<LuiScrollComp>(LuiCompType.ScrollView, out var scroll))
			{
				scroll.anchor = pos;
				scroll.offset = offset;
			}
			else
			{
				scroll = new();
				scroll.anchor = pos;
				scroll.offset = offset;
				luiComponents.Add(scroll.type, scroll);
			}
			return this;
		}

		public LuiContainer SetScrollPivot(Vector2 pivot)
		{
			if (luiComponents.TryGetValue<LuiScrollComp>(LuiCompType.ScrollView, out var scroll))
			{
				scroll.pivot = pivot;
			}
			else
			{
				scroll = new();
				scroll.pivot = pivot;
				luiComponents.Add(scroll.type, scroll);
			}
			return this;
		}

		public LuiContainer SetScrollbarPosition(float horizontal = 0, float vertical = 0)
		{
			if (luiComponents.TryGetValue<LuiScrollComp>(LuiCompType.ScrollView, out var scroll))
			{
				if (horizontal != 0)
					scroll.horizontalNormalizedPosition = horizontal;
				if (vertical != 0)
					scroll.verticalNormalizedPosition = vertical;
			}
			else
			{
				scroll = new();
				if (horizontal != 0)
					scroll.horizontalNormalizedPosition = horizontal;
				if (vertical != 0)
					scroll.verticalNormalizedPosition = vertical;
				luiComponents.Add(scroll.type, scroll);
			}
			return this;
		}

		#endregion

	}
}

public static class LuiColors
{
	public const string Transparent = "0.0 0.0 0.0 0.0";
	public const string White = "1.0 1.0 1.0 1.0";
	public const string Gray = "0.5 0.5 0.5 1.0";
	public const string Black = "0.0 0.0 0.0 1.0";
	public const string Red = "1.0 0.0 0.0 1.0";
	public const string Green = "0.0 1.0 0.0 1.0";
	public const string Blue = "0.0 0.0 1.0 1.0";
}

public class LuiComponentDictionary : IEnumerable
{
	private readonly LuiCompBase[] _values;
	private int _count;
	private const int DictionarySize = 10; //Dictionary based on most possible types of components in one element, at the date od 24.02.2025 it's 8 (including draggables), so adding 2 more for safety.

	public LuiComponentDictionary()
	{
		_values = new LuiCompBase[DictionarySize];
		_count = 0;
	}

	public int Count => _count;

	public void Add<T>(LuiCompType key, T value) where T : LuiCompBase
	{
		if (_count >= _values.Length)
			throw new InvalidOperationException("Dictionary is full");

		_values[_count] = value;
		_count++;
	}

	public void Clear()
	{
		_count = 0;
	}

	public bool TryGetValue<T>(LuiCompType key, out T value) where T : LuiCompBase
	{
		for (int i = 0; i < _count; i++)
		{
			if (_values[i].type == key && _values[i] is T typedValue)
			{
				value = typedValue;
				return true;
			}
		}
		value = null;
		return false;
	}

	public IEnumerator GetEnumerator()
	{
		for (int i = 0; i < _count; i++)
		{
			yield return _values[i];
		}
	}
}

public readonly struct LuiOffset
{
	public static readonly LuiOffset None = new(0, 0, 0, 0);

	public readonly Vector2 offsetMin;
	public readonly Vector2 offsetMax;

	public LuiOffset(float xMin, float yMin, float xMax, float yMax)
	{
		offsetMin = new Vector2(xMin, yMin);
		offsetMax = new Vector2(xMax, yMax);
	}

	public static bool operator ==(LuiOffset a, LuiOffset b)
	{
		return a.offsetMax == b.offsetMax && a.offsetMin == b.offsetMin;
	}

	public static bool operator !=(LuiOffset a, LuiOffset b)
	{
		return a.offsetMax != b.offsetMax || a.offsetMin != b.offsetMin;
	}

	public override bool Equals(object obj)
	{
		return obj is LuiOffset other && Equals(other);
	}

	private bool Equals(LuiOffset other)
	{
		return offsetMin.Equals(other.offsetMin) && offsetMax.Equals(other.offsetMax);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + offsetMin.GetHashCode();
			hash = hash * 31 + offsetMax.GetHashCode();
			return hash;
		}
	}
}

public readonly struct LuiPosition
{
	public static readonly LuiPosition None = new(0, 0, 0, 0);
	public static readonly LuiPosition Full = new(0, 0, 1, 1);
	public static readonly LuiPosition UpperLeft = new(0, 1, 0, 1);
	public static readonly LuiPosition UpperCenter = new(.5f, 1, .5f, 1);
	public static readonly LuiPosition UpperRight = new(1, 1, 1, 1);
	public static readonly LuiPosition MiddleLeft = new(0, .5f, 0, .5f);
	public static readonly LuiPosition MiddleCenter = new(.5f, .5f, .5f, .5f);
	public static readonly LuiPosition MiddleRight = new(1, .5f, 1, .5f);
	public static readonly LuiPosition LowerLeft = new(0, 0, 0, 0);
	public static readonly LuiPosition LowerCenter = new(.5f, 0, .5f, 0);
	public static readonly LuiPosition LowerRight = new(1, 0, 1, 0);

	public readonly Vector2 anchorMin;
	public readonly Vector2 anchorMax;

	public LuiPosition(float xMin, float yMin, float xMax, float yMax)
	{
		anchorMin = new Vector2(xMin, yMin);
		anchorMax = new Vector2(xMax, yMax);
	}

	public static bool operator ==(LuiPosition a, LuiPosition b)
	{
		return a.anchorMax == b.anchorMax && a.anchorMin == b.anchorMin;
	}

	public static bool operator !=(LuiPosition a, LuiPosition b)
	{
		return a.anchorMax != b.anchorMax || a.anchorMin != b.anchorMin;
	}

	public override bool Equals(object obj)
	{
		return obj is LuiPosition other && Equals(other);
	}

	private bool Equals(LuiPosition other)
	{
		return anchorMax.Equals(other.anchorMax) && anchorMin.Equals(other.anchorMin);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + anchorMax.GetHashCode();
			hash = hash * 31 + anchorMin.GetHashCode();
			return hash;
		}
	}
}

public enum LuiCompType
{
	Text,
	Image,
	RawImage,
	Button,
	Outline,
	InputField,
	NeedsCursor,
	RectTransform,
	Countdown,
	HorizontalLayoutGroup,
	VerticalLayoutGroup,
	GridLayoutGroup,
	ContentSizeFitter,
	LayoutElement,
	Draggable,
	Slot,
	NeedsKeyboard,
	ScrollView,
}

public class LuiCompBase
{
	public LuiCompType type;
	public bool enabled = true;
	public float fadeIn; //Present in like 80% of elements but to reduce method list, adding it here.
	public string placeholderParentId; //Present in like 80% of elements but to reduce method list, adding it here.
}

public class LuiTextComp : LuiCompBase
{
	public string text;
	public int fontSize;
	public string font;
	public string align;
	public string color;
	public string verticalOverflow;

	public LuiTextComp()
	{
		type = LuiCompType.Text;
	}
}

public class LuiImageComp : LuiCompBase
{
	public string sprite;
	public string material;
	public string color;
	public string imageType;
	public bool fillCenter;
	public string png;
	public string slice;
	public int itemid;
	public ulong skinid;

	public LuiImageComp()
	{
		type = LuiCompType.Image;
	}
}

public class LuiRawImageComp : LuiCompBase
{
	public string sprite;
	public string color;
	public string material;
	public string url;
	public string png;
	public string steamid;

	public LuiRawImageComp()
	{
		type = LuiCompType.RawImage;
	}
}

public class LuiButtonComp : LuiCompBase
{
	public string command;
	public string close;
	public string sprite;
	public string material;
	public string color;
	public string imageType;
	public string normalColor;
	public string highlightedColor;
	public string pressedColor;
	public string selectedColor;
	public string disabledColor;
	public float colorMultiplier = -1;
	public float fadeDuration = -1;

	public LuiButtonComp()
	{
		type = LuiCompType.Button;
	}
}

public class LuiOutlineComp : LuiCompBase
{
	public string color;
	public Vector2 distance;
	public bool useGraphicAlpha;

	public LuiOutlineComp()
	{
		type = LuiCompType.Outline;
	}
}

public class LuiInputComp : LuiCompBase
{
	public int fontSize;
	public string font;
	public string align;
	public string color;
	public int characterLimit;
	public string command;
	public string lineType;
	public string text;
	public bool readOnly;
	public string placeholderId;
	public bool password;
	public bool needsKeyboard;
	public bool hudMenuInput;
	public bool autofocus;

	public LuiInputComp()
	{
		type = LuiCompType.InputField;
	}
}

public class LuiCursorComp : LuiCompBase
{
	public LuiCursorComp()
	{
		type = LuiCompType.NeedsCursor;
	}
}

public class LuiRectTransformComp : LuiCompBase
{
	public LuiPosition anchor = LuiPosition.Full;
	public LuiOffset offset = LuiOffset.None;
	public float rotation;
	public string setParent;
	public int setTransformIndex = -1;

	public LuiRectTransformComp()
	{
		type = LuiCompType.RectTransform;
	}
}

public class LuiCountdownComp : LuiCompBase
{
	public float endTime = -1;
	public float startTime = -1;
	public float step;
	public float interval;
	public string timerFormat;
	public string numberFormat;
	public bool destroyIfDone = true;
	public string command;

	public LuiCountdownComp()
	{
		type = LuiCompType.Countdown;
	}
}

public class LuiHorizontalLayoutGroupComp : LuiCompBase
{
	public float spacing;
	public string childAlignment;
	public bool childForceExpandWidth = true;
	public bool childForceExpandHeight = true;
	public bool childControlWidth;
	public bool childControlHeight;
	public bool childScaleWidth;
	public bool childScaleHeight;
	public string padding;

	public LuiHorizontalLayoutGroupComp()
	{
		type = LuiCompType.HorizontalLayoutGroup;
	}
}

public class LuiVerticalLayoutGroupComp : LuiCompBase
{
	public float spacing;
	public string childAlignment;
	public bool childForceExpandWidth = true;
	public bool childForceExpandHeight = true;
	public bool childControlWidth;
	public bool childControlHeight;
	public bool childScaleWidth;
	public bool childScaleHeight;
	public string padding;

	public LuiVerticalLayoutGroupComp()
	{
		type = LuiCompType.VerticalLayoutGroup;
	}
}

public class LuiGridLayoutGroupComp : LuiCompBase
{
	public Vector2 cellSize = new Vector2(100, 100);
	public Vector2 spacing;
	public string startCorner;
	public string startAxis;
	public string childAlignment;
	public string constraint;
	public int constraintCount;
	public string padding;

	public LuiGridLayoutGroupComp()
	{
		type = LuiCompType.GridLayoutGroup;
	}
}

public class LuiContentSizeFitterComp : LuiCompBase
{
	public string horizontalFit;
	public string verticalFit;

	public LuiContentSizeFitterComp()
	{
		type = LuiCompType.ContentSizeFitter;
	}
}

public class LuiLayoutElementComp : LuiCompBase
{
	public float preferredWidth = -1f;
	public float preferredHeight = -1f;
	public float minWidth;
	public float minHeight;
	public float flexibleWidth;
	public float flexibleHeight;
	public bool ignoreLayout;

	public LuiLayoutElementComp()
	{
		type = LuiCompType.LayoutElement;
	}
}

public class LuiDraggableComp : LuiCompBase
{
	public bool limitToParent;
	public float maxDistance;
	public bool allowSwapping;
	public bool dropAnywhere = true;
	public float dragAlpha = -1;
	public int parentLimitIndex = -1;
	public string filter;
	public Vector2 parentPadding;
	public Vector2 anchorOffset;
	public bool keepOnTop;
	public string positionRPC;
	public bool moveToAnchor;
	public bool rebuildAnchor;

	public LuiDraggableComp()
	{
		type = LuiCompType.Draggable;
	}
}

public class LuiSlotComp : LuiCompBase
{
	public string filter;

	public LuiSlotComp()
	{
		type = LuiCompType.Slot;
	}
}

public class LuiKeyboardComp : LuiCompBase
{
	public LuiKeyboardComp()
	{
		type = LuiCompType.NeedsKeyboard;
	}
}

public class LuiScrollComp : LuiCompBase
{
	public LuiPosition anchor = LuiPosition.Full;
	public LuiOffset offset = LuiOffset.None;
	public Vector2 pivot = new Vector2(0.5f, 0.5f);
	public bool horizontal;
	public bool vertical;
	public string movementType;
	public float elasticity = -1;
	public bool inertia;
	public float decelerationRate = -1;
	public float scrollSensitivity = -1;
	public LuiScrollbar horizontalScrollbar;
	public LuiScrollbar verticalScrollbar;
	public float horizontalNormalizedPosition;
	public float verticalNormalizedPosition;


	public LuiScrollComp()
	{
		type = LuiCompType.ScrollView;
	}
}

public struct LuiScrollbar
{
	public bool disabled; //reverse of enabled
	public bool invert;
	public bool autoHide;
	public string handleSprite;
	public float size;
	public string handleColor;
	public string highlightColor;
	public string pressedColor;
	public string trackSprite;
	public string trackColor;
}
}
