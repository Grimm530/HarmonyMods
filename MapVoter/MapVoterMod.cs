using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

namespace MapVoter;

/// <summary>
/// Harmony mod for map voting. Standalone, no Oxide.
/// CUI via CommunityEntity.ServerInstance.ClientRPC (see CUI Reference).
/// Config: HarmonyConfig/MapVoter.json
/// </summary>
public class MapVoterMod : IHarmonyModHooks
{
    public static MapVoterMod Instance { get; private set; }

    private MapVoterConfig _config;
        private readonly Dictionary<string, int> _votes = new();
        private readonly HashSet<ulong> _votedPlayers = new();
        private readonly HashSet<string> _votedDiscordIds = new();
    private bool _voteActive;
    private const string UI_PANEL = "MapVoter_Main";
    private const string FULLSCREEN_PANEL = "MapVoter_Fullscreen";
    private List<MapVoterConfig.MapOption> _currentVoteMaps = new();
    private bool _mapsLoading;

    private List<ConsoleSystem.Command> _mvoteCommands = new();
    private ConsoleSystem.Command _mvoteStartCommand;
    private ConsoleSystem.Command _mapvoteStartCommand;
    private ConsoleSystem.Command _mvotepostCommand;
    private ConsoleSystem.Command _mvoteDiscordCommand;
    /// <summary>RCON / bot: <c>global.discordvote &lt;steam64&gt; &lt;mapIndex&gt;</c></summary>
    private ConsoleSystem.Command _discordBotVoteCommand;
    private ConsoleSystem.Command _mvoteWipeCommand;
    private ConsoleSystem.Command _mvotereadyCommand;
    private ConsoleSystem.Command _mvoteCommand;
    private ConsoleSystem.Command _mapvoteAliasCommand;
    private volatile bool _downloadCancelled;
    private Coroutine _voteEndAtWipeCoroutine;
    private Coroutine _autoWipeCheckCoroutine;
    private Coroutine _autoVoteCheckCoroutine;
    private Coroutine _discordVoteRefreshCoroutine;
    private Coroutine _bootstrapCoroutine;
    private Coroutine _loadMapsCoroutine;
    private GameObject _runnerObject;
    private MapVoterRunner _runner;
    private readonly HashSet<ulong> _uiViewers = new();
    private bool _restartScheduled;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
        Instance = this;
        TryApplyFindPatch();
        LoadConfig();
        RegisterConsoleCommands();
        // Leftover pending wipe (shutdown died before OnUnloaded) — apply cfg then delete identity files.
        ServerWipe();
        HandlePostWipePluginDataWipe();
        EnsureRunner();
        _bootstrapCoroutine = StartModCoroutine(BootstrapCoroutine());
        Info("MapVoter Harmony mod loaded. Config: HarmonyConfig/MapVoter.json");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        _downloadCancelled = true;
        StopModCoroutine(ref _bootstrapCoroutine);
        StopModCoroutine(ref _voteEndAtWipeCoroutine);
        StopModCoroutine(ref _autoWipeCheckCoroutine);
        StopModCoroutine(ref _autoVoteCheckCoroutine);
        StopModCoroutine(ref _discordVoteRefreshCoroutine);
        StopModCoroutine(ref _loadMapsCoroutine);
        DestroyRunner();
        UnregisterConsoleCommands();
        DestroyAllUI();
        // Update server.cfg for wipe before shutdown (if pending wipe data exists)
        ServerWipe();
        Instance = null;
    }

    private void EnsureRunner()
    {
        if (_runner != null) return;
        _runnerObject = new GameObject("MapVoterRunner");
        UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
        _runnerObject.hideFlags = HideFlags.HideAndDontSave;
        _runner = _runnerObject.AddComponent<MapVoterRunner>();
    }

    private void DestroyRunner()
    {
        if (_runnerObject != null)
        {
            UnityEngine.Object.Destroy(_runnerObject);
            _runnerObject = null;
        }
        _runner = null;
    }

    private Coroutine StartModCoroutine(IEnumerator routine)
    {
        EnsureRunner();
        return _runner.StartCoroutine(routine);
    }

    private void StopModCoroutine(ref Coroutine routine)
    {
        if (routine == null) return;
        if (_runner != null)
        {
            try { _runner.StopCoroutine(routine); } catch { }
        }
        routine = null;
    }

    private IEnumerator BootstrapCoroutine()
    {
        while (Instance != null && ServerMgr.Instance == null)
            yield return null;
        if (Instance == null) yield break;

        if (TryReadVoteSeedsFromFile(out int mapSize, out List<int> seeds) && mapSize > 0)
        {
            int withImages = 0;
            for (int i = 0; i < seeds.Count; i++)
            {
                if (!string.IsNullOrEmpty(FindImageFileForSeed(mapSize, seeds[i])))
                    withImages++;
            }
            if (withImages == 0)
            {
                Info($"MapVoter: Persisted seeds ({seeds.Count}) have no matching images in {GetImagesPath()} - ignoring. Admin: mvote to pick from the pool.");
                DeleteVoteSeedsFile();
                DeleteVoteStateFile();
            }
            else
            {
                Info($"MapVoter: Found persisted vote seeds ({seeds.Count} maps, {withImages} images, size {mapSize}) - restoring");
                TryReadVoteStateFromFile();
                StopModCoroutine(ref _loadMapsCoroutine);
                _loadMapsCoroutine = StartModCoroutine(LoadMapsAndActivateCoroutine(mapSize, seeds, false));
            }
        }

        if (_config?.AutoWipe?.EnableAutoWipe == true)
            _autoWipeCheckCoroutine = StartModCoroutine(AutoWipeCheckCoroutine());
        if (_config?.AutoVote?.EnableAutoVote == true)
            _autoVoteCheckCoroutine = StartModCoroutine(AutoVoteCheckCoroutine());
        LogWipeCalendar();
    }

    private void TryApplyFindPatch()
    {
        try
        {
            var harmony = new Harmony("com.facepunch.rust_dedicated.MapVoter.find");
            if (!Patches.Patch_ConsoleSystem_Server_Find.TryApply(harmony))
                Log("MapVoter: ConsoleSystem.Index.Server.Find patch skipped (method not present in this build). Commands use Dict/GlobalDict registration instead.");
        }
        catch (Exception ex)
        {
            Log($"MapVoter: ConsoleSystem.Index.Server.Find patch failed (non-fatal): {ex.Message}");
        }
    }

    private const string MVOTE_READY_MSG = "Picking maps from the image pool and posting to Discord...";

    private void RegisterConsoleCommands()
    {
        try
        {
            var openCommands = new List<string>();
            foreach (string cmd in GetOpenCommands())
            {
                if (string.IsNullOrWhiteSpace(cmd)) continue;
                var c = cmd.Trim();
                if (c.Equals("mvote", StringComparison.OrdinalIgnoreCase)) continue;
                openCommands.Add(c);
            }
            if (openCommands.Count == 0) openCommands.Add("vote");
            foreach (string cmd in openCommands)
            {
                var c = cmd.Trim();
                var cmdObj = new ConsoleSystem.Command
                {
                    Name = c,
                    FullName = "global." + c,
                    Variable = true,
                    ServerAdmin = false,
                    Call = arg =>
                    {
                        var p = arg.Player();
                        if (p != null) { Log($"Console command {c}: opening UI"); OpenUI(p); }
                    }
                };
                ConsoleSystem.Index.Server.Dict["global." + c] = cmdObj;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[c] = cmdObj;
                _mvoteCommands.Add(cmdObj);
            }

            _mvoteCommand = new ConsoleSystem.Command
            {
                Name = "mvote",
                FullName = "global.mvote",
                Variable = true,
                ServerAdmin = false,
                AllowRunFromServer = true,
                Call = arg =>
                {
                    var p = arg.Player();
                    string msg = HandleMvoteCommand(p);
                    if (p != null)
                    {
                        SendMessage(p, msg);
                        OpenUI(p);
                    }
                    else
                        Info(msg);
                }
            };
            ConsoleSystem.Index.Server.Dict["global.mvote"] = _mvoteCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mvote"] = _mvoteCommand; // mvote = create list only, not open UI

            _mvoteStartCommand = new ConsoleSystem.Command
            {
                Name = "mvtest",
                FullName = "global.mvtest",
                Variable = true,
                ServerAdmin = true,
                AllowRunFromServer = true,
                Call = arg => { string msg = StartVoteFromPool(); var pl = arg?.Player(); if (pl != null) SendMessage(pl, msg); else Info(msg); }
            };
            ConsoleSystem.Index.Server.Dict["global.mvtest"] = _mvoteStartCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mvtest"] = _mvoteStartCommand;

            _mapvoteStartCommand = new ConsoleSystem.Command
            {
                Name = "mapvotestart",
                FullName = "global.mapvotestart",
                Variable = true,
                ServerAdmin = true,
                AllowRunFromServer = true,
                Call = arg => { string msg = StartVoteFromPool(); var pl = arg?.Player(); if (pl != null) SendMessage(pl, msg); else Info(msg); }
            };
            ConsoleSystem.Index.Server.Dict["global.mapvotestart"] = _mapvoteStartCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mapvotestart"] = _mapvoteStartCommand;

            _mvotereadyCommand = new ConsoleSystem.Command
            {
                Name = "mvoteready",
                FullName = "global.mvoteready",
                Variable = true,
                ServerAdmin = true,
                AllowRunFromServer = true,
                Call = arg =>
                {
                    string msg = StartVoteFromPool();
                    var pl = arg?.Player();
                    if (pl != null) SendMessage(pl, msg);
                    else Info(msg);
                }
            };
            ConsoleSystem.Index.Server.Dict["global.mvoteready"] = _mvotereadyCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mvoteready"] = _mvotereadyCommand;

            _mvotepostCommand = new ConsoleSystem.Command
            {
                Name = "mvotepost",
                FullName = "global.mvotepost",
                Variable = true,
                ServerAdmin = true,
                AllowRunFromServer = true,
                Call = arg =>
                {
                    string msg = StartVoteFromPool();
                    var pl = arg?.Player();
                    if (pl != null) SendMessage(pl, msg);
                    else Info(msg);
                }
            };
            ConsoleSystem.Index.Server.Dict["global.mvotepost"] = _mvotepostCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mvotepost"] = _mvotepostCommand;

            _mvoteWipeCommand = new ConsoleSystem.Command
            {
                Name = "mvotewipe",
                FullName = "global.mvotewipe",
                Variable = true,
                ServerAdmin = true,
                AllowRunFromServer = true,
                Call = arg => ForceWipeNow()
            };
            ConsoleSystem.Index.Server.Dict["global.mvotewipe"] = _mvoteWipeCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mvotewipe"] = _mvoteWipeCommand;

            _mvoteDiscordCommand = new ConsoleSystem.Command
            {
                Name = "mvotediscord",
                FullName = "global.mvotediscord",
                Variable = true,
                ServerAdmin = true,
                Call = arg => ResendVoteToDiscord()
            };
            ConsoleSystem.Index.Server.Dict["global.mvotediscord"] = _mvoteDiscordCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mvotediscord"] = _mvoteDiscordCommand;

            // Discord bot: RCON global.discordvote <steam64> <mapIndex> (same registration path as mvote: Dict + GlobalDict + Index.All)
            _discordBotVoteCommand = new ConsoleSystem.Command
            {
                Name = "discordvote",
                FullName = "global.discordvote",
                Variable = true,
                ServerAdmin = false,
                AllowRunFromServer = true,
                Call = HandleSteamVoteCommand
            };
            ConsoleSystem.Index.Server.Dict["global.discordvote"] = _discordBotVoteCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["discordvote"] = _discordBotVoteCommand;

            // mapvote = alias so typing "mapvote" doesn't give "command not found"; reply with help
            _mapvoteAliasCommand = new ConsoleSystem.Command
            {
                Name = "mapvote",
                FullName = "global.mapvote",
                Variable = true,
                ServerAdmin = false,
                AllowRunFromServer = true,
                Call = arg =>
                {
                    string msg = "MapVoter: Admins use 'mvote' or 'mvoteready' to start a vote from the image pool. Players use 'vote' to open the UI.";
                    if (arg?.Player() != null)
                        SendMessage(arg.Player(), msg);
                    else
                        UnityEngine.Debug.Log("[MapVoter] " + msg);
                }
            };
            ConsoleSystem.Index.Server.Dict["global.mapvote"] = _mapvoteAliasCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["mapvote"] = _mapvoteAliasCommand;

            // Rebuild Index.All so server console finds our commands (required or "Command 'mvote' not found").
            ConsoleIndexCompat.RebuildAllFromServerDict();
        }
        catch (Exception ex) { Log($"Console command registration failed: {ex.Message}"); }
    }

    private void UnregisterConsoleCommands()
    {
        try
        {
            foreach (var cmdObj in _mvoteCommands)
            {
                if (cmdObj == null) continue;
                string c = cmdObj.Name;
                ConsoleSystem.Index.Server.Dict?.Remove("global." + c);
                ConsoleSystem.Index.Server.GlobalDict?.Remove(c);
            }
            _mvoteCommands.Clear();
            if (_mvoteStartCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mvtest");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mvtest");
                _mvoteStartCommand = null;
            }
            if (_mapvoteStartCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mapvotestart");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mapvotestart");
                _mapvoteStartCommand = null;
            }
            if (_mvotepostCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mvotepost");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mvotepost");
                _mvotepostCommand = null;
            }
            if (_mvoteWipeCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mvotewipe");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mvotewipe");
                _mvoteWipeCommand = null;
            }
            if (_mvoteDiscordCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mvotediscord");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mvotediscord");
                _mvoteDiscordCommand = null;
            }
            if (_discordBotVoteCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.discordvote");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("discordvote");
                _discordBotVoteCommand = null;
            }
            if (_mvoteCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mvote");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mvote"); // key is "mvote"
                _mvoteCommand = null;
            }
            if (_mvotereadyCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mvoteready");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mvoteready");
                _mvotereadyCommand = null;
            }
            if (_mapvoteAliasCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.mapvote");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("mapvote");
                _mapvoteAliasCommand = null;
            }
            try
            {
                ConsoleIndexCompat.RebuildAllFromServerDict();
            }
            catch { }
        }
        catch { }
    }

    private void LoadConfig()
    {
        string path = Path.Combine(GetServerRoot(), "HarmonyConfig", "MapVoter.json");
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                _config = Newtonsoft.Json.JsonConvert.DeserializeObject<MapVoterConfig>(json);
                if (_config != null) return;
            }
        }
        catch (Exception ex)
        {
            Log($"Config load error: {ex.Message}");
        }

        _config = new MapVoterConfig();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(_config, Newtonsoft.Json.Formatting.Indented));
        }
        catch { }
    }

    private static string GetServerRoot()
    {
        string dataPath = Application.dataPath ?? "";
        if (string.IsNullOrEmpty(dataPath)) return ".";
        return Path.GetFullPath(Path.Combine(dataPath, ".."));
    }

    /// <summary>Returns true if we handled the message (caller should skip original).</summary>
    internal bool OnChatSay(BasePlayer player, string message)
    {
        if (player == null || _config == null) return false;
        if (GetUIisDisabled()) return false;
        string msg = message?.Trim();
        if (string.IsNullOrEmpty(msg)) return false;
        if (msg.StartsWith("/")) msg = msg.Substring(1).Trim();

        if (msg.Equals("mvote", StringComparison.OrdinalIgnoreCase))
        {
            string reply = HandleMvoteCommand(player);
            SendMessage(player, reply);
            OpenUI(player);
            return true;
        }

        if (msg.StartsWith("mvtest", StringComparison.OrdinalIgnoreCase) || msg.StartsWith("mapvotestart", StringComparison.OrdinalIgnoreCase))
        {
            if (!player.IsAdmin)
            {
                SendMessage(player, "Only admins can start a map vote.");
                return true;
            }
            SendMessage(player, StartVoteFromPool());
            OpenUI(player);
            return true;
        }

        if (msg.StartsWith("mvoteready", StringComparison.OrdinalIgnoreCase) || msg.StartsWith("mvotepost", StringComparison.OrdinalIgnoreCase))
        {
            if (!player.IsAdmin)
            {
                SendMessage(player, "Only admins can start a map vote. Use F1 console: mvoteready");
                return true;
            }
            SendMessage(player, StartVoteFromPool());
            OpenUI(player);
            return true;
        }

        bool isOpenCmd = false;
        foreach (var c in GetOpenCommands())
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var t = c.Trim();
            if (t.Equals("mvote", StringComparison.OrdinalIgnoreCase)) continue;
            if (msg.Equals(t, StringComparison.OrdinalIgnoreCase)) { isOpenCmd = true; break; }
        }
        if (!isOpenCmd) return false;

        Log($"OnChatSay: opening UI for {player?.displayName}");
        OpenUI(player);
        return true;
    }

    internal string GetOpenCommand()
    {
        foreach (var c in GetOpenCommands())
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var t = c.Trim();
            if (!t.Equals("mvote", StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return "vote";
    }

    /// <summary>Run MapVoter command from server console (no player). Returns true if we handled it. Use only Debug.Log to avoid deadlock.</summary>
    public bool TryRunServerConsoleCommand(string input)
    {
        if (!TryHandleDedicatedMapVoterLine(input, out var reply))
            return false;
        UnityEngine.Debug.Log("[MapVoter] Console: " + input.Trim());
        if (!string.IsNullOrEmpty(reply) && !reply.StartsWith("MapVoter:", StringComparison.Ordinal))
            UnityEngine.Debug.Log("[MapVoter] " + reply);
        return true;
    }

    /// <summary>Used by Patch_ConsoleSystem_Server_Find so server console Find() returns our commands. Game may pass "mvote", "global.mvote", or full RCON line (e.g. "global.discordvote 76561198000000000 4") — we match on first token only.</summary>
    public ConsoleSystem.Command GetMapVoterCommand(string strName)
    {
        if (string.IsNullOrEmpty(strName)) return null;
        // RCON/WebRCON and some code paths pass the full command line; take first token as command name.
        var parts = strName.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var n = (parts.Length > 0 ? parts[0] : strName.Trim()).ToLowerInvariant();
        if (n == "global.mvote" || n == "mvote") return _mvoteCommand;
        if (n == "global.mvoteready" || n == "mvoteready") return _mvotereadyCommand;
        if (n == "global.mvotepost" || n == "mvotepost") return _mvotepostCommand;
        if (n == "global.mvotewipe" || n == "mvotewipe") return _mvoteWipeCommand;
        if (n == "global.mvtest" || n == "mvtest") return _mvoteStartCommand;
        if (n == "global.mapvotestart" || n == "mapvotestart") return _mapvoteStartCommand;
        if ((n == "global.mapvote" || n == "mapvote") && _mapvoteAliasCommand != null) return _mapvoteAliasCommand;
        // Required: server console / WebRCON use Find(); Dict alone is not enough (see HARMONY_MODS_GUIDE.md).
        if (n == "global.discordvote" || n == "discordvote") return _discordBotVoteCommand;
        if (n == "global.mvotediscord" || n == "mvotediscord") return _mvoteDiscordCommand;
        return null;
    }

    private List<string> GetOpenCommands()
    {
        if (_config?.Commands?.MapVote == null) return new List<string> { "mvote" };
        var raw = _config.Commands.MapVote.Trim();
        if (string.IsNullOrEmpty(raw)) return new List<string> { "mvote" };
        var list = new List<string>();
        foreach (var s in raw.Split(',', ';'))
        {
            var t = s.Trim();
            if (!string.IsNullOrEmpty(t)) list.Add(t);
        }
        return list.Count > 0 ? list : new List<string> { "mvote" };
    }

    private bool GetUIisDisabled()
    {
        return _config?.Options?.UIisDisabled ?? false;
    }

    internal void HandleCuiCommand(BasePlayer player, string[] args)
    {
        if (player == null || args == null || args.Length < 1) return;
        string raw = args[0] as string ?? args[0]?.ToString() ?? "";
        if (!raw.StartsWith("MapVoter_")) return;
        string action = raw.Substring("MapVoter_".Length);

        if (action == "CLOSE")
        {
            if (player != null) _uiViewers.Remove(player.userID);
            DestroyUI(player);
            return;
        }

        if (action.StartsWith("VOTE_"))
        {
            string mapId = action.Substring(5);
            RecordVote(player, mapId);
            RefreshUI(player);
            return;
        }

        if (action.StartsWith("VIEW_"))
        {
            string mapId = action.Substring(5);
            var maps = GetVoteMaps();
            var m = maps?.Find(x => x.Id == mapId);
            if (m != null)
            {
                if (!string.IsNullOrEmpty(m.PngTextureId) && uint.TryParse(m.PngTextureId, out _))
                {
                    ShowFullscreenView(player, m);
                }
                else
                {
                    string link = !string.IsNullOrEmpty(m.ViewUrl) ? m.ViewUrl : m.ImageUrl;
                    if (!string.IsNullOrEmpty(link))
                        SendMessage(player, "Map link: " + link);
                }
            }
            return;
        }

        if (action == "CLOSE_VIEW")
        {
            DestroyUI(player, FULLSCREEN_PANEL);
            return;
        }

        if (action == "START")
        {
            StartVoteFromPool();
            RefreshUI(player);
            return;
        }

        if (action == "STOP")
        {
            StopVote();
            RefreshUI(player);
        }
    }

    private void HandleSteamVoteCommand(ConsoleSystem.Arg arg)
    {
        string result = ReplyForSteamVoteArgs(arg?.Args == null ? null : System.Array.ConvertAll(arg.Args, x => x.ToString()));
        arg?.ReplyWith(result);
    }

    private const string SteamVoteUsageReply = "ERROR: Usage: global.discordvote <steamId64> <mapIndex>";

    private static bool TryParseSteamVoteArgs(string steamStr, string mapIndexStr, out ulong steamId, out int mapIndex)
    {
        steamId = 0UL;
        mapIndex = 0;
        if (string.IsNullOrEmpty(steamStr) || !ulong.TryParse(steamStr.Trim(), out steamId) || steamId == 0UL)
            return false;
        if (!int.TryParse(mapIndexStr?.Trim(), out mapIndex) || mapIndex < 1)
            return false;
        return true;
    }

    /// <summary>Used by ConsoleSystem.Arg handler and by raw server/RCON lines.</summary>
    private string ReplyForSteamVoteArgs(string[] args)
    {
        if (args == null || args.Length < 2)
            return SteamVoteUsageReply;
        if (!TryParseSteamVoteArgs(args[0], args[1], out ulong steamId, out int mapIndex))
            return "ERROR";
        return RecordVoteFromSteam(steamId, mapIndex);
    }

    /// <summary>Args after the command token (e.g. <c>76561198... 4</c>).</summary>
    private string ReplyForSteamVoteRemainder(string remainder)
    {
        if (string.IsNullOrWhiteSpace(remainder))
            return SteamVoteUsageReply;
        var ap = remainder.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (ap.Length < 2)
            return SteamVoteUsageReply;
        return ReplyForSteamVoteArgs(new[] { ap[0], ap[1] });
    }

    /// <summary><c>"global."</c> is 7 characters — using <c>Substring(8)</c> breaks every <c>global.*</c> command (e.g. <c>mapvote</c> → <c>apvote</c>).</summary>
    private static string StripGlobalConsolePrefix(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return cmd;
        const string prefix = "global.";
        return cmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? cmd.Substring(prefix.Length)
            : cmd;
    }

    /// <summary>
    /// Server console (<c>ServerConsole.Update</c>) often never calls <c>Find()</c>; WebRCON may also bypass it.
    /// Handle our commands here so <c>global.discordvote …</c> works from <c>&gt;</c> and from RCON.
    /// </summary>
    internal bool TryHandleDedicatedMapVoterLine(string input, out string reply)
    {
        reply = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var parts = input.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts.Length > 0 ? parts[0].Trim() : "";
        if (string.IsNullOrEmpty(cmd))
            return false;
        cmd = StripGlobalConsolePrefix(cmd);
        var norm = cmd.ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1].Trim() : "";

        if (norm == "mvote")
        {
            reply = HandleMvoteCommand(null);
            return true;
        }
        if (norm == "mvtest" || norm == "mapvotestart")
        {
            reply = StartVoteFromPool();
            return true;
        }
        if (norm == "mvoteready" || norm == "mvotepost")
        {
            reply = StartVoteFromPool();
            return true;
        }
        if (norm == "mvotewipes")
        {
            reply = BuildWipeCalendarReply();
            return true;
        }
        if (norm == "mvotewipe")
        {
            ForceWipeNow();
            reply = "Wipe queued. Restarting server in 60 seconds.";
            return true;
        }
        if (norm == "mapvote")
        {
            reply = "MapVoter: Admins use 'mvote' or 'mvoteready' to start a vote from the image pool. Players use 'vote' to open the UI.";
            return true;
        }
        if (norm == "discordvote")
        {
            reply = ReplyForSteamVoteRemainder(rest);
            return true;
        }
        if (norm == "mvotediscord")
        {
            ResendVoteToDiscord();
            reply = "OK";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Record vote from Discord bot after linking resolves Discord→Steam (e.g. Platform Sync).
    /// Tallies go to <see cref="SaveVoteStateToFile"/> (current_vote_state.json). Optional audit line in discord_steam_vote_audit.log.
    /// </summary>
    internal string RecordVoteFromSteam(ulong steamId, int mapIndex)
    {
        if (!_voteActive)
        {
            return "NO_VOTE";
        }

        var maps = GetVoteMaps();
        if (maps == null || maps.Count == 0)
        {
            return "NO_VOTE";
        }
        if (mapIndex < 1 || mapIndex > maps.Count)
        {
            return "INVALID_INDEX";
        }

        string mapId = maps[mapIndex - 1]?.Id;
        if (string.IsNullOrEmpty(mapId))
        {
            return "ERROR";
        }

        if (_votedPlayers.Contains(steamId))
        {
            return "ALREADY_VOTED";
        }

        _votedPlayers.Add(steamId);
        _votes.TryGetValue(mapId, out int count);
        _votes[mapId] = count + 1;
        SaveVoteStateToFile();
        TryAppendSteamVoteAudit(steamId, mapIndex, mapId);
        SendToDiscordVoteUpdate();
        Log($"MapVoter: Steam vote recorded for {mapId} (Steam: {steamId})");
        return "OK";
    }

    private void TryAppendSteamVoteAudit(ulong steamId, int mapIndex, string mapId)
    {
        try
        {
            var dir = GetVoteSeedsDirectory();
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "discord_steam_vote_audit.log");
            var line = $"{DateTime.UtcNow:o}\tsteam={steamId}\tmapIndex={mapIndex}\tmapId={mapId}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch { /* ignore */ }
    }

    private void RecordVote(BasePlayer player, string mapId)
    {
        if (!_voteActive) return;
        if (_votedPlayers.Contains(player.userID))
        {
            SendMessage(player, "You have already voted.");
            return;
        }

        _votedPlayers.Add(player.userID);
        _votes.TryGetValue(mapId, out int count);
        _votes[mapId] = count + 1;
        SaveVoteStateToFile();
        SendToDiscordVoteUpdate();
        SendMessage(player, $"Vote recorded for {mapId}.");
    }

    /// <summary>Admin /mvote: start a new vote from the image pool if none is active. Players: just open the UI.</summary>
    private string HandleMvoteCommand(BasePlayer player)
    {
        bool isAdmin = player == null || player.IsAdmin;
        if (isAdmin && !_voteActive && !_mapsLoading)
            return StartVoteFromPool();
        if (_mapsLoading)
            return "MapVoter: Loading map images from the pool...";
        if (_voteActive)
            return "Map vote is open. Pick a map in the UI.";
        return "No map vote is active. An admin can start one with mvote or mvoteready.";
    }

    /// <summary>Pick random maps from the local image pool, load them, post to Discord, and open the vote.</summary>
    private string StartVoteFromPool()
    {
        if (_mapsLoading)
            return "MapVoter: Still loading map images. Wait, then try again.";
        if (_voteActive && _currentVoteMaps.Count > 0)
        {
            SendToDiscordVoteStarted();
            return "MapVoter: Vote already open. Re-posted current maps to Discord.";
        }

        int mapSize = _config?.MapSize ?? 0;
        if (mapSize <= 0)
            return "MapVoter: Set 'Map size' (e.g. 4000) in HarmonyConfig/MapVoter.json first.";

        var pool = ScanImagePool(mapSize);
        if (pool.Count == 0)
        {
            string dir = GetImagesPath();
            Info($"MapVoter: No images in pool. Add files named {mapSize}_<seed>.png or .jpg to {dir}");
            return $"MapVoter: No map images found in {dir}. Add {mapSize}_<seed>.png files (pool can grow over time).";
        }

        int count = GetVoteMapCount();
        if (count > pool.Count) count = pool.Count;

        ShufflePool(pool);
        var picked = new List<int>(count);
        for (int i = 0; i < count; i++)
            picked.Add(pool[i].Seed);

        WriteVoteSeedsToFile(mapSize, picked);
        WriteAutoVoteCycleLock();

        _votes.Clear();
        _votedPlayers.Clear();
        _votedDiscordIds.Clear();
        _currentVoteMaps.Clear();
        DeleteVoteStateFile();

        StopModCoroutine(ref _voteEndAtWipeCoroutine);
        StopModCoroutine(ref _discordVoteRefreshCoroutine);
        StopModCoroutine(ref _loadMapsCoroutine);

        _voteActive = true;
        _mapsLoading = true;
        _discordVoteRefreshCoroutine = StartModCoroutine(DiscordVoteRefreshCoroutine());
        _loadMapsCoroutine = StartModCoroutine(LoadMapsAndActivateCoroutine(mapSize, picked, true));

        string msg = $"MapVoter: Picked {picked.Count} maps from a pool of {pool.Count} (size {mapSize}). Loading images and posting to Discord...";
        Info(msg);
        return msg;
    }

    private int GetVoteMapCount()
    {
        int n = _config?.NumberOfMaps ?? 8;
        if (n <= 0) n = 8;
        return Math.Max(1, Math.Min(12, n));
    }

    private struct PoolEntry
    {
        public int Size;
        public int Seed;
        public string Path;
    }

    /// <summary>Scan Images path for files named {size}_{seed}.png/.jpg/.jpeg matching the configured map size.</summary>
    private List<PoolEntry> ScanImagePool(int mapSize)
    {
        var list = new List<PoolEntry>();
        string dir = GetImagesPath();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return list;

        string[] files;
        try { files = Directory.GetFiles(dir); }
        catch (Exception ex)
        {
            Info($"MapVoter: Could not read image pool {dir}: {ex.Message}");
            return list;
        }

        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];
            string ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext)) continue;
            if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = Path.GetFileNameWithoutExtension(file);
            int us = name.IndexOf('_');
            if (us <= 0 || us >= name.Length - 1) continue;
            if (!int.TryParse(name.Substring(0, us), out int size) || size <= 0) continue;
            if (mapSize > 0 && size != mapSize) continue;
            if (!int.TryParse(name.Substring(us + 1), out int seed) || seed <= 0) continue;

            list.Add(new PoolEntry { Size = size, Seed = seed, Path = file });
        }
        return list;
    }

    private static void ShufflePool(List<PoolEntry> list)
    {
        var r = new System.Random(Guid.NewGuid().GetHashCode());
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = r.Next(i + 1);
            var tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    /// <summary>Legacy alias used by UI START button and older call sites.</summary>
    private void StartVote()
    {
        StartVoteFromPool();
    }

    /// <summary>Legacy alias: starting a vote now always picks from the image pool and posts to Discord.</summary>
    private void PostVoteFromSeeds()
    {
        StartVoteFromPool();
    }

    private IEnumerator VoteEndAtWipeCoroutine(TimeSpan timeUntilWipe)
    {
        yield return new WaitForSeconds((float)timeUntilWipe.TotalSeconds);
        _voteEndAtWipeCoroutine = null;
        StopVote();
    }

    /// <summary>While voting is active, refresh Discord cards hourly so "Next wipe" stays current even without new votes.</summary>
    private IEnumerator DiscordVoteRefreshCoroutine()
    {
        while (_voteActive)
        {
            yield return new WaitForSeconds(3600f);
            if (_voteActive && !_mapsLoading)
            {
                SendToDiscordVoteUpdate();
            }
        }
        _discordVoteRefreshCoroutine = null;
    }

        private void StopVote()
        {
        _voteActive = false;
        _mapsLoading = false;
        StopModCoroutine(ref _discordVoteRefreshCoroutine);
        StopModCoroutine(ref _voteEndAtWipeCoroutine);
        StopModCoroutine(ref _loadMapsCoroutine);
        DeleteVoteSeedsFile();
        DeleteVoteStateFile();
        string winner = GetWinner();
        string msg = winner != null ? $"Vote ended. Winner: {winner}" : "Vote ended.";
        Info(msg);
        SendToDiscord("vote_ended", "Map vote ended!", winner != null ? $"Winner: **{winner}**" : "No votes were cast.");
        }

    private string GetWinner()
    {
        string best = null;
        int bestCount = 0;
        foreach (var kv in _votes)
        {
            if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
        }
        return best;
    }

    private void OpenUI(BasePlayer player)
    {
        if (player == null) return;
        _uiViewers.Add(player.userID);
        DestroyUI(player);
        DestroyUI(player, FULLSCREEN_PANEL);
        string json = BuildMainUI(player);
        if (string.IsNullOrEmpty(json)) return;
        SendUI(player, json);
    }

    private void RefreshUI(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        DestroyUI(player);
        OpenUI(player);
    }

    private List<MapVoterConfig.MapOption> GetVoteMaps()
    {
        int mapSize = _config?.MapSize ?? 0;
        if (mapSize > 0)
            return _currentVoteMaps;
        return _config?.Maps ?? new List<MapVoterConfig.MapOption>();
    }

    private string GetImagesPath()
    {
        var path = _config?.ImagesPath?.Trim();
        if (!string.IsNullOrEmpty(path))
        {
            if (!Path.IsPathRooted(path))
                return Path.GetFullPath(Path.Combine(GetServerRoot(), path));
            return Path.GetFullPath(path);
        }
        return Path.Combine(GetServerRoot(), "HarmonyImages", "MapVoter");
    }

        private const string VOTE_SEEDS_FILENAME = "current_vote_seeds.txt";
        private const string VOTE_STATE_FILENAME = "current_vote_state.json";

        private string GetVoteSeedsDirectory() => Path.Combine(GetServerRoot(), "HarmonyData", "MapVoter");
        private string GetVoteSeedsFilePath() => Path.Combine(GetVoteSeedsDirectory(), VOTE_SEEDS_FILENAME);
        private string GetVoteStateFilePath() => Path.Combine(GetVoteSeedsDirectory(), VOTE_STATE_FILENAME);
        private string GetAutoVoteCycleFilePath() => Path.Combine(GetVoteSeedsDirectory(), "auto_vote_cycle.txt");

    private void WriteAutoVoteCycleLock()
    {
        try
        {
            string id = GetWipeCycleId();
            if (string.IsNullOrEmpty(id)) return;
            var dir = GetVoteSeedsDirectory();
            Directory.CreateDirectory(dir);
            File.WriteAllText(GetAutoVoteCycleFilePath(), id);
        }
        catch (Exception ex) { Log($"MapVoter: Could not write auto-vote cycle lock: {ex.Message}"); }
    }

    private bool IsAutoVoteCycleLocked()
    {
        try
        {
            string path = GetAutoVoteCycleFilePath();
            if (!File.Exists(path)) return false;
            string saved = File.ReadAllText(path)?.Trim();
            string current = GetWipeCycleId();
            return !string.IsNullOrEmpty(saved) && string.Equals(saved, current, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private void ClearAutoVoteCycleLock()
    {
        try
        {
            string path = GetAutoVoteCycleFilePath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private string GetWipeCycleId()
    {
        if (!TryGetNextWipe(out var wipe)) return "";
        return wipe.At.ToString("yyyy-MM-dd-HH-mm");
    }

    private struct UpcomingWipe
    {
        public DateTime At;
        public bool IsForced;
    }

    private static DateTime GetFirstWeekday(int year, int month, DayOfWeek day)
    {
        var d = new DateTime(year, month, 1);
        int diff = ((int)day - (int)d.DayOfWeek + 7) % 7;
        return d.AddDays(diff);
    }

    private static bool TryParseClock(string raw, TimeSpan fallback, out TimeSpan tod)
    {
        if (!string.IsNullOrWhiteSpace(raw) && TimeSpan.TryParse(raw.Trim(), out tod))
            return true;
        tod = fallback;
        return true;
    }

    private int GetMapWipeIntervalDays()
    {
        int interval = _config?.AutoWipe?.MapWipeIntervalDays ?? 0;
        if (interval > 0) return interval;
        var sched = _config?.AutoWipe?.MapWipeSchedule;
        if (sched != null)
        {
            for (int i = 0; i < sched.Count; i++)
            {
                if (sched[i] > 0) return sched[i];
            }
        }
        return 14;
    }

    /// <summary>
    /// Forced wipe: first Thursday of each month at Forced Wipe time.
    /// Map wipes: every N days after that Thursday at Wipe time, while still before the next forced wipe.
    /// </summary>
    private bool TryGetNextWipe(out UpcomingWipe wipe)
    {
        wipe = default;
        if (!TryParseClock(_config?.AutoWipe?.ForcedWipeTime, new TimeSpan(13, 45, 0), out var forcedTod))
            forcedTod = new TimeSpan(13, 45, 0);
        if (!TryParseClock(_config?.AutoWipe?.WipeTime, new TimeSpan(17, 30, 0), out var mapTod))
            mapTod = new TimeSpan(17, 30, 0);
        int interval = GetMapWipeIntervalDays();
        DateTime now = DateTime.Now;
        DateTime monthCursor = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        for (int m = 0; m < 8; m++)
        {
            DateTime month = monthCursor.AddMonths(m);
            DateTime firstThu = GetFirstWeekday(month.Year, month.Month, DayOfWeek.Thursday);
            DateTime nextMonth = month.AddMonths(1);
            DateTime nextFirstThu = GetFirstWeekday(nextMonth.Year, nextMonth.Month, DayOfWeek.Thursday);
            DateTime forcedAt = firstThu.Date + forcedTod;
            DateTime nextForcedAt = nextFirstThu.Date + forcedTod;

            if (forcedAt > now)
            {
                wipe = new UpcomingWipe { At = forcedAt, IsForced = true };
                return true;
            }

            int days = interval;
            while (days <= 62)
            {
                DateTime mapAt = firstThu.Date.AddDays(days) + mapTod;
                if (mapAt >= nextForcedAt)
                    break;
                if (mapAt > now)
                {
                    wipe = new UpcomingWipe { At = mapAt, IsForced = false };
                    return true;
                }
                days += interval;
            }
        }
        return false;
    }

    private void CollectUpcomingWipes(List<UpcomingWipe> list, int maxCount)
    {
        if (list == null || maxCount <= 0) return;
        if (!TryParseClock(_config?.AutoWipe?.ForcedWipeTime, new TimeSpan(13, 45, 0), out var forcedTod))
            forcedTod = new TimeSpan(13, 45, 0);
        if (!TryParseClock(_config?.AutoWipe?.WipeTime, new TimeSpan(17, 30, 0), out var mapTod))
            mapTod = new TimeSpan(17, 30, 0);
        int interval = GetMapWipeIntervalDays();
        DateTime now = DateTime.Now;
        DateTime monthCursor = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        for (int m = 0; m < 14 && list.Count < maxCount; m++)
        {
            DateTime month = monthCursor.AddMonths(m);
            DateTime firstThu = GetFirstWeekday(month.Year, month.Month, DayOfWeek.Thursday);
            DateTime nextMonth = month.AddMonths(1);
            DateTime nextFirstThu = GetFirstWeekday(nextMonth.Year, nextMonth.Month, DayOfWeek.Thursday);
            DateTime forcedAt = firstThu.Date + forcedTod;
            DateTime nextForcedAt = nextFirstThu.Date + forcedTod;

            if (forcedAt > now)
                list.Add(new UpcomingWipe { At = forcedAt, IsForced = true });
            if (list.Count >= maxCount) return;

            int days = interval;
            while (days <= 62 && list.Count < maxCount)
            {
                DateTime mapAt = firstThu.Date.AddDays(days) + mapTod;
                if (mapAt >= nextForcedAt)
                    break;
                if (mapAt > now)
                    list.Add(new UpcomingWipe { At = mapAt, IsForced = false });
                days += interval;
            }
        }
    }

    private string BuildWipeCalendarReply()
    {
        var list = new List<UpcomingWipe>();
        CollectUpcomingWipes(list, 8);
        if (list.Count == 0)
            return "MapVoter: Could not compute wipe calendar.";
        var sb = new StringBuilder();
        sb.Append("MapVoter wipe calendar (server local time):");
        for (int i = 0; i < list.Count; i++)
            sb.Append(" | ").Append(list[i].At.ToString("yyyy-MM-dd HH:mm")).Append(list[i].IsForced ? " FORCED" : " map");
        return sb.ToString();
    }

    private void LogWipeCalendar()
    {
        var list = new List<UpcomingWipe>();
        CollectUpcomingWipes(list, 6);
        if (list.Count == 0)
        {
            Info("MapVoter: Wipe calendar could not be computed.");
            return;
        }
        Info("MapVoter wipe calendar (server local time):");
        for (int i = 0; i < list.Count; i++)
        {
            string kind = list[i].IsForced ? "FORCED (first Thursday " + (_config?.AutoWipe?.ForcedWipeTime ?? "13:45") + ")" : "map wipe (" + (_config?.AutoWipe?.WipeTime ?? "17:30") + ")";
            Info("  " + list[i].At.ToString("yyyy-MM-dd HH:mm") + "  " + kind);
        }
        TryApplyWipeTimerOverride(list[0]);
    }

    private void TryApplyWipeTimerOverride(UpcomingWipe wipe)
    {
        try
        {
            var local = DateTime.SpecifyKind(wipe.At, DateTimeKind.Local);
            long unix = new DateTimeOffset(local).ToUnixTimeSeconds();
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "wipetimer.wipeUnixTimestampOverride", unix.ToString());
            Info("MapVoter: Set in-game wipe timer to " + wipe.At.ToString("yyyy-MM-dd HH:mm") + " (unix " + unix + ")");
        }
        catch (Exception ex)
        {
            Log("MapVoter: Could not set WipeTimer override: " + ex.Message);
        }
    }

    private void WriteVoteSeedsToFile(int mapSize, List<int> seeds)
    {
        if (seeds == null || seeds.Count == 0) return;
        try
        {
            var dir = GetVoteSeedsDirectory();
            Directory.CreateDirectory(dir);
            var path = GetVoteSeedsFilePath();
            var lines = new List<string> { mapSize.ToString() };
            foreach (var s in seeds) lines.Add(s.ToString());
            File.WriteAllLines(path, lines);
            Log($"MapVoter: Wrote {seeds.Count} seeds to {path}");
        }
        catch (Exception ex) { Log($"MapVoter: Could not write vote seeds file: {ex.Message}"); }
    }

    private void DeleteVoteSeedsFile()
    {
        try
        {
            var path = GetVoteSeedsFilePath();
            if (File.Exists(path))
            {
                File.Delete(path);
                Log("MapVoter: Deleted vote seeds file (vote ended/wipe)");
            }
        }
        catch (Exception ex) { Log($"MapVoter: Could not delete vote seeds file: {ex.Message}"); }
    }

    private void DeleteVoteStateFile()
    {
        try
        {
            var path = GetVoteStateFilePath();
            if (File.Exists(path))
            {
                File.Delete(path);
                Log("MapVoter: Deleted vote state file (vote ended/wipe)");
            }
        }
        catch (Exception ex) { Log($"MapVoter: Could not delete vote state file: {ex.Message}"); }
    }

    #region Auto-Wipe

    private const string WIPE_DATA_FILENAME = "autowipe_pending.json";
    private const string WIPE_DONE_FILENAME = "autowipe_last.json";

    private string GetWipeDataFilePath() => Path.Combine(GetServerRoot(), "HarmonyConfig", WIPE_DATA_FILENAME);
    private string GetWipeDoneFilePath() => Path.Combine(GetServerRoot(), "HarmonyConfig", WIPE_DONE_FILENAME);

    private static string GetServerCfgPath(string serverIdentity)
    {
        if (string.IsNullOrWhiteSpace(serverIdentity))
            serverIdentity = "main";
        var root = GetServerRootStatic();
        return Path.Combine(root, "server", serverIdentity.Trim(), "cfg", "server.cfg");
    }

    private static string GetServerRootStatic()
    {
        string dataPath = Application.dataPath ?? "";
        return string.IsNullOrEmpty(dataPath) ? "." : Path.GetFullPath(Path.Combine(dataPath, ".."));
    }

    private AutoWipeData LoadWipeData()
    {
        try
        {
            var path = GetWipeDataFilePath();
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<AutoWipeData>(json);
        }
        catch { return null; }
    }

    private void SaveWipeData(AutoWipeData data)
    {
        try
        {
            var path = GetWipeDataFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented));
        }
        catch (Exception ex) { Log($"MapVoter: Could not save wipe data: {ex.Message}"); }
    }

    private void ClearWipeData()
    {
        try
        {
            var path = GetWipeDataFilePath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private void HandlePostWipePluginDataWipe()
    {
        var statePath = Path.Combine(GetVoteSeedsDirectory(), "last_wipe_signal.txt");
        if (!WipeSignal.ShouldWipe(statePath)) return;

        AutoWipeData data = null;
        try
        {
            var donePath = GetWipeDoneFilePath();
            if (File.Exists(donePath))
                data = Newtonsoft.Json.JsonConvert.DeserializeObject<AutoWipeData>(File.ReadAllText(donePath));
        }
        catch { }

        DeleteVoteSeedsFile();
        DeleteVoteStateFile();
        WipeSignal.MarkWiped(statePath);
        Log("MapVoter: Cleared leftover vote after wipe signal.");

        if (data == null || !data.WasWipeDay) return;

        var sdw = _config?.ServerDataWipe;
        var lw = _config?.LogsWipe;
        var ow = _config?.OxideWipe;

        bool doServerForced = sdw != null && data.WasForcedWipe && sdw.EnableOnForcedWipeDay && sdw.FileNamesToDeleteOnForcedWipeDay?.Count > 0;
        bool doServerMap = sdw != null && !data.WasForcedWipe && sdw.EnableOnMapWipeDay && sdw.FileNamesToDeleteOnMapWipeDay?.Count > 0;
        bool doLogsForced = lw != null && data.WasForcedWipe && lw.EnableOnForcedWipeDay;
        bool doLogsMap = lw != null && !data.WasForcedWipe && lw.EnableOnMapWipeDay;
        bool doOxideForced = ow != null && data.WasForcedWipe && ow.EnableOnForcedWipeDay;
        bool doOxideMap = ow != null && !data.WasForcedWipe && ow.EnableOnMapWipeDay;
        bool doSeasonBlueprints = ShouldWipeBlueprintsThisWipe(data);

        if (!doServerForced && !doServerMap && !doLogsForced && !doLogsMap && !doOxideForced && !doOxideMap && !doSeasonBlueprints) return;

        // Server data wipe (server/{identity}/ - map files, player data, etc.)
        if (doServerForced || doServerMap)
        {
            var serverIdentity = _config?.AutoWipe?.ServerIdentity?.Trim() ?? "grimm";
            var serverDir = Path.Combine(GetServerRoot(), "server", serverIdentity);
            if (Directory.Exists(serverDir))
            {
                var dir = new DirectoryInfo(serverDir);
                var toDelete = doServerForced ? sdw.FileNamesToDeleteOnForcedWipeDay : sdw.FileNamesToDeleteOnMapWipeDay;
                int newSeed = data.MapSeed;
                int newSize = data.MapSize;
                bool isCustomMap = data.IsCustomMap;
                foreach (var pattern in toDelete ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(pattern)) continue;
                    try
                    {
                        foreach (var file in dir.EnumerateFiles("*" + pattern + "*"))
                        {
                            // For proceduralmap: don't delete the NEW map (format: proceduralmap.SIZE.SEED.MAPID.ext)
                            if (pattern.IndexOf("proceduralmap", StringComparison.OrdinalIgnoreCase) >= 0 && !isCustomMap && newSeed != 0 && newSize != 0)
                            {
                                var newMapMarker = "." + newSize + "." + newSeed + ".";
                                if (file.Name.IndexOf(newMapMarker, StringComparison.Ordinal) >= 0)
                                    continue; // Keep new map
                            }
                            file.Delete();
                            Log($"MapVoter: Deleted server data: {file.Name}");
                        }
                    }
                    catch (Exception ex) { Log($"MapVoter: Server data wipe error for {pattern}: {ex.Message}"); }
                }
            }
        }

        if (doSeasonBlueprints)
        {
            var serverIdentity = _config?.AutoWipe?.ServerIdentity?.Trim() ?? "grimm";
            var serverDir = Path.Combine(GetServerRoot(), "server", serverIdentity);
            if (Directory.Exists(serverDir))
            {
                try
                {
                    foreach (var file in new DirectoryInfo(serverDir).EnumerateFiles("*player.blueprints*"))
                    {
                        file.Delete();
                        Log($"MapVoter: Deleted season blueprint file: {file.Name}");
                    }
                }
                catch (Exception ex) { Log($"MapVoter: Season blueprint wipe error: {ex.Message}"); }
            }
        }

        // Logs wipe: delete every file in logs/. Skip the live logfile if Windows has it locked.
        if (doLogsForced || doLogsMap)
        {
            var logsDir = Path.Combine(GetServerRoot(), "logs");
            if (Directory.Exists(logsDir))
            {
                var dir = new DirectoryInfo(logsDir);
                foreach (var file in dir.EnumerateFiles())
                {
                    try
                    {
                        file.Delete();
                        Log($"MapVoter: Deleted log file: {file.Name}");
                    }
                    catch (Exception ex) { Log($"MapVoter: Logs wipe skip {file.Name}: {ex.Message}"); }
                }
            }
        }

        // Oxide wipe (only if oxide folder exists)
        if (doOxideForced || doOxideMap)
        {
            var oxideDir = Path.Combine(GetServerRoot(), "oxide");
            if (Directory.Exists(oxideDir))
            {
                if (ow.DeleteOxideLogsFolder)
                {
                    var oxideLogsDir = Path.Combine(oxideDir, "logs");
                    if (Directory.Exists(oxideLogsDir))
                    {
                        try
                        {
                            Directory.Delete(oxideLogsDir, true);
                            Log("MapVoter: Deleted oxide/logs folder");
                        }
                        catch (Exception ex) { Log($"MapVoter: Oxide logs folder delete error: {ex.Message}"); }
                    }
                }
                var oxideDataDir = Path.Combine(oxideDir, "data");
                foreach (var fileName in ow.OxideDataFilesToDelete ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(fileName)) continue;
                    try
                    {
                        var filePath = Path.Combine(oxideDataDir, fileName.Trim());
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            Log($"MapVoter: Deleted Oxide data: {fileName}");
                        }
                    }
                    catch (Exception ex) { Log($"MapVoter: Oxide data wipe error for {fileName}: {ex.Message}"); }
                }
            }
        }

        ClearWipeData();
    }

    private bool ShouldWipeBlueprintsThisWipe(AutoWipeData data)
    {
        if (data == null || !data.WasWipeDay) return false;
        var aw = _config?.AutoWipe;
        if (aw == null) return false;
        if (!data.WasForcedWipe) return false;
        if (aw.WipeBPsAtForcedWipeDay) return true;
        var months = aw.SeasonWipeMonths;
        if (months == null || months.Count == 0) return false;
        var at = data.WipeAt.Year > 2000 ? data.WipeAt : DateTime.Now;
        return months.Contains(at.Month);
    }

    private IEnumerator AutoWipeCheckCoroutine()
    {
        yield return new WaitForSeconds(30f);

        if (_config?.AutoWipe?.EnableAutoWipe != true) yield break;
        if (!TryGetNextWipe(out var next))
        {
            Info("MapVoter: Auto-wipe enabled but wipe calendar could not be computed.");
            yield break;
        }

        Info("MapVoter: Auto-wipe next event " + next.At.ToString("yyyy-MM-dd HH:mm") + (next.IsForced ? " FORCED" : " map") + ".");

        while (Instance != null && !_restartScheduled)
        {
            if (_config?.AutoWipe?.EnableAutoWipe != true) yield break;
            if (!TryGetNextWipe(out next))
            {
                yield return new WaitForSeconds(60f);
                continue;
            }

            TimeSpan timeUntilWipe = next.At - DateTime.Now;
            double minutesUntil = timeUntilWipe.TotalMinutes;
            int window = Math.Max(1, Math.Min(1440, _config?.AutoWipe?.ScheduleRestartWithinMinutes ?? 120));

            int ivOver2h = Math.Max(1, _config?.AutoWipe?.CheckIntervalMinutesWhenOver2h ?? 60);
            int iv30mTo2h = Math.Max(1, _config?.AutoWipe?.CheckIntervalMinutesWhen30mTo2h ?? 15);
            int ivUnder30m = Math.Max(1, _config?.AutoWipe?.CheckIntervalMinutesWhenUnder30m ?? 2);
            float nextCheckSeconds = minutesUntil > 120 ? ivOver2h * 60f : minutesUntil > 30 ? iv30mTo2h * 60f : ivUnder30m * 60f;
            if (minutesUntil > 48 * 60)
                nextCheckSeconds = Math.Max(nextCheckSeconds, 3600f);

            if (minutesUntil > 0 && minutesUntil <= window)
            {
                int restartSeconds = (int)Math.Ceiling(timeUntilWipe.TotalSeconds);
                if (restartSeconds < 60) restartSeconds = 60;

                string winner = GetWinner();
                int mapSize = _config?.MapSize ?? 4000;
                int seed;
                bool isCustomMap = _config?.AutoWipe?.CustomMap?.EnableCustomMap == true
                    && !string.IsNullOrWhiteSpace(_config?.AutoWipe?.CustomMap?.CustomMapUrl);

                if (isCustomMap)
                {
                    seed = 0;
                }
                else if (!string.IsNullOrEmpty(winner) && int.TryParse(winner, out seed))
                {
                    // Use vote winner
                }
                else
                {
                    seed = new System.Random(Guid.NewGuid().GetHashCode()).Next(100000, 2100000000);
                }

                var wipeData = new AutoWipeData
                {
                    IsWipeDay = true,
                    MapSeed = seed,
                    MapSize = mapSize,
                    IsCustomMap = isCustomMap,
                    CustomMapUrl = isCustomMap ? _config?.AutoWipe?.CustomMap?.CustomMapUrl?.Trim() ?? "" : "",
                    WasForcedWipe = next.IsForced,
                    WipeAt = next.At
                };
                SaveWipeData(wipeData);
                _restartScheduled = true;

                Info($"MapVoter: Auto-wipe - scheduling restart in {restartSeconds}s for {(next.IsForced ? "FORCED" : "map")} wipe at {next.At:yyyy-MM-dd HH:mm}. Map: {(isCustomMap ? "custom" : $"seed {seed}, size {mapSize}")}");
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "restart", restartSeconds.ToString());
                yield break;
            }

            yield return new WaitForSeconds(nextCheckSeconds);
        }
    }

    private IEnumerator AutoVoteCheckCoroutine()
    {
        yield return new WaitForSeconds(20f);
        Info("MapVoter: Auto-vote schedule enabled. Will pick maps from the image pool when the vote window opens.");

        while (Instance != null)
        {
            if (_config?.AutoVote?.EnableAutoVote != true)
            {
                yield return new WaitForSeconds(60f);
                continue;
            }
            if (_voteActive || _mapsLoading)
            {
                yield return new WaitForSeconds(60f);
                continue;
            }
            if (IsAutoVoteCycleLocked())
            {
                yield return new WaitForSeconds(60f);
                continue;
            }
            if (!TryGetScheduledVoteOpenTime(out DateTime opensAt))
            {
                yield return new WaitForSeconds(60f);
                continue;
            }
            if (DateTime.Now < opensAt)
            {
                yield return new WaitForSeconds(30f);
                continue;
            }

            Info($"MapVoter: Auto-vote window opened (scheduled {opensAt:yyyy-MM-dd HH:mm}). Starting vote from image pool.");
            StartVoteFromPool();
            yield return new WaitForSeconds(60f);
        }
    }

    private bool TryGetScheduledVoteOpenTime(out DateTime voteOpensAt)
    {
        voteOpensAt = default;
        if (!TryGetNextWipe(out var wipe)) return false;
        int daysBefore = Math.Max(0, _config?.AutoVote?.StartVotingDaysBeforeWipe ?? 4);
        if (!TryParseClock(_config?.AutoVote?.VoteStartTime, new TimeSpan(17, 0, 0), out var tod))
            tod = new TimeSpan(17, 0, 0);
        voteOpensAt = wipe.At.Date.AddDays(-daysBefore) + tod;
        return true;
    }

    /// <summary>Admin command: immediately queue wipe data and restart so wipe applies on next boot.</summary>
    private void ForceWipeNow()
    {
        try
        {
            string winner = GetWinner();
            int mapSize = _config?.MapSize ?? 4000;
            int seed;
            bool isCustomMap = _config?.AutoWipe?.CustomMap?.EnableCustomMap == true
                && !string.IsNullOrWhiteSpace(_config?.AutoWipe?.CustomMap?.CustomMapUrl);

            if (isCustomMap)
            {
                seed = 0;
            }
            else if (!string.IsNullOrEmpty(winner) && int.TryParse(winner, out seed))
            {
                // Use vote winner as wipe seed.
            }
            else
            {
                seed = new System.Random(Guid.NewGuid().GetHashCode()).Next(100000, 2100000000);
            }

            var wipeData = new AutoWipeData
            {
                IsWipeDay = true,
                MapSeed = seed,
                MapSize = mapSize,
                IsCustomMap = isCustomMap,
                CustomMapUrl = isCustomMap ? _config?.AutoWipe?.CustomMap?.CustomMapUrl?.Trim() ?? "" : "",
                WasForcedWipe = true,
                WipeAt = DateTime.Now
            };

            SaveWipeData(wipeData);
            _restartScheduled = true;
            Log($"MapVoter: mvotewipe queued. Restart in 60s. Map: {(isCustomMap ? "custom" : $"seed {seed}, size {mapSize}")}");
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "restart", "60");
        }
        catch (Exception ex)
        {
            Log($"MapVoter: mvotewipe failed: {ex.Message}");
        }
    }

    private void ServerWipe()
    {
        var data = LoadWipeData();
        if (data == null || !data.IsWipeDay) return;

        string serverIdentity = _config?.AutoWipe?.ServerIdentity?.Trim();
        if (string.IsNullOrEmpty(serverIdentity)) serverIdentity = "main";

        var serverCfgPath = GetServerCfgPath(serverIdentity);
        if (!File.Exists(serverCfgPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(serverCfgPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("server.port " + ConVar.Server.port);
                if (data.IsCustomMap)
                {
                    sb.AppendLine("//server.seed");
                    sb.AppendLine("//server.worldsize");
                    sb.AppendLine("server.levelurl " + (data.CustomMapUrl ?? ""));
                }
                else
                {
                    sb.AppendLine("server.seed " + data.MapSeed);
                    sb.AppendLine("server.worldsize " + data.MapSize);
                    sb.AppendLine("//server.levelurl");
                }
                sb.AppendLine("//Generated by MapVoter on wipe");
                File.WriteAllText(serverCfgPath, sb.ToString());
                Log($"MapVoter: Created server.cfg at {serverCfgPath}");
            }
            catch (Exception ex) { Log($"MapVoter: Could not create server.cfg: {ex.Message}"); }
            ClearWipeData();
            return;
        }

        try
        {
            var lines = new List<string>(File.ReadAllLines(serverCfgPath));
            bool hasSeed = false, hasWorldSize = false, hasLevelUrl = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var uncomm = line.TrimStart();
                if (uncomm.StartsWith("//")) uncomm = uncomm.Substring(2).TrimStart();

                if (uncomm.StartsWith("server.seed"))
                {
                    hasSeed = true;
                    lines[i] = data.IsCustomMap ? "//server.seed" : "server.seed " + data.MapSeed;
                }
                else if (uncomm.StartsWith("server.worldsize"))
                {
                    hasWorldSize = true;
                    lines[i] = data.IsCustomMap ? "//server.worldsize" : "server.worldsize " + data.MapSize;
                }
                else if (uncomm.StartsWith("server.levelurl"))
                {
                    hasLevelUrl = true;
                    lines[i] = data.IsCustomMap ? "server.levelurl " + (data.CustomMapUrl ?? "") : "//server.levelurl";
                }
            }

            if (!hasSeed) lines.Add(data.IsCustomMap ? "//server.seed" : "server.seed " + data.MapSeed);
            if (!hasWorldSize) lines.Add(data.IsCustomMap ? "//server.worldsize" : "server.worldsize " + data.MapSize);
            if (!hasLevelUrl) lines.Add(data.IsCustomMap ? "server.levelurl " + (data.CustomMapUrl ?? "") : "//server.levelurl");

            lines.RemoveAll(l => l.Trim().StartsWith("//Generated by MapVoter"));
            lines.Add("//Generated by MapVoter on wipe");

            File.WriteAllLines(serverCfgPath, lines);
            Log($"MapVoter: Updated server.cfg for wipe - {(data.IsCustomMap ? "custom map" : $"seed {data.MapSeed}, size {data.MapSize}")}");
        }
        catch (Exception ex) { Log($"MapVoter: Could not update server.cfg: {ex.Message}"); }
        finally
        {
            try
            {
                var done = new AutoWipeData
                {
                    WasWipeDay = true,
                    WasForcedWipe = data?.WasForcedWipe ?? false,
                    WipeAt = data?.WipeAt ?? DateTime.Now,
                    MapSeed = data?.MapSeed ?? 0,
                    MapSize = data?.MapSize ?? 0,
                    IsCustomMap = data?.IsCustomMap ?? false
                };
                var donePath = GetWipeDoneFilePath();
                var dir = Path.GetDirectoryName(donePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(donePath, Newtonsoft.Json.JsonConvert.SerializeObject(done));
                WipeSignal.Write(done.WipeAt, done.MapSeed, done.WasForcedWipe);
            }
            catch { }
            ClearWipeData();
        }
    }

    #endregion

    /// <summary>Read persisted vote seeds. Returns true if file exists and parses successfully.</summary>
    private bool TryReadVoteSeedsFromFile(out int mapSize, out List<int> seeds)
    {
        mapSize = 0;
        seeds = new List<int>();
        try
        {
            var path = GetVoteSeedsFilePath();
            if (!File.Exists(path)) return false;
            var lines = File.ReadAllLines(path);
            if (lines == null || lines.Length < 2) return false;
            if (!int.TryParse(lines[0]?.Trim(), out mapSize) || mapSize <= 0) return false;
            for (int i = 1; i < lines.Length; i++)
            {
                var s = lines[i]?.Trim();
                if (string.IsNullOrEmpty(s)) continue;
                if (int.TryParse(s, out int seed) && seed > 0)
                    seeds.Add(seed);
            }
            return seeds.Count > 0;
        }
        catch (Exception ex)
        {
            Log($"MapVoter: Could not read vote seeds file: {ex.Message}");
            return false;
        }
    }

    /// <summary>Persist current vote tallies and who has voted so they survive reloads.</summary>
    private void SaveVoteStateToFile()
    {
        try
        {
            if (!_voteActive) return;
            var path = GetVoteStateFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var data = new VoteStateData
            {
                Votes = new Dictionary<string, int>(_votes),
                VotedPlayers = new List<ulong>(_votedPlayers),
                VotedDiscordIds = new List<string>(_votedDiscordIds)
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Log($"MapVoter: Could not save vote state: {ex.Message}");
        }
    }

    /// <summary>Load persisted vote tallies if present.</summary>
    private void TryReadVoteStateFromFile()
    {
        try
        {
            var path = GetVoteStateFilePath();
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<VoteStateData>(json);
            if (data == null) return;

            _votes.Clear();
            _votedPlayers.Clear();
            _votedDiscordIds.Clear();

            if (data.Votes != null)
            {
                foreach (var kv in data.Votes)
                    _votes[kv.Key] = kv.Value;
            }
            if (data.VotedPlayers != null)
            {
                foreach (var id in data.VotedPlayers)
                    _votedPlayers.Add(id);
            }
            if (data.VotedDiscordIds != null)
            {
                foreach (var d in data.VotedDiscordIds)
                    if (!string.IsNullOrWhiteSpace(d))
                        _votedDiscordIds.Add(d);
            }
            Log("MapVoter: Restored vote state from file.");
        }
        catch (Exception ex)
        {
            Log($"MapVoter: Could not read vote state file: {ex.Message}");
        }
    }

    private static bool IsValidImage(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 4) return false;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        return false;
    }

    /// <summary>Resize with GDI+ (CPU, safe for ~10MB map PNGs). Returns PNG for FileStorage.</summary>
    private byte[] ResizeAndCompressForStorage(byte[] originalBytes)
    {
        int maxDim = Mathf.Clamp(_config?.MapImageMaxDimension ?? 768, 256, 2048);
        return ResizeWithGdi(originalBytes, maxDim, false);
    }

    /// <summary>Resize image for Discord payload (JPEG to keep POST size down).</summary>
    private byte[] ResizeForDiscord(byte[] originalBytes, int maxDimension)
    {
        int maxDim = Mathf.Clamp(maxDimension, 256, 1024);
        return ResizeWithGdi(originalBytes, maxDim, true);
    }

    private byte[] ResizeWithGdi(byte[] originalBytes, int maxDim, bool jpeg)
    {
        if (originalBytes == null || originalBytes.Length == 0) return null;
        try
        {
            using (var input = new MemoryStream(originalBytes))
            using (var src = System.Drawing.Image.FromStream(input, false, false))
            {
                int w = src.Width;
                int h = src.Height;
                if (w <= 0 || h <= 0) return null;
                float scale = 1f;
                if (w > maxDim || h > maxDim)
                    scale = Math.Min((float)maxDim / w, (float)maxDim / h);
                int nw = Math.Max(1, (int)(w * scale));
                int nh = Math.Max(1, (int)(h * scale));
                using (var bmp = new System.Drawing.Bitmap(nw, nh))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, nw, nh);
                    using (var output = new MemoryStream())
                    {
                        if (jpeg)
                        {
                            var codec = GetJpegCodec();
                            if (codec != null)
                            {
                                var eps = new System.Drawing.Imaging.EncoderParameters(1);
                                long q = Math.Max(50, Math.Min(95, _config?.MapImageJpegQuality ?? 75));
                                eps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, q);
                                bmp.Save(output, codec, eps);
                            }
                            else
                                bmp.Save(output, System.Drawing.Imaging.ImageFormat.Jpeg);
                        }
                        else
                            bmp.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                        return output.ToArray();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"MapVoter: Image resize failed: {ex.Message}");
            return null;
        }
    }

    private static System.Drawing.Imaging.ImageCodecInfo _jpegCodec;

    private static System.Drawing.Imaging.ImageCodecInfo GetJpegCodec()
    {
        if (_jpegCodec != null) return _jpegCodec;
        var codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
        for (int i = 0; i < codecs.Length; i++)
        {
            if (codecs[i].FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid)
            {
                _jpegCodec = codecs[i];
                break;
            }
        }
        return _jpegCodec;
    }

    /// <summary>Parse RustMaps API JSON. Returns (imageIconUrl, viewPageUrl).</summary>
    private static (string imageUrl, string viewUrl) TryParseMapInfo(string json)
    {
        if (string.IsNullOrEmpty(json)) return (null, null);
        try
        {
            var root = JObject.Parse(json);
            var data = root["data"];
            if (data == null) return (null, null);
            JObject obj = data as JObject;
            if (obj == null && data is JArray arr && arr.Count > 0)
                obj = arr[0] as JObject;
            if (obj == null) return (null, null);
            var imageUrl = (obj["imageIconUrl"] ?? obj["image_icon_url"] ?? obj["thumbnailUrl"] ?? obj["thumbnail_url"])?.ToString();
            var viewUrl = (obj["url"] ?? obj["pageUrl"] ?? obj["mapUrl"])?.ToString();
            var mapId = (obj["id"] ?? obj["mapId"])?.ToString();
            if (string.IsNullOrEmpty(viewUrl) && !string.IsNullOrEmpty(mapId))
                viewUrl = "https://rustmaps.com/map/" + mapId;
            return (imageUrl, viewUrl);
        }
        catch { }
        return (null, null);
    }

    private IEnumerator DownloadAndLoadMapsCoroutine()
    {
        int mapSize = _config?.MapSize ?? 0;
        int count = Math.Max(1, Math.Min(12, _config?.NumberOfMaps ?? 8));
        string tpl = _config?.RustMapsImageUrlTemplate?.Trim();
        if (string.IsNullOrEmpty(tpl)) tpl = "https://rustmaps.com/map/{0}_{1}";
        string apiKey = _config?.RustMapsApiKey?.Trim() ?? "";
        bool localOnly = _config?.UseLocalImagesOnly ?? false;

        var seeds = new List<int>();
        var seen = new HashSet<int>();
        var r = new System.Random(Guid.NewGuid().GetHashCode());
        while (seeds.Count < count)
        {
            int s = r.Next(100000, 2100000000);
            if (seen.Add(s)) seeds.Add(s);
        }

        WriteVoteSeedsToFile(mapSize, seeds);

        var imagesDir = GetImagesPath();
        Directory.CreateDirectory(imagesDir);

        var seedsToGenerate = new List<int>();
        foreach (var seed in seeds)
        {
            string localFile = Path.Combine(imagesDir, $"{mapSize}_{seed}.png");
            if (!File.Exists(localFile))
                seedsToGenerate.Add(seed);
        }
        if (seedsToGenerate.Count > 0)
        {
            string seedsFile = Path.Combine(imagesDir, "seeds_to_generate.txt");
            try
            {
                var lines = new List<string> { mapSize.ToString() };
                foreach (var s in seedsToGenerate) lines.Add(s.ToString());
                File.WriteAllLines(seedsFile, lines);
                Log($"MapVoter: Wrote {seedsToGenerate.Count} seeds to {seedsFile} - run GenerateMapImages.ps1 to create images");
            }
            catch (Exception ex) { Log($"MapVoter: Could not write seeds file: {ex.Message}"); }
        }

        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed)
        {
            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (e is CommunityEntity c && c != null && !c.IsDestroyed) { ce = c; break; }
            }
        }
        if (ce == null || ce.IsDestroyed)
        {
            Log("DownloadAndLoadMaps: CommunityEntity not found");
            _mapsLoading = false;
            yield break;
        }

        var seedToMapId = new Dictionary<int, string>();
        var seedToViewUrl = new Dictionary<int, string>();
        var seedToImageUrl = new Dictionary<int, string>();
        int fetched = 0;
        foreach (var seed in seeds)
        {
            if (_downloadCancelled) break;
            string localFile = Path.Combine(imagesDir, $"{mapSize}_{seed}.png");
            if (File.Exists(localFile))
            {
                seedToViewUrl[seed] = $"https://rustmaps.com/map/{mapSize}_{seed}";
                fetched++;
                continue;
            }
            if (localOnly) continue;
            if (string.IsNullOrEmpty(apiKey)) continue;

            string imageUrl = null;
            string viewUrl = null;
            string mapId = null;

            if (!string.IsNullOrEmpty(apiKey))
            {
                string uriGet = $"https://api.rustmaps.com/v4/maps/{mapSize}/{seed}?staging=false";
                using (var req = UnityWebRequest.Get(uriGet))
                {
                    req.timeout = 15;
                    req.SetRequestHeader("X-API-Key", apiKey);
                    req.SetRequestHeader("accept", "application/json");
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200 && req.downloadHandler?.data != null)
                    {
                        try
                        {
                            var info = TryParseMapInfo(req.downloadHandler.text);
                            imageUrl = info.imageUrl;
                            viewUrl = info.viewUrl ?? ($"https://rustmaps.com/map/{mapSize}_{seed}");
                        }
                        catch { }
                    }
                }
                if (string.IsNullOrEmpty(imageUrl))
                {
                    string uriPath = $"https://api.rustmaps.com/v4/maps/{mapSize}/{seed}?staging=false";
                    using (var req = UnityWebRequest.Get(uriPath))
                    {
                        req.timeout = 15;
                        req.SetRequestHeader("X-API-Key", apiKey);
                        req.SetRequestHeader("accept", "application/json");
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200 && req.downloadHandler?.data != null)
                        {
                            try
                            {
                                var info = TryParseMapInfo(req.downloadHandler.text);
                                imageUrl = info.imageUrl;
                                viewUrl = info.viewUrl ?? ($"https://rustmaps.com/map/{mapSize}_{seed}");
                            }
                            catch { }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(imageUrl))
                {
                    seedToViewUrl[seed] = viewUrl ?? imageUrl;
                    seedToImageUrl[seed] = imageUrl;
                    using (var req = UnityWebRequest.Get(imageUrl))
                    {
                        req.timeout = 20;
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success && req.downloadHandler?.data != null)
                        {
                            var bytes = req.downloadHandler.data;
                            if (bytes != null && bytes.Length > 4)
                            {
                                uint magic = (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
                                bool isPng = magic == 0x89504E47;
                                bool isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
                                if (isPng || isJpeg)
                                {
                                    try
                                    {
                                        File.WriteAllBytes(localFile, bytes);
                                        fetched++;
                                        Log($"RustMaps seed {seed}: image from GET lookup");
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    continue;
                }

                long postCode = 0;
                var postBody = new JObject { ["size"] = mapSize, ["seed"] = seed, ["staging"] = false };
                byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(postBody.ToString());
                using (var req = new UnityWebRequest("https://api.rustmaps.com/v4/maps", "POST"))
                {
                    var uploadHandler = new UploadHandlerRaw(bodyBytes);
                    uploadHandler.contentType = "application/json";
                    req.uploadHandler = uploadHandler;
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.timeout = 30;
                    req.SetRequestHeader("X-API-Key", apiKey);
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("accept", "application/json");
                    yield return req.SendWebRequest();
                    postCode = req.responseCode;
                    if (req.result == UnityWebRequest.Result.Success && req.downloadHandler?.data != null)
                    {
                        try
                        {
                            var resp = JObject.Parse(req.downloadHandler.text);
                            var data = resp["data"];
                            mapId = data?["mapId"]?.ToString() ?? data?["id"]?.ToString();
                        }
                        catch { }
                    }
                    else if (req.responseCode == 409 && req.downloadHandler?.data != null)
                    {
                        try
                        {
                            var resp = JObject.Parse(req.downloadHandler.text);
                            mapId = resp["data"]?["id"]?.ToString();
                            if (!string.IsNullOrEmpty(mapId))
                                Log($"RustMaps seed {seed}: 409 map exists but not ready, polling...");
                        }
                        catch { }
                    }
                    else
                    {
                        var err = req.error ?? "";
                        if (req.downloadHandler?.text != null)
                            err = req.downloadHandler.text.Length > 150 ? req.downloadHandler.text.Substring(0, 150) + "..." : req.downloadHandler.text;
                        Log($"RustMaps POST seed {seed}: HTTP {(int)req.responseCode} - {err}");
                    }
                }
                if (string.IsNullOrEmpty(mapId)) continue;

                if (postCode == 201 || postCode == 409)
                {
                    Log($"RustMaps seed {seed}: queued, waiting 65s...");
                    yield return new WaitForSeconds(65f);
                }
                if (_downloadCancelled) break;

                imageUrl = null;
                viewUrl = null;
                for (int retry = 0; retry < 3 && string.IsNullOrEmpty(imageUrl) && !_downloadCancelled; retry++)
                {
                    if (retry > 0)
                    {
                        Log($"RustMaps seed {seed}: retry {retry}/3 in 25s...");
                        yield return new WaitForSeconds(25f);
                    }
                    if (_downloadCancelled) break;
                    using (var req = UnityWebRequest.Get($"https://api.rustmaps.com/v4/maps/{mapId}"))
                    {
                        req.timeout = 25;
                        req.SetRequestHeader("X-API-Key", apiKey);
                        req.SetRequestHeader("accept", "application/json");
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success && req.downloadHandler?.data != null)
                        {
                            try
                            {
                                string json = req.downloadHandler.text;
                                var info = TryParseMapInfo(json);
                                imageUrl = info.imageUrl;
                                viewUrl = info.viewUrl ?? ("https://rustmaps.com/map/" + mapId);
                            }
                            catch { }
                        }
                    }
                }
                seedToMapId[seed] = mapId;
                if (!string.IsNullOrEmpty(viewUrl)) seedToViewUrl[seed] = viewUrl;
                if (!string.IsNullOrEmpty(imageUrl)) seedToImageUrl[seed] = imageUrl;

                if (!string.IsNullOrEmpty(imageUrl))
                {
                    using (var req = UnityWebRequest.Get(imageUrl))
                    {
                        req.timeout = 20;
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success && req.downloadHandler?.data != null)
                        {
                            var bytes = req.downloadHandler.data;
                            if (bytes != null && bytes.Length > 4)
                            {
                                uint magic = (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
                                bool isPng = magic == 0x89504E47;
                                bool isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
                                if (isPng || isJpeg)
                                {
                                    try
                                    {
                                        File.WriteAllBytes(localFile, bytes);
                                        fetched++;
                                        Log($"RustMaps seed {seed}: image saved");
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
            }
        }

        if (fetched == 0 && !string.IsNullOrEmpty(apiKey) && !localOnly)
            Log("MapVoter: No images downloaded - check API key at rustmaps.com/user/profile (free: 250/month)");

        var newMaps = new List<MapVoterConfig.MapOption>();
        foreach (var seed in seeds)
        {
            string localFile = Path.Combine(imagesDir, $"{mapSize}_{seed}.png");
            string pngId = "";
            if (File.Exists(localFile))
            {
                try
                {
                    var bytes = File.ReadAllBytes(localFile);
                    if (IsValidImage(bytes))
                    {
                        var toStore = ResizeAndCompressForStorage(bytes);
                        if (toStore == null || toStore.Length == 0) toStore = bytes;
                        if (toStore != null && toStore.Length > 0)
                        {
                            var crc = FileStorage.server.Store(toStore, FileStorage.Type.png, ce.net.ID);
                            pngId = crc.ToString();
                        }
                    }
                }
                catch { }
            }

            string imageUrl = seedToImageUrl.TryGetValue(seed, out var imgUrl) ? imgUrl : string.Format(tpl, mapSize, seed);
            string viewUrl = seedToViewUrl.TryGetValue(seed, out var v) ? v : imageUrl;
            newMaps.Add(new MapVoterConfig.MapOption
            {
                Id = seed.ToString(),
                Name = "Seed: " + seed,
                ImageUrl = imageUrl,
                ViewUrl = viewUrl,
                PngTextureId = pngId
            });
        }

        if (_downloadCancelled)
        {
            _mapsLoading = false;
            yield break;
        }

        _currentVoteMaps.Clear();
        _currentVoteMaps.AddRange(newMaps);
        _mapsLoading = false;
        Log($"MapVoter: Loaded {_currentVoteMaps.Count} maps from {imagesDir}");
        SendToDiscordVoteStarted();
    }

    private CommunityEntity FindCommunityEntity()
    {
        var ce = CommunityEntity.ServerInstance;
        if (ce != null && !ce.IsDestroyed) return ce;
        if (BaseNetworkable.serverEntities == null) return null;
        foreach (var e in BaseNetworkable.serverEntities)
        {
            if (e is CommunityEntity c && c != null && !c.IsDestroyed)
                return c;
        }
        return null;
    }

    private string FindImageFileForSeed(int mapSize, int seed)
    {
        var imagesDir = GetImagesPath();
        string png = Path.Combine(imagesDir, mapSize + "_" + seed + ".png");
        if (File.Exists(png)) return png;
        string jpg = Path.Combine(imagesDir, mapSize + "_" + seed + ".jpg");
        if (File.Exists(jpg)) return jpg;
        string jpeg = Path.Combine(imagesDir, mapSize + "_" + seed + ".jpeg");
        if (File.Exists(jpeg)) return jpeg;
        return null;
    }

    private void RefreshOpenVoteUIs()
    {
        if (_uiViewers.Count == 0) return;
        var ids = new List<ulong>(_uiViewers);
        for (int i = 0; i < ids.Count; i++)
        {
            var player = BasePlayer.FindByID(ids[i]);
            if (player == null || player.net?.connection == null)
            {
                _uiViewers.Remove(ids[i]);
                continue;
            }
            RefreshUI(player);
        }
    }

    /// <summary>Load picked seeds from the image pool. Optionally post to Discord and run until wipe.</summary>
    private IEnumerator LoadMapsAndActivateCoroutine(int mapSize, List<int> seeds, bool postToDiscord)
    {
        _voteActive = true;
        _mapsLoading = true;

        string tpl = _config?.RustMapsImageUrlTemplate?.Trim();
        if (string.IsNullOrEmpty(tpl)) tpl = "https://rustmaps.com/map/{0}_{1}";

        var ce = FindCommunityEntity();
        if (ce == null)
        {
            Info("MapVoter: CommunityEntity not ready, retrying in 2s...");
            yield return new WaitForSeconds(2f);
            ce = FindCommunityEntity();
        }
        if (ce == null)
        {
            Info("MapVoter: CommunityEntity not found - cannot load map images for UI.");
            _mapsLoading = false;
            yield break;
        }

        var newMaps = new List<MapVoterConfig.MapOption>();
        int loadedImages = 0;
        for (int i = 0; i < seeds.Count; i++)
        {
            if (_downloadCancelled) break;
            int seed = seeds[i];
            string localFile = FindImageFileForSeed(mapSize, seed);
            string pngId = "";
            string imageDataBase64 = "";
            if (!string.IsNullOrEmpty(localFile) && File.Exists(localFile))
            {
                byte[] bytes = null;
                try { bytes = File.ReadAllBytes(localFile); }
                catch (Exception ex) { Info($"MapVoter: Failed reading {localFile}: {ex.Message}"); }
                if (bytes != null && IsValidImage(bytes))
                {
                    var toStore = ResizeAndCompressForStorage(bytes);
                    if (toStore == null || toStore.Length == 0) toStore = bytes;
                    if (toStore != null && toStore.Length > 0)
                    {
                        try
                        {
                            var crc = FileStorage.server.Store(toStore, FileStorage.Type.png, ce.net.ID);
                            pngId = crc.ToString();
                        }
                        catch (Exception ex) { Info($"MapVoter: FileStorage store failed for seed {seed}: {ex.Message}"); }
                        var discordBytes = ResizeForDiscord(bytes, _config?.DiscordImageMaxDimension ?? 512);
                        imageDataBase64 = (discordBytes != null && discordBytes.Length > 0)
                            ? Convert.ToBase64String(discordBytes)
                            : Convert.ToBase64String(toStore);
                        loadedImages++;
                    }
                }
                yield return null;
            }
            else
            {
                Info($"MapVoter: No image for seed {seed} in {GetImagesPath()}");
            }

            newMaps.Add(new MapVoterConfig.MapOption
            {
                Id = seed.ToString(),
                Name = "Seed: " + seed,
                ImageUrl = string.Format(tpl, mapSize, seed),
                ViewUrl = $"https://rustmaps.com/map/{mapSize}_{seed}",
                PngTextureId = pngId,
                ImageDataBase64 = imageDataBase64
            });
        }

        if (_downloadCancelled)
        {
            _mapsLoading = false;
            _loadMapsCoroutine = null;
            yield break;
        }

        _currentVoteMaps.Clear();
        _currentVoteMaps.AddRange(newMaps);
        _mapsLoading = false;
        _loadMapsCoroutine = null;
        Info($"MapVoter: Loaded {_currentVoteMaps.Count} maps ({loadedImages} with images) from {GetImagesPath()}.");

        RefreshOpenVoteUIs();

        if (postToDiscord)
        {
            Info("MapVoter: Posting vote to Discord...");
            SendToDiscordVoteStarted();
        }
        else
        {
            Info("MapVoter: Vote restored (Discord not posted on reload). Run mvotediscord to send maps to Discord.");
        }

        StopModCoroutine(ref _voteEndAtWipeCoroutine);
        if (TryGetNextWipe(out var nextWipe))
        {
            var timeUntilWipe = nextWipe.At - DateTime.Now;
            if (timeUntilWipe.TotalSeconds > 1)
            {
                _voteEndAtWipeCoroutine = StartModCoroutine(VoteEndAtWipeCoroutine(timeUntilWipe));
                Info($"MapVoter: Vote open until next wipe {nextWipe.At:yyyy-MM-dd HH:mm} ({timeUntilWipe.TotalDays:F1} days). Players: /vote");
            }
        }
        else
        {
            Info("MapVoter: Vote open. Wipe calendar not available - vote runs until stopped. Players: /vote");
        }
    }

    private IEnumerator RestoreVoteFromFileCoroutine(int mapSize, List<int> seeds)
    {
        yield return LoadMapsAndActivateCoroutine(mapSize, seeds, false);
    }

    private IEnumerator LoadMapsFromDiskAndActivateVoteCoroutine(int mapSize, List<int> seeds)
    {
        yield return LoadMapsAndActivateCoroutine(mapSize, seeds, true);
    }

    private string BuildMainUI(BasePlayer player)
    {
        var maps = GetVoteMaps();
        if (maps == null || maps.Count == 0)
        {
            string msg = _mapsLoading
                ? "Loading map images..."
                : _config?.MapSize > 0
                    ? "No vote active. Admin: /mvote starts a vote from the image pool. Players: /vote"
                    : "Set 'Map size' (e.g. 4000) in HarmonyConfig/MapVoter.json";
            return BuildSimplePanel(msg);
        }

        var elements = new List<JObject>();
        const string parent = "Overlay";

        var mainPanel = new JObject
        {
            ["name"] = UI_PANEL,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.08 0.08 0.12 0.97", ["sprite"] = "Assets/Content/UI/UI.Background.Tile.psd" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.02 0.02", ["anchormax"] = "0.98 0.98" },
                new JObject { ["type"] = "NeedsCursor" }
            }
        };
        elements.Add(mainPanel);

        elements.Add(CreateLabel("MapVoter_Title", UI_PANEL, "MAP VOTING MANAGER", 22, "0.02 0.95", "0.9 0.99"));
        elements.Add(CreateLabel("MapVoter_Status", UI_PANEL, _voteActive ? "Voting open - pick a map!" : "Vote not active", 12, "0.02 0.90", "0.9 0.94"));
        AddButton(elements, "MapVoter_Close", UI_PANEL, "0.93 0.95", "0.98 0.99", "cui.endtest MapVoter_CLOSE", "X", 14);

        const string GRID_NAME = "MapVoter_Grid";
        elements.Add(new JObject
        {
            ["name"] = GRID_NAME,
            ["parent"] = UI_PANEL,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.02 0.02", ["anchormax"] = "0.98 0.87" },
                new JObject
                {
                    ["type"] = "UnityEngine.UI.GridLayoutGroup",
                    ["cellSize"] = "280 280",
                    ["spacing"] = "12 12",
                    ["startCorner"] = "UpperLeft",
                    ["startAxis"] = "Horizontal",
                    ["childAlignment"] = "UpperCenter",
                    ["constraint"] = "FixedColumnCount",
                    ["constraintCount"] = 4,
                    ["padding"] = "8 8 8 8"
                }
            }
        });

        for (int i = 0; i < maps.Count; i++)
            CreateMapCard(elements, player, maps[i], i, GRID_NAME);

        return Newtonsoft.Json.JsonConvert.SerializeObject(elements);
    }

    private void CreateMapCard(List<JObject> elements, BasePlayer player, MapVoterConfig.MapOption map, int index, string gridParent)
    {
        string cardName = $"MapVoter_Card_{index}";
        bool hasVoted = player != null && _votedPlayers.Contains(player.userID);
        string btnCmd = "cui.endtest MapVoter_VOTE_" + map.Id;
        string viewCmd = "cui.endtest MapVoter_VIEW_" + map.Id;

        elements.Add(new JObject
        {
            ["name"] = cardName,
            ["parent"] = gridParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.15 0.15 0.2 0.95", ["sprite"] = BtnSprite }
            }
        });

        if (!string.IsNullOrEmpty(map.PngTextureId) && uint.TryParse(map.PngTextureId, out _))
        {
            elements.Add(new JObject
            {
                ["name"] = cardName + "_img",
                ["parent"] = cardName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.RawImage", ["png"] = map.PngTextureId, ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.15 0.22", ["anchormax"] = "0.85 0.92" }
                }
            });
        }

        elements.Add(CreateLabel(cardName + "_seed", cardName, "Seed: " + map.Id, 12, "0.03 0.1", "0.97 0.28", "MiddleCenter"));

        AddButton(elements, cardName + "_view", cardName, "0.10 0.02", "0.45 0.12", viewCmd, "View", 10);

        if (!hasVoted && _voteActive)
        {
            AddButton(elements, cardName + "_vote", cardName, "0.50 0.02", "0.85 0.12", btnCmd, "VOTE", 11);
        }
        else if (hasVoted)
        {
            elements.Add(CreateLabel(cardName + "_voted", cardName, "Voted", 11, "0.10 0.02", "0.90 0.12", "MiddleCenter"));
        }
    }

    private static JObject CreateLabel(string name, string parent, string text, int fontSize, string anchorMin, string anchorMax, string align = "MiddleLeft")
    {
        return new JObject
        {
            ["name"] = name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text ?? "", ["fontSize"] = fontSize, ["color"] = "1 1 1 1", ["align"] = align ?? "MiddleLeft" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax }
            }
        };
    }

    private const string BtnSprite = "Assets/Content/UI/UI.Background.Tile.psd";

    /// <summary>Creates button + label. Uses game default sprite path to avoid client NullRef.</summary>
    private static void AddButton(List<JObject> elements, string name, string parent, string anchorMin, string anchorMax, string command, string text, int fontSize)
    {
        elements.Add(new JObject
        {
            ["name"] = name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = command, ["color"] = "0.2 0.6 0.2 1", ["sprite"] = BtnSprite },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax }
            }
        });
        elements.Add(CreateLabel(name + "_lbl", name, text ?? "", fontSize, "0 0", "1 1", "MiddleCenter"));
    }

    private static string BuildSimplePanel(string message)
    {
        var elements = new List<JObject>
        {
            new JObject
            {
                ["name"] = UI_PANEL,
                ["parent"] = "Overlay",
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.1 0.1 0.15 0.95", ["sprite"] = "Assets/Content/UI/UI.Background.Tile.psd" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.25 0.25", ["anchormax"] = "0.75 0.75" },
                    new JObject { ["type"] = "NeedsCursor" }
                }
            },
            CreateLabel("MapVoter_Msg", UI_PANEL, message, 18, "0.08 0.35", "0.92 0.8", "MiddleCenter")
        };
        AddButton(elements, "MapVoter_Close", UI_PANEL, "0.25 0.08", "0.75 0.22", "cui.endtest MapVoter_CLOSE", "Close", 18);
        return Newtonsoft.Json.JsonConvert.SerializeObject(elements);
    }

    private void SendUI(BasePlayer player, string json)
    {
        if (player?.net?.connection == null || string.IsNullOrEmpty(json))
        {
            Log("SendUI: player or connection null, or empty json");
            return;
        }
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed)
        {
            if (BaseNetworkable.serverEntities != null)
                foreach (var e in BaseNetworkable.serverEntities)
                {
                    if (e is CommunityEntity c && c != null && !c.IsDestroyed) { ce = c; break; }
                }
        }
        if (ce == null || ce.IsDestroyed)
        {
            Log("SendUI: CommunityEntity not found - UI will not display");
            return;
        }
        try
        {
            ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
            Log($"SendUI: sent {json.Length} chars to {player.displayName}");
        }
        catch (Exception ex) { Log($"SendUI error: {ex.Message}"); }
    }

    private void DestroyUI(BasePlayer player, string panelName = null)
    {
        if (player?.net?.connection == null) return;
        string panel = panelName ?? UI_PANEL;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed)
        {
            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (e is CommunityEntity c && c != null && !c.IsDestroyed) { ce = c; break; }
            }
        }
        if (ce != null && !ce.IsDestroyed)
            ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), panel);
    }

    private void ShowFullscreenView(BasePlayer player, MapVoterConfig.MapOption map)
    {
        DestroyUI(player, FULLSCREEN_PANEL);
        string json = BuildFullscreenViewUI(map);
        if (!string.IsNullOrEmpty(json))
            SendUI(player, json);
    }

    private string BuildFullscreenViewUI(MapVoterConfig.MapOption map)
    {
        var elements = new List<JObject>();

        var panel = new JObject
        {
            ["name"] = FULLSCREEN_PANEL,
            ["parent"] = "Overlay",
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0.95", ["sprite"] = BtnSprite },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" },
                new JObject { ["type"] = "NeedsCursor" }
            }
        };
        elements.Add(panel);

        elements.Add(new JObject
        {
            ["name"] = FULLSCREEN_PANEL + "_img",
            ["parent"] = FULLSCREEN_PANEL,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.RawImage", ["png"] = map.PngTextureId, ["color"] = "1 1 1 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.02 0.02", ["anchormax"] = "0.98 0.96" }
            }
        });

        elements.Add(CreateLabel(FULLSCREEN_PANEL + "_seed", FULLSCREEN_PANEL, "Seed: " + map.Id, 16, "0.02 0.96", "0.9 0.99", "MiddleLeft"));

        AddButton(elements, FULLSCREEN_PANEL + "_close", FULLSCREEN_PANEL, "0.92 0.92", "0.98 0.98", "cui.endtest MapVoter_CLOSE_VIEW", "X", 16);

        return Newtonsoft.Json.JsonConvert.SerializeObject(elements);
    }

    private void DestroyAllUI()
    {
        if (BaseNetworkable.serverEntities == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        foreach (var e in BaseNetworkable.serverEntities)
        {
            if (e is BasePlayer p && p?.net?.connection != null)
            {
                ce.ClientRPC(RpcTarget.Player("DestroyUI", p.net.connection), UI_PANEL);
                ce.ClientRPC(RpcTarget.Player("DestroyUI", p.net.connection), FULLSCREEN_PANEL);
            }
        }
    }

    private static void SendMessage(BasePlayer player, string msg)
    {
        if (player == null) return;
        ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 0, 0, msg);
    }

    private void Info(string msg)
    {
        UnityEngine.Debug.Log($"[MapVoter] {msg}");
    }

    private void Log(string msg)
    {
        bool doLog = (_config?.Options?.ConsoleDebug ?? false) || (_config?.Options?.FileDebug ?? false) || _config == null;
        if (doLog)
            UnityEngine.Debug.Log($"[MapVoter] {msg}");
    }

    private void ResendVoteToDiscord()
    {
        if (_config?.Discord?.LogToDiscord != true)
        {
            UnityEngine.Debug.Log("[MapVoter] mvotediscord: Discord logging is disabled. Set 'Log to Discord (true/false)': true and channel IDs in HarmonyConfig/MapVoter.json");
            return;
        }
        string url = _config.Discord.BridgeUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url))
        {
            UnityEngine.Debug.Log("[MapVoter] mvotediscord: Bridge URL is empty. Set 'Discord bridge URL' in config.");
            return;
        }
        if (string.IsNullOrEmpty(_config.Discord.VoteChannelId))
        {
            UnityEngine.Debug.Log("[MapVoter] mvotediscord: Vote Channel id is empty. Set it in config.");
            return;
        }
        if (_voteActive)
        {
            UnityEngine.Debug.Log("[MapVoter] mvotediscord: Sending vote_started to Discord bridge...");
            SendToDiscordVoteStarted();
        }
        else
        {
            string winner = GetWinner();
            string desc = winner != null ? $"Winner: **{winner}** (manual resend)" : "No votes were cast. (manual resend)";
            UnityEngine.Debug.Log("[MapVoter] mvotediscord: Sending vote_ended to Discord bridge...");
            SendToDiscord("vote_ended", "Map vote ended (manual resend)", desc);
        }
    }

    private void SendToDiscordVoteStarted()
    {
        if (_config?.Discord?.LogToDiscord != true) return;
        string url = _config.Discord.BridgeUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(_config.Discord.VoteChannelId)) return;

        var maps = GetVoteMaps();
        if (maps == null || maps.Count == 0)
        {
            SendToDiscord("vote_started", "Map vote is open!", "Loading maps... Use /mvote in-game to vote.");
            return;
        }

        string nextWipe = GetNextWipeDisplay();

        int mapSize = _config?.MapSize ?? 0;
        string hostname = ConVar.Server.hostname ?? "Rust Server";

        var mapsPayload = new List<Dictionary<string, object>>();
        int i = 1;
        foreach (var m in maps)
        {
            _votes.TryGetValue(m.Id ?? "", out int voteCount);
            var mapEntry = new Dictionary<string, object>
            {
                ["id"] = m.Id ?? "",
                ["seed"] = m.Id ?? "",
                ["size"] = mapSize > 0 ? mapSize.ToString() : "",
                ["imageUrl"] = m.ImageUrl ?? string.Format(_config?.RustMapsImageUrlTemplate ?? "https://rustmaps.com/map/{0}_{1}", mapSize, m.Id),
                ["name"] = m.Name ?? $"Seed: {m.Id}",
                ["index"] = i++,
                ["votes"] = voteCount
            };
            // Bot on this machine reads C:\svr1\maps\images via mapsImagePath — skip huge base64.
            mapsPayload.Add(mapEntry);
        }

        var payload = new Dictionary<string, object>
        {
            ["event"] = "vote_started",
            ["channelId"] = _config.Discord.VoteChannelId,
            ["title"] = hostname,
            ["description"] = $"Map vote has started • Next wipe: {nextWipe}",
            ["mentionRole"] = FormatDiscordRoleMention(_config.Discord.MentionRole),
            ["hostname"] = hostname,
            ["nextWipe"] = nextWipe,
            ["maps"] = mapsPayload
        };

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
        StartModCoroutine(SendToDiscordCoroutine(GetBridgePostUrls(), json));
    }

    /// <summary>Discord only pings a role as &lt;@&amp;id&gt;. Accepts a raw snowflake, that mention, @everyone, or existing markup.</summary>
    private static string FormatDiscordRoleMention(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim();
        if (s.Equals("@everyone", StringComparison.OrdinalIgnoreCase) || s.Equals("@here", StringComparison.OrdinalIgnoreCase))
            return s.ToLowerInvariant();
        if (s.StartsWith("<@", StringComparison.Ordinal) && s.EndsWith(">"))
            return s;
        if (s.Length >= 17 && s.Length <= 20 && ulong.TryParse(s, out _))
            return "<@&" + s + ">";
        return s;
    }

    /// <summary>Human-readable wipe ETA for Discord cards: "in 1d 2h 23m".</summary>
    private string GetNextWipeDisplay()
    {
        if (!TryGetNextWipe(out var wipe)) return "—";
        var span = wipe.At - DateTime.Now;
        if (span.TotalSeconds <= 0) return "soon";
        int days = (int)span.TotalDays;
        int hours = span.Hours;
        int minutes = span.Minutes;
        if (days > 0) return $"in {days}d {hours}h {minutes}m";
        if (hours > 0) return $"in {hours}h {minutes}m";
        if (minutes > 0) return $"in {minutes}m";
        return "soon";
    }

    /// <summary>Send live vote counts to Discord bridge so it can update map cards.</summary>
    private void SendToDiscordVoteUpdate()
    {
        if (_config?.Discord?.LogToDiscord != true) return;
        string url = _config.Discord.BridgeUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(_config.Discord.VoteChannelId)) return;

        var maps = GetVoteMaps();
        if (maps == null || maps.Count == 0) return;

        int mapSize = _config?.MapSize ?? 0;
        string hostname = ConVar.Server.hostname ?? "Rust Server";
        string nextWipe = GetNextWipeDisplay();

        var mapsPayload = new List<Dictionary<string, object>>();
        int i = 1;
        foreach (var m in maps)
        {
            _votes.TryGetValue(m.Id ?? "", out int voteCount);
            var mapEntry = new Dictionary<string, object>
            {
                ["id"] = m.Id ?? "",
                ["seed"] = m.Id ?? "",
                ["size"] = mapSize > 0 ? mapSize.ToString() : "",
                ["imageUrl"] = m.ImageUrl ?? string.Format(_config?.RustMapsImageUrlTemplate ?? "https://rustmaps.com/map/{0}_{1}", mapSize, m.Id),
                ["name"] = m.Name ?? $"Seed: {m.Id}",
                ["index"] = i++,
                ["votes"] = voteCount
            };
            mapsPayload.Add(mapEntry);
        }

        var payload = new Dictionary<string, object>
        {
            ["event"] = "vote_update",
            ["channelId"] = _config.Discord.VoteChannelId,
            ["title"] = hostname,
            ["nextWipe"] = nextWipe,
            ["maps"] = mapsPayload
        };

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
        StartModCoroutine(SendToDiscordCoroutine(GetBridgePostUrls(), json));
    }

    /// <summary>POST event to ticket-support-system mapvoterDiscordBridge. Uses coroutine to log success/failure.</summary>
    private void SendToDiscord(string eventType, string title, string description)
    {
        if (_config?.Discord?.LogToDiscord != true) return;
        string url = _config.Discord.BridgeUrl?.Trim().TrimEnd('/');
        string channelId = eventType == "vote_started" ? _config.Discord.VoteChannelId
            : eventType == "vote_ended" ? _config.Discord.WinningMapChannelId
            : _config.Discord.LogsChannelId;
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(channelId)) return;

        var payload = new Dictionary<string, object>
        {
            ["event"] = eventType,
            ["channelId"] = channelId,
            ["title"] = title ?? "",
            ["description"] = description ?? "",
            ["mentionRole"] = FormatDiscordRoleMention(_config.Discord.MentionRole)
        };
        // Bridge keys sessions by VoteChannelId; vote_ended posts to WinningMapChannelId — tell bridge which session to purge.
        if (eventType == "vote_ended" && !string.IsNullOrEmpty(_config.Discord.VoteChannelId))
            payload["voteChannelId"] = _config.Discord.VoteChannelId;

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

        StartModCoroutine(SendToDiscordCoroutine(GetBridgePostUrls(), json));
    }

    private static void AddBridgePostUrl(List<string> list, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        string b = baseUrl.Trim().TrimEnd('/');
        const string suffix = "/mapvoter";
        if (b.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            b = b.Substring(0, b.Length - suffix.Length).TrimEnd('/');
        string full = b + suffix;
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], full, StringComparison.OrdinalIgnoreCase))
                return;
        }
        list.Add(full);
    }

    /// <summary>Configured URL first, then loopback. Same-machine bot cannot be reached via the public IP (hairpin NAT).</summary>
    private List<string> GetBridgePostUrls()
    {
        var list = new List<string>();
        AddBridgePostUrl(list, _config?.Discord?.BridgeUrl);
        AddBridgePostUrl(list, "https://127.0.0.1:3921");
        AddBridgePostUrl(list, "http://127.0.0.1:3921");
        return list;
    }

    private IEnumerator SendToDiscordCoroutine(List<string> urls, string json)
    {
        if (urls == null || urls.Count == 0 || string.IsNullOrEmpty(json))
            yield break;

        byte[] body = Encoding.UTF8.GetBytes(json);
        string lastErr = "";
        for (int i = 0; i < urls.Count; i++)
        {
            string url = urls[i];
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 120;
                req.certificateHandler = new AcceptAllCertificatesHandler();
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200)
                {
                    UnityEngine.Debug.Log($"[MapVoter] Discord: Posted to bridge OK ({url})");
                    yield break;
                }

                lastErr = $"{req.result} HTTP {(int)req.responseCode} - {req.error ?? req.downloadHandler?.text ?? ""}";
                UnityEngine.Debug.LogWarning($"[MapVoter] Discord bridge {url} failed: {lastErr}");
            }
        }

        UnityEngine.Debug.LogWarning("[MapVoter] Discord bridge failed on all URLs. Rust and the ticket bot are on this machine — use https://127.0.0.1:3921 and confirm mapvoterDiscordBridge is listening on 3921.");
    }
}

/// <summary>MonoBehaviour host so MapVoter coroutines run even before ServerMgr exists.</summary>
internal sealed class MapVoterRunner : MonoBehaviour
{
}

/// <summary>Accepts self-signed certificates for Discord bridge (internal use only).</summary>
internal sealed class AcceptAllCertificatesHandler : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData) => true;
}

internal static class StringExt
{
    internal static string NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Persisted auto-wipe data. Written when scheduling restart, read on unload to update server.cfg.</summary>
internal class AutoWipeData
{
    [Newtonsoft.Json.JsonProperty("isWipeDay")]
    public bool IsWipeDay { get; set; }

    [Newtonsoft.Json.JsonProperty("wasWipeDay")]
    public bool WasWipeDay { get; set; }

    [Newtonsoft.Json.JsonProperty("mapSeed")]
    public int MapSeed { get; set; }

    [Newtonsoft.Json.JsonProperty("mapSize")]
    public int MapSize { get; set; }

    [Newtonsoft.Json.JsonProperty("isCustomMap")]
    public bool IsCustomMap { get; set; }

    [Newtonsoft.Json.JsonProperty("customMapUrl")]
    public string CustomMapUrl { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("wasForcedWipe")]
    public bool WasForcedWipe { get; set; }

    [Newtonsoft.Json.JsonProperty("wipeAt")]
    public DateTime WipeAt { get; set; }
}

internal class VoteStateData
{
    [Newtonsoft.Json.JsonProperty("votes")]
    public Dictionary<string, int> Votes { get; set; }

    [Newtonsoft.Json.JsonProperty("votedPlayers")]
    public List<ulong> VotedPlayers { get; set; }

    [Newtonsoft.Json.JsonProperty("votedDiscordIds")]
    public List<string> VotedDiscordIds { get; set; }
}
