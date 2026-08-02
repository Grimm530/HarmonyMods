using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RaidableBasesBuyableUI
{
    /// <summary>
    /// Harmony entry for RaidableBases Buyable UI (gallery).
    /// Config: HarmonyConfig/RaidableBasesBuyableUI.json
    /// Data:   HarmonyData/RaidableBasesBuyableUI/
    /// </summary>
    public class RaidableBasesBuyableUIMod : IHarmonyModHooks
    {
        public const string HarmonyId = "com.facepunch.rust_dedicated.RaidableBasesBuyableUI";
        public const string CuiMarker = "RBBUI";
        public const string ApiDataKey = "RaidableBasesBuyableUI_ApiType";

        public static RaidableBasesBuyableUIMod Instance { get; private set; }
        public static RaidableBasesBuyableUIPlugin Plugin { get; private set; }

        private Harmony _manualHarmony;
        private readonly Dictionary<string, ConsoleSystem.Command> _commands = new(StringComparer.OrdinalIgnoreCase);
        private Coroutine _hookPatchCoroutine;
        private Action _permissionsReadyCallback;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            Paths.EnsureDataDirs();
            Plugin = new RaidableBasesBuyableUIPlugin();
            Plugin.Initialize();
            RegisterCommands();
            AppDomain.CurrentDomain.SetData(ApiDataKey, typeof(RaidableBasesBuyableUIPlugin));
            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            // Harmony OnLoaded often runs before ServerMgr exists — defer like image loading.
            if (ServerMgr.Instance != null)
                _hookPatchCoroutine = ServerMgr.Instance.StartCoroutine(EnsureCallHookPatch());
            else
                StartDeferredHookPatch();
            Debug.Log("[RaidableBasesBuyableUI] Loaded. Config: HarmonyConfig/RaidableBasesBuyableUI.json");
        }

        private void StartDeferredHookPatch()
        {
            try
            {
                var go = new GameObject("RBBUI_HookPatchWait");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<HookPatchWaitBehaviour>().Begin(this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] Defer hook patch failed: " + ex.Message);
            }
        }

        private sealed class HookPatchWaitBehaviour : MonoBehaviour
        {
            private RaidableBasesBuyableUIMod _mod;

            public void Begin(RaidableBasesBuyableUIMod mod)
            {
                _mod = mod;
                StartCoroutine(Wait());
            }

            private IEnumerator Wait()
            {
                for (int i = 0; i < 120; i++)
                {
                    if (ServerMgr.Instance != null && _mod != null)
                    {
                        var mod = _mod;
                        Destroy(gameObject);
                        mod._hookPatchCoroutine = ServerMgr.Instance.StartCoroutine(mod.EnsureCallHookPatch());
                        yield break;
                    }
                    yield return new WaitForSeconds(0.5f);
                }
                Destroy(gameObject);
                Debug.LogWarning("[RaidableBasesBuyableUI] ServerMgr never became ready - RB CallHook patches not applied.");
            }
        }

        private void OnPermissionsReady()
        {
            try
            {
                Plugin?.TryGrantDefaultAllow();
                PermissionsBridge.RegisterPermission("raidablebasesbuyableui.spawn.bypass");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] Permissions ready re-register: " + ex.Message);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_permissionsReadyCallback != null)
            {
                PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
                _permissionsReadyCallback = null;
            }

            try
            {
                if (_hookPatchCoroutine != null && ServerMgr.Instance != null)
                    ServerMgr.Instance.StopCoroutine(_hookPatchCoroutine);
            }
            catch { }

            Plugin?.Shutdown();
            UnregisterCommands();

            try { _manualHarmony?.UnpatchAll(HarmonyId + ".hooks"); } catch { }
            _manualHarmony = null;

            AppDomain.CurrentDomain.SetData(ApiDataKey, null);
            Plugin = null;
            Instance = null;
            Debug.Log("[RaidableBasesBuyableUI] Unloaded.");
        }

        private IEnumerator EnsureCallHookPatch()
        {
            bool granted = false;
            bool callHook = false;
            bool showUi = false;
            bool buyRaid = false;
            bool tryChat = false;

            for (int i = 0; i < 90; i++)
            {
                if (!granted && Plugin != null && Plugin.TryGrantDefaultAllow())
                    granted = true;

                if (!callHook)
                    callHook = TryPatchCallHook();
                if (!showUi)
                    showUi = TryPatchShowBuyableUi();
                if (!buyRaid)
                    buyRaid = TryPatchCommandBuyRaid();
                if (!tryChat)
                    tryChat = TryPatchTryHandleChat();

                // Chat intercept + CommandBuyRaid are the critical paths
                if (buyRaid || tryChat)
                {
                    if (!granted)
                    {
                        for (int j = 0; j < 15 && !granted; j++)
                        {
                            yield return CoroutineEx.waitForSeconds(1f);
                            if (Plugin != null && Plugin.TryGrantDefaultAllow())
                                granted = true;
                        }
                    }
                    if (!callHook) TryPatchCallHook();
                    if (!showUi) TryPatchShowBuyableUi();
                    if (!buyRaid) TryPatchCommandBuyRaid();
                    if (!tryChat) TryPatchTryHandleChat();
                    yield break;
                }

                yield return CoroutineEx.waitForSeconds(1f);
            }
            Debug.LogWarning("[RaidableBasesBuyableUI] Could not patch RaidableBases chat/CommandBuyRaid after 90s - empty /buyraid will show BuySyntax.");
        }

        private bool TryPatchTryHandleChat()
        {
            try
            {
                var method = Patches.RaidableBases_TryHandleChat_Patch.FindTargetMethod();
                if (method == null) return false;

                _manualHarmony ??= new Harmony(HarmonyId + ".hooks");
                var patches = Harmony.GetPatchInfo(method);
                if (patches?.Prefixes != null)
                {
                    foreach (var p in patches.Prefixes)
                    {
                        if (p.owner == HarmonyId + ".hooks")
                            return true;
                    }
                }

                var prefix = typeof(Patches.RaidableBases_TryHandleChat_Patch).GetMethod(
                    nameof(Patches.RaidableBases_TryHandleChat_Patch.Prefix),
                    BindingFlags.Public | BindingFlags.Static);
                _manualHarmony.Patch(method, prefix: new HarmonyMethod(prefix));
                Debug.Log("[RaidableBasesBuyableUI] Patched RaidableBases.CommandRegistry.TryHandleChat (empty buyraid -> gallery).");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] TryHandleChat patch: " + ex.Message);
                return false;
            }
        }

        private bool TryPatchCommandBuyRaid()
        {
            try
            {
                var method = Patches.RaidableBases_CommandBuyRaid_Patch.FindTargetMethod();
                if (method == null) return false;

                _manualHarmony ??= new Harmony(HarmonyId + ".hooks");
                var patches = Harmony.GetPatchInfo(method);
                if (patches?.Prefixes != null)
                {
                    foreach (var p in patches.Prefixes)
                    {
                        if (p.owner == HarmonyId + ".hooks")
                            return true;
                    }
                }

                var prefix = typeof(Patches.RaidableBases_CommandBuyRaid_Patch).GetMethod(
                    nameof(Patches.RaidableBases_CommandBuyRaid_Patch.Prefix),
                    BindingFlags.Public | BindingFlags.Static);
                _manualHarmony.Patch(method, prefix: new HarmonyMethod(prefix));
                Debug.Log("[RaidableBasesBuyableUI] Patched RaidableBases.CommandBuyRaid (empty args -> gallery).");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] CommandBuyRaid patch: " + ex.Message);
                return false;
            }
        }

        private bool TryPatchShowBuyableUi()
        {
            try
            {
                var method = Patches.RaidableBases_ShowBuyableUi_Patch.FindTargetMethod();
                if (method == null) return false;

                _manualHarmony ??= new Harmony(HarmonyId + ".hooks");
                var patches = Harmony.GetPatchInfo(method);
                if (patches?.Prefixes != null)
                {
                    foreach (var p in patches.Prefixes)
                    {
                        if (p.owner == HarmonyId + ".hooks")
                            return true;
                    }
                }

                var prefix = typeof(Patches.RaidableBases_ShowBuyableUi_Patch).GetMethod(
                    nameof(Patches.RaidableBases_ShowBuyableUi_Patch.Prefix),
                    BindingFlags.Public | BindingFlags.Static);
                _manualHarmony.Patch(method, prefix: new HarmonyMethod(prefix));
                Debug.Log("[RaidableBasesBuyableUI] Patched RaidableBases.UiHandler.ShowBuyableUi -> custom gallery.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] ShowBuyableUi patch: " + ex.Message);
                return false;
            }
        }

        private bool TryPatchCallHook()
        {
            try
            {
                var method = FindCallHookMethod();
                if (method == null) return false;

                _manualHarmony ??= new Harmony(HarmonyId + ".hooks");
                // Avoid double-patching on retries
                var patches = Harmony.GetPatchInfo(method);
                if (patches?.Prefixes != null)
                {
                    foreach (var p in patches.Prefixes)
                    {
                        if (p.owner == HarmonyId + ".hooks")
                            return true;
                    }
                }

                var prefix = typeof(Patches.RaidableBases_CallHook_Patch).GetMethod(
                    nameof(Patches.RaidableBases_CallHook_Patch.Prefix),
                    BindingFlags.Public | BindingFlags.Static);
                _manualHarmony.Patch(method, prefix: new HarmonyMethod(prefix));
                Debug.Log("[RaidableBasesBuyableUI] Patched RaidableBases.Interface.CallHook for OnPurchaseBase / purchase tracking.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] CallHook patch: " + ex.Message);
                return false;
            }
        }

        private static MethodInfo FindCallHookMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType("RaidableBases.Interface"); } catch { }
                if (t == null) continue;
                var m = t.GetMethod("CallHook", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(object[]) }, null);
                if (m != null) return m;
            }
            return null;
        }

        private void RegisterCommands()
        {
            RegisterConsole("uit", Plugin.CmdBuyableUITest, client: false);
            RegisterConsole("rbbui.reloadimages", Plugin.CmdReloadImages, client: false);
            // Also register server-side aliases for the buyable UI commands (console / RCON).
            RegisterConsole("ui_buyable_show", Plugin.CmdBuyableShow, client: false);
            RegisterConsole("ui_buyable_purchase", Plugin.CmdBuyablePurchase, client: false);
            RegisterConsole("ui_buyable_changepage", Plugin.CmdBuyableChangePage, client: false);
            RegisterConsole("ui_buyable_color", Plugin.CmdSetColor, client: false);
            RegisterConsole("ui_buyable_transparency", Plugin.CmdSetTransparency, client: false);
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool client)
        {
            try
            {
                var key = name.Trim().ToLowerInvariant();
                var full = "global." + key;
                var cmd = new ConsoleSystem.Command
                {
                    Name = key,
                    FullName = full,
                    Variable = false,
                    ServerAdmin = false,
                    ServerUser = true,
                    Client = client,
                    Call = arg => handler(arg)
                };
                if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey(full))
                {
                    ConsoleSystem.Index.Server.Dict[full] = cmd;
                    if (ConsoleSystem.Index.Server.GlobalDict != null)
                        ConsoleSystem.Index.Server.GlobalDict[key] = cmd;
                    _commands[key] = cmd;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] Register " + name + ": " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            foreach (var kv in _commands)
            {
                try
                {
                    ConsoleSystem.Index.Server.Dict?.Remove("global." + kv.Key);
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(kv.Key);
                }
                catch { }
            }
            _commands.Clear();
        }
    }
}
