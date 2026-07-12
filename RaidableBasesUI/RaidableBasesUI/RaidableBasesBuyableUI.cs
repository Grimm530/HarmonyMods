using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Game.Rust.Cui;
using RaidableBasesBuyableUI.ExtensionMethods;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RaidableBasesBuyableUI
{
    /// <summary>
    /// Buyable gallery UI for Raidable Bases (Harmony port of Oxide RaidableBasesBuyableUI 1.0.61).
    /// Config: HarmonyConfig/RaidableBasesBuyableUI.json
    /// Data:   HarmonyData/RaidableBasesBuyableUI/
    /// </summary>
    public class RaidableBasesBuyableUIPlugin
    {
        private const ulong RB_LOGO = 2935766590;
        private const string PanelSprite = "Assets/Content/UI/UI.Background.Tile.psd";
        private const string BUYABLE_UI_MAIN = "BUYABLE_UI_MAIN";
        private const string BUYABLE_UI_OPTIONS = "BUYABLE_UI_OPTIONS";
        private const string BUYABLE_UI_BACKGROUND = "BUYABLE_UI_BACKGROUND";
        private const string BUYABLE_UI_HEADER = "BUYABLE_UI_HEADER";
        private const double menuButtonAnchorMinY = -110.74;
        private const double menuButtonAnchorMaxY = -75.74;
        private const double menuButtonSize = 35.0;
        private const double menuButtonDist = 20.0;
        private const double baseButtonAnchorMinY = -110.74;
        private const double baseButtonAnchorMaxY = -75.74;
        private const double baseButtonSize = 100.0;
        private const double baseButtonSpaceX = 35.0;
        private const double baseButtonSpaceY = 30.0;
        private const string Name = "RaidableBasesBuyableUI";
        private SortedDictionary<string, List<string>> _PROFILES = new();
        private Dictionary<string, string> _COSTS = new();
        private Dictionary<string, List<string>> players = new();
        private Dictionary<string, string> _playerColors = new(); // Player UI color preference
        private Dictionary<string, float> _playerTransparency = new(); // Player UI transparency (0-100)
        private readonly HashSet<ulong> _pnpcBuilderMode = new HashSet<ulong>();
        private const float DEFAULT_TRANSPARENCY = 100f; // Default: 100% opacity

        public void Initialize()
        {
            LoadConfig();
            UiHandler = new ImageUiHandler();
            PermissionsBridge.RegisterPermission("raidablebasesbuyableui.allow");
            PermissionsBridge.RegisterPermission("raidablebasesbuyableui.spawn.filenames");
            PermissionsBridge.RegisterPermission("raidablebasesbuyableui.spawn.bypass");
            TryGrantDefaultAllow();
            // Oxide BuyableUI UX: category click opens the per-base image grid for everyone.
            if (PermissionsBridge.GrantGroupPermission("default", "raidablebasesbuyableui.spawn.filenames"))
                Puts("OK: raidablebasesbuyableui.spawn.filenames on group 'default'.");
            UiHandler.LoadImages();
            LoadProfiles();
            LoadCosts();
            LoadPlayerPreferences();
        }

        /// <summary>Grant gallery access to the default group. Safe to call repeatedly.</summary>
        public bool TryGrantDefaultAllow()
        {
            if (!PermissionsBridge.IsAvailable)
                return false;
            PermissionsBridge.RegisterPermission("raidablebasesbuyableui.allow");
            PermissionsBridge.RegisterPermission("raidablebasesbuyableui.spawn.filenames");
            bool ok = PermissionsBridge.GrantGroupPermission("default", "raidablebasesbuyableui.allow");
            PermissionsBridge.GrantGroupPermission("default", "raidablebasesbuyableui.spawn.filenames");
            if (ok)
            {
                Puts("OK: raidablebasesbuyableui.allow + spawn.filenames on group 'default'.");
                return true;
            }
            return false;
        }

        public void Shutdown()
        {
            if (BasePlayer.activePlayerList != null)
            {
                var snapshot = new List<BasePlayer>();
                foreach (var p in BasePlayer.activePlayerList)
                    snapshot.Add(p);
                foreach (var p in snapshot)
                    DestroyUI(p);
            }
            SavePlayerPreferences();
            UiHandler?.Unload();
        }

        /// <summary>Called from RaidableBases.Interface.CallHook patch.</summary>
        public object HandleOnPurchaseBase(object[] args)
        {
            var buyer = ResolvePlayerArg(args, 0) ?? ResolvePlayerArg(args, 1);
            if (buyer == null) return null;

            // This mod replaces RB's built-in Buyable Events UI. Always open our gallery
            // when we intercept (do not fall through to RaidableBases ShowBuyableUi).
            if (!PermissionsBridge.UserHasPermissionOrDefaultAllow(buyer.UserIDString, "raidablebasesbuyableui.allow"))
            {
                Puts("OnPurchaseBase: {0} lacks raidablebasesbuyableui.allow - gallery blocked.", buyer.displayName);
                return null;
            }

            CuiHelper.DestroyUi(buyer, "RB_UI_Buyable");
            var opened = SEND_BUYABLE_MODES(buyer);
            // Non-null stops RaidableBases from showing its own panel (even if opened==false).
            return opened ? (object)true : (object)"RaidableBasesBuyableUI";
        }

        /// <summary>Public entry used by ShowBuyableUi redirect patch.</summary>
        public void OpenBuyableModes(BasePlayer player) => SEND_BUYABLE_MODES(player);

        public object HandleOnPurchaseTakePayments(object[] args)
        {
            // Harmony RB: (buyer, player, value, mode). Oxide BuyableUI used (buyer, player, baseName).
            var player = ResolvePlayerArg(args, 1) ?? ResolvePlayerArg(args, 0);
            var baseName = ResolveStringArg(args, 2);
            if (player == null || string.IsNullOrEmpty(baseName)) return null;
            if (!players.TryGetValue(player.UserIDString, out var bases) || !bases.Contains(baseName))
                return null;
            return true; // already purchased - block repurchase (RB treats non-null as cancel)
        }

        public void HandleOnRaidableBasePurchased(object[] args)
        {
            var flat = FlattenHookArgs(args);
            // Harmony RB: userid, Location, grid, level, pvp, BaseName, spawnDT, despawnDT
            if (flat.Length < 6) return;
            var userid = flat[0]?.ToString();
            var baseName = flat[5]?.ToString();
            if (string.IsNullOrEmpty(userid) || string.IsNullOrEmpty(baseName)) return;
            if (PermissionsBridge.UserHasPermission(userid, "raidablebasesbuyableui.spawn.bypass"))
                return;
            if (!players.TryGetValue(userid, out var bases))
                players[userid] = bases = new List<string>();
            if (!bases.Contains(baseName))
                bases.Add(baseName);
        }

        private static void RunUiBuyraid(BasePlayer player, string value)
        {
            if (player?.net?.connection == null || string.IsNullOrEmpty(value)) return;
            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet().FromConnection(player.net.connection);
                ConsoleSystem.Run(opt, "ui_buyraid", value);
            }
            catch (Exception ex)
            {
                Puts("RunUiBuyraid failed: {0}", ex.Message);
            }
        }

        // API for PersonalNPCHelper - opens the full base gallery UI for bot building
        public bool OpenForPNPCBuilder(BasePlayer player)
        {
            if (player == null) return false;
            if (_PROFILES.Count == 0)
            {
                player.ChatMessage("<color=#FF9800>No raid base profiles loaded. Check HarmonyData/RaidableBases/Profiles/ and HarmonyData/copypaste/.</color>");
                return false;
            }

            _pnpcBuilderMode.Add((ulong)player.userID);
            return SEND_BUYABLE_MODES(player, "CHOOSE A BASE TO BUILD");
        }

        public void CloseForPNPCBuilder(BasePlayer player)
        {
            if (player == null) return;
            _pnpcBuilderMode.Remove((ulong)player.userID);
            DestroyUI(player);
        }

        public bool IsUiOpen(BasePlayer player)
        {
            if (player == null) return false;
            return _pnpcBuilderMode.Contains((ulong)player.userID);
        }

        public bool IsPNPCBuilderMode(BasePlayer player) =>
            player != null && _pnpcBuilderMode.Contains((ulong)player.userID);

        public void DispatchUiCommand(string cmd, ConsoleSystem.Arg arg)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            if (cmd.Equals("ui_buyable_show", StringComparison.OrdinalIgnoreCase)) { CmdBuyableShow(arg); return; }
            if (cmd.Equals("ui_buyable_purchase", StringComparison.OrdinalIgnoreCase)) { CmdBuyablePurchase(arg); return; }
            if (cmd.Equals("ui_buyable_changepage", StringComparison.OrdinalIgnoreCase)) { CmdBuyableChangePage(arg); return; }
            if (cmd.Equals("ui_buyable_color", StringComparison.OrdinalIgnoreCase)) { CmdSetColor(arg); return; }
            if (cmd.Equals("ui_buyable_transparency", StringComparison.OrdinalIgnoreCase)) { CmdSetTransparency(arg); return; }
        }

        public void CmdBuyableUITest(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !player.IsAdmin) return;
            SEND_BUYABLE_MODES(player);
        }

        public void CmdReloadImages(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !player.IsAdmin)
            {
                Reply(arg, "You don't have permission to use this command.");
                return;
            }

            Reply(arg, "Reloading images from raids directory...");
            ServerMgr.Instance.StartCoroutine(UiHandler.ReloadRaidsDirectoryImages());
            Reply(arg, "Image reload started. Check server console for progress.");
        }

        private static void Reply(ConsoleSystem.Arg arg, string msg)
        {
            if (arg?.Connection != null)
                arg.ReplyWith(msg);
            else
                Puts(msg);
        }

        private static BasePlayer ResolvePlayerArg(object[] args, int index)
        {
            if (args == null || index < 0 || index >= args.Length) return null;
            return args[index] as BasePlayer;
        }

        private static string ResolveStringArg(object[] args, int index)
        {
            if (args == null || index < 0 || index >= args.Length || args[index] == null) return null;
            return args[index].ToString();
        }

        private static object[] FlattenHookArgs(object[] args)
        {
            if (args == null || args.Length == 0) return Array.Empty<object>();
            if (args.Length == 1 && args[0] is object[] inner) return inner;
            return args;
        }

        private static object CallExternalHook(string name, params object[] args)
        {
            // Notify other Harmony mods (e.g. PersonalNPCHelper) via AppDomain if they subscribe.
            try
            {
                var handlers = AppDomain.CurrentDomain.GetData("RaidableBasesBuyableUI_ExternalHooks") as System.Collections.IDictionary;
                if (handlers != null && handlers.Contains(name) && handlers[name] is Delegate del)
                    return del.DynamicInvoke(args);
            }
            catch (Exception ex)
            {
                Puts("CallExternalHook {0}: {1}", name, ex.Message);
            }
            return null;
        }

        private void DestroyUI(BasePlayer player)
        {
            if (player == null) return;

            _pnpcBuilderMode.Remove((ulong)player.userID);
            
            CuiHelper.DestroyUi(player, "BUYABLE_UI_COLORPICKER");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_TRANSPARENCY");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_HEADER_BACKGROUND");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_HEADER_TEXT");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_HEADER_BOY");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_BUYHEADER_BACKGROUND");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_BACKGROUND_IMAGE");
            
            CuiHelper.DestroyUi(player, BUYABLE_UI_OPTIONS);
            CuiHelper.DestroyUi(player, BUYABLE_UI_MAIN);
            CuiHelper.DestroyUi(player, BUYABLE_UI_BACKGROUND);
            CuiHelper.DestroyUi(player, BUYABLE_UI_HEADER);
        }

        public void CmdBuyablePurchase(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            string baseName = string.Join(" ", arg.Args);

            if (IsPNPCBuilderMode(player))
            {
                DestroyUI(player);
                CallExternalHook("OnPNPCBuilderBaseSelected", player, baseName);
                return;
            }

            DestroyUI(player);
            if (!players.TryGetValue(player.UserIDString, out var bases) || !bases.Contains(baseName))
            {
                RunUiBuyraid(player, baseName);
            }
        }

        public void CmdBuyableChangePage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var index = Convert.ToInt32(arg.GetString(0));
            var profile = string.Join(" ", arg.Args.ToStringArray().Skip(1));
            SEND_BUYABLE_BASES(player, profile, index);
        }

        public void CmdBuyableShow(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            var profile = string.Join(" ", arg.Args);
            if (profile == "Close")
            {
                if (IsPNPCBuilderMode(player))
                    CallExternalHook("OnPNPCBuilderBaseUiClosed", player);
                DestroyUI(player);
                return;
            }

            CuiHelper.DestroyUi(player, BUYABLE_UI_OPTIONS);

            // This mod's purpose is the per-base gallery (Oxide RaidableBasesBuyableUI UX).
            // Always open the base grid for a category; do not collapse to mode-only ui_buyraid.
            SEND_BUYABLE_BASES(player, profile, 0);
        }
        public void CmdSetColor(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            DebugPuts($"[DEBUG] ui_buyable_color called - Player: {(player?.displayName ?? "NULL")}, Args: {(arg.HasArgs() ? string.Join(" ", arg.Args) : "NONE")}");
            
            if (player == null)
            {
                DebugPuts($"[DEBUG] Player is NULL!");
                return;
            }
            
            if (!arg.HasArgs())
            {
                DebugPuts($"[DEBUG] No args provided!");
                return;
            }

            var colorName = arg.GetString(0);
            DebugPuts($"[DEBUG] Color name from args: '{colorName}'");
            
            if (string.IsNullOrEmpty(colorName))
            {
                DebugPuts($"[DEBUG] Color name is null or empty!");
                return;
            }

            var allowedColors = new[] { "blue", "green", "purple", "red", "default" };
            colorName = colorName.ToLower();
            if (!allowedColors.Contains(colorName))
            {
                Puts($"Invalid color: {colorName}");
                return;
            }

            _playerColors[player.UserIDString] = colorName;
            DebugPuts($"OK: Player {player.displayName} set color to: {colorName}");
            
            SavePlayerPreferences();
            
            SEND_BUYABLE_MODES(player);
        }
        public void CmdSetTransparency(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            DebugPuts($"[DEBUG] ui_buyable_transparency called - Player: {(player?.displayName ?? "NULL")}, Args: {(arg.HasArgs() ? string.Join(" ", arg.Args) : "NONE")}");
            
            if (player == null)
            {
                DebugPuts($"[DEBUG] Player is NULL!");
                return;
            }
            
            if (!arg.HasArgs())
            {
                DebugPuts($"[DEBUG] No args provided!");
                return;
            }

            var direction = arg.GetString(0);
            DebugPuts($"[DEBUG] Direction from args: '{direction}'");
            
            if (string.IsNullOrEmpty(direction))
            {
                DebugPuts($"[DEBUG] Direction is null or empty!");
                return;
            }

            direction = direction.ToLower();
            var currentTransparency = GetPlayerTransparency(player.UserIDString);
            var step = 10f; // Adjust by 10% each click

            if (direction == "increase")
            {
                var newTransparency = Math.Min(100f, currentTransparency + step);
                _playerTransparency[player.UserIDString] = newTransparency;
                DebugPuts($"OK: Player {player.displayName} increased transparency to: {newTransparency}%");
            }
            else if (direction == "decrease")
            {
                var newTransparency = Math.Max(10f, currentTransparency - step);
                _playerTransparency[player.UserIDString] = newTransparency;
                DebugPuts($"OK: Player {player.displayName} decreased transparency to: {newTransparency}%");
            }
            else
            {
                DebugPuts($"[DEBUG] Invalid direction: '{direction}'");
                return;
            }

            SavePlayerPreferences();

            SEND_BUYABLE_MODES(player);
        }

        private bool SEND_BUYABLE_MODES(BasePlayer player, string text = "BUY A RAID BASE", int fontSize = 26, string fontName = "robotocondensed-bold.ttf", TextAnchor align = TextAnchor.MiddleCenter, string color = "1 1 1 1", double spacing = menuButtonSize + menuButtonDist)
        {
            if (IsPNPCBuilderMode(player) && text == "BUY A RAID BASE")
                text = "CHOOSE A BASE TO BUILD";

            // CLEANUP FIRST: remove any previous UI to prevent overlapping elements
            CuiHelper.DestroyUi(player, "RB_UI_Buyable");
            CuiHelper.DestroyUi(player, BUYABLE_UI_OPTIONS);
            CuiHelper.DestroyUi(player, BUYABLE_UI_HEADER);
            CuiHelper.DestroyUi(player, BUYABLE_UI_BACKGROUND);
            CuiHelper.DestroyUi(player, BUYABLE_UI_MAIN);
            CuiHelper.DestroyUi(player, "BUYABLE_UI_COLORPICKER");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_TRANSPARENCY");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_BACKGROUND_IMAGE");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_HEADER_BACKGROUND");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_HEADER_TEXT");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_HEADER_BOY");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_BUYHEADER_BACKGROUND");
            
            var container = new CuiElementContainer();
            var buttons = new List<string>();
            var count = 0;

            // Define the correct order for difficulty buttons
            var difficultyOrder = new[] { "Easy", "Medium", "Hard", "Expert", "Nightmare" };
            var orderedButtons = new List<string>();
            var otherButtons = new List<string>();
            var processedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Filter categories based on config, or show all if config is empty
            var profilesToProcess = new List<string>();
            if (config.AllowedCategories != null && config.AllowedCategories.Count > 0)
            {
                var allowedCategories = new HashSet<string>(config.AllowedCategories, StringComparer.OrdinalIgnoreCase);
                foreach (var profile in _PROFILES.Keys)
                {
                    if (allowedCategories.Contains(profile))
                    {
                        profilesToProcess.Add(profile);
                    }
                }
            }
            else
            {
                profilesToProcess.AddRange(_PROFILES.Keys);
            }
            foreach (var difficulty in difficultyOrder)
            {
                foreach (var profile in profilesToProcess)
                {
                    if (string.Equals(profile, difficulty, StringComparison.OrdinalIgnoreCase) && !processedProfiles.Contains(profile))
                    {
                        orderedButtons.Add(profile);
                        processedProfiles.Add(profile);
                        break;
                    }
                }
            }
            foreach (var profile in profilesToProcess)
            {
                if (!processedProfiles.Contains(profile))
                {
                    otherButtons.Add(profile);
                }
            }            
            otherButtons.Sort();
            
            buttons.AddRange(orderedButtons);
            buttons.AddRange(otherButtons);

            if (orderedButtons.Count == 0 && otherButtons.Count == 0)
            {
                if (IsPNPCBuilderMode(player))
                {
                    _pnpcBuilderMode.Remove((ulong)player.userID);
                    player.ChatMessage("<color=#FF9800>No base categories match RaidableBasesBuyableUI.json Allowed Categories.</color>");
                }
                return false;
            }

            buttons.Add("Close");

            var playerColor = GetPlayerColor(player.UserIDString);
            var playerTransparency = GetPlayerTransparency(player.UserIDString);
            var transparencyAlpha = playerTransparency / 100f;

             string backgroundColor = "0.1037736 0.101466 0.08762015";
            if (playerColor != "default")
            {
                var gradientImage = UiHandler.GetGradientImage(playerColor);
                if (IsValidPng(gradientImage))
                {
                    // Use gradient image as background overlay - CursorEnabled = false so it doesn't block clicks
                    // Make panel background fully transparent (alpha 0) so only gradient shows
                    container.Add(new CuiPanel
                    {
                        CursorEnabled = false, // Don't block clicks on child elements
                        Image = { Color = "1 1 1 0", Sprite = PanelSprite }, // Fully transparent - gradient image will show
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "46.376 38.908", OffsetMax = "-46.384 -38.891" }
                    }, "Overlay", "BUYABLE_UI_BACKGROUND");

                    container.Add(new CuiElement
                    {
                        Name = "BUYABLE_UI_BACKGROUND_IMAGE",
                        Parent = "BUYABLE_UI_BACKGROUND",
                        Components = {
                            new CuiRawImageComponent { Color = $"1 1 1 {transparencyAlpha}", Png = gradientImage },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                        }
                    });
                }
                else
                {
                    container.Add(new CuiPanel
                    {
                        CursorEnabled = false, 
                        Image = { Color = "1 1 1 0", Sprite = PanelSprite }, 
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "46.376 38.908", OffsetMax = "-46.384 -38.891" }
                    }, "Overlay", "BUYABLE_UI_BACKGROUND");

                    var backdropImage = UiHandler.GetImage("backdrop");
                    if (IsValidPng(backdropImage))
                    {
                        container.Add(new CuiElement
                        {
                            Name = "BUYABLE_UI_BACKGROUND_IMAGE",
                            Parent = "BUYABLE_UI_BACKGROUND",
                            Components = {
                                new CuiRawImageComponent { Color = $"1 1 1 {0.08f * transparencyAlpha}", Png = backdropImage },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                            }
                        });
                    }
                    else if (!string.IsNullOrEmpty(config?.BackdropURL))
                    {
                        container.Add(new CuiElement
                        {
                            Name = "BUYABLE_UI_BACKGROUND_IMAGE",
                            Parent = "BUYABLE_UI_BACKGROUND",
                            Components = {
                                new CuiRawImageComponent { Color = $"1 1 1 {0.08f * transparencyAlpha}", Url = config.BackdropURL },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                            }
                        });
                    }
                }
            }
            else
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = false, 
                    Image = { Color = $"{backgroundColor} {transparencyAlpha}", Sprite = PanelSprite },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "46.376 38.908", OffsetMax = "-46.384 -38.891" }
                }, "Overlay", "BUYABLE_UI_BACKGROUND");

                var backdropImage = UiHandler.GetImage("backdrop");
                if (IsValidPng(backdropImage))
                {
                    container.Add(new CuiElement
                    {
                        Name = "BUYABLE_UI_BACKGROUND_IMAGE",
                        Parent = "BUYABLE_UI_BACKGROUND",
                        Components = {
                            new CuiRawImageComponent { Color = $"1 1 1 {0.95f * transparencyAlpha}", Png = backdropImage },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                        }
                    });
                }
                else if (!string.IsNullOrEmpty(config?.BackdropURL))
                {
                    container.Add(new CuiElement
                    {
                        Name = "BUYABLE_UI_BACKGROUND_IMAGE",
                        Parent = "BUYABLE_UI_BACKGROUND",
                        Components = {
                            new CuiRawImageComponent { Color = $"1 1 1 {0.95f * transparencyAlpha}", Url = config.BackdropURL },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                        }
                    });
                }
            }

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-551.004 202.33", OffsetMax = "550.996 302.33" }
            }, "Overlay", BUYABLE_UI_MAIN);

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-0.005 -0.005", OffsetMax = "0.005 0.005" }
            }, BUYABLE_UI_MAIN, "BUYABLE_UI_PANEL");

            var categoriesBgAlpha = (playerColor != "default") ? 0f : (0.8f * transparencyAlpha);
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = $"0.08235294 0.07843138 0.07450981 {categoriesBgAlpha}", Sprite = PanelSprite },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-571 -480", OffsetMax = "-370 -50" }
            }, BUYABLE_UI_MAIN, "BUYABLE_UI_BUYHEADER");

            if (playerColor != "default")
            {
                var categoriesGradientImage = UiHandler.GetGradientImage(playerColor);
                if (IsValidPng(categoriesGradientImage))
                {
                    container.Add(new CuiElement
                    {
                        Name = "BUYABLE_UI_BUYHEADER_BACKGROUND",
                        Parent = "BUYABLE_UI_BUYHEADER",
                        Components = {
                            new CuiRawImageComponent { Color = $"1 1 1 {transparencyAlpha}", Png = categoriesGradientImage },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                        }
                    });
                }
            }

            foreach (var button in buttons)
            {
                string cost = GetBuyableCost(button);
                
                container.Add(new CuiButton
                {
                    Button = { Color = _(config.GetButtonColor(button), 1f), Command = $"ui_buyable_show {button}" },
                    Text = { Text = Convert.ToString(button).ToUpper() + cost, Font = fontName, FontSize = 16, Align = align, Color = _(config.GetButtonTextColor(button), 1f) },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-551 {menuButtonAnchorMinY - (count * spacing)}", OffsetMax = $"-391.357 {menuButtonAnchorMaxY - (count * spacing)}" }
                }, BUYABLE_UI_MAIN, $"BUYABLE_UI_BUTTON_{count}");

                count++;
            }

            var headerBgAlpha = (playerColor != "default") ? 0f : (0.8f * transparencyAlpha);
            container.Add(new CuiPanel
            {
                CursorEnabled = true, // Enable cursor for clickable buttons
                Image = { Color = $"0.08235294 0.07843138 0.07450981 {headerBgAlpha}", Sprite = PanelSprite },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "46.376 210", OffsetMax = "-46.384 290" }
            }, "Overlay", "BUYABLE_UI_HEADER");

            if (playerColor != "default")
            {
                var headerGradientImage = UiHandler.GetGradientImage(playerColor);
                if (IsValidPng(headerGradientImage))
                {
                    container.Add(new CuiElement
                    {
                        Name = "BUYABLE_UI_HEADER_BACKGROUND",
                        Parent = "BUYABLE_UI_HEADER",
                        Components = {
                            new CuiRawImageComponent { Color = $"1 1 1 {transparencyAlpha}", Png = headerGradientImage },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                        }
                    });
                }
            }
            
            container.Add(new CuiElement
            {
                Name = "BUYABLE_UI_HEADER_TEXT",
                Parent = "BUYABLE_UI_HEADER",
                Components = {
                    new CuiTextComponent { Text = text, Font = fontName, FontSize = fontSize, Align = align, Color = color },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-551.004 -27.67", OffsetMax = "550.996 72.33" }
                }
            });
            
            var boyImage = UiHandler.GetImage("boy");
            if (IsValidPng(boyImage))
            {
                container.Add(new CuiElement
                {
                    Name = "BUYABLE_UI_HEADER_BOY",
                    Parent = "BUYABLE_UI_HEADER",
                    Components = {
                        new CuiRawImageComponent { Color = "1 1 1 1", Png = boyImage },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "10 10", OffsetMax = "90 -10" }
                    }
                });
            }
            
            if (!IsPNPCBuilderMode(player))
                AddColorPicker(container, player, playerColor);
            
            DebugPuts($"[DEBUG] UI structure complete - Header and color picker added last");

            CuiHelper.DestroyUi(player, BUYABLE_UI_BACKGROUND);
            CuiHelper.DestroyUi(player, BUYABLE_UI_HEADER);
            CuiHelper.DestroyUi(player, BUYABLE_UI_MAIN);
            CuiHelper.AddUi(player, container);

            return true;
        }

        private string GetPlayerColor(string userId)
        {
            return _playerColors.TryGetValue(userId, out var color) ? color : "default";
        }

        private float GetPlayerTransparency(string userId)
        {
            return _playerTransparency.TryGetValue(userId, out var transparency) ? transparency : DEFAULT_TRANSPARENCY;
        }

        private void AddColorPicker(CuiElementContainer container, BasePlayer player, string playerColor)
        {
            DebugPuts($"[DEBUG] AddColorPicker called for player: {player?.displayName ?? "NULL"}");
            
            var colorPickerBgAlpha = (playerColor != "default") ? 0f : 0.6f;
            var colorPickerPanel = "BUYABLE_UI_COLORPICKER";
            container.Add(new CuiPanel
            {
                CursorEnabled = true, 
                Image = { Color = $"0.08235294 0.07843138 0.07450981 {colorPickerBgAlpha}" },
                RectTransform = { AnchorMin = "0.63 0", AnchorMax = "0.90 1", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, "BUYABLE_UI_HEADER", colorPickerPanel);
            
            DebugPuts($"[DEBUG] Color picker panel added: {colorPickerPanel}");

            var colors = new[] { "blue", "green", "purple", "red" };
            var colorBoxSize = 0.2;
            var colorBoxSpacing = 0.02;
            var startX = 0.05;

            var defaultBoxName = "colorpicker_default";
            var defaultBoxX = startX;
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 1" }, // Black background
                RectTransform = { AnchorMin = $"{defaultBoxX} 0.1", AnchorMax = $"{defaultBoxX + colorBoxSize} 0.9" }
            }, colorPickerPanel, defaultBoxName);

            // clickable button on top for default
            var defaultCommand = "ui_buyable_color default";
            DebugPuts($"[DEBUG] Adding default button with command: {defaultCommand}");
            var defaultButtonPanelName = $"{defaultBoxName}_button";
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "1 1 1 0" }, // Fully transparent - button handles clicks
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, defaultBoxName, defaultButtonPanelName);
            
            container.Add(new CuiButton
            {
                Button = { 
                    Color = "1 1 1 0.01", // Nearly transparent but still clickable
                    Command = defaultCommand,
                    FadeIn = 0f
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, defaultButtonPanelName);

            // color gradient buttons, starting after the default button
            for (int i = 0; i < colors.Length; i++)
            {
                var colorName = colors[i];
                var colorBoxName = $"colorpicker_{colorName}";
                var gradientImage = UiHandler.GetGradientImage(colorName);
                if (!IsValidPng(gradientImage))
                {
                    Puts($"Warning: Gradient image not found for {colorName}");
                    gradientImage = null;
                }
                // Position after default button (i+1 to account for default button)
                var boxX = startX + ((i + 1) * (colorBoxSize + colorBoxSpacing));
                
                // add RawImage as a child element to avoid conflict
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "0 0 0 0", Sprite = PanelSprite }, 
                    RectTransform = { AnchorMin = $"{boxX} 0.1", AnchorMax = $"{boxX + colorBoxSize} 0.9" }
                }, colorPickerPanel, colorBoxName);

                if (IsValidPng(gradientImage))
                {
                    container.Add(new CuiElement
                    {
                        Parent = colorBoxName,
                        Components = {
                            new CuiRawImageComponent { Color = "1 1 1 1", Png = gradientImage },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                        }
                    });
                }

                var buttonCommand = $"ui_buyable_color {colorName}";
                DebugPuts($"[DEBUG] Adding color button for {colorName} with command: {buttonCommand}");
                var buttonPanelName = $"{colorBoxName}_button";
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "1 1 1 0" }, 
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                }, colorBoxName, buttonPanelName);
                
                container.Add(new CuiButton
                {
                    Button = { 
                        Color = "1 1 1 0.01", 
                        Command = buttonCommand,
                        FadeIn = 0f
                    },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                }, buttonPanelName);
            }

            var transparencyPanel = "BUYABLE_UI_TRANSPARENCY";
            var transX = startX + ((colors.Length + 1) * (colorBoxSize + colorBoxSpacing));
            var transparencyBgAlpha = (playerColor != "default") ? 0f : 0.8f;
            container.Add(new CuiPanel
            {
                CursorEnabled = true, 
                Image = { Color = $"0.08235294 0.07843138 0.07450981 {transparencyBgAlpha}" },
                RectTransform = { AnchorMin = $"{transX} 0.1", AnchorMax = $"{transX + colorBoxSize} 0.9" }
            }, colorPickerPanel, transparencyPanel);

            DebugPuts($"[DEBUG] Adding transparency decrease button");
            container.Add(new CuiButton
            {
                Button = { Color = "0.4 0.4 0.4 1", Command = "ui_buyable_transparency decrease" },
                Text = { Text = "-", Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.05 0.15", AnchorMax = "0.45 0.85" }
            }, transparencyPanel);

            DebugPuts($"[DEBUG] Adding transparency increase button");
            container.Add(new CuiButton
            {
                Button = { Color = "0.4 0.4 0.4 1", Command = "ui_buyable_transparency increase" },
                Text = { Text = "+", Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.55 0.15", AnchorMax = "0.95 0.85" }
            }, transparencyPanel);
            
            DebugPuts($"[DEBUG] Color picker setup complete - all buttons added");
        }

        private void SEND_BUYABLE_BASES(BasePlayer player, string mode, int indexToStartFrom, string fontName = "robotocondensed-bold.ttf", TextAnchor align = TextAnchor.MiddleCenter, double spacingX = baseButtonSize + baseButtonSpaceX, double spacingY = baseButtonSize + baseButtonSpaceY)
        {
            if (!_PROFILES.TryGetValue(mode, out var baseNames))
            {
                return;
            }

            // CLEANUP FIRST: remove old header / color picker / options to prevent overlapping elements
            CuiHelper.DestroyUi(player, BUYABLE_UI_OPTIONS);
            CuiHelper.DestroyUi(player, BUYABLE_UI_HEADER);
            CuiHelper.DestroyUi(player, "BUYABLE_UI_COLORPICKER");
            CuiHelper.DestroyUi(player, "BUYABLE_UI_TRANSPARENCY");

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-350.21 56.72", OffsetMax = "-250.21 176.72" }
            }, "Overlay", BUYABLE_UI_OPTIONS);

            if (indexToStartFrom < 0 || indexToStartFrom > baseNames.Count - 1)
            {
                indexToStartFrom = 0;
            }

            var countLeftToRight = 0;
            var rowTopToBottom = 0;
            var sourceCount = 0;
            var indexCount = 0;

            if (!players.TryGetValue(player.UserIDString, out var bases))
            {
                players[player.UserIDString] = bases = new();
            }

            if (bases.Count > 0 && baseNames.TrueForAll(bases.Contains))
            {
                bases.Clear();
            }

            foreach (var baseName in baseNames)
            {
                if (indexCount < indexToStartFrom)
                {
                    indexCount++;
                    continue;
                }

                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = "0.3207547 0.3207547 0.3207547 1", Sprite = PanelSprite },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-50 + (countLeftToRight * spacingX)} {-60 - (rowTopToBottom * spacingY)}", OffsetMax = $"{50 + (countLeftToRight * spacingX)} {60 - (rowTopToBottom * spacingY)}" }
                }, BUYABLE_UI_OPTIONS, $"Base_{countLeftToRight}_{rowTopToBottom}");

                ulong skin = UiHandler.GetSkin(baseName);
                if (skin != 0uL)
                {
                    container.Add(new CuiElement
                    {
                        Name = $"img_{countLeftToRight}_{rowTopToBottom}",
                        Parent = $"Base_{countLeftToRight}_{rowTopToBottom}",
                        Components = {
                            new CuiImageComponent { Color = "1 1 1 1", ItemId = -996920608, SkinId = skin },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-47 -37", OffsetMax = "47 57" }
                        }
                    });
                }
                else
                {
                    var basePng = UiHandler.GetImage(baseName);
                    if (IsValidPng(basePng))
                    {
                        container.Add(new CuiElement
                        {
                            Name = $"img_{countLeftToRight}_{rowTopToBottom}",
                            Parent = $"Base_{countLeftToRight}_{rowTopToBottom}",
                            Components = {
                                new CuiRawImageComponent { Color = "1 1 1 1", Png = basePng },
                                new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-47 -37", OffsetMax = "47 57" }
                            }
                        });
                    }
                }

                string color = "0.2666667 0.5333334 0.427451 1";
                string command = null;
                if (IsPNPCBuilderMode(player) || !bases.Contains(baseName))
                {
                    command = $"ui_buyable_purchase {baseName}";
                    color = "0.427451 0.5333334 0.2666667 1";
                }

                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command },
                    Text = { Text = baseName.ToUpper(), Font = fontName, FontSize = 8, Align = align, Color = "0.9058824 0.8823529 0.8078431 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -60", OffsetMax = "50 -41.266" }
                }, $"Base_{countLeftToRight}_{rowTopToBottom}", "button");

                if (++countLeftToRight >= 7)
                {
                    countLeftToRight = 0;
                    rowTopToBottom++;
                }

                if (++sourceCount >= 21)
                {
                    break;
                }
            }

            // Recreate header with gradient background, then add navigation buttons AFTER gradient so they're on top
            // Get player color and transparency preferences
            var playerColor = GetPlayerColor(player.UserIDString);
            var playerTransparency = GetPlayerTransparency(player.UserIDString);
            var transparencyAlpha = playerTransparency / 100f;
            
            // Recreate header panel (must be recreated to ensure proper z-ordering)
            var headerBgAlpha = (playerColor != "default") ? 0f : (0.8f * transparencyAlpha);
            container.Add(new CuiPanel
            {
                CursorEnabled = true, // Enable cursor for clickable buttons
                Image = { Color = $"0.08235294 0.07843138 0.07450981 {headerBgAlpha}", Sprite = PanelSprite },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "46.376 210", OffsetMax = "-46.384 290" }
            }, "Overlay", "BUYABLE_UI_HEADER");
            
            // Add gradient background FIRST (so buttons appear on top)
            if (playerColor != "default")
            {
                var headerGradientImage = UiHandler.GetGradientImage(playerColor);
                if (IsValidPng(headerGradientImage))
                {
                    container.Add(new CuiElement
                    {
                        Name = "BUYABLE_UI_HEADER_BACKGROUND",
                        Parent = "BUYABLE_UI_HEADER",
                        Components = {
                            new CuiRawImageComponent { Color = $"1 1 1 {transparencyAlpha}", Png = headerGradientImage },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                        }
                    });
                }
            }
            
            // Add "Buy a Raid" text
            var headerText = IsPNPCBuilderMode(player) ? "CHOOSE A BASE TO BUILD" : "BUY A RAID BASE";
            container.Add(new CuiElement
            {
                Name = "BUYABLE_UI_HEADER_TEXT",
                Parent = "BUYABLE_UI_HEADER",
                Components = {
                    new CuiTextComponent { Text = headerText, Font = fontName, FontSize = 26, Align = align, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-551.004 -27.67", OffsetMax = "550.996 72.33" }
                }
            });
            
            // Add boy.png image on the left side of the header
            var boyImage = UiHandler.GetImage("boy");
            if (IsValidPng(boyImage))
            {
                container.Add(new CuiElement
                {
                    Name = "BUYABLE_UI_HEADER_BOY",
                    Parent = "BUYABLE_UI_HEADER",
                    Components = {
                        new CuiRawImageComponent { Color = "1 1 1 1", Png = boyImage },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "10 10", OffsetMax = "90 -10" }
                    }
                });
            }
            
            // Navigation buttons in header panel, left-aligned after boy image
            // Position: Image (left) -> Back -> Next (one after the other)
            // IMPORTANT: Add buttons AFTER gradient so they render on top
            var buttonWidth = 60.0;
            var buttonHeight = 30.0;
            var buttonSpacing = 25.0; // Spacing between buttons
            
            // Position buttons in header, starting after boy image
            var buttonStartX = 110.0; // After boy image (~90px wide + 20px spacing)
            var buttonYBottom = 10.0; // Match boy image bottom (10px from header bottom)
            var buttonYTop = buttonYBottom + buttonHeight; // Top of button (40px from bottom)
            
            // Calculate X positions - BACK is always first, NEXT is always after BACK with spacing
            var backX = buttonStartX;
            var nextX = backX + buttonWidth + buttonSpacing; // NEXT is always after BACK, even if BACK doesn't exist
            
            DebugPuts($"[DEBUG] Navigation buttons - BACK at X={backX}, NEXT at X={nextX}, BACK visible: {indexToStartFrom > 0}, NEXT visible: {(baseNames.Count - 1) - indexToStartFrom > 20}");
            
            // BACK button - separate button, positioned first (added AFTER gradient)
            if (indexToStartFrom > 0)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "0.4 0.4 0.4 0.8", Command = $"ui_buyable_changepage {indexToStartFrom - 21} {mode}" },
                    Text = { Text = "BACK", Font = fontName, FontSize = 16, Align = align, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{backX} {buttonYBottom}", OffsetMax = $"{backX + buttonWidth} {buttonYTop}" }
                }, "BUYABLE_UI_HEADER", $"NavBack_{mode}_{indexToStartFrom}");
            }
            
            // NEXT button - separate button, positioned after BACK (added AFTER gradient)
            if ((baseNames.Count - 1) - indexToStartFrom > 20)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "0.4 0.4 0.4 0.8", Command = $"ui_buyable_changepage {indexToStartFrom + 21} {mode}" },
                    Text = { Text = "NEXT", Font = fontName, FontSize = 16, Align = align, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{nextX} {buttonYBottom}", OffsetMax = $"{nextX + buttonWidth} {buttonYTop}" }
                }, "BUYABLE_UI_HEADER", $"NavNext_{mode}_{indexToStartFrom}");
            }
            
            // Add color picker - MUST BE ABSOLUTE LAST so it's on top and clickable
            AddColorPicker(container, player, playerColor);

            CuiHelper.DestroyUi(player, BUYABLE_UI_OPTIONS);
            CuiHelper.AddUi(player, container);
        }

        private static bool IsValidPng(string png) =>
            !string.IsNullOrEmpty(png) && png != $"{RB_LOGO}" && uint.TryParse(png, out var id) && id != 0;

        private string _(string hex, float a)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 7) return hex;
            return $"{_(hex, 0, 2) / 255} {_(hex, 2, 2) / 255} {_(hex, 4, 2) / 255} {Mathf.Clamp(a, 0f, 1f)}";
        }

        private double _(string hex, int j, int k)
        {
            return int.TryParse(hex.TrimStart('#').Substring(j, k), NumberStyles.AllowHexSpecifier, NumberFormatInfo.CurrentInfo, out var num) ? num : 1;
        }

        private static bool FileExists(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return false;
            var path = Path.Combine(Paths.CopyPasteDir, baseName + ".json");
            return File.Exists(path);
        }

        private void LoadProfiles()
        {
            _PROFILES.Clear();
            string folder = Paths.ProfilesDir;
            if (!Directory.Exists(folder))
            {
                Puts("Profiles directory not found: {0}", folder);
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.json");
            }
            catch (UnauthorizedAccessException ex)
            {
                Puts(ex.ToString());
                return;
            }

            foreach (string file in files)
            {
                try
                {
                    if (file.Contains("_empty"))
                        continue;

                    var profileName = Path.GetFileNameWithoutExtension(file);
                    var json = File.ReadAllText(file);
                    var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (data == null) continue;

                    var extra = new List<string>();
                    var mode = "Close";

                    foreach (var element in data)
                    {
                        if (element.Key == "Additional Bases")
                        {
                            JObject jo = element.Value as JObject ?? (element.Value != null ? JObject.FromObject(element.Value) : null);
                            if (jo == null) continue;
                            foreach (var obj in jo.ToObject<Dictionary<string, object>>())
                            {
                                if (FileExists(obj.Key))
                                    extra.Add(obj.Key);
                            }
                        }
                        else if (element.Key.StartsWith("Difficulty") && !(element.Key.Contains("Level")))
                        {
                            var str = element.Value?.ToString();
                            if (!string.IsNullOrEmpty(str) && str != "Close")
                                mode = str;
                        }
                    }

                    AddFiles(mode, extra);
                }
                catch (JsonSerializationException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    Puts("{0}\n{1}", file, ex);
                }
            }

            Puts("Loaded {0} buyable categories from profiles.", _PROFILES.Count);
        }

        private void AddFiles(string mode, List<string> extras)
        {
            if (!_PROFILES.TryGetValue(mode, out var files))
            {
                _PROFILES[mode] = files = new();
            }
            foreach (var extra in extras)
            {
                if (!files.Contains(extra))
                {
                    files.Add(extra);
                }
            }
        }

        public string GetBuyableCost(string mode)
        {
            return _COSTS.TryGetValue(mode, out var value) ? $" ({value})" : string.Empty;
        }

        private void LoadCosts()
        {
            var path = Paths.RaidableBasesConfigFile;
            if (!File.Exists(path)) return;

            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(path));
            }
            catch
            {
                return;
            }

            var settings = root["Settings"] as JObject;
            if (settings == null) return;

            var buyableEventCost = settings["Buyable Event Costs"] as JObject;
            if (buyableEventCost == null) return;

            bool Require(string key) => buyableEventCost[key]?.Type == JTokenType.Boolean && buyableEventCost[key].Value<bool>();

            if (Require("Require Economics Costs"))
            {
                var economics = ToDict(settings["Economics Buy Raid Costs (0 = disabled)"]);
                if (economics != null && economics.Count > 0 && ExtractCosts(economics, true))
                    return;
            }
            if (Require("Require Server Rewards Costs"))
            {
                var sr = ToDict(settings["ServerRewards Buy Raid Costs (0 = disabled)"]);
                if (sr != null && sr.Count > 0 && ExtractCosts(sr, false))
                    return;
            }
            if (Require("Require Custom Costs"))
            {
                var custom = ToDict(settings["Custom Buy Raid Cost"]);
                if (custom != null && custom.Count > 0 && ExtractCosts(custom, false))
                    return;
            }
            Puts("Buyable Event Costs has not been configured in RaidableBases.");
        }

        private static Dictionary<string, object> ToDict(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.ToObject<Dictionary<string, object>>(); }
            catch { return null; }
        }

        private void LoadPlayerPreferences()
        {
            try
            {
                var path = Paths.PlayerPreferencesFile;
                if (!File.Exists(path))
                {
                    Puts("Player preferences data file not found, starting fresh.");
                    return;
                }

                var data = JsonConvert.DeserializeObject<Dictionary<string, PlayerPreferences>>(File.ReadAllText(path));
                if (data == null)
                {
                    Puts("Failed to load player preferences, starting fresh.");
                    return;
                }

                foreach (var kvp in data)
                {
                    if (!string.IsNullOrEmpty(kvp.Value.Color) && kvp.Value.Color != "default")
                        _playerColors[kvp.Key] = kvp.Value.Color;
                    var transparency = Mathf.Clamp(kvp.Value.Transparency, 10f, 100f);
                    _playerTransparency[kvp.Key] = transparency;
                }

                Puts("Loaded preferences for {0} players.", data.Count);
            }
            catch (Exception ex)
            {
                Puts("Error loading player preferences: {0}", ex.Message);
            }
        }

        private void SavePlayerPreferences()
        {
            try
            {
                var data = new Dictionary<string, PlayerPreferences>();
                var allUserIds = new HashSet<string>();
                foreach (var userId in _playerColors.Keys)
                    allUserIds.Add(userId);
                foreach (var userId in _playerTransparency.Keys)
                    allUserIds.Add(userId);

                foreach (var userId in allUserIds)
                {
                    var color = _playerColors.TryGetValue(userId, out var c) ? c : "default";
                    var transparency = _playerTransparency.TryGetValue(userId, out var t) ? t : DEFAULT_TRANSPARENCY;
                    if (color != "default" || transparency != DEFAULT_TRANSPARENCY)
                    {
                        data[userId] = new PlayerPreferences
                        {
                            Color = color,
                            Transparency = transparency
                        };
                    }
                }

                Paths.EnsureDataDirs();
                File.WriteAllText(Paths.PlayerPreferencesFile, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Puts("Error saving player preferences: {0}", ex.Message);
            }
        }

        private bool ExtractCosts(Dictionary<string, object> costs, bool eco)
        {
            bool any = false;
            foreach (var mode in _PROFILES.Keys)
            {
                if (!costs.TryGetValue(mode.ToString(), out var obj) || obj == null)
                    continue;

                if (obj is JValue jv)
                    obj = jv.Value;

                // Custom cost list
                IList list = null;
                if (obj is IList il && obj is not string)
                    list = il;
                else if (obj is JArray jarr)
                    list = jarr;

                if (list != null)
                {
                    foreach (var obj2 in list)
                    {
                        Dictionary<string, object> dict = null;
                        if (obj2 is Dictionary<string, object> d) dict = d;
                        else if (obj2 is JObject jo) dict = jo.ToObject<Dictionary<string, object>>();
                        if (dict == null) continue;

                        bool enabled = false;
                        if (dict.TryGetValue("Enabled", out var en))
                        {
                            if (en is bool b) enabled = b;
                            else if (en is JValue je && je.Type == JTokenType.Boolean) enabled = je.Value<bool>();
                            else bool.TryParse(en?.ToString(), out enabled);
                        }
                        if (!enabled) continue;

                        if (!dict.TryGetValue("Amount", out var obj3) || obj3 == null) continue;
                        if (!double.TryParse(obj3.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                            continue;

                        any = true;
                        string itemName = dict.TryGetValue("Item Name", out var n1) ? n1?.ToString() ?? "" : "";
                        string shortname = dict.TryGetValue("Item Shortname", out var n2) ? n2?.ToString() ?? "" : "";
                        _COSTS[mode] = $"{amount} {(string.IsNullOrEmpty(itemName) ? shortname : itemName)}";
                    }
                    continue;
                }

                if (double.TryParse(obj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cost) && cost > 0)
                {
                    _COSTS[mode] = eco ? $"${cost}" : $"{cost} RP";
                    any = true;
                }
            }
            return any;
        }

        private ImageUiHandler UiHandler;

        private class ImageUiHandler
        {
            public ImageUiHandler()
            {
            }

            internal Coroutine _coroutine;
            internal Dictionary<string, string> Images = new();

            public void LoadImages()
            {
                // Harmony OnLoaded runs before ServerMgr exists. Defer so we never NRE
                // or touch FileStorage while identity is still the default.
                if (ServerMgr.Instance == null)
                {
                    try
                    {
                        var go = new GameObject("RBBUI_ImageLoadWait");
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        go.AddComponent<ImageLoadWaitBehaviour>().Begin(this);
                    }
                    catch (Exception ex)
                    {
                        Puts("LoadImages defer failed: " + ex.Message);
                    }
                    return;
                }

                if (_coroutine != null)
                    return;
                _coroutine = ServerMgr.Instance.StartCoroutine(LoadAllImages());
            }

            private sealed class ImageLoadWaitBehaviour : MonoBehaviour
            {
                private ImageUiHandler _handler;

                public void Begin(ImageUiHandler handler)
                {
                    _handler = handler;
                    StartCoroutine(Wait());
                }

                private IEnumerator Wait()
                {
                    for (int i = 0; i < 120; i++)
                    {
                        if (ServerMgr.Instance != null)
                        {
                            var identity = ConVar.Server.identity;
                            if (!string.IsNullOrEmpty(identity) &&
                                !string.Equals(identity, "my_server_identity", StringComparison.OrdinalIgnoreCase))
                            {
                                var handler = _handler;
                                Destroy(gameObject);
                                handler?.LoadImages();
                                yield break;
                            }
                        }
                        yield return new WaitForSeconds(0.5f);
                    }
                    Destroy(gameObject);
                }
            }

            private IEnumerator LoadAllImages()
            {
                // Wait until CommunityEntity + FileStorage are safe (correct identity).
                for (int i = 0; i < 120; i++)
                {
                    var identity = ConVar.Server.identity;
                    bool identityOk = !string.IsNullOrEmpty(identity) &&
                        !string.Equals(identity, "my_server_identity", StringComparison.OrdinalIgnoreCase);
                    var ce = CommunityEntity.ServerInstance;
                    if (identityOk && ce != null && ce.net != null)
                    {
                        bool fsOk = false;
                        try { fsOk = FileStorage.server != null; }
                        catch { fsOk = false; }
                        if (fsOk) break;
                    }
                    yield return new WaitForSeconds(0.5f);
                }

                yield return LoadGradientImages();
                yield return LoadRaidsDirectoryImages(); // Load all images from raids directory
                yield return LoadBackdropImage(); // Load backdrop image from local file system
            }
            
            private IEnumerator LoadRaidsDirectoryImages()
            {
                var raidsDir = Paths.RaidsDir;
                
                if (!Directory.Exists(raidsDir))
                {
                    Puts($"Raids directory not found: {raidsDir} - creating directory");
                    try
                    {
                        Directory.CreateDirectory(raidsDir);
                        Puts($"Created raids directory: {raidsDir}");
                    }
                    catch (Exception ex)
                    {
                        Puts($"Error creating raids directory: {ex.Message}");
                    }
                    yield return null;
                    yield break;
                }
                
                Puts($"Scanning and loading images from raids directory: {raidsDir}");
                
                var pngFiles = Directory.GetFiles(raidsDir, "*.png", SearchOption.TopDirectoryOnly);
                Puts($"Found {pngFiles.Length} PNG files in raids directory");
                
                var loadedCount = 0;
                var indexedCount = 0;
                foreach (var filePath in pngFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    
                    // Skip if already loaded with this exact name
                    if (Images.ContainsKey(fileName))
                    {
                        continue;
                    }
                    
                    bool loadSuccess = false;
                    try
                    {
                        var bytes = File.ReadAllBytes(filePath);
                        if (bytes != null && bytes.Length > 0)
                        {
                            var textureId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                            var textureIdString = textureId.ToString();
                            
                            // Store with original filename as primary key
                            Images[fileName] = textureIdString;
                            loadedCount++;
                            
                            // Also index by common name variations for faster lookup
                            var variations = new List<string>
                            {
                                fileName.ToLower(),
                                fileName.ToUpper(),
                                ToImageKey(fileName),
                                ToPascalCase(fileName),
                                ToPascalCase(fileName).ToLower()
                            };
                            
                            // Remove "raid" prefix variations
                            if (fileName.StartsWith("raid", StringComparison.OrdinalIgnoreCase))
                            {
                                var withoutPrefix = fileName.Substring(4);
                                variations.Add(withoutPrefix);
                                variations.Add(withoutPrefix.ToLower());
                                variations.Add(ToPascalCase(withoutPrefix));
                            }
                            
                            // Index by all variations (case-insensitive)
                            foreach (var variation in variations)
                            {
                                if (!string.IsNullOrEmpty(variation) && !Images.ContainsKey(variation))
                                {
                                    Images[variation] = textureIdString;
                                    indexedCount++;
                                }
                            }
                            
                            loadSuccess = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Puts($"Error loading image {fileName}: {ex.Message}");
                    }
                    
                    // Yield every 10 images to avoid blocking
                    if (loadSuccess && loadedCount % 10 == 0)
                    {
                        yield return null;
                    }
                }
                
                Puts($"OK: Loaded {loadedCount} images from raids directory (indexed {indexedCount} name variations, total in cache: {Images.Count})");
                Puts($"Images are automatically matched by name - add PNG files to the raids folder to display them");
                yield return null;
            }

            public IEnumerator ReloadRaidsDirectoryImages()
            {
                Puts($"Reloading images from raids directory...");
                yield return LoadRaidsDirectoryImages();
                Puts($"Image reload complete.");
            }
            
            private IEnumerator LoadBackdropImage()
            {
                // Check if backdrop is already loaded
                if (Images.ContainsKey("backdrop"))
                {
                    yield return null;
                    yield break;
                }
                
                // Try to load backdrop from local Images directory
                var imagesDir = Paths.ImagesDir;
                var backdropPath = Path.Combine(imagesDir, "backdrop.png");
                if (!File.Exists(backdropPath))
                {
                    var alt = Path.Combine(imagesDir, "wADvKGi.png");
                    if (File.Exists(alt))
                        backdropPath = alt;
                }

                if (File.Exists(backdropPath))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(backdropPath);
                        if (bytes != null && bytes.Length > 0)
                        {
                            var textureId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                            Images["backdrop"] = textureId.ToString();
                            Puts("OK: Loaded backdrop image from local file -> texture ID: {0}", textureId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Puts("Error loading backdrop image: {0}", ex.Message);
                    }
                }
                else
                {
                    Puts("Backdrop image not found at: {0} (using default)", Path.Combine(imagesDir, "backdrop.png"));
                }
                
                yield return null;
            }

            private IEnumerator LoadGradientImages()
            {
                var gradientColors = new[] { "blue", "green", "purple", "red" };
                
                // Use Oxide's DataFileSystem to get the correct path
                var fullImagesPath = Paths.ImagesDir;
                
                // Ensure directory exists
                if (!Directory.Exists(fullImagesPath))
                {
                    Directory.CreateDirectory(fullImagesPath);
                    Puts($"Created directory: {fullImagesPath}");
                }
                
                Puts($"Looking for gradient images in: {fullImagesPath}");
                
                // List files in directory for debugging
                if (Directory.Exists(fullImagesPath))
                {
                    var files = Directory.GetFiles(fullImagesPath, "*.png");
                    Puts($"Found {files.Length} PNG files in directory:");
                    foreach (var file in files)
                    {
                        Puts($"  - {Path.GetFileName(file)}");
                    }
                }
                
                foreach (var color in gradientColors)
                {
                    var imageKey = $"gradient_{color}";
                    var fileName = $"{imageKey}.png";
                    var filePath = Path.Combine(fullImagesPath, fileName);
                    
                    // Normalize path separators for Windows
                    filePath = Path.GetFullPath(filePath);
                    
                    Puts($"Checking for gradient image: {filePath}");
                    
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            var bytes = File.ReadAllBytes(filePath);
                            if (bytes != null && bytes.Length > 0)
                            {
                                // Store image in FileStorage and get texture ID
                                var textureId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                                Images[imageKey] = textureId.ToString();
                                Puts($"OK: Loaded gradient image: {imageKey} -> texture ID: {textureId} (file: {fileName}, size: {bytes.Length} bytes)");
                            }
                            else
                            {
                                Puts($"Warning: Gradient image file is empty: {filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Puts($"Error loading gradient image {fileName}: {ex.Message}");
                            Puts($"Stack trace: {ex.StackTrace}");
                        }
                    }
                    else
                    {
                        Puts($"FAIL: Gradient image not found: {filePath}");
                        // Try alternative path
                        var altPath = Path.Combine(Paths.ServerRoot, "HarmonyImages", "RaidableBasesBuyableUI", fileName);
                        altPath = altPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                        if (File.Exists(altPath))
                        {
                            Puts($"Found image at alternative path: {altPath}");
                            try
                            {
                                var bytes = File.ReadAllBytes(altPath);
                                if (bytes != null && bytes.Length > 0)
                                {
                                    var textureId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                                    Images[imageKey] = textureId.ToString();
                                    Puts($"OK: Loaded gradient image from alternative path: {imageKey} -> texture ID: {textureId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Puts($"Error loading from alternative path: {ex.Message}");
                            }
                        }
                    }
                }
                
                // Load boy.png image for title panel
                var boyImageKey = "boy";
                var boyFileName = "boy.png";
                var boyFilePath = Path.Combine(fullImagesPath, boyFileName);
                boyFilePath = Path.GetFullPath(boyFilePath);
                
                Puts($"Checking for boy image: {boyFilePath}");
                
                if (File.Exists(boyFilePath))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(boyFilePath);
                        if (bytes != null && bytes.Length > 0)
                        {
                            var textureId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                            Images[boyImageKey] = textureId.ToString();
                            Puts($"OK: Loaded boy image: {boyImageKey} -> texture ID: {textureId} (file: {boyFileName}, size: {bytes.Length} bytes)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Puts($"Error loading boy image {boyFileName}: {ex.Message}");
                    }
                }
                else
                {
                    Puts($"FAIL: Boy image not found: {boyFilePath}");
                }
                
                Puts($"Loaded {Images.Count} gradient images total");
                yield return null;
            }



            public ulong GetSkin(string key)
            {
                // Skins support removed - always return 0 to use images instead
                return 0;
            }

            public string GetImage(string key)
            {
                // First check if image is already loaded in dictionary
                if (Images.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
                
                // If not found, try to load from local raids directory with multiple name variations
                var imagePath = LoadImageFromRaidsDirectory(key);
                if (!string.IsNullOrEmpty(imagePath))
                {
                    return imagePath;
                }
                
                // Never return a workshop skin ID as FileStorage png (client AddUI NullRef / blank UI)
                return null;
            }
            
            private string LoadImageFromRaidsDirectory(string baseName)
            {
                try
                {
                    // Construct path to raids directory
                    var raidsDir = Paths.RaidsDir;
                    
                    if (!Directory.Exists(raidsDir))
                    {
                        return null;
                    }
                    
                    // Try multiple name variations to find the image file
                    var nameVariations = new List<string>
                    {
                        baseName,                                    // Exact match: "raideasy1"
                        baseName.ToLower(),                          // Lowercase: "raideasy1"
                        baseName.ToUpper(),                          // Uppercase: "RAIDEASY1"
                        ToImageKey(baseName),                        // Image key format: "raideasy1" (sanitized)
                        ToPascalCase(baseName),                      // PascalCase: "RaidEasy1"
                        ToPascalCase(baseName).ToLower(),            // PascalCase lowercase: "raideasy1"
                    };
                    
                    // Also try with common prefixes/suffixes removed
                    var cleanName = baseName;
                    if (cleanName.StartsWith("raid", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanName = cleanName.Substring(4);
                        nameVariations.Add(cleanName);
                        nameVariations.Add(cleanName.ToLower());
                        nameVariations.Add(ToPascalCase(cleanName));
                    }
                    
                    // Remove duplicates while preserving order
                    var uniqueVariations = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var variation in nameVariations)
                    {
                        if (!string.IsNullOrEmpty(variation) && seen.Add(variation))
                        {
                            uniqueVariations.Add(variation);
                        }
                    }
                    
                    // Try each variation
                    foreach (var nameVariation in uniqueVariations)
                    {
                        var imagePath = Path.Combine(raidsDir, $"{nameVariation}.png");
                        
                        if (File.Exists(imagePath))
                        {
                            // Read the image file
                            var bytes = File.ReadAllBytes(imagePath);
                            if (bytes != null && bytes.Length > 0)
                            {
                                // Store in FileStorage and cache in Images dictionary
                                var textureId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                                var textureIdString = textureId.ToString();
                                Images[baseName] = textureIdString;
                                Puts($"OK: Loaded image from raids directory: {baseName} -> texture ID: {textureId} (matched file: {nameVariation}.png, size: {bytes.Length} bytes)");
                                return textureIdString;
                            }
                        }
                    }
                    
                    // If no exact match found, try case-insensitive file search
                    var pngFiles = Directory.GetFiles(raidsDir, "*.png", SearchOption.TopDirectoryOnly);
                    foreach (var filePath in pngFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        if (string.Equals(fileName, baseName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fileName, ToImageKey(baseName), StringComparison.OrdinalIgnoreCase))
                        {
                            var bytes = File.ReadAllBytes(filePath);
                            if (bytes != null && bytes.Length > 0)
                            {
                                var textureId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                                var textureIdString = textureId.ToString();
                                Images[baseName] = textureIdString;
                                Puts($"OK: Loaded image from raids directory (case-insensitive match): {baseName} -> texture ID: {textureId} (matched file: {fileName}.png, size: {bytes.Length} bytes)");
                                return textureIdString;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Puts($"Error loading image from raids directory for {baseName}: {ex.Message}");
                }
                
                return null;
            }
            
            private string ToImageKey(string name)
            {
                if (string.IsNullOrEmpty(name)) return string.Empty;
                var sb = new System.Text.StringBuilder(name.Length);
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                        sb.Append(char.ToLowerInvariant(c));
                }
                return sb.ToString();
            }
            
            private string ToPascalCase(string name)
            {
                if (string.IsNullOrEmpty(name)) return string.Empty;
                var sb = new System.Text.StringBuilder(name.Length);
                bool newWord = true;
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    bool isAlnum = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                    if (!isAlnum)
                    {
                        newWord = true;
                        continue;
                    }
                    if (newWord)
                    {
                        sb.Append(char.ToUpperInvariant(c));
                        newWord = false;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            public string GetGradientImage(string colorName)
            {
                var imageKey = $"gradient_{colorName}";
                var imageId = GetImage(imageKey);
                if (!IsValidPng(imageId))
                {
                    Puts($"Warning: Gradient image not found for color: {colorName} (key: {imageKey})");
                    return null;
                }
                return imageId;
            }


            public void Unload()
            {
                if (_coroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_coroutine);
                    _coroutine = null;
                }
            }
        }

        internal static void Puts(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format)) return;
            var msg = args != null && args.Length != 0 ? string.Format(format, args) : format;
            Debug.Log("[RaidableBasesBuyableUI] " + msg);
        }

        private void DebugPuts(string format, params object[] args)
        {
            if (config == null || !config.EnableDebugLogging) return;
            Puts(format, args);
        }

        private class PlayerPreferences
        {
            [JsonProperty("Color")]
            public string Color { get; set; } = "default";

            [JsonProperty("Transparency")]
            public float Transparency { get; set; } = 100f;
        }

        #region Configuration

        private Configuration config;

        public class Configuration
        {
            [JsonProperty(PropertyName = "Enable Debug Logging")]
            public bool EnableDebugLogging { get; set; } = false;

            [JsonProperty(PropertyName = "Close Button Color")]
            public string CloseColor { get; set; } = "#497CAF";

            [JsonProperty(PropertyName = "Close Text Color")]
            public string CloseTextColor { get; set; } = "#0000FF";

            [JsonProperty(PropertyName = "Backdrop URL")]
            public string BackdropURL { get; set; } = "https://i.imgur.com/wADvKGi.png";

            [JsonProperty(PropertyName = "Allowed Categories", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AllowedCategories { get; set; } = new List<string>();

            [JsonProperty(PropertyName = "Difficulty")]
            public Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
            {
                { "Easy Button Color", "#446488" },
                { "Easy Text Color", "#00FF00" },
                { "Medium Button Color", "#446488" },
                { "Medium Text Color", "#FFEB04" },
                { "Hard Button Color", "#446488" },
                { "Hard Text Color", "#FF0000" },
                { "Expert Button Color", "#446488" },
                { "Expert Text Color", "#0000FF" },
                { "Nightmare Button Color", "#446488" },
                { "Nightmare Text Color", "#000000" },
            };

            public string GetButtonColor(string mode)
            {
                if (Colors.TryGetValue($"{mode} Button Color", out var color)) return color;
                return CloseColor;
            }

            public string GetButtonTextColor(string mode)
            {
                if (Colors.TryGetValue($"{mode} Text Color", out var color)) return color;
                return CloseTextColor;
            }
        }

        private void LoadConfig()
        {
            var path = Paths.ConfigFile;
            try
            {
                if (File.Exists(path))
                {
                    config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(path));
                    if (config != null)
                    {
                        // Ensure Allowed Categories etc. persist / fill missing
                        SaveConfig();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Puts(ex.ToString());
            }

            config = new Configuration();
            SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(Paths.ConfigFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(Paths.ConfigFile, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Puts("Config save failed: {0}", ex.Message);
            }
        }

        #endregion Configuration
    }
}

namespace RaidableBasesBuyableUI.ExtensionMethods
{
    public static class ExtensionMethods
    {
        public static string[] ToStringArray(this string[] args) => args;
        public static string[] ToStringArray(this Facepunch.StringView[] args)
        {
            if (args == null || args.Length == 0) return Array.Empty<string>();
            string[] array = new string[args.Length];
            for (int i = 0; i < args.Length; i++) array[i] = args[i].ToString();
            return array;
        }
        public static string[] Skip(this string[] a, int b)
        {
            if (a == null || a.Length == 0 || b >= a.Length) return Array.Empty<string>();
            if (b <= 0) return a;
            string[] c = new string[a.Length - b];
            int n = 0;
            for (int i = b; i < a.Length; i++) { c[n++] = a[i]; }
            return c;
        }
    }
}

