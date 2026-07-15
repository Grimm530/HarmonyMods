using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.RestoreItems;

namespace RestoreItemsHarmony
{
    /// <summary>
    /// Harmony entry for RestoreItems 2.1.6. Exposes dungeon/restore API via AppDomain.
    /// Config: HarmonyConfig/RestoreItems.json
    /// Data:   HarmonyData/RestoreItems/
    /// </summary>
    public class RestoreItemsHarmonyMod : IHarmonyModHooks
    {
        public static RestoreItemsHarmonyMod Instance { get; private set; }
        public static OxidePlugin Plugin { get; private set; }

        public const string AppDomainApiKey = "RestoreItems_ApiType";
        public const string AppDomainPluginKey = "RestoreItems_Plugin";

        public const int VersionMajor = 2;
        public const int VersionMinor = 1;
        public const int VersionPatch = 6;

        private Action _permissionsReadyCallback;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            try
            {
                Plugin = new OxidePlugin();
                Plugin.HarmonyInit();
            }
            catch (Exception ex)
            {
                Debug.LogError("[RestoreItems] FAIL: construct/config: " + ex);
                return;
            }

            RegisterApiType();
            _permissionsReadyCallback = OnPermissionsReady;
            RegisterPermissionsReadyCallback(_permissionsReadyCallback);
            BindPluginReferences();

            ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log($"[RestoreItems] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[RestoreItems] -> Config: HarmonyConfig/RestoreItems.json");
            Debug.Log("[RestoreItems] -> Data: HarmonyData/RestoreItems/");
            Debug.Log("[RestoreItems] -> Chat: /getstuff (configurable), /restored.debug");
        }

        private void OnPermissionsReady()
        {
            try
            {
                if (Plugin != null && !Plugin.permission.PermissionExists("restoreitems.use"))
                    Plugin.permission.RegisterPermission("restoreitems.use", Plugin);
                TryGrantDefaultUse();
                Debug.Log("[RestoreItems] OK: Permissions ready.");
            }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] Permissions ready: " + ex.Message); }
        }

        private static void TryGrantDefaultUse()
        {
            try
            {
                var permType = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
                var grant = permType?.GetMethod("GrantGroupPermission", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(string) }, null);
                if (grant == null) return;
                if (grant.Invoke(null, new object[] { "default", "restoreitems.use" }) is bool ok && ok)
                    Debug.Log("[RestoreItems] OK: restoreitems.use granted to group default.");
            }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] TryGrantDefaultUse: " + ex.Message); }
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null) yield return null;
            yield return new WaitForSeconds(1f);

            BindPluginReferences();
            try { Plugin?.HarmonyServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[RestoreItems] OnServerInitialized: " + ex); }

            RegisterCommands();
            RefreshChatCommandsFromConfig();
            Debug.Log("[RestoreItems] OK: Server initialized.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            _permissionsReadyCallback = null;
            UnregisterCommands();
            try { Plugin?.HarmonyUnload(); } catch { }
            UnregisterApiType();
            ModRunner.Destroy();
            Plugin = null;
            Instance = null;
            Debug.Log("[RestoreItems] OK: Unloaded.");
        }

        private void BindPluginReferences()
        {
            if (Plugin == null) return;
            try
            {
                Plugin.Economics = Plugin.plugins.Find("Economics");
                Plugin.RaidableBases = Plugin.plugins.Find("RaidableBases");
            }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] BindPluginReferences: " + ex.Message); }
        }

        private void RegisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(RestoreItemsHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, new RestoreItemsPluginWrapper(this));
            }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] RegisterApiType: " + ex.Message); }
        }

        private void UnregisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
            }
            catch { }
        }

        public void RefreshChatCommandsFromConfig()
        {
            _chatCommands.Clear();
            if (Plugin?.cmd == null) return;
            foreach (var reg in Plugin.cmd.RegisteredChatCommands)
                _chatCommands.Add(reg.name);
            try
            {
                var cfgField = Plugin.GetType().GetField("config", BindingFlags.Instance | BindingFlags.NonPublic);
                var cfg = cfgField?.GetValue(Plugin);
                var chatS = cfg?.GetType().GetField("chatS")?.GetValue(cfg);
                var cmdName = chatS?.GetType().GetField("playerChatCommand")?.GetValue(chatS) as string;
                if (!string.IsNullOrWhiteSpace(cmdName))
                    _chatCommands.Add(cmdName.Trim());
            }
            catch { }
            _chatCommands.Add("restored.debug");
            _chatCommands.Add("restoretest");
        }

        public bool TryHandleChat(BasePlayer player, string message)
        {
            if (player == null || Plugin == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            var parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string name = parts[0];
            if (!_chatCommands.Contains(name)) return false;
            string[] args = parts.Skip(1).ToArray();

            foreach (var reg in Plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeChat(reg.method, player, name, args);
                return true;
            }

            if (string.Equals(name, "restoretest", StringComparison.OrdinalIgnoreCase))
            {
                InvokeChat(nameof(OxidePlugin.CmdRestoreTest), player, name, args);
                return true;
            }

            return false;
        }

        private void InvokeChat(string method, BasePlayer player, string command, string[] args)
        {
            var mi = typeof(OxidePlugin).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) return;
            try { mi.Invoke(Plugin, new object[] { player, command, args }); }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] InvokeChat " + method + ": " + ex.Message); }
        }

        private void RegisterCommands()
        {
            UnregisterCommands();
            RefreshChatCommandsFromConfig();
        }

        private void UnregisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    dict?.Remove(cmd.FullName);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }

        private static void RegisterPermissionsReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                var permType = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
                var mi = permType?.GetMethod("RegisterReadyCallback", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Action) }, null);
                if (mi != null) { mi.Invoke(null, new object[] { callback }); return; }
            }
            catch { }

            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as IList;
                if (list == null)
                {
                    list = new List<Action>();
                    AppDomain.CurrentDomain.SetData("Permissions_ReadyCallbacks", list);
                }
                if (!list.Contains(callback)) list.Add(callback);
            }
            catch { }

            if (AppDomain.CurrentDomain.GetData("Permissions_ApiType") is Type)
            {
                try { callback(); } catch { }
            }
        }

        // ---- Static API (Oxide Call parity) ----

        public static bool RestorePlayerItems(BasePlayer player) => Plugin?.RestorePlayerItems(player) ?? false;
        public static bool HasItemsToRestore(BasePlayer player) => Plugin?.HasItemsToRestore(player) ?? false;
        public static void StorePlayerItems(BasePlayer player, BaseEntity entity) => Plugin?.StorePlayerItems(player, entity);
        public static bool AutoRestorePlayerItems(BasePlayer player) => Plugin?.AutoRestorePlayerItems(player) ?? false;
        public static bool SaveDungeonInventory(BasePlayer player) => Plugin?.SaveDungeonInventory(player) ?? false;
        public static bool RestoreDungeonInventory(BasePlayer player) => Plugin?.RestoreDungeonInventory(player) ?? false;
        public static bool ClearDungeonInventory(BasePlayer player) => Plugin?.ClearDungeonInventory(player) ?? false;
        public static bool HasDungeonInventory(BasePlayer player) => Plugin?.HasDungeonInventory(player) ?? false;

        public object Call(string method, params object[] args)
        {
            if (Plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                var mi = typeof(OxidePlugin).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) return null;
                var pars = mi.GetParameters();
                if (args == null) args = Array.Empty<object>();
                if (pars.Length == args.Length) return mi.Invoke(Plugin, args);
                if (pars.Length == 1 && args.Length >= 1 && pars[0].ParameterType == typeof(BasePlayer))
                    return mi.Invoke(Plugin, new object[] { args[0] });
                return mi.Invoke(Plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RestoreItems] Call(" + method + "): " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>Oxide Plugin.Call bridge for consumers resolving RestoreItems_Plugin.</summary>
    public sealed class RestoreItemsPluginWrapper
    {
        private readonly RestoreItemsHarmonyMod _mod;
        public RestoreItemsPluginWrapper(RestoreItemsHarmonyMod mod) => _mod = mod;
        public string Name => "RestoreItems";
        public bool IsLoaded => _mod != null && RestoreItemsHarmonyMod.Plugin != null;
        public object Call(string method, params object[] args) => _mod?.Call(method, args);
    }
}

namespace Oxide.Plugins
{
    public partial class RestoreItems
    {
        public void DispatchOnPlayerDeath(BasePlayer player, HitInfo info) => OnPlayerDeath(player, info);
        public void DispatchOnDied(BasePlayer player, HitInfo info) => OnDied(player, info);
        public void DispatchOnItemAddedToContainer(ItemContainer container, Item item) => OnItemAddedToContainer(container, item);
        public void DispatchOnItemStacked(Item targetItem, Item sourceItem, ItemContainer container, int amount) => OnItemStacked(targetItem, sourceItem, container, amount);
        public void DispatchOnItemStacked(Item targetItem, Item sourceItem, ItemContainer container) => OnItemStacked(targetItem, sourceItem, container);
        public void DispatchOnEntitySpawned(PlayerCorpse corpse) => OnEntitySpawned(corpse);
        public void DispatchOnEntitySpawned(DroppedItemContainer container) => OnEntitySpawned(container);
        public void DispatchOnEntitySpawned(DroppedItem droppedItem) => OnEntitySpawned(droppedItem);
        public void DispatchOnEntityKill(BaseNetworkable entity) => OnEntityKill(entity);
    }
}
