using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Facepunch;
using Newtonsoft.Json;
using UnityEngine;

namespace EntityOwner
{
    /// <summary>
    /// Entity Owner 3.4.3 logic (Oxide port). Config / lang / ownership / auth helpers.
    /// </summary>
    public sealed class EntityOwnerService
    {
        public const string VersionString = "3.4.3";

        private static readonly string[] AllPermissions =
        {
            "entityowner.cancheckowners",
            "entityowner.cancheckcodes",
            "entityowner.canchangeowners",
            "entityowner.seedetails",
            "entityowner.cancheckassignee"
        };

        private static readonly string[] ChatCommandNames =
        {
            "prod", "setowner", "own", "unown", "auth", "deauth", "prod2"
        };

        private readonly int _layerMasks = LayerMask.GetMask("Construction", "Construction Trigger", "Trigger", "Deployed");
        private readonly string _serverRoot;
        private readonly string _configPath;
        private readonly LangStore _lang = new LangStore();

        private Configuration _config;
        private int _entityLimit = 8000;
        private float _distanceThreshold = 3f;
        private float _cupboardDistanceThreshold = 20f;
        private bool _debug;

        public Configuration Config => _config;

        public EntityOwnerService(string serverRoot)
        {
            _serverRoot = serverRoot;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "EntityOwner.json");
        }

        public IEnumerable<string> GetChatCommandNames() => ChatCommandNames;

        #region Config

        public class Configuration
        {
            public bool Debug = false;
            public int EntityLimit = 8000;
            public float DistanceThreshold = 3f;
            public float CupboardDistanceThreshold = 20f;
            public string VERSION = "3.4.3";
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<Configuration>(json);
                }

                if (_config == null)
                {
                    Debug.Log("[EntityOwner] Creating new configuration file.");
                    _config = new Configuration();
                    SaveConfig();
                }
                else
                {
                    _debug = _config.Debug;
                    _entityLimit = _config.EntityLimit;
                    _distanceThreshold = _config.DistanceThreshold;
                    _cupboardDistanceThreshold = _config.CupboardDistanceThreshold;
                    LoadData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EntityOwner] FAIL: load config - using defaults. " + ex.Message);
                _config = new Configuration();
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config ?? new Configuration(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EntityOwner] FAIL: save config: " + ex.Message);
            }
        }

        private void ReloadConfig()
        {
            if (_config == null)
                _config = new Configuration();

            _config.VERSION = VersionString;
            _config.Debug = false;
            _config.EntityLimit = _entityLimit;
            _config.DistanceThreshold = _distanceThreshold;
            _config.CupboardDistanceThreshold = _cupboardDistanceThreshold;

            Debug.Log("[EntityOwner] Upgrading configuration file.");
            SaveConfig();
        }

        private void LoadData()
        {
            if (_config == null)
            {
                ReloadConfig();
                return;
            }

            if (string.IsNullOrEmpty(_config.VERSION))
            {
                ReloadConfig();
            }
            else if (_config.VERSION != VersionString)
            {
                ReloadConfig();
            }
        }

        #endregion

        #region Lang

        public void LoadDefaultMessages()
        {
            _lang.RegisterMessages(CreateDefaultMessages(), "en");
            _lang.LoadHarmonyLanguageOverrides(_serverRoot, "EntityOwner");
        }

        private static Dictionary<string, string> CreateDefaultMessages() => new Dictionary<string, string>
        {
            ["Denied: Permission"] = "You are not allowed to use this command",
            ["Error: Unknown"] = "Undefined error occurred. Look up the log for more info",
            ["Target: None"] = "No target found",
            ["Target: Owner"] = "Owner: {0}",
            ["Target: Limit"] = "Exceeded entity limit.",
            ["Syntax: Owner"] = "Invalid syntax: /owner",
            ["Syntax: SetOwner"] = "Incorrect Syntax.\n/setowner <playername|steamid>",
            ["Syntax: Own"] = "Invalid Syntax. \n/own type player\nTypes: all/block/storage/cupboard/sign/sleepingbag/plant/oven/door/turret\n/own player",
            ["Syntax: Unown"] = "Invalid Syntax. \n/unown type player\nTypes: all/block/storage/cupboard/sign/sleepingbag/plant/oven/door/turret\n/unown player",
            ["Syntax: Prod2"] = "Invalid Syntax. \n/prod2 type \nTypes:\n all/block/entity/storage/cupboard/sign/sleepingbag/plant/oven/door/turret",
            ["Syntax: Auth"] = "Invalid Syntax. \n/auth turret player\n/auth cupboard player/auth player\n/auth",
            ["Syntax: Deauth"] = "Invalid Syntax. \n/deauth turret player\n/deauth cupboard player/deauth player\n/deauth",
            ["Ownership: Changing"] = "Changing ownership..",
            ["Ownership: Removing"] = "Removing ownership..",
            ["Ownership: New"] = "New owner of all around is: {0}",
            ["Ownership: New Self"] = "Owner: You were given ownership of this house and nearby deployables",
            ["Ownership: Count"] = "Count ({0})",
            ["Ownership: Removed"] = "Ownership removed",
            ["Ownership: Changed"] = "Ownership changed",
            ["Entities: None"] = "No entities found.",
            ["Entities: Authorized"] = "({0}) Authorized",
            ["Entities: Count"] = "Counted {0} entities ({1}/{2})",
            ["Structure: Prodding"] = "Prodding structure..",
            ["Structure: Condition Percent"] = "Condition: {0}%",
            ["Player: Unknown Percent"] = "Unknown: {0}%",
            ["Player: None"] = "Target player not found",
            ["Cupboards: Prodding"] = "Prodding cupboards..",
            ["Cupboards: Authorizing"] = "Authorizing cupboards..",
            ["Cupboards: Authorized"] = "Authorized {0} on {1} cupboards",
            ["Cupboards: Deauthorizing"] = "Deauthorizing cupboards..",
            ["Cupboard: Deauthorized"] = "Deauthorized {0} on {1} cupboards",
            ["Turrets: Authorized"] = "Authorized {0} on {1} turrets",
            ["Turrets: Authorizing"] = "Authorizing turrets..",
            ["Turrets: Prodding"] = "Prodding turrets..",
            ["Turrets: Deauthorized"] = "Deauthorized {0} on {1} turrets",
            ["Turrets: Deauthorizing"] = "Deauthorizing turrets..",
            ["Lock: Code"] = "Code: {0}",
            ["Lock: Owner"] = "Lock owner: {0}",
            ["Bag: Assignee"] = "Assigned: {0}"
        };

        private string GetMsg(string key, BasePlayer player = null)
        {
            return _lang.GetMessage(key);
        }

        #endregion

        #region Lifecycle

        public void OnServerInitialized()
        {
            try
            {
                LoadConfig();

                _debug = _config.Debug;
                _entityLimit = _config.EntityLimit;
                _distanceThreshold = _config.DistanceThreshold;
                _cupboardDistanceThreshold = _config.CupboardDistanceThreshold;

                if (_distanceThreshold >= 5f)
                {
                    Debug.LogWarning("[EntityOwner] ALERT: Distance threshold configuration option is ABOVE 5. This may cause serious performance degradation (lag) when using EntityOwner commands");
                }

                RegisterPermissions();
            }
            catch (Exception ex)
            {
                Debug.LogError("[EntityOwner] OnServerInitialized failed: " + ex.Message);
            }
        }

        private const string AdminGroup = "admin";

        public void RegisterPermissions()
        {
            foreach (string perm in AllPermissions)
                PermissionsBridge.RegisterPermission(perm);
            EnsureAdminGroupPermissions();
        }

        private void EnsureAdminGroupPermissions()
        {
            if (!PermissionsBridge.IsAvailable)
            {
                Debug.LogWarning("[EntityOwner] Permissions not available - cannot grant entityowner.* to admin group.");
                return;
            }

            if (!PermissionsBridge.GroupExists(AdminGroup))
                PermissionsBridge.CreateGroup(AdminGroup, "Administrators", 0);

            int granted = 0;
            foreach (string perm in AllPermissions)
            {
                if (PermissionsBridge.GrantGroupPermission(AdminGroup, perm))
                    granted++;
            }

            Debug.Log("[EntityOwner] Ensured Permissions group '" + AdminGroup + "' has entityowner.* (" + granted + "/" + AllPermissions.Length + " grants).");
        }

        #endregion

        #region Chat dispatch

        public bool HandleChatCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null || string.IsNullOrEmpty(command))
                return false;

            switch (command.ToLowerInvariant())
            {
                case "prod":
                    CmdProd(player, command, args);
                    return true;
                case "setowner":
                    CmdSetowner(player, command, args);
                    return true;
                case "own":
                    CmdOwn(player, command, args);
                    return true;
                case "unown":
                    CmdUnown(player, command, args);
                    return true;
                case "auth":
                    CmdAuth(player, command, args);
                    return true;
                case "deauth":
                    CmdDeauth(player, command, args);
                    return true;
                case "prod2":
                    CmdProd2(player, command, args);
                    return true;
                default:
                    return false;
            }
        }

        private void SendReply(BasePlayer player, string msg)
        {
            if (player == null || string.IsNullOrEmpty(msg)) return;
            player.ChatMessage(msg);
        }

        #endregion

        #region Help

        public void SendHelpText(BasePlayer player)
        {
            var sb = new StringBuilder();
            if (CanCheckOwners(player) || CanChangeOwners(player))
            {
                sb.Append("<size=18>EntityOwner</size> by <color=#ce422b>Calytic</color> at <color=#ce422b>http://rustservers.io</color>\n");
            }

            if (CanCheckOwners(player))
            {
                sb.Append("  ").Append("<color=\"#ffd479\">/prod</color> - Check ownership of entity you are looking at").Append("\n");
                sb.Append("  ").Append("<color=\"#ffd479\">/prod2</color> - Check ownership of entire structure/all deployables").Append("\n");
                sb.Append("  ").Append("<color=\"#ffd479\">/prod2 block</color> - Check ownership structure only").Append("\n");
                sb.Append("  ").Append("<color=\"#ffd479\">/prod2 cupboard</color> - Check authorization on all nearby cupboards").Append("\n");
                sb.Append("  ").Append("<color=\"#ffd479\">/auth</color> - Check authorization list of tool cupboard you are looking at").Append("\n");
            }

            if (CanChangeOwners(player))
            {
                sb.Append("  ").Append("<color=\"#ffd479\">/own [all/block]</color> - Take ownership of entire structure").Append("\n");
                sb.Append("  ").Append("<color=\"#ffd479\">/own [all/block] PlayerName</color> - Give ownership of entire structure to specified player").Append("\n");
                sb.Append("  ").Append("<color=\"#ffd479\">/unown [all/block]</color> - Remove ownership from entire structure").Append("\n");
                sb.Append("  ").Append("<color=\"#ffd479\">/auth PlayerName</color> - Authorize specified player on all nearby cupboards").Append("\n");
            }

            SendReply(player, sb.ToString());
        }

        #endregion

        #region Chat Commands

        private void CmdProd(BasePlayer player, string command, string[] args)
        {
            if (!CanCheckOwners(player))
            {
                SendReply(player, GetMsg("Denied: Permission", player));
                return;
            }

            if (args == null || args.Length == 0)
            {
                var target = RaycastAll<BaseEntity>(player.eyes.HeadRay());
                if (target is bool)
                {
                    SendReply(player, GetMsg("Target: None", player));
                    return;
                }

                if (target is BaseEntity)
                {
                    var targetEntity = (BaseEntity)target;
                    var owner = GetOwnerName(targetEntity);
                    if (string.IsNullOrEmpty(owner))
                        owner = "N/A";

                    string msg = string.Format(GetMsg("Target: Owner", player), owner);

                    var baseLock = targetEntity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
                    if (baseLock != null)
                    {
                        if (baseLock.OwnerID != 0 && baseLock.OwnerID != targetEntity.OwnerID)
                        {
                            var lockOwnerName = GetOwnerName(baseLock);
                            msg += "\n" + string.Format(GetMsg("Lock: Owner", player), lockOwnerName);
                        }
                    }

                    if (CanSeeDetails(player))
                    {
                        msg += "\n<color=#D3D3D3>Name: " + targetEntity.ShortPrefabName + "</color>";
                        if (targetEntity.skinID > 0)
                            msg += "\n<color=#D3D3D3>Skin: " + targetEntity.skinID + "</color>";

                        if (targetEntity.PrefabName != targetEntity.ShortPrefabName)
                            msg += "\n<color=#D3D3D3>Prefab: \"" + targetEntity.PrefabName + "\"</color>";

                        msg += "\n<color=#D3D3D3>Outside: " + (targetEntity.IsOutside() ? "Yes" : "No") + "</color>";
                    }

                    if (CanCheckCodes(player))
                    {
                        var codeLock = baseLock as CodeLock;
                        if (codeLock != null)
                        {
                            string keyCode = codeLock.code;
                            msg += "\n" + string.Format(GetMsg("Lock: Code", player), keyCode);
                        }
                    }

                    if (CanCheckAssignee(player))
                    {
                        if (targetEntity is SleepingBag)
                        {
                            SleepingBag bag = (SleepingBag)targetEntity;
                            msg += "\n" + string.Format(GetMsg("Bag: Assignee", player), FindPlayerName(bag.deployerUserID));
                        }
                    }

                    SendReply(player, msg);
                }
            }
            else
            {
                SendReply(player, GetMsg("Syntax: Owner", player));
            }
        }

        private void CmdSetowner(BasePlayer player, string command, string[] args)
        {
            if (!CanChangeOwners(player))
            {
                SendReply(player, GetMsg("Denied: Permission", player));
                return;
            }

            if (args == null || args.Length == 0 || string.IsNullOrEmpty(args[0]))
            {
                SendReply(player, GetMsg("Syntax: SetOwner", player));
                return;
            }

            ulong newOwnerId = FindUserIDByPartialName(args[0]);
            if (newOwnerId == 0)
            {
                SendReply(player, GetMsg("Player: None", player));
                return;
            }

            try
            {
                var targetEntity = RaycastAll<BaseEntity>(player.eyes.HeadRay());

                if (targetEntity is bool)
                {
                    SendReply(player, GetMsg("Target: None", player));
                    return;
                }

                if (targetEntity is BaseEntity)
                {
                    var targetBaseEntity = (BaseEntity)targetEntity;
                    try
                    {
                        var oldOwner = GetOwnerName(targetBaseEntity);
                        if (string.IsNullOrEmpty(oldOwner))
                            oldOwner = "N/A";

                        ChangeOwner(targetBaseEntity, newOwnerId);

                        var newOwner = GetOwnerName(targetBaseEntity);
                        if (string.IsNullOrEmpty(newOwner))
                            newOwner = "N/A";

                        SendReply(player, "Changed owner of [" + targetBaseEntity.ShortPrefabName + "] from [" + oldOwner + "] to [" + newOwner + "]");
                    }
                    catch (Exception ex)
                    {
                        SendReply(player, GetMsg("Error: Unknown", player));
                        Debug.LogException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                SendReply(player, GetMsg("Error: Unknown", player));
                Debug.LogException(ex);
            }
        }

        private void CmdOwn(BasePlayer player, string command, string[] args)
        {
            if (!CanChangeOwners(player))
            {
                SendReply(player, GetMsg("Denied: Permission", player));
                return;
            }

            var massTrigger = false;
            string type = null;
            ulong target = (ulong)player.userID;

            if (args == null || args.Length == 0)
                args = new[] { "1" };

            if (args.Length > 2)
            {
                SendReply(player, GetMsg("Syntax: Own", player));
                return;
            }

            if (args.Length == 1)
            {
                type = args[0];
                if (type == "all" || type == "storage" || type == "block" || type == "cupboard" || type == "sign" || type == "sleepingbag" || type == "plant" || type == "oven" || type == "door" || type == "turret")
                {
                    massTrigger = true;
                    target = (ulong)player.userID;
                }
                else if (!string.IsNullOrEmpty(type))
                {
                    target = FindUserIDByPartialName(type);
                    type = "1";
                    if (target == 0)
                    {
                        SendReply(player, GetMsg("Player: None", player));
                    }
                    else
                    {
                        massTrigger = true;
                    }
                }
                else
                {
                    massTrigger = true;
                    type = "1";
                }
            }
            else if (args.Length == 2)
            {
                type = args[0];
                target = FindUserIDByPartialName(args[1]);
                if (target == 0)
                {
                    SendReply(player, GetMsg("Player: None", player));
                }
                else
                {
                    massTrigger = true;
                }
            }

            if (!massTrigger || type == null) return;

            switch (type)
            {
                case "1":
                    BaseEntity entity;
                    if (TryGetEntity<BaseEntity>(player, out entity))
                    {
                        ChangeOwner(entity, target);
                        SendReply(player, GetMsg("Ownership: Changed", player));
                    }
                    else
                    {
                        SendReply(player, GetMsg("Target: None", player));
                    }
                    break;
                case "all":
                    MassChangeOwner<BaseEntity>(player, target);
                    break;
                case "block":
                    MassChangeOwner<BuildingBlock>(player, target);
                    break;
                case "storage":
                    MassChangeOwner<StorageContainer>(player, target);
                    break;
                case "sign":
                    MassChangeOwner<Signage>(player, target);
                    break;
                case "sleepingbag":
                    MassChangeOwner<SleepingBag>(player, target);
                    break;
                case "plant":
                    MassChangeOwner<GrowableEntity>(player, target);
                    break;
                case "oven":
                    MassChangeOwner<BaseOven>(player, target);
                    break;
                case "turret":
                    MassChangeOwner<AutoTurret>(player, target);
                    break;
                case "door":
                    MassChangeOwner<Door>(player, target);
                    break;
                case "cupboard":
                    MassChangeOwner<BuildingPrivlidge>(player, target);
                    break;
            }
        }

        private void CmdUnown(BasePlayer player, string command, string[] args)
        {
            if (!CanChangeOwners(player))
            {
                SendReply(player, GetMsg("Denied: Permission", player));
                return;
            }

            if (args == null || args.Length == 0)
                args = new[] { "1" };

            if (args.Length > 1)
            {
                SendReply(player, GetMsg("Syntax: Unown", player));
                return;
            }

            if (args.Length != 1) return;

            switch (args[0])
            {
                case "1":
                    BaseEntity entity;
                    if (TryGetEntity<BaseEntity>(player, out entity))
                    {
                        RemoveOwner(entity);
                        SendReply(player, GetMsg("Ownership: Removed", player));
                    }
                    else
                    {
                        SendReply(player, GetMsg("Target: None", player));
                    }
                    break;
                case "all":
                    MassChangeOwner<BaseEntity>(player);
                    break;
                case "block":
                    MassChangeOwner<BuildingBlock>(player);
                    break;
                case "storage":
                    MassChangeOwner<StorageContainer>(player);
                    break;
                case "sign":
                    MassChangeOwner<Signage>(player);
                    break;
                case "sleepingbag":
                    MassChangeOwner<SleepingBag>(player);
                    break;
                case "plant":
                    MassChangeOwner<GrowableEntity>(player);
                    break;
                case "oven":
                    MassChangeOwner<BaseOven>(player);
                    break;
                case "turret":
                    MassChangeOwner<AutoTurret>(player);
                    break;
                case "door":
                    MassChangeOwner<Door>(player);
                    break;
                case "cupboard":
                    MassChangeOwner<BuildingPrivlidge>(player);
                    break;
            }
        }

        private void CmdAuth(BasePlayer player, string command, string[] args)
        {
            if (!CanChangeOwners(player))
            {
                SendReply(player, GetMsg("Denied: Permission", player));
                return;
            }

            var massCupboard = false;
            var massTurret = false;
            var checkCupboard = false;
            var checkTurret = false;
            var error = false;
            BasePlayer target = null;

            if (args == null)
                args = Array.Empty<string>();

            if (args.Length > 2)
            {
                error = true;
            }
            else if (args.Length == 1)
            {
                if (args[0] == "cupboard")
                {
                    checkCupboard = true;
                }
                else if (args[0] == "turret")
                {
                    checkTurret = true;
                }
                else
                {
                    massCupboard = true;
                    target = FindPlayerByPartialName(args[0]);
                }
            }
            else if (args.Length == 0)
            {
                checkCupboard = true;
            }
            else if (args.Length == 2)
            {
                if (args[0] == "cupboard")
                {
                    massCupboard = true;
                    target = FindPlayerByPartialName(args[1]);
                }
                else if (args[0] == "turret")
                {
                    massTurret = true;
                    target = FindPlayerByPartialName(args[1]);
                }
                else
                {
                    error = true;
                }
            }

            if ((massTurret || massCupboard) && target?.net?.connection == null)
            {
                SendReply(player, GetMsg("Player: None", player));
                return;
            }

            if (error)
            {
                SendReply(player, GetMsg("Syntax: Auth", player));
                return;
            }

            if (massCupboard)
                MassCupboardAuthorize(player, target);

            if (checkCupboard)
            {
                var priv = RaycastAll<BuildingPrivlidge>(player.eyes.HeadRay());
                if (priv is bool)
                {
                    SendReply(player, GetMsg("Target: None", player));
                    return;
                }

                if (priv is BuildingPrivlidge)
                    ProdCupboard(player, (BuildingPrivlidge)priv);
            }

            if (massTurret)
                MassTurretAuthorize(player, target);

            if (checkTurret)
            {
                var turret = RaycastAll<AutoTurret>(player.eyes.HeadRay());
                if (turret is bool)
                {
                    SendReply(player, GetMsg("Target: None", player));
                    return;
                }

                if (turret is AutoTurret)
                    ProdTurret(player, (AutoTurret)turret);
            }
        }

        private void CmdDeauth(BasePlayer player, string command, string[] args)
        {
            if (!CanChangeOwners(player))
            {
                SendReply(player, GetMsg("Denied: Permission", player));
                return;
            }

            var massCupboard = false;
            var massTurret = false;
            var error = false;
            BasePlayer target = null;

            if (args == null)
                args = Array.Empty<string>();

            if (args.Length > 2)
            {
                error = true;
            }
            else if (args.Length == 1)
            {
                if (args[0] == "cupboard")
                {
                    SendReply(player, "Invalid Syntax. /deauth cupboard PlayerName");
                    return;
                }

                if (args[0] == "turret")
                {
                    SendReply(player, "Invalid Syntax. /deauth turret PlayerName");
                    return;
                }

                massCupboard = true;
                target = FindPlayerByPartialName(args[0]);
            }
            else if (args.Length == 0)
            {
                SendReply(player, "Invalid Syntax. /deauth PlayerName\n/deauth turret/cupboard PlayerName");
                return;
            }
            else if (args.Length == 2)
            {
                if (args[0] == "cupboard")
                {
                    massCupboard = true;
                    target = FindPlayerByPartialName(args[1]);
                }
                else if (args[0] == "turret")
                {
                    massTurret = true;
                    target = FindPlayerByPartialName(args[1]);
                }
                else
                {
                    error = true;
                }
            }

            if ((massTurret || massCupboard) && target?.net?.connection == null)
            {
                SendReply(player, GetMsg("Player: None", player));
                return;
            }

            if (error)
            {
                SendReply(player, GetMsg("Syntax: Deauth", player));
                return;
            }

            if (massCupboard)
                MassCupboardDeauthorize(player, target);

            if (massTurret)
                MassTurretDeauthorize(player, target);
        }

        private void CmdProd2(BasePlayer player, string command, string[] args)
        {
            if (!CanCheckOwners(player))
            {
                SendReply(player, GetMsg("Denied: Permission", player));
                return;
            }

            if (args == null)
                args = Array.Empty<string>();

            bool highlight = false;
            if (args.Length > 0)
            {
                if (args[0] == "highlight")
                {
                    highlight = true;
                    args = SkipFirst(args);
                }

                if (args.Length == 0)
                {
                    MassProd<BaseEntity>(player, highlight);
                    return;
                }

                switch (args[0])
                {
                    case "all":
                        args = SkipFirst(args);
                        MassProd<BaseEntity>(player, highlight, args);
                        break;
                    case "block":
                        MassProd<BuildingBlock>(player, highlight);
                        break;
                    case "storage":
                        MassProd<StorageContainer>(player, highlight);
                        break;
                    case "sign":
                        MassProd<Signage>(player, highlight);
                        break;
                    case "sleepingbag":
                        MassProd<SleepingBag>(player, highlight);
                        break;
                    case "plant":
                        MassProd<GrowableEntity>(player, highlight);
                        break;
                    case "oven":
                        MassProd<BaseOven>(player, highlight);
                        break;
                    case "turret":
                        MassProdTurret(player, highlight);
                        break;
                    case "cupboard":
                        MassProdCupboard(player, highlight);
                        break;
                    default:
                        MassProd<BaseEntity>(player, highlight, args);
                        break;
                }
            }
            else if (args.Length == 0)
            {
                MassProd<BaseEntity>(player);
            }
            else
            {
                SendReply(player, GetMsg("Syntax: Prod2", player));
            }
        }

        #endregion

        #region Permission Checks

        private static bool HasAuthBypass(BasePlayer player)
        {
            return player != null && player.net?.connection != null && player.net.connection.authLevel > 0;
        }

        private bool CanCheckOwners(BasePlayer player)
        {
            if (player == null) return false;
            if (HasAuthBypass(player)) return true;
            return PermissionsBridge.UserHasPermission(player.UserIDString, "entityowner.cancheckowners");
        }

        private bool CanCheckCodes(BasePlayer player)
        {
            if (player == null) return false;
            if (HasAuthBypass(player)) return true;
            return PermissionsBridge.UserHasPermission(player.UserIDString, "entityowner.cancheckcodes");
        }

        private bool CanCheckAssignee(BasePlayer player)
        {
            if (player == null) return false;
            if (HasAuthBypass(player)) return true;
            return PermissionsBridge.UserHasPermission(player.UserIDString, "entityowner.cancheckassignee");
        }

        private bool CanSeeDetails(BasePlayer player)
        {
            if (player == null) return false;
            if (HasAuthBypass(player)) return true;
            return PermissionsBridge.UserHasPermission(player.UserIDString, "entityowner.seedetails");
        }

        private bool CanChangeOwners(BasePlayer player)
        {
            if (player == null) return false;
            if (HasAuthBypass(player)) return true;
            return PermissionsBridge.UserHasPermission(player.UserIDString, "entityowner.canchangeowners");
        }

        #endregion

        #region Ownership Methods

        private bool TryGetEntity<T>(BasePlayer player, out BaseEntity entity) where T : BaseEntity
        {
            entity = null;

            var target = RaycastAll<BaseEntity>(player.eyes.HeadRay());

            if (target is T)
            {
                entity = (BaseEntity)target;
                return true;
            }

            return false;
        }

        private void MassChangeOwner<T>(BasePlayer player, ulong target = 0) where T : BaseEntity
        {
            object entityObject = false;

            if (typeof(T) == typeof(BuildingBlock))
                entityObject = FindBuilding(player.transform.position, _distanceThreshold);
            else
                entityObject = FindEntity(player.transform.position, _distanceThreshold);

            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                if (target == 0)
                    SendReply(player, GetMsg("Ownership: Removing", player));
                else
                    SendReply(player, GetMsg("Ownership: Changing", player));

                var entity = (T)entityObject;
                var entityList = new HashSet<T>();
                var checkFrom = new List<Vector3>();
                entityList.Add(entity);
                checkFrom.Add(entity.transform.position);
                var c = 1;
                if (target == 0)
                    RemoveOwner(entity);
                else
                    ChangeOwner(entity, target);

                var current = 0;
                var bbs = 0;
                var ebs = 0;
                if (entity is BuildingBlock)
                    bbs++;
                else
                    ebs++;

                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        if (_debug)
                            SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);
                        SendReply(player, string.Format(GetMsg("Entities: Count", player), c, bbs, ebs));
                        break;
                    }

                    if (current > checkFrom.Count)
                    {
                        SendReply(player, string.Format(GetMsg("Entities: Count", player), c, bbs, ebs));
                        break;
                    }

                    var hits = FindEntities<T>(checkFrom[current - 1], _distanceThreshold);

                    foreach (var entityComponent in hits)
                    {
                        if (!entityList.Add(entityComponent)) continue;
                        c++;
                        checkFrom.Add(entityComponent.transform.position);

                        if (entityComponent is BuildingBlock)
                            bbs++;
                        else
                            ebs++;

                        if (target == 0)
                            RemoveOwner(entityComponent);
                        else
                            ChangeOwner(entityComponent, target);
                    }

                    Pool.FreeUnmanaged(ref hits);
                }

                if (target == 0)
                {
                    SendReply(player, string.Format(GetMsg("Ownership: New", player), "No one"));
                }
                else
                {
                    BasePlayer targetPlayer = BasePlayer.FindByID(target);

                    if (targetPlayer != null)
                    {
                        SendReply(player, string.Format(GetMsg("Ownership: New", player), targetPlayer.displayName));
                        SendReply(targetPlayer, GetMsg("Ownership: New Self", player));
                    }
                    else
                    {
                        string offlineName = ServerMgr.Instance?.persistance?.GetPlayerName(target) ?? target.ToString();
                        SendReply(player, string.Format(GetMsg("Target: Owner", player), offlineName));
                    }
                }
            }
        }

        private void MassProd<T>(BasePlayer player, bool highlight = false, params string[] filter) where T : BaseEntity
        {
            object entityObject = FindEntity(player.transform.position, _distanceThreshold);
            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                float health = 0f;
                float maxHealth = 0f;
                var prodOwners = new Dictionary<ulong, int>();
                var entity = (BaseEntity)entityObject;
                if (entity.transform == null)
                {
                    SendReply(player, GetMsg("Entities: None", player));
                    return;
                }

                SendReply(player, GetMsg("Structure: Prodding", player));

                var entityList = new HashSet<T>();
                var checkFrom = new List<Vector3>();

                if (entity is T)
                    entityList.Add((T)entity);

                var total = 0;
                var skip = false;
                if (entity is T)
                {
                    if (filter != null && filter.Length > 0)
                    {
                        skip = true;
                        foreach (var f in filter)
                        {
                            if (entity.name.ToLower().Contains(f.ToLower()))
                            {
                                skip = false;
                                break;
                            }
                        }
                    }

                    if (!skip)
                    {
                        prodOwners.Add(entity.OwnerID, 1);
                        health += entity.Health();
                        maxHealth += entity.MaxHealth();
                        total++;
                    }
                }

                var current = -1;
                var distanceThreshold = _distanceThreshold;
                if (typeof(T) != typeof(BuildingBlock) && typeof(T) != typeof(BaseEntity))
                    distanceThreshold += 30f;

                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);
                        break;
                    }

                    if (current > checkFrom.Count)
                        break;

                    var hits = FindEntities<T>(checkFrom.Count > 0 ? checkFrom[current - 1] : entity.transform.position, distanceThreshold);
                    skip = false;
                    foreach (var fentity in hits)
                    {
                        if (fentity.transform == null || !entityList.Add(fentity) || fentity.name == "player/player")
                            continue;

                        if (filter != null && filter.Length > 0)
                        {
                            skip = true;
                            foreach (var f in filter)
                            {
                                if (fentity.name.ToLower().Contains(f.ToLower()))
                                {
                                    skip = false;
                                    break;
                                }
                            }
                        }

                        checkFrom.Add(fentity.transform.position);

                        if (!skip)
                        {
                            total++;
                            if (highlight)
                                SendHighlight(player, fentity.transform.position);

                            var pid = fentity.OwnerID;
                            if (prodOwners.ContainsKey(pid))
                                prodOwners[pid]++;
                            else
                                prodOwners.Add(pid, 1);

                            health += fentity.Health();
                            maxHealth += fentity.MaxHealth();
                        }
                    }

                    Pool.FreeUnmanaged(ref hits);
                }

                var unknown = 100;

                var msg = string.Empty;

                msg = "<size=16>Structure</size>\n";
                msg += "Entities: " + total + "\n";

                if (health > 0 && maxHealth > 0)
                {
                    var condition = Mathf.Round(health * 100 / maxHealth);
                    msg += string.Format(GetMsg("Structure: Condition Percent", player), condition);
                }

                SendReply(player, msg);

                msg = "<size=16>Ownership</size>\n";

                if (total > 0)
                {
                    foreach (var kvp in prodOwners)
                    {
                        var perc = kvp.Value * 100 / total;
                        if (kvp.Key != 0)
                        {
                            var n = FindPlayerName(kvp.Key);
                            msg += n + ": " + perc + "%\n";
                            unknown -= perc;
                        }
                    }
                }

                if (unknown > 0)
                    msg += string.Format(GetMsg("Player: Unknown Percent", player), unknown);

                SendReply(player, msg);
            }
        }

        private void SendHighlight(BasePlayer player, Vector3 position)
        {
            player.SendConsoleCommand("ddraw.sphere", 30f, Color.magenta, position, 2f);
            player.SendNetworkUpdateImmediate();
        }

        private void ProdCupboard(BasePlayer player, BuildingPrivlidge cupboard)
        {
            List<string> authorizedUsers;
            var sb = new StringBuilder();
            if (TryGetCupboardUserNames(cupboard, out authorizedUsers))
            {
                sb.AppendLine(string.Format(GetMsg("Entities: Authorized", player), authorizedUsers.Count));
                foreach (var n in authorizedUsers)
                    sb.AppendLine(n);
            }
            else
                sb.Append(string.Format(GetMsg("Target: None", player)));

            SendReply(player, sb.ToString());
        }

        private void ProdTurret(BasePlayer player, AutoTurret turret)
        {
            List<string> authorizedUsers;
            var sb = new StringBuilder();
            if (TryGetTurretUserNames(turret, out authorizedUsers))
            {
                sb.AppendLine(string.Format(GetMsg("Entities: Authorized", player), authorizedUsers.Count));
                foreach (var n in authorizedUsers)
                    sb.AppendLine(n);
            }
            else
            {
                sb.Append(string.Format(GetMsg("Target: None", player)));
            }

            SendReply(player, sb.ToString());
        }

        private void MassProdCupboard(BasePlayer player, bool highlight = false)
        {
            object entityObject = FindEntity(player.transform.position, _distanceThreshold);

            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                var total = 0;
                var prodOwners = new Dictionary<ulong, int>();
                SendReply(player, GetMsg("Cupboards: Prodding", player));
                var entity = (BaseEntity)entityObject;
                var entityList = new HashSet<BaseEntity>();
                var checkFrom = new List<Vector3>();

                checkFrom.Add(entity.transform.position);

                var current = 0;
                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        if (_debug)
                            SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);

                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    if (current > checkFrom.Count)
                    {
                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    var entities = FindEntities<BuildingPrivlidge>(checkFrom[current - 1], _cupboardDistanceThreshold);

                    foreach (var e in entities)
                    {
                        if (!entityList.Add(e)) continue;
                        if (highlight)
                            SendHighlight(player, e.transform.position);
                        checkFrom.Add(e.transform.position);

                        foreach (var userid in e.authorizedPlayers)
                        {
                            if (prodOwners.ContainsKey(userid))
                                prodOwners[userid]++;
                            else
                                prodOwners.Add(userid, 1);
                        }

                        total++;
                    }

                    Pool.FreeUnmanaged(ref entities);
                }

                var unknown = 100;
                if (total > 0)
                {
                    foreach (var kvp in prodOwners)
                    {
                        var perc = kvp.Value * 100 / total;
                        var n = FindPlayerName(kvp.Key);

                        if (!n.Contains("Unknown: "))
                        {
                            SendReply(player, n + ": " + perc + "%");
                            unknown -= perc;
                        }
                    }

                    if (unknown > 0)
                        SendReply(player, string.Format(GetMsg("Player: Unknown Percent", player), unknown));
                }
            }
        }

        private void MassProdTurret(BasePlayer player, bool highlight = false)
        {
            object entityObject = FindEntity(player.transform.position, _distanceThreshold);

            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                var total = 0;
                var prodOwners = new Dictionary<ulong, int>();
                SendReply(player, GetMsg("Turrets: Prodding", player));
                var entity = (BaseEntity)entityObject;
                var entityList = new HashSet<BaseEntity>();
                var checkFrom = new List<Vector3>();

                checkFrom.Add(entity.transform.position);

                var current = 0;
                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        if (_debug)
                            SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);

                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    if (current > checkFrom.Count)
                    {
                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    var entities = FindEntities<BaseEntity>(checkFrom[current - 1], _distanceThreshold);

                    foreach (var e in entities)
                    {
                        if (!entityList.Add(e)) continue;
                        if (highlight)
                            SendHighlight(player, e.transform.position);
                        checkFrom.Add(e.transform.position);

                        if (e is AutoTurret)
                        {
                            var turret = (AutoTurret)e;
                            if (turret.OwnerID.IsSteamId())
                            {
                                if (prodOwners.ContainsKey(turret.OwnerID))
                                    prodOwners[turret.OwnerID]++;
                                else
                                    prodOwners.Add(turret.OwnerID, 1);
                            }

                            foreach (var userid in turret.authorizedPlayers)
                            {
                                if (prodOwners.ContainsKey(userid))
                                    prodOwners[userid]++;
                                else
                                    prodOwners.Add(userid, 1);
                            }
                        }
                        else if (e is FlameTurret)
                        {
                            var turret = (FlameTurret)e;
                            if (turret.OwnerID.IsSteamId())
                            {
                                if (prodOwners.ContainsKey(turret.OwnerID))
                                    prodOwners[turret.OwnerID]++;
                                else
                                    prodOwners.Add(turret.OwnerID, 1);
                            }
                        }
                        else
                        {
                            continue;
                        }

                        total++;
                    }

                    Pool.FreeUnmanaged(ref entities);
                }

                var unknown = 100;
                if (total > 0)
                {
                    foreach (var kvp in prodOwners)
                    {
                        var perc = kvp.Value * 100 / total;
                        var n = FindPlayerName(kvp.Key);

                        if (!n.Contains("Unknown: "))
                        {
                            SendReply(player, n + ": " + perc + "%");
                            unknown -= perc;
                        }
                    }

                    if (unknown > 0)
                        SendReply(player, string.Format(GetMsg("Player: Unknown Percent", player), unknown));
                }
            }
        }

        private void MassCupboardAuthorize(BasePlayer player, BasePlayer target)
        {
            object entityObject = FindEntity(player.transform.position, _distanceThreshold);

            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                var total = 0;
                SendReply(player, GetMsg("Cupboards: Authorizing", player));
                var entity = (BaseEntity)entityObject;
                var entityList = new HashSet<BaseEntity>();
                var checkFrom = new List<Vector3>();

                checkFrom.Add(entity.transform.position);

                var current = 0;
                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        if (_debug)
                            SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);

                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    if (current > checkFrom.Count)
                    {
                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    var entities = FindEntities<BuildingPrivlidge>(checkFrom[current - 1], _cupboardDistanceThreshold);

                    foreach (var priv in entities)
                    {
                        if (!entityList.Add(priv)) continue;
                        checkFrom.Add(priv.transform.position);
                        if (HasCupboardAccess(priv, target)) continue;
                        priv.authorizedPlayers.Add((ulong)target.userID);

                        priv.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                        total++;
                    }

                    Pool.FreeUnmanaged(ref entities);
                }

                SendReply(player, string.Format(GetMsg("Cupboards: Authorized", player), target.displayName, total));
            }
        }

        private void MassCupboardDeauthorize(BasePlayer player, BasePlayer target)
        {
            object entityObject = FindEntity(player.transform.position, _distanceThreshold);

            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                var total = 0;
                SendReply(player, GetMsg("Cupboards: Deauthorizing", player));
                var entity = (BaseEntity)entityObject;
                var entityList = new HashSet<BaseEntity>();
                var checkFrom = new List<Vector3>();

                checkFrom.Add(entity.transform.position);

                var current = 0;
                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        if (_debug)
                            SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);

                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    if (current > checkFrom.Count)
                    {
                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    var entities = FindEntities<BuildingPrivlidge>(checkFrom[current - 1], _cupboardDistanceThreshold);

                    foreach (var priv in entities)
                    {
                        if (!entityList.Add(priv)) continue;
                        checkFrom.Add(priv.transform.position);

                        if (!HasCupboardAccess(priv, target)) continue;
                        if (priv.authorizedPlayers.Remove((ulong)target.userID))
                            priv.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                        total++;
                    }

                    Pool.FreeUnmanaged(ref entities);
                }

                SendReply(player, string.Format(GetMsg("Cupboard: Deauthorized", player), target.displayName, total));
            }
        }

        private void MassTurretAuthorize(BasePlayer player, BasePlayer target)
        {
            object entityObject = FindEntity(player.transform.position, _distanceThreshold);

            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                var total = 0;
                SendReply(player, GetMsg("Turrets: Authorizing", player));
                var entity = (BaseEntity)entityObject;
                var entityList = new HashSet<BaseEntity>();
                var checkFrom = new List<Vector3>();

                checkFrom.Add(entity.transform.position);

                var current = 0;
                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        if (_debug)
                            SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);

                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    if (current > checkFrom.Count)
                    {
                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    var entities = FindEntities<BaseEntity>(checkFrom[current - 1], _distanceThreshold);

                    foreach (var e in entities)
                    {
                        if (!entityList.Add(e)) continue;
                        checkFrom.Add(e.transform.position);

                        var turret = e as AutoTurret;
                        if (turret == null || HasTurretAccess(turret, target)) continue;
                        turret.authorizedPlayers.Add((ulong)target.userID);

                        turret.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                        turret.SetTarget(null);
                        total++;
                    }

                    Pool.FreeUnmanaged(ref entities);
                }

                SendReply(player, string.Format(GetMsg("Turrets: Authorized", player), target.displayName, total));
            }
        }

        private void MassTurretDeauthorize(BasePlayer player, BasePlayer target)
        {
            object entityObject = FindEntity(player.transform.position, _distanceThreshold);

            if (entityObject is bool)
            {
                SendReply(player, GetMsg("Entities: None", player));
            }
            else
            {
                var total = 0;
                SendReply(player, GetMsg("Turrets: Deauthorizing", player));
                var entity = (BaseEntity)entityObject;
                var entityList = new HashSet<BaseEntity>();
                var checkFrom = new List<Vector3>();

                checkFrom.Add(entity.transform.position);

                var current = 0;
                while (true)
                {
                    current++;
                    if (current > _entityLimit)
                    {
                        if (_debug)
                            SendReply(player, GetMsg("Target: Limit", player) + " " + _entityLimit);

                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    if (current > checkFrom.Count)
                    {
                        SendReply(player, string.Format(GetMsg("Ownership: Count", player), total));
                        break;
                    }

                    var entities = FindEntities<BaseEntity>(checkFrom[current - 1], _distanceThreshold);

                    foreach (var e in entities)
                    {
                        if (!entityList.Add(e)) continue;
                        checkFrom.Add(e.transform.position);

                        var turret = e as AutoTurret;
                        if (turret == null || !HasTurretAccess(turret, target)) continue;
                        if (turret.authorizedPlayers.Remove((ulong)target.userID))
                        {
                            turret.SetTarget(null);
                            total++;
                        }

                        turret.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    }

                    Pool.FreeUnmanaged(ref entities);
                }

                SendReply(player, string.Format(GetMsg("Turrets: Deauthorized", player), target.displayName, total));
            }
        }

        private bool TryGetCupboardUserNames(BuildingPrivlidge cupboard, out List<string> names)
        {
            names = new List<string>();
            if (cupboard.authorizedPlayers == null)
                return false;
            if (cupboard.authorizedPlayers.Count == 0)
                return false;

            foreach (var userid in cupboard.authorizedPlayers)
                names.Add(FindPlayerName(userid) + " - " + userid);

            return true;
        }

        private bool TryGetTurretUserNames(AutoTurret turret, out List<string> names)
        {
            names = new List<string>();
            if (turret.authorizedPlayers == null)
                return false;
            if (turret.authorizedPlayers.Count == 0)
                return false;

            foreach (var userid in turret.authorizedPlayers)
                names.Add(FindPlayerName(userid) + " - " + userid);

            return true;
        }

        private bool HasCupboardAccess(BuildingPrivlidge cupboard, BasePlayer player)
        {
            return cupboard.IsAuthed(player);
        }

        private bool HasTurretAccess(AutoTurret turret, BasePlayer player)
        {
            return turret.IsAuthed(player);
        }

        private string GetOwnerName(BaseEntity entity)
        {
            return FindPlayerName(entity.OwnerID);
        }

        private BasePlayer GetOwnerPlayer(BaseEntity entity)
        {
            if (entity.OwnerID.IsSteamId())
                return BasePlayer.FindByID(entity.OwnerID);

            return null;
        }

        private void RemoveOwner(BaseEntity entity)
        {
            entity.OwnerID = 0;
        }

        private void ChangeOwner(BaseEntity entity, object player)
        {
            var oldOwner = GetOwnerDisplayName(entity);
            if (string.IsNullOrEmpty(oldOwner))
                oldOwner = "N/A";

            if (player is BasePlayer)
                entity.OwnerID = (ulong)((BasePlayer)player).userID;
            else if (player is ulong && ((ulong)player).IsSteamId())
                entity.OwnerID = (ulong)player;
            else if (player is string)
            {
                ulong id;
                var basePlayer = BasePlayer.Find((string)player);

                if (ulong.TryParse((string)player, out id) && id.IsSteamId())
                    entity.OwnerID = id;
                else if (basePlayer is BasePlayer)
                    entity.OwnerID = (ulong)basePlayer.userID;
            }

            var newOwner = GetOwnerDisplayName(entity);
            if (string.IsNullOrEmpty(newOwner))
                newOwner = "N/A";

            if (_debug)
                Debug.Log("[EntityOwner] Changed owner of [" + entity.ShortPrefabName + "] from [" + oldOwner + "] to [" + newOwner + "]");
        }

        private object FindEntityData(BaseEntity entity)
        {
            if (!entity.OwnerID.IsSteamId())
                return false;

            return entity.OwnerID.ToString();
        }

        #endregion

        #region Utility Methods

        private object RaycastAll<T>(Vector3 position, Vector3 aim) where T : BaseEntity
        {
            var hits = Physics.RaycastAll(position, aim);
            GamePhysics.Sort(hits);
            var distance = 100f;
            object target = false;
            foreach (var hit in hits)
            {
                var ent = hit.GetEntity();
                if (ent is T && hit.distance < distance)
                {
                    target = ent;
                    break;
                }
            }

            return target;
        }

        private object RaycastAll<T>(Ray ray) where T : BaseEntity
        {
            var hits = Physics.RaycastAll(ray);
            GamePhysics.Sort(hits);
            var distance = 100f;
            object target = false;
            foreach (var hit in hits)
            {
                var ent = hit.GetEntity();
                if (ent is T && hit.distance < distance)
                {
                    target = ent;
                    break;
                }
            }

            return target;
        }

        private object FindBuilding(Vector3 position, float distance = 3f)
        {
            var hit = FindEntity<BuildingBlock>(position, distance);

            if (hit != null)
                return hit;

            return false;
        }

        private object FindEntity(Vector3 position, float distance = 3f, params string[] filter)
        {
            var hit = FindEntity<BaseEntity>(position, distance, filter);

            if (hit != null)
                return hit;

            return false;
        }

        private T FindEntity<T>(Vector3 position, float distance = 3f, params string[] filter) where T : BaseEntity
        {
            var list = Pool.Get<List<T>>();
            Vis.Entities(position, distance, list, _layerMasks);

            if (list.Count > 0)
            {
                foreach (var e in list)
                {
                    if (filter != null && filter.Length > 0)
                    {
                        foreach (var f in filter)
                        {
                            if (e.name.Contains(f))
                                return e;
                        }
                    }
                    else
                    {
                        return e;
                    }
                }

                Pool.FreeUnmanaged(ref list);
            }

            return null;
        }

        private List<T> FindEntities<T>(Vector3 position, float distance = 3f) where T : BaseEntity
        {
            var list = Pool.Get<List<T>>();
            Vis.Entities(position, distance, list, _layerMasks);
            return list;
        }

        public List<BuildingBlock> GetProfileConstructions(BasePlayer player)
        {
            var result = new List<BuildingBlock>();
            var blocks = UnityEngine.Object.FindObjectsOfType<BuildingBlock>();
            foreach (var block in blocks)
            {
                if (block.OwnerID == (ulong)player.userID)
                    result.Add(block);
            }

            return result;
        }

        public List<BaseEntity> GetProfileDeployables(BasePlayer player)
        {
            var result = new List<BaseEntity>();
            var entities = UnityEngine.Object.FindObjectsOfType<BaseEntity>();
            foreach (var entity in entities)
            {
                if (entity.OwnerID == (ulong)player.userID && !(entity is BuildingBlock))
                    result.Add(entity);
            }

            return result;
        }

        public void ClearProfile(BasePlayer player)
        {
            var entities = UnityEngine.Object.FindObjectsOfType<BaseEntity>();
            foreach (var entity in entities)
            {
                if (entity.OwnerID == (ulong)player.userID && !(entity is BuildingBlock))
                    RemoveOwner(entity);
            }
        }

        private string FindPlayerName(ulong playerID)
        {
            if (playerID.IsSteamId())
            {
                var foundPlayer = FindPlayerByPartialName(playerID.ToString());
                if (foundPlayer)
                {
                    if (foundPlayer.IsSleeping())
                        return foundPlayer.displayName + " [<color=#ADD8E6>Sleeping</color>]";

                    return foundPlayer.displayName + " [<color=#32CD32>Online</color>]";
                }

                string offlineName = ServerMgr.Instance?.persistance?.GetPlayerName(playerID);
                if (!string.IsNullOrEmpty(offlineName))
                    return offlineName + " [<color=#FF0000>Offline</color>]";
            }

            return "Unknown: " + playerID;
        }

        private string GetOwnerDisplayName(BaseEntity entity)
        {
            var playerID = entity.OwnerID;

            if (playerID.IsSteamId())
            {
                var foundPlayer = FindPlayerByPartialName(playerID.ToString());
                if (foundPlayer)
                    return foundPlayer.displayName;

                string offlineName = ServerMgr.Instance?.persistance?.GetPlayerName(playerID);
                if (!string.IsNullOrEmpty(offlineName))
                    return offlineName;
            }

            return "Unknown: " + playerID;
        }

        private ulong FindUserIDByPartialName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            if (ulong.TryParse(name, out ulong userId))
                return userId;

            var p = FindPlayerByPartialName(name);
            return p != null ? (ulong)p.userID : 0;
        }

        private BasePlayer FindPlayerByPartialName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            if (ulong.TryParse(name, out ulong userId))
                return BasePlayer.FindAwakeOrSleepingByID(userId);

            return BasePlayer.FindAwakeOrSleeping(name);
        }

        private static string[] SkipFirst(string[] args)
        {
            if (args == null || args.Length <= 1)
                return Array.Empty<string>();

            var result = new string[args.Length - 1];
            for (int i = 1; i < args.Length; i++)
                result[i - 1] = args[i];
            return result;
        }

        #endregion
    }

    /// <summary>Embedded messages + optional HarmonyLanguage/EntityOwner.json overrides.</summary>
    internal sealed class LangStore
    {
        private readonly Dictionary<string, string> _en = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _fileLoaded;

        public void RegisterMessages(Dictionary<string, string> messages, string language)
        {
            if (messages == null) return;
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var kv in messages)
                _en[kv.Key] = kv.Value ?? "";
        }

        public void LoadHarmonyLanguageOverrides(string serverRoot, string modName)
        {
            if (_fileLoaded) return;
            _fileLoaded = true;
            try
            {
                string path = Path.Combine(serverRoot, "HarmonyLanguage", modName + ".json");
                if (!File.Exists(path)) return;
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (map == null || map.Count == 0) return;
                foreach (var kv in map)
                    _en[kv.Key] = kv.Value ?? "";
                Debug.Log("[EntityOwner] Loaded " + map.Count + " language strings from HarmonyLanguage/" + modName + ".json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EntityOwner] HarmonyLanguage load failed: " + ex.Message);
            }
        }

        public string GetMessage(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return _en.TryGetValue(key, out string msg) && !string.IsNullOrEmpty(msg) ? msg : key;
        }
    }
}
