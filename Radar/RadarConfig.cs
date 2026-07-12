using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Radar
{
    /// <summary>
    /// Config for Harmony Radar. Mirrors key shapes from AdminRadar.json (colors, drawing distances, GUI, options, settings)
    /// and is stored at server-root HarmonyConfig/Radar.json per Harmony mods guide.
    /// </summary>
    public static class RadarConfig
    {
        [Serializable]
        public class ColorHexCodes
        {
            [JsonProperty("Player Arrows")]
            public string PlayerArrows { get; set; } = "#000000";

            [JsonProperty("Distance")]
            public string Distance { get; set; } = "#ffa500";

            [JsonProperty("Helicopters")]
            public string Helicopters { get; set; } = "#ff00ff";

            [JsonProperty("Bradley")]
            public string Bradley { get; set; } = "#ff00ff";

            [JsonProperty("MiniCopter")]
            public string MiniCopter { get; set; } = "#ff00ff";

            [JsonProperty("MiniCopter (ScrapTransportHelicopter)")]
            public string MiniCopterScrap { get; set; } = "#ff00ff";

            [JsonProperty("Online Player")]
            public string OnlinePlayer { get; set; } = "#ffffff";

            [JsonProperty("Online Player (Underground)")]
            public string OnlinePlayerUnderground { get; set; } = "#ffffff";

            [JsonProperty("Online Player (Flying)")]
            public string OnlinePlayerFlying { get; set; } = "#ffffff";

            [JsonProperty("Online Dead Player")]
            public string OnlineDeadPlayer { get; set; } = "#ff0000";

            [JsonProperty("Dead Player")]
            public string DeadPlayer { get; set; } = "#ff0000";

            [JsonProperty("Sleeping Player")]
            public string SleepingPlayer { get; set; } = "#00ffff";

            [JsonProperty("Sleeping Dead Player")]
            public string SleepingDeadPlayer { get; set; } = "#ff0000";

            [JsonProperty("Health")]
            public string Health { get; set; } = "#ff0000";

            [JsonProperty("Idle Time")]
            public string IdleTime { get; set; } = "#00ffff";

            [JsonProperty("Backpacks")]
            public string Backpacks { get; set; } = "#c0c0c0";

            [JsonProperty("Scientists")]
            public string Scientists { get; set; } = "#ffff00";

            [JsonProperty("Scientist Peacekeeper")]
            public string ScientistPeacekeeper { get; set; } = "#ffff00";

            [JsonProperty("Murderers")]
            public string Murderers { get; set; } = "#000000";

            [JsonProperty("Animals")]
            public string Animals { get; set; } = "#0000ff";

            [JsonProperty("Resources")]
            public string Resources { get; set; } = "#ffff00";

            [JsonProperty("Collectibles")]
            public string Collectibles { get; set; } = "#ffff00";

            [JsonProperty("Tool Cupboards")]
            public string ToolCupboards { get; set; } = "#ff9900";

            [JsonProperty("Sleeping Bags")]
            public string SleepingBags { get; set; } = "#ff00ff";

            [JsonProperty("Airdrops")]
            public string Airdrops { get; set; } = "#ff00ff";

            [JsonProperty("AutoTurrets")]
            public string AutoTurrets { get; set; } = "#ffff00";

            [JsonProperty("Corpses")]
            public string Corpses { get; set; } = "#ffff00";

            [JsonProperty("Box")]
            public string Box { get; set; } = "#ff00ff";

            [JsonProperty("Loot")]
            public string Loot { get; set; } = "#ffff00";

            [JsonProperty("Stash")]
            public string Stash { get; set; } = "#ffffff";

            [JsonProperty("Boat")]
            public string Boat { get; set; } = "#ff00ff";

            [JsonProperty("CargoPlane")]
            public string CargoPlane { get; set; } = "#ff00ff";

            [JsonProperty("CargoShip")]
            public string CargoShip { get; set; } = "#ff00ff";

            [JsonProperty("Car")]
            public string Car { get; set; } = "#ff00ff";

            [JsonProperty("CCTV")]
            public string CCTV { get; set; } = "#ff00ff";

            [JsonProperty("CH47")]
            public string CH47 { get; set; } = "#ff00ff";

            [JsonProperty("RidableHorse")]
            public string RidableHorse { get; set; } = "#ff00ff";

            [JsonProperty("MLRS")]
            public string MLRS { get; set; } = "#ff00ff";

            [JsonProperty("NPC")]
            public string NPC { get; set; } = "#ff00ff";

            [JsonProperty("RHIB")]
            public string RHIB { get; set; } = "#ff00ff";

            [JsonProperty("Traps")]
            public string Traps { get; set; } = "#ff00ff";

            [JsonProperty("Prefab")]
            public string Prefab { get; set; } = "#00ffff";
        }

        [Serializable]
        public class DrawingDistances
        {
            [JsonProperty("Sleepers Min Y")]
            public float SleepersMinY { get; set; } = 0.0f;

            [JsonProperty("Player Corpses")]
            public float PlayerCorpses { get; set; } = 200.0f;

            [JsonProperty("Players")]
            public float Players { get; set; } = 5000.0f;

            [JsonProperty("Airdrop Crates")]
            public float AirdropCrates { get; set; } = 400.0f;

            [JsonProperty("Animals")]
            public float Animals { get; set; } = 200.0f;

            [JsonProperty("Boats")]
            public float Boats { get; set; } = 150.0f;

            [JsonProperty("Boxes")]
            public float Boxes { get; set; } = 100.0f;

            [JsonProperty("BradleyAPC")]
            public float BradleyAPC { get; set; } = 9999.0f;

            [JsonProperty("Cargo Plane")]
            public float CargoPlane { get; set; } = 9999.0f;

            [JsonProperty("Cars")]
            public float Cars { get; set; } = 500.0f;

            [JsonProperty("CCTV")]
            public float CCTV { get; set; } = 500.0f;

            [JsonProperty("Collectibles")]
            public float Collectibles { get; set; } = 100.0f;

            [JsonProperty("Loot Containers")]
            public float LootContainers { get; set; } = 150.0f;

            [JsonProperty("MiniCopter")]
            public float MiniCopter { get; set; } = 200.0f;

            [JsonProperty("MLRS")]
            public float MLRS { get; set; } = 5000.0f;

            [JsonProperty("NPC Players")]
            public float NpcPlayers { get; set; } = 300.0f;

            [JsonProperty("Patrol Helicopter")]
            public float PatrolHelicopter { get; set; } = 9999.0f;

            [JsonProperty("Resources (Ore)")]
            public float ResourcesOre { get; set; } = 200.0f;

            [JsonProperty("Ridable Horses")]
            public float RidableHorses { get; set; } = 250.0f;

            [JsonProperty("Sleeping Bags")]
            public float SleepingBags { get; set; } = 250.0f;

            [JsonProperty("Stashes")]
            public float Stashes { get; set; } = 250.0f;

            [JsonProperty("Tool Cupboards")]
            public float ToolCupboards { get; set; } = 150.0f;

            [JsonProperty("Prefab")]
            public float Prefab { get; set; } = 50.0f;

            [JsonProperty("Traps")]
            public float Traps { get; set; } = 100.0f;

            [JsonProperty("Turrets")]
            public float Turrets { get; set; } = 100.0f;

            [JsonProperty("Vending Machines")]
            public float VendingMachines { get; set; } = 250.0f;

            [JsonProperty("Radar Drops Command")]
            public float RadarDropsCommand { get; set; } = 150.0f;

            // Extension for Harmony Radar (not in original AdminRadar.json)
            [JsonProperty("Backpacks")]
            public float Backpacks { get; set; } = 250.0f;
        }

        [Serializable]
        public class GuiConfig
        {
            [JsonProperty("Move Arrow Text")]
            public string MoveArrowText { get; set; } = "↕";

            // Pixel offsets for panel (anchor 0.75 0 = 25% from right). 220 wide, half height.
            [JsonProperty("Offset Min")]
            public string OffsetMin { get; set; } = "-220 20";

            [JsonProperty("Offset Max")]
            public string OffsetMax { get; set; } = "0 115";

            [JsonProperty("Color On")]
            public string ColorOn { get; set; } = "0.69 0.49 0.29 0.5";

            [JsonProperty("Color Off")]
            public string ColorOff { get; set; } = "0.29 0.49 0.69 0.5";

            [JsonProperty("Show Button - All")]
            public bool ShowButtonAll { get; set; } = true;

            [JsonProperty("Show Button - Airdrops")]
            public bool ShowButtonAirdrops { get; set; } = true;

            [JsonProperty("Show Button - Bags")]
            public bool ShowButtonBags { get; set; } = true;

            [JsonProperty("Show Button - Boats")]
            public bool ShowButtonBoats { get; set; } = false;

            [JsonProperty("Show Button - Bradley")]
            public bool ShowButtonBradley { get; set; } = false;

            [JsonProperty("Show Button - Box")]
            public bool ShowButtonBox { get; set; } = true;

            [JsonProperty("Show Button - Cars")]
            public bool ShowButtonCars { get; set; } = false;

            [JsonProperty("Show Button - CCTV")]
            public bool ShowButtonCCTV { get; set; } = true;

            [JsonProperty("Show Button - CargoPlanes")]
            public bool ShowButtonCargoPlanes { get; set; } = false;

            [JsonProperty("Show Button - CargoShips")]
            public bool ShowButtonCargoShips { get; set; } = false;

            [JsonProperty("Show Button - CH47")]
            public bool ShowButtonCH47 { get; set; } = false;

            [JsonProperty("Show Button - Collectibles")]
            public bool ShowButtonCollectibles { get; set; } = true;

            [JsonProperty("Show Button - Dead")]
            public bool ShowButtonDead { get; set; } = true;

            [JsonProperty("Show Button - Heli")]
            public bool ShowButtonHeli { get; set; } = false;

            [JsonProperty("Show Button - Loot")]
            public bool ShowButtonLoot { get; set; } = true;

            [JsonProperty("Show Button - MiniCopter")]
            public bool ShowButtonMiniCopter { get; set; } = false;

            [JsonProperty("Show Button - MLRS")]
            public bool ShowButtonMLRS { get; set; } = true;

            [JsonProperty("Show Button - NPC")]
            public bool ShowButtonNPC { get; set; } = true;

            [JsonProperty("Show Button - Ore")]
            public bool ShowButtonOre { get; set; } = true;

            [JsonProperty("Show Button - Ridable Horses")]
            public bool ShowButtonRidableHorses { get; set; } = false;

            [JsonProperty("Show Button - RigidHullInflatableBoats")]
            public bool ShowButtonRigidHullInflatableBoats { get; set; } = false;

            [JsonProperty("Show Button - Sleepers")]
            public bool ShowButtonSleepers { get; set; } = true;

            [JsonProperty("Show Button - Stash")]
            public bool ShowButtonStash { get; set; } = true;

            [JsonProperty("Show Button - TC")]
            public bool ShowButtonTC { get; set; } = true;

            [JsonProperty("Show Button - Prefab")]
            public bool ShowButtonPrefab { get; set; } = true;

            [JsonProperty("Show Button - TC Turrets")]
            public bool ShowButtonTCTurrets { get; set; } = true;

            [JsonProperty("Show Button - Traps")]
            public bool ShowButtonTraps { get; set; } = true;
        }

        [Serializable]
        public class TrackAdminStatusConfig
        {
            [JsonProperty("Radar")]
            public bool Radar { get; set; }

            [JsonProperty("Radar Text")]
            public string RadarText { get; set; } = "<color=#00FF00>R</color>";

            [JsonProperty("Console Godmode")]
            public bool God { get; set; }

            [JsonProperty("Console Godmode Text")]
            public string GodText { get; set; } = "<color=#89CFF0>G</color>";

            [JsonProperty("Plugin Godmode")]
            public bool GodPlugin { get; set; }

            [JsonProperty("Plugin Godmode Text")]
            public string GodPluginText { get; set; } = "<color=#0000CD>G</color>";

            [JsonProperty("Vanish")]
            public bool Vanish { get; set; } = true;

            [JsonProperty("Vanish Text")]
            public string VanishText { get; set; } = "<color=#FF00FF>V</color>";

            [JsonProperty("NOCLIP")]
            public bool NoClip { get; set; }

            [JsonProperty("NOCLIP Text")]
            public string NoClipText { get; set; } = "<color=#FFFF00>F</color>";

            [JsonProperty("Spectating")]
            public bool Spectating { get; set; }

            [JsonProperty("Spectating Text")]
            public string SpectatingText { get; set; } = "<color=#00FFFF>S</color>";

            [JsonIgnore]
            public bool Any =>
                Radar || God || GodPlugin || Vanish || NoClip || Spectating;
        }

        [Serializable]
        public class OptionsConfig
        {
            /// <summary>Item or entity shortnames. Empty list disables box tracking (AdminRadar 5.4.312).</summary>
            [JsonProperty("Boxes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Boxes { get; set; } = CreateDefaultBoxes();

            /// <summary>Legacy key from older Radar/AdminRadar configs.</summary>
            [JsonProperty("Additional Boxes")]
            private List<string> LegacyAdditionalBoxes
            {
                set
                {
                    if (value == null || value.Count == 0)
                        return;
                    if (Boxes == null || Boxes.Count == 0)
                        Boxes = value;
                }
            }

            public static List<string> CreateDefaultBoxes() => new List<string>
            {
                "abyss_barrel",
                "bamboo_barrel",
                "box.wooden.large",
                "coffinstorage",
                "dropbox.deployed",
                "heli_crate",
                "krieg_storage",
                "mailbox.deployed",
                "medieval.box.wooden.large",
                "missionstash",
                "small_stash_deployed",
                "storage_barrel",
                "vendingmachine.deployed",
                "wicker_barrel",
                "woodbox_deployed",
                "industrial_storage_horizontal",
                "industrial_storage_vertical"
            };

            [JsonProperty("Additional Traps")]
            public List<string> AdditionalTraps { get; set; } = new List<string>
            {
                "barricade.metal",
                "barricade.stone",
                "barricade.wood",
                "barricade.woodwire",
                "spikes.floor",
                "guntrap",
                "sam_site_turret_deployed",
                "flameturret"
            };

            [JsonProperty("Draw Distant Players With X")]
            public bool DrawDistantPlayersWithX { get; set; } = false;

            [JsonProperty("Draw Empty Containers")]
            public bool DrawEmptyContainers { get; set; } = true;

            [JsonProperty("Abbreviate Item Names")]
            public bool AbbreviateItemNames { get; set; } = true;

            [JsonProperty("Show Resource Amounts")]
            public bool ShowResourceAmounts { get; set; } = true;

            [JsonProperty("Show X Items From Barrel And Crate")]
            public int ShowXItemsFromBarrelAndCrate { get; set; } = 0;

            [JsonProperty("Show X Items From Airdrop")]
            public int ShowXItemsFromAirdrop { get; set; } = 0;

            [JsonProperty("Show X Items From Stash")]
            public int ShowXItemsFromStash { get; set; } = 0;

            [JsonProperty("Show X Items From Backpacks")]
            public int ShowXItemsFromBackpacks { get; set; } = 3;

            [JsonProperty("Show X Items From Corpses")]
            public int ShowXItemsFromCorpses { get; set; } = 3;

            [JsonProperty("Show NPC At World View")]
            public bool ShowNpcAtWorldView { get; set; } = true;

            [JsonProperty("Show NPC Name As Prefab Name")]
            public bool ShowNpcNameAsPrefabName { get; set; } = false;

            [JsonProperty("Show Authed Count On Cupboards")]
            public bool ShowAuthedCountOnCupboards { get; set; } = true;

            [JsonProperty("Show Bag Count On Cupboards")]
            public bool ShowBagCountOnCupboards { get; set; } = true;

            [JsonProperty("Show Npc Player Target")]
            public bool ShowNpcPlayerTarget { get; set; } = false;

            [JsonProperty("Radar Buildings Draw Time")]
            public float RadarBuildingsDrawTime { get; set; } = 60.0f;

            [JsonProperty("Radar Drops Draw Time")]
            public float RadarDropsDrawTime { get; set; } = 60.0f;

            [JsonProperty("Radar Find Draw Time")]
            public float RadarFindDrawTime { get; set; } = 60.0f;

            [JsonProperty("Radar FindByID Draw Time")]
            public float RadarFindByIdDrawTime { get; set; } = 60.0f;
        }

        [Serializable]
        public class SettingsConfig
        {
            [JsonProperty("Barebones Performance Mode")]
            public bool BarebonesPerformanceMode { get; set; } = false;

            [JsonProperty("Restrict Access To Steam64 IDs")]
            public List<string> RestrictAccessToSteam64Ids { get; set; } = new List<string>();

            [JsonProperty("Restrict Access To Auth Level")]
            public int RestrictAccessToAuthLevel { get; set; } = 1;

            [JsonProperty("Default Distance")]
            public float DefaultDistance { get; set; } = 500.0f;

            [JsonProperty("Default Refresh Time")]
            public float DefaultRefreshTime { get; set; } = 5.0f;

            [JsonProperty("Minimum Refresh Time")]
            public float MinimumRefreshTime { get; set; } = 0.1f;

            [JsonProperty("User Interface Enabled")]
            public bool UserInterfaceEnabled { get; set; } = true;

            [JsonProperty("Player Name Text Size")]
            public int PlayerNameTextSize { get; set; } = 24;

            [JsonProperty("Player Information Text Size")]
            public int PlayerInformationTextSize { get; set; } = 24;

            [JsonProperty("Entity Name Text Size")]
            public int EntityNameTextSize { get; set; } = 24;

            [JsonProperty("Entity Information Text Size")]
            public int EntityInformationTextSize { get; set; } = 24;
        }

        [Serializable]
        public class ConfigData
        {
            [JsonProperty("Color-Hex Codes")]
            public ColorHexCodes ColorHexCodes { get; set; } = new ColorHexCodes();

            [JsonProperty("Drawing Distances")]
            public DrawingDistances DrawingDistances { get; set; } = new DrawingDistances();

            [JsonProperty("GUI")]
            public GuiConfig GUI { get; set; } = new GuiConfig();

            [JsonProperty("Options")]
            public OptionsConfig Options { get; set; } = new OptionsConfig();

            [JsonProperty("Settings")]
            public SettingsConfig Settings { get; set; } = new SettingsConfig();

            [JsonProperty("Track Admin Status")]
            public TrackAdminStatusConfig TrackAdminStatus { get; set; } = new TrackAdminStatusConfig();
        }

        public static ConfigData Config { get; private set; }
        private static string _configPath;

        public static void LoadConfig()
        {
            try
            {
                var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var paths = new[]
                {
                    Path.Combine(serverRoot, "HarmonyConfig", "Radar.json"),
                    Path.Combine(serverRoot, "oxide", "config", "Radar.json"),
                    Path.Combine(serverRoot, "Config", "Radar.json"),
                    Path.Combine(serverRoot, "Radar.json")
                };

                foreach (var p in paths)
                {
                    if (!File.Exists(p))
                        continue;

                    var json = File.ReadAllText(p);
                    var cfg = JsonConvert.DeserializeObject<ConfigData>(json);
                    if (cfg != null)
                    {
                        Config = cfg;
                        _configPath = p;
                        UnityEngine.Debug.Log("[Radar] Config loaded from " + p);
                        return;
                    }
                }

                // Create default config in HarmonyConfig on first load.
                Config = new ConfigData();
                _configPath = Path.Combine(serverRoot, "HarmonyConfig", "Radar.json");
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                SaveConfig();
                UnityEngine.Debug.Log("[Radar] Config created at " + _configPath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Radar] Config load error: " + ex.Message);
                Config ??= new ConfigData();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                if (_configPath == null || Config == null) return;
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Radar] Config save error: " + ex.Message);
            }
        }

        public static Color GetColorFromHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex))
                return fallback;

            try
            {
                if (hex[0] == '#')
                    hex = hex.Substring(1);

                if (hex.Length == 6)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    return new Color32(r, g, b, 255);
                }

                if (hex.Length == 8)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    byte a = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                    return new Color32(r, g, b, a);
                }
            }
            catch
            {
                // ignored; fall through to fallback
            }

            return fallback;
        }

        public static float GetDistanceFor(RadarEntityType type, float viewDistance)
        {
            var dd = Config?.DrawingDistances;
            if (dd == null)
                return viewDistance;

            float configured;
            switch (type)
            {
                case RadarEntityType.Players:
                case RadarEntityType.Sleepers:
                    configured = dd.Players;
                    break;
                case RadarEntityType.Dead:
                    configured = dd.PlayerCorpses;
                    break;
                case RadarEntityType.Bags:
                    configured = dd.SleepingBags;
                    break;
                case RadarEntityType.TC:
                    configured = dd.ToolCupboards;
                    break;
                case RadarEntityType.Stash:
                    configured = dd.Stashes;
                    break;
                case RadarEntityType.Backpack:
                    configured = dd.Backpacks;
                    break;
                case RadarEntityType.Box:
                    configured = dd.Boxes;
                    break;
                case RadarEntityType.Loot:
                    configured = dd.LootContainers;
                    break;
                case RadarEntityType.Npc:
                    configured = dd.NpcPlayers;
                    break;
                case RadarEntityType.Ore:
                    configured = dd.ResourcesOre;
                    break;
                case RadarEntityType.Trap:
                    configured = dd.Traps;
                    break;
                case RadarEntityType.Turret:
                    configured = dd.Turrets;
                    break;
                case RadarEntityType.Col:
                    configured = dd.Collectibles;
                    break;
                case RadarEntityType.Airdrop:
                    configured = dd.AirdropCrates;
                    break;
                case RadarEntityType.CCTV:
                    configured = dd.CCTV;
                    break;
                case RadarEntityType.MLRS:
                    configured = dd.MLRS;
                    break;
                case RadarEntityType.Prefab:
                    configured = dd.Prefab;
                    break;
                default:
                    configured = viewDistance;
                    break;
            }

            if (configured <= 0f)
                configured = viewDistance;

            return Mathf.Min(viewDistance, configured);
        }

        public static float GetScanRadius(float viewDistance)
        {
            var dd = Config?.DrawingDistances;
            if (dd == null)
                return viewDistance + 50f;

            float maxConfigured = Mathf.Max(
                dd.Players,
                dd.PlayerCorpses,
                dd.SleepingBags,
                dd.ToolCupboards,
                dd.Stashes,
                dd.Backpacks,
                dd.Boxes,
                dd.LootContainers,
                dd.NpcPlayers,
                dd.ResourcesOre,
                dd.Traps,
                dd.Turrets,
                dd.Collectibles,
                dd.AirdropCrates,
                dd.CCTV,
                dd.MLRS,
                dd.Prefab
            );

            if (maxConfigured <= 0f)
                maxConfigured = viewDistance;

            return Mathf.Min(viewDistance, maxConfigured) + 50f;
        }

        public static bool ShouldShowButton(RadarEntityType type)
        {
            var g = Config?.GUI;
            if (g == null)
                return true;

            switch (type)
            {
                case RadarEntityType.Players:
                    // No explicit flag in AdminRadar.json; always show.
                    return true;
                case RadarEntityType.Sleepers:
                    return g.ShowButtonSleepers;
                case RadarEntityType.Dead:
                    return g.ShowButtonDead;
                case RadarEntityType.Bags:
                    return g.ShowButtonBags;
                case RadarEntityType.TC:
                    return g.ShowButtonTC;
                case RadarEntityType.Stash:
                    return g.ShowButtonStash;
                case RadarEntityType.Backpack:
                    // Backpack not in original GUI list; treat as Bags-style.
                    return g.ShowButtonBags;
                case RadarEntityType.Prefab:
                    return g.ShowButtonPrefab;
                case RadarEntityType.Npc:
                    return g.ShowButtonNPC;
                default:
                    return true;
            }
        }
    }
}

