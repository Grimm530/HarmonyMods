using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ConVar;
using Newtonsoft.Json;
using UnityEngine;

namespace TruePVE;

public class TruePVEMod : IHarmonyModHooks
{
	private static readonly FieldInfo ServerClanField = typeof(BasePlayer).GetField("serverClan", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly Dictionary<string, DateTime> LootDebugThrottle = new Dictionary<string, DateTime>();

	private string _configPath;

	public static TruePVEMod Instance { get; private set; }

	public TruePVEConfig Config { get; private set; }

	public bool LootDebugEnabled
	{
		get
		{
			TruePVEConfig config = Config;
			if (config == null)
			{
				return false;
			}
			return config.PreventLooting?.Debug == true;
		}
	}

	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Instance = this;
		LoadConfig();
		ApplyGamePvE();
		bool turretDebug = Config?.PvE?.TurretIgnorePlayersDebug == true;
		Debug.Log((object)("[TruePVE] Harmony mod loaded. Game PvE applied; Prevent Looting / Loot Defender active per config." + (turretDebug ? " Turret debug logging ON." : "")));
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		LootDefenderState.Clear();
		lock (LootDebugThrottle)
		{
			LootDebugThrottle.Clear();
		}
		Instance = null;
		Debug.Log((object)"[TruePVE] Harmony mod unloaded.");
	}

	private void LoadConfig()
	{
		string fullPath = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
		string[] array = new string[3]
		{
			Path.Combine(fullPath, "HarmonyConfig", "TruePVE.json"),
			Path.Combine(fullPath, "Config", "TruePVE.json"),
			Path.Combine(fullPath, "TruePVE.json")
		};
		foreach (string text in array)
		{
			if (!File.Exists(text))
			{
				continue;
			}
			_configPath = text;
			try
			{
				string text2 = File.ReadAllText(text);
				Config = JsonConvert.DeserializeObject<TruePVEConfig>(text2);
				if (Config == null)
				{
					Config = new TruePVEConfig();
				}
				if (Config.PreventLooting == null)
				{
					Config.PreventLooting = new PreventLootingOptions();
				}
				if (Config.LootDefender == null)
				{
					Config.LootDefender = new LootDefenderOptions();
				}
				if (Config.PvE == null)
				{
					Config.PvE = new PvEOptions();
				}
				Debug.Log((object)("[TruePVE] Config loaded from " + text));
				return;
			}
			catch (Exception ex)
			{
				Debug.LogWarning((object)("[TruePVE] Failed to load config from " + text + ": " + ex.Message + ". Trying next path."));
			}
		}
		Config = new TruePVEConfig();
		if (Config.PreventLooting == null)
		{
			Config.PreventLooting = new PreventLootingOptions();
		}
		if (Config.LootDefender == null)
		{
			Config.LootDefender = new LootDefenderOptions();
		}
		if (Config.PvE == null)
		{
			Config.PvE = new PvEOptions();
		}
		_configPath = Path.Combine(fullPath, "HarmonyConfig", "TruePVE.json");
		SaveConfig();
		Debug.Log((object)("[TruePVE] No config found; default config created at " + _configPath));
	}

	private void SaveConfig()
	{
		if (string.IsNullOrEmpty(_configPath) || Config == null)
		{
			return;
		}
		try
		{
			string directoryName = Path.GetDirectoryName(_configPath);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			string contents = JsonConvert.SerializeObject((object)Config, (Formatting)1);
			File.WriteAllText(_configPath, contents);
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("[TruePVE] Failed to save config to " + _configPath + ": " + ex.Message));
		}
	}

	private void ApplyGamePvE()
	{
		if (Config?.PvE != null)
		{
			if (Config.PvE.EnableGamePvE)
			{
				Server.pve = true;
			}
			Server.pveBulletDamageMultiplier = Config.PvE.PveBulletDamageMultiplier;
		}
	}

	public bool IsAlly(ulong ownerId, ulong playerId)
	{
		if (ownerId == 0L || playerId == ownerId)
		{
			return true;
		}
		if (Config?.PreventLooting == null)
		{
			return false;
		}
		if (AreTeammates(ownerId, playerId))
		{
			return true;
		}
		if (AreClanmates(ownerId, playerId))
		{
			return true;
		}
		return false;
	}

	public bool IsOwnedByPlayer(BasePlayer player, BaseEntity entity)
	{
		if ((Object)(object)player == (Object)null || (Object)(object)entity == (Object)null)
		{
			return false;
		}
		if (entity.OwnerID != 0L)
		{
			return entity.OwnerID == (ulong)player.userID;
		}
		return false;
	}

	private static BasePlayer FindPlayerOrSleeper(ulong userId)
	{
		return BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
	}

	private bool AreTeammates(ulong ownerId, ulong playerId)
	{
		if (!Config.PreventLooting.UseTeamsForAllies || (Object)(object)RelationshipManager.ServerInstance == (Object)null)
		{
			return false;
		}
		RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(ownerId);
		if (playerTeam?.members == null)
		{
			return false;
		}
		for (int i = 0; i < playerTeam.members.Count; i++)
		{
			if (playerTeam.members[i] == playerId)
			{
				return true;
			}
		}
		return false;
	}

	private bool AreClanmates(ulong ownerId, ulong playerId)
	{
		if (!Config.PreventLooting.UseFriendsAPIForAllies)
		{
			return false;
		}
		if (IsClanMember(FindPlayerOrSleeper(ownerId), playerId))
		{
			return true;
		}
		return IsClanMember(FindPlayerOrSleeper(playerId), ownerId);
	}

	private static bool IsClanMember(BasePlayer player, ulong targetUserId)
	{
		if ((Object)(object)player == (Object)null || ServerClanField == null)
		{
			return false;
		}
		object value = ServerClanField.GetValue(player);
		if (value == null)
		{
			return false;
		}
		if (!(value.GetType().GetProperty("Members", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value, null) is IEnumerable enumerable))
		{
			return false;
		}
		foreach (object item in enumerable)
		{
			if (item != null && item.GetType().GetProperty("SteamId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null) is ulong num && num == targetUserId)
			{
				return true;
			}
		}
		return false;
	}

	public BuildingPrivlidge GetBuildingPrivilege(BasePlayer player, BaseEntity atEntity)
	{
		if (atEntity is BuildingPrivlidge result)
		{
			return result;
		}
		if ((Object)(object)player == (Object)null || (Object)(object)atEntity == (Object)null)
		{
			return null;
		}
		try
		{
			return player.GetBuildingPrivilege();
		}
		catch
		{
			return null;
		}
	}

	public bool ShouldAllowStorageAccess(BasePlayer player, BaseEntity atEntity)
	{
		if ((Object)(object)player == (Object)null || (Object)(object)atEntity == (Object)null)
		{
			return false;
		}
		if (atEntity is BuildingPrivlidge)
		{
			return CanAccessToolCupboard(player, atEntity);
		}
		if (atEntity.OwnerID == 0L || atEntity.OwnerID == (ulong)player.userID)
		{
			return true;
		}
		if (IsAlly(atEntity.OwnerID, player.userID))
		{
			return true;
		}
		BuildingPrivlidge buildingPrivilege = GetBuildingPrivilege(player, atEntity);
		TruePVEConfig config = Config;
		if (config != null && config.PreventLooting?.OnlyInCupboardRange == true && (Object)(object)buildingPrivilege == (Object)null)
		{
			return true;
		}
		TruePVEConfig config2 = Config;
		if (config2 != null && config2.PreventLooting?.RespectCupboardAuthorization == true && (Object)(object)buildingPrivilege != (Object)null && buildingPrivilege.IsAuthed(player))
		{
			return true;
		}
		return false;
	}

	public bool CanAccessToolCupboard(BasePlayer player, BaseEntity cupboard)
	{
		if ((Object)(object)player == (Object)null || (Object)(object)cupboard == (Object)null)
		{
			return false;
		}
		if (cupboard.OwnerID == 0L)
		{
			return true;
		}
		if (cupboard.OwnerID == (ulong)player.userID)
		{
			return true;
		}
		return IsAlly(cupboard.OwnerID, player.userID);
	}

	public bool IsBuildingAuthed(BasePlayer player, BaseEntity atEntity)
	{
		BuildingPrivlidge buildingPrivilege = GetBuildingPrivilege(player, atEntity);
		if ((Object)(object)buildingPrivilege != (Object)null && (Object)(object)player != (Object)null)
		{
			return buildingPrivilege.IsAuthed(player);
		}
		return false;
	}

	public void LogLootBlocked(BasePlayer looter, BaseEntity target, string reason)
	{
		LogLootDecision(looter, target, false, reason);
	}

	public void LogLootAllowed(BasePlayer looter, BaseEntity target, string reason)
	{
		LogLootDecision(looter, target, true, reason);
	}

	public void LogLootDecision(BasePlayer looter, BaseEntity target, bool allowed, string reason)
	{
		if (!LootDebugEnabled || (Object)(object)looter == (Object)null || (Object)(object)target == (Object)null || string.IsNullOrEmpty(reason))
		{
			return;
		}
		ulong num = ((target.net != null) ? target.net.ID.Value : 0);
		string key = $"{looter.userID}:{num}:{allowed}:{reason}";
		DateTime utcNow = DateTime.UtcNow;
		lock (LootDebugThrottle)
		{
			if (LootDebugThrottle.TryGetValue(key, out var value) && (utcNow - value).TotalSeconds < 1.0)
			{
				return;
			}
			LootDebugThrottle[key] = utcNow;
		}
		ulong lootOwnerId = GetLootOwnerId(target);
		bool flag = lootOwnerId != 0L && IsAlly(lootOwnerId, looter.userID);
		bool flag2 = lootOwnerId != 0L && IsBuildingAuthed(looter, target);
		string decision = allowed ? "ALLOWED" : "BLOCKED";
		Debug.Log((object)("[TruePVE][LootDebug] " + decision + " looter=" + FormatPlayer(looter) + " target=" + DescribeTarget(target) + " " + $"owner={lootOwnerId} " + $"ally={flag} " + $"tcAuthed={flag2} " + "reason=" + reason));
	}

	private static string FormatPlayer(BasePlayer player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return "null";
		}
		return $"{player.displayName} ({player.userID})";
	}

	private static ulong GetLootOwnerId(BaseEntity target)
	{
		if ((Object)(object)target == (Object)null)
		{
			return 0uL;
		}
		if (target is BasePlayer basePlayer)
		{
			return basePlayer.userID;
		}
		if (target is PlayerCorpse playerCorpse)
		{
			return playerCorpse.playerSteamID;
		}
		if (target is LootableCorpse { playerSteamID: not 0uL } lootableCorpse)
		{
			return lootableCorpse.playerSteamID;
		}
		if (target is DroppedItemContainer { playerSteamID: not 0uL } droppedItemContainer)
		{
			return droppedItemContainer.playerSteamID;
		}
		return target.OwnerID;
	}

	private static string DescribeTarget(BaseEntity target)
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)target == (Object)null)
		{
			return "null";
		}
		string text = (string.IsNullOrEmpty(target.ShortPrefabName) ? ((object)target).GetType().Name : target.ShortPrefabName);
		Vector3 position = ((Component)target).transform.position;
		return $"{((object)target).GetType().Name}/{text} @ ({position.x:F1}, {position.y:F1}, {position.z:F1})";
	}

	public static bool IsAdminOrDeveloperLooter(BasePlayer player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return false;
		}
		if (player.IsAdmin || player.IsDeveloper)
		{
			return true;
		}
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				Type type = assemblies[i].GetType("DeveloperListOverride.DeveloperListOverrideConfig");
				if (!(type == null))
				{
					MethodInfo method = type.GetMethod("IsOverrideDeveloper", BindingFlags.Static | BindingFlags.Public);
					if (!(method == null))
					{
						return (bool)method.Invoke(null, new object[1] { player.UserIDString ?? "" });
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}
}
