using System;
using GrimmCuiHarmony;
using System.Collections;
using GrimmCuiHarmony;
using System.Collections.Generic;
using GrimmCuiHarmony;
using System.IO;
using GrimmCuiHarmony;
using System.Text;
using GrimmCuiHarmony;
using ConVar;
using GrimmCuiHarmony;
using HarmonyLib;
using GrimmCuiHarmony;
using Newtonsoft.Json;
using GrimmCuiHarmony;
using UnityEngine;
using GrimmCuiHarmony;
using Leaderboard.Storage;
using GrimmCuiHarmony;
using Leaderboard.Relay;
using GrimmCuiHarmony;
using Leaderboard.Discord;
using GrimmCuiHarmony;
using Leaderboard.Patches;
using GrimmCuiHarmony;

namespace Leaderboard;

public class LeaderboardMod : IHarmonyModHooks
{
    public static LeaderboardMod Instance { get; private set; }

    public const string AppDomainApiKey = "Leaderboard_ApiType";
    public const string AppDomainPluginKey = "Leaderboard_Plugin";
    public const string AppDomainPluginAliasKey = "UltimateLeaderboard_Plugin";

    private readonly Dictionary<ulong, PlayerStats> _playerStats = new();
    private readonly Dictionary<ulong, float> _lastCommandTime = new();
    private readonly object _statsLock = new();

    private ILeaderboardStorage _storage;
    private LeaderboardConfig _config;
    private string _configPath;
    private HarmonyLib.Harmony _harmony;
    private float _relayBatchTimer;
    private readonly List<StatUpdatePayload> _relayBatch = new();
    private readonly Dictionary<ulong, int> _openLeaderboardCategory = new();
    private readonly Dictionary<ulong, int> _openLeaderboardProfileTab = new();
    private readonly Dictionary<ulong, int> _openLeaderboardTop10Tab = new();
    /// <summary>When set, My Statistics shows this player's profile instead of the viewer's own (e.g. after clicking a Search card).</summary>
    private readonly Dictionary<ulong, ulong> _viewedProfileUserId = new();
    private readonly HashSet<ulong> _serverPanelPlayers = new();
    private PluginWrapper _pluginWrapper;
    private readonly object _uiLock = new();
    private GameObject _tickObject;
    private float _discordTimer;
    private const string UiLayer = "UI.Leaderboard";

    private static readonly Dictionary<string, string> _localImageIds = new();
    private static readonly object _localImageLock = new();

    public int GetLeaderboardCategory(ulong userId)
    {
        lock (_uiLock)
            return _openLeaderboardCategory.TryGetValue(userId, out var c) ? c : 0;
    }

    public void SetLeaderboardCategory(ulong userId, int categoryIndex)
    {
        lock (_uiLock)
            _openLeaderboardCategory[userId] = Math.Max(0, Math.Min(2, categoryIndex));
    }

    public void OnLeaderboardClosed(ulong userId)
    {
        lock (_uiLock)
        {
            _openLeaderboardCategory.Remove(userId);
            _openLeaderboardProfileTab.Remove(userId);
            _openLeaderboardTop10Tab.Remove(userId);
            _viewedProfileUserId.Remove(userId);
        }
    }

    /// <summary>Whose stats to show in My Statistics: returns viewed target or the viewer's own id.</summary>
    public ulong GetViewedProfileTarget(ulong viewerUserId)
    {
        lock (_uiLock)
            return _viewedProfileUserId.TryGetValue(viewerUserId, out var target) ? target : viewerUserId;
    }

    public void SetViewedProfile(ulong viewerUserId, ulong targetUserId)
    {
        lock (_uiLock)
            _viewedProfileUserId[viewerUserId] = targetUserId;
    }

    public void ClearViewedProfile(ulong viewerUserId)
    {
        lock (_uiLock)
            _viewedProfileUserId.Remove(viewerUserId);
    }

    public int GetLeaderboardProfileTab(ulong userId)
    {
        lock (_uiLock)
            return _openLeaderboardProfileTab.TryGetValue(userId, out var t) ? t : 0;
    }

    public void SetLeaderboardProfileTab(ulong userId, int tabIndex)
    {
        lock (_uiLock)
            _openLeaderboardProfileTab[userId] = Math.Max(0, Math.Min(3, tabIndex));
    }

    public int GetLeaderboardTop10Tab(ulong userId)
    {
        lock (_uiLock)
            return _openLeaderboardTop10Tab.TryGetValue(userId, out var t) ? t : 0;
    }

    public void SetLeaderboardTop10Tab(ulong userId, int tabIndex)
    {
        lock (_uiLock)
            _openLeaderboardTop10Tab[userId] = Math.Max(0, Math.Min(3, tabIndex));
    }

    /// <summary>Top 10 by kills for UI: userId, name, kills, deaths, animalKills, npcKills.</summary>
    public List<(ulong userId, string name, int kills, int deaths, int animalKills, int npcKills)> GetTop10Killers()
    {
        var list = new List<(ulong, string, int, int, int, int)>();
        lock (_statsLock)
        {
            foreach (var kv in _playerStats)
            {
                if (kv.Value.HiddenFromLeaderboard) continue;
                list.Add((kv.Key, kv.Value.LastName ?? kv.Key.ToString(), kv.Value.GetKills(), kv.Value.GetDeaths(), kv.Value.GetAnimalKills(), kv.Value.GetNpcKills()));
            }
        }
        list.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        var result = new List<(ulong, string, int, int, int, int)>();
        for (int i = 0; i < Math.Min(10, list.Count); i++)
            result.Add(list[i]);
        return result;
    }

    /// <summary>Top 10 by construction (raiders): userId, name, foundation, walls, floors, doors, toolCupboards.</summary>
    public List<(ulong userId, string name, int foundation, int walls, int floors, int doors, int toolCupboards)> GetTop10Raiders()
    {
        var list = new List<(ulong, string, int, int, int, int, int)>();
        lock (_statsLock)
        {
            foreach (var kv in _playerStats)
            {
                if (kv.Value.HiddenFromLeaderboard) continue;
                var s = kv.Value;
                list.Add((kv.Key, s.LastName ?? kv.Key.ToString(), s.GetFoundations(), s.GetWalls(), s.GetFloors(), s.GetDoors(), s.GetToolCupboards()));
            }
        }
        list.Sort((a, b) => (b.Item3 + b.Item4 + b.Item5 + b.Item6 + b.Item7).CompareTo(a.Item3 + a.Item4 + a.Item5 + a.Item6 + a.Item7));
        var result = new List<(ulong, string, int, int, int, int, int)>();
        for (int i = 0; i < Math.Min(10, list.Count); i++)
            result.Add(list[i]);
        return result;
    }

    private static readonly (LootType type, string key)[] FarmersResourcesKeys =
    {
        (LootType.Gather, "stones"), (LootType.Gather, "wood"), (LootType.Gather, "sulfur.ore"), (LootType.Gather, "metal.ore"),
        (LootType.Gather, "hq.metal.ore"), (LootType.Gather, "leather"), (LootType.Gather, "bone.fragments"), (LootType.Gather, "fat.animal"),
        (LootType.LootItems, "scrap")
    };
    private static readonly (LootType type, string key)[] FarmersHarvestedKeys =
    {
        (LootType.Gather, "hemp-collectable"), (LootType.Gather, "blue.berry"), (LootType.Gather, "red.berry"), (LootType.Gather, "yellow.berry"),
        (LootType.Gather, "black.berry"), (LootType.Gather, "green.berry"), (LootType.Gather, "white.berry"), (LootType.Gather, "potato"),
        (LootType.Gather, "cloth"), (LootType.Gather, "mushroom"), (LootType.Gather, "corn"), (LootType.Gather, "pumpkin"),
        (LootType.Gather, "orchid"), (LootType.Gather, "rose"), (LootType.Gather, "sunflower"), (LootType.Gather, "wheat")
    };
    private static readonly (LootType type, string key)[] FarmersMiscKeys =
    {
        (LootType.Kill, "bear"), (LootType.Kill, "polarbear"), (LootType.Kill, "boar"), (LootType.Kill, "chicken"),
        (LootType.Kill, "stag"), (LootType.Kill, "wolf2"), (LootType.Kill, "panther"), (LootType.Kill, "crocodile"),
        (LootType.Kill, "snake.entity"), (LootType.Kill, "tiger"), (LootType.Kill, "simpleshark"), (LootType.Kill, "bradleyapc"),
        (LootType.Kill, "helicopter")
    };

    /// <summary>Top 10 by farming/resource totals: userId, name, resources, harvested, misc, recycled, fishing.</summary>
    public List<(ulong userId, string name, float resources, float harvested, float misc, float recycled, float fishing)> GetTop10Farmers()
    {
        var list = new List<(ulong, string, float, float, float, float, float)>();
        lock (_statsLock)
        {
            foreach (var kv in _playerStats)
            {
                if (kv.Value.HiddenFromLeaderboard) continue;
                var s = kv.Value;
                float res = s.GetSumForEntries(FarmersResourcesKeys);
                float farm = s.GetSumForEntries(FarmersHarvestedKeys);
                float misc = s.GetSumForEntries(FarmersMiscKeys);
                float recycled = s.GetTotal(LootType.RecycleItem);
                float fishing = s.GetTotal(LootType.Fishing);
                list.Add((kv.Key, s.LastName ?? kv.Key.ToString(), res, farm, misc, recycled, fishing));
            }
        }
        list.Sort((a, b) => (b.Item3 + b.Item4 + b.Item5 + b.Item6 + b.Item7).CompareTo(a.Item3 + a.Item4 + a.Item5 + a.Item6 + a.Item7));
        var result = new List<(ulong, string, float, float, float, float, float)>();
        for (int i = 0; i < Math.Min(10, list.Count); i++)
            result.Add(list[i]);
        return result;
    }

    /// <summary>Top 10 players for display (name, kills, deaths, kdr, points).</summary>
    public List<(string name, int kills, int deaths, float kdr, float points)> GetTop10ForUI()
    {
        var list = new List<(string, int, int, float, float)>();
        lock (_statsLock)
        {
            foreach (var kv in _playerStats)
            {
                if (kv.Value.HiddenFromLeaderboard) continue;
                var k = kv.Value.GetKills();
                var d = kv.Value.GetDeaths();
                var kdr = d > 0 ? (float)Math.Round((double)k / d, 2) : k;
                list.Add((kv.Value.LastName ?? kv.Key.ToString(), k, d, kdr, kv.Value.Points));
            }
        }
        list.Sort((a, b) => b.Item5.CompareTo(a.Item5));
        var result = new List<(string, int, int, float, float)>();
        for (int i = 0; i < Math.Min(10, list.Count); i++)
            result.Add(list[i]);
        return result;
    }

    /// <summary>Top 10 by total play time (including current session): userId, name, totalSeconds.</summary>
    /// <param name="viewer">If set, this player is always included with their current play time (so they see their time even when not in top 10).</param>
    public List<(ulong userId, string name, double totalSeconds)> GetTop10ByPlayTime(BasePlayer viewer = null)
    {
        var list = new List<(ulong, string, double)>();
        lock (_statsLock)
        {
            foreach (var kv in _playerStats)
            {
                if (kv.Value.HiddenFromLeaderboard) continue;
                double sec = kv.Value.GetTotalPlayTimeIncludingCurrent();
                list.Add((kv.Key, kv.Value.LastName ?? kv.Key.ToString(), sec));
            }
        }
        list.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        var result = new List<(ulong, string, double)>();
        for (int i = 0; i < Math.Min(10, list.Count); i++)
            result.Add(list[i]);

        // Ensure viewing player sees their total play time: if not in top 10, add them with same source as profile
        if (viewer != null)
        {
            bool inList = false;
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Item1 == viewer.userID) { inList = true; break; }
            }
            if (!inList && TryGetStats(viewer.userID, out var stats))
            {
                double sec = stats.GetTotalPlayTimeIncludingCurrent();
                result.Add((viewer.userID, viewer.displayName ?? viewer.userID.ToString(), sec));
            }
        }
        return result;
    }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        GrimmCui.RegisterReadyCallback(GrimmCuiRegistration.Register);
        Instance = this;
        _pluginWrapper = new PluginWrapper(this);
        RegisterApiType();
        GrimmCoreHurtRegistration.Register();
        _harmony = new HarmonyLib.Harmony("com.leaderboard.patches");
        _harmony.PatchAll(typeof(LeaderboardMod).Assembly);

        _configPath = Path.Combine(Environment.CurrentDirectory, "HarmonyConfig", "Leaderboard.json");
        LoadConfig();
        InitStorage();

        _tickObject = new GameObject("LeaderboardTick");
        UnityEngine.Object.DontDestroyOnLoad(_tickObject);
        _tickObject.AddComponent<LeaderboardTickBehaviour>();

        RegisterCommands();
        StartLocalImagesLoadCoroutine();
        UnityEngine.Debug.Log("[Leaderboard] Loaded. Commands: /leaderboard, /lb, /stats. ServerPanel: Leaderboard_Plugin / UltimateLeaderboard_Plugin");
    }

    private void NextTick(Action action)
    {
        if (action == null) return;
        MonoBehaviour runner = _tickObject != null ? _tickObject.GetComponent<LeaderboardTickBehaviour>() : null;
        if (runner == null) runner = ServerMgr.Instance as MonoBehaviour;
        if (runner != null) runner.StartCoroutine(NextTickCoroutine(action));
    }

    private static IEnumerator NextTickCoroutine(Action action)
    {
        yield return null;
        try { action?.Invoke(); } catch (Exception ex) { UnityEngine.Debug.LogWarning($"[Leaderboard] NextTick: {ex.Message}"); }
    }

    /// <summary>Delay before loading HarmonyImages/Leaderboard PNGs into FileStorage so CUI can process them (server boot readiness).</summary>
    private const float LocalImagesDelaySeconds = 10f;

    private void StartLocalImagesLoadCoroutine()
    {
        var runner = _tickObject != null ? _tickObject.GetComponent<LeaderboardTickBehaviour>() : null;
        if (runner == null) return;
        runner.StartCoroutine(DelayedLoadLocalImages());
    }

    private static IEnumerator DelayedLoadLocalImages()
    {
        yield return new WaitForSeconds(LocalImagesDelaySeconds);
        LoadLocalImagesToFileStorage();
    }

    private static void LoadLocalImagesToFileStorage()
    {
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;

        var imagesDir = Path.Combine(Environment.CurrentDirectory, "HarmonyImages", "Leaderboard");
        if (!Directory.Exists(imagesDir)) return;

        var iconFiles = new List<string>();
        try
        {
            var paths = Directory.GetFiles(imagesDir, "*.png", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < paths.Length; i++)
            {
                var name = Path.GetFileName(paths[i]);
                if (name != null && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    iconFiles.Add(name);
            }
        }
        catch { }

        lock (_localImageLock)
        {
            _localImageIds.Clear();
            for (int i = 0; i < iconFiles.Count; i++)
            {
                var fileName = iconFiles[i];
                var path = Path.Combine(imagesDir, fileName);
                if (!File.Exists(path)) continue;
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var crc = FileStorage.server.Store(bytes, FileStorage.Type.png, ce.net.ID);
                    _localImageIds[fileName] = crc.ToString();
                }
                catch { }
            }
        }
        if (_localImageIds.Count > 0)
            UnityEngine.Debug.Log($"[Leaderboard] Loaded {_localImageIds.Count} local images from HarmonyImages/Leaderboard/");
    }

    /// <summary>Returns FileStorage texture ID (uint as string) for CUI RawImage "png", or null if not loaded.</summary>
    public string GetImageId(string iconFileName)
    {
        if (string.IsNullOrEmpty(iconFileName)) return null;
        lock (_localImageLock) return _localImageIds.TryGetValue(iconFileName, out var id) ? id : null;
    }

    public void OnWorldLoaded()
    {
        TryWipeFromMapVoterSignal();
        NextTick(() => EventModPatches.TryApply(_harmony));
    }

    private void TryWipeFromMapVoterSignal()
    {
        var statePath = Path.Combine(Environment.CurrentDirectory, "HarmonyData", "Leaderboard", "last_wipe_signal.txt");
        if (!WipeSignal.ShouldWipe(statePath)) return;

        lock (_statsLock)
            _playerStats.Clear();

        _storage?.Wipe();
        WipeSignal.MarkWiped(statePath);
        UnityEngine.Debug.Log("[Leaderboard] Wipe signal applied — stats cleared.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        if (_tickObject != null) { UnityEngine.Object.Destroy(_tickObject); _tickObject = null; }
        _storage?.SaveAll(true);
        FlushRelayBatch();
        _harmony?.UnpatchAll("com.leaderboard.patches");
        UnregisterCommands();
        GrimmCoreHurtRegistration.Unregister();
        UnregisterApiType();
        _pluginWrapper = null;
        lock (_uiLock) _serverPanelPlayers.Clear();
        Instance = null;
        UnityEngine.Debug.Log("[Leaderboard] Unloaded.");
    }

    private void LoadConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonConvert.DeserializeObject<LeaderboardConfig>(json);
            }
            _config ??= new LeaderboardConfig();
            try { File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config, Formatting.Indented)); } catch { }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Config: {ex.Message}");
            _config = new LeaderboardConfig();
        }
    }

    private void InitStorage()
    {
        var folder = Path.Combine(Environment.CurrentDirectory, _config.DataFolder ?? "LeaderboardData");
        _storage = new JsonLeaderboardStorage(folder);
    }

    private void RegisterCommands()
    {
        try
        {
            var closeCmd = new ConsoleSystem.Command
            {
                Name = "LEADERBOARD_CLOSE",
                FullName = "global.leaderboard.close",
                Variable = true,
                ServerAdmin = false,
                Replicated = true,
                Call = (arg) =>
                {
                    var p = arg.Player();
                    if (p != null)
                    {
                        OnLeaderboardClosed(p.userID);
                        LeaderboardUI.Destroy(p);
                    }
                }
            };
            ConsoleSystem.Index.Server.Dict["global.leaderboard.close"] = closeCmd;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["leaderboard.close"] = closeCmd;
        }
        catch (Exception ex) { UnityEngine.Debug.LogWarning($"[Leaderboard] Register close: {ex.Message}"); }

        foreach (var cmd in _config?.Commands ?? new[] { "leaderboard", "lb", "stats" })
        {
            if (string.IsNullOrWhiteSpace(cmd)) continue;
            var c = cmd.Trim().ToLowerInvariant();
            ConsoleSystem.Command existing;
            if (ConsoleSystem.Index.Server.Dict != null && ConsoleSystem.Index.Server.Dict.TryGetValue("global." + c, out existing))
                continue;
            var command = new ConsoleSystem.Command
            {
                Name = c.ToUpperInvariant() + "_LB",
                FullName = "global." + c,
                Variable = true,
                ServerAdmin = false,
                Replicated = true,
                Call = (arg) =>
                {
                    var player = arg.Player();
                    if (player != null) OpenLeaderboard(player);
                }
            };
            try
            {
                ConsoleSystem.Index.Server.Dict["global." + c] = command;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[c] = command;
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning($"[Leaderboard] Register {c}: {ex.Message}"); }
        }
    }

    private void UnregisterCommands()
    {
        try
        {
            if (ConsoleSystem.Index.Server.Dict != null)
                ConsoleSystem.Index.Server.Dict.Remove("global.leaderboard.close");
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict.Remove("leaderboard.close");
        }
        catch { }

        foreach (var cmd in _config?.Commands ?? new[] { "leaderboard", "lb", "stats" })
        {
            var c = cmd?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(c)) continue;
            try
            {
                if (ConsoleSystem.Index.Server.Dict != null)
                    ConsoleSystem.Index.Server.Dict.Remove("global." + c);
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict.Remove(c);
            }
            catch { }
        }
    }

    public void OpenLeaderboard(BasePlayer player)
    {
        if (player == null) return;
        if (IsRateLimited(player.userID)) return;
        ExitServerPanelMode(player);
        EnsurePlayerLoaded(player.userID, player.displayName, () =>
        {
            SetLeaderboardCategory(player.userID, 0);
            SetLeaderboardProfileTab(player.userID, 0);
            LeaderboardUI.Show(player);
        });
    }

    #region ServerPanel

    public bool IsServerPanelMode(BasePlayer player) =>
        player != null && IsServerPanelMode(player.userID);

    public bool IsServerPanelMode(ulong userId)
    {
        lock (_uiLock) return _serverPanelPlayers.Contains(userId);
    }

    public void EnterServerPanelMode(BasePlayer player)
    {
        if (player == null) return;
        lock (_uiLock) _serverPanelPlayers.Add(player.userID);
    }

    public void ExitServerPanelMode(BasePlayer player)
    {
        if (player == null) return;
        lock (_uiLock) _serverPanelPlayers.Remove(player.userID);
    }

    /// <summary>
    /// ServerPanel Plugin page entry. Returns CUI JSON (no outer brackets) for
    /// UI.Server.Panel.Content → UI.Server.Panel.Content.Plugin.
    /// </summary>
    public string API_OpenPlugin(BasePlayer player)
    {
        if (player == null) return null;
        try
        {
            EnterServerPanelMode(player);
            // Warm stats if already in memory; async load is fine for first paint.
            EnsurePlayerLoaded(player.userID, player.displayName, null);
            return LeaderboardUI.BuildForServerPanel(player);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Leaderboard] API_OpenPlugin: " + ex.Message);
            return null;
        }
    }

    public void OnServerPanelClosed(BasePlayer player)
    {
        if (player == null) return;
        ExitServerPanelMode(player);
        OnLeaderboardClosed(player.userID);
    }

    public void OnServerPanelCategoryPage(BasePlayer player, object category, int page)
    {
        if (player == null) return;
        ExitServerPanelMode(player);
        OnLeaderboardClosed(player.userID);
    }

    /// <summary>Header kills/deaths fields in ServerPanel call this (UltimateLeaderboard-compatible).</summary>
    public object API_GetPlayerStat(ulong userId, string lootType, string key)
    {
        if (!TryGetStats(userId, out var stats) || string.IsNullOrEmpty(key)) return 0;
        if (string.Equals(lootType, "Kill", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(key, "kills", StringComparison.OrdinalIgnoreCase))
            return stats.GetKills();
        if (string.Equals(lootType, "Death", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(key, "deaths", StringComparison.OrdinalIgnoreCase))
            return stats.GetDeaths();
        if (Enum.TryParse(lootType, true, out LootType lt) && stats.TryGetItem(lt, key, out var value))
            return value;
        return 0;
    }

    public void RequestServerPanelRefresh(BasePlayer player)
    {
        if (player == null) return;
        try
        {
            var wrapper = AppDomain.CurrentDomain.GetData("ServerPanel_Plugin");
            if (wrapper == null) return;
            var call = wrapper.GetType().GetMethod("Call",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            call?.Invoke(wrapper, new object[] { "API_OnServerPanelRefreshContent", new object[] { player } });
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Leaderboard] RequestServerPanelRefresh: " + ex.Message);
        }
    }

    private void RegisterApiType()
    {
        try
        {
            AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(LeaderboardMod));
            AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _pluginWrapper);
            AppDomain.CurrentDomain.SetData(AppDomainPluginAliasKey, _pluginWrapper);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Leaderboard] RegisterApiType: " + ex.Message);
        }
    }

    private void UnregisterApiType()
    {
        try
        {
            AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
            AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
            AppDomain.CurrentDomain.SetData(AppDomainPluginAliasKey, null);
        }
        catch { }
    }

    public object Call(string method, params object[] args)
    {
        if (string.IsNullOrEmpty(method)) return null;
        args ??= Array.Empty<object>();
        try
        {
            switch (method)
            {
                case "API_OpenPlugin":
                    return API_OpenPlugin(args.Length > 0 ? args[0] as BasePlayer : null);
                case "OnServerPanelClosed":
                    OnServerPanelClosed(args.Length > 0 ? args[0] as BasePlayer : null);
                    return null;
                case "OnServerPanelCategoryPage":
                {
                    var player = args.Length > 0 ? args[0] as BasePlayer : null;
                    object category = args.Length > 1 ? args[1] : null;
                    int page = 0;
                    if (args.Length > 2)
                    {
                        if (args[2] is int i) page = i;
                        else int.TryParse(args[2]?.ToString(), out page);
                    }
                    OnServerPanelCategoryPage(player, category, page);
                    return null;
                }
                case "API_GetPlayerStat":
                {
                    ulong userId = 0;
                    if (args.Length > 0)
                    {
                        if (args[0] is ulong u) userId = u;
                        else if (args[0] is long l) userId = (ulong)l;
                        else ulong.TryParse(args[0]?.ToString(), out userId);
                    }
                    return API_GetPlayerStat(userId,
                        args.Length > 1 ? args[1]?.ToString() : null,
                        args.Length > 2 ? args[2]?.ToString() : null);
                }
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Call({method}): " + ex.Message);
            return null;
        }
    }

    /// <summary>Published on AppDomain for ServerPanel PluginBridge (IsLoaded + Call).</summary>
    public sealed class PluginWrapper
    {
        private readonly LeaderboardMod _mod;
        public PluginWrapper(LeaderboardMod mod) => _mod = mod;
        public bool IsLoaded => _mod != null && Instance == _mod;
        public string Name => "Leaderboard";
        public object Call(string method, params object[] args) => _mod?.Call(method, args);
        public object API_OpenPlugin(BasePlayer player) => _mod?.API_OpenPlugin(player);
    }

    #endregion

    public bool IsRateLimited(ulong userId)
    {
        var now = UnityEngine.Time.realtimeSinceStartup;
        lock (_lastCommandTime)
        {
            if (_lastCommandTime.TryGetValue(userId, out var last) && now - last < (_config?.CooldownSeconds ?? 0.2f))
                return true;
            _lastCommandTime[userId] = now;
        }
        return false;
    }

    public PlayerStats GetOrCreateStats(ulong userId, string displayName = null)
    {
        lock (_statsLock)
        {
            if (_playerStats.TryGetValue(userId, out var s)) return s;
            var stats = new PlayerStats(userId) { LastName = displayName ?? "Unknown" };
            _playerStats[userId] = stats;
            return stats;
        }
    }

    public bool TryGetStats(ulong userId, out PlayerStats stats)
    {
        lock (_statsLock)
            return _playerStats.TryGetValue(userId, out stats);
    }

    public void EnsurePlayerLoaded(ulong userId, string displayName, Action onLoaded)
    {
        lock (_statsLock)
        {
            if (_playerStats.TryGetValue(userId, out var s))
            {
                s.LastName = displayName ?? s.LastName;
                onLoaded?.Invoke();
                return;
            }
        }
        _storage?.LoadPlayer(userId, stats =>
        {
            lock (_statsLock)
            {
                stats.LastName = displayName ?? stats.LastName;
                _playerStats[userId] = stats;
            }
            onLoaded?.Invoke();
        });
    }

    public void OnPlayerConnected(BasePlayer player)
    {
        if (player == null || !SteamIdHelper.IsSteamId(player.userID)) return;
        EnsurePlayerLoaded(player.userID, player.displayName, () =>
        {
            var stats = GetOrCreateStats(player.userID, player.displayName);
            stats.ConnectTime = DateTime.UtcNow;
            stats.IsOnline = true;
            stats.LastName = player.displayName ?? stats.LastName;
        });
    }

    public void OnPlayerDisconnected(BasePlayer player)
    {
        if (player == null) return;
        if (TryGetStats(player.userID, out var stats))
        {
            stats.DisconnectTime = DateTime.UtcNow;
            stats.TotalPlayTime += (DateTime.UtcNow - stats.ConnectTime).TotalSeconds;
            stats.IsOnline = false;
            stats.LastName = player.displayName ?? stats.LastName;
            _storage?.SavePlayer(stats);
            if (_config?.Relay?.Enabled == true)
                FlushRelayBatch();
        }
    }

    public void RecordStat(ulong userId, LootType type, string prefab, float value)
    {
        if (string.IsNullOrEmpty(prefab)) return;
        var stats = GetOrCreateStats(userId, null);
        stats.AddStats(type, prefab, value);

        if (_config?.Relay?.Enabled == true && !string.IsNullOrEmpty(_config.Relay.Url))
        {
            stats.TryGetItem(type, prefab, out var total);
            lock (_relayBatch)
            {
                _relayBatch.Add(new StatUpdatePayload
                {
                    UserId = userId,
                    LootType = (int)type,
                    ShortName = prefab,
                    ItemValue = total
                });
            }
        }
    }

    public void RecordStatSet(ulong userId, LootType type, string prefab, float value)
    {
        if (string.IsNullOrEmpty(prefab)) return;
        var stats = GetOrCreateStats(userId, null);
        stats.SetStats(type, prefab, value);

        if (_config?.Relay?.Enabled == true && !string.IsNullOrEmpty(_config.Relay.Url))
        {
            lock (_relayBatch)
            {
                _relayBatch.Add(new StatUpdatePayload
                {
                    UserId = userId,
                    LootType = (int)type,
                    ShortName = prefab,
                    ItemValue = value
                });
            }
        }
    }

    public LeaderboardConfig GetConfig() => _config;

    /// <summary>Returns full URL for a leaderboard icon file, or null if ImageBaseUrl is not set.</summary>
    public string GetImageUrl(string iconFileName)
    {
        var baseUrl = _config?.ImageBaseUrl?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(iconFileName)) return null;
        return baseUrl.TrimEnd('/') + "/" + iconFileName;
    }

    /// <summary>Returns game item definition id for a resource stat key (e.g. shortname), or null if not an item.</summary>
    public int? GetItemIdForResource(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var def = ItemManager.FindItemDefinition(key);
        return def?.itemid;
    }

    public ILeaderboardStorage GetStorage() => _storage;

    public Dictionary<ulong, PlayerStats> GetAllStatsSnapshot()
    {
        lock (_statsLock)
            return new Dictionary<ulong, PlayerStats>(_playerStats);
    }

    /// <summary>All players (userId, name) sorted alphabetically by name, excluding hidden.</summary>
    public List<(ulong userId, string name)> GetAllPlayersSortedByName()
    {
        var list = new List<(ulong, string)>();
        lock (_statsLock)
        {
            foreach (var kv in _playerStats)
            {
                if (kv.Value.HiddenFromLeaderboard) continue;
                list.Add((kv.Key, kv.Value.LastName ?? kv.Key.ToString()));
            }
        }
        list.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private void FlushRelayBatch()
    {
        List<StatUpdatePayload> copy;
        lock (_relayBatch)
        {
            if (_relayBatch.Count == 0) return;
            copy = new List<StatUpdatePayload>(_relayBatch);
            _relayBatch.Clear();
        }
        if (_config?.Relay?.Url == null) return;

        var userIds = new HashSet<ulong>();
        foreach (var u in copy)
            userIds.Add(u.UserId);

        var players = new List<PlayerStatsPayload>();
        lock (_statsLock)
        {
            foreach (var uid in userIds)
            {
                if (!_playerStats.TryGetValue(uid, out var s)) continue;
                players.Add(new PlayerStatsPayload
                {
                    UserId = s.UserId,
                    LastIP = s.LastIP ?? "",
                    LastName = s.LastName ?? "",
                    ConnectTime = s.ConnectTime.ToString("o"),
                    DisconnectTime = s.DisconnectTime.ToString("o"),
                    TotalPlayTime = s.TotalPlayTime.ToString("N", System.Globalization.CultureInfo.InvariantCulture),
                    Points = s.Points,
                    HiddenFromLeaderboard = s.HiddenFromLeaderboard ? 1 : 0
                });
            }
        }

        RelaySender.SendBatch(_config.Relay.Url, copy, players);
    }

    public void Update(float deltaTime)
    {
        if (_config?.Relay?.Enabled == true && !string.IsNullOrEmpty(_config.Relay.Url))
        {
            _relayBatchTimer += deltaTime;
            var interval = _config.Relay.BatchIntervalSeconds;
            if (_relayBatchTimer >= (interval > 0 ? interval : 30f))
            {
                _relayBatchTimer = 0f;
                FlushRelayBatch();
            }
        }

        if (_config?.Discord?.Enabled == true && !string.IsNullOrEmpty(_config.Discord.WebhookUrl) && _config.Discord.AutoMessageIntervalSeconds > 0)
        {
            _discordTimer += deltaTime;
            if (_discordTimer >= _config.Discord.AutoMessageIntervalSeconds)
            {
                _discordTimer = 0f;
                SendDiscordSnapshot();
            }
        }
    }

    private void SendDiscordSnapshot()
    {
        var url = _config?.Discord?.WebhookUrl;
        if (string.IsNullOrEmpty(url)) return;

        var fields = new List<(string name, string value)>();
        lock (_statsLock)
        {
            var byKills = new List<(string name, int kills)>();
            var sb = new StringBuilder();
            foreach (var kv in _playerStats)
            {
                if (kv.Value.HiddenFromLeaderboard) continue;
                var k = kv.Value.GetKills();
                byKills.Add((kv.Value.LastName ?? kv.Key.ToString(), k));
            }
            byKills.Sort((a, b) => b.kills.CompareTo(a.kills));
            sb.Clear();
            for (int i = 0; i < Math.Min(5, byKills.Count); i++)
                sb.AppendLine($"{i + 1}. **{byKills[i].name}** — {byKills[i].kills}");
            if (sb.Length > 0)
                fields.Add(("Top 5 Kills", sb.ToString()));
        }
        if (fields.Count > 0)
            DiscordHelper.SendWebhook(url, "Leaderboard", fields);
    }
}
