using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Facepunch;
using Oxide.Core.Libraries;
using UnityEngine;
using UnityEngine.Networking;
using Random = Oxide.Core.Random;
using Oxide.Plugins.QuestExtensionMethods;

namespace Oxide.Plugins
{
	[Info("Quest", "Grimm530", "8.6.8")]
	[Description("An advanced quest system for your server!")]
	public partial class Quest : RustPlugin 
	{
		#region ReferencePlugins

		[PluginReference] Plugin IQChat, Friends, Clans, EventHelper, Battles, Duel, Duelist, ArenaTournament, Notify, SkillTree, CustomVendingSetup;

		private void SendChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel = ConVar.Chat.ChatChannel.Global)
		{
			if (IQChat)
				if (_config.settingsIQChat.UIAlertUse)
					IQChat?.Call("API_ALERT_PLAYER_UI", player, message);
				else IQChat?.Call("API_ALERT_PLAYER", player, message, _config.settingsIQChat.CustomPrefix, _config.settingsIQChat.CustomAvatar);
			else player.SendConsoleCommand("chat.add", channel, 0, message);
		}

		private bool IsFriends(ulong userID, ulong targetID)
		{
			if (Friends is not null)
				return Friends.Call("HasFriend", userID, targetID) is true;
    
			return RelationshipManager.ServerInstance.playerToTeam.TryGetValue(userID, out RelationshipManager.PlayerTeam team) && team.members.Contains(targetID);
		}

		private bool IsClans(string userID, string targetID)
		{
			if (Clans)
			{
				string tagUserID = (string)Clans?.Call("GetClanOf", userID);
				string tagTargetID = (string)Clans?.Call("GetClanOf", targetID);
				if (tagUserID == null && tagTargetID == null)
				{
					return false;
				}

				return tagUserID == tagTargetID;
			}
			else
			{
				return false;
			}
		}

		private bool IsDuel(ulong userID)
		{
			object playerId = ObjectCache.Get(userID);
			BasePlayer player = null;
			if (Duel != null || Duelist != null)
				player = BasePlayer.FindByID(userID);

			object result = EventHelper?.Call("EMAtEvent", playerId);
			if (result is bool && ((bool)result) == true)
				return true;


			if (Battles != null && Battles.Call<bool>("IsPlayerOnBattle", playerId))
				return true;


			if (Duel != null && Duel.Call<bool>("IsPlayerOnActiveDuel", player))
				return true;
			if (Duelist != null && Duelist.Call<bool>("inEvent", player))
				return true;

			if (ArenaTournament != null && ArenaTournament.Call<bool>("IsOnTournament", playerId))
				return true;

			return false;
		}

		// Native image storage using FileStorage
		private Dictionary<string, uint> _imageCache = new Dictionary<string, uint>();
		
		private string GetImage(string shortname, ulong skin = 0)
		{
			string key = $"{shortname}_{skin}";
			if (_imageCache.TryGetValue(key, out uint imageId))
			{
				return imageId.ToString();
			}
			return "0"; // Return 0 for no image found
		}

		private bool AddImage(string imageName, string shortname, ulong skin = 0)
		{
			// Images are now handled by the ImageUI class loading from local files
			// This method is kept for compatibility but no longer processes URLs
			return true;
		}

		private bool HasImage(string imageName, ulong imageId = 0)
		{
			string key = $"{imageName}_{imageId}";
			return _imageCache.ContainsKey(key);
		}
		
		private uint StoreImage(byte[] imageData, string shortname, ulong skin = 0)
		{
			try
			{
				if (CommunityEntity.ServerInstance?.net?.ID == null)
					return 0;
					
				uint imageId = FileStorage.server.Store(imageData, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
				if (imageId != 0)
				{
					string key = $"{shortname}_{skin}";
					_imageCache[key] = imageId;
				}
				return imageId;
			}
			catch
			{
				return 0;
			}
		}

		#endregion

		#region Variables

		private const bool RU = false;
		
		public static Quest? Instance;
		private ImageUI _imageUI;


		
		public static Timer QuestCooldownsTimer;

		private Dictionary<long, QuestDefinition> _questList = new();

		private Dictionary<ulong, PlayerData> _playersInfo = new();
		private QuestStatistics _questStatistics = new();
		
		// Wipe detection and summary
		private DateTime _wipeStartTime = DateTime.MinValue;
		private bool _wipeSummarySent = false;

		private class CompletedQuestRecord
		{
			public long QuestID;
			public string CompletedDate;
			public bool RewardClaimed;
		}

		private class PlayerData
		{
			public List<long> CompletedQuestIds = new();
			public Dictionary<long, double> PlayerQuestCooldowns = new();
			public List<PlayerQuest> CurrentPlayerQuests = new();
			// Daily quests state
			public Dictionary<long, int> DailyProgressCounts = new Dictionary<long, int>();
			public HashSet<long> DailyCompletedToday = new HashSet<long>();
			public string LastDailyResetDate = string.Empty; // yyyy-MM-dd
			// Pinned quests state
			public List<long> PinnedQuestIds = new List<long>();
			// Completed quest history for reflection
			public List<CompletedQuestRecord> CompletedQuestHistory = new List<CompletedQuestRecord>();
			// Background image preference
			public string BackgroundImageName = "9"; // Default to image "9"

			public double? GetCooldownForQuest(long questId)
			{
				if (PlayerQuestCooldowns == null)
				{
					return null;
				}

				double cooldown;
				if (PlayerQuestCooldowns.TryGetValue(questId, out cooldown))
				{
					return cooldown;
				}

				return null;
			}
		}

		#endregion

		#region Const

		#endregion

		#region Lang

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["Quest_MissingQuests"] = "You do not have a file with tasks, the plugin will not work correctly! Create one on the Website - https://Quest.skyplugins.ru/ or use the included one.",
				["Quest_UI_TASKLIST"] = "Quest List",
				["Quest_UI_Awards"] = "Rewards",
				["Quest_UI_TASKCount"] = "{0} QUESTS",
				["Quest_UI_CHIPperformed"] = "Completed",
				["Quest_UI_CHIPInProgress"] = "In progress",
				["Quest_UI_QUESTREPEATCAN"] = "Repeatable",
				["Quest_UI_QUESTREPEATfForbidden"] = "Not Repeatable",
				["Quest_UI_NotOnCooldown"] = "Not on Cooldown",
				["Quest_UI_OnCooldown"] = "On Cooldown",
				["Quest_UI_InfoRepeatInCD"] = "{0}  |  {1}",
				["Quest_UI_QuestBtnPerformed"] = "COMPLETED",
				["Quest_UI_QuestBtnPass"] = "COMPLETE",
				["Quest_UI_QuestBtnDelivery"] = "DELIVER",
				["Quest_UI_QuestBtnRefuse"] = "REFUSE",
				["Quest_UI_ACTIVEOBJECTIVES"] = "Objective: {0}",
				["Quest_UI_MiniQLInfo"] = "{0}\nProgress: {1} / {2}\nQuest: {3}",
				["Quest_UI_MiniQLInfoDelivery"] = "{0}\nQuest: {3}",
				["Quest_UI_QuestLimit"] = "You have to many <color=#4286f4>unfinished</color> Quests",
				["Quest_UI_AlreadyTaken"] = "You have already <color=#4286f4>taken</color> this Quest!",
				["Quest_UI_NotPerm"] = "You do not have the rights to perform this Quest.",
				["Quest_UI_CategoryLocked"] = "Complete the previous category first to unlock this quest.",
				["Quest_UI_AlreadyDone"] = "You have already <color=#4286f4>completed</color> this Quest!",
				["Quest_UI_ACTIVECOLDOWN"] = "This Quest is on Cooldown.",
				["Quest_UI_LackOfSpace"] = "Your inventory is full! Clear some space and try again!",
				["Quest_UI_QuestsCompleted"] = "Quest Completed! Enjoy your reward!",
				["Quest_UI_PassedTasks"] = "So this Quest was to much for you? \n Try again later!",
				["Quest_UI_ActiveQuestCount"] = "You have no active Quests.",
				["Quest_Finished_QUEST"] = "You have completed the task: <color=#4286f4>{0}</color>",
				["Quest_Finished_QUEST_ALL"] = "Player <color=#4286f4>{0}</color> just completed a task: <color=#4286f4>{1}</color> and got a reward!",
				["Quest_UI_InsufficientResources"] = "You don't have {0}, you should definitely bring this to Sidorovich",
				["Quest_UI_InsufficientResourcesSkin"] = "You don't have the required item, you need to bring it to Sidorovich",
				["Quest_UI_NotResourcesAmount"] = "You don't have enough {0}, you need {1}",
				["Quest_UI_CATEGORY"] = "CATEGORIES",
				["Quest_UI_CATEGORY_ONE"] = "Quests",
				["Quest_UI_TASKS_LIST_EMPTY"] = "Quest list is empty",
				["Quest_UI_TASKS_INFO_EMPTY"] = "Select a task to see information about it",
				["Quest_REPEATABLE_QUEST_AVAILABLE_AGAIN"] = "You can participate in the quest \"<color=#4286f4>{0}</color>\" again! \nDon't miss your chance!",
				["Quest_STAT_1"] = "Main Statistics",
				["Quest_STAT_2"] = "**Tasks Completed:** {0}\n\n**Total Tasks Taken:** {1}\n\n**Tasks Declined:** {2}",
				["Quest_STAT_3"] = "Top 5 Tasks",
				["Quest_STAT_4"] = "**🔥 Frequently Performed Tasks:**\n{0}\n\n**❄️ Rarely Performed Tasks:**\n{1}\n",
				["Quest_STAT_5"] = "Player Quest Completions",
				["Quest_STAT_6"] = "**Recent Quest Completions:**\n{0}",
				["Quest_STAT_CMD_1"] = "Your statistics collection is disabled. Activate this feature in the configuration settings!",
				["Quest_STAT_CMD_2"] = "You haven't set a webhook. Please specify it in the configuration settings and try again!",
				["Quest_STAT_CMD_3"] = "Statistical data has been successfully sent!",
				["Quest_INSUFFICIENT_PERMISSIONS_ERROR"] = "You don't have sufficient permissions to use this command.",
				["Quest_COMMAND_SYNTAX_ERROR"] = "Incorrect syntax! Use: Quest.player.reset [steamid64]",
				["Quest_INVALID_PLAYER_ID_INPUT"] = "Invalid input! Please enter a valid player ID.",
				["Quest_NOT_A_STEAM_ID"] = "The entered ID is not a SteamID. Please check and try again.",
				["Quest_PLAYER_PROGRESS_RESET"] = "The player's progress has been successfully reset!",
				["Quest_PLAYER_NOT_FOUND_BY_STEAMID"] = "Player with the specified Steam ID not found.",

			}, this);
		}

		#endregion

		#region Configuration

		private Configuration _config;

		private class Configuration
		{
			public class Settings
			{
				[JsonProperty("Max number of concurrent quests")]
				public int questCount = 3;

				[JsonProperty("Play sound effect upon task completion")]
				public bool SoundEffect = true;

				[JsonProperty("Effect")]
				public string Effect = "assets/prefabs/locks/keypad/effects/lock.code.lock.prefab";

				[JsonProperty("Clear player progress when wipe ?")]
				public bool useWipe = true;
				[JsonProperty("Clean up player permissions when wiping?")]
				public bool useWipePermission = true;

				[JsonProperty("Quests file name")]
				public string questListDataName = "Quest";

				[JsonProperty("Commands to open quest list with progress", ObjectCreationHandling = ObjectCreationHandling.Replace)]
				public string[] questListProgress = { "qlist", "quest" };

				[JsonProperty("Notify all players on task completion?")]
				public bool sandNotifyAllPlayer = false;

				[JsonProperty("[Skill Tree] Ignore bonus from Skill Tree plugin when mining")]
				public bool UseSkillTreeIgnoreHooks = false;

				[JsonProperty("Enable debug messages in console?")]
				public bool EnableDebug = false;

				[JsonProperty("Daily quest start time (HH:mm) - when dailies are given. Server restarts at 5:00, use 5:15")]
				public string DailyStartTime = "05:15";

				[JsonProperty("Daily quest end time (HH:mm) - when day resets. Use 4:45 before next start")]
				public string DailyEndTime = "04:45";
			}

			public class SettingsIQChat
			{
				[JsonProperty("IQChat : Custom prefix in chat")]
				public string CustomPrefix = "Quest";

				[JsonProperty("IQChat : Custom chat avatar (If required)")]
				public string CustomAvatar = "0";

				[JsonProperty("IQChat : Use UI notification (true - yes/false - no)")]
				public bool UIAlertUse;
			}

			public class SettingsNotify
			{
				[JsonProperty("Enable notifications (Is required - https://codefling.com/plugins/notify)")]
				public bool useNotify = false;

				[JsonProperty("Notification Type (Is required - https://codefling.com/plugins/notify)")]
				public int typeNotify = 0;
			}

		public class StatisticsCollectionSettings
		{
			[JsonProperty("Enable statistics collection and publication to Discord?")]
			public bool useStatistics = false;

			[JsonProperty("Discord webhook for statistics publication")]
			public string discordWebhookUrl = "";

			[JsonProperty("How often to publish statistics? (Sec)")]
			public float publishFrequency = 21600;

			[JsonProperty("Include player quest completion details in Discord notifications?")]
			public bool includePlayerDetails = true;
		}

			public class SettingsUI
			{
				[JsonProperty("Header and buttons opacity (0-1, solid elements)")]
				public float HeaderButtonsAlpha = 1f;

				[JsonProperty("Background overlay opacity (0-1, main background and shade tint)")]
				public float MainBackgroundAlpha = 0.5f;
			}

			[JsonProperty("General Settings")]
			public Settings settings = new Settings();

			[JsonProperty("UI Colors (header, buttons, main background opacity)")]
			public SettingsUI settingsUI = new SettingsUI();
			
			[JsonProperty("Statistics collection settings")]
			public StatisticsCollectionSettings statisticsCollectionSettings = new StatisticsCollectionSettings();

			[JsonProperty("IQChat Settings (if applicable)")]
			public SettingsIQChat settingsIQChat = new SettingsIQChat();

			[JsonProperty("Notification Settings")]
			public SettingsNotify settingsNotify = new SettingsNotify();
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				_config = Config.ReadObject<Configuration>();
				if (_config == null)
				{
					throw new Exception();
				}
				if (_config.settingsUI == null)
					_config.settingsUI = new Configuration.SettingsUI();

				SaveConfig();
			}
			catch
			{
				for (int i = 0; i < 3; i++)
				{
					PrintError("Configuration file is corrupt! Check your config file at https://jsonlint.com/");
				}

				LoadDefaultConfig();
			}

			SaveConfig();
		}

		protected override void SaveConfig()
		{
			Config.WriteObject(_config);
		}

		protected override void LoadDefaultConfig()
		{
			_config = new Configuration();
		}

		#endregion

		#region QuestData

		private class PlayerQuest
		{
			public long ParentQuestID;
			public QuestType ParentQuestType;

			public ulong UserID;

			public bool Finished;
			public int Count;

			public void AddCount(int amount = 1)
			{
				Count += amount;
				BasePlayer player = BasePlayer.FindByID(UserID);
				QuestDefinition parentQuest = Instance._questList[ParentQuestID];
				if (parentQuest.ActionCount <= Count)
				{
					Count = parentQuest.ActionCount;
					if (player != null && player.IsConnected)
					{
						if (Instance._config.settings.SoundEffect)
						{
							Instance.RunEffect(player, Instance._config.settings.Effect);
						}

						if (Instance._config.settingsNotify.useNotify && Instance.Notify)
						{
							Instance.Notify.CallHook("SendNotify", player, Instance._config.settingsNotify.typeNotify, 
								"Quest_Finished_QUEST".GetAdaptedMessage(player.UserIDString, parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString))));
						}
						else
						{
							Instance.SendChat(player, "Quest_Finished_QUEST".GetAdaptedMessage(player.UserIDString, parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString))));
						}

						if (Instance._config.settings.sandNotifyAllPlayer)
						{
							foreach (BasePlayer players in BasePlayer.activePlayerList)
							{
								Instance.SendChat(players, "Quest_Finished_QUEST_ALL".GetAdaptedMessage( players.UserIDString, player.displayName,
									parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString))));
							}
						}

						Interface.CallHook("OnQuestCompleted", player, parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString)));
						Instance._questStatistics.GatherTaskStatistics(TaskType.TaskExecution, ParentQuestID);
						Instance._questStatistics.GatherTaskStatistics(TaskType.Completed);
					}

					Finished = true;
					// Linear progression: auto-reward and assign next quest on completion
					if (player != null && player.IsConnected && parentQuest.QuestType != QuestType.Delivery)
					{
						long qid = ParentQuestID;
						Instance.timer.Once(0.1f, () => Instance.CompleteQuestAutoReward(player, qid));
					}
				}

				if (Instance._openMiniQuestListPlayers.Contains(UserID))
					Instance.OpenMQL_CMD(player);
				
				// Update pinned quests if they exist - use a timer to ensure progress is updated first
				if (player != null && Instance._playersInfo.ContainsKey(player.userID) && 
				    Instance._playersInfo[player.userID].PinnedQuestIds != null && 
				    Instance._playersInfo[player.userID].PinnedQuestIds.Contains(ParentQuestID))
				{
					Instance.timer.Once(0.1f, () => Instance.ShowPinnedQuests(player));
				}
			}
		}

		public enum TaskType
		{
			Completed,
			Taken,
			Declined,
			TaskExecution
		}

		private enum QuestType
		{
			IQPlagueSkill,
			IQHeadReward,
			IQCases,
			OreBonus,
			ChinookIvent,
			Gather,
			EntityKill,
			Craft,
			Research,
			Loot,
			Grade,
			Swipe,
			Deploy,
			PurchaseFromNpc,
			HackCrate,
			RecycleItem,
			Growseedlings,
			RaidableBases,
			Fishing,
			BossMonster,
			HarborEvent,
			SatelliteDishEvent,
			Sputnik,
			AbandonedBases,
			Delivery,
			IQDronePatrol,
			GasStationEvent,
			Triangulation,
			FerryTerminalEvent,
			Convoy,
			Caravan,
			IQDefenderSupply,
			BigWheelBet
		}

		private enum PrizeType
		{
			Item,
			BluePrint,
			CustomItem,
			Command
		}

		private class QuestDefinition
		{
			internal class Prize
			{
				public string PrizeName;
				public PrizeType PrizeType;
				public string ItemShortName;
				public int ItemAmount;
				public string CustomItemName;
				public ulong ItemSkinID;
				public string PrizeCommand;
				public string CommandImageName;
				public bool IsHidden;
			}

			public long QuestID;
			public string QuestDisplayName;
			public string QuestDisplayNameMultiLanguage;
			public string QuestDescription;
			public string QuestDescriptionMultiLanguage;
			public string QuestMissions;
			public string QuestMissionsMultiLanguage;
			public string QuestCategory; // Optional category set in data/Quest/Quest.json

			public string QuestPermission;
			public QuestType QuestType;
			public string Target;
			public int ActionCount;
			public bool IsRepeatable;
			public bool IsMultiLanguage;
			public bool IsReturnItemsRequired;
			public int Cooldown;
			public bool IsDaily = false;
			
			[JsonIgnore]
			public bool IsMoreTarget = false;
			[JsonIgnore]
			public string[] Targets;
			public List<Prize> PrizeList = new List<Prize>();

			public string GetDisplayName(string language) => IsMultiLanguage ? QuestDisplayNameMultiLanguage : QuestDisplayName;
			public string GetDescription(string language) => IsMultiLanguage ? QuestDescriptionMultiLanguage : QuestDescription;
			public string GetMissions(string language) => IsMultiLanguage ? QuestMissionsMultiLanguage : QuestMissions;
		}

		private static int GetPrizeDisplayAmount(QuestDefinition.Prize prize)
		{
			if (prize == null) return 0;
			if (prize.PrizeType == PrizeType.Command && !string.IsNullOrEmpty(prize.PrizeCommand))
			{
				// Parse amount from command (e.g. "deposit %STEAMID% 100" or "gr.give %STEAMID% 250")
				string[] parts = prize.PrizeCommand.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				for (int i = parts.Length - 1; i >= 0; i--)
				{
					if (int.TryParse(parts[i], out int amt) && amt > 0)
						return amt;
				}
			}
			return prize.ItemAmount;
		}

		#endregion

		#region Hooks

		#region QuestHook

		#region Type Upgrade

		private object OnStructureUpgrade(BaseCombatEntity entity, BasePlayer player, BuildingGrade.Enum grade)
		{
			QuestProgress(player.userID, QuestType.Grade, ((int)grade).ToString());
			return null;
		}

		#endregion

		#region IQPlagueSkill

		private void StudySkill(BasePlayer player, string name)
		{
			QuestProgress(player.userID, QuestType.IQPlagueSkill, name);
		}

		#endregion

		#region HeadReward

		private void KillHead(BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.IQHeadReward);
		}

		#endregion

		#region IqCase

		private void OnOpenedCase(BasePlayer player, string name)
		{
			QuestProgress(player.userID, QuestType.IQCases, name);
		}

		#endregion

		#region Chinook

		private void LootHack(BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.ChinookIvent);
		}

		#endregion

		#region Gather

		#region GatherFix

		private void GatherHooksSub()
		{
			// Harmony: SkillTree does not broadcast OnSkillTreeHandleDispenser across assemblies.
			// Always use native gather patches.
			foreach (string hook in _gatherHooksSkillTree)
				Unsubscribe(hook);
			foreach (string hook in _gatherHooks)
				Subscribe(hook);
		}

		private string[] _gatherHooks =
		{
			"OnCollectiblePickedup",
			"OnDispenserGathered",
			"OnDispenserBonusReceived",
		};

		private string[] _gatherHooksSkillTree =
		{
			"STCanReceiveYield",
			"OnSkillTreeHandleDispenser",
		};
		
		
		#endregion

		private void OnDispenserGathered(ResourceDispenser dispenser, BasePlayer player, Item item)
		{
			if(player == null) return;
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}
		
		private void OnDispenserBonusReceived(ResourceDispenser dispenser, BasePlayer player, Item item) => OnDispenserGathered(dispenser, player, item);

		private void OnCollectiblePickedup(CollectibleEntity collectible, BasePlayer player, Item item)
		{
			if (player == null || item == null)
				return;
			
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}

		private void STCanReceiveYield(BasePlayer player, GrowableEntity entity, Item item)
		{
			if (player == null || item == null || item.info == null) return;
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}

		private void STCanReceiveYield(BasePlayer player, CollectibleEntity entity, ItemAmount ia)
		{
			if (player == null || ia == null || ia.itemDef == null) return;
			QuestProgress(player.userID, QuestType.Gather, ia.itemDef.shortname, "", null, (int)ia.amount);
		}

		private void OnSkillTreeHandleDispenser(BasePlayer player, BaseEntity entity, Item item)
		{
			if (player == null || item == null || item.info == null) return;
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}


		#endregion

		#region Craft

		private void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
		{
			QuestProgress(crafter.owner.userID, QuestType.Craft, task.blueprint.targetItem.shortname, "", null, item.amount);
		}

		#endregion

		#region Research

		private void OnTechTreeNodeUnlocked(Workbench workbench, TechTreeData.NodeInstance node, BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.Research, node.itemDef.shortname);
		}

		private void OnItemResearched(ResearchTable table, int amountToConsume)
		{
			QuestProgress(table.LastLootedBy, QuestType.Research, table.GetTargetItem().info.shortname);
		}

		#endregion

		#region Deploy

		private void OnEntityBuilt(Planner plan, GameObject go)
		{
			if(plan == null) return;
			BasePlayer player = plan.GetOwnerPlayer();
			if (player == null || go == null || plan.GetItem() == null)
			{
				return;
			}
			BaseEntity ent = go.ToBaseEntity();
			if (ent == null || ent.skinID == 11543256361)
			{
				return;
			}
			
			QuestProgress(player.userID, QuestType.Deploy, plan.GetItem().info.shortname);
		}

		#endregion
		
		#region Loot

		#region OnLootEntity

		private HashSet<ulong> Looted = new();
		
		private void OnEntityDestroy(BaseEntity entity)
		{
			if (entity == null) return;
			ulong net = entity.net?.ID.Value ?? 0;
			if (Looted.Contains(net))
				Looted.Remove(net);
		}
		private void OnLootEntity(BasePlayer player, BaseEntity entity)
		{
			if (entity == null || player == null)
				return;
			ulong netId = entity.net?.ID.Value ?? 0;
			if (!Looted.Add(netId))
				return;


			switch (entity)
			{
				case LootContainer lootContainer:
					if (lootContainer.inventory != null)
						QuestProgress(player.userID, QuestType.Loot, "", "", lootContainer.inventory.itemList);
					break;
				
				case LootableCorpse lootableCorpse:
					if(lootableCorpse.playerSteamID.IsSteamId())
						return;

					if (lootableCorpse.containers != null)
					{
						foreach (ItemContainer container in lootableCorpse.containers)
							if (container != null)
								QuestProgress(player.userID, QuestType.Loot, "", "", container.itemList);
					}
					break;
				
				case DroppedItemContainer droppedItemContainer:
					if(droppedItemContainer.prefabID != 1519640547 || droppedItemContainer.playerSteamID.IsSteamId())
						return;

					if (droppedItemContainer.inventory != null)
						QuestProgress(player.userID, QuestType.Loot, "", "", droppedItemContainer.inventory.itemList);
					break;
			}
		}
		
		private void OnContainerDropItems(ItemContainer container)
		{
			if (container == null || container.entityOwner == null)
				return;

			string prefabName = container.entityOwner.ShortPrefabName;
			if (prefabName == null || (!prefabName.Contains("barrel") && !prefabName.Contains("roadsign")))
				return;

			if (container.entityOwner is LootContainer lootContainer)
			{
				ulong netId = lootContainer.net?.ID.Value ?? 0;
				if (!Looted.Add(netId))
					return;

				if (lootContainer.lastAttacker is BasePlayer player)
				{
					QuestProgress(player.userID, QuestType.Loot, "", "", lootContainer.inventory.itemList);
				}
			}
		}

		#endregion

		#endregion

		#region Swipe

		private void OnCardSwipe(CardReader cardReader, Keycard card, BasePlayer player)
		{
			if (card == null || cardReader == null || player == null) return;
			if (!cardReader.HasFlag(BaseEntity.Flags.On) && card.accessLevel == cardReader.accessLevel)
				QuestProgress(player.userID, QuestType.Swipe, card.accessLevel.ToString());
		}

		#endregion

		#region EntityKill
		
		private void OnPlayerDeath(BasePlayer player, HitInfo info)
		{
			if (player == null || info == null || !player.userID.IsSteamId())
				return;
			BasePlayer attacker = info.InitiatorPlayer;
			if (attacker == null)
				return;

			if (IsFriends(player.userID.Get(), attacker.userID.Get()) || IsClans(player.UserIDString, attacker.UserIDString) || IsDuel(attacker.userID.Get()) || player.userID == attacker.userID)
				return;

			QuestProgress(attacker.userID, QuestType.EntityKill, "player");
		}
		
		private Dictionary<NetworkableId, ulong> heliCashed = new();
		private void OnPatrolHelicopterKill(PatrolHelicopter entity, HitInfo info)
		{
			if (entity == null || info == null || info.InitiatorPlayer == null)
				return;

			BasePlayer player = info.InitiatorPlayer;
			if (player.userID.IsSteamId())
			{
				heliCashed[entity.net.ID] = player.userID;
			}
		}
        
		private void OnEntityKill(PatrolHelicopter entity)
		{
			if (entity == null || entity.net == null)
				return;

			if (heliCashed.TryGetValue(entity.net.ID, out ulong playerId))
			{
				QuestProgress(playerId, QuestType.EntityKill, entity.ShortPrefabName.ToLowerInvariant());
				heliCashed.Remove(entity.net.ID);
			}
			else
			{
				if (entity.myAI != null && entity.myAI._targetList is { Count: > 0 } targetList)
				{
					BasePlayer player = targetList[^1].ply;

					if (player != null && player.userID.IsSteamId())
					{
						QuestProgress(player.userID, QuestType.EntityKill, entity.ShortPrefabName.ToLowerInvariant());
					}
				}
			}
		}
		
		private static List<string> excludedNames = new()
		{
			"corpse", "servergibs", "player", "rug.bear.deployed"
		};
		
		private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
		{
			try
			{ 
				if (entity == null || info == null)
					return;

				string targetName = entity.ShortPrefabName;
				
				if (excludedNames.Contains(targetName))
					return;

				if (targetName == "testridablehorse")
					targetName = "horse";

				BasePlayer player = info.InitiatorPlayer;

				if (entity.GetComponent<PatrolHelicopter>() != null)
					return;
        
				if (player != null && !player.IsNpc && entity.ToPlayer() != player)
					QuestProgress(player.userID, QuestType.EntityKill, targetName.ToLower());
			}
			catch (Exception ex)
			{
				Debug.LogError($"Error while handling entity death: {ex.Message}");
			}
		}


		#endregion

		#region NPC Purchases
		
		void OnCustomVendingSetupGiveSoldItem(NPCVendingMachine machine, Item soldItem, BasePlayer buyer)
		{
			QuestProgress(buyer.userID, QuestType.PurchaseFromNpc, soldItem.info.shortname, "", null, soldItem.amount);
		}

		void OnNpcGiveSoldItem(NPCVendingMachine machine, Item soldItem, BasePlayer buyer)
		{
			if (CustomVendingSetup?.Call("API_IsCustomized", machine) is true)
				return;

			QuestProgress(buyer.userID, QuestType.PurchaseFromNpc, soldItem.info.shortname, "", null, soldItem.amount);
		}

		#endregion

		#region BigWheel Gambling

		private void OnBigWheelWin(BigWheelGame bigWheel, Item scrap, BigWheelBettingTerminal terminal, int multiplier)
		{
			if (terminal?.lastPlayer == null || scrap == null) return;
			QuestProgress(terminal.lastPlayer.userID, QuestType.BigWheelBet, "scrap", "", null, scrap.amount);
		}

		private void OnBigWheelLoss(BigWheelGame bigWheel, Item scrap, BigWheelBettingTerminal terminal)
		{
			if (terminal?.lastPlayer == null || scrap == null) return;
			QuestProgress(terminal.lastPlayer.userID, QuestType.BigWheelBet, "scrap", "", null, scrap.amount);
		}

		#endregion

		#region Crate Hack

		private void OnCrateHack(HackableLockedCrate crate)
		{
			if (crate.originalHackerPlayerId.IsSteamId())
			{
				QuestProgress(crate.originalHackerPlayerId, QuestType.HackCrate);
			}
		}

		#endregion

		#region RecycleItem

		private Dictionary<ulong, BasePlayer> _recyclePlayer = new();

		private void OnRecyclerToggle(Recycler recycler, BasePlayer player)
		{
			if (!recycler.IsOn())
			{
				if (!_recyclePlayer.TryAdd(recycler.net.ID.Value, player))
				{
					_recyclePlayer.Remove(recycler.net.ID.Value);
					_recyclePlayer.Add(recycler.net.ID.Value, player);
				}
			}
			else if (_recyclePlayer.ContainsKey(recycler.net.ID.Value))
			{
				_recyclePlayer.Remove(recycler.net.ID.Value);
			}
		}
		
		private void OnItemRecycle(Item item, Recycler recycler)
		{
			BasePlayer value;
			if (_recyclePlayer.TryGetValue(recycler.net.ID.Value, out value))
			{
				int num2 = 1;
				if (item.amount > 1)
				{
					num2 = Mathf.CeilToInt(Mathf.Min(item.amount, item.info.stackable * 0.1f));
				}
				QuestProgress(value.userID, QuestType.RecycleItem, item.info.shortname, "", null, num2);
			}
		}

		#endregion

		#region Growseedlings

		private void OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.Growseedlings, item.info.shortname, "", null, item.amount);
		}

		#endregion

		#region Raidable Bases (Nivex)

		private void OnRaidableBaseCompleted(Vector3 location, int mode, bool allowPVP, string id, float spawnTime, float despawnTime, float loadingTime, ulong ownerId, BasePlayer owner,
			List<BasePlayer> raiders)
		{
			BasePlayer player = owner ? owner : (raiders?.Count != 0 ? raiders[0] : null);
			if (player != null)
			{
				QuestProgress(player.userID, QuestType.RaidableBases, mode.ToString(), "", null);
			}
		}

		#endregion

		#region Fishing

		private void OnFishCatch(Item fish, BaseFishingRod fishingRod, BasePlayer player)
		{
			if (player == null || fish == null)
				return;

			QuestProgress(player.userID, QuestType.Fishing, fish.info.shortname, "", null, fish.amount);
		}

		#endregion

		#region BossMonster

		private void OnBossKilled(ScientistNPC boss, BasePlayer attacker)
		{
			if (boss == null || attacker == null)
				return;

			QuestProgress(attacker.userID, QuestType.BossMonster, boss.displayName, "", null);
		}

		#endregion

		#region HarborEvent

		private void OnHarborEventWinner(ulong winnerId)
		{
			QuestProgress(winnerId, QuestType.HarborEvent);
		}

		#endregion

		#region SatelliteDishEvent

		private void OnSatDishEventWinner(ulong winnerId)
		{
			QuestProgress(winnerId, QuestType.SatelliteDishEvent);
		}

		#endregion

		#region Sputnik

		private void OnSputnikEventWin(ulong userID)
		{
			QuestProgress(userID, QuestType.Sputnik);
		}

		#endregion

		#region AbandonedBases

		private void OnAbandonedBaseEnded(Vector3 center, bool allowPVP, List<BasePlayer> intruders)
		{
			if (intruders.Count <= 0)
				return;

			foreach (BasePlayer player in intruders)
			{
				QuestProgress(player.userID, QuestType.AbandonedBases);
			}
		}

		#endregion

		#region IQDronePatrol

		private void OnDroneKilled(BasePlayer player, Drone drone, string KeyDrone)
		{
			if (player == null || drone == null)
				return;

			QuestProgress(player.userID, QuestType.IQDronePatrol, KeyDrone, "", null);
		}

		#endregion

		#region IQDefenderSupply

		private void OnLootedDefenderSupply(BasePlayer player, int levelDropInt)
		{
			if (player == null)
				return;
			
			QuestProgress(player.userID, QuestType.IQDefenderSupply, levelDropInt.ToString(), "", null);
		}

		#endregion

		#region GasStationEvent

		private void OnGasStationEventWinner(ulong userID)
		{
			QuestProgress(userID, QuestType.GasStationEvent);
		}

		#endregion

		#region Triangulation 

		private void OnTriangulationWinner(ulong userID)
		{
			QuestProgress(userID, QuestType.Triangulation);
		}

		#endregion

		#region FerryTerminalEvent

		private void OnFerryTerminalEventWinner(ulong userID)
		{
			QuestProgress(userID, QuestType.FerryTerminalEvent);
		}

		#endregion

		#region Convoy

		private void OnConvoyEventWin(ulong userID)
		{
			QuestProgress(userID, QuestType.Convoy);
		}

		#endregion

		#region Caravan
		
		private void OnCaravanEventWin(ulong userID)
		{
			QuestProgress(userID, QuestType.Caravan);
		}

		#endregion

		#endregion

		private void OnNewSave()
		{
			// Send wipe summary before clearing data
			if (_config.statisticsCollectionSettings.useStatistics && !string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl) && !_wipeSummarySent)
			{
				SendWipeSummaryReport();
				_wipeSummarySent = true;
			}

			if (_config.settings.useWipe)
			{
				_playersInfo?.Clear();
				SaveData();
			}

			if (_config.settings.useWipePermission)
			{
				ClearPermission();
			}

			_wipeStartTime = DateTime.UtcNow;
			_wipeSummarySent = false;
		}


		private void Init()
		{
			Instance = this;
			LoadPlayerData();
			LoadQuestStatisticsData();
			LoadQuestData();
		}
		
		private void OnServerInitialized()
		{
			if (_questList.Count == 0)
			{
				PrintError("Quest_MissingQuests".GetAdaptedMessage());
				return;
			}

			foreach (string cmds in _config.settings.questListProgress)
				cmd.AddChatCommand(cmds, this, nameof(OpenMQL_CMD));

			// Images are now loaded by ImageUI class from local files
			GatherHooksSub();

			_imageUI = new ImageUI();
			_imageUI.DownloadImage();

			// Initialize wipe start time from database file creation date
			InitializeWipeStartTime();

			foreach (BasePlayer player in BasePlayer.activePlayerList)
				OnPlayerConnected(player);
            
			QuestCooldownsTimer = timer.Every(70f, CheckQuestCooldowns);
			timer.Every(60f, CheckDailyQuestWindow);

			if (_config.statisticsCollectionSettings.useStatistics && !string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl))
			{
				timer.Every(_config.statisticsCollectionSettings.publishFrequency, GrabAndPostStatistics);
			}
		}

		private void InitializeWipeStartTime()
		{
			if (_wipeStartTime != DateTime.MinValue)
				return; // Already set

			try
			{
				// Try to get the creation time of the PlayerInfo.json file
				string playerInfoPath = Interface.Oxide.DataFileSystem.GetFile($"{Name}/PlayerInfo").Filename;
				if (System.IO.File.Exists(playerInfoPath))
				{
					DateTime fileCreationTime = System.IO.File.GetCreationTimeUtc(playerInfoPath);
					_wipeStartTime = fileCreationTime;
					PrintWarning($"Wipe start time set to database file creation time: {_wipeStartTime:yyyy-MM-dd HH:mm:ss} UTC");
					return;
				}

				// Fallback to QuestStatistics.json if PlayerInfo doesn't exist
				string questStatsPath = Interface.Oxide.DataFileSystem.GetFile($"{Name}/QuestStatistics").Filename;
				if (System.IO.File.Exists(questStatsPath))
				{
					DateTime fileCreationTime = System.IO.File.GetCreationTimeUtc(questStatsPath);
					_wipeStartTime = fileCreationTime;
					PrintWarning($"Wipe start time set to QuestStatistics file creation time: {_wipeStartTime:yyyy-MM-dd HH:mm:ss} UTC");
					return;
				}

				// If no database files exist, use current time
				_wipeStartTime = DateTime.UtcNow;
				PrintWarning("No database files found. Wipe start time set to current time.");
			}
			catch (Exception ex)
			{
				PrintError($"Error initializing wipe start time: {ex.Message}");
				_wipeStartTime = DateTime.UtcNow;
				PrintWarning("Wipe start time set to current time due to error.");
			}
		}

		private void CheckQuestCooldowns()
		{
			foreach (BasePlayer player in BasePlayer.activePlayerList)
				CheckPlayerCooldowns(player);
		}

		private string GetCurrentDailyDate()
		{
			DateTime now = DateTime.Now;
			if (!DateTime.TryParseExact(_config.settings.DailyStartTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime startParsed))
				startParsed = new DateTime(1, 1, 1, 5, 15, 0);
			int startMinutes = startParsed.Hour * 60 + startParsed.Minute;
			int nowMinutes = now.Hour * 60 + now.Minute;
			if (nowMinutes >= startMinutes)
				return now.ToString("yyyy-MM-dd");
			return now.AddDays(-1).ToString("yyyy-MM-dd");
		}

		private void CheckDailyQuestWindow()
		{
			foreach (BasePlayer player in BasePlayer.activePlayerList)
				CheckDailyQuestWindowForPlayer(player);
		}

		private void CheckDailyQuestWindowForPlayer(BasePlayer player)
		{
			if (player == null || !player.IsConnected) return;
			if (!_playersInfo.TryGetValue(player.userID, out PlayerData pd)) return;

			string currentDailyDate = GetCurrentDailyDate();
			if (string.Equals(pd.LastDailyResetDate, currentDailyDate, StringComparison.Ordinal))
				return;

			DailyResetPlayer(player, pd);
			AssignDailiesToPlayer(player, pd);
			pd.LastDailyResetDate = currentDailyDate;
			SaveData();
		}

		private void DailyResetPlayer(BasePlayer player, PlayerData pd)
		{
			List<PlayerQuest> toRemove = new List<PlayerQuest>();
			foreach (PlayerQuest pq in pd.CurrentPlayerQuests)
			{
				if (_questList.TryGetValue(pq.ParentQuestID, out QuestDefinition q) && q.IsDaily)
					toRemove.Add(pq);
			}
			foreach (PlayerQuest pq in toRemove)
				pd.CurrentPlayerQuests.Remove(pq);
			pd.DailyProgressCounts.Clear();
			pd.DailyCompletedToday.Clear();
		}

		private void AssignDailiesToPlayer(BasePlayer player, PlayerData pd)
		{
			foreach (QuestDefinition q in _questList.Values)
			{
				if (!q.IsDaily) continue;
				if (pd.DailyCompletedToday.Contains(q.QuestID)) continue;
				if (pd.CurrentPlayerQuests.Exists(pq => pq.ParentQuestID == q.QuestID)) continue;
				if (!string.IsNullOrEmpty(q.QuestPermission) && !permission.UserHasPermission(player.UserIDString, $"{Name}.{q.QuestPermission}")) continue;

				pd.CurrentPlayerQuests.Add(new PlayerQuest { UserID = player.userID, ParentQuestID = q.QuestID, ParentQuestType = q.QuestType });
				pd.DailyProgressCounts[q.QuestID] = 0;
			}
		}

		private void CheckPlayerCooldowns(BasePlayer player)
		{
			PlayerData playerData;
			if (_playersInfo.TryGetValue(player.userID, out playerData))
			{
				List<long> questsToRemove = Pool.Get<List<long>>();

				foreach (KeyValuePair<long, double> cooldownForQuest in playerData.PlayerQuestCooldowns)
				{
					if (CurrentTime() >= cooldownForQuest.Value + 30f)
					{
						questsToRemove.Add(cooldownForQuest.Key);

						if (_questList.TryGetValue(cooldownForQuest.Key, out QuestDefinition quest))
						{
							string userId = player.UserIDString;
							SendChat(player, "Quest_REPEATABLE_QUEST_AVAILABLE_AGAIN".GetAdaptedMessage(userId, quest.GetDisplayName(lang.GetLanguage(userId))));
						}
					}
				}

				foreach (long questId in questsToRemove)
					playerData.PlayerQuestCooldowns.Remove(questId);
				
				Pool.FreeUnmanaged(ref questsToRemove);
			}
		}

		private void OnPlayerConnected(BasePlayer player)
		{
			ulong UserId = player.userID.Get();
			PlayerData playerData;
			if (!_playersInfo.TryGetValue(UserId, out playerData))
			{
				_playersInfo.Add(UserId, new PlayerData());
				NextTick(() =>
				{
					CheckDailyQuestWindowForPlayer(player);
					EnsureLinearQuestsAssigned(player);
				});
			}
			else
			{
				List<PlayerQuest> questsToRemove = new();

				foreach (PlayerQuest item in playerData.CurrentPlayerQuests)
				{
					KeyValuePair<long, QuestDefinition>? currentQuest = null;

					foreach (KeyValuePair<long, QuestDefinition> pair in _questList)
					{
						if (pair.Key == item.ParentQuestID && pair.Value.QuestType == item.ParentQuestType)
						{
							currentQuest = pair;
							break;
						}
					}

					// Daily quests stay in CurrentPlayerQuests (auto-assigned at start time)
					if (currentQuest.HasValue && currentQuest.Value.Value.IsDaily)
						continue;
					// Remove invalid quests
					if (!currentQuest.HasValue)
					{
						questsToRemove.Add(item);
					}
				}

				NextTick(() =>
				{
					foreach (PlayerQuest questToRemove in questsToRemove)
					{
						playerData.CurrentPlayerQuests.Remove(questToRemove);
					}

					CheckPlayerCooldowns(player);
					CheckDailyQuestWindowForPlayer(player);
					EnsureLinearQuestsAssigned(player);
				});
			}
		}

		private void OnServerSave()
		{
			timer.Once(10f, SaveData);
		}

		private void OnPlayerDisconnected(BasePlayer player)
		{
			ulong UserId = player.userID.Get();

			_openMiniQuestListPlayers.Remove(UserId);
		}

		private void OnServerShutdown() => Unload();

		private void Unload()
		{
			if (IsObjectNull(Instance))
				return;

			if (!IsObjectNull(QuestCooldownsTimer))
			{
				QuestCooldownsTimer.Destroy();
			}

			if (_imageUI != null)
			{
				_imageUI.UnloadImages();
				_imageUI = null;
			}

			// destroy client-side scroller UI for all connected players
			foreach (var p in BasePlayer.activePlayerList)
			{
				try
				{
					SendClientUi(p, "[{\\\"destroyUi\\\":\\\"Q_AccordionScroll\\\"}]", logPayloadInDebug: false);
					CuiHelper.DestroyUi(p, "Q_AccordionScroll");
					CuiHelper.DestroyUi(p, "Q_Scroller");
				}
				catch {}
			}

			Instance = null;
			QuestCooldownsTimer = null;
			SaveData();
			ClearPlayersData();
		}

		#endregion

		#region HelpMetods

		#region HelpUnload

		private void UnloadWithMessage(string message)
		{
			NextTick(() =>
			{
				PrintError(message);
				Interface.Oxide.UnloadPlugin(Name);
			});
		}

		private void ClearPlayersData()
		{
			foreach (BasePlayer p in BasePlayer.activePlayerList)
			{
				CuiHelper.DestroyUi(p, MINI_QUEST_LIST);
				CuiHelper.DestroyUi(p, LAYERS);
				CuiHelper.DestroyUi(p, "Q_RewardRow");
			}
		}

		#endregion

		private static bool IsObjectNull(object obj) => ReferenceEquals(obj, null);

		private static string GetFileNameWithoutExtension(string filePath)
		{
			int lastDirectorySeparatorIndex = filePath.LastIndexOfAny(new[] { '\\', '/' });
			int lastDotIndex = filePath.LastIndexOf('.');

			if (lastDotIndex > lastDirectorySeparatorIndex)
			{
				return filePath.Substring(lastDirectorySeparatorIndex + 1, lastDotIndex - lastDirectorySeparatorIndex - 1);
			}

			return filePath.Substring(lastDirectorySeparatorIndex + 1);
		}

		private void RunEffect(BasePlayer player, string path)
		{
			Effect effect = new Effect();
			Transform transform = player.transform;
			effect.Init(Effect.Type.Generic, transform.position, transform.forward);
			effect.pooledString = path;
			EffectNetwork.Send(effect, player.net.connection);
		}
		
		private void ClearPermission()
		{
			string[] allPermissions = permission.GetPermissions();
			const string permissionPrefix = "Quest.";

			foreach (string perm in allPermissions)
			{
				if (perm.Equals($"{permissionPrefix}default", StringComparison.OrdinalIgnoreCase))
					continue;

				if (perm.StartsWith(permissionPrefix, StringComparison.OrdinalIgnoreCase))
				{
					string[] usersWithPermission = permission.GetPermissionUsers(perm);

					foreach (string userEntry in usersWithPermission)
					{
						string steamId = ExtractSteamId(userEntry);
						permission.RevokeUserPermission(steamId, perm);
					}
				}
			}
		}

		private string ExtractSteamId(string userEntry)
		{
			int separatorIndex = userEntry.IndexOf('(');
			return separatorIndex > 0 ? userEntry[..separatorIndex] : userEntry;
		}

		private static class TimeHelper
		{
			public static string FormatTime(TimeSpan time, int maxSubstr = 5, string language = "en")
			{
				List<string> parts = new List<string>();
				
				if (time.Days > 0)
				{
					parts.Add($"{time.Days} day{(time.Days == 1 ? string.Empty : "s")}");
				}
				
				if (time.Hours > 0)
				{
					parts.Add($"{time.Hours} hour{(time.Hours == 1 ? string.Empty : "s")}");
				}
				
				if (time.Minutes > 0)
				{
					parts.Add($"{time.Minutes} minute{(time.Minutes == 1 ? string.Empty : "s")}");
				}
				
				if (time.Seconds > 0)
				{
					parts.Add($"{time.Seconds} second{(time.Seconds == 1 ? string.Empty : "s")}");
				}
				
				if (parts.Count == 0)
				{
					parts.Add("0 seconds");
				}
				
				return string.Join(", ", parts);
			}

			private static string Format(int units, string form)
			{
				return $"{units}{form}";
			}
		}

		private static double CurrentTime()
		{
			return Facepunch.Math.Epoch.Current;
		}

		private void Log(string msg, string file)
		{
			LogToFile(file, $"[{DateTime.Now}] {msg}", this);
		}

		#endregion

		#region UI

		private List<ulong> _openMiniQuestListPlayers = new();
		// Remember which tab the player last selected: true = Accepted, false = Available
		private readonly Dictionary<ulong, bool> _playerLastTabAccepted = new Dictionary<ulong, bool>();
		// Track if the player last selected the Daily tab
		private readonly Dictionary<ulong, bool> _playerLastTabDaily = new Dictionary<ulong, bool>();
		// Track if the player last selected the Completed tab
		private readonly Dictionary<ulong, bool> _playerLastTabCompleted = new Dictionary<ulong, bool>();
		private const string MINI_QUEST_LIST = "Mini_QuestList";
		private const string LAYERS = "UI_QuestMain";
		private const string LAYER_MAIN_BACKGROUND = "UI_QuestMainBackground";
		// Light grey border for button bars - ensures visibility against any background
		private const string UI_BUTTON_BORDER_COLOR = "0.24 0.24 0.27 1";
		// In-game menu blur material - matches vanilla CONTACTS/CRAFTING tab look (translucent, textured)
		private const string UI_BUTTON_MATERIAL = "assets/content/ui/uibackgroundblur-ingamemenu.mat";
		private const string Quest_CATEGORY_MAIN = "Quest_CATEGORY_MAIN";

		// --- TEMP category scaffolding ---
		private enum UIListMode { Categories, Quests }
		private UIListMode _listMode = UIListMode.Categories;
		private int _selectedCategoryIndex = -1; // -1 means: none picked yet

		// Returns anchor Y-min/Y-max strings for a row, evenly spaced inside [top..bottom].
		private static (string yMin, string yMax) UiRowAnchors(int rowIndex, int rows, float top = 0.88f, float bottom = 0.10f, float gap = 0.006f)
		{
			// rowIndex: 0 at top
			float band = (top - bottom) / rows;
			float yMax = top - rowIndex * band;
			float yMin = yMax - band + gap; // small gap between rows
			return (yMin.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
					yMax.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
		}

		#region MainUI

		// Dynamic category map shown in the accordion UI
		private readonly Dictionary<string, List<string>> _accordionCategories = new Dictionary<string, List<string>>();

		// Progressive category order: players must complete each category before the next unlocks (natural game progression)
		private static readonly string[] PROGRESSIVE_CATEGORY_ORDER = new[]
		{
			"Gathering", "Scavenging", "Crafting", "Building", "Access", "Hunter", "Combat",
			"Market", "Raiding", "Events"
		};

		// Linear progression: these categories are unlocked from start (first quest only)
		private static readonly HashSet<string> EARLY_CATEGORIES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Gathering", "Scavenging", "Crafting", "Building", "Access", "Hunter", "Combat"
		};

		// These unlock only when all EARLY_CATEGORIES are complete
		private static readonly HashSet<string> LATE_CATEGORIES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Market", "Raiding", "Events"
		};

		// Fallback mapping to preserve current grouping when QuestCategory is not set in data
		private static readonly Dictionary<string, List<string>> _defaultCategoryMapping = new Dictionary<string, List<string>>
		{
			["Gathering"] = new List<string>{
				"Mushroom Mayhem","Forest on the spot!","Leather Gatherer","You light up my life!","Hemp Harvester","Skull Collector",
				"Wood Gatherer","Clean up your mess!","HQM Gatherer","Sulfur Gatherer","Stone Gatherer","Metal Gatherer"
			},
			["Combat"] = new List<string>{
				"Helicopter Destroyer","Bradley Brawler","Storming the cosmodrome",
				"Turret Take Down","Chicken Hunt!","Jeepers Creepers"
			},
			["Hunter"] = new List<string>{
				"Wolf Hunter","Wolf Hunter II","Boar Hunter","Boar Hunter II","Bear Hunter","Bear Hunter II","Stag Hunter","Stag Hunter II"
			},
			["Raiding"] = new List<string>{"Easy Raider","Medium Raider","Hard Raider","Expert Raider","Nightmare Raider"},
			["Scavenging"] = new List<string>{
				"Always watching!","Burn Baby Burn","Grinding Gears","Tarp Looter","Target Aquired","Springs have Sprung","Pile of Pipes",
				"Feeling Techy","Apple A Day!","Medical Mayhem","Cat Food","The Magical Fruit","Chocolate Carnage","Take it all at once!"
			},
			["Access"] = new List<string>{
				"Ruler of red doors!","Swiper no Swiping! \"Green\"","Swiper no Swiping! \"Blue\"","Swiper no Swiping! \"Red\"",
				"Hacker Man!","This fucking box needs to be hacked!","SatelliteDishEvent","HarborEvent","AbandonedBases","Convoy Event Completion"
			},
			["Market"] = new List<string>{"We don't have enough components","A lot of rubbish has accumulated ..."},
			["Crafting"] = new List<string>{
				"It's time to fight!","Hazard Pay","Excuse me, nurse?","Ready for Raiding","Ready for Raiding II","Purge Preparation",
				"Extra Windy","Break it Down","Furnace Frenzy","Getting Started","Farm Step 2: Plant","Fish Frenzy","Dive, dive, dive!",
				"Stocking the fridge!","Warm and Cozy","Extra Sleepy","Pants Party!","Reload","Reload II","Light it Up!"
			},
			["Building"] = new List<string>{
				"Full home protection is needed!","Improved doors and windows, but forgot about the frame of the house?"
			},
			["Events"] = new List<string>{
				"Sky Dominator","Bradley Buster"
			},
		};

		private void BuildCategoriesFromData()
		{
			_accordionCategories.Clear();
			_categoryOrder.Clear();
			// Group quests by explicit QuestCategory
			foreach (QuestDefinition q in _questList.Values)
			{
				string display = q.GetDisplayName(lang.GetServerLanguage());
				if (!string.IsNullOrEmpty(q.QuestCategory))
				{
					if (!_accordionCategories.TryGetValue(q.QuestCategory, out var list))
					{
						list = new List<string>();
						_accordionCategories[q.QuestCategory] = list;
					}
					if (!list.Contains(display)) list.Add(display);
				}
			}
			// Use progressive category order: categories appear in game progression order
			// Daily is excluded - shown only via Daily tab, not in accordion
			HashSet<string> seen = new HashSet<string>();
			foreach (string cat in PROGRESSIVE_CATEGORY_ORDER)
			{
				if (string.Equals(cat, "Daily", StringComparison.OrdinalIgnoreCase)) continue;
				if (_accordionCategories.ContainsKey(cat) && seen.Add(cat))
					_categoryOrder.Add(cat);
			}
			// Append any categories not in progressive order (e.g. custom/uncategorized) at the end
			foreach (string cat in _accordionCategories.Keys)
			{
				if (string.Equals(cat, "Daily", StringComparison.OrdinalIgnoreCase)) continue;
				if (seen.Add(cat))
					_categoryOrder.Add(cat);
			}
		}

		private void PersistQuestCategoriesIfMissing()
		{
			bool changed = false;
			// Assign QuestCategory from default mapping if not provided in data
			foreach (var kv in _defaultCategoryMapping)
			{
				string category = kv.Key;
				foreach (string questName in kv.Value)
				{
					foreach (var q in _questList.Values)
					{
						if (!string.IsNullOrEmpty(q.QuestCategory))
							continue;
						string display = q.GetDisplayName(lang.GetServerLanguage());
						if (string.Equals(display, questName, StringComparison.OrdinalIgnoreCase))
						{
							q.QuestCategory = category;
							changed = true;
							break;
						}
					}
				}
			}

			if (changed)
			{
				// Persist back to data file preserving current list content
				List<QuestDefinition> toSave = new List<QuestDefinition>(_questList.Values);
				Interface.Oxide.DataFileSystem.WriteObject($"{Name}/{_config.settings.questListDataName}", toSave);
			}
		}

		private void PersistQuestOrderByUi()
		{
			try
			{
				// Dynamic ordering: categories appear by first occurrence in the data file, and within each category
				// quests appear by their display sequence in the accordion list (which mirrors data order).
				string langCode = lang.GetServerLanguage();
				List<QuestDefinition> ordered = new List<QuestDefinition>();
				HashSet<long> added = new HashSet<long>();
				// Categories in dynamic order
				foreach (string cat in _categoryOrder)
				{
					List<string> names;
					if (!_accordionCategories.TryGetValue(cat, out names))
						continue;
					foreach (string name in names)
					{
						foreach (var q in _questList.Values)
						{
							if (added.Contains(q.QuestID)) continue;
							if (!string.Equals(q.QuestCategory, cat, StringComparison.OrdinalIgnoreCase)) continue;
							if (string.Equals(q.GetDisplayName(langCode), name, StringComparison.OrdinalIgnoreCase))
							{
								ordered.Add(q);
								added.Add(q.QuestID);
								break;
							}
						}
					}
				}
				// Append any quests without a category or not listed in accordion, preserving their original load order
				foreach (long id in _loadedQuestOrder)
				{
					if (!added.Contains(id) && _questList.TryGetValue(id, out var q))
						ordered.Add(q);
				}

				Interface.Oxide.DataFileSystem.WriteObject($"{Name}/{_config.settings.questListDataName}", ordered);
			}
			catch { }
		}

		private readonly Dictionary<ulong, HashSet<string>> _playerExpandedCategories = new Dictionary<ulong, HashSet<string>>();

		private readonly Dictionary<ulong, Dictionary<string, int>> _playerCategoryScrollIndex = new Dictionary<ulong, Dictionary<string, int>>();

		// Dynamic category order (preserves first-seen order from data file)
		private readonly List<string> _categoryOrder = new List<string>();
		// Original quest order as loaded from data (by QuestID)
		private readonly List<long> _loadedQuestOrder = new List<long>();

		// Per-player desired vertical scroll position [0..1] for the accordion ScrollView
		private readonly Dictionary<ulong, float> _playerScrollPosition = new Dictionary<ulong, float>();
		

		private static string TruncateWithoutCut(string value, int maxChars)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
				return value;
			int cut = value.LastIndexOf(' ', Math.Min(maxChars, value.Length));
			if (cut <= 0)
				cut = maxChars;
			string trimmed = value.Substring(0, cut).TrimEnd();
			return trimmed + "…";
		}

		// Returns true if the player has completed every non-daily quest in this category at least once
		private bool IsCategoryComplete(ulong playerId, string categoryName)
		{
			if (!_playersInfo.TryGetValue(playerId, out PlayerData pd) || !_accordionCategories.TryGetValue(categoryName, out var questNames))
				return false;
			string langCode = lang.GetServerLanguage();
			foreach (var q in _questList.Values)
			{
				if (q.IsDaily) continue;
				if (!string.Equals(q.QuestCategory ?? "", categoryName, StringComparison.OrdinalIgnoreCase)) continue;
				if (!pd.CompletedQuestIds.Contains(q.QuestID) && !pd.PlayerQuestCooldowns.ContainsKey(q.QuestID))
					return false; // not completed and not on cooldown (repeatable just finished)
			}
			return true;
		}

		// Returns true if the category is locked
		// Admins see all categories unlocked. Early categories are always unlocked; Raiding & Economy unlock when early categories are complete.
		private bool IsCategoryLocked(ulong playerId, string categoryName)
		{
			if (permission.UserHasPermission(playerId.ToString(), "quest.admin"))
				return false;
			if (EARLY_CATEGORIES.Contains(categoryName))
				return false;
			if (!LATE_CATEGORIES.Contains(categoryName))
				return false; // Unknown category - allow
			// Late category: locked until all early categories are complete
			foreach (string early in EARLY_CATEGORIES)
			{
				if (!_accordionCategories.ContainsKey(early)) continue;
				if (!IsCategoryComplete(playerId, early))
					return true;
			}
			return false;
		}

		// Returns the first quest in this category that the player has not completed (by _loadedQuestOrder)
		private QuestDefinition GetNextQuestInCategory(ulong playerId, string categoryName)
		{
			var pd = _playersInfo.TryGetValue(playerId, out var p) ? p : null;
			if (pd == null) return null;
			foreach (long qid in _loadedQuestOrder)
			{
				if (!_questList.TryGetValue(qid, out QuestDefinition q)) continue;
				if (q.IsDaily) continue;
				if (!string.Equals(q.QuestCategory ?? "", categoryName, StringComparison.OrdinalIgnoreCase)) continue;
				if (pd.CompletedQuestIds.Contains(q.QuestID)) continue;
				if (pd.PlayerQuestCooldowns.ContainsKey(q.QuestID)) continue;
				return q;
			}
			return null;
		}

		// Auto-assign first quest of each unlocked category (linear progression - no manual accept)
		private void EnsureLinearQuestsAssigned(BasePlayer player)
		{
			if (player == null || !player.IsConnected) return;
			if (!_playersInfo.TryGetValue(player.userID, out var pd)) return;
			if (pd.CurrentPlayerQuests == null) pd.CurrentPlayerQuests = new List<PlayerQuest>();

			foreach (string cat in _categoryOrder)
			{
				if (string.Equals(cat, "Daily", StringComparison.OrdinalIgnoreCase)) continue;
				if (IsCategoryLocked(player.userID, cat)) continue;

				QuestDefinition next = GetNextQuestInCategory(player.userID, cat);
				if (next == null) continue;
				if (pd.CurrentPlayerQuests.Exists(pq => pq.ParentQuestID == next.QuestID)) continue;

				pd.CurrentPlayerQuests.Add(new PlayerQuest { UserID = player.userID, ParentQuestID = next.QuestID, ParentQuestType = next.QuestType });
			}
		}

		private int GetPlayerLevel(BasePlayer player)
		{
			if (SkillTree == null || !SkillTree.IsLoaded) return 0;
			var result = SkillTree.Call("GetPlayerLevel", player);
			return result is int level ? level : 0;
		}

		private void MainUi(BasePlayer player)
		{
			int playerLevel = GetPlayerLevel(player);
			CuiElementContainer container = new CuiElementContainer
			{
				// Full-screen main panel - fit exactly to screen (cursor/input)
				{
					new CuiPanel
					{
						CursorEnabled = true,
						Image = { Color = "1 1 1 0" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
					},
					"Overlay",
					LAYERS
				},
				// Main background - full-screen shade (theme color + opacity from config)
				new CuiElement
				{
					Name = LAYER_MAIN_BACKGROUND,
					Parent = LAYERS,
					Components =
					{
						new CuiImageComponent { Color = GetBackgroundColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
					}
				},
				// Header: solid name badge (full opacity) with border
				new CuiElement
				{
					Name = "Q_Header_Border",
					Parent = LAYER_MAIN_BACKGROUND,
					Components =
					{
						new CuiImageComponent { Color = UI_BUTTON_BORDER_COLOR, Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0 0.92", AnchorMax = "1 0.99" }
					}
				},
				new CuiElement
				{
					Name = "Q_Header",
					Parent = "Q_Header_Border",
					Components =
					{
						new CuiImageComponent { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
					}
				},
				// Steam avatar
				new CuiElement
				{
					Name = "Q_Avatar",
					Parent = "Q_Header",
					Components =
					{
						new CuiRawImageComponent { SteamId = player.UserIDString, Color = "1 1 1 1" },
						new CuiRectTransformComponent { AnchorMin = "0.01 0.15", AnchorMax = "0.055 0.85" }
					}
				},
				// Player name + level (added below)
				// Tab bar (Quests, Daily, Completed - no Accepted)
				{
					new CuiElement
					{
						Name = "Q_TabAvailable_Border",
						Parent = LAYER_MAIN_BACKGROUND,
						Components =
						{
							new CuiImageComponent { Color = UI_BUTTON_BORDER_COLOR, Sprite = "assets/content/ui/ui.background.tile.psd" },
							new CuiRectTransformComponent { AnchorMin = "0.02 0.84", AnchorMax = "0.32 0.90" }
						}
					}
				},
				{
					new CuiElement
					{
						Name = "Q_TabAvailable_BG",
						Parent = "Q_TabAvailable_Border",
						Components =
						{
							new CuiImageComponent { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
							new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
						}
					}
				},
				{
					new CuiButton
					{
						Button = { Command = "UI_Handler accordion", Color = "0 0 0 0" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
						Text = { Text = "Quests", Align = TextAnchor.MiddleCenter, FontSize = 16, Color = "1 1 1 1" }
					},
					"Q_TabAvailable_BG",
					"Q_TabAvailable"
				},
				{
					new CuiElement
					{
						Name = "Q_TabDaily_Border",
						Parent = LAYER_MAIN_BACKGROUND,
						Components =
						{
							new CuiImageComponent { Color = UI_BUTTON_BORDER_COLOR, Sprite = "assets/content/ui/ui.background.tile.psd" },
							new CuiRectTransformComponent { AnchorMin = "0.34 0.84", AnchorMax = "0.64 0.90" }
						}
					}
				},
				{
					new CuiElement
					{
						Name = "Q_TabDaily_BG",
						Parent = "Q_TabDaily_Border",
						Components =
						{
							new CuiImageComponent { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
							new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
						}
					}
				},
				{
					new CuiButton
					{
						Button = { Command = "UI_Handler daily", Color = "0 0 0 0" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
						Text = { Text = "Daily", Align = TextAnchor.MiddleCenter, FontSize = 16, Color = "1 1 1 1" }
					},
					"Q_TabDaily_BG",
					"Q_TabDaily"
				},
				{
					new CuiElement
					{
						Name = "Q_TabCompleted_Border",
						Parent = LAYER_MAIN_BACKGROUND,
						Components =
						{
							new CuiImageComponent { Color = UI_BUTTON_BORDER_COLOR, Sprite = "assets/content/ui/ui.background.tile.psd" },
							new CuiRectTransformComponent { AnchorMin = "0.66 0.84", AnchorMax = "0.98 0.90" }
						}
					}
				},
				{
					new CuiElement
					{
						Name = "Q_TabCompleted_BG",
						Parent = "Q_TabCompleted_Border",
						Components =
						{
							new CuiImageComponent { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
							new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
						}
					}
				},
				{
					new CuiButton
					{
						Button = { Command = "UI_Handler completed", Color = "0 0 0 0" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
						Text = { Text = "Completed", Align = TextAnchor.MiddleCenter, FontSize = 16, Color = "1 1 1 1" }
					},
					"Q_TabCompleted_BG",
					"Q_TabCompleted"
				},

				// Main content area (left = categories/quests, right = quest details)
				{
					new CuiPanel
					{
						Image = { Color = "0 0 0 0" },
						RectTransform = { AnchorMin = "0.04 0.02", AnchorMax = "0.96 0.82" }
					},
					LAYER_MAIN_BACKGROUND,
					"Q_Scroller"
				},
				// Main title (added below)
				// Close button (top-right of header)
				new CuiElement
				{
					Name = "CloseUIImageBR",
					Parent = "Q_Header",
					Components =
					{
						new CuiImageComponent { Color = "0.6 0.2 0.1 1", Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0.94 0.2", AnchorMax = "0.99 0.8" }
					}
				},
				{
					new CuiButton
					{
						Button = { Color = "0 0 0 0", Command = "CloseMainUI" },
						Text = { Text = "Close", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
					},
					"CloseUIImageBR",
					"BtnCloseUIBR"
				}

		};
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.07 0.2", AnchorMax = "0.35 0.8" },
				Text = { Text = $"{player.displayName}\nLVL {playerLevel}", Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleLeft, Color = "0.95 0.93 0.88 1" }
			}, "Q_Header", "Q_PlayerInfo");
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.04 0.72", AnchorMax = "0.32 0.76" },
				Text = { Text = "QUEST LOG", Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "0.95 0.93 0.88 1" }
			}, LAYER_MAIN_BACKGROUND, "Q_MainTitle");
			CuiHelper.DestroyUi(player, "UI_QuestMain");
			CuiHelper.AddUi(player, container);

			// Color swatches and top-layer elements
			CuiElementContainer topClose = new CuiElementContainer();
			
			// Add color swatches to top layer so they render on top of everything
			AddColorSwatchesToContainer(topClose, LAYER_MAIN_BACKGROUND, player);
			
			CuiHelper.AddUi(player, topClose);
			// Start with accordion categories view (all collapsed by default)
			if (!_playerExpandedCategories.ContainsKey(player.userID))
				_playerExpandedCategories[player.userID] = new HashSet<string>();
			RenderAccordion(player);
		}

		private string GetPlayerBackgroundImage(BasePlayer player)
		{
			if (player == null || !_playersInfo.ContainsKey(player.userID))
				return "9"; // Default
			
			string bgImage = _playersInfo[player.userID].BackgroundImageName;
			if (string.IsNullOrEmpty(bgImage))
				return "9"; // Default
			
			return bgImage;
		}

		private string GetPlayerButtonBackgroundImage(BasePlayer player)
		{
			// Use the same background image as the main panel, but with darker overlay
			return GetPlayerBackgroundImage(player);
		}

		private string GetButtonBackgroundColor(BasePlayer player)
		{
			return GetSolidColorForImage(GetPlayerBackgroundImage(player));
		}

		private void AddColorSwatchesToContainer(CuiElementContainer container, string parent, BasePlayer player)
		{
			// Available background images: 9 (default), gradient_red, gradient_purple, gradient_green, gradient_blue
			string[] backgroundImages = { "9", "gradient_red", "gradient_purple", "gradient_green", "gradient_blue" };
			string currentBg = GetPlayerBackgroundImage(player);
			
			// Calculate positions: bottom left corner, larger swatches for visibility
			// Making them 0.015f x 0.015f (approximately 30x30 pixels on 1920x1080)
			float swatchWidth = 0.015f;  // Larger width for visibility
			float swatchHeight = 0.015f; // Larger height for visibility
			float startX = 0.10f; // Position to the right of the close button (which is at 0.02-0.08)
			float startY = 0.02f; // At the bottom, same level as close button
			float spacing = 0.003f; // Spacing between swatches
			
			for (int i = 0; i < backgroundImages.Length; i++)
			{
				string imageName = backgroundImages[i];
				float xMin = startX + (i * (swatchWidth + spacing));
				float xMax = xMin + swatchWidth;
				float yMin = startY;
				float yMax = yMin + swatchHeight;
				
				// Determine color for the swatch based on image name
				string swatchColor = GetSwatchColor(imageName);
				bool isSelected = (imageName == currentBg);
				
				// Create swatch background (slightly larger with border if selected)
				if (isSelected)
				{
					// Selected border (slightly larger)
					container.Add(new CuiElement
					{
						Name = $"Q_Swatch_Border_{i}",
						Parent = parent,
						Components =
						{
							new CuiImageComponent { Color = "1 1 1 1" },
							new CuiRectTransformComponent { AnchorMin = $"{xMin - 0.002f} {yMin - 0.002f}", AnchorMax = $"{xMax + 0.002f} {yMax + 0.002f}" }
						}
					});
				}
				
				// Create swatch button with color
				container.Add(new CuiElement
				{
					Name = $"Q_Swatch_{i}",
					Parent = parent,
					Components =
					{
						new CuiImageComponent { Color = swatchColor },
						new CuiRectTransformComponent { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
					}
				});
				
				// Create clickable button overlay
				container.Add(new CuiButton
				{
					Button = { Command = $"UI_Handler setbg {imageName}", Color = "0 0 0 0" },
					RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" },
					Text = { Text = "", FontSize = 1, Color = "0 0 0 0" }
				}, parent, $"Q_Swatch_Button_{i}");
			}
		}

		private string GetSolidColorForImage(string imageName)
		{
			float a = Mathf.Clamp01(_config?.settingsUI?.HeaderButtonsAlpha ?? 1f);
			// RGB for header and buttons - dark tint of selected color
			string rgb;
			switch (imageName.ToLower())
			{
				case "9": rgb = "0.10 0.10 0.10"; break;
				case "gradient_red": rgb = "0.20 0.08 0.08"; break;
				case "gradient_purple": rgb = "0.12 0.06 0.18"; break;
				case "gradient_green": rgb = "0.08 0.18 0.08"; break;
				case "gradient_blue": rgb = "0.06 0.10 0.22"; break;
				default: rgb = "0.10 0.10 0.10"; break;
			}
			return $"{rgb} {a}";
		}

		private string GetBackgroundColorForImage(string imageName)
		{
			float raw = Mathf.Clamp01(_config?.settingsUI?.MainBackgroundAlpha ?? 0.5f);
			// Apply curve so 0.9→1.0 transition is gradual (pow 0.6: 0.9→0.94, 1.0→1.0)
			float a = Mathf.Pow(raw, 0.6f);
			string rgb;
			switch (imageName.ToLower())
			{
				case "9": rgb = "0.12 0.12 0.12"; break;
				case "gradient_red": rgb = "0.22 0.10 0.10"; break;
				case "gradient_purple": rgb = "0.14 0.08 0.20"; break;
				case "gradient_green": rgb = "0.10 0.20 0.10"; break;
				case "gradient_blue": rgb = "0.08 0.12 0.25"; break;
				default: rgb = "0.12 0.12 0.12"; break;
			}
			return $"{rgb} {a}";
		}

		private string GetSwatchColor(string imageName)
		{
			// Return color representation for each background image (for swatch display)
			switch (imageName.ToLower())
			{
				case "9":
					return "0.5 0.5 0.5 1"; // Gray for default
				case "gradient_red":
					return "0.8 0.2 0.2 1"; // Red
				case "gradient_purple":
					return "0.6 0.2 0.8 1"; // Purple
				case "gradient_green":
					return "0.2 0.8 0.2 1"; // Green
				case "gradient_blue":
					return "0.2 0.4 0.8 1"; // Blue
				default:
					return "0.5 0.5 0.5 1"; // Default gray
			}
		}

		private void SendClientUi(BasePlayer player, string json, bool logPayloadInDebug = true)
		{
			if (CommunityEntity.ServerInstance == null)
			{
				PrintWarning("[Q] AddUI aborted: CommunityEntity.ServerInstance is null");
				return;
			}
			if (string.IsNullOrEmpty(json))
				return;
			if (logPayloadInDebug)
			{
				int len = json.Length;
				int head = Math.Min(160, len);
				DebugPuts($"[Q] AddUI send len={len} head='{json.Substring(0, head)}'");
			}
			CommunityEntity.ServerInstance.ClientRPC(RpcTarget.Player("AddUI", player), json);
		}

		private void DebugPuts(string message)
		{
			if (_config?.settings?.EnableDebug == true)
				Puts(message);
		}

		private void RenderAccordion(BasePlayer player, bool onlyTaken = false, UICategory? category = null)
		{
			// destroy existing scroll if any (CUI + ClientUI)
			CuiHelper.DestroyUi(player, "Q_AccordionScroll");
			SendClientUi(player, "[{\"destroyUi\":\"Q_AccordionScroll\"}]");

			// Debug: entering accordion render
			try
			{
				int expandedCount = _playerExpandedCategories.ContainsKey(player.userID) ? _playerExpandedCategories[player.userID].Count : 0;
				DebugPuts($"[Q] RenderAccordion start: expanded={expandedCount}");
			}
			catch { }

			// Preflight: create a tiny debug panel to confirm AddUI works
			string preflight = "[" +
				"{\\\"name\\\":\\\"Q_Preflight\\\",\\\"parent\\\":\\\"Overlay\\\",\\\"components\\\":[" +
				"{\\\"type\\\":\\\"UnityEngine.UI.Image\\\",\\\"color\\\":\\\"0 0 0 0.01\\\"}," +
				"{\\\"type\\\":\\\"RectTransform\\\",\\\"anchormin\\\":\\\"0.01 0.01\\\",\\\"anchormax\\\":\\\"0.02 0.02\\\"}" +
				"]}" +
				"]";
			SendClientUi(player, preflight);
			// schedule destroy preflight
			NextTick(() => SendClientUi(player, "[{\\\"destroyUi\\\":\\\"Q_Preflight\\\"}]"));

			// Build CUI ScrollView under Q_Scroller (avoid client AddUI conflicts)
			CuiHelper.DestroyUi(player, "Q_CUI_Scroll");
			CuiHelper.DestroyUi(player, "Q_CUI_Scroll_Content");
			var cui = new CuiElementContainer();

		// Prepare category map: Available tab shows only quests available to the player;
		// Accepted tab shows only currently taken quests.
		Dictionary<string, List<string>> categoriesToRender = new Dictionary<string, List<string>>();
		// If caller did not explicitly choose, default to last selected tab
		if (!onlyTaken && _playerLastTabAccepted.TryGetValue(player.userID, out bool lastAccepted) && lastAccepted)
		{
			onlyTaken = true;
		}
		// Compute the set of quest IDs to show in this view
		HashSet<long> includedQuestIds = new HashSet<long>();
		bool isDaily = _playerLastTabDaily.ContainsKey(player.userID) && _playerLastTabDaily[player.userID];
		bool isCompleted = _playerLastTabCompleted.ContainsKey(player.userID) && _playerLastTabCompleted[player.userID];
		bool showAvailableQuests = !onlyTaken && !isDaily && !isCompleted;
		
		if (onlyTaken)
		{
			List<PlayerQuest> playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests != null)
			{
				foreach (PlayerQuest pq in playerQuests)
					includedQuestIds.Add(pq.ParentQuestID);
			}
		}
		else if (isDaily)
		{
			foreach (QuestDefinition q in GetQuestsByCategory(UICategory.Daily, player.userID))
				includedQuestIds.Add(q.QuestID);
		}
		else if (isCompleted)
		{
			foreach (QuestDefinition q in GetQuestsByCategory(UICategory.Completed, player.userID))
				includedQuestIds.Add(q.QuestID);
		}
		else
		{
			if (permission.UserHasPermission(player.UserIDString, "quest.admin"))
			{
				// Admin view: show all non-daily quests for organization
				foreach (QuestDefinition q in _questList.Values)
				{
					if (q.IsDaily) continue;
					if (!string.IsNullOrEmpty(q.QuestPermission) && !permission.UserHasPermission(player.UserIDString, $"{Name}." + q.QuestPermission))
						continue;
					includedQuestIds.Add(q.QuestID);
				}
			}
			else
			{
				foreach (QuestDefinition q in GetQuestsByCategory(UICategory.Available, player.userID))
					includedQuestIds.Add(q.QuestID);
			}
		}
		string playerLanguage = lang.GetLanguage(player.UserIDString);
		
		// Special handling for Completed tab - create a single "Completed Quests" category
		if (isCompleted)
		{
			List<string> completedQuestNames = new List<string>();
			foreach (long qid in includedQuestIds)
			{
				QuestDefinition q;
				if (_questList.TryGetValue(qid, out q))
				{
					completedQuestNames.Add(q.GetDisplayName(playerLanguage));
				}
			}
			if (completedQuestNames.Count > 0)
			{
				categoriesToRender["Completed Quests"] = completedQuestNames;
			}
		}
		else if (isDaily)
		{
			// Special handling for Daily tab - create "Daily" category with quest buttons on left
			List<string> dailyQuestNames = new List<string>();
			foreach (long qid in includedQuestIds)
			{
				QuestDefinition q;
				if (_questList.TryGetValue(qid, out q))
				{
					dailyQuestNames.Add(q.GetDisplayName(playerLanguage));
				}
			}
			if (dailyQuestNames.Count > 0)
			{
				categoriesToRender["Daily"] = dailyQuestNames;
			}
		}
		else
		{
			// Build categories in progressive order; for Available tab, locked categories show with no quests
			// Exclude Daily from accordion - it's shown via the Daily tab
			foreach (string cat in _categoryOrder)
			{
				if (string.Equals(cat, "Daily", StringComparison.OrdinalIgnoreCase)) continue;
				if (showAvailableQuests && IsCategoryLocked(player.userID, cat))
				{
					categoriesToRender[cat] = new List<string>(); // Show category row as LOCKED (empty, no quests)
					continue;
				}
				// Linear progression: show only 1 quest per category (the next/current one)
				QuestDefinition nextQuest = GetNextQuestInCategory(player.userID, cat);
				if (nextQuest != null && includedQuestIds.Contains(nextQuest.QuestID))
				{
					categoriesToRender[cat] = new List<string> { nextQuest.GetDisplayName(playerLanguage) };
				}
				else
				{
					// Fallback: first matching quest from includedQuestIds (for admin or edge cases)
					List<string> listForCat = null;
					for (int i = 0; i < _loadedQuestOrder.Count; i++)
					{
						long qid = _loadedQuestOrder[i];
						if (!includedQuestIds.Contains(qid)) continue;
						QuestDefinition q;
						if (!_questList.TryGetValue(qid, out q)) continue;
						string qc = string.IsNullOrEmpty(q.QuestCategory) ? "Uncategorized" : q.QuestCategory;
						if (!string.Equals(qc, cat, StringComparison.Ordinal)) continue;
						categoriesToRender[cat] = new List<string> { q.GetDisplayName(playerLanguage) };
						break; // Only 1 per category
					}
				}
			}
			// Append uncategorized at the end (1 quest only, preserving load order)
			{
				for (int i = 0; i < _loadedQuestOrder.Count; i++)
				{
					long qid = _loadedQuestOrder[i];
					if (!includedQuestIds.Contains(qid)) continue;
					QuestDefinition q;
					if (!_questList.TryGetValue(qid, out q)) continue;
					if (!string.IsNullOrEmpty(q.QuestCategory)) continue;
					categoriesToRender["Uncategorized"] = new List<string> { q.GetDisplayName(playerLanguage) };
					break; // Only 1 per category
				}
			}
		}

			// Calculate how many rows we'll render (categories + expanded children)
			int rowHeight = 28;
			int gapBetweenRows = 4; // Small gap between each rectangle
			int slotHeight = rowHeight + gapBetweenRows;
			int totalRows = 0;
			bool hasExpandedState = _playerExpandedCategories.ContainsKey(player.userID);
			
			// For Completed tab, auto-expand the "Completed Quests" category
			if (isCompleted && categoriesToRender.ContainsKey("Completed Quests"))
			{
				if (!hasExpandedState)
					_playerExpandedCategories[player.userID] = new HashSet<string>();
				_playerExpandedCategories[player.userID].Add("Completed Quests");
				hasExpandedState = true;
			}
			// For Daily tab, auto-expand the "Daily" category so quest buttons show
			if (isDaily && categoriesToRender.ContainsKey("Daily"))
			{
				if (!hasExpandedState)
					_playerExpandedCategories[player.userID] = new HashSet<string>();
				_playerExpandedCategories[player.userID].Add("Daily");
				hasExpandedState = true;
			}
			
			foreach (var kv in categoriesToRender)
			{
				// one for category itself
				totalRows++;
				// add children if expanded
				if (hasExpandedState && _playerExpandedCategories[player.userID].Contains(kv.Key))
					totalRows += kv.Value.Count;
			}

			// Add extra padding to prevent text cutoff at the bottom
			int bottomPadding = 40; // Extra space at the bottom to prevent cutoff
			int contentHeight = Math.Max(slotHeight * Math.Max(1, totalRows) + bottomPadding, 300);
			DebugPuts($"[Q] RenderAccordion sizing: rowHeight={rowHeight} totalRows={totalRows} contentHeight={contentHeight}");

			float initialVNorm = 1f;
			if (_playerScrollPosition.TryGetValue(player.userID, out float savedNorm))
			{
				initialVNorm = Mathf.Clamp01(savedNorm);
			}

			// Left panel: categories accordion (each row is its own rectangle, gaps between)
			cui.Add(new CuiElement
			{
				Parent = "Q_Scroller",
				Name = "Q_CUI_Scroll",
				Components =
				{
					new CuiScrollViewComponent { Vertical = true, Horizontal = false, ContentTransform = new CuiRectTransform { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{contentHeight}", OffsetMax = "0 0", Pivot = "0 1" }, VerticalNormalizedPosition = initialVNorm },
					new CuiRectTransformComponent { AnchorMin = "0.02 0.12", AnchorMax = "0.26 0.88" },
					new CuiImageComponent { Color = "0 0 0 0" }
				}
			});

			// add rows (categories + expanded children)
			int added = 0;
			foreach (var kv in categoriesToRender)
			{
				string categoryName = kv.Key;
				bool forAvailableTab = !onlyTaken && !isDaily && !isCompleted;
				bool locked = forAvailableTab && IsCategoryLocked(player.userID, categoryName);
				bool completed = forAvailableTab && IsCategoryComplete(player.userID, categoryName);
				int totalInCat = _accordionCategories.TryGetValue(categoryName, out var catList) ? catList.Count : 0;
				int activeInCat = 0; // 1 if player has a quest in this category, 0 otherwise
				if (isDaily && categoryName == "Daily")
				{
					totalInCat = kv.Value.Count;
					if (_playersInfo.TryGetValue(player.userID, out var pdata) && pdata.DailyCompletedToday != null)
					{
						foreach (var q in GetQuestsByCategory(UICategory.Daily, player.userID))
							if (!pdata.DailyCompletedToday.Contains(q.QuestID)) activeInCat++; // not yet done = active
					}
				}
				else if (forAvailableTab && totalInCat > 0 && _playersInfo.TryGetValue(player.userID, out var pdata) && pdata.CurrentPlayerQuests != null)
				{
					// 1 if player has a quest from this category assigned (not finished)
					QuestDefinition next = GetNextQuestInCategory(player.userID, categoryName);
					if (next != null && pdata.CurrentPlayerQuests.Exists(pq => pq.ParentQuestID == next.QuestID && !pq.Finished))
						activeInCat = 1;
				}
				string displayText = categoryName;
				if (locked) displayText = TruncateWithoutCut($"{categoryName} – LOCKED", 28);
				else if (completed) displayText = TruncateWithoutCut($"{categoryName} ✓", 26);
				else if (totalInCat > 0) displayText = TruncateWithoutCut($"{categoryName} ({activeInCat}/{totalInCat})", 28);
				int yTop = -added * slotHeight;
				string rowId = $"Q_CUI_Cat_{categoryName.GetHashCode()}";
				string rowBorderId = rowId + "_B";
				cui.Add(new CuiElement
				{
					Parent = "Q_CUI_Scroll",
					Name = rowBorderId,
					Components =
					{
						new CuiImageComponent { Color = UI_BUTTON_BORDER_COLOR, Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"4 {yTop - rowHeight}", OffsetMax = $"-4 {yTop}", SetParent = "Content", Pivot = "0 1" }
					}
				});
				cui.Add(new CuiElement
				{
					Parent = rowBorderId,
					Name = rowId,
					Components =
					{
						new CuiImageComponent { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
					}
				});
				if (locked)
				{
					cui.Add(new CuiLabel
					{
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "8 0", OffsetMax = "0 0" },
						Text = { Text = displayText, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.6 0.6 0.6 1" }
					}, rowId);
				}
				else
				{
					cui.Add(new CuiButton
					{
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "8 0", OffsetMax = "0 0" },
						Button = { Color = "0 0 0 0", Command = $"UI_Handler togglecat {EscapeArg(categoryName)}" },
						Text = { Text = displayText, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = completed ? "0.6 1 0.6 1" : "1 1 1 1" }
					}, rowId);
				}
				added++;

				// children
				bool expanded = hasExpandedState && _playerExpandedCategories[player.userID].Contains(categoryName);
				if (expanded)
				{
					foreach (var questName in kv.Value)
					{
						int childYTop = -added * slotHeight;
						string childId = $"Q_CUI_Child_{(categoryName + questName).GetHashCode()}";
						string childBorderId = childId + "_B";
						cui.Add(new CuiElement
						{
							Parent = "Q_CUI_Scroll",
							Name = childBorderId,
							Components =
							{
								new CuiImageComponent { Color = UI_BUTTON_BORDER_COLOR, Sprite = "assets/content/ui/ui.background.tile.psd" },
								new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"4 {childYTop - rowHeight}", OffsetMax = $"-4 {childYTop}", SetParent = "Content", Pivot = "0 1" }
							}
						});
						cui.Add(new CuiElement
						{
							Parent = childBorderId,
							Name = childId,
							Components =
							{
								new CuiImageComponent { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
								new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
							}
						});
						cui.Add(new CuiButton
						{
							RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "24 0", OffsetMax = "0 0" },
							Button = { Color = "0 0 0 0", Command = $"UI_Handler questbyname {EscapeArg(questName)}" },
							Text = { Text = TruncateWithoutCut(questName, 26), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" }
						}, childId);
						added++;
					}
				}
			}
			CuiHelper.AddUi(player, cui);
			DebugPuts("[Q] RenderAccordion applied CUI scroll view and rows");
			UpdateTasksCount(player, added);

			// Do NOT create our own content; use the ScrollView's built-in Content

			// Admin-only: overlay a small debug label on the scroll view to inspect layering
			if (player?.net?.connection != null && player.net.connection.authLevel >= 2)
			{
				var dbg = new System.Text.StringBuilder();
				dbg.Append("[");
				dbg.Append("{\"parent\":\"Q_AccordionScroll\",\"components\":[");
				dbg.Append("{\"type\":\"UnityEngine.UI.Text\",\"text\":\"SV#1\",\"fontSize\":12,\"align\":\"UpperLeft\",\"color\":\"1 0.8 0 1\"},");
				dbg.Append("{\"type\":\"RectTransform\",\"anchormin\":\"0 1\",\"anchormax\":\"0 1\",\"offsetmin\":\"4 -40\",\"offsetmax\":\"120 -20\"}");
				dbg.Append("]}");
				dbg.Append("]");
				SendClientUi(player, dbg.ToString());
			}

			return;
		}

		// Calculates a best-effort normalized vertical scroll position so that the given
		// category header row appears near the top after re-render.
		private float CalculateScrollToCategory(string categoryName, ulong playerId)
		{
			try
			{
				int rowHeight = 28;
				int totalRows = 0;
				int targetRowIndex = 0;
				bool found = false;
				bool hasExpanded = _playerExpandedCategories.ContainsKey(playerId);
				foreach (var kv in _accordionCategories)
				{
					// count this category row
					if (!found)
						targetRowIndex = totalRows;
					totalRows++;
					if (string.Equals(kv.Key, categoryName, StringComparison.OrdinalIgnoreCase))
					{
						found = true;
					}
					// include children rows for expanded categories (using current expanded state)
					if (hasExpanded && _playerExpandedCategories[playerId].Contains(kv.Key))
						totalRows += kv.Value.Count;
				}

				if (!found || totalRows <= 1)
					return 1f; // default to top

				// Map row index to normalized position (1=top, 0=bottom)
				float norm = 1f - (float)targetRowIndex / (float)(Mathf.Max(1, totalRows - 1));
				return Mathf.Clamp01(norm);
			}
			catch { return 1f; }
		}

		// Destroys legacy quest UI panels to prevent overlap with the new accordion UI
		// NOTE: Does NOT destroy QuestInfoPanel or its children - those are still in use!
		private void ClearLegacyQuestUi(BasePlayer player)
		{
			// Only destroy truly legacy panels from the old list-based UI
			// The QuestInfoPanel (right side quest details) is still actively used
			CuiHelper.DestroyUi(player, "QuestListPanel");
			CuiHelper.DestroyUi(player, "Q_ListFooter");
			CuiHelper.DestroyUi(player, "Q_Divider");
			CuiHelper.DestroyUi(player, "Quest");
			CuiHelper.DestroyUi(player, "Previous");
			CuiHelper.DestroyUi(player, "Next");
			
		// Also ensure any client-side scroll remnants are removed
		SendClientUi(player, "[{\"destroyUi\":\"Q_AccordionScroll\"}]");
		DebugPuts("[Q] Cleared legacy quest list UI panels (preserved quest info panel)");
	}

		// Linear progression: auto-reward on completion (regular + daily)
		private void CompleteQuestAutoReward(BasePlayer player, long questID)
		{
			if (player == null || !player.IsConnected || !_playersInfo.TryGetValue(player.userID, out var playerData))
				return;

			PlayerQuest finishedQuest = null;
			if (playerData.CurrentPlayerQuests != null)
			{
				foreach (PlayerQuest pq in playerData.CurrentPlayerQuests)
				{
					if (pq.ParentQuestID == questID && pq.Finished) { finishedQuest = pq; break; }
				}
			}
			if (finishedQuest == null) return;

			if (!_questList.TryGetValue(questID, out QuestDefinition quest)) return;

			int prizeCount = 0;
			foreach (QuestDefinition.Prize prize in quest.PrizeList)
				if (prize.PrizeType != PrizeType.Command) prizeCount++;
			if (24 - player.inventory.containerMain.itemList.Count < prizeCount)
			{
				UINottice(player, "Quest_UI_LackOfSpace".GetAdaptedMessage(player.UserIDString));
				return;
			}

			GiveQuestReward(player, quest.PrizeList);

			if (quest.IsDaily)
			{
				playerData.DailyCompletedToday.Add(questID);
				playerData.DailyProgressCounts.Remove(questID);
			}
			else
			{
				if (!quest.IsRepeatable)
				{
					if (!playerData.CompletedQuestIds.Contains(questID))
						playerData.CompletedQuestIds.Add(questID);
				}
				else if (quest.Cooldown > 0)
				{
					playerData.PlayerQuestCooldowns[questID] = CurrentTime() + quest.Cooldown;
				}
				string cat = quest.QuestCategory ?? "";
				if (!string.IsNullOrEmpty(cat))
				{
					QuestDefinition next = GetNextQuestInCategory(player.userID, cat);
					if (next != null && !playerData.CurrentPlayerQuests.Exists(pq => pq.ParentQuestID == next.QuestID))
						playerData.CurrentPlayerQuests.Add(new PlayerQuest { UserID = player.userID, ParentQuestID = next.QuestID, ParentQuestType = next.QuestType });
				}
			}

			if (playerData.CompletedQuestHistory == null)
				playerData.CompletedQuestHistory = new List<CompletedQuestRecord>();
			playerData.CompletedQuestHistory.Add(new CompletedQuestRecord
			{
				QuestID = questID,
				CompletedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
				RewardClaimed = true
			});

			playerData.CurrentPlayerQuests.Remove(finishedQuest);

			SaveData();
			ClearLegacyQuestUi(player);
			RenderAccordion(player);
			CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
			if (_openMiniQuestListPlayers.Contains(player.userID))
			{
				CuiHelper.DestroyUi(player, MINI_QUEST_LIST);
				UIMiniQuestList(player);
			}
			RenderCompletedTab(player);
		}

	private void HandleClaimReward(BasePlayer player, long questID)
	{
		if (!_playersInfo.ContainsKey(player.userID))
			return;

		PlayerData playerData = _playersInfo[player.userID];
		
		// Find the finished quest in CurrentPlayerQuests
		PlayerQuest finishedQuest = playerData.CurrentPlayerQuests?.Find(pq => pq.ParentQuestID == questID && pq.Finished);
		if (finishedQuest == null)
		{
			UINottice(player, "Quest not found or not completed!");
			return;
		}

		QuestDefinition quest = _questList[questID];
		if (quest == null)
			return;

		// Check inventory space
		int count = 0;
		foreach (QuestDefinition.Prize prize in quest.PrizeList)
			if (prize.PrizeType != PrizeType.Command)
				count++;

		if (24 - player.inventory.containerMain.itemList.Count < count)
		{
			UINottice(player, "Quest_UI_LackOfSpace".GetAdaptedMessage(player.UserIDString));
			return;
		}

		// Give rewards
		GiveQuestReward(player, quest.PrizeList);
		
		// Mark quest completion
		if (!quest.IsRepeatable)
		{
			if (!playerData.CompletedQuestIds.Contains(questID))
			{
				playerData.CompletedQuestIds.Add(questID);
			}
		}
		else if (quest.Cooldown > 0)
		{
			playerData.PlayerQuestCooldowns[questID] = CurrentTime() + quest.Cooldown;
		}

		// Move quest to CompletedQuestHistory
		if (playerData.CompletedQuestHistory == null)
		{
			playerData.CompletedQuestHistory = new List<CompletedQuestRecord>();
		}
		
		playerData.CompletedQuestHistory.Add(new CompletedQuestRecord
		{
			QuestID = questID,
			CompletedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
			RewardClaimed = true
		});

		// Remove from CurrentPlayerQuests
		playerData.CurrentPlayerQuests.Remove(finishedQuest);

		UINottice(player, "Rewards claimed successfully!");

		// Refresh the completed tab to show updated state
		RenderCompletedTab(player);
	}

	private void RenderCompletedTab(BasePlayer player)
	{
		DebugPuts($"[Q] RenderCompletedTab called for {player.displayName}");
		
		// Remove previous tab's accordion content so QUEST LOG area is clean
		CuiHelper.DestroyUi(player, "Q_AccordionScroll");
		CuiHelper.DestroyUi(player, "Q_CUI_Scroll");
		CuiHelper.DestroyUi(player, "Q_CUI_Scroll_Content");
		CuiHelper.DestroyUi(player, "Q_EmptyCompleted");

		if (!_playersInfo.ContainsKey(player.userID))
		{
			DebugPuts($"[Q] No player data found for {player.displayName}");
			return;
		}

		var completedQuests = GetQuestsByCategory(UICategory.Completed, player.userID);
		DebugPuts($"[Q] Found {completedQuests.Count} completed quests for {player.displayName}");
		
		if (completedQuests.Count == 0)
		{
			DebugPuts($"[Q] No completed quests, showing empty state");
			// Show empty state - use CuiLabel (same as rest of UI) so it displays properly
			var container = new CuiElementContainer();
			container.Add(new CuiElement
			{
				Name = "Q_EmptyCompleted",
				Parent = "Q_Scroller",
				Components =
				{
					new CuiImageComponent { Color = "0.18 0.20 0.18 0", Sprite = "assets/content/ui/ui.background.tile.psd" },
					new CuiRectTransformComponent { AnchorMin = "0.32 0.48", AnchorMax = "0.68 0.52" }
				}
			});
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
				Text = { Text = "No completed quests yet!", Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" }
			}, "Q_EmptyCompleted", "Q_EmptyCompleted_Text");
			CuiHelper.AddUi(player, container);
			return;
		}

		DebugPuts($"[Q] Using accordion system for completed quests");
		RenderAccordion(player, false, UICategory.Completed);
	}


		private static string EscapeArg(string value)
		{
			if (string.IsNullOrEmpty(value)) return "";
			return value.Replace(" ", "%20").Replace("\"", "%22");
		}

		private void UpdateTasksCount(BasePlayer player, int count)
		{
			// Tab bar already shows counts (Quests (85), Daily (0/3), etc.) - no duplicate label needed
			RefreshTabLabels(player);
		}

		private void RefreshTabLabels(BasePlayer player)
		{
			// Active quests = 1 per unlocked category (max 7) + daily quests (3) = 10 max
			int avail = 0;
			foreach (string cat in _categoryOrder)
			{
				if (string.Equals(cat, "Daily", StringComparison.OrdinalIgnoreCase)) continue;
				if (IsCategoryLocked(player.userID, cat)) continue;
				if (GetNextQuestInCategory(player.userID, cat) != null) avail++;
			}
			var daily = GetQuestsByCategory(UICategory.Daily, player.userID);
			avail += daily.Count;
			int dailyTotal = daily.Count;
			int questTotal = 0;
			foreach (var q in _questList.Values)
				if (!q.IsDaily) questTotal++;
			int dailyDone = 0;
			PlayerData pd = null;
			if (_playersInfo.TryGetValue(player.userID, out pd) && pd.DailyCompletedToday != null)
			{
				foreach (var q in daily)
					if (pd.DailyCompletedToday.Contains(q.QuestID)) dailyDone++;
			}
			int taken = 0;
			if (_playersInfo.TryGetValue(player.userID, out pd) && pd.CurrentPlayerQuests != null)
			{
				foreach (var pq in pd.CurrentPlayerQuests)
				{
					if (_questList.TryGetValue(pq.ParentQuestID, out var qq) && !qq.IsDaily) taken++;
				}
				if (pd.PlayerQuestCooldowns != null)
					foreach (var qid in pd.PlayerQuestCooldowns.Keys)
					{
						if (_questList.TryGetValue(qid, out var qq) && !qq.IsDaily) taken++;
					}
			}
			int completed = GetQuestsByCategory(UICategory.Completed, player.userID).Count;
			if (pd != null && pd.CompletedQuestHistory != null)
				completed = Math.Max(completed, pd.CompletedQuestHistory.Count);
			int completedTotal = questTotal + dailyTotal; // Total possible quests (all non-daily + daily)
			CuiHelper.DestroyUi(player, "Q_TabAvailable"); CuiHelper.DestroyUi(player, "Q_TabDaily"); CuiHelper.DestroyUi(player, "Q_TabCompleted");
			var c = new CuiElementContainer();
			c.Add(new CuiButton { RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, Button = { Command = "UI_Handler accordion", Color = "0 0 0 0" }, Text = { Text = $"Quests ({avail}/{questTotal})", Align = TextAnchor.MiddleCenter, FontSize = 15, Color = "0.95 0.93 0.88 1" } }, "Q_TabAvailable_BG", "Q_TabAvailable");
			c.Add(new CuiButton { RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, Button = { Command = "UI_Handler daily", Color = "0 0 0 0" }, Text = { Text = $"Daily ({dailyDone}/{dailyTotal})", Align = TextAnchor.MiddleCenter, FontSize = 15, Color = "0.95 0.93 0.88 1" } }, "Q_TabDaily_BG", "Q_TabDaily");
			c.Add(new CuiButton { RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, Button = { Command = "UI_Handler completed", Color = "0 0 0 0" }, Text = { Text = $"Completed ({completed}/{completedTotal})", Align = TextAnchor.MiddleCenter, FontSize = 15, Color = "0.95 0.93 0.88 1" } }, "Q_TabCompleted_BG", "Q_TabCompleted");
			CuiHelper.AddUi(player, c);
		}

		#endregion

		#region Category

		private enum UICategory
		{
			Available,
			Taken,
			Daily,
			Completed,
		}

		private List<QuestDefinition> GetQuestsByCategory(UICategory category, ulong playerId)
		{
			List<QuestDefinition> result = new List<QuestDefinition>();

			PlayerData playerData = _playersInfo[playerId];
			if (playerData == null)
			{
				return result;
			}

			switch (category)
			{
			case UICategory.Available:
				// Linear progression: "Available" = currently assigned/active quests (from CurrentPlayerQuests)
				if (playerData.CurrentPlayerQuests != null)
				foreach (PlayerQuest pq in playerData.CurrentPlayerQuests)
				{
					if (_questList.TryGetValue(pq.ParentQuestID, out QuestDefinition quest) && !quest.IsDaily)
					{
						if (!string.IsNullOrEmpty(quest.QuestPermission) && !permission.UserHasPermission(playerId.ToString(), $"{Name}." + quest.QuestPermission))
							continue;
						result.Add(quest);
					}
				}
				break;

			case UICategory.Taken:
					foreach (PlayerQuest playerQuest in playerData.CurrentPlayerQuests)
					{
						QuestDefinition value;
						if (_questList.TryGetValue(playerQuest.ParentQuestID, out value))
						{
							// Exclude Daily from Taken; they live under Daily and require no acceptance
							if (value.IsDaily) continue;
							result.Add(value);
						}
					}

				foreach (long questId in playerData.PlayerQuestCooldowns.Keys)
					{
						QuestDefinition value;
						if (_questList.TryGetValue(questId, out value))
						{
							if (value.IsDaily) continue;
							result.Add(value);
						}
					}

				break;

			case UICategory.Daily:
				foreach (QuestDefinition quest in _questList.Values)
				{
					if (!quest.IsDaily) continue;
					if (!string.IsNullOrEmpty(quest.QuestPermission) && !permission.UserHasPermission(playerId.ToString(), $"{Name}." + quest.QuestPermission)) continue;
					result.Add(quest);
				}
					break;

			case UICategory.Completed:
				// Show finished but unclaimed (in CurrentPlayerQuests) + CompletedQuestHistory (linear: auto-claimed, for reflection)
				if (playerData.CurrentPlayerQuests != null)
				{
					foreach (PlayerQuest pq in playerData.CurrentPlayerQuests)
					{
						if (pq.Finished && _questList.TryGetValue(pq.ParentQuestID, out QuestDefinition q1))
							result.Add(q1);
					}
				}
				if (playerData.CompletedQuestHistory != null)
				{
					foreach (var rec in playerData.CompletedQuestHistory)
					{
						if (!_questList.TryGetValue(rec.QuestID, out QuestDefinition q2)) continue;
						bool alreadyAdded = false;
						for (int i = 0; i < result.Count; i++)
						{
							if (result[i].QuestID == rec.QuestID) { alreadyAdded = true; break; }
						}
						if (!alreadyAdded) result.Add(q2);
					}
				}
				break;
			}

			return result;
		}


		#endregion

		#region QuestList
		#endregion

		#region QuestInfo

		private void QuestInfo(BasePlayer player, long questID, UICategory category, int page = 0)
		{
			if (!_playersInfo.TryGetValue(player.userID, out PlayerData pdata))
				return;

			List<PlayerQuest> playerQuests = pdata.CurrentPlayerQuests;
			PlayerQuest foundQuest = (playerQuests != null) ? playerQuests.Find(quest => quest.ParentQuestID == questID) : null;
			QuestDefinition quests = null;
			QuestDefinition value;
			if (_questList.TryGetValue(questID, out value))
				quests = value;
			string playerLaunguage = lang.GetLanguage(player.UserIDString);
			
			// Debug: Log quest state
			DebugPuts($"[Q] QuestInfo: questID={questID}, foundQuest={foundQuest != null}, quests={quests != null}");
			if (foundQuest != null)
				DebugPuts($"[Q] QuestInfo: foundQuest.Finished={foundQuest.Finished}, ParentQuestType={foundQuest.ParentQuestType}");
			else
				DebugPuts($"[Q] QuestInfo: Quest not found in player's quest list - should show Take button");

			// Clean up old quest info content before adding new UI
			CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
			CuiHelper.DestroyUi(player, "Q_RewardRow");
			// Also destroy any reward elements that might be left behind
			for (int j = 0; j < 10; j++)
			{
				CuiHelper.DestroyUi(player, $"Prize_{j}");
			}

			CuiElementContainer container = new CuiElementContainer();

			// MAIN CONTAINER: QuestInfoPanel (right-side quest card) - border + material for visibility
			container.Add(new CuiElement
			{
				Name = "QuestInfoPanel_Border",
				Parent = LAYER_MAIN_BACKGROUND,
				Components =
				{
					new CuiImageComponent { Color = UI_BUTTON_BORDER_COLOR, Sprite = "assets/content/ui/ui.background.tile.psd" },
					new CuiRectTransformComponent { AnchorMin = "0.28 0.04", AnchorMax = "0.92 0.78" }
				}
			});
			container.Add(new CuiPanel
			{
				Image = { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
			}, "QuestInfoPanel_Border", "QuestInfoPanel");


			if (questID == 0 || quests == null)
			{
				// Show empty state message
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-200 -50", OffsetMax = "200 50" },
					Text = { Text = "Quest_UI_TASKS_INFO_EMPTY".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 19, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
				}, "QuestInfoPanel");

				CuiHelper.AddUi(player, container);
				return;
			}

			// CHILD 1: Q_TitleBlock (header)
			container.Add(new CuiPanel
			{
				Image = { Color = "0 0 0 0" },
				RectTransform = { AnchorMin = "0.02 0.88", AnchorMax = "0.98 0.96" }
			}, "QuestInfoPanel", "Q_TitleBlock");

			// CHILD 2: Q_InfoBlock (info line)
			container.Add(new CuiPanel
			{
				Image = { Color = "0 0 0 0" },
				RectTransform = { AnchorMin = "0.02 0.84", AnchorMax = "0.98 0.88" }
			}, "QuestInfoPanel", "Q_InfoBlock");

			// CHILD 3: Q_DescBlock (body text)
			container.Add(new CuiPanel
			{
				Image = { Color = "0 0 0 0" },
				RectTransform = { AnchorMin = "0.02 0.72", AnchorMax = "0.98 0.84" }
			}, "QuestInfoPanel", "Q_DescBlock");

			// CHILD 4: Q_RewardRow (rewards - child of panel so it scales evenly)
			container.Add(new CuiPanel
			{
				Image = { Color = GetSolidColorForImage(GetPlayerBackgroundImage(player)), Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
				RectTransform = { AnchorMin = "0.02 0.54", AnchorMax = "0.98 0.70" }
			}, "QuestInfoPanel", "Q_RewardRow");

			// CHILD 5: QuestCheckBox (objectives) - extends to bottom (no Take/Take All buttons)
			container.Add(new CuiPanel
			{
				Image = { Color = "0.18 0.20 0.18 0.95", Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
				RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.98 0.52" }
			}, "QuestInfoPanel", "QuestCheckBox");

			// CONTENT FOR EACH SECTION:

			// 1. TITLE CONTENT
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.00 0.00", AnchorMax = "1.00 1.00" },
				Text = { Text = quests.GetDisplayName(playerLaunguage), Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" }
			}, "Q_TitleBlock", "QuestName");

			// 2. INFO CONTENT
			string userepeat = quests.IsRepeatable ? "Quest_UI_QUESTREPEATCAN".GetAdaptedMessage(player.UserIDString) : "Quest_UI_QUESTREPEATfForbidden".GetAdaptedMessage(player.UserIDString);
			string useCooldown = quests.Cooldown > 0
				? "Quest_UI_OnCooldown".GetAdaptedMessage(player.UserIDString)
				: "Quest_UI_NotOnCooldown".GetAdaptedMessage(player.UserIDString);
			
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.00 0.00", AnchorMax = "1.00 1.00" },
				Text = { Text = "Quest_UI_InfoRepeatInCD".GetAdaptedMessage(player.UserIDString, userepeat, useCooldown), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = "0.9607844 0.5843138 0.1960784 1" }
			}, "Q_InfoBlock", "QuestInfo2");

			// 3. DESCRIPTION CONTENT
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.00 0.00", AnchorMax = "1.00 1.00" },
				Text = { Text = quests.GetDescription(playerLaunguage), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" }
			}, "Q_DescBlock", "QuestDescription");

			// 4. REWARDS CONTENT - Square slots (height matches width in pixels)
			int i = 0;
			foreach (QuestDefinition.Prize prize in quests.PrizeList)
			{
				if(prize.IsHidden) continue;
				
				string prizeLayer = "QuestInfo" + $".{i}";
				// Q_RewardRow is wide and short; for square slots: height=0.88, width=height/rowAspect ~ 0.08
				float slotHeight = 0.88f;
				float slotWidth = 0.08f;
				float slotSpacing = 0.015f;
				float xPosition = 0.02f + (i * (slotWidth + slotSpacing));
				float yMin = (1f - slotHeight) / 2f;
				float yMax = yMin + slotHeight;
				
				container.Add(new CuiElement
				{
					Name = prizeLayer,
					Parent = "Q_RewardRow",
					Components =
					{
						new CuiImageComponent { Color = "0 0 0 0", Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent
						{
							AnchorMin = $"{xPosition:F2} {yMin:F2}",
							AnchorMax = $"{xPosition + slotWidth:F2} {yMax:F2}"
						}
					}
				});
				
				// Add prize icon based on type
				switch (prize.PrizeType)
				{
					case PrizeType.Item:
						{
							var itemDef = ItemManager.FindItemDefinition(prize.ItemShortName);
							if (itemDef != null)
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiImageComponent { Color = "1 1 1 1", ItemId = itemDef.itemid },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
							else
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiImageComponent { Color = "0.4 0.4 0.4 1", Sprite = "assets/content/ui/ui.background.tile.psd" },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
						}
						break;
					case PrizeType.BluePrint:
						{
							var bpDef = ItemManager.FindItemDefinition("blueprintbase");
							if (bpDef != null)
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiImageComponent { Color = "1 1 1 1", ItemId = bpDef.itemid },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
							else
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiImageComponent { Color = "0.4 0.4 0.4 1", Sprite = "assets/content/ui/ui.background.tile.psd" },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
						}
						break;
					case PrizeType.CustomItem:
						{
							var customDef = ItemManager.FindItemDefinition(prize.ItemShortName);
							if (customDef != null)
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiImageComponent { Color = "1 1 1 1", ItemId = customDef.itemid, SkinId = prize.ItemSkinID },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
							else
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiImageComponent { Color = "0.4 0.4 0.4 1", Sprite = "assets/content/ui/ui.background.tile.psd" },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
						}
						break;
					case PrizeType.Command:
						{
							string cmdImgId = (_imageUI != null) ? _imageUI.GetImage(prize.CommandImageName) : "0";
							if (!string.IsNullOrEmpty(cmdImgId) && cmdImgId != "0")
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiRawImageComponent { Color = "1 1 1 1", Png = cmdImgId },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
							else if (!string.IsNullOrEmpty(prize.PrizeCommand) && prize.PrizeCommand.StartsWith("quest_spawn_crate:", StringComparison.OrdinalIgnoreCase))
							{
								// Use elite crate sprite when no custom image (helicopter/bradley crates)
								var crateItem = ItemManager.FindItemDefinition("supply.signal");
								if (crateItem != null)
								{
									container.Add(new CuiElement
									{
										Name = prizeLayer + ".icon",
										Parent = prizeLayer,
										Components =
										{
											new CuiImageComponent { Color = "1 1 1 1", ItemId = crateItem.itemid },
											new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
										}
									});
								}
								else
								{
									container.Add(new CuiElement
									{
										Name = prizeLayer + ".icon",
										Parent = prizeLayer,
										Components =
										{
											new CuiImageComponent { Color = "0.4 0.4 0.4 1", Sprite = "assets/content/ui/ui.background.tile.psd" },
											new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
										}
									});
								}
							}
							else
							{
								container.Add(new CuiElement
								{
									Name = prizeLayer + ".icon",
									Parent = prizeLayer,
									Components =
									{
										new CuiImageComponent { Color = "0.4 0.4 0.4 1", Sprite = "assets/content/ui/ui.background.tile.psd" },
										new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.9 0.9" }
									}
								});
							}
						}
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
				
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.25" },
					Text = { Text = GetPrizeDisplayAmount(prize).ToString(), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
				}, prizeLayer);
				
				i++;
			}

			// 5. QUESTCHECKBOX CONTENT (Objectives Panel)

			// QuestBackgroundImg - objectives text and progress bar background
			container.Add(new CuiElement
			{
				Name = "QuestBackgroundImg",
				Parent = "QuestCheckBox",
				Components =
				{
					new CuiImageComponent { Color = "0.20 0.22 0.18 0.95", Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
					new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
				}
			});

			// Objectives text (checkbox removed - completed quests move to Completed tab)
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.05 0.45", AnchorMax = "0.96 0.95" },
				Text = { Text = quests.GetMissions(playerLaunguage), Font = "robotocondensed-regular.ttf", FontSize = 18, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" }
			}, "QuestBackgroundImg", "CheckBoxTxt");

			// Progress bar - show for any quest with countable objective (not Delivery)
			int currentCount = 0;
			if (foundQuest != null && foundQuest.ParentQuestType != QuestType.Delivery)
				currentCount = foundQuest.Count;
			else if (quests != null && quests.IsDaily && _playersInfo[player.userID].DailyProgressCounts.TryGetValue(questID, out int dc))
				currentCount = dc;
			double factor = quests != null && quests.ActionCount > 0 ? Math.Min(1.0, (double)currentCount / quests.ActionCount) : 0;
			bool showProgress = quests != null && quests.QuestType != QuestType.Delivery && quests.ActionCount > 0;
			
			if (showProgress)
			{
				// Progress Bar label + count above the bar
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.45 0.38" },
					Text = { Text = "Progress Bar", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" }
				}, "QuestCheckBox", "QuestProgresLabel");
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.50 0.30", AnchorMax = "0.95 0.38" },
					Text = { Text = $"{currentCount} / {quests.ActionCount}", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }
				}, "QuestCheckBox", "QuestProgresCount");

				// Container 1: Bar border - equal 4% gap on all 4 sides within QuestCheckBox
				container.Add(new CuiElement
				{
					Name = "QuestProgresBarBorder",
					Parent = "QuestCheckBox",
					Components =
					{
						new CuiImageComponent { Color = "0.5 0.5 0.5 1", Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0.04 0.04", AnchorMax = "0.96 0.26" }
					}
				});
				// Container 2: Inner dark track - fills border with slight inset
				container.Add(new CuiElement
				{
					Name = "QuestProgresBar",
					Parent = "QuestProgresBarBorder",
					Components =
					{
						new CuiImageComponent { Color = "0.20 0.22 0.18 0.95", Sprite = "assets/content/ui/ui.background.tile.psd", Material = UI_BUTTON_MATERIAL },
						new CuiRectTransformComponent { AnchorMin = "0.03 0.03", AnchorMax = "0.97 0.97" }
					}
				});
				// Green fill - centered with equal 6% gap on all 4 sides inside the track
				container.Add(new CuiElement
				{
					Name = "QuestProgresFill",
					Parent = "QuestProgresBar",
					Components =
					{
						new CuiImageComponent { Color = "0.4 0.7 0.4 0.95", Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0.06 0.06", AnchorMax = $"{Math.Max(0.06, factor):F2} 0.94" }
					}
				});
			}

			// Add the UI to the player
			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region MiniQuestList

		private void OpenMQL_CMD(BasePlayer player)
		{
			UIMiniQuestList(player);
		}

		private void UIMiniQuestList(BasePlayer player, int page = 0)
		{
			List<PlayerQuest> playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests == null)
			{
				return;
			}

			if (playerQuests.Count == 0)
			{
				SendReply(player, "Quest_UI_ActiveQuestCount".GetAdaptedMessage(player.UserIDString));
				if (_openMiniQuestListPlayers.Contains(player.userID))
				{
					_openMiniQuestListPlayers.Remove(player.userID);
				}

				return;
			}

			if (!_openMiniQuestListPlayers.Contains(player.userID))
			{
				_openMiniQuestListPlayers.Add(player.userID);
			}

			// Hide pinned quests when mini list opens
			CuiHelper.DestroyUi(player, "PinnedQuests");
			// Also destroy any individual pinned quest elements
			for (int j = 0; j < 2; j++)
			{
				CuiHelper.DestroyUi(player, $"PinnedQuest_{j}");
			}

			playerQuests.Sort(delegate(PlayerQuest x, PlayerQuest y)
			{
				if (x.Finished && !y.Finished) return -1;
				if (!x.Finished && y.Finished) return 1;
				return 0;
			});
			string playerLaunguage = lang.GetLanguage(player.UserIDString);
			const int size = 72;
			int questCount = playerQuests.Count;
			
			// Calculate total height needed for all quests
			int totalHeight = questCount * size;
			
			CuiElementContainer container = new CuiElementContainer
			{
				// Main transparent panel - stretch top-to-bottom on the left, moved down
				{
					new CuiPanel
					{
						CursorEnabled = true,
						Image = { Color = "1 1 1 0" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "10 10", OffsetMax = "310 -60" }
					},
					"Overlay",
					MINI_QUEST_LIST, MINI_QUEST_LIST
				},

				// ScrollView for quests - with top margin for header
				{
					new CuiElement
					{
						Name = "QuestScrollView",
						Parent = MINI_QUEST_LIST,
						Components =
						{
							new CuiScrollViewComponent
							{
								Horizontal = false,
								Vertical = true,
								VerticalNormalizedPosition = 1.0f, // Start at the top to show first quests
								ContentTransform = new CuiRectTransform
								{
									AnchorMin = "0 1",
									AnchorMax = "1 1",
									OffsetMin = $"0 -{totalHeight}",
									OffsetMax = "0 0",
									Pivot = "0 1"
								}
							},
							new CuiRectTransformComponent
							{
								AnchorMin = "0 0",
								AnchorMax = "1 1",
								OffsetMin = "5 5",
								OffsetMax = "-5 -35" // top margin for header
							},
							new CuiImageComponent { Color = "0 0 0 0" }
						}
					}
				}
			};

			// Add all quests to the scroll view content
			int i = 0;
			foreach (PlayerQuest quest in playerQuests)
			{
				QuestDefinition currentQuest = _questList[quest.ParentQuestID];
				bool isPinned = _playersInfo[player.userID].PinnedQuestIds != null && _playersInfo[player.userID].PinnedQuestIds.Contains(quest.ParentQuestID);
				string color = quest.Finished ? "0.1960784 0.7176471 0.4235294 1" : (isPinned ? "1 0.647 0 1" : "0.6235294 0.2823529 0.8588235 1"); // Orange if pinned
				bool isDelivery = currentQuest.QuestType == QuestType.Delivery;
				
				// Quest item background - solid shader (no images)
				container.Add(new CuiElement
				{
					Name = $"MiniQuestImage_{i}",
					Parent = "QuestScrollView___Content",
					Components =
					{
						new CuiImageComponent { Color = "0.22 0.24 0.20 0.95", Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent 
						{ 
							AnchorMin = "0 1", 
							AnchorMax = "1 1", 
							OffsetMin = $"0 -{(i + 1) * size}", 
							OffsetMax = $"0 -{i * size}" }
					}
				});
				
				// Darken each mini quest item background
				container.Add(new CuiPanel
				{
					Image = { Color = "0 0 0 0.25", Sprite = "assets/content/ui/ui.background.tile.psd" },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
				}, $"MiniQuestImage_{i}");
				
				// Quest icon (solid color indicator)
				container.Add(new CuiElement
				{
					Name = $"ImgForMiniQuest_{i}",
					Parent = $"MiniQuestImage_{i}",
					Components =
					{
						new CuiImageComponent { Color = color, Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "5 -33.576", OffsetMax = "17.577 33.577" }
					}
				});
				
				// Quest text
				string qtext = isDelivery ? "Quest_UI_MiniQLInfoDelivery" : "Quest_UI_MiniQLInfo";
				container.Add(new CuiElement
				{
					Name = $"LabelForMiniQuest_{i}",
					Parent = $"MiniQuestImage_{i}",
					Components =
					{
						new CuiTextComponent
						{
							Text = qtext.GetAdaptedMessage(player.UserIDString, currentQuest.GetDisplayName(playerLaunguage), quest.Count, currentQuest.ActionCount, currentQuest.GetMissions(playerLaunguage)),
							Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1"
						},
						new CuiOutlineComponent { Color = "0 0 0 1", Distance = "0.6 0.6" },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "20 -28.867", OffsetMax = "280 28.867" }
					}
				});
				
				// Add Completed button for finished quests
				if (quest.Finished)
				{
					container.Add(new CuiButton
					{
						Button = { Color = "0.1960784 0.7176471 0.4235294 0.8", Command = $"UI_Handler finish {quest.ParentQuestID} Taken 0" },
						Text = { Text = "Completed", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
						RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-50 -15", OffsetMax = "-5 15" }
					}, $"MiniQuestImage_{i}", $"CompletedBtn_{i}");
				}
				
				// Add clickable area for pinning (entire quest item)
				container.Add(new CuiButton
				{
					Button = { Color = "0 0 0 0", Command = $"ToggleQuestPin {quest.ParentQuestID}" },
					Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
				}, $"MiniQuestImage_{i}", $"PinBtn_{i}");
				
				i++;
			}

			// Add header elements AFTER quest cards to ensure they render on top
			// Optional: a subtle header band (click-through blocker)
			container.Add(new CuiPanel
			{
				Image = { Color = "0.15 0.17 0.14 0.9", Sprite = "assets/content/ui/ui.background.tile.psd" },
				RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "5 -35", OffsetMax = "-5 -5" }
			}, MINI_QUEST_LIST, "MiniQuestHeader");

			// Title (parent it to header so it's neatly positioned)
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "10 -30", OffsetMax = "225 -5" },
				Text = { Text = "Quest_UI_ACTIVEOBJECTIVES".GetAdaptedMessage(player.UserIDString, playerQuests.Count),
						 Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
			}, "MiniQuestHeader", "LabelMiniQuestPanel");

			// Close button (top-right)
			container.Add(new CuiButton
			{
				Button = { Color = "0 0 0 0", Command = "CloseMiniQuestList" },
				Text = { Text = "x", Font = "robotocondensed-regular.ttf", FontSize = 15, Align = TextAnchor.MiddleCenter, Color = "1 0 0 1" },
				RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-20 -30", OffsetMax = "0 -10" }
			}, "MiniQuestHeader", "MiniQuestCloseBtn");

			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region Quest Pinning

		private void ToggleQuestPin(BasePlayer player, long questID)
		{
			if (!_playersInfo.ContainsKey(player.userID))
			{
				_playersInfo[player.userID] = new PlayerData();
			}

			PlayerData playerData = _playersInfo[player.userID];
			if (playerData.PinnedQuestIds == null)
			{
				playerData.PinnedQuestIds = new List<long>();
			}

			var pinnedQuests = playerData.PinnedQuestIds;
			
			if (pinnedQuests.Contains(questID))
			{
				// Unpin quest
				pinnedQuests.Remove(questID);
			}
			else
			{
				// Pin quest (max 2)
				if (pinnedQuests.Count >= 2)
				{
					// Remove oldest pinned quest
					pinnedQuests.RemoveAt(0);
				}
				pinnedQuests.Add(questID);
			}

			// Update quest colors in place without recreating the entire list
			if (_openMiniQuestListPlayers.Contains(player.userID))
			{
				UpdateQuestItemColors(player);
			}
		}

		private void UpdateQuestItemColors(BasePlayer player)
		{
			List<PlayerQuest> playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests == null) return;

			CuiElementContainer container = new CuiElementContainer();

			int i = 0;
			foreach (PlayerQuest quest in playerQuests)
			{
				QuestDefinition currentQuest = _questList[quest.ParentQuestID];
				bool isPinned = _playersInfo[player.userID].PinnedQuestIds != null && _playersInfo[player.userID].PinnedQuestIds.Contains(quest.ParentQuestID);
				string color = quest.Finished ? "0.1960784 0.7176471 0.4235294 1" : (isPinned ? "1 0.647 0 1" : "0.6235294 0.2823529 0.8588235 1");

				// Update quest icon color
				container.Add(new CuiElement
				{
					Name = $"ImgForMiniQuest_{i}",
					Parent = $"MiniQuestImage_{i}",
					Update = true,
					Components =
					{
						new CuiImageComponent { Color = color, Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "5 -33.576", OffsetMax = "17.577 33.577" }
					}
				});

				i++;
			}

			if (container.Count > 0)
			{
				CuiHelper.AddUi(player, container);
			}
		}

		private void ShowPinnedQuests(BasePlayer player)
		{
			// Always destroy existing pinned quests first to prevent stacking
			CuiHelper.DestroyUi(player, "PinnedQuests");
			// Also destroy any individual pinned quest elements
			for (int j = 0; j < 2; j++)
			{
				CuiHelper.DestroyUi(player, $"PinnedQuest_{j}");
			}

			if (!_playersInfo.ContainsKey(player.userID) || _playersInfo[player.userID].PinnedQuestIds == null || _playersInfo[player.userID].PinnedQuestIds.Count == 0)
				return;

			// Hide pinned quests when mini list is open
			if (_openMiniQuestListPlayers.Contains(player.userID))
			{
				return;
			}

			var pinnedQuests = _playersInfo[player.userID].PinnedQuestIds;
			var playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests == null) return;

			CuiElementContainer container = new CuiElementContainer();

			// Create pinned quest widgets using the same styling as mini quest list
			for (int i = 0; i < pinnedQuests.Count; i++)
			{
				long questID = pinnedQuests[i];
				PlayerQuest playerQuest = playerQuests.Find(q => q.ParentQuestID == questID);
				if (playerQuest == null) continue;

				QuestDefinition quest = _questList[questID];
				if (quest == null) continue;

				string playerLanguage = lang.GetLanguage(player.UserIDString);
				string questName = quest.GetDisplayName(playerLanguage);
				string progress = $"{playerQuest.Count}/{quest.ActionCount}";
				string color = playerQuest.Finished ? "0.1960784 0.7176471 0.4235294 1" : "1 0.647 0 1"; // Orange for pinned, green if completed

				// Position pinned quests in top-left area, stacked vertically
				int yOffset = 80 + (i * 40); // 40px spacing between quests, moved down 80px
				
				// Quest background (solid shader, no images)
				container.Add(new CuiElement
				{
					Name = $"PinnedQuest_{i}",
					Parent = "Overlay",
					Components =
					{
						new CuiImageComponent { Color = "0.22 0.24 0.20 0.95", Sprite = "assets/content/ui/ui.background.tile.psd" },
						new CuiRectTransformComponent 
						{ 
							AnchorMin = "0 1", 
							AnchorMax = "0 1", 
							OffsetMin = $"10 -{yOffset + 35}", 
							OffsetMax = $"160 -{yOffset}" }
					}
				});

				// Darken background overlay
				container.Add(new CuiPanel
				{
					Image = { Color = "0 0 0 0.2", Sprite = "assets/content/ui/ui.background.tile.psd" },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
				}, $"PinnedQuest_{i}");

				// Quest name (no icon, just text)
				container.Add(new CuiElement
				{
					Name = $"PinnedQuestName_{i}",
					Parent = $"PinnedQuest_{i}",
					Components =
					{
						new CuiTextComponent
						{
							Text = questName,
							Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = color
						},
						new CuiOutlineComponent { Color = "0 0 0 1", Distance = "0.6 0.6" },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "5 -5", OffsetMax = "145 15" }
					}
				});

				// Quest progress
				container.Add(new CuiElement
				{
					Name = $"PinnedQuestProgress_{i}",
					Parent = $"PinnedQuest_{i}",
					Components =
					{
						new CuiTextComponent
						{
							Text = progress,
							Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1"
						},
						new CuiOutlineComponent { Color = "0 0 0 1", Distance = "0.6 0.6" },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "5 -20", OffsetMax = "145 0" }
					}
				});
			}

			if (container.Count > 0)
			{
				CuiHelper.AddUi(player, container);
			}
		}

		#endregion

		#region Notice

		private void UINottice(BasePlayer player, string msg, string sprite = "assets/icons/warning.png", string color = "1.0 0.45 0.15 1.0")
		{
			CuiElementContainer container = new CuiElementContainer
			{
				// Main notification background - solid shader (no images)
				new CuiElement
				{
					FadeOut = 2.30f,
					Name = "QuestUiNotice",
					Parent = LAYER_MAIN_BACKGROUND,
					Components =
					{
						new CuiImageComponent { Color = "0.10 0.08 0.15 0.95", Sprite = "assets/content/ui/ui.background.tile.psd", FadeIn = 0.30f },
						new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-147.5 -55", OffsetMax = "147.5 55" }
					}
				},

				// Side accent tab - solid shader
				new CuiElement
				{
					FadeOut = 2.30f,
					Name = "NoticeFeed",
					Parent = "QuestUiNotice",
					Components =
					{
						new CuiImageComponent { Color = "1.0 0.45 0.15 1.0", Sprite = "assets/content/ui/ui.background.tile.psd", FadeIn = 0.30f },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.276 -55", OffsetMax = "55 55" }
					}
				},

				// Icon sprite - warm gold highlight
				new CuiElement
				{
					FadeOut = 2.30f,
					Name = "NoticeSprite",
					Parent = "QuestUiNotice",
					Components =
					{
						new CuiImageComponent { Color = "1.0 0.75 0.4 1.0", Sprite = sprite, FadeIn = 0.30f },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "23.5 -15.5", OffsetMax = "54.5 15.5" }
					}
				},

				// Message text - white with slight transparency for readability
				{
					new CuiLabel
					{
						RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-78.262 -33.458", OffsetMax = "143.522 33.459" },
						Text = { Text = msg, Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 0.95", FadeIn = 0.30f }
					},
					"QuestUiNotice",
					"NoticeText"
				}
			};

			CuiHelper.DestroyUi(player, "NoticeText");
			CuiHelper.DestroyUi(player, "NoticeSprite");
			CuiHelper.DestroyUi(player, "NoticeFeed");
			CuiHelper.DestroyUi(player, "QuestUiNotice");
			CuiHelper.AddUi(player, container);

			DeleteNotification(player);
		}

		private readonly Dictionary<BasePlayer, Timer> _playerTimer = new Dictionary<BasePlayer, Timer>();

		private void DeleteNotification(BasePlayer player)
		{
			Timer timers = timer.Once(3.5f, () =>
			{
				CuiHelper.DestroyUi(player, "NoticeText");
				CuiHelper.DestroyUi(player, "NoticeSprite");
				CuiHelper.DestroyUi(player, "NoticeFeed");
				CuiHelper.DestroyUi(player, "QuestUiNotice");
			});

			if (_playerTimer.ContainsKey(player))
			{
				if (_playerTimer[player] != null && !_playerTimer[player].Destroyed) _playerTimer[player].Destroy();
				_playerTimer[player] = timers;
			}
			else _playerTimer.Add(player, timers);
		}

		#endregion

		#endregion

		#region Helper Classes

		private static class ObjectCache
		{
			private static readonly object True = true;
			private static readonly object False = false;

			private static class StaticObjectCache<T>
			{
				private static readonly Dictionary<T, object> CacheByValue = new Dictionary<T, object>();

				public static object Get(T value)
				{
					object cachedObject;
					if (!CacheByValue.TryGetValue(value, out cachedObject))
					{
						cachedObject = value;
						CacheByValue[value] = cachedObject;
					}

					return cachedObject;
				}
			}

			public static object Get<T>(T value)
			{
				return StaticObjectCache<T>.Get(value);
			}

			public static object Get(bool value)
			{
				return value ? True : False;
			}
		}

		#endregion

		#region Command
		
		private void SendConsoleMessage(BasePlayer player, string message)
		{
			if(player != null)
				player.ConsoleMessage(message);
			else
				PrintWarning(message);
		}
		
		[ConsoleCommand("Quest.player.reset")]
		private void PlayerDataReset(ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player != null && !player.IsAdmin)
			{
				player.ConsoleMessage("Quest_INSUFFICIENT_PERMISSIONS_ERROR".GetAdaptedMessage(player.UserIDString));
				return;
			}
			
			if (!arg.HasArgs())
			{
				SendConsoleMessage(player, "Quest_COMMAND_SYNTAX_ERROR".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}

			ulong playerid;
			if(!ulong.TryParse(arg.GetString(0), out playerid))
			{
				SendConsoleMessage(player, "Quest_INVALID_PLAYER_ID_INPUT".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}

			if (!playerid.IsSteamId())
			{
				SendConsoleMessage(player, "Quest_NOT_A_STEAM_ID".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}
			
			if (_playersInfo.ContainsKey(playerid))
			{
				_playersInfo[playerid] = new PlayerData();
				SendConsoleMessage(player, "Quest_PLAYER_PROGRESS_RESET".GetAdaptedMessage(PlayerOrNull(player)));
			}
			else
			{
				SendConsoleMessage(player, "Quest_PLAYER_NOT_FOUND_BY_STEAMID".GetAdaptedMessage(PlayerOrNull(player)));
			}
		}
		
		[ConsoleCommand("Quest.stat")]
		private void StatisticsPost (ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player != null && !player.IsAdmin)
			{
				player.ConsoleMessage("Quest_INSUFFICIENT_PERMISSIONS_ERROR".GetAdaptedMessage(player.UserIDString));
				return;
			}
			
			if (!_config.statisticsCollectionSettings.useStatistics)
			{
				SendConsoleMessage(player, "Quest_STAT_CMD_1".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}

			if (string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl))
			{
				SendConsoleMessage(player, "Quest_STAT_CMD_2".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}
			
			GrabAndPostStatistics();
		}

		[ConsoleCommand("Quest.wipesummary")]
		private void WipeSummaryPost(ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player != null && !player.IsAdmin)
			{
				player.ConsoleMessage("Quest_INSUFFICIENT_PERMISSIONS_ERROR".GetAdaptedMessage(player.UserIDString));
				return;
			}

			if (!_config.statisticsCollectionSettings.useStatistics)
			{
				SendConsoleMessage(player, "Quest_STAT_CMD_1".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}

			if (string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl))
			{
				SendConsoleMessage(player, "Quest_STAT_CMD_2".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}
			
			SendWipeSummaryReport();
			SendConsoleMessage(player, "Wipe summary report sent to Discord");
		}

		[ConsoleCommand("Quest.setwipestart")]
		private void SetWipeStart(ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player != null && !player.IsAdmin)
			{
				player.ConsoleMessage("Quest_INSUFFICIENT_PERMISSIONS_ERROR".GetAdaptedMessage(player.UserIDString));
				return;
			}

			if (arg.Args.Length == 0)
			{
				SendConsoleMessage(player, "Usage: Quest.setwipestart <hours_ago> (e.g., 'Quest.setwipestart 24' for 24 hours ago)");
				return;
			}

			if (int.TryParse(arg.GetString(0), out int hoursAgo))
			{
				_wipeStartTime = DateTime.UtcNow.AddHours(-hoursAgo);
				SendConsoleMessage(player, $"Wipe start time set to {_wipeStartTime:yyyy-MM-dd HH:mm:ss} UTC ({hoursAgo} hours ago)");
			}
			else
			{
				SendConsoleMessage(player, "Invalid number format. Please provide hours as a number.");
			}
		}

		[ConsoleCommand("Quest.testdiscord")]
		private void TestDiscordReport(ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player != null && !player.IsAdmin)
			{
				player.ConsoleMessage("Quest_INSUFFICIENT_PERMISSIONS_ERROR".GetAdaptedMessage(player.UserIDString));
				return;
			}

			if (!_config.statisticsCollectionSettings.useStatistics)
			{
				SendConsoleMessage(player, "Statistics collection is disabled in config");
				return;
			}

			if (string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl))
			{
				SendConsoleMessage(player, "Discord webhook URL is not configured");
				return;
			}

			// Test with a simple message first
			string testMessage = "🧪 **Quest Test Message**\n" +
							   $"**Time:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n" +
							   "**Status:** Discord integration test from Quest";

			SendDiscordTextChunks(_config.statisticsCollectionSettings.discordWebhookUrl, testMessage, "Quest Test");
			SendConsoleMessage(player, "Test message sent to Discord");
		}
		
		private string PlayerOrNull(BasePlayer player)
		{
			return player != null ? player.UserIDString : null;
		}

		// Handler for daily quest finishing (legacy - no Take/Refuse buttons, completion is automatic)
		private void HandleDailyQuestFinish(BasePlayer player, long questID, UICategory category, int pageIndex, bool cancel)
		{
			QuestDefinition globalQuest = _questList[questID];
			if (globalQuest == null) return;

			if (cancel)
			{
				// Refuse daily quest - remove from daily progress
				if (_playersInfo[player.userID].DailyProgressCounts.ContainsKey(questID))
					_playersInfo[player.userID].DailyProgressCounts.Remove(questID);
				
				// Update QuestInfoPanel to show "Take" button again
				QuestInfo(player, questID, category, pageIndex);
				UINottice(player, "Quest_UI_PassedTasks".GetAdaptedMessage(player.UserIDString));
				_questStatistics.GatherTaskStatistics(TaskType.Declined);
				
				// Update pinned quests if this quest is pinned
				if (_playersInfo.ContainsKey(player.userID) && _playersInfo[player.userID].PinnedQuestIds != null && _playersInfo[player.userID].PinnedQuestIds.Contains(questID))
				{
					ShowPinnedQuests(player);
				}
			}
			else
			{
				// Complete daily quest - give rewards and mark as completed today
				int count = 0;
				foreach (QuestDefinition.Prize prize in globalQuest.PrizeList)
					if (prize.PrizeType != PrizeType.Command)
						count++;

				if (24 - player.inventory.containerMain.itemList.Count < count)
				{
					UINottice(player, "Quest_UI_LackOfSpace".GetAdaptedMessage(player.UserIDString));
					return;
				}

				// Move quest to Completed tab instead of giving rewards immediately
				if (_playersInfo[player.userID].CompletedQuestHistory == null)
				{
					_playersInfo[player.userID].CompletedQuestHistory = new List<CompletedQuestRecord>();
				}
				
				// Add to completed quest history
				_playersInfo[player.userID].CompletedQuestHistory.Add(new CompletedQuestRecord
				{
					QuestID = questID,
					CompletedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
					RewardClaimed = false
				});
				
				// Mark as completed today and reset progress
				_playersInfo[player.userID].DailyCompletedToday.Add(questID);
				if (_playersInfo[player.userID].DailyProgressCounts.ContainsKey(questID))
					_playersInfo[player.userID].DailyProgressCounts.Remove(questID);
				
				UINottice(player, "Quest completed! Check the Completed tab to claim your reward.");
				
				// Update QuestInfoPanel to show completed state
				QuestInfo(player, questID, category, pageIndex);
				
				// Update pinned quests if this quest is pinned
				if (_playersInfo.ContainsKey(player.userID) && _playersInfo[player.userID].PinnedQuestIds != null && _playersInfo[player.userID].PinnedQuestIds.Contains(questID))
				{
					ShowPinnedQuests(player);
				}
				
			}
		}

		[ConsoleCommand("Quest.takeall")]
		private void CmdTakeAll(ConsoleSystem.Arg arg)
		{
			// Take/Take All removed - quests are auto-assigned (linear progression + daily at start time)
		}

		[ConsoleCommand("CloseMiniQuestList")]
		void CloseMiniQuestList(ConsoleSystem.Arg arg)
		{
			CuiHelper.DestroyUi(arg.Player(), MINI_QUEST_LIST);
			if (_openMiniQuestListPlayers.Contains(arg.Player().userID))
			{
				_openMiniQuestListPlayers.Remove(arg.Player().userID);
			}
			// Show pinned quests when mini list closes
			ShowPinnedQuests(arg.Player());
		}

		[ConsoleCommand("ToggleQuestPin")]
		void ToggleQuestPin(ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player == null || !arg.HasArgs(1)) return;
			
			if (long.TryParse(arg.GetString(0), out long questID))
			{
				ToggleQuestPin(player, questID);
			}
		}

		[ConsoleCommand("CloseMainUI")]
		void CloseLayerPlayer(ConsoleSystem.Arg arg)
		{
			BasePlayer p = arg.Player();
			CuiHelper.DestroyUi(p, LAYERS);
			CuiHelper.DestroyUi(p, "Q_RewardRow");
			// also destroy client-side scroll panel if present
			SendClientUi(p, "[{\"destroyUi\":\"Q_AccordionScroll\"}]");
		}

		[ChatCommand("quest")]
		void OpenQuestMenu(BasePlayer player)
		{
			MainUi(player);
		}

		[ConsoleCommand("UI_Handler")]
		private void CmdConsoleHandler(ConsoleSystem.Arg args)
		{
            var cmdArgs = args.Args == null ? Array.Empty<string>() : Array.ConvertAll(args.Args, value => value.ToString());
			BasePlayer player = args.Player();
			if (player == null || !_playersInfo.TryGetValue(player.userID, out PlayerData pd)) return;
			List<PlayerQuest> playerQuests = pd.CurrentPlayerQuests;
			if (playerQuests == null)
			{
				return;
			}

			if (args.HasArgs())
			{
					switch (args.GetString(0))
				{
					case "page":
					{
						int pageIndex;
						UICategory category;
						if (int.TryParse(cmdArgs[1], out pageIndex) && Enum.TryParse(cmdArgs[2], out category))
						{
							// Return to new accordion UI instead of old list
							ClearLegacyQuestUi(player);
							RenderAccordion(player);
						}

						break;
					}
					case "category":
					{
						CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
						RenderAccordion(player);
						break;
					}
					case "pageQLIST":
					{
						int pageIndex;
						if (int.TryParse(cmdArgs[1], out pageIndex))
						{
							// Return to new accordion UI instead of old mini list
							ClearLegacyQuestUi(player);
							CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
							RenderAccordion(player);
						}

						break;
					}
					case "coldown":
					{
						UINottice(player, "Quest_UI_ACTIVECOLDOWN".GetAdaptedMessage(player.UserIDString));
						break;
					}
					case "questinfo":
					{
						long questIndex;
						UICategory category;
						int pageIndex;
						if (long.TryParse(cmdArgs[1], out questIndex) && Enum.TryParse(cmdArgs[2], out category) && int.TryParse(cmdArgs[3], out pageIndex))
						{
							QuestInfo(player, questIndex, category, pageIndex);
						}

						break;
					}
					case "finish":
					{
						long questID;
						UICategory category;
						int pageIndex;
				bool cancel = args.HasArgs(5) && bool.TryParse(cmdArgs[4], out cancel);
				DebugPuts($"[Q] UI_Handler finish questID={(cmdArgs.Length > 1 ? cmdArgs[1] : string.Empty)} category={(cmdArgs.Length > 2 ? cmdArgs[2] : string.Empty)} page={(cmdArgs.Length > 3 ? cmdArgs[3] : string.Empty)} cancel={cancel}");
						if (args.HasArgs(4) && long.TryParse(cmdArgs[1], out questID) && Enum.TryParse(cmdArgs[2], out category) && int.TryParse(cmdArgs[3], out pageIndex))
						{
							QuestDefinition globalQuest = _questList[questID];
							if (globalQuest != null)
							{
								// Completely ignore daily quests - they have their own system
								if (globalQuest.IsDaily)
								{
									HandleDailyQuestFinish(player, questID, category, pageIndex, cancel);
									return;
								}
								
								PlayerQuest currentQuest = playerQuests.Find(quest => quest.ParentQuestID == globalQuest.QuestID);
								if (currentQuest == null)
								{
									return;
								}

								if (currentQuest.Finished || (currentQuest.ParentQuestType == QuestType.Delivery && cancel == false))
								{
									int count = 0;
									foreach (QuestDefinition.Prize prize in globalQuest.PrizeList)
										if (prize.PrizeType != PrizeType.Command)
											count++;

									if (24 - player.inventory.containerMain.itemList.Count < count)
									{
										UINottice(player, "Quest_UI_LackOfSpace".GetAdaptedMessage(player.UserIDString));
										return;
									}

									if (globalQuest.IsReturnItemsRequired)
									{
										ulong skins;
										if (globalQuest.QuestType is QuestType.Loot or QuestType.Delivery && ulong.TryParse(globalQuest.Target, out skins))
										{
											if (!TakeSkinIdItemsForQuest(player, globalQuest, skins))
												return;
										}
										else if (globalQuest.QuestType is QuestType.Gather or QuestType.Loot or QuestType.Craft or QuestType.PurchaseFromNpc or QuestType.Growseedlings or QuestType.Fishing or QuestType.Delivery)
										{
											if (!TakeItemsNeededForQuest(player, globalQuest))
												return;
										}
									}

									// Move quest to Completed tab instead of giving rewards immediately
									if (_playersInfo[player.userID].CompletedQuestHistory == null)
									{
										_playersInfo[player.userID].CompletedQuestHistory = new List<CompletedQuestRecord>();
									}
									
									// Add to completed quest history
									_playersInfo[player.userID].CompletedQuestHistory.Add(new CompletedQuestRecord
									{
										QuestID = questID,
										CompletedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
										RewardClaimed = false
									});
									
									// Remove from current quests
									playerQuests.Remove(currentQuest);
									
									UINottice(player, "Quest completed! Check the Completed tab to claim your reward.");
							// Return to new accordion UI after completing
							ClearLegacyQuestUi(player);
							RenderAccordion(player);
							// Destroy QuestInfoPanel and re-display quest info with updated button state (now shows Take again)
							CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
							QuestInfo(player, questID, category, pageIndex);
							
							// Update mini quest list if it's open
							if (_openMiniQuestListPlayers.Contains(player.userID))
							{
								CuiHelper.DestroyUi(player, MINI_QUEST_LIST);
								UIMiniQuestList(player);
							}
								}
								else
								{
									UINottice(player, "Quest_UI_PassedTasks".GetAdaptedMessage(player.UserIDString));
									playerQuests.Remove(currentQuest);
									// Refuse: clear old UI and go back to new accordion
									ClearLegacyQuestUi(player);
									RenderAccordion(player);
									// Destroy QuestInfoPanel - quest is now back in Available tab
									CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
									_questStatistics.GatherTaskStatistics(TaskType.Declined);
									
									// Update mini quest list if it's open
									if (_openMiniQuestListPlayers.Contains(player.userID))
									{
										CuiHelper.DestroyUi(player, MINI_QUEST_LIST);
										UIMiniQuestList(player);
									}
								}
							}
							else
							{
								UINottice(player, "You have not taken this quest!");
							}
						}

						break;
					}
					case "cats":
					{
						_playerLastTabAccepted[player.userID] = false;
						RenderAccordion(player, false);
						break;
					}
					case "setbg":
					{
						// UI_Handler setbg <imageName>
						if (args.HasArgs(2))
						{
							string imageName = cmdArgs[1];
							// Validate image name
							string[] validImages = { "9", "gradient_red", "gradient_purple", "gradient_green", "gradient_blue" };
							if (Array.IndexOf(validImages, imageName) >= 0)
							{
								if (!_playersInfo.ContainsKey(player.userID))
									_playersInfo[player.userID] = new PlayerData();
								
								_playersInfo[player.userID].BackgroundImageName = imageName;
								SaveData();
								
								// Refresh the UI to show new background
								MainUi(player);
							}
						}
						break;
					}
			case "accordion":
			{
			_playerLastTabAccepted[player.userID] = false;
			_playerLastTabDaily[player.userID] = false;
			_playerLastTabCompleted[player.userID] = false;
			CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
			CuiHelper.DestroyUi(player, "Q_RewardRow");
			CuiHelper.DestroyUi(player, "Q_EmptyCompleted");
			RenderAccordion(player, false);
				break;
			}
				case "accordion_taken":
					// Accepted tab removed; redirect to Quests tab
					_playerLastTabAccepted[player.userID] = false;
					_playerLastTabDaily[player.userID] = false;
					_playerLastTabCompleted[player.userID] = false;
					CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
					CuiHelper.DestroyUi(player, "Q_RewardRow");
					CuiHelper.DestroyUi(player, "Q_EmptyCompleted");
					RenderAccordion(player, false);
					break;
					case "pagecats":
					{
						int pageIndex = 0;
						if (args.HasArgs(2) && int.TryParse(cmdArgs[1], out pageIndex))
						{
							_listMode = UIListMode.Categories;
							// Return to new accordion UI instead of old list
							ClearLegacyQuestUi(player);
							CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
							RenderAccordion(player);
						}
						break;
					}
			case "daily":
			{
				_playerLastTabAccepted[player.userID] = false;
				_playerLastTabDaily[player.userID] = true;
				_playerLastTabCompleted[player.userID] = false;
				CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
				CuiHelper.DestroyUi(player, "Q_RewardRow");
				CuiHelper.DestroyUi(player, "Q_EmptyCompleted");
				RenderAccordion(player, false);
				break;
			}
			case "completed":
			{
				DebugPuts($"[Q] Completed tab clicked by {player.displayName}");
				_playerLastTabAccepted[player.userID] = false;
				_playerLastTabDaily[player.userID] = false;
				_playerLastTabCompleted[player.userID] = true;
				CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
				CuiHelper.DestroyUi(player, "Q_RewardRow");
				CuiHelper.DestroyUi(player, "Q_EmptyCompleted");
				RenderCompletedTab(player);
				break;
			}
			case "claim_reward":
			{
				if (args.HasArgs(2) && long.TryParse(cmdArgs[1], out long questID))
				{
					HandleClaimReward(player, questID);
				}
				break;
			}
				case "opencat":
					{
						// UI_Handler opencat <index> <uiCategory> <page>
						if (args.HasArgs(4) &&
							int.TryParse(cmdArgs[1], out var index) &&
							Enum.TryParse(cmdArgs[2], out UICategory cat) &&
							int.TryParse(cmdArgs[3], out var pageIndex))
						{
							_selectedCategoryIndex = index;
							_listMode = UIListMode.Quests;

							// Return to new accordion UI instead of old list
							ClearLegacyQuestUi(player);
							CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
							RenderAccordion(player);
						}
						break;
					}
					case "togglecat":
					{
						if (args.HasArgs(2))
						{
							string raw = cmdArgs[1].Replace("%20", " ").Replace("%22", "\"");
							if (!_playerExpandedCategories.ContainsKey(player.userID))
								_playerExpandedCategories[player.userID] = new HashSet<string>();
							var set = _playerExpandedCategories[player.userID];
							if (set.Contains(raw)) set.Remove(raw); else set.Add(raw);
							// Save desired scroll so the toggled category header is near the top on re-render
							_playerScrollPosition[player.userID] = CalculateScrollToCategory(raw, player.userID);
							CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
							CuiHelper.DestroyUi(player, "Q_RewardRow");
							// Also destroy any reward elements that might be left behind
							for (int k = 0; k < 10; k++)
							{
								CuiHelper.DestroyUi(player, $"Prize_{k}");
							}
							RenderAccordion(player);
						}
						break;
					}
					case "questbyname":
					{
						if (args.HasArgs(2))
						{
							// Rebuild full quest name: CUI splits on spaces, so "Hemp Harvester" becomes ["Hemp","Harvester"]
							string qName = cmdArgs[1];
							for (int i = 2; i < cmdArgs.Length; i++)
								qName += " " + cmdArgs[i];
							qName = qName.Replace("%20", " ").Replace("%22", "\"");
							DebugPuts($"[Q] UI_Handler questbyname: '{qName}'");
							string playerLang = lang.GetLanguage(player.UserIDString);
							QuestDefinition found = null;
							foreach (var q in _questList.Values)
							{
								string display = q.GetDisplayName(playerLang);
								if (string.Equals(display, qName, StringComparison.OrdinalIgnoreCase))
								{
									found = q;
									break;
								}
							}
							if (found != null)
							{
								DebugPuts($"[Q] Quest found: ID={found.QuestID} Name='{found.GetDisplayName(playerLang)}'");
								// Route to proper category so buttons render correctly
								UICategory cat;
								if (_playerLastTabCompleted.ContainsKey(player.userID) && _playerLastTabCompleted[player.userID])
								{
									cat = UICategory.Completed;
									// Ensure Q_RewardRow and all reward elements are destroyed when switching quests in Completed tab
									CuiHelper.DestroyUi(player, "Q_RewardRow");
									for (int k = 0; k < 10; k++)
									{
										CuiHelper.DestroyUi(player, $"Prize_{k}");
									}
									// Add a small delay to ensure destruction happens before new UI is created
									timer.Once(0.1f, () => QuestInfo(player, found.QuestID, cat, 0));
									return;
								}
								else if (found.IsDaily)
								{
									cat = UICategory.Daily;
								}
								else if (_playerLastTabAccepted.ContainsKey(player.userID) && _playerLastTabAccepted[player.userID])
								{
									cat = UICategory.Taken;
								}
								else
								{
									cat = UICategory.Available;
								}
								QuestInfo(player, found.QuestID, cat, 0);
							}
							else
							{
								DebugPuts($"[Q] Quest not found for name: '{qName}'");
							}
						}
						break;
					}
					case "catscroll":
					{
						if (args.HasArgs(3))
						{
							string cat = cmdArgs[1].Replace("%20", " ").Replace("%22", "\"");
							int start = 0;
							int.TryParse(cmdArgs[2], out start);
							if (!_playerCategoryScrollIndex.ContainsKey(player.userID))
								_playerCategoryScrollIndex[player.userID] = new Dictionary<string, int>();
							_playerCategoryScrollIndex[player.userID][cat] = Math.Max(0, start);
							CuiHelper.DestroyUi(player, "QuestInfoPanel_Border");
							RenderAccordion(player);
						}
						break;
					}
				}
			}
		}

		#endregion

		#region HelpQuestsMetods

		#region Rewards and bring items

		private void GiveQuestReward(BasePlayer player, List<QuestDefinition.Prize> prizeList)
		{
			foreach (QuestDefinition.Prize check in prizeList)
			{
				switch (check.PrizeType)
				{
					case PrizeType.Item:
						Item newItem = ItemManager.CreateByPartialName(check.ItemShortName, check.ItemAmount);
						if (newItem == null)
						{
							PrintWarning($"Unable to create quest reward item '{check.ItemShortName}' for {player.displayName} ({player.UserIDString}).");
							continue;
						}
						player.GiveItem(newItem, BaseEntity.GiveItemReason.Crafted);
						break;
					case PrizeType.Command:
						string cmd = check.PrizeCommand.Replace("%STEAMID%", player.UserIDString);
						if (cmd.StartsWith("quest_spawn_crate:", StringComparison.OrdinalIgnoreCase))
						{
							string crateType = cmd.Substring("quest_spawn_crate:".Length).Trim();
							SpawnLootCrateForPlayer(player, crateType);
						}
						else
						{
							Server.Command(cmd);
						}
						break;
					case PrizeType.CustomItem:
						Item customItem = ItemManager.CreateByPartialName(check.ItemShortName, check.ItemAmount, check.ItemSkinID);
						if (customItem == null)
						{
							PrintWarning($"Unable to create custom quest reward item '{check.ItemShortName}' for {player.displayName} ({player.UserIDString}).");
							continue;
						}
						customItem.name = check.CustomItemName;
						player.GiveItem(customItem, BaseEntity.GiveItemReason.Crafted);
						break;
					case PrizeType.BluePrint:
						Item itemBp = ItemManager.Create(ItemManager.blueprintBaseDef);
						ItemDefinition targetItem = ItemManager.FindItemDefinition(check.ItemShortName);
						if (itemBp == null || targetItem == null)
						{
							PrintWarning($"Unable to create quest reward blueprint for '{check.ItemShortName}' for {player.displayName} ({player.UserIDString}).");
							continue;
						}
						itemBp.blueprintTarget = targetItem.isRedirectOf != null ? targetItem.isRedirectOf.itemid : targetItem.itemid;
						player.GiveItem(itemBp, BaseEntity.GiveItemReason.Crafted);
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}

		private void SpawnLootCrateForPlayer(BasePlayer player, string crateType)
		{
			if (player == null || !player.IsConnected) return;
			string prefab = null;
			if (string.Equals(crateType, "helicopter", StringComparison.OrdinalIgnoreCase))
				prefab = "assets/prefabs/npc/patrol helicopter/heli_crate.prefab";
			else if (string.Equals(crateType, "bradley", StringComparison.OrdinalIgnoreCase))
				prefab = "assets/prefabs/npc/m2bradley/bradley_crate.prefab";
			if (string.IsNullOrEmpty(prefab)) return;
			Vector3 pos = player.transform.position + player.eyes.BodyForward() * 2f + Vector3.up * 0.5f;
			LootContainer crate = GameManager.server.CreateEntity(prefab, pos, Quaternion.identity) as LootContainer;
			if (crate != null)
			{
				crate.Spawn();
				if (crate.inventory != null)
					crate.SpawnLoot();
			}
		}

		private bool TakeItemsNeededForQuest(BasePlayer player, QuestDefinition globalQuest)
		{
			ItemDefinition idItem = ItemManager.FindItemDefinition(globalQuest.Target);
			int? item = null;
			if (player != null && player.inventory != null)
				item = player.inventory.GetAmount(idItem.itemid);
			
			if (item is 0 or null)
			{
				UINottice(player, "Quest_UI_InsufficientResources".GetAdaptedMessage(player.UserIDString, idItem.displayName.english));
				return false;
			}

			if (item < globalQuest.ActionCount)
			{
				UINottice(player, "Quest_UI_NotResourcesAmount".GetAdaptedMessage(player.UserIDString, idItem.displayName.english, globalQuest.ActionCount));
				return false;
			}

			if (item >= globalQuest.ActionCount)
			{
				player.inventory.Take(null, idItem.itemid, globalQuest.ActionCount);
			}

			return true;
		}

		private bool TakeSkinIdItemsForQuest(BasePlayer player, QuestDefinition globalQuest, ulong skins)
		{
			List<Item> acceptedItems = Pool.Get<List<Item>>();
			int itemAmount = 0;
			int amountQuest = globalQuest.ActionCount;
			string itemName = string.Empty;
			List<Item> items = Pool.Get<List<Item>>();
			player.inventory.GetAllItems(items);
			foreach (Item item in items)
			{
				if (item.skin == skins)
				{
					acceptedItems.Add(item);
					itemAmount += item.amount;
					itemName = item.GetName();
				}
			}
			Pool.Free(ref items);
			if (acceptedItems.Count == 0)
			{
				UINottice(player, "Quest_UI_InsufficientResourcesSkin".GetAdaptedMessage(player.UserIDString));
				return false;
			}

			if (itemAmount < amountQuest)
			{
				UINottice(player, "Quest_UI_NotResourcesAmount".GetAdaptedMessage(player.UserIDString, itemName, amountQuest));
				return false;
			}

			foreach (Item use in acceptedItems)
			{
				if (use.amount >= amountQuest)
				{
					use.amount -= amountQuest;
					if (use.amount == 0)
					{
						use.RemoveFromContainer();
						use.Remove();
					}

					amountQuest = 0;
				}
				else
				{
					amountQuest -= use.amount;
					use.RemoveFromContainer();
					use.Remove();
				}

				if (amountQuest == 0)
				{
					break;
				}
			}
			
			Pool.Free(ref acceptedItems);
			player.inventory.SendSnapshot();
			return true;
		}

		#endregion

		#region QuestProgress

		private void QuestProgress(ulong playerUserID, QuestType questType, string entName = "", string skinId = "", List<Item> items = null, int count = 1)
		{
			if (!_playersInfo.TryGetValue(playerUserID, out PlayerData playerData))
				return;

			List<PlayerQuest> playerQuests = playerData.CurrentPlayerQuests.FindAll(x => x.ParentQuestType == questType && !x.Finished);

			foreach (PlayerQuest quest in playerQuests)
			{
				QuestDefinition parentQuest = _questList[quest.ParentQuestID];
				if (string.IsNullOrEmpty(entName) && items == null)
				{
					quest.AddCount(count);
					return;
				}
				
				if (items != null)
				{
					ulong skinIditem;
					bool isSkinID = ulong.TryParse(parentQuest.Target, out skinIditem);
					foreach (Item item in items)
					{
						if(item.info.shortname.Equals(parentQuest.Target, StringComparison.OrdinalIgnoreCase) || (isSkinID && item.skin.Equals(skinIditem)))
							quest.AddCount(item.amount);
					}
					continue;
				}
				
				switch (questType)
				{
					case QuestType.IQCases:
					case QuestType.HarborEvent:
					case QuestType.SatelliteDishEvent:
					case QuestType.Sputnik:
					case QuestType.Caravan:
					case QuestType.Convoy:
					case QuestType.GasStationEvent:
					case QuestType.FerryTerminalEvent:
					case QuestType.Triangulation:
					case QuestType.AbandonedBases:
					case QuestType.IQDronePatrol:
					case QuestType.IQDefenderSupply:
					case QuestType.IQHeadReward:
					{
						if (parentQuest.Target.Equals(entName) || parentQuest.Target.Equals("0") || parentQuest.Target.Equals("999"))
							quest.AddCount(count);
						break;
					}
					case QuestType.Swipe:
					{
						if (parentQuest.Target.Equals(entName))
							quest.AddCount(count);
						break;
					}
					case QuestType.EntityKill:
					{
						if (parentQuest.IsMoreTarget)
						{
							foreach (string target in parentQuest.Targets)
							{
								if (entName.Equals(target, StringComparison.OrdinalIgnoreCase))
									quest.AddCount(count);
							}
						}
						else
						{
							if (entName.Equals(parentQuest.Target, StringComparison.OrdinalIgnoreCase))
								quest.AddCount(count);
						}
						break;
					}
					default:
					{
						if (entName.Equals(parentQuest.Target, StringComparison.OrdinalIgnoreCase) || skinId.Equals(parentQuest.Target))
							quest.AddCount(count);
						break;
					}
				}
			}
			
			Interface.CallHook("OnQuestProgress", playerUserID, (int)questType, entName, skinId, items, count);
		}

		#endregion

		#endregion

		private void DownloadImages()
		{
			// The ImageUI class will load images from the data/Quest/Images/ folder
			// and store them using FileStorage.server.Store()
		}

		#region ImageLoader

		private class ImageUI
		{
			private readonly string _paths;
			private readonly string _printPath;
			private readonly Dictionary<string, ImageData> _images;

			private enum ImageStatus
			{
				NotLoaded,
				Loaded,
				Failed
			}

			public ImageUI()
			{
				string root = Oxide.Core.OxideMod.ResolveServerRoot();
				string harmonyImages = Path.Combine(root, "HarmonyImages", Instance.Name);
				if (Directory.Exists(harmonyImages))
				{
					_paths = harmonyImages.Replace('\\', '/') + "/";
					_printPath = "HarmonyImages/" + Instance.Name + "/";
				}
				else
				{
					_paths = Instance.Name + "/Images/";
					_printPath = "HarmonyData/" + _paths;
				}
				_images = new Dictionary<string, ImageData>();
				
				// Only add images that actually exist in the quest data
				// Scan the quest data for CommandImageName values
				LoadImageNamesFromQuestData();
			}
			
			private void LoadImageNamesFromQuestData()
			{
				// Only load images from quest prize CommandImageName - no UI background images
				try
				{
					var questData = Interface.Oxide.DataFileSystem.ReadObject<List<QuestDefinition>>($"{Instance.Name}/Quest");
					if (questData != null)
					{
						foreach (var quest in questData)
						{
							if (quest.PrizeList != null)
							{
								foreach (var prize in quest.PrizeList)
								{
									if (!string.IsNullOrEmpty(prize.CommandImageName) && !_images.ContainsKey(prize.CommandImageName))
									{
										_images[prize.CommandImageName] = new ImageData();
										Instance.DebugPuts($"[Q] Added quest reward image to load list: {prize.CommandImageName}");
									}
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					Instance.PrintError($"[Q] Error loading image names from quest data: {ex.Message}");
				}
			}

			private class ImageData
			{
				public ImageStatus Status = ImageStatus.NotLoaded;
				public string Id { get; set; }
			}

			public string GetImage(string name)
			{
				if (string.IsNullOrEmpty(name))
					return null;
					
				ImageData image;
				if (_images.TryGetValue(name, out image))
				{
					if (image.Status == ImageStatus.Loaded)
						return image.Id;
					else if (image.Status == ImageStatus.NotLoaded)
					{
						// Image not loaded yet, trigger download
						Instance.DebugPuts($"[Q] Image '{name}' not loaded yet, triggering download");
						DownloadImage();
						return null;
					}
					else if (image.Status == ImageStatus.Failed)
					{
						// Don't spam the console with failed image messages
						return null;
					}
				}
				else
				{
					// Image not in dictionary, add it and try to load
					_images[name] = new ImageData();
					Instance.DebugPuts($"[Q] Image '{name}' not found in dictionary, adding to load list");
					DownloadImage();
				}
				return null;
			}

			public void DownloadImage()
			{
				KeyValuePair<string, ImageData>? image = null;
				foreach (KeyValuePair<string, ImageData> img in _images)
				{
					if (img.Value.Status == ImageStatus.NotLoaded)
					{
						image = img;
						break;
					}
				}

				if (image != null)
				{
					ServerMgr.Instance.StartCoroutine(ProcessDownloadImage(image.Value));
				}
				else
				{
					List<string> failedImages = new List<string>();
					List<string> loadedImages = new List<string>();

					foreach (KeyValuePair<string, ImageData> img in _images)
					{
						if (img.Value.Status == ImageStatus.Failed)
						{
							failedImages.Add(img.Key);
						}
						else if (img.Value.Status == ImageStatus.Loaded)
						{
							loadedImages.Add(img.Key);
						}
					}

					if (failedImages.Count > 0)
					{
						string images = string.Join(", ", failedImages);
						Instance.PrintWarning($"Failed to load the following images: {images}. These images will not be displayed. Make sure they exist in the '{_printPath}' folder.");
					}
					
					if (loadedImages.Count > 0)
					{
						Instance.DebugPuts($"[Q] Successfully loaded {loadedImages.Count} images: {string.Join(", ", loadedImages)}");
					}
					else
					{
						Instance.Puts($"[Q] No images were loaded. Make sure image files exist in the '{_printPath}' folder.");
					}
				}
			}

			public void UnloadImages()
			{
				foreach (KeyValuePair<string, ImageData> item in _images)
					if (item.Value.Status == ImageStatus.Loaded)
						if (item.Value?.Id != null)
							FileStorage.server.Remove(uint.Parse(item.Value.Id), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);

				_images?.Clear();
			}

			private IEnumerator ProcessDownloadImage(KeyValuePair<string, ImageData> image)
			{
				string filename = image.Key.EndsWith(".png") ? image.Key : image.Key + ".png";
				string url = _paths.IndexOf(':') >= 0 || _paths.StartsWith("/") || _paths.StartsWith("\\")
					? "file://" + _paths + filename
					: "file://" + Interface.Oxide.DataDirectory + "/" + _paths + filename;

				using UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
				yield return www.SendWebRequest();

				if (www.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
				{
					image.Value.Status = ImageStatus.Failed;
				}
				else
				{
					Texture2D tex = DownloadHandlerTexture.GetContent(www);
					image.Value.Id = FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString();
					image.Value.Status = ImageStatus.Loaded;
					UnityEngine.Object.DestroyImmediate(tex);
				}

				DownloadImage();
			}
		}

		#endregion

		#region Statistics
		private void GrabAndPostStatistics()
		{
			FancyMessage.Embed embed = new("Quest_STAT_1".GetAdaptedMessage(),"Quest_STAT_2".GetAdaptedMessage(null, _questStatistics.CompletedTasks, _questStatistics.TakenTasks, _questStatistics.DeclinedTasks));
            
			string mostExecutedInfo = ExtractQuestInfo(_questStatistics.GetTop5MostExecutedTasks());
			string leastExecutedInfo = ExtractQuestInfo(_questStatistics.GetTop5LeastExecutedTasks());
			
			FancyMessage.Embed embed2 = new("Quest_STAT_3".GetAdaptedMessage(), "Quest_STAT_4".GetAdaptedMessage(null, mostExecutedInfo, leastExecutedInfo));
			
			List<FancyMessage.Embed> embeds = new() { embed, embed2 };
			
			// Add player completion details if enabled
			if (_config.statisticsCollectionSettings.includePlayerDetails)
			{
				string playerCompletions = ExtractPlayerQuestCompletions();
				if (!string.IsNullOrEmpty(playerCompletions.Trim()))
				{
					FancyMessage.Embed embed3 = new("Quest_STAT_5".GetAdaptedMessage(), "Quest_STAT_6".GetAdaptedMessage(null, playerCompletions));
					embeds.Add(embed3);
				}
			}
			
			FancyMessage message = new(ConVar.Server.hostname ,embeds);

			string jsonEmbed = message.ToJson();

			SendDiscordNotification(jsonEmbed);
		}
		
		private readonly Dictionary<string, string> _headersDiscord = new()
		{
			{"Content-Type", "application/json"}
		};
		private void SendDiscordNotification(string json)
		{
			string url = $"{_config.statisticsCollectionSettings.discordWebhookUrl}?wait=true";
			webrequest.Enqueue(url, json, (code, response) =>
			{
				if (code == 200)
				{
					PrintWarning("Quest_STAT_CMD_3".GetAdaptedMessage());
				}
				else
				{
					PrintError($"[SendDiscordNotification] Error: {code}\n{response}");
				}
			}, this, RequestMethod.POST, _headersDiscord, 10F);
		}

		private void SendWipeSummaryReport()
		{
			if (!_config.statisticsCollectionSettings.useStatistics || string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl))
				return;

			try
			{
				var wipeSummary = FormatWipeSummaryReport();
				if (string.IsNullOrEmpty(wipeSummary))
				{
					PrintWarning("No wipe summary data to send to Discord");
					return;
				}

				// Send in chunks to avoid Discord size limits
				SendDiscordTextChunks(_config.statisticsCollectionSettings.discordWebhookUrl, wipeSummary, "Quest Statistics");
				PrintWarning("Wipe summary report sent to Discord (chunked)");
			}
			catch (Exception ex)
			{
				PrintError($"Error sending wipe summary report: {ex.Message}");
			}
		}

		private void SendDiscordTextChunks(string webhook, string message, string username)
		{
			if (string.IsNullOrEmpty(webhook) || string.IsNullOrEmpty(message))
				return;
			
			// Truncate username to Discord's 80 character limit
			string safeUsername = string.IsNullOrEmpty(username) ? "Quest" : username;
			if (safeUsername.Length > 80)
			{
				safeUsername = safeUsername.Substring(0, 77) + "...";
			}
			
			const int maxLen = 1800;
			var chunks = new List<string>();
			int index = 0;
			
			// Split message into chunks
			while (index < message.Length)
			{
				int len = Math.Min(maxLen, message.Length - index);
				chunks.Add(message.Substring(index, len));
				index += len;
			}
			
			PrintWarning($"[SendDiscordTextChunks] Sending {chunks.Count} chunks to Discord in sequence");
			
			// Send chunks sequentially with delays
			SendChunkSequentially(webhook, chunks, safeUsername, 0);
		}
		
		private void SendChunkSequentially(string webhook, List<string> chunks, string username, int chunkIndex)
		{
			if (chunkIndex >= chunks.Count)
			{
				PrintWarning($"[SendDiscordTextChunks] All {chunks.Count} chunks sent successfully");
				return;
			}
			
			string chunk = chunks[chunkIndex];
			var payload = new Dictionary<string, object>
			{
				{"content", chunk},
				{"username", username},
				{"avatar_url", "https://www.dropbox.com/scl/fi/cfqwdj0sqdtn7ydog3g14/gr.png?rlkey=0ataku53xk5ouytcskmvt5vxx&st=dlljaqox&dl=1"}
			};
			
			webrequest.Enqueue(webhook, Newtonsoft.Json.JsonConvert.SerializeObject(payload), (code, response) => 
			{
				if (code != 200 && code != 204)
				{
					PrintError($"[SendDiscordTextChunks] Chunk {chunkIndex + 1} failed with code {code}: {response}");
				}
				else
				{
					PrintWarning($"[SendDiscordTextChunks] Chunk {chunkIndex + 1} sent successfully");
				}
				
				// Send next chunk after a delay
				if (chunkIndex + 1 < chunks.Count)
				{
					timer.Once(1.0f, () => SendChunkSequentially(webhook, chunks, username, chunkIndex + 1));
				}
				else
				{
					PrintWarning($"[SendDiscordTextChunks] All {chunks.Count} chunks sent successfully");
				}
			}, this, RequestMethod.POST, _headersDiscord, 30f);
		}

		private string FormatWipeSummaryReport()
		{
			StringBuilder reportBuilder = new StringBuilder();
			
			// Check if wipe start time is valid
			if (_wipeStartTime == DateTime.MinValue)
			{
				reportBuilder.AppendLine("**Wipe Duration:** Unknown (wipe start time not recorded)");
				reportBuilder.AppendLine($"**Wipe End:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
				reportBuilder.AppendLine();
			}
			else
			{
				// Calculate wipe duration
				TimeSpan wipeDuration = DateTime.UtcNow - _wipeStartTime;
				string wipeDurationText = $"{wipeDuration.Days}d {wipeDuration.Hours}h {wipeDuration.Minutes}m";
				
				reportBuilder.AppendLine($"**Wipe Duration:** {wipeDurationText}");
				reportBuilder.AppendLine($"**Wipe Start:** {_wipeStartTime:yyyy-MM-dd HH:mm:ss} UTC");
				reportBuilder.AppendLine($"**Wipe End:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
				reportBuilder.AppendLine();
			}

			// Get all quest completions from the database (represents current wipe)
			var wipeCompletions = new List<(string playerName, string steamId, string questName, string rewards, string completedDate)>();
			
            foreach (var playerData in _playersInfo)
            {
                BasePlayer player = BasePlayer.FindByID(playerData.Key);
                string playerNameSafe = player != null ? player.displayName : playerData.Key.ToString();
                string steamIdSafe = playerData.Key.ToString();

                // Get all completions from the database (all data represents current wipe)
                List<CompletedQuestRecord> playerWipeCompletions = new List<CompletedQuestRecord>();
				if (playerData.Value.CompletedQuestHistory != null)
				{
					foreach (var record in playerData.Value.CompletedQuestHistory)
					{
						if (DateTime.TryParse(record.CompletedDate, out DateTime _))
							playerWipeCompletions.Add(record);
					}
					playerWipeCompletions.Sort((a, b) => DateTime.Compare(DateTime.Parse(a.CompletedDate), DateTime.Parse(b.CompletedDate)));
				}

				foreach (var record in playerWipeCompletions)
				{
					if (_questList.TryGetValue(record.QuestID, out QuestDefinition quest))
					{
						string rewards = FormatQuestRewards(quest.PrizeList);
						wipeCompletions.Add((
                            playerNameSafe,
                            steamIdSafe,
							quest.GetDisplayName(lang.GetServerLanguage()),
							rewards,
							record.CompletedDate
						));
					}
				}
			}

			// Check if we have any completions
			if (wipeCompletions.Count == 0)
			{
				// Show quest statistics even if no detailed completions
				reportBuilder.AppendLine("**No detailed quest completions found in database.**");
				reportBuilder.AppendLine();
				reportBuilder.AppendLine("**📊 QUEST STATISTICS:**");
				reportBuilder.AppendLine($"- Completed Tasks: {_questStatistics.CompletedTasks}");
				reportBuilder.AppendLine($"- Taken Tasks: {_questStatistics.TakenTasks}");
				reportBuilder.AppendLine($"- Declined Tasks: {_questStatistics.DeclinedTasks}");
				reportBuilder.AppendLine();
				reportBuilder.AppendLine("**Note:** Quest completion history may not be available, but statistics show quest activity.");
				return reportBuilder.ToString();
			}

			// Group by player and summarize
			var playerGroups = new Dictionary<(string, string), List<(string playerName, string steamId, string questName, string rewards, string completedDate)>>();
			foreach (var c in wipeCompletions)
			{
				var key = (c.playerName, c.steamId);
				if (!playerGroups.TryGetValue(key, out var list))
				{
					list = new List<(string, string, string, string, string)>();
					playerGroups[key] = list;
				}
				list.Add(c);
			}
			var playerSummaries = new List<(string PlayerName, string SteamId, List<(string, string, string, string, string)> Completions, int TotalQuests)>();
			foreach (var kv in playerGroups)
			{
				kv.Value.Sort((a, b) => DateTime.Compare(DateTime.Parse(a.Item5), DateTime.Parse(b.Item5)));
				playerSummaries.Add((kv.Key.Item1, kv.Key.Item2, kv.Value, kv.Value.Count));
			}
			playerSummaries.Sort((a, b) => b.TotalQuests.CompareTo(a.TotalQuests));

			reportBuilder.AppendLine($"**Total Quest Completions:** {wipeCompletions.Count}");
			reportBuilder.AppendLine($"**Active Players:** {playerSummaries.Count}");
			reportBuilder.AppendLine();

			// Show top performers
			reportBuilder.AppendLine("**🏆 TOP QUEST COMPLETERS:**");
			int takeCount = Math.Min(10, playerSummaries.Count);
			for (int i = 0; i < takeCount; i++)
			{
				var p = playerSummaries[i];
				reportBuilder.AppendLine($"**{p.PlayerName}** ({p.SteamId}) - {p.TotalQuests} quests completed");
			}
			reportBuilder.AppendLine();

			// Show detailed completions for each player
			reportBuilder.AppendLine("**📋 DETAILED QUEST COMPLETIONS:**");
			foreach (var player in playerSummaries)
			{
				reportBuilder.AppendLine($"**{player.PlayerName}** ({player.SteamId}) - {player.TotalQuests} quests:");
				
				foreach (var completion in player.Completions)
				{
					DateTime completedDate = DateTime.Parse(completion.Item5);
					reportBuilder.AppendLine($"• {completion.Item3} - {completedDate:MM/dd HH:mm}");
					if (!string.IsNullOrEmpty(completion.Item4) && completion.Item4 != "No rewards")
					{
						reportBuilder.AppendLine($"  Rewards: {completion.Item4}");
					}
				}
				reportBuilder.AppendLine();
			}

			return reportBuilder.ToString();
		}
		private string ExtractQuestInfo(Dictionary<long, int> quests)
		{
			StringBuilder questInfoBuilder = new StringBuilder();

			foreach (KeyValuePair<long, int> questPair in quests)
			{
				QuestDefinition foundQuest;
				if (_questList.TryGetValue(questPair.Key, out foundQuest))
				{
					questInfoBuilder.AppendLine($"- **{foundQuest.GetDisplayName(lang.GetServerLanguage())}**: {questPair.Value}");
				}
			}

			return questInfoBuilder.ToString();
		}

		private string ExtractPlayerQuestCompletions()
		{
			StringBuilder playerInfoBuilder = new StringBuilder();
			var recentCompletions = new List<(string playerName, string steamId, string questName, string rewards, string completedDate)>();

			// Calculate time frame for this report
			DateTime now = DateTime.Now;
			DateTime startTime = now.AddHours(-24);
			string timeFrameText = $"**Time Frame:** {startTime:yyyy-MM-dd HH:mm} to {now:yyyy-MM-dd HH:mm} (Last 24 Hours)";

			// Get all players and their completed quest history
            foreach (var playerData in _playersInfo)
            {
                BasePlayer player = BasePlayer.FindByID(playerData.Key);
                string playerNameSafe = player != null ? player.displayName : playerData.Key.ToString();
                string steamIdSafe = playerData.Key.ToString();

                // Get recent completions (last 24 hours)
                List<CompletedQuestRecord> recentQuests = new List<CompletedQuestRecord>();
				if (playerData.Value.CompletedQuestHistory != null)
				{
					foreach (var record in playerData.Value.CompletedQuestHistory)
					{
						if (!DateTime.TryParse(record.CompletedDate, out DateTime completedDate)) continue;
						if (completedDate >= startTime && completedDate <= now)
							recentQuests.Add(record);
					}
					recentQuests.Sort((a, b) => DateTime.Compare(DateTime.Parse(b.CompletedDate), DateTime.Parse(a.CompletedDate)));
					if (recentQuests.Count > 5) recentQuests.RemoveRange(5, recentQuests.Count - 5);
				}

				foreach (var record in recentQuests)
				{
					if (_questList.TryGetValue(record.QuestID, out QuestDefinition quest))
					{
						string rewards = FormatQuestRewards(quest.PrizeList);
						recentCompletions.Add((
                            playerNameSafe,
                            steamIdSafe,
							quest.GetDisplayName(lang.GetServerLanguage()),
							rewards,
							record.CompletedDate
						));
					}
				}
			}

			// Add time frame header
			playerInfoBuilder.AppendLine(timeFrameText);
			playerInfoBuilder.AppendLine();

			// Check if we have any completions
			if (recentCompletions.Count == 0)
			{
				playerInfoBuilder.AppendLine("No quest completions in the specified time frame.");
				return playerInfoBuilder.ToString();
			}

			// Sort by completion date (most recent first) and take top 20
			recentCompletions.Sort((a, b) => DateTime.Compare(DateTime.Parse(b.Item5), DateTime.Parse(a.Item5)));
			int topCount = Math.Min(20, recentCompletions.Count);
			var sortedCompletions = new List<(string, string, string, string, string)>();
			for (int i = 0; i < topCount; i++) sortedCompletions.Add(recentCompletions[i]);

			// Group by player for better formatting
			var groupedByPlayer = new Dictionary<(string, string), List<(string, string, string, string, string)>>();
			foreach (var c in sortedCompletions)
			{
				var key = (c.Item1, c.Item2);
				if (!groupedByPlayer.TryGetValue(key, out var list))
				{
					list = new List<(string, string, string, string, string)>();
					groupedByPlayer[key] = list;
				}
				list.Add(c);
			}

			foreach (var kv in groupedByPlayer)
			{
				playerInfoBuilder.AppendLine($"**{kv.Key.Item1}** ({kv.Key.Item2})");
				
				foreach (var completion in kv.Value)
				{
					playerInfoBuilder.AppendLine($"- {completion.Item3}");
					if (!string.IsNullOrEmpty(completion.Item4))
					{
						playerInfoBuilder.AppendLine($"  - {completion.Item4}");
					}
				}
				playerInfoBuilder.AppendLine();
			}

			return playerInfoBuilder.ToString();
		}

		private string FormatQuestRewards(List<QuestDefinition.Prize> prizeList)
		{
			if (prizeList == null || prizeList.Count == 0)
				return "No rewards";

			var rewardStrings = new List<string>();
			
			foreach (var prize in prizeList)
			{
				if (prize.IsHidden) continue;

				switch (prize.PrizeType)
				{
					case PrizeType.Item:
						rewardStrings.Add($"{prize.ItemAmount}x {prize.ItemShortName}");
						break;
					case PrizeType.CustomItem:
						rewardStrings.Add($"{prize.ItemAmount}x {prize.CustomItemName}");
						break;
					case PrizeType.BluePrint:
						rewardStrings.Add($"Blueprint: {prize.ItemShortName}");
						break;
					case PrizeType.Command:
						rewardStrings.Add($"Command: {prize.PrizeCommand}");
						break;
				}
			}

			return rewardStrings.Count > 0 ? string.Join(", ", rewardStrings) : "No rewards";
		}

		#endregion
		
		#region DiscordClass

        public class FancyMessage
        {
            [JsonProperty("content")] public string Content;

            [JsonProperty("username")] public string Username;

            [JsonProperty("avatar_url")] public string AvatarUrl;

            [JsonProperty("embeds")] public List<Embed> Embeds;

            public FancyMessage(string content, List<Embed> embeds)
            {
                Content = content;
                Username = "Quest Statistics";
                AvatarUrl = "https://www.dropbox.com/scl/fi/cfqwdj0sqdtn7ydog3g14/gr.png?rlkey=0ataku53xk5ouytcskmvt5vxx&st=dlljaqox&dl=1";
                Embeds = embeds;
            }

            public string ToJson() => JsonConvert.SerializeObject(this);

            public class Embed
            {
                [JsonProperty("title")] public string Title { get; }
                [JsonProperty("description")] public string Description { get; }
                [JsonProperty("color")] public int Color { get; }
                [JsonProperty("timestamp")] public string Timestamp { get; }

                public Embed(string title, string description = "")
                {
                    Title = title;
                    Description = description;
                    Color = 16689937;
                    Timestamp = DateTime.UtcNow.ToString("o");
                }
            }
            
        }

        #endregion

		#region Data
		private List<QuestDefinition> LoadQuestList()
		{
			return Interface.Oxide.DataFileSystem.ExistsDatafile($"{Name}/{_config.settings.questListDataName}")
				? Interface.Oxide.DataFileSystem.ReadObject<List<QuestDefinition>>($"{Name}/{_config.settings.questListDataName}")
				: null;
		}

		private void LoadQuestData()
		{
			List<QuestDefinition> questList = LoadQuestList();
			if (questList != null)
			{
				HashSet<long> currentQuestIds = new();
				foreach (QuestDefinition quest in questList)
				{
					currentQuestIds.Add(quest.QuestID);
					_questList.Add(quest.QuestID, quest);
					_questStatistics.TaskExecutionCounts.TryAdd(quest.QuestID, 0);
					_loadedQuestOrder.Add(quest.QuestID);
				}

				List<long> keysToRemove = new();
				foreach (long taskId in _questStatistics.TaskExecutionCounts.Keys)
					if (!currentQuestIds.Contains(taskId))
						keysToRemove.Add(taskId);
				
				foreach (long key in keysToRemove)
					_questStatistics.TaskExecutionCounts.Remove(key);
				SaveData();
			}
			else
			{
				_questList = new Dictionary<long, QuestDefinition>();
			}

			if (!permission.PermissionExists("quest.admin", this))
				permission.RegisterPermission("quest.admin", this);

			if (_questList.Count > 0)
			{
				foreach (QuestDefinition quest in _questList.Values)
				{
					if (!string.IsNullOrEmpty(quest.QuestPermission) && !permission.PermissionExists($"{Name}." + quest.QuestPermission, this))
					{
						permission.RegisterPermission($"{Name}." + quest.QuestPermission, this);
					}

					if (quest.QuestType == QuestType.EntityKill)
					{
						if (quest.Target.Contains(","))
						{
							quest.IsMoreTarget = true;
							quest.Targets = quest.Target.Split(',');
						}
					}
				}
			}

			// Rebuild category map from loaded data (or default mapping if none provided)
			BuildCategoriesFromData();
			// Optionally persist missing QuestCategory back into data file to match legacy mapping
			PersistQuestCategoriesIfMissing();
			// Persist the quest order to match the /quest UI (now fully dynamic)
			PersistQuestOrderByUi();
		}
		private class QuestStatistics
		{
			public int CompletedTasks, TakenTasks, DeclinedTasks;

			public Dictionary<long, int> TaskExecutionCounts = new();

			#region Metods
			public void GatherTaskStatistics(TaskType taskType, long? taskId = null)
			{
				switch (taskType)
				{
					case TaskType.Completed:
						CompletedTasks += 1;
						break;
					case TaskType.Taken:
						TakenTasks += 1;
						break;
					case TaskType.Declined:
						DeclinedTasks += 1;
						break;
					case TaskType.TaskExecution:
						if (taskId.HasValue)
						{
							if (!TaskExecutionCounts.TryAdd(taskId.Value, 1))
							{
								TaskExecutionCounts[taskId.Value] += 1;
							}
						}
						else
						{
							throw new ArgumentNullException("For TaskExecution type, taskId must be provided.");
						}

						break;
					default:
						throw new ArgumentException("Unknown task type");
				}
			}
            
			public Dictionary<long, int> GetTop5MostExecutedTasks()
			{
				List<KeyValuePair<long, int>> topTasks = new List<KeyValuePair<long, int>>();
    
				foreach (KeyValuePair<long, int> task in TaskExecutionCounts)
				{
					if (topTasks.Count < 5)
					{
						topTasks.Add(task);
						topTasks.Sort((a, b) => b.Value.CompareTo(a.Value));
					}
					else
					{
						if (task.Value > topTasks[4].Value)
						{
							topTasks[4] = task;
							topTasks.Sort((a, b) => b.Value.CompareTo(a.Value));
						}
					}
				}

				Dictionary<long, int> result = new Dictionary<long, int>();
				foreach (KeyValuePair<long, int> kvp in topTasks)
				{
					result[kvp.Key] = kvp.Value;
				}

				return result;
			}
        
			public Dictionary<long, int> GetTop5LeastExecutedTasks()
			{
				List<KeyValuePair<long, int>> leastTasks = new List<KeyValuePair<long, int>>();
    
				foreach (KeyValuePair<long, int> task in TaskExecutionCounts)
				{
					if (leastTasks.Count < 5)
					{
						leastTasks.Add(task);
						leastTasks.Sort((a, b) => a.Value.CompareTo(b.Value));
					}
					else
					{
						if (task.Value < leastTasks[4].Value)
						{
							leastTasks[4] = task;
							leastTasks.Sort((a, b) => a.Value.CompareTo(b.Value));
						}
					}
				}

				Dictionary<long, int> result = new Dictionary<long, int>();
				foreach (KeyValuePair<long, int> kvp in leastTasks)
				{
					result[kvp.Key] = kvp.Value;
				}

				return result;
			}

			#endregion
		}

		private void LoadQuestStatisticsData()
		{
			_questStatistics = Interface.Oxide.DataFileSystem.ReadObject<QuestStatistics>(this.Name + $"/QuestStatistics");
			if (_questStatistics == null)
			{
				_questStatistics = new QuestStatistics();
			}
		}
		private void LoadPlayerData()
		{
			_playersInfo = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>(this.Name + $"/PlayerInfo");
			if (_playersInfo == null)
			{
				_playersInfo = new Dictionary<ulong, PlayerData>();
			}
		}

		private void SaveData()
		{
			Interface.Oxide.DataFileSystem.WriteObject(this.Name + $"/PlayerInfo", _playersInfo);
			Interface.Oxide.DataFileSystem.WriteObject(this.Name + $"/QuestStatistics", _questStatistics);
		}

		#endregion
	}
}

namespace Oxide.Plugins.QuestExtensionMethods
{
	public static class ExtensionMethods
	{
		private static readonly Lang Lang = Interface.Oxide.GetLibrary<Lang>();

		#region GetLang
		
		public static string GetAdaptedMessage(this string langKey, in string userID, params object[] args)
		{
			string message = Lang.GetMessage(langKey, Quest.Instance, userID);
			
			StringBuilder stringBuilder = Pool.Get<StringBuilder>();
		
			try
			{
				return stringBuilder.AppendFormat(message, args).ToString();
			}
			finally
			{
				stringBuilder.Clear();
				Pool.FreeUnmanaged(ref stringBuilder);
			}
		}
		
		public static string GetAdaptedMessage(this string langKey, in string userID = null)
		{
			return Lang.GetMessage(langKey, Quest.Instance, userID);
		}
		
		#endregion
		
		#region Pagination

		public static IEnumerable<T> Page<T>(this List<T> source, int page, int pageSize)
		{
			int start = page * pageSize;
			int end = start + pageSize;
			for (int i = start; i < end && i < source.Count; i++)
			{
				yield return source[i];
			}
		}

		#endregion

		#region Entity Extensions
		
		public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;
		
		#endregion
	}
}