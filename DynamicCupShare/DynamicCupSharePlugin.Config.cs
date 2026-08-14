using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DynamicCupShareHarmony
{
    public partial class DynamicCupSharePlugin
    {
        internal class ConfigData
        {
            [JsonProperty(PropertyName = "Sharing Options")]
            public ShareDefaults Sharing { get; set; }

            [JsonProperty(PropertyName = "Permission Options")]
            public Permissions Permission { get; set; }

            [JsonProperty(PropertyName = "Turret Share Options")]
            public TurretTargeting Turrets { get; set; }

            [JsonProperty(PropertyName = "Building Restrictions")]
            public BuildBlocker Building { get; set; }

            [JsonProperty(PropertyName = "Security Options")]
            public SecurityOptions Security { get; set; }

            [JsonProperty(PropertyName = "Data Management")]
            public DataManagement Data { get; set; }

            [JsonProperty(PropertyName = "UI Colors")]
            public UIColors Colors { get; set; }

            [JsonProperty(PropertyName = "Blueprint Share Options")]
            public BlueprintShareOptions Blueprints { get; set; }

            [JsonProperty(PropertyName = "Building Workbench Options")]
            public BuildingWorkbenchOptions BuildingWorkbench { get; set; }

            public class ShareDefaults
            {
                [JsonProperty(PropertyName = "Allowed share types")]
                public AllowedShares Allowed { get; set; }

                public ClanDefaults Clan { get; set; }
                public Defaults Friend { get; set; }
                public Defaults Team { get; set; }

                [JsonProperty(PropertyName = "Chat command")]
                public string ChatCommand { get; set; }

                [JsonProperty(PropertyName = "Disable key lock sharing")]
                public bool DisableKeylocks { get; set; }

                public class AllowedShares
                {
                    [JsonProperty(PropertyName = "Allow cupboard sharing")]
                    public bool Cupboards { get; set; }
                    [JsonProperty(PropertyName = "Allow door sharing")]
                    public bool Doors { get; set; }
                    [JsonProperty(PropertyName = "Allow box sharing")]
                    public bool Boxes { get; set; }
                    [JsonProperty(PropertyName = "Allow locker sharing")]
                    public bool Lockers { get; set; }
                    [JsonProperty(PropertyName = "Allow turret sharing")]
                    public bool Turrets { get; set; }
                    [JsonProperty(PropertyName = "Allow composter sharing (also enables locks to be placed on composters)")]
                    public bool Composters { get; set; }
                    [JsonProperty(PropertyName = "Allow dropbox sharing (also enables locks to be placed on dropboxes)")]
                    public bool DropBoxes { get; set; }
                    [JsonProperty(PropertyName = "Allow vending machine sharing (also enables locks to be placed on vending machines)")]
                    public bool VendingMachines { get; set; }
                    [JsonProperty(PropertyName = "Allow furnace sharing (also enables locks to be placed on furnaces)")]
                    public bool Furnace { get; set; }
                    [JsonProperty(PropertyName = "Allow bbq sharing (also enables locks to be placed on bbqs)")]
                    public bool Bbq { get; set; }
                    [JsonProperty(PropertyName = "Allow refinery sharing (also enables locks to be placed on refinery)")]
                    public bool Refinery { get; set; }
                    [JsonProperty(PropertyName = "Allow planter sharing (also enables locks to be placed on planters)")]
                    public bool Planters { get; set; }
                    [JsonProperty(PropertyName = "Allow hitch and trough sharing (also enables locks to be placed on hitch and troughs)")]
                    public bool Hitch { get; set; }
                    [JsonProperty(PropertyName = "Allow mixing table sharing (also enables locks to be placed on mixing table)")]
                    public bool MixingTable { get; set; }
                    [JsonProperty(PropertyName = "Allow chicken coop sharing (also enables locks to be placed on chicken coops)")]
                    public bool ChickenCoop { get; set; }
                    [JsonProperty(PropertyName = "Allow beehive sharing (also enables locks to be placed on beehives)")]
                    public bool Beehive { get; set; }
                    [JsonProperty(PropertyName = "Allow blueprint sharing")]
                    public bool Blueprints { get; set; }

                    [JsonIgnore]
                    public List<ShareType> AllowedShareTypes
                    {
                        get
                        {
                            List<ShareType> list = new List<ShareType>();
                            if (Boxes) list.Add(ShareType.Box);
                            if (Cupboards) list.Add(ShareType.Cupboard);
                            if (Doors) list.Add(ShareType.Door);
                            if (Lockers) list.Add(ShareType.Locker);
                            if (Turrets) list.Add(ShareType.Turret);
                            if (Furnace) list.Add(ShareType.Furnace);
                            if (Refinery) list.Add(ShareType.Refinery);
                            if (Bbq) list.Add(ShareType.Bbq);
                            if (Composters) list.Add(ShareType.Composter);
                            if (Planters) list.Add(ShareType.Planters);
                            if (DropBoxes) list.Add(ShareType.Dropbox);
                            if (VendingMachines) list.Add(ShareType.VendingMachine);
                            if (Hitch) list.Add(ShareType.Hitch);
                            if (MixingTable) list.Add(ShareType.MixingTable);
                            if (ChickenCoop) list.Add(ShareType.ChickenCoop);
                            if (Beehive) list.Add(ShareType.Beehive);
                            if (Blueprints) list.Add(ShareType.Blueprint);
                            return list;
                        }
                    }
                }

                public class ClanDefaults : Defaults
                {
                    [JsonProperty(PropertyName = "Clan sharing includes alliances?")]
                    public bool Alliances { get; set; }
                }

                public class Defaults
                {
                    [JsonProperty(PropertyName = "Is this share type allowed?")]
                    public bool Enabled { get; set; }
                    [JsonProperty(PropertyName = "Enable cupboard sharing by default")]
                    public bool Cupboards { get; set; }
                    [JsonProperty(PropertyName = "Enable door sharing by default")]
                    public bool Doors { get; set; }
                    [JsonProperty(PropertyName = "Enable box sharing by default")]
                    public bool Boxes { get; set; }
                    [JsonProperty(PropertyName = "Enable locker sharing by default")]
                    public bool Lockers { get; set; }
                    [JsonProperty(PropertyName = "Enable turret sharing by default")]
                    public bool Turrets { get; set; }
                    [JsonProperty(PropertyName = "Enable furnace sharing by default")]
                    public bool Furnace { get; set; }
                    [JsonProperty(PropertyName = "Enable refinery sharing by default")]
                    public bool Refinery { get; set; }
                    [JsonProperty(PropertyName = "Enable bbq sharing by default")]
                    public bool Bbq { get; set; }
                    [JsonProperty(PropertyName = "Enable composter sharing by default")]
                    public bool Composters { get; set; }
                    [JsonProperty(PropertyName = "Enable planter sharing by default")]
                    public bool Planters { get; set; }
                    [JsonProperty(PropertyName = "Enable dropbox sharing by default")]
                    public bool DropBoxes { get; set; }
                    [JsonProperty(PropertyName = "Enable vending machine sharing by default")]
                    public bool VendingMachines { get; set; }
                    [JsonProperty(PropertyName = "Enable hitch and trough sharing by default")]
                    public bool Hitch { get; set; }
                    [JsonProperty(PropertyName = "Enable mixing table sharing by default")]
                    public bool MixingTable { get; set; }
                    [JsonProperty(PropertyName = "Enable chicken coop sharing by default")]
                    public bool ChickenCoop { get; set; }
                    [JsonProperty(PropertyName = "Enable beehive sharing by default")]
                    public bool Beehive { get; set; }
                    [JsonProperty(PropertyName = "Enable blueprint sharing by default")]
                    public bool Blueprints { get; set; }

                    [JsonIgnore]
                    public ShareType ShareType
                    {
                        get
                        {
                            ShareType type = ShareType.None;
                            if (Cupboards && Configuration.Sharing.Allowed.Cupboards) type |= ShareType.Cupboard;
                            if (Doors && Configuration.Sharing.Allowed.Doors) type |= ShareType.Door;
                            if (Boxes && Configuration.Sharing.Allowed.Boxes) type |= ShareType.Box;
                            if (Lockers && Configuration.Sharing.Allowed.Lockers) type |= ShareType.Locker;
                            if ((Turrets && Configuration.Sharing.Allowed.Turrets) || Configuration.Security.TurretShareOverride) type |= ShareType.Turret;
                            if (Composters && Configuration.Sharing.Allowed.Composters) type |= ShareType.Composter;
                            if (DropBoxes && Configuration.Sharing.Allowed.DropBoxes) type |= ShareType.Dropbox;
                            if (VendingMachines && Configuration.Sharing.Allowed.VendingMachines) type |= ShareType.VendingMachine;
                            if (Furnace && Configuration.Sharing.Allowed.Furnace) type |= ShareType.Furnace;
                            if (Refinery && Configuration.Sharing.Allowed.Refinery) type |= ShareType.Refinery;
                            if (Bbq && Configuration.Sharing.Allowed.Bbq) type |= ShareType.Bbq;
                            if (Planters && Configuration.Sharing.Allowed.Planters) type |= ShareType.Planters;
                            if (Hitch && Configuration.Sharing.Allowed.Hitch) type |= ShareType.Hitch;
                            if (MixingTable && Configuration.Sharing.Allowed.MixingTable) type |= ShareType.MixingTable;
                            if (ChickenCoop && Configuration.Sharing.Allowed.ChickenCoop) type |= ShareType.ChickenCoop;
                            if (Beehive && Configuration.Sharing.Allowed.Beehive) type |= ShareType.Beehive;
                            if (Blueprints && Configuration.Sharing.Allowed.Blueprints) type |= ShareType.Blueprint;
                            return type;
                        }
                    }
                }
            }

            public class Permissions
            {
                [JsonProperty(PropertyName = "Clan Share Permission (if enabled, players will need this permission to use Clan share)")]
                public TogglablePermission ClanShare { get; set; }
                [JsonProperty(PropertyName = "Friend Share Permission (if enabled, players will need this permission to use Friend share)")]
                public TogglablePermission FriendShare { get; set; }
                [JsonProperty(PropertyName = "Team Share Permission (if enabled, players will need this permission to use Team share)")]
                public TogglablePermission TeamShare { get; set; }
                [JsonProperty(PropertyName = "Admin Permission (required to toggle admin mode)")]
                public string AdminPermission { get; set; }
                [JsonProperty(PropertyName = "Blueprint share permission (required to auto-share studied/tech-tree blueprints)")]
                public string BlueprintUse { get; set; }
                [JsonProperty(PropertyName = "Blueprint toggle permission (/bs toggle)")]
                public string BlueprintToggle { get; set; }
                [JsonProperty(PropertyName = "Blueprint manual share permission (/bs share)")]
                public string BlueprintShare { get; set; }
                [JsonProperty(PropertyName = "Blueprint show permission (/bs show)")]
                public string BlueprintShow { get; set; }
                [JsonProperty(PropertyName = "Blueprint bypass permission (share with any player via /bs share)")]
                public string BlueprintBypass { get; set; }
                [JsonProperty(PropertyName = "Building workbench permission (required to use workbenches anywhere in an authorized building)")]
                public string BuildingWorkbenchUse { get; set; }
                [JsonProperty(PropertyName = "Building workbench cancel-craft permission")]
                public string BuildingWorkbenchCancelCraft { get; set; }
                [JsonProperty(PropertyName = "Toggle admin mode when player connects (requires the admin permission)")]
                public bool ToggleAdminPermissionOnJoin { get; set; }

                public struct TogglablePermission
                {
                    public string Permission { get; set; }
                    public bool Enabled { get; set; }

                    public TogglablePermission(string permission, bool enabled)
                    {
                        Permission = permission;
                        Enabled = enabled;
                    }
                }
            }

            public class TurretTargeting
            {
                [JsonProperty(PropertyName = "Turret share includes gun traps")]
                public bool IncludeGunTraps { get; set; }
                [JsonProperty(PropertyName = "Turret share includes flame turrets")]
                public bool IncludeFlameTurrets { get; set; }
                [JsonProperty(PropertyName = "Turret share includes sam sites")]
                public bool IncludeSameSites { get; set; }
            }

            public class DataManagement
            {
                [JsonProperty(PropertyName = "Save data in ProtoBuf format")]
                public bool UseProtoStorage { get; set; }
                [JsonProperty(PropertyName = "Purge user data after X days of inactivity (0 is disabled)")]
                public int PurgeAfter { get; set; }
            }

            public class BuildBlocker
            {
                [JsonProperty(PropertyName = "Prevent building on icebergs")]
                public bool PreventIceberg { get; set; }
                [JsonProperty(PropertyName = "Prevent building on ice sheets")]
                public bool PreventIcesheet { get; set; }
                [JsonProperty(PropertyName = "Prevent building on ice lakes")]
                public bool PreventIcelake { get; set; }
            }

            public class SecurityOptions
            {
                [JsonProperty(PropertyName = "Permanently enable turret sharing between clan and team members")]
                public bool TurretShareOverride { get; set; }
                [JsonProperty(PropertyName = "Prevent friendly players from accessing cupboard and turret auth lists")]
                public bool BlockAuth { get; set; }
                [JsonProperty(PropertyName = "Prevent non-friendly players from accessing cupboard and turret auth lists")]
                public bool BlockNonAuth { get; set; }
                [JsonProperty(PropertyName = "Prevent cupboard and turret sharing if owner is not in the authorized list of that entity")]
                public bool PreventShareNoOwner { get; set; }
                [JsonProperty(PropertyName = "Maximum allowed authorizations on a tool cupboard (0 = disabled)")]
                public int MaxCupboardAuth { get; set; }
                [JsonProperty(PropertyName = "Allow friendly players to lock/unlock shared locks")]
                public bool ShareLockUnlock { get; set; }
            }

            public class BlueprintShareOptions
            {
                [JsonProperty(PropertyName = "Share blueprint items")]
                public bool ItemSharingEnabled { get; set; } = true;

                [JsonProperty(PropertyName = "Share tech tree blueprints")]
                public bool TechTreeSharingEnabled { get; set; } = true;

                [JsonProperty(PropertyName = "Share blueprints to existing members on join")]
                public bool ShareToExistingMembers { get; set; }

                [JsonProperty(PropertyName = "Share blueprints to new members on join")]
                public bool ShareToNewMembers { get; set; }

                [JsonProperty(PropertyName = "Lose shared blueprints on leave")]
                public bool LoseBlueprintsOnLeave { get; set; }

                [JsonProperty(PropertyName = "Clear blueprint share data on wipe")]
                public bool ClearDataOnWipe { get; set; } = true;

                [JsonProperty(PropertyName = "Receive messages enabled")]
                public bool ReceiveMessagesEnabled { get; set; } = true;

                [JsonProperty(PropertyName = "Share messages enabled")]
                public bool ShareMessagesEnabled { get; set; } = true;

                [JsonProperty(PropertyName = "Items blocked from sharing")]
                public HashSet<string> BlockedItems { get; set; } = new HashSet<string>();

                [JsonProperty(PropertyName = "Chat command")]
                public string ChatCommand { get; set; } = "bs";

                [JsonProperty(PropertyName = "Debug mode")]
                public bool Debug { get; set; }
            }

            public class BuildingWorkbenchOptions
            {
                [JsonProperty(PropertyName = "Enabled")]
                public bool Enabled { get; set; } = true;

                [JsonProperty(PropertyName = "Display workbench built notification")]
                public bool BuiltNotification { get; set; } = true;

                [JsonProperty(PropertyName = "Display cancel craft notification")]
                public bool CancelCraftNotification { get; set; } = true;

                [JsonProperty(PropertyName = "Inside building check frequency (Seconds)")]
                public float UpdateRate { get; set; } = 3f;

                [JsonProperty(PropertyName = "Enable Fast Building Check (Only checks above and below a player)")]
                public bool FastBuildingCheck { get; set; }

                [JsonProperty(PropertyName = "Enable Boat Check")]
                public bool EnableBoatCheck { get; set; } = true;

                [JsonProperty(PropertyName = "Distance from base to be considered inside building (Meters)")]
                public float BaseDistance { get; set; } = 16f;

                [JsonProperty(PropertyName = "Required distance from last update (Meters)")]
                public float RequiredDistance { get; set; } = 5f;
            }

            public class UIColors
            {
                public Color Background { get; set; }
                public Color Panel { get; set; }
                public Color Button { get; set; }
                public Color Highlight { get; set; }
                public Color Close { get; set; }

                public class Color
                {
                    public string Hex { get; set; }
                    public float Alpha { get; set; }
                }
            }
        }

        private void LoadConfig()
        {
            string path = DynamicCupShareHost.Instance.ConfigPath;
            try
            {
                if (File.Exists(path))
                    Configuration = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] Config load failed: " + ex.Message);
            }

            if (Configuration == null)
            {
                Configuration = GenerateDefaultConfiguration();
                SaveConfig();
            }
            else
            {
                ConfigData defaults = GenerateDefaultConfiguration();
                if (Configuration.Sharing == null) Configuration.Sharing = defaults.Sharing;
                if (Configuration.Permission == null) Configuration.Permission = defaults.Permission;
                if (Configuration.Turrets == null) Configuration.Turrets = defaults.Turrets;
                if (Configuration.Building == null) Configuration.Building = defaults.Building;
                if (Configuration.Security == null) Configuration.Security = defaults.Security;
                if (Configuration.Data == null) Configuration.Data = defaults.Data;
                if (Configuration.Colors == null) Configuration.Colors = defaults.Colors;
                if (Configuration.Blueprints == null)
                {
                    Configuration.Blueprints = defaults.Blueprints;
                    Configuration.Sharing.Allowed.Blueprints = true;
                    if (Configuration.Sharing.Clan != null) Configuration.Sharing.Clan.Blueprints = true;
                    if (Configuration.Sharing.Friend != null) Configuration.Sharing.Friend.Blueprints = true;
                    if (Configuration.Sharing.Team != null) Configuration.Sharing.Team.Blueprints = true;
                    _seedBlueprintFlags = true;
                    SaveConfig();
                }
                if (Configuration.BuildingWorkbench == null)
                {
                    Configuration.BuildingWorkbench = defaults.BuildingWorkbench;
                    SaveConfig();
                }
                if (string.IsNullOrEmpty(Configuration.Permission.AdminPermission))
                    Configuration.Permission.AdminPermission = defaults.Permission.AdminPermission;
                if (string.IsNullOrEmpty(Configuration.Permission.BlueprintUse))
                    Configuration.Permission.BlueprintUse = defaults.Permission.BlueprintUse;
                if (string.IsNullOrEmpty(Configuration.Permission.BlueprintToggle))
                    Configuration.Permission.BlueprintToggle = defaults.Permission.BlueprintToggle;
                if (string.IsNullOrEmpty(Configuration.Permission.BlueprintShare))
                    Configuration.Permission.BlueprintShare = defaults.Permission.BlueprintShare;
                if (string.IsNullOrEmpty(Configuration.Permission.BlueprintShow))
                    Configuration.Permission.BlueprintShow = defaults.Permission.BlueprintShow;
                if (string.IsNullOrEmpty(Configuration.Permission.BlueprintBypass))
                    Configuration.Permission.BlueprintBypass = defaults.Permission.BlueprintBypass;
                if (string.IsNullOrEmpty(Configuration.Permission.BuildingWorkbenchUse))
                    Configuration.Permission.BuildingWorkbenchUse = defaults.Permission.BuildingWorkbenchUse;
                if (string.IsNullOrEmpty(Configuration.Permission.BuildingWorkbenchCancelCraft))
                    Configuration.Permission.BuildingWorkbenchCancelCraft = defaults.Permission.BuildingWorkbenchCancelCraft;
                if (string.IsNullOrEmpty(Configuration.Sharing.ChatCommand))
                    Configuration.Sharing.ChatCommand = defaults.Sharing.ChatCommand;
            }
        }

        private void SaveConfig()
        {
            if (Configuration == null) return;
            try
            {
                string path = DynamicCupShareHost.Instance.ConfigPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(Configuration, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] Config save failed: " + ex.Message);
            }
        }

        private static ConfigData GenerateDefaultConfiguration()
        {
            return new ConfigData
            {
                Permission = new ConfigData.Permissions
                {
                    ClanShare = new ConfigData.Permissions.TogglablePermission("dynamiccupshare.canclanshare", false),
                    FriendShare = new ConfigData.Permissions.TogglablePermission("dynamiccupshare.canfriendshare", false),
                    TeamShare = new ConfigData.Permissions.TogglablePermission("dynamiccupshare.canteamshare", false),
                    AdminPermission = "dynamiccupshare.adminmode",
                    BlueprintUse = "blueprintshare.use",
                    BlueprintToggle = "blueprintshare.toggle",
                    BlueprintShare = "blueprintshare.share",
                    BlueprintShow = "blueprintshare.show",
                    BlueprintBypass = "blueprintshare.bypass",
                    BuildingWorkbenchUse = "buildingworkbench.use",
                    BuildingWorkbenchCancelCraft = "buildingworkbench.cancelcraft",
                },
                Sharing = new ConfigData.ShareDefaults
                {
                    Allowed = new ConfigData.ShareDefaults.AllowedShares
                    {
                        Boxes = true,
                        Cupboards = true,
                        Doors = true,
                        Lockers = true,
                        Turrets = true,
                        Blueprints = true,
                    },
                    Clan = new ConfigData.ShareDefaults.ClanDefaults
                    {
                        Enabled = true,
                        Boxes = true,
                        Cupboards = true,
                        Doors = true,
                        Lockers = true,
                        Turrets = true,
                        Blueprints = true,
                        Alliances = false
                    },
                    Friend = new ConfigData.ShareDefaults.Defaults
                    {
                        Enabled = false,
                        Boxes = true,
                        Cupboards = true,
                        Doors = true,
                        Lockers = true,
                        Turrets = true,
                        Blueprints = true
                    },
                    Team = new ConfigData.ShareDefaults.Defaults
                    {
                        Enabled = true,
                        Boxes = true,
                        Cupboards = true,
                        Doors = true,
                        Lockers = true,
                        Turrets = true,
                        Blueprints = true
                    },
                    ChatCommand = "share",
                    DisableKeylocks = false
                },
                Building = new ConfigData.BuildBlocker(),
                Turrets = new ConfigData.TurretTargeting
                {
                    IncludeFlameTurrets = true,
                    IncludeGunTraps = true,
                    IncludeSameSites = true
                },
                Security = new ConfigData.SecurityOptions
                {
                    BlockAuth = true,
                    BlockNonAuth = false,
                    TurretShareOverride = false,
                    MaxCupboardAuth = 0,
                    PreventShareNoOwner = true
                },
                Colors = new ConfigData.UIColors
                {
                    Background = new ConfigData.UIColors.Color { Hex = "151515", Alpha = 0.94f },
                    Panel = new ConfigData.UIColors.Color { Hex = "FFFFFF", Alpha = 0.165f },
                    Button = new ConfigData.UIColors.Color { Hex = "2A2E32", Alpha = 1f },
                    Highlight = new ConfigData.UIColors.Color { Hex = "C4FF00", Alpha = 1f },
                    Close = new ConfigData.UIColors.Color { Hex = "CE422B", Alpha = 1f }
                },
                Data = new ConfigData.DataManagement
                {
                    UseProtoStorage = false,
                    PurgeAfter = 7
                },
                Blueprints = new ConfigData.BlueprintShareOptions(),
                BuildingWorkbench = new ConfigData.BuildingWorkbenchOptions()
            };
        }

        internal class StoredData
        {
            public Hash<ulong, PlayerData> playerData = new Hash<ulong, PlayerData>();
            public int timeSaved;

            internal PlayerData SetupPlayer(ulong playerId)
            {
                if (!playerId.IsSteamId())
                    return null;

                if (!playerData.TryGetValue(playerId, out PlayerData data))
                    playerData[playerId] = data = new PlayerData(Configuration.Sharing.Clan.ShareType, Configuration.Sharing.Friend.ShareType, Configuration.Sharing.Team.ShareType);

                return data;
            }

            internal PlayerData FindPlayerData(ulong playerId)
            {
                if (playerData.TryGetValue(playerId, out PlayerData data))
                    return data;
                return null;
            }

            public class PlayerData
            {
                public ShareType clan = ShareType.None;
                public ShareType friend = ShareType.None;
                public ShareType team = ShareType.None;
                public int lastOnline;
                public BlueprintLearnData learntBlueprints;
                public bool? buildingWorkbenchEnabled;

                public PlayerData() { }

                public PlayerData(ShareType clans, ShareType friends, ShareType teams)
                {
                    clan = clans;
                    friend = friends;
                    team = teams;
                    learntBlueprints = new BlueprintLearnData();
                    buildingWorkbenchEnabled = true;
                }

                internal bool BuildingWorkbenchEnabled => buildingWorkbenchEnabled != false;

                internal BlueprintLearnData Blueprints
                {
                    get
                    {
                        if (learntBlueprints == null)
                            learntBlueprints = new BlueprintLearnData();
                        return learntBlueprints;
                    }
                }

                internal bool IsSharing(TeamType type, ShareType share)
                {
                    switch (type)
                    {
                        case TeamType.Clan: return (clan & share) == share;
                        case TeamType.Friend: return (friend & share) == share;
                        case TeamType.Team: return (team & share) == share;
                    }
                    return false;
                }

                internal void Share(TeamType type, ShareType share)
                {
                    switch (type)
                    {
                        case TeamType.Clan: clan |= share; return;
                        case TeamType.Friend: friend |= share; return;
                        case TeamType.Team: team |= share; return;
                    }
                }

                internal void Unshare(TeamType type, ShareType share)
                {
                    switch (type)
                    {
                        case TeamType.Clan: clan &= ~share; return;
                        case TeamType.Friend: friend &= ~share; return;
                        case TeamType.Team: team &= ~share; return;
                    }
                }
            }
        }

        internal class BlueprintLearnData
        {
            public List<int> Team = new List<int>();
            public List<int> Clan = new List<int>();
            public Dictionary<string, List<int>> Friends = new Dictionary<string, List<int>>();

            public HashSet<int> TeamSet() => Team != null ? new HashSet<int>(Team) : new HashSet<int>();
            public HashSet<int> ClanSet() => Clan != null ? new HashSet<int>(Clan) : new HashSet<int>();

            public HashSet<int> FriendSet(string friendId)
            {
                if (Friends == null)
                    Friends = new Dictionary<string, List<int>>();
                if (!Friends.TryGetValue(friendId, out List<int> list) || list == null)
                {
                    list = new List<int>();
                    Friends[friendId] = list;
                }
                return new HashSet<int>(list);
            }

            public List<int> FriendList(string friendId)
            {
                if (Friends == null)
                    Friends = new Dictionary<string, List<int>>();
                if (!Friends.TryGetValue(friendId, out List<int> list) || list == null)
                {
                    list = new List<int>();
                    Friends[friendId] = list;
                }
                return list;
            }
        }

        internal class TemporaryShareData
        {
            public Hash<ulong, List<ulong>> temporaryCupboardShares = new Hash<ulong, List<ulong>>();
            public Hash<ulong, List<ulong>> temporaryTurretShares = new Hash<ulong, List<ulong>>();
            public Hash<ulong, List<ulong>> temporaryCodeLockShare = new Hash<ulong, List<ulong>>();
        }

        private void SaveData()
        {
            if (storedData == null) return;
            storedData.timeSaved = UnixTimeStampUtc();
            try
            {
                string path = DynamicCupShareHost.Instance.DataPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(storedData, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] Data save failed: " + ex.Message);
            }
        }

        private void LoadData()
        {
            try
            {
                string path = DynamicCupShareHost.Instance.DataPath;
                if (File.Exists(path))
                    storedData = JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] Data load failed: " + ex.Message);
            }

            if (storedData?.playerData == null)
                storedData = new StoredData();

            SeedBlueprintShareFlags();
            TryMigrateOxideBlueprintShare();
        }

        private void SeedBlueprintShareFlags()
        {
            if (!_seedBlueprintFlags || storedData?.playerData == null) return;
            _seedBlueprintFlags = false;

            foreach (var kv in storedData.playerData)
            {
                StoredData.PlayerData data = kv.Value;
                if (data == null) continue;
                if (Configuration.Sharing.Clan != null && Configuration.Sharing.Clan.Blueprints)
                    data.Share(TeamType.Clan, ShareType.Blueprint);
                if (Configuration.Sharing.Friend != null && Configuration.Sharing.Friend.Blueprints)
                    data.Share(TeamType.Friend, ShareType.Blueprint);
                if (Configuration.Sharing.Team != null && Configuration.Sharing.Team.Blueprints)
                    data.Share(TeamType.Team, ShareType.Blueprint);
            }

            SaveData();
        }

        private void TryMigrateOxideBlueprintShare()
        {
            string hostRoot = DynamicCupShareHost.Instance?.ServerRoot;
            if (string.IsNullOrEmpty(hostRoot)) return;

            string marker = Path.Combine(DynamicCupShareHost.Instance.DataDirectory, "blueprintshare_migrated.txt");
            if (File.Exists(marker)) return;

            string oxidePath = Path.Combine(hostRoot, "oxide", "data", "BlueprintShare.json");
            if (!File.Exists(oxidePath))
            {
                try { File.WriteAllText(marker, DateTime.UtcNow.ToString("o")); } catch { }
                return;
            }

            try
            {
                OxideBlueprintShareFile oxide = JsonConvert.DeserializeObject<OxideBlueprintShareFile>(File.ReadAllText(oxidePath));
                if (oxide?.Players == null)
                {
                    File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                    return;
                }

                int imported = 0;
                foreach (var kv in oxide.Players)
                {
                    if (!ulong.TryParse(kv.Key, out ulong playerId) || kv.Value == null)
                        continue;

                    StoredData.PlayerData data = storedData.SetupPlayer(playerId);
                    if (data == null) continue;

                    if (!kv.Value.SharingEnabled)
                    {
                        data.Unshare(TeamType.Clan, ShareType.Blueprint);
                        data.Unshare(TeamType.Friend, ShareType.Blueprint);
                        data.Unshare(TeamType.Team, ShareType.Blueprint);
                    }

                    BlueprintLearnData src = kv.Value.LearntBlueprints;
                    if (src == null) continue;

                    BlueprintLearnData dest = data.Blueprints;
                    MergeIntList(dest.Team, src.Team);
                    MergeIntList(dest.Clan, src.Clan);
                    if (src.Friends != null)
                    {
                        if (dest.Friends == null)
                            dest.Friends = new Dictionary<string, List<int>>();
                        foreach (var friend in src.Friends)
                        {
                            if (string.IsNullOrEmpty(friend.Key) || friend.Value == null)
                                continue;
                            List<int> destList = dest.FriendList(friend.Key);
                            MergeIntList(destList, friend.Value);
                        }
                    }
                    imported++;
                }

                SaveData();
                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                Debug.Log($"[DynamicCupShare] Migrated Oxide BlueprintShare data for {imported} player(s).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] BlueprintShare data migrate failed: " + ex.Message);
            }
        }

        private static void MergeIntList(List<int> dest, List<int> src)
        {
            if (dest == null || src == null) return;
            for (int i = 0; i < src.Count; i++)
            {
                int id = src[i];
                if (!dest.Contains(id))
                    dest.Add(id);
            }
        }

        private class OxideBlueprintShareFile
        {
            public Dictionary<string, OxideBlueprintSharePlayer> Players { get; set; }
        }

        private class OxideBlueprintSharePlayer
        {
            public bool SharingEnabled { get; set; } = true;
            public BlueprintLearnData LearntBlueprints { get; set; }
        }

        private static readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Message.Title"] = "<color=#ce422b>[ DCS ]</color> ",
            ["UI.Title"] = "Dynamic Share",
            ["UI.Share.Clan"] = "Clan Share",
            ["UI.Share.Friend"] = "Friend Share",
            ["UI.Share.Team"] = "Team Share",
            ["UI.Type.Box"] = "Boxes",
            ["UI.Type.Cupboard"] = "Cupboards",
            ["UI.Type.Door"] = "Doors",
            ["UI.Type.Locker"] = "Lockers",
            ["UI.Type.Turret"] = "Turrets",
            ["UI.Type.Hitch"] = "Hitch & Troughs",
            ["UI.Type.Composter"] = "Composters",
            ["UI.Type.Dropbox"] = "Drop Boxes",
            ["UI.Type.VendingMachine"] = "Vending Machines",
            ["UI.Type.Furnace"] = "Furnaces",
            ["UI.Type.Refinery"] = "Refineries",
            ["UI.Type.Bbq"] = "BBQs",
            ["UI.Type.Planters"] = "Planters",
            ["UI.Type.MixingTable"] = "Mixing Tables",
            ["UI.Type.ChickenCoop"] = "Chicken Coop",
            ["UI.Type.Beehive"] = "Behive",
            ["UI.Type.Blueprint"] = "Blueprints",
            ["UI.Tab.Sharing"] = "Share",
            ["UI.Tab.Commands"] = "Cmds",
            ["UI.Commands.Intro"] = "Sharing tab toggles access. Commands and extra options are below.",
            ["UI.Commands.Share"] = "/{0} — Open sharing options",
            ["UI.Commands.Share.Action"] = "Open",
            ["UI.Commands.Bs"] = "/{0} help toggle share show",
            ["UI.Commands.BsToggle"] = "/{0} toggle — Auto-share studied BPs",
            ["UI.Commands.BsShare"] = "/{0} share — Give a player your BPs",
            ["UI.Commands.BsShow"] = "/{0} show — Blueprints shared with you",
            ["UI.Commands.BsShowFriend"] = "Show friend-shared blueprints",
            ["UI.Commands.Admin"] = "/dcsadmin — Cupboard admin auth mode",
            ["UI.Commands.SharePlayer"] = "/shareplayer — Edit another player's shares",
            ["UI.Commands.PlayerPlaceholder"] = "player name",
            ["UI.Commands.SteamPlaceholder"] = "steam ID",
            ["UI.Commands.FriendPlaceholder"] = "friend name",
            ["UI.Commands.Go"] = "Go",
            ["UI.Commands.Team"] = "Team",
            ["UI.Commands.Clan"] = "Clan",
            ["UI.Commands.Friend"] = "Friend",
            ["UI.Commands.BuildingWorkbench"] = "Building-wide workbench (anywhere you're authed)",
            ["Chat.NoTurretToggle"] = "<color=#ce422b>Turret share can not be toggled</color>",
            ["Chat.ShareEnabled"] = "You have enabled <color=#ce422b>{0}</color> sharing for <color=#ce422b>{1}s</color>",
            ["Chat.ShareDisabled"] = "You have disabled <color=#ce422b>{0}</color> sharing for <color=#ce422b>{1}s</color>",
            ["Message.AdminEnabled"] = "<color=#ce422b>[ DCS ]</color> Admin mode enabled!",
            ["Message.AdminDisabled"] = "<color=#ce422b>[ DCS ]</color> Admin mode disabled!",
            ["Error.NoPermissions"] = "<color=#ce422b>You do not have permission to use this command</color>",
            ["Error.MaxCupboardAuth"] = "<color=#ce422b>This cupboard already has the maximum allowed authorizations</color>",
            ["Error.AuthDenied"] = "<color=#ce422b>Authorization denied!</color>",
            ["Error.ClearAuthDenied"] = "<color=#ce422b>Clear authorization denied!</color>",
            ["Error.NoBuild.Iceberg"] = "You are not allowed to build on <color=#ce422b>icebergs</color>",
            ["Error.NoBuild.IceSheet"] = "You are not allowed to build on <color=#ce422b>ice sheets</color>",
            ["Error.NoBuild.IceLake"] = "You are not allowed to build on <color=#ce422b>ice lakes</color>",

            ["BP.Prefix"] = "<color=#ce422b>[ DCS ]</color> ",
            ["BP.ArgumentsError"] = "Incorrect command usage. Try <color=#ffff00>/bs help</color>.",
            ["BP.Help"] = "<color=#ce422b>Blueprint Share Commands:</color>\n" +
                          "<color=#ce422b>/bs toggle</color> - Toggle automatic blueprint sharing\n" +
                          "<color=#ce422b>/bs share <player></color> - Share your learned blueprints with a player\n" +
                          "<color=#ce422b>/bs show <team|clan|friend> [name]</color> - View blueprints shared with you\n" +
                          "<color=#ce422b>/bs help</color> - Show this help menu",
            ["BP.ToggleOn"] = "Blueprint sharing is now <color=#00ff00>enabled</color>.",
            ["BP.ToggleOff"] = "Blueprint sharing is now <color=#ff0000>disabled</color>.",
            ["BP.NoPermission"] = "You don't have permission to use this command.",
            ["BP.CannotShare"] = "You cannot share blueprints with this player, they must be in your team, clan, or friends list.",
            ["BP.NoTarget"] = "You must specify a player to share with.",
            ["BP.TargetEqualsPlayer"] = "You cannot share blueprints with yourself.",
            ["BP.PlayerNotFound"] = "Player not found.",
            ["BP.PlayerSharedBlueprints"] = "You shared <color=#ffff00>{0}</color> blueprint(s) with <color=#ffff00>{1}</color>.",
            ["BP.TargetLearntBlueprints"] = "<color=#ffff00>{0}</color> has shared <color=#ffff00>{1}</color> blueprint(s) with you.",
            ["BP.NoBlueprintsToShare"] = "You have no new blueprints to share with <color=#ffff00>{0}</color>.",
            ["BP.PlayerSharedBlueprint"] = "You have learned the <color=#ffff00>{0}</color> blueprint and shared it with <color=#ffff00>{1}</color> player(s).",
            ["BP.TargetLearntBlueprint"] = "<color=#ffff00>{0}</color> has shared the <color=#ffff00>{1}</color> blueprint with you.",
            ["BP.BlueprintBlocked"] = "The <color=#ffff00>{0}</color> blueprint is blocked from sharing, but you have still learned it.",
            ["BP.TargetSharingDisabled"] = "Cannot share blueprints with <color=#ffff00>{0}</color> — they have disabled sharing.",
            ["BP.BlueprintsRemoved"] = "You have lost access to <color=#ffff00>{0}</color> blueprint(s).",
            ["BP.ShowMissingArgument"] = "You must specify which shared blueprints to view. Options: <color=#ffff00>team</color>, <color=#ffff00>clan</color>, or <color=#ffff00>friend</color>.",
            ["BP.ShowFriendArgumentMissing"] = "You must specify a friend's name.",
            ["BP.NotFriends"] = "You are not friends with this player.",
            ["BP.NoSharedBlueprints"] = "No blueprints have been shared with you.",
            ["BP.SharedBlueprintsTitle"] = "Blueprints Shared With You:",
            ["BP.ShowSharedBlueprints"] = "<color=#ffff00>Tier {0}:</color> {1}",
            ["BP.LoseBlueprintsDisabled"] = "This feature has been disabled by the server administrator.",
            ["BP.Disabled"] = "Blueprint sharing is not enabled on this server.",
            ["BW.Notification"] = "Your workbench range has been increased to work inside your building.",
            ["BW.CraftCanceled"] = "Your workbench level has changed. Crafts that required a higher level have been cancelled.",
            ["BW.ToggleOn"] = "Building-wide workbench is now <color=#00ff00>enabled</color>.",
            ["BW.ToggleOff"] = "Building-wide workbench is now <color=#ff0000>disabled</color>.",
        };
    }
}
