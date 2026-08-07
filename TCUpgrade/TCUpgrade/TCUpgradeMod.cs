using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Facepunch;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TCUpgrade;

public class TCUpgradeMod : IHarmonyModHooks
{
	public const string ModVersion = "1.6.5";

	/// <summary>Unique cui.endtest marker (AdminMenu/TeleportGUI pattern). Clients only forward ConsoleGen commands.</summary>
	public const string CuiMarker = "TCUPGRADE";

	private const ulong HammerWallpaperSkin = 3494416562uL;

	private const int ClothItemId = -858312878;

	private const string CmdPrefix = "cui.endtest TCUPGRADE ";

	private static object _replicatedList;

	private readonly Dictionary<BuildingPrivlidge, TCConfig> _buildingCupboard = new Dictionary<BuildingPrivlidge, TCConfig>();

	private readonly Dictionary<ulong, BuildingPrivlidge> _playerLootingTc = new Dictionary<ulong, BuildingPrivlidge>();

	/// <summary>TC for which upgrade menu or a submenu is open after inventory was closed (Tab / detached CUI).</summary>
	private readonly Dictionary<ulong, BuildingPrivlidge> _playerMenuCup = new Dictionary<ulong, BuildingPrivlidge>();

	private readonly Dictionary<ulong, BoatWallpaperConfig> _playerBoatWallpaper = new Dictionary<ulong, BoatWallpaperConfig>();

	private static readonly Dictionary<string, string> _localImageIds = new Dictionary<string, string>();

	private static bool _localImagesLoadRetryScheduled;

	private static bool _localImagesLazyLoadAttempted;

	private readonly Dictionary<ulong, TCSkin> _playerSelectedSkins = new Dictionary<ulong, TCSkin>();

	private ConsoleSystem.Command _sendCmdCommand;

	private ConsoleSystem.Command _wphammerCommand;

	private ConsoleSystem.Command _addwpCommand;

	private ConsoleSystem.Command _openBoatWallpaperCommand;

	private TCUpgradeData _data;

	private int _maxGradeTier = 4;

	private static Type _cachedOxideModType;

	private static object _cachedOxideModInstance;

	public static TCUpgradeMod Instance { get; private set; }

	public bool ForceBothSides => TCUpgradeConfig.Config?.ForceBothSides ?? true;

	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Instance = this;
		TCUpgradeConfig.LoadConfig();
		_data = TCUpgradeData.Load();
		try
		{
			_sendCmdCommand = new ConsoleSystem.Command
			{
				Name = "SENDCMD",
				FullName = "global.SENDCMD",
				Variable = true,
				ServerAdmin = false,
				Replicated = true,
				Call = HandleSendCmd
			};
			ConsoleSystem.Index.Server.Dict["global.SENDCMD"] = _sendCmdCommand;
			if (ConsoleSystem.Index.Server.GlobalDict != null)
			{
				ConsoleSystem.Index.Server.GlobalDict["SENDCMD"] = _sendCmdCommand;
			}
			PropertyInfo property = typeof(ConsoleSystem.Index.Server).GetProperty("Replicated", BindingFlags.Static | BindingFlags.Public);
			if (property != null && property.GetValue(null) is IList list && !list.Contains(_sendCmdCommand))
			{
				list.Add(_sendCmdCommand);
				_replicatedList = list;
			}
			SendReplicatedCommandsToActivePlayers();
			_wphammerCommand = new ConsoleSystem.Command
			{
				Name = "wphammer",
				FullName = "global.wphammer",
				Variable = true,
				ServerAdmin = false,
				Call = CmdWphammer
			};
			_addwpCommand = new ConsoleSystem.Command
			{
				Name = "addwp",
				FullName = "global.addwp",
				Variable = true,
				ServerAdmin = false,
				Call = CmdAddwp
			};
			_openBoatWallpaperCommand = new ConsoleSystem.Command
			{
				Name = "tcupgrade.openboatwallpaper",
				FullName = "global.tcupgrade.openboatwallpaper",
				Variable = true,
				ServerAdmin = false,
				Call = CmdOpenBoatWallpaper
			};
			ConsoleSystem.Index.Server.Dict["global.wphammer"] = _wphammerCommand;
			ConsoleSystem.Index.Server.Dict["global.addwp"] = _addwpCommand;
			ConsoleSystem.Index.Server.Dict["global.tcupgrade.openboatwallpaper"] = _openBoatWallpaperCommand;
			if (ConsoleSystem.Index.Server.GlobalDict != null)
			{
				ConsoleSystem.Index.Server.GlobalDict["tcupgrade.openboatwallpaper"] = _openBoatWallpaperCommand;
			}
		}
		catch (Exception ex)
		{
			Log("Command registration failed (some features may not work): " + ex.Message, force: true);
		}
		try
		{
			if (BaseNetworkable.serverEntities != null)
		{
			foreach (var entity in BaseNetworkable.serverEntities)
			{
				if (entity is BuildingPrivlidge buildingPrivlidge && (Object)(object)buildingPrivlidge != (Object)null && buildingPrivlidge.skinID == 0L)
				{
					UpdateBlockedItems(buildingPrivlidge);
				}
			}
		}
		}
		catch
		{
		}
		Log($"TCUpgrade {ModVersion} loaded.", force: true);
		Log($"Config path: HarmonyConfig/TCUpgrade.json (relative to server root). Debug={TCUpgradeConfig.Config?.Debug ?? false}", force: true);
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config != null && config.Debug)
		{
			Log($"Config: ItemsList count={(TCUpgradeConfig.Config?.ItemsList?.Count).GetValueOrDefault()}");
		}
		NextTick(LoadLocalImagesToFileStorage);
	}

	private static void LoadLocalImagesToFileStorage()
	{
		CommunityEntity communityEntity = CommunityEntity.ServerInstance;
		if (((Object)(object)communityEntity == (Object)null || communityEntity.IsDestroyed) && BaseNetworkable.serverEntities != null)
		{
			foreach (var e in BaseNetworkable.serverEntities)
			{
				if (e is CommunityEntity communityEntity2 && (Object)(object)communityEntity2 != (Object)null && !communityEntity2.IsDestroyed)
				{
					communityEntity = communityEntity2;
					break;
				}
			}
		}
		if ((Object)(object)communityEntity == (Object)null || communityEntity.IsDestroyed)
		{
			if (!_localImagesLoadRetryScheduled)
			{
				_localImagesLoadRetryScheduled = true;
				ServerMgr instance = SingletonComponent<ServerMgr>.Instance;
				if (instance != null)
				{
					((MonoBehaviour)instance).StartCoroutine(DelayedRetryLoadImagesCoroutine());
				}
			}
			TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
			if (config != null && config.Debug)
			{
				Log("LoadLocalImages: CommunityEntity not found (will retry in 5s at boot)");
			}
			return;
		}
		string text = ((!string.IsNullOrWhiteSpace(TCUpgradeConfig.Config?.ImagesPathOverride)) ? TCUpgradeConfig.Config.ImagesPathOverride.Trim() : Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "HarmonyImages", "TCUpgrade"));
		if (!Directory.Exists(text))
		{
			TCUpgradeConfig.ConfigData config2 = TCUpgradeConfig.Config;
			if (config2 != null && config2.Debug)
			{
				Log("LoadLocalImages: directory not found: " + text);
			}
			return;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>
		{
			{ "lock5", "lock5.png" },
			{ "upgrade2", "upgrade.png" },
			{ "nowp", "no.png" },
			{ "wood", "wood.png" },
			{ "stone", "stone.png" },
			{ "metal", "metal.png" },
			{ "armored", "armored.png" },
			{ "legacywood", "legacywood.png" },
			{ "gingerbread", "gingerbread.png" },
			{ "adobe", "adobe.png" },
			{ "brick", "brick.png" },
			{ "brutalist", "brutalist.png" },
			{ "container", "container.png" },
			{ "jungle", "jungle.png" },
			{ "spacetest", "spacetest.png" },
			{ "crypt", "crypt.png" }
		};
		for (int i = 0; i <= 16; i++)
		{
			dictionary["color_" + i] = Path.Combine("colours", i + ".png");
		}
		lock (_localImageIds)
		{
			_localImageIds.Clear();
			foreach (KeyValuePair<string, string> item in dictionary)
			{
				string path = Path.Combine(text, item.Value);
				if (!File.Exists(path))
				{
					continue;
				}
				try
				{
					byte[] data = File.ReadAllBytes(path);
					string value = FileStorage.server.Store(data, FileStorage.Type.png, communityEntity.net.ID).ToString();
					_localImageIds[item.Key] = value;
					string fileName = Path.GetFileName(path);
					if (TCUpgradeConfig.Config?.ItemsList == null)
					{
						continue;
					}
					foreach (TCUpgradeConfig.ItemInfo items in TCUpgradeConfig.Config.ItemsList)
					{
						if (!string.IsNullOrEmpty(items.Img) && items.Img.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
						{
							_localImageIds[items.Img] = value;
						}
					}
				}
				catch
				{
				}
			}
		}
		if (_localImageIds.Count > 0)
		{
			Log($"Loaded {_localImageIds.Count} local images from HarmonyImages/TCUpgrade/");
		}
		else if (!_localImagesLoadRetryScheduled)
		{
			_localImagesLoadRetryScheduled = true;
			ServerMgr instance2 = SingletonComponent<ServerMgr>.Instance;
			if (instance2 != null)
			{
				((MonoBehaviour)instance2).StartCoroutine(DelayedRetryLoadImagesCoroutine());
			}
		}
	}

	private static void SendReplicatedCommandsToActivePlayers()
	{
		foreach (BasePlayer player in BasePlayer.activePlayerList)
		{
			if ((Object)(object)player?.net?.connection == (Object)null)
				continue;
			ServerMgr.SendReplicatedVars(player.net.connection);
		}
	}

	private static IEnumerator DelayedRetryLoadImagesCoroutine()
	{
		yield return CoroutineEx.waitForSeconds(5f);
		LoadLocalImagesToFileStorage();
		if (_localImageIds.Count > 0 && (TCUpgradeConfig.Config?.Debug ?? false))
		{
			Log("LoadLocalImages: retry succeeded after delay.");
		}
	}

	private static string GetLocalImage(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		lock (_localImageIds)
		{
			if (_localImageIds.TryGetValue(name, out var value))
			{
				return value;
			}
		}
		if (!_localImagesLazyLoadAttempted || File.Exists(Path.Combine((!string.IsNullOrWhiteSpace(TCUpgradeConfig.Config?.ImagesPathOverride)) ? TCUpgradeConfig.Config.ImagesPathOverride.Trim() : Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "HarmonyImages", "TCUpgrade"), name + ".png")))
		{
			_localImagesLazyLoadAttempted = true;
			try
			{
				LoadLocalImagesToFileStorage();
			}
			catch
			{
			}
			lock (_localImageIds)
			{
				string value2;
				return _localImageIds.TryGetValue(name, out value2) ? value2 : null;
			}
		}
		return null;
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		ListHashSet<BasePlayer> activePlayerList = BasePlayer.activePlayerList;
		if (activePlayerList == null)
		{
			return;
		}
		foreach (var current in activePlayerList)
		{
			CUIHelper.DestroyUi(current, "TCUpgrade.buttons");
			CUIHelper.DestroyUi(current, "TCUpgrade.upgrade");
			DestroyBoatWallpaperUi(current);
		}
		foreach (KeyValuePair<BuildingPrivlidge, TCConfig> item in _buildingCupboard)
		{
			if (item.Value.WorkUpgrade != null)
			{
				ServerMgr instance = SingletonComponent<ServerMgr>.Instance;
				if (instance != null)
				{
					((MonoBehaviour)instance).StopCoroutine(item.Value.WorkUpgrade);
				}
			}
			if (item.Value.WorkRepair != null)
			{
				ServerMgr instance2 = SingletonComponent<ServerMgr>.Instance;
				if (instance2 != null)
				{
					((MonoBehaviour)instance2).StopCoroutine(item.Value.WorkRepair);
				}
			}
			if (item.Value.WorkReskin != null)
			{
				ServerMgr instance3 = SingletonComponent<ServerMgr>.Instance;
				if (instance3 != null)
				{
					((MonoBehaviour)instance3).StopCoroutine(item.Value.WorkReskin);
				}
			}
			if (item.Value.WorkWallpaper != null)
			{
				ServerMgr instance4 = SingletonComponent<ServerMgr>.Instance;
				if (instance4 != null)
				{
					((MonoBehaviour)instance4).StopCoroutine(item.Value.WorkWallpaper);
				}
			}
			if (item.Value.WorkUpwall != null)
			{
				ServerMgr instance5 = SingletonComponent<ServerMgr>.Instance;
				if (instance5 != null)
				{
					((MonoBehaviour)instance5).StopCoroutine(item.Value.WorkUpwall);
				}
			}
		}
		foreach (KeyValuePair<ulong, BoatWallpaperConfig> item2 in _playerBoatWallpaper)
		{
			if (item2.Value.WorkWallpaper != null)
			{
				ServerMgr instance6 = SingletonComponent<ServerMgr>.Instance;
				if (instance6 != null)
				{
					((MonoBehaviour)instance6).StopCoroutine(item2.Value.WorkWallpaper);
				}
			}
		}
		_buildingCupboard.Clear();
		_playerLootingTc.Clear();
		_playerMenuCup.Clear();
		_playerBoatWallpaper.Clear();
		_localImagesLoadRetryScheduled = false;
		_localImagesLazyLoadAttempted = false;
		try
		{
			if (_replicatedList is IList list && _sendCmdCommand != null)
			{
				list.Remove(_sendCmdCommand);
			}
			if (_sendCmdCommand != null)
			{
				ConsoleSystem.Index.Server.Dict.Remove("global.SENDCMD");
				ConsoleSystem.Index.Server.GlobalDict?.Remove("SENDCMD");
			}
			if (_wphammerCommand != null)
			{
				ConsoleSystem.Index.Server.Dict.Remove("global.wphammer");
			}
			if (_addwpCommand != null)
			{
				ConsoleSystem.Index.Server.Dict.Remove("global.addwp");
			}
			if (_openBoatWallpaperCommand != null)
			{
				ConsoleSystem.Index.Server.Dict.Remove("global.tcupgrade.openboatwallpaper");
				ConsoleSystem.Index.Server.GlobalDict?.Remove("tcupgrade.openboatwallpaper");
			}
		}
		catch
		{
		}
		_replicatedList = null;
		Instance = null;
		Log("Mod unloaded.", force: true);
	}

	private static void Log(string msg, bool force = false)
	{
		bool flag = TCUpgradeConfig.Config?.Debug ?? false;
		if (force || flag)
		{
			Debug.Log((object)("[TCUpgrade] " + msg));
		}
	}

	public bool HasPermission(string userId, string perm)
	{
		if (string.IsNullOrEmpty(perm))
		{
			return true;
		}
		if (ulong.TryParse(userId, out var result) && TCUpgradeConfig.Config?.AdminSteamIds?.Contains(result) == true)
		{
			return true;
		}
		switch (perm)
		{
		case "TCUpgrade.use":
		case "default":
			return true;
		case "TCUpgrade.reskin.nocost":
			return true;
		default:
			if (perm.EndsWith(".nocost"))
			{
				return false;
			}
			break;
		case null:
			break;
		}
		switch (perm)
		{
		case "TCUpgrade.updefault":
		case "TCUpgrade.upskin":
			return TCUpgradeConfig.Config?.GrantUpgradeSkinPermissionsToAll ?? true;
		case "TCUpgrade.upgrade":
		case "TCUpgrade.repair":
		case "TCUpgrade.reskin":
		case "TCUpgrade.wallpaper":
		case "TCUpgrade.upwall":
		case "TCUpgrade.authlist":
		case "TCUpgrade.tcskinchange":
		case "TCUpgrade.tcskindeployed":
		case "TCUpgrade.admin":
		case "TCUpgrade.wallpaper.custom":
		case "TCUpgrade.autolock":
		case "TCUpgrade.autocodelock":
			return true;
		default:
			if (perm.StartsWith("TCUpgrade."))
			{
				return true;
			}
			break;
		case null:
			break;
		}
		return false;
	}

	private bool CanUseItemSkin(BasePlayer player, TCUpgradeConfig.ItemInfo item)
	{
		if ((Object)(object)player == (Object)null || item == null)
		{
			return false;
		}
		if (HasPermission(player.UserIDString, item.Permission))
		{
			return TCUpgradeHelpers.IsSkinOwnedOrBypass(player, item.SkinId);
		}
		return false;
	}

	public TCConfig GetOrCreateConfig(BuildingPrivlidge cup)
	{
		if (!_buildingCupboard.TryGetValue(cup, out var value))
		{
			value = new TCConfig();
			_buildingCupboard[cup] = value;
		}
		return value;
	}

	public void OnLootStarted(BasePlayer player, BuildingPrivlidge cup)
	{
		if (!((Object)(object)player == (Object)null) && !((Object)(object)cup == (Object)null))
		{
			GetOrCreateConfig(cup);
			if (_playerMenuCup.TryGetValue(player.userID, out var prevMenuCup) && (Object)(object)prevMenuCup != (Object)null && (Object)(object)prevMenuCup != (Object)(object)cup)
			{
				ClearMenuCup(player);
				DestroyDetachedModals(player);
			}
			_playerLootingTc[player.userID] = cup;
			Log($"OnLootStarted: player={player?.displayName} cup={cup?.net?.ID}");
			ServerMgr instance = SingletonComponent<ServerMgr>.Instance;
			if (instance != null)
			{
				((MonoBehaviour)instance).StartCoroutine(ShowButtonTCDelayed(player, cup));
			}
		}
	}

	public void OnLootEnded(BasePlayer player)
	{
		if (!((Object)(object)player == (Object)null))
		{
			_playerLootingTc.Remove(player.userID);
			CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
			if (!_playerMenuCup.ContainsKey(player.userID))
			{
				DestroyDetachedModals(player);
			}
		}
	}

	private static void DestroyBoatWallpaperUi(BasePlayer player)
	{
		if (!((Object)(object)player == (Object)null))
		{
			CUIHelper.DestroyUi(player, "TCUpgrade.boatwallpaper");
		}
	}

	private static void DestroyDetachedModals(BasePlayer player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		CUIHelper.DestroyUi(player, "TCUpgrade.color");
		CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
		CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
	}

	private void ClearMenuCup(BasePlayer player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		_playerMenuCup.Remove(player.userID);
	}

	private void RegisterMenuCup(BasePlayer player, BuildingPrivlidge cup)
	{
		if ((Object)(object)player == (Object)null || (Object)(object)cup == (Object)null)
		{
			return;
		}
		_playerMenuCup[player.userID] = cup;
	}

	private void ShowButtonTCIfStillLooting(BasePlayer player, BuildingPrivlidge cup)
	{
		if (!((Object)(object)player == (Object)null))
		{
			if (_playerLootingTc.TryGetValue(player.userID, out var value) && (Object)(object)value == (Object)(object)cup)
			{
				ShowButtonTC(player, cup);
				return;
			}
			CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
			if (!_playerMenuCup.ContainsKey(player.userID))
			{
				DestroyDetachedModals(player);
			}
		}
	}

	private IEnumerator ShowButtonTCDelayed(BasePlayer player, BuildingPrivlidge cup)
	{
		yield return CoroutineEx.waitForSeconds(0.35f);
		if (!((Object)(object)player == (Object)null) && !((Object)(object)cup == (Object)null) && _playerLootingTc.TryGetValue(player.userID, out var value) && !((Object)(object)value != (Object)(object)cup))
		{
			ShowButtonTC(player, cup);
		}
	}

	private void ShowButtonTC(BasePlayer player, BuildingPrivlidge cup)
	{
		CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
		NextTick(delegate
		{
			if (!((Object)(object)player == (Object)null) && _playerLootingTc.TryGetValue(player.userID, out var value) && !((Object)(object)value != (Object)(object)cup))
			{
				TCConfig orCreateConfig = GetOrCreateConfig(cup);
				List<JObject> list = new List<JObject>();
				string offsetMin = TCUpgradeConfig.Config?.OffsetMin ?? "278 621";
				string offsetMax = TCUpgradeConfig.Config?.OffsetMax ?? "571 643";
				string anchorMin = TCUpgradeConfig.Config?.AnchorMin ?? "0.5 0";
				string anchorMax = TCUpgradeConfig.Config?.AnchorMax ?? "0.5 0";
				string text = TCUpgradeConfig.Config?.BtnTcColor ?? "0.3 0.40 0.3 0.60";
				string text2 = TCUpgradeConfig.Config?.BtnTcColorActive ?? "0.90 0.20 0.20 0.50";
				string color = (orCreateConfig.Work ? text2 : text);
				string color2 = (orCreateConfig.Repair ? text2 : (HasPermission(player.UserIDString, "TCUpgrade.repair") ? text : "0.2 0.2 0.2 0.5"));
				string color3 = text;
				int itemId = GetItemId("wood");
				int itemId2 = GetItemId("hammer");
				int itemId3 = GetItemId("box.wooden");
				string text3 = TCUpgradeConfig.Config?.ButtonsParent?.Trim();
				if (string.IsNullOrEmpty(text3))
				{
					text3 = "Hud";
				}
				list.Add(CUIHelper.Container("TCUpgrade.buttons", text3, anchorMin, anchorMax, offsetMin, offsetMax, needsCursor: true));
				list.AddRange(CUIHelper.Button("btn_upgrade", "TCUpgrade.buttons", color, LangHelper.Lang("UPGRADE"), 12, "0 0", $"{0.32f:F3} 1", "cui.endtest SENDCMD MENU", itemId));
				list.AddRange(CUIHelper.Button("btn_repair", "TCUpgrade.buttons", color2, LangHelper.Lang("REPAIR"), 12, $"{0.34f:F3} 0", $"{0.65999997f:F3} 1", "cui.endtest SENDCMD REPAIR", itemId2));
				list.AddRange(CUIHelper.Button("btn_auth", "TCUpgrade.buttons", color3, LangHelper.Lang("AUTH"), 11, $"{0.68f:F3} 0", "1 1", "cui.endtest SENDCMD AUTH 0", itemId3));
				Log($"ShowButtonTC: sending {list.Count} elements to {player?.displayName}");
				CUIHelper.AddUi(player, list);
			}
		});
	}

	private void ShowMenu(BasePlayer player, BuildingPrivlidge cup, int page = 0)
	{
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		List<TCUpgradeConfig.ItemInfo> buildingItems = GetBuildingItems();
		if (buildingItems.Count == 0)
		{
			return;
		}
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		List<JObject> list = new List<JObject>();
		int num = 12;
		list.Add(CUIHelper.Panel("TCUpgrade.upgrade", "OverlayNonScaled", "0.02 0.03 0.04 0.93", "0.5 0.5", "0.5 0.5", "-1000 -800", "1000 800", needsCursor: true));
		list.Add(CUIHelper.Panel("TCUpgrade.title", "TCUpgrade.upgrade", "0.06 0.08 0.09 0.94", "0.5 0.5", "0.5 0.5", "-450 230", "450 260"));
		list.Add(CUIHelper.Label("title_lbl", "TCUpgrade.title", LangHelper.Lang("title1"), 16, "0.022 0.05", "0.8 0.95", "1 1 1 0.9", "MiddleLeft"));
		list.AddRange(CUIHelper.Button("close", "TCUpgrade.title", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 13, "0.89 0", "0.999 0.982", "cui.endtest SENDCMD CLOSE"));
		list.Add(CUIHelper.Panel("TCUpgrade.content", "TCUpgrade.upgrade", "0.08 0.09 0.10 0.91", "0.5 0.5", "0.5 0.5", "-450 -190", "450 230"));
		string indicatorColor = (orCreateConfig.Effect ? "0.2 0.8 0.2 1" : "0.8 0.2 0.2 1");
		string indicatorColor2 = (orCreateConfig.Downgrade ? "0.2 0.8 0.2 1" : "0.8 0.2 0.2 1");
		list.AddRange(CUIHelper.ButtonWithStatusIndicator("effect", "TCUpgrade.content", "0.45 0.45 0.45 0.95", indicatorColor, orCreateConfig.Effect ? LangHelper.Lang("EffectON") : LangHelper.Lang("EffectOFF"), 10, "0.02 0.05", "0.055 0.09", "0.06 0.05", "0.12 0.09", string.Format("{0}EFFECT {1}", "cui.endtest SENDCMD ", page)));
		list.AddRange(CUIHelper.ButtonWithStatusIndicator("downgrade", "TCUpgrade.content", "0.45 0.45 0.45 0.95", indicatorColor2, orCreateConfig.Downgrade ? LangHelper.Lang("DowngradeON") : LangHelper.Lang("DowngradeOFF"), 10, "0.12 0.05", "0.155 0.09", "0.16 0.05", "0.38 0.09", string.Format("{0}DOWNGRADE {1}", "cui.endtest SENDCMD ", page)));
		if (page > 0)
		{
			list.AddRange(CUIHelper.Button("prev", "TCUpgrade.content", "0.3 0.3 0.8 0.9", "< Back", 10, "0.76 0.02", "0.86 0.08", string.Format("{0}PAGE {1}", "cui.endtest SENDCMD ", page - 1)));
		}
		if (HasPermission(player.UserIDString, "TCUpgrade.tcskinchange"))
		{
			int itemId = GetItemId("cupboard.tool");
			list.AddRange(CUIHelper.Button("tcskin", "TCUpgrade.content", "0.2 0.6 0.2 0.6", LangHelper.Lang("TCSkin"), 14, "0.42 0.02", "0.58 0.08", string.Format("{0}TCSKIN {1}", "cui.endtest SENDCMD ", page), itemId, iconOnLeft: true));
		}
		if (buildingItems.Count > (page + 1) * num)
		{
			list.AddRange(CUIHelper.Button("next", "TCUpgrade.content", "0.3 0.3 0.8 0.9", "Next >", 10, "0.76 0.02", "0.86 0.08", string.Format("{0}PAGE {1}", "cui.endtest SENDCMD ", page + 1)));
		}
		list.AddRange(CUIHelper.Button("stop", "TCUpgrade.content", orCreateConfig.Work ? "0.9 0.2 0.2 0.5" : "0.3 0.3 0.3 0.6", LangHelper.Lang("STOP"), 11, "0.88 0.02", "0.98 0.08", string.Format("{0}STOP {1}", "cui.endtest SENDCMD ", page)));
		int num2 = 135;
		int num3 = 135;
		int num4 = -430;
		int num5 = 190;
		int num6 = 10;
		int num7 = 35;
		int num8 = page * num;
		int num9 = Math.Min(num, buildingItems.Count - num8);
		for (int i = 0; i < num9; i++)
		{
			TCUpgradeConfig.ItemInfo itemInfo = buildingItems[num8 + i];
			int num10 = i % 6;
			int num11 = i / 6;
			int num12 = num4 + num10 * (num2 + num6);
			int num13 = num5 - num11 * (num3 + num7);
			string text = $"card_{itemInfo.ID}_{page}";
			list.Add(CUIHelper.Panel(text, "TCUpgrade.content", "0.2 0.3 0.2 0.6", "0.5 0.5", "0.5 0.5", $"{num12} {num13 - num3 - 25}", $"{num12 + num2} {num13}"));
			JObject val = null;
			if (!string.IsNullOrEmpty(itemInfo.Img))
			{
				if (itemInfo.Img.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || itemInfo.Img.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				{
					val = CUIHelper.RawImageUrl($"icon_{itemInfo.ID}_{page}", text, itemInfo.Img, "0.05 0.22", "0.95 0.92");
				}
				else
				{
					bool num14 = TCUpgradeConfig.Config?.UseUrlForMenuImages ?? false;
					string imageUrl = CUIHelper.GetImageUrl(itemInfo.Img);
					if (num14 && !string.IsNullOrEmpty(imageUrl))
					{
						val = CUIHelper.RawImageUrl($"icon_{itemInfo.ID}_{page}", text, imageUrl, "0.05 0.22", "0.95 0.92");
					}
					if (val == null)
					{
						string name = ((itemInfo.Img.IndexOf("/", StringComparison.Ordinal) >= 0) ? Path.GetFileNameWithoutExtension(itemInfo.Img) : itemInfo.Img);
						string text2 = GetLocalImage(itemInfo.Img) ?? GetLocalImage(name);
						if (!string.IsNullOrEmpty(text2))
						{
							val = CUIHelper.RawImage($"icon_{itemInfo.ID}_{page}", text, text2, "0.05 0.22", "0.95 0.92");
						}
					}
				}
			}
			if (val == null && !string.IsNullOrEmpty(TCUpgradeConfig.Config?.ImageUrlBase))
			{
				string text3 = TCUpgradeConfig.Config.ImageUrlBase.Trim().TrimEnd('/') + "/";
				string text4 = itemInfo.Name?.ToLowerInvariant().Replace(" ", "") ?? "";
				if (!string.IsNullOrEmpty(text4))
				{
					val = CUIHelper.RawImageUrl($"icon_{itemInfo.ID}_{page}", text, text3 + text4 + ".png", "0.05 0.22", "0.95 0.92");
				}
			}
			if (val == null && itemInfo.ItemID != 0)
			{
				val = CUIHelper.Image($"icon_{itemInfo.ID}_{page}", text, itemInfo.ItemID, (ulong)itemInfo.SkinId, "0.05 0.22", "0.95 0.92");
			}
			else if (val == null && itemInfo.ItemID2 != 0)
			{
				val = CUIHelper.Image($"icon_{itemInfo.ID}_{page}", text, itemInfo.ItemID2, (ulong)itemInfo.SkinId, "0.05 0.22", "0.95 0.92");
			}
			if (val != null)
			{
				list.Add(val);
			}
			list.Add(CUIHelper.Label($"lbl_{itemInfo.ID}_{page}", text, itemInfo.Name, 12, "0.05 0", "0.55 0.15", "0.7 0.7 0.7 1", "MiddleLeft"));
			bool flag = CanUseItemSkin(player, itemInfo);
			if (!flag)
			{
				list.AddRange(CUIHelper.Button($"upg_{itemInfo.ID}_{page}", text, "0.2 0.2 0.2 0.8", LangHelper.Lang("NODLC"), 10, "0.6 0", "0.99 0.15", "cui.endtest SENDCMD NODLC"));
			}
			else
			{
				string text5 = (itemInfo.Color ? string.Format("{0}COLOR {1} {2} {3} {4} {5}", "cui.endtest SENDCMD ", itemInfo.ID, itemInfo.Grade, itemInfo.SkinId, ColorIndexFromUint(orCreateConfig.Colour), page) : string.Format("{0}UPGRADE {1} {2} {3} {4} 0", "cui.endtest SENDCMD ", itemInfo.ID, itemInfo.Grade, itemInfo.SkinId, page));
				string color = ((orCreateConfig.Work && orCreateConfig.Id == itemInfo.ID) ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.1");
				list.AddRange(CUIHelper.Button($"upg_{itemInfo.ID}_{page}", text, color, (orCreateConfig.Work && orCreateConfig.Id == itemInfo.ID) ? LangHelper.Lang("STOP") : LangHelper.Lang("UPGRADE"), 10, "0.6 0", "0.99 0.15", (orCreateConfig.Work && orCreateConfig.Id == itemInfo.ID) ? string.Format("{0}STOP {1}", "cui.endtest SENDCMD ", page) : text5));
			}
			list.AddRange(CUIHelper.Button($"cost_{itemInfo.ID}_{page}", text, "0.4 0.4 0.4 0.3", "?", 10, "0.82 0.78", "0.95 0.91", string.Format("{0}COSTUPGRADE {1} {2} {3} {4}", "cui.endtest SENDCMD ", itemInfo.ID, itemInfo.Grade, itemInfo.SkinId, page)));
			if (flag)
			{
				TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
				if ((config == null || config.Reskin) && HasPermission(player.UserIDString, "TCUpgrade.reskin"))
				{
					bool flag2 = orCreateConfig.Reskin && orCreateConfig.Id == itemInfo.ID;
					list.Add(CUIHelper.Panel($"reskin_bg_{itemInfo.ID}_{page}", text, flag2 ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.30", "0.82 0.62", "0.95 0.75", "0 0", "0 0"));
					JObject val2 = CUIHelper.Image($"reskin_icon_{itemInfo.ID}_{page}", $"reskin_bg_{itemInfo.ID}_{page}", -596876839, 0uL, "0.05 0.05", "0.95 0.95");
					if (val2 != null)
					{
						list.Add(val2);
					}
					string text6 = string.Format("{0}RESKIN {1} {2} {3} {4}", "cui.endtest SENDCMD ", itemInfo.ID, itemInfo.Grade, itemInfo.SkinId, page);
					if (itemInfo.Color)
					{
						text6 += $" {ColorIndexFromUint(orCreateConfig.Colour)}";
					}
					list.AddRange(CUIHelper.Button($"reskin_{itemInfo.ID}_{page}", text, "0 0 0 0", "", 10, "0.82 0.62", "0.95 0.75", text6));
				}
			}
			if (!flag)
			{
				continue;
			}
			TCUpgradeConfig.ConfigData config2 = TCUpgradeConfig.Config;
			if ((config2 == null || config2.Wallpaper) && HasPermission(player.UserIDString, "TCUpgrade.wallpaper") && !cup.HasParent())
			{
				bool flag3 = orCreateConfig.WorkWallpaper != null && orCreateConfig.Id == itemInfo.ID;
				list.Add(CUIHelper.Panel($"wall_bg_{itemInfo.ID}_{page}", text, flag3 ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.30", "0.82 0.46", "0.95 0.59", "0 0", "0 0"));
				JObject val3 = CUIHelper.Image($"wall_icon_{itemInfo.ID}_{page}", $"wall_bg_{itemInfo.ID}_{page}", 1629564540, 0uL, "0.05 0.05", "0.95 0.95");
				if (val3 != null)
				{
					list.Add(val3);
				}
				list.AddRange(CUIHelper.Button($"wall_{itemInfo.ID}_{page}", text, "0 0 0 0", "", 10, "0.82 0.46", "0.95 0.59", string.Format("{0}WALLPAPER {1} {2} {3} {4} Wall", "cui.endtest SENDCMD ", itemInfo.ID, itemInfo.Grade, itemInfo.SkinId, page)));
			}
		}
		RegisterMenuCup(player, cup);
		CUIHelper.AddUi(player, list);
	}

	private List<TCUpgradeConfig.ItemInfo> GetBuildingItems()
	{
		List<TCUpgradeConfig.ItemInfo> list = new List<TCUpgradeConfig.ItemInfo>();
		if (TCUpgradeConfig.Config?.ItemsList != null)
		{
			foreach (TCUpgradeConfig.ItemInfo items in TCUpgradeConfig.Config.ItemsList)
			{
				if (items.Enabled)
				{
					list.Add(items);
				}
			}
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config != null && !config.AutoSortItems)
		{
			return list;
		}
		Dictionary<string, int> gradeOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["wood"] = 1,
			["stone"] = 2,
			["metal"] = 3,
			["armored"] = 4
		};
		list.Sort(delegate(TCUpgradeConfig.ItemInfo a, TCUpgradeConfig.ItemInfo b)
		{
			int value;
			int num = (gradeOrder.TryGetValue(a?.Grade ?? "", out value) ? value : 999);
			int value3;
			int value2 = (gradeOrder.TryGetValue(b?.Grade ?? "", out value3) ? value3 : 999);
			int num2 = num.CompareTo(value2);
			return (num2 != 0) ? num2 : a.ID.CompareTo(b.ID);
		});
		return list;
	}

	public void HandleSendCmdFromCui(ConsoleSystem.Arg arg)
	{
		string[] array = GetArgStrings(arg);
		if (array == null || array.Length < 1)
		{
			return;
		}
		string[] actionArgs = StripCuiMarker(array);
		if (actionArgs != null && actionArgs.Length >= 1)
		{
			HandleSendCmdWithArgs(arg.Player(), actionArgs);
		}
	}

	/// <summary>
	/// Strips TCUPGRADE / SENDCMD bridge markers from cui.endtest args.
	/// Accepts: ["TCUPGRADE","MENU"], ["SENDCMD","MENU"], or a single combined token.
	/// </summary>
	private static string[] StripCuiMarker(string[] array)
	{
		if (array == null || array.Length < 1)
		{
			return null;
		}
		if (array.Length == 1)
		{
			string token = array[0];
			foreach (string prefix in new[] { "TCUPGRADE ", "SENDCMD ", "cui.endtest TCUPGRADE ", "cui.endtest SENDCMD " })
			{
				if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					string rest = token.Substring(prefix.Length).TrimStart();
					return string.IsNullOrEmpty(rest) ? null : rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				}
			}
			return null;
		}
		string marker = array[0];
		int start = 0;
		if (string.Equals(marker, CuiMarker, StringComparison.OrdinalIgnoreCase) || string.Equals(marker, "SENDCMD", StringComparison.OrdinalIgnoreCase))
		{
			start = 1;
		}
		else if (string.Equals(marker, "cui.endtest", StringComparison.OrdinalIgnoreCase) && array.Length >= 2
			&& (string.Equals(array[1], CuiMarker, StringComparison.OrdinalIgnoreCase) || string.Equals(array[1], "SENDCMD", StringComparison.OrdinalIgnoreCase)))
		{
			start = 2;
		}
		else
		{
			return null;
		}
		if (array.Length <= start)
		{
			return null;
		}
		string[] result = new string[array.Length - start];
		for (int i = 0; i < result.Length; i++)
		{
			result[i] = array[start + i];
		}
		return result;
	}

	private void HandleSendCmd(ConsoleSystem.Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!((Object)(object)basePlayer == (Object)null))
		{
			HandleSendCmdWithArgs(basePlayer, GetArgStrings(arg));
		}
	}

	internal static string[] GetArgStrings(ConsoleSystem.Arg arg)
	{
		if (arg == null) return Array.Empty<string>();

		object rawArgs = typeof(ConsoleSystem.Arg).GetField("Args", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(arg);
		if (rawArgs == null)
		{
			return Array.Empty<string>();
		}
		if (rawArgs is string singleArg)
		{
			return string.IsNullOrEmpty(singleArg) ? Array.Empty<string>() : new[] { singleArg };
		}
		if (rawArgs is Array array)
		{
			var result = new List<string>(array.Length);
			foreach (object value in array)
			{
				if (value != null)
					result.Add(value.ToString());
			}
			return result.ToArray();
		}
		return Array.Empty<string>();
	}

	private void HandleSendCmdWithArgs(BasePlayer player, string[] args)
	{
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		BuildingPrivlidge buildingPrivlidge = null;
		if (_playerLootingTc.TryGetValue(player.userID, out var lootCup) && (Object)(object)lootCup != (Object)null && _buildingCupboard.ContainsKey(lootCup))
		{
			buildingPrivlidge = lootCup;
		}
		else if (_playerMenuCup.TryGetValue(player.userID, out var menuCup) && (Object)(object)menuCup != (Object)null && _buildingCupboard.ContainsKey(menuCup))
		{
			buildingPrivlidge = menuCup;
		}
		else
		{
			buildingPrivlidge = GetPlayerTC(player);
		}
		if ((Object)(object)buildingPrivlidge != (Object)null && buildingPrivlidge.IsDestroyed)
		{
			ClearMenuCup(player);
			buildingPrivlidge = GetPlayerTC(player);
		}
		if ((Object)(object)buildingPrivlidge != (Object)null && !_buildingCupboard.ContainsKey(buildingPrivlidge))
		{
			GetOrCreateConfig(buildingPrivlidge);
		}
		if ((Object)(object)buildingPrivlidge == (Object)null || !_buildingCupboard.ContainsKey(buildingPrivlidge))
		{
			buildingPrivlidge = player.GetBuildingPrivilege();
			if ((Object)(object)buildingPrivlidge != (Object)null && !_buildingCupboard.ContainsKey(buildingPrivlidge))
			{
				GetOrCreateConfig(buildingPrivlidge);
			}
			if ((Object)(object)buildingPrivlidge == (Object)null || !_buildingCupboard.ContainsKey(buildingPrivlidge))
			{
				TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
				if (config != null && config.Debug)
				{
					Log($"HandleSendCmd: no TC for {player.displayName} (not looting? cup={buildingPrivlidge?.net?.ID})");
				}
				return;
			}
		}
		if (!buildingPrivlidge.IsAuthed(player))
		{
			TCUpgradeConfig.ConfigData config2 = TCUpgradeConfig.Config;
			if (config2 != null && config2.Debug)
			{
				Log("HandleSendCmd: " + player.displayName + " not authed on TC");
			}
			return;
		}
		if (args == null || args.Length < 1)
		{
			TCUpgradeConfig.ConfigData config3 = TCUpgradeConfig.Config;
			if (config3 != null && config3.Debug)
			{
				Log("HandleSendCmd: no args from " + player.displayName);
			}
			return;
		}
		TCUpgradeConfig.ConfigData config4 = TCUpgradeConfig.Config;
		if (config4 != null && config4.Debug)
		{
			Log("HandleSendCmd: " + player.displayName + " -> " + args[0]);
		}
		string text = args[0];
		if (text == null)
		{
			return;
		}
		switch (text.Length)
		{
		case 4:
			switch (text[0])
			{
			case 'M':
				if (text == "MENU")
				{
					ShowMenu(player, buildingPrivlidge);
				}
				break;
			case 'P':
			{
				if (text == "PAGE" && args.Length >= 2 && int.TryParse(args[1], out var result16))
				{
					ShowMenu(player, buildingPrivlidge, result16);
				}
				break;
			}
			case 'S':
			{
				if (text == "STOP" && args.Length >= 2 && int.TryParse(args[1], out var result17))
				{
					HandleStop(player, buildingPrivlidge, result17);
				}
				break;
			}
			case 'A':
			{
				if (text == "AUTH" && HasPermission(player.UserIDString, "TCUpgrade.authlist") && args.Length >= 2 && int.TryParse(args[1], out var result15))
				{
					ShowMenuAuthlist(player, buildingPrivlidge, result15);
				}
				break;
			}
			}
			break;
		case 5:
			switch (text[2])
			{
			case 'O':
				if (text == "CLOSE")
				{
					ClearMenuCup(player);
					CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
					CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
					if (_playerLootingTc.TryGetValue(player.userID, out var stillLooting) && (Object)(object)stillLooting == (Object)(object)buildingPrivlidge)
					{
						ShowButtonTC(player, buildingPrivlidge);
					}
				}
				break;
			case 'D':
				if (text == "NODLC")
				{
					TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
					TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
				}
				break;
			case 'L':
				if (text == "COLOR" && args.Length >= 6)
				{
					ShowMenuColor(player, buildingPrivlidge, args[1], args[2], args[3], args[4], int.Parse(args[5]));
				}
				break;
			}
			break;
		case 6:
			switch (text[0])
			{
			case 'R':
				if (!(text == "REPAIR"))
				{
					if (text == "RESKIN")
					{
						if (IsRaidBlocked(player))
						{
							TCUpgradeHelpers.CreateGameTip(buildingPrivlidge, LangHelper.Lang("RaidBlocked"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
						}
						else if (!HasPermission(player.UserIDString, "TCUpgrade.reskin"))
						{
							TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
						}
						else if (args.Length >= 5)
						{
							HandleReskin(player, buildingPrivlidge, args);
						}
					}
				}
				else if (IsRaidBlocked(player))
				{
					TCUpgradeHelpers.CreateGameTip(buildingPrivlidge, LangHelper.Lang("RaidBlocked"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
				}
				else if (HasPermission(player.UserIDString, "TCUpgrade.repair"))
				{
					ToggleRepair(player, buildingPrivlidge);
				}
				break;
			case 'E':
			{
				if (text == "EFFECT" && args.Length >= 2 && int.TryParse(args[1], out var result4))
				{
					TCConfig orCreateConfig = GetOrCreateConfig(buildingPrivlidge);
					orCreateConfig.Effect = !orCreateConfig.Effect;
					ShowMenu(player, buildingPrivlidge, result4);
				}
				break;
			}
			case 'U':
				if (text == "UPWALL")
				{
					if (IsRaidBlocked(player))
					{
						TCUpgradeHelpers.CreateGameTip(buildingPrivlidge, LangHelper.Lang("RaidBlocked"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
					}
					else if (!HasPermission(player.UserIDString, "TCUpgrade.upwall"))
					{
						TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
					}
					else if (args.Length >= 5)
					{
						HandleUpwall(player, buildingPrivlidge, args);
					}
				}
				break;
			case 'T':
			{
				if (text == "TCSKIN" && HasPermission(player.UserIDString, "TCUpgrade.tcskinchange") && args.Length >= 2 && int.TryParse(args[1], out var result5))
				{
					ShowMenuTCSkin(player, buildingPrivlidge, result5);
					CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
				}
				break;
			}
			case 'C':
			{
				if (text == "CLOSE2" && args.Length >= 2 && int.TryParse(args[1], out var result3))
				{
					CUIHelper.DestroyUi(player, "TCUpgrade.color");
					CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
					ShowMenu(player, buildingPrivlidge, result3);
				}
				break;
			}
			}
			break;
		case 11:
			switch (text[3])
			{
			case 'T':
			{
				if (text == "COSTUPGRADE" && args.Length >= 5 && int.TryParse(args[1], out var result8) && int.TryParse(args[3], out var result9) && int.TryParse(args[4], out var result10))
				{
					HandleCostUpgrade(player, buildingPrivlidge, result8, args[2], result9, result10);
				}
				break;
			}
			case 'L':
				if (text == "WALLPAPERON")
				{
					if (IsRaidBlocked(player))
					{
						TCUpgradeHelpers.CreateGameTip(buildingPrivlidge, LangHelper.Lang("RaidBlocked"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
					}
					else if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper"))
					{
						TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
					}
					else if (args.Length >= 7)
					{
						HandleWallpaperOn(player, buildingPrivlidge, args);
					}
				}
				break;
			case 'C':
			{
				if (text == "DELCUSTOMWP" && HasPermission(player.UserIDString, "TCUpgrade.admin") && args.Length >= 4 && ulong.TryParse(args[1], out var result7) && _data.CustomWallpapers.TryGetValue(args[2], out var value3) && value3.Remove(result7))
				{
					_data.Save();
					ShowMenuWallpaper(player, buildingPrivlidge, int.Parse(args[4]), args[2]);
				}
				break;
			}
			case 'O':
			{
				if (text == "COLORSELECT" && args.Length >= 6 && int.TryParse(args[4], out var result6))
				{
					GetOrCreateConfig(buildingPrivlidge).Colour = TCUpgradeHelpers.ColorIndexToUint(result6);
					ShowMenuColor(player, buildingPrivlidge, args[1], args[2], args[3], args[4], int.Parse(args[5]));
				}
				break;
			}
			}
			break;
		case 9:
			switch (text[0])
			{
			case 'D':
			{
				if (text == "DOWNGRADE" && args.Length >= 2 && int.TryParse(args[1], out var result14))
				{
					TCConfig orCreateConfig2 = GetOrCreateConfig(buildingPrivlidge);
					orCreateConfig2.Downgrade = !orCreateConfig2.Downgrade;
					ShowMenu(player, buildingPrivlidge, result14);
				}
				break;
			}
			case 'W':
				if (text == "WALLPAPER" && HasPermission(player.UserIDString, "TCUpgrade.wallpaper") && args.Length >= 5)
				{
					CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
					ShowMenuWallpaper(player, buildingPrivlidge, int.Parse(args[4]), (args.Length > 5) ? args[5] : "Wall");
				}
				break;
			}
			break;
		case 12:
			switch (text[6])
			{
			case 'S':
			{
				if (!(text == "TCSKINSELECT") || args.Length < 3)
				{
					break;
				}
				TCSkin tCSkin = TCSkinFromShortName(args[1]);
				if (TCSkinMeta.Data.TryGetValue(tCSkin, out (string, string, string, int, int) value7))
				{
					if (!TCUpgradeHelpers.IsSkinOwnedOrBypass(player, value7.Item5))
					{
						TCUpgradeHelpers.CreateGameTip(buildingPrivlidge, LangHelper.Lang("NoTCSkin"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
						TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.notcskin", "You don't own this TC skin.");
						break;
					}
					SetPlayerSelectedSkin(player.userID, tCSkin);
					TCSkinReplace(buildingPrivlidge, player, tCSkin);
					CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
					CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
				}
				break;
			}
			case 'L':
				if (text == "TCSKINLOCKED")
				{
					TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.notcskin", "You don't own this TC skin.");
				}
				break;
			}
			break;
		case 18:
			switch (text[13])
			{
			case 'S':
			{
				if (text == "BOATWALLPAPERSIDES" && _playerBoatWallpaper.TryGetValue(player.userID, out var value4) && args.Length >= 5 && int.TryParse(args[1], out var result11))
				{
					bool.TryParse(args[2], out value4.WpExternal);
					bool.TryParse(args[3], out value4.WpInternal);
					UpdateBoatWallpaperControls(player, result11, args[4]);
				}
				break;
			}
			case 'A':
			{
				if (text == "BOATWALLPAPERAPPLY" && _playerBoatWallpaper.TryGetValue(player.userID, out var value5) && args.Length >= 3 && int.TryParse(args[1], out var result12))
				{
					HandleBoatWallpaperApply(player, value5, result12, args[2]);
				}
				break;
			}
			case 'C':
				if (text == "BOATWALLPAPERCLOSE")
				{
					DestroyBoatWallpaperUi(player);
				}
				break;
			}
			break;
		case 7:
			if (!(text == "UPGRADE"))
			{
				break;
			}
			if (args.Length < 6)
			{
				TCUpgradeConfig.ConfigData config5 = TCUpgradeConfig.Config;
				if (config5 != null && config5.Debug)
				{
					Log($"HandleSendCmd UPGRADE: expected 6 args, got {((args != null) ? args.Length : 0)}");
				}
			}
			else if (IsRaidBlocked(player))
			{
				TCUpgradeHelpers.CreateGameTip(buildingPrivlidge, LangHelper.Lang("RaidBlocked"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			}
			else if (!HasPermission(player.UserIDString, "TCUpgrade.upgrade"))
			{
				TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			}
			else if (!TCUpgradeHelpers.Unlock(_maxGradeTier, args[2]))
			{
				TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeBlock"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			}
			else
			{
				StartUpgrade(player, buildingPrivlidge, args);
			}
			break;
		case 15:
		{
			if (text == "WALLPAPERSELECT" && args.Length >= 6 && ulong.TryParse(args[5], out var result18))
			{
				TCConfig orCreateConfig4 = GetOrCreateConfig(buildingPrivlidge);
				ulong wallpaperId2 = orCreateConfig4.WallpaperId;
				orCreateConfig4.WallpaperId = result18;
				UpdateBuildingWallpaperSelection(player, buildingPrivlidge, wallpaperId2, result18, int.Parse(args[4]), (args.Length > 6) ? args[6] : "Wall");
			}
			break;
		}
		case 14:
			if (text == "WALLPAPERSIDES" && args.Length >= 7)
			{
				TCConfig orCreateConfig3 = GetOrCreateConfig(buildingPrivlidge);
				if (args.Length > 5)
				{
					bool.TryParse(args[5], out orCreateConfig3.WpExternal);
				}
				if (args.Length > 6)
				{
					bool.TryParse(args[6], out orCreateConfig3.WpInternal);
				}
				UpdateBuildingWallpaperControls(player, buildingPrivlidge, int.Parse(args[4]), (args.Length > 7) ? args[7] : "Wall");
			}
			break;
		case 10:
		{
			if (text == "REMOVEAUTH" && HasPermission(player.UserIDString, "TCUpgrade.authlist") && args.Length >= 4 && ulong.TryParse(args[3], out var result19) && buildingPrivlidge.IsAuthed(player))
			{
				buildingPrivlidge.authorizedPlayers.Remove(result19);
				buildingPrivlidge.SendNetworkUpdate();
				if ((ulong)player.userID == result19)
				{
					CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
				}
				else
				{
					ShowMenuAuthlist(player, buildingPrivlidge, int.Parse(args[1]));
				}
			}
			break;
		}
		case 13:
			if (text == "BOATWALLPAPER")
			{
				TryOpenBoatWallpaper(player);
			}
			break;
		case 21:
		{
			if (text == "BOATWALLPAPERCATEGORY" && _playerBoatWallpaper.TryGetValue(player.userID, out var _) && args.Length >= 3 && int.TryParse(args[1], out var result13))
			{
				ShowMenuBoatWallpaper(player, result13, args[2]);
			}
			break;
		}
		case 19:
		{
			if (text == "BOATWALLPAPERSELECT" && _playerBoatWallpaper.TryGetValue(player.userID, out var value2) && args.Length >= 4 && ulong.TryParse(args[1], out var result) && int.TryParse(args[2], out var result2))
			{
				ulong wallpaperId = value2.WallpaperId;
				value2.WallpaperId = result;
				UpdateBoatWallpaperSelection(player, wallpaperId, result, result2, args[3]);
			}
			break;
		}
		case 8:
		case 16:
		case 17:
		case 20:
			break;
		}
	}

	private static int GetItemId(string shortName)
	{
		return ItemManager.FindItemDefinition(shortName)?.itemid ?? 0;
	}

	private static int ColorIndexFromUint(uint c)
	{
		if (c == 0)
		{
			return 0;
		}
		for (int i = 1; i < TCUpgradeHelpers.Colors.Length; i++)
		{
			if (TCUpgradeHelpers.ColorIndexToUint(i) == c)
			{
				return i;
			}
		}
		return 0;
	}

	/// <summary>
	/// Skins with <see cref="ConstructionSkin_CustomDetail"/> (e.g. shipping container) read
	/// <see cref="BuildingBlock.playerCustomColourToApply"/> inside <c>ChangeSkin</c> and set <c>customColour</c> from
	/// <see cref="ConstructionSkin_CustomDetail.GetStartingDetailColour"/>. Index 0 means random in game — set a real swatch index before <c>UpdateSkin</c>.
	/// Other skins ignore this and use packed RGBA via <see cref="BuildingBlock.SetCustomColour"/> after refresh.
	/// </summary>
	private static void SyncPlayerCustomColourBeforeSkin(BuildingBlock block, bool useMultiColour, uint packedPaletteColour)
	{
		if (block == null)
		{
			return;
		}
		if (!useMultiColour)
		{
			BuildingBlockCompat.SetPlayerCustomColourToApply(block, 0u);
			return;
		}
		int idx = ColorIndexFromUint(packedPaletteColour);
		BuildingBlockCompat.SetPlayerCustomColourToApply(block, (uint)(idx <= 0 ? 1 : idx));
	}

	private static void ApplyPackedColourIfNotCustomDetailSkin(BuildingBlock block, bool useMultiColour, uint packedPaletteColour)
	{
		if (block == null || !useMultiColour)
		{
			return;
		}
		if (block.GetComponentInChildren<ConstructionSkin_CustomDetail>() != null)
		{
			return;
		}
		block.SetCustomColour(packedPaletteColour);
	}

	/// <summary>For normal skins, <c>customColour</c> is packed RGBA. For <see cref="ConstructionSkin_CustomDetail"/>, it is the palette index.</summary>
	private static bool CustomColoursMatchForBlock(BuildingBlock block, TCConfig cfg)
	{
		if (!cfg.Color)
		{
			return true;
		}
		int desiredIdx = ColorIndexFromUint(cfg.Colour);
		if (desiredIdx <= 0)
		{
			desiredIdx = 1;
		}
		if (block.GetComponentInChildren<ConstructionSkin_CustomDetail>() != null && (ulong)block.skinID == (ulong)cfg.SkinId)
		{
			return (int)block.customColour == desiredIdx;
		}
		return block.customColour == cfg.Colour;
	}

	private static TCSkin TCSkinFromShortName(string shortName)
	{
		foreach (KeyValuePair<TCSkin, (string, string, string, int, int)> datum in TCSkinMeta.Data)
		{
			if (datum.Value.Item1 == shortName)
			{
				return datum.Key;
			}
		}
		return TCSkin.Default;
	}

	private void HandleWallpaperOn(BasePlayer player, BuildingPrivlidge cup, string[] args)
	{
		if (!int.TryParse(args[1], out var result) || !int.TryParse(args[3], out var result2) || !int.TryParse(args[4], out var _))
		{
			return;
		}
		string text = args[2];
		bool wallpall = args.Length > 5 && args[5] == "true";
		string category = ((args.Length > 6) ? args[6] : "Wall");
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		if (TCUpgradeHelpers.IsOnBarge(cup))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
			return;
		}
		if (orCreateConfig.WallpaperId > 1 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, (int)orCreateConfig.WallpaperId))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		BuildingGrade.Enum grade = text.ToLower() switch
		{
			"stone" => BuildingGrade.Enum.Stone, 
			"metal" => BuildingGrade.Enum.Metal, 
			"armored" => BuildingGrade.Enum.TopTier, 
			_ => BuildingGrade.Enum.Wood, 
		};
		orCreateConfig.Id = result;
		orCreateConfig.Grade = grade;
		orCreateConfig.SkinId = result2;
		orCreateConfig.Wallpall = wallpall;
		orCreateConfig.Work = !orCreateConfig.Work;
		orCreateConfig.Player = player.userID;
		if (orCreateConfig.Work)
		{
			orCreateConfig.WorkWallpaper = ((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(WallpaperProgress(player, cup, category));
		}
		else if (orCreateConfig.WorkWallpaper != null)
		{
			((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkWallpaper);
			orCreateConfig.WorkWallpaper = null;
		}
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		CUIHelper.DestroyUi(player, "TCUpgrade.color");
		ShowButtonTC(player, cup);
	}

	private void HandleBoatWallpaperApply(BasePlayer player, BoatWallpaperConfig cfg, int page, string category)
	{
		if ((Object)(object)player == (Object)null || cfg == null)
		{
			return;
		}
		if ((Object)(object)cfg.Boat == (Object)null || cfg.Boat.IsDestroyed)
		{
			player.ChatMessage(LangHelper.Lang("NotOnBoat"));
			DestroyBoatWallpaperUi(player);
			return;
		}
		if (cfg.WallpaperId > 1 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, (int)cfg.WallpaperId))
		{
			TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		cfg.Page = page;
		cfg.Category = category;
		cfg.Work = !cfg.Work;
		if (cfg.Work)
		{
			cfg.WorkWallpaper = ((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(BoatWallpaperProgress(player, cfg, category));
		}
		else if (cfg.WorkWallpaper != null)
		{
			((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(cfg.WorkWallpaper);
			cfg.WorkWallpaper = null;
		}
		UpdateBoatWallpaperControls(player, page, category);
	}

	private void HandleReskin(BasePlayer player, BuildingPrivlidge cup, string[] args)
	{
		if (!int.TryParse(args[1], out var result) || !int.TryParse(args[3], out var result2) || !int.TryParse(args[4], out var result3))
		{
			return;
		}
		string text = args[2];
		bool color = args.Length > 5 && args[5] != "0";
		TCUpgradeConfig.ItemInfo itemInfo = TCUpgradeHelpers.GetItemInfo(result, text);
		if (itemInfo != null && itemInfo.DisableBarges && TCUpgradeHelpers.IsOnBarge(cup))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
			return;
		}
		if (itemInfo != null && !HasPermission(player.UserIDString, itemInfo.Permission))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		if (result2 > 0 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, result2))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		BuildingGrade.Enum grade = text.ToLower() switch
		{
			"stone" => BuildingGrade.Enum.Stone, 
			"metal" => BuildingGrade.Enum.Metal, 
			"armored" => BuildingGrade.Enum.TopTier, 
			_ => BuildingGrade.Enum.Wood, 
		};
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		orCreateConfig.Id = result;
		orCreateConfig.Grade = grade;
		orCreateConfig.SkinId = result2;
		orCreateConfig.Color = color;
		orCreateConfig.Reskin = !orCreateConfig.Reskin;
		orCreateConfig.Player = player.userID;
		if (orCreateConfig.Reskin)
		{
			orCreateConfig.WorkReskin = ((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(ReskinProgress(player, cup));
		}
		else if (orCreateConfig.WorkReskin != null)
		{
			((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkReskin);
			orCreateConfig.WorkReskin = null;
		}
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		ShowMenu(player, cup, result3);
	}

	private void HandleUpwall(BasePlayer player, BuildingPrivlidge cup, string[] args)
	{
		if (!int.TryParse(args[1], out var result) || !int.TryParse(args[3], out var result2) || !int.TryParse(args[4], out var _))
		{
			return;
		}
		string text = args[2];
		TCUpgradeConfig.ItemInfo itemInfo = TCUpgradeHelpers.GetItemInfo(result, text);
		if (itemInfo != null && itemInfo.DisableBarges && TCUpgradeHelpers.IsOnBarge(cup))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
			return;
		}
		if (itemInfo != null && !HasPermission(player.UserIDString, itemInfo.Permission))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		if (result2 > 0 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, result2))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		BuildingGrade.Enum grade = text.ToLower() switch
		{
			"stone" => BuildingGrade.Enum.Stone, 
			"metal" => BuildingGrade.Enum.Metal, 
			"armored" => BuildingGrade.Enum.TopTier, 
			_ => BuildingGrade.Enum.Wood, 
		};
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		orCreateConfig.Id = result;
		orCreateConfig.Grade = grade;
		orCreateConfig.SkinId = result2;
		orCreateConfig.Upwall = !orCreateConfig.Upwall;
		orCreateConfig.Player = player.userID;
		if (orCreateConfig.Upwall)
		{
			orCreateConfig.WorkUpwall = ((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(ReskinProgressWall(player, cup));
		}
		else if (orCreateConfig.WorkUpwall != null)
		{
			((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkUpwall);
			orCreateConfig.WorkUpwall = null;
		}
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		ShowButtonTC(player, cup);
	}

	private void HandleStop(BasePlayer player, BuildingPrivlidge cup, int page)
	{
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		orCreateConfig.Work = !orCreateConfig.Work;
		if (orCreateConfig.Work)
		{
			orCreateConfig.WorkUpgrade = ((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(UpdateProgress(player, cup));
		}
		else
		{
			if (orCreateConfig.WorkUpgrade != null)
			{
				((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkUpgrade);
				orCreateConfig.WorkUpgrade = null;
			}
			if (orCreateConfig.WorkWallpaper != null)
			{
				((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkWallpaper);
				orCreateConfig.WorkWallpaper = null;
			}
			if (orCreateConfig.WorkUpwall != null)
			{
				((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkUpwall);
				orCreateConfig.WorkUpwall = null;
			}
		}
		ShowMenu(player, cup, page);
	}

	private void HandleCostUpgrade(BasePlayer player, BuildingPrivlidge cup, int id, string gradeStr, int skinId, int page)
	{
		BuildingGrade.Enum grade = gradeStr.ToLower() switch
		{
			"stone" => BuildingGrade.Enum.Stone, 
			"metal" => BuildingGrade.Enum.Metal, 
			"armored" => BuildingGrade.Enum.TopTier, 
			_ => BuildingGrade.Enum.Wood, 
		};
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		orCreateConfig.Id = id;
		orCreateConfig.Grade = grade;
		orCreateConfig.SkinId = skinId;
		((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(UpdateCost(player, cup));
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		ShowButtonTC(player, cup);
	}

	private void StartUpgrade(BasePlayer player, BuildingPrivlidge cup, string[] args)
	{
		if (args.Length < 6 || !int.TryParse(args[1], out var result) || !int.TryParse(args[3], out var result2) || !int.TryParse(args[4], out var _))
		{
			return;
		}
		string text = args[2];
		bool color = args[5] == "1";
		TCUpgradeConfig.ItemInfo itemInfo = TCUpgradeHelpers.GetItemInfo(result, text);
		if (itemInfo != null && itemInfo.DisableBarges && TCUpgradeHelpers.IsOnBarge(cup))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
			return;
		}
		if (itemInfo != null && !HasPermission(player.UserIDString, itemInfo.Permission))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		if (result2 > 0 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, result2))
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, "assets/prefabs/weapons/toolgun/effects/repairerror.prefab", 10f, "danger");
			TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
			return;
		}
		BuildingGrade.Enum grade = text.ToLower() switch
		{
			"stone" => BuildingGrade.Enum.Stone, 
			"metal" => BuildingGrade.Enum.Metal, 
			"armored" => BuildingGrade.Enum.TopTier, 
			_ => BuildingGrade.Enum.Wood, 
		};
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		orCreateConfig.Id = result;
		orCreateConfig.Grade = grade;
		orCreateConfig.SkinId = result2;
		orCreateConfig.Color = color;
		orCreateConfig.Work = !orCreateConfig.Work;
		orCreateConfig.Player = player.userID;
		if (orCreateConfig.Work)
		{
			orCreateConfig.WorkUpgrade = ((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(UpdateProgress(player, cup));
		}
		else if (orCreateConfig.WorkUpgrade != null)
		{
			((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkUpgrade);
			orCreateConfig.WorkUpgrade = null;
		}
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		ShowButtonTC(player, cup);
	}

	private void ToggleRepair(BasePlayer player, BuildingPrivlidge cup)
	{
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		orCreateConfig.Repair = !orCreateConfig.Repair;
		orCreateConfig.Player = player.userID;
		if (orCreateConfig.Repair)
		{
			orCreateConfig.WorkRepair = ((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StartCoroutine(RepairProgress(player, cup));
		}
		else if (orCreateConfig.WorkRepair != null)
		{
			((MonoBehaviour)SingletonComponent<ServerMgr>.Instance).StopCoroutine(orCreateConfig.WorkRepair);
			orCreateConfig.WorkRepair = null;
		}
		ShowButtonTC(player, cup);
	}

	private IEnumerator UpdateCost(BasePlayer player, BuildingPrivlidge cup)
	{
		BuildingManager.Building building = cup?.GetBuilding();
		if (building?.buildingBlocks == null)
		{
			yield break;
		}
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		List<ulong> validBlockOwnersForTC = GetValidBlockOwnersForTC(player, cup, (IEnumerable<BuildingBlock>)building.buildingBlocks);
		Dictionary<ItemDefinition, int> dictionary = new Dictionary<ItemDefinition, int>();
		foreach (BuildingBlock current in building.buildingBlocks)
		{
			if (!validBlockOwnersForTC.Contains(current.OwnerID) || orCreateConfig.Grade == current.grade)
			{
				continue;
			}
			bool num = (TCUpgradeConfig.Config?.Downgrade ?? true) && orCreateConfig.Downgrade;
			bool flag = (ulong)player.userID == current.OwnerID || HasPermission(player.UserIDString, "TCUpgrade.admin");
			if (((!num || ((TCUpgradeConfig.Config?.OnlyOwner ?? true) && !flag)) && orCreateConfig.Grade < current.grade) || ((TCUpgradeConfig.Config?.OnlyOwnerUp ?? true) && !flag && orCreateConfig.Grade > current.grade))
			{
				continue;
			}
			foreach (ItemAmount item in current.blockDefinition.GetGrade(orCreateConfig.Grade, 0uL).CostToBuild())
			{
				if (dictionary.TryGetValue(item.itemDef, out var value))
				{
					dictionary[item.itemDef] = value + (int)item.amount;
				}
				else
				{
					dictionary[item.itemDef] = (int)item.amount;
				}
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<ItemDefinition, int> item2 in dictionary)
		{
			stringBuilder.AppendLine($"{item2.Value} x {item2.Key.shortname}");
		}
		string text = ((dictionary.Count == 0) ? LangHelper.Lang("NoUpgradeAvailable") : LangHelper.Lang("TotalCostUP", stringBuilder.ToString()));
		TCUpgradeHelpers.CreateGameTip(cup, text, player, "assets/prefabs/deployable/research table/effects/research-success.prefab");
	}

	private IEnumerator UpdateProgress(BasePlayer player, BuildingPrivlidge cup)
	{
		BuildingManager.Building building = cup.GetBuilding();
		if (building?.buildingBlocks == null)
		{
			yield break;
		}
		yield return CoroutineEx.waitForSeconds(0.15f);
		TCConfig cfg = GetOrCreateConfig(cup);
		List<ulong> teamMembers = GetValidBlockOwnersForTC(player, cup, (IEnumerable<BuildingBlock>)building.buildingBlocks);
		float cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyUpgrade);
		bool show = true;
		foreach (BuildingBlock current in building.buildingBlocks)
		{
			if ((Object)(object)cup == (Object)null || !cfg.Work)
			{
				show = false;
				break;
			}
			if (teamMembers.Contains(current.OwnerID) && cfg.Grade != current.grade)
			{
				bool num = (TCUpgradeConfig.Config?.Downgrade ?? true) && cfg.Downgrade;
				bool flag = (ulong)player.userID == current.OwnerID || HasPermission(player.UserIDString, "TCUpgrade.admin");
				if (((num && (!(TCUpgradeConfig.Config?.OnlyOwner ?? true) || flag)) || cfg.Grade >= current.grade) && (!(TCUpgradeConfig.Config?.OnlyOwnerUp ?? true) || flag || cfg.Grade <= current.grade))
				{
					UpgradeBlock(cup, current, cfg.Grade, player);
					yield return CoroutineEx.waitForSeconds(cd);
				}
			}
		}
		cfg.Work = false;
		cfg.WorkUpgrade = null;
		if (show)
		{
			TCUpgradeHelpers.CreateGameTip(cup, (teamMembers.Count <= 1 && !HasPermission(player.UserIDString, "TCUpgrade.admin")) ? LangHelper.Lang("UpgradeFinishNoPlayer") : LangHelper.Lang("UpgradeFinish"), player, "assets/prefabs/deployable/research table/effects/research-success.prefab");
		}
		ShowButtonTCIfStillLooting(player, cup);
	}

	private IEnumerator RepairProgress(BasePlayer player, BuildingPrivlidge cup)
	{
		BuildingManager.Building building = cup.GetBuilding();
		if (building?.buildingBlocks == null)
		{
			yield break;
		}
		yield return CoroutineEx.waitForSeconds(0.15f);
		TCConfig cfg = GetOrCreateConfig(cup);
		float costMult = TCUpgradeHelpers.ResourcesRepair(player.UserIDString);
		float cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyRepair);
		List<BaseCombatEntity> list = new List<BaseCombatEntity>((IEnumerable<BaseCombatEntity>)building.buildingBlocks);
		if (TCUpgradeConfig.Config?.Deployables ?? (building.decayEntities != null))
		{
			list.AddRange((IEnumerable<BaseCombatEntity>)building.decayEntities);
		}
		bool show = true;
		bool warned = false;
		float cooldown = TCUpgradeConfig.Config?.RepairCooldown ?? 30f;
		foreach (BaseCombatEntity item in list)
		{
			if (!cfg.Repair)
			{
				show = false;
				break;
			}
			if (item.SecondsSinceAttacked < cooldown)
			{
				if (!warned)
				{
					warned = true;
					TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RepairBlockedRecentDamage", item.ShortPrefabName, (cooldown - item.SecondsSinceAttacked).ToString("0.0")), player, "assets/bundled/prefabs/fx/ore_break.prefab", 10f, "warning");
				}
			}
			else if (RepairBlock(player, item, cup, costMult))
			{
				yield return CoroutineEx.waitForSeconds(cd);
			}
		}
		cfg.Repair = false;
		cfg.WorkRepair = null;
		if (show)
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RepairFinish"), player, "assets/prefabs/deployable/research table/effects/research-success.prefab");
		}
		ShowButtonTCIfStillLooting(player, cup);
	}

	private bool RepairBlock(BasePlayer player, BaseCombatEntity entity, BuildingPrivlidge cup, float costMult)
	{
		if ((Object)(object)entity == (Object)null || !entity.IsValid() || entity.IsDestroyed || !entity.repair.enabled || entity.health >= entity.MaxHealth())
		{
			return false;
		}
		float num = entity.MaxHealth() - entity.health;
		float num2 = num / entity.MaxHealth();
		if (num <= 0f || num2 <= 0f)
		{
			return false;
		}
		List<ItemAmount> list = entity.RepairCost(num2);
		float num3 = 0f;
		for (int i = 0; i < list.Count; i++)
		{
			num3 += list[i].amount;
		}
		if (num3 <= 0f)
		{
			entity.health += num;
			entity.SendNetworkUpdate();
			entity.OnRepairFinished(player);
			return true;
		}
		if (HasPermission(player.UserIDString, "TCUpgrade.repair.nocost"))
		{
			entity.health += num;
			entity.SendNetworkUpdate();
			entity.OnRepairFinished(player);
			return true;
		}
		foreach (ItemAmount item in list)
		{
			int num4 = (int)(item.amount * costMult);
			if (cup.inventory.GetAmount(item.itemid, onlyUsableAmounts: false, redirectAllowed: false) < num4)
			{
				TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesRepairDetail", TCUpgradeHelpers.GetMissingResources(list, cup.inventory)), player, "assets/bundled/prefabs/fx/ore_break.prefab", 10f, "danger");
				GetOrCreateConfig(cup).Repair = false;
				return false;
			}
		}
		foreach (ItemAmount item2 in list)
		{
			cup.inventory.Take(null, item2.itemid, (int)(item2.amount * costMult));
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if ((config == null || config.PlayFx) && GetOrCreateConfig(cup).Effect)
		{
			Effect.server.Run("assets/prefabs/deployable/modular car lift/effects/modular-car-lift-repair.prefab", ((Component)entity).transform.position);
		}
		entity.health += num;
		entity.SendNetworkUpdate();
		if (entity.health >= entity.MaxHealth())
		{
			entity.OnRepairFinished(player);
		}
		else
		{
			entity.OnRepair();
		}
		return true;
	}

	private void UpgradeBlock(BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade, BasePlayer player)
	{
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		if (!HasPermission(player.UserIDString, "TCUpgrade.upgrade.nocost"))
		{
			if (!CanUpgrade(player, cup, block, grade))
			{
				orCreateConfig.Work = false;
				List<ItemAmount> missingList = block.blockDefinition.GetGrade(grade, 0uL).CostToBuild();
				TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesUpgradeDetail", TCUpgradeHelpers.GetMissingResources(missingList, cup.inventory)), player, "assets/bundled/prefabs/fx/ore_break.prefab", 10f, "danger");
				return;
			}
			foreach (ItemAmount item in block.blockDefinition.GetGrade(grade, 0uL).CostToBuild())
			{
				TCUpgradeHelpers.TakeResources(cup.inventory.itemList, item.itemDef.shortname, (int)item.amount);
			}
		}
		string text = block.ShortPrefabName ?? "";
		if (text.Contains("foundation") || text.Contains("floor") || text.Contains("roof") || !TCUpgradeHelpers.CheckBlock(block))
		{
			block.skinID = (ulong)orCreateConfig.SkinId;
			TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
			if ((config == null || config.PlayFx) && orCreateConfig.Effect)
			{
				object strName = grade switch
				{
					BuildingGrade.Enum.Wood => "assets/bundled/prefabs/fx/build/frame_place.prefab", 
					BuildingGrade.Enum.Stone => "assets/bundled/prefabs/fx/build/promote_stone.prefab", 
					BuildingGrade.Enum.Metal => "assets/bundled/prefabs/fx/build/promote_metal.prefab", 
					_ => "assets/bundled/prefabs/fx/build/promote_toptier.prefab", 
				};
				block.ClientRPC(RpcTarget.NetworkGroup("DoUpgradeEffect"), (int)grade, block.skinID);
				Effect.server.Run((string)strName, ((Component)block).transform.position);
			}
			block.SetGrade(grade);
			SyncPlayerCustomColourBeforeSkin(block, orCreateConfig.Color, orCreateConfig.Colour);
			block.UpdateSkin();
			block.SetHealthToMax();
			ApplyPackedColourIfNotCustomDetailSkin(block, orCreateConfig.Color, orCreateConfig.Colour);
			block.SendNetworkUpdateImmediate();
		}
	}

	private bool CanUpgrade(BasePlayer player, BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade)
	{
		foreach (ItemAmount item in block.blockDefinition.GetGrade(grade, 0uL).CostToBuild())
		{
			if ((float)cup.inventory.GetAmount(item.itemid, onlyUsableAmounts: false, redirectAllowed: false) < item.amount)
			{
				return false;
			}
		}
		return true;
	}

	private IEnumerator ReskinProgress(BasePlayer player, BuildingPrivlidge cup)
	{
		BuildingManager.Building building = cup.GetBuilding();
		if (building?.buildingBlocks == null)
		{
			yield break;
		}
		yield return CoroutineEx.waitForSeconds(0.15f);
		TCConfig cfg = GetOrCreateConfig(cup);
		float cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyReskin);
		bool show = true;
		foreach (BuildingBlock current in building.buildingBlocks)
		{
			if ((Object)(object)cup == (Object)null || !cfg.Reskin)
			{
				show = false;
				break;
			}
			if (cfg.Grade == current.grade && ((ulong)cfg.SkinId != current.skinID || (cfg.Color && !CustomColoursMatchForBlock(current, cfg))))
			{
				ReskinBlock(cup, current, cfg.Grade, player);
				yield return CoroutineEx.waitForSeconds(cd);
			}
		}
		cfg.Reskin = false;
		cfg.WorkReskin = null;
		if (show)
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("ReskinFinish"), player, "assets/prefabs/deployable/research table/effects/research-success.prefab");
		}
		ShowButtonTCIfStillLooting(player, cup);
	}

	private void ReskinBlock(BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade, BasePlayer player)
	{
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		if (!HasPermission(player.UserIDString, "TCUpgrade.reskin.nocost") && !CanUpgrade(player, cup, block, grade))
		{
			orCreateConfig.Reskin = false;
			List<ItemAmount> missingList = block.blockDefinition.GetGrade(grade, 0uL).CostToBuild();
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesReskinDetail", TCUpgradeHelpers.GetMissingResources(missingList, cup.inventory)), player, "assets/bundled/prefabs/fx/ore_break.prefab", 10f, "danger");
			return;
		}
		string text = block.ShortPrefabName ?? "";
		if (!text.Contains("foundation") && !text.Contains("floor") && !text.Contains("roof") && TCUpgradeHelpers.CheckBlock(block))
		{
			return;
		}
		if (!HasPermission(player.UserIDString, "TCUpgrade.reskin.nocost"))
		{
			foreach (ItemAmount item in block.blockDefinition.GetGrade(grade, 0uL).CostToBuild())
			{
				TCUpgradeHelpers.TakeResources(cup.inventory.itemList, item.itemDef.shortname, (int)item.amount);
			}
		}
		block.skinID = (ulong)orCreateConfig.SkinId;
		SyncPlayerCustomColourBeforeSkin(block, orCreateConfig.Color, orCreateConfig.Colour);
		block.UpdateSkin();
		ApplyPackedColourIfNotCustomDetailSkin(block, orCreateConfig.Color, orCreateConfig.Colour);
		block.SendNetworkUpdateImmediate();
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if ((config == null || config.PlayFx) && orCreateConfig.Effect)
		{
			Effect.server.Run("assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab", ((Component)block).transform.position);
			Effect.server.Run("assets/prefabs/tools/spraycan/reskineffect.prefab", ((Component)block).transform.position);
		}
	}

	private IEnumerator ReskinProgressWall(BasePlayer player, BuildingPrivlidge cup)
	{
		if ((Object)(object)cup == (Object)null || (Object)(object)player == (Object)null || !_buildingCupboard.ContainsKey(cup))
		{
			yield break;
		}
		Vector3 position = ((Component)cup).transform.position;
		float radius = TCUpgradeConfig.Config?.UpwallDis ?? 100f;
		float delay = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyReskin);
		TCConfig cfg = GetOrCreateConfig(cup);
		List<ulong> validOwners = GetValidBlockOwnersForTC(player, cup, (IEnumerable<BuildingBlock>)((cup?.GetBuilding())?.buildingBlocks));
		bool flag = false;
		List<BaseEntity> nearbyWalls = Pool.Get<List<BaseEntity>>();
		Vis.Entities(position, radius, nearbyWalls, LayerMask.GetMask(new string[1] { "Construction" }), (QueryTriggerInteraction)2);
		foreach (BaseEntity item in nearbyWalls)
		{
			if (!cfg.Upwall)
			{
				flag = false;
				break;
			}
			if ((Object)(object)item == (Object)null || item.ShortPrefabName == null || (!item.ShortPrefabName.Contains("wall.external") && !item.ShortPrefabName.Contains("gates.external.high")) || !validOwners.Contains(item.OwnerID))
			{
				continue;
			}
			string targetPrefab = TCUpgradeHelpers.GetTargetPrefab(item.ShortPrefabName, cfg.SkinId);
			if (targetPrefab == null || targetPrefab == item.PrefabName)
			{
				continue;
			}
			if (TCUpgradeConfig.Config?.SameWallGrade ?? true)
			{
				TCUpgradeHelpers.WallMaterialType wallType = TCUpgradeHelpers.GetWallType(item.ShortPrefabName);
				TCUpgradeHelpers.WallMaterialType wallType2 = TCUpgradeHelpers.GetWallType(targetPrefab);
				if (!TCUpgradeHelpers.CanChangeWall(wallType, wallType2))
				{
					continue;
				}
			}
			ReskinWall(cup, item, player);
			yield return CoroutineEx.waitForSeconds(delay);
			flag = true;
		}
		Pool.FreeUnmanaged<BaseEntity>(ref nearbyWalls);
		cfg.Upwall = false;
		cfg.WorkUpwall = null;
		if (flag)
		{
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("ReskinWallFinish"), player, "assets/prefabs/deployable/research table/effects/research-success.prefab");
		}
		ShowButtonTCIfStillLooting(player, cup);
	}

	private void ReskinWall(BuildingPrivlidge cup, BaseEntity wall, BasePlayer player)
	{
		Vector3 position = ((Component)wall).transform.position;
		Quaternion rotation = ((Component)wall).transform.rotation;
		ulong ownerID = wall.OwnerID;
		string targetPrefab = TCUpgradeHelpers.GetTargetPrefab(wall.ShortPrefabName, GetOrCreateConfig(cup).SkinId);
		if (string.IsNullOrEmpty(targetPrefab))
		{
			return;
		}
		BaseEntity baseEntity = GameManager.server.CreateEntity(targetPrefab, position, rotation);
		if (!((Object)(object)baseEntity == (Object)null))
		{
			if (baseEntity.ShortPrefabName == "wall.external.high.legacy")
			{
				((MonoBehaviour)baseEntity).Invoke("PopulateVariants", 0f);
			}
			baseEntity.skinID = 0uL;
			baseEntity.OwnerID = ownerID;
			baseEntity.Spawn();
			if (baseEntity is BaseCombatEntity baseCombatEntity && wall is BaseCombatEntity baseCombatEntity2)
			{
				baseCombatEntity.health = baseCombatEntity2.health;
				baseCombatEntity.lastAttackedTime = 0f;
			}
			TCUpgradeHelpers.CopyLock(wall, baseEntity);
			wall.Kill();
			TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
			if ((config == null || config.PlayFx) && GetOrCreateConfig(cup).Effect)
			{
				Effect.server.Run("assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab", position);
				Effect.server.Run("assets/prefabs/building/wall.external.high.stone/effects/wall-external-stone-deploy.prefab", position);
			}
		}
	}

	private List<ulong> GetTeamMembers(BasePlayer player)
	{
		List<ulong> list = new List<ulong> { player.userID };
		if (player.currentTeam != 0L)
		{
			RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
			if (playerTeam?.members != null)
			{
				list.AddRange(playerTeam.members);
			}
		}
		return list;
	}

	private List<ulong> GetValidBlockOwnersForTC(BasePlayer player, BuildingPrivlidge cup, IEnumerable<BuildingBlock> buildingBlocks)
	{
		if (HasPermission(player.UserIDString, "TCUpgrade.admin"))
		{
			if (buildingBlocks == null)
			{
				return new List<ulong> { player.userID };
			}
			List<ulong> list = buildingBlocks.Select((BuildingBlock b) => b.OwnerID).Distinct().ToList();
			if (list.Count == 0)
			{
				return new List<ulong> { player.userID };
			}
			return list;
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config == null || !config.TeamUpdate)
		{
			return new List<ulong> { player.userID };
		}
		return GetTeamMembers(player);
	}

	private BuildingPrivlidge GetPlayerTC(BasePlayer player)
	{
		if (Physics.Raycast(player.transform.position, Vector3.down, out var hit, 2f))
			return hit.collider?.GetComponentInParent<BuildingBlock>()?.GetBuildingPrivilege();
		return null;
	}

	public TCSkin GetPlayerSelectedSkin(ulong userId)
	{
		if (!_playerSelectedSkins.TryGetValue(userId, out var value))
		{
			return TCSkin.Default;
		}
		return value;
	}

	public void SetPlayerSelectedSkin(ulong userId, TCSkin skin)
	{
		_playerSelectedSkins[userId] = skin;
	}

	public void TCSkinReplace(BuildingPrivlidge tc, BasePlayer player, TCSkin skin)
	{
		if (!TCSkinMeta.Data.TryGetValue(skin, out var meta))
		{
			return;
		}
		Vector3 position = ((Component)tc).transform.position;
		Quaternion rotation = ((Component)tc).transform.rotation;
		BaseEntity newTc = GameManager.server.CreateEntity(meta.PrefabPath, position, rotation);
		if ((Object)(object)newTc == (Object)null)
		{
			return;
		}
		newTc.OwnerID = tc.OwnerID;
		newTc.Spawn();
		NextTick(delegate
		{
			try
			{
				BuildingPrivlidge buildingPrivlidge = newTc as BuildingPrivlidge;
				if (!((Object)(object)buildingPrivlidge == (Object)null))
				{
					if (tc.HasParent())
					{
						BaseEntity parentEntity = tc.GetParentEntity();
						if ((Object)(object)parentEntity != (Object)null && !parentEntity.IsDestroyed)
						{
							newTc.SetParent(parentEntity, worldPositionStays: true);
						}
					}
					foreach (ulong authorizedPlayer in tc.authorizedPlayers)
					{
						buildingPrivlidge.authorizedPlayers.Add(authorizedPlayer);
					}
					buildingPrivlidge.AttachToBuilding(tc.buildingID);
					buildingPrivlidge.BuildingDirty();
					buildingPrivlidge.SendNetworkUpdate();
					UpdateBlockedItems(buildingPrivlidge);
					if (tc.inventory != null && buildingPrivlidge.inventory != null)
					{
						List<Item> list = new List<Item>(tc.inventory.itemList);
						for (int i = 0; i < list.Count; i++)
						{
							Item item = list[i];
							Item item2 = ItemManager.CreateByItemID(item.info.itemid, item.amount, item.skin);
							if (item2 != null)
							{
								item2.condition = item.condition;
								item2.maxCondition = item.maxCondition;
								item2.MoveToContainer(buildingPrivlidge.inventory);
							}
						}
					}
					TCUpgradeHelpers.CopyLock(tc, buildingPrivlidge);
					Effect.server.Run(meta.EffectPath, ((Component)newTc).transform.position);
					if (tc.inventory != null)
					{
						AccessTools.Method(typeof(ItemContainer), "Clear")?.Invoke(tc.inventory, null);
					}
					tc.Kill();
					newTc.UpdateNetworkGroup();
					newTc.SendNetworkUpdateImmediate();
				}
			}
			catch (Exception arg)
			{
				Debug.LogError((object)$"[TCUpgrade] TCSkinReplace error: {arg}");
			}
		});
	}

	private void NextTick(Action action)
	{
		ServerMgr instance = SingletonComponent<ServerMgr>.Instance;
		if (instance != null)
		{
			((MonoBehaviour)instance).StartCoroutine(NextTickCoroutine(action));
		}
	}

	private IEnumerator NextTickCoroutine(Action action)
	{
		yield return null;
		try
		{
			action?.Invoke();
		}
		catch (Exception arg)
		{
			Debug.LogError((object)$"[TCUpgrade] NextTick error: {arg}");
		}
	}

	private void CmdWphammer(ConsoleSystem.Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		string[] array = GetArgStrings(arg);
		if ((Object)(object)basePlayer != (Object)null)
		{
			if (!HasPermission(basePlayer.UserIDString, "TCUpgrade.admin"))
			{
				arg.ReplyWith("No permission.");
			}
			else
			{
				GiveWallpaperHammer(basePlayer);
			}
		}
		else if (array.Length >= 1)
		{
			BasePlayer basePlayer2 = BasePlayer.Find(array[0]) ?? BasePlayer.FindAwakeOrSleeping(array[0]);
			if ((Object)(object)basePlayer2 != (Object)null)
			{
				GiveWallpaperHammer(basePlayer2);
				arg.ReplyWith("Wallpaper hammer given to " + basePlayer2.displayName + ".");
			}
			else
			{
				arg.ReplyWith("Player not found.");
			}
		}
	}

	private void CmdAddwp(ConsoleSystem.Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		string[] array = GetArgStrings(arg);
		if ((Object)(object)basePlayer == (Object)null)
		{
			return;
		}
		if (!HasPermission(basePlayer.UserIDString, "TCUpgrade.admin"))
		{
			basePlayer.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_NoPermission"));
			return;
		}
		if (array.Length != 2 || !ulong.TryParse(array[0], out var result))
		{
			basePlayer.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_Usage"));
			return;
		}
		string text = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(array[1].Trim().ToLower());
		if (text != "Wall" && text != "Floor" && text != "Ceiling")
		{
			basePlayer.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_InvalidCategory"));
			return;
		}
		if (!_data.CustomWallpapers.ContainsKey(text))
		{
			_data.CustomWallpapers[text] = new HashSet<ulong>();
		}
		if (_data.CustomWallpapers[text].Add(result))
		{
			_data.Save();
			basePlayer.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_Added", result, text));
		}
		else
		{
			basePlayer.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_AlreadyExists"));
		}
	}

	private void CmdOpenBoatWallpaper(ConsoleSystem.Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!((Object)(object)basePlayer == (Object)null))
		{
			TryOpenBoatWallpaper(basePlayer);
		}
	}

	public bool RunChatCommand(BasePlayer player, string cmd, string[] args)
	{
		if ((Object)(object)player == (Object)null || string.IsNullOrEmpty(cmd))
		{
			return false;
		}
		if (cmd != "wpb")
		{
			return false;
		}
		TryOpenBoatWallpaper(player);
		return true;
	}

	private bool TryOpenBoatWallpaper(BasePlayer player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return false;
		}
		if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper"))
		{
			player.ChatMessage(LangHelper.Lang("UpgradeLock"));
			return false;
		}
		SteeringWheel steeringWheel = player.GetMounted() as SteeringWheel;
		if ((Object)(object)steeringWheel == (Object)null)
		{
			player.ChatMessage(LangHelper.Lang("NotOnBoat"));
			return false;
		}
		BaseEntity parentEntity = steeringWheel.GetParentEntity();
		if ((Object)(object)parentEntity == (Object)null)
		{
			player.ChatMessage(LangHelper.Lang("NotOnBoat"));
			return false;
		}
		if (!_playerBoatWallpaper.TryGetValue(player.userID, out var value))
		{
			value = new BoatWallpaperConfig();
			_playerBoatWallpaper[player.userID] = value;
		}
		value.Boat = parentEntity;
		BoatWallpaperConfig boatWallpaperConfig = value;
		if (boatWallpaperConfig.Category == null)
		{
			boatWallpaperConfig.Category = "Wall";
		}
		if (value.Page < 0)
		{
			value.Page = 0;
		}
		DestroyBoatWallpaperUi(player);
		ShowMenuBoatWallpaper(player, value.Page, value.Category);
		return true;
	}

	private void GiveWallpaperHammer(BasePlayer player)
	{
		Item item = ItemManager.CreateByName("hammer", 1, 3494416562uL);
		if (item != null)
		{
			player.GiveItem(item);
		}
	}

	public void StopCoroutinesForPlayer(ulong userId)
	{
		foreach (KeyValuePair<BuildingPrivlidge, TCConfig> item in _buildingCupboard)
		{
			if (item.Value.Player != userId)
			{
				continue;
			}
			if (item.Value.WorkUpgrade != null)
			{
				ServerMgr instance = SingletonComponent<ServerMgr>.Instance;
				if (instance != null)
				{
					((MonoBehaviour)instance).StopCoroutine(item.Value.WorkUpgrade);
				}
				item.Value.WorkUpgrade = null;
			}
			if (item.Value.WorkRepair != null)
			{
				ServerMgr instance2 = SingletonComponent<ServerMgr>.Instance;
				if (instance2 != null)
				{
					((MonoBehaviour)instance2).StopCoroutine(item.Value.WorkRepair);
				}
				item.Value.WorkRepair = null;
			}
			if (item.Value.WorkReskin != null)
			{
				ServerMgr instance3 = SingletonComponent<ServerMgr>.Instance;
				if (instance3 != null)
				{
					((MonoBehaviour)instance3).StopCoroutine(item.Value.WorkReskin);
				}
				item.Value.WorkReskin = null;
			}
			if (item.Value.WorkWallpaper != null)
			{
				ServerMgr instance4 = SingletonComponent<ServerMgr>.Instance;
				if (instance4 != null)
				{
					((MonoBehaviour)instance4).StopCoroutine(item.Value.WorkWallpaper);
				}
				item.Value.WorkWallpaper = null;
			}
			if (item.Value.WorkUpwall != null)
			{
				ServerMgr instance5 = SingletonComponent<ServerMgr>.Instance;
				if (instance5 != null)
				{
					((MonoBehaviour)instance5).StopCoroutine(item.Value.WorkUpwall);
				}
				item.Value.WorkUpwall = null;
			}
			break;
		}
		if (_playerBoatWallpaper.TryGetValue(userId, out var value) && value.WorkWallpaper != null)
		{
			ServerMgr instance6 = SingletonComponent<ServerMgr>.Instance;
			if (instance6 != null)
			{
				((MonoBehaviour)instance6).StopCoroutine(value.WorkWallpaper);
			}
			value.WorkWallpaper = null;
			value.Work = false;
		}
	}

	private bool IsRaidBlocked(BasePlayer player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return false;
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config == null || !config.UseNoEscape)
		{
			TCUpgradeConfig.ConfigData config2 = TCUpgradeConfig.Config;
			if (config2 == null || !config2.UseRaidBlock)
			{
				return false;
			}
		}
		try
		{
			Type type = _cachedOxideModType;
			if (type == null)
			{
				type = Type.GetType("Oxide.Core.OxideMod, Oxide.Core");
				if (type == null)
				{
					Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
					for (int i = 0; i < assemblies.Length; i++)
					{
						type = assemblies[i].GetType("Oxide.Core.OxideMod");
						if (type != null)
						{
							break;
						}
					}
				}
				if (type != null)
				{
					_cachedOxideModType = type;
				}
			}
			if (type == null)
			{
				return false;
			}
			object obj = _cachedOxideModInstance;
			if (obj == null)
			{
				obj = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
				if (obj != null)
				{
					_cachedOxideModInstance = obj;
				}
			}
			if (obj == null)
			{
				return false;
			}
			MethodInfo method = type.GetMethod("GetPlugin", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(string) }, null);
			if (method == null)
			{
				return false;
			}
			TCUpgradeConfig.ConfigData config3 = TCUpgradeConfig.Config;
			object obj2 = ((config3 != null && config3.UseNoEscape) ? method.Invoke(obj, new object[1] { "NoEscape" }) : null);
			TCUpgradeConfig.ConfigData config4 = TCUpgradeConfig.Config;
			object obj3 = ((config4 != null && config4.UseRaidBlock) ? method.Invoke(obj, new object[1] { "RaidBlock" }) : null);
			if (obj2 != null)
			{
				MethodInfo method2 = obj2.GetType().GetMethod("Call", new Type[2]
				{
					typeof(string),
					typeof(object[])
				});
				bool flag = default(bool);
				int num;
				if (method2 != null)
				{
					object obj4 = method2.Invoke(obj2, new object[2]
					{
						"IsRaidBlocked",
						new object[1] { player.UserIDString }
					});
					if (obj4 is bool)
					{
						flag = (bool)obj4;
						num = 1;
					}
					else
					{
						num = 0;
					}
				}
				else
				{
					num = 0;
				}
				if (((uint)num & (flag ? 1u : 0u)) != 0)
				{
					return true;
				}
			}
			if (obj3 != null)
			{
				MethodInfo method3 = obj3.GetType().GetMethod("Call", new Type[2]
				{
					typeof(string),
					typeof(object[])
				});
				bool flag2 = default(bool);
				int num2;
				if (method3 != null)
				{
					object obj4 = method3.Invoke(obj3, new object[2]
					{
						"IsRaidBlocked",
						new object[1] { player.UserIDString }
					});
					if (obj4 is bool)
					{
						flag2 = (bool)obj4;
						num2 = 1;
					}
					else
					{
						num2 = 0;
					}
				}
				else
				{
					num2 = 0;
				}
				if (((uint)num2 & (flag2 ? 1u : 0u)) != 0)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private IEnumerator WallpaperProgress(BasePlayer player, BuildingPrivlidge cup, string category)
	{
		BuildingManager.Building building = cup.GetBuilding();
		if (building?.buildingBlocks == null)
		{
			yield break;
		}
		yield return CoroutineEx.waitForSeconds(0.15f);
		TCConfig cfg = GetOrCreateConfig(cup);
		List<ulong> teamMembers = GetValidBlockOwnersForTC(player, cup, (IEnumerable<BuildingBlock>)building.buildingBlocks);
		float cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyWallpaper);
		bool show = true;
		BuildingGrade.Enum grade = cfg.Grade;
		ulong wallpaperId = cfg.WallpaperId;
		bool isCeiling = category == "Ceiling";
		_ = category == "Floor";
		foreach (BuildingBlock current in building.buildingBlocks)
		{
			if ((Object)(object)cup == (Object)null || !cfg.Work)
			{
				show = false;
				break;
			}
			if (!teamMembers.Contains(current.OwnerID))
			{
				continue;
			}
			if (category == "Wall")
			{
				bool flag = cfg.WpInternal;
				bool wpExternal = cfg.WpExternal;
				if (!flag && !wpExternal)
				{
					flag = true;
				}
				bool num = !flag || (current.wallpaperID == wallpaperId && current.wallpaperHealth != -1f);
				bool flag2 = !wpExternal || (current.wallpaperID2 == wallpaperId && current.wallpaperHealth2 != -1f);
				if (num && flag2)
				{
					continue;
				}
			}
			else
			{
				ulong num2 = (isCeiling ? current.wallpaperID2 : current.wallpaperID);
				float num3 = (isCeiling ? current.wallpaperHealth2 : current.wallpaperHealth);
				if (num2 == wallpaperId && num3 != -1f)
				{
					continue;
				}
			}
			if ((grade == current.grade || cfg.Wallpall) && (!(category == "Wall") || (current.ShortPrefabName.Contains("wall") && !current.ShortPrefabName.Contains("wall.frame"))) && (!(category == "Floor") || current.ShortPrefabName.Contains("floor") || current.ShortPrefabName.Contains("foundation") || current.ShortPrefabName.Contains("hull")) && (!(category == "Ceiling") || current.ShortPrefabName.Contains("floor") || current.ShortPrefabName.Contains("roof")) && ((ulong)cfg.SkinId == current.skinID || cfg.Wallpall))
			{
				WallpaperBlock(cup, current, player, category);
				yield return CoroutineEx.waitForSeconds(cd);
			}
		}
		cfg.Work = false;
		cfg.WorkWallpaper = null;
		if (show)
		{
			TCUpgradeHelpers.CreateGameTip(cup, (teamMembers.Count <= 1 && !HasPermission(player.UserIDString, "TCUpgrade.admin")) ? LangHelper.Lang("WallpaperFinishNoPlayer") : LangHelper.Lang("WallpaperFinish"), player, "assets/prefabs/deployable/research table/effects/research-success.prefab");
		}
		ShowButtonTCIfStillLooting(player, cup);
	}

	private void WallpaperBlock(BuildingPrivlidge cup, BuildingBlock block, BasePlayer player, string category)
	{
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		int clothCost = TCUpgradeConfig.Config?.WallResource ?? 5;
		if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper.nocost") && !CanWallpaper(player, cup))
		{
			orCreateConfig.Work = false;
			int haveCloth = cup.inventory.GetAmount(ClothItemId, onlyUsableAmounts: false, redirectAllowed: false);
			TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesWallpaperDetail", clothCost, haveCloth), player, "assets/bundled/prefabs/fx/ore_break.prefab", 10f, "danger");
			return;
		}
		string text = block.ShortPrefabName ?? "";
		if (!text.Contains("foundation") && !text.Contains("floor") && !text.Contains("roof") && TCUpgradeHelpers.CheckBlock(block))
		{
			return;
		}
		ulong wallpaperId = orCreateConfig.WallpaperId;
		if (wallpaperId == 1)
		{
			bool flag = orCreateConfig.WpInternal;
			bool wpExternal = orCreateConfig.WpExternal;
			if (!flag && !wpExternal)
			{
				flag = true;
			}
			if (flag)
			{
				block.RemoveWallpaper(0);
			}
			if (wpExternal)
			{
				block.RemoveWallpaper(1);
			}
		}
		else
		{
			if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper.nocost"))
			{
				TCUpgradeHelpers.TakeResources(cup.inventory.itemList, "cloth", clothCost);
			}
			switch (category)
			{
			case "Wall":
			{
				bool flag2 = orCreateConfig.WpInternal;
				bool wpExternal2 = orCreateConfig.WpExternal;
				if (!flag2 && !wpExternal2)
				{
					flag2 = true;
				}
				if (flag2)
				{
					block.SetWallpaper(wallpaperId);
				}
				if ((TCUpgradeConfig.Config?.BothSides ?? true) && wpExternal2)
				{
					block.SetWallpaper(wallpaperId, 1);
				}
				break;
			}
			case "Floor":
				if (block.ShortPrefabName.Contains("foundation") || block.ShortPrefabName.Contains("hull"))
				{
					block.SetWallpaper(wallpaperId);
				}
				else
				{
					block.SetWallpaper(wallpaperId, 1);
				}
				break;
			case "Ceiling":
				block.SetWallpaper(wallpaperId);
				break;
			}
		}
		TCUpgradeHelpers.ApplyWallpaperProtection(block, TCUpgradeConfig.Config?.WallpaperDamage ?? true);
		if (TCUpgradeConfig.Config?.PlayFx ?? orCreateConfig.Effect)
		{
			Effect.server.Run("assets/prefabs/wallpaper/effects/place.prefab", ((Component)block).transform.position);
		}
	}

	private bool CanWallpaper(BasePlayer player, BuildingPrivlidge cup)
	{
		return cup.inventory.GetAmount(-858312878, onlyUsableAmounts: false, redirectAllowed: false) >= (TCUpgradeConfig.Config?.WallResource ?? 5);
	}

	private bool CanWallpaperBoat(BasePlayer player)
	{
		return player?.inventory?.GetAmount(-858312878) >= (TCUpgradeConfig.Config?.WallResource ?? 5);
	}

	private IEnumerator BoatWallpaperProgress(BasePlayer player, BoatWallpaperConfig cfg, string category)
	{
		if ((Object)(object)cfg?.Boat == (Object)null || cfg.Boat.IsDestroyed)
		{
			yield break;
		}
		List<BuildingBlock> buildingBlocks = new List<BuildingBlock>();
		if (cfg.Boat.children != null)
		{
			foreach (BaseEntity child in cfg.Boat.children)
			{
				if (child is BuildingBlock item)
				{
					buildingBlocks.Add(item);
				}
			}
		}
		yield return CoroutineEx.waitForSeconds(0.15f);
		float cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyWallpaper);
		bool show = true;
		ulong wallpaperId = cfg.WallpaperId;
		bool isCeiling = category == "Ceiling";
		foreach (BuildingBlock item2 in buildingBlocks)
		{
			if (!cfg.Work || (Object)(object)cfg.Boat == (Object)null || cfg.Boat.IsDestroyed)
			{
				show = false;
				break;
			}
			if (category == "Wall")
			{
				bool flag = cfg.WpInternal;
				bool wpExternal = cfg.WpExternal;
				if (!flag && !wpExternal)
				{
					flag = true;
				}
				bool num = !flag || (item2.wallpaperID == wallpaperId && item2.wallpaperHealth != -1f);
				bool flag2 = !wpExternal || (item2.wallpaperID2 == wallpaperId && item2.wallpaperHealth2 != -1f);
				if (num && flag2)
				{
					continue;
				}
			}
			else
			{
				ulong num2 = (isCeiling ? item2.wallpaperID2 : item2.wallpaperID);
				float num3 = (isCeiling ? item2.wallpaperHealth2 : item2.wallpaperHealth);
				if (num2 == wallpaperId && num3 != -1f)
				{
					continue;
				}
			}
			if ((!(category == "Wall") || (item2.ShortPrefabName.Contains("wall") && !item2.ShortPrefabName.Contains("wall.frame"))) && (!(category == "Floor") || item2.ShortPrefabName.Contains("floor") || item2.ShortPrefabName.Contains("hull")) && (!(category == "Ceiling") || item2.ShortPrefabName.Contains("floor") || item2.ShortPrefabName.Contains("roof")))
			{
				BoatWallpaperBlock(cfg, item2, player, category);
				yield return CoroutineEx.waitForSeconds(cd);
			}
		}
		cfg.Work = false;
		cfg.WorkWallpaper = null;
		if (show)
		{
			TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("WallpaperFinish"), player, "assets/prefabs/deployable/research table/effects/research-success.prefab");
		}
		if (_playerBoatWallpaper.ContainsKey(player.userID))
		{
			UpdateBoatWallpaperControls(player, cfg.Page, cfg.Category);
		}
	}

	private void BoatWallpaperBlock(BoatWallpaperConfig cfg, BuildingBlock block, BasePlayer player, string category)
	{
		int clothCost = TCUpgradeConfig.Config?.WallResource ?? 5;
		if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper.nocost") && !CanWallpaperBoat(player))
		{
			cfg.Work = false;
			int haveClothBoat = player.inventory?.GetAmount(ClothItemId) ?? 0;
			TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("NoResourcesWallpaperBoatDetail", clothCost, haveClothBoat), player, "assets/bundled/prefabs/fx/ore_break.prefab", 10f, "danger");
			return;
		}
		ulong wallpaperId = cfg.WallpaperId;
		if (wallpaperId == 1)
		{
			bool flag = cfg.WpInternal;
			bool wpExternal = cfg.WpExternal;
			if (!flag && !wpExternal)
			{
				flag = true;
			}
			if (flag)
			{
				block.RemoveWallpaper(0);
			}
			if (wpExternal)
			{
				block.RemoveWallpaper(1);
			}
		}
		else
		{
			if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper.nocost"))
			{
				player.inventory.Take(null, -858312878, clothCost);
			}
			switch (category)
			{
			case "Wall":
			{
				bool flag2 = cfg.WpInternal;
				bool wpExternal2 = cfg.WpExternal;
				if (!flag2 && !wpExternal2)
				{
					flag2 = true;
				}
				if (flag2)
				{
					block.SetWallpaper(wallpaperId);
				}
				if ((TCUpgradeConfig.Config?.BothSides ?? true) && wpExternal2)
				{
					block.SetWallpaper(wallpaperId, 1);
				}
				break;
			}
			case "Floor":
				if (block.ShortPrefabName.Contains("foundation") || block.ShortPrefabName.Contains("hull"))
				{
					block.SetWallpaper(wallpaperId);
				}
				else
				{
					block.SetWallpaper(wallpaperId, 1);
				}
				break;
			case "Ceiling":
				block.SetWallpaper(wallpaperId);
				break;
			}
		}
		TCUpgradeHelpers.ApplyWallpaperProtection(block, TCUpgradeConfig.Config?.WallpaperDamage ?? true);
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config == null || config.PlayFx)
		{
			Effect.server.Run("assets/prefabs/wallpaper/effects/place.prefab", ((Component)block).transform.position);
		}
	}

	private (int itemId, List<ulong> skinIds) GetWallpaperItems(BasePlayer player, string category)
	{
		List<ulong> list = new List<ulong> { 1uL };
		int item = category switch
		{
			"Wall" => 553967074, 
			"Floor" => -551431036, 
			"Ceiling" => 1730664641, 
			_ => 0, 
		};
		ItemDefinition itemDefinition = category switch
		{
			"Wall" => WallpaperSettings.WallpaperItemDef, 
			"Floor" => WallpaperSettings.FlooringItemDef, 
			"Ceiling" => WallpaperSettings.CeilingItemDef, 
			_ => null, 
		};
		if (HasPermission(player.UserIDString, "TCUpgrade.wallpaper.custom") && _data.CustomWallpapers.TryGetValue(category, out var value))
		{
			foreach (ulong item3 in value)
			{
				if (!list.Contains(item3))
				{
					list.Add(item3);
				}
			}
		}
		list.Add(0uL);
		if (itemDefinition?.skins != null)
		{
			ItemSkinDirectory.Skin[] skins = itemDefinition.skins;
			for (int i = 0; i < skins.Length; i++)
			{
				int num = skins[i].id;
				if (TCUpgradeHelpers.IsWallpaperAllowed(player, num))
				{
					ulong item2 = (ulong)num;
					if (!list.Contains(item2))
					{
						list.Add(item2);
					}
				}
			}
		}
		return (itemId: item, skinIds: list);
	}

	private void ShowMenuWallpaper(BasePlayer player, BuildingPrivlidge cup, int page, string category = "Wall")
	{
		CUIHelper.DestroyUi(player, "TCUpgrade.color");
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		List<JObject> list = new List<JObject>();
		list.Add(CUIHelper.Panel("TCUpgrade.color", "OverlayNonScaled", "0.04 0.05 0.06 0.92", "0.5 0.5", "0.5 0.5", "-300 -240", "300 240", needsCursor: true));
		list.Add(CUIHelper.Label("wp_title", "TCUpgrade.color", LangHelper.Lang("title5"), 14, "0.05 0.93", "0.92 0.98", "1 1 1 0.9"));
		list.AddRange(CUIHelper.Button("wp_close", "TCUpgrade.color", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 12, "0.92 0.93", "0.98 0.98", string.Format("{0}CLOSE2 {1}", "cui.endtest SENDCMD ", page)));
		float num = 0.28f;
		float num2 = 0.055f;
		float num3 = 0.04f;
		float num4 = 0.86f;
		float num5 = 0.02f;
		for (int i = 0; i < 3; i++)
		{
			string text = (new string[3] { "Wall", "Floor", "Ceiling" })[i];
			bool flag = text == category;
			float num6 = num3 + (float)i * (num + num5);
			float num7 = num6 + num;
			list.AddRange(CUIHelper.Button("wp_cat_" + text, "TCUpgrade.color", flag ? "0.8 1 0.5 0.6" : "0.2 0.3 0.2 0.6", text.ToUpper(), 11, $"{num6} {num4}", $"{num7} {num4 + num2}", string.Format("{0}WALLPAPER 0 wood 0 {1} {2}", "cui.endtest SENDCMD ", page, text)));
		}
		(int itemId, List<ulong> skinIds) wallpaperItems = GetWallpaperItems(player, category);
		int item = wallpaperItems.itemId;
		List<ulong> item2 = wallpaperItems.skinIds;
		int num8 = 4;
		int num9 = 10;
		int num10 = 82;
		int num11 = 82;
		int num12 = (int)Math.Ceiling((double)item2.Count / (double)num8) * (num11 + num9);
		int num13 = num8 * num10 + (num8 - 1) * num9;
		float num14 = num3;
		float num15 = num3 + 3f * (num + num5) - num5;
		list.Add(CUIHelper.ScrollView("wp_scroll", "TCUpgrade.color", $"{num14:F2} 0.12", $"{num15:F2} 0.84", "0 0", "0 0", num12, num13));
		string parent = "wp_scroll___Content";
		int num16 = -num13 / 2;
		int num17 = num12 / 2 - num9;
		int num18 = 0;
		int num19 = 0;
		for (int j = 0; j < item2.Count; j++)
		{
			ulong num20 = item2[j];
			int listX = num16 + num19 * (num10 + num9);
			int listY = num17 - num18 * (num11 + num9);
			list.AddRange(BuildWallpaperTile("building", parent, item, num20, orCreateConfig.WallpaperId == num20, string.Format("{0}WALLPAPERSELECT 0 wood 0 {1} {2} {3}", "cui.endtest SENDCMD ", page, num20, category), listX, listY, num10, num11));
			num19++;
			if (num19 >= num8)
			{
				num19 = 0;
				num18++;
			}
		}
		list.AddRange(BuildBuildingWallpaperControls(cup, page, category));
		CUIHelper.AddUi(player, list);
	}

	private List<JObject> BuildWallpaperTile(string prefix, string parent, int itemId, ulong skinId, bool selected, string command, int listX, int listY, int listSizeX, int listSizeY)
	{
		string text = $"{prefix}_wallpaper_{skinId}";
		List<JObject> list = new List<JObject> { CUIHelper.Panel(text, parent, selected ? "1 1 1 0.7" : "0.2 0.3 0.2 0.6", "0.5 0.5", "0.5 0.5", $"{listX} {listY - listSizeY}", $"{listX + listSizeX} {listY}") };
		switch (skinId)
		{
		case 1uL:
		{
			JObject val2 = CUIHelper.RawImage(text + "_img", text, GetLocalImage("nowp"), "0.1 0.1", "0.9 0.9");
			if (val2 != null)
			{
				list.Add(val2);
			}
			break;
		}
		default:
			if (itemId != 0)
			{
				JObject val = CUIHelper.Image(text + "_img", text, itemId, skinId, "0.1 0.1", "0.9 0.9");
				if (val != null)
				{
					list.Add(val);
				}
			}
			break;
		case 0uL:
			break;
		}
		list.AddRange(CUIHelper.Button(text + "_btn", text, "0 0 0 0", skinId switch
		{
			0uL => "?", 
			1uL => "X", 
			_ => "", 
		}, 10, "0 0", "1 1", command));
		return list;
	}

	private List<JObject> BuildBuildingWallpaperControls(BuildingPrivlidge cup, int page, string category)
	{
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		List<JObject> list = new List<JObject>();
		bool work = orCreateConfig.Work;
		bool flag = category == "Wall";
		if ((TCUpgradeConfig.Config?.BothSides ?? true) && flag)
		{
			string color = (orCreateConfig.WpInternal ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9");
			string color2 = (orCreateConfig.WpExternal ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9");
			list.AddRange(CUIHelper.Button("wp_int", "TCUpgrade.color", "0.35 0.35 0.35 0.8", "", 10, "0.045 0.02", "0.09 0.07", string.Format("{0}WALLPAPERSIDES 0 wood 0 {1} {2} {3} {4}", "cui.endtest SENDCMD ", page, orCreateConfig.WpExternal.ToString().ToLower(), (!orCreateConfig.WpInternal).ToString().ToLower(), category)));
			list.Add(CUIHelper.Panel("wp_int_swatch", "wp_int", color, "0.5 0.5", "0.5 0.5", "-8 -8", "8 8"));
			list.Add(CUIHelper.Label("wp_int_lbl", "TCUpgrade.color", LangHelper.Lang(orCreateConfig.WpInternal ? "InternalON" : "InternalOFF"), 10, "0.10 0.02", "0.30 0.07", "0.7 0.7 0.7 1", "MiddleLeft"));
			list.AddRange(CUIHelper.Button("wp_ext", "TCUpgrade.color", "0.35 0.35 0.35 0.8", "", 10, "0.265 0.02", "0.31 0.07", string.Format("{0}WALLPAPERSIDES 0 wood 0 {1} {2} {3} {4}", "cui.endtest SENDCMD ", page, (!orCreateConfig.WpExternal).ToString().ToLower(), orCreateConfig.WpInternal.ToString().ToLower(), category)));
			list.Add(CUIHelper.Panel("wp_ext_swatch", "wp_ext", color2, "0.5 0.5", "0.5 0.5", "-8 -8", "8 8"));
			list.Add(CUIHelper.Label("wp_ext_lbl", "TCUpgrade.color", LangHelper.Lang(orCreateConfig.WpExternal ? "ExternalON" : "ExternalOFF"), 10, "0.32 0.02", "0.52 0.07", "0.7 0.7 0.7 1", "MiddleLeft"));
			list.AddRange(CUIHelper.Button("wp_go", "TCUpgrade.color", work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", work ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERGRADE"), 11, "0.50 0.02", "0.72 0.07", work ? string.Format("{0}STOP 0 wood 0 {1} {2}", "cui.endtest SENDCMD ", page, category) : string.Format("{0}WALLPAPERON 0 wood 0 {1} false {2}", "cui.endtest SENDCMD ", page, category)));
			list.AddRange(CUIHelper.Button("wp_all", "TCUpgrade.color", work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", work ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERALL"), 11, "0.74 0.02", "0.96 0.07", work ? string.Format("{0}STOP 0 wood 0 {1} {2}", "cui.endtest SENDCMD ", page, category) : string.Format("{0}WALLPAPERON 0 wood 0 {1} true {2}", "cui.endtest SENDCMD ", page, category)));
		}
		else
		{
			list.AddRange(CUIHelper.Button("wp_go", "TCUpgrade.color", work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", work ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERGRADE"), 11, "0.18 0.02", "0.45 0.07", work ? string.Format("{0}STOP 0 wood 0 {1} {2}", "cui.endtest SENDCMD ", page, category) : string.Format("{0}WALLPAPERON 0 wood 0 {1} false {2}", "cui.endtest SENDCMD ", page, category)));
			list.AddRange(CUIHelper.Button("wp_all", "TCUpgrade.color", work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", work ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERALL"), 11, "0.52 0.02", "0.96 0.07", work ? string.Format("{0}STOP 0 wood 0 {1} {2}", "cui.endtest SENDCMD ", page, category) : string.Format("{0}WALLPAPERON 0 wood 0 {1} true {2}", "cui.endtest SENDCMD ", page, category)));
		}
		return list;
	}

	private void UpdateBuildingWallpaperSelection(BasePlayer player, BuildingPrivlidge cup, ulong oldWallId, ulong newWallId, int page, string category)
	{
		int itemId;
		List<ulong> skinList;
		int listStartX;
		int listStartY;
		if (!((Object)(object)player == (Object)null) && !((Object)(object)cup == (Object)null))
		{
			TCConfig orCreateConfig = GetOrCreateConfig(cup);
			(int, List<ulong>) wallpaperItems = GetWallpaperItems(player, category);
			itemId = wallpaperItems.Item1;
			skinList = wallpaperItems.Item2;
			int num = (int)Math.Ceiling((double)skinList.Count / 4.0) * 92;
			int num2 = 358;
			listStartX = -num2 / 2;
			listStartY = num / 2 - 10;
			if (oldWallId != newWallId)
			{
				Redraw(oldWallId, selected: false);
			}
			Redraw(newWallId, orCreateConfig.WallpaperId == newWallId);
		}
		void Redraw(ulong skinId, bool selected)
		{
			int num3 = skinList.IndexOf(skinId);
			if (num3 >= 0)
			{
				int num4 = num3 / 4;
				int num5 = num3 % 4;
				int listX = listStartX + num5 * 92;
				int listY = listStartY - num4 * 92;
				string name = $"building_wallpaper_{skinId}";
				CUIHelper.DestroyUi(player, name);
				CUIHelper.AddUi(player, BuildWallpaperTile("building", "wp_scroll___Content", itemId, skinId, selected, string.Format("{0}WALLPAPERSELECT 0 wood 0 {1} {2} {3}", "cui.endtest SENDCMD ", page, skinId, category), listX, listY, 82, 82));
			}
		}
	}

	private void UpdateBuildingWallpaperControls(BasePlayer player, BuildingPrivlidge cup, int page, string category)
	{
		if (!((Object)(object)player == (Object)null) && !((Object)(object)cup == (Object)null))
		{
			string[] array = new string[8] { "wp_int", "wp_int_swatch", "wp_int_lbl", "wp_ext", "wp_ext_swatch", "wp_ext_lbl", "wp_go", "wp_all" };
			foreach (string name in array)
			{
				CUIHelper.DestroyUi(player, name);
			}
			CUIHelper.AddUi(player, BuildBuildingWallpaperControls(cup, page, category));
		}
	}

	private void ShowMenuBoatWallpaper(BasePlayer player, int page = 0, string category = "Wall")
	{
		if (!_playerBoatWallpaper.TryGetValue(player.userID, out var value))
		{
			return;
		}
		value.Page = page;
		value.Category = category;
		DestroyBoatWallpaperUi(player);
		List<JObject> list = new List<JObject>();
		list.Add(CUIHelper.Panel("TCUpgrade.boatwallpaper", "OverlayNonScaled", "0.04 0.05 0.06 0.92", "0.5 0.5", "0.5 0.5", "-300 -240", "300 240", needsCursor: true));
		list.Add(CUIHelper.Label("boat_wp_title", "TCUpgrade.boatwallpaper", LangHelper.Lang("BoatWallpaperTitle"), 14, "0.05 0.93", "0.92 0.98", "1 1 1 0.9"));
		list.AddRange(CUIHelper.Button("boat_wp_close", "TCUpgrade.boatwallpaper", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 12, "0.92 0.93", "0.98 0.98", "cui.endtest SENDCMD BOATWALLPAPERCLOSE"));
		float num = 0.28f;
		float num2 = 0.055f;
		float num3 = 0.04f;
		float num4 = 0.86f;
		float num5 = 0.02f;
		for (int i = 0; i < 3; i++)
		{
			string text = (new string[3] { "Wall", "Floor", "Ceiling" })[i];
			bool flag = text == category;
			float num6 = num3 + (float)i * (num + num5);
			float num7 = num6 + num;
			list.AddRange(CUIHelper.Button("boat_wp_cat_" + text, "TCUpgrade.boatwallpaper", flag ? "0.8 1 0.5 0.6" : "0.2 0.3 0.2 0.6", text.ToUpper(), 11, $"{num6} {num4}", $"{num7} {num4 + num2}", string.Format("{0}BOATWALLPAPERCATEGORY {1} {2}", "cui.endtest SENDCMD ", page, text)));
		}
		(int itemId, List<ulong> skinIds) wallpaperItems = GetWallpaperItems(player, category);
		int item = wallpaperItems.itemId;
		List<ulong> item2 = wallpaperItems.skinIds;
		int num8 = (int)Math.Ceiling((double)item2.Count / 4.0) * 92;
		int num9 = 358;
		float num10 = num3;
		float num11 = num3 + 3f * (num + num5) - num5;
		list.Add(CUIHelper.ScrollView("boat_wp_scroll", "TCUpgrade.boatwallpaper", $"{num10:F2} 0.12", $"{num11:F2} 0.84", "0 0", "0 0", num8, num9));
		string parent = "boat_wp_scroll___Content";
		int num12 = -num9 / 2;
		int num13 = num8 / 2 - 10;
		int num14 = 0;
		int num15 = 0;
		for (int j = 0; j < item2.Count; j++)
		{
			ulong num16 = item2[j];
			int listX = num12 + num15 * 92;
			int listY = num13 - num14 * 92;
			list.AddRange(BuildWallpaperTile("boat", parent, item, num16, value.WallpaperId == num16, string.Format("{0}BOATWALLPAPERSELECT {1} {2} {3}", "cui.endtest SENDCMD ", num16, page, category), listX, listY, 82, 82));
			num15++;
			if (num15 >= 4)
			{
				num15 = 0;
				num14++;
			}
		}
		list.AddRange(BuildBoatWallpaperControls(value, page, category));
		CUIHelper.AddUi(player, list);
	}

	private List<JObject> BuildBoatWallpaperControls(BoatWallpaperConfig cfg, int page, string category)
	{
		List<JObject> list = new List<JObject>();
		bool work = cfg.Work;
		bool flag = category == "Wall";
		if ((TCUpgradeConfig.Config?.BothSides ?? true) && flag)
		{
			string color = (cfg.WpInternal ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9");
			string color2 = (cfg.WpExternal ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9");
			list.AddRange(CUIHelper.Button("boat_wp_int", "TCUpgrade.boatwallpaper", "0.35 0.35 0.35 0.8", "", 10, "0.045 0.02", "0.09 0.07", string.Format("{0}BOATWALLPAPERSIDES {1} {2} {3} {4}", "cui.endtest SENDCMD ", page, cfg.WpExternal.ToString().ToLower(), (!cfg.WpInternal).ToString().ToLower(), category)));
			list.Add(CUIHelper.Panel("boat_wp_int_swatch", "boat_wp_int", color, "0.5 0.5", "0.5 0.5", "-8 -8", "8 8"));
			list.Add(CUIHelper.Label("boat_wp_int_lbl", "TCUpgrade.boatwallpaper", LangHelper.Lang(cfg.WpInternal ? "InternalON" : "InternalOFF"), 10, "0.10 0.02", "0.30 0.07", "0.7 0.7 0.7 1", "MiddleLeft"));
			list.AddRange(CUIHelper.Button("boat_wp_ext", "TCUpgrade.boatwallpaper", "0.35 0.35 0.35 0.8", "", 10, "0.265 0.02", "0.31 0.07", string.Format("{0}BOATWALLPAPERSIDES {1} {2} {3} {4}", "cui.endtest SENDCMD ", page, (!cfg.WpExternal).ToString().ToLower(), cfg.WpInternal.ToString().ToLower(), category)));
			list.Add(CUIHelper.Panel("boat_wp_ext_swatch", "boat_wp_ext", color2, "0.5 0.5", "0.5 0.5", "-8 -8", "8 8"));
			list.Add(CUIHelper.Label("boat_wp_ext_lbl", "TCUpgrade.boatwallpaper", LangHelper.Lang(cfg.WpExternal ? "ExternalON" : "ExternalOFF"), 10, "0.32 0.02", "0.52 0.07", "0.7 0.7 0.7 1", "MiddleLeft"));
			list.AddRange(CUIHelper.Button("boat_wp_apply", "TCUpgrade.boatwallpaper", work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", work ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERALL"), 11, "0.60 0.02", "0.96 0.07", string.Format("{0}BOATWALLPAPERAPPLY {1} {2}", "cui.endtest SENDCMD ", page, category)));
		}
		else
		{
			list.AddRange(CUIHelper.Button("boat_wp_apply", "TCUpgrade.boatwallpaper", work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", work ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERALL"), 11, "0.35 0.02", "0.65 0.07", string.Format("{0}BOATWALLPAPERAPPLY {1} {2}", "cui.endtest SENDCMD ", page, category)));
		}
		return list;
	}

	private void UpdateBoatWallpaperSelection(BasePlayer player, ulong oldWallId, ulong newWallId, int page, string category)
	{
		int itemId;
		List<ulong> skinList;
		int listStartX;
		int listStartY;
		if (_playerBoatWallpaper.TryGetValue(player.userID, out var value))
		{
			value.Page = page;
			value.Category = category;
			(int, List<ulong>) wallpaperItems = GetWallpaperItems(player, category);
			itemId = wallpaperItems.Item1;
			skinList = wallpaperItems.Item2;
			int num = (int)Math.Ceiling((double)skinList.Count / 4.0) * 92;
			int num2 = 358;
			listStartX = -num2 / 2;
			listStartY = num / 2 - 10;
			if (oldWallId != newWallId)
			{
				Redraw(oldWallId, selected: false);
			}
			Redraw(newWallId, value.WallpaperId == newWallId);
		}
		void Redraw(ulong skinId, bool selected)
		{
			int num3 = skinList.IndexOf(skinId);
			if (num3 >= 0)
			{
				int num4 = num3 / 4;
				int num5 = num3 % 4;
				int listX = listStartX + num5 * 92;
				int listY = listStartY - num4 * 92;
				string name = $"boat_wallpaper_{skinId}";
				CUIHelper.DestroyUi(player, name);
				CUIHelper.AddUi(player, BuildWallpaperTile("boat", "boat_wp_scroll___Content", itemId, skinId, selected, string.Format("{0}BOATWALLPAPERSELECT {1} {2} {3}", "cui.endtest SENDCMD ", skinId, page, category), listX, listY, 82, 82));
			}
		}
	}

	private void UpdateBoatWallpaperControls(BasePlayer player, int page, string category)
	{
		if (_playerBoatWallpaper.TryGetValue(player.userID, out var value))
		{
			value.Page = page;
			value.Category = category;
			string[] array = new string[7] { "boat_wp_int", "boat_wp_int_swatch", "boat_wp_int_lbl", "boat_wp_ext", "boat_wp_ext_swatch", "boat_wp_ext_lbl", "boat_wp_apply" };
			foreach (string name in array)
			{
				CUIHelper.DestroyUi(player, name);
			}
			CUIHelper.AddUi(player, BuildBoatWallpaperControls(value, page, category));
		}
	}

	private void ShowMenuAuthlist(BasePlayer player, BuildingPrivlidge cup, int page)
	{
		CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
		List<ulong> authPlayers = GetAuthPlayers(player, cup);
		List<JObject> list = new List<JObject>();
		list.Add(CUIHelper.Panel("TCUpgrade.authlist", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-250 -350", "250 350", needsCursor: true));
		list.Add(CUIHelper.Label("auth_title", "TCUpgrade.authlist", LangHelper.Lang("title3"), 22, "0.02 0.92", "0.98 0.98", "1 1 1 0.9"));
		list.AddRange(CUIHelper.Button("auth_close", "TCUpgrade.authlist", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 13, "0.78 0.92", "0.98 0.98", "cui.endtest SENDCMD CLOSE"));
		int contentHeight = Math.Max(200, authPlayers.Count * 50);
		list.Add(CUIHelper.ScrollView("auth_scroll", "TCUpgrade.authlist", "0.02 0.02", "0.98 0.88", "0 0", "0 0", contentHeight));
		bool flag = TCUpgradeConfig.Config?.SteamIdShow ?? true;
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		bool flag2 = config != null && config.ShowPlayerStatusInAuthList && TCUpgradeConfig.Config?.AdminSteamIds?.Contains(player.userID) == true;
		int num = 0;
		foreach (ulong item in authPlayers)
		{
			BasePlayer basePlayer = BasePlayer.FindAwakeOrSleepingByID(item);
			string text = basePlayer?.displayName ?? SingletonComponent<ServerMgr>.Instance?.persistance?.GetPlayerName(item) ?? item.ToString();
			if (flag2)
			{
				text += (((Object)(object)basePlayer != (Object)null && basePlayer.IsConnected) ? (" " + LangHelper.Lang("Online")) : (" " + LangHelper.Lang("Offline")));
			}
			string text2 = $"auth_row_{item}";
			list.Add(CUIHelper.Panel(text2, "auth_scroll", "0.2 0.3 0.2 0.5", "0.02 1", "0.98 1", "0 " + -(num + 1) * 50, "0 " + -num * 50));
			JObject val = CUIHelper.RawImageSteamId(text2 + "_avatar", text2, item, "0 0.05", "0.15 0.95");
			if (val != null)
			{
				list.Add(val);
			}
			list.Add(CUIHelper.Label(text2 + "_name", text2, text, 12, flag ? "0.18 0.45" : "0.18 0.15", "0.75 0.95", "1 1 1 0.9", "MiddleLeft"));
			if (flag)
			{
				list.Add(CUIHelper.Label(text2 + "_steamid", text2, item.ToString(), 10, "0.18 0.05", "0.75 0.5", "1 1 1 0.7", "MiddleLeft"));
			}
			list.AddRange(CUIHelper.Button(text2 + "_rem", text2, "0.8 0.2 0.2 0.8", LangHelper.Lang("REMOVE"), 10, "0.78 0.1", "0.98 0.9", string.Format("{0}REMOVEAUTH {1} 0 {2}", "cui.endtest SENDCMD ", page, item)));
			num++;
		}
		if (authPlayers.Count == 0)
		{
			list.Add(CUIHelper.Label("auth_empty", "auth_scroll", LangHelper.Lang("NoAuthPlayers") ?? "No authorized players", 12, "0.1 0.4", "0.9 0.6", "0.7 0.7 0.7 0.8"));
		}
		CUIHelper.AddUi(player, list);
	}

	private List<ulong> GetAuthPlayers(BasePlayer viewer, BuildingPrivlidge cup)
	{
		List<ulong> list = new List<ulong>();
		if (cup?.authorizedPlayers == null)
		{
			return list;
		}
		bool flag = TCUpgradeConfig.Config?.Adminshow ?? false;
		foreach (ulong authorizedPlayer in cup.authorizedPlayers)
		{
			if (flag || !HasPermission(authorizedPlayer.ToString(), "TCUpgrade.admin") || (!((Object)(object)viewer == (Object)null) && authorizedPlayer == (ulong)viewer.userID))
			{
				list.Add(authorizedPlayer);
			}
		}
		return list;
	}

	private void ShowMenuTCSkin(BasePlayer player, BuildingPrivlidge cup, int page)
	{
		CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
		List<JObject> list = new List<JObject>();
		list.Add(CUIHelper.Panel("TCUpgrade.tcskin", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-400 -220", "400 220", needsCursor: true));
		list.Add(CUIHelper.Label("tcsk_title", "TCUpgrade.tcskin", LangHelper.Lang("title4"), 18, "0.02 0.88", "0.98 0.98", "1 1 1 0.9"));
		list.AddRange(CUIHelper.Button("tcsk_close", "TCUpgrade.tcskin", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 13, "0.88 0.88", "0.98 0.98", string.Format("{0}CLOSE2 {1}", "cui.endtest SENDCMD ", page)));
		list.Add(CUIHelper.Panel("tcsk_content", "TCUpgrade.tcskin", "0.15 0.15 0.15 0.45", "0.02 0.02", "0.98 0.82", "0 0", "0 0"));
		int num = -(TCSkinMeta.Data.Count * 140 + (TCSkinMeta.Data.Count - 1) * 20) / 2 + 70 + 10;
		int num2 = 0;
		foreach (KeyValuePair<TCSkin, (string, string, string, int, int)> datum in TCSkinMeta.Data)
		{
			bool num3 = TCUpgradeHelpers.IsSkinOwnedOrBypass(player, datum.Value.Item5);
			string color = (num3 ? "0.2 0.35 0.2 0.7" : "0.7 0.2 0.2 0.7");
			int num4 = num + num2 * 160;
			int num5 = num4 - 70;
			int num6 = num4 + 70;
			string text = $"tcsk_card_{datum.Key}";
			list.Add(CUIHelper.Panel(text, "tcsk_content", color, "0.5 0.5", "0.5 0.5", $"{num5} {-70}", $"{num6} {70}"));
			JObject val = CUIHelper.Image(text + "_icon", text, datum.Value.Item4, (ulong)datum.Value.Item5, "0.1 0.1", "0.9 0.9");
			if (val != null)
			{
				list.Add(val);
			}
			string text2 = datum.Value.Item1.Replace("cupboard.tool.", "").Replace("cupboard.tool", "Default");
			if (string.IsNullOrEmpty(text2))
			{
				text2 = "Default";
			}
			list.Add(CUIHelper.Label(text + "_lbl", text, text2, 12, "0.05 0", "0.95 0.15", "1 1 1 0.9"));
			string command = (num3 ? string.Format("{0}TCSKINSELECT {1} {2}", "cui.endtest SENDCMD ", datum.Value.Item1, page) : string.Format("{0}TCSKINLOCKED {1} {2}", "cui.endtest SENDCMD ", datum.Value.Item1, page));
			list.AddRange(CUIHelper.Button(text + "_btn", text, "0 0 0 0", "", 10, "0 0", "1 1", command));
			num2++;
		}
		CUIHelper.AddUi(player, list);
	}

	private void ShowMenuColor(BasePlayer player, BuildingPrivlidge cup, string id, string grade, string skinId, string color, int page)
	{
		CUIHelper.DestroyUi(player, "TCUpgrade.color");
		CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
		TCConfig orCreateConfig = GetOrCreateConfig(cup);
		List<JObject> list = new List<JObject>();
		list.Add(CUIHelper.Panel("TCUpgrade.color", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-1000 -800", "1000 800", needsCursor: true));
		list.Add(CUIHelper.Panel("col_content", "TCUpgrade.color", "0.2 0.23 0.2 0.95", "0.5 0.5", "0.5 0.5", "-200 -230", "200 250"));
		list.Add(CUIHelper.Label("col_title", "col_content", LangHelper.Lang("title2"), 16, "0.02 0.88", "0.98 0.98", "1 1 1 0.9"));
		list.AddRange(CUIHelper.Button("col_close", "col_content", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 13, "0.8 0.88", "0.98 0.98", string.Format("{0}CLOSE2 {1}", "cui.endtest SENDCMD ", page)));
		bool flag = TCUpgradeConfig.Config?.EnableMultiColor ?? true;
		int num = 0;
		int num2 = -175;
		int num3 = 185;
		for (int i = 0; i < TCUpgradeHelpers.Colors.Length; i++)
		{
			if (i == 0 && !flag)
			{
				continue;
			}
			int num4 = ((num >= 12 && flag) ? 62 : 80);
			int num5 = ((num >= 12 && flag) ? 62 : 80);
			if (num < 13 && num % 4 == 0 && num != 0)
			{
				num2 = -175;
				num3 -= 90;
			}
			float num6 = ((float)num2 - -200f) / 400f;
			float num7 = ((float)(num3 - num5) - -230f) / 480f;
			float num8 = ((float)(num2 + num4) - -200f) / 400f;
			float num9 = ((float)num3 - -230f) / 480f;
			num2 += num4 + 10;
			string color2 = ((orCreateConfig.Colour == TCUpgradeHelpers.ColorIndexToUint(i)) ? "1 1 1 0.7" : "0.2 0.3 0.2 0.6");
			list.AddRange(CUIHelper.Button($"col_{i}", "col_content", color2, "", 10, $"{num6} {num7}", $"{num8} {num9}", string.Format("{0}COLORSELECT {1} {2} {3} {4} {5}", "cui.endtest SENDCMD ", id, grade, skinId, i, page)));
			string localImage = GetLocalImage("color_" + i);
			float num10 = 3f / (float)num4;
			if (!string.IsNullOrEmpty(localImage))
			{
				JObject val = CUIHelper.RawImage($"col_{i}_img", $"col_{i}", localImage, $"{num10} {num10}", $"{1f - num10} {1f - num10}");
				if (val != null)
				{
					list.Add(val);
				}
			}
			else
			{
				string text = ((i == 0) ? "0.5 0.5 0.5 1" : TCUpgradeHelpers.Colors[i]);
				if (!string.IsNullOrEmpty(text))
				{
					JObject val2 = CUIHelper.Panel($"col_{i}_img", $"col_{i}", text, $"{num10} {num10}", $"{1f - num10} {1f - num10}", "0 0", "0 0");
					if (val2 != null)
					{
						list.Add(val2);
					}
				}
			}
			num++;
		}
		list.AddRange(CUIHelper.Button("col_upgrade", "col_content", orCreateConfig.Work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", orCreateConfig.Work ? LangHelper.Lang("STOP") : LangHelper.Lang("UPGRADE"), 12, "0.35 0.04", "0.65 0.11", orCreateConfig.Work ? string.Format("{0}STOP {1}", "cui.endtest SENDCMD ", page) : string.Format("{0}UPGRADE {1} {2} {3} {4} 1", "cui.endtest SENDCMD ", id, grade, skinId, page)));
		CUIHelper.AddUi(player, list);
	}

	public void UpdateBlockedItems(BuildingPrivlidge cupboard)
	{
		if (cupboard?.inventory?.blockedItems == null)
		{
			return;
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config?.AllowedItemsConfig == null)
		{
			return;
		}
		HashSet<ItemDefinition> hashSet = new HashSet<ItemDefinition>(cupboard.inventory.blockedItems);
		string itemCategoryFilter = config.ItemCategoryFilter ?? "Resources";
		if (itemCategoryFilter == "All")
		{
			cupboard.onlyAcceptCategory = ItemCategory.All;
		}
		else if (itemCategoryFilter == "Resources")
		{
			cupboard.onlyAcceptCategory = ItemCategory.Resources;
		}
		else if (itemCategoryFilter == "ResourcesAndComponents")
		{
			cupboard.onlyAcceptCategory = ItemCategory.All;
			HashSet<string> allowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"wiretool",
				"hosetool",
				"pipetool",
				"hammer",
				"wallpaper.tool",
				"building.planner",
				"boat.planner"
			};
			for (int i = 0; i < ItemManager.itemList.Count; i++)
			{
				ItemDefinition itemDefinition = ItemManager.itemList[i];
				bool isAllowed = itemDefinition.category == ItemCategory.Resources || itemDefinition.category == ItemCategory.Component || allowedTools.Contains(itemDefinition.shortname);
				if (!isAllowed)
				{
					hashSet.Add(itemDefinition);
				}
			}
		}
		foreach (ItemDefinition item in ItemManager.itemList)
		{
			if (config.AllowedItemsConfig.TryGetValue(item.shortname, out var value))
			{
				if (value)
				{
					hashSet.Remove(item);
				}
				else
				{
					hashSet.Add(item);
				}
			}
		}
		cupboard.inventory.blockedItems = hashSet;
		cupboard.inventory.MarkDirty();
	}
}
