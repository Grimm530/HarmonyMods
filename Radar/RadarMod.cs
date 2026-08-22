using System;
using System.Collections.Generic;
using System.Reflection;
using ConVar;
using Facepunch;
using HarmonyChat;
using Network;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Radar;

/// <summary>
/// Harmony mod: Entity radar (players, sleepers, corpses, bags, TCs, stashes, backpacks).
/// Uses built-in ddraw.arrow, ddraw.box, ddraw.text. Chat: /radar. CUI toggles entity types.
/// Buttons use cui.endtest RADAR &lt;action&gt; to avoid conflict with TCUpgrade SENDCMD.
/// </summary>
public class RadarMod : IHarmonyModHooks
{
    public static RadarMod Instance { get; private set; }

    internal const string UI_PANEL = "Radar_ESP_Panel";
    internal const string UI_MOVE_PANEL = "Radar_ESP_Move";
    private const string PanelMaterial = "assets/content/ui/namefontmaterial.mat";
    private const string PanelSprite = "assets/content/ui/ui.background.tile.psd";

    private static ConsoleSystem.Command _radarCmdCommand;
    private static ConsoleSystem.Command _radarChatCommand;
    private static object _replicatedList;
    private const float DrawDuration = 0.6f;
    private const float MaxDistance = 300f;
    private const float ScanRadius = 400f;

    private static MethodInfo _clientRpcString;
    private static readonly Vector3 VoiceArrowFrom = new Vector3(0f, 5f);
    private static readonly Vector3 VoiceArrowTo = new Vector3(0f, 2.5f);
    private static readonly List<ulong> _voiceCleanupRadarIds = new List<ulong>(8);
    private static readonly List<ulong> _voiceCleanupSpeakerIds = new List<ulong>(16);

    internal readonly Dictionary<ulong, RadarState> PlayerStates = new Dictionary<ulong, RadarState>();
    private readonly Dictionary<ulong, Dictionary<ulong, float>> _voices = new Dictionary<ulong, Dictionary<ulong, float>>();
    private float _nextVoiceCleanupTime;

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        _clientRpcString = typeof(BaseEntity).GetMethod("ClientRPC", new[] { typeof(RpcTarget), typeof(string) });
        try
        {
            RadarConfig.LoadConfig();
            _radarCmdCommand = new ConsoleSystem.Command
            {
                Name = "RADAR_CMD",
                FullName = "global.RADAR_CMD",
                Variable = true,
                ServerAdmin = false,
                Replicated = true,
                Call = HandleRadarCmd
            };
            _radarChatCommand = new ConsoleSystem.Command
            {
                Name = "radar",
                FullName = "global.radar",
                Variable = true,
                ServerAdmin = false,
                Replicated = true,
                Call = HandleRadarChat
            };
            ConsoleSystem.Index.Server.Dict["global.RADAR_CMD"] = _radarCmdCommand;
            ConsoleSystem.Index.Server.Dict["global.radar"] = _radarChatCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
            {
                ConsoleSystem.Index.Server.GlobalDict["RADAR_CMD"] = _radarCmdCommand;
                ConsoleSystem.Index.Server.GlobalDict["radar"] = _radarChatCommand;
            }

            // Add to replicated list so clients who join after server start receive the commands (fixes "unknown command" when used with AdminTime etc.).
            var serverType = typeof(ConsoleSystem.Index.Server);
            var prop = serverType.GetProperty("Replicated", BindingFlags.Public | BindingFlags.Static);
            if (prop != null)
            {
                var list = prop.GetValue(null) as System.Collections.IList;
                if (list != null)
                {
                    if (!list.Contains(_radarCmdCommand)) list.Add(_radarCmdCommand);
                    if (!list.Contains(_radarChatCommand)) list.Add(_radarChatCommand);
                    _replicatedList = list;
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Radar] Command registration failed: {ex.Message}");
        }
        ChatSayBridge.Register("Radar", OnChatSay);
        UnityEngine.Debug.Log("[Radar] Loaded. Use /radar to toggle. Admin only.");
    }

    private static void HandleRadarCmd(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null) return;
        var mod = Instance;
        if (mod == null) return;
        mod.HandleCuiCommand(player, ToStringArray(arg.Args));
    }

    private static string[] ToStringArray(StringView[] args)
    {
        if (args == null || args.Length == 0) return Array.Empty<string>();

        var result = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
            result[i] = args[i].ToString();
        return result;
    }

    /// <summary>Handler for console command "radar" (e.g. /radar in chat). Client looks up "radar" by name; RADAR_CMD alone would show "unknown command".</summary>
    private static void HandleRadarChat(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null) return;
        var mod = Instance;
        if (mod == null) return;
        mod.HandleRadarCommand(player, ToStringArray(arg.Args));
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        try { ChatSayBridge.Unregister("Radar"); } catch { }
        foreach (var kv in PlayerStates)
        {
            var p = BasePlayer.FindByID(kv.Key);
            if (p != null)
            {
                kv.Value.MoveModeActive = false;
                DestroyMoveUI(p);
                DestroyRadar(p);
                DestroyUI(p);
            }
        }
        PlayerStates.Clear();
        if (_replicatedList is System.Collections.IList list)
        {
            if (_radarCmdCommand != null) list.Remove(_radarCmdCommand);
            if (_radarChatCommand != null) list.Remove(_radarChatCommand);
        }
        if (_radarCmdCommand != null)
        {
            ConsoleSystem.Index.Server.Dict?.Remove("global.RADAR_CMD");
            ConsoleSystem.Index.Server.GlobalDict?.Remove("RADAR_CMD");
        }
        if (_radarChatCommand != null)
        {
            ConsoleSystem.Index.Server.Dict?.Remove("global.radar");
            ConsoleSystem.Index.Server.GlobalDict?.Remove("radar");
        }
        _radarCmdCommand = null;
        _radarChatCommand = null;
        _replicatedList = null;
        _clientRpcString = null;
        _voices.Clear();
        Instance = null;
    }

    internal bool OnChatSay(BasePlayer player, string message)
    {
        if (player == null) return false;
        var msg = message?.Trim();
        if (string.IsNullOrEmpty(msg)) return false;
        if (msg.StartsWith("/")) msg = msg.Substring(1).Trim();
        if (!IsRadarChatCommand(msg)) return false;

        return HandleRadarCommand(player, SplitRadarArgs(msg));
    }

    private static bool IsRadarChatCommand(string msg)
    {
        if (msg.Equals("radar", StringComparison.OrdinalIgnoreCase))
            return true;
        return msg.Length > 6 && msg.StartsWith("radar ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tokens after the leading "radar" command name. Empty array means toggle.</summary>
    private static string[] SplitRadarArgs(string msg)
    {
        var parts = msg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return Array.Empty<string>();
        var rest = new string[parts.Length - 1];
        Array.Copy(parts, 1, rest, 0, rest.Length);
        return rest;
    }

    /// <returns>True if Radar handled the command (caller should skip original).</returns>
    internal bool HandleRadarCommand(BasePlayer player, string[] args)
    {
        if (player == null) return false;

        if (!player.IsAdmin && !player.IsDeveloper)
        {
            SendMessage(player, "Radar requires admin.");
            return true;
        }

        if (args != null && args.Length > 0 && string.Equals(args[0], "findbyitem", StringComparison.OrdinalIgnoreCase))
        {
            CommandFindByItem(player, args);
            return true;
        }

        ToggleRadar(player);
        return true;
    }

    /// <summary>AdminRadar 5.4.312: map item shortname → entity shortname for Options → Boxes.</summary>
    private static void CommandFindByItem(BasePlayer player, string[] args)
    {
        if (args == null || args.Length < 2)
        {
            SendMessage(player, "Item shortname not specified. Usage: radar findbyitem industrial.storage");
            return;
        }

        var search = string.Join(" ", args, 1, args.Length - 1);
        if (!RadarBehaviour.TryFindDeployables(search, out var results, out var count))
        {
            SendMessage(player, $"No deployable matches found for '{search}'.");
            return;
        }

        SendMessage(player, "\nResults:\n" + results);
        if (count >= RadarBehaviour.FindByItemMaxResults)
            SendMessage(player, $"(showing first {RadarBehaviour.FindByItemMaxResults})");
    }

    /// <summary>
    /// AdminRadar voice ESP: yellow arrow on nearby speakers. Game method is <c>ServerMgr.OnPlayerVoice</c>
    /// (<c>ArraySegment&lt;byte&gt;</c> / <c>ReadOnlySpan&lt;byte&gt;</c>); this path does not read voice bytes.
    /// </summary>
    internal void OnPlayerVoice(BasePlayer speaker)
    {
        var voice = RadarConfig.Config?.VoiceDetection;
        if (voice == null || !voice.Enabled || voice.Distance <= 0f)
            return;
        if (speaker == null || speaker.IsDestroyed)
            return;
        if (PlayerStates.Count == 0)
            return;

        Vector3 a = speaker.transform.position;
        float currentTime = UnityEngine.Time.time;
        float sqrMax = voice.SqrDistance;
        float interval = voice.Interval < 3 ? 3f : voice.Interval;
        ulong speakerId = speaker.userID;

        if (currentTime >= _nextVoiceCleanupTime)
            CleanupVoices(currentTime);

        foreach (var kv in PlayerStates)
        {
            if (!kv.Value.Enabled)
                continue;

            var observer = BasePlayer.FindByID(kv.Key);
            if (observer == null || !observer.IsConnected || observer.transform == null)
                continue;
            if ((a - observer.transform.position).sqrMagnitude > sqrMax)
                continue;

            if (!_voices.TryGetValue(kv.Key, out var speakers))
            {
                speakers = new Dictionary<ulong, float>();
                _voices[kv.Key] = speakers;
            }

            if (speakers.TryGetValue(speakerId, out float expiry) && currentTime < expiry)
                continue;

            speakers[speakerId] = currentTime + interval;
            observer.Command("ddraw.arrow", interval + 0.02f, Color.yellow, a + VoiceArrowFrom, a + VoiceArrowTo, 0.5f);
        }
    }

    private void CleanupVoices(float currentTime)
    {
        _nextVoiceCleanupTime = currentTime + 30f;
        if (_voices.Count == 0)
            return;

        _voiceCleanupRadarIds.Clear();
        foreach (var radarId in _voices.Keys)
            _voiceCleanupRadarIds.Add(radarId);

        for (int r = 0; r < _voiceCleanupRadarIds.Count; r++)
        {
            var radarId = _voiceCleanupRadarIds[r];
            if (!_voices.TryGetValue(radarId, out var speakers) || speakers.Count == 0)
            {
                _voices.Remove(radarId);
                continue;
            }

            _voiceCleanupSpeakerIds.Clear();
            foreach (var speakerId in speakers.Keys)
                _voiceCleanupSpeakerIds.Add(speakerId);

            for (int s = 0; s < _voiceCleanupSpeakerIds.Count; s++)
            {
                var speakerId = _voiceCleanupSpeakerIds[s];
                if (speakers.TryGetValue(speakerId, out float expiry) && currentTime >= expiry)
                    speakers.Remove(speakerId);
            }

            if (speakers.Count == 0)
                _voices.Remove(radarId);
        }
    }

    /// <returns>True if Radar handled the command (caller should skip original).</returns>
    /// <param name="args">From cui.endtest: ["RADAR", action]; from RADAR_CMD: [action]. Caller guarantees player and args non-null.</param>
    internal bool HandleCuiCommand(BasePlayer player, string[] args)
    {
        if (args.Length < 1) return false;
        string action;
        if (args.Length >= 2 && string.Equals(args[0]?.ToString(), "RADAR", StringComparison.OrdinalIgnoreCase))
            action = args[1]?.ToString() ?? "";
        else if (args.Length >= 1)
            action = args[0]?.ToString() ?? "";
        else
            return false;

        if (!player.IsAdmin && !player.IsDeveloper) return false;

        if (action == "CLOSE")
        {
            var state = GetOrCreateState(player);
            state.MoveModeActive = false;
            DestroyMoveUI(player);
            DestroyUI(player);
            return true;
        }

        if (action == "TOGGLE_RADAR")
        {
            ToggleRadar(player);
            return true;
        }

        if (action == "TOGGLE_ALL")
        {
            var state = GetOrCreateState(player);
            state.SetAll(!state.IsAllEnabled());
            RefreshUI(player);
            return true;
        }

        if (action.StartsWith("TOGGLE_") && Enum.TryParse<RadarEntityType>(action.Substring(7), true, out var type))
        {
            var state = GetOrCreateState(player);
            state.Toggle(type);
            RefreshUI(player);
            return true;
        }

        if (action == "RANGE_UP")
        {
            var state = GetOrCreateState(player);
            state.IncreaseRange();
            RefreshUI(player);
            SendMessage(player, $"Range: {state.ViewDistance}m");
            return true;
        }

        if (action == "RANGE_DOWN")
        {
            var state = GetOrCreateState(player);
            state.DecreaseRange();
            RefreshUI(player);
            SendMessage(player, $"Range: {state.ViewDistance}m");
            return true;
        }

        if (action == "REFRESH_UP")
        {
            var state = GetOrCreateState(player);
            state.IncreaseRefreshRate();
            RefreshUI(player);
            SendMessage(player, $"Refresh: {state.RefreshInterval:F1}s");
            return true;
        }
        if (action == "REFRESH_DOWN")
        {
            var state = GetOrCreateState(player);
            state.DecreaseRefreshRate();
            RefreshUI(player);
            SendMessage(player, $"Refresh: {state.RefreshInterval:F1}s");
            return true;
        }
        if (action == "DISTANCE_UP")
        {
            var state = GetOrCreateState(player);
            state.IncreaseDistance100();
            RefreshUI(player);
            SendMessage(player, $"Distance: {state.ViewDistance}m");
            return true;
        }
        if (action == "DISTANCE_DOWN")
        {
            var state = GetOrCreateState(player);
            state.DecreaseDistance100();
            RefreshUI(player);
            SendMessage(player, $"Distance: {state.ViewDistance}m");
            return true;
        }

        if (action == "MOVE_TOGGLE")
        {
            var state = GetOrCreateState(player);
            state.MoveModeActive = !state.MoveModeActive;
            RefreshUI(player);
            if (state.MoveModeActive) OpenMoveUI(player);
            else DestroyMoveUI(player);
            return true;
        }

        if (action == "MOVE_LEFT" || action == "MOVE_RIGHT" || action == "MOVE_UP" || action == "MOVE_DOWN")
        {
            var state = GetOrCreateState(player);
            // Treat stored values as pixel offsets (matching AdminRadar UI offsets).
            const float step = 15f;
            ParseOffsets(state.UiAnchorMin, state.UiAnchorMax, out float minX, out float minY, out float maxX, out float maxY);
            switch (action)
            {
                case "MOVE_LEFT":
                    minX -= step;
                    maxX -= step;
                    break;
                case "MOVE_RIGHT":
                    minX += step;
                    maxX += step;
                    break;
                case "MOVE_UP":
                    minY += step;
                    maxY += step;
                    break;
                case "MOVE_DOWN":
                    minY -= step;
                    maxY -= step;
                    break;
            }
            state.UiAnchorMin = $"{minX:F3} {minY:F3}";
            state.UiAnchorMax = $"{maxX:F3} {maxY:F3}";
            RadarUserData.Save(player.userID, state.UiAnchorMin, state.UiAnchorMax);
            RefreshUI(player);
            OpenMoveUI(player);
            return true;
        }

        // Unsupported filter buttons (Box, Loot, Npc, Ore, etc.) - no-op so UI doesn't error.
        if (action.StartsWith("NOOP"))
        {
            return true;
        }

        return false;
    }

    private static void ParseOffsets(string omin, string omax, out float minX, out float minY, out float maxX, out float maxY)
    {
        var a = omin?.Split(' ') ?? new[] { "0", "0" };
        var b = omax?.Split(' ') ?? new[] { "0", "0" };
        float.TryParse(a.Length > 0 ? a[0] : "0", out minX);
        float.TryParse(a.Length > 1 ? a[1] : "0", out minY);
        float.TryParse(b.Length > 0 ? b[0] : "0", out maxX);
        float.TryParse(b.Length > 1 ? b[1] : "0", out maxY);
    }

    /// <summary>Returns panel offset strings that produce a visible panel. Uses defaults if stored values look like normalized (0-1).</summary>
    private static void GetPanelOffsets(RadarState state, out string offsetMin, out string offsetMax)
    {
        ParseOffsets(state.UiAnchorMin, state.UiAnchorMax, out float minX, out float minY, out float maxX, out float maxY);
        float w = maxX - minX, h = maxY - minY;
        if (w > 0 && w < 50 && h > 0 && h < 50 && minX >= 0 && minX <= 1 && minY >= 0 && minY <= 1)
        {
            offsetMin = "-120 20";
            offsetMax = "100 115";
            return;
        }
        offsetMin = state.UiAnchorMin;
        offsetMax = state.UiAnchorMax;
    }

    internal void ToggleRadar(BasePlayer player)
    {
        if (!player.IsAdmin && !player.IsDeveloper)
        {
            DestroyRadar(player);
            DestroyMoveUI(player);
            DestroyUI(player);
            return;
        }
        var state = GetOrCreateState(player);
        state.Enabled = !state.Enabled;

        if (state.Enabled)
        {
            var go = player.gameObject;
            if (go.GetComponent<RadarBehaviour>() == null)
                go.AddComponent<RadarBehaviour>();
            OpenUI(player);
        }
        else
        {
            DestroyRadar(player);
            var s = GetOrCreateState(player);
            s.MoveModeActive = false;
            DestroyMoveUI(player);
            DestroyUI(player);
        }
        SendMessage(player, $"Radar {(state.Enabled ? "ON" : "OFF")}");
    }

    internal static bool IsRadarActive(ulong userId)
    {
        return Instance != null
            && Instance.PlayerStates.TryGetValue(userId, out var state)
            && state.Enabled;
    }

    internal RadarState GetOrCreateState(BasePlayer player)
    {
        if (player == null) return null;
        if (!PlayerStates.TryGetValue(player.userID, out var state))
        {
            state = new RadarState();
            var saved = RadarUserData.Load(player.userID);
            if (saved != null)
            {
                if (!string.IsNullOrEmpty(saved.UiAnchorMin)) state.UiAnchorMin = saved.UiAnchorMin;
                if (!string.IsNullOrEmpty(saved.UiAnchorMax)) state.UiAnchorMax = saved.UiAnchorMax;
            }
            PlayerStates[player.userID] = state;
        }
        return state;
    }

    internal void DestroyRadar(BasePlayer player)
    {
        if (player == null) return;
        var comp = player.GetComponent<RadarBehaviour>();
        if (comp != null)
            UnityEngine.Object.Destroy(comp);
        player.Command("ddraw.clear");
        _voices.Remove(player.userID);
    }

    internal void OpenUI(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        if (!player.IsAdmin && !player.IsDeveloper) return;
        var settings = RadarConfig.Config?.Settings;
        if (settings != null && !settings.UserInterfaceEnabled)
            return;
        DestroyUI(player);
        SendUI(player, BuildUI(player));
    }

    internal void RefreshUI(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        DestroyUI(player);
        OpenUI(player);
    }

    /// <summary>Button entry: label, command suffix (e.g. TOGGLE_TC or NOOP), optional entity type for state.</summary>
    private struct GridButton
    {
        public string Label;
        public string Command;
        public RadarEntityType? Type;
        public bool IsMove;
        public bool IsAll;
        public GridButton(string label, string command, RadarEntityType? type, bool isMove = false, bool isAll = false)
        {
            Label = label;
            Command = command;
            Type = type;
            IsMove = isMove;
            IsAll = isAll;
        }
    }

    private static readonly GridButton[] GridButtonOrder =
    {
        new GridButton("All", "TOGGLE_ALL", null, false, true),
        new GridButton("Sleepers", "TOGGLE_Sleepers", RadarEntityType.Sleepers),
        new GridButton("TC", "TOGGLE_TC", RadarEntityType.TC),
        new GridButton("Bag", "TOGGLE_Bags", RadarEntityType.Bags),
        new GridButton("Box", "TOGGLE_Box", RadarEntityType.Box),
        new GridButton("Dead", "TOGGLE_Dead", RadarEntityType.Dead),
        new GridButton("Stash", "TOGGLE_Stash", RadarEntityType.Stash),
        new GridButton("Loot", "TOGGLE_Loot", RadarEntityType.Loot),
        new GridButton("Npc", "TOGGLE_Npc", RadarEntityType.Npc),
        new GridButton("Ore", "TOGGLE_Ore", RadarEntityType.Ore),
        new GridButton("Trap", "TOGGLE_Trap", RadarEntityType.Trap),
        new GridButton("Turret", "TOGGLE_Turret", RadarEntityType.Turret),
        new GridButton("Col", "TOGGLE_Col", RadarEntityType.Col),
        new GridButton("Airdrop", "TOGGLE_Airdrop", RadarEntityType.Airdrop),
        new GridButton("CCTV", "TOGGLE_CCTV", RadarEntityType.CCTV),
        new GridButton("MLRS", "TOGGLE_MLRS", RadarEntityType.MLRS),
        new GridButton("Prefab", "TOGGLE_Prefab", RadarEntityType.Prefab),
        new GridButton("Players", "TOGGLE_Players", RadarEntityType.Players),
        new GridButton("↕", "MOVE_TOGGLE", null, true, false),
        new GridButton("×", "CLOSE", null)
    };

    private string BuildUI(BasePlayer player)
    {
        var state = GetOrCreateState(player);
        var elements = new List<JObject>();

        // Light blue default; purple when on/clicked. ~20% opacity so background shows through.
        const string offColor = "0.45 0.65 0.95 0.2";
        const string onColor = "0.55 0.25 0.85 0.2";
        string moveColor = state.MoveModeActive ? onColor : offColor;

        GetPanelOffsets(state, out string offsetMin, out string offsetMax);

        // Panel: 25% from right edge = anchor at 0.75; transparent background.
        var main = new JObject
        {
            ["name"] = UI_PANEL,
            ["parent"] = "Hud",
            ["destroyUi"] = UI_PANEL,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0", ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.75 0", ["anchormax"] = "0.75 0", ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
            }
        };
        elements.Add(main);

        // Grid: 5 columns, 6 rows total. Row 0 = Refresh Rate & Distance (evenly above filter rows); rows 2–5 = filter buttons.
        const int cols = 5;
        const int totalRows = 6;
        const float pad = 0.02f;
        const float gap = 0.02f;
        float cellW = (1f - 2f * pad - (cols - 1) * gap) / cols;
        float cellH = (1f - 2f * pad - (totalRows - 1) * gap) / totalRows;

        // Control row: one row down; Refresh left-aligned, Distance right-aligned, micro inset so both fit inside panel.
        float row0MaxY = 1f - pad - (cellH + gap);
        float row0MinY = row0MaxY - cellH;
        const float edgeInset = 0.01f;
        float refMinX = pad + edgeInset;
        float refMaxX = 0.5f - gap * 0.5f;
        float distMinX = 0.5f + gap * 0.5f;
        float distMaxX = 1f - pad - edgeInset;
        AddControlRow(elements, UI_PANEL, refMinX, row0MinY, refMaxX, row0MaxY, "Refresh Rate", state.RefreshInterval.ToString("F1") + "s", "cui.endtest RADAR REFRESH_UP", "cui.endtest RADAR REFRESH_DOWN", offColor, onColor);
        AddControlRow(elements, UI_PANEL, distMinX, row0MinY, distMaxX, row0MaxY, "Distance", state.ViewDistance + "m", "cui.endtest RADAR DISTANCE_UP", "cui.endtest RADAR DISTANCE_DOWN", offColor, onColor);

        // Filter buttons: rows 2–5 (same 5 cols)
        for (int i = 0; i < GridButtonOrder.Length; i++)
        {
            var btn = GridButtonOrder[i];
            int col = i % cols;
            int row = i / cols + 2;
            float minX = pad + col * (cellW + gap);
            float maxX = minX + cellW;
            float maxY = 1f - pad - row * (cellH + gap);
            float minY = maxY - cellH;

            string cellAmin = $"{minX:F3} {minY:F3}";
            string cellAmax = $"{maxX:F3} {maxY:F3}";

            // Outer border (thin line); slightly transparent so background shows through.
            string frameName = $"Radar_btn_{i}_frame";
            elements.Add(new JObject
            {
                ["name"] = frameName,
                ["parent"] = UI_PANEL,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0.5", ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = cellAmin, ["anchormax"] = cellAmax }
                }
            });

            string color;
            string labelText = btn.Label;
            if (btn.IsMove)
                color = moveColor;
            else if (btn.IsAll)
            {
                bool allOn = state.IsAllEnabled();
                color = allOn ? onColor : offColor;
                labelText = allOn ? "All" : "All";
            }
            else if (btn.Type.HasValue)
            {
                bool on = state.IsEnabled(btn.Type.Value);
                color = on ? onColor : offColor;
            }
            else
                color = offColor;

            string cmd = "cui.endtest RADAR " + btn.Command;
            string btnName = $"Radar_btn_{i}";
            elements.Add(AddButton(btnName, frameName, "0.04 0.04", "0.96 0.96", cmd, labelText, color));
            if (btn.Command == "CLOSE")
            {
                var addedBtn = (JObject)elements[elements.Count - 1];
                var comps = (JArray)addedBtn["components"];
                ((JObject)comps[0])["close"] = UI_PANEL;
            }
            elements.Add(CreateLabel(btnName + "_lbl", btnName, labelText, 9, "0 0", "1 1", "MiddleCenter"));
        }

        return Newtonsoft.Json.JsonConvert.SerializeObject(elements);
    }

    /// <summary>Adds a control row: frame (same blue as grid buttons) + label + value + up/down arrow buttons.</summary>
    private void AddControlRow(List<JObject> elements, string parent, float minX, float minY, float maxX, float maxY, string labelText, string valueText, string cmdUp, string cmdDown, string offColor, string onColor)
    {
        string frameName = "Radar_ctrl_" + labelText.Replace(" ", "_") + "_frame";
        elements.Add(new JObject
        {
            ["name"] = frameName,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = offColor, ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = $"{minX:F3} {minY:F3}", ["anchormax"] = $"{maxX:F3} {maxY:F3}" }
            }
        });
        elements.Add(CreateLabel(frameName + "_lbl", frameName, labelText + " " + valueText, 8, "0.02 0.1", "0.58 0.9", "MiddleLeft"));
        elements.Add(AddButton(frameName + "_up", frameName, "0.60 0.1", "0.79 0.9", cmdUp, "↑", offColor));
        elements.Add(CreateLabel(frameName + "_up_lbl", frameName + "_up", "↑", 10, "0 0", "1 1", "MiddleCenter"));
        elements.Add(AddButton(frameName + "_down", frameName, "0.81 0.1", "1 0.9", cmdDown, "↓", offColor));
        elements.Add(CreateLabel(frameName + "_down_lbl", frameName + "_down", "↓", 10, "0 0", "1 1", "MiddleCenter"));
    }

    private static JObject AddButton(string name, string parent, string amin, string amax, string cmd, string text, string color)
    {
        return new JObject
        {
            ["name"] = name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = cmd, ["color"] = color, ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = amin, ["anchormax"] = amax }
            }
        };
    }

    private static JObject CreateLabel(string name, string parent, string text, int fontSize, string amin, string amax, string align = "MiddleLeft")
    {
        return new JObject
        {
            ["name"] = name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text ?? "", ["fontSize"] = fontSize, ["color"] = "1 1 1 1", ["align"] = align ?? "MiddleLeft" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = amin, ["anchormax"] = amax }
            }
        };
    }

    internal void SendUI(BasePlayer player, string json)
    {
        if (player?.net?.connection == null || string.IsNullOrEmpty(json)) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        InvokeClientRpcString(ce, RpcTarget.Player("AddUI", player.net.connection), json);
    }

    internal void DestroyUI(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce != null && !ce.IsDestroyed)
            InvokeClientRpcString(ce, RpcTarget.Player("DestroyUI", player.net.connection), UI_PANEL);
    }

    internal void DestroyMoveUI(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce != null && !ce.IsDestroyed)
            InvokeClientRpcString(ce, RpcTarget.Player("DestroyUI", player.net.connection), UI_MOVE_PANEL);
    }

    private static void InvokeClientRpcString(BaseEntity entity, RpcTarget target, string arg)
    {
        if (entity == null || _clientRpcString == null)
            return;
        try
        {
            _clientRpcString.Invoke(entity, new object[] { target, arg });
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Radar] ClientRPC failed: " + ex.Message);
        }
    }

    private void OpenMoveUI(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        DestroyMoveUI(player);
        var state = GetOrCreateState(player);
        if (!state.MoveModeActive) return;
        var elements = new List<JObject>();
        elements.Add(new JObject
        {
            ["name"] = UI_MOVE_PANEL,
            ["parent"] = UI_PANEL,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0.6 0.8", ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 1", ["anchormax"] = "0.5 1", ["offsetmin"] = "-40 -30", ["offsetmax"] = "40 12" },
                new JObject { ["type"] = "NeedsCursor" }
            }
        });
        elements.Add(AddButton($"{UI_MOVE_PANEL}_L", UI_MOVE_PANEL, "0 0.2", "0.22 0.8", "cui.endtest RADAR MOVE_LEFT", "←", "0 0 0.6 0.9"));
        elements.Add(CreateLabel($"{UI_MOVE_PANEL}_L_lbl", $"{UI_MOVE_PANEL}_L", "←", 12, "0 0", "1 1", "MiddleCenter"));
        elements.Add(AddButton($"{UI_MOVE_PANEL}_U", UI_MOVE_PANEL, "0.24 0.2", "0.46 0.8", "cui.endtest RADAR MOVE_UP", "↑", "0 0 0.6 0.9"));
        elements.Add(CreateLabel($"{UI_MOVE_PANEL}_U_lbl", $"{UI_MOVE_PANEL}_U", "↑", 12, "0 0", "1 1", "MiddleCenter"));
        elements.Add(AddButton($"{UI_MOVE_PANEL}_D", UI_MOVE_PANEL, "0.48 0.2", "0.70 0.8", "cui.endtest RADAR MOVE_DOWN", "↓", "0 0 0.6 0.9"));
        elements.Add(CreateLabel($"{UI_MOVE_PANEL}_D_lbl", $"{UI_MOVE_PANEL}_D", "↓", 12, "0 0", "1 1", "MiddleCenter"));
        elements.Add(AddButton($"{UI_MOVE_PANEL}_R", UI_MOVE_PANEL, "0.72 0.2", "0.94 0.8", "cui.endtest RADAR MOVE_RIGHT", "→", "0 0 0.6 0.9"));
        elements.Add(CreateLabel($"{UI_MOVE_PANEL}_R_lbl", $"{UI_MOVE_PANEL}_R", "→", 12, "0 0", "1 1", "MiddleCenter"));
        SendUI(player, Newtonsoft.Json.JsonConvert.SerializeObject(elements));
    }

    internal static void SendMessage(BasePlayer player, string msg)
    {
        if (player?.net?.connection == null) return;
        ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 0, 0, msg);
    }
}
