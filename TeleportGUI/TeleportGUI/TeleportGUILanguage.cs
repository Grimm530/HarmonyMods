using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>
    /// Oxide-free localization bridge. The parent mod owns lifecycle wiring through
    /// Initialize/Shutdown and can retrieve safely formatted messages through Get.
    /// </summary>
    public static class TeleportGUILanguage
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> English = CreateEnglishMessages();
        private static Dictionary<string, string> _overrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;

        public static void Initialize(string serverRoot = null)
        {
            lock (Sync)
            {
                _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string root = string.IsNullOrWhiteSpace(serverRoot)
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                    : Path.GetFullPath(serverRoot);
                string path = Path.Combine(root, "HarmonyLanguage", "TeleportGUI.json");

                try
                {
                    if (File.Exists(path))
                    {
                        var messages = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                        if (messages != null)
                        {
                            foreach (var entry in messages)
                            {
                                if (!string.IsNullOrEmpty(entry.Key) && entry.Value != null)
                                    _overrides[entry.Key] = entry.Value;
                            }
                        }

                        Debug.Log("[TeleportGUI] Loaded " + _overrides.Count +
                                  " language strings from HarmonyLanguage/TeleportGUI.json");
                    }
                    else
                    {
                        Debug.LogWarning("[TeleportGUI] HarmonyLanguage/TeleportGUI.json missing; using embedded English.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[TeleportGUI] HarmonyLanguage load failed; using embedded English: " + ex.Message);
                    _overrides.Clear();
                }

                _initialized = true;
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                _overrides.Clear();
                _initialized = false;
            }
        }

        public static string Get(string key, BasePlayer player, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            EnsureInitialized();

            string message;
            lock (Sync)
            {
                if (!_overrides.TryGetValue(key, out message) &&
                    !English.TryGetValue(key, out message))
                {
                    message = key;
                }
            }

            if (args == null || args.Length == 0)
                return message;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, message, args);
            }
            catch (FormatException ex)
            {
                Debug.LogWarning("[TeleportGUI] Invalid language format for key '" + key + "': " + ex.Message);
                return message;
            }
        }

        private static void EnsureInitialized()
        {
            lock (Sync)
            {
                if (_initialized)
                    return;
            }

            Initialize();
        }

        private static Dictionary<string, string> CreateEnglishMessages()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Purchase.Error.SR"] = "You do not have enough RP to purchase this teleport ({0} RP)",
                ["Purchase.Error.Economics"] = "You do not have enough coins to purchase this teleport ({0} coins)",
                ["Purchase.Error.Scrap"] = "You do not have enough scrap to purchase this teleport ({0} scrap)",
                ["Purchase.Success.SR"] = "You have purchased this teleport for {0} RP",
                ["Purchase.Success.Economics"] = "You have purchased this teleport for {0} coins",
                ["Purchase.Success.Scrap"] = "You have purchased this teleport for {0} scrap",
                ["Purchase.Refund.SR"] = "You were refunded {0} RP",
                ["Purchase.Refund.Economics"] = "You were refunded {0} coins",
                ["Purchase.Refund.Scrap"] = "You were refunded {0} scrap",
                ["TPRequest.Here.Sent"] = "You sent a teleport here request to {0}",
                ["TPRequest.Here.Received"] = "You have received a teleport here request from {0}",
                ["TPRequest.Sent"] = "You sent a teleport request to {0}",
                ["TPRequest.Received"] = "You have received a teleport request from {0}",
                ["TPRequest.Instant.Sent"] = "Your teleport request to {0} was accepted automatically. Teleporting in {1} seconds",
                ["TPRequest.Instant.Received"] = "The teleport request from {0} was accepted as you have auto-accept enabled. Teleporting in {1} seconds.",
                ["TPRequest.To.Accepted"] = "Your teleport request to {0} was accepted. Teleporting in {1} seconds.",
                ["TPRequest.From.Accepted"] = "Teleport request from {0} accepted. Teleporting in {1} seconds.",
                ["TPRequest.Here.To.Denied"] = "Your teleport here request to {0} was denied",
                ["TPRequest.Here.From.Denied"] = "Teleport here request from {0} denied",
                ["TPRequest.To.Denied"] = "Your teleport request to {0} was denied",
                ["TPRequest.From.Denied"] = "Teleport request from {0} denied",
                ["TPRequest.To.Cancelled"] = "Teleport request to {0} cancelled",
                ["TPRequest.From.Cancelled"] = "Teleport request from {0} cancelled",
                ["TPRequest.Here.To.TimeOut"] = "Your teleport here request to {0} timed out",
                ["TPRequest.Here.From.TimeOut"] = "The teleport here request from {0} timed out",
                ["TPRequest.To.TimeOut"] = "Your teleport request to {0} timed out",
                ["TPRequest.From.TimeOut"] = "The teleport request from {0} timed out",
                ["TPR.Admin.YouWereTPTo"] = "You were teleported to {0}",
                ["TPR.Admin.TPTo"] = "{0} teleported to you",
                ["TPR.Admin.YouTPToYou"] = "You teleported {0} to your position",
                ["TPR.YouTPTo"] = "You teleported to {0}",
                ["TPR.InvalidSyntax"] = "Invalid TPR syntax! /tpr {player name}",
                ["TPR.Here.InvalidSyntax"] = "Invalid TPR syntax! /tprhere {player name}",
                ["TP.TargetPendingRequest"] = "{0} has a pending teleport request",
                ["TP.SelfHasPendingRequest"] = "You have a pending teleport request",
                ["TP.To.Cancelled"] = "Teleport to {0} was cancelled",
                ["TP.From.Cancelled"] = "Teleport from {0} was cancelled",
                ["TP.To.Cancelled.Reason"] = "Teleport to {0} was cancelled : {1}",
                ["TP.From.Cancelled.Reason"] = "Teleport from {0} was cancelled : {1}",
                ["TP.Error.TargetDead"] = "Teleport cancelled. The target player has died",
                ["TP.Error.NoPending"] = "You have no teleports to cancel",
                ["TP.Error.PositionSyntax"] = "Invalid position syntax! /tp {x} {y} {z}",
                ["TP.Success.Position"] = "You teleported to {0} {1} {2}",
                ["TPB.NoBackLocation"] = "You do not have a location to TP back to",
                ["TPB.Success"] = "You teleported back to your previous location",
                ["TPGrid.Error.Syntax"] = "Invalid grid reference, should look like 'A1'",
                ["TPGrid.Success"] = "You teleported to grid location {0}",
                ["TPDeath.Error.NoLocation"] = "You do not have a death location stored",
                ["TPDeath.Success"] = "You teleported to where you last died",
                ["TPMarker.Error.NoMarkers"] = "You do not have any markers on the map",
                ["TPMarker.Error.InvalidID"] = "The marker index you specified is out of range",
                ["TPMarker.Error.InvalidLabel"] = "There is no marker with the label you specified",
                ["TPMarker.Error.InvalidSyntax"] = "Invalid arguments;\n/tpm - Teleports to the last marker placed\n/tpm <index> - Teleports to the marker with the specified index\n/tpm <name> - Teleports to the marker with the specified label",
                ["TPMarker.Success"] = "You teleported to marker {0}",
                ["TPB.Admin.NoBackLocation"] = "{0} does not have a location to TP back to",
                ["TPB.Admin.Success"] = "You teleported {0} back to their previous location",
                ["TPB.Admin.Success.Target"] = "You were teleported back to your previous location by an admin",
                ["TPA.NonePending"] = "You do not have any pending teleport requests",
                ["TP.CantTeleportSelf"] = "You can not teleport to yourself",
                ["TPL.NoNameSpecified"] = "You must specify a location name",
                ["TPL.Success"] = "You have saved your position as teleport location {0}",
                ["TPL.Error.NoLocations"] = "You do not have any saved locations",
                ["TPL.Error.NoLocation.Name"] = "You do not have a saved location with the name {0}",
                ["TPL.List"] = "Your saved locations: {0}",
                ["Home.SelfHasPendingRequest"] = "You have a pending home teleport",
                ["Home.Error.IsBuildingBlocked"] = "You can not set home in a building blocked area",
                ["Home.Error.NoBuildingPrivilege"] = "You must be authed on a tool cupboard to set a home",
                ["Home.Error.OnTugboat"] = "You can not set home on a tugboat",
                ["Home.Error.LimitReached"] = "You already have the maximum number of homes allowed",
                ["Home.Error.NearbyRadius"] = "You already have a home set {0}m away. The minimum distance for homes is {1}m",
                ["Home.Error.NotOnFoundation"] = "Homes can only be set on foundations",
                ["Home.Error.NotOnFoundationFloor"] = "Homes can only be set on foundations and floors",
                ["Home.Error.NoTPZone"] = "Homes can not be set in NoTP zones",
                ["Home.Error.NoBackLocation"] = "You do not have a location to TP back to",
                ["Home.Error.NoPending"] = "You have no pending home teleports to cancel",
                ["Home.Error.Invalid"] = "The home {0} has been destroyed as the block it was placed on has been destroyed",
                ["Home.Error.NoEntity"] = "The home {0} has been destroyed",
                ["Home.Error.BagPublic"] = "The home {0} can not be used as it is a public bag/bed",
                ["Home.Error.BagNotAssigned"] = "The home {0} can not be used as it is not assigned to you",
                ["Home.Error.Blocked"] = "The home {0} is currently blocked by a deployed item",
                ["Home.Error.DoesntExist"] = "The home {0} doesnt exist",
                ["Home.Error.NoHomes"] = "You have not created any homes",
                ["Home.Error.AlreadyExists"] = "A home with the name {0} already exists",
                ["Home.Error.BagDisableCommand"] = "The /sethome is disabled on this server. Homes are created on bags/beds",
                ["Home.Error.SetHomeSyntax"] = "Invalid sethome syntax! /sethome {homename}",
                ["Home.Error.DelHomeSyntax"] = "Invalid delhome syntax! /delhome {homename}",
                ["Home.Error.NoHomes.Target"] = "{0} has not created any homes",
                ["Home.Error.DoesntExist.Target"] = "{0} does not have a home with the name {1}",
                ["Home.Cancelled"] = "You have cancelled your pending home teleport",
                ["Home.List"] = "Your homes: {0}",
                ["Home.List.Target"] = "Homes for {0}: {1}",
                ["Home.Success.Created"] = "You have created a new home with the name {0}",
                ["Home.Success.Created.Bed"] = "A home point has been created on this bag/bed with the name {0}",
                ["Home.Success.Created.Remaining"] = "You have created a new home with the name {0} ({1} homes remaining)",
                ["Home.Success.Created.Bed.Remaining"] = "A home point has been created on this bag/bed with the name {0}  ({1} homes remaining)",
                ["Home.Success.Deleted"] = "You have deleted the home {0}",
                ["Notification.BedHomeDestroyed"] = "Your home {0} was destroyed",
                ["Warp.SelfHasPendingRequest"] = "You have a pending warp teleport",
                ["Warp.Error.DoesntExist"] = "The warp {0} doesnt exist",
                ["Warp.Error.NoPermission"] = "You do not have permission to use this warp point",
                ["Warp.Error.NoBackLocation"] = "You do not have a location to TP back to",
                ["WarpTo.Error.Syntax"] = "Invalid warp syntax! /warp {to/list} {warpname}",
                ["WarpTo.List"] = "Available warps: {0}",
                ["WarpAdd.Error.Syntax2"] = "Invalid warp add syntax! /warpadd {warpname} {opt:permission} {opt:command}",
                ["WarpAdd.Error.Exists"] = "A warp with the name {0} already exists",
                ["WarpAdd.Success"] = "You have created a warp point on your position with the name {0}",
                ["WarpAdd.Success.WithPermission"] = ", and permission {0}",
                ["WarpAdd.Success.WithCommand"] = ", and command /{0}",
                ["WarpRemove.Error.Syntax"] = "Invalid warp remove syntax! /warpremove {warpname}",
                ["WarpRemove.Success"] = "You have removed the warp point {0}",
                ["TP.CooldownFormat"] = "Your teleport is on cooldown for another {0}",
                ["Home.CooldownFormat"] = "Your home teleport is on cooldown for another {0}",
                ["Warp.CooldownFormat"] = "Your warp teleport is on cooldown for another {0}",
                ["TP.MaxTeleportsReached"] = "You have reached your max teleports for today.",
                ["Home.MaxTeleportsReached"] = "You have reached your max home teleports for today.",
                ["Warp.MaxTeleportsReached"] = "You have reached your max warp teleports for today.",
                ["General.InsideFoundation"] = "The target position is inside a foundation",
                ["General.NoPermission"] = "You do not have permission to use this command",
                ["General.PlayerNotFound"] = "Unable to find a player with the name {0}",
                ["General.MultiplePlayersFound"] = "Multiple players found with the name {0}\n{1}",
                ["Notification.TP.Remaining"] = "{0} teleports remaining today.",
                ["Notification.Home.Remaining"] = "{0} home teleports remaining today.",
                ["Notification.Warp.Remaining"] = "{0} warp teleports remaining today.",
                ["UI.Title"] = "Teleport GUI",
                ["UI.Teleport"] = "Teleport",
                ["UI.Homes"] = "Homes",
                ["UI.Warps"] = "Warps",
                ["UI.CostToTP"] = "Cost to TP: {0} {1}",
                ["UI.TPLimit"] = "You have used all your teleports for today",
                ["UI.DailyLimitRemain"] = "Daily Limit Remaining: {0}",
                ["UI.CooldownRemain"] = "Cooldown Remaining : %TIME_LEFT%s",
                ["UI.NoPlayers"] = "No players available at the moment",
                ["UI.NoHomes"] = "You have not created any homes",
                ["UI.NoWarps"] = "No warp points have been created",
                ["UI.TPR"] = "TPR",
                ["UI.TPHere"] = "HERE",
                ["UI.HomeName"] = "Home Name",
                ["UI.WarpName"] = "Warp Name",
                ["UI.Permission"] = "Permission",
                ["UI.Command"] = "Command",
                ["UI.CreateNewWarp"] = "Create Warp Point",
                ["UI.CreateNewHome"] = "Create New Home",
                ["UI.Save"] = "Save",
                ["UI.Cancel"] = "Cancel",
                ["UI.Close"] = "Close",
                ["UI.Unlimited2"] = "~",
                ["UI.AA.Clan"] = "Auto accept clan",
                ["UI.AA.Team"] = "Auto accept team",
                ["UI.AA.Friend"] = "Auto accept friends",
                ["UI.AA.All"] = "Auto accept all",
                ["UI.ShowSleepers"] = "Show sleepers",
                ["UI.TeleportSettings"] = "Teleport Settings",
                ["PurchaseMode.Scrap"] = "Scrap",
                ["PurchaseMode.ServerRewards"] = "RP",
                ["PurchaseMode.Economics"] = "coins",
                ["Popup.Incoming.TPHere"] = "INCOMING TPHERE (%TIME_LEFT%s)",
                ["Popup.Incoming.TPR"] = "INCOMING TPR (%TIME_LEFT%s)",
                ["Popup.Outgoing.TPHere"] = "OUTGOING TPHERE (%TIME_LEFT%s)",
                ["Popup.Outgoing.TPR"] = "OUTGOING TPR (%TIME_LEFT%s)",
                ["Popup.Incoming.TP"] = "INCOMING TP IN %TIME_LEFT%s",
                ["Popup.Outgoing.TP"] = "TELEPORTING IN %TIME_LEFT%s",
                ["Popup.Outgoing.TP.Home"] = "TELEPORTING IN %TIME_LEFT%s",
                ["Popup.Outgoing.TP.Warp"] = "TELEPORTING IN %TIME_LEFT%s",
                ["Condition.Wounded"] = "You can not teleport whilst wounded",
                ["Condition.Bleeding.Self"] = "You can not teleport whilst bleeding",
                ["Condition.Bleeding.Target"] = "You can not teleport when the target is bleeding",
                ["Condition.Mounted.Self"] = "You can not teleport whilst mounted",
                ["Condition.Mounted.Target"] = "You can not teleport when the target is mounted",
                ["Condition.InWater.Self"] = "You can not teleport whilst in water",
                ["Condition.InWater.Target"] = "You can not teleport when the target is in water",
                ["Condition.OnWater.Self"] = "You can not teleport whilst on water",
                ["Condition.OnWater.Target"] = "You can not teleport when the target is on water",
                ["Condition.NoTPZone.Self"] = "You can not teleport whilst in a NoTP zone",
                ["Condition.NoTPZone.Target"] = "You can not teleport when the target is in a NoTP zone",
                ["Condition.SafeZone.Self"] = "You can not teleport whilst in a safe zone",
                ["Condition.SafeZone.Target"] = "You can not teleport when the target is in a safe zone",
                ["Condition.Hostile.Self"] = "You can not teleport whilst deemed hostile",
                ["Condition.Hostile.Target"] = "You can not teleport when the target is deemed hostile",
                ["Condition.Crafting.Self"] = "You can not teleport whilst crafting",
                ["Condition.Crafting.Target"] = "You can not teleport when the target is crafting",
                ["Condition.BuildingBlocked.Self"] = "You can not teleport whilst building blocked",
                ["Condition.BuildingBlocked.Target"] = "You can not teleport when the target is building blocked",
                ["Condition.BuildingBlocked.Position"] = "You can not teleport as you are building blocked in the desired location",
                ["Condition.RaidBlocked.Self"] = "You can not teleport whilst raid/combat blocked",
                ["Condition.RaidBlocked.Target"] = "You can not teleport when the target is raid/combat blocked",
                ["Condition.CargoShip.Self"] = "You can not teleport whilst on the cargo ship",
                ["Condition.CargoShip.Target"] = "You can not teleport when the target is on the cargo ship",
                ["Condition.HAB.Self"] = "You can not teleport whilst in a hot air balloon",
                ["Condition.HAB.Target"] = "You can not teleport when the target is in a hot air balloon",
                ["Condition.TugBoat.Self"] = "You can not teleport whilst on a tug boat",
                ["Condition.TugBoat.Target"] = "You can not teleport when the target is on a tug boat",
                ["Condition.OilRig.Self"] = "You can not teleport whilst you are near the oil rig",
                ["Condition.OilRig.Target"] = "You can not teleport when the target is near the oil rig",
                ["Condition.OilRig.Position"] = "You can not teleport as the desired location is too close to the oil rig",
                ["Condition.UnderwaterLabs.Self"] = "You can not teleport whilst you are in underwater labs",
                ["Condition.UnderwaterLabs.Target"] = "You can not teleport when the target is in underwater labs",
                ["Condition.UnderwaterLabs.Position"] = "You can not teleport as the desired location is too close to underwater labs",
                ["Condition.TrainTunnels.Self"] = "You can not teleport whilst you are in the train tunnels",
                ["Condition.TrainTunnels.Target"] = "You can not teleport when the target is the train tunnels",
                ["Condition.TrainTunnels.Position"] = "You can not teleport as the desired location is too close to the train tunnels",
                ["Condition.Monument.Self"] = "You can not teleport whilst you are in a monument",
                ["Condition.Monument.Target"] = "You can not teleport when the target is in a monument",
                ["Condition.Monument.Position"] = "You can not teleport as the desired location is too close to a monument",
                ["Condition.Topology.Self"] = "You can not teleport from your current position",
                ["Condition.Topology.Target"] = "You can not teleport to the targets current position",
                ["Condition.Topology.Position"] = "You can not teleport to the target position",
                ["Reason.Disconnected"] = "Disconnected",
                ["Reason.EntityDestroyed"] = "Location Destroyed",
                ["Reason.TookDamage"] = "Hurt",
                ["Reason.Death"] = "Died",
                ["Reason.Declined"] = "Declined",
                ["Help.Warp.1"] = "/warp to <warpname> - Teleport to the specified warp point",
                ["Help.Warp.2"] = "/warp list - Show available warp points",
                ["Help.Warp.5"] = "/warpadd <warpname> <opt:permission> <opt:command> - Add a new warp point on your current position",
                ["Help.Warp.4"] = "/warpremove <warpname> - Delete the specified warp point",
                ["Help.Homes.1"] = "/home <homename> - Teleport to the specified home",
                ["Help.Homes.2"] = "/sethome <homename> - Create a home on your current position",
                ["Help.Homes.3"] = "/delhome <homename> - Delete the home with the specified name",
                ["Help.Homes.4"] = "/listhomes - List all of your home names",
                ["Help.Homes.5"] = "/homec - Cancel a pending home teleport",
                ["Help.TP.1"] = "/tpsave <name> - Save your current position as a TP location",
                ["Help.TP.2"] = "/tpl <name> - Teleport to the specified TP location",
                ["Help.TP.3"] = "/tplist - List all of your TP locations",
                ["Help.TP.4"] = "/tpr <playername> - Request a teleport to the specified player",
                ["Help.TP.5"] = "/tpa - Accept an incoming TP request",
                ["Help.TP.6"] = "/tpd - Decline an incoming TP request",
                ["Help.TP.7"] = "/tpc - Cancel a pending TP request",
                ["Help.TP.8"] = "/tpb - Teleport back to your previous location",
                ["Help.TP.9"] = "/tprhere <playername> - Request a teleport that brings the specified player to you"
            };
        }
    }
}
