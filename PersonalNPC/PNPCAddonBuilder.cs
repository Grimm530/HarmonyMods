// Requires: PersonalNPC

using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using System;
using System.Linq;
using System.Collections;
using Facepunch;

namespace PersonalNPCHarmony
{
    /// <summary>
    /// PNPC Builder AI Addon 1.0.0 ported for Harmony. Its config lives in the shared
    /// PersonalNPC.json under "Available buildings (by PNPC bot spawn name)".
    /// </summary>
    public class PNPCAddonBuilder : PersonalNPCPluginBase
    {
        public override string Name => "PNPCAddonBuilder";

        public PNPCAddonBuilder()
        {
            Version = new VersionNumber(1, 0, 0);
        }

        private Configuration _config;

        private Dictionary<ulong, PlayerBuilderController> _controllersByOwner = new Dictionary<ulong, PlayerBuilderController>();

        public PlayerBuilderController GetControllerByOwner(ulong id)
        {
            if(_controllersByOwner.ContainsKey(id))
            {
                var controller = _controllersByOwner[id];

                if(controller == null) _controllersByOwner.Remove(id);
                else return controller;
            }

            return null;
        }

        public readonly BuildCostInfo buildCostInfo = new BuildCostInfo();
        public const int WoodItemID = -151838493, StoneItemID = -2099697608, MetalItemID = 69511070, HQMItemID = 317398316, GearsItemID = 479143914;

        public class BuildCostInfo : Dictionary<string, ResourcesFromBuild>
        {
            public BuildCostInfo()
            {
                Add("assets/prefabs/building core/foundation.triangle/foundation.triangle.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/foundation/foundation.prefab", new ResourcesFromBuild(50, 0, 0, 0, 0, 200, 300, 200, 25));
                Add("assets/prefabs/building core/foundation.steps/foundation.steps.prefab", new ResourcesFromBuild(50, 0, 0, 0, 0, 200, 300, 200, 25));
                Add("assets/prefabs/building core/floor.triangle/floor.triangle.prefab", new ResourcesFromBuild(13, 0, 0, 0, 0, 50, 75, 50, 7));
                Add("assets/prefabs/building core/floor/floor.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/roof/roof.prefab", new ResourcesFromBuild(50, 0, 0, 0, 0, 200, 300, 200, 25));
                Add("assets/prefabs/building core/ramp/ramp.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/wall/wall.prefab", new ResourcesFromBuild(50, 0, 0, 0, 0, 200, 300, 200, 25));
                Add("assets/prefabs/building core/wall.half/wall.half.prefab", new ResourcesFromBuild(50, 0, 0, 0, 0, 200, 300, 200, 25));
                Add("assets/prefabs/building core/wall.low/wall.low.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/wall.frame/wall.frame.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/wall.window/wall.window.prefab", new ResourcesFromBuild(35, 0, 0, 0, 0, 140, 210, 140, 18));
                Add("assets/prefabs/building core/wall.doorway/wall.doorway.prefab", new ResourcesFromBuild(35, 0, 0, 0, 0, 140, 210, 140, 18));
                Add("assets/prefabs/building core/stairs.spiral.triangle/block.stair.spiral.triangle.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/stairs.spiral/block.stair.spiral.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/stairs.l/block.stair.lshape.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));
                Add("assets/prefabs/building core/stairs.u/block.stair.ushape.prefab", new ResourcesFromBuild(25, 0, 0, 0, 0, 100, 150, 100, 13));

                Add("assets/prefabs/building/door.hinged/door.hinged.wood.prefab", new ResourcesFromBuild(300, 0, 0, 0, 0));
                Add("assets/prefabs/building/door.double.hinged/door.double.hinged.wood.prefab", new ResourcesFromBuild(350, 0, 0, 0, 0));
                Add("assets/prefabs/building/door.hinged/door.hinged.metal.prefab", new ResourcesFromBuild(0, 0, 150, 0, 0));
                Add("assets/prefabs/building/door.double.hinged/door.double.hinged.metal.prefab", new ResourcesFromBuild(0, 0, 200, 0, 0));
                Add("assets/prefabs/building/door.hinged/door.hinged.toptier.prefab", new ResourcesFromBuild(0, 0, 0, 20, 5));
                Add("assets/prefabs/building/door.double.hinged/door.double.hinged.toptier.prefab", new ResourcesFromBuild(0, 0, 0, 25, 5));

                Add("assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab", new ResourcesFromBuild(1000, 0, 0, 0, 0));
                Add("assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab", new ResourcesFromBuild(1000, 0, 0, 0, 0));
            }

            public ResourcesFromBuild GetResources(string prefab)
            {
                TryGetValue(prefab, out var resources);
                return resources;
            }

        }
        
        public class ResourcesFromBuild
        {
            public int Wood, Stone, Metal, HQM, Gears;
            private Dictionary<BuildingGrade.Enum, int> gradeResources;

            private ResourcesFromBuild(int wood, int stone, int metal, int hQM, int gears)
            {
                Wood = wood;
                Stone = stone;
                Metal = metal;
                HQM = hQM;
                Gears = gears;
                gradeResources = new Dictionary<BuildingGrade.Enum, int>();
            }
            public ResourcesFromBuild(int wood, int stone, int metal, int hQM, int gears, int grade_wood = 0, int grade_stone = 0, int grade_metal= 0, int grade_top = 0) : this(wood, stone, metal, hQM, gears)
            {
                if(grade_wood == 0 && grade_stone == 0 && grade_metal == 0 && grade_top == 0) return;
                gradeResources.Add(BuildingGrade.Enum.Wood, grade_wood);
                gradeResources.Add(BuildingGrade.Enum.Stone, grade_stone);
                gradeResources.Add(BuildingGrade.Enum.Metal, grade_metal);
                gradeResources.Add(BuildingGrade.Enum.TopTier, grade_top);
            }

            public ResourcesFromBuild()
            {
                gradeResources = new Dictionary<BuildingGrade.Enum, int>();
            }

            public void Add(ResourcesFromBuild resources, BuildingGrade.Enum? grade)
            {
                if(resources == null) return;
                Wood += resources.Wood;
                Stone += resources.Stone;
                Metal += resources.Metal;
                HQM += resources.HQM;
                Gears += resources.Gears;
                int value = 0;
                if(grade != null && resources.gradeResources != null && resources.gradeResources.TryGetValue((BuildingGrade.Enum)grade, out value))
                {
                    switch((BuildingGrade.Enum)grade)
                    {
                        case BuildingGrade.Enum.Wood: Wood += value; break;
                        case BuildingGrade.Enum.Stone: Stone += value; break;
                        case BuildingGrade.Enum.Metal: Metal += value; break;
                        case BuildingGrade.Enum.TopTier: HQM += value; break;
                        default: break;
                    }
                }
            }
            public void AddNew(ResourcesFromBuild resources, BuildingGrade.Enum? grade)
            {
                Clear();
                Add(resources, grade);
            }
            public void Clear()
            {
                Wood = 0;
                Stone = 0;
                Metal = 0;
                HQM = 0;
                Gears = 0;
                gradeResources.Clear();
            }
            public void AddNew(int wood, int stone, int metal, int hQM, int gears)
            {
                Clear();
                Wood = wood;
                Stone = stone;
                Metal = metal;
                HQM = hQM;
                Gears = gears;
            }
            internal void SubtractionResourcesFromPlayer(PlayerInventory inventory, ResourcesFromBuild currentSubtractionResources)
            {
                currentSubtractionResources.Clear();
                if(inventory == null) return;
                int wood = 0, stone = 0, metal = 0, hQM = 0, gears = 0;
                if(Wood > 0)
                {
                    wood = inventory.Take(null, WoodItemID, Wood);
                    Wood -= wood;
                }
                if(Stone > 0)
                {
                    stone = inventory.Take(null, StoneItemID, Stone);
                    Stone -= stone;
                }
                if(Metal > 0)
                {
                    metal = inventory.Take(null, MetalItemID, Metal);
                    Metal -= metal;
                }
                if(HQM > 0)
                {
                    hQM = inventory.Take(null, HQMItemID, HQM);
                    HQM -= hQM;
                }
                if(Gears > 0)
                {
                    gears = inventory.Take(null, GearsItemID, Gears);
                    Gears -= gears;
                }
                currentSubtractionResources.AddNew(wood, stone, metal, hQM, gears);
            }
            public void GetResources(PlayerInventory inventoryOwner, ItemContainer containerBot)
            {
                int value;
                if(Wood > 0) value = inventoryOwner.Take(null, WoodItemID, Wood);
                else value = 0;
                if(value > 0) SetItem(WoodItemID, value, containerBot);
                if(Stone > 0) value = inventoryOwner.Take(null, StoneItemID, Stone);
                else value = 0;
                if(value > 0) SetItem(StoneItemID, value, containerBot);
                if(Metal > 0) value = inventoryOwner.Take(null, MetalItemID, Metal);
                else value = 0;
                if(value > 0) SetItem(MetalItemID, value, containerBot);
                if(HQM > 0) value = inventoryOwner.Take(null, HQMItemID, HQM);
                else value = 0;
                if(value > 0) SetItem(HQMItemID, value, containerBot);
                if(Gears > 0) value = inventoryOwner.Take(null, GearsItemID, Gears);
                else value = 0;
                if(value > 0) SetItem(GearsItemID, value, containerBot);
            }
            private void SetItem(int idItem, int amount, ItemContainer container)
            {
                ItemManager.CreateByItemID(idItem, amount)?.MoveToContainer(container);
            }

            internal void Subtraction(ResourcesFromBuild currentSubtractionResources)
            {
                Wood -= currentSubtractionResources.Wood;
                Stone -= currentSubtractionResources.Stone;
                Metal -= currentSubtractionResources.Metal;
                HQM -= currentSubtractionResources.HQM;
                Gears -= currentSubtractionResources.Gears;
            }
            public string ToString(PlayerInventory inventory, PNPCAddonBuilder plugin, string userID, bool requireResources = true)
            {
                if(inventory == null) return "";
                string text = "";
                string color = "";
                int num;
                if(Wood > 0)
                {
                    num = inventory.GetAmount(WoodItemID);
                    color = requireResources ? (num >= Wood ? "green" : "red") : "green";
                    int displayNum = requireResources ? num : Wood;
                    text += $"\n{plugin.GetMsg("Resource_Wood", userID)}: {Wood}/<color={color}>{displayNum}</color>";
                }
                if(Stone > 0)
                {
                    num = inventory.GetAmount(StoneItemID);
                    color = requireResources ? (num >= Stone ? "green" : "red") : "green";
                    int displayNum = requireResources ? num : Stone;
                    text += $"\n{plugin.GetMsg("Resource_Stone", userID)}: {Stone}/<color={color}>{displayNum}</color>";
                }
                if(Metal > 0)
                {
                    num = inventory.GetAmount(MetalItemID);
                    color = requireResources ? (num >= Metal ? "green" : "red") : "green";
                    int displayNum = requireResources ? num : Metal;
                    text += $"\n{plugin.GetMsg("Resource_MetalFragment", userID)}: {Metal}/<color={color}>{displayNum}</color>";
                }
                if(HQM > 0)
                {
                    num = inventory.GetAmount(HQMItemID);
                    color = requireResources ? (num >= HQM ? "green" : "red") : "green";
                    int displayNum = requireResources ? num : HQM;
                    text += $"\n{plugin.GetMsg("Resource_HQM", userID)}: {HQM}/<color={color}>{displayNum}</color>";
                }
                if(Gears > 0)
                {
                    num = inventory.GetAmount(GearsItemID);
                    color = requireResources ? (num >= Gears ? "green" : "red") : "green";
                    int displayNum = requireResources ? num : Gears;
                    text += $"\n{plugin.GetMsg("Resource_Gears", userID)}: {Gears}/<color={color}>{displayNum}</color>";
                }
                return text;
            }
            public bool CanBuild() => Wood == 0 && Stone == 0 && Metal == 0 && HQM == 0 && Gears == 0;
        }

        #region Config

        public class Configuration 
        {
            [JsonProperty("Available buildings (by PNPC bot spawn name)")]
            public Dictionary<string, List<BotSetup>> botsSetup = new Dictionary<string, List<BotSetup>>();

            public class BotSetup 
            {
                [JsonProperty("The name of the building to be selected via /pnpc build {building_name}")]
                public string spawnName = "builder_test";

                [JsonProperty("CopyPaste file name")]
                public string filename = "builder_test";

                [JsonProperty("Require resources?")]
                public bool requireResources = true;

                [JsonProperty("Enable building block upgrade effects?")]
                public bool enableUpgradeEffects = true;

                [JsonProperty("Speed")]
                public SpeedSetup speed = new SpeedSetup();

                [JsonProperty("Build")]
                public BuildSetup build = new BuildSetup();

                public class BuildSetup
                {
                    [JsonProperty("Max allowable height of foundation")]
                    public float maxHeightBlockFoundation = 3.1f;

                    [JsonProperty("Start height to ground")]
                    public float startHeightToGround = 0.5f;

                    [JsonProperty("Max depth when building in water")]
                    public float maxDepthInWater = 2.5f;

                    [JsonProperty("Disable stability?")]
                    public bool disableStability = false;

                    [JsonProperty("Checks")]
                    public ChecksSetup Checks = new ChecksSetup();

                    [JsonProperty("Doors")]
                    public DoorsSetup Doors = new DoorsSetup();

                    [JsonProperty("Cupboards (experimental)")]
                    public CupSetup Cup = new CupSetup();

                    public class DoorsSetup
                    {
                        [JsonProperty("Deploy doors?")]
                        public bool deployDoors = false;
                    }
                    
                    public class CupSetup 
                    {
                        [JsonProperty("Deploy cupboards?")]
                        public bool deployCup = false;

                        [JsonProperty("Authorize the owner?")]
                        public bool canAuth = false;
                    }

                    public class ChecksSetup
                    {
                        [JsonProperty("Check is close to road?")]
                        public bool checkIsCloseToRoad = true;

                        [JsonProperty("Check is building blocked?")]
                        public bool checkIsBuildingBlocked = true;

                        [JsonProperty("Check for Prevent Building triggers")]
                        public bool checkForPreventBuilding = true;

                        [JsonProperty("Prevent Building check radius")]
                        public float preventBuildingRadius = 10f;
                    }
                }

                public class SpeedSetup 
                {
                    [JsonProperty("Bot speed multiplier")]
                    public float lerpSpeedMultiplier = 1f;

                    [JsonProperty("Build speed multiplier")]
                    public float buildSpeedMultiplier = 1f;

                    [JsonProperty("Time to check 1 foundation")]
                    public float timeCheckFoundation = 0.15f;

                    [JsonProperty("Time to check 1 building block")]
                    public float timeCheckBuildingBlock = 0.15f;
                }
            }
        }

        protected override void LoadDefaultConfig() 
        {
            _config = new Configuration
            {
                botsSetup = new Dictionary<string, List<Configuration.BotSetup>>
                {
                    ["bot1"] = new List<Configuration.BotSetup>
                    {
                        new Configuration.BotSetup
                        {
                            filename = "builder_test",
                        }
                    }
                },
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();

                SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError("{0}", ex);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Loc

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ChatCommand_Error_NoPermission"] = "You don't have any available bases to build!",
                ["ChatCommand_Error_NotFound"] = "Base not found",
                ["ChatCommand_Error_NoResources"] = "The bot doesn't have enough resources!\nFor construction, it is necessary:\n",
                ["ChatCommand_Error_Abort"] = "Construction stopped!",
                ["ChatCommand_Error_NotEnoughSpaceBelt"] = "Not enough slots free in bot's belt container, at least 2 slots should be free!",

                ["ChatCommand_Success_Despawn"] = "Building mode disabled!",
                ["ChatCommand_Success_Built"] = "Your building is ready!",
                ["ChatCommand_Success_Start"] = "Construction has started!",

                ["ChatCommand_Notice_AvailableBases"] = "<size=16>Available bases:</size>\n{BASES}\n\nEnter /pnpc build [base name] to start!",
                ["ChatCommand_Notice_Check"] = "Checking the place to build...",
                ["Calculation_of_the_drawing"] = "Calculation of the building drawing...",
                ["Calculation_resources"] = "List of resources for construction:\n",

                ["CopyPaste_Error_BuildBlocked"] = "Building is blocked here!",
                ["CopyPaste_Error_CloseToRoad"] = "Close to road, can't build here!",
                ["CopyPaste_Error_NotSuitablePlace"] = "Not suitable landscape for construction!",
                ["CopyPaste_Error_WallBlocked"] = "Obstacle to wall construction detected!",
                ["CopyPaste_Error_RoofBlocked"] = "Obstacle to roof construction detected!",

                ["Resource_Wood"] = "Wood",
                ["Resource_Stone"] = "Stones",
                ["Resource_MetalFragment"] = "Metal Fragments",
                ["Resource_HQM"] = "High Quality Metal",
                ["Resource_Gears"] = "Gears",

                ["CommandPaused"] = "Construction is paused!",
                ["BotCalculated"] = "Checking the place to build...",
                ["BotCheckedTerritory"] = "Checking the place to build...",
                ["BotBuilding"] = "Construction resumed!",
                ["Bot_WaitForBuildPoint"] = "Aim at the build spot and press your task button (middle mouse) to place a marker arrow.",
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ChatCommand_Error_NoPermission"] = "У вас нет никаких доступных баз для постройки!",
                ["ChatCommand_Error_NotFound"] = "База не найдена",
                ["ChatCommand_Error_NoResources"] = "У Бота недостаточно ресурсов!\nДля строительства необходимо:\n",
                ["ChatCommand_Error_Abort"] = "Строительство прекращено!",
                ["ChatCommand_Error_NotEnoughSpaceBelt"] = "Недостаточно слотов в контейнере быстрого доступа у бота, необходимо хотя бы 2 свободных слота!",

                ["ChatCommand_Success_Despawn"] = "Режим строительства отключен!",
                ["ChatCommand_Success_Built"] = "Ваше здание готово!",
                ["ChatCommand_Success_Start"] = "Строительство начато!",

                ["ChatCommand_Notice_AvailableBases"] = "<size=16>Доступные базы:</size>\n{BASES}\n\nВведите /pnpc build [название базы], чтобы начать!",
                ["ChatCommand_Notice_Check"] = "Проверка территории под строительство...",
                ["Calculation_of_the_drawing"] = "Расчет чертежа здания...",
                ["Calculation_resources"] = "Список ресурсов для строительства:\n",

                ["CopyPaste_Error_BuildBlocked"] = "Строительство недоступно здесь!",
                ["CopyPaste_Error_CloseToRoad"] = "Близко к дороге, строительство недоступно!",
                ["CopyPaste_Error_NotSuitablePlace"] = "Не подходящий ландшафт для строительства!",
                ["CopyPaste_Error_WallBlocked"] = "Обнаружено препятствие для строительства стены!",
                ["CopyPaste_Error_RoofBlocked"] = "Обнаружено препятствие для строительства крыши!",

                ["Resource_Wood"] = "Дерево",
                ["Resource_Stone"] = "Камни",
                ["Resource_MetalFragment"] = "Метал. фрагменты",
                ["Resource_HQM"] = "МВК",
                ["Resource_Gears"] = "Шестерни",

                ["CommandPaused"] = "Строительство приостановлено!",
                ["BotCalculated"] = "Бот рассчитывает чертеж здания...",
                ["BotCheckedTerritory"] = "Бот проверяет территорию под строительство...",
                ["BotBuilding"] = "Строительство возобновлено!",
                ["Bot_WaitForBuildPoint"] = "Наведитесь на место строительства и нажмите кнопку задачи (средняя кнопка мыши), чтобы поставить маркер.",
            }, this, "ru");
        }

        #endregion

        #region Methods

        public List<Configuration.BotSetup> GetBotSetup(PersonalNPC.PlayerBotController pnpcController) 
        {
            List<Configuration.BotSetup> setups = new List<Configuration.BotSetup>();

            foreach(var key in _config.botsSetup.Keys)
            {
                if(pnpcController.botSetup.spawnName == key)
                {
                    setups.AddRange(_config.botsSetup[key]);
                }
            }

            return setups;
        }

        private ResourcesFromBuild CalculateResourcesForSetup(Configuration.BotSetup setup)
        {
            var resources = new ResourcesFromBuild();
            var path = "copypaste/" + setup.filename;

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(path))
                return resources;

            var datafile = Interface.Oxide.DataFileSystem.GetDatafile(path);
            var ents = datafile["entities"] as List<object>;
            if (ents == null)
                return resources;

            foreach (Dictionary<string, object> data in ents)
            {
                var prefabname = (string)data["prefabname"];
                if (!data.ContainsKey("grade") && !prefabname.Contains("assets/prefabs/building/door.hinged") &&
                    !prefabname.Contains("assets/prefabs/building/door.double.hinged") &&
                    !prefabname.Contains("assets/prefabs/deployable/tool cupboard/cupboard.tool") &&
                    !prefabname.Contains("assets/prefabs/deployable/tool cupboard/retro/cupboard.tool"))
                    continue;

                BuildingGrade.Enum? grade = !data.ContainsKey("grade") ? null : (BuildingGrade.Enum)Convert.ToInt32(data["grade"]);
                bool isDoor = setup.build.Doors.deployDoors && (prefabname.Contains("assets/prefabs/building/door.hinged") || prefabname.Contains("assets/prefabs/building/door.double.hinged"));
                bool isCup = setup.build.Cup.deployCup && (prefabname.Contains("assets/prefabs/deployable/tool cupboard/cupboard.tool") || prefabname.Contains("assets/prefabs/deployable/tool cupboard/retro/cupboard.tool"));

                if (prefabname.Contains("assets/prefabs/building core/foundation") ||
                    prefabname.Contains("assets/prefabs/building core/floor") ||
                    prefabname.Contains("assets/prefabs/building core/ramp") ||
                    prefabname.Contains("assets/prefabs/building core/roof") ||
                    prefabname.Contains("assets/prefabs/building core/wall") ||
                    prefabname.Contains("assets/prefabs/building core/stairs") ||
                    isDoor || isCup)
                {
                    resources.Add(buildCostInfo.GetResources(prefabname), grade);
                }
            }

            return resources;
        }

        private bool PlayerHasResources(BasePlayer player, ResourcesFromBuild needed, bool requireResources)
        {
            if (player == null || !requireResources)
                return true;

            if (needed.Wood > 0 && player.inventory.GetAmount(WoodItemID) < needed.Wood) return false;
            if (needed.Stone > 0 && player.inventory.GetAmount(StoneItemID) < needed.Stone) return false;
            if (needed.Metal > 0 && player.inventory.GetAmount(MetalItemID) < needed.Metal) return false;
            if (needed.HQM > 0 && player.inventory.GetAmount(HQMItemID) < needed.HQM) return false;
            if (needed.Gears > 0 && player.inventory.GetAmount(GearsItemID) < needed.Gears) return false;
            return true;
        }

        private string FormatResourceSummary(ResourcesFromBuild needed)
        {
            var parts = new List<string>();
            if (needed.Wood > 0) parts.Add($"Wood {needed.Wood}");
            if (needed.Stone > 0) parts.Add($"Stone {needed.Stone}");
            if (needed.Metal > 0) parts.Add($"Metal {needed.Metal}");
            if (needed.HQM > 0) parts.Add($"HQM {needed.HQM}");
            if (needed.Gears > 0) parts.Add($"Gears {needed.Gears}");
            return parts.Count == 0 ? "No materials required" : string.Join(" · ", parts);
        }

        private string GetCostDisplay(BasePlayer player, ResourcesFromBuild needed, bool requireResources)
        {
            if (player == null)
                return string.Empty;

            string text = needed.ToString(player.inventory, this, player.UserIDString, requireResources);
            if (string.IsNullOrEmpty(text))
                return "No materials required";

            return text.Trim();
        }

        // API for PersonalNPCHelper build picker UI
        internal object GetBuildList(BasePlayer player, PersonalNPC.PlayerBotController pnpcController)
        {
            var list = new List<Dictionary<string, object>>();
            if (player == null || pnpcController == null)
                return list;

            var setups = GetBotSetup(pnpcController);
            for (int i = 0; i < setups.Count; i++)
            {
                var setup = setups[i];
                if (setup == null || string.IsNullOrEmpty(setup.spawnName))
                    continue;

                var needed = CalculateResourcesForSetup(setup);
                bool canAfford = PlayerHasResources(player, needed, setup.requireResources);
                bool fileExists = Interface.Oxide.DataFileSystem.ExistsDatafile("copypaste/" + setup.filename);
                string costDisplay = GetCostDisplay(player, needed, setup.requireResources);

                list.Add(new Dictionary<string, object>
                {
                    ["name"] = setup.spawnName,
                    ["file"] = setup.filename,
                    ["fileExists"] = fileExists,
                    ["requireResources"] = setup.requireResources,
                    ["canAfford"] = canAfford && fileExists,
                    ["summary"] = fileExists ? FormatResourceSummary(needed) : "CopyPaste file missing",
                    ["costDisplay"] = costDisplay,
                    ["wood"] = needed.Wood,
                    ["stone"] = needed.Stone,
                    ["metal"] = needed.Metal,
                    ["hqm"] = needed.HQM,
                    ["gears"] = needed.Gears
                });
            }

            return list;
        }

        private bool IsAwaitingBuildPoint(BasePlayer player)
        {
            var controller = GetControllerByOwner(player.userID);
            return controller != null && controller.Status == PlayerBuilderController.StatusBot.WaitPoint;
        }

        internal void SelectBuildFromUi(BasePlayer player, PersonalNPC.PlayerBotController pnpcController, string buildName)
        {
            TryStartBuildFromFile(player, pnpcController, buildName);
        }

        private Configuration.BotSetup ResolveBuildSetup(List<Configuration.BotSetup> bots, string buildName)
        {
            if (string.IsNullOrEmpty(buildName))
                return null;

            for (int i = 0; i < bots.Count; i++)
            {
                var setup = bots[i];
                if (setup == null) continue;
                if (string.Equals(setup.spawnName, buildName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(setup.filename, buildName, StringComparison.OrdinalIgnoreCase))
                    return setup;
            }

            if (Interface.Oxide.DataFileSystem.ExistsDatafile("copypaste/" + buildName))
            {
                return new Configuration.BotSetup
                {
                    spawnName = buildName,
                    filename = buildName,
                    requireResources = true
                };
            }

            return null;
        }

        internal bool TryStartBuildFromFile(BasePlayer player, PersonalNPC.PlayerBotController pnpcController, string buildName)
        {
            var setups = GetBotSetup(pnpcController);
            var bot = ResolveBuildSetup(setups, buildName);
            if (bot == null)
            {
                SendMsg(player, "ChatCommand_Error_NotFound");
                return false;
            }

            if (bot.requireResources)
            {
                var needed = CalculateResourcesForSetup(bot);
                if (!PlayerHasResources(player, needed, true))
                {
                    player.ChatMessage(GetMsg("ChatCommand_Error_NoResources", player.UserIDString) +
                        needed.ToString(player.inventory, this, player.UserIDString, true));
                    return false;
                }
            }

            Build(player, pnpcController, bot.spawnName);
            return true;
        }

        public string GetMsg(string key, string id) => lang.GetMessage(key, this, id);
        
        public void SendMsg(BasePlayer player, string key, params object[] args) => player.ChatMessage(string.Format(lang.GetMessage(key, this, player.UserIDString), args));

        internal bool InputPNPC(BasePlayer player, float rayLength)
        {
            var controller = GetControllerByOwner(player.userID);
            if (controller == null) return false;

            if (controller.Status == PlayerBuilderController.StatusBot.WaitPoint)
            {
                controller.OnInput(rayLength);
                return true;
            }

            // Hold the task button during calculation / validation / active building
            return true;
        }
        
        internal bool IsBuilderActive(BasePlayer player)
        {
            var controller = GetControllerByOwner(player.userID);
            return controller != null;
        }

        internal void ResetBuilder(BasePlayer player)
        {
            var controller = GetControllerByOwner(player.userID);

            if (controller != null)
            {
                UnityEngine.Object.Destroy(controller);
                _controllersByOwner.Remove(player.userID);
            }
        }

        internal void Build(BasePlayer player, PersonalNPC.PlayerBotController pnpcController, string buildName)
        {
            var controller = GetControllerByOwner(player.userID);

            if (buildName == string.Empty)
            {
                if (controller != null)
                {
                    UnityEngine.Object.Destroy(controller);
                    SendMsg(player, "ChatCommand_Success_Despawn");
                }
                else
                {
                    var botSetup = GetBotSetup(pnpcController);

                    if (botSetup != null)
                    {
                        if (botSetup.Count == 0) SendMsg(player, "ChatCommand_Error_NoPermission");
                        else
                        {
                            if (botSetup.Count == 1)
                            {
                                Build(player, pnpcController, botSetup[0].spawnName);
                                return;
                            }

                            string msg = lang.GetMessage("ChatCommand_Notice_AvailableBases", this, player.UserIDString);
                            string availableBots = "";

                            foreach (var bot in botSetup) availableBots = availableBots + $"\n{botSetup.IndexOf(bot) + 1}. {bot.spawnName}";

                            msg = msg.Replace("{BASES}", availableBots);
                            player.ChatMessage(msg);
                        }
                    }
                }

                return;
            }

            if (controller != null && buildName == "pause")
            {
                if (controller.Status == PlayerBuilderController.StatusBot.Building && !controller.Command_paused) controller.Command_paused = true;
                controller.PrintStatus();

                pnpcController.Navigator.SetNavMeshEnabled(true);
                pnpcController.Navigator.Resume();

                pnpcController.Navigator.CanUseNavMesh = true;
                pnpcController.Navigator.CanUseAStar = true;

                pnpcController.Navigator.DefaultArea = controller.DefaultArea;

                pnpcController.Navigator.Speed = controller.DefaultSpeed;
                pnpcController.Navigator.Agent.enabled = true;
                pnpcController.Navigator.enabled = true;

                return;
            }

            if (controller != null && buildName == "resume")
            {
                if (controller.Status == PlayerBuilderController.StatusBot.Building && controller.Command_paused) controller.Command_paused = false;
                controller.PrintStatus();

                pnpcController.Navigator.SetNavMeshEnabled(false);
                pnpcController.Navigator.Pause();

                pnpcController.Navigator.CanUseNavMesh = false;
                pnpcController.Navigator.CanUseAStar = false;

                pnpcController.Navigator.DefaultArea = "Not Walkable";

                pnpcController.Navigator.Speed = 0f;
                pnpcController.Navigator.Agent.enabled = false;
                pnpcController.Navigator.enabled = false;

                return;
            }

            var bots = GetBotSetup(pnpcController);

            if (bots.Count > 0)
            {
                var bot = ResolveBuildSetup(bots, buildName);

                if (bot == null)
                {
                    SendMsg(player, "ChatCommand_Error_NotFound");
                    return;
                }

                if (controller != null)
                {
                    UnityEngine.Object.Destroy(controller);
                    SendMsg(player, "ChatCommand_Success_Despawn");
                    return;
                }

                if (pnpcController.bot.inventory.containerBelt.itemList.Count > 4)
                {
                    SendMsg(player, "ChatCommand_Error_NotEnoughSpaceBelt");
                    return;
                }

                controller = PlayerBuilderController.CreateBot<PlayerBuilderController>(player, pnpcController, bot, buildCostInfo, this);

                if (_controllersByOwner.ContainsKey(player.userID))
                {
                    var existingController = _controllersByOwner[player.userID];
                    if (existingController != null) UnityEngine.Object.Destroy(existingController);
                    _controllersByOwner.Remove(player.userID);
                }

                _controllersByOwner.Add(player.userID, controller);
            }
            else SendMsg(player, "ChatCommand_Error_NoPermission");
        }
        
        #endregion

        #region Hooks

        internal void Unload()
        {
            foreach(var controller in _controllersByOwner.Values)
            {
                if(controller == null) continue;
                UnityEngine.Object.Destroy(controller);
            }
        }
        // ---- Harmony lifecycle ----
        public override void HarmonyInit()
        {
            LoadConfig();
            LoadDefaultMessages();
        }

        public override void HarmonyServerInitialized()
        {
        }

        public override void HarmonyUnload()
        {
            Unload();
        }

        internal object OnEntityTakeDamage(BaseCombatEntity ent, HitInfo info)
        {
            if(ent == null || info == null) return null;

            if(!info.damageTypes.Has(Rust.DamageType.Decay) || ent.OwnerID == 0) return null;
            if(!ent.OwnerID.IsSteamId()) return null;

            var controller = GetControllerByOwner(ent.OwnerID);
            if(controller != null) return false;

            return null;
        }

        internal object OnStructureUpgrade(BaseCombatEntity entity, BasePlayer player, BuildingGrade.Enum grade)
        {
            if (entity == null || player == null) return null;
            if (player.IsNpc || !player.userID.IsSteamId() || entity.OwnerID == 0 || !entity.OwnerID.IsSteamId()) return null;

            var controller = GetControllerByOwner(entity.OwnerID);
            if (controller != null) return false;

            return null;
        }

        #endregion

        #region Behaviour
        
        public class PlayerBuilderController : FacepunchBehaviour
        {
            public float maxHeightBlockFoundation = 3.1f, startHeightToGround = 0.5f;

            private Configuration.BotSetup _botSetup;
            private BasePlayer _owner, _botPlayer;
            private BaseNavigator _bot;
            private NPCPlayer _npcPlayer;
            private PersonalNPC.PlayerBotController _pnpcController;

            private List<StabilityEntity> _stabilityEntities = new List<StabilityEntity>();
            private Vector3 _buildPoint = Vector3.zero;

            private Coroutine _buildCoroutine;
            private ItemId _planID, _hammerID;

            private float _myHeight, _defaultSpeed, _rayLength = 10f;
            private string _defaultArea;

            public string DefaultArea => _defaultArea;
            public float DefaultSpeed => _defaultSpeed;

            private bool _isNullGround, _canBuildBlocks;

            public PNPCAddonBuilder BuilderInstance;
            public BuildCostInfo BuildCost;

            public Configuration.BotSetup BotSetup => _botSetup;

            #region Unity Callbacks

            private void Start()
            {
                maxHeightBlockFoundation = BotSetup.build.maxHeightBlockFoundation;
                startHeightToGround = BotSetup.build.startHeightToGround;

                StartCoroutine(InitBuilders());
            }

            private void Update()
            {
                if(!isInitBuilders || _buildCoroutine != null) return;

                if(_buildPoint != Vector3.zero)
                {
                    _bot.SetDestination(_buildPoint);

                    if(Vector3.Distance(_buildPoint, _bot.transform.position) < _rayLength)
                    {
                        if(_buildCoroutine == null)
                        {
                            _buildCoroutine = StartCoroutine(BuildCoroutine());
                        }
                    }
                }
            }

            private void OnDestroy()
            {
                if(_bot != null)
                {                 
                    _bot.SetNavMeshEnabled(true);
                    _bot.Resume();

                    _bot.CanUseNavMesh = true;
                    _bot.CanUseAStar = true;

                    if(!string.IsNullOrEmpty(DefaultArea)) _bot.DefaultArea = DefaultArea;
                    if(DefaultSpeed != 0f) _bot.Speed = DefaultSpeed;
                    
                    _bot.Agent.enabled = true;
                    _bot.enabled = true;
                }

                if(_stabilityEntities != null) 
                {
                    foreach (var entity in _stabilityEntities)
                    {
                        entity.grounded = _botSetup.build.disableStability;
                        entity.InitializeSupports();
                        entity.UpdateStability();
                    }
                }

                if(_owner != null && BuilderInstance != null && _buildCoroutine != null) BuilderInstance.SendMsg(_owner, "ChatCommand_Error_Abort");
            }

            #endregion

            #region Max39ru

            public StatusBot Status;
            private ResourcesFromBuild resources = new ResourcesFromBuild();
            private ResourcesFromBuild currentResources = new ResourcesFromBuild();
            private ResourcesFromBuild currentSubtractionResources = new ResourcesFromBuild();
            private string path;
            private DynamicConfigFile datafile;
            private bool isInitBuilders;
            private List<object> ents;
            private float rotationCorrection = 0f;
            Quaternion quaternionRotation = new Quaternion(0, 0, 0, 1);
            Vector3 eulerRotation;
            private List<Dictionary<string, object>> foundationsObjects = new(), floorObjects = new(), rampObjects = new(),
                roofObjects = new(), stairsObjects = new(), wallObjects = new(), doorObjects = new(), cupObjects = new();
            List<Dictionary<string, object>> todoSorted;
            bool bot_paused = false;
            public bool Command_paused = false;
            Dictionary<string, object> current;
            private IEnumerator InitBuilders()
            {
                path = "copypaste/" + _botSetup.filename;
                datafile = Interface.Oxide.DataFileSystem.GetDatafile(path);

                todoSorted = new List<Dictionary<string, object>>();
                BuilderInstance.SendMsg(_owner, "Calculation_of_the_drawing");

                Status = StatusBot.CalculationConstruction;
                yield return InitializationFile();

                isInitBuilders = true;
                _owner.ChatMessage(BuilderInstance.GetMsg("Calculation_resources", _owner.UserIDString) + resources.ToString(_npcPlayer.inventory, BuilderInstance, _owner.UserIDString, _botSetup.requireResources));
                Status = StatusBot.WaitPoint;
                BuilderInstance.SendMsg(_owner, "Bot_WaitForBuildPoint");
            }

            private IEnumerator InitializationFile()
            {
                ents = datafile["entities"] as List<object>;
                eulerRotation = new Vector3(0f, rotationCorrection, 0f);
                string prefabname;
                int initYieldCounter = 0;
                foreach (Dictionary<string, object> data in ents)
                {
                    prefabname = (string)data["prefabname"];
                    if (!data.ContainsKey("grade") && !prefabname.Contains("assets/prefabs/building/door.hinged") &&
                        !prefabname.Contains("assets/prefabs/building/door.double.hinged") &&
                        !prefabname.Contains("assets/prefabs/deployable/tool cupboard/cupboard.tool") &&
                        !prefabname.Contains("assets/prefabs/deployable/tool cupboard/retro/cupboard.tool"))
                        continue;

                    var pos = (Dictionary<string, object>)data["pos"];
                    var rot = (Dictionary<string, object>)data["rot"];

                    data.Add("position", pos);

                    data.Add("rotation", rot);
                    
                    if (data.ContainsKey("items"))
                        data["items"] = new List<object>();
                        
                    bool isDoor = _botSetup.build.Doors.deployDoors && (prefabname.Contains("assets/prefabs/building/door.hinged") || prefabname.Contains("assets/prefabs/building/door.double.hinged"));
                    bool isCup = _botSetup.build.Cup.deployCup && (prefabname.Contains("assets/prefabs/deployable/tool cupboard/cupboard.tool") || prefabname.Contains("assets/prefabs/deployable/tool cupboard/retro/cupboard.tool"));
                    BuildingGrade.Enum? grade = !data.ContainsKey("grade") ? null : (BuildingGrade.Enum)Convert.ToInt32(data["grade"]);

                    if(prefabname.Contains("assets/prefabs/building core/foundation")) 
                    {
                        foundationsObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }

                    if(prefabname.Contains("assets/prefabs/building core/floor")) 
                    {
                        floorObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }

                    if(prefabname.Contains("assets/prefabs/building core/ramp")) 
                    {
                        rampObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }

                    if(prefabname.Contains("assets/prefabs/building core/roof")) 
                    {
                        roofObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }

                    if(prefabname.Contains("assets/prefabs/building core/wall")) 
                    {
                        wallObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }

                    if(prefabname.Contains("assets/prefabs/building core/stairs")) 
                    {
                        stairsObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }

                    if(isDoor)
                    {
                        doorObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }
                    if(isCup)
                    {
                        cupObjects.Add(data);
                        resources.Add(BuildCost.GetResources(prefabname), grade);
                        goto Continue;
                    }
                    Continue:
                    if (++initYieldCounter >= 32)
                    {
                        initYieldCounter = 0;
                        yield return CoroutineEx.waitForEndOfFrame;
                    }
                }
                
                yield return CoroutineEx.waitForEndOfFrame;
                
                resources.GetResources(_owner.inventory, _botPlayer.inventory.containerMain);
            }
            private IEnumerator InitPosition()
            {
                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(foundationsObjects);

                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(floorObjects);

                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(rampObjects);

                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(roofObjects);

                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(wallObjects);

                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(stairsObjects);

                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(doorObjects);

                yield return CoroutineEx.waitForEndOfFrame;

                SetPosition(cupObjects);
            }
            private IEnumerator Sorted()
            {
                yield return CoroutineEx.waitForEndOfFrame;

                todoSorted.Clear();

                // Chain nearest-neighbor from marker so the bot walks locally instead of ping-ponging by distance rings.
                Vector3 pathOrigin = _buildPoint;

                foundationsObjects = OrderByNearestNeighbor(foundationsObjects, pathOrigin);
                todoSorted.AddRange(foundationsObjects);
                pathOrigin = LastPosition(foundationsObjects, pathOrigin);

                rampObjects = OrderByNearestNeighbor(rampObjects, pathOrigin);
                todoSorted.AddRange(rampObjects);
                pathOrigin = LastPosition(rampObjects, pathOrigin);

                stairsObjects = OrderByNearestNeighbor(stairsObjects, pathOrigin);
                todoSorted.AddRange(stairsObjects);
                pathOrigin = LastPosition(stairsObjects, pathOrigin);

                wallObjects = OrderByNearestNeighbor(wallObjects, pathOrigin);
                todoSorted.AddRange(wallObjects);
                pathOrigin = LastPosition(wallObjects, pathOrigin);

                floorObjects = OrderByNearestNeighbor(floorObjects, pathOrigin);
                todoSorted.AddRange(floorObjects);
                pathOrigin = LastPosition(floorObjects, pathOrigin);

                roofObjects = OrderByNearestNeighbor(roofObjects, pathOrigin);
                todoSorted.AddRange(roofObjects);
                pathOrigin = LastPosition(roofObjects, pathOrigin);

                doorObjects = OrderByNearestNeighbor(doorObjects, pathOrigin);
                cupObjects = OrderByNearestNeighbor(cupObjects, pathOrigin);
            }
            
            private void SetPosition(List<Dictionary<string, object>> entities)
            {
                foreach (Dictionary<string, object> entity in entities)
                {
                    var pos = (Dictionary<string, object>)entity["pos"];
                    var rot = (Dictionary<string, object>)entity["rot"];
                    
                    entity["position"] = 
                        quaternionRotation * new Vector3(Convert.ToSingle(pos["x"]), Convert.ToSingle(pos["y"]),
                        Convert.ToSingle(pos["z"])) + _buildPoint;
                        

                    entity["rotation"] = 
                        Quaternion.Euler(eulerRotation + new Vector3(Convert.ToSingle(rot["x"]),
                        Convert.ToSingle(rot["y"]), Convert.ToSingle(rot["z"])) * 57.2958f);
                }
            }
            private void RemoveBuild(Dictionary<string, object> remove)
            {
                doorObjects.Remove(remove);
                cupObjects.Remove(remove);
                todoSorted.Remove(remove);
            }
            private bool CheckCost()
            {
                if (!_botSetup.requireResources) return true;

                currentResources.SubtractionResourcesFromPlayer(_npcPlayer.inventory, currentSubtractionResources);
                resources.Subtraction(currentSubtractionResources);

                return currentResources.CanBuild();
            }
            public void EndLoot()
            {
                bot_paused = false;
                Command_paused = false;
            }

            #endregion Max39ru

            #region CopyPaste Methods

            private static bool HasGrade(BuildingBlock block, BuildingGrade.Enum grade, ulong skin) // CopyPaste
            {
                foreach (var constructionGrade in block.blockDefinition.grades)
                {
                    var baseGrade = constructionGrade.gradeBase;
                    if (baseGrade.type == grade && baseGrade.skin == skin)
                        return true;
                }

                return false;
            }

            private bool CheckPlaced(string prefabname, Vector3 pos, Quaternion rot) // CopyPaste
            {
                var ents = Pool.Get<List<BaseEntity>>();

                try
                {
                    Vis.Entities(pos, 0.01f, ents);

                    foreach (var ent in ents)
                    {
                        if (ent.PrefabName != prefabname 
                            || Vector3.Distance(ent.transform.position, pos) > 0.01f 
                                || Vector3.Distance(ent.transform.rotation.eulerAngles, rot.eulerAngles) > 0.01f) continue;

                        return true;
                    }

                    return false;
                }
                finally
                {
                    Pool.FreeUnmanaged(ref ents);
                }
            }

            #endregion

            #region Rust Check Methods

            private bool TestPlacingCloseToRoad(Vector3 pos, Quaternion rot)
            {
                if (TerrainMeta.HeightMap == null || TerrainMeta.TopologyMap == null) return true;

                OBB obb = new OBB(pos, Vector3.one, rot);
                float num = Mathf.Abs(TerrainMeta.HeightMap.GetHeight(obb.position) - obb.position.y);

                if (num > 9.0) return true;

                float radius = Mathf.Lerp(3f, 0.0f, num / 9f);
                Vector3 position = obb.position, point1 = obb.GetPoint(-1f, 0.0f, -1f), point2 = obb.GetPoint(-1f, 0.0f, 1f), point3 = obb.GetPoint(1f, 0.0f, -1f), point4 = obb.GetPoint(1f, 0.0f, 1f);

                return ((TerrainMeta.TopologyMap.GetTopology(position, radius) | TerrainMeta.TopologyMap.GetTopology(point1, radius) | TerrainMeta.TopologyMap.GetTopology(point2, radius) | TerrainMeta.TopologyMap.GetTopology(point3, radius) | TerrainMeta.TopologyMap.GetTopology(point4, radius)) & 526336) == 0;
            }
            
            #endregion

            #region List Methods

            private static Vector3 LastPosition(List<Dictionary<string, object>> entities, Vector3 fallback)
            {
                if (entities == null || entities.Count == 0) return fallback;
                return (Vector3)entities[entities.Count - 1]["position"];
            }

            private static List<Dictionary<string, object>> OrderByNearestNeighbor(List<Dictionary<string, object>> collection, Vector3 start)
            {
                if (collection == null || collection.Count <= 1) return collection;

                var result = new List<Dictionary<string, object>>(collection.Count);
                var remaining = new List<Dictionary<string, object>>(collection);
                Vector3 current = start;

                while (remaining.Count > 0)
                {
                    int bestIdx = 0;
                    float bestDist = float.MaxValue;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        float dist = ((Vector3)remaining[i]["position"] - current).sqrMagnitude;
                        if (dist >= bestDist) continue;

                        bestDist = dist;
                        bestIdx = i;
                    }

                    var next = remaining[bestIdx];
                    int lastIdx = remaining.Count - 1;
                    if (bestIdx != lastIdx) remaining[bestIdx] = remaining[lastIdx];
                    remaining.RemoveAt(lastIdx);

                    result.Add(next);
                    current = (Vector3)next["position"];
                }

                return result;
            }

            #endregion

            #region Build Check Methods

            protected IEnumerator CheckFoundationsGround(List<Dictionary<string, object>> foundationsData)
            {
                if(foundationsData == null || foundationsData.Count == 0) yield break;
                yield return CoroutineEx.waitForSeconds(_botSetup.speed.timeCheckFoundation);

                _myHeight = 0f;

                for(int i = 0; i < foundationsData.Count; i++)
                {
                    try
                    {
                        var lowestPos = (Dictionary<string, object>)foundationsData[i]["position"];
                        
                        Vector3 lowestNewPos = quaternionRotation * new Vector3(Convert.ToSingle(lowestPos["x"]), Convert.ToSingle(lowestPos["y"]),
                                Convert.ToSingle(lowestPos["z"])) + _buildPoint + new Vector3(0, _myHeight + startHeightToGround, 0);
                        
                        var lowestHeight = TerrainMeta.HeightMap.GetHeight(lowestNewPos);
                        
                        if(lowestNewPos.y < lowestHeight)
                        {
                            _myHeight += lowestHeight - lowestNewPos.y;
                            i = -1;

                            continue;
                        }

                        if(lowestNewPos.y > lowestHeight && lowestNewPos.y - lowestHeight > maxHeightBlockFoundation) yield break;
                        if(lowestHeight < (BotSetup.build.maxDepthInWater * -1f)) yield break;

                        Vector3 p2 = lowestNewPos - new Vector3(0, 3f, 0);
                        var raycastHits = Physics.CapsuleCastAll(lowestNewPos, p2, 4f, (p2 - lowestNewPos).normalized, 3f, LayerMask.GetMask("Construction"));

                        if(raycastHits.Length > 0) yield break;

                        if(_botSetup.build.Checks.checkIsCloseToRoad)
                        {
                            if(!TestPlacingCloseToRoad(lowestNewPos, quaternionRotation))
                            {
                                BuilderInstance.SendMsg(_owner, "CopyPaste_Error_CloseToRoad");
                                _buildPoint = Vector3.zero;
                                _buildCoroutine = null;
                                _isNullGround = false;

                                yield break;
                            }
                        }

                        if(_botSetup.build.Checks.checkIsBuildingBlocked)
                        {
                            if(_owner.IsBuildingBlocked(lowestNewPos, quaternionRotation, new Bounds()))
                            {
                                BuilderInstance.SendMsg(_owner, "CopyPaste_Error_BuildBlocked");
                                
                                _buildPoint = Vector3.zero;
                                _buildCoroutine = null;
                                _isNullGround = false;

                                yield break;
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    yield return CoroutineEx.waitForSeconds(_botSetup.speed.timeCheckFoundation);
                }

                _isNullGround = true;

                yield break;
            }

            protected IEnumerator CheckedBuildBlocksCoroutine(List<Dictionary<string, object>> blocksData, int? layers)
            {
                yield return CoroutineEx.waitForSeconds(_botSetup.speed.timeCheckBuildingBlock);

                if(blocksData == null || blocksData.Count == 0) yield break;

                bool isLayers = layers != null;
                int _layers = !isLayers ? 0 : (int)layers;

                Vector3? lastPosition = null;
                _canBuildBlocks = true;
                
                foreach(Dictionary<string, object> entity in blocksData)
                {
                    RaycastHit hitInfo = new();

                    try
                    {
                        Vector3 lowestNewPos = (Vector3)entity["position"];
                        
                        if(lastPosition == null)
                        {
                            lastPosition = lowestNewPos;
                            continue;
                        }

                        bool layersResult = isLayers ? !Physics.Linecast((Vector3)lastPosition, lowestNewPos, out hitInfo, _layers) : true;

                        if(isLayers && _botSetup.build.Checks.checkForPreventBuilding) 
                        {
                            if(hitInfo.collider?.transform?.parent != null)
                            {
                                if(hitInfo.collider.transform.parent.name.Contains("cave"))
                                {
                                    layersResult = true;
                                }
                            }
                        }

                        if(_botSetup.build.Checks.checkIsCloseToRoad)
                        {
                            if(!TestPlacingCloseToRoad(lowestNewPos, quaternionRotation))
                            {
                                BuilderInstance.SendMsg(_owner, "CopyPaste_Error_CloseToRoad");
                                _buildPoint = Vector3.zero;
                                _buildCoroutine = null;

                                _canBuildBlocks = false;

                                yield break;
                            }
                        }

                        if(_botSetup.build.Checks.checkIsBuildingBlocked)
                        {
                            if(_owner.IsBuildingBlocked(lowestNewPos, quaternionRotation, new Bounds()))
                            {
                                BuilderInstance.SendMsg(_owner, "CopyPaste_Error_BuildBlocked");
                                
                                _buildPoint = Vector3.zero;
                                _buildCoroutine = null;

                                _canBuildBlocks = false;

                                yield break;
                            }
                        }

                        _canBuildBlocks = isLayers ? layersResult : !Physics.Linecast((Vector3)lastPosition, lowestNewPos, out hitInfo);
                        
                        lastPosition = lowestNewPos;

                        if(_botSetup.build.Checks.checkForPreventBuilding)
                        {
                            var physResults = new List<Collider>(Physics.OverlapSphere(lowestNewPos, _botSetup.build.Checks.preventBuildingRadius, LayerMask.GetMask("Prevent Building")));

                            physResults.RemoveAll((col) =>
                            {
                                if(col.transform.parent != null)
                                {
                                    if(col.transform.parent.name.Contains("cave"))
                                    {
                                        return true;
                                    }
                                }

                                return false;
                            });

                            if(physResults.Count != 0)
                            {
                                BuilderInstance.SendMsg(_owner, "CopyPaste_Error_BuildBlocked");
                                _buildPoint = Vector3.zero;
                                _buildCoroutine = null;
                                _canBuildBlocks = false;
    
                                yield break;
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    if(!_canBuildBlocks) yield break;
                    
                    yield return CoroutineEx.waitForSeconds(_botSetup.speed.timeCheckBuildingBlock);
                }

                yield break;
            }

            #endregion

            #region Main Stack

            private IEnumerator BuildCoroutine()
            {
                eulerRotation = new Vector3(0f, rotationCorrection, 0f);
                quaternionRotation = Quaternion.Euler(eulerRotation);

                _stabilityEntities = new List<StabilityEntity>();

                BuilderInstance.SendMsg(_owner, "ChatCommand_Notice_Check");
                Status = StatusBot.CheckedTerritory;

                yield return StartCoroutine(CheckFoundationsGround(foundationsObjects));

                if (!_isNullGround)
                {
                    BuilderInstance.SendMsg(_owner, "CopyPaste_Error_NotSuitablePlace");

                    _buildPoint = Vector3.zero;
                    _buildCoroutine = null;
                    Status = StatusBot.WaitPoint;

                    yield break;
                }

                _buildPoint += new Vector3(0, _myHeight + startHeightToGround, 0);
                yield return InitPosition();

                List<string> layerMasks = new List<string> {
                    "Construction", "World", "Prevent Movement"
                };

                if (_botSetup.build.Checks.checkForPreventBuilding) layerMasks.Add("Prevent Building");

                int layersMask = LayerMask.GetMask(layerMasks.ToArray());

                yield return StartCoroutine(CheckedBuildBlocksCoroutine(wallObjects, layersMask));

                if (!_canBuildBlocks)
                {
                    BuilderInstance.SendMsg(_owner, "CopyPaste_Error_WallBlocked");
                    _buildPoint = Vector3.zero;
                    _buildCoroutine = null;
                    Status = StatusBot.WaitPoint;

                    yield break;
                }

                yield return StartCoroutine(CheckedBuildBlocksCoroutine(roofObjects, layersMask));

                if (!_canBuildBlocks)
                {
                    BuilderInstance.SendMsg(_owner, "CopyPaste_Error_RoofBlocked");
                    _buildPoint = Vector3.zero;
                    _buildCoroutine = null;
                    Status = StatusBot.WaitPoint;

                    yield break;
                }

                yield return StartCoroutine(Sorted());

                BuilderInstance.SendMsg(_owner, "ChatCommand_Success_Start");
                Status = StatusBot.Building;

                _bot.SetNavMeshEnabled(false);
                _bot.Pause();

                _bot.CanUseNavMesh = false;
                _bot.CanUseAStar = false;

                _defaultArea = _bot.DefaultArea;
                _bot.DefaultArea = "Not Walkable";

                _defaultSpeed = _bot.Speed;
                _bot.Speed = 0f;
                _bot.Agent.enabled = false;
                _bot.enabled = false;

                List<Dictionary<string, object>> _todoSorted = Pool.Get<List<Dictionary<string, object>>>();
                _todoSorted.AddRange(todoSorted);

                yield return SpawnObjects(_todoSorted, true);

                Pool.FreeUnmanaged(ref _todoSorted);

                if (_botSetup.build.Doors.deployDoors)
                {
                    List<Dictionary<string, object>> _doorObjects = Pool.Get<List<Dictionary<string, object>>>();
                    _doorObjects.AddRange(doorObjects);

                    yield return SpawnObjects(_doorObjects);

                    Pool.FreeUnmanaged(ref _doorObjects);
                }

                if (_botSetup.build.Cup.deployCup)
                {
                    List<Dictionary<string, object>> _cupObjects = Pool.Get<List<Dictionary<string, object>>>();
                    _cupObjects.AddRange(cupObjects);

                    yield return SpawnObjects(_cupObjects);

                    Pool.FreeUnmanaged(ref _cupObjects);
                }

                foreach (var entity in _stabilityEntities)
                {
                    if (entity == null) continue;

                    entity.grounded = _botSetup.build.disableStability;
                    entity.InitializeSupports();
                    entity.UpdateStability();
                }

                _stabilityEntities = new List<StabilityEntity>();
                _buildPoint = Vector3.zero;

                _npcPlayer.SetAimDirection(_owner.transform.position - _npcPlayer.transform.position);
                _npcPlayer.UpdateActiveItem(new ItemId());

                yield return new WaitForSeconds(1f);

                BuilderInstance.SendMsg(_owner, "ChatCommand_Success_Built");

                _buildPoint = Vector3.zero;
                _buildCoroutine = null;

                _bot.SetNavMeshEnabled(true);
                _bot.Resume();

                _bot.CanUseNavMesh = true;
                _bot.CanUseAStar = true;

                _bot.DefaultArea = _defaultArea;  

                _bot.Speed = _defaultSpeed;
                _bot.Agent.enabled = true;
                _bot.enabled = true;

                _pnpcController.FollowPlayer();

                Destroy(this);

                yield break;
            }
            
            private IEnumerator SpawnObjects(List<Dictionary<string, object>> objects, bool isBuildBlock = false)
            {
                bool hasShownMessage = false;

                foreach (var data in objects)
                {
                    uint buildingID = 0;
                    var prefabname = (string)data["prefabname"];
                    var skinid = ulong.Parse(data["skinid"].ToString());
                    var pos = (Vector3)data["position"];
                    var rot = (Quaternion)data["rotation"];

                    if (isBuildBlock)
                    {
                        if (CheckPlaced(prefabname, pos, rot)) continue;

                        if (prefabname.Contains("pillar") || prefabname.Contains("locks")) continue;
                    }
                    BuildingGrade.Enum? grade = isBuildBlock && data.ContainsKey("grade") ? (BuildingGrade.Enum)Convert.ToInt32(data["grade"]) : null;
                    currentResources.AddNew(BuildCost.GetResources(prefabname), grade);
                    current = data;

                checkCost:
                    if (!CheckCost())
                    {
                        bot_paused = true;
                        if (!hasShownMessage)
                        {
                            _owner.ChatMessage(BuilderInstance.GetMsg("ChatCommand_Error_NoResources", _owner.UserIDString) + resources.ToString(_npcPlayer.inventory, BuilderInstance, _owner.UserIDString, _botSetup.requireResources));
                            hasShownMessage = true;
                        }
                        yield return CoroutineEx.waitForSeconds(2f);
                        goto checkCost;
                    }
                    else
                    {
                        bot_paused = false;
                        if (!Command_paused) goto create;
                    }

                    while (Command_paused)
                    {
                        yield return CoroutineEx.waitForSeconds(0.5f);
                    }

                create:

                    var entity = GameManager.server.CreateEntity(prefabname, pos, rot);

                    if (entity == null)
                        continue;

                    _npcPlayer.UpdateActiveItem(_planID);

                    RemoveBuild(data);

                    var transform = entity.transform;

                    transform.position = pos;
                    transform.rotation = rot;

                    entity.SendMessage("SetDeployedBy", _owner, SendMessageOptions.DontRequireReceiver);

                    entity.OwnerID = _owner.userID;

                    BuildingBlock buildingBlock = entity as BuildingBlock;
                    if (buildingBlock != null)
                    {
                        buildingBlock.blockDefinition = PrefabAttribute.server.Find<Construction>(buildingBlock.prefabID);
                    }

                    if (entity is DecayEntity decayEntity)
                    {
                        if (buildingID == 0)
                            buildingID = BuildingManager.server.NewBuildingID();

                        decayEntity.AttachToBuilding(buildingID);
                    }
                    if (entity is StabilityEntity stabilityEntity)
                    {
                        if (!stabilityEntity.grounded || _botSetup.build.disableStability)
                        {
                            stabilityEntity.grounded = true;
                            _stabilityEntities.Add(stabilityEntity);
                        }
                    }

                    _npcPlayer.SetAimDirection(entity.transform.position - _npcPlayer.transform.position);

                    float time = 0f;

                    yield return new WaitUntil(() =>
                    {
                        time += Time.deltaTime;

                        float height = _bot.transform.position.y;
                        RaycastHit hit;

                        if (Physics.Raycast(_bot.transform.position + new Vector3(0, 10f, 0), Vector3.down, out hit, Mathf.Infinity, LayerMask.GetMask("Construction"))
                            && !hit.GetEntity().PrefabName.Contains("wall.frame")) height = hit.point.y;

                        _bot.transform.position = Vector3.Lerp(new Vector3(_bot.transform.position.x, height, _bot.transform.position.z), entity.transform.position, Time.deltaTime * _botSetup.speed.lerpSpeedMultiplier);

                        return (time > 3f || Vector3.Distance(_botPlayer.transform.position, entity.transform.position) < 1f);
                    });

                    entity.skinID = skinid;
                    entity.Spawn();

                    if (buildingBlock != null)
                    {
                        if (_botSetup.enableUpgradeEffects) Effect.server.Run("assets/bundled/prefabs/fx/build/frame_place.prefab", entity.transform.position);

                        var _grade = grade == null ? BuildingGrade.Enum.Twigs : (BuildingGrade.Enum)grade;
                        if (skinid != 0ul && !HasGrade(buildingBlock, _grade, skinid))
                            skinid = 0ul;
                        entity.skinID = skinid;

                        _npcPlayer.UpdateActiveItem(_hammerID);
                        yield return new WaitForSeconds(1f * _botSetup.speed.buildSpeedMultiplier);

                        if (buildingBlock != null)
                        {
                            buildingBlock.ChangeGrade(_grade);

                            if (_botSetup.enableUpgradeEffects)
                            {
                                if (_grade == BuildingGrade.Enum.Wood)
                                {
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/promote_wood.prefab", _npcPlayer.transform.position);
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/construction_upgrade_wood.prefab", _npcPlayer.transform.position);
                                }
                                else if (_grade == BuildingGrade.Enum.Metal)
                                {
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/promote_metal.prefab", _npcPlayer.transform.position);
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/construction_upgrade_metal.prefab", _npcPlayer.transform.position);
                                }
                                else if (_grade == BuildingGrade.Enum.Stone)
                                {
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/promote_stone.prefab", _npcPlayer.transform.position);
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/construction_upgrade_stone.prefab", _npcPlayer.transform.position);
                                }
                                else if (_grade == BuildingGrade.Enum.TopTier)
                                {
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/construction_upgrade_toptier.prefab", _npcPlayer.transform.position);
                                    Effect.server.Run("assets/bundled/prefabs/fx/build/promote_toptier.prefab", buildingBlock.transform.position);
                                }
                            }

                            buildingBlock.SetHealthToMax();
                            buildingBlock.UpdateSkin();
                            buildingBlock.SendNetworkUpdate();
                            buildingBlock.ResetUpkeepTime();

                            if (data.TryGetValue("customColour", out object customColour))
                                buildingBlock.SetCustomColour(Convert.ToUInt32(customColour));
                        }
                    }
                    else if (entity is BaseCombatEntity baseCombat) baseCombat.SetHealth(baseCombat.MaxHealth());

                    if (entity != null)
                    {
                        if (entity is BuildingPrivlidge cup)
                        {
                            if (_botSetup.build.Cup.canAuth && !cup.IsAuthed(_owner)) cup.AddPlayer(_owner, _owner.userID);
                            cup.SendNetworkUpdate();
                        }

                        var flagsData = new Dictionary<string, object>();

                        if (data.ContainsKey("flags"))
                            flagsData = data["flags"] as Dictionary<string, object>;

                        var flags = new Dictionary<BaseEntity.Flags, bool>();

                        foreach (var flagData in flagsData)
                        {
                            BaseEntity.Flags baseFlag;
                            if (Enum.TryParse(flagData.Key, out baseFlag))
                                flags.Add(baseFlag, Convert.ToBoolean(flagData.Value));
                        }

                        foreach (var flag in flags) entity.SetFlag(flag.Key, flag.Value);
                    }

                    yield return new WaitForSeconds(0.1f);
                }
            }
            
            public void OnInput(float rayLength)
            {
                _rayLength = rayLength;

                if (bot_paused && Status == StatusBot.Building && !CheckCost())
                {
                    _owner.ChatMessage(BuilderInstance.GetMsg("ChatCommand_Error_NoResources", _owner.UserIDString) + resources.ToString(_npcPlayer.inventory, BuilderInstance, _owner.UserIDString, _botSetup.requireResources));
                    return;
                }

                if (Status == StatusBot.WaitPoint)
                {
                    RaycastHit hit;

                    if (Physics.Raycast(_owner.eyes.HeadRay(), out hit, rayLength))
                    {
                        if(!_owner.IsAdmin)
                        {
                            _owner.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                            _owner.SendNetworkUpdateImmediate();

                            _owner.SendConsoleCommand("ddraw.arrow", 3, Color.red, hit.point + new Vector3(0f, hit.point.y + 5), hit.point, 1.5f);

                            _owner.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                            _owner.SendNetworkUpdateImmediate();
                        }
                        else
                        {
                            _owner.SendConsoleCommand("ddraw.arrow", 3, Color.red, hit.point + new Vector3(0f, hit.point.y + 5), hit.point, 1.5f);
                        }

                        _buildPoint = hit.point;
                        rotationCorrection = _owner.viewAngles.y;
                        Status = StatusBot.CheckedTerritory;
                    }
                }
            }
            
            #endregion

            #region Methods

            public void PrintStatus()
            {
                if(bot_paused)
                {
                    _owner.ChatMessage(BuilderInstance.GetMsg("ChatCommand_Error_NoResources", _owner.UserIDString) + resources.ToString(_npcPlayer.inventory, BuilderInstance, _owner.UserIDString, _botSetup.requireResources));
                    return;
                }
                if(Command_paused)
                {
                    BuilderInstance.SendMsg(_owner, "CommandPaused");
                    return;
                }
                switch(Status)
                {
                    case StatusBot.CalculationConstruction: BuilderInstance.SendMsg(_owner, "BotCalculated"); return;
                    case StatusBot.CheckedTerritory: BuilderInstance.SendMsg(_owner, "BotCheckedTerritory"); return;
                    case StatusBot.Building: BuilderInstance.SendMsg(_owner, "BotBuilding"); return;
                }
            }

            #endregion

            #region Static

            public static T CreateBot<T>(BasePlayer caller, PersonalNPC.PlayerBotController pnpcController, Configuration.BotSetup botSetup, BuildCostInfo info, PNPCAddonBuilder pluginInstance, Vector3? pos = null) where T : PlayerBuilderController
            {
                var bot = pnpcController.bot;

                var plannerItem = ItemManager.CreateByName("building.planner");
                plannerItem.MoveToContainer(bot.inventory.containerBelt);

                var hammerItem = ItemManager.CreateByName("hammer");
                hammerItem.MoveToContainer(bot.inventory.containerBelt);

                bot.UpdateActiveItem(plannerItem.uid);
                
                var controller = bot.gameObject.AddComponent<T>();
                
                controller._botSetup = botSetup;

                controller._owner = caller;
                controller._botPlayer = bot;
                controller._npcPlayer = bot;
                controller._pnpcController = pnpcController;
                
                controller._planID = plannerItem.uid;
                controller._hammerID = hammerItem.uid;

                controller.BuilderInstance = pluginInstance;
                controller.BuildCost = info;

                controller._bot = bot.GetComponent<BaseNavigator>();
                controller._bot.StoppingDistance = 0.01f;

                return controller;
            }
        
            #endregion

            public enum StatusBot
            {
                CalculationConstruction,
                CheckedTerritory,
                WaitPoint,
                Building
            }
        }

        #endregion
    }
}  