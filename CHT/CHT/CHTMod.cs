using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PluginBody = Oxide.Plugins.CustomHelicopterTiers2;

namespace CHT
{
    /// <summary>
    /// Harmony entry for Custom Helicopter Tiers. Facepunch loader PatchAll's this assembly before OnLoaded.
    /// </summary>
    public sealed class CHTMod : IHarmonyModHooks
    {
        public const string AppDomainApiKey = "CHT_ApiType";

        public static CHTMod Instance { get; private set; }
        public static PluginBody Plugin { get; private set; }

        private Coroutine _init;
        private HarmonyLib.Harmony _findHarmony;
        private readonly Dictionary<string, ConsoleSystem.Command> _commandMap =
            new Dictionary<string, ConsoleSystem.Command>(StringComparer.OrdinalIgnoreCase);
        private string _shopCommandName = "heli.shop";

        private static readonly string[] FixedCommands =
        {
            "cht.tier", "cht.callprofile", "cht.heli", "cht.gib", "cht.crate", "cht.shopcontroller", "cht.openshop"
        };

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            try
            {
                Plugin = new PluginBody { Name = "CHT" };
                Plugin.HarmonyLoadConfig();
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(CHTMod));
                PermissionsBridge.Initialize(PluginBody.GetRegisteredPermissions());
                RegisterCommands();

                try
                {
                    _findHarmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.CHT.find");
                    if (!Patches.Patch_ConsoleSystem_Server_Find.TryApply(_findHarmony))
                        Debug.LogWarning("[CHT] Could not patch ConsoleSystem.Find; Shop cht.openshop may fail.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[CHT] Find patch failed: " + ex.Message);
                }

                // PatchAll on cui.endtest can drop Shop/Kits handlers when CHT loads after them.
                Patches.CuiEndtestRebind.EnsureForeignPrefixes();

                _init = ModRunner.Instance.StartCoroutine(InitRoutine());
                Debug.Log("[CHT] Loaded. Config: HarmonyConfig/CHT.json. Tiers: HarmonyData/CHT/. Open UI: F1 `" + _shopCommandName + "` or Shop command `cht.openshop <steamid>`");
            }
            catch (Exception e)
            {
                Debug.LogError("[CHT] Load failed: " + e);
            }
        }

        private IEnumerator InitRoutine()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return null;

            try
            {
                Plugin.CallInit();
                PermissionsBridge.Initialize(PluginBody.GetRegisteredPermissions());
                Plugin.CallOnServerInitialized();
                RegisterShopCommand();
                // Second pass after other mods finish init (covers odd load races).
                Patches.CuiEndtestRebind.EnsureForeignPrefixes();
            }
            catch (Exception e)
            {
                Debug.LogError("[CHT] Init failed: " + e);
            }

            _init = null;
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_init != null && ModRunner.Instance != null)
            {
                ModRunner.Instance.StopCoroutine(_init);
                _init = null;
            }

            try { Plugin?.CallUnload(); }
            catch (Exception e) { Debug.LogWarning("[CHT] Unload: " + e.Message); }

            UnregisterCommands();

            try { _findHarmony?.UnpatchAll(_findHarmony.Id); }
            catch { }
            _findHarmony = null;

            PermissionsBridge.Shutdown();
            ModRunner.Destroy();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            Plugin = null;
            Instance = null;
            Debug.Log("[CHT] Unloaded.");
        }

        public ConsoleSystem.Command GetCommand(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.Trim();
            if (n.StartsWith("global.", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(7);
            return _commandMap.TryGetValue(n, out var cmd) ? cmd : null;
        }

        private void RegisterCommands()
        {
            try
            {
                foreach (string n in FixedCommands)
                    RegisterOne(n, MakeHandler(n));

                RegisterShopCommand();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CHT] Command registration failed: " + ex.Message);
            }
        }

        private void RegisterShopCommand()
        {
            string shop = Plugin?.GetShopChatCommand();
            if (string.IsNullOrWhiteSpace(shop))
                shop = "heli.shop";
            shop = shop.Trim().TrimStart('/', '\\');

            if (!string.Equals(_shopCommandName, shop, StringComparison.OrdinalIgnoreCase) &&
                _commandMap.ContainsKey(_shopCommandName))
            {
                UnregisterOne(_shopCommandName);
            }

            _shopCommandName = shop;
            RegisterOne(_shopCommandName, OpenShopFromArg);
        }

        private void RegisterOne(string name, Action<ConsoleSystem.Arg> handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;

            // Facepunch Find("cht.openshop") looks up Dict["cht.openshop"] (has dot → no global. prefix).
            // Find("heli") looks up Dict["global.heli"] then GlobalDict["heli"].
            bool hasDot = name.IndexOf('.') >= 0;
            string cmdParent = string.Empty;
            string cmdName = name;
            string fullName;
            string dictKey;

            if (hasDot)
            {
                var parts = name.Split(new[] { '.' }, 2);
                cmdParent = parts[0];
                cmdName = parts[1];
                fullName = name;
                dictKey = name;
            }
            else
            {
                fullName = "global." + name;
                dictKey = fullName;
            }

            var c = new ConsoleSystem.Command
            {
                Name = cmdName,
                Parent = cmdParent,
                FullName = fullName,
                Variable = false,
                ServerAdmin = true,
                ServerUser = true,
                AllowRunFromServer = true,
                Call = handler
            };

            var dict = ConsoleSystem.Index.Server.Dict;
            var globalDict = ConsoleSystem.Index.Server.GlobalDict;
            if (dict != null) dict[dictKey] = c;
            if (!hasDot && globalDict != null) globalDict[cmdName] = c;

            // Keep Find-inject map under the public command string players/Shop use.
            _commandMap[name] = c;
        }

        private void UnregisterOne(string name)
        {
            try
            {
                bool hasDot = !string.IsNullOrEmpty(name) && name.IndexOf('.') >= 0;
                if (hasDot)
                    ConsoleSystem.Index.Server.Dict?.Remove(name);
                else
                {
                    ConsoleSystem.Index.Server.Dict?.Remove("global." + name);
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(name);
                }
            }
            catch { }
            _commandMap.Remove(name);
        }

        private void UnregisterCommands()
        {
            foreach (string n in new List<string>(_commandMap.Keys))
                UnregisterOne(n);
            _commandMap.Clear();
        }

        private static Action<ConsoleSystem.Arg> MakeHandler(string name)
        {
            return arg =>
            {
                if (Plugin == null) return;
                switch (name)
                {
                    case "cht.tier": Plugin.cmdTier(arg); break;
                    case "cht.callprofile": Plugin.cmdCallProfile(arg); break;
                    case "cht.heli": Plugin.cmdHeli(arg); break;
                    case "cht.gib": Plugin.cmdGib(arg); break;
                    case "cht.crate": Plugin.cmdCrate(arg); break;
                    case "cht.shopcontroller": Plugin.cmdShopController(arg); break;
                    case "cht.openshop": OpenShopFromArg(arg); break;
                }
            };
        }

        /// <summary>
        /// Opens the heli purchase UI. Supports player console or server: cht.openshop &lt;steamid&gt;.
        /// Shop calls <see cref="TryOpenShop(ulong)"/> via AppDomain so this does not depend on ConsoleSystem.Find.
        /// </summary>
        public static void OpenShopFromArg(ConsoleSystem.Arg arg)
        {
            if (Plugin == null || arg == null) return;

            BasePlayer player = arg.Player();
            ulong steamId = 0;
            if (player == null && arg.HasArgs(1))
            {
                string id = arg.GetString(0);
                if (ulong.TryParse(id, out steamId))
                    player = BasePlayer.FindByID(steamId) ?? BasePlayer.FindSleeping(steamId);
            }

            if (player == null || !player.IsConnected)
            {
                arg.ReplyWith("[CHT] Openshop needs a player (use cht.openshop <steamid> from server/Shop).");
                return;
            }

            TryOpenShop(player);
        }

        /// <summary>
        /// AppDomain API for Shop: open the heli shop UI for a connected player.
        /// </summary>
        public static bool TryOpenShop(ulong steamId)
        {
            if (steamId == 0) return false;
            var player = BasePlayer.FindByID(steamId) ?? BasePlayer.FindSleeping(steamId);
            return TryOpenShop(player);
        }

        public static bool TryOpenShop(BasePlayer player)
        {
            if (Plugin == null || player == null || !player.IsConnected)
                return false;

            Plugin.cmdHeliShop(player, "heli.shop", Array.Empty<string>());
            return true;
        }

        public bool RunChatCommand(BasePlayer player, string command, string[] args)
        {
            if (Plugin == null || player == null || string.IsNullOrEmpty(command))
                return false;

            string shop = Plugin.GetShopChatCommand();
            if (string.IsNullOrEmpty(shop))
                shop = "heli.shop";
            shop = shop.Trim().TrimStart('/', '\\');

            if (!string.Equals(command, shop, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command, "cht.openshop", StringComparison.OrdinalIgnoreCase))
                return false;

            Plugin.cmdHeliShop(player, command, args ?? Array.Empty<string>());
            return true;
        }
    }

    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }

        private static GameObject _go;
        private static readonly Queue<Action> _queue = new Queue<Action>();

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("CHT_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void Destroy()
        {
            lock (_queue) _queue.Clear();
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            Instance = null;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) _queue.Enqueue(action);
        }

        private void Update()
        {
            while (true)
            {
                Action action;
                lock (_queue)
                {
                    if (_queue.Count == 0) return;
                    action = _queue.Dequeue();
                }
                try { action(); }
                catch (Exception e) { Debug.LogWarning("[CHT] NextTick: " + e.Message); }
            }
        }
    }
}
