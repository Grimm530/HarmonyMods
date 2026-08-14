/*
< ----- End-User License Agreement ----->

This software and all associated files (“Software”) are the intellectual property of the Developer.  
By installing, loading, or using this Software, you agree to the following terms:

1. You may not merge, publish, redistribute, sublicense, or sell this Software or any modified versions of it without the Developer’s explicit written consent.

2. You may copy or modify the Software **only for personal, private use on servers you own or operate**.  
   Distribution of modified or unmodified versions to any third party is strictly prohibited.

3. THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDER AND CONTRIBUTORS "AS IS" AND WITHOUT WARRANTY OF ANY KIND, 
   EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT.
   
4. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES 
   (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) 
   HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
   ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Developer: Grimm530 (r3ap3rsg@gmail.com)

Copyright © Grimm530. All rights reserved.
*/
using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Configuration;
using UnityEngine;
using Network;
using Network.Visibility;
using Facepunch;
using Facepunch.Extend;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HarmonyLib;

namespace Oxide.Plugins
{
    [Info("UpLifted", "Grimm530", "1.2.9", ResourceId = 110)]
    // Thanks to the original author, FuJiCuRa.
    public partial class UpLifted : RustPlugin
    {
        #region Fields - Plugin References & Core State

        [PluginReference] private Plugin Clans, Friends;
        private static UpLifted UpL;
        private bool initialized;
        private bool Changed;
        private bool hasFullyLoaded;
        private double lastHour;
        private static bool isDayTime;
        private int versionMajor;
        private int versionMinor;
        private bool _newConfig = false;
        private static Dictionary<string, bool> adminAccessEnabled = new Dictionary<string, bool>();
        private static Dictionary<string, bool> adminLiftEntkill = new Dictionary<string, bool>();
        private static Dictionary<NetworkableId, Elevator> crossReference;
        private Dictionary<ulong, Elevator> playerStartups;
        private HashSet<ulong> _persistedElevatorIds = new HashSet<ulong>();
        private List<string> permissionGroups = new List<string>();
        private List<ConsoleSystem.Command> aclVars;
        private static WaitForFixedUpdate waitFFU = new WaitForFixedUpdate();
        private static WaitForEndOfFrame waitFOF = new WaitForEndOfFrame();
        private static string cabinControlUI = "CabinControlUI";
        private static string cabinDestroyUI = "CabinDestroyUI";
        private static string cabinSettingsUI = "CabinSettingsUI";
        private static string cabinPlacementUI = "CabinPlacementUI";
        private static string cabinSharingUI = "CabinSharingUI";

        #endregion

        #region Fields - Permission & Config Data

        private Dictionary<string, object> permissionBlock = new Dictionary<string, object>();

        private Dictionary<string, object> playerPermissionMatrix = new Dictionary<string, object>
        {
            {"MaxLiftPerUser", new object[] {-1, 1, 2, "The number of lifts a player can create"}},
            {"MaxMoveSpeed", new object[] {5, 1, 3, "The maximum speed being available to choose"}},
            {"MaxPlacementFloor", new object[] {-1, 2, 3, "The last floor a player can place a lift on"}},
            {"MaxFloorRange", new object[] {15, 3, 7, "The from-to-range a player can place a lift"}},
            {"MaxFloorLevel", new object[] {-1, 5, 10, "The last floor a player can place a lift to"}},
            {"FuelConsumePerFloor", new object[] {1, 5, 3, "The amount of consumed fuel per moved floor"}},
            {"FuelStorageItemSlots", new object[] {6, 1, 3, "The available slots for the central fuel storage"}},
            {"FuelStorageStackSize", new object[] {0, 500, 1000, "The stacksize limit for the central fuel storage"}},
            {"DoorSkin", new object[] {3560088598uL, 3560088598uL, 3560088598uL, "The default skin for new placements"}},
            {"BuildCostMultiplier", new object[] {1.0f, 2.0f, 1.5f, "The buildcost multiplier for build and remove"}},
            {"EnableBuildCost", new object[] {false, true, true, "Enable or disable buildcosts for a player"}},
            {"AllowDoorItemOrCost", new object[] {true, false, true, "If true, player can pay door cost with vanilla garage door items (1 per door) instead of materials"}},
            {"AllowElevatorItemPerFloor", new object[] {true, false, true, "If true, player can pay floor cost with vanilla elevator items (1 per floor) instead of materials"}},
            {"EnableFuelUsage", new object[] {false, true, true, "Enable or disable fuel usage for a player"}},
            {"CanCreate", new object[] {true, false, true, "Needed option to allow the placement of lifts"}},
            {"CanReskin", new object[] {true, false, true, "Needed option to allow door reskinning"}},
            {"CanPlaceOnFloor", new object[] {true, false, false, "Needed option to place on floors also"}},
            {"AccessVipShare", new object[] {true, false, false, "Needed option to allow access to VIP shares"}},
            {"BaseComfort", new object[] {100, 0, 25, "The cabin's enabled comfort level from 0 to 100"}},
            {"BaseTemperature", new object[] {34, 0, 17, "The cabin's enabled temperature level from 34 to 0"}},
        };

        #endregion

        #region Configuration

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            Dictionary<string, object> data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                Changed = true;
            }

            if (!data.TryGetValue(datavalue, out object value))
            {
                value = defaultValue;
                data[datavalue] = value;
                Changed = true;
            }

            return value;
        }

        protected override void LoadDefaultConfig()
        {
            _newConfig = true;
            Config.Clear();
            GetConfig("BuildCostItems", "HijackVanillaElevatorPlacement", false);
            GetConfig("Debug", "DebugBuildingBlock", false);
            LoadVariables();
        }

        private string msg(string key, string id = null)
        {
            return lang.GetMessage(key, this, id);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    {
                        "Couldn't use the RemoverTool on a Lift installation",
                        "Couldn't use the RemoverTool on a Lift installation"
                    },
                    {
                        "Couldn't add a home inside a Lift installation",
                        "Couldn't add a home inside a Lift installation"
                    },
                    {"Entkill disabled for Lifts. Toggle this by: ", "Entkill disabled for Lifts. Toggle this by: "},
                    {
                        "No build or place access inside a Lift installation",
                        "No build or place access inside a Lift installation"
                    },
                    {"You are not permitted for this command", "You are not permitted for this command"},
                    {"You are building blocked on that position", "You are building blocked on that position"},
                    {"You cannot place on deployed objects", "You cannot place on deployed objects"},
                    {"You do not own this foundation", "You do not own this foundation"},
                    {"You cannot place anymore on this level", "You cannot place anymore on this level"},
                    {"You reached your placement limit!", "You reached your placement limit!"},
                    {
                        "The foundation needs to be at least of grade Stone",
                        "The foundation needs to be at least of grade Stone"
                    },
                    {
                        "You cannot build under the terrain/caves/cliffs",
                        "You cannot build under the terrain/caves/cliffs"
                    },
                    {"Too close by another installed lift", "Too close by another installed lift"},
                    {"You can place on foundations only", "You can place on foundations only"},
                    {"Choose the planned Lift size", "Choose the planned Lift size"},
                    {"Choose where to pre-place doors", "Choose where to pre-place doors"},
                    {"You reached your placement limit", "You reached your placement limit"},
                    {"Lift admin access enabled", "Lift admin access enabled"},
                    {"Lift admin access disabled", "Lift admin access disabled"},
                    {"Lift admin entkill enabled", "Lift admin entkill enabled"},
                    {"Lift admin entkill disabled", "Lift admin entkill disabled"},
                    {"This lift was sucessfully reset", "This lift was sucessfully reset"},
                    {"Nothing valid to reset was found", "Nothing valid to reset was found"},
                    {
                        "The new lift failed for some reason. Please try from a lower floor",
                        "The new lift failed for some reason. Please try from a lower floor"
                    },
                    {"Do Placement", "Do Placement"}, {"Do Place (Upward Overwrite)", "Do Place (Upward Overwrite)"},
                    {"Cancel Action", "Cancel Action"}, {"Cancel", "Cancel"}, {"Tear down\nLift", "Tear down\nLift"},
                    {"Remove Lift", "Remove Lift"},
                    {"Operating\nPanel", "Operating\nPanel"}, {"Lighting", "Lighting"}, {"Move Speed", "Move Speed"},
                    {"Floor OnHold", "Floor OnHold"}, {"Idle Close", "Idle Close"}, {"Power State", "Power State"},
                    {"Clan Share", "Clan Share"}, {"FriendShare", "FriendShare"}, {"Door Skin", "Door Skin"},
                    {"Update", "Update"}, {"Bad", "Bad"}, {"Good", "Good"}, {"Or", "Or"}, {"FoC", "FoC"}, {"Door", "Door"},
                    {"Elevators", "Elevators"}, {"Garage Doors", "Garage Doors"},
                    {"Floor", "Floor"}, {"Current", "Current"}, {"Static", "Static"}, {"Share Mode", "Share Mode"},
                    {"Floor Sharing\nOptions", "Floor Sharing\nOptions"}, {"Switch\nAll Floors", "Switch\nAll Floors"},
                    {"Reset\nAll Floors", "Reset\nAll Floors"}, {"Settings\nPanel", "Settings\nPanel"},
                    {"PowerState.Internal", "Internal"}, {"PowerState.TC", "TC"}, {"PowerState.FoC", "FoC"},
                    {"CabinLightMode.Dynamic", "Dynamic"}, {"CabinLightMode.AlwaysOn", "AlwaysOn"},
                    {"CabinLightMode.Disabled", "Disabled"}, {"ShareMode.None", "None"}, {"ShareMode.Clan", "Clan"},
                    {"ShareMode.Friends", "Friends"}, {"ShareMode.Full", "Full"}, {"ShareMode.Public", "Public"},
                    {"FloorShare.Public", "Public"}, {"FloorShare.Full", "Full"}, {"FloorShare.Friends", "Friends"},
                    {"FloorShare.Clan", "Clan"}, {"FloorShare.Excluded", "Excluded"}, {"FloorShare.Hidden", "Hidden"},
                    {"FloorShare.HiddenVIP", "HiddenVIP"},
                }, this);
        }

        #endregion

        #region Fields - Config Variables

        private bool loadOverrideCorrupted;
        private string createChatCommand;
        private string helpChatCommand;
        private string pluginPrefix;
        private string prefixColor;
        private string prefixFormat;
        private int buildingGrade = 3;
        private static string knockEffectGuid;
        private int lastStableFloor;
        private static bool enableClanShare;
        private static bool enableFriendShare;
        private static bool enableTeamShare;
        private static bool clansEnabled;
        private static bool friendsEnabled;
        private bool sharingPlugins;
        private bool enableEntIdKillProtection;
        private bool enableRemoverToolProtection;
        private bool enableSetHomeDeny;
        private static bool isGroundBlockVulnerable;
        private static bool doorDestructionKillsLiftCabin;
        private static bool sleepWatchEnabled;
        private static float sleepWatchDelay;
        private static float sleepWatchInterval;
        private static bool sleepWatchMoveDown;
        private static bool preventPlacementInCaves;
        private static bool preventPlacementAboveDeployables;
        private bool admAccessSwitchable;
        private bool admSwitchEnabledAtLogin;
        private string admAccessToggleCmd;
        private int admAccessAuthLevel;
        private string admAccessPermission;
        private int defaultMoveSpeed;
        private List<object> admPseudoPerms = new List<object>();
        private List<string> pseudoPerms = new List<string>();
        private static bool hijackVanillaElevatorPlacement;
        private static string elevatorItemShortname;
        private static int elevatorItemsPerFloor;
        private static bool useElevatorAsCost;
        private const uint VanillaElevatorPrefabId = 3978222077u;
        private static bool debugBuildingBlock;
        private static bool verboseElevatorLogs;

        #endregion

        #region Load Variables

        private void LoadVariables()
        {
            versionMinor = Convert.ToInt32(GetConfig("Debug", "VersionMinor", -1));
            if (!_newConfig && versionMinor < 1)
            {
                Config.Save(Manager.ConfigPath + string.Format("\\{0}_OLD.json", Name));
                PrintWarning("Created a copy of the old config");
                Config.Clear();
                Config["Debug", "VersionMinor"] = Version.Minor;
                Config["Debug", "VersionMajor"] = Version.Major;
                Changed = true;
            }

            if (versionMinor == -1)
            {
                Config["Debug", "VersionMinor"] = Version.Minor;
                Changed = true;
            }

            buildingGrade = Mathf.Clamp(Convert.ToInt32(GetConfig("Walls", "Grade (0 -> 4, (0 = Twigs 4 = Armoured))", 3)), 0, 4);
            versionMajor = Convert.ToInt32(GetConfig("Debug", "VersionMajor", Version.Major));
            versionMinor = Convert.ToInt32(GetConfig("Debug", "VersionMinor", Version.Minor));
            loadOverrideCorrupted = Convert.ToBoolean(GetConfig("Debug", "LoadOverrideCorrupted", false));
            debugBuildingBlock = Convert.ToBoolean(GetConfig("Debug", "DebugBuildingBlock", false));
            verboseElevatorLogs = Convert.ToBoolean(GetConfig("Debug", "VerboseElevatorLogs", false));
            permissionBlock = (Dictionary<string, object>) GetConfig("Permission", "AccessControl", new Dictionary<string, object>());
            createChatCommand = Convert.ToString(GetConfig("Commands", "Start lift creation", "newlift"));
            helpChatCommand = Convert.ToString(GetConfig("Commands", "Reset lift movement", "liftaid"));
            pluginPrefix = Convert.ToString(GetConfig("Formatting", "PluginPrefix", "Up·Lift·ed"));
            prefixColor = Convert.ToString(GetConfig("Formatting", "PrefixColor", "#468499"));
            prefixFormat = Convert.ToString(GetConfig("Formatting", "PrefixFormat", "<color={0}>{1}</color>: "));
            enableEntIdKillProtection = Convert.ToBoolean(GetConfig("HealthCare", "EnableEntIdKillProtection", true));
            enableRemoverToolProtection = Convert.ToBoolean(GetConfig("HealthCare", "EnableRemoverToolProtection", true));
            enableSetHomeDeny = Convert.ToBoolean(GetConfig("HealthCare", "EnableSetHomeDeny", true));
            isGroundBlockVulnerable = Convert.ToBoolean(GetConfig("HealthCare", "IsGroundBlockVulnerable", true));
            doorDestructionKillsLiftCabin = Convert.ToBoolean(GetConfig("HealthCare", "DoorDestructionKillsLiftCabin", true));
            lastStableFloor = Convert.ToInt32(GetConfig("HealthCare", "LastStableFloor", 18));
            enableClanShare = Convert.ToBoolean(GetConfig("Support", "EnableClanSharing", true));
            enableFriendShare = Convert.ToBoolean(GetConfig("Support", "EnableFriendSharing", true));
            enableTeamShare = Convert.ToBoolean(GetConfig("Support", "EnableTeamSharing", true));
            sleepWatchEnabled = Convert.ToBoolean(GetConfig("AbuseControl", "SleepWatchEnabled", true));
            sleepWatchDelay = Convert.ToSingle(GetConfig("AbuseControl", "SleepWatchDelay", 180));
            sleepWatchInterval = Convert.ToSingle(GetConfig("AbuseControl", "SleepWatchInterval", 60));
            sleepWatchMoveDown = Convert.ToBoolean(GetConfig("AbuseControl", "SleepWatchMoveDown", true));
            preventPlacementInCaves = Convert.ToBoolean(GetConfig("AbuseControl", "PreventPlacementInCaves", true));
            preventPlacementAboveDeployables =
                Convert.ToBoolean(GetConfig("AbuseControl", "PreventPlacementAboveDeployables", true));
            admAccessSwitchable = Convert.ToBoolean(GetConfig("Administrative", "AdmAccessSwitchable", true));
            admSwitchEnabledAtLogin = Convert.ToBoolean(GetConfig("Administrative", "AdmSwitchEnabledAtLogin", false));
            admAccessToggleCmd = Convert.ToString(GetConfig("Administrative", "AdmAccessToggleCmd", "liftadmin"));
            admAccessAuthLevel = Convert.ToInt32(GetConfig("Administrative", "AdmAccessAuthLevel", 2));
            admAccessPermission = Convert.ToString(GetConfig("Administrative", "AdmAccessPermission", "admaccess"));
            admPseudoPerms = (List<object>) GetConfig("Administrative", "AdmPseudoPerms",
                new List<object> {"fauxadm.allowed", "fakeadmin.allow"});
            defaultMoveSpeed = Convert.ToInt32(GetConfig("Defaults", "defaultMoveSpeed", 1));
            hijackVanillaElevatorPlacement = Convert.ToBoolean(GetConfig("BuildCostItems", "HijackVanillaElevatorPlacement", false));
            elevatorItemShortname = Convert.ToString(GetConfig("BuildCostItems", "ElevatorItemShortname", "elevator"));
            elevatorItemsPerFloor = Mathf.Clamp(Convert.ToInt32(GetConfig("BuildCostItems", "ElevatorItemsPerFloor", 1)), 1, 100);
            useElevatorAsCost = Convert.ToBoolean(GetConfig("BuildCostItems", "UseElevatorAsCost", true));
            if (!Changed) return;
            SaveConfig();
            Changed = false;
        }

        #endregion

        #region Lifecycle - Init, Unload, Plugin Loaded

        private void Init()
        {
            LoadVariables();
            LoadDefaultMessages();
            permission.RegisterPermission(Title + "." + admAccessPermission, this);
            cmd.AddChatCommand(createChatCommand, this, "chatCmdTowerLift");
            cmd.AddChatCommand(helpChatCommand, this, "chatCmdLiftReset");
            cmd.AddChatCommand(admAccessToggleCmd, this, "chatCmdToggleAdm");
            cmd.AddConsoleCommand("upl." + admAccessToggleCmd, this, "cmdToggleAdm");
            cmd.AddChatCommand("liftowner", this, "chatCmdLiftOwner");
            UpL = this;
            hasFullyLoaded = false;
            aclVars = new List<ConsoleSystem.Command>();
        }

        private void SaveAllElevators(bool isUnload = false)
        {
            if (crossReference == null || crossReference.Count == 0) return;
            Dictionary<string, ELStorage> toJson = new Dictionary<string, ELStorage>();
            int ct1 = 0, ct2 = 0;
            foreach (var kv in crossReference)
            {
                Elevator mapElevator = kv.Value;
                if (mapElevator == null || mapElevator.lift == null || mapElevator.lift.IsDestroyed) continue;
                if (kv.Key.Value != mapElevator.lift.net.ID.Value) continue;
                ct1++;
                if (mapElevator.IsReady)
                {
                    ELStorage data = mapElevator.Sv2Prt(out bool success, isUnload);
                    if (success)
                    {
                        toJson[mapElevator.lift.net.ID.Value.ToString()] = data;
                        ct2++;
                    }
                    else
                        PrintWarning("Excluded damaged entry from being saved");
                }
            }
            if (toJson.Count > 0)
            {
                WriteDataFileReadable(toJson);
                Puts($"Saved '{ct2} of {ct1}' Lifts to disk" + (isUnload ? " (unload)" : ""));
            }
        }

        internal static bool PluginUnloadInProgress;

        private void Unload()
        {
                SaveAllElevators(isUnload: true);
            foreach (ConsoleSystem.Command cmd in new List<ConsoleSystem.Command>(aclVars))
                ConsoleSystem.Index.Server.Dict.Remove(cmd.FullName?.ToLower());
            foreach (BasePlayer player in new List<BasePlayer>(BasePlayer.activePlayerList)) LiftUI.DestroyAllUi(player);
            if (crossReference != null)
            {
                PluginUnloadInProgress = true;
                List<Elevator> toDestroy = Facepunch.Pool.Get<List<Elevator>>();
                try
                {
                    toDestroy.AddRange(crossReference.Values);
                    foreach (Elevator obj in toDestroy)
                    {
                        if (obj == null) continue;
                        CabinComfort cc = obj.GetCabinComfortForCleanup();
                        if (cc != null) UnityEngine.Object.DestroyImmediate(cc);
                        obj.enabled = false;
                        // Immediate: deferred Destroy leaves component until next frame; reload then hits
                        // "SKIP: lift already has Elevator" and never repopulates crossReference.
                        UnityEngine.Object.DestroyImmediate(obj);
                    }
                }
                finally
                {
                    PluginUnloadInProgress = false;
                    Facepunch.Pool.FreeUnmanaged(ref toDestroy);
                }
            }
        }

        private void OnPluginLoaded(Plugin name)
        {
            if (!initialized || name.Name == Title) return;
            if (name.Name == "Clans" && enableClanShare) clansEnabled = true;
            if (name.Name == "Friends" && enableFriendShare) friendsEnabled = true;
            if (clansEnabled || friendsEnabled) sharingPlugins = true;
        }

        private void OnPluginUnloaded(Plugin name)
        {
            if (!initialized || name.Name == Title) return;
            if (name.Name == "Clans") clansEnabled = false;
            if (name.Name == "Friends") friendsEnabled = false;
            if (!clansEnabled && !friendsEnabled) sharingPlugins = false;
        }

        #endregion

        #region OnServerInitialized & Data Loading

        private void OnServerInitialized()
        {
            CreateAcl();
            CreateOverview();
            crossReference = new Dictionary<NetworkableId, Elevator>();
            playerStartups = new Dictionary<ulong, Elevator>();
            foreach (string pseudoPerm in admPseudoPerms.ConvertAll(obj => Convert.ToString(obj)))
                if (permission.PermissionExists(pseudoPerm))
                    pseudoPerms.Add(pseudoPerm.ToLower());
            adminLiftEntkill = new Dictionary<string, bool>();
            adminAccessEnabled = new Dictionary<string, bool>();
            
            foreach (BasePlayer player in new List<BasePlayer>(BasePlayer.activePlayerList)) SetPlayer(player);
            foreach (BasePlayer player in new List<BasePlayer>(BasePlayer.sleepingPlayerList)) SetPlayer(player);
            
            if (TOD_Sky.Instance.Cycle.Hour > TOD_Sky.Instance.SunriseTime + 1.5 &&
                TOD_Sky.Instance.Cycle.Hour < TOD_Sky.Instance.SunsetTime - 1.5) isDayTime = true;
            else isDayTime = false;
            
            lastHour = -1d;
            if (Clans && enableClanShare) clansEnabled = true;
            if (Friends && enableFriendShare) friendsEnabled = true;
            initialized = true;
            Dictionary<string, ELStorage> fromJson = ReadDataFileReadable();
            _persistedElevatorIds = new HashSet<ulong>();
            int ct1 = 0;
            int ct2 = 0;

            foreach (KeyValuePair<string, ELStorage> storedElev in fromJson)
            {
                ct1++;
                if (!ulong.TryParse(storedElev.Key, out ulong liftNetId)) continue;
                _persistedElevatorIds.Add(liftNetId);
                ELStorage data = storedElev.Value;
                if (data == null) continue;
                if (verboseElevatorLogs) Puts($"[UpLifted Load] Lift NetID (from file): {liftNetId}");
                BaseNetworkable ent = BaseNetworkable.serverEntities.Find(new NetworkableId(liftNetId));
                if (ent == null)
                {
                    if (verboseElevatorLogs) Puts($"[UpLifted Load] SKIP: lift entity not found (destroyed or wrong save)");
                    continue;
                }
                ProceduralLift procLift = ent as ProceduralLift;
                if (procLift == null) { if (verboseElevatorLogs) Puts($"[UpLifted Load] SKIP: entity is not ProceduralLift"); continue; }
                Elevator[] stale = procLift.gameObject.GetComponents<Elevator>();
                if (stale != null && stale.Length > 0)
                {
                    if (verboseElevatorLogs)
                        Puts($"[UpLifted Load] Removing {stale.Length} stale Elevator component(s) on lift {liftNetId} (reload / deferred destroy)");
                    foreach (Elevator s in stale)
                    {
                        if (s == null) continue;
                        s.enabled = false;
                        UnityEngine.Object.DestroyImmediate(s);
                    }
                }

                if (verboseElevatorLogs) LogLiftConnections(liftNetId, data, false, null, null);
                Elevator elev = procLift.gameObject.AddComponent<Elevator>();
                try
                {
                    bool ldOk = elev.Ld2Prt(procLift, data);
                    if (verboseElevatorLogs) Puts($"[UpLifted Load] Ld2Prt returned {ldOk}");
                    if (ldOk) ct2++;
                }
                catch (Exception ex)
                {
                    if (!loadOverrideCorrupted)
                    {
                        PrintWarning("Loading problem detected at:" + ex.ToString());
                        PrintWarning("Plugin HALTED to protect the datafile. Contact the Developer");
                        Interface.Oxide.UnloadPlugin(UpL.Title);
                        return;
                    }
                    PrintWarning($"Failed loading of lift at '{ent.transform.position.ToString()}': {ex.Message}");
                }
            }

            Puts($"Loaded \'{ct2} of {ct1}\' Lifts from disk");
            hasFullyLoaded = true;
            if (!enableEntIdKillProtection) Unsubscribe(nameof(OnServerCommand));
            if (!enableSetHomeDeny) Unsubscribe(nameof(OnPlayerCommand));
            if (!enableRemoverToolProtection) Unsubscribe(nameof(canRemove));
        }

        #endregion

        #region Hooks - Stability, Save, Server Save

        private object OnEntityStabilityCheck(StabilityEntity stabilityEntity)
        {
            if (stabilityEntity.GetComponentInParent<ProceduralLift>())
            {
                return false;
            }
            return null;
        }

        // Read data file; supports old format (values as strings) and new readable format (values as objects). Key = lift netId as string.
        private Dictionary<string, ELStorage> ReadDataFileReadable()
        {
            var raw = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, object>>(Title) ?? new Dictionary<string, object>();
            var result = new Dictionary<string, ELStorage>();
            foreach (var kvp in raw)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                ELStorage data = null;
                if (kvp.Value is string s)
                {
                    try { data = JsonConvert.DeserializeObject<ELStorage>(s); }
                    catch { /* old format */ }
                }
                else if (kvp.Value is JObject jo)
                    data = jo.ToObject<ELStorage>();
                else if (kvp.Value is ELStorage es)
                    data = es;
                if (data != null)
                    result[kvp.Key] = data;
            }
            return result;
        }

        // Write data file in readable format (each elevator is a full JSON object, not a string).
        private void WriteDataFileReadable(Dictionary<string, ELStorage> data)
        {
            Interface.Oxide.DataFileSystem.WriteObject(Title, data);
        }

        // Save only a single new elevator to disk (called when elevator becomes Ready). Does not run on every server save.
        private void SaveNewElevator(Elevator e)
        {
            if (e?.lift == null || !hasFullyLoaded) return;
            ulong id = e.lift.net.ID.Value;
            if (_persistedElevatorIds.Contains(id)) return;
            _persistedElevatorIds.Add(id);
            ELStorage data = e.Sv2Prt(out bool success, false);
            if (!success)
            {
                _persistedElevatorIds.Remove(id);
                return;
            }
            Dictionary<string, ELStorage> current = ReadDataFileReadable();
            current[id.ToString()] = data;
            WriteDataFileReadable(current);
            Puts($"Saved new elevator {id} to disk");
        }

        private void OnServerSave()
        {
            SaveAllElevators(isUnload: false);
        }

        // Display name for a floor: ground = "gfloor" (long) or "G" (short), others = "1", "2", ...
        public static string GetFloorDisplayName(int floorIndex, int lowerFloor, bool shortForm = false)
        {
            return floorIndex == lowerFloor ? (shortForm ? "G" : "gfloor") : (floorIndex - lowerFloor).ToString();
        }

        // Log connection status for one elevator record so the data file and missing links are clear.
        private void LogLiftConnections(ulong liftNetId, ELStorage data, bool afterLoad, BaseEntity foundGround, Door[] doorsLoaded)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"[UpLifted Connections] Lift NetID (cabin)={liftNetId}");
            BaseNetworkable liftEnt = BaseNetworkable.serverEntities.Find(new NetworkableId(liftNetId));
            sb.Append(liftEnt != null && !(liftEnt?.IsDestroyed ?? true) ? " cabin=FOUND" : " cabin=MISSING");
            sb.Append($" | ground_block_id={data.groundBlockID}");
            BaseEntity g = foundGround ?? (BaseNetworkable.serverEntities.Find(new NetworkableId(data.groundBlockID)) as BaseEntity);
            sb.Append(g != null && !g.IsDestroyed ? " FOUND" : " MISSING");
            if (data.doorsID != null)
            {
                sb.Append(" | doors: ");
                int lowFloor = data.gFloor;
                for (int i = 0; i < data.doorsID.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    ulong did = data.doorsID[i];
                    string floorName = GetFloorDisplayName(i, lowFloor, false);
                    if (did == 0) { sb.Append($"{floorName}=none"); continue; }
                    bool found = afterLoad && doorsLoaded != null && i < doorsLoaded.Length && doorsLoaded[i] != null;
                    if (!found) found = (BaseNetworkable.serverEntities.Find(new NetworkableId(did)) != null);
                    sb.Append($"{floorName}_id={did}{(found ? " OK" : " MISSING")}");
                }
            }
            sb.Append($" | owner_steam_id={data.ownerID}");
            Puts(sb.ToString());
        }

        #endregion

        #region Helpers - Clans & Friends

        private static bool HasFriend(string owner, string friend)
        {
            if (!enableFriendShare || UpL.Friends == null)
                return false;
            object result = UpL.Friends.Call("HasFriend", owner, friend);
            return result is bool b && b;
        }

        private static bool SameTeam(ulong ownerId, ulong memberId)
        {
            if (!enableTeamShare || !RelationshipManager.TeamsEnabled() || ownerId == 0 || memberId == 0)
                return false;
            RelationshipManager.PlayerTeam ownerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(ownerId);
            if (ownerTeam == null)
                return false;
            return ownerTeam.members.Contains(memberId);
        }

        private static bool SameTeamS(string owner, string member)
        {
            if (!ulong.TryParse(owner, out ulong ownerId) || !ulong.TryParse(member, out ulong memberId))
                return false;
            return SameTeam(ownerId, memberId);
        }

        private static bool SameNativeClan(ulong ownerId, ulong memberId)
        {
            if (!enableClanShare || ownerId == 0 || memberId == 0 || ownerId == memberId)
                return ownerId != 0 && ownerId == memberId;
            BasePlayer ownerPlayer = BasePlayer.FindByID(ownerId) ?? BasePlayer.FindSleeping(ownerId);
            BasePlayer memberPlayer = BasePlayer.FindByID(memberId) ?? BasePlayer.FindSleeping(memberId);
            if (ownerPlayer != null && memberPlayer != null && ownerPlayer.clanId != 0 &&
                ownerPlayer.clanId == memberPlayer.clanId)
                return true;
            if (ownerPlayer?.serverClan != null)
            {
                foreach (var member in ownerPlayer.serverClan.Members)
                {
                    if (member.SteamId == memberId)
                        return true;
                }
                if (ownerPlayer.serverClan.Creator == memberId)
                    return true;
            }
            if (memberPlayer?.serverClan != null)
            {
                foreach (var member in memberPlayer.serverClan.Members)
                {
                    if (member.SteamId == ownerId)
                        return true;
                }
                if (memberPlayer.serverClan.Creator == ownerId)
                    return true;
            }
            return false;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null) SetPlayer(player);
        }

        private void SetPlayer(BasePlayer player)
        {
            if (player.Connection != null && player.net.connection.authLevel >= admAccessAuthLevel ||
                IsPseudoAdmin(player.UserIDString) ||
                permission.UserHasPermission(player.UserIDString, admAccessPermission))
            {
                adminAccessEnabled[player.UserIDString] = admAccessSwitchable ? admSwitchEnabledAtLogin : true;
                adminLiftEntkill[player.UserIDString] = false;
            }
        }

        private static bool SameClanS(string owner, string member)
        {
            if (!enableClanShare)
                return false;
            if (UpL.Clans != null)
            {
                object o = UpL.Clans.Call("GetClanOf", owner);
                object m = UpL.Clans.Call("GetClanOf", member);
                if (o != null && m != null && (string) o == (string) m)
                    return true;
            }
            if (!ulong.TryParse(owner, out ulong ownerId) || !ulong.TryParse(member, out ulong memberId))
                return false;
            return SameNativeClan(ownerId, memberId);
        }

        private static bool ClanCheck(string owner, string oldTag, out string newTag)
        {
            newTag = default(string);
            if (clansEnabled)
            {
                object tag = UpL.Clans.Call("GetClanOf", owner);
                if (tag != null) newTag = (string) tag;
                if (newTag != oldTag && oldTag != "") return true;
            }

            return false;
        }

        #endregion

        #region Helpers - Placement & Ground

        private bool CrtPrcdrlLft(BasePlayer player, BaseEntity baseEntity, out ProceduralLift l, out int offset)
        {
            l = default(ProceduralLift);
            offset = default(int);
            float angle = 360f - Vector3.SignedAngle(player.transform.position - baseEntity.transform.position,
                              baseEntity.transform.forward, Vector3.up) - 180f;
            int offsetDown = Mathf.FloorToInt(angle / 45);
            bool isEven = offsetDown % 2 == 0;
            Vector3 liftPosition = baseEntity.transform.position + new Vector3(0, 3.60f, 0);
            ProceduralLift liftEntity = (ProceduralLift) GameManager.server.CreateEntity(StringPool.Get(2518050576),
                liftPosition, Quaternion.Euler(0, baseEntity.transform.eulerAngles.y, 0), true);
            if (liftEntity == null) return false;
            liftEntity.transform.localRotation *= Quaternion.Euler(0, (isEven ? offsetDown : offsetDown + 1) * 45, 0);
            liftEntity.OwnerID = player.userID;
            l = liftEntity;
            offset = -1;
            if (offsetDown is 7 or 0) offset = 1;
            else if (offsetDown is 1 or 2) offset = 2;
            else if (offsetDown is 3 or 4) offset = 3;
            else if (offsetDown is 5 or 6) offset = 4;
            return true;
        }

        #endregion

        #region Chat & Console Commands - Admin & Lift

        private void chatCmdLiftOwner(BasePlayer player, string command, string[] args)
        {
            bool value = false;
            if (!adminAccessEnabled.TryGetValue(player.UserIDString, out value)) return;
            if (args == null || args.Length < 1) return;
            IPlayer newOwner = covalence.Players.FindPlayer(args[0]);
            if (newOwner == null) return;
            Ray ray = player.eyes.BodyRay();
            if (!Physics.Raycast(ray, out RaycastHit raycastHit, 10f, 2097153)) return;
            BaseEntity baseEntity = raycastHit.GetEntity();
            if (!baseEntity) return;
            if (crossReference.TryGetValue(baseEntity.net.ID, out Elevator obj) && obj != null)
            {
                obj.SetOwner(Convert.ToUInt64(newOwner.Id));
                player.ChatMessage($"Changed lift ownership to {newOwner.Name}");
                return;
            }
        }

        private void chatCmdToggleAdm(BasePlayer player, string command, string[] args)
        {
            if (!adminAccessEnabled.TryGetValue(player.UserIDString, out bool value))
            {
                if (admAccessSwitchable)
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("You are not permitted for this command"));
                return;
            }

            bool value2 = adminLiftEntkill[player.UserIDString];
            bool value3 = value2;
            if (!value && args.Length > 0 && args[0].ToLower() == "kill") return;
            if (value && args.Length > 0 && args[0].ToLower() == "kill")
            {
                value3 = adminLiftEntkill[player.UserIDString];
                value3 = !value3;
                adminLiftEntkill[player.UserIDString] = value3;
                if (value3)
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("Lift admin entkill enabled"));
                else
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("Lift admin entkill disabled"));
            }
            else if (admAccessSwitchable)
            {
                value = !value;
                adminAccessEnabled[player.UserIDString] = value;
                if (value)
                {
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("Lift admin access enabled"));
                }
                else
                {
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("Lift admin access disabled"));
                    adminLiftEntkill[player.UserIDString] = false;
                }
            }
        }

        private void cmdToggleAdm(ConsoleSystem.Arg arg)
        {
            if (arg != null && arg.Connection != null && arg.Connection.player != null)
            {
                var argsList = new List<string>();
                for (int i = 0; arg.HasArgs(i + 1); i++) argsList.Add(arg.GetString(i));
                chatCmdToggleAdm((BasePlayer) arg.Connection.player, admAccessToggleCmd, argsList.ToArray());
            }
        }

        private void chatCmdLiftReset(BasePlayer player, string command, string[] args)
        {
            Ray ray = player.eyes.BodyRay();
            if (!Physics.Raycast(ray, out RaycastHit raycastHit, 10f, 2097153)) return;
            BaseEntity baseEntity = raycastHit.GetEntity();
            if (!baseEntity)
            {
                player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                   msg("Nothing valid to reset was found"));
                return;
            }

            if (crossReference.TryGetValue(baseEntity.net.ID, out Elevator obj) && obj != null)
            {
                if (obj.IsOwner(player) || IsAdm(player)) obj.RstMvmnt(player);
                return;
            }
            else
            {
                player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                   msg("Nothing valid to reset was found"));
            }
        }

        private Vector3 GtGrndBldng(Vector3 sourcePos)
        {
            sourcePos.y = TerrainMeta.HeightMap.GetHeight(sourcePos);
            if (Physics.Raycast(sourcePos, Vector3.down, out RaycastHit hitinfo, 2097152))
            {
                sourcePos.y = Math.Max(hitinfo.point.y, sourcePos.y);
                return sourcePos;
            }

            if (Physics.Raycast(sourcePos, Vector3.up, out hitinfo, 2097152))
                sourcePos.y = Math.Max(hitinfo.point.y, sourcePos.y);
            return sourcePos;
        }

        #endregion

        #region Chat Command - Tower Lift (Placement)

        private void chatCmdTowerLift(BasePlayer player, string command, string[] args)
        {
            if (!(bool) GetAccess("CanCreate", player.UserIDString, false))
            {
                player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                   msg("You are not permitted for this command"));
                return;
            }

            Ray ray = player.eyes.BodyRay();
            if (!Physics.Raycast(ray, out RaycastHit raycastHit, 10f, 2097152)) return;
            BaseEntity baseEntity = raycastHit.GetEntity();
            if (!baseEntity || !(baseEntity is BuildingBlock)) return;
            bool canPlaceFloor = (bool) GetAccess("CanPlaceOnFloor", player.UserIDString, false);
            if (!canPlaceFloor && baseEntity.ShortPrefabName != "foundation")
            {
                player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                   msg("You can place on foundations only"));
                return;
            }

            if (baseEntity.ShortPrefabName == "foundation" || (canPlaceFloor && baseEntity.ShortPrefabName == "floor"))
            {
                if (preventPlacementInCaves)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(baseEntity.transform.position + Vector3.up, Vector3.up, out hit, 100f,
                            8454144) && !IsAdm(player))
                    {
                        player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                           msg("You cannot build under the terrain/caves/cliffs"));
                        return;
                    }
                }

                OBB obb = baseEntity.WorldSpaceBounds();
                if (player.IsBuildingBlocked(obb) && !IsAdm(player))
                {
                    if (debugBuildingBlock) Puts($"[UpLifted chatCmdTowerLift] BLOCKED: player.IsBuildingBlocked(obb)=true (game blocked placement on this OBB)");
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("You are building blocked on that position"));
                    return;
                }

                if (preventPlacementAboveDeployables)
                    if (Physics.CheckBox(obb.position + Vector3.up * 3, obb.extents, obb.rotation, 256,
                            QueryTriggerInteraction.Ignore) && !IsAdm(player))
                    {
                        player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                           msg("You cannot place on deployed objects"));
                        return;
                    }

                if (baseEntity.OwnerID != player.userID && !IsAdm(player))
                {
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("You do not own this foundation"));
                    return;
                }

                if ((int) (baseEntity as BuildingBlock).grade < 2 && !IsAdm(player))
                {
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("The foundation needs to be at least of grade Stone"));
                    return;
                }

                int startLevel = 1;
                if (canPlaceFloor && baseEntity.ShortPrefabName == "floor")
                {
                    Vector3 sourcePos = baseEntity.transform.position;
                    Vector3 groundPos = GtGrndBldng(sourcePos);
                    startLevel = Mathf.CeilToInt((sourcePos.y - groundPos.y) / 3f);
                }

                int maxPlace = (int) GetAccess("MaxPlacementFloor", player.UserIDString, 1, true);
                int maxLevel = (int) GetAccess("MaxFloorLevel", player.UserIDString, 1, true);
                int maxRange = lastStableFloor + 1 - startLevel;
                if (maxPlace != -1 && startLevel > maxPlace || maxLevel != -1 && maxLevel - startLevel < 1 ||
                    maxRange < 2)
                {
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("You cannot place anymore on this level"));
                    return;
                }

                List<Collider> list = Facepunch.Pool.Get<List<Collider>>();
                Vis.Colliders<Collider>(baseEntity.transform.TransformPoint(Vector3.zero), 4f, list, 536870912,
                    QueryTriggerInteraction.Collide);
                foreach (Collider current in list)
                    if (!(current.transform.root == baseEntity.gameObject.transform.root))
                    {
                        BaseEntity baseEntity2 = current.gameObject.ToBaseEntity();
                        if (baseEntity2 && crossReference.TryGetValue(baseEntity2.net.ID, out Elevator obj) && obj != null &&
                            !IsAdm(player))
                        {
                            player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                               msg("Too close by another installed lift"));
                            Facepunch.Pool.FreeUnmanaged(ref list);
                            return;
                        }
                    }

                Facepunch.Pool.FreeUnmanaged(ref list);
                
                
                int count = 0;
                if (crossReference.Count > 0)
                {
                    foreach (var kv in crossReference)
                        if (kv.Value != null && kv.Value.GetOwner == player.userID && kv.Key == kv.Value.liftID)
                            count++;
                }
                int limit = (int) GetAccess("MaxLiftPerUser", player.UserIDString, 1, true);
                if (limit > 0 && count >= limit && !IsAdm(player))
                {
                    player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                       msg("You reached your placement limit"));
                    return;
                }

                Elevator elev;
                if (CrtPrcdrlLft(player, baseEntity, out ProceduralLift procLift, out int offsetDown))
                {
                    Debug.Log($"offsetdown {offsetDown}");
                    baseEntity.RefreshEntityLinks();
                    elev = procLift.gameObject.AddComponent<Elevator>();
                    playerStartups[player.userID] = elev;
                    playerStartups[player.userID].controlUI = new LiftUI();
                    playerStartups[player.userID].controlUI.IntPlcmnt(elev, procLift, player, baseEntity, offsetDown,
                        startLevel, maxRange, maxLevel);
                }
                else
                {
                    UpL.playerStartups.Remove(player.userID);
                }
            }
        }

        #endregion

        #region Hooks - Tick & Time

        private void OnTick()
        {
            var sky = TOD_Sky.Instance;
            if (sky?.Cycle == null) return;
            if (lastHour == Math.Floor(sky.Cycle.Hour)) return;
            lastHour = Math.Floor(sky.Cycle.Hour);
            if (sky.Cycle.Hour > sky.SunriseTime + 1.5 &&
                sky.Cycle.Hour < sky.SunsetTime - 1.5) isDayTime = true;
            else isDayTime = false;
        }

        private void OnTimeSunrise()
        {
            isDayTime = true;
        }

        private void OnTimeSunset()
        {
            isDayTime = false;
        }

        #endregion

        #region Console Commands - UI (Lift Commands & Placement)

        [ConsoleCommand("_ul.commands")]
        private void CmdLftCmmnds(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs(2)) return;
            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            NetworkableId elevatorID = arg.GetEntityID(0);
            if (!elevatorID.IsValid) return;
            string action = arg.GetString(1, "");
            if (action == "") return;
            int num = arg.GetInt(2, -1);
            if (num == -1) return;
            if (crossReference.TryGetValue(elevatorID, out Elevator obj) && obj != null)
                obj.UIEnterCommands(player, r(action), num);
        }

        [ConsoleCommand("_ul.placement")]
        private void CmdLftPlcmnt(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs(2)) return;
            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            string action = arg.GetString(0, "");
            if (action == "") return;
            int num = arg.GetInt(1, -1);
            if (num == -1) return;
            if (playerStartups.TryGetValue(player.userID, out Elevator obj)) obj.controlUI.UpdtPlcmnt(player, action, num);
        }

        #endregion

        #region Data Types - ELStorage

        // String constants for persisted state (replaces enums). Values match lang keys e.g. CabinLightMode.Dynamic.
        public static class St
        {
            public const string CabinWaiting = "Waiting", CabinIdle = "Idle", CabinBusy = "Busy", CabinUpKeep = "UpKeep";
            public const string MoveNone = "None", MoveUp = "Up", MoveDown = "Down";
            public const string LastNone = "None", LastCall = "Call", LastPanel = "Panel", LastForce = "Force";
            public const string TowerCreating = "Creating", TowerReady = "Ready", TowerLoading = "Loading", TowerSaving = "Saving", TowerNone = "None", TowerSpawned = "Spawned";
            public const string QSingle = "Single", QNone = "None", QMultiple = "Multiple";
            public const string PowerInternal = "Internal", PowerTC = "TC", PowerFoC = "FoC";
            public const string FloorOpen = "Open", FloorClosed = "Closed", FloorBusy = "Busy", FloorNone = "None";
            public const string LightDynamic = "Dynamic", LightAlwaysOn = "AlwaysOn", LightDisabled = "Disabled";
            public const string ShareNone = "None", ShareClan = "Clan", ShareFriends = "Friends", ShareFull = "Full", SharePublic = "Public", ShareDisabled = "Disabled";
            public const string FsPublic = "Public", FsFull = "Full", FsFriends = "Friends", FsClan = "Clan", FsExcluded = "Excluded", FsHidden = "Hidden", FsHiddenVIP = "HiddenVIP";
            public static readonly string[] ShareModeCycle = { ShareNone, ShareClan, ShareFriends, ShareFull, SharePublic };
            public static readonly string[] FloorShareCycle = { FsPublic, FsFull, FsFriends, FsClan, FsExcluded, FsHidden, FsHiddenVIP };
            public static readonly string[] LightModeCycle = { LightDynamic, LightAlwaysOn, LightDisabled };
        }

        // Persisted elevator data. Key connection fields: groundBlockID = foundation entity netId; doorsID[floor] = door entity netId (0 = none). These must exist in world to connect.</summary>
        public class ELStorage
        {
            public float timeToTake = 0f;
            public float timeTaken = 0f;
            public ulong ownerID = 0uL;
            // Foundation (ground block) entity netId. Must exist in world.</summary>
            public ulong groundBlockID = 0u;
            // World position of lift and ground block for restoring after server restart (entity IDs change).</summary>
            public float liftPosX, liftPosY, liftPosZ, groundPosX, groundPosY, groundPosZ;
            // Door entity netId per floor index. 0 = no door for that floor. Must exist in world.</summary>
            public ulong[] doorsID = new ulong[1];
            public string[] fShares = new string[1];
            public int startSock;
            public List<int> cQueue = new List<int>();
            public List<int> pQueue = new List<int>();
            public int topFloor = 1;
            public int cabinFloor = 1;
            public int destFloor = 1;
            public int lastFloor = 1;
            public int gFloor = 1;
            public float initialHeight;
            public string _state;
            public string _call;
            public string _floor;
            public string _last;
            public string _tower;
            public string _power;
            public string _panel;
            public string _lightmode;
            public int moveSpeed;
            public int maxSpeed;
            public int fuelConsume;
            public bool enableFuel;
            public bool enableCost;
            public float costMultiplier;
            public int floorTime;
            public int closeTime;
            public string _sharemode;
            public ulong skinID;
            public string clanTag;
            public string oldTag;
            public uint cTime;
            public List<ulong> _passengers = new List<ulong>();

            public ELStorage()
            {
            }
        }

        #endregion

        #region Elevator Class

        public class Elevator : FacepunchBehaviour
        {
            private Oxide.Core.Libraries.Time time = Interface.Oxide.GetLibrary<Core.Libraries.Time>();

            private string msg(string key, string id = null)
            {
                return UpL.lang.GetMessage(key, UpL, id);
            }

            public LiftUI controlUI; 
            private CabinComfort cabinComfort;
            internal CabinComfort GetCabinComfortForCleanup() => cabinComfort;
            private BoxCollider boxCollider;
            private List<ulong> toBeProtected = new List<ulong>();
            private int moveSpeed = UpL.defaultMoveSpeed;
            private int maxSpeed = 1;
            private int floorTime = 3;
            private int fuelConsume = 3;
            private int closeTime = 160;
            private bool enableFuel = true;
            private bool enableCost = true;
            private float costMultiplier = 1.0f;
            private bool calledDestroy = false;
            private float timeToTake = 0f;
            private float timeTaken = 0f;
            private ItemContainer storeBox = null;
            private ItemContainer[] tunaBoxes;
            private ulong skinID = 0uL;
            private string knockEffectGuid = "c76bed57f17dc634bb4b7726b69e4a11";
            private BuildingManager.Building baseBuilding;
            private BuildingPrivlidge buildingPrivilege;
            private float initialHeight;
            private List<int> cQueue = new List<int>();
            private List<int> pQueue = new List<int>();
            private int startSock;
            private int _topFloor = 1;
            private int _cabinFloor = 1;
            private int _destFloor = 1;
            private int _lastFloor = 1;
            private int _gFloor = 1;
            private Vector3 _currentPosition = new Vector3();
            public ProceduralLift lift;
            public NetworkableId liftID;
            private BasePlayer ownerPlayer = null;
            private BasePlayer lastUser = null;
            private string clanTag = string.Empty;
            private string oldTag = string.Empty;
            private uint cTime;
            private ulong ownerID;
            private BaseEntity groundBlock = null;
            private Vector3[] stops;
            private Door[] doors;
            private string[] floorShares;
            private SimpleLight cabinLight;
            private SimpleBuildingBlock floorGrill;
            private Recycler recyclerBox;
            private PressButton cabinLock;
            private Mailbox fuelBox;
            private Vector3[] socketPoints = new Vector3[5];
            private int[] socketOrder = new int[5];
            private BaseEntity[][] baseBlocksFloors;
            private bool doorSwitched = false;
            private string _state = St.CabinIdle;
            private string _call = St.QNone;
            private string _panel = St.QNone;
            private string _power = St.PowerInternal;
            private string _floor = St.FloorNone;
            private string _tower = St.TowerNone;
            private string _last = St.LastNone;
            private string _direction = St.MoveNone;
            private string _lightmode = St.LightDynamic;
            public string _sharemode = St.ShareNone;
            public bool IsIdle => _state == St.CabinWaiting || _state == St.CabinIdle;
            private bool UpKeep => _state == St.CabinUpKeep;

            private void OnCreation()
            {
                _tower = St.TowerCreating;
            }

            private void OnReady()
            {
                _tower = St.TowerReady;
                UpL?.SaveNewElevator(this);
            }

            private void OnSaving()
            {
                _tower = St.TowerSaving;
            }

            private void OnLoading()
            {
                _tower = St.TowerLoading;
            }

            private void OnSpawned()
            {
                _tower = St.TowerSpawned;
            }

            public bool IsReady => _tower == St.TowerReady || _tower != St.TowerSaving &&
                                   _tower != St.TowerLoading && _tower != St.TowerCreating &&
                                   _tower != St.TowerNone;

            private bool IsSpawned => _tower == St.TowerSpawned || _tower != St.TowerCreating;

            public int CurrentFloor
            {
                get { return _cabinFloor; }
                set { _cabinFloor = value; }
            }

            public int UpperFloor
            {
                get { return _topFloor; }
                set { _topFloor = value; }
            }

            public int LowerFloor
            {
                get { return _gFloor; }
                set { _gFloor = value; }
            }

            public int DestinationFloor
            {
                get { return _destFloor; }
                set { _destFloor = value; }
            }

            public int LastCallFromFloor
            {
                get { return _lastFloor; }
                set { _lastFloor = value; }
            }

            private int ArrSum { get { return _topFloor + 1; } }

            private Vector3 CurrentPosition
            {
                get { return _currentPosition; }
                set { _currentPosition = value; }
            }

            public Door GetDoorAt(int f)
            {
                return f - 1 <= doors.Length && doors[f] != null ? doors[f] : null;
            }

            public int GetDoorCount
            {
                get { int c = 0; for (int i = 0; i < doors.Length; i++) if (doors[i] != null) c++; return c; }
            }

            public bool IsGround(BaseEntity ent)
            {
                return groundBlock == ent ? true : false;
            }

            public bool IsDoor(BaseEntity ent)
            {
                for (int i = 0; i < doors.Length; i++) if (doors[i] == ent) return true; return false;
            }

            public bool IsOwner(BasePlayer player)
            {
                return ownerID == player.userID ? true : IsAdm(player) ? true : false;
            }

            public ulong GetOwner
            {
                get { return ownerID; }
            }
             
            public string GetTag
            {
                get { return clanTag; }
            }
            public uint GetAge
            {
                get { return cTime; }
            }

            public void SetOwner(ulong id)
            {
                ownerID = id;
            }

            public bool EnableFuel
            {
                get { return enableFuel; }
            }
            public bool EnableBuildCost
            {
                get { return enableCost; }
            }
            public string GetLightMode => _lightmode ?? St.LightDynamic;

            public int MoveSpeed
            {
                get { return moveSpeed; }
                set { moveSpeed = value; }
            }

            public int MaxSpeed
            {
                get { return maxSpeed; }
                set { maxSpeed = value; }
            }

            public int FloorWait
            {
                get { return floorTime; }
                set { floorTime = value; }
            }

            public int IdleWait
            {
                get { return closeTime; }
                set { closeTime = value; }
            }

            public string GetPowerState => _power ?? St.PowerInternal;

            public string GetFloorMode(int f)
            {
                return floorShares != null && f < floorShares.Length ? floorShares[f] ?? St.FsFull : St.FsFull;
            }

            public ulong GetSkinID => skinID;
            private HashSet<BasePlayer> _passengers;
            private bool _holdDoorTriggered;
            private bool _holdSleeperTriggered;

            private bool HasPassenger()
            {
                return _passengers.Count > 0 ? true : false;
            }

            private bool DoorTriggered
            {
                get { return _holdDoorTriggered; }
                set { _holdDoorTriggered = value; }
            }

            private bool SleepTriggered
            {
                get { return _holdSleeperTriggered; }
                set { _holdSleeperTriggered = value; }
            }

            public bool IsPassenger(ulong id)
            {
                foreach (var p in _passengers) if (p.userID == id) return true; return false;
            }

            private int SleeperCount
            {
                get { int c = 0; foreach (var p in _passengers) if (p.IsSleeping()) c++; return c; }
            }

            private bool IsDayTime()
            {
                return TOD_Sky.Instance.Cycle.Hour > TOD_Sky.Instance.SunriseTime + 1.5 &&
                       TOD_Sky.Instance.Cycle.Hour < TOD_Sky.Instance.SunsetTime - 1.5;
            }

            public void BeforeCreation()
            {
                Vector3 groundPos = groundBlock.transform.position;
                float topFloorY = groundPos.y + UpperFloor * 3f;
                float minY = groundPos.y + 1f;
                const float towerRadius = 2f;
                const uint roofPrefabId = 3895720527u;

                BuildingBlock gb = groundBlock as BuildingBlock;
                if (gb != null)
                {
                    BuildingManager.Building building = gb.GetBuilding();
                    if (building != null && building.buildingBlocks != null)
                    {
                        foreach (BuildingBlock block in building.buildingBlocks)
                        {
                            if (block == null || block.IsDestroyed) continue;
                            if (block == groundBlock) continue;
                            Vector3 p = block.transform.position;
                            if (p.y < minY) continue;
                            if (p.y >= topFloorY - 0.5f) continue;
                            if (Mathf.Abs(p.x - groundPos.x) > towerRadius || Mathf.Abs(p.z - groundPos.z) > towerRadius) continue;
                            string shortName = block.ShortPrefabName ?? "?";
                            bool isFloorOrRoofOrFrame = (shortName != null && (shortName.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0 || shortName.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0 || shortName.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0))
                                || block.prefabID == roofPrefabId;
                            if (!isFloorOrRoofOrFrame) continue;
                            ulong netId = block.net?.ID.Value ?? 0;
                            string debugLine = $"[UpLifted UpwardOverwrite] Removed: ShortPrefabName=\"{shortName}\" PrefabId={block.prefabID} NetID={netId}";
                            UpL.Puts(debugLine);
                            if (ownerPlayer != null && ownerPlayer.IsConnected)
                                ownerPlayer.ChatMessage(debugLine);
                            block.Kill(BaseNetworkable.DestroyMode.None);
                        }

                        if (building.doors != null)
                        {
                            foreach (Door door in building.doors)
                            {
                                if (door == null || door.IsDestroyed) continue;
                                Vector3 p = door.transform.position;
                                if (p.y < minY) continue;
                                if (p.y >= topFloorY - 0.5f) continue;
                                if (Mathf.Abs(p.x - groundPos.x) > towerRadius || Mathf.Abs(p.z - groundPos.z) > towerRadius) continue;
                                string shortName = door.ShortPrefabName ?? "?";
                                if (shortName == null || shortName.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                ulong netId = door.net?.ID.Value ?? 0;
                                string debugLine = $"[UpLifted UpwardOverwrite] Removed: ShortPrefabName=\"{shortName}\" PrefabId={door.prefabID} NetID={netId}";
                                UpL.Puts(debugLine);
                                if (ownerPlayer != null && ownerPlayer.IsConnected)
                                    ownerPlayer.ChatMessage(debugLine);
                                door.Kill(BaseNetworkable.DestroyMode.None);
                            }
                        }

                        return;
                    }
                }

                Ray ray = new Ray(groundPos + Vector3.up, Vector3.up);
                float maxDist = 3f * UpperFloor;
                RaycastHit[] hits = Physics.RaycastAll(ray, maxDist, 2097152);
                if (hits == null || hits.Length == 0) return;
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit hit in hits)
                {
                    BaseEntity ent = hit.GetEntity();
                    if (ent == null || ent.IsDestroyed) continue;
                    if (ent.transform.position.y >= topFloorY - 0.5f) continue;
                    string shortName = ent.ShortPrefabName ?? "?";
                    ulong netId = ent.net?.ID.Value ?? 0;
                    string debugLine = $"[UpLifted UpwardOverwrite] Removed (raycast): ShortPrefabName=\"{shortName}\" PrefabId={ent.prefabID} NetID={netId}";
                    UpL.Puts(debugLine);
                    if (ownerPlayer != null && ownerPlayer.IsConnected)
                        ownerPlayer.ChatMessage(debugLine);
                    ent.Kill(BaseNetworkable.DestroyMode.None);
                }
            }

            public bool IntNw(ProceduralLift procLift, BaseEntity baseEntity, BasePlayer basePlayer, int offSocket,
                int startFloor, int stopFloor, char[] placeDoors, List<ItemAmount> collectAmounts, bool hasCost,
                float costMulti, bool useItemPath = false, int requiredDoorCount = 0, int requiredFloorCount = 0, int doorItemId = 0, int elevatorItemId = 0)
            {
                toBeProtected = new List<ulong>();
                OnCreation();
                enabled = false;
                UpperFloor = stopFloor;
                LowerFloor = startFloor;
                CurrentFloor = LowerFloor;
                DestinationFloor = LowerFloor;
                LastCallFromFloor = LowerFloor;
                ownerPlayer = basePlayer;
                ownerID = basePlayer.userID;
                if (ClanCheck(ownerID.ToString(), oldTag, out clanTag)) oldTag = clanTag;
                cTime = time.GetUnixTimestamp();
                groundBlock = baseEntity; 
                BeforeCreation();
                crossReference[groundBlock.net.ID] = this;
                GetBuilding();
                doors = new Door[ArrSum];
                floorShares = new string[ArrSum];
                for (int i = _gFloor; i < ArrSum; i++) floorShares[i] = St.FsFull;
                tunaBoxes = new ItemContainer[ArrSum];
                CurrentFloor = LowerFloor;
                startSock = offSocket;
                lift = procLift;
                lift.Spawn();
                liftID = lift.net.ID;
                //AddFlrGrll();
                FtCmpnnts(lift);
                ConfigureProceduralLiftNetworking();
                if (controlUI != null)
                    controlUI.Init(this, lift.net.ID);
                OnSpawned();                
                initialHeight = lift.transform.position.y;
                CurrentPosition = lift.transform.position;
                InitSocketDirection();
                IntFlrStps();
                maxSpeed = (int) GetAccess("MaxMoveSpeed", basePlayer.UserIDString, 1);
                fuelConsume = (int) GetAccess("FuelConsumePerFloor", basePlayer.UserIDString, 5);
                enableFuel = (bool) GetAccess("EnableFuelUsage", basePlayer.UserIDString, true);
                enableCost = hasCost;
                costMultiplier = costMulti;
                SetStates();
                StartCoroutine(CrtNwShft(placeDoors, collectAmounts, basePlayer, hasCost, costMultiplier, useItemPath, requiredDoorCount, requiredFloorCount, doorItemId, elevatorItemId, done =>
                {
                    if (!done)
                    {
                        UpL.PrintWarning($"Failed lift creation by '{(basePlayer != null ? basePlayer.ToString(): ownerID.ToString())}' at '{groundBlock.transform.position.ToString()}' from floor '{LowerFloor}' to '{UpperFloor}' | Stopped at: {lastShaftError}");
                        if (basePlayer)
                            basePlayer.ChatMessage(string.Format(UpL.prefixFormat, UpL.prefixColor, UpL.pluginPrefix) + msg("The new lift failed for some reason. Please try from a lower floor") + $" | Stopped at: {lastShaftError}");
                        RemoveBasement();
                        lastShaftError = string.Empty;
                    }
                }));
                return true;
            }

            public ELStorage Sv2Prt(out bool isFinished, bool isUnload = false)
            {
                isFinished = default(bool);
                ELStorage data = new ELStorage();
                try
                {
                    OnSaving();
                    data.timeToTake = timeToTake;
                    data.timeTaken = timeTaken;
                    data.ownerID = ownerID;
                    data.groundBlockID = groundBlock.net.ID.Value;
                    Vector3 liftPos = lift.transform.position;
                    Vector3 groundPos = groundBlock.transform.position;
                    data.liftPosX = liftPos.x; data.liftPosY = liftPos.y; data.liftPosZ = liftPos.z;
                    data.groundPosX = groundPos.x; data.groundPosY = groundPos.y; data.groundPosZ = groundPos.z;
                    data.doorsID = new ulong[ArrSum];
                    for (int i = _gFloor; i < _topFloor + 1; i++)
                        if (doors[i] != null)
                            data.doorsID[i] = doors[i].net.ID.Value;
                    data.fShares = new string[ArrSum];
                    for (int i = _gFloor; i < _topFloor + 1; i++)
                        if (doors[i] != null)
                            data.fShares[i] = floorShares[i] ?? St.FsFull;
                    data.startSock = startSock;
                    data.cQueue = cQueue;
                    data.pQueue = pQueue;
                    data.topFloor = _topFloor;
                    data.cabinFloor = _cabinFloor;
                    data.destFloor = _destFloor;
                    data.lastFloor = _lastFloor;
                    data.gFloor = _gFloor;
                    data.initialHeight = initialHeight;
                    data._state = _state ?? St.CabinIdle;
                    data._call = _call ?? St.QNone;
                    data._floor = _floor ?? St.FloorNone;
                    data._panel = _panel ?? St.QNone;
                    data._power = _power ?? St.PowerInternal;
                    data._tower = _tower ?? St.TowerNone;
                    data._last = _last ?? St.LastNone;
                    data._lightmode = _lightmode ?? St.LightDynamic;
                    data._passengers = SavePassengers();
                    data.moveSpeed = moveSpeed;
                    data.maxSpeed = maxSpeed;
                    data.enableFuel = enableFuel;
                    data.fuelConsume = fuelConsume;
                    data.enableCost = enableCost;
                    data.costMultiplier = costMultiplier;
                    data.floorTime = floorTime;
                    data.closeTime = closeTime;
                    data._sharemode = _sharemode ?? St.ShareNone;
                    data.clanTag = clanTag;
                    data.oldTag = oldTag;
                    data.cTime = cTime;
                    data.skinID = skinID;
                    if (!isUnload) OnReady();
                    isFinished = true;
                }
                catch (Exception ex)
                {
                    UpL.PrintWarning("Exception while saving elevator: " + ex.Message);
                    isFinished = false;
                }

                return data;
            }

            public bool Ld2Prt(ProceduralLift procLift, ELStorage data)
            {
                OnLoading();
                crossReference[procLift.net.ID] = this;
                timeToTake = data.timeToTake;
                timeTaken = data.timeTaken;
                groundBlock = BaseNetworkable.serverEntities.Find(new NetworkableId(data.groundBlockID)) as BaseEntity;
                if (groundBlock != null)
                {
                    GetBuilding();
                    crossReference[groundBlock.net.ID] = this;
                }
                else if (verboseElevatorLogs)
                    UpL.Puts($"[UpLifted Ld2Prt] FAIL: groundBlock not found for data.groundBlockID={data.groundBlockID} (entity no longer exists or wrong ID)");

                lift = procLift;
                liftID = lift.net.ID;
                //AddFlrGrll();
                FtCmpnnts(lift);
                ConfigureProceduralLiftNetworking();
                _topFloor = data.topFloor;
                _gFloor = data.gFloor;
                _cabinFloor = Mathf.Max(data.cabinFloor, _gFloor);
                _destFloor = Mathf.Max(data.destFloor, _gFloor);
                _lastFloor = Mathf.Max(data.lastFloor, _gFloor);
                _currentPosition = lift.transform.position;
                initialHeight = data.initialHeight;
                ownerID = data.ownerID;
                tunaBoxes = new ItemContainer[ArrSum];
                GetCabinLight();
                GetRecycler();
                GetCabinLock();
                GetFuelBox();
                if (groundBlock == null)
                {
                    if (verboseElevatorLogs) UpL.Puts($"[UpLifted Ld2Prt] Returning false: groundBlock is null (data.groundBlockID={data.groundBlockID})");
                    StartCoroutine(SoftDestroy(default(NetworkableId)));
                    return false;
                }

                doors = new Door[ArrSum];
                for (int i = _gFloor; i < _topFloor + 1; i++)
                    if (data.doorsID[i] != 0)
                    {
                        Door getDoor = BaseNetworkable.serverEntities.Find(new NetworkableId(data.doorsID[i])) as Door;
                        if (getDoor)
                        {
                            if (i != CurrentFloor && getDoor.HasFlag(BaseEntity.Flags.Open))
                                getDoor.SetFlag(BaseEntity.Flags.Open, false);
                            if (!getDoor.HasFlag(BaseEntity.Flags.Locked))
                                getDoor.SetFlag(BaseEntity.Flags.Locked, true);
                            getDoor.knockEffect.guid = knockEffectGuid;
                            FtCmpnnts(getDoor);
                            FtCmpnnts(getDoor.FindLinkedEntity<BuildingBlock>());
                            BaseEntity slot = getDoor.GetSlot(BaseEntity.Slot.Lock);
                            if (slot != null)
                            {
                                FtCmpnnts(slot);
                                if (slot is BaseFuelLightSource or BaseOven)
                                {
                                    slot.KillMessage();
                                    AddFloorLight(getDoor);
                                }
                                else
                                {
                                    GetFloorLight(slot);
                                }
                            }
                            else
                            {
                                AddFloorLight(getDoor);
                            }

                            doors[i] = getDoor;
                        }
                    }

                floorShares = new string[ArrSum];
                if (data.fShares != null)
                    for (int i = _gFloor; i < _topFloor + 1; i++)
                        floorShares[i] = data.fShares[i] ?? St.FsFull;
                startSock = data.startSock;
                pQueue = data.pQueue ?? new List<int>();
                cQueue = data.cQueue ?? new List<int>();
                _state = data._state ?? St.CabinIdle;
                _call = data._call ?? St.QNone;
                _panel = data._panel ?? St.QNone;
                _floor = data._floor ?? St.FloorNone;
                _power = data._power ?? St.PowerInternal;
                _tower = data._tower ?? St.TowerNone;
                if (_tower == St.TowerSaving || _tower == St.TowerLoading || _tower == St.TowerCreating || _tower == St.TowerNone)
                    _tower = St.TowerReady;
                _last = data._last ?? St.LastNone;
                _lightmode = data._lightmode ?? St.LightDynamic;
                moveSpeed = data.moveSpeed;
                maxSpeed = data.maxSpeed;
                enableFuel = data.enableFuel;
                fuelConsume = data.fuelConsume;
                enableCost = data.enableCost;
                costMultiplier = data.costMultiplier;
                floorTime = data.floorTime;
                closeTime = data.closeTime;
                _sharemode = data._sharemode ?? St.ShareNone;
                clanTag = data.clanTag;
                oldTag = data.oldTag;
                if (ClanCheck(ownerID.ToString(), oldTag, out clanTag)) oldTag = clanTag;
                cTime = data.cTime;
                if (cTime <= 1) cTime = time.GetUnixTimestamp();
                skinID = data.skinID;
                InitSocketDirection();
                GetSocketPoints();
                GetFloorStops();
                GetGroundDoor();
                for (int floor = _gFloor + 1; floor < _topFloor + 1; floor++)
                    GetFloorFront(floor);

                SetStates();
                SetupCollider();
                EnsureFloorGrill();
                LoadPassengers(data._passengers as List<ulong>);
                controlUI = new LiftUI();
                controlUI.Init(this, lift.net.ID);
                OnReady();
                if (doors[CurrentFloor].HasFlag(BaseEntity.Flags.Open)) StartIdleClose();
                if (_lightmode == St.LightDynamic && !IsDayTime() && HasPassenger() ||
                    _lightmode == St.LightAlwaysOn) SwitchCabinLight(true);
                else SwitchCabinLight(false);
                if (UpL != null) UpL.LogLiftConnections(lift.net.ID.Value, data, true, groundBlock, doors);
                return true;
            }

            public void RstMvmnt(BasePlayer player)
            {
                OnCreation();
                enabled = false;
                if (recyclerBox) recyclerBox.SetFlag(BaseEntity.Flags.On, false);
                lift.transform.position =
                    new Vector3(lift.transform.position.x, initialHeight, lift.transform.position.z);
                MvPssngrs(initialHeight);
                SncPs(lift);
                SncKll(fuelBox);
                for (int i = 0; i < doors.Length; i++)
                {
                    Door door = doors[i];
                    if (door == null) continue;
                    SwtchFlrLght(door, 3);
                    door.SetFlag(BaseEntity.Flags.Open, false);
                    SncKll(door);
                }

                _currentPosition = lift.transform.position;
                _state = St.CabinIdle;
                _call = St.QNone;
                _panel = St.QNone;
                _floor = St.FloorNone;
                _last = St.LastNone;
                _direction = St.MoveNone;
                timeToTake = 0f;
                timeTaken = 0f;
                cQueue = new List<int>();
                pQueue = new List<int>();
                _cabinFloor = _gFloor;
                _destFloor = _gFloor;
                _lastFloor = _gFloor;
                OnReady();
                player.ChatMessage(string.Format(UpL.prefixFormat, UpL.prefixColor, UpL.pluginPrefix) +
                                   msg("This lift was sucessfully reset"));
            }

            public void ChckDrKnck(Door door, BasePlayer player)
            {
                lastUser = player;
                if (calledDestroy || UpKeep)
                {
                    SendEffRecycleStop(door);
                    return;
                }

                if (!HasLiftAccss(player))
                {
                    if (IsPassenger(player.userID) && IsIdle && CurrentFloor == DestinationFloor)
                    {
                        OpenFloor(CurrentFloor);
                        SwtchFlrLght(CurrentFloor, 1);
                        return;
                    }

                    SendEffDenied(door);
                    return;
                }

                int index = Array.IndexOf(doors, door);
                if (index == -1) return;
                if (IsPassenger(player.userID))
                    if (CurrentFloor != DestinationFloor && !IsIdle)
                    {
                        SendEffRecycleStop(door);
                        return;
                    }

                if (!HasFloorAccess(player, GetFloorMode(index)))
                {
                    SendEffDenied(door);
                    return;
                }

                TryAddExternal(index, player);
            }

            public object ChckLftUs(ProceduralLift procLift, BasePlayer player)
            {
                lastUser = player;
                if (!HasLiftAccss(player))
                {
                    SendEffDenied();
                    return false;
                }

                if (calledDestroy || !HasPassenger())
                    if (calledDestroy)
                        return false;
                if (controlUI == null)
                {
                    controlUI = new LiftUI();
                    controlUI.Init(this, lift.net.ID);
                }
                controlUI.CrtCbnUI(player, true);
                return false;
            }

            public void SwtchFlrLght(object floor, int lvl)
            {
                Door door = null;
                if (floor is int) door = doors[(int) floor];
                else if (floor is Door) door = floor as Door;
                if (door == null) return;
                CardReader slot = (CardReader) door.GetSlot(BaseEntity.Slot.Lock);
                if (slot)
                {
                    slot.SetFlag(BaseEntity.Flags.Reserved1, lvl == 1);
                    slot.SetFlag(BaseEntity.Flags.Reserved2, lvl == 2);
                    slot.SetFlag(BaseEntity.Flags.Reserved3, lvl == 3);
                }
            }

            public void SwitchCabinLight(bool state)
            {
                if (cabinLight == null) AddCabinLight();
                if (!state && cabinLight.HasFlag(BaseEntity.Flags.On)) cabinLight.SetFlag(BaseEntity.Flags.On, false);
                else if (state && !cabinLight.HasFlag(BaseEntity.Flags.On))
                    cabinLight.SetFlag(BaseEntity.Flags.On, true);
            }

            public void SetUpKeep(bool flag)
            {
                if (flag && _state != St.CabinUpKeep)
                {
                    _state = St.CabinUpKeep;
                    enabled = false;
                }
                else if (!flag && _state == St.CabinUpKeep)
                {
                    _state = St.CabinWaiting;
                    if (doorSwitched)
                    {
                        doorSwitched = false;
                        for (int di = 0; di < doors.Length; di++) { if (doors[di] != null) SwtchFlrLght(doors[di], 3); }
                        _call = St.QNone;
                        _panel = St.QNone;
                        cQueue = new List<int>();
                        pQueue = new List<int>();
                    }

                    enabled = true;
                    DoNextMove();
                }
            }

            public bool HasLiftAccss(BasePlayer player)
            {
                if (IsOwner(player) || IsAdm(player) || _sharemode == St.SharePublic)
                    return true;

                if ((_sharemode == St.ShareClan || _sharemode == St.ShareFull) &&
                    SameClanS(ownerID.ToString(), player.UserIDString))
                    return true;

                if ((_sharemode == St.ShareFriends || _sharemode == St.ShareFull) &&
                    HasFriend(ownerID.ToString(), player.UserIDString))
                    return true;

                if (enableTeamShare && (_sharemode == St.ShareClan || _sharemode == St.ShareFull) &&
                    SameTeamS(ownerID.ToString(), player.UserIDString))
                    return true;

                return false;                
            }

            public bool HasFloorAccess(BasePlayer player, string share, int p = 0)
            {
                if (IsOwner(player) || IsAdm(player) || share == St.FsPublic)
                    return true;

                if (_sharemode == St.SharePublic && share != St.FsExcluded && share != St.FsHidden &&
                    share != St.FsHiddenVIP)
                    return true;

                if (share == St.FsHiddenVIP)
                    if ((bool) GetAccess("AccessVipShare", player.UserIDString, false))
                        return true;
                if (share == St.FsFriends || share == St.FsFull)
                    if (HasFriend(ownerID.ToString(), player.UserIDString))
                        return true;
                if (share == St.FsClan || share == St.FsFull)
                {
                    if (SameClanS(ownerID.ToString(), player.UserIDString))
                        return true;
                    if (enableTeamShare && SameTeamS(ownerID.ToString(), player.UserIDString))
                        return true;
                }
                return false;
            }

            public void UIEnterCommands(BasePlayer player, string action, int num)
            {
                lastUser = player;
                SendEffectTo(2414984321, lift, player);
                if (action == "fgngr")
                {
                    SetUpKeep(false);
                    return;
                }
                else if (action == "fjvgpu")
                {
                    if (num == 0)
                    {
                        SendEffLock();
                        return;
                    }

                    doorSwitched = true;
                    bool hasDoor = GetDoorAt(num) != null ? true : false;
                    StartCoroutine(SwtchFlrFrnt(player, num, hasDoor, GetDoorAt(num)));
                    return;
                }
                else if (action == "bcra")
                {
                    OpenFloor(num);
                    SwtchFlrLght(num, 1);
                    return;
                }
                else if (action == "pybfr")
                {
                    CloseFloor(num);
                    SwtchFlrLght(num, 3);
                    return;
                }
                else if (action == "zbir")
                {
                    if (num == 0)
                    {
                        SendEffLock();
                        return;
                    }

                    if (num == 6666)
                    {
                        if (IsOwner(player))
                        {
                            if (player.userID == ownerID) ownerPlayer = player;
                            controlUI.CrtDstryUI(player);
                            return;
                        }

                        return;
                    }

                    if (num == 7777)
                    {
                        if (IsOwner(player))
                        {
                            if (player.userID == ownerID) ownerPlayer = player;
                            controlUI.CrtSttngsUI(player);
                            return;
                        }

                        return;
                    }

                    if (num == 1111)
                    {
                        SetUpKeep(false);
                        controlUI.CrtCbnUI(player);
                        return;
                    }
                    TryAddInternal(num);
                    return;
                }
                else if (action == "sybbezbqr")
                {
                    if (num == 0)
                    {
                        SendEffLock();
                        return;
                    }

                    if (num == 1111)
                    {
                        for (int i = LowerFloor; i < ArrSum; i++)
                            floorShares[i] = NextFloorShare(floorShares[i]);
                        controlUI.CrtShrngUI(player);
                        return;
                    }

                    if (num == 6666)
                    {
                        for (int i = LowerFloor; i < ArrSum; i++) floorShares[i] = St.FsFull;
                        controlUI.CrtShrngUI(player);
                        return;
                    }

                    floorShares[num] = NextFloorShare(floorShares[num]);
                    controlUI.CrtShrngUI(player);
                    return;
                }
                else if (action == "shry")
                {
                    if (num == 0)
                    {
                        SendEffDeploy();
                        player.inventory.loot.StartLootingEntity(recyclerBox, false);
                        player.inventory.loot.AddContainer(recyclerBox.inventory);
                        player.inventory.loot.SendImmediate();
                        player.ClientRPC(RpcTarget.Player(r("ECP_BcraYbbgCnary"), player), "genericsmall");
                        return;
                    }
                    else
                    {
                        if (_power == St.PowerInternal && buildingPrivilege) _power = St.PowerTC;
                        else _power = St.PowerInternal;
                        controlUI.CrtSttngsUI(player);
                        return;
                    }
                }
                else if (action == "qrfgebl")
                {
                    if (num == 0)
                    {
                        controlUI.CrtSttngsUI(player);
                        return;
                    }

                    if (CurrentFloor != LowerFloor)
                    {
                        _last = St.LastPanel;
                        DestinationFloor = LowerFloor;
                        calledDestroy = true;
                        StrtMvng();
                        return;
                    }

                    bool keepBuilding = num == 2 ? true : false;
                    RemoveBasement(keepBuilding);
                    return;
                }
                else if (action == "pnovayvtug")
                {
                    int i = Array.IndexOf(St.LightModeCycle, _lightmode ?? St.LightDynamic);
                    _lightmode = i < 0 ? St.LightDynamic : St.LightModeCycle[(i + 1) % St.LightModeCycle.Length];

                    if (_lightmode == St.LightAlwaysOn ||
                        _lightmode == St.LightDynamic && !IsDayTime() && HasPassenger()) SwitchCabinLight(true);
                    else SwitchCabinLight(false);
                    controlUI.CrtSttngsUI(player);
                    return;
                }
                else if (action == "funerzbqr")
                {
                    if (num == 0) DefineShareMode(player);
                    else controlUI.CrtShrngUI(player);
                    return;
                }
                else if (action == "fcrrq")
                {
                    if (moveSpeed == maxSpeed) moveSpeed = 1;
                    else moveSpeed++;
                    controlUI.CrtSttngsUI(player);
                    return;
                }
                else if (action == "sybbejnvg")
                {
                    if (floorTime == 15) floorTime = 3;
                    else floorTime += 2;
                    controlUI.CrtSttngsUI(player);
                    return;
                }
                else if (action == "vqyrjnvg")
                {
                    if (closeTime >= 640) closeTime = -1;
                    else if (closeTime == -1) closeTime = 10;
                    else closeTime *= 2;
                    StartIdleClose();
                    controlUI.CrtSttngsUI(player);
                    return;
                }
                else if (action == "qbbefxva")
                {
                    SendEffDeploy();
                    SetUpKeep(false);
                    StoreBox(player);
                    return;
                }
                else if (action == "pnapry")
                {
                    return;
                }
            }

            private void DefineShareMode(BasePlayer player = null)
            {
                if (player != null) _sharemode = NextShareMode(_sharemode);
                if (player != null) controlUI.CrtSttngsUI(player);
            }

            private static string NextShareMode(string current)
            {
                int i = Array.IndexOf(St.ShareModeCycle, current ?? St.ShareNone);
                if (i < 0) return St.ShareNone;
                return St.ShareModeCycle[(i + 1) % St.ShareModeCycle.Length];
            }

            private static string NextFloorShare(string current)
            {
                int i = Array.IndexOf(St.FloorShareCycle, current ?? St.FsFull);
                if (i < 0) return St.FsFull;
                return St.FloorShareCycle[(i + 1) % St.FloorShareCycle.Length];
            }

            public string GetShareMode(bool withsuffix = false)
            {
                if (_sharemode == St.ShareClan)
                {
                    if (clansEnabled || enableClanShare || enableTeamShare) return "<color=#008000>(o.k.)</color>";
                    return "<color=#ffff00>(n/a)</color>";
                }
                if (_sharemode == St.ShareFriends)
                {
                    if (friendsEnabled) return "<color=#008000>(o.k.)</color>";
                    return "<color=#ffff00>(n/a)</color>";
                }
                if (_sharemode == St.ShareFull)
                {
                    if (friendsEnabled || clansEnabled || enableClanShare || enableTeamShare)
                        return "<color=#008000>(o.k.)</color>";
                    return "<color=#ffff00>(n/a)</color>";
                }
                return "";
            }

            private void TryAddInternal(int num)
            {
                if (!pQueue.Contains(num))
                {
                    if (!PreFuelCheck(CurrentFloor, num))
                    {
                        Effect.server.Run(StringPool.Get(821899790), lift, 0u, new Vector3(-1f, -2f, 1.5f),
                            Vector3.zero, null, false);
                        return;
                    }

                    pQueue.Add(num);
                }
                else
                {
                    return;
                }

                OnPanelQueue();
                TryMvCbn(num, St.LastPanel);
            }

            private void TryAddExternal(int num, BasePlayer player)
            {
                if (num == CurrentFloor && !doors[CurrentFloor].HasFlag(BaseEntity.Flags.Open))
                {
                    Effect.server.Run(StringPool.Get(2125801479), doors[num], 0u, Vector3.zero, Vector3.zero, null,
                        false);
                    OpenFloor(num);
                    SwtchFlrLght(num, 1);
                    return;
                }

                if (!cQueue.Contains(num))
                {
                    if (_state == St.CabinIdle && !PreFuelCheck(CurrentFloor, num))
                    {
                        Effect.server.Run(StringPool.Get(3618221308), doors[num], 0u, Vector3.zero, Vector3.zero, null,
                            false);
                        Effect.server.Run(StringPool.Get(821899790), doors[num].GetSlot(BaseEntity.Slot.Lock), 0u,
                            Vector3.zero, Vector3.zero, null, false);
                        return;
                    }

                    Effect.server.Run(StringPool.Get(2125801479), doors[num], 0u, Vector3.zero, Vector3.zero, null,
                        false);
                    cQueue.Add(num);
                    SwtchFlrLght(num, 2);
                }
                else
                {
                    Effect.server.Run(StringPool.Get(2125801479), doors[num], 0u, Vector3.zero, Vector3.zero, null,
                        false);
                }

                OnCallQueue();
                LastCallFromFloor = num;
                TryMvCbn(num, St.LastCall);
            }

            private void TryMvCbn(int num, string last)
            {
                if (_last == St.LastNone || _state == St.CabinIdle)
                {
                    DestinationFloor = num;
                    _last = last;
                    StrtMvng();
                }
            }

            public void StartIdleClose()
            {
                if (closeTime > 0)
                {
                    CancelInvoke(DoIdleClose);
                    Invoke(DoIdleClose, (float) closeTime);
                }
            }

            private void DoIdleClose()
            {
                if (SleepTriggered)
                {
                    if (SleeperCount > 0)
                    {
                        CancelInvoke(DoIdleClose);
                        return;
                    }
                    else
                    {
                        SleepTriggered = false;
                    }
                }

                if (!doors[CurrentFloor].HasFlag(BaseEntity.Flags.Open))
                {
                    CancelInvoke(DoIdleClose);
                    return;
                }

                lift.SetFlag(BaseEntity.Flags.Busy, true);
                doors[DestinationFloor].SetFlag(BaseEntity.Flags.Open, false);
                SwtchFlrLght(DestinationFloor, 3);
                _state = St.CabinBusy;
                _floor = St.FloorBusy;
                enabled = true;
            }

            private void CloseFloor(int num)
            {
                if (IsIdle && doors[num].HasFlag(BaseEntity.Flags.Open))
                {
                    CancelInvoke(DoIdleClose);
                    lift.SetFlag(BaseEntity.Flags.Busy, true);
                    doors[num].SetFlag(BaseEntity.Flags.Open, false);
                    _state = St.CabinBusy;
                    _floor = St.FloorBusy;
                    enabled = true;
                    return;
                }

                SendEffLock();
            }

            public void OpenFloor(int num, bool force = false)
            {
                if ((IsIdle || force) && !doors[num].HasFlag(BaseEntity.Flags.Open))
                {
                    CancelInvoke(DoIdleClose);
                    lift.SetFlag(BaseEntity.Flags.Busy, true);
                    doors[num].SetFlag(BaseEntity.Flags.Open, true);
                    _state = St.CabinBusy;
                    _floor = St.FloorBusy;
                    enabled = true;
                    return;
                }

                SendEffLock();
            }

            private void DoNextMove()
            {
                if (UpKeep) return;
                if (DoorTriggered)
                {
                    DoorTriggered = false;
                    Invoke(DoNextMove, (float) floorTime);
                    return;
                }

                if (_last == St.LastPanel || _last == St.LastForce)
                {
                    if (_call != St.QNone)
                    {
                        DoCallMove();
                        CancelInvoke(DoIdleClose);
                        return;
                    }
                    else if (_panel != St.QNone)
                    {
                        DoPanelMove();
                        CancelInvoke(DoIdleClose);
                        return;
                    }
                }

                if (_last == St.LastCall || _last == St.LastForce)
                {
                    if (_panel != St.QNone)
                    {
                        DoPanelMove();
                        CancelInvoke(DoIdleClose);
                        return;
                    }
                    else if (_call != St.QNone)
                    {
                        DoCallMove();
                        CancelInvoke(DoIdleClose);
                        return;
                    }
                }

                EnterIdle();
            }

            private void EnterIdle()
            {
                _last = St.LastNone;
                _state = St.CabinIdle;
                enabled = false;
            }

            private void DoPanelMove()
            {
                DestinationFloor = pQueue[0];
                _last = St.LastPanel;
                StrtMvng();
            }

            private void DoCallMove()
            {
                DestinationFloor = cQueue[0];
                _last = St.LastCall;
                StrtMvng();
            }

            public void DoForceMove(int floor = 1)
            {
                _last = St.LastForce;
                DestinationFloor = floor;
                StrtMvng();
            }

            private void StrtMvng()
            {
                GetDirection();
                if (_last != St.LastForce && !calledDestroy && !FuelCheck())
                {
                    if (_last == St.LastCall)
                    {
                        OnCallQueue(true);
                        for (int di = 0; di < doors.Length; di++) { if (doors[di] != null) SwtchFlrLght(doors[di], 3); }
                        if (LastCallFromFloor == DestinationFloor)
                            Effect.server.Run(StringPool.Get(821899790),
                                doors[LastCallFromFloor].GetSlot(BaseEntity.Slot.Lock), 0u, Vector3.zero, Vector3.zero,
                                null, false);
                    }
                    else if (_last == St.LastPanel)
                    {
                        OnPanelQueue(true);
                        {
                            Effect.server.Run(StringPool.Get(821899790), cabinLight, 0u, Vector3.zero, Vector3.zero,
                                null, false);
                        }
                    }

                    EnterIdle();
                    return;
                }

                CancelInvoke(DoIdleClose);
                _state = St.CabinBusy;
                enabled = true;
                timeToTake = Vector3.Distance(stops[CurrentFloor], stops[DestinationFloor]) / moveSpeed;
                timeTaken = 0f;
                lift.SetFlag(BaseEntity.Flags.Busy, true);
                _floor = St.FloorBusy;
                _nextProceduralLiftNetRefresh = 0f;
            }

            private void GetDirection()
            {
                if (CurrentFloor < DestinationFloor) _direction = St.MoveUp;
                else _direction = St.MoveDown;
            }

            private void OnCallQueue(bool isDone = false)
            {
                if (isDone) cQueue.RemoveAt(0);
                if (cQueue.Count == 0) _call = St.QNone;
                if (cQueue.Count == 1) _call = St.QSingle;
                if (cQueue.Count > 1) _call = St.QMultiple;
            }

            private void OnPanelQueue(bool isDone = false)
            {
                if (isDone) pQueue.RemoveAt(0);
                if (pQueue.Count == 0) _panel = St.QNone;
                if (pQueue.Count == 1) _panel = St.QSingle;
                if (pQueue.Count > 1) _panel = St.QMultiple;
            }

            private void Update()
            {
                if (IsIdle || !IsReady || UpKeep) return;
                if (CurrentPosition == stops[DestinationFloor])
                {
                    if (doors[DestinationFloor].HasFlag(BaseEntity.Flags.Busy)) return;
                    if (calledDestroy)
                    {
                        enabled = false;
                        RemoveBasement();
                        return;
                    }

                    _floor = St.FloorOpen;
                    _state = St.CabinWaiting;
                    CurrentFloor = DestinationFloor;
                    if (_last != St.LastForce)
                    {
                        if (_last == St.LastCall) OnCallQueue(true);
                        if (_last == St.LastPanel) OnPanelQueue(true);
                    }

                    lift.SetFlag(BaseEntity.Flags.Busy, false);
                    if (_panel != St.QNone || _call != St.QNone)
                    {
                        Invoke(DoNextMove, (float) floorTime);
                    }
                    else
                    {
                        _last = St.LastNone;
                        _state = St.CabinIdle;
                        enabled = false;
                        if (closeTime > 0)
                        {
                            CancelInvoke(DoIdleClose);
                            Invoke(DoIdleClose, (float) closeTime);
                        }
                    }

                    return;
                }

                if (doors[CurrentFloor].HasFlag(BaseEntity.Flags.Open))
                {
                    doors[CurrentFloor].SetFlag(BaseEntity.Flags.Open, false);
                    SwtchFlrLght(CurrentFloor, 3);
                    _floor = St.FloorBusy;
                    return;
                }

                if (doors[CurrentFloor].HasFlag(BaseEntity.Flags.Busy) ||
                    doors[CurrentFloor].HasFlag(BaseEntity.Flags.Open)) return;
                if (_floor != St.FloorClosed)
                {
                    _floor = St.FloorClosed;
                    if (recyclerBox == null) AddRecycler();
                    else recyclerBox.SetFlag(BaseEntity.Flags.On, true);
                    SendEffect(3499498126, lift);
                }

                timeTaken += Time.deltaTime;
                float y = Mathf.SmoothStep(stops[CurrentFloor].y, stops[DestinationFloor].y,
                    Mathf.InverseLerp(0, timeToTake, timeTaken));
                MvPssngrs(y);
                CurrentPosition = new Vector3(CurrentPosition.x, y, CurrentPosition.z);
                lift.transform.position = CurrentPosition;
                //floorGrill.transform.position = lift.transform.position - (Vector3.up * 0.4f);
                SncPs(lift);
                if (CurrentPosition == stops[DestinationFloor])
                {
                    SncKll(fuelBox);
                    timeToTake = 0f;
                    timeTaken = 0f;
                    recyclerBox.SetFlag(BaseEntity.Flags.On, false);
                    SendEffect(3618221308, lift);
                    doors[DestinationFloor].SetFlag(BaseEntity.Flags.Open, true);
                    _floor = St.FloorBusy;
                    SwtchFlrLght(DestinationFloor, 1);
                    ForceProceduralLiftNetworkResync();
                }
            }

            // Throttle for mid-travel visibility group refresh (cave/overworld crossing).
            private float _nextProceduralLiftNetRefresh;

            // After FP network visibility changes, the lift should use a group derived from world position.
            // Large range widens cross-cell visibility (see BaseNetworkable.networkRange / NetworkVisibilityGrid).
            private void ConfigureProceduralLiftNetworking()
            {
                ProceduralLift pl = lift as ProceduralLift;
                if (pl == null) return;
                pl.networkRange = EntityNetworkRange.Large;
                pl.UpdateNetworkGroup();
                _nextProceduralLiftNetRefresh = UnityEngine.Time.time + 0.45f;
            }

            // Per-frame UpdateNetworkGroup + manual GroupChange caused rapid group churn; subscribers could be empty for a
            // tick (no EntityPosition), client desync, NRE, frozen player. Refresh occasionally while moving and always when docked.
            private void ForceProceduralLiftNetworkResync()
            {
                ProceduralLift pl = lift as ProceduralLift;
                if (pl == null) return;
                pl.networkRange = EntityNetworkRange.Large;
                pl.UpdateNetworkGroup();
                _nextProceduralLiftNetRefresh = UnityEngine.Time.time + 0.45f;
                SncPs(lift, refreshLiftNetworkGroup: false);
            }

            private void SncPs(BaseEntity entity, bool refreshLiftNetworkGroup = true)
            {
                if (refreshLiftNetworkGroup && entity == lift && lift is ProceduralLift)
                {
                    float t = UnityEngine.Time.time;
                    if (t >= _nextProceduralLiftNetRefresh)
                    {
                        _nextProceduralLiftNetRefresh = t + 0.45f;
                        entity.UpdateNetworkGroup();
                    }
                }

                if (entity.net == null || entity.net.group == null || entity.net.group.subscribers == null ||
                    entity.net.group.subscribers.Count == 0) return;

                NetWrite netWrite = Net.sv.StartWrite();
                netWrite.PacketID(Message.Type.GroupChange);
                netWrite.EntityID(entity.net.ID);
                netWrite.GroupID(entity.net.group.ID);
                netWrite.Send(new SendInfo(entity.net.group.subscribers));

                netWrite = Net.sv.StartWrite();
                netWrite.PacketID(Message.Type.EntityPosition);
                netWrite.EntityID(entity.net.ID);
                netWrite.WriteObject(entity.GetNetworkPosition());
                netWrite.WriteObject(entity.GetNetworkRotation().eulerAngles);
                netWrite.Float(entity.GetNetworkTime());

                SendInfo info = new SendInfo(entity.net.group.subscribers);
                info.method = SendMethod.ReliableUnordered;
                info.priority = Network.Priority.Immediate;
                netWrite.Send(info);

                    if (entity.children != null)
                    foreach (BaseEntity current in entity.children)
                        SncPs(current, refreshLiftNetworkGroup);
            }

            private void SncKll(BaseEntity entity)
            {
                if (entity.net == null || entity.net.group == null || entity.net.group.subscribers == null ||
                    entity.net.group.subscribers.Count == 0) return;
                if (BaseEntity.Query.Server != null) BaseEntity.Query.Server.Move(entity);
                
                NetWrite netWrite = Net.sv.StartWrite();
                netWrite.PacketID(Message.Type.EntityDestroy);
                netWrite.EntityID(entity.net.ID);
                netWrite.UInt8(0);
                netWrite.Send(new SendInfo(entity.net.group.subscribers));

                    entity.SendNetworkUpdateImmediate();
                if (entity.children != null)
                    foreach (BaseEntity current in entity.children)
                        SncKll(current);
            }

            public void EntityKill(NetworkableId entid, bool isDoor)
            {
                if (lift == null || groundBlock == null) return;
                if (entid == lift.net.ID || entid == groundBlock.net.ID || isDoor && doorDestructionKillsLiftCabin)
                {
                    StartCoroutine(SoftDestroy(entid));
                    return;
                }

                crossReference.Remove(entid);
            }

            private void OnDestroy()
            {
                if (UpLifted.PluginUnloadInProgress)
                {
                    CancelInvoke(DoIdleClose);
                    if (!floorGrill?.IsDestroyed ?? false)
                        floorGrill?.Kill();
                    DestroyBoxColliderSafe();
                    return;
                }

                if (!floorGrill?.IsDestroyed ?? false)
                    floorGrill?.Kill();

                CancelInvoke(DoIdleClose);
                DestroyBoxColliderSafe();
                if (!enabled || calledDestroy) return;
                foreach (ulong id in new List<ulong>(toBeProtected)) crossReference.Remove(new NetworkableId(id));
                StartCoroutine(SoftDestroy(default(NetworkableId)));
            }

            private IEnumerator SoftDestroy(NetworkableId entid)
            {
                if (calledDestroy) yield break;
                calledDestroy = true;
                if (groundBlock && !groundBlock.IsDestroyed)
                {
                    crossReference.Remove(groundBlock.net.ID);
                    Destroy(groundBlock.gameObject.GetComponent<Prevent_Building>());
                    yield return waitFOF;
                }

                if (fuelBox && !fuelBox.IsDestroyed)
                {
                    fuelBox.KillMessage();
                    yield return waitFOF;
                }

                if (recyclerBox && !recyclerBox.IsDestroyed)
                {
                    recyclerBox.KillMessage();
                    yield return waitFOF;
                }

                if (cabinLight && !cabinLight.IsDestroyed)
                {
                    cabinLight.KillMessage();
                    yield return waitFOF;
                }

                if (cabinLock && !cabinLock.IsDestroyed)
                {
                    cabinLock.KillMessage();
                    yield return waitFOF;
                }

                if (doors != null && doors.Length > 0)
                    foreach (Door d in doors)
                    {
                        if (d == null) continue;
                        BaseEntity slot = d.GetSlot(BaseEntity.Slot.Lock);
                        if (slot != null && slot is CardReader) slot.KillMessage();
                    }

                crossReference.Remove(entid);
                DestroyBoxColliderSafe();
                if (lift && !lift.IsDestroyed)
                {
                    crossReference.Remove(lift.net.ID);
                    SendEffect(2184296839, lift);
                    lift.KillMessage();
                    yield return waitFOF;
                }

                Destroy(this);
                yield return null;
            }

            public void SetupCollider()
            {
                gameObject.layer = 18;
                if (boxCollider != null)
                {
                    UnityEngine.Object.DestroyImmediate(boxCollider);
                    boxCollider = null;
                }

                boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.center = new Vector3(0, -3.6f, 0);
                boxCollider.size = new Vector3(2.9f, 2.9f, 2.9f);
                _passengers = new HashSet<BasePlayer>();
            }

            private void SleepWatch()
            {
                if (SleepTriggered)
                {
                    if (sleepWatchMoveDown && CurrentFloor != LowerFloor)
                    {
                        if (_state != St.CabinBusy) DoForceMove(LowerFloor);
                        CancelInvoke(SleepWatch);
                        InvokeRandomized(SleepWatch, sleepWatchInterval, sleepWatchInterval,
                            sleepWatchInterval * 0.33f);
                        return;
                    }

                    if (CurrentFloor == LowerFloor || !sleepWatchMoveDown)
                        if (!doors[CurrentFloor].HasFlag(BaseEntity.Flags.Open))
                        {
                            OpenFloor(CurrentFloor);
                            SwtchFlrLght(CurrentFloor, 1);
                        }
                }

                foreach (BasePlayer player in new List<BasePlayer>(_passengers))
                    if (player.IsSleeping() && !IsAdm(player))
                    {
                        SleepTriggered = true;
                        CancelInvoke(SleepWatch);
                        InvokeRandomized(SleepWatch, sleepWatchInterval, sleepWatchInterval,
                            sleepWatchInterval * 0.33f);
                        return;
                    }

                SleepTriggered = false;
                InvokeRandomized(SleepWatch, sleepWatchDelay, sleepWatchDelay, sleepWatchDelay * 0.33f);
            }

            public List<ulong> SavePassengers()
            {
                List<ulong> l = new List<ulong>();
                foreach (BasePlayer p in _passengers) l.Add(p.userID);
                return l;
            }

            public void LoadPassengers(List<ulong> l = null)
            {
                if (l == null || l.Count == 0) return;
                foreach (ulong id in l)
                {
                    BasePlayer p = BasePlayer.FindByID(id);
                    if (p != null) _passengers.Add(p);
                }

                if (_passengers.Count > 0 && GetLightMode == St.LightDynamic && !IsDayTime()) SwitchCabinLight(true);
                if (_passengers.Count > 0 && sleepWatchEnabled)
                    InvokeRandomized(SleepWatch, sleepWatchDelay, sleepWatchDelay, sleepWatchDelay * 0.33f);
            }

            public void MvPssngrs(float toPos)
            {
                foreach (BasePlayer player in new List<BasePlayer>(_passengers))
                    if (player.IsDead())
                        RemovePassenger(player);
            }

            public void RemovePassenger(BasePlayer player)
            {
                _passengers.Remove(player);
                (player as BaseEntity).SetParent(null, true, true);
                if (player != null)
                {
                    player.PauseFlyHackDetection(5f);
                    player.PauseSpeedHackDetection(5f);
                }

                controlUI.DestroyUi(player);
                if (_passengers.Count == 0)
                {
                    CancelInvoke(SleepWatch);
                    SleepTriggered = false;
                    if (GetLightMode == St.LightDynamic) SwitchCabinLight(false);
                }
            }

            public bool ClsDrpBx(Mailbox fuelBox)
            {
                foreach (BasePlayer player in new List<BasePlayer>(_passengers))
                    if (player.inventory.loot != null)
                        if (player.inventory.loot.entitySource is Mailbox)
                        {
                            if (player.inventory.loot.containers.Contains(fuelBox.inventory))
                            {
                                Item slot = fuelBox.inventory.GetSlot(fuelBox.mailInputSlot);
                                if (slot != null)
                                {
                                    fuelBox.inventory.itemList.Remove(slot);
                                    fuelBox.inventory.MarkDirty();
                                    player.inventory.GiveItem(slot);
                                }
                            }

                            player.EndLooting();
                            player.ClientRPC(RpcTarget.Player(r("BaQvrq"), player));
                            return IsAdm(player);
                        }

                return false;
            }

            private void OnTriggerEnter(Collider other)
            {
                if ((other.gameObject.layer & 17) > 0)
                {
                    BasePlayer component = other.gameObject.GetComponent<BasePlayer>();
                    if (component != null && !_passengers.Contains(component))
                    {
                        _passengers.Add(component);
                        (component as BaseEntity).SetParent(lift, true, true);
                        _holdDoorTriggered = true;
                        StartIdleClose();
                    }

                    if (_passengers.Count == 1 && sleepWatchEnabled)
                        InvokeRandomized(SleepWatch, sleepWatchDelay, sleepWatchDelay, sleepWatchDelay * 0.33f);
                    if (_passengers.Count > 0 && GetLightMode == St.LightDynamic && !IsDayTime()) SwitchCabinLight(true);
                }
            }

            private void OnTriggerExit(Collider other)
            {
                if ((other.gameObject.layer & 17) > 0)
                {
                    BasePlayer component = other.gameObject.GetComponent<BasePlayer>();
                    if (component != null && _passengers.Contains(component))
                    {
                        _passengers.Remove(component);
                        (component as BaseEntity).SetParent(null, true, true);
                        if (component != null)
                        {
                            component.PauseFlyHackDetection(5f);
                            component.PauseSpeedHackDetection(5f);
                        }

                        controlUI.DestroyUi(component);
                        _holdDoorTriggered = true;
                        StartIdleClose();
                    }

                    if (_passengers.Count == 0)
                    {
                        CancelInvoke(SleepWatch);
                        SleepTriggered = false;
                        if (GetLightMode == St.LightDynamic) SwitchCabinLight(false);
                    }
                }
            }

            public int GetFuel()
            {
                ItemContainer inv = null;
                if (_power == St.PowerInternal)
                {
                    inv = recyclerBox.inventory;
                    return inv.GetAmount(-946369541, false);
                }
                else
                {
                    if (buildingPrivilege == null) GetBuilding();
                    if (buildingPrivilege == null) return 0;
                    inv = buildingPrivilege.inventory;
                    return inv.GetAmount(-946369541, false);
                }
            }

            public Item GetFuelItem()
            {
                ItemContainer inv = null;
                if (_power == St.PowerInternal)
                {
                    inv = recyclerBox.inventory;
                }
                else
                {
                    if (buildingPrivilege == null) GetBuilding();
                    if (buildingPrivilege == null) return null;
                    inv = buildingPrivilege.inventory;
                }

                Item fuelItem = inv.FindItemByItemID(-946369541);
                if (fuelItem == null) return null;
                if (fuelItem.amount == 1) fuelItem.DoRemove();
                else fuelItem.amount--;
                return ItemManager.CreateByItemID(-946369541, 1);
            }

            public bool InsertFuel(Item item)
            {
                ItemContainer inv = null;
                if (_power == St.PowerInternal)
                {
                    inv = recyclerBox.inventory;
                }
                else
                {
                    if (buildingPrivilege == null) GetBuilding();
                    if (buildingPrivilege == null) inv = recyclerBox.inventory;
                    else inv = buildingPrivilege.inventory;
                }

                if (inv.maxStackSize <= 0)
                {
                    if (item.MoveToContainer(inv, -1, true)) return true;
                }
                else
                {
                    int hasAmount = inv.GetAmount(-946369541, false);
                    int maxAmount = inv.capacity * inv.maxStackSize;
                    if (hasAmount >= maxAmount)
                    {
                        WithDrawFuel(item);
                        return false;
                    }

                    List<Item> foundItems = new List<Item>();
                    inv.FindItemsByItemID(foundItems, -946369541);
                    foreach (Item slot in new List<Item>(foundItems))
                    {
                        inv.itemList.Remove(slot);
                        slot.Remove(0f);
                    }

                    ItemManager.DoRemoves();
                    int inputAmount = item.amount + hasAmount;
                    for (int i = 0; i < inv.capacity; i++)
                    {
                        Item slot = ItemManager.CreateByItemID(-946369541, Math.Min(inv.maxStackSize, inputAmount));
                        inv.itemList.Add(slot);
                        slot.parent = inv;
                        inputAmount -= slot.amount;
                        if (inputAmount <= 0)
                        {
                            inv.MarkDirty();
                            return true;
                        }
                    }

                    item.amount = inputAmount;
                    WithDrawFuel(item);
                }

                return false;
            }

            public void WithDrawFuel(Item item)
            {
                fuelBox.inventory.itemList.Add(item);
                item.parent = fuelBox.inventory;
                item.position = fuelBox.mailInputSlot;
                item.MarkDirty();
                fuelBox.inventory.MarkDirty();
            }

            private void GetBuilding()
            {
                baseBuilding = BuildingManager.server.GetBuilding((groundBlock as DecayEntity).buildingID);
                if (baseBuilding.HasBuildingPrivileges())
                    buildingPrivilege = baseBuilding.GetDominatingBuildingPrivilege();
            }

            public bool FuelCheck()
            {
                if (!enableFuel || lastUser != null && IsAdm(lastUser)) return true;
                int floors = _direction == St.MoveUp
                    ? DestinationFloor - CurrentFloor
                    : CurrentFloor - DestinationFloor;
                int needFuel = floors * fuelConsume;
                if (GetFuel() < needFuel) return false;
                ItemContainer inv = null;
                if (_power == St.PowerTC)
                    inv = buildingPrivilege.gameObject.GetComponent<StorageContainer>().inventory;
                else inv = recyclerBox.gameObject.GetComponent<StorageContainer>().inventory;
                List<Item> list = new List<Item>();
                inv.FindItemsByItemID(list, -946369541);
                if (list == null || list.Count == 0) return false;
                foreach (Item current in list)
                {
                    int num = needFuel;
                    int num2 = 0;
                    while (num2 < num && current.amount > 0)
                    {
                        current.UseItem(1);
                        num2++;
                    }

                    if (num2 == num) return true;
                }

                return false;
            }

            public bool PreFuelCheck(int curr, int dest)
            {
                if (!enableFuel || lastUser != null && IsAdm(lastUser)) return true;
                int needFuel = fuelConsume * (curr < dest ? dest - curr : curr - dest);
                if (GetFuel() < needFuel) return false;
                return true;
            }

            private void SetStates()
            {
                if (!enableFuel) _power = St.PowerFoC;
                DefineShareMode();
                SetUpKeep(false);
            }

            public void IntFlrStps()
            {
                stops = new Vector3[ArrSum];
                stops[LowerFloor] = lift.transform.position;
                for (int i = LowerFloor + 1; i < ArrSum; i++)
                    stops[i] = stops[LowerFloor] + new Vector3(0, 3f * ((float) i - LowerFloor), 0);
            }

            public void GetFloorStops()
            {
                stops = new Vector3[ArrSum];
                Vector3 startPos = lift.transform.position;
                startPos.y = initialHeight;
                stops[LowerFloor] = startPos;
                for (int i = LowerFloor + 1; i < ArrSum; i++)
                    stops[i] = stops[LowerFloor] + new Vector3(0, 3f * ((float) i - LowerFloor), 0);
            }

            public void InitSocketDirection()
            {
                int startAt = startSock;
                for (int j = 1; j < 5; j++)
                {
                    socketOrder[j] = startAt;
                    if (startAt >= 4) startAt = 1;
                    else startAt++;
                }
            }

            private void GetSocketPoints()
            {
                Construction construction = (groundBlock as BuildingBlock).blockDefinition;
                var wallSockets = new List<Socket_Base>();
                foreach (Socket_Base s in construction.allSockets)
                    if (s is ConstructionSocket cs && cs.socketType == ConstructionSocket.Type.Wall)
                        wallSockets.Add(s);
                int i = 1;
                foreach (Socket_Base socket in wallSockets)
                {
                    int o = socketOrder[i];
                    socketPoints[o] = groundBlock.transform.localToWorldMatrix.MultiplyPoint3x4(socket.position);
                    i++;
                }
            }

            private void PayInForShaft(List<ItemAmount> collectAmounts, BasePlayer player, bool useItemPath = false, int requiredDoorCount = 0, int requiredFloorCount = 0, int doorItemId = 0, int elevatorItemId = 0)
            {
                if (!(bool) GetAccess("EnableBuildCost", player.UserIDString, true)) return;
                List<Item> list = new List<Item>();
                if (useItemPath && doorItemId != 0 && elevatorItemId != 0)
                {
                    int elevatorTake = requiredFloorCount * elevatorItemsPerFloor;
                    player.inventory.Take(list, elevatorItemId, elevatorTake);
                    player.inventory.Take(list, doorItemId, requiredDoorCount);
                    player.Command(r("abgr.vai"), new object[] {elevatorItemId, -elevatorTake});
                    player.Command(r("abgr.vai"), new object[] {doorItemId, -requiredDoorCount});
                }
                else
                {
                    foreach (ItemAmount current in collectAmounts)
                    {
                        player.inventory.Take(list, current.itemDef.itemid, (int) current.amount);
                        player.Command(r("abgr.vai"), new object[] {current.itemDef.itemid, current.amount * -1f});
                    }
                }

                foreach (Item current2 in list) current2.Remove(0f);
            }

            public List<ItemAmount> GetPartsCost(int grade, int setFloors, char[] placeDoors, float costMulti)
            {
                List<ItemAmount> collectAmounts = new List<ItemAmount>();
                for (int floor = 1; floor < setFloors + 1; floor++)
                {
                    bool hasDoor = floor == UpperFloor
                        ? true
                        : Convert.ToBoolean(Convert.ToInt32(placeDoors[floor].ToString()));
                    if (hasDoor)
                    {
                        foreach (ItemAmount current in CostForPart(grade, 919059809)) // wallframe
                            {
                                ItemAmount existing = null;
                                foreach (ItemAmount x in collectAmounts) if (x.itemDef.itemid == current.itemDef.itemid) { existing = x; break; }
                                if (existing != null) existing.amount += Mathf.Ceil(current.amount * costMulti);
                                else collectAmounts.Add(new ItemAmount(current.itemDef, Mathf.Ceil(current.amount * costMulti)));
                            }
                        foreach (ItemAmount current in CostForObject(3647679950)) // garage door
                            {
                                ItemAmount existing = null;
                                foreach (ItemAmount x in collectAmounts) if (x.itemDef.itemid == current.itemDef.itemid) { existing = x; break; }
                                if (existing != null) existing.amount += Mathf.Ceil(current.amount * costMulti);
                                else collectAmounts.Add(new ItemAmount(current.itemDef, Mathf.Ceil(current.amount * costMulti)));
                            }
                    }
                }

                return collectAmounts;
            }

            private List<ItemAmount> CostForObject(uint prefabID)
            {
                Construction construction = PrefabAttribute.server.Find<Construction>(prefabID);
                return GameManager.server.FindPrefab(construction.fullName).GetComponent<BaseCombatEntity>()
                    .BuildCost().Items;
            }

            private List<ItemAmount> CostForPart(int gradeNum, uint prefabID)
            {
                Construction construction = PrefabAttribute.server.Find<Construction>(prefabID);
                List<ItemAmount> fullAmount = new List<ItemAmount>();
                foreach (ItemAmount current in construction.defaultGrade.CostToBuild())
                    {
                        ItemAmount existing = null;
                        foreach (ItemAmount x in fullAmount) if (x.itemDef.itemid == current.itemDef.itemid) { existing = x; break; }
                        if (existing != null) existing.amount += current.amount;
                        else fullAmount.Add(new ItemAmount(current.itemDef, current.amount));
                    }
                foreach (ItemAmount current in construction.grades[gradeNum].CostToBuild())
                    {
                        ItemAmount existing = null;
                        foreach (ItemAmount x in fullAmount) if (x.itemDef.itemid == current.itemDef.itemid) { existing = x; break; }
                        if (existing != null) existing.amount += current.amount;
                        else fullAmount.Add(new ItemAmount(current.itemDef, current.amount));
                    }
                return fullAmount;
            }

            private void AddCabinLight()
            {
                cabinLight = (SimpleLight) GameManager.server.CreateEntity(StringPool.Get(1523703314),
                    lift.transform.position, default(Quaternion), true);
                if (cabinLight == null) return;
                cabinLight.SetParent(lift, 0);
                cabinLight.transform.localPosition = new Vector3(0f, -0.25f, -0.35f);
                cabinLight.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
                cabinLight.SetFlag(BaseEntity.Flags.Reserved8, true);
                cabinLight.SetFlag(BaseEntity.Flags.On, false);
                cabinLight.Spawn();
                FtCmpnnts(cabinLight);
            }

            private void EnsureFloorGrill()
            {
                if (floorGrill != null && !floorGrill.IsDestroyed) return;
                AddFloorGrill();
            }

            private void DestroyBoxColliderSafe()
            {
                if (boxCollider == null) return;
                var bc = boxCollider;
                boxCollider = null;
                UnityEngine.Object.Destroy(bc);
            }

            private void AddFloorGrill()
            {
                floorGrill = (SimpleBuildingBlock)GameManager.server.CreateEntity(StringPool.Get(2480303744), lift.transform.position, Quaternion.identity, true);
                if (floorGrill == null) return;
                floorGrill.SetParent(lift, 0);
                floorGrill.enableSaving = false;
                floorGrill.grounded = true;
                floorGrill.Spawn();
                floorGrill.grounded = true;
                // Local Y only (90°): parent lift already carries world yaw.
                floorGrill.transform.localPosition = new Vector3(0f, -0.4f, 0f);
                floorGrill.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                FtCmpnnts(floorGrill);
                floorGrill.SendNetworkUpdateImmediate();
            }

            private void GetCabinLight()
            {
                foreach (BaseEntity child in new List<BaseEntity>(lift.children))
                {
                    if (child is BaseFuelLightSource)
                    {
                        child.KillMessage();
                        AddCabinLight();
                        return;
                    }

                    if (child is SimpleLight) cabinLight = child as SimpleLight;
                }

                if (cabinLight == null)
                {
                    AddCabinLight();
                    return;
                }

                cabinLight.SetFlag(BaseEntity.Flags.Reserved8, true);
                cabinLight.SetFlag(BaseEntity.Flags.On, false);
                cabinLight.transform.localPosition = new Vector3(0f, -0.25f, -0.35f);
                cabinLight.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
                FtCmpnnts(cabinLight);
                cabinLight.SendNetworkUpdateImmediate();
            }

            private void FtCmpnnts(BaseEntity entity)
            {
                if (entity.net != null) crossReference[entity.net.ID] = this;
                if (entity is ProceduralLift)
                {
                    Transform antihack_volume = entity.gameObject.transform.Find("antihack_volume");
                    if (antihack_volume)
                    {
                        antihack_volume.SetParent(null);
                        Destroy(antihack_volume.GetComponent<BoxCollider>());
                        Destroy(antihack_volume.GetComponent<EnvironmentVolume>());
                    }

                    const int preventBuildingLayer = 29;
                    int disabled = 0;
                    foreach (Collider col in entity.GetComponentsInChildren<Collider>(true))
                    {
                        if (col != null && col.gameObject.layer == preventBuildingLayer)
                        {
                            col.enabled = false;
                            disabled++;
                        }
                    }
                    if (UpL != null && debugBuildingBlock)
                        UpL.Puts($"[UpLifted FtCmpnnts] ProceduralLift: disabled {disabled} collider(s) on layer {preventBuildingLayer} (Prevent Building)");

                    // Register lift trigger and other children so use-on-trigger and OnEntityKill resolve to this elevator
                    foreach (BaseEntity child in entity.children)
                    {
                        if (child != null && child.net != null)
                            crossReference[child.net.ID] = this;
                    }
                    return;
                }

                if (entity is BaseCombatEntity) toBeProtected.Add(entity.net.ID.Value);
                if (entity is Door) FillUppSlot(entity as Door);
                foreach (BoxCollider c in entity.GetComponents<BoxCollider>()) Destroy(c);
                foreach (MeshCollider c in entity.GetComponents<MeshCollider>()) Destroy(c);
                Destroy(entity.GetComponent<DestroyOnGroundMissing>());
                Destroy(entity.GetComponent<GroundWatch>());               
            }

            private void AddRecycler()
            {
                recyclerBox = (Recycler) GameManager.server.CreateEntity(StringPool.Get(1729604075),
                    lift.transform.position, default(Quaternion), true);
                if (recyclerBox == null) return;
                recyclerBox.SetParent(lift, 0);
                recyclerBox.transform.localPosition = new Vector3(0f, -3.75f, 0f);
                recyclerBox.transform.localRotation = Quaternion.Euler(90, 0, 0);
                recyclerBox.allowedItem = ItemManager.FindItemDefinition(-946369541);
                recyclerBox.onlyAcceptCategory = ItemCategory.Resources;
                recyclerBox.inventorySlots = (int) GetAccess("FuelStorageItemSlots", ownerID.ToString(), 2, true);
                recyclerBox.maxStackSize = (int) GetAccess("FuelStorageStackSize", ownerID.ToString(), 500, true);
                recyclerBox.Spawn();
                FtCmpnnts(recyclerBox);
                cabinComfort = recyclerBox.gameObject.AddComponent<CabinComfort>();
                cabinComfort.Setup(recyclerBox as BaseEntity,
                    (int) GetAccess("BaseComfort", ownerID.ToString(), 0, true),
                    (int) GetAccess("BaseTemperature", ownerID.ToString(), 0, true));
                recyclerBox.SetFlag(BaseEntity.Flags.Busy, true);
            }

            private void GetRecycler()
            {
                recyclerBox = null;
                foreach (BaseEntity child in lift.children)
                    if (child is Recycler)
                    {
                        recyclerBox = child as Recycler;
                        break;
                    }

                if (recyclerBox == null)
                {
                    AddRecycler();
                    return;
                }

                recyclerBox.transform.localPosition = new Vector3(0f, -3.75f, 0f);
                recyclerBox.transform.localRotation = Quaternion.Euler(90, 0, 0);
                recyclerBox.SendNetworkUpdate();
                FtCmpnnts(recyclerBox);
                recyclerBox.inventory.onlyAllowedItems = new ItemDefinition[] { ItemManager.FindItemDefinition(-946369541) };
                recyclerBox.onlyAcceptCategory = ItemCategory.Resources;
                cabinComfort = recyclerBox.gameObject.AddComponent<CabinComfort>();
                cabinComfort.Setup(recyclerBox as BaseEntity,
                    (int) GetAccess("BaseComfort", ownerID.ToString(), 0, true),
                    (int) GetAccess("BaseTemperature", ownerID.ToString(), 0, true));
                recyclerBox.SetFlag(BaseEntity.Flags.Busy, true);
            }

            private void AddCabinLock()
            {
                cabinLock = (PressButton) GameManager.server.CreateEntity("assets/prefabs/io/electric/switches/pressbutton/pressbutton.prefab",
                    lift.transform.position, default(Quaternion), true);
                if (!cabinLock) return;
                cabinLock.SetParent(lift, 0);
                cabinLock.transform.localPosition = new Vector3(-0.8f, -3.25f, 1.4f);
                cabinLock.transform.localRotation = Quaternion.Euler(0, -180, 0);
                cabinLock.pressDuration = 0.25f;
                cabinLock.Spawn();
                
            }

            private void GetCabinLock()
            {
                cabinLock = null;
                CodeLock del = null;
                foreach (BaseEntity child in lift.children)
                {
                    if (child is CodeLock)
                    {
                        del = child as CodeLock;
                    }

                    if (child is PressButton)
                    {
                        cabinLock = child as PressButton;
                        break;
                    }
                }

                if (del)
                {
                    lift.RemoveChild(del);
                    del.KillMessage();
                }

                if (cabinLock == null)
                {
                    AddCabinLock();
                    return;
                }

                cabinLock.SetParent(lift, 0);
                cabinLock.transform.localPosition = new Vector3(-0.8f, -3.25f, 1.4f);
                cabinLock.transform.localRotation = Quaternion.Euler(0, -180, 0);
                cabinLock.pressDuration = 0.25f;
                cabinLock.SetFlag(BaseEntity.Flags.On, false);
                DestroyOnClient(cabinLock);
                cabinLock.SendNetworkUpdateImmediate();
            }

            private void AddFuelBox()
            {
                fuelBox = (Mailbox) GameManager.server.CreateEntity(StringPool.Get(661881069), lift.transform.position,
                    default(Quaternion), true);
                if (fuelBox == null) return;
                fuelBox.OwnerID = ownerID;
                fuelBox.SetParent(lift, 0);
                fuelBox.transform.localPosition = new Vector3(0.9f, -3.8f, 0.9f);
                fuelBox.transform.localRotation = Quaternion.Euler(-90, 0, 180);
                fuelBox.allowedItem = ItemManager.FindItemDefinition(-946369541);
                fuelBox.onlyAcceptCategory = ItemCategory.Resources;
                fuelBox.needsBuildingPrivilegeToUse = false;
                fuelBox.Spawn();
                fuelBox.inventory.onItemAddedRemoved = new Action<Item, bool>(OnDropBoxAdded);
                FtCmpnnts(fuelBox);
                fuelBox.SetFlag(BaseEntity.Flags.Locked, !enableFuel);
            }

            private void OnDropBoxAdded(Item item, bool added)
            {
                if (!added || item == null) return;
                fuelBox.inventory.itemList.Remove(item);
                fuelBox.inventory.MarkDirty();
                InsertFuel(item);
                Effect.server.Run(fuelBox.mailDropSound.resourcePath, fuelBox.GetDropPosition(), default(Vector3), null,
                    false);
                bool isAdmin = ClsDrpBx(fuelBox);
            }

            private void GetFuelBox()
            {
                foreach (BaseEntity child in lift.children)
                    if (child is Mailbox)
                    {
                        fuelBox = child as Mailbox;
                        break;
                    }

                if (fuelBox == null)
                {
                    AddFuelBox();
                    return;
                }

                fuelBox.allowedItem = ItemManager.FindItemDefinition(-946369541);
                fuelBox.inventory.onlyAllowedItems = new ItemDefinition[] { ItemManager.FindItemDefinition(-946369541) };
                fuelBox.onlyAcceptCategory = ItemCategory.Resources;
                fuelBox.needsBuildingPrivilegeToUse = false;
                fuelBox.inventory.onItemAddedRemoved = new Action<Item, bool>(OnDropBoxAdded);
                fuelBox.SetFlag(BaseEntity.Flags.Locked, !enableFuel);
                FtCmpnnts(fuelBox);
            }

            private void DestroyOnClient(BaseEntity entity)
            {
                NetWrite netWrite = Net.sv.StartWrite();
                netWrite.PacketID(Message.Type.EntityDestroy);
                netWrite.EntityID(entity.net.ID);
                netWrite.UInt8(0);
                netWrite.Send(new SendInfo(entity.net.group.subscribers));
            }

            private void AddFloorLight(BaseEntity parentEnt)
            {
                CardReader r = (CardReader) GameManager.server.CreateEntity(StringPool.Get(1841596500),
                    default(Vector3), default(Quaternion), true);
                if (r != null)
                {
                    r.SetParent(parentEnt, 0);
                    r.OwnerID = ownerID;
                    r.transform.localPosition = new Vector3(-0.09f, 0.2f, -1.3f);
                    r.transform.localRotation = Quaternion.Euler(0, -90, 0);
                    r.SetFlag(BaseEntity.Flags.Reserved8, true);
                    r.accessLevel = -1;
                    r.Spawn();
                    FtCmpnnts(r);
                    parentEnt.SetSlot(BaseEntity.Slot.Lock, r);
                    r.SetFlag(r.AccessLevel1, false);
                    r.SetFlag(r.AccessLevel2, false);
                    r.SetFlag(r.AccessLevel3, true);
                }
            }

            private void GetFloorLight(BaseEntity slot)
            {
                slot.transform.localPosition = new Vector3(-0.09f, 0.2f, -1.3f);
                slot.transform.localRotation = Quaternion.Euler(0, -90, 0);
                FtCmpnnts(slot);
                CardReader r = slot as CardReader;
                if (r)
                {
                    r.SetFlag(BaseEntity.Flags.Reserved8, true);
                    r.accessLevel = -1;
                    r.SetFlag(r.AccessLevel1, false);
                    r.SetFlag(r.AccessLevel2, false);
                    r.SetFlag(r.AccessLevel3, true);
                }

                DestroyOnClient(slot);
                slot.SendNetworkUpdateImmediate();
            }

            private void RemoveBasement(bool liftOnly = false)
            {
                CancelInvoke(DoIdleClose);
                if (liftOnly)
                {
                    StartCoroutine(SoftDestroy(default(NetworkableId)));
                    return;
                }

                StartCoroutine(RemoveShaft(done =>
                {
                    if (done) calledDestroy = true;
                    Destroy(this);
                }));
            }

            private IEnumerator RemoveShaft(Action<bool> done)
            {
                for (int floor = UpperFloor; floor > LowerFloor - 1; floor--)
                {
                    try
                    {
                        if (baseBlocksFloors[floor][1] != null && !baseBlocksFloors[floor][1].IsDestroyed)
                        {
                            Door door = baseBlocksFloors[floor][1]?.FindLinkedEntity<Door>();
                            if (door != null)
                            {
                                crossReference.Remove(door.net.ID);
                                BuildingBlock doorBlock = door.FindLinkedEntity<BuildingBlock>();
                                if (doorBlock != null)
                                    crossReference.Remove(doorBlock.net.ID);
                                if (door.HasSlot(BaseEntity.Slot.Lock))
                                {
                                    BaseEntity reader = door.GetSlot(BaseEntity.Slot.Lock);
                                    if (reader != null && !reader.IsDestroyed)
                                        reader.Kill(BaseNetworkable.DestroyMode.None);
                                }
                                if (door.GetSlot(BaseEntity.Slot.UpperModifier) != null)
                                {
                                    BaseEntity closure = door.GetSlot(BaseEntity.Slot.UpperModifier);
                                    if (closure != null && !closure.IsDestroyed)
                                        closure.Kill(BaseNetworkable.DestroyMode.None);
                                }
                                door.SetFlag(BaseEntity.Flags.Locked, false);
                                door.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                            }
                        }
                    }
                    catch
                    {
                    }

                    yield return waitFFU;
                }

                if (cabinLight && !cabinLight.IsDestroyed)
                {
                    cabinLight.Kill(BaseNetworkable.DestroyMode.None);
                    yield return waitFFU;
                }

                if (cabinLock && !cabinLock.IsDestroyed)
                {
                    cabinLock.Kill(BaseNetworkable.DestroyMode.None);
                    yield return waitFFU;
                }

                if (recyclerBox && !recyclerBox.IsDestroyed)
                {
                    recyclerBox.Kill(BaseNetworkable.DestroyMode.None);
                    yield return waitFFU;
                }

                if (fuelBox && !fuelBox.IsDestroyed)
                {
                    fuelBox.Kill(BaseNetworkable.DestroyMode.None);
                    yield return waitFFU;
                }

                if (lift && !lift.IsDestroyed)
                {
                    SendEffect(2184296839, lift);
                    crossReference.Remove(lift.net.ID);
                    lift.Kill(BaseNetworkable.DestroyMode.None);
                }

                if (groundBlock)
                {
                    Destroy(groundBlock.gameObject.GetComponent<Prevent_Building>());
                    crossReference.Remove(groundBlock.net.ID);
                }

                Destroy(boxCollider);
                done(true);
            }

            private void CollectPayOut(ref List<ItemAmount> collectAmounts, List<ItemAmount> partAmounts,
                float costMulti = 1.0f)
            {
                foreach (ItemAmount current in partAmounts)
                    {
                        ItemAmount existing = null;
                        foreach (ItemAmount x in collectAmounts) if (x.itemDef.itemid == current.itemDef.itemid) { existing = x; break; }
                        if (existing != null) existing.amount += current.amount * costMulti;
                        else collectAmounts.Add(new ItemAmount(current.itemDef, current.amount * costMulti));
                    }
            }

            private void PayOutForPart(List<ItemAmount> collectAmounts)
            {
                if (ownerPlayer == null)
                {
                    ownerPlayer = BasePlayer.FindByID(ownerID);
                    if (ownerPlayer == null) return;
                }

                foreach (ItemAmount current in collectAmounts)
                {
                    ownerPlayer.Command(r("abgr.vai"), new object[] {current.itemDef.itemid, current.amount});
                    Item item = ItemManager.Create(current.itemDef, (int) current.amount);
                    ownerPlayer.inventory.GiveItem(item);
                }
            }

            private string lastShaftError;

            // Last reason AddWllSd/AddWllFrm/ObjctFnlzd refused to spawn (stability, placement, etc.).
            private string _lastWallSpawnFailReason = string.Empty;

            private IEnumerator CrtNwShft(char[] d, List<ItemAmount> collectAmounts, BasePlayer player, bool hasCost,
                float costMulti, bool useItemPath, int requiredDoorCount, int requiredFloorCount, int doorItemId, int elevatorItemId, Action<bool> done)
            {
                lastShaftError = string.Empty;
                try
                {
                    GetSocketPoints();
                }
                catch
                {
                    lastShaftError = "GetSocketPoints";
                    done(false);
                    yield break;
                }

                collectAmounts?.Clear();
                yield return waitFFU;
                try
                {
                    AddGroundDoor(collectAmounts);
                }
                catch
                {
                    lastShaftError = "AddGroundDoor";
                    done(false);
                    yield break;
                }

                yield return waitFFU;
                if (hasCost && player) player.ClientRPC(RpcTarget.Player(r("BaQvrq"), player));
                for (int floor = LowerFloor + 1; floor < ArrSum; floor++)
                {
                    try
                    {
                        AddFlrFrnt(floor,
                            floor == UpperFloor ? true : Convert.ToBoolean(Convert.ToInt32(d[floor].ToString())),
                            collectAmounts);
                    }
                    catch (Exception ex)
                    {
                        lastShaftError = $"AddFloorFront Lvl:{floor} | {ex.GetBaseException().Message}";
                        done(false);
                        yield break;
                    }

                    yield return waitFFU;
                    if (enableCost && player) player.ClientRPC(RpcTarget.Player(r("BaQvrq"), player));
                    yield return waitFFU;
                }

                try
                {
                    AddCabinLight();
                    SwitchCabinLight(false);
                }
                catch
                {
                    lastShaftError = "AddCabinLight";
                    done(false);
                    yield break;
                }

                try
                {
                    AddRecycler();
                }
                catch
                {
                    lastShaftError = "AddRecycler";
                    done(false);
                    yield break;
                }

                try
                {
                    AddCabinLock();
                }
                catch
                {
                    lastShaftError = "AddCbnLck";
                    done(false);
                    yield break;
                }

                try
                {
                    AddFuelBox();
                }
                catch
                {
                    lastShaftError = "AddFuelBox";
                    yield break;
                }

                SetupCollider();
                EnsureFloorGrill();
                if (hasCost && player) player.ClientRPC(RpcTarget.Player(r("BaQvrq"), player));
                if (hasCost && player) PayInForShaft(collectAmounts, player, useItemPath, requiredDoorCount, requiredFloorCount, doorItemId, elevatorItemId);
                Invoke(DoIdleClose, (float) closeTime);
                controlUI = new LiftUI();
                controlUI.Init(this, lift.net.ID);
                OnReady();
                done(true);
                yield return null;
            }

            private bool AddGroundDoor(List<ItemAmount> collectAmounts)
            {
                baseBlocksFloors = new BaseEntity[ArrSum][];
                for (int floor = LowerFloor; floor < ArrSum; floor++) baseBlocksFloors[floor] = new BaseEntity[5];
                EntityLink link = GtLnk(groundBlock, socketOrder[1]);
                if (link != null && link.IsOccupied())
                {
                    BaseEntity existing = link.connections[0].owner;
                    baseBlocksFloors[LowerFloor][1] = existing;
                    FtCmpnnts(existing);
                    if (existing.prefabID == 2194854973) // wall - leave it, no door
                    {
                        doors[LowerFloor] = null;
                        return true;
                    }
                    if (existing.prefabID == 919059809) // doorframe - add door if missing
                    {
                    if (existing.FindLinkedEntity<Door>() == null)
                    {
                        BaseEntity baseFill = AddGrgDr(existing);
                        AddFloorLight(baseFill);
                        doors[LowerFloor] = baseFill as Door;
                        FillUppSlot(doors[LowerFloor]);
                        doors[LowerFloor].knockEffect.guid = knockEffectGuid;
                        crossReference[baseFill.net.ID] = this;
                        baseFill.SetFlag(BaseEntity.Flags.Locked, true);
                        baseFill.SetFlag(BaseEntity.Flags.Open, true);
                        SwtchFlrLght(baseFill, 1);
                        if (enableCost && collectAmounts != null)
                            CollectPayOut(ref collectAmounts, CostForObject(3647679950), costMultiplier);
                    }
                        else
                        {
                            doors[LowerFloor] = existing.FindLinkedEntity<Door>();
                            FillUppSlot(doors[LowerFloor]);
                        }
                        return true;
                    }
                }
                BaseEntity baseBlock = AddWllFrm(groundBlock, link);
                baseBlocksFloors[LowerFloor][1] = baseBlock;
                BaseEntity groundDoor = AddGrgDr(baseBlock);
                AddFloorLight(groundDoor);
                doors[LowerFloor] = groundDoor as Door;
                FillUppSlot(doors[LowerFloor]);
                doors[LowerFloor].knockEffect.guid = knockEffectGuid;
                crossReference[groundDoor.net.ID] = this;
                groundDoor.SetFlag(BaseEntity.Flags.Locked, true);
                groundDoor.SetFlag(BaseEntity.Flags.Open, true);
                SwtchFlrLght(groundDoor, 1);
                if (enableCost && collectAmounts != null)
                {
                    CollectPayOut(ref collectAmounts, CostForPart((int)UpL.buildingGrade, 919059809), costMultiplier);
                    CollectPayOut(ref collectAmounts, CostForObject(3647679950), costMultiplier);
                }
                return true;
            }

            private void GetGroundDoor()
            {
                baseBlocksFloors = new BaseEntity[ArrSum][];
                for (int floor = LowerFloor; floor < ArrSum; floor++) baseBlocksFloors[floor] = new BaseEntity[5];
                EntityLink link = GtLnk(groundBlock, socketOrder[1]);
                if (link.IsOccupied())
                {
                    baseBlocksFloors[LowerFloor][1] = link.connections[0].owner;
                    FtCmpnnts(baseBlocksFloors[LowerFloor][1]);
                    if (baseBlocksFloors[LowerFloor][1].FindLinkedEntity<Door>() == null)
                    {
                        BaseEntity baseFill = AddGrgDr(baseBlocksFloors[LowerFloor][1]);
                        AddFloorLight(baseFill);
                        doors[LowerFloor] = baseFill as Door;
                        FillUppSlot(doors[LowerFloor]);
                        doors[LowerFloor].knockEffect.guid = knockEffectGuid;
                    }
                }
                else
                {
                    AddGroundDoor(null);
                }
            }

            private bool ClnnnrFlr(int level)
            {
                EntityLink fLink = GtLnkRf(baseBlocksFloors[level - 1][1], 2);
                if (fLink.IsOccupied())
                {
                    BaseEntity owner = fLink.connections[0].owner;
                    if (owner.transform.position.x == groundBlock.transform.position.x &&
                        owner.transform.position.z == groundBlock.transform.position.z)
                    {
                        owner.Kill(BaseNetworkable.DestroyMode.None);
                        fLink.Clear();
                        return true;
                    }
                }

                fLink = GtLnkRf(baseBlocksFloors[level - 1][1], 1);
                if (fLink.IsOccupied())
                {
                    BaseEntity owner = fLink.connections[0].owner;
                    if (owner.transform.position.x == groundBlock.transform.position.x &&
                        owner.transform.position.z == groundBlock.transform.position.z)
                    {
                        owner.Kill(BaseNetworkable.DestroyMode.None);
                        fLink.Clear();
                        return true;
                    }
                }

                return false;
            }

            private bool AddFlrFrnt(int level, bool isDoor, List<ItemAmount> collectAmounts = null)
            {
                _lastWallSpawnFailReason = string.Empty;
                EntityLink link = GtLnk(baseBlocksFloors[level - 1][1]);
                if (link == null)
                    throw new InvalidOperationException(
                        "No upward wall socket on the floor below (misaligned shaft, wrong block type, or missing links).");

                if (link.IsOccupied())
                {
                    BaseEntity existing = link.connections[0].owner;
                    baseBlocksFloors[level][1] = existing;
                    FtCmpnnts(existing);
                    if (existing.prefabID == 2194854973) // wall - leave it
                    {
                        doors[level] = null;
                        return true;
                    }
                    if (existing.prefabID == 919059809) // doorframe - add door if missing
                    {
                        if (existing.FindLinkedEntity<Door>() == null)
                        {
                            BaseEntity baseFill = AddGrgDr(existing);
                            AddFloorLight(baseFill);
                            doors[level] = baseFill as Door;
                            FillUppSlot(doors[level]);
                            doors[level].knockEffect.guid = knockEffectGuid;
                            baseFill.SetFlag(BaseEntity.Flags.Locked, true, false);
                            crossReference[baseFill.net.ID] = this;
                            if (CurrentFloor == level) baseFill.SetFlag(BaseEntity.Flags.Open, true);
                            if (enableCost && collectAmounts != null)
                                CollectPayOut(ref collectAmounts, CostForObject(3647679950), costMultiplier);
                        }
                        else
                        {
                            doors[level] = existing.FindLinkedEntity<Door>();
                            FillUppSlot(doors[level]);
                        }
                        return true;
                    }
                }

                BaseEntity baseBlock = null;
                if (isDoor)
                {
                    baseBlock = AddWllFrm(baseBlocksFloors[level - 1][1], link);
                    if (baseBlock == null)
                        throw new InvalidOperationException(string.IsNullOrEmpty(_lastWallSpawnFailReason)
                            ? "Door frame failed to spawn."
                            : _lastWallSpawnFailReason);
                    baseBlock.transform.rotation = baseBlocksFloors[LowerFloor][1].transform.rotation;
                    BaseEntity baseFill = null;
                    baseFill = AddGrgDr(baseBlock);
                    if (baseFill == null)
                        throw new InvalidOperationException(string.IsNullOrEmpty(_lastWallSpawnFailReason)
                            ? "Garage door failed to spawn on new frame."
                            : _lastWallSpawnFailReason);
                    AddFloorLight(baseFill);
                    doors[level] = baseFill as Door;
                    FillUppSlot(doors[level]);
                    doors[level].knockEffect.guid = knockEffectGuid;
                    baseFill.SetFlag(BaseEntity.Flags.Locked, true, false);
                    crossReference[baseFill.net.ID] = this;
                    if (enableCost && collectAmounts != null)
                    {
                        CollectPayOut(ref collectAmounts, CostForPart((int)UpL.buildingGrade, 919059809), costMultiplier);
                        CollectPayOut(ref collectAmounts, CostForObject(3647679950), costMultiplier);
                    }
                }
                else
                {
                    baseBlock = AddWllSd(baseBlocksFloors[level - 1][1], link);
                    if (baseBlock == null)
                        throw new InvalidOperationException(string.IsNullOrEmpty(_lastWallSpawnFailReason)
                            ? "Wall failed to spawn."
                            : _lastWallSpawnFailReason);
                    baseBlock.transform.rotation = baseBlocksFloors[LowerFloor][1].transform.localRotation *
                                                   Quaternion.Euler(0, 180f, 0);
                    if (enableCost && collectAmounts != null)
                        CollectPayOut(ref collectAmounts, CostForPart((int)UpL.buildingGrade, 2194854973), costMultiplier);
                }

                baseBlock.SendNetworkUpdateImmediate();
                baseBlock.ClientRPC(RpcTarget.NetworkGroup(r("ErserfuFxva")));
                baseBlocksFloors[level][1] = baseBlock;
                return true;
            }

            private void FillUppSlot(Door door)
            {
                if (door == null) return;
                if (door.GetSlot(BaseEntity.Slot.UpperModifier) is TreeMarker) return;
                door.GetSlot(BaseEntity.Slot.UpperModifier)?.Kill();
                BaseEntity marker = GameManager.server.CreateEntity(StringPool.Get(954334883), new Vector3(),
                    new Quaternion(), true);
                if (marker == null) return;
                marker.Spawn();
                door.SetSlot(BaseEntity.Slot.UpperModifier, marker);
                door.canTakeCloser = false;
            }

            private void GetFloorFront(int level)
            {
                if (baseBlocksFloors[level - 1][1] == null) return;
                EntityLink link = GtLnk(baseBlocksFloors[level - 1][1]);
                if (link != null && link.IsOccupied())
                {
                    baseBlocksFloors[level][1] = link.connections[0].owner;
                    FtCmpnnts(baseBlocksFloors[level][1]);
                    if (baseBlocksFloors[level][1].prefabID == 919059809 &&
                        baseBlocksFloors[level][1].FindLinkedEntity<Door>() == null)
                    {
                        BaseEntity baseFill = AddGrgDr(baseBlocksFloors[level][1]);
                        AddFloorLight(baseFill);
                        doors[level] = baseFill as Door;
                        FillUppSlot(doors[level]);
                        doors[level].knockEffect.guid = knockEffectGuid;
                        baseFill.SetFlag(BaseEntity.Flags.Locked, true);
                        if (CurrentFloor == level) baseFill.SetFlag(BaseEntity.Flags.Open, true);
                    }
                    else
                    {
                        FillUppSlot(doors[level]);
                    }
                }
                else
                {
                    BaseEntity baseBlock = null;
                    baseBlock = AddWllSd(baseBlocksFloors[level - 1][1], link);
                    baseBlock.transform.rotation = baseBlocksFloors[LowerFloor][1].transform.localRotation *
                                                   Quaternion.Euler(0, 180f, 0);
                    baseBlock.ClientRPC(RpcTarget.NetworkGroup(r("ErserfuFxva")));
                    baseBlocksFloors[level][1] = baseBlock;
                }
            }

            private IEnumerator SwtchFlrFrnt(BasePlayer player, int level, bool hasDoor, Door door)
            {
                if (baseBlocksFloors[level - 1][1] == null) yield break;
                EntityLink link = GtLnk(baseBlocksFloors[level - 1][1]);
                if (link != null && link.IsOccupied())
                {
                    BaseEntity getBlock = link.connections[0].owner;
                    if (getBlock.prefabID == 919059809)
                    {
                        if (enableCost)
                        {
                            List<ItemAmount> collect = new List<ItemAmount>();
                            CollectPayOut(ref collect, CostForPart(3, 2194854973), costMultiplier);
                            if ((bool) GetAccess("EnableBuildCost", player.UserIDString))
                                foreach (ItemAmount collectAmount in collect)
                                {
                                    float have = (float) player.inventory.GetAmount(collectAmount.itemDef.itemid);
                                    if (have < collectAmount.amount)
                                    {
                                        SendEffect(3618221308, lift);
                                        yield break;
                                    }
                                }

                            PayInForShaft(collect, player);
                        }

                        Door getDoor = getBlock.FindLinkedEntity<Door>();
                        if (getDoor != null)
                        {
                            if (getDoor.HasSlot(BaseEntity.Slot.Lock))
                            {
                                BaseEntity reader = getDoor.GetSlot(BaseEntity.Slot.Lock);
                                if (reader != null)
                                {
                                    reader.Kill();
                                    yield return waitFOF;
                                }
                            }

                            doors[level] = null;
                            getDoor.Kill();
                            yield return waitFOF;
                        }

                        Effect.server.Run(StringPool.Get(2676581608), getBlock, 0u, Vector3.zero, Vector3.zero, null,
                            false);
                        getBlock.Kill();
                        AddFlrFrnt(level, !hasDoor);
                        controlUI.CrtSttngsUI(player);
                        yield break;
                    }
                    else if (getBlock.prefabID == 2194854973)
                    {
                        if (enableCost)
                        {
                            List<ItemAmount> collect = new List<ItemAmount>();
                            CollectPayOut(ref collect, CostForPart(3, 919059809), costMultiplier);
                            CollectPayOut(ref collect, CostForObject(3647679950), costMultiplier);
                            if ((bool) GetAccess("EnableBuildCost", player.UserIDString))
                                foreach (ItemAmount collectAmount in collect)
                                {
                                    float have = (float) player.inventory.GetAmount(collectAmount.itemDef.itemid);
                                    if (have < collectAmount.amount)
                                    {
                                        SendEffect(3618221308, lift);
                                        yield break;
                                    }
                                }

                            PayInForShaft(collect, player);
                        }

                        Effect.server.Run(StringPool.Get(2676581608), getBlock, 0u, Vector3.zero, Vector3.zero, null,
                            false);
                        getBlock.Kill();
                        AddFlrFrnt(level, !hasDoor);
                        controlUI.CrtSttngsUI(player);
                        yield break;
                    }
                }
            }

            private EntityLink GtLnk(BaseEntity block, int num = 0)
            {
                var links = block.GetEntityLinks();
                if (links == null) return null;
                foreach (EntityLink e in links)
                    if (e.socket.socketName.EndsWith(r("jnyy-srznyr")) ||
                        e.socket.socketName.EndsWith($"{r("senzr-srznyr")}/{num}") ||
                        e.socket.socketName.EndsWith($"{r("jnyy-srznyr")}/{num}"))
                        return e;
                return null;
            }

            private EntityLink GtLnkRf(BaseEntity block, int num = 0)
            {
                var links = block.GetEntityLinks();
                if (links == null) return null;
                foreach (EntityLink e in links)
                    if (e.socket.socketName.EndsWith(r("sybbe-srznyr")) ||
                        e.socket.socketName.EndsWith($"{r("sybbe-srznyr")}/{num}"))
                        return e;
                return null;
            }

            private BaseEntity AddWllSd(BaseEntity placeOn, EntityLink link)
            {
                Construction construction = PrefabAttribute.server.Find<Construction>(2194854973);
                Construction.Target target = default(Construction.Target);
                target.socket = link.socket;
                if (placeOn.prefabID is 72949757 or 916411076)
                    target.rotation += Quaternion.Euler(0, 180f, 0).eulerAngles;
                target.entity = placeOn;

                BaseEntity baseEntity = CrtCnstrctn(target, construction);
                if (baseEntity == null)
                {
                    _lastWallSpawnFailReason =
                        "Wall placement invalid (same as vanilla: not enough space, deploy blocked, or socket blocked).";
                    return null;
                }

                if (ObjctFnlzd(baseEntity))
                    return baseEntity;
                return null;
            }

            private BaseEntity AddWllFrm(BaseEntity placeOn, EntityLink link)
            {
                Construction construction = PrefabAttribute.server.Find<Construction>(919059809);
                Construction.Target target = default(Construction.Target);
                if (link != null && link.IsOccupied())
                {
                    link.connections[0].owner.Kill(BaseNetworkable.DestroyMode.None);
                    link.Clear();
                }

                target.socket = link.socket;
                target.entity = placeOn;
                BaseEntity baseEntity = CrtCnstrctn(target, construction);
                if (baseEntity == null)
                {
                    _lastWallSpawnFailReason =
                        "Door frame placement invalid (not enough space / blocked / occupied socket).";
                    return null;
                }

                if (ObjctFnlzd(baseEntity)) return baseEntity;
                return null;
            }

            private BaseEntity AddGrgDr(BaseEntity placeOn)
            {
                Construction construction = PrefabAttribute.server.Find<Construction>(3647679950);
                Construction.Target target = default(Construction.Target);
                Socket_Base[] source = PrefabAttribute.server.FindAll<Socket_Base>(placeOn.prefabID);
                target.socket = null;
                foreach (Socket_Base s in source)
                    if (s.socketName == $"{placeOn.ShortPrefabName}{r("/fbpxrgf/senzr-srznyr/1")}")
                    { target.socket = s; break; }
                if (target.socket == null) return null;
                EntityLink link = placeOn.FindLink(target.socket);
                if (link != null && link.IsOccupied())
                {
                    if (link.connections[0].owner.ShortPrefabName == r("jnyy.senzr.tnentrqbbe"))
                    {
                        return link.connections[0].owner;
                    }
                    else
                    {
                        DestroyOnClient(link.connections[0].owner);
                        link.connections[0].owner.Kill();
                    }
                }

                target.entity = placeOn;
                BaseEntity baseEntity = CrtCnstrctn(target, construction);
                if (baseEntity == null)
                {
                    _lastWallSpawnFailReason = "Garage door placement invalid on frame.";
                    return null;
                }

                if (ObjctFnlzd(baseEntity,
                        skinID != 0uL
                            ? skinID
                            : Convert.ToUInt64(GetAccess("DoorSkin", ownerID.ToString(), 0uL, true))) &&
                    baseEntity != null) return baseEntity;
                return null;
            }

            private bool ObjctFnlzd(BaseEntity baseEntity, ulong skin = new ulong())
            {
                if (baseEntity == null)
                {
                    _lastWallSpawnFailReason = "Spawn produced null entity.";
                    return false;
                }

                baseEntity.gameObject.AwakeFromInstantiate();
                BuildingBlock block = baseEntity as BuildingBlock;
                if (block)
                {
                    block.blockDefinition = PrefabAttribute.server.Find<Construction>(block.prefabID);
                    block.SetGrade((BuildingGrade.Enum) UpL.buildingGrade);
                    float num2 = block.currentGrade.maxHealth;
                }

                BaseCombatEntity combat = baseEntity as BaseCombatEntity;
                if (combat)
                {
                    float num2 = !(block != null) ? combat.startHealth : block.currentGrade.maxHealth;
                    combat.ResetLifeStateOnSpawn = false;
                    combat.InitializeHealth(num2, num2);
                }

                baseEntity.OwnerID = ownerID;
                baseEntity.skinID = skin;
                baseEntity.Spawn();
                if (baseEntity != null && baseEntity.net != null)
                {
                    FtCmpnnts(baseEntity);
                    if (baseEntity.prefabID == 919059809) SendEffect(172001365, baseEntity);
                    baseEntity.EntityLinkBroadcast();
                    if (ConVar.Server.stability)
                    {
                        StabilityEntity stabilityEntity = baseEntity as StabilityEntity;
                        if (stabilityEntity)
                        {
                            stabilityEntity.StabilityCheck();
                            if (stabilityEntity.cachedStability < ConVar.Stability.collapse)
                            {
                                _lastWallSpawnFailReason =
                                    $"Stability too low for new piece (stable={stabilityEntity.cachedStability:F1}, need>={ConVar.Stability.collapse}). " +
                                    "Add foundations/supports, or compare server.stability with the working server.";
                                return false;
                            }
                        }
                    }

                    return true;
                }

                _lastWallSpawnFailReason = "Entity missing net after Spawn().";
                return false;
            }

            private BaseEntity CrtCnstrctn(Construction.Target target, Construction component)
            {
                GameObject gameObject =
                    GameManager.server.CreatePrefab(component.fullName, Vector3.zero, Quaternion.identity, false);
                bool flag = UpdtPlcmnt(gameObject.transform, component, ref target);
                BaseEntity bsntt = gameObject.ToBaseEntity();
                if (!flag)
                {
                    if (bsntt.IsValid()) bsntt.Kill(BaseNetworkable.DestroyMode.None);
                    else GameManager.Destroy(gameObject, 0f);
                    return null;
                }

                DecayEntity dcyEntt = bsntt as DecayEntity;
                if (dcyEntt) dcyEntt.AttachToBuilding(target.entity as DecayEntity);
                return bsntt;
            }

            private bool UpdtPlcmnt(Transform tn, Construction common, ref Construction.Target target)
            {
                List<Socket_Base> list = Facepunch.Pool.Get<List<Socket_Base>>();
                common.FindMaleSockets(target, list);
                foreach (Socket_Base current in list)
                {
                    Construction.Placement plcmnt = default;
                    if (!(target.entity != null) || !(target.socket != null) ||
                        !target.entity.IsOccupied(target.socket))
                    {
                        if (!plcmnt.isPopulated) plcmnt = current.DoPlacement(target);
                        if (plcmnt.isPopulated)
                        {
                            tn.position = plcmnt.position;
                            tn.rotation = plcmnt.rotation;
                            Facepunch.Pool.FreeUnmanaged(ref list);
                            return true;
                        }
                    }
                }

                Facepunch.Pool.FreeUnmanaged(ref list);
                return false;
            }

            private IEnumerator ChngDrSkns()
            {
                for (int di = 0; di < doors.Length; di++)
                {
                    Door door = doors[di];
                    if (door == null) continue;
                    door.skinID = skinID;
                    door.SendNetworkUpdateImmediate();
                    SendEffect(172001365, door);
                    yield return waitFFU;
                }
            }

            private void StoreBox(BasePlayer player)
            {
                player.inventory.loot.StartLootingEntity(fuelBox, false);
                storeBox = new ItemContainer();
                storeBox.ServerInitialize(null, fuelBox.inventorySlots);
                storeBox.GiveUID();
                storeBox.onlyAllowedItems = new ItemDefinition[] { ItemManager.FindItemDefinition(-148794216) };
                storeBox.onItemAddedRemoved += new Action<Item, bool>(OnGarageDoorAdded);
                storeBox.playerOwner = player;
                player.inventory.loot.AddContainer(storeBox);
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player(r("ECP_BcraYbbgCnary"), player), r("znvyobkragel"));
            }

            private void OnGarageDoorAdded(Item item, bool bAdded)
            {
                ulong oldSkin = skinID;
                skinID = item.skin == 0uL
                    ? Convert.ToUInt64(GetAccess("DoorSkin", ownerID.ToString(), 0uL, true))
                    : item.skin;
                storeBox.itemList.Remove(item);
                storeBox.MarkDirty();
                storeBox.playerOwner.inventory.GiveItem(item);
                storeBox.playerOwner.EndLooting();
                storeBox.playerOwner.ClientRPC(RpcTarget.Player(r("BaQvrq"), storeBox.playerOwner));
                if (oldSkin != skinID) StartCoroutine(ChngDrSkns());
            }

            public void TunaBox(BasePlayer player, BaseOven oven, BaseEntity parent)
            {
                int f = Array.IndexOf(doors, (Door) parent);
                player.inventory.loot.StartLootingEntity(fuelBox, false);
                tunaBoxes[f] = new ItemContainer();
                tunaBoxes[f].ServerInitialize(null, fuelBox.inventorySlots);
                tunaBoxes[f].GiveUID();
                tunaBoxes[f].onlyAllowedItems = new ItemDefinition[] { ItemManager.FindItemDefinition(-946369541) };
                tunaBoxes[f].onItemAddedRemoved += new Action<Item, bool>(OnTunaFuelAdded);
                tunaBoxes[f].playerOwner = player;
                tunaBoxes[f].entityOwner = parent;
                player.inventory.loot.AddContainer(tunaBoxes[f]);
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player(r("ECP_BcraYbbgCnary"), player), r("znvyobkragel"));
            }

            private void OnTunaFuelAdded(Item item, bool bAdded)
            {
                if (!bAdded || item == null) return;
                ItemContainer parent = item.parent;
                int f = Array.IndexOf(tunaBoxes, parent);
                parent.itemList.Remove(item);
                parent.MarkDirty();
                InsertFuel(item);
                Effect.server.Run(fuelBox.mailDropSound.resourcePath, parent.entityOwner.transform.position,
                    default(Vector3), null, false);
                parent.playerOwner.EndLooting();
                parent.playerOwner.ClientRPC(RpcTarget.Player(r("BaQvrq"), parent.playerOwner));
                parent.playerOwner = null;
                parent.entityOwner = null;
                parent.Kill();
                tunaBoxes[f] = null;
            }

            public void SendEffect(uint id, BaseEntity ent)
            {
                Effect.server.Run(StringPool.Get(id), ent, 0u, Vector3.zero, Vector3.zero, null, false);
            }

            public void SendEffectTo(uint id, BaseEntity ent, BasePlayer player)
            {
                Effect effect = new Effect();
                effect.Init(Effect.Type.Generic, ent.transform.position, player.transform.forward, null);
                effect.pooledString = StringPool.Get(id);
                EffectNetwork.Send(effect, player.net.connection);
            }

            public void SendEffUnlock(BaseEntity e = null)
            {
                SendEffect(996087289, e != null ? e : lift);
            }

            public void SendEffLock(BaseEntity e = null)
            {
                SendEffect(242195671, e != null ? e : lift);
            }

            public void SendEffDenied(BaseEntity e = null)
            {
                SendEffect(4112119478, e != null ? e : lift);
            }

            public void SendEffUpdated(BaseEntity e = null)
            {
                SendEffect(4109975300, e != null ? e : lift);
            }

            public void SendEffDeploy(BaseEntity e = null)
            {
                SendEffect(1538559707, e != null ? e : lift);
            }

            public void SendEffFovChange(BaseEntity e = null)
            {
                SendEffect(2414984321, e != null ? e : lift);
            }

            public void SendEffRecycleStop(BaseEntity e = null)
            {
                SendEffect(3618221308, e != null ? e : lift);
            }

            public void SendEffRecycleStart(BaseEntity e = null)
            {
                SendEffect(3499498126, e != null ? e : lift);
            }
        }

        #endregion

        #region LiftUI Class

        public class LiftUI
        {
            public LiftUI()
            {
            }

            private string msg(string key, string id = null)
            {
                return UpL.lang.GetMessage(key, UpL, id);
            }

            public Elevator elev = null;
            private NetworkableId elevatorID;
            private BaseEntity baseEntity;
            private ProceduralLift lift;
            private int lastIndex;
            private char[] doorsToPlaceChar;
            private int offsetDown;
            private bool hasCreateCost;
            private float costMultiplier;
            private List<ItemAmount> collectAmounts;

            private string[] floorLetters = new string[]
            {
                " ", "⑴", "⑵", "⑶", "⑷", "⑸", "⑹", "⑺", "⑻", "⑼", "⑽", "⑾", "⑿", "⒀", "⒁", "⒂", "⒃", "⒄", "⒅", "⒆", "⒇"
            };

            private string[] floorLettersSingle = new string[]
            {
                " ", "⑴", "⑵", "⑶", "⑷", "⑸", "⑹", "⑺", "⑻", "⑼", "⑽", "⑾", "⑿", "⒀", "⒁", "⒂", "⒃", "⒄", "⒅", "⒆", "⒇"
            };

            private int maxRows;
            private int numSpecialBtns;
            private int maxButtons;
            private int maxColumns;
            private float pWitdhHalf;
            private int currentFloor;
            private int upperFloor;
            private int lowerFloor;
            private bool useFloorNumbers;
            private int stopLevel;
            private int startLevel;
            private int arrLength;
            private bool enableFuel;
            private string textColorIron = "0.878431373 0.400000000 0.200000000 0.75";
            private string textColorNickel = "0.313725490 0.815686275 0.313725490 0.75";
            private string textColorChlorine = "0.121568627 0.941176471 0.121568627 0.75";
            private string textColorKrypton = "0.360784314 0.721568627 0.819607843 0.75";
            private string textColorSelenium = "1.0 0.631372549 0.0 0.75";
            private string textColorGold = "1.0 0.819607843 0.137254902 0.75";
            private string textColorBohrium = "0.878431373 0.0 0.219607843 0.75";
            private string textColorSilicon = "0.941176471 0.784313725 0.627450980 0.5";
            private string textColorMercury = "0.721568627 0.721568627 0.815686275 0.5";
            private string bgColorBromine = "0.650980392 0.160784314 0.160784314 0.05";
            private string bgColorAstantine = "0.458823529 0.309803922 0.270588235 0.5";
            private string buttonColor = "0.25 0.25 0.25 0.5";
            private string font = r("EbobgbPbaqrafrq-Erthyne.ggs");

            public static void DestroyAllUi(BasePlayer player)
            {
                if (player == null) return;
                CuiHelper.DestroyUi(player, cabinControlUI);
                CuiHelper.DestroyUi(player, cabinDestroyUI);
                CuiHelper.DestroyUi(player, cabinSettingsUI);
                CuiHelper.DestroyUi(player, cabinPlacementUI);
                CuiHelper.DestroyUi(player, cabinSharingUI);
            }

            public void DestroyUi(BasePlayer player)
            {
                if (player == null) return;
                CuiHelper.DestroyUi(player, cabinControlUI);
                CuiHelper.DestroyUi(player, cabinDestroyUI);
                CuiHelper.DestroyUi(player, cabinSettingsUI);
                CuiHelper.DestroyUi(player, cabinPlacementUI);
                CuiHelper.DestroyUi(player, cabinSharingUI);
            }

            public void Init(Elevator e, NetworkableId id)
            {
                elev = e;
                maxRows = 5;
                elevatorID = id;
                upperFloor = elev.UpperFloor;
                lowerFloor = elev.LowerFloor;
                enableFuel = elev.EnableFuel;
                useFloorNumbers = upperFloor > 20;
            }

            public void IntPlcmnt(Elevator e, ProceduralLift l, BasePlayer player, BaseEntity ent, int offSet,
                int startFrom, int limitRange, int maxFloor)
            {
                elev = e;
                lift = l;
                offsetDown = offSet;
                baseEntity = ent;
                startLevel = startFrom;
                int maxRange = Math.Min(limitRange, (int) GetAccess("MaxFloorRange", player.UserIDString, 3, true));
                stopLevel = Math.Min(maxFloor, maxRange + startFrom);
                if (stopLevel <= 0) stopLevel = limitRange + startFrom - 1;
                arrLength = stopLevel + 1;
                doorsToPlaceChar = new char[arrLength];
                for (int ci = 0; ci < arrLength; ci++) doorsToPlaceChar[ci] = '0';
                lastIndex = stopLevel;
                doorsToPlaceChar[startLevel] = '1';
                doorsToPlaceChar[lastIndex] = '1';
                hasCreateCost = (bool) GetAccess("EnableBuildCost", player.UserIDString, true);
                costMultiplier = Convert.ToSingle(GetAccess("BuildCostMultiplier", player.UserIDString, 1f));
                if (hasCreateCost) collectAmounts = elev.GetPartsCost(3, lastIndex, doorsToPlaceChar, costMultiplier);
                CrtPlcmntUI(player, lastIndex, doorsToPlaceChar);
            }

            public void UpdtPlcmnt(BasePlayer player, string action, int num)
            {
                elev.SendEffectTo(3499498126, player, player);
                if (action == "accept")
                {
                    if (num == 0)
                    {
                        elev.SendEffectTo(4112119478, player, player);
                        UpL.playerStartups.Remove(player.userID);
                        UnityEngine.Object.Destroy(lift.gameObject.GetComponent<Elevator>());
                        UnityEngine.Object.Destroy(lift, 1f);
                        try
                        {
                            lift.Kill();
                        }
                        catch
                        {
                        }

                        return;
                    }

                    baseEntity.RefreshEntityLinks();
                    int doorCount = 0;
                    int floorCount = lastIndex - startLevel + 1;
                    for (int i = startLevel; i <= lastIndex; i++)
                        if (doorsToPlaceChar[i] == '1') doorCount++;
                    bool allowDoorItem = (bool) GetAccess("AllowDoorItemOrCost", player.UserIDString, true);
                    bool allowElevatorItem = (bool) GetAccess("AllowElevatorItemPerFloor", player.UserIDString, true);
                    string doorShortnamePlace = Convert.ToString(UpL.GetConfig("BuildCostItems", "DoorItemShortname", "wall.frame.garagedoor"));
                    ItemDefinition doorItemDefPlace = ItemManager.FindItemDefinition(doorShortnamePlace);
                    if (doorItemDefPlace == null && doorShortnamePlace != "wall.frame.garagedoor")
                        doorItemDefPlace = ItemManager.FindItemDefinition("wall.frame.garagedoor");
                    ItemDefinition elevatorItemDef = ItemManager.FindItemDefinition(Convert.ToString(UpL.GetConfig("BuildCostItems", "ElevatorItemShortname", "elevator")));
                    int doorItemId = doorItemDefPlace?.itemid ?? 0;
                    int elevatorItemId = elevatorItemDef?.itemid ?? 0;
                    int requiredElevatorItems = floorCount * elevatorItemsPerFloor;
                    bool useItemPath = hasCreateCost && useElevatorAsCost && allowDoorItem && allowElevatorItem && doorItemId != 0 && elevatorItemId != 0
                        && player.inventory.GetAmount(elevatorItemId) >= requiredElevatorItems
                        && player.inventory.GetAmount(doorItemId) >= doorCount;
                    if (!elev.IntNw(lift, baseEntity, player, offsetDown, startLevel, lastIndex, doorsToPlaceChar,
                        collectAmounts, hasCreateCost, costMultiplier, useItemPath, doorCount, floorCount, doorItemId, elevatorItemId))
                    {
                        elev.SendEffectTo(4112119478, player, player);
                        UpL.playerStartups.Remove(player.userID);
                        UnityEngine.Object.Destroy(lift.gameObject.GetComponent<Elevator>());
                        UnityEngine.Object.Destroy(lift, 1f);
                        try
                        {
                            lift.Kill();
                        }
                        catch
                        {
                        }

                        return;
                    }

                    elev.SendEffectTo(1538559707, player, player);
                    crossReference[lift.net.ID] = elev;
                    UpL.playerStartups.Remove(player.userID);
                    return;
                }

                if (action == "level")
                {
                    elev.SendEffectTo(996087289, player, player);
                    if (num == startLevel) num++;
                    if (lastIndex < num && num > startLevel) doorsToPlaceChar[lastIndex] = '0';
                    lastIndex = num;
                    doorsToPlaceChar[startLevel] = '1';
                    doorsToPlaceChar[lastIndex] = '1';
                    if (hasCreateCost)
                        collectAmounts = elev.GetPartsCost(3, lastIndex, doorsToPlaceChar, costMultiplier);
                    CrtPlcmntUI(player, num, doorsToPlaceChar);
                    return;
                }

                if (action == "door")
                {
                    doorsToPlaceChar[num] = doorsToPlaceChar[num] == '1' ? '0' : '1';
                    if (num > lastIndex) doorsToPlaceChar[num] = '0';
                    if (num == 0 || num == lastIndex) doorsToPlaceChar[num] = '1';
                    else if (num <= lastIndex)
                        elev.SendEffectTo(doorsToPlaceChar[num] == '1' ? 996087289u : 242195671u, player, player);
                    doorsToPlaceChar[0] = '1';
                    doorsToPlaceChar[lastIndex] = '1';
                    if (hasCreateCost)
                        collectAmounts = elev.GetPartsCost(3, lastIndex, doorsToPlaceChar, costMultiplier);
                    CrtPlcmntUI(player, lastIndex, doorsToPlaceChar);
                    return;
                }
            }

            private float[] ButtonPosControl(int i)
            {
                float bHeight = 1f / maxRows;
                float bWidth = 1f / maxColumns;
                int colNumber = i == 0 ? 0 : Mathf.FloorToInt(i / maxRows);
                int rowNumber = i - colNumber * maxRows;
                float offsetX = 0f + colNumber * bWidth;
                float offsetY = 0f + bHeight * rowNumber;
                return new float[]
                    {offsetX + 0.005f, offsetY + 0.005f, offsetX + bWidth - 0.005f, offsetY + bHeight - 0.005f};
            }

            private const string BtnColorHighlight = "0.18 0.18 0.18 0.92";
            private const string BtnColorPressed = "0.12 0.12 0.12 0.98";
            private const float BtnFadeDuration = 0.1f;

            private CuiPanel NewPanel()
            {
                return new CuiPanel
                {
                    Image = new CuiImageComponent {Color = bgColorBromine}, CursorEnabled = true,
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}
                };
            }

            private CuiElement NewRootPanel(string name, string destroyUi = null)
            {
                return new CuiElement
                {
                    Name = name,
                    Parent = "Overlay",
                    DestroyUi = destroyUi ?? name,
                    Components =
                    {
                        new CuiNeedsCursorComponent(),
                        new CuiImageComponent {Color = bgColorBromine},
                        new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                    }
                };
            }

            private CuiPanel NewBorderPanel(string c, string x, string y)
            {
                return new CuiPanel
                    {Image = new CuiImageComponent {Color = c}, RectTransform = {AnchorMin = x, AnchorMax = y}};
            }

            private CuiButton NewBgButton(string cmd, string viewForm)
            {
                return new CuiButton
                {
                    Button = {Command = cmd, Close = viewForm, Color = "0 0 0 0"},
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}, Text = {Text = string.Empty}
                };
            }

            private CuiButton NormalButton(string x, string y, string cmd, string close, string color, string text,
                string tColor, int fontsize = 15, TextAnchor align = TextAnchor.MiddleCenter, bool interactive = true)
            {
                var btn = new CuiButton
                {
                    RectTransform = {AnchorMin = x, AnchorMax = y},
                    Button = {Command = cmd, Close = close, Color = color},
                    Text = {Align = align, Text = text, Color = tColor, FontSize = fontsize, Font = font}
                };
                if (interactive && !string.IsNullOrEmpty(cmd))
                {
                    btn.Button.FadeIn = BtnFadeDuration;
                    btn.Button.HighlightedColor = BtnColorHighlight;
                    btn.Button.PressedColor = BtnColorPressed;
                }
                return btn;
            }

            private CuiButton PsBttn(int count, string command, string close, string bcolor, string text, string tcolor,
                int fontsize = 15)
            {
                float[] buPo = ButtonPosControl(count);
                var btn = new CuiButton
                {
                    RectTransform = {AnchorMin = $"{buPo[0]} {buPo[1]}", AnchorMax = $"{buPo[2]} {buPo[3]}"},
                    Button = {Command = command, Close = close, Color = bcolor},
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter, Text = text, Color = tcolor, FontSize = fontsize, Font = font
                    }
                };
                if (!string.IsNullOrEmpty(command))
                {
                    btn.Button.FadeIn = BtnFadeDuration;
                    btn.Button.HighlightedColor = BtnColorHighlight;
                    btn.Button.PressedColor = BtnColorPressed;
                }
                return btn;
            }

            public bool WayUpFree(BaseEntity ent, int fls)
            {
                RaycastHit hit;
                Ray ray;
                for (int l = 0; l < fls; l++)
                {
                    ray = new Ray(ent.transform.position + Vector3.up * (l * 3), ent.transform.up);
                    if (Physics.Raycast(ray, out hit, 3f, 2097152)) return false;
                }

                return true;
            }

            public void CrtPlcmntUI(BasePlayer player, int indexAt, char[] placeDoorsChar)
            {
                bool enoughRes = true; /*bool isUpFree = Prevent_Building.WayUpFree(baseEntity, indexAt);*/
                bool isUpFree = WayUpFree(baseEntity, indexAt);
                StringBuilder sbMaterials = new StringBuilder();
                StringBuilder sbAlternatives = new StringBuilder();
                int doorCount = 0;
                int floorCount = indexAt - startLevel + 1;
                for (int i = startLevel; i <= indexAt; i++)
                    if (placeDoorsChar[i] == '1') doorCount++;
                bool allowDoorItem = (bool) GetAccess("AllowDoorItemOrCost", player.UserIDString, true);
                bool allowElevatorItem = (bool) GetAccess("AllowElevatorItemPerFloor", player.UserIDString, true);
                string doorShortname = Convert.ToString(UpL.GetConfig("BuildCostItems", "DoorItemShortname", "wall.frame.garagedoor"));
                ItemDefinition doorItemDef = ItemManager.FindItemDefinition(doorShortname);
                if (doorItemDef == null && doorShortname != "wall.frame.garagedoor")
                    doorItemDef = ItemManager.FindItemDefinition("wall.frame.garagedoor");
                ItemDefinition elevatorItemDef = ItemManager.FindItemDefinition(Convert.ToString(UpL.GetConfig("BuildCostItems", "ElevatorItemShortname", "elevator")));
                int doorItemId = doorItemDef?.itemid ?? 0;
                int elevatorItemId = elevatorItemDef?.itemid ?? 0;
                bool enoughResMaterials = true;
                if (hasCreateCost)
                {
                    foreach (ItemAmount collectAmount in collectAmounts)
                    {
                        float have = (float) player.inventory.GetAmount(collectAmount.itemDef.itemid);
                        if (have < collectAmount.amount)
                        {
                            enoughResMaterials = false;
                            enoughRes = false;
                            sbMaterials.AppendLine(
                                $"<color=#ff6b00>{msg("Bad")}   </color>> {collectAmount.itemDef.displayName.english} (<color=#ff6b00>{have}</color> < {collectAmount.amount})");
                        }
                        else
                        {
                            sbMaterials.AppendLine(
                                $"<color=#00f700>{msg("Good")} </color>> {collectAmount.itemDef.displayName.english} (<color=#00f700>{have}</color> > {collectAmount.amount})");
                        }
                    }
                    if (allowDoorItem && allowElevatorItem && useElevatorAsCost)
                    {
                        int haveDoors = doorItemId != 0 ? player.inventory.GetAmount(doorItemId) : 0;
                        int haveElevators = elevatorItemId != 0 ? player.inventory.GetAmount(elevatorItemId) : 0;
                        int requiredElevators = floorCount * elevatorItemsPerFloor;
                        bool enoughResItems = haveDoors >= doorCount && haveElevators >= requiredElevators;
                        if (enoughResItems) enoughRes = true;
                        if (haveElevators < requiredElevators)
                            sbAlternatives.AppendLine(
                                $"<color=#ff6b00>{msg("Bad")}   </color>> {msg("Elevators")} (<color=#ff6b00>{haveElevators}</color> < {requiredElevators})");
                        else
                            sbAlternatives.AppendLine(
                                $"<color=#00f700>{msg("Good")} </color>> {msg("Elevators")} (<color=#00f700>{haveElevators}</color> > {requiredElevators})");
                        if (haveDoors < doorCount)
                            sbAlternatives.AppendLine(
                                $"<color=#ff6b00>{msg("Bad")}   </color>> {msg("Garage Doors")} (<color=#ff6b00>{haveDoors}</color> < {doorCount})");
                        else
                            sbAlternatives.AppendLine(
                                $"<color=#00f700>{msg("Good")} </color>> {msg("Garage Doors")} (<color=#00f700>{haveDoors}</color> > {doorCount})");
                    }
                }

                if (!hasCreateCost) enoughRes = true;
                CuiElementContainer result = new CuiElementContainer();
                result.Add(NewRootPanel(cabinPlacementUI));
                string rootPanelName = cabinPlacementUI;
                result.Add(NewBgButton(string.Empty, string.Empty), rootPanelName);
                float resizeX = (float) arrLength - 21f;
                if (resizeX < 15) resizeX = 15;
                float x1 = 0.375f - resizeX * 0.005f;
                float x2 = 0.625f + resizeX * 0.005f;
                float yP = 0.705f;
                float rootBottom = hasCreateCost ? 0.345f : 0.425f;
                result.Add(
                    NewBorderPanel("0.4 0.4 0.4 1.0", $"{x1 - 0.005} {rootBottom}", $"{x2 + 0.005} {yP}"), rootPanelName);
                result.Add(NewBgButton(string.Empty, string.Empty), rootPanelName);
                yP -= 0.005f;
                string headerPanel1 = result.Add(NewBorderPanel(bgColorAstantine, $"{x1} {yP - 0.05f}", $"{x2} {yP}"),
                    rootPanelName);
                yP -= 0.055f;
                string headerPanel2 = result.Add(NewBorderPanel(bgColorAstantine, $"{x1} {yP - 0.05f}", $"{x2} {yP}"),
                    rootPanelName);
                yP -= 0.055f;
                string headerPanel3 = result.Add(NewBorderPanel(bgColorAstantine, $"{x1} {yP - 0.05f}", $"{x2} {yP}"),
                    rootPanelName);
                yP -= 0.055f;
                string headerPanel4 = result.Add(NewBorderPanel(bgColorAstantine, $"{x1} {yP - 0.05f}", $"{x2} {yP}"),
                    rootPanelName);
                string headerPanel5 = null;
                string headerPanel6;
                if (hasCreateCost)
                {
                    yP -= 0.055f + 0.025f;
                    headerPanel5 =
                        result.Add(NewBorderPanel(bgColorAstantine, $"{x1} {yP - 0.05f}", $"{x2} {yP + 0.025f}"),
                            rootPanelName);
                }
                yP -= 0.055f;
                headerPanel6 = result.Add(NewBorderPanel(bgColorAstantine, $"{x1} {yP - 0.05f}", $"{x2} {yP}"),
                    rootPanelName);
                result.Add(
                    NormalButton($"0.1 0", $"0.9 1", "", "", "0 0 0 0", msg("Choose the planned Lift size"),
                        textColorGold), headerPanel1);
                result.Add(
                    NormalButton($"0.1 0", $"0.9 1", "", "", "0 0 0 0", msg("Choose where to pre-place doors"),
                        textColorGold), headerPanel3);
                if (hasCreateCost)
                {
                    string materialsText = sbMaterials.ToString().Replace("\r", "");
                    string alternativesText = sbAlternatives.ToString().Replace("\r", "");
                    if (!string.IsNullOrEmpty(materialsText))
                        result.Add(
                            NormalButton($"0.02 0.05", $"0.40 0.95", "", "", "0 0 0 0", materialsText,
                                textColorKrypton, 14, TextAnchor.UpperLeft, interactive: false), headerPanel5);
                    if (!string.IsNullOrEmpty(alternativesText))
                    {
                        result.Add(
                            NormalButton($"0.40 0.38", $"0.60 0.62", "", "", "0 0 0 0", msg("Or"),
                                textColorKrypton, 15, TextAnchor.MiddleCenter, interactive: false), headerPanel5);
                        result.Add(
                            NormalButton($"0.60 0.05", $"0.98 0.95", "", "", "0 0 0 0", alternativesText,
                                textColorKrypton, 14, TextAnchor.UpperLeft, interactive: false), headerPanel5);
                    }
                }
                string textColor = isUpFree ? textColorKrypton : textColorSelenium;
                string command = enoughRes ? $"_ul.placement  accept 1" : "";
                string close = enoughRes ? cabinPlacementUI : "";
                string text = isUpFree ? msg("Do Placement") : msg("Do Place (Upward Overwrite)");
                int size = isUpFree ? 15 : 10;
                string button;
                string color;
                if (hasCreateCost && !enoughRes)
                {
                    textColor = textColorSilicon;
                    command = "";
                    close = "";
                    text = msg("Do Placement");
                    size = 15;
                }

                result.Add(NormalButton($"0.05 0.2", $"0.45 0.8", command, close, buttonColor, text, textColor, size),
                    headerPanel6);
                result.Add(
                    NormalButton($"0.55 0.2", $"0.95 0.8", $"_ul.placement  accept 0", cabinPlacementUI, buttonColor,
                        msg("Cancel Action"), textColorKrypton, interactive: true), headerPanel6);
                float step = 1f / (arrLength - startLevel);
                float x = 0f;
                for (int p = startLevel; p < arrLength; p++)
                {
                    if (p > indexAt) placeDoorsChar[p] = '0';
                    x += step;
                    button = indexAt < p ? "0.25 0.25 0.25 0.25" : "0.25 0.25 0.25 0.75";
                    color = indexAt < p ? textColorKrypton : textColorGold;
                    command = p != startLevel && p != indexAt ? $"_ul.placement level {p}" : "";
                    result.Add(
                        NormalButton($"{x - step + 0.005f} 0", $"{x - 0.005f} 1", command, "", button,
                            $"{p.ToString()}", color), headerPanel2);
                    command = p < indexAt && p != startLevel && p != indexAt ? $"_ul.placement door {p}" : "";
                    button = placeDoorsChar[p] == '1' || p == startLevel || p == indexAt
                        ? "0.25 0.25 0.25 0.75"
                        : "0.25 0.25 0.25 0.25";
                    color = placeDoorsChar[p] == '1' || p == startLevel || p == indexAt
                        ? textColorGold
                        : textColorKrypton;
                    result.Add(
                        NormalButton($"{x - step + 0.005f} 0", $"{x - 0.005f} 1", command, "", button,
                            $"{p.ToString()}", color), headerPanel4);
                }

                CuiHelper.AddUi(player, result);
            }

            public void CrtCbnUI(BasePlayer player, bool freshOpen = false)
            {
                elev.SendEffUnlock();
                int cP = 0;
                numSpecialBtns = 5;
                currentFloor = elev.CurrentFloor;
                bool isOwner = elev.IsOwner(player);
                bool isPassenger = elev.IsPassenger(player.userID) || IsAdm(player);
                maxButtons = elev.GetDoorCount + numSpecialBtns;
                maxColumns = Mathf.CeilToInt((float) maxButtons / (float) maxRows);
                pWitdhHalf = maxColumns * 0.05f;
                CuiElementContainer result = new CuiElementContainer();
                result.Add(NewRootPanel(cabinControlUI));
                string rootPanelName = cabinControlUI;
                result.Add(NewBgButton(string.Empty, cabinControlUI), rootPanelName);
                result.Add(
                    NewBorderPanel("0.4 0.4 0.4 1.0", $"{0.495f - pWitdhHalf} 0.295", $"{0.505f + pWitdhHalf} 0.705"),
                    rootPanelName);
                result.Add(NewBgButton(string.Empty, cabinControlUI), rootPanelName);
                string headerPanel =
                    result.Add(NewBorderPanel(bgColorAstantine, $"{0.5f - pWitdhHalf} 0.3", $"{0.5f + pWitdhHalf} 0.7"),
                        rootPanelName);
                if (isOwner)
                    for (int p = lowerFloor; p < upperFloor + 1; p++)
                    {
                        Door d = elev.GetDoorAt(p);
                        if (d == null) continue;
                        cP++;
                        string textColor = currentFloor != p ? textColorIron : textColorGold;
                        string command = currentFloor != p
                            ? $"_ul.commands {elevatorID} move {p}"
                            : $"_ul.commands {elevatorID} move 0";
                        string close = currentFloor != p ? cabinControlUI : string.Empty;
                        if (!isPassenger)
                        {
                            textColor = textColorMercury;
                            command = string.Empty;
                            close = string.Empty;
                        }

                        result.Add(
                            PsBttn(cP - 1, command, close, buttonColor,
                                GetFloorDisplayName(p, lowerFloor, true), textColor, 40), headerPanel);
                    }
                else
                    for (int p = lowerFloor; p < upperFloor + 1; p++)
                    {
                        Door d = elev.GetDoorAt(p);
                        if (d == null) continue;
                        string fShare = elev.GetFloorMode(p);
                        bool hasAccess = elev.HasFloorAccess(player, fShare, p);
                        if (fShare == St.FsHidden || fShare == St.FsHiddenVIP && !hasAccess) continue;
                        cP++;
                        string close = string.Empty;
                        string textColor = textColorMercury;
                        string command = string.Empty;
                        if (fShare == St.FsExcluded)
                        {
                            textColor = textColorMercury;
                            command = string.Empty;
                            close = string.Empty;
                        }

                        if (hasAccess)
                        {
                            textColor = currentFloor != p ? textColorNickel : textColorChlorine;
                            command = currentFloor != p
                                ? $"_ul.commands {elevatorID} move {p}"
                                : $"_ul.commands {elevatorID} move 0";
                            close = currentFloor != p ? cabinControlUI : string.Empty;
                        }

                        result.Add(
                            PsBttn(cP - 1, command, close, buttonColor,
                                GetFloorDisplayName(p, lowerFloor, true), textColor, 40), headerPanel);
                    }

                int fillCount = maxColumns * maxRows - numSpecialBtns - cP;
                for (int f = 1; f < fillCount + 1; f++)
                    result.Add(PsBttn(cP++, string.Empty, string.Empty, buttonColor, "¨", textColorSilicon, 40),
                        headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} open {currentFloor}", cabinControlUI, buttonColor, "↔",
                        textColorSelenium, 40), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} close {currentFloor}", cabinControlUI, buttonColor, "↺",
                        textColorSelenium, 40), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} cancel {currentFloor}", cabinControlUI, buttonColor, "↲",
                        textColorSelenium, 40), headerPanel);
                bool flag = enableFuel == true && isOwner == true && elev.GetPowerState == St.PowerInternal;
                result.Add(
                    PsBttn(cP++, flag ? $"_ul.commands {elevatorID} fuel 0" : string.Empty,
                        flag ? cabinControlUI : string.Empty, buttonColor,
                        enableFuel ? $"↯<size=18>({elev.GetFuel()}F)</size>" : $"↯<size=18>({msg("FoC")})</size>",
                        textColorSelenium, 40), headerPanel);
                result.Add(
                    PsBttn(cP++, isOwner == true ? $"_ul.commands {elevatorID} move 7777" : string.Empty,
                        isOwner == true ? cabinControlUI : string.Empty, buttonColor, "Ⓢ",
                        isOwner == true ? textColorSelenium : textColorMercury, 40), headerPanel);
                CuiHelper.AddUi(player, result);
            }

            public void CrtDstryUI(BasePlayer player)
            {
                CuiElementContainer result = new CuiElementContainer();
                result.Add(NewRootPanel(cabinDestroyUI, cabinControlUI));
                string rootPanelName = cabinDestroyUI;
                result.Add(NewBgButton(string.Empty, cabinDestroyUI), rootPanelName);
                result.Add(NewBorderPanel("0.4 0.4 0.4 1.0", $"0.345 0.445", $"0.655 0.555"), rootPanelName);
                result.Add(NewBgButton(string.Empty, cabinDestroyUI), rootPanelName);
                string headerPanel = result.Add(NewBorderPanel(bgColorAstantine, $"0.35 0.45", $"0.65 0.55"),
                    rootPanelName);
                result.Add(
                    NormalButton($"0.05 0.2", $"0.55 0.8", $"_ul.commands {elevatorID} destroy 1", cabinDestroyUI,
                        buttonColor, msg("Remove Lift"), textColorBohrium, 17), headerPanel);
                result.Add(
                    NormalButton($"0.55 0.2", $"0.95 0.8", $"_ul.commands {elevatorID} destroy 0", cabinDestroyUI,
                        buttonColor, msg("Cancel"), textColorKrypton, 17), headerPanel);
                CuiHelper.AddUi(player, result);
            }

            public void CrtSttngsUI(BasePlayer player)
            {
                elev.SendEffUnlock();
                elev.SetUpKeep(true);
                int cP = 0;
                numSpecialBtns = 10;
                currentFloor = elev.CurrentFloor;
                maxButtons = upperFloor + 1 - lowerFloor + numSpecialBtns;
                maxColumns = Mathf.CeilToInt((float) maxButtons / (float) maxRows);
                pWitdhHalf = maxColumns * 0.05f;
                CuiElementContainer result = new CuiElementContainer();
                result.Add(NewRootPanel(cabinSettingsUI));
                string rootPanelName = cabinSettingsUI;
                result.Add(NewBgButton($"_ul.commands {elevatorID} state 0", cabinSettingsUI), rootPanelName);
                result.Add(
                    NewBorderPanel("0.4 0.4 0.4 1.0", $"{0.495f - pWitdhHalf} 0.295", $"{0.505f + pWitdhHalf} 0.705"),
                    rootPanelName);
                result.Add(NewBgButton($"_ul.commands {elevatorID} state 0", cabinSettingsUI), rootPanelName);
                string headerPanel =
                    result.Add(NewBorderPanel(bgColorAstantine, $"{0.5f - pWitdhHalf} 0.3", $"{0.5f + pWitdhHalf} 0.7"),
                        rootPanelName);
                for (int p = lowerFloor; p < upperFloor + 1; p++)
                {
                    bool hasDoor = elev.GetDoorAt(p) != null ? true : false;
                    cP++;
                    string textColor = hasDoor ? textColorIron : textColorNickel;
                    textColor = p == lowerFloor || p == upperFloor || p == currentFloor ? textColorGold : textColor;
                    string command = $"_ul.commands {elevatorID} switch {p}";
                    command = p == lowerFloor || p == upperFloor || p == currentFloor
                        ? $"_ul.commands {elevatorID} switch 0"
                        : command;
                    string floorName = GetFloorDisplayName(p, lowerFloor, false);
                    string text = hasDoor
                        ? $"{msg("Floor")} {floorName}\n- {msg("Door")} -"
                        : $"{msg("Floor")} {floorName}\n+ {msg("Door")} +";
                    text = p == lowerFloor || p == upperFloor ? $"{msg("Floor")} {floorName}\n{msg("Static")}" : text;
                    text = p == currentFloor ? $"{msg("Floor")} {floorName}\n{msg("Current")}" : text;
                    result.Add(PsBttn(cP - 1, command, string.Empty, buttonColor, text, textColor, 15), headerPanel);
                }

                int fillCount = maxColumns * maxRows - numSpecialBtns - cP;
                for (int f = 1; f < fillCount + 1; f++)
                    result.Add(PsBttn(cP++, string.Empty, string.Empty, buttonColor, "¨", textColorSilicon, 45),
                        headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} move 6666", cabinSettingsUI, buttonColor,
                        msg("Tear down\nLift"), textColorBohrium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} move 1111", cabinSettingsUI, buttonColor,
                        msg("Operating\nPanel"), textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} cabinlight 0", string.Empty, buttonColor,
                        msg("Lighting") +
                        $"\n<color=#2e96ad>{msg($"CabinLightMode.{elev.GetLightMode}")}</color>",
                        textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} speed 0", string.Empty, buttonColor,
                        msg("Move Speed") +
                        $"\n<color=#2e96ad>{elev.MoveSpeed.ToString()}</color> / <color=#2e96ad>{elev.MaxSpeed.ToString()}</color>",
                        textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} floorwait 0", string.Empty, buttonColor,
                        msg("Floor OnHold") + $"\n<color=#2e96ad>{elev.FloorWait.ToString()}</color> s",
                        textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} idlewait 0", string.Empty, buttonColor,
                        msg("Idle Close") +
                        $"\n<color=#2e96ad>{(elev.IdleWait != -1 ? elev.IdleWait.ToString() : "∞")}</color> s",
                        textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, enableFuel ? $"_ul.commands {elevatorID} fuel 1" : string.Empty, string.Empty,
                        buttonColor,
                        msg("Power State") +
                        $"\n <color=#2e96ad>{msg($"PowerState.{elev.GetPowerState}")}</color>",
                        textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} sharemode 0", string.Empty, buttonColor,
                        msg("Share Mode") +
                        $"\n <color=#2e96ad>{msg($"ShareMode.{elev._sharemode}")} {elev.GetShareMode()}</color>",
                        textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} sharemode 1", cabinSettingsUI, buttonColor,
                        msg("Floor Sharing\nOptions"), textColorSelenium), headerPanel);
                bool canSkin = (bool) GetAccess("CanReskin", player.UserIDString, false, true);
                string skinId =
                    elev.GetSkinID == Convert.ToUInt64(GetAccess("DoorSkin", player.UserIDString, 0uL, true))
                        ? "Default"
                        : elev.GetSkinID.ToString();
                result.Add(
                    PsBttn(cP++, canSkin ? $"_ul.commands {elevatorID} doorskin 0" : string.Empty,
                        canSkin ? cabinSettingsUI : string.Empty, buttonColor,
                        msg("Door Skin") + $"\n<color=#2e96ad>{(canSkin ? skinId : "n.a.")}</color>",
                        textColorSelenium), headerPanel);
                CuiHelper.AddUi(player, result);
            }

            public void CrtShrngUI(BasePlayer player)
            {
                int cP = 0;
                numSpecialBtns = 5;
                maxButtons = elev.GetDoorCount + numSpecialBtns;
                maxColumns = Mathf.CeilToInt((float) maxButtons / (float) maxRows);
                pWitdhHalf = maxColumns * 0.05f;
                CuiElementContainer result = new CuiElementContainer();
                result.Add(NewRootPanel(cabinSharingUI));
                string rootPanelName = cabinSharingUI;
                result.Add(NewBgButton(string.Empty, cabinSharingUI), rootPanelName);
                result.Add(
                    NewBorderPanel("0.4 0.4 0.4 1.0", $"{0.495f - pWitdhHalf} 0.295", $"{0.505f + pWitdhHalf} 0.705"),
                    rootPanelName);
                result.Add(NewBgButton(string.Empty, cabinSharingUI), rootPanelName);
                string headerPanel =
                    result.Add(NewBorderPanel(bgColorAstantine, $"{0.5f - pWitdhHalf} 0.3", $"{0.5f + pWitdhHalf} 0.7"),
                        rootPanelName);
                for (int p = lowerFloor; p < upperFloor + 1; p++)
                {
                    Door d = elev.GetDoorAt(p);
                    if (d == null) continue;
                    cP++;
                    string textColor = textColorSilicon;
                    string command = $"_ul.commands {elevatorID} floormode {p}";
                    string close = string.Empty;
                    string floorName = GetFloorDisplayName(p, lowerFloor, false);
                    string text =
                        $"{msg("Floor")} {floorName}\n<color=#2e96ad>{msg($"FloorShare.{elev.GetFloorMode(p)}")}</color>";
                    result.Add(PsBttn(cP - 1, command, close, buttonColor, text, textColor, 15), headerPanel);
                }

                int fillCount = maxColumns * maxRows - numSpecialBtns - cP;
                for (int f = 1; f < fillCount + 1; f++)
                    result.Add(PsBttn(cP++, string.Empty, string.Empty, buttonColor, "¨", textColorSilicon, 40),
                        headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} floormode 1111", string.Empty, buttonColor,
                        msg("Switch\nAll Floors"), textColorKrypton), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} floormode 6666", string.Empty, buttonColor,
                        msg("Reset\nAll Floors"), textColorIron), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} move 7777", cabinSharingUI, buttonColor,
                        msg("Settings\nPanel"), textColorSelenium), headerPanel);
                result.Add(
                    PsBttn(cP++, $"_ul.commands {elevatorID} state 0", cabinSharingUI, buttonColor, msg("Close\nMenu"),
                        textColorSelenium), headerPanel);
                result.Add(PsBttn(cP++, string.Empty, string.Empty, buttonColor, "¨", textColorSilicon, 40),
                    headerPanel);
                CuiHelper.AddUi(player, result);
            }
        }

        #endregion

        #region Helpers - ROT13

        private static string r(string i)
        {
            if (string.IsNullOrEmpty(i)) return i;
            char[] arr = new char[i.Length];
            for (int idx = 0; idx < i.Length; idx++)
            {
                char x = i[idx];
                arr[idx] = x >= 'a' && x <= 'z' ? (char)((x - 'a' + 13) % 26 + 'a') :
                    x >= 'A' && x <= 'Z' ? (char)((x - 'A' + 13) % 26 + 'A') : x;
            }
            return new string(arr);
        }

        #endregion

        #region Prevent_Building Class

        public class Prevent_Building : FacepunchBehaviour
        {
            private BoxCollider boxCollider;
            private BaseEntity block;
            private int floors;

            public void Setup(BaseEntity ent, int f)
            {
                floors = f;
                float height = floors * 3f;
                block = ent;
                transform.SetParent(block.transform, false);
                gameObject.layer = 29;
                boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.center = new Vector3(0f, height / 2f, 0f);
                boxCollider.size = new Vector3(2.9f, height, 2.9f);
            }

            public void BeforeCreation()
            {
                Ray ray = new Ray(transform.position + Vector3.up, transform.up);
                for (int l = 1; l < floors; l++)
                    if (Physics.Raycast(ray, out RaycastHit hit, 3f * floors, 2097152))
                    {
                        BaseEntity ent = hit.GetEntity();
                        if (ent) ent.Kill(BaseNetworkable.DestroyMode.None);
                    }
            }

            public static bool WayUpFree(BaseEntity ent, int fls)
            {
                RaycastHit hit;
                Ray ray;
                for (int l = 0; l < fls; l++)
                {
                    ray = new Ray(ent.transform.position + Vector3.up * (l * 3), ent.transform.up);
                    if (Physics.Raycast(ray, out hit, 3f, 2097152)) return false;
                }

                return true;
            }

            private void OnDestroy()
            {                
                Destroy(boxCollider);
            }
        }

        #endregion

        #region CabinComfort Class

        public class CabinComfort : FacepunchBehaviour
        {
            private SphereCollider sphereCollider;
            private TriggerComfort triggerComfort;
            private TriggerTemperature triggerTemperature;

            public void Setup(BaseEntity ent, int comfort = 0, int temp = 0)
            {
                sphereCollider = gameObject.AddComponent<SphereCollider>();
                sphereCollider.isTrigger = true;
                sphereCollider.radius = 1.4f;
                if (comfort > 0)
                {
                    triggerComfort = gameObject.AddComponent<TriggerComfort>();
                    triggerComfort.interestLayers.value = 131072;
                    triggerComfort.baseComfort = (float) comfort / 100f;
                    triggerComfort.triggerSize = sphereCollider.radius * gameObject.transform.localScale.y;
                    triggerComfort.minComfortRange = 1.4f;
                }

                if (temp > 0)
                {
                    triggerTemperature = gameObject.AddComponent<TriggerTemperature>();
                    triggerTemperature.interestLayers.value = 131072;
                    triggerTemperature.triggerSize = sphereCollider.radius * gameObject.transform.localScale.y;
                    triggerTemperature.Temperature = temp;
                }

                if (triggerTemperature || triggerComfort)
                {
                    gameObject.layer = 18;
                    transform.SetParent(ent.transform, false);
                }
                else
                {
                    Destroy(this);
                }
            }

            private void OnDestroy()
            {
                Destroy(sphereCollider);
                Destroy(triggerComfort);
                Destroy(triggerTemperature);
            }
        }

        #endregion

        #region Hooks - Game Events (Melee, Server/Player Command, Build, Door, Lift, Damage, Kill, Button)

        private void OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            if (info == null || info.HitEntity == null || info.HitEntity.GetComponent<CardReader>() == null) return;
            if (crossReference.TryGetValue(info.HitEntity.net.ID, out Elevator obj) && obj != null)
                if (player != null && (obj.IsOwner(player) || IsAdm(player)) && obj.IsIdle)
                    obj.controlUI.CrtSttngsUI(player);
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg.cmd.FullName == "global.entid" && arg.GetString(0, string.Empty) == "kill")
            {
                BaseEntity baseEntity = BaseNetworkable.serverEntities.Find(arg.GetEntityID(1)) as BaseEntity;
                if (baseEntity && crossReference.TryGetValue(baseEntity.net.ID, out Elevator obj) && obj != null)
                {
                    if (obj.IsGround(baseEntity) && isGroundBlockVulnerable) return null;
                    if (arg.Player() != null)
                    {
                        if (IsAdm(arg.Connection.userid.ToString()) &&
                            adminLiftEntkill[arg.Connection.userid.ToString()]) return null;
                        arg.Player().ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                                 msg("Entkill disabled for Lifts. Toggle this by: ") +
                                                 $" \'{admAccessToggleCmd} kill\'");
                    }

                    return false;
                }
            }

            return null;
        }

        private object OnPlayerCommand(ConsoleSystem.Arg arg)
        {
            string checkTp = arg.GetString(0, "");
            if (checkTp.ToLower().Contains("sethome") || checkTp.ToLower().Contains("home add"))
            {
                BasePlayer player = arg.Connection?.player as BasePlayer;
                if (player == null) return null;
                if (Physics.Raycast(player.transform.position, Vector3.down, out RaycastHit hit, player.transform.position.y,
                        2097152))
                {
                    BaseEntity ent = hit.GetEntity();
                    if (ent && crossReference.TryGetValue(ent.net.ID, out Elevator obj) && obj != null)
                    {
                        if (obj.IsGround(ent) && isGroundBlockVulnerable) return null;
                        player.ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                           msg("Couldn't add a home inside a Lift installation"));
                        return false;
                    }
                }

                return null;
            }

            return null;
        }

        private object canRemove(BasePlayer player)
        {
            bool flag1 = Physics.Raycast(player.eyes.HeadRay(), out RaycastHit RayHit, 10f, 2097409);
            BaseEntity TargetEntity = flag1 ? RayHit.GetEntity() : null;
            if (TargetEntity && crossReference.TryGetValue(TargetEntity.net.ID, out Elevator obj) && obj != null)
            {
                if (obj.IsGround(TargetEntity) && isGroundBlockVulnerable) return null;
                return string.Format(prefixFormat, prefixColor, pluginPrefix) +
                       msg("Couldn't use the RemoverTool on a Lift installation");
            }

            return null;
        }

        private object CanDeployItem(BasePlayer basePlayer, Deployer deployer, NetworkableId entityId)
        {
            if (!hijackVanillaElevatorPlacement) return null;
            if (basePlayer == null || deployer == null) return null;

            bool isElevatorItem = false;
            Item active = basePlayer.GetActiveItem();
            if (active != null && active.info != null && string.Equals(active.info.shortname ?? string.Empty, elevatorItemShortname, StringComparison.OrdinalIgnoreCase))
                isElevatorItem = true;
            if (!isElevatorItem)
            {
                var dep = deployer.GetDeployable();
                if (dep != null && dep.prefabID == VanillaElevatorPrefabId)
                    isElevatorItem = true;
            }
            if (!isElevatorItem) return null;

            // Block vanilla elevator placement and open plugin /newlift menu instead
            chatCmdTowerLift(basePlayer, createChatCommand, Array.Empty<string>());
            return false;
        }

        private object CanBuild(Planner plan, Construction prefab, Construction.Target target)
        {
            if (plan == null || prefab == null || prefab.deployable == null && !prefab.hierachyName.Contains("floor")) return null;

            if (debugBuildingBlock)
            {
                string prefabName = prefab.hierachyName ?? prefab.fullName ?? "?";
                string targetInfo = target.entity != null ? $"{target.entity.ShortPrefabName} netId={target.entity.net?.ID.Value}" : "null";
                Puts($"[UpLifted CanBuild] Called: prefab=\"{prefabName}\" targetEntity={targetInfo} pos={target.position}");
            }

            // Hijack vanilla elevator placement (visual/ghost placement): block and open /newlift menu
            if (hijackVanillaElevatorPlacement)
            {
                bool isElevator = (prefab.hierachyName != null && prefab.hierachyName.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) >= 0)
                    || (prefab.fullName != null && prefab.fullName.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) >= 0);
                if (isElevator)
                {
                    if (debugBuildingBlock) Puts("[UpLifted CanBuild] BLOCKED: elevator placement hijack (opening /newlift menu)");
                    BasePlayer p = plan.GetOwnerPlayer();
                    if (p != null)
                    {
                        chatCmdTowerLift(p, createChatCommand, Array.Empty<string>());
                    }
                    return false;
                }
            }

            Construction.Placement placement = new Construction.Placement(default);
            if (target.socket != null)
            {
                List<Socket_Base> list = Facepunch.Pool.Get<List<Socket_Base>>();
                prefab.FindMaleSockets(target, list);
                foreach (Socket_Base current in list)
                    if (!(target.entity != null) || !(target.socket != null) ||
                        !target.entity.IsOccupied(target.socket))
                    {
                        placement = current.DoPlacement(target);
                        if (placement.isPopulated) break;
                    }

                Facepunch.Pool.FreeUnmanaged(ref list);
                if (!placement.isPopulated) return null;
            }
            else
            {
                placement.position = target.position;
                placement.rotation = Quaternion.Euler(target.rotation);
                if (placement.rotation == Quaternion.identity)
                    placement.rotation = Quaternion.Euler(0, plan.GetOwnerPlayer().transform.rotation.y, 0);
            }

            OBB oBB = new OBB(placement.position, Vector3.one, placement.rotation, prefab.bounds);
            if (Physics.Raycast(oBB.position, Vector3.down, out RaycastHit hit, oBB.position.y, 2097152))
            {
                BaseEntity ent = hit.GetEntity();
                const float sameFloorMaxDistance = 3.5f;
                bool hitIsNearPlacement = hit.distance <= sameFloorMaxDistance;
                if (ent != null && ent.ShortPrefabName.Contains("foundation") && crossReference.ContainsKey(ent.net.ID) && hitIsNearPlacement)
                {
                    if (debugBuildingBlock) Puts($"[UpLifted CanBuild] BLOCKED: building on lift foundation (inside lift) ent={ent.ShortPrefabName} netId={ent.net.ID.Value} hitDist={hit.distance:F2}");
                    Effect.server.Run(StringPool.Get(3618221308), ent, 0u, Vector3.zero, Vector3.zero, null, false);
                    plan.GetOwnerPlayer().ChatMessage(string.Format(prefixFormat, prefixColor, pluginPrefix) +
                                                      msg("No build or place access inside a Lift installation"));
                    return false;
                }
                if (debugBuildingBlock && ent != null)
                    Puts($"[UpLifted CanBuild] foundation/lift check: hit ent={ent.ShortPrefabName} netId={ent.net.ID.Value} hitDist={hit.distance:F2} isLiftFoundation={ent.ShortPrefabName.Contains("foundation") && crossReference.ContainsKey(ent.net.ID)} nearPlacement={hitIsNearPlacement}");
            }
            else if (debugBuildingBlock)
                Puts("[UpLifted CanBuild] foundation/lift check: raycast down hit nothing");

            if (debugBuildingBlock) Puts("[UpLifted CanBuild] Allowed (return null)");
            return null;
        }

        private void OnDoorKnocked(Door door, BasePlayer player)
        {
            if (door != null && player != null && crossReference.TryGetValue(door.net.ID, out Elevator obj) && obj != null)
                obj.ChckDrKnck(door, player);
        }

        private void OnPlayerSleep(BasePlayer player)
        {
            if (initialized && player != null) LiftUI.DestroyAllUi(player);
        }

        private object OnLiftUse(ProceduralLift lift, BasePlayer player)
        {
            if (lift != null && player != null && crossReference.TryGetValue(lift.net.ID, out Elevator obj) && obj != null)
                return obj.ChckLftUs(lift, player);
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!initialized || entity == null || entity.net == null) return null;
            if (crossReference.TryGetValue(entity.net.ID, out Elevator obj) && obj != null)
            {
                if (obj.IsGround(entity) && isGroundBlockVulnerable) return null;
                if (obj.IsDoor(entity) && doorDestructionKillsLiftCabin) return null;

                if (info.InitiatorPlayer != null)
                    info.InitiatorPlayer.ChatMessage("You can not damage parts of a elevator");
                return true;
                /*if (info.damageTypes.Total() > entity.MaxHealth()) return false;
                NextFrame(() => entity.Heal(entity.MaxHealth()));*/
            }

            return null;
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (!initialized || entity == null || entity.net == null) return;
            if (crossReference.TryGetValue(entity.net.ID, out Elevator obj) && obj != null)
            {
                bool isDoor = obj.IsDoor((BaseEntity) entity);
                obj.EntityKill(entity.net.ID, isDoor);
            }
        }

        private void OnButtonPress(PressButton pressButton, BasePlayer player)
        {
            if (!pressButton || !player)
                return;

                        ProceduralLift proceduralLift = pressButton.GetParentEntity() as ProceduralLift;
            if (proceduralLift)
            {
                OnLiftUse(proceduralLift, player);
                }
        }

        #endregion

        #region Permission & ACL

        private string CreateAcl(bool hasParams = false, bool isAdding = true, string newOrOldGroup = "")
        {
            string result = "acl successfully changed and saved to file";
            permissionGroups = new List<string>();
            bool wasAdded = false;
            foreach (KeyValuePair<string, object> current in permissionBlock)
            {
                var dict = current.Value as Dictionary<string, object>;
                if (dict == null) continue;
                foreach (string key in dict.Keys)
                {
                    string keyLower = key.ToLower();
                    if (!permissionGroups.Contains(keyLower) && keyLower != "admin" && keyLower != "default")
                        permissionGroups.Add(keyLower);
                }
            }
            foreach (KeyValuePair<string, object> usagePerm in playerPermissionMatrix)
            {
                object[] presets = usagePerm.Value as object[];
                if (!permissionBlock.ContainsKey(usagePerm.Key))
                {
                    wasAdded = true;
                    Dictionary<string, object> permgroups = new Dictionary<string, object>
                        {{"admin", presets[0]}, {"default", presets[1]}, {"vip_ex", presets[2]},};
                    foreach (string group in permissionGroups)
                        if (!permgroups.ContainsKey(group))
                            permgroups.Add(group, presets[2]);
                    if (hasParams && isAdding && !permgroups.ContainsKey(newOrOldGroup))
                        permgroups.Add(newOrOldGroup, presets[2]);
                    permissionBlock.Add(usagePerm.Key, permgroups);
                }
                else
                {
                    bool wasFilled = false;
                    Dictionary<string, object> current = permissionBlock[usagePerm.Key] as Dictionary<string, object>;
                    if (!current.ContainsKey("admin"))
                    {
                        current.Add("admin", presets[0]);
                        wasFilled = true;
                    }

                    if (!current.ContainsKey("default"))
                    {
                        current.Add("default", presets[0]);
                        wasFilled = true;
                    }

                    if (wasFilled)
                    {
                        permissionBlock[usagePerm.Key] = current;
                        wasAdded = true;
                    }
                }
            }

            foreach (KeyValuePair<string, object> current in new List<KeyValuePair<string, object>>(permissionBlock))
            {
                if (!playerPermissionMatrix.ContainsKey(current.Key))
                {
                    permissionBlock.Remove(current.Key);
                    wasAdded = true;
                    continue;
                }

                Dictionary<string, object> groupSet = current.Value as Dictionary<string, object>;
                if (hasParams)
                {
                    if (isAdding)
                    {
                        if (newOrOldGroup is "admin" or "default") return "Aborted: Is inbuilt group";
                        if (groupSet.ContainsKey(newOrOldGroup)) return "Aborted: Already added!";
                        groupSet.Add(newOrOldGroup, (playerPermissionMatrix[current.Key] as object[])[2]);
                        result = $"Group {newOrOldGroup} was added and activated";
                    }
                    else if (!isAdding)
                    {
                        if (newOrOldGroup is "admin" or "default") return "Aborted: Is inbuilt group";
                        if (!groupSet.ContainsKey(newOrOldGroup) && !groupSet.ContainsKey(newOrOldGroup.ToLower()))
                            return "Aborted: Not found!";
                        groupSet.Remove(newOrOldGroup);
                        groupSet.Remove(newOrOldGroup.ToLower());
                        result = $"Group {newOrOldGroup} was removed";
                    }

                    permissionBlock[current.Key] = groupSet;
                    wasAdded = true;
                }

                foreach (string key in groupSet.Keys)
                    if (!permissionGroups.Contains(key) && key != "admin" && key != "default")
                        permissionGroups.Add(key);
            }

            if (wasAdded)
            {
                Config["Permission", "AccessControl"] = permissionBlock;
                SaveConfig();
                return result;
            }

            return string.Empty;
        }

        private void CreateOverview()
        {
            if (aclVars != null && aclVars.Count > 0)
                foreach (ConsoleSystem.Command cmd in new List<ConsoleSystem.Command>(aclVars))
                    ConsoleSystem.Index.Server.Dict.Remove(cmd.FullName?.ToLower());
            aclVars = new List<ConsoleSystem.Command>();
            string parent = "uplifted.acl.";
            foreach (KeyValuePair<string, object> current in new List<KeyValuePair<string, object>>(permissionBlock))
            foreach (KeyValuePair<string, object> groupSet in current.Value as Dictionary<string, object>)
            {
                ConsoleSystem.Command newCmd = new ConsoleSystem.Command
                {
                    Name = current.Key.ToLower(), Parent = parent + groupSet.Key,
                    FullName = parent + groupSet.Key + "." + current.Key.ToLower(), ServerAdmin = true, Variable = true,
                    Description = "| " + (string) (playerPermissionMatrix[current.Key] as object[])[3],
                    GetOveride = () =>
                        (permissionBlock[current.Key] as Dictionary<string, object>)[groupSet.Key].ToString(),
                    SetOveride = delegate(string str)
                    {
                        bool changed = false;
                        object value = (permissionBlock[current.Key] as Dictionary<string, object>)[groupSet.Key];
                        TypeCode typeCode = Convert.GetTypeCode(value);
                        switch (typeCode)
                        {
                            case TypeCode.Boolean:
                                if (value.ToString().ToBool() != str.ToBool())
                                {
                                    (permissionBlock[current.Key] as Dictionary<string, object>)[groupSet.Key] =
                                        str.ToBool();
                                    changed = true;
                                }

                                break;
                            case TypeCode.Single:
                            case TypeCode.Double:
                                if (value.ToString().ToFloat() != str.ToFloat())
                                {
                                    (permissionBlock[current.Key] as Dictionary<string, object>)[groupSet.Key] =
                                        str.ToFloat();
                                    changed = true;
                                }

                                break;
                            case TypeCode.Int32:
                            case TypeCode.UInt32:
                            case TypeCode.UInt64:
                                if (value.ToString().ToInt() != str.ToInt())
                                {
                                    (permissionBlock[current.Key] as Dictionary<string, object>)[groupSet.Key] =
                                        str.ToInt();
                                    changed = true;
                                }

                                break;
                            default: break;
                        }

                        if (changed)
                        {
                            Config["Permission", "AccessControl"] = permissionBlock;
                            SaveConfig();
                            CreateAcl();
                        }
                    }
                };
                aclVars.Add(newCmd);
            }

            foreach (ConsoleSystem.Command aclVar in aclVars)
                ConsoleSystem.Index.Server.Dict[aclVar.FullName.ToLower()] = aclVar;
            ConsoleIndexCompat.RebuildAllFromServerDict();
        }

        private static string GetGroup(string userid, bool isActive)
        {
            if (!isActive && UpL.IsAdmPassive(userid))
                return "admin";

            if (adminAccessEnabled.TryGetValue(userid, out bool value))
            {
                if (isActive && value || !isActive)
                    return "admin";
            }

            string retGroup = "default";
            int lastRank = -1;
            foreach (string group in UpL.permissionGroups)
            {
                if (UpL.permission.UserHasGroup(userid, group))
                {
                    int rank = UpL.permission.GetGroupRank(group);
                    if (rank > lastRank)
                    {
                        retGroup = group;
                        lastRank = rank;
                    }                    
                }
            }

            return retGroup;
        }

        private static object GetAccess(string variable, string userid, object fallbackValue = default(object), bool isActive = true)
        {
            if (UpL.permissionBlock.TryGetValue(variable, out object usagePerm))
            {
                string grouptype = GetGroup(userid, isActive);
                Dictionary<string, object> perms = usagePerm as Dictionary<string, object>;
                if (perms.TryGetValue(grouptype, out object value))
                    return value;
            }

            return fallbackValue;
        }

        private bool IsAdmPassive(string id)
        {
            if (admAccessAuthLevel == 2 && ServerUsers.Is(Convert.ToUInt64(id), ServerUsers.UserGroup.Owner))
                return true;

            if (admAccessAuthLevel == 1 &&
                ServerUsers.Is(Convert.ToUInt64(id), ServerUsers.UserGroup.Moderator))
                return true;

            if (IsPseudoAdmin(id))
                return true;

            if (permission.UserHasPermission(id, admAccessPermission))
                return true;

            return false;
        }

        private static bool IsAdm(object p)
        {
            bool value = false;
            if (p is BasePlayer && adminAccessEnabled.TryGetValue((p as BasePlayer).UserIDString, out value))
                return value;
            else if (p is ulong && adminAccessEnabled.TryGetValue((string) p, out value)) return value;
            else if (p is string && adminAccessEnabled.TryGetValue((string) p, out value)) return value;
            return value;
        }

        private bool IsPseudoAdmin(string id)
        {
            foreach (string perm in pseudoPerms)
                if (permission.UserHasPermission(id, perm))
                    return true;
            return false;
        }

        private void OnUserPermissionGranted(string id, string perm)
        {
            if (pseudoPerms.Contains(perm.ToLower()))
            {
                adminAccessEnabled[id] = admAccessSwitchable ? admSwitchEnabledAtLogin : true;
                adminLiftEntkill[id] = false;
            }
        }

        [ConsoleCommand("upl.reloadacl")]
        private void CmdAclReload(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            LoadConfig();
            permissionBlock =
                (Dictionary<string, object>) GetConfig("Permission", "AccessControl", new Dictionary<string, object>());
            CreateAcl();
            CreateOverview();
            SendReply(arg,
                $"Reloaded acl with '{permissionBlock.Count}' switches across '{permissionGroups.Count + 2}' groups");
        }

        [ConsoleCommand("upl.addgroup")]
        private void CmdAclGroupAdd(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (!arg.HasArgs(1))
            {
                SendReply(arg, "Provide a goupname");
                return;
            }

            string newOrOld = arg.GetString(0, "");
            if (newOrOld.Length < 3)
            {
                SendReply(arg, "Group needs to have at least 3 characters");
                return;
            }

            LoadConfig();
            permissionBlock =
                (Dictionary<string, object>) GetConfig("Permission", "AccessControl", new Dictionary<string, object>());
            string result = CreateAcl(true, true, newOrOld.ToLower());
            if (result.StartsWith("Aborted"))
            {
                SendWarning(arg, result);
            }
            else
            {
                CreateOverview();
                SendReply(arg, result);
            }
        }

        [ConsoleCommand("upl.delgroup")]
        private void CmdAclGroupDel(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (!arg.HasArgs(1))
            {
                SendReply(arg, "Provide a goupname");
                return;
            }

            string newOrOld = arg.GetString(0, "").ToLower();
            if (newOrOld.Length < 3)
            {
                SendReply(arg, "Group needs to have at least 3 characters");
                return;
            }

            LoadConfig();
            permissionBlock =
                (Dictionary<string, object>) GetConfig("Permission", "AccessControl", new Dictionary<string, object>());
            string result = CreateAcl(true, false, newOrOld);
            if (result.StartsWith("Aborted"))
            {
                SendWarning(arg, result);
            }
            else
            {
                CreateOverview();
                SendReply(arg, result);
            }
        }

        [ConsoleCommand("upl.clonegroup")]
        private void CmdCloneGoup(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (!arg.HasArgs(2))
            {
                SendReply(arg, "Provide a source and destination goupname");
                return;
            }

            string fromGroup = arg.GetString(0, "").ToLower();
            string toGroup = arg.GetString(1, "").ToLower();
            if (fromGroup.Length < 3 || toGroup.Length < 3)
            {
                SendReply(arg, "Group needs to have at least 3 characters (from to)");
                return;
            }

            if (toGroup is "admin" or "default")
            {
                SendReply(arg, "You cannot clone into a standard-group");
                return;
            }

            if (fromGroup != "admin" && fromGroup != "default" &&
                (!permissionGroups.Contains(toGroup) || !permissionGroups.Contains(fromGroup)))
            {
                SendReply(arg, "At least one group does not exist in the acl");
                return;
            }

            foreach (KeyValuePair<string, object> current in new List<KeyValuePair<string, object>>(permissionBlock))
            {
                Dictionary<string, object> groupSet = current.Value as Dictionary<string, object>;
                (permissionBlock[current.Key] as Dictionary<string, object>)[toGroup] = groupSet[fromGroup];
            }

            Config["Permission", "AccessControl"] = permissionBlock;
            SaveConfig();
            CreateAcl();
            SendReply(arg, "Group successfully cloned");
        }

        [ConsoleCommand("upl.resetgroup")]
        private void CmdResetGoup(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (!arg.HasArgs(1))
            {
                SendReply(arg, "Provide a goupname");
                return;
            }

            string fromGroup = arg.GetString(0, "").ToLower();
            if (fromGroup.Length < 3)
            {
                SendReply(arg, "Group needs to have at least 3 characters");
                return;
            }

            int toGroup;
            if (fromGroup == "admin") toGroup = 0;
            else if (fromGroup == "default") toGroup = 2;
            else toGroup = 1;
            foreach (KeyValuePair<string, object> current in new List<KeyValuePair<string, object>>(permissionBlock))
            {
                object[] groupSet = playerPermissionMatrix[current.Key] as object[];
                (permissionBlock[current.Key] as Dictionary<string, object>)[fromGroup] = groupSet[toGroup];
            }

            Config["Permission", "AccessControl"] = permissionBlock;
            SaveConfig();
            CreateAcl();
            SendReply(arg, "Group was successfully reset to default");
        }

        [ConsoleCommand("upl.getgroupforuser")]
        private void CmdGetGroupForUser(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            if (!arg.HasArgs(1))
            {
                SendReply(arg, "Provide a user-id or username");
                return;
            }

            string userString = arg.GetString(0, "").ToLower();
            if (!permission.UserExists(userString))
            {
                IPlayer p = covalence.Players.FindPlayerById(userString);
                if (p != null)
                {
                    if (!permission.UserExists(p.Id))
                    {
                        SendReply(arg, "No userdata found under the provided info");
                        return;
                    }

                    string group1 = GetGroup(p.Id, false);
                    string group2 = GetGroup(p.Id, true);
                    if (group1 != group2)
                        SendReply(arg, $"Group-result for '{p.Name}': {group1}(passive) | {group2}(active)");
                    else SendReply(arg, $"Group-result for '{p.Name}': {group2}");
                    return;
                }

                IPlayer pl = covalence.Players.FindPlayer(userString);
                if (pl != null)
                {
                    if (!permission.UserExists(pl.Id))
                    {
                        SendReply(arg, "No userdata found under the provided info");
                        return;
                    }

                    string group1 = GetGroup(pl.Id, false);
                    string group2 = GetGroup(pl.Id, true);
                    if (group1 != group2)
                        SendReply(arg, $"Group-result for '{pl.Name}': {group1}(passive) | {group2}(active)");
                    else SendReply(arg, $"Group-result for '{pl.Name}': {group2}");
                    return;
                }

                SendReply(arg, "No userdata found under the provided info");
                return;
            } 
        }
 
        #endregion       
    }    
}        