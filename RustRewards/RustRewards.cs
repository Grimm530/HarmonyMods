using System;
using System.Collections.Generic; 
using System.Linq;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using Newtonsoft.Json;

namespace RustRewardsHarmony
{
    // To do

    //  Fix teleport distance abuse. 
    //  Add API for GetMultiplier and NotifyReward 
    //  Track user supply drops and give a different reward.PPlank  
    //  Add permission to enable player settings UI.
    //  Add growables as new section.
    //  Consider adding rewards to all in team/clan ?
    //  Confirm what happens when user makes mistake in config - Reports of config file becoming null.
    //  Add kill reward for street signs?
    //  TODO - Add GrimmBoss and BetterNPC   




    //  Changes in 3.2.4
    //  Automatically updated old sticky note image url to new url, if in use.
	//  Changes to 'Add Multipliers'. Fractions are ignored.
	//  Rounded final reward value to whole number for scrap/SR, and two places for Economics.
	//  Fix for missing DriftWood entry.
	//  Finally fixed NRE in GiveReward, from OnPlayedDeath
	//  Added Kill and harvest rewards for JungleScientist and snake.
	//  Added Jungle trees.
	//  Auto-removes unused/outdated config entries.
	//  Changes in 3.2.5 (ported)
	//  Added new vine and schizo trees.
	//  Fixed issue with happy hour sync after plugin reload.
	//  Broader sapling prefab matching (_sapling_).


    /// <summary>
    /// RustRewards 3.2.5 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// </summary>
    public class RustRewards : RustRewardsPluginBase
	{
		bool loaded = false;
        Plugin Clans, Economics, Friends, GUIAnnouncements, NoEscape, ServerRewards, ZoneManager, RaidableBases, PlaytimeTracker;

		Dictionary<ulong, Dictionary<ulong, float>> VehicleAttackers = new Dictionary<ulong, Dictionary<ulong, float>>();

		Queue<(string webhook, string content)> _discordOutgoing = new Queue<(string webhook, string content)>();
		bool _discordWebhookBusy = false;
		bool _dataDirty = false;

		private bool EventTerritory(BaseEntity entity)
		{
			if (!RaidableBases || RaidableBases == null)
				return false;
			return entity.OwnerID == 0 && Convert.ToBoolean(RaidableBases?.Call("EventTerritory", entity.transform.position));
		}

		public static RustRewards rr;

		public RustRewards()
		{
			Version = new VersionNumber(3, 2, 5);
		}

		private const string AdminUIPermission = "rustrewards.adminui";
		private const string HarvestPermission = "rustrewards.harvest"; 
		private const string KillPermission = "rustrewards.kill";
		private const string OpenPermission = "rustrewards.open";
		private const string PickupPermission = "rustrewards.pickup";
		private const string ActivityPermission = "rustrewards.activity";
		private const string WelcomePermission = "rustrewards.welcome";

		public enum RewardType { Kill, Harvest, Open, Pickup, Activity, Welcome }
		public enum Currency { None, Scrap, ServerRewards, Economics };
		public Currency currency = Currency.None;

		int CurrentHour = 0;
		int CurrentDay => (int)DateTime.UtcNow.AddHours(conf?.Settings?.General != null ? conf.Settings.General.UTCHourOffset : 0).DayOfWeek;

		public bool IsNight()
		{
			if (conf.Settings.General.UseServerDayNightHours)
				return TOD_Sky.Instance.IsNight;

			CurrentHour = (int)TOD_Sky.Instance.Cycle.Hour;
			if (conf.Settings.General.UseRealTime)
				CurrentHour = DateTime.UtcNow.Hour + conf.Settings.General.UTCHourOffset;

			if (conf.Settings.General.NightStartHour > conf.Settings.General.DayStartHour)
				return CurrentHour >= conf.Settings.General.NightStartHour || CurrentHour < conf.Settings.General.DayStartHour;
			else
				return CurrentHour >= conf.Settings.General.NightStartHour && CurrentHour < conf.Settings.General.DayStartHour;
		}

		public bool HappyHour()
		{
			CurrentHour = (int)TOD_Sky.Instance.Cycle.Hour; 
			if (conf.Settings.General.UseRealTime)
				CurrentHour = DateTime.UtcNow.Hour + conf.Settings.General.UTCHourOffset;

			if (conf.Settings.General.HappyHour_BeginHour > conf.Settings.General.HappyHour_EndHour)
				return CurrentHour > conf.Settings.General.HappyHour_BeginHour || CurrentHour < conf.Settings.General.HappyHour_EndHour; 
			else
				return CurrentHour > conf.Settings.General.HappyHour_BeginHour && CurrentHour < conf.Settings.General.HappyHour_EndHour;
		}

		#region ConfigPrep
		public Dictionary<string, double> WeaponsList = new Dictionary<string, double>();

		public Kill Kills = new Kill();
		public class Kill
		{
			public Dictionary<string, double> NPCs = new Dictionary<string, double>();
			public Dictionary<string, double> Animals = new Dictionary<string, double>() { { "simpleshark", 0.0 } };
			public Dictionary<string, double> Vehicles = new Dictionary<string, double>() { { "patrolhelicopter", 0.0 }, { "bradleyapc", 0.0 }, { "ch47.entity", 0.0 }, { "ch47scientists.entity", 0.0 } };
			public Dictionary<string, double> MountedWeapons = new Dictionary<string, double>();
			public Dictionary<string, double> Players = new Dictionary<string, double>() { { "Players", 0.0 }, { "Suicide", 0.0 }, { "Death", 0.0 }, { "Sleepers", 0.0 } };
		}

		public Harvest Harvests = new Harvest();
        List<string> OddWood = new List<string>()
            {
                "birch_tiny_tundra", "birch_small_tundra", "birch_medium_tundra", "birch_big_tundra", "birch_large_tundra",
                "birch_tiny_temp", "birch_small_temp", "birch_medium_temp", "birch_big_temp", "birch_large_temp",
                "wood-pile",
                "douglas_fir_d_small",
                "palm_tree_short_a_entity",  "palm_tree_short_b_entity", "palm_tree_short_c_entity",
                "palm_tree_small_a_entity", "palm_tree_small_b_entity", "palm_tree_small_c_entity", "palm_tree_med_a_entity",  "palm_tree_tall_a_entity", "palm_tree_tall_b_entity",
            };
        string TreeTypes(string name)
		{ 
			if (OddWood.Contains(name))
				return name;
			if (name.Contains("cactus-"))
				return "Cactus";
			if (name.Contains("swamp_tree"))
				return "Swamp_Tree";
            if (name.Contains("_sapling_"))
                return "Sapling";
            if (name.Contains("oak_"))
                return "Oak";
            if (name.Contains("pine_"))
                return "Pine";
            if (name.Contains("american_beech_")) 
                return "American_Beech";
            if (name.Contains("_log_"))
                return "Log"; 
            if (name.Contains("driftwood_"))
                return "Driftwood";
            if (name.Contains("douglas_fir_"))
                return "Douglas_Fir";
            if (name.Contains("mauritia"))
                return "Mauritia_Flexuosa";
            if (name.Contains("trumpet")) 
                return "Trumpet_Tree";
            if (name.Contains("crepitans"))
                return "Hura_Crepitans";
			if (name.Contains("vineswingingtree"))
				return "Vine_Swinging_Tree";
			if (name.Contains("schizolobium"))
				return "Schizolobium";
            return string.Empty;
        }

		public class Harvest 
		{
			public Dictionary<string, double> Flesh = new Dictionary<string, double>() { };
			public Dictionary<string, double> Ore = new Dictionary<string, double>() { { "stone", 0.0 }, { "metal", 0.0 }, { "sulfur", 0.0 }, };
			public Dictionary<string, double> Tree = new Dictionary<string, double>() { { "Cactus", 0.0 }, { "Swamp_Tree", 0.0 }, { "Sapling", 0.0 }, { "Oak", 0.0 }, { "Pine", 0.0 }, { "American_Beech", 0.0 }, { "Log", 0.0 }, { "Driftwood", 0.0 }, { "Douglas_Fir", 0.0 }, { "Mauritia_Flexuosa", 0.0 }, { "Trumpet_Tree", 0.0 }, { "Hura_Crepitans", 0.0 }, { "Vine_Swinging_Tree", 0.0 }, { "Schizolobium", 0.0 } }; 

			/*
			CACTUS
				cactus-1, cactus-2, cactus-3, cactus-4, cactus-5, cactus-6, cactus-7
			SWAMP TREE
				swamp_tree_c, swamp_tree_f, swamp_tree_d, swamp_tree_e, swamp_tree_a, swamp_tree_b
			SAPLING
				pine_sapling_d, pine_sapling_e, pine_sapling_c, pine_sapling_a, pine_sapling_b, pine_sapling_a_snow, pine_sapling_b_snow, pine_sapling_d_snow, pine_sapling_e_snow, pine_sapling_c_snow
			OAK
				oak_b, oak_c, oak_d, oak_e, oak_f
			PINE
				pine_a, pine_b, pine_c, pine_d, pine_dead_snow_a, pine_dead_snow_b, pine_dead_snow_c, pine_dead_snow_d, pine_dead_snow_e, pine_dead_snow_f. pine_dead_a, pine_dead_b, pine_dead_c, pine_dead_d, pine_dead_e, pine_dead_f
			AMERICAN BEECH
				american_beech_a, american_beech_b, american_beech_c, american_beech_d, american_beech_e, american_beech_a_dead, american_beech_e_dead
			LOG
				ead_log_b, dead_log_c, dead_log_a
			DRIFTWOOD
				driftwood_set_3, driftwood_set_1, driftwood_set_2, driftwood_1, driftwood_2, driftwood_3driftwood_4, driftwood_5
			FIR
				douglas_fir_a_snow, douglas_fir_b_snow, douglas_fir_c_snow, douglas_fir_a, douglas_fir_b, douglas_fir_c, douglas_fir_d
			*/
        }

        public Dictionary<string, double> Open = new Dictionary<string, double>();
		public Dictionary<string, double> Pickup = new Dictionary<string, double>();
		#endregion

		const int ScrapId = -932201673;

		#region SetupAndTakedown
		internal void Init() 
		{
			RegisterPerm(AdminUIPermission);
			RegisterPerm(ActivityPermission);
			RegisterPerm(HarvestPermission);
			RegisterPerm(KillPermission);
			RegisterPerm(OpenPermission);
			RegisterPerm(PickupPermission);
			RegisterPerm(WelcomePermission);

			lang.RegisterMessages(Messages, this); 
		}
		 
		internal void OnServerSave() 
		{
			SaveConf();
			if (_dataDirty)
				SaveData();
		}

		bool newsave = false;
		internal void OnNewSave(string filename)
		{
			newsave = true;
			
			// Send final wipe summary report before resetting data
			if (conf?.Settings?.DiscordReporting?.EnableDiscordReporting == true && !string.IsNullOrEmpty(conf.Settings.DiscordReporting.DiscordWebhook))
			{
				SendWipeSummaryReport();
			}

			// Reset all player statistics for the new wipe
			if (storedData?.PlayerStatistics != null)
			{
				foreach (var playerStats in storedData.PlayerStatistics.Values)
				{
					playerStats.ResetWipeStats();
				}
			}

			// Capture per-wipe playtime baselines from PlaytimeTracker
			if (IsPlaytimeTrackerAvailable())
			{
				storedData.WipeBaselinePlay.Clear();
				storedData.WipeBaselineAFK.Clear();
				foreach (var kv in storedData.PlayerStatistics)
				{
					var id = kv.Key;
					var pt = GetPlayerPlayTime(id);
					var afk = GetPlayerAFKTime(id);
					if (pt is double p)
						storedData.WipeBaselinePlay[id] = p;
					if (afk is double a)
						storedData.WipeBaselineAFK[id] = a;
				}
			}

			// Clear other wipe-related data
			RewardSeconds.Clear();
			LastLoc.Clear();
			LastKills.Clear();

			MarkDataDirty();
			PrintWarning("Server wipe detected! Player statistics reset and final report sent.");
		}

		internal void OnServerInitialized()
		{
			rr = this;
			// Set up config information in advance.   
			var Weapons = ItemManager.itemList.Where(x => x.category == ItemCategory.Weapon && !x.shortname.Contains("weapon.mod")); 

			foreach (var entry in Weapons)
				WeaponsList[entry.shortname] = 1.0;  
			
			ImportPlaytimeTrackerNames();

			// Backfill player name cache for currently connected and sleeping players
			foreach (var p in BasePlayer.activePlayerList)
			{
				if (p != null && !string.IsNullOrEmpty(p.displayName))
					storedData.PlayerNames[p.userID] = p.displayName;
			}
			foreach (var p in BasePlayer.sleepingPlayerList)
			{
				if (p != null && !string.IsNullOrEmpty(p.displayName))
					storedData.PlayerNames[p.userID] = p.displayName;
			}

			// Use prefab definitions instead of scanning every live server entity twice at startup.
			var AllEnts = Resources.FindObjectsOfTypeAll<BaseEntity>();
			var ItemMods = Resources.FindObjectsOfTypeAll<ItemMod>();

            foreach (var entry in ItemMods.OfType<ItemModUnwrap>().Where(x => x != null))
			{
				DictAdd(entry.name);
				Open[entry.name] = 0.0; 
			}

			foreach (var entry in ItemMods.OfType<ItemModMenuOption>().Where(x => x != null && x.option.name.english == "Gut"))
			{
				DictAdd(entry.name);
				Harvests.Flesh[entry.name] = 0.0;
			}
			List<string> Trees = new List<string>();
			List<string> Exclusions = new List<string>() { "CH47Helicopter", "CH47HelicopterAIController", "BaseArcadeMachine", "CardTable", "BaseCrane" };
            foreach (var e in AllEnts)
			{
				if (e == null)
					continue;



				if (e is TreeEntity)
					Trees.Add(e.ShortPrefabName);


				if (e is LootContainer)
				{
                    string name = e.ShortPrefabName; 
                    if (name.Contains("roadsign") || name.Contains("dm ") || name.Contains("test "))
                        continue;
                    if (e.PrefabName.Contains("underwater_labs"))
                        name = "underwater_labs_" + e.ShortPrefabName;

                    DictAdd(name);
                    Open[name] = 0.0;
                    continue; 
				}

                if (e is BaseNpc || e is BaseNPC2 || e is WildlifeHazard)
                {
                    if (e.ShortPrefabName.Contains("tutorial"))
                        continue;

                    DictAdd(e.ShortPrefabName);
                    Kills.Animals[e.ShortPrefabName] = 0.0;
                    continue;
                }

				var rd = e.GetComponent<ResourceDispenser>();
				if (rd != null)
				{
                    string name = e.ShortPrefabName;
                    if (name == "scientist_corpse" || name == "murderer_corpse")
                        continue;

					if (name == "snake.corpse")
					{
                        DictAdd("snake.entity");
                        Harvests.Flesh["snake.entity"] = 0.0;
						continue;
                    }

                    if (e.GetComponent<ResourceDispenser>().gatherType == ResourceDispenser.GatherType.Flesh)
                    {
                        DictAdd(e.ShortPrefabName);
                        Harvests.Flesh[e.ShortPrefabName] = 0.0;
						continue;
                    }
                }

				if (e is CollectibleEntity || e is GrowableEntity)
				{
                    DictAdd(e.ShortPrefabName);
                    Pickup[e.ShortPrefabName] = 0.0;
					continue;
                }
		 
                if ((e is GunTrap || e is SamSite || e is AutoTurret || e is FlameTurret))
				{
					//if (e.ShortPrefabName.Contains("deployed"))
					//{
						DictAdd(e.ShortPrefabName);
						Kills.MountedWeapons[e.ShortPrefabName] = 0.0;
						continue;
					//}
					//else
					//	Puts(e.ShortPrefabName + " - ignored");
                }

				if (e is BaseVehicle)
				{
					if (!Exclusions.Contains(e.GetType().ToString()))
						Kills.Vehicles[e.GetType().ToString()] = 0.0;
				}
            }

			//Trees = Trees.Distinct().ToList();
			//foreach (var tree in Trees)
			//	if (TreeTypes(tree) == string.Empty)
			//		Puts(tree);
			//  Print trees which aren't handled.

            foreach (var entry in new List<string>() { "BotReSpawn", "ZombieHorde", "OilRig", "Excavator", "CompoundScientist", "BanditTown", "MountedScientist", "JunkPileScientist", "DungeonScarecrow", "ScareCrow", "MilitaryTunnelScientist", "CargoShip", "APCScientist", "APCScientistHeavy", "HeavyScientist", "JungleScientist", "TunnelDweller", "UnderwaterDweller", "Trainyard", "Airfield", "DesertScientist", "ArcticResearchBase", "NuclearMissileSilo", "LaunchSite", "Gingerbread" })
			{
				DictAdd(entry);
				Kills.NPCs[entry] = 0.0; 
				Harvests.Flesh[entry] = 0.0;
			}

            foreach (var ore in Harvests.Ore.Keys)
                DictAdd(ore);
            foreach (var veh in Kills.Vehicles.Keys) 
                DictAdd(veh);
            //NEW
            foreach (var oddwood in OddWood)
                Harvests.Tree[oddwood] = 0.0; 

			foreach (var tree in Harvests.Tree.Keys) 
				DictAdd(tree);
			//

			StoreReferences();

            if (!LoadConfigVariables()) 
            {
                Puts("Config file issue detected. Please delete file, or check syntax and fix.");
                return;
            }

            RemoveUnusedAndSort();

            if (conf.Settings.UI.BackgroundImage == "https://www.wallpapertip.com/wmimgs/16-169722_transparent-background-sticky-note-clipart.png")
				conf.Settings.UI.BackgroundImage = "RustRewards/pinned.png"; 

            foreach (var entry in conf.Group_Multipliers)
				if (!permission.GroupExists(entry.Key))
					permission.CreateGroup(entry.Key, entry.Key, 0); 

			DictAdd("simpleshark");

            foreach (var entry in conf.Permission_Multipliers.Where(x => !x.Key.Contains(".")))
				RegisterPerm(Title + "." + entry.Key);
			CheckDependencies();

			cmd.AddChatCommand($"{conf.Settings.UI.MainCommandAlias}", this, "RustRewardsUI");

			// Preload UI background from local file path into FileStorage
			LoadLocalUiBackground();

			if (newsave && conf.Settings.General.Reset_Activity_Reward_At_Wipe)
				foreach (var record in storedData.PlayerPrefs)
					record.Value.Activity_Given = false;

			foreach (var player in BasePlayer.activePlayerList)
				OnPlayerConnected(player);

			if (conf.Settings.Rewards.ActivityReward_Seconds > 0)
				ServerMgr.Instance.InvokeRepeating(this.CheckActivity, 1, 60);

			if (conf.Settings.Multipliers.HappyHour > 1)
			{
				HappyHourRef = HappyHour();
				first = false;
				ServerMgr.Instance.InvokeRepeating(this.CheckHappyHour, 1, 15);
			}

			// Initialize Discord reporting timer
			if (conf.Settings.DiscordReporting.EnableDiscordReporting && !string.IsNullOrEmpty(conf.Settings.DiscordReporting.DiscordWebhook))
			{
				_discordReportTimer = timer.Every(conf.Settings.DiscordReporting.ReportIntervalHours * 3600, SendDiscordReport);
				PrintWarning($"Discord reporting enabled - reports every {conf.Settings.DiscordReporting.ReportIntervalHours} hours");
			}

            PurgeOldPrefs();
            if (_dataDirty)
				SaveData();
			SaveConf();
            loaded = true;
		}

		List<string> RefKillAnimals;
        List<string> RefKillMountedWeapons;
        List<string> RefKillNPCs;
        List<string> RefKillPlayers;
        List<string> RefKillVehicles;
		List<string> RefHarvestFlesh;
        List<string> RefHarvestOre;
		List<string> RefHarvestTree;
		List<string> RefOpen;
		List<string> RefPickup;
		List<string> RefMultipliers;

        void StoreReferences()
		{
			RefKillAnimals = Kills.Animals.Keys.ToList();
            RefKillMountedWeapons = Kills.MountedWeapons.Keys.ToList();
            RefKillNPCs = Kills.NPCs.Keys.ToList();
            RefKillPlayers = Kills.Players.Keys.ToList();
            RefKillVehicles = Kills.Vehicles.Keys.ToList();
            RefHarvestFlesh = Harvests.Flesh.Keys.ToList(); 
            RefHarvestOre = Harvests.Ore.Keys.ToList();
            RefHarvestTree = Harvests.Tree.Keys.ToList();
            RefOpen = Open.Keys.ToList();
            RefPickup = Pickup.Keys.ToList();
            RefMultipliers = WeaponsList.Keys.ToList();
        }

		void RemoveUnusedAndSort()
		{
			// Preserve config values; only drop keys the prefab scan no longer knows about,
			// and add any newly scanned keys at 0. Never replace conf amounts with rr.* defaults (0).
			conf.RewardTypes.Kill.Animals = MergeSorted(conf.RewardTypes.Kill.Animals, rr.Kills.Animals, RefKillAnimals);
			conf.RewardTypes.Kill.MountedWeapons = MergeSorted(conf.RewardTypes.Kill.MountedWeapons, rr.Kills.MountedWeapons, RefKillMountedWeapons);
			conf.RewardTypes.Kill.NPCs = MergeSorted(conf.RewardTypes.Kill.NPCs, rr.Kills.NPCs, RefKillNPCs);
			conf.RewardTypes.Kill.Players = MergeSorted(conf.RewardTypes.Kill.Players, rr.Kills.Players, RefKillPlayers);
			conf.RewardTypes.Kill.Vehicles = MergeSorted(conf.RewardTypes.Kill.Vehicles, rr.Kills.Vehicles, RefKillVehicles);
			conf.RewardTypes.Harvest.Flesh = MergeSorted(conf.RewardTypes.Harvest.Flesh, rr.Harvests.Flesh, RefHarvestFlesh);
			conf.RewardTypes.Harvest.Ore = MergeSorted(conf.RewardTypes.Harvest.Ore, rr.Harvests.Ore, RefHarvestOre);
			conf.RewardTypes.Harvest.Tree = MergeSortedTree(conf.RewardTypes.Harvest.Tree, rr.Harvests.Tree, RefHarvestTree);
			conf.RewardTypes.Open = MergeSorted(conf.RewardTypes.Open, rr.Open, RefOpen);
			conf.RewardTypes.Pickup = MergeSortedPickup(conf.RewardTypes.Pickup, rr.Pickup, RefPickup);
			conf.Weapon_Multipliers = MergeSorted(conf.Weapon_Multipliers, rr.WeaponsList, RefMultipliers);

			// Keep working dicts in sync for any code still reading rr.*
			rr.Kills.Animals = conf.RewardTypes.Kill.Animals;
			rr.Kills.MountedWeapons = conf.RewardTypes.Kill.MountedWeapons;
			rr.Kills.NPCs = conf.RewardTypes.Kill.NPCs;
			rr.Kills.Players = conf.RewardTypes.Kill.Players;
			rr.Kills.Vehicles = conf.RewardTypes.Kill.Vehicles;
			rr.Harvests.Flesh = conf.RewardTypes.Harvest.Flesh;
			rr.Harvests.Ore = conf.RewardTypes.Harvest.Ore;
			rr.Harvests.Tree = conf.RewardTypes.Harvest.Tree;
			rr.Open = conf.RewardTypes.Open;
			rr.Pickup = conf.RewardTypes.Pickup;
			rr.WeaponsList = conf.Weapon_Multipliers;
		}

		static Dictionary<string, double> MergeSorted(Dictionary<string, double> fromConfig, Dictionary<string, double> scanned, List<string> allowedKeys)
		{
			var result = new Dictionary<string, double>();
			if (allowedKeys != null)
			{
				foreach (var key in allowedKeys.OrderBy(k => k))
				{
					double val = 0;
					if (fromConfig != null && fromConfig.TryGetValue(key, out var cfg))
						val = cfg;
					else if (scanned != null && scanned.TryGetValue(key, out var sc))
						val = sc;
					result[key] = val;
				}
			}
			// Keep config-only keys that were not in the scan (custom entries)
			if (fromConfig != null)
			{
				foreach (var kv in fromConfig.OrderBy(x => x.Key))
				{
					if (!result.ContainsKey(kv.Key))
						result[kv.Key] = kv.Value;
				}
			}
			return result;
		}

		Dictionary<string, double> MergeSortedTree(Dictionary<string, double> fromConfig, Dictionary<string, double> scanned, List<string> allowedKeys)
		{
			var merged = MergeSorted(fromConfig, scanned, allowedKeys);
			return merged.OrderBy(x => x.Key).OrderBy(x => OddWood.Contains(x.Key)).ToDictionary(val => val.Key, val => val.Value);
		}

		static Dictionary<string, double> MergeSortedPickup(Dictionary<string, double> fromConfig, Dictionary<string, double> scanned, List<string> allowedKeys)
		{
			var merged = MergeSorted(fromConfig, scanned, allowedKeys);
			return merged.OrderBy(x => x.Key).OrderByDescending(x => x.Key.Contains("entity")).ToDictionary(val => val.Key, val => val.Value);
		}

		void DictAdd(string name)
		{
			if (!storedData.FriendlyNames.ContainsKey(name))
				storedData.FriendlyNames.Add(name, name);
		}

		void PurgeOldPrefs()
		{
			int counter = 0;
			if (storedData?.PlayerPrefs == null)
				return;
			foreach (var entry in storedData.PlayerPrefs.ToDictionary(val => val.Key, val => val.Value))
			{
				if ((DateTime.Now - entry.Value.LastActive).TotalDays > conf.Settings.General.Delete_Player_Prefs_After_Days)
				{
					counter++;
					storedData.PlayerPrefs.Remove(entry.Key);
					storedData.PlayerStatistics?.Remove(entry.Key);
					storedData.PlayerNames?.Remove(entry.Key);
					storedData.WipeBaselinePlay?.Remove(entry.Key);
					storedData.WipeBaselineAFK?.Remove(entry.Key);
				}
			}
			if (counter > 0)
			{
				MarkDataDirty();
                PrintWarning($"Deleted {counter} player preference records, for {conf.Settings.General.Delete_Player_Prefs_After_Days} days of inactivity.");
			}
        }
		internal void Unload()
		{
			foreach (BasePlayer player in BasePlayer.activePlayerList)
				DestroyMenu(player, true, false, false);

			if (conf?.Settings.Rewards.ActivityReward_Seconds > 0)
				ServerMgr.Instance.CancelInvoke(this.CheckActivity);

			if (conf?.Settings.Multipliers.HappyHour > 1)
				ServerMgr.Instance.CancelInvoke(this.CheckHappyHour);

			// Clean up Discord reporting timer
			if (_discordReportTimer != null)
			{
				_discordReportTimer.Destroy();
				_discordReportTimer = null;
			}

			if (_dataDirty)
				SaveData();
		}
		// ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
		public override void HarmonyInit()
		{
			rr = this;
			LoadConfig();
			Init();
			Loaded();
			ResolvePluginReferences();
		}

		public override void HarmonyServerInitialized()
		{
			ResolvePluginReferences();
			OnServerInitialized();
		}

		public override void HarmonyUnload() => Unload();

		internal void ResolvePluginReferences()
		{
			Economics = plugins.Find("Economics");
			RaidableBases = plugins.Find("RaidableBases");
			Clans = plugins.Find("Clans");
			Friends = plugins.Find("Friends");
			GUIAnnouncements = plugins.Find("GUIAnnouncements");
			NoEscape = plugins.Find("NoEscape");
			ServerRewards = plugins.Find("ServerRewards");
			ZoneManager = plugins.Find("ZoneManager");
			PlaytimeTracker = plugins.Find("PlaytimeTracker");
		}

		protected override void LoadConfig()
		{
			// Host.Config already points at HarmonyConfig/RustRewards.json
			LoadConfigVariables();
		}
		#endregion

		void CheckDependencies()  
		{
			if (conf.Settings.RewardCurrency.UseScrap) 
			{
                currency = Currency.Scrap;
                PrintWarning("Using Scrap");
			}
			else if (conf.Settings.RewardCurrency.UseEconomics)
			{
                currency = Currency.Economics;
				PrintWarning("Using Economics");
			}
			else if (conf.Settings.RewardCurrency.UseServerRewards)
            {
                currency = Currency.ServerRewards;
				PrintWarning("Using Server Rewards");
			}

			if (conf.Settings.Allies.UseFriendsPlugin && !Friends)
				Puts("Friends plugin wasn't loaded. Option has been disabled.");

			if (conf.Settings.Allies.UseClansPlugin)
			{
				if (Clans)
					Puts("Using Clans Harmony mod for ally checks.");
				else
					Puts("Using vanilla game clans for ally checks.");
			}

			if (conf.Settings.Plugins.UseZoneManagerPlugin && !ZoneManager)
			{
				conf.Settings.Plugins.UseZoneManagerPlugin = false;
				Puts("Zone Manager plugin wasn't loaded. Option has been disabled.");
			}

			if (conf.Settings.Plugins.UseGUIAnnouncementsPlugin && !GUIAnnouncements)
				Puts("GUI Announcements plugin wasn't loaded. Option has been disabled.");

			if (conf.Settings.Plugins.UseNoEscape && !NoEscape)
				Puts("No Escape plugin wasn't loaded. Option has been disabled.");
		}

		Dictionary<ulong, int> RewardSeconds = new Dictionary<ulong, int>();
        Dictionary<ulong, Vector3> LastLoc = new Dictionary<ulong, Vector3>();
		Timer _discordReportTimer;

        void CheckActivity()
		{
			var interval = conf.Settings.Rewards.ActivityReward_Seconds;
			if (interval <= 0)
				return;

			foreach (var player in BasePlayer.activePlayerList)
			{
				if (player?.net?.connection == null)
					continue;

				if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, ActivityPermission))
					continue;

				var userId = player.userID.Get();
				var duration = (int)(UnityEngine.Time.realtimeSinceStartup - player.net.connection.connectionTime);

				int lastReward;
				if (!RewardSeconds.TryGetValue(userId, out lastReward))
				{
					RewardSeconds[userId] = duration;
					LastLoc[userId] = player.transform.position;
					continue;
				}

				if (duration - lastReward < 0)
					continue;

				int Rewards = (int)Mathf.Floor((duration - lastReward) / (float)interval);
				if (Rewards == 0)
					continue;

				RewardSeconds[userId] = lastReward + interval * Rewards;

				Vector3 lastPos;
				if (!LastLoc.TryGetValue(userId, out lastPos))
					lastPos = player.transform.position;

				if (conf.Settings.Rewards.Activity_Reward_For_AFK || lastPos != player.transform.position)
				{
					LastLoc[userId] = player.transform.position;
					GiveReward(player, RewardType.Activity, conf.Settings.Rewards.ActivityRewardAmount * Rewards);
				}
			}
		}

		bool first = true;
		bool HappyHourRef = false; 

		void CheckHappyHour()  
		{
			if (HappyHour() == true)
			{
				if (!HappyHourRef || first)
				{
					HappyHourRef = true;
					MessagePlayers("happyhourstart"); 
				}
			}
			else
			{
				if (HappyHourRef || first)
				{
					HappyHourRef = false;
					MessagePlayers("happyhourend");
				}
			}
			first = false;
		}

		string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

		bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);

		void RegisterPerm(string perm)
		{
			if (!permission.PermissionExists(perm))
				permission.RegisterPermission(perm, this);
		}

		string CleanIP(string ipaddress)
		{
			if (string.IsNullOrEmpty(ipaddress)) return " ";

			if (!ipaddress.Contains(":") || ipaddress.LastIndexOf(":") == 0) return ipaddress;
			return ipaddress.Substring(0, ipaddress.LastIndexOf(":"));
		}

		private void MessagePlayers(string key)
		{
			if (conf.Settings.Plugins.UseGUIAnnouncementsPlugin && GUIAnnouncements)
			{
				foreach (var player in BasePlayer.activePlayerList)
					GUIAnnouncements?.Call("CreateAnnouncement", String.Concat(Lang("Prefix", player.UserIDString), Lang(key, player.UserIDString)), conf.Settings.Announcements.GUI_Announcement_Banner_Colour, conf.Settings.Announcements.GUI_Announcement_Text_Colour, player);
			}
			else
				foreach (var player in BasePlayer.activePlayerList) 
					Player.Reply(player, string.Format(conf.Settings.Announcements.ChatMessageFormat, Lang("Prefix", player.UserIDString), Lang(key, player.UserIDString)), "", conf.Settings.General.ChatIcon);
		}

		private void MessagePlayer(BasePlayer player, string msg, string prefix)
		{
			if (player?.net?.connection == null || String.IsNullOrWhiteSpace(msg))
				return;

			if (!String.IsNullOrWhiteSpace(prefix))
				msg = string.Format(conf.Settings.Announcements.ChatMessageFormat, prefix, msg);

			if (conf.Settings.Plugins.UseGUIAnnouncementsPlugin && GUIAnnouncements)
				GUIAnnouncements?.Call("CreateAnnouncement", msg, conf.Settings.Announcements.GUI_Announcement_Banner_Colour, conf.Settings.Announcements.GUI_Announcement_Text_Colour, player);
			else
				Player.Reply(player, msg, "", conf.Settings.General.ChatIcon);
		}

		private void NotifyReward(BasePlayer player, string msg, string prefix, bool GUI)
		{
			if (player?.net?.connection == null || String.IsNullOrWhiteSpace(msg))
				return;

			if (!String.IsNullOrWhiteSpace(prefix))
				msg = string.Format(conf.Settings.Announcements.ChatMessageFormat, prefix, msg);

			if (GUI)
				GUIAnnouncements?.Call("CreateAnnouncement", msg, conf.Settings.Announcements.GUI_Announcement_Banner_Colour, conf.Settings.Announcements.GUI_Announcement_Text_Colour, player);
			else
				Player.Reply(player, msg, "", conf.Settings.General.ChatIcon);
		}

		private void TakeScrap(BasePlayer player, int itemAmount)
		{
			if (player.inventory.Take(null, ScrapId, itemAmount) > 0)
				player.SendConsoleCommand("note.inv", ScrapId, itemAmount * -1);
		}

		private object GiveScrap(BasePlayer player, int amount = 1)
		{

			Item item = ItemManager.Create(ItemManager.FindItemDefinition(-932201673));

			if (item == null)
				return false;

			item.amount = amount;

			if (player == null)
			{
				item.Remove();
				return false;
			}
			if (!player.inventory.GiveItem(item, player.inventory.containerMain))
			{
				item.Remove();
				return false;
			}
			return true;
		}

		void PayPlayer(BasePlayer baseplayer, double amount)
		{
			if (currency != Currency.Economics)
				amount = Math.Round(amount, 0);
			if (amount == 0.0d)
				return;

			if (currency == Currency.Scrap)
			{
				if (amount < 0.0d)
				{
					//Puts("GrimmRewards does not currently support taking scrap from players");
					//TakeScrap(baseplayer, (int)(amount));
					//// Create a scrap debt in data? Pay it off as you collect?
					return;
				}
				else
					GiveScrap(baseplayer, (int)(amount)); 
			}
			// Both plugins look for a positive number to take/withdraw
			else if (currency == Currency.ServerRewards)
			{
				if (amount < 0.0d)
					ServerRewards?.Call("TakePoints", baseplayer.UserIDString, -1 * (int)(amount));
				else
					ServerRewards?.Call("AddPoints", baseplayer.UserIDString, (int)(amount));
			}
			else
			{
				if (amount < 0.0d)
					Economics?.Call("Withdraw", baseplayer.UserIDString, -1 * amount);
				else
					Economics?.Call("Deposit", baseplayer.UserIDString, amount);
			} 
		}

		bool CheckPlayer(string playerId, double amount)
		{
			double balance = 0.0d;
			if (currency == Currency.ServerRewards)
				balance = (double)ServerRewards?.Call("CheckPoints", playerId);
			else if (currency == Currency.Economics)
				balance = (double)Economics?.Call("Balance", playerId);

			return !(amount.CompareTo(balance) < 0);
		}

		void GiveRustReward(BasePlayer player, int type, double amount, BaseEntity ent = null, string weapon = "", float distance = 0f, string name = null) => GiveReward(player, (RewardType)type, amount, ent, weapon, distance, name);
		void GiveReward(BasePlayer player, RewardType type, double amount, BaseEntity ent = null, string weapon = "", float distance = 0f, string name = null)
		{
            if (!loaded || conf == null || storedData?.PlayerPrefs == null || amount == 0 || player == null || player.IsNpc)
				return;
			// Soft-resolve currency plugins in case load order left refs null
			if (currency == Currency.Economics && !Economics)
				Economics = plugins?.Find("Economics");
			if (currency == Currency.ServerRewards && !ServerRewards)
				ServerRewards = plugins?.Find("ServerRewards");
            if (currency == Currency.None || (currency == Currency.ServerRewards && !ServerRewards) || (currency == Currency.Economics && !Economics))
				return;

            ulong uid = (ulong)player.userID;
            if (!storedData.PlayerPrefs.TryGetValue(uid, out PlayerPrefs prefs) || prefs == null)
			{
				prefs = new PlayerPrefs()
				{
					Type = storedData.PrefDefaults?.Default_Notification_Type ?? 0,
					Position = storedData.PrefDefaults?.Position ?? 3,
					Show_Activity = storedData.PrefDefaults?.Show_Activity ?? true,
					Show_Harvest = storedData.PrefDefaults?.Show_Harvest ?? true,
					Show_Kills = storedData.PrefDefaults?.Show_Kills ?? true,
					Show_Open = storedData.PrefDefaults?.Show_Open ?? true,
					Show_Pickup = storedData.PrefDefaults?.Show_Pickup ?? true,
					Show_Welcome = storedData.PrefDefaults?.Show_Welcome ?? true,
				};
				storedData.PlayerPrefs[uid] = prefs;
				MarkDataDirty();
			}

			// Track player statistics for Discord reporting
			if (conf.Settings.DiscordReporting.EnableDiscordReporting)
			{
				if (!storedData.PlayerStatistics.ContainsKey(player.userID))
					storedData.PlayerStatistics[player.userID] = new PlayerStats();
				
				storedData.PlayerStatistics[player.userID].AddReward(type, amount);
				MarkDataDirty();
			}

            if (Interface.CallHook("OnRustReward", player, type.ToString()) != null) 
				return;

            if (name == null)
				name = ent?.ShortPrefabName;

            double Multiplier = GetMultiplier(player, weapon, distance, ent);
			amount *= Multiplier;

            if (NoEscape && conf.Settings.Plugins.UseNoEscape)
			{
				var success = NoEscape?.Call("IsBlocked", player);
				if (success != null && success is bool && (bool)success)
				{
					MessagePlayer(player, Lang("NoEscapeBlocked", player.UserIDString), Lang("Prefix", player.UserIDString));
					return;
				}
			}

            string formatted_amount = amount.ToString();
			if (currency == Currency.Economics)
                amount = Math.Round(amount, 2); 
			else
			{
				amount = Math.Round(amount, 0);
				formatted_amount = string.Format("{0:#;-#;0}", amount);
			}

            var amt = Math.Abs(amount);

            if (amt > 0 && ((amt < 0.01d && currency == Currency.Economics) || (amt < 1.0d && currency == Currency.ServerRewards)))
                return;
            if (amt < 0 && ((amt > -0.01d && currency == Currency.Economics) || (amt > -1.0d && currency == Currency.ServerRewards)))
                return;

            if (ent?.net?.connection != null) 
			{
				var victim = ent as BasePlayer;
				if (victim != null && conf.Settings.General.TakeMoneyfromVictim)
				{
					try
					{
						PayPlayer(victim, -amount); 
						MessagePlayer(victim, Lang("VictimKilled", victim.UserIDString, victim.displayName), Lang("Prefix", victim.UserIDString));
						if (conf.Settings.General.LogToFile)
							LogToFile(Name, $"[{DateTime.Now}] " + victim.displayName + " ( " + victim.UserIDString + " / " + CleanIP(victim.net.connection.ipaddress) + " )" + " lost " + formatted_amount + " for " + type, this);
						if (conf.Settings.General.LogToConsole)
							Puts($"{victim.displayName} ( {victim.UserIDString} / {CleanIP(victim.net.connection.ipaddress)} ) lost {formatted_amount} for {type}");
					}
					catch
					{
						MessagePlayer(player, Lang("VictimNoMoney", player.UserIDString, victim.displayName), Lang("Prefix", player.UserIDString));
					}
				}
			}
			PayPlayer(player, amount);  

			if (!conf.Settings.General.Disable_All_Notifications)
			{
				if (prefs.Type != 3 && prefs.ShowReward(type))
				{
					if (prefs.Type == 2)
					{
						if (amount > 0)
							RRRUI(player, type, $"+{amount}");
						else
							RRRUI(player, type, $"-{amount}"); 
					}
					else //if (name != null) 
						NotifyReward(player, Lang(type.ToString() + (type == RewardType.Kill && amount < 0 ? "_negative" : ""), player.UserIDString, new object[] { (amount < 1 ? amount.ToString("0.00") : amount.ToString()), GetFriendly(name, ent), Math.Round(distance, 2) }), Lang("Prefix", player.UserIDString), prefs.Type == 1);
				}
			}
			if (conf.Settings.General.LogToFile)
				LogToFile(Name, $"[{DateTime.Now}] " + player.displayName + " ( " + player.userID + " / " + CleanIP(player.net.connection.ipaddress) + " )" + " got " + formatted_amount + " for " + type, this);
			if (conf.Settings.General.LogToConsole)
				Puts($"{player.displayName} ( {player.UserIDString} / {CleanIP(player.net.connection.ipaddress)} got {formatted_amount} for {type}");
		}

		Dictionary<RewardType, string> Colours = new Dictionary<RewardType, string>()
		{
			{RewardType.Kill, "1 0 0 1"},
			{RewardType.Harvest, "0 1 0 1"},
			{RewardType.Open, "0 0 1 10"},
			{RewardType.Pickup, "1 1 0 1"},
			{RewardType.Activity, "0 1 1 1"},
			{RewardType.Welcome, "1 1 1 1"}
		};

		void RRRUI(BasePlayer player, RewardType type, string message)
		{
			CuiHelper.DestroyUi(player, "RRRUI");
			timer.Once(1.4f, () =>
			{
				if (player != null)
					CuiHelper.DestroyUi(player, "RRRUI");
			});

			var prefs = Positions[storedData.PlayerPrefs[player.userID].Position];

			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { FadeIn = 0.7f, Color = $"0.1 0.1 0.1 0" }, RectTransform = { AnchorMin = prefs[0], AnchorMax = prefs[1] }, CursorEnabled = false }, "Overlay", "RRRUI");

			if (_uiBackgroundPngId != 0)
			{
				elements.Add(new CuiElement { Parent = mainName, Components = { new CuiRawImageComponent { FadeIn = 0.7f, Png = _uiBackgroundPngId.ToString(), Sprite = Sprite }, new CuiRectTransformComponent { AnchorMin = "0.15 0.15", AnchorMax = "0.85 0.85" }, }, });
			}

			elements.Add(new CuiLabel { Text = { FadeIn = 0.7f, Text = message, Color = Colours[type], FontSize = message.Length > 3 ? 28 : message.Length > 2 ? 34 : 38, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, }, mainName);
			CuiHelper.AddUi(player, elements);
		}

		// Turns "GrowableEntity" into "Growable Entity" for display when no friendly name is configured
		static string HumanizeTypeName(string name)
		{
			if (string.IsNullOrEmpty(name)) return name;
			var sb = new System.Text.StringBuilder(name.Length + 4);
			for (int i = 0; i < name.Length; i++)
			{
				if (i > 0 && char.IsUpper(name[i]))
					sb.Append(' ');
				sb.Append(name[i]);
			}
			return sb.ToString();
		}

		string GetFriendly(string name, BaseEntity ent) 
		{
			if (ent == null && name == null)
				return null;

			BasePlayer player = ent as BasePlayer;
			if (player != null)
			{
				if (!string.IsNullOrEmpty(player.displayName))
					return player.displayName;
				if (IsBotReSpawn(player) != null || player.Categorize() == "Zombie")
					return player.displayName;
				name = GetNPCType(player); 
			}
			if (name == null)
				return "No Record";

			if (!storedData.FriendlyNames.ContainsKey(name))
			{
				// Known entity type names that may appear in config but aren't from ShortPrefabName
				string friendly = null;
				if (name == "GrowableEntity")
					friendly = "Growable";
				if (friendly != null)
				{
					storedData.FriendlyNames[name] = friendly;
					return friendly;
				}
				// Auto-add a human-readable form so we don't spam the log for this type again
				friendly = HumanizeTypeName(name);
				storedData.FriendlyNames[name] = friendly;
				return friendly;
			}
			return storedData.FriendlyNames[name]; 
		}

		public object IsBotReSpawn(BasePlayer player)
        {
			foreach (var comp in player.GetComponents<Component>())
				if (comp.ToString().Contains("BotData"))
					return true;
			return null;
		}

		// Resolve a player's display name: prefer online displayName, fallback to cached last-known name, else "Unknown Player"
		private string ResolvePlayerName(ulong userId, BasePlayer player = null)
		{
			if (player != null && !string.IsNullOrEmpty(player.displayName))
				return player.displayName;
			string cached;
			if (storedData != null && storedData.PlayerNames != null && storedData.PlayerNames.TryGetValue(userId, out cached) && !string.IsNullOrEmpty(cached))
				return cached;
			// Fallback to PlaytimeTracker data if available
			var ptt = plugins?.Find("PlaytimeTracker");
			var nameObj = ptt?.Call("GetDisplayName", userId.ToString());
			if (nameObj is string s && !string.IsNullOrEmpty(s))
			{
				storedData.PlayerNames[userId] = s;
				return s;
			}
			return "Unknown Player";
		}

		private void ImportPlaytimeTrackerNames()
		{
			try
			{
				var dataFile = Interface.Oxide.DataFileSystem.GetFile("PlaytimeTracker/user_data");
				if (dataFile == null) return;
				var json = dataFile.ReadObject<Newtonsoft.Json.Linq.JObject>();
				if (json == null) return;
				var users = json["_userData"] as Newtonsoft.Json.Linq.JObject;
				if (users == null) return;
				foreach (var prop in users.Properties())
				{
					if (ulong.TryParse(prop.Name, out var id))
					{
						var displayName = prop.Value["displayName"]?.ToString();
						if (!string.IsNullOrEmpty(displayName) && (!storedData.PlayerNames.TryGetValue(id, out var existing) || existing != displayName))
						{
							storedData.PlayerNames[id] = displayName;
							MarkDataDirty();
						}
					}
				}
			}
			catch {}
		}

		double GetMultiplier(BasePlayer player, string weapon, float distance, BaseEntity ent = null)
		{
			double TimeMulti = IsNight() ? conf.Settings.Multipliers.Nighttime : conf.Settings.Multipliers.Daytime;
			double RBMulti = ent != null && !ent.IsDestroyed && ent.transform != null && EventTerritory(ent) ? conf.Settings.Multipliers.RaidableBases : 1;
            double DistanceMulti = conf.Settings.Multipliers.UseDynamicDistance ? 1.0f + (distance * conf.Settings.Multipliers.DynamicDistance) : Get_Distance_Multiplier(distance);
			double WeaponMulti = weapon != null && conf.Weapon_Multipliers.ContainsKey(weapon) ? conf.Weapon_Multipliers[weapon] : 1;
			double DayMulti = conf.WeekDay_Multipliers.ElementAt(CurrentDay).Value;
            double HappyMulti = HappyHourRef ? conf.Settings.Multipliers.HappyHour : 1;

            double ZoneMulti = 1;
			if (ZoneManager)
			{
				List<string> playerzones = ((string[])ZoneManager?.Call("GetPlayerZoneIDs", player)).ToList();
				foreach (var zone in playerzones)
					if (zone != null && storedData.ZoneMultipliers.ContainsKey(zone))
						if (storedData.ZoneMultipliers[zone] > ZoneMulti)
							ZoneMulti = storedData.ZoneMultipliers[zone];
            }

            double PermMulti = 1;
			foreach (var entry in conf.Permission_Multipliers)
				if (HasPerm(player.UserIDString, Title + "." + entry.Key))
					if (entry.Value > PermMulti)
						PermMulti = entry.Value;

            double GroupMulti = 1;
			foreach (var entry in conf.Group_Multipliers)
				if (permission.UserHasGroup(player.UserIDString, entry.Key))
					if (entry.Value > GroupMulti)
						GroupMulti = entry.Value;

			return ProcessMultis(new List<double>() { TimeMulti, PermMulti, GroupMulti, DistanceMulti, WeaponMulti, DayMulti, ZoneMulti, HappyMulti, RBMulti }); 
		}

		double ProcessMultis(List<double> multis) 
		{
			double multiplier = 1;

			if (conf.Settings.General.Use_Highest_Multiplier_Only)
				return multis.Max<double>();

			foreach (var multi in multis)
			{
				if (conf.Settings.General.Add_Multipliers)
				{
					if (multi > 1)
						multiplier += multi - 1;
				}
				else
					multiplier *= multi;
			}
			return multiplier;
        }

		private ulong GetMajorityAttacker(ulong id)
		{
			if (VehicleAttackers.ContainsKey(id))
				return VehicleAttackers[id].OrderByDescending(pair => pair.Value).First().Key;
			return 0U;
		}

		#region OxideHooks
		internal void OnPlayerConnected(BasePlayer player)
		{
			if (conf == null || player == null)
				return;

			var userId = player.userID.Get();
			int duration = 0;
			if (player.net?.connection != null)
				duration = (int)(UnityEngine.Time.realtimeSinceStartup - player.net.connection.connectionTime);

			if (!RewardSeconds.ContainsKey(userId))
				RewardSeconds[userId] = duration;

			if (!LastLoc.ContainsKey(userId))
				LastLoc[userId] = player.transform.position;

			if (!LastKills.ContainsKey(userId))
				LastKills[userId] = new DateTime();

			// Initialize player statistics for Discord reporting
			if (conf.Settings.DiscordReporting.EnableDiscordReporting)
			{
				if (!storedData.PlayerStatistics.ContainsKey(player.userID))
				{
					storedData.PlayerStatistics[player.userID] = new PlayerStats();
					MarkDataDirty();
				}
			}

			// Cache last known display name for offline resolution
			if (!string.IsNullOrEmpty(player.displayName))
				storedData.PlayerNames[player.userID] = player.displayName;

			if (!storedData.PlayerPrefs.ContainsKey((ulong)player.userID))
			{
				storedData.PlayerPrefs[(ulong)player.userID] = new PlayerPrefs() 
				{
					Type = storedData.PrefDefaults.Default_Notification_Type,
					Position = storedData.PrefDefaults.Position,
					Show_Activity = storedData.PrefDefaults.Show_Activity,
					Show_Harvest = storedData.PrefDefaults.Show_Harvest,
					Show_Kills = storedData.PrefDefaults.Show_Kills,
					Show_Open = storedData.PrefDefaults.Show_Open,
					Show_Pickup = storedData.PrefDefaults.Show_Pickup,
					Show_Welcome = storedData.PrefDefaults.Show_Welcome,
				};
			}

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, WelcomePermission))
			{
				MarkDataDirty();
				return;
			}

            storedData.PlayerPrefs[(ulong)player.userID].LastActive = DateTime.Now;

            if (conf.Settings.Rewards.WelcomeMoneyAmount > 0 && !storedData.PlayerPrefs[(ulong)player.userID].Activity_Given)
			{
				storedData.PlayerPrefs[(ulong)player.userID].Activity_Given = true;
				MarkDataDirty();
				GiveReward(player, RewardType.Welcome, conf.Settings.Rewards.WelcomeMoneyAmount);
			}
			else
				MarkDataDirty();
		}

		List<ulong> HarvestCoolDown = new List<ulong>();
		List<ulong> BonusCoolDown = new List<ulong>();
		bool CoolDownPlayer(BasePlayer player, List<ulong> list)
        {
			if (list.Contains(player.userID))
				return true;
			list.Add(player.userID);
			timer.Once(0.1f, () => list.Remove(player.userID));
			return false;
        }

		internal void OnDispenserGather(ResourceDispenser d, BaseEntity entity, Item item)
		{
			if (!loaded)
				return;
			BasePlayer player = entity?.ToPlayer(); 
			var corpse = d.GetComponent<PlayerCorpse>();
            
            if (corpse != null)
			{
                var id = corpse.playerSteamID;
				NextTick(() =>
				{
					if (d == null && CorpseTypes.ContainsKey(id))
						if (CorpseTypes[id] != null && conf.RewardTypes.Harvest.Flesh.ContainsKey(CorpseTypes[id]))
							GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[CorpseTypes[id]], null, "", 0, CorpseTypes[id]); 
					//GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[CorpseTypes[id]], name:CorpseTypes[id]);
				});
				return;
			}

			var ent = d.baseEntity;
			bool Hazard = ent is WildlifeHazard;
			if (ent != null)
			{
				if (conf.Settings.General.Use_Harvesting_Cooldown && CoolDownPlayer(player, HarvestCoolDown)) 
					return;
                var name = ent.ShortPrefabName;

                NextTick(() =>
				{
                    if (ent.IsDestroyed && conf.RewardTypes.Harvest.Flesh.ContainsKey(name))
					{
                        GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[name], ent);
						return;
					}
                    if (ent.Health() <= 0 && d.gatherType == ResourceDispenser.GatherType.Tree)
					{
                        string tree = TreeTypes(name); 
						if (tree != string.Empty)
							GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Tree[tree], ent, "", 0, tree);
						return;
					}
				});
			}
		}

		List<ulong> Bonuses = new List<ulong>();
        List<string> ores = new List<string>() { "metal", "stone", "sulfur" };

        internal         void OnDispenserBonus(ResourceDispenser d, BasePlayer player, Item i)
		{
			if (!loaded || !conf.Settings.Rewards.HarvestReward)
				return;
			if (d == null || player == null)
				return;

            if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, HarvestPermission))
				return;

			var ent = d.GetComponent<BaseEntity>();
			if (ent?.net?.ID == null || Bonuses.Contains(ent.net.ID.Value)) //metal/hqm 
				return;

			Bonuses.Add(ent.net.ID.Value);

			if (conf.Settings.General.Use_Harvesting_Cooldown && CoolDownPlayer(player, BonusCoolDown))
				return;

			foreach (var entry in ores) 
				if (ent.ShortPrefabName.Contains(entry))
					GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Ore[entry], ent, "", 0, entry);
		}

		internal void OnGrowableGathered(GrowableEntity growable, Item item, BasePlayer player)
		{
			if (!loaded || player == null || growable == null || !conf.Settings.Rewards.PickupReward)
				return;
			if (growable.planter == null && conf.Settings.General.Only_Reward_Growables_From_Planters)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, PickupPermission))
				return;

			if (conf.RewardTypes.Pickup.ContainsKey(growable.ShortPrefabName))
				GiveReward(player, RewardType.Pickup, conf.RewardTypes.Pickup[growable.ShortPrefabName], growable);
		}

		internal void OnCollectiblePickup(CollectibleEntity entity, BasePlayer player)  
		{
			if (!loaded || player == null || entity == null || !conf.Settings.Rewards.PickupReward)
				return;
			string shortName = entity.ShortPrefabName;
			if (string.IsNullOrEmpty(shortName))
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, PickupPermission))
				return;

			ulong netId = 0;
			try { if (entity.net != null) netId = entity.net.ID.Value; } catch { }
			if (netId != 0)
			{
				if (UsedIDs.Contains(netId))
					return;
				UsedIDs.Add(netId);
			}

			if (conf.RewardTypes.Pickup.ContainsKey(shortName))
				GiveReward(player, RewardType.Pickup, conf.RewardTypes.Pickup[shortName], entity, "", 0, shortName);
		}

		List<ulong> UsedIDs = new List<ulong>();

		internal void OnItemAction(Item item, string action, BasePlayer player)
		{
			if (!loaded || player == null || item?.info?.name == null || !conf.Settings.Rewards.OpenReward)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, OpenPermission))
				return;

			if (conf.Settings.SkinnedItemBlackList.Contains(item.skin))
				return;

			if (conf.RewardTypes.Harvest.Flesh.ContainsKey(item.info.name))
				if (action == "Gut")
					GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[item.info.name], null, "", 0, item.info.name);
					//GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[item.info.name], name: item.info.name);

			if (conf.RewardTypes.Open.ContainsKey(item.info.name))
				if (action == "unwrap")
					GiveReward(player, RewardType.Open, conf.RewardTypes.Open[item.info.name], null, "", 0, item.info.name);
					//GiveReward(player, RewardType.Open, conf.RewardTypes.Open[item.info.name], name: item.info.name);
		}

		internal void OnLootEntityEnd(BasePlayer player, BaseCombatEntity container)
		{
			if (!loaded || player == null || container?.PrefabName == null || container?.ShortPrefabName == null || conf == null || !conf.Settings.Rewards.OpenReward)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, OpenPermission))
				return;

			if (conf.Settings.SkinnedItemBlackList.Contains(container.skinID))
				return;

			string name = container.PrefabName.Contains("underwater_labs") ? "underwater_labs_" + container.ShortPrefabName : container.ShortPrefabName;

			NextTick(() =>
			{
				if (container == null && player != null)
					if (conf.RewardTypes.Open.ContainsKey(name))
						GiveReward(player, RewardType.Open, conf.RewardTypes.Open[name], container, "", 0, name);
			});
		}

		internal void OnEntityDeath(BaseEntity entity, HitInfo info)
		{
			if (entity?.net?.ID == null || !loaded)
				return;

			var ID = entity.net.ID.Value;
			BasePlayer attacker = null;
			if (entity is PatrolHelicopter || entity is BaseHelicopter || entity is BaseVehicle || entity is BradleyAPC)
			{
				attacker = BasePlayer.FindByID(GetMajorityAttacker(entity.net.ID.Value));
				NextTick(() =>
				{
					if (VehicleAttackers.ContainsKey(ID))
						VehicleAttackers.Remove(ID); 
				});
			}

			if (attacker == null)
			{
				if (!(entity is GunTrap) && !(entity is SamSite) && !(entity is AutoTurret) && !(entity is FlameTurret))
					return;
				attacker = info?.InitiatorPlayer;
			}

			if (attacker == null || attacker.IsNpc)
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
				return;

			if (attacker.userID == entity.OwnerID || FriendCheck(attacker.userID, entity.OwnerID))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, entity.transform.position);

			if (conf.RewardTypes.Kill.MountedWeapons.ContainsKey(entity.ShortPrefabName))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.MountedWeapons[entity.ShortPrefabName], entity, weapon ?? "", distance);

			if (conf.RewardTypes.Kill.Vehicles.ContainsKey(entity.ShortPrefabName))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Vehicles[entity.ShortPrefabName], entity, weapon ?? "", distance);
		}

		internal void OnEntityDeath(BaseVehicle vehicle, HitInfo info)
		{
			if (!loaded || vehicle?.net?.ID == null)
				return;
	
			var ID = vehicle.net.ID.Value;
			var attacker = BasePlayer.FindByID(GetMajorityAttacker(vehicle.net.ID.Value));

			if (attacker?.net?.connection == null || !conf.Settings.Rewards.KillReward)
				return;

			if (attacker.userID == vehicle.OwnerID || FriendCheck(attacker.userID, vehicle.OwnerID))
				return;

			NextTick(() => 
			{
				if (VehicleAttackers.ContainsKey(ID))
					VehicleAttackers.Remove(ID);
			});

			string name = vehicle.GetType().ToString();
			if (vehicle is CH47Helicopter || vehicle is CH47HelicopterAIController)
				name = vehicle.ShortPrefabName;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission)) 
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, vehicle.transform.position);

			if (conf.RewardTypes.Kill.Vehicles.ContainsKey(name))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Vehicles[name], vehicle, weapon ?? "", distance, name);
		}

        internal void OnEntityDeath(BaseNPC2 animal, HitInfo info) => AnimalKill(animal, info);
        internal void OnEntityDeath(BaseNpc animal, HitInfo info) => AnimalKill(animal, info);
        internal void OnEntityDeath(SimpleShark shark, HitInfo info) => AnimalKill(shark, info);
        internal void OnEntityDeath(SnakeHazard snake, HitInfo info) => AnimalKill(snake, info);

        internal void AnimalKill(BaseCombatEntity animal, HitInfo info)
		{
            if (!loaded || animal == null)
                return;

            if (animal.HasFlag(BaseEntity.Flags.Reserved8)) //wildlife hazard corpse
                return;

            var attacker = info?.InitiatorPlayer;
            if (attacker?.net?.connection == null || attacker.IsNpc || !conf.Settings.Rewards.KillReward)
                return;

            if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
                return;

            var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
            var distance = Vector3.Distance(attacker.transform.position, animal.transform.position);

            if (conf.RewardTypes.Kill.Animals.ContainsKey(animal.ShortPrefabName))
                GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Animals[animal.ShortPrefabName], animal, weapon ?? "", distance);
        }

		internal void OnEntityTakeDamage(BaseEntity entity, HitInfo info)
		{
			if (!loaded || entity == null)
				return;
			var player = info?.InitiatorPlayer; 
			if (player == null || player.IsNpc)
				return;

			var ent = entity is BaseVehicleModule ? entity.GetComponentInParent<BaseVehicle>() : entity;
			if (ent?.net?.ID == null)
				return;

			if (!(ent is PatrolHelicopter) && !(ent is BaseHelicopter) && !(ent is BaseVehicle) && !(ent is BradleyAPC))
				return;
			if (player.userID == ent.OwnerID || FriendCheck(player.userID, ent.OwnerID))
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, KillPermission))
				return;

			float amount = info?.damageTypes?.Total() ?? 0;

			if (!VehicleAttackers.ContainsKey(ent.net.ID.Value))
				VehicleAttackers.Add(ent.net.ID.Value, new Dictionary<ulong, float>());
			if (!VehicleAttackers[ent.net.ID.Value].ContainsKey(player.userID))
				VehicleAttackers[ent.net.ID.Value].Add(player.userID, amount);
			else
				VehicleAttackers[ent.net.ID.Value][player.userID]+= amount;
		}

		internal void OnEntityDeath(LootContainer barrel, HitInfo info)
		{
			if (!loaded || barrel == null)
				return;
			var attacker = info?.InitiatorPlayer;
			if (attacker?.net?.connection == null || attacker.IsNpc || !conf.Settings.Rewards.OpenReward)
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, OpenPermission))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, barrel.transform.position);

			if (conf.RewardTypes.Open.ContainsKey(barrel.ShortPrefabName))
				GiveReward(attacker, RewardType.Open, conf.RewardTypes.Open[barrel.ShortPrefabName], barrel, weapon ?? "", distance);
		}

		Dictionary<ulong, DateTime> LastKills = new Dictionary<ulong, DateTime>();
		Dictionary<ulong, string> CorpseTypes = new Dictionary<ulong, string>();

		//void OnEntityKill(BasePlayer player) => OnPlayerDeath(player, null);
		internal void OnPlayerDeath(BasePlayer player, HitInfo info)
		{
			if (player.userID.IsSteamId() && !player.IsConnected)
				return;
			if (!loaded || info == null || player == null || (player.userID.IsSteamId() && !player.IsConnected))
				return;

			if (player.Health() > 0) 
				return;

			var attacker = info?.InitiatorPlayer;
			if (attacker == null || !conf.Settings.Rewards.KillReward)
				return;

			if (!IsNPC(attacker) && conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
				return; 

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, player.transform.position);

			if (player.userID.IsSteamId())
			{
				if (player == attacker)
				{
					GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Players["Suicide"], player, weapon ?? "", distance, "Suicide"); 
					return;
				}
				if (!IsNPC(attacker))
				{
					if (FriendCheck(attacker.userID, player.userID))
						return;
					if ((DateTime.Now - LastKills[attacker.userID]).TotalSeconds < conf.Settings.General.Player_Kill_Reward_CoolDown_Seconds)
						return;
					LastKills[attacker.userID] = DateTime.Now;
				}

				CorpseTypes[player.userID] = "player_corpse";

				if (player.IsSleeping()) //// Check if this is too late
					GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Players["Sleepers"], player, weapon ?? "", distance, "Sleepers");
				else
				{
					if (!IsNPC(attacker))
						GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Players["Players"], player, weapon ?? "", distance, "Players");
					GiveReward(player, RewardType.Kill, conf.RewardTypes.Kill.Players["Death"], player, weapon ?? "", distance, "Death");  
				}
				return;
			}

			CorpseTypes[player.userID] = GetNPCType(player);
			if (CorpseTypes[player.userID] == null)
				return;

			if (conf.RewardTypes.Kill.NPCs.ContainsKey(CorpseTypes[player.userID]))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.NPCs[CorpseTypes[player.userID]], player, weapon ?? "", distance);
			else
				CorpseTypes.Remove(player.userID);
		}

		bool IsNPC(BasePlayer player) => player.Categorize() == "Zombie" || player?.net?.connection == null || player.IsNpc;

		public string GetNPCType(BasePlayer player)
		{
			foreach (var comp in player.GetComponents<Component>())
			{
				if (comp.ToString().Contains("BotData"))
					return "BotReSpawn";
			}

			if (player.Categorize() == "Zombie")
				return "ZombieHorde";



            foreach (var entry in names)
                if (player.ShortPrefabName == entry.Key)
                    return entry.Value;

            foreach (var entry in names) if (player.ShortPrefabName.Contains(entry.Key))
                if (player.ShortPrefabName.Contains(entry.Key))
                    return entry.Value;

			if (player is NPCPlayer)
			{
				var instance = player?.GetComponent<SpawnPointInstance>()?.parentSpawnPoint;
				if (instance != null)
				{
					var name = instance?.GetComponentInParent<PrefabParameters>()?.ToString();
					if (name != null)
					{
						foreach (var n in names)
							if (name.Contains(n.Key))
								return n.Value;
					}
				}
			}
			return null;
		}
		#endregion
		 
		Dictionary<string, string> names = new Dictionary<string, string>() //Friendlier names for config file clarity.
		{
			{"oilrig", "OilRig"},
			{"excavator", "Excavator"},
			{"peacekeeper", "CompoundScientist"},
			{"bandit_guard", "BanditTown"},
			{"_ch47_gunner", "MountedScientist"},
			{"junkpile", "JunkPileScientist"},
			{"scarecrow_dungeon", "DungeonScarecrow" },
			{"scarecrow", "ScareCrow"},
			{"military_tunnel", "MilitaryTunnelScientist" },
			{"scientist_full", "MilitaryTunnelScientist"},
			{"scientist_turret", "CargoShip"},
			{"scientistnpc_cargo", "CargoShip"},
			{"scientist_astar", "CargoShip"},
            {"scientistnpc_bradley", "APCScientist" },
            {"scientistnpc_bradley_heavy", "APCScientistHeavy" },
            {"scientistnpc_heavy", "HeavyScientist"},
            {"scientistnpc_outbreak", "JungleScientist"},
            {"tunneldweller", "TunnelDweller"},
			{"underwaterdweller" , "UnderwaterDweller"},
			{"trainyard" , "Trainyard"},
			{"airfield" , "Airfield"},
			{"scientistnpc_roamtethered", "DesertScientist" },
			{"arctic_research_base", "ArcticResearchBase" },
			{"nuclear_missile_silo", "NuclearMissileSilo" },
			{"launch_site", "LaunchSite" },
            {"gingerbread", "Gingerbread" },
		};

		#region Allies
		bool FriendCheck(ulong player, ulong victim)
		{
			if (!player.IsSteamId() || !victim.IsSteamId())
				return false;
			if (conf.Settings.Allies.UseClansPlugin && IsClanmate(player, victim)) 
				return true;
			if (Friends && conf.Settings.Allies.UseFriendsPlugin && IsFriend(player, victim))
				return true;
			if (conf.Settings.Allies.UseRustTeams && IsTeamMate(player, victim))
				return true;

			return false;
		}

		bool IsClanmate(ulong playerId, ulong friendId)
		{
			if (playerId == 0UL || friendId == 0UL)
				return false;
			if (playerId == friendId)
				return true;

			if (IsVanillaClanmate(playerId, friendId))
				return true;

			if (Clans == null)
				return false;

			object playerTag = Clans.Call("GetClanOf", playerId);
			object friendTag = Clans.Call("GetClanOf", friendId);
			if (playerTag is string && friendTag is string)
				if (playerTag == friendTag) return true;
			return false;
		}

		static bool IsVanillaClanmate(ulong playerId, ulong friendId)
		{
			try
			{
				if (!ConVar.Clan.enabled)
					return false;
			}
			catch { return false; }

			BasePlayer a = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
			BasePlayer b = BasePlayer.FindByID(friendId) ?? BasePlayer.FindSleeping(friendId);
			if (a == null || b == null)
				return false;
			return a.clanId != 0L && a.clanId == b.clanId;
		}

		bool IsFriend(ulong playerID, ulong friendID) => (bool)Friends?.Call("IsFriend", (ulong)playerID, (ulong)friendID);

		bool IsTeamMate(ulong player, ulong victim)
		{
			var team1 = RelationshipManager.ServerInstance.FindPlayersTeam(player);
			var team2 = RelationshipManager.ServerInstance.FindPlayersTeam(victim); 
			return team1 != null && team2 != null && team1 == team2;
		}
		#endregion

		#region Data
		StoredData storedData;
	class StoredData
	{
		public Dictionary<string, double> ZoneMultipliers = new Dictionary<string, double>();
		public Dictionary<string, string> FriendlyNames = new Dictionary<string, string>();
		public Dictionary<ulong, PlayerPrefs> PlayerPrefs = new Dictionary<ulong, PlayerPrefs>();
		public Dictionary<ulong, PlayerStats> PlayerStatistics = new Dictionary<ulong, PlayerStats>();
		public Dictionary<ulong, string> PlayerNames = new Dictionary<ulong, string>();
		public PlayerPrefDefaults PrefDefaults = new PlayerPrefDefaults();
			// Baseline totals from PlaytimeTracker at wipe start for per-wipe deltas
			public Dictionary<ulong, double> WipeBaselinePlay = new Dictionary<ulong, double>();
			public Dictionary<ulong, double> WipeBaselineAFK = new Dictionary<ulong, double>();
	}

		class PlayerStats
		{
			public Dictionary<RewardType, double> CategoryTotals = new Dictionary<RewardType, double>();
			public DateTime LastReset = DateTime.Now;
			public DateTime WipeStartTime = DateTime.Now;

			public PlayerStats()
			{
				foreach (RewardType type in Enum.GetValues(typeof(RewardType)))
				{
					CategoryTotals[type] = 0.0;
				}
			}

			public void AddReward(RewardType type, double amount)
			{
				if (CategoryTotals.ContainsKey(type))
					CategoryTotals[type] += amount;
				else
					CategoryTotals[type] = amount;
			}

			public void ResetStats()
			{
				foreach (RewardType type in Enum.GetValues(typeof(RewardType)))
				{
					CategoryTotals[type] = 0.0;
				}
				LastReset = DateTime.Now;
			}

			public void ResetWipeStats()
			{
				foreach (RewardType type in Enum.GetValues(typeof(RewardType)))
				{
					CategoryTotals[type] = 0.0;
				}
				WipeStartTime = DateTime.Now;
				LastReset = DateTime.Now;
			}
		}

		class PlayerPrefDefaults
		{
			public int Default_Notification_Type = 0;
			public int Position = 3; 
			public bool Show_Kills = true;
			public bool Show_Harvest = true;
			public bool Show_Open = true;
			public bool Show_Pickup = true;
			public bool Show_Activity = true;
			public bool Show_Welcome = true;
		}

		class PlayerPrefs
		{
			public bool ShowReward(RewardType type)
			{
				switch (type)
				{ 
					case RewardType.Kill: return Show_Kills;
					case RewardType.Harvest: return Show_Harvest;
					case RewardType.Open: return Show_Open;
					case RewardType.Pickup: return Show_Pickup;
					case RewardType.Activity: return Show_Activity;
					case RewardType.Welcome: return Show_Welcome;
				}
				return false;
			}
			public int Type;
			public bool Show_Kills;
			public bool Show_Harvest;
			public bool Show_Open;
			public bool Show_Pickup;
			public bool Show_Activity;
			public bool Show_Welcome;
			public int Position = 3;
			public bool Activity_Given = false;
			public DateTime LastActive = DateTime.Now;
		} 

		public List<string[]> Positions = new List<string[]>() { new string[] { "0.05 0.8", "0.15 0.9" }, new string[] { "0.85 0.8", "0.95 0.9" }, new string[] { "0.45 0.8", "0.55 0.9" }, new string[] { "0.45 0.45", "0.55 0.55" } };
		public List<string> Indicies = new List<string>() { "Top_Left", "Top_Right", "Top_Middle", "Middle" };
		public List<string> Notification = new List<string>() { "Chat", "Banner", "Icon", "Off" };

		internal void Loaded()
		{ 
			storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("RustRewards/RustRewards"); 
		}

		void MarkDataDirty() => _dataDirty = true;

		void SaveData()
		{
			Interface.Oxide.DataFileSystem.WriteObject("RustRewards/RustRewards", storedData);
			_dataDirty = false;
		} 

		#region PlaytimeTracker Integration
		private object GetPlayerPlayTime(ulong playerId)
		{
			var pt = plugins?.Find("PlaytimeTracker");
			if (pt == null) return null;
			return pt.Call("GetPlayTime", playerId.ToString());
		}

		private object GetPlayerAFKTime(ulong playerId)
		{
			var pt = plugins?.Find("PlaytimeTracker");
			if (pt == null) return null;
			return pt.Call("GetAFKTime", playerId.ToString());
		}

		private object GetPlayerReferrals(ulong playerId)
		{
			try
			{
				return Interface.Call("GetReferrals", playerId.ToString());
			}
			catch
			{
				return null;
			}
		}

		private string FormatTime(double timeInSeconds)
		{
			TimeSpan timeSpan = TimeSpan.FromSeconds(timeInSeconds);
			int days = timeSpan.Days;
			int hours = timeSpan.Hours + (days * 24);
			return string.Format("{0:00}h:{1:00}m:{2:00}s", hours, timeSpan.Minutes, timeSpan.Seconds);
		}

		private bool IsPlaytimeTrackerAvailable()
		{
			return plugins?.Find("PlaytimeTracker") != null;
		}
		#endregion

		#region UI Image Loading
		private uint _uiBackgroundPngId;
		private void LoadLocalUiBackground()
		{
			_uiBackgroundPngId = 0;
			try
			{
				if (string.IsNullOrEmpty(conf?.Settings?.UI?.BackgroundImage))
					return;
				// Expecting a local file path (e.g., "oxide/data/RustRewards/ui/background.png")
				var path = conf.Settings.UI.BackgroundImage;
				// Resolve under data directory when given a relative path
				if (!System.IO.Path.IsPathRooted(path))
				{
					var dataDir = Interface.Oxide?.DataDirectory ?? "oxide/data";
					path = System.IO.Path.Combine(dataDir, path.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString()));
				}
				if (!System.IO.File.Exists(path))
				{
					PrintWarning($"UI background not found at {path}");
					return;
				}
				var bytes = System.IO.File.ReadAllBytes(path);
				if (bytes == null || bytes.Length == 0)
					return;
				var entity = CommunityEntity.ServerInstance ?? BaseNetworkable.serverEntities.OfType<CommunityEntity>().FirstOrDefault();
				if (entity == null)
					return;
				_uiBackgroundPngId = FileStorage.server.Store(bytes, FileStorage.Type.png, entity.net.ID);
			}
			catch (System.Exception ex)
			{
				PrintWarning($"Failed to load local UI background: {ex.Message}");
			}
		}
		#endregion

		#region Discord Reporting
		void SendDiscordReport()
		{
			if (!conf.Settings.DiscordReporting.EnableDiscordReporting || string.IsNullOrEmpty(conf.Settings.DiscordReporting.DiscordWebhook))
				return;

			try
			{
				var report = FormatDiscordReport();
				if (string.IsNullOrEmpty(report))
				{
					PrintWarning("No player statistics to report to Discord");
					return;
				}

				var parts = SendDiscordTextChunks(conf.Settings.DiscordReporting.DiscordWebhook, report);
				if (parts > 0)
					PrintWarning($"Discord player statistics report queued ({parts} message(s))");
			}
			catch (Exception ex)
			{
				PrintError($"Error sending Discord report: {ex.Message}");
			}
		}

		string FormatDiscordReport()
		{
			var report = new System.Text.StringBuilder();
			var cutoffTime = DateTime.Now.AddHours(-conf.Settings.DiscordReporting.MinActivityHours);
			bool hasAnyData = false;
			bool usePlaytimeTracker = IsPlaytimeTrackerAvailable();

			report.AppendLine("**GrimmRewards - Player Statistics Report**");
			report.AppendLine($"*Report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
			if (usePlaytimeTracker)
				report.AppendLine("*Enhanced with PlaytimeTracker data*");
			report.AppendLine();

			// Get all players with statistics
			var playersWithStats = new List<(ulong userId, string playerName, PlayerStats stats, double playTime, double afkTime, double actualPlayTime, double earningRate)>();

			foreach (var kvp in storedData.PlayerStatistics)
			{
				var userId = kvp.Key;
				var stats = kvp.Value;

                // Include offline players when configured. If IncludeOfflinePlayers is false,
                // only include currently connected players. When true, do NOT apply the
                // recent-activity cutoff so that offline players are still reported.
                if (!conf.Settings.DiscordReporting.IncludeOfflinePlayers)
                {
                    var onlinePlayer = BasePlayer.FindByID(userId);
                    if (onlinePlayer == null || !onlinePlayer.IsConnected)
                        continue;
                    // When requiring online players, still respect cutoff for stale data
                    if (stats.LastReset < cutoffTime)
                        continue;
                }

				// Get player name (prefer online name, fallback to cached)
				var player = BasePlayer.FindByID(userId);
				var playerName = ResolvePlayerName(userId, player);

				// Get playtime data from PlaytimeTracker if available
				double playTime = 0;
				double afkTime = 0;
				double actualPlayTime = 0;
				double earningRate = 0;

				if (usePlaytimeTracker)
				{
					var playTimeObj = GetPlayerPlayTime(userId);
					var afkTimeObj = GetPlayerAFKTime(userId);
					
					if (playTimeObj is double pt)
						playTime = pt;
					if (afkTimeObj is double at)
						afkTime = at;
					
					actualPlayTime = playTime - afkTime;
				}

				// Check if player has any non-zero statistics
				bool hasStats = false;
				foreach (var category in stats.CategoryTotals)
				{
					if (Math.Abs(category.Value) > 0.01) // Account for floating point precision
					{
						hasStats = true;
						break;
					}
				}

				if (hasStats)
				{
					// Calculate earning rate if we have playtime data
					if (usePlaytimeTracker && actualPlayTime > 0)
					{
						var totalEarnings = stats.CategoryTotals.Values.Sum();
						earningRate = totalEarnings / (actualPlayTime / 3600); // per hour
					}

					playersWithStats.Add((userId, playerName, stats, playTime, afkTime, actualPlayTime, earningRate));
				}
			}

			if (playersWithStats.Count == 0)
			{
				return "No player statistics to report.";
			}

			// Sort by total earnings (sum of all categories)
			playersWithStats.Sort((a, b) => 
			{
				var totalA = a.stats.CategoryTotals.Values.Sum();
				var totalB = b.stats.CategoryTotals.Values.Sum();
				return totalB.CompareTo(totalA); // Descending order
			});

			foreach (var (userId, playerName, stats, playTime, afkTime, actualPlayTime, earningRate) in playersWithStats)
			{
				report.AppendLine($"**{playerName}** ({userId})");
				
				// Add time information if PlaytimeTracker is available
				if (usePlaytimeTracker)
				{
					report.AppendLine($"- Play Time: {FormatTime(actualPlayTime)} (Total: {FormatTime(playTime)})");
					if (afkTime > 0)
						report.AppendLine($"- AFK Time: {FormatTime(afkTime)}");
					if (earningRate > 0)
						report.AppendLine($"- Earning Rate: {FormatCurrencyAmount(earningRate)}/hour");
					report.AppendLine();
				}
				
				// Format each category
				var categoryNames = new Dictionary<RewardType, string>
				{
					{ RewardType.Kill, "Kills" },
					{ RewardType.Harvest, "Farming" },
					{ RewardType.Open, "Looting" },
					{ RewardType.Pickup, "Pickup" },
					{ RewardType.Activity, "Activity" },
					{ RewardType.Welcome, "Welcome" }
				};

				foreach (var category in stats.CategoryTotals)
				{
					if (Math.Abs(category.Value) > 0.01) // Only show non-zero values
					{
						var categoryName = categoryNames.ContainsKey(category.Key) ? categoryNames[category.Key] : category.Key.ToString();
						var amount = FormatCurrencyAmount(category.Value);
						report.AppendLine($"- {categoryName} = {amount}");
						hasAnyData = true;
					}
				}
				report.AppendLine();
			}

			return hasAnyData ? report.ToString() : "";
		}

		string FormatCurrencyAmount(double amount)
		{
			if (currency == Currency.Economics)
				return $"${amount:F2}";
			else if (currency == Currency.ServerRewards)
				return $"{amount:F0} SR";
			else if (currency == Currency.Scrap)
				return $"{amount:F0} Scrap";
			else
				return $"{amount:F2}";
		}

		static float? ParseDiscordRetryAfterSeconds(string response)
		{
			if (string.IsNullOrEmpty(response))
				return null;
			try
			{
				var jo = Newtonsoft.Json.Linq.JObject.Parse(response);
				var t = jo["retry_after"];
				if (t == null || t.Type == Newtonsoft.Json.Linq.JTokenType.Null)
					return null;
				return Convert.ToSingle(t) + 0.05f;
			}
			catch
			{
				return null;
			}
		}

		void EnqueueDiscordWebhookContent(string webhook, string content)
		{
			if (string.IsNullOrEmpty(webhook) || string.IsNullOrEmpty(content))
				return;
			_discordOutgoing.Enqueue((webhook, content));
			TryProcessDiscordWebhookQueue();
		}

		void TryProcessDiscordWebhookQueue()
		{
			if (_discordWebhookBusy || _discordOutgoing.Count == 0)
				return;

			_discordWebhookBusy = true;
			var (webhook, message) = _discordOutgoing.Peek();

			if (message.Length > 2000)
			{
				int originalLength = message.Length;
				message = message.Substring(0, 1997) + "...";
				PrintWarning($"Discord message truncated to 2000 characters (was {originalLength})");
			}

			var payload = new Dictionary<string, object>
			{
				{"content", message},
				{"username", "GrimmRewards"},
				{"avatar_url", "https://www.dropbox.com/scl/fi/cfqwdj0sqdtn7ydog3g14/gr.png?rlkey=0ataku53xk5ouytcskmvt5vxx&st=dlljaqox&dl=1"}
			};

			webrequest.Enqueue(webhook, JsonConvert.SerializeObject(payload), (code, response) =>
			{
				if (code == 200 || code == 204)
				{
					_discordWebhookBusy = false;
					_discordOutgoing.Dequeue();
					TryProcessDiscordWebhookQueue();
					return;
				}

				if (code == 429)
				{
					float wait = ParseDiscordRetryAfterSeconds(response) ?? 1f;
					if (wait < 0.35f)
						wait = 0.35f;
					PrintWarning($"Discord rate limited; retrying in {wait:F2}s");
					// Keep _discordWebhookBusy true until retry so new enqueues cannot start a duplicate send for the same queue head.
					timer.Once(wait, () =>
					{
						_discordWebhookBusy = false;
						TryProcessDiscordWebhookQueue();
					});
					return;
				}

				_discordWebhookBusy = false;
				PrintError($"Discord webhook failed: HTTP {code} - {response}");
				_discordOutgoing.Dequeue();
				TryProcessDiscordWebhookQueue();
			}, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } }, 30f);
		}

		// Splits into Discord-sized chunks (2000 char limit) and queues for one-at-a-time delivery with 429 retry.
		int SendDiscordTextChunks(string webhook, string message)
		{
			if (string.IsNullOrEmpty(webhook) || string.IsNullOrEmpty(message))
				return 0;

			const int maxLen = 1800;
			var lines = message.Split('\n').ToList();
			var current = new System.Text.StringBuilder();
			int queued = 0;

			void FlushCurrent()
			{
				if (current.Length == 0)
					return;
				EnqueueDiscordWebhookContent(webhook, current.ToString());
				queued++;
				current.Clear();
			}

			foreach (var line in lines)
			{
				if (line.Length > maxLen)
				{
					FlushCurrent();
					int lineIndex = 0;
					while (lineIndex < line.Length)
					{
						int chunkSize = Math.Min(maxLen, line.Length - lineIndex);
						EnqueueDiscordWebhookContent(webhook, line.Substring(lineIndex, chunkSize));
						queued++;
						lineIndex += chunkSize;
					}
					continue;
				}

				if (current.Length + line.Length + 1 > maxLen)
					FlushCurrent();

				if (current.Length > 0)
					current.Append("\n");
				current.Append(line);
			}
			FlushCurrent();
			return queued;
		}

		void SendWipeSummaryReport()
		{
			if (!conf.Settings.DiscordReporting.EnableDiscordReporting || string.IsNullOrEmpty(conf.Settings.DiscordReporting.DiscordWebhook))
				return;

			try
			{
				var report = FormatWipeSummaryReport();
				if (!string.IsNullOrEmpty(report))
				{
					var parts = SendDiscordTextChunks(conf.Settings.DiscordReporting.DiscordWebhook, report);
					PrintWarning(parts > 0 ? $"Wipe summary report queued ({parts} message(s))" : "Wipe summary had nothing to queue for Discord");
				}
            }
            catch (Exception ex)
            {
                PrintError($"Error sending wipe summary report: {ex.Message}");
            }
        }

        internal void CmdRustRewardsWipeSummary(ConsoleSystem.Arg arg)
        {
            try
            {
                var report = FormatWipeSummaryReport();
                if (!string.IsNullOrEmpty(report))
                {
                    // Print to server console
                    Puts(report);
					// Send to Discord if configured
					if (conf.Settings.DiscordReporting.EnableDiscordReporting && !string.IsNullOrEmpty(conf.Settings.DiscordReporting.DiscordWebhook))
					{
						// Use chunked text to avoid 400 errors on large embeds
						var parts = SendDiscordTextChunks(conf.Settings.DiscordReporting.DiscordWebhook, report);
						PrintWarning(parts > 0 ? $"[GrimmRewards] Wipe summary queued for Discord ({parts} message(s))" : "[GrimmRewards] Wipe summary produced no Discord messages");
					}
					else
					{
						PrintWarning($"[GrimmRewards] Discord reporting disabled or webhook missing. Enabled: {conf.Settings.DiscordReporting.EnableDiscordReporting}, Webhook: {!string.IsNullOrEmpty(conf.Settings.DiscordReporting.DiscordWebhook)}");
					}
                }
                else
                {
                    PrintWarning("[GrimmRewards] No player statistics available for wipe summary.");
                }
            }
            catch (Exception ex)
            {
                PrintError($"[GrimmRewards] Error generating wipe summary: {ex.Message}");
            }
        }

		string FormatWipeSummaryReport()
		{
			if (storedData?.PlayerStatistics == null || storedData.PlayerStatistics.Count == 0)
				return "No player statistics available for wipe summary.";

			var report = new System.Text.StringBuilder();
			var cutoffTime = DateTime.Now.AddHours(-conf.Settings.DiscordReporting.MinActivityHours);
			bool hasAnyData = false;
			bool usePlaytimeTracker = IsPlaytimeTrackerAvailable();

			report.AppendLine("**🏁 WIPE SUMMARY REPORT**");
			report.AppendLine($"*Wipe completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
			if (usePlaytimeTracker)
				report.AppendLine("*Enhanced with PlaytimeTracker data*");
			report.AppendLine();

			// Get all players with statistics
			var playersWithStats = new List<(string playerName, ulong steamId, PlayerStats stats, double playTime, double afkTime, double actualPlayTime, double earningRate)>();

			foreach (var playerStat in storedData.PlayerStatistics)
			{
				var player = BasePlayer.FindByID(playerStat.Key);
				if (player == null && !conf.Settings.DiscordReporting.IncludeOfflinePlayers)
					continue;

				string playerName = ResolvePlayerName(playerStat.Key, player);
				
				// Get playtime data from PlaytimeTracker if available
				double playTime = 0;
				double afkTime = 0;
				double actualPlayTime = 0;
				double earningRate = 0;

				if (usePlaytimeTracker)
				{
					var playTimeObj = GetPlayerPlayTime(playerStat.Key);
					var afkTimeObj = GetPlayerAFKTime(playerStat.Key);
					
					if (playTimeObj is double pt)
						playTime = pt;
					if (afkTimeObj is double at)
						afkTime = at;
					
					// Apply per-wipe baselines if present
					double basePlay = 0, baseAFK = 0;
					if (storedData.WipeBaselinePlay.TryGetValue(playerStat.Key, out var bp)) basePlay = bp;
					if (storedData.WipeBaselineAFK.TryGetValue(playerStat.Key, out var ba)) baseAFK = ba;
					actualPlayTime = Math.Max(0, (playTime - basePlay) - Math.Max(0, afkTime - baseAFK));
				}
				else
				{
					// Fallback to old system - no time tracking available
					actualPlayTime = 0;
				}

				// Check if player was active during the wipe
				if (storedData.PlayerPrefs.TryGetValue(playerStat.Key, out var prefs))
				{
					if (prefs.LastActive >= cutoffTime || conf.Settings.DiscordReporting.IncludeOfflinePlayers)
					{
						// Calculate earning rate if we have playtime data
						if (usePlaytimeTracker && actualPlayTime > 0)
						{
							var totalEarnings = playerStat.Value.CategoryTotals.Values.Sum();
							earningRate = totalEarnings / (actualPlayTime / 3600); // per hour
						}

						playersWithStats.Add((playerName, playerStat.Key, playerStat.Value, playTime, afkTime, actualPlayTime, earningRate));
					}
				}
			}

			if (playersWithStats.Count == 0)
			{
				return "No active players found for wipe summary.";
			}

			// Sort by total rewards earned (sum of all categories)
			playersWithStats.Sort((a, b) => 
			{
				double totalA = a.stats.CategoryTotals.Values.Sum();
				double totalB = b.stats.CategoryTotals.Values.Sum();
				return totalB.CompareTo(totalA);
			});

			foreach (var (playerName, steamId, stats, playTime, afkTime, actualPlayTime, earningRate) in playersWithStats)
			{
				// Check if player has any rewards
				bool hasRewards = stats.CategoryTotals.Values.Any(total => total > 0);
				if (!hasRewards && actualPlayTime < 360) continue; // Less than 6 minutes

				report.AppendLine($"**{playerName}** ({steamId})");
				
				// Add time information
				if (usePlaytimeTracker)
				{
					report.AppendLine($"- Play Time: {FormatTime(actualPlayTime)} (Total: {FormatTime(playTime)})");
					if (afkTime > 0)
						report.AppendLine($"- AFK Time: {FormatTime(afkTime)}");
					if (earningRate > 0)
						report.AppendLine($"- Earning Rate: {FormatCurrencyAmount(earningRate)}/hour");
				}
				else
				{
					report.AppendLine($"- Play Time: {actualPlayTime / 3600:F1} hours");
				}
				
				// Add reward categories
				foreach (var category in stats.CategoryTotals)
				{
					if (category.Value > 0)
					{
						string formattedAmount = FormatCurrencyAmount(category.Value);
						report.AppendLine($"- {category.Key} = {formattedAmount}");
					}
				}
				report.AppendLine();
				hasAnyData = true;
			}

			if (!hasAnyData)
			{
				return "No player activity found for wipe summary.";
			}

			return report.ToString();
		}

		internal void CmdRustRewardsSetWipeBaseline(ConsoleSystem.Arg arg)
		{
			if (!arg.IsServerside) return;
			if (!IsPlaytimeTrackerAvailable())
			{
				PrintWarning("PlaytimeTracker not available; cannot capture baseline.");
				return;
			}
			storedData.WipeBaselinePlay.Clear();
			storedData.WipeBaselineAFK.Clear();
			int count = 0;
			foreach (var kv in storedData.PlayerStatistics)
			{
				var id = kv.Key;
				var pt = GetPlayerPlayTime(id);
				var afk = GetPlayerAFKTime(id);
				if (pt is double p)
				{
					storedData.WipeBaselinePlay[id] = p;
					count++;
				}
				if (afk is double a)
					storedData.WipeBaselineAFK[id] = a;
			}
			SaveData();
			PrintWarning($"Captured wipe playtime baseline for {count} players at {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
		}

		internal void CmdSendDiscordReport(ConsoleSystem.Arg arg)
		{
			if (!arg.IsServerside) return;
			
			PrintWarning("Sending manual Discord report...");
			SendDiscordReport();
		}
		#endregion

		#endregion

		#region Config
		private ConfigData conf;

		internal bool LoadConfigVariables()
		{
			try
			{
				conf = Config.ReadObject<ConfigData>();

				if (conf == null)
					return false;
			}
			catch
			{
				return false; 
			}

			SaveConf();
			return true;
		}  

		protected override void LoadDefaultConfig()
		{
			Puts("Creating new config file.");
		}

		void SaveConf()
		{
			if (conf != null)
				Config.WriteObject(conf, true);
		} 

		class ConfigData 
		{
			public Settings Settings = new Settings();
			public Dictionary<string, double> Distance_Multipliers = new Dictionary<string, double>() { { "Distance_010", 1.0 }, { "Distance_025", 1.0 }, { "Distance_050", 1.0 }, { "Distance_100", 1.0 }, { "Distance_200", 1.0 }, { "Distance_300", 1.0 }, { "Distance_400", 1.0 } };
			public Dictionary<string, double> Group_Multipliers = new Dictionary<string, double>() { { "Default", 1.0 } };
			public Dictionary<string, double> Permission_Multipliers = new Dictionary<string, double>() { { "Default", 1.0 } };
			public Dictionary<string, double> Weapon_Multipliers = rr.WeaponsList;
			public Dictionary<string, double> WeekDay_Multipliers = new Dictionary<string, double>() { { "Sunday", 1.0 }, { "Monday", 1.0 }, { "Tuesday", 1.0 }, { "Wednesday", 1.0 }, { "Thursday", 1.0 }, { "Friday", 1.0 }, { "Saturday", 1.0 } };
			public RewardTypes RewardTypes = new RewardTypes();
		}

		public double Get_Distance_Multiplier(float distance) => distance >= 400 ? conf.Distance_Multipliers["Distance_400"] : distance >= 300 ? conf.Distance_Multipliers["Distance_300"] : distance >= 200 ? conf.Distance_Multipliers["Distance_200"] : distance >= 100 ? conf.Distance_Multipliers["Distance_100"] : distance >= 50 ? conf.Distance_Multipliers["Distance_050"] : distance >= 25 ? conf.Distance_Multipliers["Distance_025"] : distance >= 10 ? conf.Distance_Multipliers["Distance_010"] : 1;

		public class RewardTypes
		{
			public Kill Kill = rr.Kills;
			public Harvest Harvest = rr.Harvests;
			public Dictionary<string, double> Open = rr.Open;
			public Dictionary<string, double> Pickup = rr.Pickup;
		}

		public class Settings 
		{
			public General General = new General();
			public RewardCurrency RewardCurrency = new RewardCurrency();
			public Allies Allies = new Allies();
			public ThirdPartyPlugins Plugins = new ThirdPartyPlugins(); 
			public Announcements Announcements = new Announcements();
			public Multipliers Multipliers = new Multipliers();
			public Rewards Rewards = new Rewards();
			public DiscordReporting DiscordReporting = new DiscordReporting();
			public List<ulong> SkinnedItemBlackList = new List<ulong>();
			public UI UI = new UI();
		}

		public class DiscordReporting
		{
			public bool EnableDiscordReporting = false;
			public string DiscordWebhook = "";
			public int ReportIntervalHours = 12;
			public bool IncludeOfflinePlayers = true;
			public int MinActivityHours = 1; // Only include players active in last X hours
		}

		public class UI
		{
			public string MainCommandAlias = "GrimmRewards";
			public string ButtonColour = "0.7 0.32 0.17 1";
			public string ButtonColour2 = "0.4 0.1 0.1 1";
			public double Reward_Small_Increment = 1.0;
			public double Reward_Large_Increment = 10.0;
			public double Multiplier_Increment = 0.1;
			public string BackgroundImage = "RustRewards/pinned.png";
		}

		public class General
		{
			public bool UseServerDayNightHours = true;
			public bool UseRealTime = false;
			public int UTCHourOffset = 0;
			public int DayStartHour = 8, NightStartHour = 20;
			public bool Reset_Activity_Reward_At_Wipe = false;
			public bool UI_Requires_Admin_Perm = false;
            public bool Disable_All_Notifications = false;
			public bool TakeMoneyfromVictim = false;
			public bool LogToFile = false;
			public bool LogToConsole = false;
			public int HappyHour_BeginHour = 17;
			public int HappyHour_EndHour = 21;
			public int Player_Kill_Reward_CoolDown_Seconds = 0;
			public bool View_Reward_Values = true;
			public ulong ChatIcon = 0;
            public bool Only_Reward_Growables_From_Planters = false;
			public bool Use_Harvesting_Cooldown = true;
			public bool Add_Multipliers = false;
			public bool Use_Highest_Multiplier_Only = false;
			public int Delete_Player_Prefs_After_Days = 100;  
        }

		public class Allies
		{
			public bool UseFriendsPlugin = true;
			public bool UseClansPlugin = true;
			public bool UseRustTeams = true;
		}

		public class RewardCurrency
		{
			public bool UseScrap = true;
			public bool UseEconomics = false;
			public bool UseServerRewards = false;
		}
		public class ThirdPartyPlugins
		{
			public bool UseGUIAnnouncementsPlugin = false;
			public bool UseZoneManagerPlugin = false;
			public bool UseNoEscape = false;
		}

		public class Announcements
		{
			public string ChatMessageFormat = "<color=#CCBB00>{0}</color><color=#FFFFFF>{1}</color>";
			public string GUI_Announcement_Banner_Colour = "Blue";
			public string GUI_Announcement_Text_Colour = "Yellow";
		}

		public class Multipliers
		{
			public bool UseDynamicDistance = false; 
			public double DynamicDistance = 0.01f;
			public double HappyHour = 1.0;
			public double RaidableBases = 1.0;
			public double Daytime = 1.0;
			public double Nighttime = 1.0;
		}

		public class Rewards
		{
			public int ActivityReward_Seconds = 600;
			public double ActivityRewardAmount = 0.0;
			public bool Activity_Reward_For_AFK = true;
			public double WelcomeMoneyAmount = 0.0;
			public bool Use_Permissions = false;
			public bool OpenReward = true;
			public bool KillReward = true;
			public bool PickupReward = true;
			public bool HarvestReward = true;
		}
		#endregion

		#region Messages

		Dictionary<string, string> Messages = new Dictionary<string, string>
		{
			["Show_Kills"] = "Show Kills",
			["Show_Harvest"] = "Show Harvest",
			["Show_Open"] = "Show Open",
			["Show_Pickup"] = "Show Pickup",
			["Show_Activity"] = "Show Activity",
			["Show_Welcome"] = "Show Welcome",
			["IconPosition"] = "Icon Position", 
			["Type"] = "Type",
			["RewardNotificationSettings"] = "Reward Notification Settings",

			["Kill"] = "You received {0} | Kill | {1} | {2}m.",
			["Harvest"] = "You received {0} | Harvest | {1}.",
			["Open"] = "You received {0} | Loot | {1}.",
			["Pickup"] = "You received {0} | Pickup | {1}.",
			["Activity"] = "You received an activity reward of {0}.",

			["Kill_negative"] = "You lost {0} | Kill | {1} | {2}m.",

			["Welcome"] = "You received a welcome reward of {0}.",
			["NotificationInfo"] = "Here you can toggle notification type Chat/Banner/Icon/Off, \nenable and disable notifications for the various categories, \nand set the position for Icon UI notifications on-screen.",
			["happyhourend"] = "Happy Hour(s) ended.",
			["happyhourstart"] = "Happy Hour(s) started.",
			["Prefix"] = "GrimmRewards : ",
			["rrm changed"] = "Rewards Messages for {0} is now {1}. Currently on are: {2}",
			["rrm syntax"] = "/rrm syntax:  /rrm type state  Type is one of a, h, o, p or k (Activity, Havest, Open, Pickup or Kill).  State is on or off.  for example /rrm h off",
			["rrm type"] = "type must be one of: a, h, o, p or k only. (Activity, Havest, Open, Pickup or Kill",
			["rrm state"] = "state need to be one of: on or off.",
			["VictimNoMoney"] = "{0} doesn't have enough money.",
			["VictimKilled"] = "You lost {0} Reward for being killed by a player",
			["rewardset"] = "Reward was set",
			["setrewards"] = "Variables you can set:",
			["pvptoosoon"] = "It is too soon for another reward on killing {0}!",
			["NoEscapeBlocked"] = "You can't get rewards while blocked!"
		};
		#endregion

		#region CUI
		const string Font = "robotocondensed-regular.ttf";
		const string Sprite = "assets/content/textures/generic/fulltransparent.tga";

		internal void OnPlayerDisconnected(BasePlayer player, string reason)
		{
			DestroyMenu(player, true, false, false);
		}

		void DestroyMenu(BasePlayer player, bool all, bool admin, bool prefs)
		{
			if (admin)
				SaveConf();
			if (prefs)
				SaveData();

			if (all)
			{ 
				CuiHelper.DestroyUi(player, "RRPUI"); 
				CuiHelper.DestroyUi(player, "RRRUI");
				CuiHelper.DestroyUi(player, "RRBGUI");
			}
			CuiHelper.DestroyUi(player, "RRMainUI");
		}

		internal void RustRewardsUI(BasePlayer player, string command, string[] args)
		{
			if (conf.Settings.General.UI_Requires_Admin_Perm && !HasPerm(player.UserIDString, AdminUIPermission))
				return;

			if (conf.Settings.General.Disable_All_Notifications)
			{
				if (conf.Settings.General.View_Reward_Values || HasPerm(player.UserIDString, AdminUIPermission)) 
				{
					RRBGUI(player);
					RRMainUI(player, 0, 0, 0);
				}
			}
			else
				RRPlayerUI(player);
		}

		internal void rrv(ConsoleSystem.Arg arg)
		{
			RRBGUI(arg.Player());
			RRMainUI(arg.Player(), 0, 0, 0); 
		}

		void RRPlayerUI(BasePlayer player)
		{
			DestroyMenu(player, true, false, false);
			string guiString = string.Format("0.1 0.1 0.1 0.98");
			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { Color = guiString }, RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.95" }, CursorEnabled = true }, "Overlay", "RRPUI");
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0.95", AnchorMax = $"0.999 1" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.999 0.05" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiButton { Button = { Command = "CloseRR false true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = "0.955 0.96", AnchorMax = "0.99 0.99" }, Text = { Text = "X", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiLabel { Text = { Text = "GrimmRewards", FontSize = 20, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.95", AnchorMax = "0.8 1" } }, mainName);
			elements.Add(new CuiLabel { Text = { Text = Lang("RewardNotificationSettings", player.UserIDString) + " - " + player.displayName, FontSize = 20, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.85", AnchorMax = "0.8 0.9" } }, mainName);

            double t = 0.76;
			double b = 0.8;

            var record = storedData.PlayerPrefs[player.userID];
			var fields = record.GetType().GetFields().ToList();
 
            elements.Add(new CuiLabel { Text = { Text = $"Type", FontSize = 16, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.38 {t}", AnchorMax = $"0.49 {b}" } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRChangeType {record.Type}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.51 {t}", AnchorMax = $"0.62 {b}" }, Text = { Text = $"{Notification[record.Type]}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

            t -= 0.05;
			b -= 0.05;

            for (int i = 0; i < fields.Count(); i++)
			{ 
				var name = fields[i].Name;
                if (name == "Position" || name == "Type" || name == "Activity_Given" || name == "LastActive" || name == "Activity_Given")
					continue;
                bool val = (bool)fields[i].GetValue(record);
                //  Add lang entries for enum rewardtype

                elements.Add(new CuiLabel { Text = { Text = $"{Lang(fields[i].Name, player.UserIDString)}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.38 {t}", AnchorMax = $"0.49 {b}" } }, mainName);
				elements.Add(new CuiButton { Button = { Command = $"RRChangePref {fields[i].Name}", Color = val ? conf.Settings.UI.ButtonColour : conf.Settings.UI.ButtonColour2 }, RectTransform = { AnchorMin = $"0.51 {t}", AnchorMax = $"0.62 {b}" }, Text = { Text = $"{val}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

				t -= 0.05;
				b -= 0.05;
			}
            elements.Add(new CuiLabel { Text = { Text = Lang("IconPosition", player.UserIDString), FontSize = 16, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.38 {t}", AnchorMax = $"0.49 {b}" } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRChangePos {record.Position}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.51 {t}", AnchorMax = $"0.62 {b}" }, Text = { Text = $"{Indicies[record.Position].Replace("_", " ")}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

			elements.Add(new CuiLabel { Text = { Text = Lang("NotificationInfo"), FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.25" } }, mainName);

			if (conf.Settings.General.View_Reward_Values || HasPerm(player.UserIDString, AdminUIPermission))
				elements.Add(new CuiButton { Button = { Command = "rrv", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.01", AnchorMax = $"0.6 0.040" }, Text = { Text = "View reward values", FontSize = 14, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);


			CuiHelper.AddUi(player, elements);
		}

		internal void RRChangePref(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			var record = storedData.PlayerPrefs[player.userID];
			var field = record.GetType().GetField(arg.GetString(0));
			field.SetValue(record, !(bool)field.GetValue(record));

			RRPlayerUI(player);
		}

		internal void RRChangePos(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			var record = storedData.PlayerPrefs[player.userID];
			record.Position = record.Position == 3 ? 0 : record.Position + 1;
			RRPlayerUI(player);
			SendTestNotify(player);
		}

		internal void RRChangeType(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			var record = storedData.PlayerPrefs[player.userID];
			record.Type = record.Type == Notification.Count() - 1 ? 0 : record.Type + 1;
			if (!GUIAnnouncements && record.Type == 1)
				record.Type++;
			RRPlayerUI(player);
			SendTestNotify(player);
		}

		void SendTestNotify(BasePlayer player) 
        {
			if (!conf.Settings.General.Disable_All_Notifications)
			{
				var prefs = storedData.PlayerPrefs[player.userID];
				if (prefs.Type != 3 && prefs.ShowReward(RewardType.Activity))
				{
					if (prefs.Type == 2)
					{
						RRRUI(player, RewardType.Activity, $"+{1}");
					}
					else
						NotifyReward(player, "Reward notification test", Lang("Prefix", player.UserIDString), prefs.Type == 1);
				}
			}
		}

		void RRBGUI(BasePlayer player)
		{
			DestroyMenu(player, true, false, false);
			string guiString = string.Format("0.1 0.1 0.1 0.98");
			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { Color = guiString }, RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.95" }, CursorEnabled = true }, "Overlay", "RRBGUI");
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0.95", AnchorMax = $"0.999 1" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.999 0.05" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"CloseRR {HasPerm(player.UserIDString, AdminUIPermission)} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = "0.955 0.96", AnchorMax = "0.99 0.99" }, Text = { Text = "X", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiLabel { Text = { Text = "GrimmRewards", FontSize = 20, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.95", AnchorMax = "0.8 1" } }, mainName);
			CuiHelper.AddUi(player, elements);
		}

		void RRMainUI(BasePlayer player, int tab, int subtab, int subsubtab)
		{
			DestroyMenu(player, false, false, false);
			bool Control = HasPerm(player.UserIDString, AdminUIPermission);
			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.1 0", AnchorMax = "0.9 0.9" }, CursorEnabled = true }, "Overlay", "RRMainUI");
			elements.Add(new CuiElement { Parent = "RRMainUI", Components = { new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });

			double top = 0.875;
			double bottom = 0.9;

			elements.Add(new CuiButton { Button = { Command = $"RRUI {0} 0 0", Color = tab == 0 ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.15 0.95", AnchorMax = $"0.35 0.99" }, Text = { Text = $"Reward Values", FontSize = 18, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRUI {1} 0 0", Color = tab == 1 ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.95", AnchorMax = $"0.6 0.99" }, Text = { Text = $"Multipliers", FontSize = 18, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRUI {2} 0 0", Color = tab == 2 ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.65 0.95", AnchorMax = $"0.85 0.99" }, Text = { Text = $"Zones", FontSize = 18, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

			bool odd = true;
			double l = 0.12;
			double r = 0.28;
			double left = 0;
			List<string> fields = tab == 0 ? typeof(RewardTypes).GetFields().Select(x => x.Name).ToList() : new List<string>() { "Permission", "Group", "Distance", "Weapon", "WeekDay" };

			if (tab == 1)
            {
				l = 0.09;
				r = 0.245;
			}
			if (tab != 2)
				for (int i = 0; i < fields.Count(); i++) 
				{
					if (fields[i] == "Activity" || fields[i] == "Welcome")
						continue;

					elements.Add(new CuiButton { Button = { Command = $"RRUI {tab} {i} 0", Color = subtab == i ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{l} 0.91", AnchorMax = $"{r} 0.935" }, Text = { Text = $"{fields[i]}", FontSize = 14, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					l += 1.0 / (fields.Count+1);
					r += 1.0 / (fields.Count + 1);
				}

			l = 0.04;
			r = 0.16;

			//RewardValues  
			if (tab == 0)
			{
				if (subtab == 0 || subtab == 1)
				{
					Type type = subtab == 0 ? typeof(Kill) : typeof(Harvest);
					var innerfields = type.GetFields().Select(x => x.Name).ToList();
					for (int i = 0; i < innerfields.Count(); i++)
					{

						elements.Add(new CuiButton { Button = { Command = $"RRUI {tab} {subtab} {i}", Color = subsubtab == i ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{l} {top}", AnchorMax = $"{r} {bottom}" }, Text = { Text = $"{innerfields[i]}", FontSize = 14, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						l += 0.2;
						r += 0.2;
					}

					top -= 0.075f;
					bottom -= 0.075f;

					var records = (subtab == 0 ? type.GetField(innerfields[subsubtab]).GetValue(conf.RewardTypes.Kill) : type.GetField(innerfields[subsubtab]).GetValue(conf.RewardTypes.Harvest)) as Dictionary<string, double>;

					if (Control)
					{
						elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.202 {top + 0.003}", AnchorMax = $"0.215 {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.22 {top + 0.003}", AnchorMax = $"0.233 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.287 {top + 0.003}", AnchorMax = $"0.30 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.305 {top + 0.003}", AnchorMax = $"0.318 {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						top -= 0.025f;
						bottom -= 0.025f;
					}
					foreach (var value in records)
					{
						if (!Control && value.Value == 0)
							continue;

						if (top < 0.14)
						{
							if (left == 0.67)
							{
								Puts("UI Overflow - notify author");
								continue;
							}
							top = Control ? 0.775 : 0.8;
							bottom = Control ? 0.8 : 0.825;
							left += 0.33f;
						}

						top -= 0.025f;
						bottom -= 0.025f;

						if (odd && left == 0)
							elements.Add(new CuiPanel { Image = { Color = $"0 0 0 0.8" }, RectTransform = { AnchorMin = $"{left} {top}", AnchorMax = $"0.999 {bottom}" }, CursorEnabled = true }, mainName);

						elements.Add(new CuiLabel { Text = { Text = $"{value.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.02} {top}", AnchorMax = $"{left + 0.3} {bottom}" } }, mainName);

						if (Control)
						{
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.202} {top + 0.003}", AnchorMax = $"{left + 0.215} {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.22} {top + 0.003}", AnchorMax = $"{left + 0.233} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.287} {top + 0.003}", AnchorMax = $"{left + 0.30} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.305} {top + 0.003}", AnchorMax = $"{left + 0.318} {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						}
						elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.235} {top + 0.003}", AnchorMax = $"{left + 0.285} {bottom - 0.003}" } }, mainName);

						odd = !odd;
					}
				}

				if (subtab == 2)
				{
					if (Control)
					{
						elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.202 {top + 0.003}", AnchorMax = $"0.215 {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.22 {top + 0.003}", AnchorMax = $"0.233 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.287 {top + 0.003}", AnchorMax = $"0.30 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.305 {top + 0.003}", AnchorMax = $"0.318 {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						top -= 0.025f;
						bottom -= 0.025f;
					}

					foreach (var value in conf.RewardTypes.Open)
					{
						if (!Control && value.Value == 0)
							continue;
						if (top < 0.14)
						{
							if (left > 0.67)
							{
								Puts("UI Overflow - notify author");
								continue;
							}
							top = Control ? 0.85 : 0.875;
							bottom = Control ? 0.875 : 0.9;
							left+=0.33f;
						}

						top -= 0.025;
						bottom -= 0.025;

						if (odd && left == 0)
							elements.Add(new CuiButton { Button = { Command = "", Color = "0 0 0 0.8" }, RectTransform = { AnchorMin = $"{left} {top}", AnchorMax = $"0.999 {bottom}" }, Text = { Text = "", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

						elements.Add(new CuiLabel { Text = { Text = $"{GetFriendly(value.Key, null)}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.02} {top}", AnchorMax = $"{left + 0.3} {bottom}" } }, mainName);

						if (Control)
						{
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.202} {top + 0.003}", AnchorMax = $"{left + 0.215} {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.22} {top + 0.003}", AnchorMax = $"{left + 0.233} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.287} {top + 0.003}", AnchorMax = $"{left + 0.30} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.305} {top + 0.003}", AnchorMax = $"{left + 0.318} {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						}
						elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.235} {top + 0.003}", AnchorMax = $"{left + 0.285} {bottom - 0.003}" } }, mainName);
						odd = !odd;
					}
				}
				 
				if (subtab == 3)
				{
					if (Control)
					{
						elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.242 {top + 0.003}", AnchorMax = $"0.255 {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.345 {top + 0.003}", AnchorMax = $"0.358 {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						top -= 0.025f;
						bottom -= 0.025f;
					}
					foreach (var value in conf.RewardTypes.Pickup)
					{
						if (!Control && value.Value == 0)
							continue;
						if (top < 0.14)
						{
							if (left == 0.5)
							{
								Puts("UI Overflow - notify author");
								continue;
							}
							top = Control ? 0.85 : 0.875;
							bottom = Control ? 0.875 : 0.9;
							left = 0.5;
						}

						top -= 0.025;
						bottom -= 0.025;

						if (odd && left == 0)
							elements.Add(new CuiPanel { Image = { Color = $"0 0 0 0.8" }, RectTransform = { AnchorMin = $"{left} {top}", AnchorMax = $"0.999 {bottom}" }, CursorEnabled = true }, mainName);

						elements.Add(new CuiLabel { Text = { Text = $"{value.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.02} {top}", AnchorMax = $"{left + 0.3} {bottom}" } }, mainName);

						if (Control)
						{
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.242} {top + 0.003}", AnchorMax = $"{left + 0.255} {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.26} {top + 0.003}", AnchorMax = $"{left + 0.273} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.327} {top + 0.003}", AnchorMax = $"{left + 0.34} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.345} {top + 0.003}", AnchorMax = $"{left + 0.358} {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						}
						elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top + 0.003}", AnchorMax = $"{left + 0.325} {bottom - 0.003}" } }, mainName);
						odd = !odd;
					}
				}
			}

			//Multipliers
			if (tab == 1)
			{
				Dictionary<string, Dictionary<string, double>> Collections = new Dictionary<string, Dictionary<string, double>>()
				{
					{ "Permission_Multipliers", conf.Permission_Multipliers },
					{ "Group_Multipliers", conf.Group_Multipliers },
					{ "Distance_Multipliers", conf.Distance_Multipliers },
					{ "Weapon_Multipliers", conf.Weapon_Multipliers },
					{ "WeekDay_Multipliers", conf.WeekDay_Multipliers }
				};

				if (Control)
				{
					elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllMult {tab} {subtab} {subsubtab} {Collections.ElementAt(subtab).Key} - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllMult {tab} {subtab} {subsubtab}  {Collections.ElementAt(subtab).Key} - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					top -= 0.025f;
					bottom -= 0.025f;
				}

				foreach (var value in Collections.ElementAt(subtab).Value)
				{
					if (!Control && value.Value == 1)
						continue;
					if (top < 0.14)
					{
						top = Control ? 0.85 : 0.875;
						bottom = Control ? 0.875 : 0.9;
						left += 0.33;
					}

					top -= 0.025;
					bottom -= 0.025;

					if (odd && left == 0)
						elements.Add(new CuiButton { Button = { Command = "", Color = "0 0 0 0.8" }, RectTransform = { AnchorMin = $"0 {top}", AnchorMax = $"0.999 {bottom}" }, Text = { Text = "", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					elements.Add(new CuiLabel { Text = { Text = $"{value.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.02} {top}", AnchorMax = $"{left + 0.3} {bottom}" } }, mainName);

					if (Control)
					{
						elements.Add(new CuiButton { Button = { Command = $"RRChangeMult {tab} {subtab} {subsubtab} {Collections.ElementAt(subtab).Key} {value.Key} false {conf.Settings.UI.Multiplier_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.23} {top + 0.003}", AnchorMax = $"{left + 0.243} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeMult {tab} {subtab} {subsubtab} {Collections.ElementAt(subtab).Key} {value.Key} true {conf.Settings.UI.Multiplier_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.297} {top + 0.003}", AnchorMax = $"{left + 0.31} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					}
					elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.245} {top + 0.003}", AnchorMax = $"{left + 0.295} {bottom - 0.003}" } }, mainName);
					odd = !odd;
				}
			}

			if (tab == 2)
			{
				if (Control && storedData.ZoneMultipliers.Count() > 0)
				{
					elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.15 {top}", AnchorMax = $"0.25 {bottom}" } }, mainName);

					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllZoneMult {tab} {subtab} {subsubtab} {storedData.ZoneMultipliers.First().Value} {conf.Settings.UI.Multiplier_Increment} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllZoneMult {tab} {subtab} {subsubtab}  {storedData.ZoneMultipliers.First().Value} {conf.Settings.UI.Multiplier_Increment} true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					top -= 0.05f;
					bottom -= 0.05f;
				}

				elements.Add(new CuiLabel { Text = { Text = $"Zone ID", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.15} {top}", AnchorMax = $"{left + 0.25} {bottom}" } }, mainName);
				elements.Add(new CuiLabel { Text = { Text = $"Multiplier", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top}", AnchorMax = $"{left + 0.325} {bottom}" } }, mainName);

				foreach (var entry in storedData.ZoneMultipliers)
				{
					if (!Control && entry.Value == 1) 
						continue;
					if (top < 0.3)
					{
						if (left == 0.5)
						{
							Puts("UI Overflow - notify author");
							continue;
						}
						top = Control ? 0.85 : 0.875;
						bottom = Control ? 0.875 : 0.9;
						left = 0.5;
					}

					top -= 0.025;
					bottom -= 0.025;

					if (odd && left == 0)
						elements.Add(new CuiButton { Button = { Command = "", Color = "0 0 0 0.8" }, RectTransform = { AnchorMin = $"0 {top}", AnchorMax = $"0.999 {bottom}" }, Text = { Text = "", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					if (Control)
					{
						elements.Add(new CuiButton { Button = { Command = $"RRChangeZoneMult {tab} {subtab} {subsubtab} {entry.Key} {conf.Settings.UI.Multiplier_Increment} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.26} {top + 0.003}", AnchorMax = $"{left + 0.273} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeZoneMult {tab} {subtab} {subsubtab} {entry.Key} {conf.Settings.UI.Multiplier_Increment} true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.327} {top + 0.003}", AnchorMax = $"{left + 0.34} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					}

					elements.Add(new CuiLabel { Text = { Text = $"{entry.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.15} {top}", AnchorMax = $"{left + 0.25} {bottom}" } }, mainName);
					elements.Add(new CuiLabel { Text = { Text = $"{entry.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top}", AnchorMax = $"{left + 0.325} {bottom}" } }, mainName);
					odd = !odd;
				}

				if (Control)
				{
					bool flag = false;
					if (ZoneManager)
					{
						List<string> playerzones = ((string[])ZoneManager?.Call("GetPlayerZoneIDs", player)).ToList();
						foreach (var zone in playerzones)
						{
							flag = true;
							if (!storedData.ZoneMultipliers.ContainsKey(zone))
								elements.Add(new CuiButton { Button = { Command = $"RRZone {tab} {subtab} {subsubtab} {zone} true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.45 0.15", AnchorMax = $"0.55 0.18" }, Text = { Text = "Add current zone", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							else
								elements.Add(new CuiButton { Button = { Command = $"RRZone {tab} {subtab} {subsubtab} {zone} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.45 0.15", AnchorMax = $"0.55 0.18" }, Text = { Text = "Remove current zone", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							break;
						}
					}
					if (!flag)
						elements.Add(new CuiButton { Button = { Command = "", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.3 0.15", AnchorMax = $"0.7 0.18" }, Text = { Text = "Enter a zone to add or remove it.", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
				}
			}
			CuiHelper.AddUi(player, elements);
		}
		#endregion

		#region UICommands
		internal void RRUI(ConsoleSystem.Arg arg)
		{
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
		}

		internal void RRChangeNum(ConsoleSystem.Arg arg)
		{
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			bool up = Convert.ToBoolean(cmdArgs[6]);
			double num = Convert.ToDouble(cmdArgs[7]);
			bool s = cmdArgs[4] == "-";
			var r = conf.RewardTypes.GetType().GetField(cmdArgs[3]);
			var robj = r.GetValue(conf.RewardTypes);
			var sub = s ? null : robj.GetType().GetField(cmdArgs[4]);

			var subobj = s ? (Dictionary<string, double>)robj : (Dictionary<string, double>)sub.GetValue(robj);
			//subobj[cmdArgs[5]] = Math.Round(Mathf.Max(0, (float)(up ? subobj[cmdArgs[5]] + num : subobj[cmdArgs[5]] - num)), 1);
			subobj[cmdArgs[5]] = Math.Round((float)(up ? subobj[cmdArgs[5]] + num : subobj[cmdArgs[5]] - num), 1);

			if (!s)
				sub.SetValue(robj, subobj);
			else
				r.SetValue(conf.RewardTypes, subobj);

			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
		}

		internal void RRChangeAll(ConsoleSystem.Arg arg)
		{
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			bool up = Convert.ToBoolean(cmdArgs[6]);
			int num = Convert.ToInt16(cmdArgs[7]);
			bool s = cmdArgs[4] == "-";
			var r = conf.RewardTypes.GetType().GetField(cmdArgs[3]);
			var robj = r.GetValue(conf.RewardTypes);
			var sub = s ? null : robj.GetType().GetField(cmdArgs[4]);

			var subobj = s ? (Dictionary<string, double>)robj : (Dictionary<string, double>)sub.GetValue(robj);

			var refnum = subobj.First().Value;
			foreach (var entry in subobj.ToDictionary(val => val.Key, val => val.Value))
				subobj[entry.Key] = Math.Round(Mathf.Max(0, (float)(up ? refnum + num : refnum - num)), 1);

			if (!s)
				sub.SetValue(robj, subobj);
			else
				r.SetValue(conf.RewardTypes, subobj);

			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
		}

		internal void RRChangeMult(ConsoleSystem.Arg arg)
		{
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			bool up = Convert.ToBoolean(cmdArgs[5]);
			double num = Convert.ToDouble(cmdArgs[6]);
			var r = conf.GetType().GetField(cmdArgs[3]);
			var robj = (Dictionary<string, double>)r.GetValue(conf);
			robj[cmdArgs[4]] = Math.Round(Mathf.Max(0, (float)(up ? robj[cmdArgs[4]] + num : robj[cmdArgs[4]] - num)), 1);
			r.SetValue(conf, robj);
			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
		}

		internal void RRChangeAllMult(ConsoleSystem.Arg arg)
		{
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			bool up = Convert.ToBoolean(cmdArgs[5]);
			double num = Convert.ToDouble(cmdArgs[6]);
			var r = conf.GetType().GetField(cmdArgs[3]);
			var robj = (Dictionary<string, double>)r.GetValue(conf);
			var refnum = robj.First().Value;

			foreach (var entry in robj.ToDictionary(val => val.Key, val => val.Value))
				robj[entry.Key] = Math.Round(Mathf.Max(0, (float)(up ? refnum + num : refnum - num)), 1);

			r.SetValue(conf, robj);
			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
		}

		internal void RRZone(ConsoleSystem.Arg arg)
		{
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			bool add = Convert.ToBoolean(cmdArgs[4]);

			if (add) 
			{
				if (!storedData.ZoneMultipliers.ContainsKey(cmdArgs[3]))
					storedData.ZoneMultipliers.Add(cmdArgs[3], 0.0);
			}
			else
				storedData.ZoneMultipliers.Remove(cmdArgs[3]);
			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
			SaveConf();
		}

		internal void RRChangeZoneMult(ConsoleSystem.Arg arg)
		{
            var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			double num = Convert.ToDouble(cmdArgs[4]);
			bool up = Convert.ToBoolean(cmdArgs[5]);
			var value = storedData.ZoneMultipliers[cmdArgs[3]];
			storedData.ZoneMultipliers[cmdArgs[3]] = Math.Round(Mathf.Max(0, (float)(up ? value + num : value - num)), 1);
			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
			SaveConf();
		}

		internal void RRChangeAllZoneMult(ConsoleSystem.Arg arg)
		{
			var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			double num = Convert.ToDouble(cmdArgs[4]);
			bool up = Convert.ToBoolean(cmdArgs[5]);
			var value = Convert.ToDouble(cmdArgs[3]);

			foreach (var entry in storedData.ZoneMultipliers.ToDictionary(val => val.Key, val => val.Value))
				storedData.ZoneMultipliers[entry.Key] = Math.Round(Mathf.Max(0, (float)(up ? value + num : value - num)), 1);

			RRMainUI(player, Convert.ToInt16(cmdArgs[0]), Convert.ToInt16(cmdArgs[1]), Convert.ToInt16(cmdArgs[2]));
			SaveConf();
		}

		internal void CloseRR(ConsoleSystem.Arg arg)
		{
			var cmdArgs = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString());
			var player = arg.Player();
			if (player == null)
				return;

			bool admin = Convert.ToBoolean(cmdArgs[0]);
			bool prefs = Convert.ToBoolean(cmdArgs[1]);
			DestroyMenu(player, true, admin, prefs);
		}
		#endregion
	}

	public class Embed
	{
		public int color { get; set; }

		[JsonProperty("fields")] public List<Field> Fields { get; set; } = new();

		public Embed AddField(string name, string value, bool inline, int colors)
		{
			Fields.Add(new Field(name, System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty), inline));
			color = colors;
			return this;
		}

		public string ToJson()
		{
			return JsonConvert.SerializeObject(this);
		}
	}

	public class Field
	{
		[JsonProperty("name")] public string Name { get; set; }

		[JsonProperty("value")] public string Value { get; set; }

		[JsonProperty("inline")] public bool Inline { get; set; }

		public Field(string name, string value, bool inline)
		{
			Name = name;
			Value = value;
			Inline = inline;
		}
	}

	public class WebhookMessage
	{
		[JsonProperty("content")] public string Content { get; set; }

		[JsonProperty("username")] public string Username { get; set; }

		[JsonProperty("avatar_url")] public string AvatarUrl { get; set; }

		[JsonProperty("embeds")] public List<Embed> Embeds { get; set; } = new();

		public WebhookMessage(string content, Embed embed)
		{
			Content = content;
			Embeds.Add(embed);
			Username = "GrimmRewards";
			AvatarUrl = "https://www.dropbox.com/scl/fi/cfqwdj0sqdtn7ydog3g14/gr.png?rlkey=0ataku53xk5ouytcskmvt5vxx&st=dlljaqox&dl=1";
		}

		public string ToJson()
		{
			return JsonConvert.SerializeObject(this);
		}
	}
} 