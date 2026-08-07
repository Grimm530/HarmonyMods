using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Facepunch;
using UnityEngine;

namespace TCUpgrade;

public static class TCUpgradeHelpers
{
	public enum WallMaterialType
	{
		Wood,
		Stone,
		Unknown
	}

	public const string FxNoResources = "assets/bundled/prefabs/fx/ore_break.prefab";

	public const string FxFinish = "assets/prefabs/deployable/research table/effects/research-success.prefab";

	public const string FxSpray = "assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab";

	public const string FxReskin = "assets/prefabs/tools/spraycan/reskineffect.prefab";

	public const string FxWall = "assets/prefabs/building/wall.external.high.stone/effects/wall-external-stone-deploy.prefab";

	public const string FxCloth = "assets/prefabs/wallpaper/effects/place.prefab";

	public const string FxRepair = "assets/prefabs/deployable/modular car lift/effects/modular-car-lift-repair.prefab";

	public const string FxError = "assets/prefabs/weapons/toolgun/effects/repairerror.prefab";

	private static readonly List<(int start, int end, int dlcSteamItemId)> SkinIdRanges = new List<(int, int, int)>
	{
		(10244, 10268, 10265),
		(10272, 10279, 10280),
		(10311, 10313, 10273),
		(10360, 10409, 10387),
		(10483, 10488, 10473),
		(10521, 10557, 10520)
	};

	private static readonly HashSet<ulong> WhitelistedSkins = new HashSet<ulong>
	{
		2uL,
		10242uL,
		10243uL,
		10246uL,
		10372uL,
		10386uL,
		10384uL,
		10388uL,
		10401uL,
		10406uL
	};

	public static readonly string[] Colors = new string[17]
	{
		"", "0.25 0.56 0.75 1", "0.25 0.72 0.31 1", "0.65 0.28 0.85 1", "0.48 0.15 0.08 1", "0.92 0.46 0.06 1", "0.87 0.87 0.87 1", "0.18 0.18 0.16 1", "0.42 0.33 0.27 1", "0.17 0.21 0.33 1",
		"0.16 0.34 0.17 1", "0.83 0.29 0.16 1", "0.85 0.53 0.38 1", "0.90 0.67 0.15 1", "0.34 0.32 0.31 1", "0.08 0.33 0.37 1", "0.68 0.61 0.56 1"
	};

	public const string FxPromoteStone = "assets/bundled/prefabs/fx/build/promote_stone.prefab";

	public const string FxPromoteMetal = "assets/bundled/prefabs/fx/build/promote_metal.prefab";

	public const string FxPromoteTopTier = "assets/bundled/prefabs/fx/build/promote_toptier.prefab";

	public const string FxFramePlace = "assets/bundled/prefabs/fx/build/frame_place.prefab";

	public static void ShowToast(BasePlayer player, GameTip.Styles style, string token, string english, params string[] args)
	{
		if (!((Object)(object)player == (Object)null) && player.IsConnected)
		{
			player.ShowToast(style, new Translate.Phrase(token, english), overlay: false, args);
		}
	}

	public static bool IsSkinOwnedOrBypass(BasePlayer player, int skinId)
	{
		if ((Object)(object)player == (Object)null)
		{
			return true;
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config != null && config.AllowAllSkins)
		{
			return true;
		}
		if (skinId <= 0)
		{
			return true;
		}
		if ((Object)(object)player.blueprints == (Object)null)
		{
			return true;
		}
		return CheckSkinOwnershipCompat(player.blueprints, skinId, player);
	}

	private static bool CheckSkinOwnershipCompat(PlayerBlueprints blueprints, int skinId, BasePlayer player)
	{
		try
		{
			Type typeFromHandle = typeof(PlayerBlueprints);
			MethodInfo method = typeFromHandle.GetMethod("CheckSkinOwnership", new Type[2]
			{
				typeof(int),
				typeof(ulong)
			});
			if (method != null)
			{
				ulong num = player.userID.Get();
				return (bool)method.Invoke(blueprints, new object[2] { skinId, num });
			}
			MethodInfo method2 = typeFromHandle.GetMethod("CheckSkinOwnership", new Type[2]
			{
				typeof(int),
				typeof(BasePlayer)
			});
			if (method2 != null)
			{
				return (bool)method2.Invoke(blueprints, new object[2] { skinId, player });
			}
		}
		catch
		{
		}
		return true;
	}

	public static bool IsWallpaperAllowed(BasePlayer player, int skinId)
	{
		if ((Object)(object)player == (Object)null)
		{
			return true;
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config != null && config.AllowAllSkins)
		{
			return true;
		}
		if (skinId <= 0 || WhitelistedSkins.Contains((ulong)skinId))
		{
			return true;
		}
		if ((Object)(object)player.blueprints?.steamInventory != (Object)null && player.blueprints.steamInventory.HasItem(skinId))
		{
			return true;
		}
		foreach (var (start, end, dlcId) in SkinIdRanges)
		{
			if (skinId >= start && skinId <= end)
			{
				return (Object)(object)player.blueprints?.steamInventory != (Object)null && player.blueprints.steamInventory.HasItem(dlcId);
			}
		}
		return false;
	}

	public static string GetMissingResources(List<ItemAmount> required, ItemContainer inventory)
	{
		if (required == null || inventory == null)
		{
			return "?";
		}
		List<string> parts = new List<string>();
		foreach (ItemAmount item in required)
		{
			if (item == null || (Object)(object)item.itemDef == (Object)null)
			{
				continue;
			}
			int have = inventory.GetAmount(item.itemid, onlyUsableAmounts: false, redirectAllowed: false);
			int need = (int)item.amount;
			if (have < need)
			{
				parts.Add($"{item.itemDef.displayName.english}: {need - have}");
			}
		}
		if (parts.Count <= 0)
		{
			return "?";
		}
		return string.Join(", ", parts);
	}

	public static void ApplyWallpaperProtection(BuildingBlock block, bool wallpaperDamage)
	{
		if ((Object)(object)block == (Object)null)
		{
			return;
		}
		if (!wallpaperDamage)
		{
			if (block.wallpaperProtection == null)
			{
				block.wallpaperProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
			}
			if (block.wallpaperProtection.amounts.Length < 26)
			{
				block.wallpaperProtection.amounts = new float[26];
			}
			for (int i = 0; i < block.wallpaperProtection.amounts.Length; i++)
			{
				block.wallpaperProtection.amounts[i] = 1f;
			}
		}
		else
		{
			block.wallpaperProtection = null;
		}
	}

	public static bool IsOnBarge(BuildingPrivlidge cup)
	{
		if (cup?.GetBuilding()?.buildingBlocks == null || cup.GetBuilding().buildingBlocks.Count == 0)
		{
			return false;
		}
		BaseEntity baseEntity = cup.GetBuilding().buildingBlocks[0];
		while ((Object)(object)baseEntity != (Object)null)
		{
			if (baseEntity is PlayerBoat || baseEntity is ModularCar)
			{
				return true;
			}
			baseEntity = baseEntity.GetParentEntity();
		}
		return false;
	}

	public static TCUpgradeConfig.ItemInfo GetItemInfo(int id, string grade)
	{
		if (TCUpgradeConfig.Config?.ItemsList == null)
		{
			return null;
		}
		string text = (grade ?? "").ToLowerInvariant();
		foreach (TCUpgradeConfig.ItemInfo items in TCUpgradeConfig.Config.ItemsList)
		{
			if (items.Enabled && items.ID == id && (items.Grade ?? "").ToLowerInvariant() == text)
			{
				return items;
			}
		}
		return null;
	}

	public static uint ColorIndexToUint(int index)
	{
		if (index <= 0 || index >= Colors.Length)
		{
			return 0u;
		}
		string[] array = Colors[index].Split(' ');
		if (array.Length < 4)
		{
			return 0u;
		}
		if (!float.TryParse(array[0], out var result) || !float.TryParse(array[1], out var result2) || !float.TryParse(array[2], out var result3) || !float.TryParse(array[3], out var result4))
		{
			return 0u;
		}
		byte b = (byte)(Mathf.Clamp01(result) * 255f);
		byte b2 = (byte)(Mathf.Clamp01(result2) * 255f);
		byte b3 = (byte)(Mathf.Clamp01(result3) * 255f);
		return (uint)(((byte)(Mathf.Clamp01(result4) * 255f) << 24) | (b << 16) | (b2 << 8) | b3);
	}

	public static bool Unlock(int maxGradeTier, string requiredGrade)
	{
		string text = (requiredGrade ?? "").ToLowerInvariant();
		if (maxGradeTier == 1 && text == "wood")
		{
			return true;
		}
		if (maxGradeTier == 2 && (text == "wood" || text == "stone"))
		{
			return true;
		}
		if (maxGradeTier == 3)
		{
			switch (text)
			{
			case "wood":
			case "stone":
			case "metal":
				return true;
			}
		}
		if (maxGradeTier >= 4)
		{
			return true;
		}
		return false;
	}

	public static float Frequency(string steamId, Dictionary<string, float> frequency)
	{
		if (frequency == null)
		{
			return 2f;
		}
		float num = 100f;
		foreach (KeyValuePair<string, float> item in frequency)
		{
			if (TCUpgradeMod.Instance != null && TCUpgradeMod.Instance.HasPermission(steamId, item.Key))
			{
				num = Mathf.Min(num, item.Value);
			}
		}
		if (!(num >= 100f))
		{
			return num;
		}
		return 2f;
	}

	public static float ResourcesRepair(string steamId)
	{
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config?.CostListRepair == null)
		{
			return 1f;
		}
		float num = 100f;
		foreach (KeyValuePair<string, float> item in config.CostListRepair)
		{
			if (TCUpgradeMod.Instance != null && TCUpgradeMod.Instance.HasPermission(steamId, item.Key))
			{
				num = Mathf.Min(num, item.Value);
			}
		}
		if (!(num >= 100f))
		{
			return num;
		}
		return 1f;
	}

	public static void TakeResources(IEnumerable<Item> itemList, string shortName, int amount)
	{
		if (amount <= 0 || itemList == null)
		{
			return;
		}
		List<Item> list = Pool.Get<List<Item>>();
		int num = 0;
		foreach (Item item in itemList)
		{
			if (!((Object)(object)item?.info == (Object)null) && !(item.info.shortname != shortName))
			{
				int num2 = amount - num;
				if (num2 <= 0)
				{
					break;
				}
				if (item.amount > num2)
				{
					item.MarkDirty();
					item.amount -= num2;
					break;
				}
				num += item.amount;
				list.Add(item);
				if (num >= amount)
				{
					break;
				}
			}
		}
		foreach (Item item2 in list)
		{
			item2?.Remove();
		}
		Pool.FreeUnmanaged<Item>(ref list);
	}

	public static bool CheckBlock(BuildingBlock block)
	{
		if ((Object)(object)block == (Object)null)
		{
			return true;
		}
		if (!block.blockDefinition.checkVolumeOnUpgrade)
		{
			return false;
		}
		DeployVolume[] volumes = PrefabAttribute.server.FindAll<DeployVolume>(block.prefabID);
		return !DeployVolume.Check(((Component)block).transform.position, ((Component)block).transform.rotation, volumes, ~(1 << ((Component)block).gameObject.layer));
	}

	public static void CreateGameTip(BuildingPrivlidge cup, string text, BasePlayer player, string sound, float length = 10f, string style = "")
	{
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (!string.IsNullOrEmpty(sound))
		{
			Vector3 posWorld = (((Object)(object)cup != (Object)null && !cup.IsDestroyed) ? ((Component)cup).transform.position : ((Component)player).transform.position);
			Effect.server.Run(sound, posWorld);
		}
		List<BasePlayer> list = new List<BasePlayer>();
		if ((Object)(object)cup != (Object)null && !cup.IsDestroyed && cup.authorizedPlayers != null)
		{
			foreach (ulong authorizedPlayer in cup.authorizedPlayers)
			{
				BasePlayer basePlayer = BasePlayer.FindByID(authorizedPlayer);
				if ((Object)(object)basePlayer != (Object)null && basePlayer.IsConnected)
				{
					list.Add(basePlayer);
				}
			}
		}
		if (list.Count == 0)
		{
			list.Add(player);
		}
		foreach (BasePlayer item in list)
		{
			if ((Object)(object)item == (Object)null || !item.IsConnected)
			{
				continue;
			}
			if (config == null || config.AlertGametip)
			{
				if (style == "danger")
				{
					item.SendConsoleCommand("chat.add", 2, 0, "<color=#ff0000>[TCUpgrade]</color> " + text);
				}
				else
				{
					item.SendConsoleCommand("gametip.hidegametip");
					item.SendConsoleCommand("gametip.showgametip", text);
					BasePlayer p = item;
					ServerMgr instance = SingletonComponent<ServerMgr>.Instance;
					if (instance != null)
					{
						((MonoBehaviour)instance).StartCoroutine(HideGameTipDelayed(p, length));
					}
				}
			}
			if (config == null || config.AlertChat)
			{
				string text2 = config?.ColorPrefix ?? "#f74d31";
				item.SendConsoleCommand("chat.add", 2, 0, "<color=" + text2 + ">[TCUpgrade]</color> " + text);
			}
		}
	}

	public static void CopyLock(BaseEntity fromEntity, BaseEntity toEntity)
	{
		if ((Object)(object)fromEntity == (Object)null || (Object)(object)toEntity == (Object)null || !fromEntity.HasSlot(BaseEntity.Slot.Lock) || !toEntity.HasSlot(BaseEntity.Slot.Lock))
		{
			return;
		}
		BaseEntity slot = fromEntity.GetSlot(BaseEntity.Slot.Lock);
		if (slot is CodeLock codeLock)
		{
			CodeLock codeLock2 = GameManager.server.CreateEntity(codeLock.PrefabName) as CodeLock;
			if ((Object)(object)codeLock2 != (Object)null)
			{
				codeLock2.OwnerID = codeLock.OwnerID;
				codeLock2.code = codeLock.code;
				codeLock2.whitelistPlayers = new List<ulong>(codeLock.whitelistPlayers);
				codeLock2.guestCode = codeLock.guestCode;
				codeLock2.guestPlayers = new List<ulong>(codeLock.guestPlayers);
				codeLock2.SetFlag(BaseEntity.Flags.Locked, b: true);
				codeLock2.SetParent(toEntity, toEntity.GetSlotAnchorName(BaseEntity.Slot.Lock));
				((Component)codeLock2).transform.localPosition = Vector3.zero;
				((Component)codeLock2).transform.localRotation = Quaternion.identity;
				codeLock2.Spawn();
				toEntity.SetSlot(BaseEntity.Slot.Lock, codeLock2);
			}
		}
		else
		{
			if (!(slot is KeyLock keyLock))
			{
				return;
			}
			KeyLock keyLock2 = GameManager.server.CreateEntity(keyLock.PrefabName) as KeyLock;
			if ((Object)(object)keyLock2 != (Object)null)
			{
				keyLock2.OwnerID = keyLock.OwnerID;
				FieldInfo field = typeof(KeyLock).GetField("keyCode", BindingFlags.Instance | BindingFlags.NonPublic);
				if (field != null)
				{
					field.SetValue(keyLock2, field.GetValue(keyLock));
				}
				keyLock2.SetFlag(BaseEntity.Flags.Locked, b: true);
				keyLock2.SetParent(toEntity, toEntity.GetSlotAnchorName(BaseEntity.Slot.Lock));
				((Component)keyLock2).transform.localPosition = Vector3.zero;
				((Component)keyLock2).transform.localRotation = Quaternion.identity;
				keyLock2.Spawn();
				toEntity.SetSlot(BaseEntity.Slot.Lock, keyLock2);
			}
		}
	}

	public static string GetTargetPrefab(string shortName, int skinId)
	{
		if (shortName != null && shortName.Contains("wall.external"))
		{
			if (!WallPrefabs.Walls.TryGetValue(skinId, out var value))
			{
				return null;
			}
			return value;
		}
		if (shortName != null && shortName.Contains("gates.external.high"))
		{
			if (!WallPrefabs.Gates.TryGetValue(skinId, out var value2))
			{
				return null;
			}
			return value2;
		}
		return null;
	}

	public static WallMaterialType GetWallType(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return WallMaterialType.Unknown;
		}
		string text = name.ToLowerInvariant();
		if (text.Contains("wood") || text.Contains("frontier"))
		{
			return WallMaterialType.Wood;
		}
		if (text.Contains("adobe") || text.Contains("stone") || text.Contains("ice"))
		{
			return WallMaterialType.Stone;
		}
		return WallMaterialType.Unknown;
	}

	public static bool CanChangeWall(WallMaterialType from, WallMaterialType to)
	{
		if (from == WallMaterialType.Unknown || to == WallMaterialType.Unknown)
		{
			return false;
		}
		return from == to;
	}

	private static IEnumerator HideGameTipDelayed(BasePlayer p, float delay)
	{
		yield return CoroutineEx.waitForSeconds(delay);
		if ((Object)(object)p != (Object)null && p.IsConnected)
		{
			p.SendConsoleCommand("gametip.hidegametip");
		}
	}
}
