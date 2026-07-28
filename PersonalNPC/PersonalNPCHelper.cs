// Requires: PersonalNPC

using System;
using System.Linq;
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace PersonalNPCHarmony
{
    /// <summary>
    /// PersonalNPCHelper 1.3.0 ported for Harmony. Co-hosted with PersonalNPC in one DLL, so the
    /// Oxide load/unload dance around the parent plugin is gone.
    /// </summary>
    public class PersonalNPCHelper : PersonalNPCPluginBase
    {
        internal Plugin PersonalNPC, PNPCAddonBuilder, RaidableBasesBuyableUI;

        public override string Name => "PersonalNPCHelper";

        public PersonalNPCHelper()
        {
            Version = new VersionNumber(1, 3, 0);
        }

        private StoredData _data;
        private const string PersonalNPCPerm = "personalnpc.bot1";
        private const string BuyableUiPluginName = "RaidableBasesBuyableUI";
        private const string WheelName = "PNPCWheel";
        private const string BuildUiName = "PNPCBuildUi";

        private readonly HashSet<ulong> _wheelOpen = new HashSet<ulong>();
        private readonly HashSet<ulong> _buildUiOpen = new HashSet<ulong>();

        #region Frankenstein Parts

        private static readonly string[] HeadShortnames = {
            "frankensteins.monster.01.head",
            "frankensteins.monster.02.head",
            "frankensteins.monster.03.head"
        };

        private static readonly string[] TorsoShortnames = {
            "frankensteins.monster.01.torso",
            "frankensteins.monster.02.torso",
            "frankensteins.monster.03.torso"
        };

        private static readonly string[] LegsShortnames = {
            "frankensteins.monster.01.legs",
            "frankensteins.monster.02.legs",
            "frankensteins.monster.03.legs"
        };

        private static readonly HashSet<string> SpawnSubcommands = new HashSet<string> { "bot1", "bot2", "bot3" };

        #endregion

        #region Wheel Slot Definitions

        private struct WheelSlot
        {
            public readonly string Label;
            public readonly string Id;
            public readonly string PnpcCmd;
            public readonly string BtnColor;

            public WheelSlot(string label, string id, string pnpcCmd, string btnColor)
            {
                Label = label;
                Id = id;
                PnpcCmd = pnpcCmd;
                BtnColor = btnColor;
            }
        }

        private static readonly WheelSlot[] _slots = {
            new WheelSlot("FOLLOW",        "follow",     "/pnpc follow",     "0.30 0.69 0.31 0.85"),
            new WheelSlot("COMBAT",        "combat",     "/pnpc combat",     "0.90 0.49 0.13 0.85"),
            new WheelSlot("AUTO\nFARM",    "autofarm",   "/pnpc farm all",   "0.61 0.46 0.15 0.85"),
            new WheelSlot("LOOT\nALL",     "lootall",    "/pnpc loot-all",   "0.26 0.56 0.79 0.85"),
            new WheelSlot("DEPOSIT\nLOOT", "deposit",    "/pnpc deposit",    "0.55 0.35 0.65 0.85"),
            new WheelSlot("DESPAWN",       "despawn",    "/pnpc",            "0.55 0.17 0.17 0.85"),
            new WheelSlot("BUILD",         "build",      "",                 "0.45 0.55 0.25 0.85"),
            new WheelSlot("AUTO\nPICKUP",  "autopickup", "/pnpc pickup all", "0.15 0.65 0.55 0.85"),
            new WheelSlot("STAY",          "stay",       "/pnpc idle",       "0.46 0.46 0.46 0.85"),
        };

        #endregion

        #region Data

        private class StoredData
        {
            public HashSet<ulong> UnlockedPlayers = new HashSet<ulong>();
        }

        internal void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("PersonalNPC/PersonalNPCHelper", _data);

        #endregion

        #region Lifecycle

        internal void Init()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("PersonalNPC/PersonalNPCHelper") ?? new StoredData();

            if (_data.UnlockedPlayers.Count == 0)
            {
                var legacy = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("GrimmBotCraft");
                if (legacy?.UnlockedPlayers?.Count > 0)
                {
                    _data = legacy;
                    SaveData();
                    Puts($"Migrated {_data.UnlockedPlayers.Count} unlocked players from GrimmBotCraft data.");
                }
            }
        }

        internal void Unload()
        {
            CloseAllWheels();
            CloseAllBuildUis();
            SaveData();
        }
        // ---- Harmony lifecycle ----
        public override void HarmonyInit()
        {
            Init();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public override void HarmonyUnload()
        {
            Unload();
        }

        internal void OnServerSave() => SaveData();

        internal void OnServerInitialized()
        {
            foreach (ulong steamId in _data.UnlockedPlayers)
            {
                string id = steamId.ToString();
                if (!permission.UserHasPermission(id, PersonalNPCPerm))
                    permission.GrantUserPermission(id, PersonalNPCPerm, null);
            }


            EnsureBuyableUiPlugin();
        }

        private void EnsureBuyableUiPlugin()
        {
            if (!plugins.Exists(BuyableUiPluginName))
                Interface.Oxide.LoadPlugin(BuyableUiPluginName);
        }

        internal void OnNewSave()
        {
            CloseAllWheels();
            CloseAllBuildUis();

            foreach (ulong steamId in _data.UnlockedPlayers)
                permission.RevokeUserPermission(steamId.ToString(), PersonalNPCPerm);

            int count = _data.UnlockedPlayers.Count;
            _data.UnlockedPlayers.Clear();
            SaveData();
            Puts($"Wipe detected - reset {count} unlocked players and revoked {PersonalNPCPerm}.");
        }

        internal void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null)
            {
                _wheelOpen.Remove(player.userID);
                _buildUiOpen.Remove(player.userID);
            }
        }

        #endregion

        #region PersonalNPC API

        // Called from PersonalNPC.PlayerBotController.OnInput (same pattern as PNPCAddonBuilder)
        internal bool InputPNPC(BasePlayer player)
        {
            if (player == null || PersonalNPC == null)
                return false;

            if (!PersonalNPC.Call<bool>("HasBot", player))
                return false;

            // Builder addon owns the task button for the entire build session (mark spot, calculate, build)
            if (IsBuilderSessionActive(player))
                return false;

            // Build picker is open - swallow task button so wheel/tasks don't fire
            if (IsBuildPickerOpen(player))
                return true;

            if (_wheelOpen.Contains(player.userID))
            {
                CloseWheel(player);
                return true;
            }

            OpenWheel(player);
            return true;
        }

        private bool IsBuilderSessionActive(BasePlayer player)
        {
            if (PNPCAddonBuilder == null || !PNPCAddonBuilder.IsLoaded)
                return false;

            try
            {
                return PNPCAddonBuilder.Call<bool>("IsBuilderActive", player);
            }
            catch
            {
                return false;
            }
        }

        private bool IsBuildPickerOpen(BasePlayer player)
        {
            if (player == null) return false;
            if (_buildUiOpen.Contains(player.userID)) return true;
            if (RaidableBasesBuyableUI != null && RaidableBasesBuyableUI.IsLoaded)
            {
                try { return RaidableBasesBuyableUI.Call<bool>("IsUiOpen", player); }
                catch { }
            }
            return false;
        }

        #endregion

        #region Build Picker Hooks

        internal void OnPNPCBuilderBaseSelected(BasePlayer player, string baseName)
        {
            if (player == null || string.IsNullOrEmpty(baseName)) return;
            _buildUiOpen.Remove(player.userID);
            SelectBuild(player, baseName);
        }

        internal void OnPNPCBuilderBaseUiClosed(BasePlayer player)
        {
            if (player == null) return;
            _buildUiOpen.Remove(player.userID);
        }

        #endregion

        #region Frankenstein Unlock Gate

        internal object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return null;
            if (command.ToLower() != "pnpc") return null;
            if (PersonalNPC == null) return null;

            if (_data.UnlockedPlayers.Contains(player.userID))
                return null;

            bool isSpawnAttempt = IsSpawnAttempt(player, args);
            if (!isSpawnAttempt)
                return null;

            Item head = FindPartInInventory(player, HeadShortnames);
            Item torso = FindPartInInventory(player, TorsoShortnames);
            Item legs = FindPartInInventory(player, LegsShortnames);

            if (head != null && torso != null && legs != null)
            {
                head.UseItem(1);
                torso.UseItem(1);
                legs.UseItem(1);

                _data.UnlockedPlayers.Add(player.userID);
                SaveData();

                if (!permission.UserHasPermission(player.UserIDString, PersonalNPCPerm))
                    permission.GrantUserPermission(player.UserIDString, PersonalNPCPerm, null);

                player.ChatMessage("<color=#8BC34A>Your Frankenstein parts have been consumed. Bot unlocked permanently!</color>");
                return null;
            }

            string missing = BuildMissingMessage(head, torso, legs);
            player.ChatMessage($"<color=#FF9800>You need Frankenstein body parts to unlock your bot!</color>\n{missing}");
            return true;
        }

        private bool IsSpawnAttempt(BasePlayer player, string[] args)
        {
            bool hasBot = PersonalNPC.Call<bool>("HasBot", player);
            if (hasBot) return false;

            if (args == null || args.Length == 0) return true;

            string sub = args[0].ToLower();
            if (SpawnSubcommands.Contains(sub)) return true;

            return false;
        }

        private Item FindPartInInventory(BasePlayer player, string[] shortnames)
        {
            foreach (string shortname in shortnames)
            {
                Item item = player.inventory.FindItemByItemName(shortname);
                if (item != null) return item;
            }
            return null;
        }

        private string BuildMissingMessage(Item head, Item torso, Item legs)
        {
            var parts = new List<string>(3);
            if (head == null) parts.Add("<color=#F44336>Head</color> (any tier)");
            if (torso == null) parts.Add("<color=#F44336>Torso</color> (any tier)");
            if (legs == null) parts.Add("<color=#F44336>Legs</color> (any tier)");
            return "Missing: " + string.Join(", ", parts);
        }

        #endregion

        #region Build Picker UI

        internal void CmdBuildUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!arg.HasArgs())
            {
                OpenBuildUi(player);
                return;
            }

            string action = arg.GetString(0);

            if (action == "close")
            {
                CloseBuildUi(player);
                return;
            }

            if (action == "select" && arg.Args.Length > 1)
            {
                SelectBuild(player, arg.GetString(1));
                return;
            }

            if (action == "need" && arg.Args.Length > 1)
            {
                ShowBuildRequirements(player, arg.GetString(1));
            }
        }

        private void ShowBuildRequirements(BasePlayer player, string buildName)
        {
            if (PersonalNPC == null || PNPCAddonBuilder == null)
                return;

            var controller = PersonalNPC.Call("GetBotController", player);
            var builds = PNPCAddonBuilder.Call("GetBuildList", player, controller) as List<Dictionary<string, object>>;
            if (builds == null)
                return;

            for (int i = 0; i < builds.Count; i++)
            {
                var entry = builds[i];
                if (!string.Equals(entry["name"]?.ToString(), buildName, StringComparison.OrdinalIgnoreCase))
                    continue;

                player.ChatMessage($"<color=#FF9800>Materials needed for {buildName}:</color>");
                string detail = BuildResourceDetail(player, entry, true);
                player.ChatMessage(string.IsNullOrEmpty(detail) ? "No resources required." : detail);
                return;
            }
        }

        internal void OpenBuildUi(BasePlayer player)
        {
            if (PersonalNPC == null)
            {
                player.ChatMessage("<color=#FF9800>PersonalNPC is not loaded.</color>");
                return;
            }

            if (PNPCAddonBuilder == null || !PNPCAddonBuilder.IsLoaded)
            {
                player.ChatMessage("<color=#FF9800>PNPC Builder addon is not loaded.</color>");
                return;
            }

            if (!PersonalNPC.Call<bool>("HasBot", player))
            {
                player.ChatMessage("<color=#FF9800>You don't have a bot spawned!</color>");
                return;
            }

            CloseWheel(player);
            CloseBuildUi(player);
            EnsureBuyableUiPlugin();

            if (RaidableBasesBuyableUI != null && RaidableBasesBuyableUI.IsLoaded)
            {
                bool opened = false;
                try { opened = RaidableBasesBuyableUI.Call<bool>("OpenForPNPCBuilder", player); }
                catch { RaidableBasesBuyableUI.Call("OpenForPNPCBuilder", player); opened = true; }

                if (opened)
                {
                    _buildUiOpen.Add(player.userID);
                    return;
                }
            }

            OpenSimpleBuildUi(player);
        }

        private void OpenSimpleBuildUi(BasePlayer player)
        {
            var controller = PersonalNPC.Call("GetBotController", player);
            if (controller == null)
            {
                player.ChatMessage("<color=#FF9800>Could not find your bot controller.</color>");
                return;
            }

            var builds = PNPCAddonBuilder.Call("GetBuildList", player, controller) as List<Dictionary<string, object>>;
            if (builds == null || builds.Count == 0)
            {
                player.ChatMessage("<color=#FF9800>No bases configured. Install RaidableBasesBuyableUI or edit PNPCAddonBuilder.json</color>");
                return;
            }

            CloseWheel(player);
            CloseBuildUi(player);
            _buildUiOpen.Add(player.userID);

            var container = new CuiElementContainer();
            const float panelWidth = 360f;
            const float titleBarHeight = 32f;
            const float topPadding = 10f;
            const float bottomPadding = 10f;
            const float spaceBetweenButtons = 5f;
            const float buttonHeight = 64f;
            const float hintHeight = 22f;
            const float panelAlpha = 0.94f;
            string panelColor = $"0.08 0.09 0.11 {panelAlpha}";
            string titleColor = $"0.12 0.14 0.18 {panelAlpha}";

            float requiredPanelHeight = titleBarHeight + hintHeight + topPadding + bottomPadding +
                (buttonHeight * builds.Count) + (spaceBetweenButtons * Math.Max(0, builds.Count - 1));

            AddPanel(container, panelColor, "0.5 0.12", "0.5 0.12",
                $"{-panelWidth / 2f} 0", $"{panelWidth / 2f} {requiredPanelHeight}",
                "Overlay", BuildUiName, true);

            AddPanel(container, titleColor, "0 1", "1 1", "0 -32", "0 0", BuildUiName, "PNPCB_TitleBar");
            AddLabel(container, "Choose Base Design", 14, TextAnchor.MiddleCenter, "1 1 1 1",
                "0 0", "1 1", "40 0", "-4 0", "PNPCB_TitleBar", "PNPCB_Title");
            AddButton(container, "0.55 0.17 0.17 0.9", "pnpchelper.build close", "X", "1 1 1 1", 14,
                TextAnchor.MiddleCenter, "1 0.5", "1 0.5", "-42 -14", "-8 14", "PNPCB_TitleBar", "PNPCB_Close");

            AddLabel(container, "Click a base to select it. Required materials shown below each name.", 11, TextAnchor.MiddleCenter, "0.7 0.7 0.7 0.8",
                "0 1", "1 1", "8 -54", "-8 -32", BuildUiName, "PNPCB_Hint");

            for (int i = 0; i < builds.Count; i++)
            {
                var entry = builds[i];
                string name = entry["name"]?.ToString() ?? $"base{i}";
                string summary = entry["summary"]?.ToString() ?? "";
                string costDisplay = entry.ContainsKey("costDisplay") ? entry["costDisplay"]?.ToString() ?? "" : "";
                bool requireResources = entry.ContainsKey("requireResources") && Convert.ToBoolean(entry["requireResources"]);
                bool fileExists = !entry.ContainsKey("fileExists") || Convert.ToBoolean(entry["fileExists"]);
                bool canAfford = fileExists && (!entry.ContainsKey("canAfford") || Convert.ToBoolean(entry["canAfford"]));
                string file = entry["file"]?.ToString() ?? "";

                string buttonText = name;
                if (!string.IsNullOrEmpty(summary) && summary != costDisplay.Trim())
                    buttonText += $"\n<size=11>{summary}</size>";
                else if (!string.IsNullOrEmpty(summary))
                    buttonText += $"\n<size=11>{summary}</size>";
                else if (!string.IsNullOrEmpty(costDisplay))
                    buttonText += $"\n<size=10>{costDisplay}</size>";
                if (!string.IsNullOrEmpty(file))
                    buttonText += $"\n<size=9><color=#888>copypaste/{file}</color></size>";

                float buttonY = titleBarHeight + hintHeight + topPadding + i * (buttonHeight + spaceBetweenButtons);
                string btnColor = !fileExists ? "0.35 0.35 0.35 0.92" : canAfford ? "0.20 0.45 0.28 0.92" : "0.45 0.18 0.18 0.92";
                string command = !fileExists
                    ? "pnpchelper.build close"
                    : canAfford
                        ? $"pnpchelper.build select {name}"
                        : $"pnpchelper.build need {name}";

                AddButton(container, btnColor, command, buttonText, "1 1 1 1", 11,
                    TextAnchor.MiddleCenter, "0 1", "1 1",
                    $"10 {-buttonY - buttonHeight}", $"-10 {-buttonY}",
                    BuildUiName, $"PNPCB_{i}");
            }

            CuiHelper.AddUi(player, container);
        }

        private string BuildResourceDetail(BasePlayer player, Dictionary<string, object> entry, bool requireResources)
        {
            if (!requireResources || player == null)
                return string.Empty;

            var lines = new List<string>();
            AppendResourceLine(lines, "Wood", Convert.ToInt32(entry["wood"]), player.inventory.GetAmount(-151838493));
            AppendResourceLine(lines, "Stone", Convert.ToInt32(entry["stone"]), player.inventory.GetAmount(-2099697608));
            AppendResourceLine(lines, "Metal", Convert.ToInt32(entry["metal"]), player.inventory.GetAmount(69511070));
            AppendResourceLine(lines, "HQM", Convert.ToInt32(entry["hqm"]), player.inventory.GetAmount(317398316));
            AppendResourceLine(lines, "Gears", Convert.ToInt32(entry["gears"]), player.inventory.GetAmount(479143914));
            return lines.Count == 0 ? string.Empty : string.Join("  ", lines);
        }

        private static void AppendResourceLine(List<string> lines, string label, int need, int have)
        {
            if (need <= 0) return;
            string color = have >= need ? "#8BC34A" : "#F44336";
            lines.Add($"<color={color}>{label} {need}/{have}</color>");
        }

        private void SelectBuild(BasePlayer player, string buildName)
        {
            if (PNPCAddonBuilder == null || PersonalNPC == null)
                return;

            var controller = PersonalNPC.Call("GetBotController", player);
            if (controller == null)
            {
                player.ChatMessage("<color=#FF9800>Could not find your bot controller.</color>");
                return;
            }

            CloseBuildUi(player);

            var started = PNPCAddonBuilder.Call<bool>("TryStartBuildFromFile", player, controller, buildName);
            if (started != true)
                return;

            player.ChatMessage("<color=#8BC34A>Base selected: " + buildName + "</color>");
            player.ChatMessage("Wait for resource calculation, then <color=#FFD54F>aim at the build spot</color> and press your <color=#FFD54F>task button</color> (middle mouse) to place a marker arrow.");
        }

        internal void CloseBuildUi(BasePlayer player)
        {
            _buildUiOpen.Remove(player.userID);
            CuiHelper.DestroyUi(player, BuildUiName);
            if (RaidableBasesBuyableUI != null && RaidableBasesBuyableUI.IsLoaded)
                RaidableBasesBuyableUI.Call("CloseForPNPCBuilder", player);
        }

        internal void CloseAllBuildUis()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null)
                    CloseBuildUi(player);
            }
            _buildUiOpen.Clear();
        }

        private static void AddPanel(CuiElementContainer c, string color, string amin, string amax, string omin, string omax, string parent, string name, bool cursor = false)
        {
            c.Add(new CuiPanel
            {
                CursorEnabled = cursor,
                Image = { Color = color, Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
            }, parent, name);
        }

        private static void AddButton(CuiElementContainer c, string btnColor, string command, string text, string textColor, int fontSize, TextAnchor align, string amin, string amax, string omin, string omax, string parent, string name)
        {
            c.Add(new CuiButton
            {
                Button = { Color = btnColor, Command = command, Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                Text = { Text = text, FontSize = fontSize, Align = align, Color = textColor },
                RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
            }, parent, name);
        }

        private static void AddLabel(CuiElementContainer c, string text, int fontSize, TextAnchor align, string textColor, string amin, string amax, string omin, string omax, string parent, string name)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = fontSize, Align = align, Color = textColor },
                RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
            }, parent, name);
        }

        #endregion

        #region Radial Wheel UI

        internal void CmdBotWheel(BasePlayer player, string command, string[] args)
        {
            ToggleWheel(player);
        }

        internal void CmdBotWheelAlias(BasePlayer player, string command, string[] args)
        {
            ToggleWheel(player);
        }

        internal void CmdWheel(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!arg.HasArgs())
            {
                ToggleWheel(player);
                return;
            }

            string action = arg.GetString(0);
            CloseWheel(player);

            if (action == "close") return;

            if (action == "build")
            {
                OpenBuildUi(player);
                return;
            }

            if (PersonalNPC == null) return;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Id == action)
                {
                    rust.RunClientCommand(player, "chat.say", _slots[i].PnpcCmd);
                    return;
                }
            }
        }

        internal void ToggleWheel(BasePlayer player)
        {
            if (_wheelOpen.Contains(player.userID))
            {
                CloseWheel(player);
                return;
            }
            OpenWheel(player);
        }

        internal void OpenWheel(BasePlayer player)
        {
            if (PersonalNPC == null)
            {
                player.ChatMessage("<color=#FF9800>PersonalNPC is not loaded.</color>");
                return;
            }

            bool hasBot = PersonalNPC.Call<bool>("HasBot", player);
            if (!hasBot)
            {
                player.ChatMessage("<color=#FF9800>You don't have a bot spawned!</color>");
                return;
            }

            CuiHelper.DestroyUi(player, WheelName);
            _wheelOpen.Add(player.userID);

            var c = new CuiElementContainer();

            // Transparent full-screen overlay for cursor capture
            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.08" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", WheelName);

            // Click-anywhere-outside to close
            c.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "pnpchelper.wheel close" },
                Text = { Text = "" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, WheelName, "GW_CloseBg");

            // Center hub
            c.Add(new CuiPanel
            {
                Image = {
                    Color = "0.06 0.06 0.08 0.92",
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                },
                RectTransform = {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-26 -26", OffsetMax = "26 26"
                }
            }, WheelName, "GW_Hub");

            c.Add(new CuiLabel
            {
                Text = {
                    Text = "PNPC",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.55 0.85 0.55 1"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "GW_Hub", "GW_HubTxt");

            // Radial option buttons
            const float radius = 158f;
            const float halfW = 36f;
            const float halfH = 26f;

            for (int i = 0; i < _slots.Length; i++)
            {
                double angleStep = 360.0 / _slots.Length;
                double angleRad = (90.0 - i * angleStep) * Math.PI / 180.0;
                float cx = (float)(radius * Math.Cos(angleRad));
                float cy = (float)(radius * Math.Sin(angleRad));

                c.Add(new CuiButton
                {
                    Button = {
                        Color = _slots[i].BtnColor,
                        Command = $"pnpchelper.wheel {_slots[i].Id}",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    Text = {
                        Text = _slots[i].Label,
                        FontSize = 11,
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1"
                    },
                    RectTransform = {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = $"{cx - halfW} {cy - halfH}",
                        OffsetMax = $"{cx + halfW} {cy + halfH}"
                    }
                }, WheelName, $"GW_B{i}");
            }

            c.Add(new CuiLabel
            {
                Text = {
                    Text = "Click an action  ·  Click outside to close",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.75 0.75 0.75 0.6"
                },
                RectTransform = {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-300 -220", OffsetMax = "300 -190"
                }
            }, WheelName, "GW_Hint");

            CuiHelper.AddUi(player, c);
        }

        internal void CloseWheel(BasePlayer player)
        {
            _wheelOpen.Remove(player.userID);
            CuiHelper.DestroyUi(player, WheelName);
        }

        internal void CloseAllWheels()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && _wheelOpen.Contains(player.userID))
                    CuiHelper.DestroyUi(player, WheelName);
            }
            _wheelOpen.Clear();
        }

        #endregion

        #region Admin Console Commands

        internal void CmdReset(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                Puts("Usage: pnpchelper.reset <steamid|*>");
                return;
            }

            string first = arg.GetString(0);

            if (first == "*")
            {
                int count = _data.UnlockedPlayers.Count;
                foreach (ulong id in _data.UnlockedPlayers)
                    permission.RevokeUserPermission(id.ToString(), PersonalNPCPerm);
                _data.UnlockedPlayers.Clear();
                SaveData();
                Puts($"Reset all {count} unlocked players and revoked {PersonalNPCPerm}.");
                return;
            }

            if (ulong.TryParse(first, out ulong steamId))
            {
                if (_data.UnlockedPlayers.Remove(steamId))
                {
                    SaveData();
                    permission.RevokeUserPermission(steamId.ToString(), PersonalNPCPerm);
                    Puts($"Reset unlock and revoked {PersonalNPCPerm} for {steamId}.");
                }
                else Puts($"Player {steamId} was not unlocked.");
            }
            else Puts("Invalid Steam ID.");
        }

        internal void CmdGrant(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                Puts("Usage: pnpchelper.grant <steamid>");
                return;
            }

            string first = arg.GetString(0);

            if (ulong.TryParse(first, out ulong steamId))
            {
                if (_data.UnlockedPlayers.Add(steamId))
                {
                    SaveData();
                    permission.GrantUserPermission(steamId.ToString(), PersonalNPCPerm, null);
                    Puts($"Granted bot unlock and {PersonalNPCPerm} permission to {steamId}.");
                }
                else Puts($"Player {steamId} already unlocked.");
            }
            else Puts("Invalid Steam ID.");
        }

        #endregion
    }
}
