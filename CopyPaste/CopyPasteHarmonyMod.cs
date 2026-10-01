using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CopyPasteHarmony
{
    /// <summary>
    /// Harmony entry point for CopyPaste 4.2.81. Hosts the ported plugin and exposes the static API
    /// that RaidableBases (and other mods) resolve via reflection.
    /// </summary>
    public class CopyPasteHarmonyMod : IHarmonyModHooks
    {
        public static CopyPasteHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 4;
        public const int VersionMinor = 2;
        public const int VersionPatch = 81;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        /// <summary>AppDomain key for handshake: RaidableBases reads this to get our API type.</summary>
        public const string AppDomainApiKey = "CopyPaste_ApiType";

        private CopyPaste _plugin;
        private List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            CopyPasteHost.Init(root);
            _plugin = new CopyPaste();
            CopyPasteHost.Instance.Plugin = _plugin;
            RegisterApiType();
            _plugin.HarmonyInit();
            RegisterCommands();
            // StringPool / ItemManager are not ready during early Harmony boot — defer like Oxide OnServerInitialized.
            ScheduleServerInitialized();
            Debug.Log($"[CopyPaste Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch} (waiting for game filesystem if needed)");
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                // StringPool.Init loads via FileSystem — not ready during early Harmony boot.
                bool stringPoolReady = false;
                try
                {
                    var field = typeof(StringPool).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Static);
                    stringPoolReady = field != null && field.GetValue(null) is bool b && b;
                    if (!stringPoolReady && field == null)
                    {
                        // Fallback probe
                        StringPool.Get("assets/prefabs/building core/floor.frame/floor.frame.prefab");
                        stringPoolReady = true;
                    }
                }
                catch { stringPoolReady = false; }

                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (stringPoolReady && itemsReady)
                {
                    _plugin.HarmonyServerInitialized();
                    Debug.Log($"[CopyPaste Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch
            {
                // Not ready yet
            }

            if (attempt > 120)
            {
                Debug.LogWarning("[CopyPaste Harmony] Timed out waiting for StringPool/ItemManager; initializing anyway");
                try { _plugin.HarmonyServerInitialized(); } catch (Exception ex) { Debug.LogError("[CopyPaste Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("CopyPasteHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[CopyPaste Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private CopyPasteHarmonyMod _mod;
            private int _attempt;
            public void Begin(CopyPasteHarmonyMod mod, int attempt)
            {
                _mod = mod;
                _attempt = attempt;
                StartCoroutine(Wait());
            }
            private System.Collections.IEnumerator Wait()
            {
                yield return new WaitForSeconds(0.5f);
                var mod = _mod;
                var attempt = _attempt;
                Destroy(gameObject);
                mod?.ScheduleServerInitialized(attempt + 1);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();
            _plugin?.HarmonyUnload();
            UnregisterApiType();
            CopyPasteHost.Shutdown();
            _plugin = null;
            Instance = null;
        }

        private static void RegisterApiType()
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(CopyPasteHarmonyMod)); }
            catch (Exception ex) { Debug.LogWarning("[CopyPaste Harmony] RegisterApiType: " + ex.Message); }
        }

        private static void UnregisterApiType()
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }
        }

        // ---- Static API for RaidableBases (must stay on CopyPasteHarmonyMod) ----

        public static bool IsPasteReady()
        {
            return Instance?._plugin != null && Instance._plugin.IsPasteReadyPublic();
        }

        /// <summary>
        /// Returns List (not HashSet) so callers preserve file order. Same transform as Oxide PreLoadData.
        /// </summary>
        public static List<Dictionary<string, object>> PreLoadData(List<object> entities, Vector3 startPos,
            float rotationCorrection, bool deployables, bool inventories, bool auth, bool vending)
        {
            var plugin = Instance?._plugin;
            if (plugin == null) return new List<Dictionary<string, object>>();
            var set = plugin.PreLoadData(entities, startPos, rotationCorrection, deployables, inventories, auth, vending);
            return set == null ? new List<Dictionary<string, object>>() : set.ToList();
        }

        public static object FindBestHeight(ICollection<Dictionary<string, object>> entities, Vector3 startPos)
        {
            var plugin = Instance?._plugin;
            if (plugin == null) return 0f;
            return plugin.FindBestHeight(entities, startPos);
        }

        /// <summary>
        /// Paste API. <paramref name="player"/> may be BasePlayer, IPlayer, or duck-typed (RaidableBases console player).
        /// </summary>
        public static object Paste(ICollection<Dictionary<string, object>> entities, Dictionary<string, object> protocol,
            bool ownership, Vector3 startPos, object player, bool stability, float rotationCorrection,
            float heightAdj, bool auth, Action callback, Action<BaseEntity> callbackSpawned, string filename,
            bool checkPlaced, bool enableSaving, bool? dlc = null, int? skinsMode = null)
        {
            var plugin = Instance?._plugin;
            if (plugin == null) return null;
            var iplayer = ResolvePlayer(player);
            return plugin.Paste(entities, protocol, ownership, startPos, iplayer, stability, rotationCorrection,
                heightAdj, auth, callback, callbackSpawned, filename, checkPlaced, enableSaving, dlc, skinsMode);
        }

        /// <summary>Optional: paste from game .data file via ConVar.CopyPaste.</summary>
        public static List<BaseEntity> PasteFromDataFile(string dataFilePath, Vector3 origin, Quaternion rotation, ulong steamId = 0)
        {
            var result = new List<BaseEntity>();
            try
            {
                if (string.IsNullOrEmpty(dataFilePath) || !File.Exists(dataFilePath))
                    return result;
                var data = ConVar.CopyPaste.LoadFileFromBundles(dataFilePath);
                if (data == null) return result;
                var options = new ConVar.CopyPaste.PasteOptions
                {
                    Origin = origin,
                    PlayerRotation = rotation,
                    Deployables = true,
                    Vehicles = true,
                    Resources = true,
                    NPCs = false,
                    AutoAuth = false
                };
                var pasted = ConVar.CopyPaste.PasteEntities(data, options, steamId);
                if (pasted != null)
                    result.AddRange(pasted);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CopyPaste] PasteFromDataFile: " + ex.Message);
            }
            return result;
        }

        private static IPlayer ResolvePlayer(object player)
        {
            if (player == null) return new RustConsolePlayer();
            if (player is IPlayer ip) return ip;
            if (player is BasePlayer bp) return bp.ToIPlayer();
            try
            {
                var t = player.GetType();
                var objProp = t.GetProperty("Object");
                if (objProp?.GetValue(player) is BasePlayer nested)
                    return nested.ToIPlayer();
                var idProp = t.GetProperty("Id");
                var reply = t.GetMethod("Reply", new[] { typeof(string) });
                if (idProp != null && reply != null)
                    return new ForeignPlayerAdapter(player);
            }
            catch { }
            return new RustConsolePlayer();
        }

        private sealed class ForeignPlayerAdapter : IPlayer
        {
            private readonly object _inner;
            private readonly PropertyInfo _id;
            private readonly PropertyInfo _obj;
            private readonly PropertyInfo _name;
            private readonly PropertyInfo _isAdmin;
            private readonly MethodInfo _reply;
            public ForeignPlayerAdapter(object inner)
            {
                _inner = inner;
                var t = inner.GetType();
                _id = t.GetProperty("Id");
                _obj = t.GetProperty("Object");
                _name = t.GetProperty("Name");
                _isAdmin = t.GetProperty("IsAdmin");
                _reply = t.GetMethod("Reply", new[] { typeof(string) });
            }
            public string Id => _id?.GetValue(_inner) as string ?? "0";
            public object Object => _obj?.GetValue(_inner);
            public string Name => _name?.GetValue(_inner) as string ?? "Server";
            public bool IsAdmin => _isAdmin != null && _isAdmin.GetValue(_inner) is bool b && b;
            public bool IsServer => Object == null;
            public bool IsConnected => true;
            public void Reply(string message) => _reply?.Invoke(_inner, new object[] { message });
            public void Message(string msg) => Reply(msg);
            public bool HasPermission(string perm) => IsAdmin || IsServer;
        }

        // ---- Chat / console commands ----

        private void RegisterCommands()
        {
            ScrubCopyPasteFromReplicatedList();

            string[] chatCommands = { "copy", "paste", "copylist", "pasteback", "undo" };
            foreach (string name in chatCommands)
            {
                var localName = name;
                // Do NOT set Variable/Replicated or add to Index.Server.Replicated —
                // clients don't have these in ConsoleGen and spam:
                // "Replicated convar not found on client: global.copy" etc.
                var cmd = new ConsoleSystem.Command
                {
                    Name = localName,
                    FullName = "global." + localName,
                    Variable = false,
                    Replicated = false,
                    ServerAdmin = false,
                    AllowRunFromServer = false,
                    Call = arg =>
                    {
                        var player = arg?.Player();
                        if (player == null) return;
                        string[] args;
                        try
                        {
                            var raw = arg.Args;
                            if (raw == null || raw.Length == 0)
                                args = Array.Empty<string>();
                            else
                            {
                                args = new string[raw.Length];
                                for (int i = 0; i < raw.Length; i++)
                                    args[i] = raw[i].ToString();
                            }
                        }
                        catch { args = Array.Empty<string>(); }
                        HandleCommand(player, localName, args);
                    }
                };
                ConsoleSystem.Index.Server.Dict["global." + localName] = cmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[localName] = cmd;
                _registeredCommands.Add(cmd);
            }
        }

        private static void ScrubCopyPasteFromReplicatedList()
        {
            try
            {
                var replicated = typeof(ConsoleSystem.Index.Server)
                    .GetField("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IList;
                if (replicated == null) return;

                string[] names = { "copy", "paste", "copylist", "pasteback", "undo" };
                for (int i = replicated.Count - 1; i >= 0; i--)
                {
                    if (replicated[i] is not ConsoleSystem.Command cmd) continue;
                    string full = cmd.FullName ?? string.Empty;
                    string name = cmd.Name ?? string.Empty;
                    bool match = false;
                    foreach (var n in names)
                    {
                        if (name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                            full.Equals("global." + n, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                    }
                    if (!match) continue;
                    cmd.Replicated = false;
                    cmd.Variable = false;
                    replicated.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CopyPaste] ScrubCopyPasteFromReplicatedList: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            ScrubCopyPasteFromReplicatedList();

            string[] chatCommands = { "copy", "paste", "copylist", "pasteback", "undo" };
            foreach (string name in chatCommands)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global." + name);
                ConsoleSystem.Index.Server.GlobalDict?.Remove(name);
            }
            _registeredCommands.Clear();
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string name = parts[0].ToLowerInvariant();
            if (name != "copy" && name != "paste" && name != "copylist" && name != "pasteback" && name != "undo")
                return false;
            HandleCommand(player, name, parts.Skip(1).ToArray());
            return true;
        }

        private void HandleCommand(BasePlayer player, string name, string[] args)
        {
            if (_plugin == null || player == null) return;
            var iplayer = player.ToIPlayer();
            switch (name)
            {
                case "copy": _plugin.CmdCopy(iplayer, name, args); break;
                case "paste": _plugin.CmdPaste(iplayer, name, args); break;
                case "copylist": _plugin.CmdList(iplayer, name, args); break;
                case "pasteback": _plugin.CmdPasteBack(iplayer, name, args); break;
                case "undo": _plugin.CmdUndo(iplayer, name, args); break;
            }
        }
    }
}
