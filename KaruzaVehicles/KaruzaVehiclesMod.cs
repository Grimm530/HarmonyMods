using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace KaruzaVehicles
{
    /// <summary>
    /// Harmony entry for the Karuza custom-entity vehicle stack.
    /// Load order: 0Permissions -> KaruzaVehicles. Unload Oxide Karuza plugins first.
    /// </summary>
    public class KaruzaVehiclesMod : IHarmonyModHooks
    {
        public static KaruzaVehiclesMod Instance { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 10;
        public const int VersionPatch = 0;

        public const string AppDomainApiKey = "KaruzaVehicles_ApiType";

        public CustomEntities CustomEntities { get; private set; }
        public KaruzaEntitiesCommon Common { get; private set; }
        public BulletProjectile BulletProjectile { get; private set; }
        public RustCar RustCar { get; private set; }
        public RustHelicopter RustHelicopter { get; private set; }
        public RustPlane RustPlane { get; private set; }
        public KaruzaVehiclePush VehiclePush { get; private set; }
        public KaruzaVehicleHorseTowing HorseTowing { get; private set; }

        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (RustPlugin plugin, string method)> _chatHandlers =
            new Dictionary<string, (RustPlugin, string)>(StringComparer.OrdinalIgnoreCase);
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;

            try
            {
                CustomEntities = new CustomEntities();
                Common = new KaruzaEntitiesCommon();
                BulletProjectile = new BulletProjectile();
                RustCar = new RustCar();
                RustHelicopter = new RustHelicopter();
                RustPlane = new RustPlane();
                VehiclePush = new KaruzaVehiclePush();
                HorseTowing = new KaruzaVehicleHorseTowing();

                Common.BulletProjectile = new PluginTargetBridge(BulletProjectile, "BulletProjectile");
                Common.SimpleStatus = null;
            }
            catch (Exception ex)
            {
                Debug.LogError("[KaruzaVehicles] FAIL: construct: " + ex);
                return;
            }

            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(KaruzaVehiclesMod));
                // Standalone CustomEntities.dll must not also load — this assembly hosts CustomEntities.
                AppDomain.CurrentDomain.SetData("CustomEntities_ApiType", typeof(CustomEntities));
                AppDomain.CurrentDomain.SetData("CustomEntities_Plugin", CustomEntities);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] SetData: " + ex.Message);
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<KaruzaVehiclesRunner>().Begin(this);

            Debug.Log($"[KaruzaVehicles] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[KaruzaVehicles] -> Config: HarmonyConfig/KaruzaEntitiesCommon.json, RustCar.json, RustHelicopter.json, RustPlane.json");
            Debug.Log("[KaruzaVehicles] -> Vehicle defs: HarmonyConfig/RustCar/, RustHelicopter/, RustPlane/");
            Debug.Log("[KaruzaVehicles] -> Lang: HarmonyLanguage/KaruzaVehicles.json");
            Debug.Log("[KaruzaVehicles] -> Load order: 0Permissions -> KaruzaVehicles. Unload Oxide Karuza plugins.");
        }

        private void OnPermissionsReady()
        {
            try
            {
                var ce = CustomEntities;
                ce?.permission.RegisterPermission("customentities.admin", ce);
                Debug.Log("[KaruzaVehicles] OK: Permissions ready — customentities.admin re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady) return;
            _serverReady = true;

            try
            {
                CustomEntities.Init();
                CustomEntities.HarmonyLoadConfig();
                CustomEntities.OnServerInitialized();

                BulletProjectile.OnServerInitialized();

                Common.Init();
                Common.HarmonyLoadConfig();
                LoadLanguageFiles(Common);
                Common.HarmonyLoadDefaultMessages();
                Common.OnServerInitialized();

                RustCar.OnServerInitialized();
                RustHelicopter.OnServerInitialized();
                RustPlane.OnServerInitialized();
                VehiclePush.OnServerInitialized();
                HorseTowing.OnServerInitialized();

                RegisterPluginCommands(CustomEntities);
                RegisterPluginCommands(RustCar);
                RegisterPluginCommands(RustHelicopter);
                RegisterPluginCommands(RustPlane);

                Debug.Log("[KaruzaVehicles] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[KaruzaVehicles] FAIL: OnServerInitialized: " + ex);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }

            UnregisterConsoleCommands();

            try { HorseTowing?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] HorseTowing unload: " + ex.Message); }
            try { VehiclePush?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] Push unload: " + ex.Message); }
            try { RustPlane?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] RustPlane unload: " + ex.Message); }
            try { RustHelicopter?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] RustHelicopter unload: " + ex.Message); }
            try { RustCar?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] RustCar unload: " + ex.Message); }
            try { Common?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] Common unload: " + ex.Message); }
            try { BulletProjectile?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] BulletProjectile unload: " + ex.Message); }
            try { CustomEntities?.Unload(); } catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] CustomEntities unload: " + ex.Message); }

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData("CustomEntities_ApiType", null); } catch { }
            try { AppDomain.CurrentDomain.SetData("CustomEntities_Plugin", null); } catch { }

            CustomEntities = null;
            Common = null;
            BulletProjectile = null;
            RustCar = null;
            RustHelicopter = null;
            RustPlane = null;
            VehiclePush = null;
            HorseTowing = null;
            Instance = null;
            Debug.Log("[KaruzaVehicles] OK: Unloaded.");
        }

        private static void LoadLanguageFiles(KaruzaEntitiesCommon common)
        {
            if (common?.lang == null) return;
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string langDir = Path.Combine(root, "HarmonyLanguage");
                string en = Path.Combine(langDir, "KaruzaVehicles.json");
                common.lang.LoadLanguageFile("en", en);
                foreach (var locale in new[] { "de", "es", "lt", "lv", "ru" })
                {
                    string path = Path.Combine(langDir, "KaruzaVehicles." + locale + ".json");
                    common.lang.LoadLanguageFile(locale, path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] Lang load: " + ex.Message);
            }
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("KaruzaVehicles_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<KaruzaVehiclesRunner>();
        }

        private void RegisterPluginCommands(RustPlugin plugin)
        {
            if (plugin == null) return;
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var mi in plugin.GetType().GetMethods(bf))
            {
                var attrs = mi.GetCustomAttributes(typeof(ConsoleCommandAttribute), false);
                if (attrs == null || attrs.Length == 0) continue;
                foreach (ConsoleCommandAttribute attr in attrs)
                {
                    if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                    var method = mi;
                    RegisterConsole(attr.Command.Trim(), arg =>
                    {
                        try { method.Invoke(plugin, new object[] { arg }); }
                        catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] " + attr.Command + ": " + ex.Message); }
                    });
                }
            }

            foreach (var entry in plugin.CovalenceCommands)
            {
                if (string.IsNullOrEmpty(entry.command)) continue;
                _chatCommands.Add(entry.command);
                _chatHandlers[entry.command] = (plugin, entry.method);
                string captured = entry.command;
                string method = entry.method;
                string perm = entry.perm;
                RegisterConsole(captured, arg =>
                {
                    var player = arg?.Player();
                    IPlayer iplayer = player != null
                        ? (IPlayer)new BasePlayerWrapper(player)
                        : new RustConsolePlayer();
                    if (!string.IsNullOrEmpty(perm) && player != null &&
                        !PermissionsBridge.UserHasPermission(player.UserIDString, perm) &&
                        !player.IsAdmin)
                    {
                        arg.ReplyWith("No permission.");
                        return;
                    }
                    string[] args = Array.Empty<string>();
                    if (arg?.Args != null && arg.Args.Length > 0)
                    {
                        args = new string[arg.Args.Length];
                        for (int i = 0; i < arg.Args.Length; i++)
                            args[i] = arg.Args[i].ToString() ?? "";
                    }
                    try
                    {
                        var mi = plugin.GetType().GetMethod(method, bf);
                        mi?.Invoke(plugin, new object[] { iplayer, captured, args });
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[KaruzaVehicles] " + captured + ": " + ex.Message);
                    }
                });
            }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatHandlers.TryGetValue(command, out var handler)) return false;
            if (handler.plugin == null) return false;

            var entry = handler.plugin.CovalenceCommands.FirstOrDefault(c =>
                string.Equals(c.command, command, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(entry.perm) &&
                !PermissionsBridge.UserHasPermission(player.UserIDString, entry.perm) &&
                !player.IsAdmin)
            {
                player.ChatMessage("No permission.");
                return true;
            }

            try
            {
                var mi = handler.plugin.GetType().GetMethod(handler.method,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(handler.plugin, new object[] { new BasePlayerWrapper(player), command, args ?? Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] chat " + command + ": " + ex.Message);
            }
            return true;
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
        {
            try
            {
                bool hasDot = name.Contains(".");
                string cmdParent = "";
                string cmdName = name;
                string fullName;
                if (hasDot)
                {
                    var parts = name.Split(new[] { '.' }, 2);
                    cmdParent = parts[0];
                    cmdName = parts[1];
                    fullName = name;
                }
                else
                    fullName = "global." + name;

                var cmd = new ConsoleSystem.Command
                {
                    Name = cmdName,
                    Parent = cmdParent,
                    FullName = fullName,
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Replicated = false,
                    Call = a =>
                    {
                        try { handler(a); }
                        catch (Exception ex) { Debug.LogWarning("[KaruzaVehicles] command " + name + ": " + ex.Message); }
                    }
                };
                _commands.Add(cmd);
                ConsoleSystem.Index.Server.Dict[hasDot ? fullName : fullName] = cmd;
                if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] FAIL: RegisterConsole(" + name + "): " + ex.Message);
            }
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    dict?.Remove(cmd.FullName);
                    if (string.IsNullOrEmpty(cmd.Parent))
                        globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
            _chatCommands.Clear();
            _chatHandlers.Clear();
        }
    }

    internal sealed class KaruzaVehiclesRunner : MonoBehaviour
    {
        private KaruzaVehiclesMod _mod;
        private bool _started;

        public void Begin(KaruzaVehiclesMod mod)
        {
            _mod = mod;
            if (!_started)
            {
                _started = true;
                StartCoroutine(WaitForServer());
            }
        }

        private IEnumerator WaitForServer()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            while (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
                yield return null;
            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }
    }
}
