using System.Collections.Generic;

namespace TCUpgrade;

public static class LangHelper
{
	private static readonly Dictionary<string, string> Messages = new Dictionary<string, string>
	{
		["title1"] = "BUILDING AUTO UPGRADE",
		["title2"] = "COLOR SELECTION",
		["title3"] = "AUTHORIZED PLAYERS",
		["title4"] = "SELECT SKIN FOR TC",
		["title5"] = "SELECT WALLPAPER FOR GRADE CONSTRUCTION",
		["BoatWallpaperTitle"] = "BOAT WALLPAPER SELECTION",
		["CLOSE"] = "CLOSE",
		["STOP"] = "STOP",
		["UPGRADE"] = "UPGRADE",
		["ListAuth"] = "LIST AUTH",
		["AUTH"] = "AUTH",
		["Repair"] = "REPAIR",
		["Repairing"] = "REPAIRING",
		["Upgrade"] = "UPGRADE",
		["Upgrading"] = "UPGRADING",
		["Reskin"] = "RESKIN",
		["CheckUpdate"] = "CHECK UPDATE",
		["RaidBlocked"] = "You cannot do this while you have Raid Block.",
		["ErrorTC2"] = "Oops, something went wrong, open the TC again while standing on a building block.",
		["UpgradeFinish"] = "The improvement process is complete.",
		["UpgradeFinishNoPlayer"] = "Upgrade completed on your buildings. No players have been detected in your team.",
		["RepairFinish"] = "The repair process is complete.",
		["ReskinFinish"] = "The reskin process is complete.",
		["ReskinWallFinish"] = "The External Wall reskin process is complete.",
		["NoResourcesRepair"] = "Repair stopped due to lack of resources.",
		["NoResourcesUpgrade"] = "Improvements stopped due to lack of resources.",
		["NoResourcesReskin"] = "Reskin stopped due to lack of resources.",
		["NoResourcesWallpaper"] = "Wallpaper placement was stopped due to lack of fabric in the TC.",
		["NoResourcesWallpaperBoat"] = "Wallpaper placement was stopped due to lack of cloth in your inventory.",
		["UpgradeBlock"] = "Upgrading to this level is currently locked.",
		["UpgradeLock"] = "You do not have permissions to improve the selected option.",
		["LOCK"] = "LOCK",
		["EffectON"] = "EFFECT ON",
		["EffectOFF"] = "EFFECT OFF",
		["DowngradeON"] = "DOWNGRADE ON",
		["DowngradeOFF"] = "DOWNGRADE OFF",
		["TCSkin"] = "TC SKIN",
		["WALLPAPER"] = "WALLPAPER",
		["WALLPAPERGRADE"] = "PLACE GRADE",
		["WALLPAPERALL"] = "PLACE ALL",
		["WallpaperFinish"] = "Wallpaper placement is complete.",
		["WallpaperFinishNoPlayer"] = "Wallpapering completed on your buildings. No players detected in your team.",
		["TotalCostUP"] = "Total cost for upgrade: {0}",
		["NoUpgradeAvailable"] = "There is nothing to improve, the cost is 0.",
		["WALL"] = "WALL",
		["FLOOR"] = "FLOOR",
		["CEILING"] = "CEILING",
		["RepairBlockedRecentDamage"] = "Could not repair: {0} due to recent damage. Try again in {1} seconds.",
		["NoDLCPurchased"] = "You don't have this DLC purchased.",
		["DisableBarges"] = "Not available for Barges",
		["AddWP_NoPermission"] = "You don't have permission to use this command.",
		["AddWP_Usage"] = "Usage: /addwp <skinid> <Wall|Floor|Ceiling>",
		["AddWP_InvalidCategory"] = "Invalid category. Use: Wall, Floor, or Ceiling.",
		["AddWP_Added"] = "Wallpaper SkinID: {0} added to category: {1}.",
		["AddWP_AlreadyExists"] = "That skin is already registered.",
		["AutoCodeLockAdded"] = "Auto Code Lock added. Code: {0}",
		["AutoLockAdded"] = "Auto Key Lock added.",
		["InternalON"] = "Internal ON",
		["InternalOFF"] = "Internal OFF",
		["ExternalON"] = "External ON",
		["ExternalOFF"] = "External OFF",
		["Back"] = "Back",
		["Next"] = "Next",
		["REMOVE"] = "REMOVE",
		["NoAuthPlayers"] = "No authorized players",
		["NoTCSkin"] = "You don't own this TC skin.",
		["NODLC"] = "NO DLC",
		["Online"] = "[Online]",
		["Offline"] = "[Offline]",
		["NotOnBoat"] = "You must be mounted on a boat steering wheel to use this command."
	};

	public static string Lang(string key, params object[] args)
	{
		if (!Messages.TryGetValue(key, out var value))
		{
			return key;
		}
		if (args == null || args.Length == 0)
		{
			return value;
		}
		try
		{
			return string.Format(value, args);
		}
		catch
		{
			return value;
		}
	}
}
