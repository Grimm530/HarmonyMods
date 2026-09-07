using System;
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
                Debug.LogWarning((object)"[Harmony] Loaded: UnlockTier1 1.0.0.0 by nivex");
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
			catch
			{
			}
		}

		internal static void UnlockTier(BasePlayer player, int maxTier = 1)
		{
			PersistantPlayer persistantPlayerInfo = player.PersistantPlayerInfo;
			foreach (ItemBlueprint blueprint in ItemManager.GetBlueprints())
			{
				if (blueprint.userCraftable && !blueprint.defaultBlueprint && blueprint.workbenchLevelRequired <= maxTier && !persistantPlayerInfo.unlockedItems.Contains(blueprint.targetItem.itemid))
				{
					persistantPlayerInfo.unlockedItems.Add(blueprint.targetItem.itemid);
				}
			}
            player.PersistantPlayerInfo = persistantPlayerInfo;
            ((BaseNetworkable)player).SendNetworkUpdateImmediate();
			((BaseEntity)player).ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
		}

		internal static void UnlockBlueprints(ulong userid, int itemid = 0)
		{
			PersistantPlayer playerInfo = SingletonComponent<ServerMgr>.Instance.persistance.GetPlayerInfo(userid);
			bool flag = playerInfo.unlockedItems.Count > 0;
			foreach (ItemBlueprint blueprint in ItemManager.GetBlueprints())
			{
				if (blueprint.userCraftable && !blueprint.NeedsSteamDLC && (itemid == 0 || blueprint.targetItem.itemid == itemid) && !playerInfo.unlockedItems.Contains(blueprint.targetItem.itemid))
				{
					playerInfo.unlockedItems.Add(blueprint.targetItem.itemid);
				}
			}
			if (!flag)
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
		Debug.LogWarning((object)"[Harmony] Unloaded: UnlockTier1 1.0.0.0 by nivex");
	}
}
