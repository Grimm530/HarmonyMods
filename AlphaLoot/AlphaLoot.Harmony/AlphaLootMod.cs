using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AlphaLoot.Harmony.Patches;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using Arg = ConsoleSystem.Arg;

namespace AlphaLoot.Harmony;

public class AlphaLootMod : IHarmonyModHooks
{
	private const string HELI_CRATE = "heli_crate";

	private const string BRADLEY_CRATE = "bradley_crate";

	private const string UNDERWATER_LABS = "underwater_labs/";

	private AlphaLootConfig _config;

	private StoredData _storedData;

	private StoredData _heliData;

	private StoredData _bradleyData;

	private DeferredSkinBlockInitializer _deferredSkinBlockInitializer;

	private string _dataPath;

	private string _configPath;

	private string _baseDataPath;

	private const string SUPPLY_DROP_PROFILE = "supply_drop";

	public static AlphaLootMod Instance { get; private set; }

	public AlphaLootConfig Config => _config;

	public StoredData StoredData => _storedData;

	public StoredData HeliData => _heliData;

	public StoredData BradleyData => _bradleyData;

	public string BaseDataPath => _baseDataPath;

	public int BradleyCrates => _config?.BradleyCrates ?? (-1);

	public int HelicopterCrates => _config?.HelicopterCrates ?? (-1);

	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Instance = this;
		TryApplyOxideCorpseBridge();
		string path = Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".";
		_baseDataPath = Path.Combine(path, "HarmonyData", "AlphaLoot");
		_configPath = Path.Combine(path, "HarmonyConfig", "AlphaLoot.json");
		if (!Directory.Exists(_baseDataPath))
		{
			Directory.CreateDirectory(_baseDataPath);
		}
		string text = Path.Combine(_baseDataPath, "LootProfiles");
		if (!Directory.Exists(text))
		{
			Directory.CreateDirectory(text);
		}
		_dataPath = text;
		LoadConfig();
		LoadData();
		SetContext();
		AlphaLootConfig config = _config;
		if (config != null && config.AutoUpdate)
		{
			AlphaLootTools.RunAutoUpdater(this);
		}
		RegisterCommands();
		int num = (_storedData?.loot_advanced?.Count).GetValueOrDefault() + (_storedData?.loot_simple?.Count).GetValueOrDefault();
		StoredData storedData = _storedData;
		int num2;
		if (storedData == null || storedData.loot_advanced?.ContainsKey("supply_drop") != true)
		{
			StoredData storedData2 = _storedData;
			num2 = ((storedData2 != null && storedData2.loot_simple?.ContainsKey("supply_drop") == true) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		bool flag = (byte)num2 != 0;
		AlphaLootConfig config2 = _config;
		if (config2 != null && config2.UseApprovedSkins)
		{
			Debug.LogWarning((object)"[AlphaLoot] WARNING! As of August 7th 2025, granting access to paid DLC that users do not own is against Rust's Terms of Service and can result in your server being delisted or worse.\nIf you continue to allow users to use paid DLC skins, you do so at your own risk!\nhttps://facepunch.com/legal/servers");
		}
		Debug.Log((object)string.Format("[AlphaLoot.Harmony] Loaded from {0}. Loot: {1}, Heli: {2}, Bradley: {3}. SupplyDrop: {4}, OverrideFancyDrop: {5}, UseApprovedSkins: {6}", _baseDataPath, num, (_heliData?.loot_advanced?.Count).GetValueOrDefault() + (_heliData?.loot_simple?.Count).GetValueOrDefault(), (_bradleyData?.loot_advanced?.Count).GetValueOrDefault() + (_bradleyData?.loot_simple?.Count).GetValueOrDefault(), flag ? "yes" : "NO - add supply_drop to loot_advanced", _config?.OverrideFancyDrop, _config?.UseApprovedSkins));
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		CancelDeferredSkinBlockInitialization();
		UnregisterCommands();
		AlphaLootContext.Config = null;
		AlphaLootContext.WeightedSkinIds = null;
		AlphaLootContext.ImportedSkinIds = null;
		AlphaLootContext.BlockedWorkshopSkinIds = null;
		Instance = null;
		_storedData = null;
		_heliData = null;
		_bradleyData = null;
		Debug.Log((object)"[AlphaLoot.Harmony] Unloaded.");
	}

	private void TryApplyOxideCorpseBridge()
	{
		try
		{
			var harmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.AlphaLoot.oxide");
			if (Interface_CallHook_OnCorpsePopulate_Patch.TryApply(harmony))
				Debug.Log((object)"[AlphaLoot] Oxide OnCorpsePopulate bridge applied.");
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("[AlphaLoot] Oxide bridge skipped: " + ex.Message));
		}
	}

	private void RegisterCommands()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		if (ConsoleSystem.Index.All == null)
		{
			GameObject val = new GameObject("AlphaLoot_DeferredCommands");
			Object.DontDestroyOnLoad((Object)val);
			val.AddComponent<DeferredCommandRegistrar>().OnReady = DoRegisterCommands;
		}
		else
		{
			DoRegisterCommands();
		}
	}

	private void DoRegisterCommands()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected O, but got Unknown
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Expected O, but got Unknown
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Expected O, but got Unknown
		try
		{
			ConsoleSystem.Command value = new ConsoleSystem.Command
			{
				Name = "additems",
				Parent = "al",
				FullName = "al.additems",
				ServerAdmin = true,
				Variable = false,
				Call = delegate(Arg a)
				{
					if (CanUseCommand(a))
					{
						AlphaLootTools.AddItems(a, ToStringArray(a.Args), Instance);
					}
				}
			};
			ConsoleSystem.Command value2 = new ConsoleSystem.Command
			{
				Name = "search",
				Parent = "al",
				FullName = "al.search",
				ServerAdmin = true,
				Variable = false,
				Call = delegate(Arg a)
				{
					if (CanUseCommand(a))
					{
						AlphaLootTools.SearchItem(a, (a != null && a.Args?.Length > 0) ? a.GetString(0, "") : "", Instance);
					}
				}
			};
			ConsoleSystem.Command value3 = new ConsoleSystem.Command
			{
				Name = "repopulateall",
				Parent = "al",
				FullName = "al.repopulateall",
				ServerAdmin = true,
				Variable = false,
				Call = delegate(Arg a)
				{
					if (CanUseCommand(a))
					{
						RepopulateAllLoot(a);
					}
				}
			};
			ConsoleSystem.Command value4 = new ConsoleSystem.Command
			{
				Name = "skins",
				Parent = "al",
				FullName = "al.skins",
				ServerAdmin = true,
				Variable = false,
				Call = delegate(Arg a)
				{
					if (CanUseCommand(a))
					{
						HandleSkinsCommand(a);
					}
				}
			};
			RegisterServerCommand("al.additems", value);
			RegisterServerCommand("al.search", value2);
			RegisterServerCommand("al.repopulateall", value3);
			RegisterServerCommand("al.skins", value4);
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[AlphaLoot.Harmony] Command registration failed (al.additems, al.search, al.repopulateall, al.skins unavailable): " + ex.Message));
		}
	}

	private void UnregisterCommands()
	{
		try
		{
			UnregisterServerCommand("al.additems");
			UnregisterServerCommand("al.search");
			UnregisterServerCommand("al.repopulateall");
			UnregisterServerCommand("al.skins");
		}
		catch
		{
		}
	}

	private void RepopulateAllLoot(Arg arg)
	{
		LootContainer[] array = Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.None);
		int num = 0;
		for (int i = 0; i < ((array != null) ? array.Length : 0); i++)
		{
			LootContainer lootContainer = array[i];
			if (!((Object)(object)lootContainer == (Object)null) && !lootContainer.IsDestroyed && !(lootContainer is HackableLockedCrate))
			{
				if (lootContainer.inventory == null)
				{
					lootContainer.CreateInventory(giveUID: true);
					lootContainer.OnInventoryFirstCreated(lootContainer.inventory);
				}
				BaseLootProfile.SetContainerCapacity(lootContainer.inventory, lootContainer.inventorySlots);
				((FacepunchBehaviour)lootContainer).CancelInvoke((Action)lootContainer.SpawnLoot);
				((FacepunchBehaviour)lootContainer).Invoke((Action)lootContainer.SpawnLoot, Random.Range(1f, 20f));
				num++;
			}
		}
		string text = $"Queued {num} containers to respawn loot (skipped HackableLockedCrate).";
		if (((arg != null) ? arg.Connection : null) != null)
		{
			arg.ReplyWith(text);
		}
		else
		{
			Debug.Log((object)("[AlphaLoot.Harmony] " + text));
		}
	}

	private void HandleSkinsCommand(Arg arg)
	{
		if (arg?.Args == null || arg.Args.Length == 0)
		{
			Reply(arg, "al.skins clear - Removes all skins from the random skin list, and all skins individually set on items in the loot table");
			return;
		}
		if (!arg.Args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
		{
			Reply(arg, "Only 'al.skins clear' is currently supported by the Harmony AlphaLoot port.");
			return;
		}
		if (!AreSkinDefinitionsReady())
		{
			Reply(arg, "SteamDefinitions have not yet been initialized. Try again later");
			return;
		}
		HashSet<ulong> hashSet = BuildBlockedWorkshopSkinIds();
		AlphaLootContext.WeightedSkinIds?.Clear();
		SaveSkinData();
		ClearSkins(_storedData, hashSet);
		ClearSkins(_heliData, hashSet);
		ClearSkins(_bradleyData, hashSet);
		AlphaLootContext.BlockedWorkshopSkinIds = hashSet;
		AlphaLootTools.SaveData(this);
		Reply(arg, "Removed all skins from the random skin list, and all skins individually set on items in the loot table");
	}

	private static string[] ToStringArray(Facepunch.StringView[] args)
	{
		if (args == null || args.Length == 0) return Array.Empty<string>();

		var result = new string[args.Length];
		for (int i = 0; i < args.Length; i++)
			result[i] = args[i].ToString();
		return result;
	}

	private static string[] ToStringArray(string[] args)
	{
		return args ?? Array.Empty<string>();
	}

	private static void RegisterServerCommand(string fullName, ConsoleSystem.Command command)
	{
		IDictionary dict = GetServerCommandDictionary();
		if (dict == null) return;
		dict[CreateCommandDictionaryKey(dict, fullName)] = command;
	}

	private static void UnregisterServerCommand(string fullName)
	{
		IDictionary dict = GetServerCommandDictionary();
		if (dict == null) return;
		dict.Remove(CreateCommandDictionaryKey(dict, fullName));
	}

	private static IDictionary GetServerCommandDictionary()
	{
		FieldInfo field = typeof(ConsoleSystem.Index.Server).GetField("Dict", BindingFlags.Public | BindingFlags.Static);
		return field?.GetValue(null) as IDictionary;
	}

	private static object CreateCommandDictionaryKey(IDictionary dict, string key)
	{
		Type keyType = dict.GetType().GetGenericArguments().FirstOrDefault() ?? typeof(string);
		if (keyType == typeof(string)) return key;
		return Activator.CreateInstance(keyType, key);
	}

	private static bool CanUseCommand(Arg arg)
	{
		if (arg.Connection == null)
		{
			return true;
		}
		BasePlayer basePlayer = arg.Player();
		if ((Object)(object)basePlayer != (Object)null)
		{
			return basePlayer.IsAdmin;
		}
		return false;
	}

	private void LoadConfig()
	{
		try
		{
			if (File.Exists(_configPath))
			{
				string text = File.ReadAllText(_configPath);
				_config = JsonConvert.DeserializeObject<AlphaLootConfig>(text);
			}
			if (_config != null)
			{
				return;
			}
			_config = new AlphaLootConfig();
			if (_configPath != null)
			{
				string directoryName = Path.GetDirectoryName(_configPath);
				if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				File.WriteAllText(_configPath, JsonConvert.SerializeObject((object)_config, (Formatting)1));
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[AlphaLoot.Harmony] Config load error: " + ex.Message));
			_config = new AlphaLootConfig();
		}
	}

	private void LoadData()
	{
		_storedData = LoadStoredData(_config?.ProfileName ?? "default_loottable");
		_heliData = LoadStoredData(_config?.HeliProfileName ?? "default_heli_loottable");
		_bradleyData = LoadStoredData(_config?.BradleyProfileName ?? "default_bradley_loottable");
		if (_heliData != null)
		{
			StoredData heliData = _heliData;
			if (heliData.loot_advanced == null)
			{
				heliData.loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
			}
			heliData = _heliData;
			if (heliData.loot_simple == null)
			{
				heliData.loot_simple = new Dictionary<string, SimpleLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
			}
		}
		if (_bradleyData != null)
		{
			StoredData heliData = _bradleyData;
			if (heliData.loot_advanced == null)
			{
				heliData.loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
			}
			heliData = _bradleyData;
			if (heliData.loot_simple == null)
			{
				heliData.loot_simple = new Dictionary<string, SimpleLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
			}
		}
		bool num = (_storedData != null && !_storedData.HasAnyProfiles) || (_heliData != null && !_heliData.HasAnyProfiles) || (_bradleyData != null && !_bradleyData.HasAnyProfiles);
		bool flag = _config?.AutoUpdate ?? false;
		if (num || flag)
		{
			AlphaLootVanillaGenerator.PopulateContainerDefinitions(ref _storedData, ref _heliData, ref _bradleyData);
			AlphaLootTools.SaveData(this);
		}
	}

	private StoredData LoadStoredData(string profileName)
	{
		string text = Path.Combine(_dataPath, profileName + ".json");
		try
		{
			if (File.Exists(text))
			{
				return JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(text));
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[AlphaLoot.Harmony] Failed to load " + text + ": " + ex.Message));
		}
		return new StoredData();
	}

	private void SetContext()
	{
		AlphaLootContext.Config = _config;
		AlphaLootContext.WeightedSkinIds = LoadSkinData();
		AlphaLootContext.ImportedSkinIds = new Dictionary<string, List<ulong>>(StringComparer.OrdinalIgnoreCase);
		AlphaLootConfig config = _config;
		if (config == null || !config.UseApprovedSkins)
		{
			AlphaLootContext.BlockedWorkshopSkinIds = new HashSet<ulong>();
			InitializeBlockedWorkshopSkinIdsWhenReady();
		}
		else
		{
			AlphaLootContext.BlockedWorkshopSkinIds = null;
		}
	}

	private void InitializeBlockedWorkshopSkinIdsWhenReady()
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		CancelDeferredSkinBlockInitialization();
		if (AreSkinDefinitionsReady())
		{
			AlphaLootContext.BlockedWorkshopSkinIds = BuildBlockedWorkshopSkinIds();
			return;
		}
		Debug.Log((object)"[AlphaLoot.Harmony] Waiting for item skin and Steam inventory definitions before building DLC skin block list...");
		GameObject val = new GameObject("AlphaLoot_DeferredSkinBlockInit");
		Object.DontDestroyOnLoad((Object)(object)val);
		_deferredSkinBlockInitializer = val.AddComponent<DeferredSkinBlockInitializer>();
		_deferredSkinBlockInitializer.OnReady = delegate
		{
			AlphaLootContext.BlockedWorkshopSkinIds = BuildBlockedWorkshopSkinIds();
			_deferredSkinBlockInitializer = null;
		};
	}

	private void CancelDeferredSkinBlockInitialization()
	{
		if (!((Object)(object)_deferredSkinBlockInitializer == (Object)null))
		{
			GameObject gameObject = ((Component)_deferredSkinBlockInitializer).gameObject;
			_deferredSkinBlockInitializer.OnReady = null;
			_deferredSkinBlockInitializer = null;
			if ((Object)(object)gameObject != (Object)null)
			{
				Object.Destroy((Object)(object)gameObject);
			}
		}
	}

	internal static bool AreSkinDefinitionsReady()
	{
		if (TryGetItemSkinDirectorySkins(out _))
		{
			return GetSteamInventoryDefinitionCount() > 0;
		}
		return false;
	}

	private static bool TryGetItemSkinDirectorySkins(out ItemSkinDirectory.Skin[] skins, bool logWarning = false)
	{
		skins = null;
		try
		{
			skins = ItemSkinDirectory.Instance?.skins;
			return skins != null && skins.Length != 0;
		}
		catch (Exception ex)
		{
			if (logWarning)
			{
				Debug.LogWarning((object)("[AlphaLoot.Harmony] Item skin directory is not ready: " + ex.Message));
			}
			return false;
		}
	}

	private static int GetSteamInventoryDefinitionCount()
	{
		return GetSteamInventoryDefinitions()?.Length ?? 0;
	}

	private static Array GetSteamInventoryDefinitions()
	{
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				Type type = assemblies[i].GetType("Steamworks.SteamInventory");
				if (!(type == null))
				{
					return type.GetProperty("Definitions", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null) as Array;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("[AlphaLoot.Harmony] Failed to inspect Steam inventory definitions: " + ex.Message));
		}
		return null;
	}

	internal static HashSet<ulong> BuildBlockedWorkshopSkinIds()
	{
		HashSet<ulong> hashSet = new HashSet<ulong>();
		try
		{
			if (!TryGetItemSkinDirectorySkins(out ItemSkinDirectory.Skin[] array, logWarning: true))
			{
				return hashSet;
			}
			for (int i = 0; i < array.Length; i++)
			{
				try
				{
					ItemSkinDirectory.Skin skin = array[i];
					if (skin.invItem is ItemSkin { UnlockedByDefault: false, workshopID: not 0uL } itemSkin)
					{
						hashSet.Add(itemSkin.workshopID);
						hashSet.Add((ulong)skin.id);
					}
				}
				catch (Exception ex)
				{
					Debug.LogWarning((object)$"[AlphaLoot.Harmony] Skipping invalid skin entry at index {i}: {ex.Message}");
				}
			}
			Array steamInventoryDefinitions = GetSteamInventoryDefinitions();
			if (steamInventoryDefinitions != null)
			{
				foreach (object item in steamInventoryDefinitions)
				{
					if (item != null)
					{
						ulong result = 0uL;
						MethodInfo methodInfo = item.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo method) => method.Name == "GetProperty" && !method.IsGenericMethod && method.ReturnType == typeof(string) && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(string));
						PropertyInfo property = item.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
						if (!ulong.TryParse(methodInfo?.Invoke(item, new object[1] { "workshopid" }) as string, out result) && property?.GetValue(item, null) is int num && num > 0)
						{
							result = (ulong)num;
						}
						if (result != 0L)
						{
							hashSet.Add(result);
						}
					}
				}
			}
			Debug.Log((object)$"[AlphaLoot.Harmony] DLC skin block list: {hashSet.Count} paid skins (UseApprovedSkins=false, TOS compliant)");
		}
		catch (Exception ex2)
		{
			Debug.LogWarning((object)("[AlphaLoot.Harmony] Could not build DLC skin block list: " + ex2.Message));
		}
		return hashSet;
	}

	private Dictionary<string, HashSet<SkinEntry>> LoadSkinData()
	{
		string path = Path.Combine(_baseDataPath, "item_skin_ids.json");
		try
		{
			if (File.Exists(path))
			{
				return JsonConvert.DeserializeObject<Dictionary<string, HashSet<SkinEntry>>>(File.ReadAllText(path)) ?? new Dictionary<string, HashSet<SkinEntry>>(StringComparer.OrdinalIgnoreCase);
			}
		}
		catch
		{
		}
		return new Dictionary<string, HashSet<SkinEntry>>(StringComparer.OrdinalIgnoreCase);
	}

	private void SaveSkinData()
	{
		try
		{
			File.WriteAllText(Path.Combine(_baseDataPath, "item_skin_ids.json"), JsonConvert.SerializeObject((object)(AlphaLootContext.WeightedSkinIds ?? new Dictionary<string, HashSet<SkinEntry>>(StringComparer.OrdinalIgnoreCase)), (Formatting)1));
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[AlphaLoot.Harmony] Failed to save skin data: " + ex.Message));
		}
	}

	public static string ToProfileName(LootContainer container)
	{
		if ((Object)(object)container == (Object)null)
		{
			return "";
		}
		if (container is SupplyDrop)
		{
			return "supply_drop";
		}
		string prefabName = container.PrefabName;
		if (prefabName != null && prefabName.Contains("underwater_labs/"))
		{
			return "underwater_labs/" + container.ShortPrefabName;
		}
		return container.ShortPrefabName ?? "";
	}

	public static string ToLootFillProfileName(BaseEntity entity, StorageContainer container)
	{
		if ((Object)(object)entity == (Object)null || (Object)(object)container == (Object)null)
		{
			return string.Empty;
		}
		return entity.ShortPrefabName + "|" + container.ShortPrefabName;
	}

	public bool TryGetContainerOverride(string profileName, out string overrideProfile)
	{
		overrideProfile = null;
		if (_config != null)
		{
			return _config.TryGetContainerOverride(profileName, out overrideProfile);
		}
		return false;
	}

	public bool TryGetLootProfile(string profileName, out BaseLootContainerProfile profile)
	{
		profile = null;
		if (TryGetContainerOverride(profileName, out var overrideProfile))
		{
			profileName = overrideProfile;
		}
		if (profileName == "heli_crate" && _heliData != null && _heliData.GetRandomLootProfile(out profile))
		{
			return true;
		}
		if (profileName == "bradley_crate" && _bradleyData != null && _bradleyData.GetRandomLootProfile(out profile))
		{
			return true;
		}
		if (_storedData != null && _storedData.TryGetLootProfile(profileName, out profile))
		{
			return true;
		}
		string text = NormalizeProfileNameForLookup(profileName);
		if (!string.IsNullOrEmpty(text) && text != profileName && _storedData.TryGetLootProfile(text, out profile))
		{
			return true;
		}
		return false;
	}

	private static string NormalizeProfileNameForLookup(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return name;
		}
		return name.Replace('_', '-');
	}

	private static string NormalizeRedirectedShortname(string shortname)
	{
		if (string.IsNullOrEmpty(shortname))
		{
			return shortname;
		}
		ItemDefinition itemDefinition = ItemManager.FindItemDefinition(shortname);
		if ((Object)(object)itemDefinition?.isRedirectOf != (Object)null)
		{
			return itemDefinition.isRedirectOf.shortname;
		}
		return shortname;
	}

	private static bool IsDlcItemShortname(string shortname)
	{
		ItemDefinition itemDefinition = ItemManager.FindItemDefinition(shortname);
		if ((Object)(object)itemDefinition != (Object)null)
		{
			if (!((Object)(object)itemDefinition.steamItem != (Object)null))
			{
				if ((Object)(object)itemDefinition.steamDlc != (Object)null)
				{
					return !itemDefinition.steamDlc.bypassLicenseCheck;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static void ClearSkins(StoredData data, HashSet<ulong> blockedSkinIds)
	{
		if (data == null)
		{
			return;
		}
		foreach (AdvancedCustomLootProfile value in data.custom_advanced.Values)
		{
			LootSpawnSlot[] array = value?.LootSpawnSlots ?? Array.Empty<LootSpawnSlot>();
			for (int i = 0; i < array.Length; i++)
			{
				ClearSkinsRecursive(array[i]?.LootDefinition, blockedSkinIds);
			}
		}
		foreach (AdvancedLootContainerProfile value2 in data.loot_advanced.Values)
		{
			LootSpawnSlot[] array = value2?.LootSpawnSlots ?? Array.Empty<LootSpawnSlot>();
			for (int i = 0; i < array.Length; i++)
			{
				ClearSkinsRecursive(array[i]?.LootDefinition, blockedSkinIds);
			}
		}
		foreach (AdvancedNPCLootProfile value3 in data.npcs_advanced.Values)
		{
			LootSpawnSlot[] array = value3?.LootSpawnSlots ?? Array.Empty<LootSpawnSlot>();
			for (int i = 0; i < array.Length; i++)
			{
				ClearSkinsRecursive(array[i]?.LootDefinition, blockedSkinIds);
			}
		}
		foreach (SimpleCustomLootProfile lootProfile in data.custom_simple.Values)
		{
			ClearSimpleProfileSkins(lootProfile?.Items, delegate(ItemAmountSpawnsWith[] items)
			{
				lootProfile.Items = items;
			}, blockedSkinIds);
		}
		foreach (SimpleLootContainerProfile lootProfile2 in data.loot_simple.Values)
		{
			ClearSimpleProfileSkins(lootProfile2?.Items, delegate(ItemAmountSpawnsWith[] items)
			{
				lootProfile2.Items = items;
			}, blockedSkinIds);
		}
		foreach (SimpleNPCLootProfile lootProfile3 in data.npcs_simple.Values)
		{
			ClearSimpleProfileSkins(lootProfile3?.Items, delegate(ItemAmountSpawnsWith[] items)
			{
				lootProfile3.Items = items;
			}, blockedSkinIds);
		}
	}

	private static void ClearSkinsRecursive(LootSpawn lootSpawn, HashSet<ulong> blockedSkinIds)
	{
		if (lootSpawn == null)
		{
			return;
		}
		if (lootSpawn.SubSpawn != null)
		{
			LootSpawn.Entry[] subSpawn = lootSpawn.SubSpawn;
			for (int i = 0; i < subSpawn.Length; i++)
			{
				ClearSkinsRecursive(subSpawn[i]?.Category, blockedSkinIds);
			}
		}
		if (lootSpawn.Items == null || lootSpawn.Items.Length == 0)
		{
			return;
		}
		List<ItemAmountRanged> list = new List<ItemAmountRanged>(lootSpawn.Items.Length);
		ItemAmountRanged[] items = lootSpawn.Items;
		foreach (ItemAmountRanged itemAmountRanged in items)
		{
			if (itemAmountRanged == null)
			{
				continue;
			}
			itemAmountRanged.Shortname = NormalizeRedirectedShortname(itemAmountRanged.Shortname);
			if (!IsDlcItemShortname(itemAmountRanged.Shortname))
			{
				if (blockedSkinIds.Contains(itemAmountRanged.SkinID))
				{
					itemAmountRanged.SkinID = 0uL;
				}
				list.Add(itemAmountRanged);
			}
		}
		lootSpawn.Items = list.ToArray();
	}

	private static void ClearSimpleProfileSkins(ItemAmountSpawnsWith[] items, Action<ItemAmountSpawnsWith[]> assign, HashSet<ulong> blockedSkinIds)
	{
		if (assign == null)
		{
			return;
		}
		if (items == null || items.Length == 0)
		{
			assign(Array.Empty<ItemAmountSpawnsWith>());
			return;
		}
		List<ItemAmountSpawnsWith> list = new List<ItemAmountSpawnsWith>(items.Length);
		foreach (ItemAmountSpawnsWith itemAmountSpawnsWith in items)
		{
			if (itemAmountSpawnsWith == null)
			{
				continue;
			}
			itemAmountSpawnsWith.Shortname = NormalizeRedirectedShortname(itemAmountSpawnsWith.Shortname);
			if (!IsDlcItemShortname(itemAmountSpawnsWith.Shortname))
			{
				if (blockedSkinIds.Contains(itemAmountSpawnsWith.SkinID))
				{
					itemAmountSpawnsWith.SkinID = 0uL;
				}
				list.Add(itemAmountSpawnsWith);
			}
		}
		assign(list.ToArray());
	}

	private static void Reply(Arg arg, string message)
	{
		if (((arg != null) ? arg.Connection : null) != null)
		{
			arg.ReplyWith(message);
		}
		else
		{
			Debug.Log((object)("[AlphaLoot.Harmony] " + message));
		}
	}

	public bool TryGetUnwrapProfile(string itemShortname, out BaseLootContainerProfile profile)
	{
		profile = null;
		if (_storedData != null)
		{
			return _storedData.TryGetLootProfile(itemShortname, out profile);
		}
		return false;
	}

	public bool TryGetNPCProfile(string profileName, out BaseLootProfile profile)
	{
		profile = null;
		if (TryGetContainerOverride(profileName, out var overrideProfile))
		{
			profileName = overrideProfile;
		}
		if (_storedData != null)
		{
			return _storedData.TryGetNPCProfile(profileName, out profile);
		}
		return false;
	}

	public void PopulateCorpseLoot(BaseEntity entity, LootableCorpse corpse)
	{
		if (!((Object)(object)entity == (Object)null) && corpse?.containers != null && corpse.containers.Length != 0)
		{
			string text = entity.ShortPrefabName;
			if (TryGetContainerOverride(text, out var overrideProfile))
			{
				text = overrideProfile;
			}
			if (TryGetNPCProfile(text, out var profile) && profile.Enabled)
			{
				string loadoutName = ((entity is HumanNPC humanNPC) ? humanNPC.GetLoadoutName() : "");
				profile.PopulateLoot(corpse.containers[0], loadoutName);
				float globalMult = _config?.GlobalMultiplier ?? 1f;
				AlphaLootTools.LogLootIfDebug(corpse.containers[0], "corpse npc=" + text, text, globalMult, profile.LootMultiplier);
			}
		}
	}

	public void PopulateLootContainer(LootContainer container, BaseLootContainerProfile lootProfile)
	{
		if (!((Object)(object)container == (Object)null) && lootProfile != null)
		{
			container.destroyOnEmpty = lootProfile.DestroyOnEmpty;
			lootProfile.PopulateLoot(container.inventory);
			((FacepunchBehaviour)container).CancelInvoke((Action)container.SpawnLoot);
			if (lootProfile.ShouldRefreshContents)
			{
				float num = Mathf.Max(60, Random.Range(lootProfile.MinSecondsBetweenRefresh, lootProfile.MaxSecondsBetweenRefresh));
				((FacepunchBehaviour)container).Invoke((Action)container.SpawnLoot, num);
			}
		}
	}
}
