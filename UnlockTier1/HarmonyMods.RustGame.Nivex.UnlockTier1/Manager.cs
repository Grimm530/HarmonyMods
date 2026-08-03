using System;
using System.Collections.Generic;
using ConVar;
using HarmonyLib;
using Network;
using ProtoBuf;
using UnityEngine;

namespace HarmonyMods.RustGame.Nivex.UnlockTier1;

public class Manager : IHarmonyModHooks
{
	[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
	internal class Bootstrap_StartupShared
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			Initialize();
		}

		internal static void Initialize()
		{
			try
			{
				Debug.LogWarning((object)"[Harmony] Loaded: UnlockTier1 1.0.1.0 by nivex");
				foreach (BasePlayer current in BasePlayer.activePlayerList)
				{
					BasePlayer_PlayerInit.UnlockTier(current);
				}
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
		}
	}

	[HarmonyPatch(typeof(BasePlayer), "PlayerInit", new Type[] { typeof(Connection) })]
	internal class BasePlayer_PlayerInit
	{
		[HarmonyPostfix]
		private static void Postfix(Connection c)
		{
			try
			{
				if (c != null && c.player is BasePlayer player)
				{
					UnlockTier(player);
				}
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
		}

		/// <summary>
		/// Steam-item / DLC blueprints must not be forced into unlockedItems.
		/// Client BlueprintInformationPanel → PlayerBlueprints.HasUnlocked can NRE on those paths
		/// (steamInventory / skins), which kicks the player when they click inventory items.
		/// </summary>
		internal static bool IsSafeToAutoUnlock(ItemBlueprint blueprint, out ItemDefinition target)
		{
			target = null;
			if (blueprint == null || !blueprint.userCraftable || blueprint.defaultBlueprint)
			{
				return false;
			}
			if (blueprint.NeedsSteamItem)
			{
				return false;
			}
			target = blueprint.targetItem;
			if (target == null)
			{
				return false;
			}
			// Same as ItemBlueprint.NeedsSteamDLC, but only after a null-safe targetItem check.
			if (target.steamDlc != null)
			{
				return false;
			}
			return true;
		}

		internal static bool EnsureUnlockedItemsList(PersistantPlayer info)
		{
			if (info == null)
			{
				return false;
			}
			if (info.unlockedItems == null)
			{
				info.unlockedItems = new List<int>();
			}
			return true;
		}

		/// <summary>
		/// Remove steam/DLC item ids previously written by older UnlockTier1 builds.
		/// Those ids are unused by stock HasUnlocked (steam/DLC checks run first) and can
		/// desync client BP UI enough to crash on item select.
		/// </summary>
		internal static bool ScrubUnsafeUnlocks(PersistantPlayer info)
		{
			if (!EnsureUnlockedItemsList(info) || info.unlockedItems.Count == 0)
			{
				return false;
			}

			bool changed = false;
			List<ItemBlueprint> blueprints = ItemManager.GetBlueprints();
			for (int i = 0; i < blueprints.Count; i++)
			{
				ItemBlueprint blueprint = blueprints[i];
				if (blueprint == null)
				{
					continue;
				}
				ItemDefinition target = blueprint.targetItem;
				if (target == null)
				{
					continue;
				}
				if (!blueprint.NeedsSteamItem && target.steamDlc == null)
				{
					continue;
				}
				if (info.unlockedItems.Remove(target.itemid))
				{
					changed = true;
				}
			}
			return changed;
		}

		internal static void UnlockTier(BasePlayer player, int maxTier = 1)
		{
			if (player == null)
			{
				return;
			}

			PersistantPlayer persistantPlayerInfo = player.PersistantPlayerInfo;
			if (!EnsureUnlockedItemsList(persistantPlayerInfo))
			{
				return;
			}

			bool changed = ScrubUnsafeUnlocks(persistantPlayerInfo);

			List<ItemBlueprint> blueprints = ItemManager.GetBlueprints();
			for (int i = 0; i < blueprints.Count; i++)
			{
				ItemBlueprint blueprint = blueprints[i];
				if (!IsSafeToAutoUnlock(blueprint, out ItemDefinition target))
				{
					continue;
				}
				if (blueprint.workbenchLevelRequired > maxTier)
				{
					continue;
				}
				if (persistantPlayerInfo.unlockedItems.Contains(target.itemid))
				{
					continue;
				}
				persistantPlayerInfo.unlockedItems.Add(target.itemid);
				changed = true;
			}

			if (!changed)
			{
				return;
			}

			player.PersistantPlayerInfo = persistantPlayerInfo;
			((BaseNetworkable)player).SendNetworkUpdateImmediate();
			((BaseEntity)player).ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
		}

		internal static void UnlockBlueprints(ulong userid, int itemid = 0)
		{
			PersistantPlayer playerInfo = SingletonComponent<ServerMgr>.Instance.persistance.GetPlayerInfo(userid);
			if (!EnsureUnlockedItemsList(playerInfo))
			{
				return;
			}

			bool hadUnlocks = playerInfo.unlockedItems.Count > 0;
			bool changed = ScrubUnsafeUnlocks(playerInfo);

			List<ItemBlueprint> blueprints = ItemManager.GetBlueprints();
			for (int i = 0; i < blueprints.Count; i++)
			{
				ItemBlueprint blueprint = blueprints[i];
				if (!IsSafeToAutoUnlock(blueprint, out ItemDefinition target))
				{
					continue;
				}
				if (itemid != 0 && target.itemid != itemid)
				{
					continue;
				}
				if (playerInfo.unlockedItems.Contains(target.itemid))
				{
					continue;
				}
				playerInfo.unlockedItems.Add(target.itemid);
				changed = true;
			}

			if (!changed)
			{
				return;
			}

			if (!hadUnlocks)
			{
				SingletonComponent<ServerMgr>.Instance.persistance.SetPlayerInfo(userid, playerInfo);
			}

			BasePlayer basePlayer = BasePlayer.FindByID(userid);
			if (basePlayer != null)
			{
				basePlayer.PersistantPlayerInfo = playerInfo;
				((BaseNetworkable)basePlayer).SendNetworkUpdateImmediate();
				((BaseEntity)basePlayer).ClientRPC(RpcTarget.Player("UnlockedBlueprint", basePlayer), 0);
			}
		}
	}

	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		if (ConVar.Server.identity != "my_server_identity")
		{
			Bootstrap_StartupShared.Initialize();
		}
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		Debug.LogWarning((object)"[Harmony] Unloaded: UnlockTier1 1.0.1.0 by nivex");
	}
}
