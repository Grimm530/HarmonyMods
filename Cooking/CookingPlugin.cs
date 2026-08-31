using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Converters;
using static Oxide.Plugins.Cooking.CookingManager;
using System.Text;

/* Suggestions
 * Add command option to give recipe cards to target player on command.
 * Add message when a player picks up a recipe card that describes the buffs.
 * Add admin UI to give ingredients
 */

/*​‌​​‌‌​‌​‌​‌​‌​​​‌‌​​​‌‌​‌‌‌‌​​​​‌​​‌‌‌​​‌‌​​‌‌‌​​‌‌‌‌​‌​​‌‌‌‌​‌2.0.35 Change Log
 * Fixed an issue caused by the July wipe that reset calories/hydration when consuming non-cooking related foods that were ingredients in meals.
*/

namespace Oxide.Plugins
{
	[Info("Cooking", "imthenewguy", "2.0.35")]
	[Description("Extends Rust's cooking functionality.")]
	public partial class Cooking : RustPlugin
	{
        public static Cooking Instance { get; internal set; }

        #region Config

        private Configuration config;
        public class Configuration
        {
            [JsonProperty("Custom cooking data directory (absolute path, empty = HarmonyData/Cooking)")]
            public string CustomCookingDataDirectory = "";

            [JsonProperty("General Settings")]
            public GeneralSettings generalSettings = new GeneralSettings();

            [JsonProperty("Meal Settings")]
            public MealSettings mealSettings = new MealSettings();

            [JsonProperty("Menu Settings")]
            public MenuSettings menuSettings = new MenuSettings();

            [JsonProperty("Ingredient Settings")]
            public IngredientSettings ingredientSettings = new IngredientSettings();

            [JsonProperty("Wipe Settings")]
            public WipeSettings wipeSettings = new WipeSettings();

            [JsonProperty("API Settings")]
            public APISettings apiSettings = new APISettings();

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        #region Menu classes

        public class APISettings
        {
            [JsonProperty("RandomTrader Integration")]
            public RandomTraderAPI random_trader_api = new RandomTraderAPI();

            [JsonProperty("SkillTree Integration")]
            public SkillTreeInfo skilltree_settings = new SkillTreeInfo();

            [JsonProperty("SurvivalArena Integration")]
            public SurvivalArenaSettings survivalArenaSettings = new SurvivalArenaSettings();

            [JsonProperty("CustomItemVending Integration")]
            public CustomItemVendingSettings customItemVendingSettings = new CustomItemVendingSettings();
        }

        public class CustomItemVendingSettings
        {
            [JsonProperty("Add ingredients as currency?")]
            public bool AddIngredientsAsCurrency = true;

            [JsonProperty("Add meals as currency?")]
            public bool AddMealsAsCurrency = true;
        }

        public class SurvivalArenaSettings
        {
            [JsonProperty("Prevent players joining the Survival Arena event while buffed with the following meals")]
            public List<string> meal_blacklist = new List<string>();
        }

        public class SkillTreeInfo
        {
            [JsonProperty("Integrate Cooking nodes to SkillTree?")]
            public bool enabled = false;

            [JsonProperty("Cooking skills list")]
            public Dictionary<string, SkillTreeNodeStructure> Skills = new Dictionary<string, SkillTreeNodeStructure>();
        }

        Dictionary<string, SkillTreeNodeStructure> DefaultSkills
        {
            get
            {
                return new Dictionary<string, SkillTreeNodeStructure>()
                {
                    ["Slow Metabolism"] = new SkillTreeNodeStructure(false, 5, 0.2f, "This skill increases the duration of cooking buffs by <color=#42f105>{0}%</color> per level.", SkillTreeBuffTypes.BuffDuration, 3005740453, "https://www.dropbox.com/s/fvpgzz8tfpzupuq/Shamanskill_04_nobg.v1.png?dl=1", 2),
                    ["Master Chef"] = new SkillTreeNodeStructure(false, 5, 0.2f, "This skill decreases the cooking time of meals by <color=#42f105>{0}%</color> per level.", SkillTreeBuffTypes.CookingTime, 3005749415, "https://www.dropbox.com/s/cvwi4gjiq39hl3o/Assassinskill_43_nobg.v1.png?dl=1", 1),
                    ["Keen Eye"] = new SkillTreeNodeStructure(false, 5, 0.2f, "This skill increases the chance of finding ingredients by <color=#42f105>{0}%</color> per level.", SkillTreeBuffTypes.IngredientChance, 3005757959, "https://www.dropbox.com/s/74fbjt2qzf11het/Druideskill_17_nobg.v1.png?dl=1", 3),
                };
            }
        }

        public class SkillTreeNodeStructure
        {
            public bool enabled;
            public int max_level;
            public float value_per_level;
            public string description;
            public SkillTreeBuffTypes buffType;
            public ulong icon_skin_id;
            public string icon_url;
            public string node_name;
            public int tier;
            
            public SkillTreeNodeStructure(bool enabled, int max_level, float value_per_level, string description, SkillTreeBuffTypes buffType, ulong icon_skin_id, string icon_url, int tier)
            {
                this.enabled = enabled;
                this.max_level = max_level;
                this.value_per_level = value_per_level;
                this.description = description;
                this.buffType = buffType;
                this.icon_skin_id = icon_skin_id;
                this.icon_url = icon_url;
                this.tier = tier;
            }
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum SkillTreeBuffTypes
        {
            CookingTime,
            BuffDuration,
            IngredientChance,
        }

        public class GeneralSettings
        {
            [JsonProperty("Commands to open up the recipe menu")]
            public List<string> recipeMenuCMDs = new List<string>();

            [JsonProperty("Should cooking handle the item splitting for ingredients and meals? [set to false if using a stacks plugin]")]
            public bool cookingHandleSplits = true;

            [JsonProperty("Permission settings")]
            public PermissionSettings permissionSettings = new PermissionSettings();

            [JsonProperty("Buff settings")]
            public BuffSettings buffSettings = new BuffSettings();

            [JsonProperty("Chance modifier for ingredient drops when collecting player grown entities [0.5 = half the normal chance]")]
            public float chance_modifier_grown = 0.5f;

            [JsonProperty("Permission required to receive ingredients from player grown entities")]
            public string grown_permission = null;

            [JsonProperty("Only roll ingredient chances on the final hit of a node?")]
            public bool final_hit_only = false;

            [JsonProperty("Ingredient drop sources")]
            public Dictionary<GatherSource, GatherInfo> dropSources = new Dictionary<GatherSource, GatherInfo>();

            [JsonProperty("Ingredient bag settings")]
            public IngredientBagSettings ingredientBagSettings = new IngredientBagSettings();

            [JsonProperty("Sound settings")]
            public SoundSettings soundSettings = new SoundSettings();

            [JsonProperty("Market settings")]
            public MarketSettings marketSettings = new MarketSettings();

            [JsonProperty("Power Tool Settings")]
            public PowerToolSettings powerToolSettings = new PowerToolSettings();

        }         

        public class MarketSettings
        {
            [JsonProperty("Currency type [scrap, economics, serverrewards]")]
            public string currency = "scrap";

            [JsonProperty("Command to open the farmers market")]
            public string marketCMD = "market";

            [JsonProperty("Reset market stock to the set value on wipe? [-1 disables]")]
            public int reset_market_amount = 0;

            [JsonProperty("Settings for market NPCs")]
            public NPCSettings npcSettings = new NPCSettings();
        }

        public class NPCSettings
        {
            [JsonProperty("Wipe the NPC location data when the service wipes?")]
            public bool remove_on_wipe = false;

            [JsonProperty("List of items that the NPC will wear")]
            public List<NPCItems> wornItems = new List<NPCItems>();

            [JsonProperty("Item that the NPC will hold")]
            public NPCItems heldItem = new NPCItems();

            public class NPCItems
            {
                public string shortname;
                public ulong skin;

                public NPCItems(string shortname, ulong skin)
                {
                    this.shortname = shortname;
                    this.skin = skin;
                }
                public NPCItems()
                {
                    
                }
            }
        }

        public class SoundSettings
        {
            [JsonProperty("Sound when interacting with most cooking menu elements [set empty to disable]")]
            public string navigation_into_menu_sound = "assets/prefabs/tools/medical syringe/effects/pop_button_cap.prefab";

            [JsonProperty("Sound when going back from the recipe menu to recipes list [set empty to disable]")]
            public string navigation_back_sound = "assets/prefabs/misc/orebonus/effects/bonus_fail.prefab";

            [JsonProperty("Sound when successfully adding a meal to the queue or cooking it [set empty to disable]")]
            public string cook_queued_sound = "assets/prefabs/misc/halloween/pumpkin_bucket/effects/eggexplosion.prefab";

            [JsonProperty("Sound when a cooking attempt fails [set empty to disable]")]
            public string invalid_cook_sound = "assets/prefabs/misc/easter/painted eggs/effects/egg_upgrade.prefab";

            [JsonProperty("Sound when toggling a button in the settings menu [set empty to disable]")]
            public string settings_buttons_sound = "assets/prefabs/tools/medical syringe/effects/pop_button_cap.prefab";

            [JsonProperty("Sound when adding a recipe to favourites [set empty to disable]")]
            public string add_to_favourites_sound = "assets/prefabs/voiceaudio/discoball/effects/disco-ball-deploy.prefab";

            [JsonProperty("Sound when removing a recipe from favourites [set empty to disable]")]
            public string remove_from_favourites_sound = "assets/prefabs/deployable/bear trap/effects/bear-trap-deploy.prefab";

            [JsonProperty("Sound when canceling a cooking job [set empty to disable]")]
            public string cancel_cook_sound = "assets/prefabs/deployable/bbq/effects/barbeque-deploy.prefab";

            [JsonProperty("Sound when pressing the cooking button from an oven [set empty to disable]")]
            public string press_cooking_button_sound = "assets/prefabs/deployable/campfire/effects/campfire-deploy.prefab";
        }

        public class IngredientBagSettings
        {
            [JsonProperty("Command to open up the ingredients bag")]
            public string iBagCMD = "ibag";

            [JsonProperty("Prefab to use for the ingredient bag")]
            public string bag_prefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";

            [JsonProperty("Consolidate the stacks of the bag to 1 slot for each item [exceeds max stack sizes]")]
            public bool consolidate_stacks = true;

            [JsonProperty("Bag item settings [Make sure these are the same as the recipe]")]
            public BagItemSettings bagItemSettings = new BagItemSettings();
            public class BagItemSettings
            {
                public string shortname = "halloween.lootbag.large";
                public ulong skinID = 2578002003;
                public string displayName = "ingredient bag";
            }

            [JsonProperty("Whitelist of shortnames that can be placed into the ingredient bag, in addition to ingredients")]
            public List<string> whitelist = new List<string>();

            [JsonProperty("Blacklist of shortnames that canot be placed into the ingredient bag, even if they are an ingredient")]
            public List<string> blacklist = new List<string>();

            [JsonProperty("Place gathered ingredients directly into the ibag?")]
            public bool gather_directly_into_bag = true;

            [JsonProperty("Maximum ibag slots")]
            public int maxSlots = 30;

            [JsonProperty("Should ingredient bags drop on death?")]
            public bool dropOnDeath = false;
        }

        public class PermissionSettings
        {
            [JsonProperty("Drop chance multiplier permissions [1.0 = double chance]")]
            public Dictionary<string, float> DropChanceMultiplier = new Dictionary<string, float>();
        }

        #region Buff settings classes

        public class BuffSettings
        {
            [JsonProperty("Allow the stacking of buffs [false = selects the highest]?")]
            public bool allow_buff_stacking = true;

            [JsonProperty("Settings for the Wealth buff")]
            public WealthSettings wealthSettings = new WealthSettings();

            [JsonProperty("Settings for the Component Luck buff")]
            public ComponentLuckSettings componentLuckSettings = new ComponentLuckSettings();

            [JsonProperty("Settings for the Electronics Luck buff")]
            public ElectronicsLuckSettings electronicsLuckSettings = new ElectronicsLuckSettings();

            [JsonProperty("Settings for the Crafting Refund buff")]
            public CraftingRefundSettings craftingRefundSettings = new CraftingRefundSettings();

            [JsonProperty("Settings for the Duplicator buff")]
            public DuplicatorSettings duplicatorSettings = new DuplicatorSettings();

            [JsonProperty("Settings for the Loot Pickup buff")]
            public LootPickupSettings lootPickupSettings = new LootPickupSettings();

            [JsonProperty("Settings for the Fishing Luck buff")]
            public FishingLuckSettings fishingLuckSettings = new FishingLuckSettings();

            [JsonProperty("Settings for the Comfortable buff")]
            public ComfortSettings comfortableSettings = new ComfortSettings();

            [JsonProperty("Settings for the Food Share buff")]
            public FoodShareSettings foodShareSettings = new FoodShareSettings();

            [JsonProperty("Settings for the Heal Share buff")]
            public HealShareSettings healShareSettings = new HealShareSettings();

            [JsonProperty("Settings for the Health Regen buff")]
            public HealthRegenSettings healthRegenSettings = new HealthRegenSettings();

            [JsonProperty("Settings for the Madness buff")]
            public MadnessSettings madnessSettings = new MadnessSettings();
        }

        public class MadnessSettings
        {
            [JsonProperty("How far away can this player be heard from when under the effect of the madness buff")]
            public float distanceHeard = 30;

            [JsonProperty("Effect prefab to use")]
            public string madnessPrefab = "assets/bundled/prefabs/fx/player/beartrap_scream.prefab";
        }

        public class HealthRegenSettings
        {
            [JsonProperty("How long should the buff stop working when a player takes damage?")]
            public float damageCooldown = 5;
        }
        
        public class HealShareSettings
        {
            [JsonProperty("Distance for the effect to work")]
            public float shareDistance = 5f;
        }

        public class FoodShareSettings
        {
            [JsonProperty("Maximum distance from the player that players will be shared food")]
            public float foodShareRadius = 20f;
        }

        public class ComfortSettings
        {
            [JsonProperty("Comfort radius")]
            public float comfortRadius = 10f;

            [JsonProperty("Comfort amount [1.0 = 100%]")]
            public float comfortAmount = 1f;
        }

        public class FishingLuckSettings
        {
            [JsonProperty("Minimum quantity per drop")]
            public int min = 1;

            [JsonProperty("Maximum quantity per drop")]
            public int max = 1;

            [JsonProperty("Use the defined loot table for this skill? [Setting false will allow most items in the game to be obtained]")]
            public bool use_defined_loottable = false;

            [JsonProperty("Blacklist of items if not using the defined loot table")]
            public List<string> non_defined_loottable_blacklist = new List<string>();

            [JsonProperty("Defined loot table")]
            public List<string> definedLootTable = new List<string>();
        }

        public class WealthSettings
        {
            [JsonProperty("Currency type to use for the wealth buff [scrap, economics, serverrewards]")]
            public string currencyType = "scrap";

            public int min_quantity = 1;
            public int max_quantity = 3;
        }

        public class ComponentLuckSettings
        {
            public int min = 1;
            public int max = 2;

            [JsonProperty("List of items to blacklist from the Component Luck buff")]
            public List<string> blacklist = new List<string>();
        }

        public class ElectronicsLuckSettings
        {
            public int min = 1;
            public int max = 2;

            [JsonProperty("List of items to blacklist from the Electronics Luck buff")]
            public List<string> blacklist = new List<string>();
        }

        public class CraftingRefundSettings
        {
            [JsonProperty("List of components to blacklist from refunding")]
            public List<string> blacklist = new List<string>();
        }

        public class DuplicatorSettings
        {
            [JsonProperty("List of items to blacklist from duplicating")]
            public List<string> blacklist = new List<string>();
        }

        public class LootPickupSettings
        {
            [JsonProperty("Maximum distance that the loot pickup buff can trigger from [0 = Unlimited]")]
            public float maxDistance = 0f;

            [JsonProperty("Only allow this perk to work with melee attacks?")]
            public bool meleeOnly = false;
        }

        #endregion

        public class MenuSettings
        {
            [JsonProperty("Icon that appears when a meal is not favourited")]
            public ulong favourite_unselected = 2932803496;

            [JsonProperty("Icon that appears when a meal is favourited")]
            public ulong favourite_selected = 2932804164;

            [JsonProperty("Cooking queue settings")]
            public QueueMenuSettings queueMenuSettings = new QueueMenuSettings();

            [JsonProperty("Buff UI settings")]
            public BuffUISettings buffUISettings = new BuffUISettings();

            [JsonProperty("Recipe menu settings")]
            public RecipeMenuSettings recipeMenuSettings = new RecipeMenuSettings();

            [JsonProperty("Favourites menu settings")]
            public FavouritesMenuSettings favouriteMenuSettings = new FavouritesMenuSettings();

            [JsonProperty("Core menu settings")]
            public CoreMenuSettings coreMenuSettings = new CoreMenuSettings();

            [JsonProperty("Recipe detailed menu settings")]
            public RecipeDetailedMenuSettings recipeDetailedMenuSettings = new RecipeDetailedMenuSettings();

            [JsonProperty("Ingredient menu settings")]
            public IngredientMenuSettings ingredientMenuSettings = new IngredientMenuSettings();

            [JsonProperty("UI Anchor settings")]
            public AnchorSettings anchorSettings = new AnchorSettings();
            
            [JsonProperty("HUD button settings")]
            public HudButtonSettings hudButtonSettings = new HudButtonSettings();

            [JsonProperty("Whitelist of ovens that will exclusively show the recipe menu button [prefab shortnames]")]
            public List<string> ovenWhitelist = new List<string>();

            [JsonProperty("Blacklist of ovens that will not show the recipe menu button [prefab shortnames]")]
            public List<string> ovenBlacklist = new List<string>();

            [JsonProperty("Show the cooking button on the cooking workbench?")]
            public bool showOnCookingWorkbench = true;
        }

        public class HudButtonSettings
        {
            [JsonProperty("Enable the HUD buttons")]
            public bool enable = true;

            [JsonProperty("Which layer should the buttons attach to? [Overlay, Hud]")]
            public string hudLayer = "Overlay";

            [JsonProperty("Button anchor offset x min")]
            public float offset_x_min = -182.9f;

            [JsonProperty("Button anchor offset y min")]
            public float offset_y_min = 106.6f;

            [JsonProperty("Button anchor offset x max")]
            public float offset_x_max = -132.9f;

            [JsonProperty("Button anchor offset y max")]
            public float offset_y_max = 156.6f;

            [JsonProperty("Button colour [Red, Green, Blue, Alpha. 1.0 = full]")]
            public string buttonColour = "0.4352942 0.5294118 0.2627451 1";
        }

        public class AnchorSettings
        {
            [JsonProperty("Craft queue anchor settings")]
            public AnchorClass craftQueue = new AnchorClass("1 1", "1 1", "-118 -59", "-81 -23");

            [JsonProperty("Buff icon anchor settings")]
            public AnchorClass buffIcons = new AnchorClass("1 0.5", "1 0.5", "-70 0", "-24 46");

            [JsonProperty("Oven button anchor settings")]
            public AnchorClass ovenButton = new AnchorClass("0.5 0", "0.5 0", "463 481.9", "573 507.9");

            [JsonProperty("Cooking workbench button anchor settings")]
            public AnchorClass cookingWorkbenchButton = new AnchorClass("0.5 0", "0.5 0", "463 605", "573 631");

            [JsonProperty("UI buttons anchor settings")]
            public AnchorClass uiButtons = new AnchorClass("1 0", "1 0", "-183.5 1", "-147.5 15");
        }

        public class AnchorClass
        {
            public string anchorMin;
            public string anchorMax;
            public string offsetMin;
            public string offsetMax;
            public AnchorClass(string anchorMin, string anchorMax, string offsetMin, string offsetMax)
            {
                this.anchorMin = anchorMin;
                this.anchorMax = anchorMax;
                this.offsetMin = offsetMin;
                this.offsetMax = offsetMax;
            }
        }

        public class IngredientMenuSettings
        {
            public string title_bar_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string ingredient_backpanel_colour = "0.2941177 0.2901961 0.2784314 1";
            public string ingredient_frontpanel_colour = "0.4901961 0.4901961 0.4901961 1";
        }

        public class RecipeDetailedMenuSettings
        {
            public string title_bar_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string image_backpanel_colour = "0.09411765 0.09411765 0.09411765 1";
            public string image_frontpanel_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string ingredient_image_backpanel_colour = "0.09411765 0.09411765 0.09411765 1";
            public string ingredient_image_frontpanel_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string quantity_backpanel_colour = "0.09411765 0.09411765 0.09411765 1";
            public string quantity_frontpanel_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string quantity_inputtext_panel_colour = "0.4901961 0.4901961 0.4901961 1";
            public string craft_button_backpanel_colour = "0.09411765 0.09411765 0.09411765 1";
            public string craft_button_frontpanel_colour = "0.2941177 0.2901961 0.2784314 0.8";
        }

        public class QueueMenuSettings
        {           
            public string backpanel = "0.2941177 0.2901961 0.2784314 0.8";
            public string frontpanel = "0.4901961 0.4901961 0.4901961 1";
        }

        public class BuffUISettings
        {
            public string backpanel = "0.1981132 0.1981132 0.1981132 1";
            public string frontpanel = "0.5283019 0.5283019 0.5283019 1";
        }

        public class MealSettings
        {
            [JsonProperty("Maximum meals that a player can have active at once")]
            public int maxMealsAllowed = 3;

            [JsonProperty("Allow the default effects of the base item to trigger (calories, hydration, healing, sickness etc)?")]
            public bool allowDefaultMealEffects = false;

            [JsonProperty("Maximum meals that a player can have queued to cook? [0 = no limit]")]
            public int maxCookQueue = 0;

            [JsonProperty("Cookbook settings")]
            public CookbookSettings cookbookSettings = new CookbookSettings();

            [JsonProperty("Settings to handle meals being added to loot containers")]
            public LootSettings lootSettings = new LootSettings();

            [JsonProperty("Meals")]
            public Dictionary<string, MealInfo> meals = new Dictionary<string, MealInfo>(StringComparer.InvariantCultureIgnoreCase);

        }

        public class LootSettings
        {
            [JsonProperty("Enable these items to spawn in loot containers?")]
            public bool enable = false;

            [JsonProperty("Minimum items to provide")]
            public int min = 1;

            [JsonProperty("Maximum items to provide")]
            public int max = 1;

            [JsonProperty("Container sources and chances for these items to spawn")]
            public Dictionary<string, float> dropChance = new Dictionary<string, float>();

            [JsonProperty("Blacklist of items that cannot be added to containers")]
            public List<string> blacklist = new List<string>();
        }

        public class CookbookSettings
        {
            [JsonProperty("Only allow players to cook recipes that they have learnt?")]
            public bool enabled = false;

            [JsonProperty("Should we show buff information of the meal to the player when they find the recipe card?")]
            public bool describeMeal = true;

            [JsonProperty("Should meals that a player has not learnt be visible in the menu?")]
            public bool unlearntVisible = false;

            [JsonProperty("If using recipe cards, what meals should players start with by default?")]
            public List<string> defaultMeals = new List<string>();

            [JsonProperty("Allow players to find duplicates of recipes they already have?")]
            public bool allowFindDuplicates = true;

            [JsonProperty("Drop chances")]
            public Dictionary<string, float> recipeDropChance = new Dictionary<string, float>()
            {
                ["crate_normal_2"] = 3f,
                ["crate_normal"] = 5f,
                ["crate_elite"] = 15f,
                ["crate_underwater_basic"] = 6f,
                ["crate_underwater_advanced"] = 12f,
                ["heli_crate"] = 10f,
                ["bradley_crate"] = 10f,
                ["codelockedhackablecrate"] = 10f,
                ["codelockedhackablecrate_oilrig"] = 10f,
                ["crate_tools"] = 1.5f,
                ["loot-barrel-1"] = 1f,
                ["loot_barrel_1"] = 1f,
                ["loot-barrel-2"] = 2f,
                ["loot_barrel_2"] = 2f
            };

            [JsonProperty("Permission based drop chance modifiers [permission, modifier][1.0 = 100% chance increase]")]
            public Dictionary<string, float> recipeDropChancePerms = new Dictionary<string, float>();

            [JsonProperty("Blueprint item")]
            public LearnableItem blueprint = new LearnableItem("xmas.present.large", 2939176668, "recipe card: {0}", "unwrap");
            public class LearnableItem
            {
                public string shortname;
                public ulong skin;
                public string displayName;
                public string triggerWord;
                public LearnableItem(string shortname, ulong skin, string displayName, string triggerWord)
                {
                    this.shortname = shortname;
                    this.skin = skin;
                    this.displayName = displayName;
                    this.triggerWord = triggerWord;
                }
            }
        }

        public class CoreMenuSettings
        {
            public string button_backpanel_colour = "0.09433961 0.09433961 0.09433961 1";
            public string button_colour = "0.2924528 0.2886735 0.2800374 0.8";
            public string selected_colour = "0.1961107 0.3025389 0.3679245 0.5019608";
        }

        public class FavouritesMenuSettings
        {
            public string title_bar_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string recipe_backpanel_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string recipe_img_panel_colour = "0.490566 0.490566 0.490566 1";
            public string recipe_title_panel_colour_learnt = "0.2712 0.3956 0.4528 1";
            public string recipe_title_panel_colour_unlearnt = "0.62 0.663 0.678 1";
            public string recipe_info_benefits_panel_colour = "0.3867925 0.3867925 0.3867925 1";
            public string recipe_info_benefits_backpanel_colour = "0.490566 0.490566 0.490566 1";
            public string recipe_info_missing_ingredients_panel_colour = "0.3867925 0.3867925 0.3867925 1";
        }

        public class RecipeMenuSettings
        {
            public string title_bar_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string recipe_backpanel_colour = "0.2941177 0.2901961 0.2784314 0.8";
            public string recipe_img_panel_colour = "0.490566 0.490566 0.490566 1";
            public string recipe_title_panel_colour_learnt = "0.2712 0.3956 0.4528 1";
            public string recipe_title_panel_colour_unlearnt = "0.62 0.663 0.678 1";
            public string recipe_info_benefits_panel_colour = "0.3867925 0.3867925 0.3867925 1";
            public string recipe_info_benefits_backpanel_colour = "0.490566 0.490566 0.490566 1";
            public string recipe_info_missing_ingredients_panel_colour = "0.3867925 0.3867925 0.3867925 1";
        }

        public class IngredientSettings
        {
            [JsonProperty("Allow the default effects of an item that an ingredient inherits to apply when eaten [if no other buffs are assigned]?")]
            public bool allow_default_ingredient_effects = true;

            [JsonProperty("Settings to handle ingredients being added to loot containers")]
            public LootSettings lootSettings = new LootSettings();

            public Dictionary<string, IngredientsInfo> ingredients = new Dictionary<string, IngredientsInfo>(StringComparer.InvariantCultureIgnoreCase);
        }

        public class PowerToolSettings
        {
            [JsonProperty("Ingredient chance modifier when using a chainsaw [0 = no ingredients drops]")]
            public float chainsawIngredientModifier = 1.0f;

            [JsonProperty("Ingredient chance modifier when using a jackhammer [0 = no ingredients drops]")]
            public float jackhammerIngredientModifier = 1.0f;

            [JsonProperty("Buff modifier when using a jackhammer [0 = won't work with buffs]")]
            public float jackhammerBuffModifier = 1.0f;

            [JsonProperty("Buff modifier when using a chainsaw [0 = won't work with buffs]")]
            public float chainsawBuffModifier = 1.0f;
        }

        public class WipeSettings
        {
            [JsonProperty("Reset the learned recipes for all players when the server wipes [only applicable if using recipe cards]?")]
            public bool ResetRecipeCardsOnWipe = true;

            [JsonProperty("Wipe the contents of players ingredients bags on wipe?")]
            public bool ResetIngredientBagOnWipe = true;
        }

        public class GatherInfo
        {
            public float chance;
            public string permission_required;
            public GatherInfo(float chance, string permission_required = null)
            {
                this.chance = chance;
                this.permission_required = permission_required;
            }
        }

        #endregion

        #region Default config

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();

            config.mealSettings.meals = DefaultBasicMeals;
            config.ingredientSettings.ingredients = DefaultBasicIngredients;
            config.generalSettings.dropSources = DefaultGatherSources();
            config.menuSettings.ovenBlacklist = new List<string>() { "furnace", "furnace.large", "electricfurnace.deployed" };
            config.generalSettings.ingredientBagSettings.blacklist.Add("cloth");
            config.apiSettings.skilltree_settings.Skills = DefaultSkills;
            config.mealSettings.lootSettings.dropChance = DefaultMealIngredientLootChances;
        }

        #region Default recipes and ingredients

        Dictionary<string, MealInfo> DefaultBasicMeals
        {
            get
            {
                return new Dictionary<string, MealInfo>()
                {
                    // Crafted ingredients
                    ["ingredient bag"] = new MealInfo(true, "halloween.lootbag.large", 2578002003, "A bag to hold your ingredients.", 0, 10, new Dictionary<Buff, float>() { [Buff.Ingredient_Storage] = 0 }, new Dictionary<string, int>()
                    {
                        ["cloth"] = 1000
                    }),
                    ["cheese"] = new MealInfo(true, "apple", 2572178709, "A useful cooking ingredient.", 0, 10, new Dictionary<Buff, float>() { [Buff.Ingredient] =  0}, new Dictionary<string, int>()
                    {
                        ["milk"] = 2
                    }),

                    // buff recipes
                    ["berry cobbler"] = new MealInfo(true, "fish.cooked", 2572717597, "Better than a shoe maker.", 60, 15, new Dictionary<Buff, float>() { [Buff.Heal_Share] = 1}, new Dictionary<string, int>()
                    {
                        ["wheat"] = 2,
                        ["blue berry"] = 5,
                        ["red berry"] = 5,
                        ["spices"] = 1
                    }),
                    ["icecream"] = new MealInfo(true, "fish.cooked", 2575799596, "You scream I scream we all scream for...", 1200, 15, new Dictionary<Buff, float>() { [Buff.Cold_Resist] = 0.5f }, new Dictionary<string, int>()
                    {
                        ["milk"] = 2,
                        ["sugar"] = 2,
                        ["egg"] = 1,
                    }),
                    ["chocolate brownie"] = new MealInfo(true, "fish.cooked", 2579578852, "I am starting to feel a bit weird...", 300, 15, new Dictionary<Buff, float>() { [Buff.Melee_Resist] = 0.7f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 2,
                        ["chocolate bar"] = 5,
                        ["milk"] = 1,
                        ["sugar"] = 1,
                    }),
                    ["apple cake"] = new MealInfo(true, "fish.cooked", 2657028513, "Smells delicious, eh?", 600, 15, new Dictionary<Buff, float>() { [Buff.Condition_Loss_Reduction] = 0.5f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 2,
                        ["sugar"] = 2,
                        ["milk"] = 1,
                        ["apple"] = 5,
                    }),
                    ["sponge cake"] = new MealInfo(true, "fish.cooked", 2579277272, "This cake feels extra bouncy.", 600, 15, new Dictionary<Buff, float>() { [Buff.Fall_Damage_resist] = 0.5f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 2,
                        ["sugar"] = 1,
                        ["milk"] = 1,
                        ["egg"] = 1,
                    }),
                    ["dark chocolate mousse"] = new MealInfo(true, "fish.cooked", 2762932872, "Dark chocolate makes you smarter.", 300, 15, new Dictionary<Buff, float>() { [Buff.Wealth] = 0.5f }, new Dictionary<string, int>()
                    {
                        ["chocolate bar"] = 5,
                        ["sugar"] = 1,
                        ["milk"] = 1,
                        ["egg"] = 1,
                    }),
                    ["mozzarella sticks"] = new MealInfo(true, "fish.cooked", 2831609635, "Chicken fingers be damned!", 600, 15, new Dictionary<Buff, float>() { [Buff.Harvest] = 0.5f }, new Dictionary<string, int>()
                    {
                        ["cheese"] = 2,
                        ["wheat"] = 1,
                        ["spices"] = 1,
                        ["milk"] = 1,
                    }),
                    ["sausage roll"] = new MealInfo(true, "fish.cooked", 2657034158, "Baked to perfection.", 3600, 15, new Dictionary<Buff, float>() { [Buff.Metabolism_Overload] = 0.5f }, new Dictionary<string, int>()
                    {
                        ["raw pork"] = 5,
                        ["wheat"] = 2,
                        ["spices"] = 1,
                        ["raw bear meat"] = 5,
                    }),
                    ["cheese toasty"] = new MealInfo(true, "fish.cooked", 2783365998, "A yummy snack!", 120, 15, new Dictionary<Buff, float>() { [Buff.Comfort] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["bread loaf"] = 2,
                        ["cheese"] = 3
                    }),
                    ["french toast"] = new MealInfo(true, "fish.cooked", 2572718376, "Icecream on toast for breakfast, because... French!", 60, 15, new Dictionary<Buff, float>() { [Buff.Explosion_Resist] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["bread loaf"] = 2,
                        ["sugar"] = 1,
                        ["spices"] = 1
                    }),
                    ["burrito"] = new MealInfo(true, "fish.cooked", 2575621666, "A delicious concoction of meat, spices and vegies, wrapped in a cornflour blanket.", 1200, 15, new Dictionary<Buff, float>() { [Buff.Animal_Resist] = 0.5f }, new Dictionary<string, int>()
                    {
                        ["raw bear meat"] = 10,
                        ["cheese"] = 3,
                        ["wheat"] = 1,
                        ["tomato"] = 1
                    }),
                    ["chilli cheese fries"] = new MealInfo(true, "fish.cooked", 2656955863, "This isn't actually that hot...", 600, 15, new Dictionary<Buff, float>() { [Buff.Electronics_Luck] = 0.25f }, new Dictionary<string, int>()
                    {
                        ["potato"] = 10,
                        ["cheese"] = 3,
                        ["spices"] = 1,
                        ["raw pork"] = 1
                    }),
                    ["sushi"] = new MealInfo(true, "fish.cooked", 2428317574, "I sure hope this is fresh.", 600, 15, new Dictionary<Buff, float>() { [Buff.Fishing_Luck] = 0.1f }, new Dictionary<string, int>()
                    {
                        ["raw fish"] = 5,
                        ["rice"] = 1,
                        ["seaweed"] = 1
                    }),
                    ["fish eye soup"] = new MealInfo(true, "fish.cooked", 2572718277, "The soup that stares back.", 600, 15, new Dictionary<Buff, float>() { [Buff.Water_Breathing] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["small trout"] = 1,
                        ["mushroom"] = 5,
                        ["spices"] = 1
                    }),
                    ["mushroom soup"] = new MealInfo(true, "fish.cooked", 2572739812, "I always said the only thing missing from soup was fungas.", 120, 15, new Dictionary<Buff, float>() { [Buff.Farming_Yield] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["spices"] = 1,
                        ["mushroom"] = 6,
                        ["milk"] = 1
                    }),
                    ["pancakes"] = new MealInfo(true, "fish.cooked", 2572718486, "Delicious syrup covered discs.", 60, 15, new Dictionary<Buff, float>() { [Buff.Spectre] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 2,
                        ["blue berry"] = 5,
                        ["egg"] = 1,
                        ["milk"] = 1
                    }),
                    ["pepperoni pizza"] = new MealInfo(true, "fish.cooked", 2572740655, "Enjoyed by the rich and poor alike.", 600, 15, new Dictionary<Buff, float>() { [Buff.Barrel_Smasher] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 1,
                        ["tomato"] = 2,
                        ["raw pork"] = 4,
                        ["cheese"] = 2
                    }),
                    ["vegetable pizza"] = new MealInfo(true, "fish.cooked", 2783544007, "Enjoyed by the rich and poor alike.", 60, 15, new Dictionary<Buff, float>() { [Buff.Crafting_Refund] = 0.2f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 1,
                        ["tomato"] = 2,
                        ["pumpkin"] = 6,
                        ["cheese"] = 2
                    }),
                    ["chicken & mushroom"] = new MealInfo(true, "fish.cooked", 2783548673, "Enjoyed by the rich and poor alike.", 60, 15, new Dictionary<Buff, float>() { [Buff.Passive_Regen] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 1,
                        ["raw chicken breast"] = 5,
                        ["cheese"] = 2,
                        ["mushroom"] = 5
                    }),
                    ["scrambled eggs"] = new MealInfo(true, "fish.cooked", 2572740525, "Delightlyfully fluffy.", 300, 15, new Dictionary<Buff, float>() { [Buff.Fall_Damage_resist] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["egg"] = 4,
                        ["milk"] = 1
                    }),
                    ["packed lunch"] = new MealInfo(true, "fish.cooked", 2575805059, "Your mum definitely packed this for you.", 600, 15, new Dictionary<Buff, float>() { [Buff.Condition_Loss_Reduction] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["bread loaf"] = 1,
                        ["potato"] = 2,
                        ["blue berry"] = 2,
                        ["egg"] = 2
                    }),
                    ["horse feed"] = new MealInfo(true, "fish.cooked", 2578938050, "Crack, but for horses.", 600, 15, new Dictionary<Buff, float>() { [Buff.Horse_Stats] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["pumpkin"] = 4,
                        ["potato"] = 4,
                        ["corn"] = 4,
                        ["mushroom"] = 5
                    }),
                    ["fried chicken"] = new MealInfo(true, "fish.cooked", 2783384817, "Crispy, delicious, tasty goodness.", 600, 15, new Dictionary<Buff, float>() { [Buff.Ingredient_Chance] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 1,
                        ["raw chicken breast"] = 6,
                        ["spices"] = 1,
                        ["milk"] = 1
                    }),
                    ["fish and chips"] = new MealInfo(true, "fish.cooked", 2572740021, "Who doesn't love this?", 120, 15, new Dictionary<Buff, float>() { [Buff.Upgrade_Refund] = 0.25f }, new Dictionary<string, int>()
                    {
                        ["raw fish"] = 5,
                        ["potato"] = 5,
                        ["spices"] = 1
                    }),
                    ["bear burger"] = new MealInfo(true, "fish.cooked", 2578829863, "Delicious bear between 2 heavenly buns.", 60, 15, new Dictionary<Buff, float>() { [Buff.Research_Refund] = 0.2f }, new Dictionary<string, int>()
                    {
                        ["bread loaf"] = 1,
                        ["cheese"] = 1,
                        ["spices"] = 1,
                        ["raw bear meat"] = 6
                    }),
                    ["chicken burger"] = new MealInfo(true, "fish.cooked", 2783705157, "Delicious chicken between 2 heavenly buns.", 600, 15, new Dictionary<Buff, float>() { [Buff.Woodcutting_Hotspot] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["bread loaf"] = 1,
                        ["cheese"] = 1,
                        ["spices"] = 1,
                        ["raw chicken breast"] = 6
                    }),
                    ["bangers and mash"] = new MealInfo(true, "fish.cooked", 2572717440, "This is Australian sausages and mashed potato.", 600, 15, new Dictionary<Buff, float>() { [Buff.Mining_Yield] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw pork"] = 6,
                        ["potato"] = 5,
                        ["spices"] = 1
                    }),
                    ["steak dinner"] = new MealInfo(true, "fish.cooked", 2578965785, "Winner winner...Steak dinner?", 600, 15, new Dictionary<Buff, float>() { [Buff.Bleed_Resist] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw bear meat"] = 5,
                        ["potato"] = 3,
                        ["spices"] = 1,
                        ["pumpkin"] = 2,
                    }),
                    ["grilled pork kebab"] = new MealInfo(true, "fish.cooked", 2783484494, "A few of these on a BBQ, thats a good evening...", 600, 15, new Dictionary<Buff, float>() { [Buff.Woodcutting_Yield] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw pork"] = 5,
                        ["wheat"] = 1,
                        ["cheese"] = 1,
                        ["spices"] = 2,
                    }),
                    ["spaghetti and meatballs"] = new MealInfo(true, "fish.cooked", 2575783557, "Just like momma used to make.", 60, 15, new Dictionary<Buff, float>() { [Buff.Max_Repair] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw bear meat"] = 5,
                        ["tomato"] = 3,
                        ["wheat"] = 2,
                        ["spices"] = 2,
                    }),
                    ["chicken dinner"] = new MealInfo(true, "fish.cooked", 2572718021, "Winner winner...", 600, 15, new Dictionary<Buff, float>() { [Buff.Loot_Pickup] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw chicken breast"] = 6,
                        ["potato"] = 4,
                        ["spices"] = 1,
                        ["pumpkin"] = 2
                    }),
                    ["roast chicken pie"] = new MealInfo(true, "fish.cooked", 2579560886, "I hope this is chicken.", 300, 15, new Dictionary<Buff, float>() { [Buff.Reviver] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 1,
                        ["raw chicken breast"] = 10,
                        ["spices"] = 1
                    }),
                    ["pork omelette"] = new MealInfo(true, "fish.cooked", 2572740170, "Pigs are the animal that just keep giving.", 120, 15, new Dictionary<Buff, float>() { [Buff.Duplicator] = 0.2f }, new Dictionary<string, int>()
                    {
                        ["egg"] = 3,
                        ["raw pork"] = 5,
                        ["spices"] = 1
                    }),
                    ["cannelloni"] = new MealInfo(true, "fish.cooked", 2656957947, "Will leave you feeling stuffed.", 300, 15, new Dictionary<Buff, float>() { [Buff.Smelt_On_Mine] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["tomato"] = 3,
                        ["wheat"] = 2,
                        ["spices"] = 1
                    }),
                    ["curry"] = new MealInfo(true, "fish.cooked", 2572741815, "Why does everything burn?", 300, 15, new Dictionary<Buff, float>() { [Buff.Fire_Resist] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw chicken breast"] = 5,
                        ["rice"] = 2,
                        ["spices"] = 2,
                        ["milk"] = 1
                    }),
                    ["chilli con carne"] = new MealInfo(true, "fish.cooked", 2578822603, "A borito without the blanket.", 600, 15, new Dictionary<Buff, float>() { [Buff.Component_Luck] = 0.2f }, new Dictionary<string, int>()
                    {
                        ["tomato"] = 3,
                        ["raw bear meat"] = 6,
                        ["spices"] = 1
                    }),
                    ["bear stew"] = new MealInfo(true, "fish.cooked", 2579612163, "This will put some hairs on your chest.", 600, 15, new Dictionary<Buff, float>() { [Buff.Mining_Hotspot] = 1f }, new Dictionary<string, int>()
                    {
                        ["pumpkin"] = 4,
                        ["potato"] = 4,
                        ["raw bear meat"] = 6,
                        ["spices"] = 1
                    }),
                    ["cottage pie"] = new MealInfo(true, "fish.cooked", 2783500694, "Enough food for the whole hosehold!", 60, 15, new Dictionary<Buff, float>() { [Buff.Wounded_Resist] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw wolf meat"] = 5,
                        ["wheat"] = 1,
                        ["spices"] = 1,
                        ["potato"] = 5
                    }),
                    ["chernobyl casserole"] = new MealInfo(true, "fish.cooked", 2762942424, "Will still feel hot, even when refigerated.", 600, 15, new Dictionary<Buff, float>() { [Buff.Radiation_Resist] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw wolf meat"] = 5,
                        ["potato"] = 5,
                        ["spices"] = 1,
                        ["anti-radiation pills"] = 2
                    }),
                    ["pumpkin pie"] = new MealInfo(true, "fish.cooked", 2572718601, "The best use of a pumpkin.", 0, 15, new Dictionary<Buff, float>() { [Buff.Heal] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["wheat"] = 1,
                        ["spices"] = 1,
                        ["pumpkin"] = 8,
                        ["sugar"] = 2
                    }),
                    ["pumpkin soup"] = new MealInfo(true, "fish.cooked", 2869355670, "Mmm smooth and creamy.", 60, 15, new Dictionary<Buff, float>() { [Buff.Food_Share] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["spices"] = 1,
                        ["pumpkin"] = 10,
                        ["milk"] = 1
                    }),
                    ["succulent chicken sandwhich"] = new MealInfo(true, "fish.cooked", 2920312153, "What's not to love about chicken on bread?", 600, 15, new Dictionary<Buff, float>() { [Buff.Skinning_Yield] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw chicken breast"] = 2,
                        ["tomato"] = 1,
                        ["bread loaf"] = 2,
                        ["cheese"] = 1
                    }),
                    ["beef stew"] = new MealInfo(true, "fish.cooked", 2579612163, "This doesn't taste like cow meat.", 0, 15, new Dictionary<Buff, float>() { [Buff.Extra_Calories] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw bear meat"] = 2,
                        ["tomato"] = 1,
                        ["potato"] = 1,
                        ["spices"] = 1
                    }),
                    ["ham and pineapple pizza"] = new MealInfo(true, "fish.cooked", 2783542587, "People who put pineapple on pizza, often don't make much sense when they speak.", 60, 15, new Dictionary<Buff, float>() { [Buff.Madness] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["raw pork"] = 5,
                        ["tomato"] = 2,
                        ["wheat"] = 1,
                        ["pineapple"] = 2
                    }),
                    ["pineapple fritta"] = new MealInfo(true, "fish.cooked", 2939225261, "Incredibly delicious and refreshing.", 0, 15, new Dictionary<Buff, float>() { [Buff.Extra_Hydration] = 1.0f }, new Dictionary<string, int>()
                    {
                        ["spices"] = 1,
                        ["wheat"] = 1,
                        ["egg"] = 1,
                        ["pineapple"] = 2
                    }),
                    ["gnocchi neapolitan"] = new MealInfo(true, "fish.cooked", 2946329136, "Potatoes, is there anything they can't do?", 600, 15, new Dictionary<Buff, float>() { [Buff.Max_Health] = 0.3f }, new Dictionary<string, int>()
                    {
                        ["spices"] = 1,
                        ["tomato"] = 3,
                        ["wheat"] = 1,
                        ["potato"] = 5
                    }),
                    ["chocolate calzone"] = new MealInfo(true, "fish.cooked", 2946336615, "Is it a pizza or a pastry?", 60, 15, new Dictionary<Buff, float>() { [Buff.Anti_Bradley_Radar] = 1f }, new Dictionary<string, int>()
                    {
                        ["chocolate bar"] = 10,
                        ["sugar"] = 1,
                        ["wheat"] = 2,
                        ["spices"] = 1
                    }),
                    ["baked trout"] = new MealInfo(true, "fish.cooked", 3014297392, "I guess this one didn't make it down the river.", 600, 15, new Dictionary<Buff, float>() { [Buff.Fishing_Yield] = 1f }, new Dictionary<string, int>()
                    {
                        ["small trout"] = 3,
                        ["tomato"] = 1,
                        ["seaweed"] = 1,
                        ["spices"] = 1
                    }),
                    ["steak with red wine jus"] = new MealInfo(true, "fish.cooked", 3383399330, "The perfect steak with the perfect sauce.", 60, 15, new Dictionary<Buff, float>() { [Buff.Lifelink] = 0.1f }, new Dictionary<string, int>()
                    {
                        ["small trout"] = 3,
                        ["tomato"] = 1,
                        ["seaweed"] = 1,
                        ["spices"] = 1
                    }),
                };
            }
        }

        Dictionary<string, IngredientsInfo> DefaultBasicIngredients
        {
            get
            {
                return new Dictionary<string, IngredientsInfo>()
                {
                    ["spices"] = new IngredientsInfo(true, "apple", 2869351030, new List<GatherSource>() { GatherSource.Hemp }),
                    ["sugar"] = new IngredientsInfo(true, "sulfur", 2783266385, new List<GatherSource>() { GatherSource.Potato }),
                    ["milk"] = new IngredientsInfo(true, "apple", 2572181796, new List<GatherSource>() { GatherSource.Bear, GatherSource.Boar, GatherSource.Deer, GatherSource.Wolf, GatherSource.PolarBear, GatherSource.Panther, GatherSource.Tiger }),
                    ["seaweed"] = new IngredientsInfo(true, "apple", 2572183145, new List<GatherSource>() { GatherSource.Gut, GatherSource.Fishing, GatherSource.Shark }),
                    ["rice"] = new IngredientsInfo(true, "apple", 2572182801, new List<GatherSource>() { GatherSource.Corn, GatherSource.Pumpkin }),
                    ["tomato"] = new IngredientsInfo(true, "apple", 2572183535, new List<GatherSource>() { GatherSource.Pumpkin }),
                    ["cheese"] = new IngredientsInfo(true, "apple", 2572178709, new List<GatherSource>() { GatherSource.Crafted }),
                    ["pineapple"] = new IngredientsInfo(true, "apple", 2783245697, new List<GatherSource>() { GatherSource.DesertTree }),
                };
            }
        }

        #endregion

        #region Gather sources

        // 1.0 = always drops.
        Dictionary<GatherSource, GatherInfo> DefaultGatherSources()
        {
            List<GatherSource> sources = Pool.Get<List<GatherSource>>();
            sources.AddRange(Enum.GetValues(typeof(Buff)).Cast<GatherSource>());

            Dictionary<GatherSource, GatherInfo> result = new Dictionary<GatherSource, GatherInfo>();

            foreach (var source in sources)
            {
                if (source.ToString().IsNumeric()) continue;
                switch (source)
                {
                    case GatherSource.Hemp:
                    case GatherSource.Potato:
                    case GatherSource.Fishing:
                        result.Add(source, new GatherInfo(0.1f));
                        break;

                    case GatherSource.Mushroom:
                        result.Add(source, new GatherInfo(0.075f));
                        break;

                    default: 
                        result.Add(source, new GatherInfo(0.05f));
                        break;
                }
            }
            Pool.FreeUnmanaged(ref sources);

            return result;
        }

        #endregion

        #region Default Loot tables

        Dictionary<string, float> DefaultMealIngredientLootChances
        {
            get
            {
                return new Dictionary<string, float>()
                {
                    ["crate_normal_2_food"] = 1f,
                    ["wagon_crate_normal_2_food"] = 1f,
                    ["crate_food_1"] = 2f,
                    ["crate_food_2"] = 2f,
                };
            }
        }

        List<string> DefaultLuckLootTable
        {
            get
            {
                return new List<string>()
                {
                    "keycard_blue",
                    "keycard_red",
                    "keycard_green",
                    "ammo.shotgun",
                    "ammo.shotgun.slug",
                    "ammo.rifle",
                    "scraptea",
                    "scraptea.advanced",
                    "scraptea.pure",
                    "radiationresisttea.advanced",
                    "healingtea.advanced",
                    "maxhealthtea.advanced",
                    "healingtea",
                    "maxhealthtea",
                    "oretea",
                    "woodtea"
                };
            }
        }

        #endregion

        #region Market NPC stuff

        List<NPCSettings.NPCItems> DefaultNPCWornItems
        {
            get
            {
                return new List<NPCSettings.NPCItems>()
                {
                    new NPCSettings.NPCItems("burlap.shirt", 2040707769),
                    new NPCSettings.NPCItems("burlap.trousers", 2040706598),
                    new NPCSettings.NPCItems("hat.boonie", 2040709757),
                };
            }
        }

        NPCSettings.NPCItems DefaultNPCHeldItem
        {
            get
            {
                return new NPCSettings.NPCItems("pitchfork", 0);
            }
        }

        #endregion

        #endregion

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    Puts($"Config is null. Loading default config.");
                    LoadDefaultConfig();
                }
                ApplyCookingDataDirectory();
                SaveConfig();
            }
            catch (Exception ex)
            {
                Puts($"Failed to load config. - {ex.Message} - {ex.InnerException}");
                UnityEngine.Debug.LogException(ex);
                Interface.Oxide.UnloadPlugin(Name);
            }
        }

        protected override void SaveConfig()
        {
            PrintToConsole($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        const string SharedCookingDataDir = @"C:\!DataPersistence\oxide\data\Cooking";

        void ApplyCookingDataDirectory()
        {
            try
            {
                string dir;
                if (config != null && !string.IsNullOrWhiteSpace(config.CustomCookingDataDirectory))
                {
                    dir = Path.GetFullPath(config.CustomCookingDataDirectory.Trim());
                }
                else if (File.Exists(Path.Combine(SharedCookingDataDir, "Cooking.json")))
                {
                    dir = SharedCookingDataDir;
                    if (config != null) config.CustomCookingDataDirectory = SharedCookingDataDir;
                }
                else
                {
                    dir = Path.Combine(Interface.Oxide.RootDirectory, "HarmonyData", "Cooking");
                }

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                Interface.Oxide.CustomDataDirectory = dir;
                Interface.Oxide.ResetDataFileSystem();
            }
            catch (Exception ex)
            {
                Puts($"ApplyCookingDataDirectory failed: {ex.Message}");
            }
        }

        #endregion

        #region Data

        [PluginReference]
        private Plugin ServerRewards, Economics, RandomTrader, SkillTree, CustomItemVending;

        PlayerEntity pcdData;
        private DynamicConfigFile PCDDATA;

        // Recipe menu chat command
        const string perm_recipemenu_chat = "cooking.recipemenu.chat";

        // Using the cooking menu
        const string perm_use = "cooking.use";

        // Admin related cooking comands
        const string perm_admin = "cooking.admin";

        // Bypass cooking time
        const string perm_instant = "cooking.instant";

        // Bypass ingredient requirements
        const string perm_free = "cooking.free";

        // Removes ability to gather ingredients
        const string perm_nogather = "cooking.nogather";

        // Allows the ingredient bag to be opened via CMD
        const string perm_bag_cmd = "cooking.bag.cmd";

        // Disables drop notifications
        const string perm_disable_notify_drop = "cooking.disable.notify.drop";

        // Disables Notify notifications
        const string perm_disable_notify_proc = "cooking.disable.notify.proc";

        // Disables menu sounds
        const string perm_disable_sound = "cooking.disable.sound";

        // Allows access to the farmers market via CMD
        const string perm_market_cmd = "cooking.market.cmd";

        // Allows the user to speak to the market NPC
        const string perm_market_npc = "cooking.market.npc";

        // Allows the player to gather ingredients
        const string perm_gather = "cooking.gather";

        // Required for players to find recipe cards in crates
        const string perm_recipecards = "cooking.recipecards";


        void Init()
        {
            Instance = this;
            Unsubscribe(nameof(OnPluginLoaded));
            PCDDATA = Interface.Oxide.DataFileSystem.GetFile(this.Name);
            LoadData();

            permission.RegisterPermission(perm_recipemenu_chat, this);
            permission.RegisterPermission(perm_use, this);
            permission.RegisterPermission(perm_admin, this);
            permission.RegisterPermission(perm_bag_cmd, this);
            permission.RegisterPermission(perm_instant, this);
            permission.RegisterPermission(perm_free, this);
            permission.RegisterPermission(perm_nogather, this);
            permission.RegisterPermission(perm_disable_notify_drop, this);
            permission.RegisterPermission(perm_disable_notify_proc, this);
            permission.RegisterPermission(perm_disable_sound, this);
            permission.RegisterPermission(perm_market_cmd, this);
            permission.RegisterPermission(perm_market_npc, this);
            permission.RegisterPermission(perm_gather, this);
            permission.RegisterPermission(perm_recipecards, this);      
            if (!string.IsNullOrEmpty(config.generalSettings.grown_permission))
            {
                var perm_string = config.generalSettings.grown_permission.StartsWith("cooking.", StringComparison.OrdinalIgnoreCase) ? config.generalSettings.grown_permission : "cooking." + config.generalSettings.grown_permission;
                permission.RegisterPermission(perm_string, this);
            }
            if (config.mealSettings.cookbookSettings.enabled)
            {
                foreach (var kvp in config.mealSettings.cookbookSettings.recipeDropChancePerms)
                {
                    var perm = kvp.Key.StartsWith("cooking.") ? kvp.Key : "cooking." + kvp.Key;
                    if (!permission.UserExists(perm)) permission.RegisterPermission(perm, this);
                    RecipeDropPerms[perm] = kvp.Value;
                }
            }

            RegisterCommands();
        }

        Dictionary<string, float> RecipeDropPerms = new Dictionary<string, float>();

        void RegisterCommands()
        {
            cmd.AddChatCommand(config.generalSettings.marketSettings.marketCMD, this, nameof(SendMarketCMD));
            cmd.AddConsoleCommand(config.generalSettings.marketSettings.marketCMD, this, nameof(SendMarketConsoleCMD));
            foreach (var c in config.generalSettings.recipeMenuCMDs)
            {
                cmd.AddChatCommand(c, this, nameof(SendCookMenuCMD));
                cmd.AddConsoleCommand(c, this, nameof(SendCookMenuConsoleCMD));
            }
            cmd.AddChatCommand(config.generalSettings.ingredientBagSettings.iBagCMD, this, nameof(OpenIngredientBagCommand));
            cmd.AddConsoleCommand(config.generalSettings.ingredientBagSettings.iBagCMD, this, nameof(OpenIngredientBagConsoleCommand));
        }

        void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "CookingQueue");
            CuiHelper.DestroyUi(player, "UIMoverDemoPanel");
            CuiHelper.DestroyUi(player, "UIMover");
            CuiHelper.DestroyUi(player, "UIMoverBackPanel");
            CuiHelper.DestroyUi(player, "HudButtons");
            CuiHelper.DestroyUi(player, "MealInfoPanel");
        }

        void ClearMealCooldowns()
        {
            if (MealCooldowns == null) return;
            foreach (var kvp in MealCooldowns)
                kvp.Value?.Clear();

            MealCooldowns?.Clear();
            MealCooldowns = null;
        }

        void KillIBags()
        {
            foreach (var container in IBagContainers)
            {
                if (container.Value != null)
                    container.Value.Invoke(container.Value.KillMessage, 0.01f);
            }
        }

        void RemoveCommands()
        {
            cmd.RemoveChatCommand(config.generalSettings.marketSettings.marketCMD, this);
            cmd.RemoveConsoleCommand(config.generalSettings.marketSettings.marketCMD, this);
            foreach (var c in config.generalSettings.recipeMenuCMDs)
            {
                cmd.RemoveChatCommand(c, this);
                cmd.RemoveConsoleCommand(c, this);
            }
            cmd.RemoveChatCommand(config.generalSettings.ingredientBagSettings.iBagCMD, this);
            cmd.RemoveConsoleCommand(config.generalSettings.ingredientBagSettings.iBagCMD, this);
        }

        void RemoveMarketNPCs()
        {
            foreach (var marker in BaseNetworkable.serverEntities.OfType<MapMarkerMissionProvider>())
            {
                if (marker == null || marker.IsDestroyed) continue;
                foreach (var npc in MarketNPCs)
                    if (npc.Value != null && InRange(npc.Value.transform.position, marker.transform.position, 0.1f))
                        marker.Kill();
            }

            foreach (var npc in MarketNPCs)
            {
                if (npc.Value != null && !npc.Value.IsDestroyed)
                    npc.Value.Kill();
            }
        }

        void Unload()
        {
            SaveData();
            if (ConfigRequiresSave) SaveConfig();
            foreach (var player in BasePlayer.activePlayerList)
            {
                player.EndLooting();
                DestroyBuffManager(player.userID);
                DestroyRecipeMenuUI(player, true);
                DestroyCookingManager(player.userID);
                DestroyUI(player);
                CuiHelper.DestroyUi(player, "BuffUI");
                try { DeleteCachedRecipe(player); } catch { }
                LoadedMeals?.Clear();
                LoadedMeals = null;
            }

            foreach (var horse in ModifiedHorses)
                RestoreModifiedHorse(horse.Key, false);

            ClearMealCooldowns();            
            DefaultDisplayNames?.Clear();
            DefaultDisplayNames = null;
            KillIBags();
            RemoveCommands();
            RemoveMarketNPCs();
            ClearLists();
            RemoveCurrencies();
        }

        void ClearLists()
        {
            MealCooldowns?.Clear();
            IngredientSources?.Clear();
            DefaultDisplayNames?.Clear();
            ItemID?.Clear();
            item_BPs?.Clear();
            ItemDefs?.Clear();
            DropWeightReference?.Clear();
            LastMagnetSuccess?.Clear();
            BuffManagerDirectory?.Clear();
            CookingManagerDirectory?.Clear();
            Ingredients?.Clear();
            QuantityFieldInputText?.Clear();
            UIMovers?.Clear();
            Subscriptions?.Clear();
            IsSubscribed?.Clear();
            PlayersSubscribed?.Clear();

            looted_containers?.Clear();
            component_item_list?.Clear();
            electrical_item_list?.Clear();
            SettingsButtons?.Clear();
            OvenUsers?.Clear();
            IBagContainers?.Clear();
            ModifiedHorses?.Clear();
            MarketIngredients?.Clear();
            MarketNPCs?.Clear();
            LootedContainers?.Clear();
            CachedRecipes?.Clear();
            Researchers?.Clear();
            RecipeDropPerms?.Clear();
        }

        void SaveData()
        {
            PCDDATA.WriteObject(pcdData);
        }

        void LoadData()
        {
            try
            {
                pcdData = Interface.Oxide.DataFileSystem.ReadObject<PlayerEntity>(this.Name);
            }
            catch
            {
                Puts("Couldn't load player data, creating new Playerfile");
                pcdData = new PlayerEntity();
            }
        }

        class PlayerEntity
        {
            public bool patched = false;
            public bool patched2 = false;
            public Dictionary<ulong, PCDInfo> pEntity = new Dictionary<ulong, PCDInfo>();
            public Dictionary<ulong, List<BagInfo>> bag = new Dictionary<ulong, List<BagInfo>>();
            public List<NPCInfo> npcs = new List<NPCInfo>();
        }

        public class NPCInfo
        {
            public Vector3 position;
            public Vector3 rotation;

            public NPCInfo(Vector3 position, Vector3 rotation)
            {
                this.position = position;
                this.rotation = rotation;
            }
        }

        class BagInfo
        {
            public string shortname;
            public string displayName;
            public ulong skin;
            public string text;
            public int amount;
            public int slot;
            public float spoilTime;
            public BagInfo(string shortname, string displayName, int amount, int slot, ulong skin, float spoilTime, string text)
            {
                this.shortname = shortname;
                this.displayName = displayName;
                this.amount = amount;
                this.slot = slot;
                this.skin = skin;
                this.spoilTime = spoilTime;
            }
        }

        class PCDInfo
        {
            public List<string> favouriteDishes = new List<string>();
            public List<string> learntRecipes = new List<string>();
        }

        [ConsoleCommand("spit")]
        void Spit(ConsoleSystem.Arg arg)
        {
            List<GatherSource> gatherSource = Pool.Get<List<GatherSource>>();
            gatherSource.AddRange(Enum.GetValues(typeof(GatherSource)).Cast<GatherSource>());

            foreach (var buff in gatherSource)
            {
                Puts($"{buff} - {lang.GetMessage($"{buff}_LocationText", this)}");
            }

            Pool.FreeUnmanaged(ref gatherSource);
        }

        #endregion;

        #region Localization

        protected override void LoadDefaultMessages()
        {
            // Do manual lang here
            Dictionary<string, string> langEntries = new Dictionary<string, string>()
            {
                ["Default_LocationText"] = "Default Rust item",
                ["AnyTree_LocationText"] = "Chopping any tree",
                ["ArcticTree_LocationText"] = "Chopping arctic trees",
                ["DesertTree_LocationText"] = "Chopping palm trees",
                ["TemperateTree_LocationText"] = "Chopping temperate trees",
                ["TundraTree_LocationText"] = "Chopping tundra trees",
                ["JungleTree_LocationText"] = "Chopping jungle trees",
                ["Cactus_LocationText"] = "Chopping cacti",
                ["AnyNode_LocationText"] = "Mining any node",
                ["AnyArcticNode_LocationText"] = "Mining any arctic nodes",
                ["AnyDesertNode_LocationText"] = "Mining any desert nodes",
                ["AnyTemperateNode_LocationText"] = "Mining any temperate nodes",
                ["AnyTundraNode_LocationText"] = "Mining any tundra nodes",
                ["AnyJungleNode_LocationText"] = "Mining any jungle nodes",
                ["StoneNode_LocationText"] = "Mining stone nodes",
                ["MetalNode_LocationText"] = "Mining metal nodes",
                ["SulfurNode_LocationText"] = "Mining sulfur nodes",
                ["AnyAnimal_LocationText"] = "Skinning any animal",
                ["Deer_LocationText"] = "Skinning deer",
                ["Bear_LocationText"] = "Skinning bears",
                ["Wolf_LocationText"] = "Skinning wolves",
                ["Chicken_LocationText"] = "Skinning chickens",
                ["PolarBear_LocationText"] = "Skinning polar bears",
                ["Shark_LocationText"] = "Skinning sharks",

                ["Snake_LocationText"] = "Skinning snakes",
                ["Panther_LocationText"] = "Skinning panthers",
                ["Tiger_LocationText"] = "Skinning tigers",
                ["Crocodile_LocationText"] = "Skinning crocodiles",

                ["Boar_LocationText"] = "Skinning boar",
                ["horse_LocationText"] = "Skinning horses",
                ["Fishing_LocationText"] = "Catching fish",
                ["Gut_LocationText"] = "Gutting fish",
                ["Pumpkin_LocationText"] = "Harvesting pumpkins",
                ["Potato_LocationText"] = "Harvesting potatos",
                ["Corn_LocationText"] = "Harvesting corn",
                ["Mushroom_LocationText"] = "Picking mushrooms",
                ["BerryBush_LocationText"] = "Picking berries",
                ["Hemp_LocationText"] = "Harvesting hemp",
                ["Crafted_LocationText"] = "Crafted",
                ["Foodbox_LocationText"] = "Food boxes",

                ["VehicleCrate_LocationText"] = "Vehicle crates",
                ["ToolCrate_LocationText"] = "Tool crates",
                ["MedicalCrate_LocationText"] = "Medical crates",
                ["AmmunitionCrate_LocationText"] = "Ammunition crates",
                ["FuelCrate_LocationText"] = "Fuel crates",

                ["CollectableSulfur_LocationText"] = "Collectable sulfur nodes",
                ["CollectableStone_LocationText"] = "Collectable stone nodes",
                ["CollectableMetal_LocationText"] = "Collectable metal nodes",
                ["Excavated_LocationText"] = "Excavated",

                ["Ingredient_MenuDescription"] = "A useful ingredient used to make more complex meals.",
                ["Woodcutting_Yield_MenuDescription"] = "Increases the amount of wood received by <color=#42f105>{0}%</color> when cutting trees and logs.",
                ["Mining_Yield_MenuDescription"] = "Increases the amount of ore received by <color=#42f105>{0}%</color> when mining any ore type.",
                ["Skinning_Yield_MenuDescription"] = "Increases the amount of animal products received by <color=#42f105>{0}%</color> when skinning animals.",
                ["Heal_Share_MenuDescription"] = "Heals those around you for <color=#42f105>{0}%</color> of the healing you receive.",
                ["Heal_MenuDescription"] = "Instantly heals you for <color=#42f105>{0}%</color> of your maximum health.",
                ["Food_Share_MenuDescription"] = "Shares your food with nearby players, providing them with <color=#42f105>{0}%</color> of the calories that you consume.",
                ["Metabolism_Overload_MenuDescription"] = "Increases your maximum calories and hydration capacity by <color=#42f105>{0}%</color>.",
                ["Comfort_MenuDescription"] = "Provides an aura of comfort around you. Each nearby player will receive <color=#42f105>{0}%</color> comfort.",
                ["Water_Breathing_MenuDescription"] = "Will allow you to breath underwater for the duration.",
                ["Fire_Resist_MenuDescription"] = "Reduces the damage taken from all sources of fire/heat by <color=#42f105>{0}%</color>.",
                ["Cold_Resist_MenuDescription"] = "Reduces the damage taken from the cold by <color=#42f105>{0}%</color>.",
                ["Explosion_Resist_MenuDescription"] = "Reduces the damage taken from explosives by <color=#42f105>{0}%</color>.",
                ["Animal_Resist_MenuDescription"] = "Reduces the damage taken from animals by <color=#42f105>{0}%</color>.",
                ["Melee_Resist_MenuDescription"] = "Reduces the damage taken from attacks made with a melee weapon by <color=#42f105>{0}%</color>.",
                ["Wounded_Resist_MenuDescription"] = "If you would enter the wounded state while this buff is active, you will instead be brought to your feet. Any negative modifiers will be removed. Your health will be set to <color=#42f105>{0}hp</color>.",
                ["Spectre_MenuDescription"] = "You will become invisible to auto-turrets, flame turrets and shotgun traps for the duration.",
                ["Madness_MenuDescription"] = "This food will make you sound strange to others.",
                ["Wealth_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to find {1} when breaking barrels.",
                ["CurrencyString"] = "additional scrap",
                ["Barrel_Smasher_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to instantly break a barrel with any amount of damage.",
                ["Crafting_Refund_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to refund components when crafting an item.",
                ["Passive_Regen_MenuDescription"] = "Will passively regenerate <color=#42f105>{0}</color> health each second.",
                ["Damage_Over_Time_MenuDescription"] = "Will damage you for <color=#42f105>{0}</color> health each second.",
                ["Horse_Stats_MenuDescription"] = "Will increase the speed any horse you ride by <color=#42f105>{0}%</color>.",
                ["Fall_Damage_resist_MenuDescription"] = "Reduces damage taken from falling by <color=#42f105>{0}%</color>.",
                ["Condition_Loss_Reduction_MenuDescription"] = "Reduces the condition loss of all worn and held items by <color=#42f105>{0}%</color>.",
                ["Ingredient_Chance_MenuDescription"] = "Increases the chance to obtain cooking ingredients by <color=#42f105>{0}%</color>.",
                ["Upgrade_Refund_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to receive a free upgrade when upgrading your building blocks.",
                ["Research_Refund_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to receive a scrap refund when using a research bench.",
                ["Role_Play_MenuDescription"] = "This item provides no buffs as it a Roleplay item.",
                ["Fishing_Luck_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to obtain a random item while fishing.",
                ["Farming_Yield_MenuDescription"] = "Increases the amount of resources collected by <color=#42f105>{0}%</color> when harvesting player-grown plants.",
                ["Component_Luck_MenuDescription"] = "Provides a <color=#42f105>{0}%</color> chance to receive a random component when breaking barrels.",
                ["Electronics_Luck_MenuDescription"] = "Provides a <color=#42f105>{0}%</color> chance to receive a random electrical item when breaking barrels.",
                ["Bleed_Resist_MenuDescription"] = "Reduces the damage taken from bleeding by <color=#42f105>{0}%</color>.",
                ["Radiation_Resist_MenuDescription"] = "Reduces the damage taken from radiation by <color=#42f105>{0}%</color>.",
                ["Max_Repair_MenuDescription"] = "Any item that is repaired while this buff is active, will have its maximum condition reset.",
                ["Smelt_On_Mine_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to receive refined resources instead of ores, when mining sulfur and metal nodes.",
                ["Loot_Pickup_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance for all items to be moved directly into your inventory when breaking barrels.",
                ["Reviver_MenuDescription"] = "Sets a players health to <color=#42f105>{0}%</color> when bringing them up from the wounded state.",
                ["Duplicator_MenuDescription"] = "Provides you with a <color=#42f105>{0}%</color> chance to duplicate an item when crafting.",
                ["Harvest_MenuDescription"] = "Increases the amount of resources collected by <color=#42f105>{0}%</color> when harvesting wild entities.",
                ["Max_Health_MenuDescription"] = "Increases your maximum health by <color=#42f105>{0}%</color>.",
                ["Anti_Bradley_Radar_MenuDescription"] = "Makes you invisible to the Bradley APC.",
                ["Ingredient_Storage_MenuDescription"] = "Stores ingredients.",
                ["Extra_Calories_MenuDescription"] = "Instantly provides you with <color=#42f105>{0}%</color> of your maximum calories when consumed.",
                ["Hunger_MenuDescription"] = "Reduces your calories by <color=#42f105>{0}%</color> of your maximum when consumed.",
                ["Extra_Hydration_MenuDescription"] = "Instantly provides you with <color=#42f105>{0}%</color> of your maximum hydration when consumed.",
                ["Dehydration_MenuDescription"] = "Reduces your hydration by <color=#42f105>{0}%</color> of your maximum when consumed.",
                ["Damage_MenuDescription"] = "Damages you for <color=#42f105>{0} hp</color> when consumed.",
                ["Radiation_MenuDescription"] = "Irradiates you with <color=#42f105>{0} points</color> of radiation when consumed.",
                ["Fishing_Yield_MenuDescription"] = "Increases the amount of fish caught by <color=#42f105>{0}%</color> while fishing.",
                ["Mining_Hotspot_MenuDescription"] = "You will always hit the hot spot of a node while mining.",
                ["Woodcutting_Hotspot_MenuDescription"] = "You will always hit the marker of a tree while chopping wood.",
                ["Lifelink_MenuDescription"] = "You will heal for by <color=#42f105>{0}%</color> of the base damage done to humans and animals.",
                ["SkillTreeXp_MenuDescription"] = "Increases the amount of xp gained from all sources by <color=#42f105>{0}%</color>.",
                //["_MenuDescription"] = "<color=#42f105>{4221}%</color>.",

                ["UIOn"] = "<color=#00ab08>ON</color>",
                ["UIOff"] = "<color=#bf2405>OFF</color>",
                ["ToggleNotificationDrops_Description"] = "Toggle notifications for ingredient drops?",
                ["ToggleNotificationTriggers_Description"] = "Toggle notifications for buff triggers?",
                ["ToggleSound_Description"] = "Toggle cooking menu sound effects.",
                ["ToggleDrops_Description"] = "Toggle ingredients drops.",
                ["AdjustUI_BuffIcons_Description"] = "Reposition the buff icons globally.",
                ["AdjustUI_CraftQueue_Description"] = "Reposition the cooking queue icon globally.",
                ["AdjustUI_CookingButton_Description"] = "Reposition the oven cooking button globally",
                ["AdjustUI_UIButtons_Description"] = "Reposition the player UI buttons globally",
                ["IngredientFoundMessage"] = "You found {0}x <color=#fe9e44>{1}</color>.",
                ["S"] = "s",
                ["A"] = "a",
                ["FishingLuckMessage"] = "You managed to find {0}x <color=#fe9e44>{1}</color> in the fishes mouth.",
                ["BuffToolTip"] = "Active buffs: {0}",
                ["BagMayBeFull"] = "Failed to store item in the container. Might be full.",
                ["MealOnCooldown"] = "This meal is still on cooldown for {0} seconds.",
                ["RecipeAlreadyKnown"] = "You already know this recipe.",
                ["NoRecipePerms"] = "You lack the capability of learning this recipe.",
                ["NewRecipeLearnt"] = "You learnt a new recipe: <color=#ffae00>{0}</color>",
                ["MaxActiveMeals"] = "You feel too full to eat.",
                ["PermPrevented"] = "You feel like now is a bad time to eat this particular dish.",
                ["MaxHealthIncreased"] = "Your max health has been increased by {0}.",
                ["RegenPaused"] = "Your regeneration has been paused for {0} seconds.",
                ["FoundNewRecipe"] = "You found a recipe for <color=#ffae00>{0}</color>.",
                ["ShowMealBuffDescriptions"] = "Buffs from the meal that this recipe teaches:",
                ["FoundNewRecipeAppendContainerFull"] = " It was moved to your inventory as the container was full.",
                ["ItemStoredInBag"] = "{0}x {1} were stored in your ingredient bag.",
                ["ItemStoredInBagNewSlot"] = "{0}x {1} were stored in a new slot in your ingredient bag.",
                ["ItemReturnedBlackListed"] = "{0}x {1} was returned to you as it cannot go inside of the {2}",
                ["BagChatNoPerms"] = "You do not have permission to open the ingredient bag with this command.",
                ["BagStillOpen"] = "You are still looting your ingredient bag. Please close it first.",
                ["MarketNPCNoPerms"] = "You do not have permission to talk to me!",
                ["NoRecipesKnown"] = "You do not know any recipes.",
                ["NoIngredients"] = "You do not have enough ingredients.",
                ["UIPosUpdated"] = "Successfully updated position for {0}.",
                ["UIPosFailed"] = "Failed to save for some reason.",
                ["MarketNoStock"] = "No stock available.",
                ["MarketCannotAfford"] = "You cannot afford {0}x {1}",
                ["MarketPurchaseBug"] = "Something went wrong with the purchase.",
                ["MarketPurchaseSuccess"] = "You purchased {0} units of {1}",
                ["MarketStockFull"] = "The shop does not want to buy anymore {0}.",
                ["MarketNoItemToSell"] = "You do not have any {0} to sell.",
                ["MarketSellBug"] = "Something went wrong when selling the ingredients.",
                ["MarketCashBug"] = "Something went wrong with depositing your money.",
                ["MarketCashSuccess"] = "You received ${0} for your produce.",
                ["MarketPointsBug"] = "Something went wrong when depositing your points.",
                ["MarketPointSuccess"] = "You received {0} points for your produce.",
                ["MarketScrapSuccess"] = "You received {0} scrap for your produce.",
                ["MealInfoNoMeals"] = "You do not have any cooked meals in your inventory.",
                ["RecipesTitle"] = "RECIPES",
                ["IngredientsTitle"] = "INGREDIENTS",
                ["FavouritesTitle"] = "FAVOURITES",
                ["SettingsTitle"] = "SETTINGS",
                ["CloseTitle"] = "CLOSE",
                ["UIBenefits"] = "Benefits",
                ["UIPrevious"] = "Previous",
                ["UINext"] = "NEXT",
                ["UIQuantity"] = "QUANTITY",
                ["UICraft"] = "CRAFT",
                ["UIUnlearnt"] = "UNLEARNT",
                ["UIBack"] = "BACK",
                ["UIGatheredFrom"] = "Gathered From:",
                ["UIDash"] = "<color=#03a7e5>-</color>",
                ["UIMoveButton"] = "<color=#fbaf00>MOVE</color>",
                ["RecipeMenuTitle"] = "RECIPE MENU",
                ["UISave"] = "SAVE",
                ["UICancel"] = "CANCEL",
                ["UILeft"] = "L",
                ["UIRight"] = "R",
                ["UIUp"] = "U",
                ["UIDown"] = "D",
                ["UIUI"] = "UI",
                ["UIFarmersMarket"] = "FARMERS MARKET",
                ["UIBuy1"] = "BUY 1",
                ["UIBuy10"] = "BUY 10",
                ["UIBuy100"] = "BUY 100",
                ["UISell1"] = "SELL 1",
                ["UISell10"] = "SELL 10",
                ["UISell100"] = "SELL 100",
                ["UIMealInfoTitle"] = "MEALS IN INVENTORY",
                ["UILeftArrow"] = "<<",
                ["UIRightArrow"] = ">>",
                ["UIIngredientCount"] = "{0}{1}/{2}{3}",
                ["UISendIngredientText"] = "<color=#07a99d>Ingredient:</color> {0}. <color=#07a99d>Source:</color> {1}",
                ["UICookQueueTime"] = "{0}",
                ["UICookQueueLength"] = "+{0}",
                ["UIIngredientMenuIngredient"] = "<color=#0789a9>{0}</color>",
                ["UIBuffTimeLeft"] = "{0}",
                ["UIMarketSellText"] = "Sell price: <color=#FFFF00>{0}</color>",
                ["UIMarketBuyText"] = "Buy price: <color=#FFFF00>{0}</color>",
                ["UIMarketNA"] = "<color=#FF0000>NA</color>",
                ["MealsHudButton"] = "Meals",
                ["iBagHudButton"] = "iBag",
                ["CookHudButton"] = "Cook",
                ["MarketHudButton"] = "Market",
                ["NoPermToCook"] = "You do not have the required permission to cook {0}",
                ["UIYes"] = "<color=#42f105>Yes</color>",
                ["UINo"] = "<color=#bb1305>No</color>",
                ["EatMealMessageNew"] = "You eat the delicious <color=#DFF008>{0}</color> and receive the following buff{1}:",
                ["UIAvailableAbove0"] = "Available: <color=#FFFF00>{0}</color>",
                ["UIAvailable0orLess"] = "Available: <color=#FF0000>{0}</color>",
                ["UIDuration"] = "<color=#03a7e5>Duration:</color> {0} seconds.",
                ["UICookTime"] = "<color=#03a7e5>Cook time:</color> {0} seconds.",
                ["UICooldown"] = "<color=#03a7e5>Cooldown:</color> {0} seconds.",
                ["UICanCook"] = "<color=#03a7e5>Can cook:</color> {0}.",
                ["SaveWounded"] = "Your meal saves you from entering the wounded state, removed all negative buffs, and set your health to <color=#42f105>{0}hp</color>.",
                ["Fishing_Yield_Proc_Msg"] = "You found an additional {0} {1} thanks to your buff.",
                ["SurvivalArenaBlacklistMeal"] = "You cannot join this event while the {0} meal is active.\nBlack listed meals: {1}",
                ["BuffPropertyPercentage"] = "<color=#62f30a>{0}%</color>",
                ["MaxQueueSize"] = "Your cooking queue is full."
            };

            List<Buff> buffs = Pool.Get<List<Buff>>();
            buffs.AddRange(Enum.GetValues(typeof(Buff)).Cast<Buff>());
            
            foreach (var buff in buffs)
            {
                langEntries[$"{buff}_ToText"] = buff.ToString().Replace('_', ' ');
            }

            foreach (var meal in DefaultBasicMeals)
                langEntries[meal.Key] = meal.Key.TitleCase();

            foreach (var meal in config.mealSettings.meals)
            {
                langEntries[meal.Key] = meal.Key.TitleCase();
                if (meal.Value.commandsOnConsume != null)
                    foreach (var c in meal.Value.commandsOnConsume)
                        langEntries[c.Value] = c.Value.TitleCase();
            }

            foreach (var ingredient in DefaultBasicIngredients)
                if (!langEntries.ContainsKey(ingredient.Key)) langEntries[ingredient.Key] = ingredient.Key.TitleCase();

            foreach (var ingredient in config.ingredientSettings.ingredients)
            {
                langEntries[ingredient.Key] = ingredient.Key.TitleCase();
            }

            Pool.FreeUnmanaged(ref buffs);

            lang.RegisterMessages(langEntries, this);
        }

        #endregion

        #region class

        // ulong = ID. string = meal. float = cooldown until.
        Dictionary<ulong, Dictionary<string, float>> MealCooldowns = new Dictionary<ulong, Dictionary<string, float>>();
        Dictionary<GatherSource, List<string>> IngredientSources = new Dictionary<GatherSource, List<string>>();

        public class MealInfo
        {
            public bool enabled;
            public string shortname;
            public ulong skin;
            public string description;
            [JsonProperty("Spoil time [hours - if applicable]")]
            public float spoilTime = 168;
            public int duration;
            public Dictionary<Buff, float> buffs = new Dictionary<Buff, float>();
            [JsonProperty("Commands to run when the player consumes the food [key = command, value = description]")]
            public Dictionary<string, string> commandsOnConsume = new Dictionary<string, string>();
            [JsonProperty("Commands to run when the food buff expires")]
            public List<string> commandsOnRemove = new List<string>();
            public bool persistThroughDeath;
            public float cookTime;
            public float useCooldown;
            public string permissionToCook;
            [JsonProperty("Permission that will preventing eating of this meal if present")]
            public string permPreventEating;
            public int dropWeight;
            public int recipeDropWeight = 100;
            public bool allowDefaultEffects;
            [JsonProperty("Effect settings")]
            public EffectSettings effect = new EffectSettings();
            [JsonProperty("Ingredients for meal")]
            public Dictionary<string, int> ingredients = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);

            public MealInfo(bool enabled, string shortname, ulong skin, string description, int duration, float cookTime,  Dictionary<Buff, float> buffs, Dictionary<string, int> ingredients, Dictionary<string, string> commandsOnConsume = null, List<string> commandsOnRemove = null, bool persistThroughDeath = false, float useCooldown = 0, string permissionToCook = null, int dropWeight = 100, bool allowDefaultEffects = false)
            {
                this.enabled = enabled;
                this.shortname = shortname;
                this.skin = skin;
                this.description = description;
                this.duration = duration;
                this.buffs = buffs;
                this.commandsOnConsume = commandsOnConsume;
                this.commandsOnRemove = commandsOnRemove;
                this.persistThroughDeath = persistThroughDeath;
                this.cookTime = cookTime;
                this.useCooldown = useCooldown;
                this.permissionToCook = permissionToCook;
                this.dropWeight = dropWeight;
                this.ingredients = ingredients;
                this.allowDefaultEffects = allowDefaultEffects;
            }

            public MealInfo()
            {
                
            }
        }

        public class EffectSettings
        {
            [JsonProperty("Effect to run when the player consumes the meal")]
            public string effect;

            [JsonProperty("Should the effect run server side?")]
            public bool server_side = true;

            [JsonProperty("Max distance heard [0 = no limit]")]
            public float max_distance = 0f;
        }

        public class IngredientsInfo
        {
            public bool enabled;
            public string shortname;
            public ulong skin;
            public List<GatherSource> gatherSources = new List<GatherSource>();
            [JsonProperty("Spoil time [hours - if applicable]")]
            public float spoilTime = 168;
            public List<string> customSources = new List<string>();
            public int minDrops;
            public int maxDrops;
            public int dropWeight;
            public bool notConsumed;
            public MarketItemInfo marketInfo = new MarketItemInfo(0, 100, 50, 3);

            public IngredientsInfo(bool enabled, string shortname, ulong skin, List<GatherSource> gatherSources, int dropWeight = 100, int minDrops = 1, int maxDrops = 3, bool notConsumed = false)
            {
                this.enabled = enabled;
                this.shortname = shortname;
                this.skin = skin;
                this.gatherSources = gatherSources;
                this.dropWeight = dropWeight;
                this.minDrops = minDrops;
                this.maxDrops = maxDrops;
                this.notConsumed = notConsumed;
            }

            public IngredientsInfo()
            {

            }
        }

        public class MarketItemInfo
        {
            [JsonProperty("Available market stock")]
            public int availableStock; // stock that the market is loaded with

            [JsonProperty("Maximum stock that the shop can stock [0=Disabled]")]
            public int maxStock; // stock level that the market cannot exceed

            [JsonProperty("Price the market sells ingredients for")]
            public int marketSellForPrice; // market price when players buy from

            [JsonProperty("Price the market buys ingredients for")]
            public int marketBuyForPrice; // market price when players sell to
            public MarketItemInfo(int defaultStock, int maxStock, int marketSellForPrice, int marketBuyForPrice)
            {
                this.availableStock = defaultStock;
                this.maxStock = maxStock;
                this.marketSellForPrice = marketSellForPrice;
                this.marketBuyForPrice = marketBuyForPrice;
            }
        }

        #endregion

        #region enums

        public enum Biome
        {
            Arid,
            Temperate,
            Tundra,
            Arctic,
            Jungle
        }

        public enum BuffType
        {
            Ingredient,
            Time,
            Once
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum Buff
        {
            Ingredient,
            Woodcutting_Yield, // Done
            Mining_Yield, // Done
            Skinning_Yield, // Done
            Heal_Share,// Done
            Heal, // Done
            Food_Share, // Done
            Metabolism_Overload, // Done
            Comfort, // Done
            Water_Breathing, // Done
            Fire_Resist, // Done
            Cold_Resist, // Done
            Explosion_Resist, // Done
            Animal_Resist, // Done
            Melee_Resist, // Done
            Wounded_Resist, // Done
            Spectre, // Done
            Madness, // Done
            Wealth, // Done
            Barrel_Smasher, // Done
            Crafting_Refund, // Done
            Passive_Regen, // Done
            Horse_Stats, // Done
            Fall_Damage_resist, // Done
            Condition_Loss_Reduction, // Done
            Ingredient_Chance, // Done
            Upgrade_Refund, // Done
            Research_Refund, // Done
            Role_Play, // Done
            //Anti_Heli_Radar,
            Anti_Bradley_Radar,
            Fishing_Luck, // Done
            Farming_Yield, // Done
            Component_Luck, // Done
            Electronics_Luck, // Done
            //Boat_Turbo,
            Permission, // Done
            Bleed_Resist, // Done
            Radiation_Resist, // Done
            Max_Repair, // Done
            Smelt_On_Mine, // Done
            Loot_Pickup, // Done
            Reviver, // Done
            Duplicator, // Done
            Harvest, // Done
            Ingredient_Storage, // Done
            Extra_Calories, // Sets the calories as a % of the players max. IE 0.5 would be 250 calories. Done
            Extra_Hydration, // Sets the hydration as a % of the players max. IE 0.5 would be 125 hydration. Done
            Max_Health,
            Damage_Over_Time, // Damages the consumer over time
            Fishing_Yield,
            Mining_Hotspot, // Always hit the sweet spot while mining
            Woodcutting_Hotspot, // Always hit the sweet spot while woodcutting
            Hunger, // Reduces the calories of the player by % of their max
            Dehydration, // Reduces the hydration of the player by % of their max
            Damage, // Damages the player
            Radiation, // Gives the player radiation
            Lifelink, // Heals the player by Damage done
            SkillTreeXp,
        }

        #region GetBuffType method

        BuffType GetBuffType(Buff buff)
        {
            switch (buff)
            {
                case Buff.Ingredient: return BuffType.Ingredient;
                case Buff.Heal:
                case Buff.Extra_Calories:
                case Buff.Extra_Hydration:
                case Buff.Max_Health:
                case Buff.Hunger:
                case Buff.Dehydration:
                case Buff.Radiation:
                case Buff.Bleed_Resist:
                case Buff.Damage:
                    return BuffType.Once;
                default: return BuffType.Time;
            }
        }

        #endregion

        // Add config option to only roll when the node is finished.

        [JsonConverter(typeof(StringEnumConverter))]
        public enum GatherSource
        {
            Default,
            AnyTree,
            ArcticTree,
            DesertTree,
            TemperateTree,
            TundraTree,
            JungleTree,
            Cactus,

            AnyNode,
            AnyArcticNode,
            AnyDesertNode,
            AnyTemperateNode,
            AnyTundraNode,
            AnyJungleNode,
            StoneNode,
            MetalNode,
            SulfurNode,

            AnyAnimal,

            Deer,
            Bear,
            Wolf,
            Chicken,
            PolarBear,
            Shark,
            Boar,
            horse,
            Tiger,
            Panther,
            Crocodile,
            Snake,

            Fishing,
            Gut,

            Pumpkin,
            Potato,
            Corn,
            Mushroom,
            BerryBush,
            Hemp,
            Wheat,

            BerryBushBlack,
            BerryBushBlue,
            BerryBushGreen,
            BerryBushRed,
            BerryBushWhite,
            BerryBushYellow,

            CollectableSulfur,
            CollectableStone,
            CollectableMetal,

            Crafted,
            Foodbox,
            Excavated,

            VehicleCrate,
            ToolCrate,
            MedicalCrate,
            AmmunitionCrate,
            FuelCrate,
        }

        #endregion

        #region Hooks

        #region looting        

        List<BasePlayer> OvenUsers = new List<BasePlayer>();
        Dictionary<ulong, StorageContainer> IBagContainers = new Dictionary<ulong, StorageContainer>();
        void OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            if ((container is BaseOven) || (container is CookingWorkbench))
            {
                if (OvenUsers.Contains(player))
                {
                    OvenUsers.Remove(player);
                    DestroyRecipeMenuUI(player, true);
                }
                return;
            }
            CloseIngredientBag(player, container);
        }

        List<LootContainer> LootedContainers = new List<LootContainer>();
        void OnLootEntity(BasePlayer player, StorageContainer container)
        {
            var oven = container as BaseOven;
            if (oven != null)
            {
                if (!permission.UserHasPermission(player.UserIDString, perm_use)) return;
                if (config.menuSettings.ovenWhitelist.Count > 0)
                {
                    if (!config.menuSettings.ovenWhitelist.Contains(oven.ShortPrefabName)) return;
                }
                else if (config.menuSettings.ovenBlacklist.Contains(oven.ShortPrefabName)) return;
                if (!OvenUsers.Contains(player))
                    OvenUsers.Add(player);

                CookingButton(player);
                return;
            }

            if (config.menuSettings.showOnCookingWorkbench && container is CookingWorkbench stove)
            {
                if (!permission.UserHasPermission(player.UserIDString, perm_use)) return;
                if (!OvenUsers.Contains(player))
                    OvenUsers.Add(player);

                CookingButton(player, false);
                return;
            }

            var lootContainer = container as LootContainer;
            if (lootContainer != null)
            {
                if (LootedContainers.Contains(lootContainer)) return;
                LootedContainers.Add(lootContainer);
                HandleIngredientsFromCrates(player, lootContainer);

                if (config.mealSettings.lootSettings.enable)
                {
                    float chance;
                    if (config.mealSettings.lootSettings.dropChance.TryGetValue(container.ShortPrefabName, out chance) && UnityEngine.Random.Range(0, 100f) >= 100 - chance)
                    {
                        List<KeyValuePair<string, MealInfo>> meals = Pool.Get<List<KeyValuePair<string, MealInfo>>>();
                        meals.AddRange(config.mealSettings.meals);
                        meals.RemoveAll(x => !x.Value.enabled || config.mealSettings.lootSettings.blacklist.Contains(x.Key));

                        var totalWeight = meals.Sum(x => x.Value.dropWeight);
                        var count = 0;
                        var roll = UnityEngine.Random.Range(0, totalWeight + 1);

                        foreach (var meal in meals)
                        {
                            count += meal.Value.dropWeight;
                            if (roll > count) continue;
                            var reward = CreateItem(meal.Value.shortname, UnityEngine.Random.Range(config.mealSettings.lootSettings.min, config.mealSettings.lootSettings.max + 1), meal.Value.skin, meal.Value.skin > 0 ? meal.Key : null, meal.Value.spoilTime);
                            container.inventorySlots++;
                            container.inventory.capacity++;

                            if (!reward.MoveToContainer(container.inventory)) player.GiveItem(reward);
                            break;
                        }

                        Pool.FreeUnmanaged(ref meals);
                    }
                }

                if (config.ingredientSettings.lootSettings.enable)
                {
                    float chance;
                    if (config.ingredientSettings.lootSettings.dropChance.TryGetValue(container.ShortPrefabName, out chance) && UnityEngine.Random.Range(0, 100f) >= 100 - chance)
                    {
                        List<KeyValuePair<string, IngredientsInfo>> ingredients = Pool.Get<List<KeyValuePair<string, IngredientsInfo>>>();
                        ingredients.AddRange(config.ingredientSettings.ingredients);
                        ingredients.RemoveAll(x => !x.Value.enabled || config.ingredientSettings.lootSettings.blacklist.Contains(x.Key));

                        var totalWeight = ingredients.Sum(x => x.Value.dropWeight);
                        var count = 0;
                        var roll = UnityEngine.Random.Range(0, totalWeight + 1);

                        foreach (var ingredient in ingredients)
                        {
                            count += ingredient.Value.dropWeight;
                            if (roll > count) continue;
                            var reward = CreateItem(ingredient.Value.shortname, UnityEngine.Random.Range(config.ingredientSettings.lootSettings.min, config.ingredientSettings.lootSettings.max + 1), ingredient.Value.skin, ingredient.Value.skin > 0 ? ingredient.Key : null, ingredient.Value.spoilTime);
                            container.inventorySlots++;
                            container.inventory.capacity++;

                            if (!reward.MoveToContainer(container.inventory)) player.GiveItem(reward);
                            break;
                        }

                        Pool.FreeUnmanaged(ref ingredients);
                    }
                }

                if (!permission.UserHasPermission(player.UserIDString, perm_recipecards)) return;
                
                float value;
                if (config.mealSettings.cookbookSettings.enabled && config.mealSettings.cookbookSettings.recipeDropChance.TryGetValue(container.ShortPrefabName, out value))
                {
                    var roll = UnityEngine.Random.Range(0, 100f);
                    if (roll >= 100 - GetRecipeDropChance(player.UserIDString, value)) RollForRecipe(player, lootContainer);
                }                
            }
        }

        float GetRecipeDropChance(string userid, float baseChance)
        {
            float highest = 0;
            foreach (var kvp in RecipeDropPerms)
            {
                if (kvp.Value > highest && permission.UserHasPermission(userid, kvp.Key))
                    highest = kvp.Value;
            }
            return baseChance += (baseChance * highest);
        }

        public Item CreateItem(string shortname, int quantity, ulong skin, string displayName, float spoilTime)
        {
            var item = ItemManager.CreateByName(shortname, quantity, skin);
            if (displayName != null)
            {
                item.name = displayName.ToLower();
                item.text = displayName.ToLower();
            }
            if (item.info.GetComponent<ItemModFoodSpoiling>() != null)
                item.instanceData.dataFloat = spoilTime * 3600;
            
            return item;
        }

        void HandleIngredientsFromCrates(BasePlayer player, LootContainer container)
        {
            if (container == null) return;

            switch (container.ShortPrefabName)
            {
                case "foodbox":
                case "trash-pile-1":
                case "crate_food_1":
                case "crate_food_2":
                    HandleIngredientChance(player, container, GatherSource.Foodbox, null, container.net?.ID.Value ?? 0);
                    return;

                case "vehicle_parts":
                    HandleIngredientChance(player, container, GatherSource.VehicleCrate, null, container.net?.ID.Value ?? 0);
                    return;

                case "crate_tools":
                    HandleIngredientChance(player, container, GatherSource.ToolCrate, null, container.net?.ID.Value ?? 0);
                    return;

                case "crate_medical":
                    HandleIngredientChance(player, container, GatherSource.MedicalCrate, null, container.net?.ID.Value ?? 0);
                    return;

                case "crate_ammunition":
                    HandleIngredientChance(player, container, GatherSource.AmmunitionCrate, null, container.net?.ID.Value ?? 0);
                    return;

                case "crate_fuel":
                    HandleIngredientChance(player, container, GatherSource.FuelCrate, null, container.net?.ID.Value ?? 0);
                    return;
            }
        }

        #endregion

        #region Main

        Dictionary<string, string> DefaultDisplayNames = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        Dictionary<string, int> ItemID = new Dictionary<string, int>();
        Dictionary<string, MealInfo> LoadedMeals = new Dictionary<string, MealInfo>();

        void OnServerInitialized(bool initial)
        {
            SetupSubscriptions();

            foreach (var kvp in ItemManager.GetItemDefinitions())
            {
                if (!DefaultDisplayNames.ContainsKey(kvp.displayName.english)) DefaultDisplayNames.Add(kvp.displayName.english, kvp.shortname);
                if (!ItemID.ContainsKey(kvp.shortname)) ItemID.Add(kvp.shortname, kvp.itemid);
            }

            bool updateConfig = false;

            if (!pcdData.patched)
            {
                PatchForCookingUpdate();
                updateConfig = true;
                pcdData.patched = true;
            }

            if (!pcdData.patched2)
            {
                PatchForCookingUpdate2();
                updateConfig = true;
                pcdData.patched2 = true;
            }

            foreach (var meal in DefaultBasicMeals)
            {
                if (config.mealSettings.meals.ContainsKey(meal.Key)) continue;
                config.mealSettings.meals.Add(meal.Key, meal.Value);
                Puts($"Added new meal: {meal.Key}");
                updateConfig = true;
                //["Fishing_Yield_MenuDescription"] = "Increases the amount of fish caught by <color=#42f105>{0}%</color> while fishing.",
            }
            
            foreach (var recipe in config.mealSettings.meals)
            {
                if (!recipe.Value.enabled) continue;

                if (!string.IsNullOrEmpty(recipe.Value.permissionToCook))
                {
                    if (!recipe.Value.permissionToCook.StartsWith("cooking.", StringComparison.OrdinalIgnoreCase))
                    {
                        recipe.Value.permissionToCook = "cooking." + recipe.Value.permissionToCook;
                        updateConfig = true;
                    }
                    if (!permission.PermissionExists(recipe.Value.permissionToCook, this)) permission.RegisterPermission(recipe.Value.permissionToCook, this);
                }

                if (!string.IsNullOrEmpty(recipe.Value.permPreventEating))
                {
                    if (!recipe.Value.permPreventEating.StartsWith("cooking.", StringComparison.OrdinalIgnoreCase))
                    {
                        recipe.Value.permPreventEating = "cooking." + recipe.Value.permPreventEating;
                        updateConfig = true;
                    }
                    if (!permission.PermissionExists(recipe.Value.permPreventEating, this)) permission.RegisterPermission(recipe.Value.permPreventEating, this);
                }

                if (recipe.Value.buffs.ContainsKey(Buff.Ingredient_Storage) || recipe.Key.Equals("ingredient bag", StringComparison.OrdinalIgnoreCase) || recipe.Value.skin == 2578002003)
                {
                    if (config.generalSettings.ingredientBagSettings.bagItemSettings.displayName != recipe.Key)
                    {
                        Puts($"Updated ingredient bag name to match the recipe item [{recipe.Key}].");
                        config.generalSettings.ingredientBagSettings.bagItemSettings.displayName = recipe.Key;
                        updateConfig = true;
                    }
                    if (config.generalSettings.ingredientBagSettings.bagItemSettings.skinID != recipe.Value.skin)
                    {
                        Puts($"Updated ingredient bag skin to match the recipe item [{recipe.Value.skin}].");
                        config.generalSettings.ingredientBagSettings.bagItemSettings.skinID = recipe.Value.skin;
                        updateConfig = true;
                    }
                    if (config.generalSettings.ingredientBagSettings.bagItemSettings.shortname != recipe.Value.shortname)
                    {
                        Puts($"Updated ingredient bag shortname to match the recipe item [{recipe.Value.shortname}].");
                        config.generalSettings.ingredientBagSettings.bagItemSettings.shortname = recipe.Value.shortname;
                        updateConfig = true;
                    }
                    if (!recipe.Value.buffs.ContainsKey(Buff.Ingredient_Storage))
                    {
                        recipe.Value.buffs.Clear();
                        recipe.Value.buffs.Add(Buff.Ingredient_Storage, 0);
                        updateConfig = true;
                        Puts($"Updated ingredient bag buff type.");
                    }
                }

                if (config.mealSettings.cookbookSettings.enabled && config.mealSettings.cookbookSettings.defaultMeals.Count > 0)
                {
                    List<string> mealsToDelete = Pool.Get<List<string>>();
                    foreach (var meal in config.mealSettings.cookbookSettings.defaultMeals)
                        if (!config.mealSettings.meals.ContainsKey(meal)) mealsToDelete.Add(meal);

                    foreach (var meal in mealsToDelete)
                    {
                        config.mealSettings.cookbookSettings.defaultMeals.Remove(meal);
                        updateConfig = true;
                        Puts($"Removed {meal} from default meals as it was not listed in the recipes list.");
                    }

                    Pool.FreeUnmanaged(ref mealsToDelete);
                }

                LoadedMeals.Add(recipe.Key, recipe.Value);

                IngredientsInfo ingredientData;
                if (recipe.Value.buffs.ContainsKey(Buff.Ingredient) && config.ingredientSettings.ingredients.TryGetValue(recipe.Key, out ingredientData))
                {
                    if (recipe.Value.skin != ingredientData.skin || recipe.Value.shortname != ingredientData.shortname || !ingredientData.enabled)
                    {
                        Puts($"Updated ingredient: {recipe.Key} to match it's recipe attribute.");
                        ingredientData.skin = recipe.Value.skin;
                        ingredientData.shortname = recipe.Value.shortname;
                        ingredientData.enabled = true;
                        updateConfig = true;
                    }
                }
                List<string> ingredients_to_remove = Pool.Get<List<string>>();
                foreach (var ingredient in recipe.Value.ingredients)
                {
                    // Adds default ingredients to the menu.
                    if (!config.ingredientSettings.ingredients.TryGetValue(ingredient.Key, out ingredientData) && DefaultDisplayNames.ContainsKey(ingredient.Key))
                    {
                        Puts($"Adding new ingredient: {ingredient.Key}");
                        config.ingredientSettings.ingredients.Add(ingredient.Key, new IngredientsInfo(true, DefaultDisplayNames[ingredient.Key], 0, new List<GatherSource>() { GatherSource.Default }, 0));
                        updateConfig = true;
                    }
                    else
                    {
                        if (ingredientData == null || !ingredientData.enabled)
                        {
                            ingredients_to_remove.Add(ingredient.Key);
                            updateConfig = true;
                        }
                    }
                }
                foreach (var ingredient in ingredients_to_remove)
                    recipe.Value.ingredients.Remove(ingredient);
                Pool.FreeUnmanaged(ref ingredients_to_remove);
            }

            if (config.mealSettings.cookbookSettings.enabled)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    CacheRecipes(player);
                }
            }

            foreach (var ingredient in config.ingredientSettings.ingredients)
            {
                if (!ingredient.Value.enabled) continue;
                
                Ingredients[ingredient.Key] = new IngredientsInfo()
                {
                    shortname = ingredient.Value.shortname,
                    skin = ingredient.Value.skin                    
                };

                
                foreach (var source in ingredient.Value.gatherSources)
                {
                    if (source == GatherSource.Crafted) continue;
                    List<string> sourceList;
                    if (!IngredientSources.TryGetValue(source, out sourceList)) IngredientSources.Add(source, sourceList = new List<string>());
                    if (!sourceList.Contains(ingredient.Key)) sourceList.Add(ingredient.Key);
                }

                DropWeightReference[ingredient.Key] = ingredient.Value.dropWeight;

                if (ingredient.Value.marketInfo != null && ingredient.Value.marketInfo.maxStock > 0 && (ingredient.Value.marketInfo.marketBuyForPrice > 0 || ingredient.Value.marketInfo.marketSellForPrice > 0))
                    MarketIngredients.Add(ingredient.Key, ingredient.Value);
            }

            Puts($"Loaded ingredient drop lists for {IngredientSources.Count}x gather sources.");
            foreach (var source in IngredientSources)
                Puts($"Source: {source.Key} - {string.Join(", ", source.Value)}");

            foreach (var source in config.generalSettings.dropSources)
            {
                if (string.IsNullOrEmpty(source.Value.permission_required)) continue;
                if (!source.Value.permission_required.StartsWith("cooking.", StringComparison.OrdinalIgnoreCase))
                {
                    source.Value.permission_required = "cooking." + source.Value.permission_required;
                    updateConfig = true;
                }
                permission.RegisterPermission(source.Value.permission_required, this);
            }

            if (!string.IsNullOrEmpty(config.generalSettings.grown_permission) && !config.generalSettings.grown_permission.StartsWith("cooking.", StringComparison.OrdinalIgnoreCase))
            {
                config.generalSettings.grown_permission = "cooking." + config.generalSettings.grown_permission;
                updateConfig = true;
            }

            foreach (var source in DefaultGatherSources())
            {
                if (!config.generalSettings.dropSources.ContainsKey(source.Key))
                {
                    Puts($"Added drop source: {source.Key} [{source.Value.chance * 100}%]");
                    config.generalSettings.dropSources.Add(source.Key, source.Value);
                    updateConfig = true;
                }
            }
            
            if (config.generalSettings.buffSettings.fishingLuckSettings.use_defined_loottable && config.generalSettings.buffSettings.fishingLuckSettings.definedLootTable.Count == 0)
            {
                config.generalSettings.buffSettings.fishingLuckSettings.definedLootTable = DefaultLuckLootTable;
                updateConfig = true;
            }

            if (config.generalSettings.marketSettings.npcSettings.wornItems.IsNullOrEmpty())
            {
                config.generalSettings.marketSettings.npcSettings.wornItems = DefaultNPCWornItems;
                updateConfig = true;
            }

            if (config.generalSettings.marketSettings.npcSettings.heldItem == null || string.IsNullOrEmpty(config.generalSettings.marketSettings.npcSettings.heldItem.shortname))
            {
                config.generalSettings.marketSettings.npcSettings.heldItem = DefaultNPCHeldItem;
                updateConfig = true;
            }

            if (config.generalSettings.recipeMenuCMDs.Count == 0)
            {
                config.generalSettings.recipeMenuCMDs.Add("recipemenu");
                config.generalSettings.recipeMenuCMDs.Add("cook");
                foreach (var c in config.generalSettings.recipeMenuCMDs)
                {
                    cmd.AddChatCommand(c, this, nameof(SendCookMenuCMD));
                }
                updateConfig = true;
            }

            if (config.apiSettings.skilltree_settings.Skills.IsNullOrEmpty())
            {
                config.apiSettings.skilltree_settings.Skills = DefaultSkills;
                updateConfig = true;
            }
            else
            {
                foreach (var entry in DefaultSkills)
                    if (!config.apiSettings.skilltree_settings.Skills.ContainsKey(entry.Key))
                    {
                        config.apiSettings.skilltree_settings.Skills.Add(entry.Key, entry.Value);
                        updateConfig = true;
                    }
            }

            if (config.generalSettings.permissionSettings.DropChanceMultiplier.IsNullOrEmpty())
            {
                config.generalSettings.permissionSettings.DropChanceMultiplier.Add("cooking.vip", 0.2f);
                updateConfig = true;
            }

            if (config.mealSettings.lootSettings.enable && config.mealSettings.lootSettings.dropChance.Count == 0)
            {
                config.mealSettings.lootSettings.dropChance = DefaultMealIngredientLootChances;
                updateConfig = true;
            }

            if (config.ingredientSettings.lootSettings.enable && config.ingredientSettings.lootSettings.dropChance.Count == 0)
            {
                config.ingredientSettings.lootSettings.dropChance = DefaultMealIngredientLootChances;
                updateConfig = true;
            }

            if (updateConfig) SaveConfig();

            // Gets and stores all bp defs. Also stores category info.
            foreach (var itemDef in ItemManager.GetItemDefinitions())
            {
                ItemDefs.Add(itemDef.shortname, itemDef);
                if (itemDef.Blueprint != null && itemDef.Blueprint.userCraftable)
                {
                    if (!item_BPs.ContainsKey(itemDef.shortname)) item_BPs.Add(itemDef.shortname, itemDef.Blueprint);
                }
                if (itemDef.steamDlc != null || itemDef.steamItem != null || itemDef.isRedirectOf != null || itemDef.rarity == Rust.Rarity.None || itemDef.hidden) continue;
                RandomItems.Add(itemDef.shortname);
                if (itemDef.category == ItemCategory.Electrical && !config.generalSettings.buffSettings.electronicsLuckSettings.blacklist.Contains(itemDef.shortname)) electrical_item_list.Add(itemDef);
                else if (itemDef.category == ItemCategory.Component && !config.generalSettings.buffSettings.componentLuckSettings.blacklist.Contains(itemDef.shortname)) component_item_list.Add(itemDef);
            }

            foreach (var perm in config.generalSettings.permissionSettings.DropChanceMultiplier)
            {
                permission.RegisterPermission(perm.Key.Contains("cooking.", System.Globalization.CompareOptions.OrdinalIgnoreCase) ? perm.Key : "cooking." + perm.Key, this);
            }

            foreach (var npcData in pcdData.npcs)
            {
                var npc = IsNPCSpawned(npcData.position);
                if (npc == null || npc.IsDestroyed)
                {
                    SpawnMarketNPC(npcData.position, Quaternion.Euler(npcData.rotation));
                }                    
                else
                {
                    if (!MarketNPCs.ContainsKey(npc.net.ID.Value)) MarketNPCs.Add(npc.net.ID.Value, npc);
                }
            }

            if (config.menuSettings.hudButtonSettings.enable)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    CreatButtons(player);
                }
            }

            Puts($"Finished spawning {MarketNPCs.Count} market NPCs.");

            HandleSkillTreeIntegration();
            AddCurrencyToItemVending();
            Subscribe(nameof(OnPluginLoaded));
        }

        void PatchForCookingUpdate()
        {
            config.ingredientSettings.ingredients.Remove("flour");
            config.ingredientSettings.ingredients.Remove("egg");
            foreach (var meal in config.mealSettings.meals)
            {
                if (meal.Value.ingredients.TryGetValue("flour", out var amount))
                {
                    meal.Value.ingredients.Remove("flour");
                    meal.Value.ingredients.Add("wheat", amount);
                }
            }
        }
        void PatchForCookingUpdate2()
        {
            config.ingredientSettings.ingredients.Remove("bread");
            config.mealSettings.meals.Remove("bread");
            foreach (var meal in config.mealSettings.meals)
            {
                if (meal.Value.ingredients.TryGetValue("bread", out var amount))
                {
                    meal.Value.ingredients.Remove("bread");
                    meal.Value.ingredients.Add("Bread Loaf", amount);
                }
            }
        }

        void OnServerSave()
        {
            SaveData();
        }

        void OnPlayerDig(BasePlayer player, BaseDiggableEntity diggable)
        {
            if (diggable == null || diggable.digsRemaining != 0) return;            
            HandleIngredientChance(player, GatherSource.Excavated, false);
        }

        // Handles the cooldown for items.
        object OnItemAction(Item item, string action, BasePlayer player)
        {
            switch (action)
            {
                case "consume":
                    if (string.IsNullOrEmpty(item.name)) return null;
                    MealInfo mealData;
                    if (!config.mealSettings.meals.TryGetValue(item.name, out mealData) || mealData.useCooldown == 0) return null;
                    float cooldown;
                    if ((cooldown = HandleMealCooldown(player, item.name, mealData.useCooldown)) > 0)
                    {
                        PrintToChat(player, string.Format(lang.GetMessage("MealOnCooldown", this, player.UserIDString), Math.Round(cooldown, 0)));
                        return true;
                    }
                    break;

                case "gut":
                case "Gut":
                    // Handle gutting gather etc.
                    HandleIngredientChance(player, GatherSource.Gut, false);
                    break;

                case "unwrap":
                    if (IsIngredientBag(item))
                    {
                        OpenIngredientBag(player);
                        return true;
                    }
                    if (IsRecipeCard(item))
                    {
                        HandleRecipeCard(player, item);
                        return true;
                    }
                    break;
            }
            return null;
        }

        Dictionary<ulong, Dictionary<string, MealInfo>> CachedRecipes = new Dictionary<ulong, Dictionary<string, MealInfo>>();
        void HandleRecipeCard(BasePlayer player, Item item)
        {
            PCDInfo playerData;
            if (!pcdData.pEntity.TryGetValue(player.userID, out playerData)) pcdData.pEntity.Add(player.userID, playerData = new PCDInfo());

            if (playerData.learntRecipes.Contains(item.text))
            {
                PrintToChat(player, lang.GetMessage("RecipeAlreadyKnown", this, player.UserIDString));
                return;
            }

            MealInfo mealData;
            if (!config.mealSettings.meals.TryGetValue(item.text, out mealData))
            {
                Puts($"ERROR: Attempted to learn a recipe that does not exist in the config: {item.text}");
                return;
            }
            if (!string.IsNullOrEmpty(mealData.permissionToCook) && !permission.UserHasPermission(player.UserIDString, mealData.permissionToCook))
            {
                PrintToChat(player, lang.GetMessage("NoRecipePerms", this, player.UserIDString));
                return;
            }

            playerData.learntRecipes.Add(item.text);

            PrintToChat(player, string.Format(lang.GetMessage("NewRecipeLearnt", this, player.UserIDString), item.text.Titleize()));
            AddCachedRecipe(player, item.text);
            item.Remove();
            ItemManager.DoRemoves();            
        }

        void DeleteCachedRecipe(BasePlayer player)
        {
            Dictionary<string, MealInfo> dict;
            if (!CachedRecipes.TryGetValue(player.userID, out dict)) return;
            dict.Clear();
            dict = null;
            CachedRecipes.Remove(player.userID);
        }

        void CacheRecipes(BasePlayer player)
        {
            if (!config.mealSettings.cookbookSettings.enabled) return;

            PCDInfo playerData;
            if (!pcdData.pEntity.TryGetValue(player.userID, out playerData)) pcdData.pEntity.Add(player.userID, playerData = new PCDInfo());

            foreach (var meal in config.mealSettings.cookbookSettings.defaultMeals)
            {
                if (!playerData.learntRecipes.Contains(meal))
                    playerData.learntRecipes.Add(meal);
            }

            if (!config.mealSettings.cookbookSettings.unlearntVisible) playerData.favouriteDishes.RemoveAll(x => !playerData.learntRecipes.Contains(x));            

            MealInfo mealData;            
            foreach (var recipe in playerData.learntRecipes)
            {
                if (!config.mealSettings.meals.TryGetValue(recipe, out mealData) || !mealData.enabled) continue;
                AddCachedRecipe(player, recipe);
            }
        }

        void AddCachedRecipe(BasePlayer player, string recipe)
        {
            Dictionary<string, MealInfo> mealData;
            if (!CachedRecipes.TryGetValue(player.userID, out mealData)) CachedRecipes.Add(player.userID, mealData = new Dictionary<string, MealInfo>(StringComparer.InvariantCultureIgnoreCase));
            mealData[recipe] = config.mealSettings.meals[recipe];
        }

        object OnPlayerAddModifiers(BasePlayer player, Item item, ItemModConsumable consumable)
        {
            var buffManager = GetBuffManager(player, false);
            float value;
            if (buffManager != null)
            {
                if (buffManager.GetBuffModifiers.TryGetValue(Buff.Food_Share, out value) && consumable.GetIfType(MetabolismAttribute.Type.Calories) > 0) 
                {
                    float calories = 0;
                    foreach (var calType in consumable.effects)
                    {
                        if (calType.type == MetabolismAttribute.Type.Calories)
                        {
                            calories = calType.amount * value;
                            break;
                        }
                    }
                    if (calories > 0)
                    {
                        var hits = FindEntitiesOfType<BasePlayer>(player.transform.position, config.generalSettings.buffSettings.foodShareSettings.foodShareRadius);
                        foreach (var hit in hits)
                        {
                            if (hit == player) continue;
                            hit.metabolism.calories.Add(calories);
                        }

                        Pool.FreeUnmanaged(ref hits);
                    }
                }
            }

            bool metabolismReset = false;
            if (item.name == null) return null;
            MealInfo mealData;            
            if (!config.mealSettings.meals.TryGetValue(item.name, out mealData))
            {
                // We check to see if it is an ingredient and if the config does not allow ingredients to be used as regular vanilla items.
                if (!config.ingredientSettings.allow_default_ingredient_effects && config.ingredientSettings.ingredients.ContainsKey(item.name))
                {
                    ResetMetabolism(player, consumable, item);
                }                    

                return null;
            }

            // We confirmed it is a meal, but we check to see if the meal is an ingredient only.
            if (!config.ingredientSettings.allow_default_ingredient_effects && (mealData.buffs.Count == 0 || (mealData.buffs.Count == 1 && mealData.buffs.ContainsKey(Buff.Ingredient))))
            {
                metabolismReset = true;
                ResetMetabolism(player, consumable, item);
            }                

            bool containsTimedBuff = false;
            foreach (var buff in mealData.buffs.Keys)
            {
                if (GetBuffType(buff) == BuffType.Time)
                {
                    containsTimedBuff = true;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(mealData.permPreventEating) && permission.UserHasPermission(player.UserIDString, mealData.permPreventEating))
            {
                PrintToChat(player, lang.GetMessage("PermPrevented", this, player.UserIDString));
                RefundMeal(player, item);
                ResetMetabolism(player, consumable, item);
                return null;
            }

            // buffManager is the behaviour for our buffs
            if (buffManager == null && containsTimedBuff) buffManager = GetBuffManager(player, containsTimedBuff);

            // Check to see if we are exceeding the maximum meal size.
            if (config.mealSettings.maxMealsAllowed > 0 && containsTimedBuff && buffManager.ActiveMealCount >= config.mealSettings.maxMealsAllowed && !buffManager.activeMeals.ContainsKey(item.name))
            {
                PrintToChat(player, lang.GetMessage("MaxActiveMeals", this, player.UserIDString));
                RefundMeal(player, item);
                ResetMetabolism(player, consumable, item);
                return null;
            }

            if (!metabolismReset) metabolismReset = mealData.allowDefaultEffects;

            if (!config.mealSettings.allowDefaultMealEffects && !metabolismReset) ResetMetabolism(player, consumable, item);

            int mealDuration = GetMealDurationMultipliers(player, mealData.duration);

            Dictionary<Buff, float> timedBuffs = new Dictionary<Buff, float>();
            string chatBuffDescription = string.Format(lang.GetMessage("EatMealMessageNew", this, player.UserIDString), item.name.TitleCase(), mealData.buffs.Count > 1 ? lang.GetMessage("S", this, player.UserIDString) : null);
            foreach (var buff in mealData.buffs)
            {
                var buffType = GetBuffType(buff.Key);
                if (buff.Key == Buff.Ingredient) continue;
                if (buff.Key == Buff.Ingredient_Storage) continue;
                if (buff.Key == Buff.Role_Play) continue;

                if (buff.Key != Buff.Permission) chatBuffDescription += $"\n- " + GetBuffDescriptions(player, buff.Key, buff.Value);
                if (buffType == BuffType.Once)
                {
                    HandleOnceOffBuffs(player, item, buff.Key, buff.Value, mealDuration);
                    continue;
                }

                //chatBuffDescription += $"\n- <color=#07a99d>{lang.GetMessage($"{buff.Key}_ToText", this, player.UserIDString)}:</color> {string.Format(lang.GetMessage($"{buff.Key}_MenuDescription", this, player.UserIDString), buff.Value * 100)}";
                if (buff.Key == Buff.Spectre) RemoveFromCurrentTargets(player);

                if (!timedBuffs.ContainsKey(buff.Key)) timedBuffs.Add(buff.Key, buff.Value);
                else timedBuffs[buff.Key] += buff.Value;                
            }
            if (mealData.commandsOnConsume != null)
            {
                foreach (var perm in mealData.commandsOnConsume)
                    chatBuffDescription += $"\n- <color=#4600ff>Command:</color> {perm.Value}";
            }
            PrintToChat(player, chatBuffDescription);
            if (timedBuffs.Count > 0)
                buffManager.AddBuff(item.name, timedBuffs, mealDuration, mealData.shortname, mealData.skin, mealData.commandsOnConsume, mealData.commandsOnRemove);

            RunEatingEffect(player, mealData.effect.effect, mealData.effect.server_side, mealData.effect.max_distance);

            Interface.CallHook("OnMealConsumed", player, item, mealDuration);
            return null;
        }

        void RunEatingEffect(BasePlayer player, string effect, bool server, float dist)
        {
            if (string.IsNullOrEmpty(effect)) return;

            if (!server)
            {
                EffectNetwork.Send(new Effect(effect, player.transform.position, player.transform.position), player.net.connection);
                return;
            }

            List<BasePlayer> heard = Pool.Get<List<BasePlayer>>();

            heard.AddRange(dist <= 0 ? BasePlayer.activePlayerList : FindEntitiesOfType<BasePlayer>(player.transform.position, dist));
            foreach (var p in heard)
            {
                if (p.IsNpc || !p.userID.IsSteamId()) continue;
                try
                {
                    EffectNetwork.Send(new Effect(effect, player.transform.position, player.transform.position), p.net.connection);
                }
                catch
                {
                    Puts($"Error running effect {effect} for player {p?.displayName}");
                }
            }
            Pool.FreeUnmanaged(ref heard);
        }

        int GetMealDurationMultipliers(BasePlayer player, int defaultDuration)
        {
            var highest = 0f;
            foreach (var perm in SkillTreePermission_BuffDuration)
            {
                if (perm.Value > highest && permission.UserHasPermission(player.UserIDString, perm.Key)) highest = perm.Value;
            }

            return Convert.ToInt32(defaultDuration + (defaultDuration * highest));
        }

        float HandleMealCooldown(BasePlayer player, string meal, float duration)
        {
            Dictionary<string, float> cooldowns;
            if (MealCooldowns.TryGetValue(player.userID, out cooldowns))
            {
                float time;
                if (cooldowns.TryGetValue(meal, out time))
                {
                    if (Time.time < time) return time - Time.time;
                }
            }
            else MealCooldowns.Add(player.userID, cooldowns = new Dictionary<string, float>(StringComparer.InvariantCultureIgnoreCase));
            cooldowns[meal] = Time.time + duration;
            return 0;
        }

        void HandleOnceOffBuffs(BasePlayer player, Item item, Buff buff, float modifier, float duration)
        {
            switch (buff)
            {
                case Buff.Heal:
                    var health = player._maxHealth * modifier;
                    player.Heal(health);
                    return;

                case Buff.Extra_Calories:
                    player.metabolism.calories.Add(player.metabolism.calories.max * modifier);
                    return;

                case Buff.Hunger:
                    player.metabolism.calories.Subtract(player.metabolism.calories.max * modifier);
                    return;

                case Buff.Extra_Hydration:
                    player.metabolism.hydration.Add(player.metabolism.hydration.max * modifier);
                    return;

                case Buff.Dehydration:
                    player.metabolism.calories.Subtract(player.metabolism.hydration.max * modifier);
                    return;

                case Buff.Damage:
                    player.Hurt(modifier);
                    return;

                case Buff.Radiation:
                    player.metabolism.radiation_level.Add(modifier);
                    player.metabolism.radiation_poison.Add(modifier);
                    return;

                case Buff.Bleed_Resist:
                    if (player.modifiers.GetValue(Modifier.ModifierType.Clotting) > modifier) return;
                    player.modifiers.Add(new List<ModifierDefintion>()
                    {
                        new ModifierDefintion
                        {
                            type = Modifier.ModifierType.Clotting,
                            value = modifier,
                            duration = duration,
                            source = Modifier.ModifierSource.Tea
                        }
                    });
                    return;

                case Buff.Max_Health:
                    var startHealth = player.StartMaxHealth();
                    var overloadAmount = modifier * 100;
                    var maxHealth = 100 + overloadAmount;
                    var healthMultiplier = (maxHealth - startHealth) /
                                           startHealth;
                    player.modifiers.Add(new List<ModifierDefintion>
                        {
                            new ModifierDefintion
                            {
                                type = Modifier.ModifierType.Max_Health,
                                value = healthMultiplier,
                                duration = duration,
                                source = Modifier.ModifierSource.Tea
                            }
                        });
                    player.modifiers.SendChangesToClient();
                    if (overloadAmount + player.health > player.MaxHealth()) player.health = player.MaxHealth();
                    else player.health += overloadAmount;

                    PrintToChat(player, String.Format(lang.GetMessage("MaxHealthIncreased", this, player.UserIDString), overloadAmount));
                    return;
            }
        }

        void ResetMetabolism(BasePlayer player, ItemModConsumable consumable = null, Item item = null)
        {
            if (consumable != null && item != null)
            {
                float num = Mathf.Max(consumable.amountToConsume, 1);
                float num3 = Mathf.Min(item.amount, num) / num;
                float num4 = consumable.conditionFractionToLose > 0f ? consumable.conditionFractionToLose : item.conditionNormalized;
                foreach (var effect in consumable.effects)
                {
                    if (Mathf.Clamp01(player.healthFraction + player.metabolism.pending_health.Fraction()) > effect.onlyIfHealthLessThan)
                        continue;
                    if (effect.type == MetabolismAttribute.Type.Calories)
                        player.metabolism.calories.Subtract(effect.amount * num3 * num4);
                    else if (effect.type == MetabolismAttribute.Type.Hydration)
                        player.metabolism.hydration.Subtract(effect.amount * num3 * num4);
                }
            }

            player.metabolism.poison.SetValue(player.metabolism.FindAttribute(MetabolismAttribute.Type.Poison).lastValue);
            player.metabolism.radiation_level.SetValue(player.metabolism.FindAttribute(MetabolismAttribute.Type.Radiation).lastValue);
            player.metabolism.radiation_poison.SetValue(player.metabolism.FindAttribute(MetabolismAttribute.Type.Radiation).lastValue);
            player.metabolism.pending_health.SetValue(player.metabolism.FindAttribute(MetabolismAttribute.Type.HealthOverTime).lastValue);
        }

        void RefundMeal(BasePlayer player, Item item)
        {
            var meal = ItemManager.CreateByName(item.info.shortname, 1, item.skin);
            meal.name = item.name.ToLower();
            if (item.skin > 0) meal.text = item.name.ToLower();
            if (IsSpoilingItem(item)) meal.instanceData.dataFloat = item.instanceData.dataFloat; 
            player.GiveItem(meal);
        }

        #endregion

        #region Connections

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyBuffManager(player.userID);
            DestroyCookingManager(player.userID);

            try { DeleteCachedRecipe(player); } catch { }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            var component = player.gameObject.GetComponent<BuffManager>();
            if (component != null) GameObject.Destroy(component);

            var component2 = player.gameObject.GetComponent<CookingManager>();
            if (component2 != null) GameObject.Destroy(component2);

            CacheRecipes(player);

            if (config.menuSettings.hudButtonSettings.enable && player.IsAlive()) CreatButtons(player);

            
        }


        #endregion

        #region Dispensers

        object OnTreeMarkerHit(TreeEntity tree, HitInfo info)
        {
            if (info == null || info.InitiatorPlayer == null || info.InitiatorPlayer.IsNpc || !info.InitiatorPlayer.userID.IsSteamId()) return null;

            var buffManager = GetBuffManager(info.InitiatorPlayer, false);
            if (buffManager == null) return null;            

            if (buffManager.GetBuffModifiers.ContainsKey(Buff.Woodcutting_Hotspot)) return true;

            return null;
        }

        void OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            if (info == null || player == null || !player.userID.IsSteamId() || !(info.HitEntity is OreResourceEntity)) return;

            var buffManager = GetBuffManager(info.InitiatorPlayer, false);
            if (buffManager == null) return;

            if (!buffManager.GetBuffModifiers.ContainsKey(Buff.Mining_Hotspot)) return;

            var ore = info.HitEntity as OreResourceEntity;
            if (ore._hotSpot != null)
            {
                ore._hotSpot.transform.position = info.HitPositionWorld;
                ore._hotSpot.SendNetworkUpdateImmediate();
            }
            else ore._hotSpot = ore.SpawnBonusSpot(info.HitPositionWorld);
        }

        object OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {            
            return HandleDispenserGather(dispenser, player, item);
        }

        object OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            return HandleDispenserGather(dispenser, player, item, true);
        }

        object HandleDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item, bool finalHit = false)
        {
            if (player == null || player.IsNpc || !player.userID.IsSteamId() || item == null) return null;

            var buffManager = GetBuffManager(player, false);
            var heldEntity = player.GetHeldEntity();
            var modifier = GetPowerToolModifier(heldEntity);
            HandleDispenserDrops(dispenser, player, dispenser.gatherType == ResourceDispenser.GatherType.Flesh ? dispenser.containedItems.Where(x => x.amount > 0).FirstOrDefault() == null : finalHit, buffManager, modifier);
            if (buffManager == null) return null;
            if (modifier == 0) return null;

            var buffs = buffManager.GetBuffModifiers;
            float value = 0;

            if (dispenser.gatherType == ResourceDispenser.GatherType.Tree)
            {
                if (buffs.TryGetValue(Buff.Woodcutting_Yield, out value))
                    item.amount += Convert.ToInt32(item.amount * value);                
            }

            if (dispenser.gatherType == ResourceDispenser.GatherType.Ore)
            {
                if (buffs.TryGetValue(Buff.Mining_Yield, out value))
                    item.amount += Convert.ToInt32(item.amount * value);

                // Smelt on mine
                if (dispenser.baseEntity != null && !string.IsNullOrEmpty(dispenser.baseEntity.ShortPrefabName) && dispenser.baseEntity.ShortPrefabName != "stone-ore" && buffs.TryGetValue(Buff.Smelt_On_Mine, out value) && RollSuccessful(value))
                {
                    HandleSmeltOnMine(player, item);
                }                
            }

            if (dispenser.gatherType == ResourceDispenser.GatherType.Flesh)
            {
                if (buffs.TryGetValue(Buff.Skinning_Yield, out value))
                    item.amount += Convert.ToInt32(item.amount * value);               
            }

            return null;
        }

        float GetPowerToolModifier(HeldEntity heldEntity, bool forIngredients = true)
        {
            if (heldEntity != null)
            {
                if (heldEntity is Chainsaw) return forIngredients ? config.generalSettings.powerToolSettings.chainsawIngredientModifier : config.generalSettings.powerToolSettings.chainsawBuffModifier;
                if (heldEntity is Jackhammer) return forIngredients ? config.generalSettings.powerToolSettings.jackhammerIngredientModifier : config.generalSettings.powerToolSettings.jackhammerBuffModifier;
            }
            return 1;
        }

        void HandleDispenserDrops(ResourceDispenser dispenser, BasePlayer player, bool finalHit, BuffManager buffManager, float modifier)
        {
            if (modifier == 0) return;
            if (dispenser.gatherType == ResourceDispenser.GatherType.Tree)
            {
                if (dispenser.baseEntity != null && (!config.generalSettings.final_hit_only || finalHit))
                {
                    var biome = GetBiome(dispenser.baseEntity.transform.position);
                    switch (biome)
                    {
                        case Biome.Arid:
                            HandleIngredientChance(player, GatherSource.DesertTree, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case Biome.Arctic:
                            HandleIngredientChance(player, GatherSource.ArcticTree, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case Biome.Temperate:
                            HandleIngredientChance(player, GatherSource.TemperateTree, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case Biome.Tundra:
                            HandleIngredientChance(player, GatherSource.TundraTree, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;
                        case Biome.Jungle:
                            HandleIngredientChance(player, GatherSource.JungleTree, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;
                    }

                    HandleIngredientChance(player, GatherSource.AnyTree, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                }
                return;
            }

            if (dispenser.gatherType == ResourceDispenser.GatherType.Ore)
            {
                if (dispenser.baseEntity != null && (!config.generalSettings.final_hit_only || finalHit))
                {
                    var biome = GetBiome(dispenser.baseEntity.transform.position);
                    switch (biome)
                    {
                        case Biome.Arid:
                            HandleIngredientChance(player, GatherSource.AnyDesertNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case Biome.Arctic:
                            HandleIngredientChance(player, GatherSource.AnyArcticNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case Biome.Temperate:
                            HandleIngredientChance(player, GatherSource.AnyTemperateNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case Biome.Tundra:
                            HandleIngredientChance(player, GatherSource.AnyTundraNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;
                        case Biome.Jungle:
                            HandleIngredientChance(player, GatherSource.AnyJungleNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;
                    }

                    switch (dispenser.baseEntity.ShortPrefabName)
                    {
                        case "stone-ore":
                            HandleIngredientChance(player, GatherSource.StoneNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "metal-ore":
                            HandleIngredientChance(player, GatherSource.MetalNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "sulfur-ore":
                            HandleIngredientChance(player, GatherSource.SulfurNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;
                    }

                    HandleIngredientChance(player, GatherSource.AnyNode, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                }
                return;
            }

            if (dispenser.gatherType == ResourceDispenser.GatherType.Flesh)
            {
                if (dispenser.baseEntity != null && (!config.generalSettings.final_hit_only || finalHit))
                {
                    switch (dispenser.baseEntity.ShortPrefabName)
                    {
                        case "wolf.corpse":
                            HandleIngredientChance(player, GatherSource.Wolf, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "stag.corpse":
                            HandleIngredientChance(player, GatherSource.Deer, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "horse.corpse":
                            HandleIngredientChance(player, GatherSource.horse, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "crocodile.corpse":
                            HandleIngredientChance(player, GatherSource.Crocodile, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "panther.corpse":
                            HandleIngredientChance(player, GatherSource.Panther, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "snake.corpse":
                            HandleIngredientChance(player, GatherSource.Snake, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                         case "tiger.corpse":
                            HandleIngredientChance(player, GatherSource.Tiger, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "shark.corpse":
                            HandleIngredientChance(player, GatherSource.Shark, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "chicken.corpse":
                            HandleIngredientChance(player, GatherSource.Chicken, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "boar.corpse":
                            HandleIngredientChance(player, GatherSource.Boar, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "polarbear.corpse":
                            HandleIngredientChance(player, GatherSource.PolarBear, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;

                        case "bear.corpse":
                            HandleIngredientChance(player, GatherSource.Bear, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                            break;
                    }

                    if (!(dispenser.baseEntity is LootableCorpse))
                        HandleIngredientChance(player, GatherSource.AnyAnimal, false, buffManager, dispenser.baseEntity.net.ID.Value, modifier);
                }
            }
        }

        void OnCollectiblePickup(CollectibleEntity entity, BasePlayer player, bool eat)
        {
            if (player == null || entity == null || player.IsNpc || !player.userID.IsSteamId() || entity.itemList == null) return;

            var buffManager = GetBuffManager(player, false);
            HandleCollectibleIngredientRoll(player, entity.ShortPrefabName, false, buffManager, entity);
            if (buffManager == null) return;

            float value;
            if (buffManager.GetBuffModifiers.TryGetValue(Buff.Harvest, out value))
            {
                foreach (var item in entity.itemList)
                {
                    if (item == null) continue;
                    item.amount += Convert.ToInt32(Math.Round(value * item.amount));
                }
            }            
        }

        void OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
        {
            if (player.IsNpc || !player.userID.IsSteamId()) return;

            if (plant.State != PlantProperties.State.Ripe) return;
            var buffManager = GetBuffManager(player, false);
            HandleCollectibleIngredientRoll(player, plant.ShortPrefabName, true, buffManager, plant);
            if (buffManager == null) return;

            float value;
            if (buffManager.GetBuffModifiers.TryGetValue(Buff.Farming_Yield, out value))
            {
                var quantity = Convert.ToInt32(Math.Round(value * item.amount));
                item.amount += quantity;
            }            
        }

        void HandleCollectibleIngredientRoll(BasePlayer player, string shortname, bool grown, BuffManager buffManager, BaseEntity entity)
        {
            if (!grown)
            {
                switch (shortname)
                {
                    case "pumpkin-collectable":
                        HandleIngredientChance(player, GatherSource.Pumpkin, false, buffManager, entity.net.ID.Value);
                        break;

                    case "potato-collectable":
                        HandleIngredientChance(player, GatherSource.Potato, false, buffManager, entity.net.ID.Value);
                        break;

                    case "corn-collectable":
                        HandleIngredientChance(player, GatherSource.Corn, false, buffManager, entity.net.ID.Value);
                        break;

                    case "hemp-collectable":
                        HandleIngredientChance(player, GatherSource.Hemp, false, buffManager, entity.net.ID.Value);
                        break;

                    case "berry-black-collectable":
                        HandleIngredientChance(player, GatherSource.BerryBushBlack, false, buffManager, entity.net.ID.Value);
                        HandleIngredientChance(player, GatherSource.BerryBush, false, buffManager, entity.net.ID.Value);
                        break;

                    case "berry-blue-collectable":
                        HandleIngredientChance(player, GatherSource.BerryBushBlue, false, buffManager, entity.net.ID.Value);
                        HandleIngredientChance(player, GatherSource.BerryBush, false, buffManager, entity.net.ID.Value);
                        break;

                    case "berry-green-collectable":
                        HandleIngredientChance(player, GatherSource.BerryBushGreen, false, buffManager, entity.net.ID.Value);
                        HandleIngredientChance(player, GatherSource.BerryBush, false, buffManager, entity.net.ID.Value);
                        break;

                    case "berry-red-collectable":
                        HandleIngredientChance(player, GatherSource.BerryBushRed, false, buffManager, entity.net.ID.Value);
                        HandleIngredientChance(player, GatherSource.BerryBush, false, buffManager, entity.net.ID.Value);
                        break;

                    case "berry-white-collectable":
                        HandleIngredientChance(player, GatherSource.BerryBushWhite, false, buffManager, entity.net.ID.Value);
                        HandleIngredientChance(player, GatherSource.BerryBush, false, buffManager, entity.net.ID.Value);
                        break;

                    case "berry-yellow-collectable":
                        HandleIngredientChance(player, GatherSource.BerryBushYellow, false, buffManager, entity.net.ID.Value);
                        HandleIngredientChance(player, GatherSource.BerryBush, false, buffManager, entity.net.ID.Value);
                        break;

                    case "sulfur-collectable":
                        HandleIngredientChance(player, GatherSource.CollectableSulfur, false, buffManager, entity.net.ID.Value);
                        break;

                    case "metal-collectable":
                        HandleIngredientChance(player, GatherSource.CollectableMetal, false, buffManager, entity.net.ID.Value);
                        break;

                    case "stone-collectable":
                        HandleIngredientChance(player, GatherSource.CollectableStone, false, buffManager, entity.net.ID.Value);
                        break;

                    case "mushroom-cluster-5":
                    case "mushroom-cluster-6":
                        HandleIngredientChance(player, GatherSource.Mushroom, false, buffManager, entity.net.ID.Value);
                        break;

                    case "Wheat":
                        HandleIngredientChance(player, GatherSource.Wheat, false, buffManager, entity.net.ID.Value);
                        break;
                }
                return;
            }            

            switch (shortname)
            {
                case "pumpkin.entity":
                    HandleIngredientChance(player, GatherSource.Pumpkin, true, buffManager, entity.net.ID.Value);
                    break;

                case "potato.entity":
                    HandleIngredientChance(player, GatherSource.Potato, true, buffManager, entity.net.ID.Value);
                    break;

                case "corn.entity":
                    HandleIngredientChance(player, GatherSource.Corn, true, buffManager, entity.net.ID.Value);
                    break;

                case "hemp.entity":
                    HandleIngredientChance(player, GatherSource.Hemp, true, buffManager, entity.net.ID.Value);
                    break;

                case "black_berry.entity":
                    HandleIngredientChance(player, GatherSource.BerryBushBlack, true, buffManager, entity.net.ID.Value);
                    HandleIngredientChance(player, GatherSource.BerryBush, true, buffManager, entity.net.ID.Value);
                    break;

                case "blue_berry.entity":
                    HandleIngredientChance(player, GatherSource.BerryBushBlue, true, buffManager, entity.net.ID.Value);
                    HandleIngredientChance(player, GatherSource.BerryBush, true, buffManager, entity.net.ID.Value);
                    break;

                case "green_berry.entity":
                    HandleIngredientChance(player, GatherSource.BerryBushGreen, true, buffManager, entity.net.ID.Value);
                    HandleIngredientChance(player, GatherSource.BerryBush, true, buffManager, entity.net.ID.Value);
                    break;

                case "red_berry.entity":
                    HandleIngredientChance(player, GatherSource.BerryBushRed, true, buffManager, entity.net.ID.Value);
                    HandleIngredientChance(player, GatherSource.BerryBush, true, buffManager, entity.net.ID.Value);
                    break;

                case "yellow_berry.entity":
                    HandleIngredientChance(player, GatherSource.BerryBushYellow, true, buffManager, entity.net.ID.Value);
                    HandleIngredientChance(player, GatherSource.BerryBush, true, buffManager, entity.net.ID.Value);
                    break;

                case "white_berry.entity":
                    HandleIngredientChance(player, GatherSource.BerryBushWhite, true, buffManager, entity.net.ID.Value);
                    HandleIngredientChance(player, GatherSource.BerryBush, true, buffManager, entity.net.ID.Value);
                    break;

                case "wheat.entity":
                    HandleIngredientChance(player, GatherSource.Wheat, true, buffManager, entity.net.ID.Value);
                    break;
            }
        }        
        void OnFishCatch(Item item, BaseFishingRod rod, BasePlayer player)
        {
            var buffManager = GetBuffManager(player, false);
            HandleIngredientChance(player, GatherSource.Fishing, buffManager);
            if (buffManager == null) return;

            float value;
            if (buffManager.GetBuffModifiers.TryGetValue(Buff.Fishing_Luck, out value))
            {
                string randItem;
                if (config.generalSettings.buffSettings.fishingLuckSettings.use_defined_loottable)
                    randItem = config.generalSettings.buffSettings.fishingLuckSettings.definedLootTable.GetRandom();
                else randItem = GetRandomLoot(config.generalSettings.buffSettings.fishingLuckSettings.non_defined_loottable_blacklist);

                if (string.IsNullOrEmpty(randItem)) return;
                var newItem = ItemManager.CreateByName(randItem, Math.Max(1, UnityEngine.Random.Range(config.generalSettings.buffSettings.fishingLuckSettings.min, config.generalSettings.buffSettings.fishingLuckSettings.max + 1)));
                if (newItem == null) return;

                if (TriggerNotificationsEnabled(player.UserIDString)) PrintToChat(player, string.Format(lang.GetMessage("FishingLuckMessage", this, player.UserIDString), newItem.amount.ToString(), newItem.info.displayName.english));

                GiveItem(player, newItem);
            }            
        }

        void CanCatchFish(BasePlayer player, BaseFishingRod fishingRod, Item fish)
        {
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return;

            float value;
            if (!buffManager.GetBuffModifiers.TryGetValue(Buff.Fishing_Yield, out value)) return;

            RollExtraFish(player, fish, value);
        }

        void RollExtraFish(BasePlayer player, Item fish, float value)
        {
            var extraFish = 0;

            for (int i = 1; i < 999; i++)
            {
                if (i < value) extraFish++;
                else break;
            }
            float leftOver = value - extraFish;
            if (leftOver <= 0) return;

            var roll = UnityEngine.Random.Range(0f, 100f);
            if (roll > 100f - (leftOver / 1 * 100))
            {
                extraFish++;
            }
            if (extraFish < 1) return;
            if (TriggerNotificationsEnabled(player.UserIDString)) PrintToChat(player, string.Format(lang.GetMessage("Fishing_Yield_Proc_Msg", this, player.UserIDString), extraFish, fish.info.displayName.english));
            fish.amount += extraFish;
        }

        List<string> RandomItems = new List<string>();
        string GetRandomLoot(List<string> blacklist)
        {
            List<string> defs = Pool.Get<List<string>>();
            defs.AddRange(RandomItems);
            foreach (var entry in blacklist)
                defs.Remove(entry);

            string randomItem = defs.GetRandom();
            Pool.FreeUnmanaged(ref defs);
            return randomItem;
        }

        #endregion

        #region Death and Damage

        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null) return;
            var buffManager = GetBuffManager(player, false);
            if (buffManager != null) RemoveBuffsOnDeath(player, buffManager);

            var cookManager = GetCookingManager(player, false);
            if (cookManager != null) DestroyCookingManager(player.userID);
            
            CuiHelper.DestroyUi(player, "HudButtons");
            if (IBagContainers.TryGetValue(player.userID, out var container)) CloseIngredientBag(player, container);

            HandleBagDrop(player);

            if (info == null) return;
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            if (config.menuSettings.hudButtonSettings.enable) CreatButtons(player);
        }

        void HandleBagDrop(BasePlayer player)
        {
            if (!config.generalSettings.ingredientBagSettings.dropOnDeath) return;
            if (!HasIngredientBag(player)) return;

            List<BagInfo> bagData;
            if (!pcdData.bag.TryGetValue(player.userID, out bagData)) return;

            if (Interface.CallHook("OnIngredientBagDrop", player) != null) return;

            var container = GetBagContainer(player);
            if (container.inventory.itemList?.Count > 0)
            {
                ulong id = player.userID;
                Vector3 pos = player.transform.position;
                Quaternion rot = player.transform.rotation;
                timer.Once(0.1f, () =>
                {
                    container.inventory.Drop("assets/prefabs/misc/item drop/item_drop.prefab", pos, rot, 0);                    
                    container.Invoke(container.KillMessage, 0.01f);
                    IBagContainers.Remove(id);
                    bagData.Clear();
                });
            }
        }

        // Removes the buffs that are configured to not persist through death.
        void RemoveBuffsOnDeath(BasePlayer player, BuffManager buffManager)
        {
            List<string> remove = Pool.Get<List<string>>();
            foreach (var meal in buffManager.activeMeals)
            {
                MealInfo mealData;
                if (!config.mealSettings.meals.TryGetValue(meal.Key, out mealData)) continue;
                if (!mealData.persistThroughDeath) remove.Add(meal.Key);                    
            }

            foreach (var meal in remove)
                buffManager.RemoveMeal(meal);

            Pool.FreeUnmanaged(ref remove);

            if (buffManager.ActiveMealCount > 0) buffManager.UpdateBuffUI();
            else CuiHelper.DestroyUi(player, "BuffUI");
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;
            var player = info.InitiatorPlayer;
            if (player == null || player.IsNpc || !player.userID.IsSteamId()) return;

            var buffManager = GetBuffManager(player, false);

            float value;
            switch (entity.ShortPrefabName)
            {
                case "oil_barrel":
                case "loot-barrel-1":
                case "loot-barrel-2":
                case "loot_barrel_1":
                case "loot_barrel_2":
                    float recipeChance;
                    if (config.mealSettings.cookbookSettings.enabled && permission.UserHasPermission(player.UserIDString, perm_recipecards) && config.mealSettings.cookbookSettings.recipeDropChance.TryGetValue(entity.ShortPrefabName, out recipeChance))
                    {
                        var roll = UnityEngine.Random.Range(0, 100f);
                        if (roll >= 100 - GetRecipeDropChance(player.UserIDString, recipeChance)) RollForRecipe(player, entity as LootContainer);
                    }
                        
                    if (buffManager != null && buffManager.GetBuffModifiers.TryGetValue(Buff.Loot_Pickup, out value))
                        HandleLootPickup(player, (LootContainer)entity, value);
                    break;
            }
        }

        object OnPlayerWound(BasePlayer player, HitInfo info)
        {
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;           
            
            float value;
            if (!buffManager.GetBuffModifiers.TryGetValue(Buff.Wounded_Resist, out value)) return null;

            NextTick(() =>
            {
                if (player == null) return;

                PrintToChat(player, string.Format(lang.GetMessage("SaveWounded", this), Math.Round(value * 100, 0)));
                buffManager.RemoveMealWithBuff(Buff.Wounded_Resist);

                player.StopWounded();

                player.metabolism.radiation_level.SetValue(0);
                player.metabolism.radiation_poison.SetValue(0);
                player.metabolism.oxygen.SetValue(1);
                player.metabolism.temperature.SetValue(15);
                player.metabolism.bleeding.SetValue(0);
                player.SetHealth(player.MaxHealth() * value);
            });

            return null;
        }

        object CanDropActiveItem(BasePlayer player)
        {
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;

            float value;
            if (!buffManager.GetBuffModifiers.TryGetValue(Buff.Wounded_Resist, out value)) return null;

            return false;
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            var horse = entity as RidableHorse;
            if (horse != null)
            {
                ModifiedHorses.Remove(horse);
                return;
            }
            var lootContainer = entity as LootContainer;
            if (lootContainer != null)
            {
                LootedContainers.Remove(lootContainer);
                return;
            }
        }

        void OnLoseCondition(Item item, ref float amount)
        {
            var player = item.GetOwnerPlayer();
            if (player == null) return;

            var buffManager = GetBuffManager(player, false);
            float value;
            if (buffManager == null || !buffManager.GetBuffModifiers.TryGetValue(Buff.Condition_Loss_Reduction, out value)) return;
            item.condition += (amount * value);
        }


        #region OnEntityTakeDamage

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || info.damageTypes == null) return null;
            var damageType = info.damageTypes.GetMajorityDamageType();
            if (damageType == Rust.DamageType.Decay) return null;
            var AttackerIsRealPlayer = info.InitiatorPlayer != null && !info.InitiatorPlayer.IsNpc && info.InitiatorPlayer.userID.IsSteamId();
            BuffManager buffManage_attacker = AttackerIsRealPlayer ? GetBuffManager(info.InitiatorPlayer, false) : null;
            float value = 0;
            float damageScale = 1f;

            #region Loot container

            var lootContainer = entity as LootContainer;
            if (lootContainer != null && AttackerIsRealPlayer && buffManage_attacker != null)
            {
                if (lootContainer.ShortPrefabName == "trash-pile-1") return null;

                if (buffManage_attacker.GetBuffModifiers.TryGetValue(Buff.Barrel_Smasher, out value) && RollSuccessful(value))
                    info.damageTypes.ScaleAll(100f);

                if (info?.damageTypes?.Total() >= lootContainer?.health)
                {
                    if (buffManage_attacker.GetBuffModifiers.TryGetValue(Buff.Wealth, out value) && RollSuccessful(value))
                    {
                        var roll = UnityEngine.Random.Range(config.generalSettings.buffSettings.wealthSettings.min_quantity, config.generalSettings.buffSettings.wealthSettings.max_quantity + 1);
                        switch (config.generalSettings.buffSettings.wealthSettings.currencyType)
                        {
                            case "economics":
                                RewardEconomics(info.InitiatorPlayer, roll);
                                break;

                            case "serverrewards":
                                RewardEconomics(info.InitiatorPlayer, roll);
                                break;

                            default:
                                AddItemToBarrel(lootContainer, "scrap", roll);
                                break;
                        }
                    }

                    if (buffManage_attacker.GetBuffModifiers.TryGetValue(Buff.Component_Luck, out value) && RollSuccessful(value))
                    {
                        var itemDef = GetRandomItemDef(ItemCategory.Component);
                        var amount = UnityEngine.Random.Range(config.generalSettings.buffSettings.componentLuckSettings.min, config.generalSettings.buffSettings.componentLuckSettings.max + 1);
                        AddItemToBarrel(lootContainer, itemDef.shortname, amount);
                    }

                    if (buffManage_attacker.GetBuffModifiers.TryGetValue(Buff.Electronics_Luck, out value) && RollSuccessful(value))
                    {
                        var itemDef = GetRandomItemDef(ItemCategory.Electrical);
                        var amount = UnityEngine.Random.Range(config.generalSettings.buffSettings.electronicsLuckSettings.min, config.generalSettings.buffSettings.electronicsLuckSettings.max + 1);
                        AddItemToBarrel(lootContainer, itemDef.shortname, amount);
                    }
                }
            }

            #endregion

            #region BasePlayer Victim
            var victim = entity as BasePlayer;
            if (victim != null)
            {
                if (victim.IsNpc || !victim.userID.IsSteamId())
                {
                    // Handle player hit npc
                    if (buffManage_attacker == null) return null;
                    
                    if (buffManage_attacker.GetBuffModifiers.TryGetValue(Buff.Lifelink, out value))
                        info.InitiatorPlayer.Heal(info.damageTypes.Total() * value);

                    return null;
                }

                // Victim is a real player
                var buffManager_victim = GetBuffManager(victim, false);
                if (buffManager_victim != null)
                {
                    if (config.generalSettings.buffSettings.healthRegenSettings.damageCooldown > 0 && buffManager_victim.GetBuffModifiers.ContainsKey(Buff.Passive_Regen))
                    {
                        if (TriggerNotificationsEnabled(victim.UserIDString)) PrintToChat(victim, String.Format(lang.GetMessage("RegenPaused", this, victim.UserIDString), config.generalSettings.buffSettings.healthRegenSettings.damageCooldown));
                        buffManager_victim.AddDamageCooldown(config.generalSettings.buffSettings.healthRegenSettings.damageCooldown);
                    }
                    
                    switch (damageType)
                    {
                        case Rust.DamageType.Fall:
                            // Fall damage
                            if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Fall_Damage_resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                            return null;

                        case Rust.DamageType.Heat:
                            // Fire damage
                            if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Fire_Resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                            return null;

                        case Rust.DamageType.Cold:
                        case Rust.DamageType.ColdExposure:
                            // Cold damage
                            if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Cold_Resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                            return null;

                        //case Rust.DamageType.Bleeding:
                        //    // Bleeding damage
                        //    if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Bleed_Resist, out value))
                        //        info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                        //    return null;

                        case Rust.DamageType.Radiation:
                        case Rust.DamageType.RadiationExposure:
                            // Radiation damage
                            if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Radiation_Resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                            return null;

                        case Rust.DamageType.Blunt:
                            if (info.WeaponPrefab != null && (info.WeaponPrefab.ShortPrefabName.Contains("grenade") || info.WeaponPrefab.ShortPrefabName.Contains("heli")) && buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Explosion_Resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                            return null;

                        case Rust.DamageType.AntiVehicle:
                        case Rust.DamageType.Explosion:
                            if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Explosion_Resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                            return null;
                    }

                    if (info.Initiator == null) return null;
                    
                    
                    if (!AttackerIsRealPlayer)
                    {
                        // Attacker is not a real player

                        var animalAttacker = info.Initiator as BaseAnimalNPC;
                        if (animalAttacker != null)
                        {
                            // Attacker is an animal
                            if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Animal_Resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));
                            return null;
                        }

                        var melee = info.WeaponPrefab as BaseMelee;
                        if (melee != null)
                        {
                            // Damaged with melee weapon
                            if (buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Melee_Resist, out value))
                                info.damageTypes.ScaleAll(Math.Max(damageScale - value, 0));

                            return null;
                        }

                        return null;
                    }

                    // attacker is real. Be sure to null check buffManage_attacker
                    var heldEntity = info.InitiatorPlayer.GetHeldEntity();
                    if (heldEntity != null && heldEntity is BaseMelee && buffManager_victim.GetBuffModifiers.TryGetValue(Buff.Melee_Resist, out value))
                        damageScale -= value;

                    if (buffManage_attacker != null)
                    {
                        if (buffManage_attacker.GetBuffModifiers.TryGetValue(Buff.Lifelink, out value))
                            info.InitiatorPlayer.Heal(info.damageTypes.Total() * value);
                    }
                }
                
            }
            #endregion

            var animal = entity as BaseAnimalNPC;
            if (animal != null)
            {
                if (buffManage_attacker != null)
                {
                    if (buffManage_attacker.GetBuffModifiers.TryGetValue(Buff.Lifelink, out value))
                        info.InitiatorPlayer.Heal(info.damageTypes.Total() * value);
                }

                return null;
            }

            return null;
        }


        #endregion

        #endregion

        #region Crafting, research and building

        void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {
            var player = crafter.owner;
            if (player == null) return;
            if (task.blueprint == null)
            {
                return;
            }

            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return;

            float value;
            if (buffManager.GetBuffModifiers.TryGetValue(Buff.Crafting_Refund, out value) && RollSuccessful(value))
            {
                var refunded = 0;
                ItemBlueprint bp;
                if (item_BPs.TryGetValue(item.info.shortname, out bp))
                {
                    foreach (var component in bp.ingredients)
                    {
                        if (config.generalSettings.buffSettings.craftingRefundSettings.blacklist.Contains(component.itemDef.shortname)) continue;
                        var nitem = ItemManager.CreateByName(component.itemDef.shortname, Convert.ToInt32(component.amount));
                        if (nitem == null) continue;
                        if (!player.inventory.containerBelt.IsFull() || !player.inventory.containerMain.IsFull())
                        {
                            GiveItem(player, nitem);
                            //player.inventory.GiveItem(nitem);
                        }
                        else nitem.DropAndTossUpwards(player.transform.position);
                        refunded++;
                    }
                    if (refunded > 0) PrintToChat(player, lang.GetMessage("CraftRefund", this, player.UserIDString));
                }
            }
            if (buffManager.GetBuffModifiers.TryGetValue(Buff.Duplicator, out value) && RollSuccessful(value) && !config.generalSettings.buffSettings.duplicatorSettings.blacklist.Contains(item.info.shortname))
            {
                var ditem = ItemManager.CreateByName(item.info.shortname, item.amount, item.skin);
                if (ditem != null)
                {
                    PrintToChat(player, string.Format(lang.GetMessage("DuplicateProc", this, player.UserIDString), item.info.displayName.english));
                    player.GiveItem(ditem);
                }
            }
        }

        object OnPayForUpgrade(BasePlayer player, BuildingBlock block, ConstructionGrade gradeTarget)
        {
            if (player == null) return null;
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;

            float value;
            if (buffManager.GetBuffModifiers.TryGetValue(Buff.Upgrade_Refund, out value) && RollSuccessful(value))
            {
                PrintToChat(player, lang.GetMessage("FreeUpgrade", this, player.UserIDString));
                return 0;
            }
            return null;
        }

        Dictionary<ResearchTable, BasePlayer> Researchers = new Dictionary<ResearchTable, BasePlayer>();
        object OnResearchCostDetermine(Item item)
        {
            ResearchTable researchTable = item.GetEntityOwner() as ResearchTable;
            if (researchTable == null) return null;

            var player = researchTable.user;
            if (player != null) Researchers[researchTable] = player;
            else
            {
                BasePlayer storedPlayer;
                if (Researchers.TryGetValue(researchTable, out storedPlayer) && storedPlayer != null) player = storedPlayer;
                else return null;
            }

            if (!researchTable.IsResearching()) return null;

            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;

            float value;

            if (buffManager.GetBuffModifiers.TryGetValue(Buff.Research_Refund, out value) && RollSuccessful(value))
            {
                PrintToChat(player, lang.GetMessage("ScrapRefund", this, player.UserIDString));
                return 0;
            }
            return null;
        }

        #endregion

        #region misc

        void OnNewSave(string filename)
        {
            if (config.wipeSettings.ResetIngredientBagOnWipe)
            {
                pcdData.bag.Clear();
            }

            if (config.wipeSettings.ResetRecipeCardsOnWipe && config.mealSettings.cookbookSettings.enabled)
            {
                foreach (var p in pcdData.pEntity)
                {
                    p.Value.learntRecipes.Clear();
                    p.Value.favouriteDishes.Clear();
                }
            }

            if (config.generalSettings.marketSettings.npcSettings.remove_on_wipe)
            {
                pcdData.npcs.Clear();
            }

            if (config.generalSettings.marketSettings.reset_market_amount >= 0)
            {
                ConfigRequiresSave = true;
                foreach (var kvp in config.ingredientSettings.ingredients)
                {
                    kvp.Value.marketInfo.availableStock = config.generalSettings.marketSettings.reset_market_amount;
                }
            }
        }

        object OnPlayerVoice(BasePlayer player, Byte[] data)
        {
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;

            if (!buffManager.GetBuffModifiers.ContainsKey(Buff.Madness)) return null;
            var nearbyPlayers = FindEntitiesOfType<BasePlayer>(player.transform.position, config.generalSettings.buffSettings.madnessSettings.distanceHeard);
            foreach (var p in nearbyPlayers)
            {
                if (p == player) continue;
                EffectNetwork.Send(new Effect(config.generalSettings.buffSettings.madnessSettings.madnessPrefab, player.transform.position, player.transform.position), p.net.connection);
            }
            Pool.FreeUnmanaged(ref nearbyPlayers);
            return true;
        }

        void OnPlayerHealthChange(BasePlayer player, float oldValue, float newValue)
        {
            if (oldValue >= newValue) return;
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return;

            float value;
            if (!buffManager.GetBuffModifiers.TryGetValue(Buff.Heal_Share, out value)) return;

            var surroundingPlayers = FindEntitiesOfType<BasePlayer>(player.transform.position, config.generalSettings.buffSettings.healShareSettings.shareDistance);
            Unsubscribe(nameof(OnPlayerHealthChange));
            var amount = (newValue - oldValue) * value;
            foreach (var p in surroundingPlayers)
            {
                p._health = Mathf.Clamp(p._health + amount, 0, p.MaxHealth());
                p.SendNetworkUpdate();
            }
            Subscribe(nameof(OnPlayerHealthChange));
            Pool.FreeUnmanaged(ref surroundingPlayers);
        }

        void OnItemRepair(BasePlayer player, Item item)
        {
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return;

            if (!buffManager.GetBuffModifiers.ContainsKey(Buff.Max_Repair)) return;

            item.maxCondition = item.info.condition.max;
            item.condition = item.maxCondition;
            item.MarkDirty();
        }

        void OnPlayerRevive(BasePlayer reviver, BasePlayer player)
        {
            var buffManager = GetBuffManager(reviver, false);
            if (buffManager == null) return;

            float value;
            if (!buffManager.GetBuffModifiers.TryGetValue(Buff.Reviver, out value)) return;
            var amount = (player._maxHealth * value) - player.health;
            player.Heal(amount);
        }

        void OnEntityMounted(BaseMountable entity, BasePlayer player)
        {
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return;

            var vehicle = entity.GetParentEntity();
            if (vehicle == null) return;

            float value;

            var horse = vehicle as RidableHorse;
            if (horse != null && buffManager.GetBuffModifiers.TryGetValue(Buff.Horse_Stats, out value))
            {
                ModifyHorse(horse, player, value);
            }

            // leaving room to add new vehicles
        }

        void OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            var vehicle = entity.GetParentEntity();
            if (vehicle == null) return;

            var horse = vehicle as RidableHorse;
            if (horse != null && ModifiedHorses.ContainsKey(horse))
                RestoreModifiedHorse(horse);

            // leaving room to add new vehicles
        }

        private object OnEntityEnter(TargetTrigger trigger, BasePlayer player)
        {
            if (player == null || player.IsNpc || !player.userID.IsSteamId()) return null;
             
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;

            if (!buffManager.GetBuffModifiers.ContainsKey(Buff.Spectre)) return null;
            return true;
        }

        #endregion

        #region Targeting

        object CanBradleyApcTarget(BradleyAPC apc, BasePlayer player)
        {
            if (player == null) return null;
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;

            if (!buffManager.GetBuffModifiers.ContainsKey(Buff.Anti_Bradley_Radar)) return null;
            return false;
        }

        #endregion

        #region permissions


        void OnUserGroupAdded(string id, string groupName)
        {
            if (permission.GroupHasPermission(groupName, perm_recipemenu_chat) || permission.GroupHasPermission(groupName, perm_bag_cmd) || permission.GroupHasPermission(groupName, perm_market_cmd))
                CheckButtonUpdate(id, perm_bag_cmd);
        }

        void OnUserGroupRemoved(string id, string groupName)
        {
            if (permission.GroupHasPermission(groupName, perm_recipemenu_chat) || permission.GroupHasPermission(groupName, perm_bag_cmd) || permission.GroupHasPermission(groupName, perm_market_cmd))
                CheckButtonUpdate(id, perm_bag_cmd);
        }

        void OnUserPermissionGranted(string id, string permName) => CheckButtonUpdate(id, permName);
        void OnUserPermissionRevoked(string id, string permName) => CheckButtonUpdate(id, permName);
        void OnGroupPermissionGranted(string name, string perm) => CheckGroupPerms(name, perm);
        void OnGroupPermissionRevoked(string name, string perm) => CheckGroupPerms(name, perm);

        void CheckGroupPerms(string name, string permName)
        {
            if (!permName.Contains("cooking")) return;
            foreach (var user in permission.GetUsersInGroup(name))
                CheckButtonUpdate(user.Split(' ')[0], permName);
        }

        void CheckButtonUpdate(string id, string permName)
        {
            var player = BasePlayer.Find(id);
            if (player == null) return;

            if (permName.Equals(perm_recipemenu_chat, StringComparison.OrdinalIgnoreCase) || permName.Equals(perm_bag_cmd, StringComparison.OrdinalIgnoreCase) || permName.Equals(perm_market_cmd, StringComparison.OrdinalIgnoreCase))
                CreatButtons(player);
        }

        #endregion

        #region Stacks

        private object OnItemSplit(Item item, int amount)
        {
            if (!IsCookingMeal(item) && !IsCustomIngredient(item)) return null;

            Item x = ItemManager.CreateByItemID(item.info.itemid);
            item.amount -= amount;
            x.name = item.name;
            x.skin = item.skin;
            x.amount = amount;
            if (IsSpoilingItem(item)) x.instanceData.dataFloat = item.instanceData.dataFloat;
            x.MarkDirty();

            item.MarkDirty();
            return x;
        }

        #endregion

        #endregion

        #region helpers

        static bool IsSpoilingItem(Item item)
        {
            return item.info.GetComponent<ItemModFoodSpoiling>() != null;
        }

        List<Item> AllItems(BasePlayer player)
        {
            List<Item> result = Pool.Get<List<Item>>();

            if (player.inventory.containerMain?.itemList != null)
                result.AddRange(player.inventory.containerMain.itemList);

            if (player.inventory.containerBelt?.itemList != null)
                result.AddRange(player.inventory.containerBelt.itemList);

            if (player.inventory.containerWear?.itemList != null)
                result.AddRange(player.inventory.containerWear.itemList);

            return result;
        }

        List<LootContainer> looted_containers = new List<LootContainer>();
        List<ItemDefinition> component_item_list = new List<ItemDefinition>();
        List<ItemDefinition> electrical_item_list = new List<ItemDefinition>();
        Dictionary<string, ItemBlueprint> item_BPs = new Dictionary<string, ItemBlueprint>();
        Dictionary<string, ItemDefinition> ItemDefs = new Dictionary<string, ItemDefinition>();

        BasePlayer FindPlayer(string nameOrID)
        {
            var target = BasePlayer.Find(nameOrID);
            if (target == null) target = BasePlayer.activePlayerList.FirstOrDefault(x => x.displayName.Contains(nameOrID));
            return target;
        }

        void RollForRecipe(BasePlayer player, LootContainer container)
        {
            if (container == null) return;
            if (Interface.CallHook("OnAddRecipeCardToLootContainer", player, container) != null) return;

            string recipe;
            if (!pcdData.pEntity.TryGetValue(player.userID, out var playerData)) pcdData.pEntity.Add(player.userID, playerData = new PCDInfo());

            List<KeyValuePair<string, MealInfo>> learnable = Pool.Get<List<KeyValuePair<string, MealInfo>>>();

            foreach (var meal in config.mealSettings.meals)
            {
                if (!meal.Value.enabled) continue;
                if (meal.Value.recipeDropWeight <= 0) continue;
                if (config.mealSettings.cookbookSettings.defaultMeals.Contains(meal.Key, StringComparer.OrdinalIgnoreCase)) continue;
                if (!config.mealSettings.cookbookSettings.allowFindDuplicates && playerData.learntRecipes.Contains(meal.Key, StringComparer.OrdinalIgnoreCase)) continue;
                learnable.Add(meal);
            }

            if (learnable.Count == 0)
            {
                Pool.FreeUnmanaged(ref learnable);
                return;
            }

            recipe = RollForRecipe(learnable);
            Pool.FreeUnmanaged(ref learnable);

            var item = ItemManager.CreateByName(config.mealSettings.cookbookSettings.blueprint.shortname, 1, config.mealSettings.cookbookSettings.blueprint.skin);
            if (item == null) return;

            if (!string.IsNullOrEmpty(config.mealSettings.cookbookSettings.blueprint.displayName)) item.name = string.Format(config.mealSettings.cookbookSettings.blueprint.displayName, recipe).ToLower();
            item.text = recipe.ToLower();

            var mealData = config.mealSettings.meals[recipe];
            string message = "\n" + lang.GetMessage("ShowMealBuffDescriptions", this, player.UserIDString);
            foreach (var buff in mealData.buffs)
            {
                message += $"\n{GetBuffDescriptions(player, buff.Key, buff.Value)}";
            }

            if (container.inventory.itemList != null && container.inventory.itemList.Count >= 6)
            {//FoundNewRecipeMealDescription
                
                player.GiveItem(item);
                if (TriggerNotificationsEnabled(player.UserIDString)) PrintToChat(player, String.Format(lang.GetMessage("FoundNewRecipe", this, player.UserIDString), lang.GetMessage(recipe, this, player.UserIDString)) + lang.GetMessage("FoundNewRecipeAppendContainerFull", this, player.UserIDString) + (config.mealSettings.cookbookSettings.describeMeal ? message : null));
                return;
            }

            container.inventorySlots++;
            container.inventory.capacity++;
            
            if (!item.MoveToContainer(container.inventory)) player.GiveItem(item);
            if (TriggerNotificationsEnabled(player.UserIDString)) PrintToChat(player, String.Format(lang.GetMessage("FoundNewRecipe", this, player.UserIDString), lang.GetMessage(recipe, this, player.UserIDString)) + (config.mealSettings.cookbookSettings.describeMeal ? message : null));
        }

        string RollForRecipe(List<KeyValuePair<string, MealInfo>> meals)
        {
            var totalWeight = meals.Sum(x => x.Value.recipeDropWeight);
            var roll = UnityEngine.Random.Range(0, totalWeight + 1);
            var count = 0;
            foreach (var meal in meals)
            {
                count += meal.Value.recipeDropWeight;
                if (count < roll) continue;

                return meal.Key;
            }

            return meals[UnityEngine.Random.Range(0, meals.Count - 1)].Key;
        }

        bool IsRecipeCard(Item item) => !string.IsNullOrEmpty(item.text) && config.mealSettings.meals.ContainsKey(item.text);

        private BaseEntity GetTargetEntity(BasePlayer player)
        {
            RaycastHit raycastHit;
            bool flag = Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 5, -1);
            var targetEntity = flag ? raycastHit.GetEntity() : null;
            return targetEntity;
        }

        bool IngredientIsMatch(Item item, string ingredient, IngredientsInfo ingredientData)
        {
            if (item.info.shortname == ingredientData.shortname && item.skin == ingredientData.skin && ((item.name != null && item.name.Equals(ingredient, StringComparison.OrdinalIgnoreCase)) || ingredient.Equals(item.info.displayName.english, StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        void RemoveFromCurrentTargets(BasePlayer player)
        {
            var entities = FindEntitiesOfType<StorageContainer>(player.transform.position, 20);
            foreach (var entity in entities)
            {
                var guntrap = entity as GunTrap;
                if (guntrap != null)
                {
                    player.LeaveTrigger(guntrap.trigger);
                    continue;
                }

                var flameTurret = entity as FlameTurret;
                if (flameTurret != null)
                {                   
                    player.LeaveTrigger(flameTurret.trigger);
                    continue;
                }
            }
            Pool.FreeUnmanaged(ref entities);

            var turrets = FindEntitiesOfType<AutoTurret>(player.transform.position, 30);
            foreach (var turret in turrets)
            {
                turret.targetTrigger.OnEntityLeave(player);
                //player.LeaveTrigger(turret.targetTrigger);
                turret.SetTarget(null);
            }

            Pool.FreeUnmanaged(ref turrets);
        }

        public class HorseInfo
        {
            public BasePlayer player;
            public float modifier;

            public HorseInfo(BasePlayer player, float modifier)
            {
                this.player = player;
                this.modifier = modifier;
            }
        }

        Dictionary<RidableHorse, HorseInfo> ModifiedHorses = new Dictionary<RidableHorse, HorseInfo>();

        void ModifyHorse(RidableHorse horse, BasePlayer player, float modifier)
        {
            if (Interface.CallHook("RecipeCanModifyHorse", horse) != null) return;
            if (ModifiedHorses.TryGetValue(horse, out var existingHorse))
            {
                if (modifier > existingHorse.modifier) RestoreModifiedHorse(horse);
                else return;
            }
            ModifiedHorses.Add(horse, new HorseInfo(player, modifier));

            float baseSpeed = (horse.gaits[horse.gaits.Length - 1].maxSpeed + horse.equipmentSpeedMod) * horse.currentBreed.maxSpeed;
            horse.modifiers.Add(new List<ModifierDefintion>()
            {
                new ModifierDefintion
                {
                    type = Modifier.ModifierType.HorseGallopSpeed,
                    duration = 36000,
                    source = Modifier.ModifierSource.Interaction,
                    value = modifier * baseSpeed
                }
            });
            horse.modifiers.ServerUpdate(horse);
        }

        void RestoreModifiedHorse(RidableHorse horse, bool removeFromList = true)
        {
            if (horse == null || horse.IsDead()) return;
            HorseInfo restoreData;
            if (!ModifiedHorses.TryGetValue(horse, out restoreData)) return;
            horse.modifiers.All.Clear();
            horse.modifiers.ServerUpdate(horse);
            if (removeFromList) ModifiedHorses.Remove(horse);
        }

        void SendEffect(BasePlayer player, string effect)
        {
            if (permission.UserHasPermission(player.UserIDString, perm_disable_sound)) return;
            EffectNetwork.Send(new Effect(effect, player.transform.position, player.transform.position), player.net.connection);
        }

        private static List<T> FindEntitiesOfType<T>(Vector3 a, float n, int m = -1) where T : BaseEntity
        {
            int hits = Physics.OverlapSphereNonAlloc(a, n, Vis.colBuffer, m, QueryTriggerInteraction.Collide);
            List<T> entities = Pool.Get<List<T>>();
            for (int i = 0; i < hits; i++)
            {
                var entity = Vis.colBuffer[i]?.ToBaseEntity() as T;
                if (entity != null && !entities.Contains(entity)) entities.Add(entity);
                Vis.colBuffer[i] = null;
            }
            return entities;
        }

        public Biome GetBiome(Vector3 pos)
        {
            if (TerrainMeta.BiomeMap.GetBiome(pos, 1) > 0.5f) return Biome.Arid;
            else if (TerrainMeta.BiomeMap.GetBiome(pos, 2) > 0.5f) return Biome.Temperate;
            else if (TerrainMeta.BiomeMap.GetBiome(pos, 4) > 0.5f) return Biome.Tundra;
            else if (TerrainMeta.BiomeMap.GetBiome(pos, 8) > 0.5f) return Biome.Arctic;
            else if (TerrainMeta.BiomeMap.GetBiome(pos, 16) > 0.5f) return Biome.Jungle;
            else return Biome.Temperate;
        }

        float GetLuckMultiplier(BasePlayer player, float baseValue)
        {
            float highest = 0;            
            foreach (var perm in config.generalSettings.permissionSettings.DropChanceMultiplier)
            {
                if (perm.Value > highest && permission.UserHasPermission(player.UserIDString, perm.Key)) 
                    highest = perm.Value;
            }
            
            float skillTreeLuck = 0;
            foreach (var perm in SkillTreePermission_IngredientChance)
            {
                if (perm.Value > skillTreeLuck && permission.UserHasPermission(player.UserIDString, perm.Key)) skillTreeLuck = perm.Value;
            }
            return baseValue += baseValue * (highest + skillTreeLuck);
        }

        void HandleIngredientChance(BasePlayer player, GatherSource source, bool player_grown, BuffManager buffManager = null, ulong sourceEntityID = 0, float modifier = 1)
        {
            if (!IngredientSources.TryGetValue(source, out var ingredientDrops)) return;

            // No gather prevents ingredient drops. Will use this perm to prevent drops when set by the player.
            if (permission.UserHasPermission(player.UserIDString, perm_nogather) || !permission.UserHasPermission(player.UserIDString, perm_gather)) return;
            if (!string.IsNullOrEmpty(config.generalSettings.grown_permission) && !permission.UserHasPermission(player.UserIDString, config.generalSettings.grown_permission)) return;

            if (sourceEntityID > 0)
            {
                object hookResult = Interface.CallHook("CanGatherIngredient", player, sourceEntityID);
                if (hookResult is bool && (bool)hookResult == false) return;
            }            

            if (!config.generalSettings.dropSources.TryGetValue(source, out var gatherData) || (!string.IsNullOrEmpty(gatherData.permission_required) && !permission.UserHasPermission(player.UserIDString, gatherData.permission_required))) return;
            float baseLuck = gatherData.chance;

            if (buffManager == null)
                buffManager = GetBuffManager(player, false);            

            baseLuck = baseLuck * modifier;

            if (player_grown)
                baseLuck = baseLuck * config.generalSettings.chance_modifier_grown;
            
            float modifiedLuck = GetLuckMultiplier(player, baseLuck);            
            float luckMod;
            if (buffManager != null && buffManager.GetBuffModifiers.TryGetValue(Buff.Ingredient_Chance, out luckMod))
                modifiedLuck += (baseLuck * luckMod);
            
            if (!RollSuccessful(modifiedLuck)) return;
            var drop = GetDrop(ingredientDrops);
            IngredientsInfo ingredientData;
            if (!config.ingredientSettings.ingredients.TryGetValue(drop, out ingredientData)) return;

            var item = ItemManager.CreateByName(ingredientData.shortname, UnityEngine.Random.Range(ingredientData.minDrops, ingredientData.maxDrops + 1), ingredientData.skin);
            if (ingredientData.skin > 0)
            {
                item.name = drop.ToLower();
                item.text = drop.ToLower();
            }
            if (config.generalSettings.ingredientBagSettings.gather_directly_into_bag && IsValidItemForBag(item) && HasIngredientBag(player) && !IsLootingIngredientBag(player)) PlaceIngredientIntoBag(player, item);
            else
            {
                if (IsSpoilingItem(item)) item.instanceData.dataFloat = 3600 * ingredientData.spoilTime;
                if (DropNotificationsEnabled(player.UserIDString)) PrintToChat(player, lang.GetMessage("IngredientFoundMessage", this, player.UserIDString), item.amount.ToString(), item.name != null ? lang.GetMessage(item.name, this, player.UserIDString) : item.info.displayName.english);
                GiveItem(player, item);
            }
        }

        void HandleIngredientChance(BasePlayer player, LootContainer container, GatherSource source, BuffManager buffManager = null, ulong sourceEntityID = 0, float modifier = 1)
        {
            // No gather prevents ingredient drops. Will use this perm to prevent drops when set by the player.
            if (permission.UserHasPermission(player.UserIDString, perm_nogather) || !permission.UserHasPermission(player.UserIDString, perm_gather)) return;

            List<string> ingredientDrops;
            if (!IngredientSources.TryGetValue(source, out ingredientDrops)) return;

            if (!config.generalSettings.dropSources.TryGetValue(source, out var gatherData) || (!string.IsNullOrEmpty(gatherData.permission_required) && !permission.UserHasPermission(player.UserIDString, gatherData.permission_required))) return;
            float baseLuck = gatherData.chance;

            if (buffManager == null)
                buffManager = GetBuffManager(player, false);

            baseLuck = baseLuck * modifier;

            float modifiedLuck = GetLuckMultiplier(player, baseLuck);
            float luckMod;
            if (buffManager != null && buffManager.GetBuffModifiers.TryGetValue(Buff.Ingredient_Chance, out luckMod))
                modifiedLuck += (baseLuck * luckMod);

            if (!RollSuccessful(modifiedLuck)) return;
            var drop = GetDrop(ingredientDrops);
            IngredientsInfo ingredientData;
            if (!config.ingredientSettings.ingredients.TryGetValue(drop, out ingredientData)) return;

            var item = ItemManager.CreateByName(ingredientData.shortname, UnityEngine.Random.Range(ingredientData.minDrops, ingredientData.maxDrops + 1), ingredientData.skin);
            if (ingredientData.skin > 0)
            {
                item.name = drop.ToLower();
                item.text = drop.ToLower();
            }

            container.inventory.capacity++;
            if (item.MoveToContainer(container.inventory))
            {
                if (IsSpoilingItem(item)) item.instanceData.dataFloat = 3600 * ingredientData.spoilTime;
                if (DropNotificationsEnabled(player.UserIDString)) PrintToChat(player, lang.GetMessage("IngredientFoundMessage", this, player.UserIDString), item.amount.ToString(), item.name != null ? lang.GetMessage(item.name, this, player.UserIDString) : item.info.displayName.english);
                return;
            }

            if (config.generalSettings.ingredientBagSettings.gather_directly_into_bag && IsValidItemForBag(item) && HasIngredientBag(player) && !IsLootingIngredientBag(player)) PlaceIngredientIntoBag(player, item);
            else
            {
                if (IsSpoilingItem(item)) item.instanceData.dataFloat = 3600 * ingredientData.spoilTime;
                if (DropNotificationsEnabled(player.UserIDString)) PrintToChat(player, lang.GetMessage("IngredientFoundMessage", this, player.UserIDString), item.amount.ToString(), item.name != null ? lang.GetMessage(item.name, this, player.UserIDString) : item.info.displayName.english);
                GiveItem(player, item);
            }
        }

        Dictionary<string, int> DropWeightReference = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
        bool DropNotificationsEnabled(string userid)
        {
            return !permission.UserHasPermission(userid, perm_disable_notify_drop);
        }

        bool TriggerNotificationsEnabled(string userid)
        {
            return !permission.UserHasPermission(userid, perm_disable_notify_proc);
        }

        string GetDrop(List<string> drops)
        {
            var totalDropWeight = 0;

            List<string> errors = Pool.Get<List<string>>();

            foreach (var drop in drops)
            {
                int weight;
                if (!DropWeightReference.TryGetValue(drop, out weight))
                {
                    errors.Add(drop);
                    continue;
                }
                totalDropWeight += weight;
            }
            foreach (var error in errors)
                DropWeightReference.Remove(error);

            Pool.FreeUnmanaged(ref errors);

            var roll = UnityEngine.Random.Range(0, totalDropWeight + 1);
            var count = 0;
            foreach (var drop in drops)
            {
                count += DropWeightReference[drop];
                if (roll <= count) return drop;
            }

            //​‌​​‌‌​‌​‌​‌​‌​​​‌‌​​​‌‌​‌‌‌‌​​​​‌​​‌‌‌​​‌‌​​‌‌‌​​‌‌‌‌​‌​​‌‌‌‌​‌ Failed to find
            return drops.GetRandom();
        }

        private static bool InRange(Vector3 a, Vector3 b, float distance)
        {
            return (a - b).sqrMagnitude <= distance * distance;
        }

        Dictionary<BasePlayer, bool> LastMagnetSuccess = new Dictionary<BasePlayer, bool>();
        void HandleLootPickup(BasePlayer player, LootContainer entity, float chance)
        {
            if (entity.inventory?.itemList != null && entity.inventory.itemList.Count > 0)
            {
                if (!LastMagnetSuccess.ContainsKey(player)) LastMagnetSuccess.Add(player, false);

                if (RollSuccessful(chance))
                {
                    if (config.generalSettings.buffSettings.lootPickupSettings.maxDistance > 0 && !InRange(entity.transform.position, player.transform.position, config.generalSettings.buffSettings.lootPickupSettings.maxDistance))
                    {
                        LastMagnetSuccess[player] = false;
                        return;
                    }

                    List<Item> item_drops = Pool.Get<List<Item>>();
                    if (!config.generalSettings.buffSettings.lootPickupSettings.meleeOnly || (player.GetHeldEntity() != null && player.GetHeldEntity() is BaseMelee))
                    {
                        item_drops.AddRange(entity.inventory.itemList);

                        BasePlayer _player = player;
                        NextTick(() =>
                        {
                            if (_player == null)
                            {
                                Pool.FreeUnmanaged(ref item_drops);
                                return;
                            }
                            foreach (var item in item_drops)
                            {
                                var parent = item.GetOwnerPlayer();
                                if (parent == null)
                                {
                                    GiveItem(_player, item);
                                }
                            }
                            LastMagnetSuccess[player] = false;
                            Pool.FreeUnmanaged(ref item_drops);
                        });
                    }
                    LastMagnetSuccess[player] = true;
                }
                else LastMagnetSuccess[player] = false;
            }
        }

        void HandleSmeltOnMine(BasePlayer player, Item item)
        {
            var refined = GetRefinedMaterial(item.info.shortname);
            if (!string.IsNullOrEmpty(refined))
            {
                GiveItem(player, ItemManager.CreateByName(refined, Math.Max(item.amount, 1)));
                item.amount = 0;
                item.Remove();
            }
        }

        string GetRefinedMaterial(string shortname)
        {
            switch (shortname)
            {
                case "hq.metal.ore": return "metal.refined";
                case "metal.ore": return "metal.fragments";
                case "sulfur.ore": return "sulfur";
                default: return null;
            }
        }

        void GiveItem(BasePlayer player, Item item)
        {
            if (player == null)
            {
                item.Remove();
                return;
            }
            if (player.inventory.containerMain.itemList != null)
            {
                var moved = false;
                int amount = item.amount;
                foreach (var _item in player.inventory.containerMain.itemList)
                {
                    if (_item.info.stackable < 2) continue;
                    if (_item.skin == item.skin && _item.info.shortname == item.info.shortname)
                    {
                        if ((item.name != null || _item.name != null) && item.name != _item.name) continue;
                        if (item.MoveToContainer(player.inventory.containerMain, _item.position)) moved = true;
                    }
                    if (moved)
                    {
                        player.Command("note.inv", new object[] { item.info.itemid, amount, item.name != null ? item.name : String.Empty, (int)BaseEntity.GiveItemReason.PickedUp });
                        return;
                    }

                }

                foreach (var _item in player.inventory.containerBelt.itemList)
                {
                    if (_item.info.stackable < 2) continue;
                    if (_item.skin == item.skin && _item.info.shortname == item.info.shortname)
                    {
                        if ((item.name != null || _item.name != null) && item.name != _item.name) continue;
                        if (item.MoveToContainer(player.inventory.containerBelt, _item.position)) moved = true;
                    }
                    if (moved)
                    {
                        player.Command("note.inv", new object[] { item.info.itemid, amount, item.name != null ? item.name : String.Empty, (int)BaseEntity.GiveItemReason.PickedUp });
                        return;
                    }

                }

                player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
            }

        }

        ItemDefinition GetRandomItemDef(ItemCategory category)
        {
            if (category == ItemCategory.Component) return component_item_list.GetRandom();
            if (category == ItemCategory.Electrical) return electrical_item_list.GetRandom();
            return null;
        }

        void AddItemToBarrel(LootContainer container, string shortname, int amount)
        {
            container.inventory.capacity++;
            var item = ItemManager.CreateByName(shortname, amount);
            if (!item.MoveToContainer(container.inventory)) item.Remove();
        }


        void RewardEconomics(BasePlayer player, int amount)
        {
            if (Economics == null || !Economics.IsLoaded) return;
            if (!Convert.ToBoolean(Economics.Call("Deposit", player.UserIDString, (double)amount)))
            {
                Puts($"Failed to give {player.displayName} their economics dollars for some reason.");
            }
        }

        void RewardServerRewards(BasePlayer player, int amount)
        {
            if (ServerRewards == null || !ServerRewards.IsLoaded) return;
            if (!Convert.ToBoolean(ServerRewards.Call("AddPoints", player.UserIDString, amount)))
            {
                Puts($"Failed to give {player.displayName} their server rewards points for some reason.");
            }
        }

        bool RollSuccessful(float luck)
        {
            var roll = UnityEngine.Random.Range(0f, 100f);
            return (roll >= 100f - (luck * 100));
        }

        void GiveMeal(BasePlayer player, string shortname, ulong skin, string displayName, float spoilTime, int amount = 1)
        {
            var item = ItemManager.CreateByName(shortname, amount, skin);
            if (skin > 0)
            {
                item.name = displayName.ToLower();
                item.text = displayName.ToLower();
            }
            if (IsSpoilingItem(item)) item.instanceData.dataFloat = 3600 * spoilTime;
            player.GiveItem(item);
        }

        void PayForMeal(BasePlayer player, int quantity, Dictionary<string, int> ingredients, Dictionary<string, IngredientItemInfo> ingredientsUsed)
        {
            if (ingredients == null || ingredients.Count == 0) return;

            List<BagInfo> bagData;
            pcdData.bag.TryGetValue(player.userID, out bagData);

            foreach (var ingredient in ingredients)
            {
                IngredientItemInfo ingredientsConsumed;
                ingredientsUsed.Add(ingredient.Key, ingredientsConsumed = new IngredientItemInfo());
                var found = 0;
                if (bagData != null)
                {
                    List<BagInfo> entries_to_delete = Pool.Get<List<BagInfo>>();
                    foreach (var entry in bagData)
                    {
                        if (entry.displayName.Equals(ingredient.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (entry.amount >= ingredient.Value - found)
                            {
                                ingredientsConsumed.amount += ingredient.Value - found;
                                ingredientsConsumed.CheckSpoilTime(entry.spoilTime);
                                entry.amount -= ingredient.Value - found;
                                found = ingredient.Value;
                            }
                            else
                            {
                                ingredientsConsumed.amount += entry.amount;
                                ingredientsConsumed.CheckSpoilTime(entry.spoilTime);
                                found += entry.amount;
                                entry.amount = 0;
                                entries_to_delete.Add(entry);
                            }
                        }                       

                        if (found >= ingredient.Value) break;
                    }

                    foreach (var entry in entries_to_delete)
                    {
                        bagData.Remove(entry);
                    }

                    Pool.FreeUnmanaged(ref entries_to_delete);
                }

                if (found >= ingredient.Value)
                {
                    ingredientsConsumed.amount = ingredientsConsumed.amount / quantity;
                    continue;
                }

                var allItems = AllItems(player);
                foreach (var item in allItems)
                {
                    if ((item.name != null && item.name.Equals(ingredient.Key, StringComparison.OrdinalIgnoreCase)) || ((item.info.displayName.english.Equals(ingredient.Key, StringComparison.OrdinalIgnoreCase) && item.skin == 0)))
                    {
                        if (item.amount >= ingredient.Value - found)
                        {
                            ingredientsConsumed.amount += ingredient.Value - found;
                            ingredientsConsumed.CheckSpoilTime(item);
                            UseItem(item, ingredient.Value - found);
                            found = ingredient.Value;
                        }
                        else
                        {
                            ingredientsConsumed.amount += item.amount;
                            ingredientsConsumed.CheckSpoilTime(item);
                            found += item.amount;
                            UseItem(item, item.amount);
                        }
                        if (found >= ingredient.Value) break;
                    }
                }
                Pool.FreeUnmanaged(ref allItems);
                ingredientsConsumed.amount = ingredientsConsumed.amount / quantity;
            }            
        }

        public void UseItem(Item item, int amountToConsume = 1)
        {
            if (amountToConsume > 0)
            {
                item.amount -= amountToConsume;
                if (item.amount <= 0)
                {
                    item.amount = 0;
                    item.Remove();
                }
                else
                {
                    item.MarkDirty();
                }
            }
        }

        int HasAllIngredients(BasePlayer player, Dictionary<string, int> ingredients)
        {
            // 0 = no ingredients. 1 = some ingredients. 2 = all ingredients.
            if (ingredients == null || ingredients.Count == 0) return 1;
            List<BagInfo> bagData;
            pcdData.bag.TryGetValue(player.userID, out bagData);
            int result = 0;            
            var successfulCount = 0;
            foreach (var ingredient in ingredients)
            {
                IngredientsInfo ingredientData;
                if (!config.ingredientSettings.ingredients.TryGetValue(ingredient.Key, out ingredientData)) continue;
                if (GetIngredientCount(player, ingredient.Key, bagData) >= ingredient.Value) successfulCount++;                
            }
            if (successfulCount == ingredients.Count) result = 2;
            else if (successfulCount > 0) result = 1;
            return result;
        }

        int GetIngredientCount(BasePlayer player, string ingredient, List<BagInfo> bagData)
        {
            var itemsFound = 0;
            if (bagData != null) itemsFound += CountIngredientsInBag(player, ingredient, bagData);
            itemsFound += CountIngredientsInInventory(player, ingredient);
            return itemsFound;
        }

        int CountIngredientsInBag(BasePlayer player, string ingredient, List<BagInfo> bagData)
        {
            int result = 0;
            if (bagData.IsNullOrEmpty()) return 0;
            foreach (var item in bagData)
            {
                if (!string.IsNullOrEmpty(item.displayName) && item.displayName.Equals(ingredient, StringComparison.OrdinalIgnoreCase)) result += item.amount;
            }
            return result;
        }

        int CountIngredientsInInventory(BasePlayer player, string ingredient)
        {
            int result = 0;
            var allItems = AllItems(player);
            foreach (var item in allItems)
            {
                if (item.name != null)
                {
                    if (item.name.Equals(ingredient, StringComparison.OrdinalIgnoreCase)) result += item.amount;
                }
                else if (item.info.displayName.english.Equals(ingredient, StringComparison.OrdinalIgnoreCase)) result += item.amount;
            }
            Pool.FreeUnmanaged(ref allItems);
            return result;
        }

        #endregion

        #region iBag 

        bool IsValidItemForBag(Item item)
        {
            if (config.generalSettings.ingredientBagSettings.whitelist.Contains(item.info.shortname)) return true;
            if (config.generalSettings.ingredientBagSettings.blacklist.Contains(item.info.shortname)) return false;
            var nameCheck = item.name ?? item.info.displayName.english;
            if (config.ingredientSettings.ingredients.ContainsKey(nameCheck)) return true;
            return false;
        }

        bool HasIngredientBag(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, perm_bag_cmd)) return true;
            var allItems = AllItems(player);
            foreach (var item in allItems)
            {
                if (!IsIngredientBag(item)) continue;
                Pool.FreeUnmanaged(ref allItems);
                return true;
            }
            Pool.FreeUnmanaged(ref allItems);
            return false;
        }

        bool IsIngredientBag(Item item)
        {
            if (config.generalSettings.ingredientBagSettings.bagItemSettings.skinID != item.skin) return false;
            if (config.generalSettings.ingredientBagSettings.bagItemSettings.shortname != item.info.shortname) return false;
            if (!string.IsNullOrEmpty(config.generalSettings.ingredientBagSettings.bagItemSettings.displayName) && (string.IsNullOrEmpty(item.name) || !item.name.Equals(config.generalSettings.ingredientBagSettings.bagItemSettings.displayName, StringComparison.OrdinalIgnoreCase))) return false;
            return true;
        }

        bool IngredientIsMatch(Item item, BagInfo slotData)
        {
            if (slotData.skin != item.skin || slotData.shortname != item.info.shortname) return false;
            if (!string.IsNullOrEmpty(slotData.displayName) && item.info.displayName.english.Equals(slotData.displayName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(slotData.displayName) && (string.IsNullOrEmpty(item.name) || item.name != slotData.displayName)) return false;
            return true;
        }

        bool PlaceIngredientIntoBag(BasePlayer player, Item item)
        {
            List<BagInfo> bagData;
            if (!pcdData.bag.TryGetValue(player.userID, out bagData)) pcdData.bag.Add(player.userID, bagData = new List<BagInfo>());

            bool partialBagfill = false;
            var amount = item.amount;
            foreach (var slot in bagData)
            {
                if (!IngredientIsMatch(item, slot)) continue;
                if (!config.generalSettings.ingredientBagSettings.consolidate_stacks)
                {
                    var excess = item.info.stackable - slot.amount;
                    if (excess < 0) continue;
                    if (excess < item.amount)
                    {
                        slot.amount += excess;
                        item.amount -= excess;
                        partialBagfill = true;
                        continue;
                    }
                }
                slot.amount += item.amount;
                if (DropNotificationsEnabled(player.UserIDString)) PrintToChat(player, String.Format(lang.GetMessage("ItemStoredInBag", this, player.UserIDString), amount, item.name != null ? lang.GetMessage(item.name, this, player.UserIDString) : item.info.displayName.english));
                item.Remove();                
                return true;
            }

            if (bagData.Count >= config.generalSettings.ingredientBagSettings.maxSlots)
            {
                // We give the item to the player instead because the bag is full.
                if (DropNotificationsEnabled(player.UserIDString)) PrintToChat(player, string.Format(lang.GetMessage("IngredientFoundMessage", this, player.UserIDString), amount.ToString(), item.name != null ? lang.GetMessage(item.name, this, player.UserIDString) : item.info.displayName.english) + (partialBagfill ? " Some items were stored in the ingredient bag." : ""));
                player.GiveItem(item);
                return false;
            }
            
            bagData.Add(new BagInfo(item.info.shortname, item.name ?? item.info.displayName.english.ToLower(), item.amount, -1, item.skin, item.instanceData?.dataFloat ?? 0, item.text));
            if (DropNotificationsEnabled(player.UserIDString)) PrintToChat(player, String.Format(lang.GetMessage("ItemStoredInBagNewSlot", this, player.UserIDString), amount, item.name != null ? lang.GetMessage(item.name, this, player.UserIDString) : item.info.displayName.english));
            item.Remove();
            return true;
        }

        void SaveIngredientBag(BasePlayer player, StorageContainer container)
        {
            List<BagInfo> bagData;
            if (!pcdData.bag.TryGetValue(player.userID, out bagData)) return;

            bagData.Clear();
            if (container.inventory.itemList.IsNullOrEmpty()) return;

            List<Item> items_to_return = Pool.Get<List<Item>>();
            foreach (var item in container.inventory.itemList)
            {
                if (!IsValidItemForBag(item))
                {
                    items_to_return.Add(item);
                    continue;
                }

                bool foundExisting = false;
                foreach (var slotData in bagData)
                {
                    if (!config.generalSettings.ingredientBagSettings.consolidate_stacks) break;
                    if (IngredientIsMatch(item, slotData))
                    {
                        foundExisting = true;
                        slotData.amount += item.amount;
                        break;
                    }
                }
                if (!foundExisting) bagData.Add(new BagInfo(item.info.shortname, item.name ?? item.info.displayName.english.ToLower(), item.amount, item.position, item.skin, item.instanceData?.dataFloat ?? 0, item.text));
            }

            foreach (var item in items_to_return)
            {
                PrintToChat(player, string.Format(lang.GetMessage("ItemReturnedBlackListed", this, player.UserIDString), item.amount, item.name != null ? lang.GetMessage(item.name, this, player.UserIDString) : item.info.displayName.english, config.generalSettings.ingredientBagSettings.bagItemSettings.displayName ?? "ingredient bag"));
                player.GiveItem(item);
            }
            Pool.FreeUnmanaged(ref items_to_return);
            bagData.RemoveAll(x => x.amount == 0);
        }

        void CloseIngredientBag(BasePlayer player, StorageContainer container)
        {
            if (!IBagContainers.TryGetValue(player.userID, out var _container) || _container != container) return;
            SaveIngredientBag(player, container);
            IBagContainers.Remove(player.userID);
            container.Invoke(container.KillMessage, 0.01f);
        }

        void OpenIngredientBagConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, perm_bag_cmd))
            {
                PrintToChat(player, lang.GetMessage("BagChatNoPerms", this, player.UserIDString));
                return;
            }
            OpenIngredientBag(player);
        }

        void OpenIngredientBagCommand(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_bag_cmd))
            {
                PrintToChat(player, lang.GetMessage("BagChatNoPerms", this, player.UserIDString));
                return;
            }
            OpenIngredientBag(player);
        }

        void OpenIngredientBag(BasePlayer player)
        {
            if (IsLootingIngredientBag(player))
            {
                PrintToChat(player, lang.GetMessage("BagStillOpen", this, player.UserIDString));

                return;
            }
            var container = GetBagContainer(player);
            PlayerLootContainer(player, container);
        }
        
        bool IsLootingIngredientBag(BasePlayer player)
        {
            if (!IBagContainers.TryGetValue(player.userID, out var container)) return false;
            if (player.inventory.loot.entitySource == null || player.inventory.loot.entitySource.net?.ID.Value != container?.net.ID.Value)
            {
                PrintToChat(player, "Found an error with the looting bag. Try opening it again.");
                CloseIngredientBag(player, container);
            }
            return true;
        }

        private class FridgeModifier : MonoBehaviour, IFoodSpoilModifier
        {
            public float GetSpoilMultiplier(Item arg)
            {
                return 0.1f;
            }
        }

        StorageContainer GetBagContainer(BasePlayer player)
        {
            float count = 0;
            try
            {
                var container = GameManager.server.CreateEntity(config.generalSettings.ingredientBagSettings.bag_prefab, new Vector3(player.transform.position.x, player.transform.position.y - 1000, player.transform.position.z)) as StorageContainer;
                count = 1;
                container.Spawn();
                container.OwnerID = player.userID;
                count = 2;
                GameObject.DestroyImmediate(container.GetComponent<GroundWatch>());
                GameObject.DestroyImmediate(container.GetComponent<DestroyOnGroundMissing>());
                container.gameObject.AddComponent<FridgeModifier>();
                count = 3;
                container.inventorySlots = config.generalSettings.ingredientBagSettings.maxSlots;
                container.inventory.capacity = config.generalSettings.ingredientBagSettings.maxSlots;
                count = 4;
                if (!IBagContainers.TryGetValue(player.userID, out var existingContainer)) IBagContainers.Add(player.userID, container);
                else existingContainer = container;
                count = 5;
                List<BagInfo> bagData;
                if (!pcdData.bag.TryGetValue(player.userID, out bagData)) pcdData.bag.Add(player.userID, bagData = new List<BagInfo>());
                count = 6;
                if (bagData.Count == 0)
                {
                    return container;
                }
                count = 7;
                List<BagInfo> invalid = Pool.Get<List<BagInfo>>();
                List<BagInfo> slotless = Pool.Get<List<BagInfo>>();
                List<Item> failMoveSlot = Pool.Get<List<Item>>();
                foreach (var slotData in bagData)
                {
                    if (slotData.amount < 1)
                    {
                        invalid.Add(slotData);
                        continue;
                    }
                    count = 7.1f;
                    // Check to see if the item has a slot or not, if not, we store it and move on so we can add it later.
                    if (slotData.slot == -1)
                    {
                        slotless.Add(slotData);
                        continue;
                    }
                    count = 7.2f;
                    // Handles all of the items that have slot numbers and stores them in the same slots.
                    var item = ItemManager.CreateByName(slotData.shortname, slotData.amount, slotData.skin);
                    count = 7.3f;
                    if (!string.IsNullOrEmpty(slotData.displayName) && !item.info.displayName.english.Equals(slotData.displayName, StringComparison.OrdinalIgnoreCase)) item.name = slotData.displayName;
                    if (string.IsNullOrEmpty(slotData.text)) item.text = slotData.text;
                    count = 7.4f;
                    if (IsSpoilingItem(item)) item.instanceData.dataFloat = Mathf.Max(slotData.spoilTime, 120);
                    if (!item.MoveToContainer(container.inventory, slotData.slot, true, true)) failMoveSlot.Add(item);
                }
                count = 8;
                foreach (var slotData in slotless)
                {
                    count = 8.1f;
                    var item = ItemManager.CreateByName(slotData.shortname, slotData.amount, slotData.skin);
                    count = 8.2f;
                    if (!string.IsNullOrEmpty(slotData.displayName)) item.name = slotData.displayName;
                    if (string.IsNullOrEmpty(slotData.text)) item.text = slotData.text;
                    count = 8.3f;
                    if (IsSpoilingItem(item)) item.instanceData.dataFloat = Mathf.Max(slotData.spoilTime, 120);
                    if (!item.MoveToContainer(container.inventory))
                    {
                        count = 8.4f;
                        player.GiveItem(item);
                        PrintToChat(player, lang.GetMessage("BagMayBeFull", this, player.UserIDString));
                    }
                }
                count = 9;
                foreach (var orphanItem in failMoveSlot)
                {
                    count = 9.1f;
                    if (!orphanItem.MoveToContainer(container.inventory))
                    {
                        count = 9.2f;
                        player.GiveItem(orphanItem);
                        PrintToChat(player, lang.GetMessage("BagMayBeFull", this, player.UserIDString));
                    }
                }

                foreach (var entry in invalid)
                {
                    bagData.Remove(entry);
                }
                Pool.FreeUnmanaged(ref invalid);
                Pool.FreeUnmanaged(ref slotless);
                Pool.FreeUnmanaged(ref failMoveSlot);
                count = 10;
                return container;
            }
            catch (Exception e)
            {
                LogToFile("GetBagContainer_Error", $"Failed at {count}. Error: {e?.Message} - 1716.", this, true, true);
            }
            return null;
        }

        void PlayerLootContainer(BasePlayer player, StorageContainer container)
        {
            player.EndLooting();
            timer.Once(0.1f, () => container.PlayerOpenLoot(player, "", false));
        }

        #endregion

        #region NPCs

        Dictionary<ulong, NPCSimpleMissionProvider> MarketNPCs = new Dictionary<ulong, NPCSimpleMissionProvider>();
        object OnNpcConversationStart(NPCSimpleMissionProvider NPCSimpleMissionProvider, BasePlayer player, ConversationData conversationData)
        {
            if (!MarketNPCs.ContainsKey(NPCSimpleMissionProvider.net.ID.Value)) return null;

            if (!permission.UserHasPermission(player.UserIDString, perm_market_npc))
            {
                PrintToChat(player, lang.GetMessage("MarketNPCNoPerms", this, player.UserIDString));
                return true;
            }

            SendMarket(player);
            return true;
        }

        [ChatCommand("removeallmarketnpcs")]
        void RemoveAllMarketNPCs(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            List<NPCSimpleMissionProvider> marketNPCs = Pool.Get<List<NPCSimpleMissionProvider>>();
            marketNPCs.AddRange(MarketNPCs.Values);
            foreach (var npc in marketNPCs)
            {
                if (!RemoveNPC(npc)) PrintToChat(player, $"Failed to remove {npc.displayName}[{npc.transform.position}]");
            }
            Pool.FreeUnmanaged(ref marketNPCs);
        }

        [ChatCommand("removemarketnpc")]
        void RemoveNPCCMD(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            var npc = GetTargetEntity(player) as NPCSimpleMissionProvider;
            if (npc == null)
            {
                PrintToChat($"NPC not found.");
                return;
            }

            if (RemoveNPC(npc)) PrintToChat(player, $"Removed the NPC.");
            else PrintToChat(player, "Failed to remove the market NPC.");
        }

        bool RemoveNPC(NPCSimpleMissionProvider npc)
        {
            foreach (var npcData in pcdData.npcs)
            {
                if (InRange(npcData.position, npc.transform.position, 0.1f))
                {
                    foreach (var marker in BaseNetworkable.serverEntities.OfType<MapMarkerMissionProvider>())
                        if (InRange(marker.transform.position, npc.transform.position, 0.1f))
                            marker.Kill();
                    
                    pcdData.npcs.Remove(npcData);
                    MarketNPCs.Remove(npc.net.ID.Value);
                    npc.Kill();
                    return true;
                }
            }
            return false;
        }

        NPCSimpleMissionProvider IsNPCSpawned(Vector3 pos)
        {
            var npcs = FindEntitiesOfType<NPCSimpleMissionProvider>(pos, 5);
            NPCSimpleMissionProvider result = null;
            foreach (var npc in npcs)
            {
                if (InRange(npc.transform.position, pos, 0.1f))                
                {
                    result = npc;
                    break;
                }
            }
            Pool.FreeUnmanaged(ref npcs);
            return result;
        }

        [ChatCommand("addmarketnpc")]
        void AddNewNPC(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            var rotation = player.transform.rotation;
            if (player.serverInput?.current != null) rotation = Quaternion.Euler(player.serverInput.current.aimAngles);

            AddNPC(player.transform.position, rotation);
        }

        void AddNPC(Vector3 pos, Quaternion rot)
        {
            pcdData.npcs.Add(new NPCInfo(pos, rot.eulerAngles));
            SpawnMarketNPC(pos, rot);
        }

        void SpawnMarketNPC(Vector3 pos, Quaternion rot)
        {
            var prefab = "assets/prefabs/npc/bandit/missionproviders/missionprovider_bandit_a.prefab" + (true ? null : "ZGpkAt==");
            var npc = GameManager.server.CreateEntity(prefab, pos, rot) as NPCSimpleMissionProvider;
            npc.Spawn();
            
            
            npc.MarkerPrefab = new GameObjectRef();
            npc.loadouts = new PlayerInventoryProperties[0];
            if (config.generalSettings.marketSettings.npcSettings.wornItems?.Count > 0)
            {
                npc.inventory.containerWear.Clear();
                foreach (var entry in config.generalSettings.marketSettings.npcSettings.wornItems)
                {
                    var item = ItemManager.CreateByName(entry.shortname, 1, entry.skin);
                    if (!item.MoveToContainer(npc.inventory.containerWear)) item.Remove();
                    item.MarkDirty();
                }
                npc.inventory.containerWear.MarkDirty();
            }           
            
            if (config.generalSettings.marketSettings.npcSettings.heldItem != null && !string.IsNullOrEmpty(config.generalSettings.marketSettings.npcSettings.heldItem.shortname))
            {
                var heldItem = ItemManager.CreateByName(config.generalSettings.marketSettings.npcSettings.heldItem.shortname, 1, config.generalSettings.marketSettings.npcSettings.heldItem.skin);
                var heldEntity = heldItem.GetHeldEntity();
                if (heldEntity != null)
                {
                    heldEntity.skinID = config.generalSettings.marketSettings.npcSettings.heldItem.skin;                    
                }
                if (!heldItem.MoveToContainer(npc.inventory.containerBelt)) heldItem.Remove();
                heldItem.MarkDirty();
            }
            MarketNPCs.Add(npc.net.ID.Value, npc);
        }


        #endregion

        #region Monobehaviours

            #region BuffManager 

        public Dictionary<ulong, BuffManager> BuffManagerDirectory = new Dictionary<ulong, BuffManager>();

        BuffManager GetBuffManager(BasePlayer player, bool addComponent = true)
        {
            BuffManager result;
            if (!BuffManagerDirectory.TryGetValue(player.userID, out result) || result == null)
            {
                if (!addComponent) return null;
                result = player.GetComponent<BuffManager>();
                if (result == null) result = player.gameObject.AddComponent<BuffManager>();
                BuffManagerDirectory[player.userID] = result;
                result.Instance = this;
                result.GetUISettings(config.menuSettings.anchorSettings.buffIcons.anchorMin, config.menuSettings.anchorSettings.buffIcons.anchorMax, config.menuSettings.anchorSettings.buffIcons.offsetMin, config.menuSettings.anchorSettings.buffIcons.offsetMax, config.menuSettings.buffUISettings.backpanel, config.menuSettings.buffUISettings.frontpanel);
            }            
            return result;
        }

        void DestroyBuffManager(ulong id)
        {
            BuffManager component;
            if (BuffManagerDirectory.TryGetValue(id, out component))
            {
                GameObject.Destroy(component);
                BuffManagerDirectory.Remove(id);
            }
        }

        public class BuffInfo
        {
            public Dictionary<Buff, float> buffs;
            public float endTime;
            public string shortname;
            public ulong skin;
            public List<string> commandsOnEnd;
            public BuffInfo(Dictionary<Buff, float> buffs, float endTime, string shortname, ulong skin, List<string> commandsOnEnd)
            {
                this.buffs = buffs;
                this.endTime = endTime;
                this.shortname = shortname;
                this.skin = skin;
                this.commandsOnEnd = commandsOnEnd;
            }
        }

        // Behaviour

        public class BuffManager : FacepunchBehaviour
        {
            public Cooking Instance;
            private BasePlayer player;
            private ulong userid;
            public Dictionary<string, BuffInfo> activeMeals = new Dictionary<string, BuffInfo>(StringComparer.InvariantCultureIgnoreCase);
            private Dictionary<Buff, float> buffMods = new Dictionary<Buff, float>();

            // Comfort buff
            private TriggerComfort comfortTrigger;
            private List<BasePlayer> comfortedPlayers = Pool.Get<List<BasePlayer>>();

            // Regen buff
            private float regenAmount = 0;
            private float dotAmount = 0;
            private float damageCooldown = 0;

            // Metabolism 
            private float metabolismModifier = 0;

            private bool hasComfort = false;

            List<string> remove = Pool.Get<List<string>>();

            // Underwater breathing
            private bool hasUnderwatereBreathing = false;

            private void Awake()
            {                
                player = GetComponent<BasePlayer>();
                userid = player.userID;
                InvokeRepeating(Run, 1, 1);
                //Interface.Oxide.LogInfo($"Setting up BuffManager for {player.displayName}");
            }

            public void Run()
            {
                if (player == null || !player.IsConnected)
                {
                    Destroy(this);
                    return;
                }
                CheckBuffs();
                if (ActiveMealCount == 0) Destroy(this);
                if (comfortTrigger != null) ComfortPlayers();
                if (regenAmount > 0 && Time.time > damageCooldown) DoRegen();
                if (dotAmount > 0) DoDamage();
                if (hasUnderwatereBreathing) HandleUnderwaterBreathing();
            }       

            private void HandleUnderwaterBreathing()
            {
                if (player.IsDead()) return;

                ItemModGiveOxygen.AirSupplyType oxygenSource;
                var beathLeft = player.GetOxygenTime(out oxygenSource);

                var isUnderwater = player.WaterFactor() > 0.85f && (oxygenSource == ItemModGiveOxygen.AirSupplyType.Lungs || beathLeft < 4);
                if (isUnderwater) player.metabolism.oxygen.SetValue(1);
            }
            
            public string AddBuff(string meal, Dictionary<Buff, float> buffs, float time, string shortname, ulong skin, Dictionary<string, string> commandsOnConsume, List<string> commandsOnEnd)
            {
                BuffInfo info;
                if (activeMeals.TryGetValue(meal, out info))
                {
                    info.endTime = Time.time + time;
                    UpdateBuffUI();
                    return null;
                }

                if (Instance.config.mealSettings.maxMealsAllowed > 0 && ActiveMealCount >= Instance.config.mealSettings.maxMealsAllowed) return "TooManyMeals";

                foreach (var buff in buffs)
                {
                    //Interface.Oxide.LogInfo($"Buffs: {buff.Key}");
                    float currentValue;
                    if (!buffMods.TryGetValue(buff.Key, out currentValue)) buffMods.Add(buff.Key, buff.Value);
                    else
                    {
                        if (Instance.config.generalSettings.buffSettings.allow_buff_stacking) buffMods[buff.Key] += buff.Value;
                        else if (currentValue < buff.Value) buffMods[buff.Key] = buff.Value;
                    }
                        
                    if (buff.Key == Buff.Comfort && !hasComfort)
                    {
                        AddComfort();
                        hasComfort = true;
                        //Interface.Oxide.LogInfo($"Found comfort: {hasComfort} @ 1784808420");
                    }

                    if (buff.Key == Buff.Metabolism_Overload)
                        AddMetabolismOverload(buff.Value);

                    if (buff.Key == Buff.Passive_Regen && buff.Value > regenAmount)
                        regenAmount = buff.Value;

                    if (buff.Key == Buff.Damage_Over_Time && buff.Value > dotAmount)
                        dotAmount = buff.Value;

                    if (buff.Key == Buff.Horse_Stats && player.isMounted)
                    {
                        var horse = player.GetMountedVehicle() as RidableHorse;
                        if (horse != null)
                        {
                            HorseInfo horseData;
                            if (!Instance.ModifiedHorses.TryGetValue(horse, out horseData) || horseData.modifier < buff.Value) Instance.ModifyHorse(horse, player, buff.Value);
                        }
                    }

                    if (buff.Key == Buff.Water_Breathing)
                        hasUnderwatereBreathing = true;
                }

                activeMeals.Add(meal, new BuffInfo(buffs, Time.time + time, shortname, skin, commandsOnEnd));
                if (commandsOnConsume != null) RunConsumeCommands(commandsOnConsume, player);

                UpdateBuffSubscriptions();
                UpdateBuffUI();

                return null;
            }

            private void UpdateBuffSubscriptions()
            {
                List<Buff> buffList = Pool.Get<List<Buff>>();
                buffList.AddRange(buffMods.Keys);
                Instance.HandlePlayerSubscription(player, buffList);
                Pool.FreeUnmanaged(ref buffList);
            }

            public void AddDamageCooldown(float time)
            {
                damageCooldown = Time.time + time;
            }

            private void DoRegen()
            {
                player.Heal(regenAmount);
            }

            private void DoDamage()
            {
                player.Hurt(dotAmount);
            }

            private void GetUpdatedRegen()
            {
                float highest = 0;
                foreach (var buff in buffMods)
                    if (buff.Key == Buff.Passive_Regen && buff.Value > highest) highest = buff.Value;

                regenAmount = highest;
            }

            private void GetUpdatedDamageOverTime()
            {
                float highest = 0;
                foreach (var buff in buffMods)
                    if (buff.Key == Buff.Damage_Over_Time && buff.Value > highest) highest = buff.Value;

                dotAmount = highest;
            }

            private void GetUpdatedHorseSpeed(RidableHorse horse)
            {
                float highest = 0;
                foreach (var buff in buffMods)
                    if (buff.Key == Buff.Horse_Stats && buff.Value > highest) highest = buff.Value;

                if (highest > 0)
                    Instance.ModifyHorse(horse, player, highest);
            }

            private void GetUpdatedMetabolism()
            {
                float highest = 0;
                foreach (var buff in buffMods)
                    if (buff.Key == Buff.Metabolism_Overload && buff.Value > highest) highest = buff.Value;

                if (highest == 0) return;

                UpdateMetabolismOverload(highest);
            }

            private void AddComfort()
            {
                comfortTrigger = player.gameObject.AddComponent<TriggerComfort>();
                comfortTrigger.baseComfort = Instance.config.generalSettings.buffSettings.comfortableSettings.comfortAmount;
            }

            private void ComfortPlayers()
            {
                var hits = FindEntitiesOfType<BasePlayer>(player.transform.position, Instance.config.generalSettings.buffSettings.comfortableSettings.comfortRadius);
                //hits.Remove(player);
                comfortedPlayers.RemoveAll(x => x == null);
                foreach (var hit in comfortedPlayers)
                {
                    if (!hits.Contains(hit))
                    {                        
                        hit.LeaveTrigger(comfortTrigger);
                        comfortTrigger.OnEntityLeave(hit);
                    }
                }
                comfortedPlayers.Clear();
                foreach (var hit in hits)
                {
                    if (hit.triggers.IsNullOrEmpty() || !hit.triggers.Contains(comfortTrigger)) 
                        hit.EnterTrigger(comfortTrigger);
                }
                comfortedPlayers.AddRange(hits);
                Pool.FreeUnmanaged(ref hits);
            }

            private void RemoveComfort()
            {
                player.LeaveTrigger(comfortTrigger);
                if (comfortTrigger == null && (comfortTrigger = player.gameObject.GetComponent<TriggerComfort>()) == null) return;
                comfortedPlayers.RemoveAll(x => x == null);
                foreach (var p in comfortedPlayers)
                    p.LeaveTrigger(comfortTrigger);

                comfortedPlayers.Clear();
                GameObject.Destroy(player.gameObject.GetComponent<TriggerComfort>());
                hasComfort = false;
            }

            private void UpdateMetabolismOverload(float modifier)
            {
                metabolismModifier = modifier;
                player.metabolism.calories.max = 500 + (500 * modifier);
                player.metabolism.hydration.max = 250 + (250 * modifier);

                if (player.metabolism.calories.value > 500) player.metabolism.calories.SetValue(player.metabolism.calories.max);
                if (player.metabolism.hydration.value > 250) player.metabolism.hydration.SetValue(player.metabolism.hydration.max);

                player.metabolism.SendChanges();
            }

            private void AddMetabolismOverload(float modifier)
            {
                if (modifier > metabolismModifier)
                {
                    metabolismModifier = modifier;
                    player.metabolism.calories.max = 500 + (500 * modifier);
                    player.metabolism.hydration.max = 250 + (250 * modifier);                   

                    player.metabolism.SendChanges();
                }
            }

            private void RemoveMetabolismOverload()
            {
                metabolismModifier = 0;
                player.metabolism.calories.max = 500;
                player.metabolism.hydration.max = 250;

                if (player.metabolism.calories.value > 500) player.metabolism.calories.SetValue(500);
                if (player.metabolism.hydration.value > 250) player.metabolism.hydration.SetValue(250);

                player.metabolism.SendChanges();
            }

            // Used to check the buffs expiry.
            private void CheckBuffs()
            {
                remove.Clear();
                foreach (var meal in activeMeals)
                {
                    if (meal.Value.endTime < Time.time)
                        remove.Add(meal.Key);
                }
                foreach (var meal in remove)
                {
                    RemoveMeal(meal);
                }                
            }

            private void RunConsumeCommands(Dictionary<string, string> commands, BasePlayer player)
            {
                foreach (var c in commands.Keys)
                {
                    try
                    {
                        string replacedCommand = c.Replace("{id}", player.UserIDString);
                        replacedCommand = replacedCommand.Replace("{name}", player.displayName);
                        string _command = replacedCommand.Split(' ')[0];
                        string[] args = replacedCommand.Split(' ').Skip(1).ToArray();
                        //Interface.Oxide.LogInfo($"Command: {_command}. Args: {args}");
                        
                        ConsoleSystem.Run(ConsoleSystem.Option.Server, _command, args);
                    }
                    catch { }
                }
            }

            private float GetNextHighest(string removedMeal, Buff buff, float currentValue)
            {
                float highestFound = 0;
                foreach (var meal in activeMeals)
                {
                    if (meal.Key == removedMeal) continue;
                    float value;
                    if (!meal.Value.buffs.TryGetValue(buff, out value) || value < highestFound) continue;
                    highestFound = value;
                }

                return highestFound;
            }

            public void RemoveMeal(string meal)
            {
                BuffInfo buffData;
                if (activeMeals.TryGetValue(meal, out buffData))
                {
                    foreach (var buff in buffData.buffs)
                    {                        
                        if (buffMods.ContainsKey(buff.Key))
                        {
                            if (Instance.config.generalSettings.buffSettings.allow_buff_stacking) buffMods[buff.Key] -= buff.Value;
                            else buffMods[buff.Key] = GetNextHighest(meal, buff.Key, buff.Value);

                            if (buffMods[buff.Key] <= 0)
                            {
                                buffMods.Remove(buff.Key);

                                switch (buff.Key)
                                {
                                    case Buff.Metabolism_Overload:
                                        RemoveMetabolismOverload();
                                        break;

                                    case Buff.Horse_Stats:
                                        var horse = player.GetMountedVehicle() as RidableHorse;
                                        if (horse != null) Instance.RestoreModifiedHorse(horse);
                                        break;

                                    case Buff.Passive_Regen:
                                        regenAmount = 0;
                                        break;

                                    case Buff.Damage_Over_Time:
                                        dotAmount = 0;
                                        break;

                                    case Buff.Spectre:
                                        RemoveSpectre();
                                        break;

                                    case Buff.Water_Breathing:
                                        hasUnderwatereBreathing = false;
                                        break;
                                }
                            }
                            else ChangeToNextHighestBuff(buff.Key);
                        }                        
                    }
                    if (buffData.commandsOnEnd != null)
                    {
                        foreach (var c in buffData.commandsOnEnd)
                        {
                            try
                            {
                                string formattedCommand = c.Replace("{id}", player.UserIDString);
                                formattedCommand = formattedCommand.Replace("{name}", player.displayName);
                                string _command = formattedCommand.Split(' ')[0];
                                string[] args = formattedCommand.Split(' ').Skip(1).ToArray();
                                //Interface.Oxide.LogInfo($"Command: {formattedCommand}. Args: {args}");
                                ConsoleSystem.Run(ConsoleSystem.Option.Server, _command, args);
                            }
                            catch { }
                        }
                    }                    
                    activeMeals.Remove(meal);
                    if (!buffMods.ContainsKey(Buff.Comfort)) RemoveComfort();
                }
                UpdateBuffSubscriptions();
                UpdateBuffUI();
            }

            private void ChangeToNextHighestBuff(Buff buffToCheck)
            {
                switch (buffToCheck)
                {
                    case Buff.Passive_Regen:
                        GetUpdatedRegen();
                        return;

                    case Buff.Damage_Over_Time:
                        GetUpdatedDamageOverTime();
                        return;

                    case Buff.Horse_Stats:
                        var horse = player.GetMountedVehicle() as RidableHorse;
                        if (horse != null) GetUpdatedHorseSpeed(horse);
                        return;

                    case Buff.Metabolism_Overload:
                        GetUpdatedMetabolism();
                        return;
                }                
            }

            private void RemoveSpectre()
            {
                var entities = FindEntitiesOfType<StorageContainer>(player.transform.position, 20);
                foreach (var entity in entities)
                {
                    var guntrap = entity as GunTrap;
                    if (guntrap != null)
                    {
                        var component = guntrap.trigger.GetComponent<Collider>();
                        if (component == null) continue;
                        Bounds bounds = component.bounds;
                        if (bounds.Contains(player.ClosestPoint(guntrap.transform.position))) guntrap.trigger.OnEntityEnter(player);
                    }

                    var flameTurret = entity as FlameTurret;
                    if (flameTurret != null)
                    {
                        var component = flameTurret.trigger.GetComponent<Collider>();
                        if (component == null) continue;
                        Bounds bounds = component.bounds;
                        if (bounds.Contains(player.ClosestPoint(flameTurret.transform.position))) flameTurret.trigger.OnEntityEnter(player);
                    }
                }
                Pool.FreeUnmanaged(ref entities);

                var turrets = FindEntitiesOfType<AutoTurret>(player.transform.position, 30);
                foreach (var turret in turrets)
                {
                    var component = turret.targetTrigger.GetComponent<Collider>();
                    if (component == null) continue;
                    Bounds bounds = component.bounds;
                    if (bounds.Contains(player.ClosestPoint(turret.transform.position))) turret.targetTrigger.OnEntityEnter(player);
                }                
            }

            public void RemoveMealWithBuff(Buff buff)
            {
                foreach (var meal in activeMeals)
                {
                    if (meal.Value.buffs.ContainsKey(buff))
                    {
                        RemoveMeal(meal.Key);
                        break;
                    }
                }
            }

            private string BuffUIBackPanelColour;
            private string BuffUIAnchorMin;
            private string BuffUIAnchorMax;
            private string BuffUIOffsetMin;
            private string BuffUIOffsetMax;
            private string BuffUIFrontPanelColour;

            public void GetUISettings(string buffuiAnchorMin, string buffuiAnchorMax, string buffuiOffsetMin, string buffuiOffsetMax, string buffuiBackPanelColour, string buffuiFrontPanelColour)
            {                
                this.BuffUIAnchorMin = buffuiAnchorMin;
                this.BuffUIAnchorMax = buffuiAnchorMax;
                this.BuffUIOffsetMin = buffuiOffsetMin;
                this.BuffUIOffsetMax = buffuiOffsetMax;
                this.BuffUIBackPanelColour = buffuiBackPanelColour;
                this.BuffUIFrontPanelColour = buffuiFrontPanelColour;
            }

            //Used to update the buff hud UI
            public void UpdateBuffUI()
            {
                Instance.BuffUI(player, activeMeals, BuffUIBackPanelColour, BuffUIFrontPanelColour, BuffUIAnchorMin, BuffUIAnchorMax, BuffUIOffsetMin, BuffUIOffsetMax);
            }

            // Returns the active buff count.
            public int ActiveMealCount
            {
                get { return activeMeals.Count; }
            }

            // Returns a dictionary of all the active buffs on this player
            public Dictionary<Buff, float> GetBuffModifiers
            {
                get
                {
                    return buffMods;
                }                              
            }

            // Clears the buffs from the player.
            public void ClearBuffs(bool destroyComponent)
            {
                activeMeals.Clear();
                if (destroyComponent) Destroy(this);
            }

            private void OnDestroy()
            {
                if (activeMeals.Count > 0)
                {
                    List<string> meals = Pool.Get<List<string>>();
                    meals.AddRange(activeMeals.Keys);
                    foreach (var meal in meals)
                        RemoveMeal(meal);
                    Pool.FreeUnmanaged(ref meals);
                }                    

                RemoveComfort();
                RemoveMetabolismOverload();
                Pool.FreeUnmanaged(ref comfortedPlayers);
                if (player.IsConnected)
                {
                    CuiHelper.DestroyUi(player, "BuffUI");
                }                
                Pool.FreeUnmanaged(ref remove);
                activeMeals.Clear();
                activeMeals = null;
                buffMods.Clear();
                UpdateBuffSubscriptions();
                buffMods = null;
                enabled = false;
                Instance.BuffManagerDirectory.Remove(userid);                
                //Interface.Oxide.LogInfo($"Destroyed BuffManager for {player?.displayName ?? "null player"}");
                CancelInvoke();
            }
        }

        #endregion

        #region Cooking manager
        public Dictionary<ulong, CookingManager> CookingManagerDirectory = new Dictionary<ulong, CookingManager>();

        CookingManager GetCookingManager(BasePlayer player, bool addcomponent = true)
        {
            CookingManager result;
            if (!CookingManagerDirectory.TryGetValue(player.userID, out result) || result == null)
            {
                // result = player.GetComponent<CookingManager>();
                if (addcomponent) result = player.gameObject.AddComponent<CookingManager>();
                if (result != null)
                {
                    CookingManagerDirectory[player.userID] = result;
                    result.Instance = this;
                    result.GetUISettings(config.menuSettings.queueMenuSettings.backpanel, config.menuSettings.anchorSettings.craftQueue.anchorMin, config.menuSettings.anchorSettings.craftQueue.anchorMax, config.menuSettings.anchorSettings.craftQueue.offsetMin, config.menuSettings.anchorSettings.craftQueue.offsetMax, config.menuSettings.queueMenuSettings.frontpanel);
                }
            }
            return result;
        }

        void DestroyCookingManager(ulong id)
        {
            CookingManager component;
            if (CookingManagerDirectory.TryGetValue(id, out component))
            {
                GameObject.DestroyImmediate(component);
            }
        }

        public Dictionary<string, IngredientsInfo> Ingredients = new Dictionary<string, IngredientsInfo>(StringComparer.InvariantCultureIgnoreCase);

        public class CookingManager : FacepunchBehaviour
        {
            public Cooking Instance;
            private BasePlayer player;
            private ulong userid;
            private List<CookingInfo> queuedMeals = new List<CookingInfo>();
            private bool cancellingCraft = false;
            private Vector3 lastKnownPosition;

            public class CookingInfo
            {
                public float finishCookingAt = 0;
                public float cookTime;
                public string displayName;
                public ulong skinID;
                public string shortname;
                public float spoilTime;
                public Dictionary<string, IngredientItemInfo> ingredientsPerMeal = new Dictionary<string, IngredientItemInfo>();

                public CookingInfo(string shortname, string displayName, ulong skinID, float cookTime, float spoilTime, Dictionary<string, IngredientItemInfo> ingredientsPerMeal)
                {
                    this.shortname = shortname;
                    this.displayName = displayName;
                    this.skinID = skinID;
                    this.cookTime = cookTime;
                    this.spoilTime = spoilTime;
                    this.ingredientsPerMeal = ingredientsPerMeal;
                }
            }

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                lastKnownPosition = player.transform.position;
                //Interface.Oxide.LogInfo("Component active.");
                userid = player.userID;

                InvokeRepeating(Run, 1, 1);
            }

            public void Run()
            {
                if (player == null || !player.IsConnected)
                {
                    Destroy(this);
                    return;
                }

                if (!player.IsConnected || player.IsDead())
                {
                    //Interface.Oxide.LogInfo($"Dead or not connected.");
                    Destroy(this);
                    return;
                }
                lastKnownPosition = player.transform.position;
                CheckCookingProgress();
            }

            public int Queued()
            {
                return queuedMeals.Count;
            }

            void CheckCookingProgress()
            {
                if (queuedMeals.Count == 0)
                {
                    Destroy(this);
                    return;
                }
                var meal = queuedMeals.First();
                if (meal.finishCookingAt == 0)
                {
                    meal.finishCookingAt = Time.time + meal.cookTime;
                    UpdateCookingQueueUI(meal.cookTime, meal.skinID);
                }
                else if (Time.time > meal.finishCookingAt)
                {
                    GiveMeal(meal);
                    //Interface.Oxide.LogInfo($"Finished making: {meal.displayName}.");
                    queuedMeals.Remove(meal);
                    // Player, meal name, is ingredient
                    Dictionary<string, int> ingredientsUsed = new Dictionary<string, int>();
                    foreach (var ingredient in meal.ingredientsPerMeal)
                        ingredientsUsed.Add(ingredient.Key, ingredient.Value.amount);
                    Interface.CallHook("OnMealCrafted", player, meal.displayName, ingredientsUsed, Instance.config.ingredientSettings.ingredients.ContainsKey(meal.displayName));
                    ingredientsUsed.Clear();
                }
            }

            private void UpdateCookingQueueUI(float timeLeft, ulong skin)
            {
                //Interface.Oxide.LogInfo($"Time left on cook for: {timeLeft} seconds.");
                // Hand cooking UI here.
                Instance.UpdateCookingQueue(player, skin, Convert.ToInt32(timeLeft), queuedMeals.Count, CookingQueueBackPanelColour, CookingQueueAnchorMin, CookingQueueAnchorMax, CookingQueueOffsetMin, CookingQueueOffsetMax, CookingQueueFrontPanelColour);
            }

            public void AddMeal(string shortname, string displayName, ulong skin, float cookTime, float spoilTime, Dictionary<string, IngredientItemInfo> ingredientsPerMeal)
            {
                var queMeal = new CookingInfo(shortname, displayName, skin, cookTime, spoilTime, ingredientsPerMeal);
                queuedMeals.Add(queMeal);
                //Interface.Oxide.LogInfo($"Meal added. New queue length: {queuedMeals.Count}");
                var current = queuedMeals.First();
                if (current.finishCookingAt > 0)
                    UpdateCookingQueueUI(current.finishCookingAt - Time.time, current.skinID);
            }

            public class IngredientItemInfo
            {
                public int amount;
                public float spoilTime;
                public void CheckSpoilTime(Item item)
                {
                    if (item.info.GetComponent<ItemModFoodSpoiling>() != null && (spoilTime <= 0 || item.instanceData.dataFloat < spoilTime)) spoilTime = item.instanceData.dataFloat;
                }
                public void CheckSpoilTime(float spolTime)
                {
                    if (spoilTime <= 0 || spolTime < spoilTime) spoilTime = spolTime;
                }
            }

            private void GiveMeal(CookingInfo meal)
            {
                var item = ItemManager.CreateByName(meal.shortname, 1, meal.skinID);
                item.name = meal.displayName.ToLower();
                item.text = meal.displayName.ToLower();
                if (IsSpoilingItem(item)) item.instanceData.dataFloat = meal.spoilTime * 3600;
                player.GiveItem(item);
            }

            private Dictionary<string, IngredientsInfo> ingredientsToRefund = new Dictionary<string, IngredientsInfo>(StringComparer.InvariantCultureIgnoreCase);
            private class IngredientsInfo
            {
                public string shortname;
                public ulong skin;
                public int amount;
                public float spoilTime;

                public IngredientsInfo(string shortname, ulong skin, int amount, float spoilTime)
                {
                    this.shortname = shortname;
                    this.skin = skin;
                    this.amount = amount;
                    this.spoilTime = spoilTime;
                }
            }

            public void CancelCurrentCraft()
            {
                if (queuedMeals.Count == 0) return;
                if (cancellingCraft) return;
                cancellingCraft = true;
                var meal = queuedMeals.First();

                foreach (var ingredient in meal.ingredientsPerMeal)
                {
                    var ingredientProfile = Instance.Ingredients[ingredient.Key];
                    var item = ItemManager.CreateByName(ingredientProfile.shortname, ingredient.Value.amount, ingredientProfile.skin);
                    item.name = ingredient.Key.ToLower();
                    if (ingredientProfile.skin > 0) item.text = ingredient.Key.ToLower();
                    if (IsSpoilingItem(item)) item.instanceData.dataFloat = ingredient.Value.spoilTime;

                    if (player != null && !player.IsDead()) player.GiveItem(item);
                    else item.DropAndTossUpwards(lastKnownPosition);
                }

                queuedMeals.Remove(meal);
                cancellingCraft = false;
            }

            public void CancelAllCrafts()
            {
                //Interface.Oxide.LogInfo("Starting cancel");
                foreach (var craft in queuedMeals)
                {
                    //Interface.Oxide.LogInfo("Looing - Craft in queuedMeals");
                    foreach (var ingredient in craft.ingredientsPerMeal)
                    {
                        //Interface.Oxide.LogInfo($"Looping Ingredient in ingredients per craft - Is {ingredient.Key} in profile: {Instance.Ingredients.ContainsKey(ingredient.Key)}");
                        var ingredientProfile = Instance.Ingredients[ingredient.Key];
                        if (!ingredientsToRefund.ContainsKey(ingredient.Key))
                        {
                            //Interface.Oxide.LogInfo("Adding a new key for refunds.");
                            ingredientsToRefund.Add(ingredient.Key, new IngredientsInfo(ingredientProfile.shortname, ingredientProfile.skin, ingredient.Value.amount, ingredient.Value.spoilTime));
                        }
                        else
                        {
                            //Interface.Oxide.LogInfo("Adding to existing key for refunds.");
                            ingredientsToRefund[ingredient.Key].amount += ingredient.Value.amount;
                        }
                    }
                }
                //Interface.Oxide.LogInfo("Startubg refund process.");
                foreach (var ingredient in ingredientsToRefund)
                {
                    var item = ItemManager.CreateByName(ingredient.Value.shortname, ingredient.Value.amount, ingredient.Value.skin);
                    if (ingredient.Value.skin != 0)
                    {
                        item.name = ingredient.Key.ToLower();
                        item.text = ingredient.Key.ToLower();
                    }
                    if (IsSpoilingItem(item)) item.instanceData.dataFloat = ingredient.Value.spoilTime;

                    if (player != null && player.IsAlive())
                    {
                        //Interface.Oxide.LogInfo("We are alive");
                        player.GiveItem(item);
                        //if (!item.MoveToContainer(player.inventory.containerMain) || !item.MoveToContainer(player.inventory.containerBelt))
                            //item.DropAndTossUpwards(lastKnownPosition, 5);
                    }
                    else
                    {
                        var dropPos = new Vector3(lastKnownPosition.x, lastKnownPosition.y + 1, lastKnownPosition.z);
                        //Interface.Oxide.LogInfo($"We are dead. Dropping at {dropPos}");
                        
                        item.DropAndTossUpwards(dropPos, 5);
                        player.SendConsoleCommand("ddraw.text", 20f, Color.yellow, dropPos, "HERE");
                    }
                }
                //Interface.Oxide.LogInfo("Cleaning up");
                queuedMeals.Clear();
                queuedMeals = null;
                ingredientsToRefund.Clear();
                ingredientsToRefund = null;
            }

            private string CookingQueueBackPanelColour;
            private string CookingQueueAnchorMin;
            private string CookingQueueAnchorMax;
            private string CookingQueueOffsetMin;
            private string CookingQueueOffsetMax;
            private string CookingQueueFrontPanelColour;

            public void GetUISettings(string cookingQueueBackPanelColour, string cookingQueueAnchorMin, string cookingQueueAnchorMax, string cookingQueueOffsetMin, string cookingQueueOffsetMax, string cookingQueueFrontPanelColour)
            {
                this.CookingQueueBackPanelColour = cookingQueueBackPanelColour;
                this.CookingQueueAnchorMin = cookingQueueAnchorMin;
                this.CookingQueueAnchorMax = cookingQueueAnchorMax;
                this.CookingQueueOffsetMin = cookingQueueOffsetMin;
                this.CookingQueueOffsetMax = cookingQueueOffsetMax;
                this.CookingQueueFrontPanelColour = cookingQueueFrontPanelColour;
            }

            private void OnDestroy()
            {
                //Interface.Oxide.LogInfo($"Destroying cooking manager component for {player.displayName}");
                CancelAllCrafts();
                //Interface.Oxide.LogInfo($"Cancelled crafts");
                if (player != null && player.IsConnected) CuiHelper.DestroyUi(player, "CookingQueue");
                //Interface.Oxide.LogInfo($"Unsent UI");
                Instance.CookingManagerDirectory.Remove(userid);
                //Interface.Oxide.LogInfo($"Removed the userid: {!Instance.CookingManagerDirectory.ContainsKey(userid)}");
                enabled = false;
                CancelInvoke();
            }
        }

        #endregion


        #endregion

        #region CUI

        void DestroyRecipeMenuUI(BasePlayer player, bool includingButton = false)
        {
            CuiHelper.DestroyUi(player, "BackPanel");
            CuiHelper.DestroyUi(player, "Core");
            CuiHelper.DestroyUi(player, "RecipesMenu");
            CuiHelper.DestroyUi(player, "ingredients_menu");
            CuiHelper.DestroyUi(player, "SendIngredientText");
            CuiHelper.DestroyUi(player, "FavouritesMenu");
            CuiHelper.DestroyUi(player, "IngredientsMenu");
            CuiHelper.DestroyUi(player, "PlayerSettings");
            CuiHelper.DestroyUi(player, "FarmersMarket");
            if (includingButton) CuiHelper.DestroyUi(player, "CookingButton");
        }

        #region Backpanel

        private void SendBackPanel(BasePlayer player)
        { 
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, "Overlay", "BackPanel");

            CuiHelper.DestroyUi(player, "BackPanel");
            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Core menu

        enum CoreElmentSelected
        {
            None,
            Recipes,
            Ingredients,
            Favourites,
            Settings
        }

        private void SendCoreMenu(BasePlayer player, CoreElmentSelected core = CoreElmentSelected.None)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0.9960784" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-0.01 -0.01", OffsetMax = "0.01 0.01" }
            }, "Overlay", "Core");

            #region Recipes

            container.Add(new CuiPanel
            {
                Image = { Color = config.menuSettings.coreMenuSettings.button_backpanel_colour},
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-435.2 109", OffsetMax = "-329.2 155" }
            }, "Core", "Recipes");

            container.Add(new CuiButton
            {
                Button = { Color = config.menuSettings.coreMenuSettings.button_colour, Command = $"cookingsendsubmenu {(int)CoreElmentSelected.Recipes}" },
                Text = { Text = lang.GetMessage("RecipesTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
            }, "Recipes", "button");

            if (core == CoreElmentSelected.Recipes)
            {
                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.coreMenuSettings.selected_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
                }, "Recipes", "selected");
            }

            #endregion

            #region Ingredients

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.coreMenuSettings.button_backpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-435.2 43", OffsetMax = "-329.2 89" }
            }, "Core", "Ingredients");

            container.Add(new CuiButton
            {
                Button = { Color = config.menuSettings.coreMenuSettings.button_colour, Command = $"cookingsendsubmenu {(int)CoreElmentSelected.Ingredients}" },
                Text = { Text = lang.GetMessage("IngredientsTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
            }, "Ingredients", "button");            

            if (core == CoreElmentSelected.Ingredients)
            {
                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.coreMenuSettings.selected_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
                }, "Ingredients", "selected");
            }

            #endregion

            #region Favourites

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.coreMenuSettings.button_backpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-435.2 -23", OffsetMax = "-329.2 23" }
            }, "Core", "Favourites");

            container.Add(new CuiButton
            {
                Button = { Color = config.menuSettings.coreMenuSettings.button_colour, Command = $"cookingsendsubmenu {(int)CoreElmentSelected.Favourites}" },
                Text = { Text = lang.GetMessage("FavouritesTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
            }, "Favourites", "button");

            if (core == CoreElmentSelected.Favourites)
            {
                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.coreMenuSettings.selected_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
                }, "Favourites", "selected");
            }

            #endregion

            #region Settings

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.coreMenuSettings.button_backpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-435.2 -89", OffsetMax = "-329.2 -43" }
            }, "Core", "Settings");

            container.Add(new CuiButton
            {
                Button = { Color = config.menuSettings.coreMenuSettings.button_colour, Command = $"cookingsendsubmenu {(int)CoreElmentSelected.Settings}" },
                Text = { Text = lang.GetMessage("SettingsTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
            }, "Settings", "button");            

            if (core == CoreElmentSelected.Settings)
            {
                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.coreMenuSettings.selected_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -20", OffsetMax = "50 20" }
                }, "Settings", "selected");
            }

            #endregion

            #region Close

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = "closerecipemenu" },
                Text = { Text = lang.GetMessage("CloseTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-435.2 -185", OffsetMax = "-329.2 -139" }
            }, "Core", "Close");

            #endregion

            CuiHelper.DestroyUi(player, "Core");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("closerecipemenu")]
        void CloseRecipeMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_back_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_back_sound);

            DestroyRecipeMenuUI(player);
        }

        void UnsendOtherPanels(BasePlayer player, CoreElmentSelected exclude)
        {
            switch (exclude)
            {
                case CoreElmentSelected.Recipes:
                    CuiHelper.DestroyUi(player, "ingredients_menu");
                    CuiHelper.DestroyUi(player, "SendIngredientText");
                    CuiHelper.DestroyUi(player, "FavouritesMenu");
                    CuiHelper.DestroyUi(player, "IngredientsMenu");
                    CuiHelper.DestroyUi(player, "PlayerSettings");
                    break;

                case CoreElmentSelected.Ingredients:
                    CuiHelper.DestroyUi(player, "RecipesMenu");
                    CuiHelper.DestroyUi(player, "ingredients_menu");
                    CuiHelper.DestroyUi(player, "SendIngredientText");
                    CuiHelper.DestroyUi(player, "FavouritesMenu");
                    CuiHelper.DestroyUi(player, "PlayerSettings");

                    break;

                case CoreElmentSelected.Settings:
                    CuiHelper.DestroyUi(player, "RecipesMenu");
                    CuiHelper.DestroyUi(player, "ingredients_menu");
                    CuiHelper.DestroyUi(player, "SendIngredientText");
                    CuiHelper.DestroyUi(player, "FavouritesMenu");
                    CuiHelper.DestroyUi(player, "IngredientsMenu");
                    break;

                case CoreElmentSelected.Favourites:
                    CuiHelper.DestroyUi(player, "RecipesMenu");
                    CuiHelper.DestroyUi(player, "ingredients_menu");
                    CuiHelper.DestroyUi(player, "SendIngredientText");
                    CuiHelper.DestroyUi(player, "IngredientsMenu");
                    CuiHelper.DestroyUi(player, "PlayerSettings");
                    break;
            }
        }

        [ConsoleCommand("cookingsendsubmenu")]
        void SendSubMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            CoreElmentSelected coreElement = (CoreElmentSelected)Convert.ToInt32(arg.GetString(0));
            if (coreElement == CoreElmentSelected.None)
            {
                DestroyRecipeMenuUI(player);
                return;
            }
            else SendCoreMenu(player, coreElement);

            UnsendOtherPanels(player, coreElement);
            switch (coreElement)
            {
                case CoreElmentSelected.Recipes:                    
                    SendRecipesMenu(player, 0);
                    break;

                case CoreElmentSelected.Favourites:
                    FavouritesMenu(player, 0);
                    break;

                case CoreElmentSelected.Ingredients:
                    IngredientsMenu(player);
                    break;

                case CoreElmentSelected.Settings:
                    PlayerSettings(player);
                    break;
            }
        }

        #endregion

        #region Recipe Menu

        void SendCookingMenu(BasePlayer player)
        {
            if (IsLootingIngredientBag(player)) player.EndLooting();
            SendBackPanel(player);
            SendCoreMenu(player, CoreElmentSelected.None);
        }

        void SendCookMenuConsoleCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, perm_recipemenu_chat)) return;
            SendCookingMenu(player);
        }

        void SendCookMenuCMD(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_recipemenu_chat)) return;
            SendCookingMenu(player);
        }

        Dictionary<string, MealInfo> GetMeals(BasePlayer player)
        {
            if (!config.mealSettings.cookbookSettings.enabled || config.mealSettings.cookbookSettings.unlearntVisible) return LoadedMeals;

            Dictionary<string, MealInfo> recipeData;
            if (CachedRecipes.TryGetValue(player.userID, out recipeData)) return recipeData;
            return null;
        }

        private void SendRecipesMenu(BasePlayer player, int firstRecipeToShowIndex)
        {
            var meals = GetMeals(player);
            if (meals == null)
            {
                PrintToChat(player, lang.GetMessage("NoRecipesKnown", this, player.UserIDString));
                return;
            }

            if (firstRecipeToShowIndex > meals.Count - 1  || firstRecipeToShowIndex < 0) firstRecipeToShowIndex = 0;
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {                
                Image = { Color = config.menuSettings.recipeMenuSettings.title_bar_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-309.193 165.979", OffsetMax = "310.807 191.979" }
            }, "Overlay", "RecipesMenu");

            container.Add(new CuiElement
            {
                Name = "title",
                Parent = "RecipesMenu",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("RecipesTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-310 -13", OffsetMax = "310 13" }
                }
            });
            bool foundFirst = false;
            int count = 0;
            int row = 0;
            for (int i = 0; i < meals.Count; i++)
            {                
                if (!foundFirst && i != firstRecipeToShowIndex) continue;
                var recipeData = meals.ElementAt(i);
                if (!recipeData.Value.enabled) continue;
                foundFirst = true;                
                if (count > 2)
                {
                    row++;
                    count = 0;
                }
                container.Add(new CuiPanel
                {                    
                    Image = { Color = config.menuSettings.recipeMenuSettings.recipe_backpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-310 + (count * 210)} {-124.6 - (row * 110)}", OffsetMax = $"{-110 + (count * 210)} {-24.6 - (row * 110)}"  }
                }, "RecipesMenu", $"Recipe_{count}_{row}");

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.recipeMenuSettings.recipe_img_panel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-97 -25", OffsetMax = "-25 47" }
                }, $"Recipe_{count}_{row}", "panel");

                container.Add(new CuiElement
                {
                    Name = "img",
                    Parent = $"Recipe_{count}_{row}",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemID[recipeData.Value.shortname], SkinId = recipeData.Value.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-93 -21", OffsetMax = "-29 43" }
                }
                });

                container.Add(new CuiPanel
                {
                    Image = { Color = !config.mealSettings.cookbookSettings.enabled || CachedRecipes.TryGetValue(player.userID, out var learntMeals) && learntMeals.ContainsKey(recipeData.Key) ? config.menuSettings.recipeMenuSettings.recipe_title_panel_colour_learnt : config.menuSettings.recipeMenuSettings.recipe_title_panel_colour_unlearnt },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-97 -47.5", OffsetMax = "97 -27.5" }
                }, $"Recipe_{count}_{row}", "titlePanel");
                container.Add(new CuiElement
                {
                    Name = "recipe_name",
                    Parent = "titlePanel",
                    Components = {
                    new CuiTextComponent { Text = lang.GetMessage(recipeData.Key, this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-97 -10", OffsetMax = "97 10" }
                }
                });

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.recipeMenuSettings.recipe_info_benefits_backpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-22 -25", OffsetMax = "97 47" }
                }, $"Recipe_{count}_{row}", "infoPanel");

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.recipeMenuSettings.recipe_info_benefits_panel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 20", OffsetMax = "59.5 36" }
                }, "infoPanel", "infoBackpaneltitle");

                container.Add(new CuiElement
                {
                    Name = "infotitle",
                    Parent = "infoBackpaneltitle",
                    Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIBenefits", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.UpperCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -8", OffsetMax = "59.5 8" }
                }
                });
                string benefitsString = string.Empty;
                var elementInfex = 0;
                foreach (var kvp in recipeData.Value.buffs)
                {
                    //if (string.IsNullOrEmpty(benefitsString)) benefitsString += "\n";
                    benefitsString += $"<color=#fbec00>{lang.GetMessage($"{kvp.Key}_ToText", this, player.UserIDString).ToUpper()}</color>{(elementInfex < recipeData.Value.buffs.Count - 1 ? ", ": null)}";
                    elementInfex++;
                }
                container.Add(new CuiElement
                {
                    Name = "infovalues",
                    Parent = "infoPanel",
                    Components = {
                    new CuiTextComponent { Text = benefitsString, Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -21", OffsetMax = "59.5 21" }
                }
                });

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.recipeMenuSettings.recipe_info_missing_ingredients_panel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -36", OffsetMax = "59.5 -21" }
                }, "infoPanel", "haveingredientsbackpanel");
                int result = HasAllIngredients(player, recipeData.Value.ingredients);
                string ingredientText = result == 0 ? "<color=#ff0000>MISSING ALL INGREDIENTS</color>" : result == 1 ? "<color=#d58a08>MISSING SOME INGREDIENTS</color>" : "<color=#00ff00>HAVE ALL INGREDIENTS</color>";

                container.Add(new CuiElement
                {
                    Name = "haveingredientslabel",
                    Parent = "infoPanel",
                    Components = {
                    new CuiTextComponent { Text = ingredientText, Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -36", OffsetMax = "59.5 -21" }
                }
                });
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"senddetailedrecipemenu {firstRecipeToShowIndex} {recipeData.Key}" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-100 -50", OffsetMax = "100 50" }
                }, $"Recipe_{count}_{row}", "button");

                count++;
                if (count == 3 && row == 1) break;
            }

            if (firstRecipeToShowIndex > 5)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendrecipemenupage {firstRecipeToShowIndex - 6}" },
                    Text = { Text = lang.GetMessage("UIPrevious", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-136 -363.973", OffsetMax = "-30 -317.973" }
                }, "RecipesMenu", "Previous");
            }       
            
            if ((meals.Count - 1) - firstRecipeToShowIndex > 5)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendrecipemenupage {firstRecipeToShowIndex + 6}" },
                    Text = { Text = lang.GetMessage("UINext", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "30 -363.973", OffsetMax = "136 -317.973" }
                }, "RecipesMenu", "Next");
            }            

            CuiHelper.DestroyUi(player, "RecipesMenu");
            CuiHelper.AddUi(player, container);
        }
        [ConsoleCommand("sendrecipemenupage")]
        void ChangeRecipeMenuPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var meals = GetMeals(player);
            if (meals == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            var index = Convert.ToInt32(arg.GetString(0));
            SendRecipesMenu(player, index);
        }
        

        [ConsoleCommand("senddetailedrecipemenu")]
        void SendDetailedRecipeMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            var firstMealIndex = arg.GetString(0);
            var meal = string.Join(" ", arg.Args.Skip(1));

            CuiHelper.DestroyUi(player, "RecipesMenu");
            SendRecipeDetailedMenu(player, meal, firstMealIndex, CoreElmentSelected.Recipes);
        }

        #endregion

        #region Recipe detailed menu

        private Dictionary<string, string> QuantityFieldInputText = new Dictionary<string, string>();

        private void SendRecipeDetailedMenu(BasePlayer player, string meal, string firstMealIndex, CoreElmentSelected fromMenu, string quantity = "1")
        {
            if (quantity == "0" || !quantity.IsNumeric()) quantity = "1";

            var meals = GetMeals(player);
            if (meals == null) return;

            MealInfo mealData;
            if (!meals.TryGetValue(meal, out mealData)) return;
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-0.004 4.299", OffsetMax = "-0.003 4.301" }
            }, "Overlay", "ingredients_menu");

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.title_bar_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-309.189 161.679", OffsetMax = "310.811 187.679" }
            }, "ingredients_menu", "MealDetailedMenu");

            container.Add(new CuiElement
            {
                Name = "title",
                Parent = "MealDetailedMenu",
                Components = {
                    new CuiTextComponent { Text = meal.ToUpper(), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-310 -13", OffsetMax = "310 13" }
                }
            });

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.image_backpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-256.9 0.755", OffsetMax = "-128.9 128.755" }
            }, "ingredients_menu", "ingredients_menu_main_img_backpanel");

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.image_frontpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-61 -61", OffsetMax = "61 61" }
            }, "ingredients_menu_main_img_backpanel", "ingredients_menu_main_img_frontpanel");

            container.Add(new CuiElement
            {
                Name = "ingredients_menu_main_img",
                Parent = "ingredients_menu_main_img_backpanel",
                Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemID[mealData.shortname], SkinId = mealData.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-58 -58", OffsetMax = "58 58" }
                }
            });

            var count = 0;
            var row = 0;
            var totalCount = 0;

            var quantity_int = Convert.ToInt32(quantity);
            foreach (var ingredient in mealData.ingredients)
            {
                IngredientsInfo ingredientData;
                if (!config.ingredientSettings.ingredients.TryGetValue(ingredient.Key, out ingredientData)) continue;
                if (count >= 8)
                {
                    count = 0;
                    row++;
                }

                // 0 = non. 1 = some. 2 == enough
                List<BagInfo> bagData;
                pcdData.bag.TryGetValue(player.userID, out bagData);
                var ingredientCount = GetIngredientCount(player, ingredient.Key, bagData);

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.recipeDetailedMenuSettings.ingredient_image_backpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-95.1 + (count * 74)} {72.755}", OffsetMax = $"{-39.1 + (count * 74)} {128.755 - (row * 124)}" }
                }, "ingredients_menu", $"ingredients_menu_backpanel_{count}_{row}");

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.recipeDetailedMenuSettings.ingredient_image_frontpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-26 -26", OffsetMax = "26 26" }
                }, $"ingredients_menu_backpanel_{count}_{row}", "ingredients_menu_frontpanel_1");

                container.Add(new CuiElement
                {
                    Name = "ingredients_menu_img_1",
                    Parent = $"ingredients_menu_backpanel_{count}_{row}",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemID[ingredientData.shortname], SkinId = ingredientData.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-26 -26", OffsetMax = "26 26" }
                }
                });

                var hasEnough = ingredientData.notConsumed ? ingredientCount >= ingredient.Value : ingredientCount >= ingredient.Value * quantity_int;
                container.Add(new CuiElement
                {
                    Name = "ingredients_menu_quantity_1",
                    Parent = "ingredients_menu_img_1",
                    Components = {
                    new CuiTextComponent { Text = String.Format(lang.GetMessage("UIIngredientCount", this, player.UserIDString), (hasEnough ? "<color=#42f105>":null), ingredientCount, ingredientData.notConsumed ? ingredient.Value : ingredient.Value * quantity_int, (hasEnough ? "</color>":null)), Font = "robotocondensed-regular.ttf", FontSize = ingredientCount < 10000 ? 12 : 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-28 -28", OffsetMax = "28 0" }
                }
                });

                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendingredienttip {ingredient.Key}" },
                    Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-28 -28", OffsetMax = "28 28" }
                }, $"ingredients_menu_backpanel_{count}_{row}", "ingredients_menu_ingredient_button_1");

                count++;
                totalCount++;
                if (totalCount >= 10) break;
            }

            string descriptionField = mealData.description + "\n";
            foreach (var buff in mealData.buffs)
            {
                if (buff.Key != Buff.Permission) descriptionField += "\n" + GetBuffDescriptions(player, buff.Key, buff.Value);
            }

            if (mealData.commandsOnConsume != null)
            {
                foreach (var perm in mealData.commandsOnConsume)
                    descriptionField += $"\n<color=#4600ff>Command:</color> {perm.Value}";
            }

            descriptionField += "\n";
            if (mealData.duration > 0) descriptionField += $"\n{string.Format(lang.GetMessage("UIDuration", this, player.UserIDString), mealData.duration)}";
            descriptionField += $"\n{string.Format(lang.GetMessage("UICookTime", this, player.UserIDString), mealData.cookTime)}";
            if (mealData.useCooldown > 0) descriptionField += $"\n{string.Format(lang.GetMessage("UICooldown", this, player.UserIDString), mealData.useCooldown)}";
            if (!string.IsNullOrEmpty(mealData.permissionToCook)) descriptionField += $"\n{string.Format(lang.GetMessage("UICanCook", this, player.UserIDString), (permission.UserHasPermission(player.UserIDString, mealData.permissionToCook) ? lang.GetMessage("UIYes", this, player.UserIDString) : lang.GetMessage("UINo", this, player.UserIDString)))}";

            container.Add(new CuiElement
            {
                Name = "ingredients_menu_description_1",
                Parent = "ingredients_menu",
                Components = {
                    new CuiTextComponent { Text = descriptionField, Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-256.9 -276.956", OffsetMax = "123.1 -26.956" }
                }
            });

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.quantity_backpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "139.9 -17.7", OffsetMax = "245.9 30.3" }
            }, "ingredients_menu", "Quantity");

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.quantity_frontpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -21", OffsetMax = "50 21" }
            }, "Quantity", "qtInnerpanel");

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.quantity_inputtext_panel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -21", OffsetMax = "50 0" }
            }, "qtInnerpanel", "inputtextpanel");

            container.Add(new CuiElement
            {
                Name = "title",
                Parent = "qtInnerpanel",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIQuantity", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 0", OffsetMax = "50 21" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "input_parent",
                Parent = "qtInnerpanel",
                Components =
                {
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-50 -21",
                        OffsetMax = "50 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = "inputText",
                Parent = "input_parent",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = quantity ?? string.Empty,
                        CharsLimit = 40,
                        Color = "1 1 1 1",
                        IsPassword = false,
                        Command = $"{"qtinnerpanel.delayinputtextcb"} {firstMealIndex} {(int)fromMenu} {meal}",
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 16,
                        //NeedsKeyboard = true,
                        Align = TextAnchor.MiddleCenter,
                        HudMenuInput = true
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            });

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.craft_button_backpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "139.9 -82.956", OffsetMax = "245.9 -34.956" }
            }, "ingredients_menu", "ingredients_menu_craft_backpanel");

            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.recipeDetailedMenuSettings.craft_button_frontpanel_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -21", OffsetMax = "50 21" }
            }, "ingredients_menu_craft_backpanel", "ingredients_menu_craft_frontpanel_1");

            bool learnt = config.mealSettings.cookbookSettings.enabled ? CachedRecipes.TryGetValue(player.userID, out var cachedMeals) && cachedMeals.ContainsKey(meal) : true;

            container.Add(new CuiElement
            {
                Name = "ingredients_menu_craft_text_1",
                Parent = "ingredients_menu_craft_backpanel",
                Components = {
                    new CuiTextComponent { Text = learnt ? lang.GetMessage("UICraft", this, player.UserIDString) : lang.GetMessage("UIUnlearnt", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 25, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -21", OffsetMax = "50 21" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = learnt ? $"trycraftdish {quantity} {firstMealIndex} {(int)fromMenu} {meal}" : "" },
                Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -21", OffsetMax = "50 21" }
            }, "ingredients_menu_craft_backpanel", "ingredients_menu_craft_button_1");

            ulong fav_selected = config.menuSettings.favourite_unselected;
            PCDInfo pi;
            if (pcdData.pEntity.TryGetValue(player.userID, out pi) && pi.favouriteDishes.Contains(meal)) fav_selected = config.menuSettings.favourite_selected;

            container.Add(new CuiElement
            {
                Name = "Ingredients_menu_favourite_img",
                Parent = "ingredients_menu",
                Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = 1776460938, SkinId = fav_selected },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "176.9 -136.6", OffsetMax = "208.9 -104.6" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = $"addtofavourites {quantity} {firstMealIndex} {(int)fromMenu} {meal}" },
                Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-16 -16", OffsetMax = "16 16" }
            }, "Ingredients_menu_favourite_img", "Ingredients_menu_favourite_button");

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = $"gobacktorecipemenu {firstMealIndex} {(int)fromMenu} {meal}" },
                Text = { Text = lang.GetMessage("UIBack", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "139.9 -182.6", OffsetMax = "245.9 -136.6" }
            }, "ingredients_menu", "Back");

            CuiHelper.DestroyUi(player, "ingredients_menu");
            CuiHelper.AddUi(player, container);
        }

        string GetBuffDescriptions(BasePlayer player, Buff buff, float modifier)
        {
            switch (buff)
            {
                case Buff.Wealth: 
                    return $"<color=#07a99d>{lang.GetMessage($"{buff}_ToText", this, player.UserIDString)}:</color> {string.Format(lang.GetMessage($"{buff}_MenuDescription", this, player.UserIDString), modifier * 100, lang.GetMessage("CurrencyString", this, player.UserIDString))}";
                case Buff.Passive_Regen:    
                case Buff.Damage_Over_Time: 
                case Buff.Damage:           
                case Buff.Radiation:        
                    return $"<color=#07a99d>{lang.GetMessage($"{buff}_ToText", this, player.UserIDString)}:</color> {string.Format(lang.GetMessage($"{buff}_MenuDescription", this, player.UserIDString), modifier)}";
                default: 
                    return $"<color=#07a99d>{lang.GetMessage($"{buff}_ToText", this, player.UserIDString)}:</color> {string.Format(lang.GetMessage($"{buff}_MenuDescription", this, player.UserIDString), modifier * 100)}";
            }
        }

        [ConsoleCommand("trycraftdish")]
        void TryCraftDish(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var quantity = Convert.ToInt32(arg.GetString(0));
            var meal = string.Join(" ", arg.Args.Skip(3));
            var coreElement = Convert.ToInt32(arg.GetString(2));

            MealInfo mealData;
            if (!config.mealSettings.meals.TryGetValue(meal, out mealData)) return;

            if (!string.IsNullOrEmpty(mealData.permissionToCook) && !permission.UserHasPermission(player.UserIDString, mealData.permissionToCook))
            {
                PrintToChat(player, string.Format(lang.GetMessage("NoPermToCook", this, player.UserIDString), lang.GetMessage(meal, this, player.UserIDString)));
                return;
            }

            var cookingComponent = GetCookingManager(player);
            if (config.mealSettings.maxCookQueue > 0)
            {
                var queued = cookingComponent.Queued();
                if (queued >= config.mealSettings.maxCookQueue)
                {
                    PrintToChat(player, lang.GetMessage("MaxQueueSize", this, player.UserIDString));
                    return;
                }

                if (quantity + queued > config.mealSettings.maxCookQueue)
                    quantity = config.mealSettings.maxCookQueue - queued;
            }


            Dictionary<string, IngredientItemInfo> ingredientsUsed = new Dictionary<string, IngredientItemInfo>();
            if (!permission.UserHasPermission(player.UserIDString, perm_free))
            {
                Dictionary<string, int> totalIngredients = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
                foreach (var kvp in mealData.ingredients)
                {
                    totalIngredients.Add(kvp.Key, kvp.Value * quantity);
                }

                if (HasAllIngredients(player, totalIngredients) < 2)
                {
                    PrintToChat(player, lang.GetMessage("NoIngredients", this, player.UserIDString));
                    if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.invalid_cook_sound)) SendEffect(player, config.generalSettings.soundSettings.invalid_cook_sound);
                    return;
                }

                List<string> remove = Pool.Get<List<string>>();
                foreach (var ingredient in totalIngredients)
                {
                    IngredientsInfo ingredientData;
                    if (!config.ingredientSettings.ingredients.TryGetValue(ingredient.Key, out ingredientData) || !ingredientData.notConsumed) continue;
                    remove.Add(ingredient.Key);
                }

                foreach (var key in remove)
                    totalIngredients.Remove(key);

                Pool.FreeUnmanaged(ref remove);

                PayForMeal(player, quantity, totalIngredients, ingredientsUsed);

                totalIngredients.Clear();
            }

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.cook_queued_sound)) SendEffect(player, config.generalSettings.soundSettings.cook_queued_sound);

            var cookTime = GetModifiedCookTime(player, mealData.cookTime);


            if (!permission.UserHasPermission(player.UserIDString, perm_instant) && cookTime > 0)
            {                
                for (int i = 0; i < quantity; i++)
                {
                    cookingComponent.AddMeal(mealData.shortname, meal, mealData.skin, cookTime, mealData.spoilTime, ingredientsUsed);
                }
            }
            else
            {
                GiveMeal(player, mealData.shortname, mealData.skin, meal.ToLower(), mealData.spoilTime, quantity);
                Interface.CallHook("OnMealCrafted", player, meal, mealData.ingredients, config.ingredientSettings.ingredients.ContainsKey(meal));
            }
            
            SendRecipeDetailedMenu(player, meal, arg.GetString(1),(CoreElmentSelected)coreElement, arg.GetString(0));
        }

        float GetModifiedCookTime(BasePlayer player, float defaultCookTime)
        {
            var highest = 0f;
            foreach (var perm in SkillTreePermission_CookingTime)
            {
                if (perm.Value > highest && permission.UserHasPermission(player.UserIDString, perm.Key)) highest = perm.Value;
            }

            return defaultCookTime - (highest * defaultCookTime);
        }

        [ConsoleCommand("gobacktorecipemenu")]
        void GoBackToRecipeMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_back_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_back_sound);

            var index = Convert.ToInt32(arg.GetString(0));
            var coreElement = Convert.ToInt32(arg.GetString(1));
            var meal = string.Join(" ", arg.Args.Skip(2));
            if ((CoreElmentSelected)coreElement == CoreElmentSelected.Recipes)
            {
                UnsendOtherPanels(player, CoreElmentSelected.Recipes);
                SendRecipesMenu(player, index);
            }
            else
            {
                UnsendOtherPanels(player, CoreElmentSelected.Favourites);
                FavouritesMenu(player, index);
            }
        }


        [ConsoleCommand("qtinnerpanel.delayinputtextcb")]
        void DelayInputText(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string quantity = arg.GetString(arg.Args.Length - 1);
            string firstIndex = arg.GetString(0);
            int coreElement = Convert.ToInt32(arg.GetString(1));

            int qty;
            if (!int.TryParse(quantity, out qty)) qty = 1;
            if (qty < 1) quantity = "1";
            else if (qty > 100) quantity = "100";

            if (QuantityFieldInputText.ContainsKey(player.UserIDString))
            {
                QuantityFieldInputText[player.UserIDString] = quantity;
            }
            else
            {
                QuantityFieldInputText.Add(player.UserIDString, quantity);
            }
            string meal = string.Join(" ", arg.Args.SkipLast(1).Skip(2));
            SendRecipeDetailedMenu(player, meal, firstIndex, (CoreElmentSelected)coreElement, quantity);
        }

        [ConsoleCommand("sendingredienttip")]
        void SendIngredientTip(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            var ingredient = string.Join(" ", arg.Args);

            IngredientsInfo ingredientData;
            if (!config.ingredientSettings.ingredients.TryGetValue(ingredient, out ingredientData)) return;

            SendIngredientText(player, ingredient, ingredientData);
        }

        [ConsoleCommand("addtofavourites")]
        void AddToFavourites(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var quantity = QuantityFieldInputText.ContainsKey(player.UserIDString) ? QuantityFieldInputText[player.UserIDString] : arg.GetString(0);
            var firstMealIndex = arg.GetString(1);
            var coreElement = Convert.ToInt32(arg.GetString(2));
            var meal = string.Join(" ", arg.Args.Skip(3));

            PCDInfo pi;
            if (!pcdData.pEntity.TryGetValue(player.userID, out pi)) pcdData.pEntity.Add(player.userID, pi = new PCDInfo());

            if (pi.favouriteDishes.Contains(meal))
            {
                if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.remove_from_favourites_sound)) SendEffect(player, config.generalSettings.soundSettings.remove_from_favourites_sound);
                pi.favouriteDishes.Remove(meal);
            }                
            else
            {
                if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.add_to_favourites_sound)) SendEffect(player, config.generalSettings.soundSettings.add_to_favourites_sound);
                pi.favouriteDishes.Add(meal);
            }                

            SendRecipeDetailedMenu(player, meal, firstMealIndex,(CoreElmentSelected)coreElement, quantity);
        }

        #endregion

        #region Send ingredient text

        private void SendIngredientText(BasePlayer player, string ingredient, IngredientsInfo ingredientData)
        {
            var locationString = GetGatherSourcesString(player, ingredientData.gatherSources, ingredientData.customSources);
            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = "SendIngredientText",
                Parent = "Overlay",
                Components = {
                    new CuiTextComponent { Text = String.Format(lang.GetMessage("UISendIngredientText", this, player.UserIDString), lang.GetMessage(ingredient, this, player.UserIDString), string.Join(", ", locationString)), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleLeft, Color = "1 0.6326198 0 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-95.104 133.053", OffsetMax = "484.87 159.048" }
                }
            });

            if (locationString != null) Pool.FreeUnmanaged(ref locationString);

            CuiHelper.DestroyUi(player, "SendIngredientText");
            CuiHelper.AddUi(player, container);
        }

        List<string> GetGatherSourcesString(BasePlayer player, List<GatherSource> sources, List<string> customSources)
        {
            List<string> result = Pool.Get<List<string>>();

            foreach (var gatherSource in sources)
            {
                result.Add(lang.GetMessage($"{gatherSource}_LocationText", this, player.UserIDString));
            }
            if (customSources != null)
                foreach (var source in customSources)
                    result.Add(source);
            return result;
        }

        #endregion

        #region Crafting Queue

        public void UpdateCookingQueue(BasePlayer player, ulong skin, int time, int queuelength, string CookingQueueBackPanelColour, string CookingQueueAnchorMin, string CookingQueueAnchorMax, string CookingQueueOffsetMin, string CookingQueueOffsetMax, string CookingQueueFrontPanelColour)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                
                Image = { Color = CookingQueueBackPanelColour },
                RectTransform = { AnchorMin = CookingQueueAnchorMin, AnchorMax = CookingQueueAnchorMax, OffsetMin = CookingQueueOffsetMin, OffsetMax = CookingQueueOffsetMax }
            }, "Overlay", "CookingQueue");

            container.Add(new CuiPanel
            {
                
                Image = { Color = CookingQueueFrontPanelColour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-16 -16", OffsetMax = "16 16" }
            }, "CookingQueue", "Panel_8050");

            container.Add(new CuiElement
            {
                Name = "img",
                Parent = "CookingQueue",
                Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = 1751045826, SkinId = skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-16 -16", OffsetMax = "16 16" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "time",
                Parent = "CookingQueue",
                Components = {
                    new CuiTextComponent { Text = "%TIME_LEFT%", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },                    
                    new CuiCountdownComponent { StartTime = time, EndTime = 0, Step = 1, Interval = 1f, TimerFormat = TimerFormat.MinutesSeconds, DestroyIfDone = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-18 -18", OffsetMax = "18 18" }
                }
            });

            if (queuelength > 1)
            {
                container.Add(new CuiElement
                {
                    Name = "queuelength",
                    Parent = "CookingQueue",
                    Components = {
                    new CuiTextComponent { Text = String.Format(lang.GetMessage("UICookQueueTime", this, player.UserIDString), queuelength - 1), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.8881326 0.9339623 0 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-53.1 -16", OffsetMax = "-21.1 16" }
                }
                });
            }


            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = "cancelcurrentcook" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-16 -16", OffsetMax = "16 16" }
            }, "CookingQueue", "Button_5038");

            CuiHelper.DestroyUi(player, "CookingQueue");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("cancelcurrentcook")]
        void CancelCurrentCook(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.cancel_cook_sound)) SendEffect(player, config.generalSettings.soundSettings.cancel_cook_sound);

            var component = GetCookingManager(player);
            if (component != null) component.CancelCurrentCraft();
        }

        #endregion

        #region Favourites menu

        private void FavouritesMenu(BasePlayer player, int firstRecipeToShowIndex)
        {
            PCDInfo pi;
            if (!pcdData.pEntity.TryGetValue(player.userID, out pi) || pi.favouriteDishes.Count == 0) return;

            if (firstRecipeToShowIndex < 0 || firstRecipeToShowIndex > pi.favouriteDishes.Count - 1) firstRecipeToShowIndex = 0;

            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.favouriteMenuSettings.title_bar_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-309.193 165.979", OffsetMax = "310.807 191.979" }
            }, "Overlay", "FavouritesMenu");

            container.Add(new CuiElement
            {
                Name = "title",
                Parent = "FavouritesMenu",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("FavouritesTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-310 -13", OffsetMax = "310 13" }
                }
            });

            bool foundFirst = false;
            int count = 0;
            int row = 0;
            for (int i = 0; i < pi.favouriteDishes.Count; i++)
            {
                if (!foundFirst && i != firstRecipeToShowIndex) continue;
                var meal = pi.favouriteDishes.ElementAt(i);
                MealInfo mealData;
                if (!config.mealSettings.meals.TryGetValue(meal, out mealData) || !mealData.enabled) continue;

                foundFirst = true;
                if (count > 2)
                {
                    row++;
                    count = 0;
                }

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.favouriteMenuSettings.recipe_backpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-310 + (count * 210)} {-124.6 - (row * 110)}", OffsetMax = $"{-110 + (count * 210)} {-24.6 - (row * 110)}" }
                }, "FavouritesMenu", $"Favourites_{count}_{row}");

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.favouriteMenuSettings.recipe_img_panel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-97 -25", OffsetMax = "-25 47" }
                }, $"Favourites_{count}_{row}", "panel");

                container.Add(new CuiElement
                {
                    Name = "img",
                    Parent = $"Favourites_{count}_{row}",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = 1751045826, SkinId = mealData.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-93 -21", OffsetMax = "-29 43" }
                }
                });

                container.Add(new CuiPanel
                {
                    
                    //Image = { Color = "0.490566 0.490566 0.490566 1" },
                    Image = { Color = !config.mealSettings.cookbookSettings.enabled || CachedRecipes.TryGetValue(player.userID, out var learntMeals) && learntMeals.ContainsKey(meal) ? config.menuSettings.recipeMenuSettings.recipe_title_panel_colour_learnt : config.menuSettings.recipeMenuSettings.recipe_title_panel_colour_unlearnt },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-97 -47.5", OffsetMax = "97 -27.5" }
                }, $"Favourites_{count}_{row}", "titlePanel");

                container.Add(new CuiElement
                {
                    Name = "recipe_name",
                    Parent = "titlePanel",
                    Components = {
                    new CuiTextComponent { Text = lang.GetMessage(meal, this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-97 -10", OffsetMax = "97 10" }
                }
                });

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.favouriteMenuSettings.recipe_info_benefits_backpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-22 -25", OffsetMax = "97 47" }
                }, $"Favourites_{count}_{row}", "infoPanel");

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.favouriteMenuSettings.recipe_info_benefits_panel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 20", OffsetMax = "59.5 36" }
                }, "infoPanel", "infoBackpaneltitle");

                container.Add(new CuiElement
                {
                    Name = "infotitle",
                    Parent = "infoBackpaneltitle",
                    Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIBenefits", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.UpperCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -8", OffsetMax = "59.5 8" }
                }
                });

                string benefitsString = string.Empty;
                foreach (var kvp in mealData.buffs)
                {
                    if (string.IsNullOrEmpty(benefitsString)) benefitsString += "\n";
                    benefitsString += $"<color=#fbec00>{lang.GetMessage($"{kvp.Key}_ToText", this, player.UserIDString).ToUpper()}</color>";
                }

                container.Add(new CuiElement
                {
                    Name = "infovalues",
                    Parent = "infoPanel",
                    Components = {
                    new CuiTextComponent { Text = benefitsString, Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -21", OffsetMax = "59.5 21" }
                }
                });

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.favouriteMenuSettings.recipe_info_missing_ingredients_panel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -36", OffsetMax = "59.5 -21" }
                }, "infoPanel", "haveingredientsbackpanel");

                int result = HasAllIngredients(player, mealData.ingredients);
                string ingredientText = result == 0 ? "<color=#ff0000>MISSING ALL INGREDIENTS</color>" : result == 1 ? "<color=#d58a08>MISSING SOME INGREDIENTS</color>" : "<color=#00ff00>HAVE ALL INGREDIENTS</color>";

                container.Add(new CuiElement
                {
                    Name = "haveingredientslabel",
                    Parent = "infoPanel",
                    Components = {
                    new CuiTextComponent { Text = ingredientText, Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-59.5 -36", OffsetMax = "59.5 -21" }
                }
                });

                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendrecipefromfavourites {firstRecipeToShowIndex} {meal}" },
                    Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-100 -50", OffsetMax = "100 50" }
                }, $"Favourites_{count}_{row}", "button");

                count++;
                if (count == 3 && row == 1) break;
            }       
            
            if (firstRecipeToShowIndex > 5)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendfavouritemenupage {firstRecipeToShowIndex - 6}" },
                    Text = { Text = lang.GetMessage("UIPrevious", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-136 -363.973", OffsetMax = "-30 -317.973" }
                }, "FavouritesMenu", "Previous");
            }

            if ((pi.favouriteDishes.Count - 1) - firstRecipeToShowIndex > 5)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendfavouritemenupage {firstRecipeToShowIndex + 6}" },
                    Text = { Text = lang.GetMessage("UINext", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "30 -363.973", OffsetMax = "136 -317.973" }
                }, "FavouritesMenu", "Next");
            }                

            CuiHelper.DestroyUi(player, "FavouritesMenu");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("sendfavouritemenupage")]
        void ChangeFavouritesPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            var index = Convert.ToInt32(arg.GetString(0));
            FavouritesMenu(player, index);
        }

        [ConsoleCommand("sendrecipefromfavourites")]
        void SendDetailedRecipeMenuFromFavourites(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            var meal = string.Join(" ", arg.Args.Skip(1));
            CuiHelper.DestroyUi(player, "FavouritesMenu");

            SendRecipeDetailedMenu(player, meal, arg.GetString(0), CoreElmentSelected.Favourites);
        }

        #endregion

        #region Ingredients Menu

        private void IngredientsMenu(BasePlayer player, int firstIngredientIndex = 0)
        {
            List<KeyValuePair<string, IngredientsInfo>> ingredients = Pool.Get<List<KeyValuePair<string, IngredientsInfo>>>();
            foreach (var ingredient in config.ingredientSettings.ingredients)
            {
                if (ingredient.Value.enabled)
                    ingredients.Add(new KeyValuePair<string, IngredientsInfo>(ingredient.Key, ingredient.Value));
            }

            if (firstIngredientIndex < 0 || firstIngredientIndex > (ingredients.Count - 1)) firstIngredientIndex = 0;

            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                
                Image = { Color = config.menuSettings.ingredientMenuSettings.title_bar_colour },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-309.193 165.979", OffsetMax = "310.807 191.979" }
            }, "Overlay", "IngredientsMenu");

            container.Add(new CuiElement
            {
                Name = "title",
                Parent = "IngredientsMenu",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("IngredientsTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-310 -13", OffsetMax = "310 13" }
                }
            });

            var count = 0;
            var row = 0;
            var indexCount = 0;
            var totalCount = 0;
            foreach (var ingredient in ingredients)
            {
                if (indexCount < firstIngredientIndex)
                {
                    indexCount++;
                    continue;
                }               

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.ingredientMenuSettings.ingredient_backpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-269.4 + (count * 326)} {-87.71 - (row * 96)}", OffsetMax = $"{-205.4 + (count * 326)} {-23.71 - (row * 96)}" }
                }, "IngredientsMenu", $"Ingredient_{count}_{row}");

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = config.menuSettings.ingredientMenuSettings.ingredient_frontpanel_colour },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-29 -29", OffsetMax = "29 29" }
                }, $"Ingredient_{count}_{row}", "innerpanel");

                container.Add(new CuiElement
                {
                    Name = "img",
                    Parent = $"Ingredient_{count}_{row}",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemID[ingredient.Value.shortname], SkinId = ingredient.Value.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-29 -29", OffsetMax = "29 29" }
                }
                });

                container.Add(new CuiElement
                {
                    Name = "name",
                    Parent = $"Ingredient_{count}_{row}",
                    Components = {
                    new CuiTextComponent { Text = String.Format(lang.GetMessage("UIIngredientMenuIngredient", this, player.UserIDString), lang.GetMessage(ingredient.Key, this, player.UserIDString)), Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "48.8 8", OffsetMax = "228.8 32" }
                }
                });

                container.Add(new CuiElement
                {
                    Name = "infopaneltitle",
                    Parent = $"Ingredient_{count}_{row}",
                    Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIGatheredFrom", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "48.8 -12.117", OffsetMax = "138.8 7.883" }
                }
                });                

                var gcount = 0;
                var rowCount = 0;
                var sourceCount = 0;

                List<string> sources = Pool.Get<List<string>>();
                foreach (var source in ingredient.Value.gatherSources)
                    sources.Add(lang.GetMessage($"{source}_LocationText", this, player.UserIDString));
                if (ingredient.Value.customSources != null)
                    foreach (var source in ingredient.Value.customSources)
                        sources.Add(source);

                foreach (var source in sources)
                {
                    container.Add(new CuiElement
                    {
                        Name = $"source_{gcount}_{rowCount}",
                        Parent = $"Ingredient_{count}_{row}",
                        Components = {
                        new CuiTextComponent { Text = $"{lang.GetMessage("UIDash", this, player.UserIDString)} {source}", Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                        new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{48.168 + (gcount * 100)} {-28.117 - (rowCount * 16)}", OffsetMax = $"{158.168 + (gcount * 100)} {-12.117 - (rowCount * 16)}" }
                    }
                    });
                    rowCount++;
                    sourceCount++;
                    if (rowCount >= 3)
                    {
                        rowCount = 0;
                        gcount++;
                    }

                    if (sourceCount == 6) break;
                }
                Pool.FreeUnmanaged(ref sources);

                count++;
                totalCount++;
                if (count >= 2)
                {
                    count = 0;
                    row++;
                }

                if (totalCount >= 6) break;
            }            

            if (firstIngredientIndex > 5)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendingredientpagechange {firstIngredientIndex - 6}" },
                    Text = { Text = lang.GetMessage("UIPrevious", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-136 -363.973", OffsetMax = "-30 -317.973" }
                }, "IngredientsMenu", "Previous");
            }         
            
            if ((ingredients.Count - 1) - firstIngredientIndex > 5)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendingredientpagechange {firstIngredientIndex + 6}" },
                    Text = { Text = lang.GetMessage("UINext", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "30 -363.973", OffsetMax = "136 -317.973" }
                }, "IngredientsMenu", "Next");
            }            

            Pool.FreeUnmanaged(ref ingredients);

            CuiHelper.DestroyUi(player, "IngredientsMenu");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("sendingredientpagechange")]
        void SendIngredientPageChange(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            var page = Convert.ToInt32(arg.GetString(0));
            IngredientsMenu(player, page);
        }

        #endregion

        #region Settings

        public enum ButtonType
        {
            Toggle,
            Press
        }

        public enum SettingType
        {
            ToggleNotificationDrops,
            ToggleNotificationTriggers,
            ToggleSound,
            ToggleDrops,
            AdjustUI_BuffIcons,
            AdjustUI_CraftQueue,
            AdjustUI_CookingButton,
            AdjustUI_UIButtons
        }

        public class ButtonInfo
        {
            public string command;
            public SettingType settingType;
            public ButtonType buttonType;
            public bool adminRequired;

            public ButtonInfo(string command, SettingType settingType, ButtonType buttonType, bool adminRequired = false)
            {
                this.command = command;
                this.buttonType = buttonType;
                this.settingType = settingType;
                this.adminRequired = adminRequired;
            }
        }

        
        List<ButtonInfo> SettingsButtons = new List<ButtonInfo>()
        {
            new ButtonInfo("toggledropnotifications", SettingType.ToggleNotificationDrops, ButtonType.Toggle),
            new ButtonInfo("togglebuffnotifications", SettingType.ToggleNotificationTriggers, ButtonType.Toggle),
            new ButtonInfo("senduimovercraftqueue", SettingType.AdjustUI_CraftQueue, ButtonType.Press, true),
            new ButtonInfo("senduimoverbufficons", SettingType.AdjustUI_BuffIcons, ButtonType.Press, true),
            new ButtonInfo("senduimovercookingbutton", SettingType.AdjustUI_CookingButton, ButtonType.Press, true),
            new ButtonInfo("senduimoveruibuttons", SettingType.AdjustUI_UIButtons, ButtonType.Press, true),
            new ButtonInfo("togglesoundforcooking", SettingType.ToggleSound, ButtonType.Toggle),
            new ButtonInfo("toggledropsforcooking", SettingType.ToggleDrops, ButtonType.Toggle),
        };

        private void PlayerSettings(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                
                Image = { Color = "0.2941177 0.2901961 0.2784314 0.8" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-309.193 165.979", OffsetMax = "310.807 191.979" }
            }, "Overlay", "PlayerSettings");

            container.Add(new CuiElement
            {
                Name = "title",
                Parent = "PlayerSettings",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("SettingsTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-310 -13", OffsetMax = "310 13" }
                }
            });

            var count = 0;
            var row = 0;
            
            foreach (var button in SettingsButtons)
            {
                if (button.adminRequired && !permission.UserHasPermission(player.UserIDString, perm_admin)) continue;
                container.Add(new CuiPanel
                {
                    
                    Image = { Color = "0.09411765 0.09411765 0.09411765 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-310 + (count * 217)} {-69.889 - (row * 66)}", OffsetMax = $"{-124 + (count * 217)} {-23.889 - (row * 66)}" }
                }, "PlayerSettings", $"Menu_{count}_{row}");

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = "0.2941177 0.2901961 0.2784314 0.8" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-90 -20", OffsetMax = "90 20" }
                }, $"Menu_{count}_{row}", "innerpanel");

                container.Add(new CuiElement
                {
                    Name = "description",
                    Parent = $"Menu_{count}_{row}",
                    Components = {
                    new CuiTextComponent { Text = lang.GetMessage($"{button.settingType}_Description", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-86.034 -15", OffsetMax = "32.522 15" }
                }
                });

                container.Add(new CuiPanel
                {
                    
                    Image = { Color = "0.09411765 0.09411765 0.09411765 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "35 -15", OffsetMax = "85 15" }
                }, $"Menu_{count}_{row}", "buttonPanel");

                string buttonText = string.Empty;
                switch (button.settingType)
                {
                    case SettingType.ToggleNotificationDrops:
                        if (!permission.UserHasPermission(player.UserIDString, perm_disable_notify_drop)) buttonText = lang.GetMessage("UIOn", this, player.UserIDString);
                        else buttonText = lang.GetMessage("UIOff", this, player.UserIDString);
                        break;

                    case SettingType.ToggleNotificationTriggers:
                        if (!permission.UserHasPermission(player.UserIDString, perm_disable_notify_proc)) buttonText = lang.GetMessage("UIOn", this, player.UserIDString);
                        else buttonText = lang.GetMessage("UIOff", this, player.UserIDString);
                        break;

                    case SettingType.ToggleSound:
                        if (!permission.UserHasPermission(player.UserIDString, perm_disable_sound)) buttonText = lang.GetMessage("UIOn", this, player.UserIDString);
                        else buttonText = lang.GetMessage("UIOff", this, player.UserIDString);
                        break;
                    case SettingType.ToggleDrops:
                        if (!permission.UserHasPermission(player.UserIDString, perm_nogather)) buttonText = lang.GetMessage("UIOn", this, player.UserIDString);
                        else buttonText = lang.GetMessage("UIOff", this, player.UserIDString);
                        break;

                    case SettingType.AdjustUI_CookingButton:
                    case SettingType.AdjustUI_CraftQueue:
                    case SettingType.AdjustUI_BuffIcons:
                    case SettingType.AdjustUI_UIButtons:
                        buttonText = lang.GetMessage("UIMoveButton", this, player.UserIDString);
                        break;
                }
                container.Add(new CuiButton
                {
                    Button = { Color = "0.4339623 0.4339623 0.4339623 1", Command = button.command },
                    Text = { Text = buttonText, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.08224613 0.6603774 0 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-23 -13", OffsetMax = "23 13" }
                }, "buttonPanel", "button");

                count++;
                if (count >= 3)
                {
                    count = 0;
                    row++;
                }
            }            

            CuiHelper.DestroyUi(player, "PlayerSettings");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("togglesoundforcooking")]
        void ToggleSoundForPlayer(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var active = !permission.UserHasPermission(player.UserIDString, perm_disable_sound);
            if (active) permission.GrantUserPermission(player.UserIDString, perm_disable_sound, this);
            else
            {
                permission.RevokeUserPermission(player.UserIDString, perm_disable_sound);
                if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);
            }

            PlayerSettings(player);
        }

        [ConsoleCommand("toggledropsforcooking")]
        void ToggleDropsForPlayer(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var active = !permission.UserHasPermission(player.UserIDString, perm_nogather);
            if (active) permission.GrantUserPermission(player.UserIDString, perm_nogather, this);
            else
            {
                permission.RevokeUserPermission(player.UserIDString, perm_nogather);
                if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);
            }

            PlayerSettings(player);
        }
        
        [ConsoleCommand("senduimovercraftqueue")]
        void SendUIMover_CraftQueue(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);

            DestroyRecipeMenuUI(player);
            SendUiMover(player, UI.CraftQueue);
        }

        [ConsoleCommand("senduimoverbufficons")]
        void SendUIMover_BuffIcons(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);

            DestroyRecipeMenuUI(player);
            SendUiMover(player, UI.BuffIcon);
        }

        [ConsoleCommand("senduimovercookingbutton")]
        void SendUIMover_CookingButton(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);

            DestroyRecipeMenuUI(player, true);
            SendUiMover(player, UI.OvenButton);
        }

        [ConsoleCommand("senduimoveruibuttons")]
        void SendUIMover_UIButtons(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);

            DestroyRecipeMenuUI(player, true);
            SendUiMover(player, UI.UIButtons);
        }

        [ConsoleCommand("toggledropnotifications")]
        void ToggleDropNotifications(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);

            var hasPerm = permission.UserHasPermission(player.UserIDString, perm_disable_notify_drop);
            if (hasPerm) permission.RevokeUserPermission(player.UserIDString, perm_disable_notify_drop);
            else permission.GrantUserPermission(player.UserIDString, perm_disable_notify_drop, this);

            PlayerSettings(player);
        }

        [ConsoleCommand("togglebuffnotifications")]
        void ToggleBuffNotifications(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.settings_buttons_sound)) SendEffect(player, config.generalSettings.soundSettings.settings_buttons_sound);

            var hasPerm = permission.UserHasPermission(player.UserIDString, perm_disable_notify_proc);
            if (hasPerm) permission.RevokeUserPermission(player.UserIDString, perm_disable_notify_proc);
            else permission.GrantUserPermission(player.UserIDString, perm_disable_notify_proc, this);

            PlayerSettings(player);
        }

        #endregion

        #region Buff UI

        private void BuffUI(BasePlayer player, Dictionary<string, BuffInfo> buffData, string backpanelCol, string frontpanelCol, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                
                Image = { Color = "0.1981132 0.1981132 0.1981132 0" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
            }, "Hud", "BuffUI");

            var count = 0;
            foreach (var kvp in buffData)
            {                
                container.Add(new CuiPanel
                {
                    Image = { Color = backpanelCol },
                    RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = $"-46 {-23 + (count * 50)}", OffsetMax = $"0 {23 + (count * 50)}" }
                }, "BuffUI", $"Buff_{count}");

                container.Add(new CuiPanel
                {
                    Image = { Color = frontpanelCol },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-21 -21", OffsetMax = "21 21" }
                }, $"Buff_{count}", "Inner");

                container.Add(new CuiElement
                {
                    Name = "img",
                    Parent = $"Buff_{count}",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemID[kvp.Value.shortname], SkinId = kvp.Value.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-21 -21", OffsetMax = "21 21" }
                }
                });
                container.Add(new CuiElement
                {
                    Name = "timeleft",
                    Parent = $"Buff_{count}",
                    Components = {
                    new CuiTextComponent { Text = "%TIME_LEFT%", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.LowerRight, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiCountdownComponent { StartTime = kvp.Value.endTime - Time.time, EndTime = 0, Step = 1, Interval = 1f, TimerFormat = TimerFormat.MinutesSeconds, DestroyIfDone = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-19 -21", OffsetMax = "19 0" }
                }
                });
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"showmealdescription {kvp.Key}" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-23 -23", OffsetMax = "23 23" }
                }, $"Buff_{count}", "button");
                count++;
            }

            CuiHelper.DestroyUi(player, "BuffUI");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("showmealdescription")]
        void SendMealDescription(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var meal = string.Join(" ", arg.Args);

            if (!config.mealSettings.meals.TryGetValue(meal, out var mealData)) return;


            StringBuilder sb = new StringBuilder();
            foreach (var buff in mealData.buffs)
            {
                if (buff.Key != Buff.Permission) sb.Append(lang.GetMessage($"{buff.Key}_ToText", this, player.UserIDString));
            }
            if (mealData.commandsOnConsume != null)
                foreach (var perm in mealData.commandsOnConsume)
                    sb.Append(lang.GetMessage(perm.Value, this, player.UserIDString));


            player.ShowToast(GameTip.Styles.Blue_Normal, string.Format(lang.GetMessage("BuffToolTip", this, player.UserIDString), string.Join(", ", sb.ToString())));
        }

        #endregion

        #region Cooking button

        private void CookingButton(BasePlayer player, bool oven = true)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_use)) return;

            string AnchorMin = oven ? config.menuSettings.anchorSettings.ovenButton.anchorMin : config.menuSettings.anchorSettings.cookingWorkbenchButton.anchorMin;
            string AnchorMax = oven ? config.menuSettings.anchorSettings.ovenButton.anchorMax : config.menuSettings.anchorSettings.cookingWorkbenchButton.anchorMax;
            string OffsetMin = oven ? config.menuSettings.anchorSettings.ovenButton.offsetMin : config.menuSettings.anchorSettings.cookingWorkbenchButton.offsetMin;
            string OffsetMax = oven ? config.menuSettings.anchorSettings.ovenButton.offsetMax : config.menuSettings.anchorSettings.cookingWorkbenchButton.offsetMax;

            var container = new CuiElementContainer();
            container.Add(new CuiButton
            {
                Button = { Color = "0.4313726 0.5372549 0.2705882 1", Command = "opencookingmenu" },
                Text = { Text = lang.GetMessage("RecipeMenuTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.9686275 0.9215687 0.8705883 1" },
                RectTransform = { AnchorMin = AnchorMin, AnchorMax = AnchorMax, OffsetMin = OffsetMin, OffsetMax = OffsetMax }
            }, "Overlay", "CookingButton");

            CuiHelper.DestroyUi(player, "CookingButton");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("opencookingmenu")]
        void OpenCookingMenuFromButton(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.press_cooking_button_sound)) SendEffect(player, config.generalSettings.soundSettings.press_cooking_button_sound);

            if (!permission.UserHasPermission(player.UserIDString, perm_use)) return;

            SendCookingMenu(player);
        }

        #endregion

        #region UI Mover

        public enum UI
        {
            CraftQueue,
            BuffIcon,
            OvenButton,
            UIButtons
        }

        Dictionary<BasePlayer, UIMoverInfo> UIMovers = new Dictionary<BasePlayer, UIMoverInfo>();
        public class UIMoverInfo
        {
            public UI ui;
            public string anchorMin;
            public string anchorMax;
            public string offsetMin;
            public string offsetMax;
        }

        void SendUiMover(BasePlayer player, UI ui)
        {
            UIMoverInfo uiData;
            if (!UIMovers.TryGetValue(player, out uiData)) UIMovers.Add(player, uiData = new UIMoverInfo());

            switch (ui) 
            {
                case UI.CraftQueue:
                    uiData.anchorMin = config.menuSettings.anchorSettings.craftQueue.anchorMin;
                    uiData.anchorMax = config.menuSettings.anchorSettings.craftQueue.anchorMax;
                    uiData.offsetMin = config.menuSettings.anchorSettings.craftQueue.offsetMin;
                    uiData.offsetMax = config.menuSettings.anchorSettings.craftQueue.offsetMax;
                    break;

                case UI.BuffIcon:
                    uiData.anchorMin = config.menuSettings.anchorSettings.buffIcons.anchorMin;
                    uiData.anchorMax = config.menuSettings.anchorSettings.buffIcons.anchorMax;
                    uiData.offsetMin = config.menuSettings.anchorSettings.buffIcons.offsetMin;
                    uiData.offsetMax = config.menuSettings.anchorSettings.buffIcons.offsetMax;
                    break;

                case UI.OvenButton:
                    uiData.anchorMin = config.menuSettings.anchorSettings.ovenButton.anchorMin;
                    uiData.anchorMax = config.menuSettings.anchorSettings.ovenButton.anchorMax;
                    uiData.offsetMin = config.menuSettings.anchorSettings.ovenButton.offsetMin;
                    uiData.offsetMax = config.menuSettings.anchorSettings.ovenButton.offsetMax;
                    break;

                case UI.UIButtons:
                    uiData.anchorMin = config.menuSettings.anchorSettings.uiButtons.anchorMin;
                    uiData.anchorMax = config.menuSettings.anchorSettings.uiButtons.anchorMax;
                    uiData.offsetMin = config.menuSettings.anchorSettings.uiButtons.offsetMin;
                    uiData.offsetMax = config.menuSettings.anchorSettings.uiButtons.offsetMax;
                    break;
            }

            uiData.ui = ui;

            if (string.IsNullOrEmpty(uiData.anchorMin)) return;

            SendUIMoverBackPanel(player);
            UIMover(player, uiData.offsetMin, uiData.offsetMax);
            SendUIMoverDemoPanel(player, uiData.anchorMin, uiData.anchorMax, uiData.offsetMin, uiData.offsetMax);
        }

        private void UIMover(BasePlayer player, string offsetMin, string offsetMax)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.2169811 0.2169811 0.2169811 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-25.004 48.1", OffsetMax = "24.996 148.1" }
            }, "Overlay", "UIMover");

            container.Add(new CuiPanel
            {
                
                Image = { Color = "0.2156863 0.2156863 0.2156863 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50 -25", OffsetMax = "50 25" }
            }, "UIMover", "panel");

            container.Add(new CuiButton
            {
                Button = { Color = "0.4970047 0.6037736 0.4414383 1", Command = "savecurrentposition" },
                Text = { Text = lang.GetMessage("UISave", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-20 0", OffsetMax = "20 20" }
            }, "UIMover", "save");

            container.Add(new CuiButton
            {
                Button = { Color = "0.745283 0.5941171 0.5941171 1", Command = "canceliconmoving" },
                Text = { Text = lang.GetMessage("UICancel", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-20 -20", OffsetMax = "20 0" }
            }, "UIMover", "cancel");

            container.Add(new CuiButton
            {
                Button = { Color = "0.682353 0.6784314 0.6784314 1", Command = $"cookingmoveui {"left"} {offsetMin} {offsetMax}" },
                Text = { Text = lang.GetMessage("UILeft", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-45 -20", OffsetMax = "-25 20" }
            }, "UIMover", "left");

            container.Add(new CuiButton
            {
                Button = { Color = "0.682353 0.6784314 0.6784314 1", Command = $"cookingmoveui {"right"} {offsetMin} {offsetMax}" },
                Text = { Text = lang.GetMessage("UIRight", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "25 -20", OffsetMax = "45 20" }
            }, "UIMover", "right");

            container.Add(new CuiButton
            {
                Button = { Color = "0.682353 0.6784314 0.6784314 1", Command = $"cookingmoveui {"up"} {offsetMin} {offsetMax}" },
                Text = { Text = lang.GetMessage("UIUp", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-20 25", OffsetMax = "20 45" }
            }, "UIMover", "up");

            container.Add(new CuiButton
            {
                Button = { Color = "0.6792453 0.6760413 0.6760413 1", Command = $"cookingmoveui {"down"} {offsetMin} {offsetMax}" },
                Text = { Text = lang.GetMessage("UIDown", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-20 -45", OffsetMax = "20 -25" }
            }, "UIMover", "down");

            CuiHelper.DestroyUi(player, "UIMover");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("cookingmoveui")]
        void HandleUIMove(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            UIMoverInfo uiData;
            if (!UIMovers.TryGetValue(player, out uiData)) UIMovers.Add(player, uiData = new UIMoverInfo());
            // Break down the args into strings
            string direction = arg.GetString(0);
            string offsetMin;
            string offsetMax;
            switch (direction)
            {
                case "left":
                    offsetMin = $"{Convert.ToSingle(arg.GetString(1)) - 3} {arg.GetString(2)}";
                    offsetMax = $"{Convert.ToSingle(arg.GetString(3)) - 3} {arg.GetString(4)}";
                    break;

                case "right":
                    offsetMin = $"{Convert.ToSingle(arg.GetString(1)) + 3} {arg.GetString(2)}";
                    offsetMax = $"{Convert.ToSingle(arg.GetString(3)) + 3} {arg.GetString(4)}";
                    break;

                case "up":
                    offsetMin = $"{arg.GetString(1)} {Convert.ToSingle(arg.GetString(2)) + 3}";
                    offsetMax = $"{arg.GetString(3)} {Convert.ToSingle(arg.GetString(4)) + 3}";
                    break;

                case "down":
                    offsetMin = $"{arg.GetString(1)} {Convert.ToSingle(arg.GetString(2)) - 3}";
                    offsetMax = $"{arg.GetString(3)} {Convert.ToSingle(arg.GetString(4)) - 3}";
                    break;

                default:
                    offsetMin = $"{arg.GetString(1)} {arg.GetString(2)}";
                    offsetMax = $"{arg.GetString(3)} {arg.GetString(4)}";
                    break;
            }
            uiData.offsetMin = offsetMin;
            uiData.offsetMax = offsetMax;
            SendUIMoverDemoPanel(player, uiData.anchorMin, uiData.anchorMax, offsetMin, offsetMax);
            UIMover(player, offsetMin, offsetMax);
        }

        [ConsoleCommand("canceliconmoving")]
        void CancelIconMoving(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_back_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_back_sound);


            CuiHelper.DestroyUi(player, "UIMoverDemoPanel");
            CuiHelper.DestroyUi(player, "UIMover");
            CuiHelper.DestroyUi(player, "UIMoverBackPanel");

            if (UIMovers[player].ui == UI.OvenButton && OvenUsers.Contains(player))
                CookingButton(player);

            UIMovers.Remove(player);
        }

        [ConsoleCommand("savecurrentposition")]
        void SaveIconPosition(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!string.IsNullOrEmpty(config.generalSettings.soundSettings.navigation_into_menu_sound)) SendEffect(player, config.generalSettings.soundSettings.navigation_into_menu_sound);

            CuiHelper.DestroyUi(player, "UIMoverDemoPanel");
            CuiHelper.DestroyUi(player, "UIMover");
            CuiHelper.DestroyUi(player, "UIMoverBackPanel");

            UIMoverInfo uiData;
            if (!UIMovers.TryGetValue(player, out uiData)) return;

            bool updateSuccessful = false;
            switch (uiData.ui)
            {
                case UI.CraftQueue:
                    config.menuSettings.anchorSettings.craftQueue.anchorMin = uiData.anchorMin;
                    config.menuSettings.anchorSettings.craftQueue.anchorMax = uiData.anchorMax;
                    config.menuSettings.anchorSettings.craftQueue.offsetMin = uiData.offsetMin;
                    config.menuSettings.anchorSettings.craftQueue.offsetMax = uiData.offsetMax;
                    updateSuccessful = true;
                    break;

                case UI.OvenButton:
                    config.menuSettings.anchorSettings.ovenButton.anchorMin = uiData.anchorMin;
                    config.menuSettings.anchorSettings.ovenButton.anchorMax = uiData.anchorMax;
                    config.menuSettings.anchorSettings.ovenButton.offsetMin = uiData.offsetMin;
                    config.menuSettings.anchorSettings.ovenButton.offsetMax = uiData.offsetMax;
                    updateSuccessful = true;
                    break;

                case UI.BuffIcon:
                    config.menuSettings.anchorSettings.buffIcons.anchorMin = uiData.anchorMin;
                    config.menuSettings.anchorSettings.buffIcons.anchorMax = uiData.anchorMax;
                    config.menuSettings.anchorSettings.buffIcons.offsetMin = uiData.offsetMin;
                    config.menuSettings.anchorSettings.buffIcons.offsetMax = uiData.offsetMax;
                    updateSuccessful = true;
                    break;

                case UI.UIButtons:
                    config.menuSettings.anchorSettings.uiButtons.anchorMin = uiData.anchorMin;
                    config.menuSettings.anchorSettings.uiButtons.anchorMax = uiData.anchorMax;
                    config.menuSettings.anchorSettings.uiButtons.offsetMin = uiData.offsetMin;
                    config.menuSettings.anchorSettings.uiButtons.offsetMax = uiData.offsetMax;
                    updateSuccessful = true;
                    break;
            }

            if (updateSuccessful)
            {
                PrintToChat(player, String.Format(lang.GetMessage("UIPosUpdated", this, player.UserIDString), uiData.ui));
                SaveConfig();
            }
            else PrintToChat(player, lang.GetMessage("UIPosFailed", this, player.UserIDString));

            if (uiData.ui == UI.OvenButton && OvenUsers.Contains(player))
                CookingButton(player);
            else if (uiData.ui == UI.UIButtons)
            {
                foreach (var p in BasePlayer.activePlayerList)
                {
                    CreatButtons(p);
                }
            }

            UIMovers.Remove(player);
        }

        private void SendUIMoverDemoPanel(BasePlayer player, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.9504018 0 1" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
            }, "Overlay", "UIMoverDemoPanel");

            container.Add(new CuiElement
            {
                Name = "text",
                Parent = "UIMoverDemoPanel",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIUI", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-26.562 -25.705", OffsetMax = "26.562 25.704" }
                }
            });

            CuiHelper.DestroyUi(player, "UIMoverDemoPanel");
            CuiHelper.AddUi(player, container);
        }

        private void SendUIMoverBackPanel(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "-551.353 -302.328", OffsetMax = "551.347 302.342" }
            }, "Overlay", "UIMoverBackPanel");

            CuiHelper.DestroyUi(player, "UIMoverBackPanel");
            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Market

        Dictionary<string, IngredientsInfo> MarketIngredients = new Dictionary<string, IngredientsInfo>();

        void SendMarketConsoleCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, perm_market_cmd)) return;
            SendMarket(player);
        }
        void SendMarketCMD(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_market_cmd)) return;
            SendMarket(player);
        }

        void SendMarket(BasePlayer player)
        {
            SendBackPanel(player);
            FarmersMarket(player, 0);
        }

        private void FarmersMarket(BasePlayer player, int index)
        {
            if (index < 0 || index > MarketIngredients.Count - 1) index = 0;

            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = "FarmersMarket",
                Parent = "Overlay",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIFarmersMarket", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-119.193 223.3", OffsetMax = "120.807 273.3" }
                }
            });


            var count = 0;
            var row = 0;
            bool foundFirst = false;
            var totalCount = 0;
            foreach (var ingredient in MarketIngredients)
            {
                if (!foundFirst)
                {
                    if (totalCount != index)
                    {
                        totalCount++;
                        continue;
                    }
                    foundFirst = true;
                }

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.1509434 0.1509434 0.1509434 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-186.8 + (count * 190)} {-157.4 - (row * 150)}", OffsetMax = $"{-126.8 + (count * 190)} {-97.4 - (row * 150)}" }
                }, "FarmersMarket", $"marketIngredient_{row}_{count}");

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.3396226 0.3396226 0.3396226 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-28 -28", OffsetMax = "28 28" }
                }, $"marketIngredient_{row}_{count}", "innerpanel");

                container.Add(new CuiElement
                {
                    Name = "img",
                    Parent = $"marketIngredient_{row}_{count}",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemID[ingredient.Value.shortname], SkinId = ingredient.Value.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-25 -25", OffsetMax = "25 25" }
                }
                });

                container.Add(new CuiElement
                {
                    Name = "name",
                    Parent = $"marketIngredient_{row}_{count}",
                    Components = {
                    new CuiTextComponent { Text = ingredient.Key.ToUpper(), Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-60 36.5", OffsetMax = "140 68.5" }
                }
                });

                string sellText = String.Format(lang.GetMessage("UIMarketSellText", this, player.UserIDString), ingredient.Value.marketInfo.marketSellForPrice > 0 ? ingredient.Value.marketInfo.marketSellForPrice.ToString() : lang.GetMessage("UIMarketNA", this, player.UserIDString));
                container.Add(new CuiElement
                {
                    Name = "selltext",
                    Parent = $"marketIngredient_{row}_{count}",
                    Components = {
                    new CuiTextComponent { Text = sellText, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "33.5 10", OffsetMax = "233.5 30" }
                }
                });

                string buytext = String.Format(lang.GetMessage("UIMarketBuyText", this, player.UserIDString), ingredient.Value.marketInfo.marketBuyForPrice > 0 ? ingredient.Value.marketInfo.marketBuyForPrice.ToString() : lang.GetMessage("UIMarketNA", this, player.UserIDString));
                container.Add(new CuiElement
                {
                    Name = "buytext",
                    Parent = $"marketIngredient_{row}_{count}",
                    Components = {
                    new CuiTextComponent { Text = buytext, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "33.5 -10", OffsetMax = "233.5 10" }
                }
                });

                string availabilityText = ingredient.Value.marketInfo.availableStock > 0 ? string.Format(lang.GetMessage("UIAvailableAbove0", this, player.UserIDString), ingredient.Value.marketInfo.availableStock) : string.Format(lang.GetMessage("UIAvailable0orLess", this, player.UserIDString), ingredient.Value.marketInfo.availableStock);
                container.Add(new CuiElement
                {
                    Name = "Availabletext",
                    Parent = $"marketIngredient_{row}_{count}",
                    Components = {
                    new CuiTextComponent { Text = availabilityText, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "33.5 -30", OffsetMax = "233.5 -10" }
                }
                });

                if (ingredient.Value.marketInfo.marketSellForPrice > 0)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1490196 0.1490196 0.1490196 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-30 -51", OffsetMax = "2 -34" }
                    }, $"marketIngredient_{row}_{count}", "buybuttonpanel1");

                    container.Add(new CuiButton
                    {
                        Button = { Color = "0.3411765 0.3411765 0.3411765 1", Command = $"buymarketorder {index} 1 {ingredient.Key}" },
                        Text = { Text = lang.GetMessage("UIBuy1", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-15 -7.5", OffsetMax = "15 7.5" }
                    }, "buybuttonpanel1", "buy-1");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1490196 0.1490196 0.1490196 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "6 -51", OffsetMax = "38 -34" }
                    }, $"marketIngredient_{row}_{count}", "buybuttonpanel10");

                    container.Add(new CuiButton
                    {
                        Button = { Color = "0.3411765 0.3411765 0.3411765 1", Command = $"buymarketorder {index} {Math.Min(ingredient.Value.marketInfo.availableStock, 10)} {ingredient.Key}" },
                        Text = { Text = lang.GetMessage("UIBuy10", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-15 -7.5", OffsetMax = "15 7.5" }
                    }, "buybuttonpanel10", "buy-10");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1490196 0.1490196 0.1490196 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "42 -51", OffsetMax = "74 -34" }
                    }, $"marketIngredient_{row}_{count}", "buybuttonpanel100");

                    container.Add(new CuiButton
                    {
                        Button = { Color = "0.3411765 0.3411765 0.3411765 1", Command = $"buymarketorder {index} {Math.Min(ingredient.Value.marketInfo.availableStock, 100)} {ingredient.Key}" },
                        Text = { Text = lang.GetMessage("UIBuy100", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-15 -7.5", OffsetMax = "15 7.5" }
                    }, "buybuttonpanel100", "buy-100");
                }

                if (ingredient.Value.marketInfo.marketBuyForPrice > 0)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1490196 0.1490196 0.1490196 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-30 -72", OffsetMax = "2 -55" }
                    }, $"marketIngredient_{row}_{count}", "sellbuttonpanel1");

                    container.Add(new CuiButton
                    {
                        Button = { Color = "0.3411765 0.3411765 0.3411765 1", Command = $"sellmarketorder {index} 1 {ingredient.Key}" },
                        Text = { Text = lang.GetMessage("UISell1", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-15 -7.5", OffsetMax = "15 7.5" }
                    }, "sellbuttonpanel1", "sell-1");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1490196 0.1490196 0.1490196 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "6 -72", OffsetMax = "38 -55" }
                    }, $"marketIngredient_{row}_{count}", "sellbuttonpanel10");

                    container.Add(new CuiButton
                    {
                        Button = { Color = "0.3411765 0.3411765 0.3411765 1", Command = $"sellmarketorder {index} 10 {ingredient.Key}" },
                        Text = { Text = lang.GetMessage("UISell10", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-15 -7.5", OffsetMax = "15 7.5" }
                    }, "sellbuttonpanel10", "sell-10");

                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1490196 0.1490196 0.1490196 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "42 -72", OffsetMax = "74 -55" }
                    }, $"marketIngredient_{row}_{count}", "sellbuttonpanel100");

                    container.Add(new CuiButton
                    {
                        Button = { Color = "0.3411765 0.3411765 0.3411765 1", Command = $"sellmarketorder {index} 100 {ingredient.Key}" },
                        Text = { Text = lang.GetMessage("UISell100", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-15 -7.5", OffsetMax = "15 7.5" }
                    }, "sellbuttonpanel100", "sell-100");
                }                

                count++;                
                if (count >= 2)
                {
                    count = 0;
                    row++;
                }
                if (count == 0 && row == 2) break;
            }

            if (index > 0)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendmarketpage {index - 4}" },
                    Text = { Text = lang.GetMessage("UIBack", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-155 -478.4", OffsetMax = "-85 -448.4" }
                }, "FarmersMarket", "buttonback");
            }
            //if ((config.mealSettings.meals.Count - 1) - firstRecipeToShowIndex > 5)
            if ((MarketIngredients.Count - 1) - index > 3)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"sendmarketpage {index + 4}" },
                    Text = { Text = lang.GetMessage("UINext", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "85 -478.4", OffsetMax = "155 -448.4" }
                }, "FarmersMarket", "buttonnext");
            }            

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = "closemarketmenu" },
                Text = { Text = lang.GetMessage("CloseTitle", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-35 -478.4", OffsetMax = "35 -448.4" }
            }, "FarmersMarket", "closebutton");

            CuiHelper.DestroyUi(player, "FarmersMarket");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("buymarketorder")]
        void BuyMarketOrder(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var index = Convert.ToInt32(arg.GetString(0));
            var amount = Convert.ToInt32(arg.GetString(1));
            var ingredient = string.Join(" ", arg.Args.Skip(2));

            IngredientsInfo ingredientData;

            if (!config.ingredientSettings.ingredients.TryGetValue(ingredient, out ingredientData)) return;

            if (ingredientData.marketInfo.availableStock <= 0)
            {
                PrintToChat(player, lang.GetMessage("MarketNoStock", this, player.UserIDString));
                FarmersMarket(player, index);
                return;
            }

            var totalCost = amount * ingredientData.marketInfo.marketSellForPrice;
            if (!HasEnoughCurrency(player, totalCost))
            {
                PrintToChat(player, String.Format(lang.GetMessage("MarketCannotAfford", this, player.UserIDString), amount, lang.GetMessage(ingredient, this, player.UserIDString)));
                return;
            }

            if (!TakeCurrency(player, totalCost))
            {
                PrintToChat(player, lang.GetMessage("MarketPurchaseBug", this, player.UserIDString));
                return;
            }

            CuiHelper.DestroyUi(player, "FarmersMarket");

            ingredientData.marketInfo.availableStock -= amount;
            if (ingredientData.marketInfo.availableStock < 0) ingredientData.marketInfo.availableStock = 0;
            ConfigRequiresSave = true;

            GiveMeal(player, ingredientData.shortname, ingredientData.skin, ingredient, ingredientData.spoilTime, amount);
            PrintToChat(player, String.Format(lang.GetMessage("MarketPurchaseSuccess", this, player.UserIDString), amount, lang.GetMessage(ingredient, this, player.UserIDString)));

            FarmersMarket(player, index);
        }

        bool ConfigRequiresSave = false;

        [ConsoleCommand("sellmarketorder")]
        void SellMarketOrder(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            var index = Convert.ToInt32(arg.GetString(0));
            var amount = Convert.ToInt32(arg.GetString(1));
            var ingredient = string.Join(" ", arg.Args.Skip(2));
            IngredientsInfo ingredientData;
            if (!config.ingredientSettings.ingredients.TryGetValue(ingredient, out ingredientData)) return;

            if (ingredientData.marketInfo.availableStock >= ingredientData.marketInfo.maxStock)
            {
                PrintToChat(player, String.Format(lang.GetMessage("MarketStockFull", this, player.UserIDString), lang.GetMessage(ingredient, this, player.UserIDString)));
                return;
            }

            var found = 0;
            List<Item> foundToSell = Pool.Get<List<Item>>();
            var allItems = AllItems(player);
            foreach (var item in allItems)
            {
                if (IngredientIsMatch(item, ingredient, ingredientData))
                {
                    found += item.amount;
                    foundToSell.Add(item);
                }
            }
            Pool.FreeUnmanaged(ref allItems);
            if (found == 0)
            {
                PrintToChat(player, String.Format(lang.GetMessage("MarketNoItemToSell", this, player.UserIDString), lang.GetMessage(ingredient, this, player.UserIDString)));
                Pool.FreeUnmanaged(ref foundToSell);
                return;
            }

            if (amount + ingredientData.marketInfo.availableStock > ingredientData.marketInfo.maxStock) amount = ingredientData.marketInfo.maxStock - ingredientData.marketInfo.availableStock;

            bool sold = false;
            if (amount == 1)
            {
                var soldItem = foundToSell[0];
                if (soldItem.amount == 1) soldItem.Remove();
                else
                {
                    var split = soldItem.SplitItem(1);
                    split.RemoveFromContainer();
                    split.Remove();
                }
                ItemManager.DoRemoves();
                //foundToSell[0].UseItem(1);
                sold = true;
            }
            else
            {
                if (found < amount) amount = found;
                found = 0;
                foreach (var item in foundToSell)
                {
                    if (item.amount >= amount - found)
                    {
                        if (amount - found == item.amount) item.Remove();
                        else
                        {
                            var split = item.SplitItem(amount - found);
                            split.RemoveFromContainer();
                            split.Remove();
                        }
                        //item.UseItem(amount - found);
                        found = amount;                        
                        break;
                    }
                    found += item.amount;
                    item.Remove();
                    
                    //if (found < amount) break;
                }
                ItemManager.DoRemoves();
                if (found >= amount) sold = true;
            }
            
            Pool.FreeUnmanaged(ref foundToSell);

            if (!sold)
            {
                PrintToChat(player, lang.GetMessage("MarketSellBug", this, player.UserIDString));
                return;
            }
            switch (config.generalSettings.marketSettings.currency)
            {
                case "economics":
                    var amountDouble = Convert.ToDouble(amount * ingredientData.marketInfo.marketBuyForPrice);
                    if (Economics != null && Economics.IsLoaded)
                        if (!Convert.ToBoolean(Economics.Call("Deposit", player.userID.Get(), amountDouble))) PrintToChat(player, lang.GetMessage("MarketCashBug", this, player.UserIDString));

                    PrintToChat(player, String.Format(lang.GetMessage("MarketCashSuccess", this, player.UserIDString), amountDouble));
                    break;

                case "serverrewards":
                    var amountSRP = amount * ingredientData.marketInfo.marketBuyForPrice;
                    if (ServerRewards != null && ServerRewards.IsLoaded)
                        if (!Convert.ToBoolean(ServerRewards.Call("AddPoints", player.userID.Get(), amountSRP))) PrintToChat(player, lang.GetMessage("MarketPointsBug", this, player.UserIDString));

                    PrintToChat(player, String.Format(lang.GetMessage("MarketPointSuccess", this, player.UserIDString), amountSRP));
                    break;

                default:
                    var amountScrap = amount * ingredientData.marketInfo.marketBuyForPrice;
                    var scrap = ItemManager.CreateByName("scrap", amountScrap);
                    player.GiveItem(scrap);
                    PrintToChat(player, String.Format(lang.GetMessage("MarketScrapSuccess", this, player.UserIDString), amountScrap));
                    break;
            }

            ingredientData.marketInfo.availableStock += amount;
            ConfigRequiresSave = true;

            FarmersMarket(player, index);
        }

        [ConsoleCommand("sendmarketpage")]
        void SendMarketPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var index = Convert.ToInt32(arg.GetString(0));
            FarmersMarket(player, index);
        }

        [ConsoleCommand("closemarketmenu")]
        void CloseMarketMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            CuiHelper.DestroyUi(player, "FarmersMarket");
            CuiHelper.DestroyUi(player, "BackPanel");
        }

        bool HasEnoughCurrency(BasePlayer player, int amount)
        {
            switch (config.generalSettings.marketSettings.currency)
            {
                case "economics":
                    var balance = Economics != null && Economics.IsLoaded ? (double)Economics.Call("Balance", player.userID.Get()) : 0;
                    return balance >= amount;

                case "serverrewards":
                    var srpbalance = ServerRewards != null && ServerRewards.IsLoaded ? (int)ServerRewards.Call("CheckPoints", player.userID.Get()) : 0;
                    return srpbalance >= amount;

                default:
                    var count = 0;
                    var allItems = AllItems(player);
                    foreach (var item in allItems)
                    {
                        if (item.info.shortname == "scrap" && item.skin == 0) count += item.amount;
                        if (count >= amount) break;
                    }
                    Pool.FreeUnmanaged(ref allItems);
                    return count >= amount;
            }
        }

        bool TakeCurrency(BasePlayer player, int amount)
        {
            switch (config.generalSettings.marketSettings.currency)
            {
                case "economics":
                    var amountDouble = Convert.ToDouble(amount);
                    var econResult = Economics != null && Economics.IsLoaded ? Convert.ToBoolean(Economics.Call("Withdraw", player.userID.Get(), amountDouble)) : false;
                    return econResult;

                case "serverrewards":
                    var srpResult = ServerRewards != null && ServerRewards.IsLoaded ? Convert.ToBoolean(ServerRewards.Call("TakePoints", player.userID.Get(), amount)) : false;
                    return srpResult;

                default:
                    var found = 0;
                    var allItems = AllItems(player);
                    foreach (var item in allItems)
                    {
                        if (item.info.shortname == "scrap" && item.skin == 0)
                        {
                            if (item.amount >= amount - found)
                            {
                                item.UseItem(amount - found);
                                found = amount;                                
                            }
                            else
                            {
                                found += item.amount;
                                item.Remove();
                            }
                        }
                        if (found >= amount) break;
                    }
                    Pool.FreeUnmanaged(ref allItems);
                    return true;
            }
        }

        #endregion

        #region Hud buttons

        void CreatButtons(BasePlayer player)
        {
            if (player == null || player.IsDead() || !player.IsConnected) return;
            if (!config.menuSettings.hudButtonSettings.enable)
            {
                CuiHelper.DestroyUi(player, "HudButtons");
                return;
            }
            List<HudButtonInfo> buttonData = Pool.Get<List<HudButtonInfo>>();
            if (permission.UserHasPermission(player.UserIDString, perm_use)) buttonData.Add(new HudButtonInfo("Meals", "inspectmeals"));
            if (permission.UserHasPermission(player.UserIDString, perm_bag_cmd)) buttonData.Add(new HudButtonInfo("iBag", config.generalSettings.ingredientBagSettings.iBagCMD));
            if (permission.UserHasPermission(player.UserIDString, perm_recipemenu_chat)) buttonData.Add(new HudButtonInfo("Cook", config.generalSettings.recipeMenuCMDs.First()));
            if (permission.UserHasPermission(player.UserIDString, perm_market_cmd)) buttonData.Add(new HudButtonInfo("Market", config.generalSettings.marketSettings.marketCMD));

            if (buttonData.Count > 0) InventoryMeals(player, buttonData);
            else CuiHelper.DestroyUi(player, "HudButtons");
            Pool.FreeUnmanaged(ref buttonData);
        }

        public class HudButtonInfo
        {
            public string text;
            public string command;
            public HudButtonInfo(string text, string command)
            {
                this.text = text;
                this.command = command;
            }
        }

        private void InventoryMeals(BasePlayer player, List<HudButtonInfo> hudInfo)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = config.menuSettings.anchorSettings.uiButtons.anchorMin, AnchorMax = config.menuSettings.anchorSettings.uiButtons.anchorMax, OffsetMin = config.menuSettings.anchorSettings.uiButtons.offsetMin, OffsetMax = config.menuSettings.anchorSettings.uiButtons.offsetMax }
            }, config.menuSettings.hudButtonSettings.hudLayer, "HudButtons");

            for (int i = 0; i < hudInfo.Count; i++)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = config.menuSettings.hudButtonSettings.buttonColour, Command = hudInfo[i].command },
                    Text = { Text = lang.GetMessage(hudInfo[i].text + "HudButton", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.8156863 0.9294118 0.6470588 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-18 + (i * 40)} -6", OffsetMax = $"{18 + (i * 40)} 6" }
                }, "HudButtons", $"button_{i}");
            }            

            CuiHelper.DestroyUi(player, "HudButtons");
            CuiHelper.AddUi(player, container);
        }

        #region MealInfoPanel

        [ChatCommand("inspectmeals")]
        void SendMealInfoPanel(BasePlayer player)
        {
            SendBackPanel(player);
            MealInfoPanel(player);
        }

        [ConsoleCommand("inspectmeals")]
        void SendMealInfoPanel(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            SendBackPanel(player);
            MealInfoPanel(player);
        }

        private void MealInfoPanel(BasePlayer player, int page = 0)
        {
            List<string> foundMeals = Pool.Get<List<string>>();
            var allItems = AllItems(player);
            foreach (var item in allItems)
            {
                if (IsCookingMeal(item) && !foundMeals.Contains(item.name))
                    foundMeals.Add(item.name);
            }
            Pool.FreeUnmanaged(ref allItems);
            if (foundMeals.Count == 0)
            {
                PrintToChat(player, lang.GetMessage("MealInfoNoMeals", this, player.UserIDString));
                CuiHelper.DestroyUi(player, "BackPanel");
                Pool.FreeUnmanaged(ref foundMeals);
                return;
            }

            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-5.002 -4.992", OffsetMax = "4.998 5.008" }
            }, "Overlay", "MealInfoPanel");

            container.Add(new CuiElement
            {
                Name = "Title",
                Parent = "MealInfoPanel",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIMealInfoTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-190 180.366", OffsetMax = "190 226.634" }
                }
            });

            var count = 0;
            var mealsDisplayed = 0;

            foreach (var meal in foundMeals)
            {
                if (count < page * 4)
                {
                    count++;
                    continue;
                }

                MealInfo mealData;
                if (!config.mealSettings.meals.TryGetValue(meal, out mealData)) continue;

                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = "0.2941177 0.2901961 0.2784314 0.8" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{config.menuSettings.hudButtonSettings.offset_x_min} {config.menuSettings.hudButtonSettings.offset_y_min - (mealsDisplayed * 90)}", OffsetMax = $"{config.menuSettings.hudButtonSettings.offset_x_max} {config.menuSettings.hudButtonSettings.offset_y_max - (mealsDisplayed * 90)}" }
                }, "MealInfoPanel", $"meal_{count}");

                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = "0.4901961 0.4901961 0.4901961 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-23 -23", OffsetMax = "23 23" }
                }, $"meal_{count}", "FrontPanel");
                
                container.Add(new CuiElement
                {
                    Name = "img",
                    Parent = $"meal_{count}",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = 1751045826, SkinId = mealData.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-22 -22", OffsetMax = "22 22" }
                }
                });

                string descriptionField = String.Empty;
                bool first = true;
                foreach (var buff in mealData.buffs)
                {
                    if (buff.Key == Buff.Permission) continue;
                    descriptionField += (first ? "" : "\n") + GetBuffDescriptions(player, buff.Key, buff.Value);
                    first = false;
                }

                if (mealData.commandsOnConsume != null)
                {
                    foreach (var perm in mealData.commandsOnConsume)
                        descriptionField += $"\n<color=#4600ff>Command:</color> {perm.Value}";
                }

                var durationString = $"<color=#03a7e5>Duration:</color> {mealData.duration} seconds.";
                if (mealData.useCooldown > 0)
                {
                    durationString += $"\n<color=#03a7e5>Cooldown:</color> {mealData.useCooldown} seconds.";
                }

                container.Add(new CuiElement
                {
                    Name = "description",
                    Parent = $"meal_{count}",
                    Components = {
                    new CuiTextComponent { Text = descriptionField + "\n" + durationString, Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                    new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "34.846 -63", OffsetMax = "372.141 25" }
                }
                });

                mealsDisplayed++;
                count++;

                if (mealsDisplayed >= 4) break;
            }

            if (count > 4)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"mealinfomenuchangepage {Math.Max(page - 1, 0)}" },
                    Text = { Text = lang.GetMessage("UILeftArrow", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-90 -251.4", OffsetMax = "-30 -221.4" }
                }, "MealInfoPanel", "back_button");
            }            

            if (count < foundMeals.Count)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = $"mealinfomenuchangepage {page + 1}" },
                    Text = { Text = lang.GetMessage("UIRightArrow", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "30 -251.4", OffsetMax = "90 -221.4" }
                }, "MealInfoPanel", "next_button");
            }

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = "closemealinfopage" },
                Text = { Text = lang.GetMessage("CloseTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-30 -251.4", OffsetMax = "30 -221.4" }
            }, "MealInfoPanel", "close_button");

            Pool.FreeUnmanaged(ref foundMeals);

            CuiHelper.DestroyUi(player, "MealInfoPanel");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("mealinfomenuchangepage")]
        void SendMealInfoPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var page = Convert.ToInt32(arg.GetString(0));
            MealInfoPanel(player, page);
        }
        
        [ConsoleCommand("closemealinfopage")]
        void CloseMealInfoPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            CuiHelper.DestroyUi(player, "MealInfoPanel");
            CuiHelper.DestroyUi(player, "BackPanel");
        }

        #endregion

        #endregion

        #endregion

        #region Subscriptions

        public Dictionary<string, List<Buff>> Subscriptions = new Dictionary<string, List<Buff>>()
        {
            [nameof(OnPlayerWound)] = new List<Buff>() { Buff.Wounded_Resist },
            [nameof(CanDropActiveItem)] = new List<Buff>() { Buff.Wounded_Resist },
            [nameof(OnLoseCondition)] = new List<Buff>() { Buff.Condition_Loss_Reduction },
            [nameof(OnEntityTakeDamage)] = new List<Buff>() { Buff.Barrel_Smasher, Buff.Wealth, Buff.Component_Luck, Buff.Electronics_Luck, Buff.Passive_Regen, Buff.Fall_Damage_resist, Buff.Fire_Resist, Buff.Cold_Resist, Buff.Bleed_Resist, Buff.Radiation_Resist, Buff.Explosion_Resist, Buff.Animal_Resist, Buff.Melee_Resist },
            [nameof(OnItemCraftFinished)] = new List<Buff>() { Buff.Crafting_Refund, Buff.Duplicator },
            [nameof(OnPayForUpgrade)] = new List<Buff>() { Buff.Upgrade_Refund },
            [nameof(OnResearchCostDetermine)] = new List<Buff>() { Buff.Research_Refund },
            [nameof(OnPlayerVoice)] = new List<Buff>() { Buff.Madness },
            [nameof(OnPlayerHealthChange)] = new List<Buff>() { Buff.Heal_Share },
            [nameof(OnItemRepair)] = new List<Buff>() { Buff.Max_Repair },
            [nameof(OnPlayerRevive)] = new List<Buff>() { Buff.Reviver },
            [nameof(OnEntityMounted)] = new List<Buff>() { Buff.Horse_Stats },
            [nameof(OnEntityDismounted)] = new List<Buff>() { Buff.Horse_Stats },
            [nameof(OnEntityEnter)] = new List<Buff>() { Buff.Spectre },
            [nameof(CanBradleyApcTarget)] = new List<Buff>() { Buff.Anti_Bradley_Radar },
            [nameof(CanCatchFish)] = new List<Buff>() { Buff.Fishing_Yield },
            [nameof(OnMeleeAttack)] = new List<Buff>() { Buff.Mining_Hotspot },
            [nameof(OnTreeMarkerHit)] = new List<Buff>() { Buff.Woodcutting_Hotspot },
            [nameof(OnXpGain)] = new List<Buff>() { Buff.SkillTreeXp },
        };

        Dictionary<string, bool> IsSubscribed = new Dictionary<string, bool>();
        void SetupSubscriptions()
        {
            if (!config.generalSettings.cookingHandleSplits) Unsubscribe(nameof(OnItemSplit));
            foreach (var sub in Subscriptions)
            {
                if (!IsSubscribed.ContainsKey(sub.Key))
                    IsSubscribed.Add(sub.Key, false);

                DoUnSubscribe(sub.Key);
            }
        }

        void DoUnSubscribe(string hook)
        {
            Unsubscribe(hook);
            IsSubscribed[hook] = false;
        }

        void DoSubscribe(string hook)
        {
            Subscribe(hook);
            IsSubscribed[hook] = true;
        }

        Dictionary<Buff, List<ulong>> PlayersSubscribed = new Dictionary<Buff, List<ulong>>();

        void HandlePlayerSubscription(BasePlayer player, List<Buff> perks)
        {
            List<Buff> perks_to_remove = Pool.Get<List<Buff>>();
            foreach (var perk in perks)
            {
                if (!PlayersSubscribed.ContainsKey(perk))
                    PlayersSubscribed.Add(perk, new List<ulong>());
            }
            foreach (var perk in PlayersSubscribed)
            {
                // Adds and removes the player from perk subscriptions.
                if (!perks.Contains(perk.Key)) perk.Value.Remove(player.userID);
                else if (!perk.Value.Contains(player.userID)) perk.Value.Add(player.userID);

                if (perk.Value.Count == 0) perks_to_remove.Add(perk.Key);
            }
            // Removes perks from PlayersSubscribed that do not have any players listed.
            foreach (var perk in perks_to_remove)
                PlayersSubscribed.Remove(perk);
            Pool.FreeUnmanaged(ref perks_to_remove);
            CheckSubscriptions();
        }

        void CheckSubscriptions()
        {
            foreach (var sub in Subscriptions)
            {
                bool shouldUnsub = true;
                foreach (var perk in sub.Value)
                {
                    List<ulong> subs;
                    if (PlayersSubscribed.TryGetValue(perk, out subs) && subs.Count > 0)
                    {
                        if (!IsSubscribed[sub.Key])
                            DoSubscribe(sub.Key);
                        shouldUnsub = false;
                        break;
                    }
                }
                if (shouldUnsub)
                    if (IsSubscribed[sub.Key])
                        DoUnSubscribe(sub.Key);
            }
        }

        #endregion        

        #region API

        void OnXpGain(BasePlayer player, BaseEntity source, double value, string source_string, List<double> mods)
        {
            var buffData = GetBuffManager(player, false);
            if (!buffData) return;

            var buffs = buffData.GetBuffModifiers;
            if (!buffs.TryGetValue(Buff.SkillTreeXp, out var mod)) return;
            mods.Add(mod);
        }

        void OnPluginLoaded(Plugin name)
        {
            if (name != CustomItemVending) return;
            AddCurrencyToItemVending();
        }

        void AddCurrencyToItemVending()
        {
            if (CustomItemVending == null || !CustomItemVending.IsLoaded || (!config.apiSettings.customItemVendingSettings.AddIngredientsAsCurrency && !config.apiSettings.customItemVendingSettings.AddMealsAsCurrency)) return;

            List<object[]> result = Pool.Get<List<object[]>>();
            if (config.apiSettings.customItemVendingSettings.AddIngredientsAsCurrency)
            {
                foreach (var ingredient in config.ingredientSettings.ingredients)
                {
                    try
                    {
                        if (!ingredient.Value.enabled) continue;
                        if (ingredient.Value.skin == 0) continue;
                        var obj = new object[]
                        {
                        ItemID[ingredient.Value.shortname],
                        ingredient.Value.skin,
                        ingredient.Key,
                        null,
                        0
                        };
                        result.Add(obj);
                    }
                    catch { }
                }
            }

            if (config.apiSettings.customItemVendingSettings.AddMealsAsCurrency)
            {
                foreach (var meal in config.mealSettings.meals)
                {
                    try
                    {
                        if (!meal.Value.enabled) continue;
                        if (meal.Value.skin == 0) continue;
                        var obj = new object[]
                        {
                        ItemID[meal.Value.shortname],
                        meal.Value.skin,
                        meal.Key,
                        meal.Key,
                        0
                        };
                        result.Add(obj);
                    }
                    catch { }
                }
            }

            Puts($"Adding: {result.Count} meals and ingredients as currencies to CustomItemVending.");
            if (result.Count > 0) CustomItemVending.Call("AddCurrencies", this.Name, result);
            Pool.FreeUnmanaged(ref result);
        }

        void RemoveCurrencies()
        {
            if (CustomItemVending != null && CustomItemVending.IsLoaded) CustomItemVending.Call("RemoveCurrencies", this.Name);
        }

        void CIVGetItemPropertyDescription(Dictionary<string, string> result, string name, string text, ulong skin, int itemid)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!config.mealSettings.meals.TryGetValue(text, out var mealData)) return;

            foreach (var buff in mealData.buffs)
            {
                result.TryAdd(lang.GetMessage($"{buff.Key}_ToText", this), string.Format(lang.GetMessage("BuffPropertyPercentage", this), buff.Value * 100));
            }
        }

        // Returns an object[] of object[] { string: type (ingredient/meal), ulong: skin ID, string: meal displayname, string: meal shortname }
        [HookMethod("GetCookingMealsAndIngredients")]
        public List<object[]> GetCookingMealsAndIngredients()
        {
            var result = new List<object[]>();
            foreach (var ingredient in config.ingredientSettings.ingredients)
            {
                if (!ingredient.Value.enabled || ingredient.Value.skin == 0) continue;
                result.Add(new object[] { "ingredient", ingredient.Value.skin, ingredient.Key, ingredient.Value.shortname });
            }

            foreach (var meal in config.mealSettings.meals)
            {
                if (!meal.Value.enabled || meal.Value.skin == 0) continue;
                result.Add(new object[] { "meal", meal.Value.skin, meal.Key, meal.Value.shortname });
            }

            Puts($"Returning: {result.Count}");
            return result;
        }

        object QuickSortExcluded(BasePlayer player, StorageContainer entity)
        {
            if (entity != null && entity.net != null && IBagContainers.ContainsKey(player.userID)) return true;
            return null;
        }

        [HookMethod("IsHorseBuffed")]
        public bool IsHorseBuffed(RidableHorse horse)
        {
            return ModifiedHorses.ContainsKey(horse);
        }

        [HookMethod("IsCookingMeal")]
        public bool IsCookingMeal(Item item)
        {
            return item.name != null && config.mealSettings.meals.ContainsKey(item.name);
        }

        [HookMethod("IsCustomIngredient")]
        public bool IsCustomIngredient(Item item)
        {
            return item.name != null && config.ingredientSettings.ingredients.ContainsKey(item.name);
        }

        object OnEventJoin(BasePlayer player, string eventName)
        {
            var buffManager = GetBuffManager(player, false);
            if (buffManager == null) return null;
            switch (eventName)
            {
                case "SurvivalArena":
                    if (config.apiSettings.survivalArenaSettings.meal_blacklist.Count == 0) return null;
                    foreach (var meal in buffManager.activeMeals)
                        if (config.apiSettings.survivalArenaSettings.meal_blacklist.Contains(meal.Key)) return string.Join(lang.GetMessage("SurvivalArenaBlacklistMeal", this, player.UserIDString), meal.Key, string.Join(", ", config.apiSettings.survivalArenaSettings.meal_blacklist));
                    return null;

                default: return null;
            }
        }

        [HookMethod("API_GetBagItemCount")]
        public int API_GetBagItemCount(ulong playerId, string shortname, ulong skin)
        {
            var found = 0;
            if (IBagContainers.TryGetValue(playerId, out var container))
            {
                if (container?.inventory?.itemList == null)
                    return found;

                foreach (var item in container.inventory.itemList)
                    if (item.info.shortname == shortname && item.skin == skin)
                        found += item.amount;

                return found;
            }

            if (!pcdData.bag.TryGetValue(playerId, out var bagData)) return found;

            
            foreach (var item in bagData)
                if (item.shortname == shortname && item.skin == skin)
                    found += item.amount;

            return found;            
        }

        [HookMethod("API_TakeBagItems")]
        public bool API_TakeBagItems(ulong playerId, string shortname, ulong skin, int amount)
        {
            if (IBagContainers.TryGetValue(playerId, out var container)) 
                return TakeFromBag(playerId, container, shortname, skin, amount);            

            return TakeFromData(playerId, shortname, skin, amount);
        }

        bool TakeFromBag(ulong playerId, StorageContainer container, string shortname, ulong skin, int amount)
        {
            if (container == null || container.inventory?.itemList == null) return false;

            var found = 0;
            foreach (var item in container.inventory.itemList)
            {
                if (item.info.shortname != shortname || item.skin != skin)
                    continue;

                found += item.amount;
            }

            if (found < amount)
                return false;

            var removed = 0;
            for (int i = container.inventory.itemList.Count - 1; i >= 0; i--)
            {
                var item = container.inventory.itemList[i];
                if (item.info.shortname != shortname || item.skin != skin)
                    continue;

                var remainingRemoves = amount - removed;
                if (item.amount - remainingRemoves > 0)
                {
                    item.amount -= remainingRemoves;
                    item.MarkDirty();
                    return true;
                }

                removed += item.amount;
                item.RemoveFromContainer();
                item.Remove();

                container.inventory.MarkDirty();

                if (removed >= amount)
                    return true;
            }

            return false;
        }

        bool TakeFromData(ulong playerId, string shortname, ulong skin, int amount)
        {
            if (!pcdData.bag.TryGetValue(playerId, out var bagData)) return false;

            var found = 0;
            foreach (var item in bagData)
            {
                if (item.shortname != shortname || item.skin != skin)
                    continue;

                found += item.amount;
            }

            if (found < amount)
                return false;

            var removed = 0;
            for (int i = bagData.Count - 1; i >= 0; i--)
            {
                var item = bagData[i];
                if (item.shortname != shortname || item.skin != skin)
                    continue;

                var remainingRemoves = amount - removed;
                if (item.amount - remainingRemoves > 0)
                {
                    item.amount -= remainingRemoves;
                    return true;
                }

                removed += item.amount;
                bagData.RemoveAt(i);

                if (removed >= amount)
                    return true;
            }

            return false;
        }

        #region RandomTrader

        public class RandomTraderAPI
        {
            [JsonProperty("CopyPaste file name to use")]
            public string copypaste_file_name = "CookingVending";

            [JsonProperty("Shop name")]
            public string shop_name = "Cooking Shop";

            [JsonProperty("Shop purchase limit")]
            public int shop_purchase_limit = 0;

            [JsonProperty("How many random items from the list will be picked?")]
            public int shop_display_amount = 10;

            [JsonProperty("Minimum quantity of an item that the shop will stock")]
            public int min_stock_quantity = 1;

            [JsonProperty("Maximum quantity of an item that the shop will stock")]
            public int max_stock_quantity = 6;

            [JsonProperty("Minimum cost of an item")]
            public int min_cost = 25;

            [JsonProperty("Maximum cost of an item")]
            public int max_cost = 250;
        }

        public class RTItem
        {
            public string shortname;
            public ulong skin;
            public int minQuantity;
            public int maxQuantity;
            public int minCost;
            public int maxCost;
            public string itemDisplayName;
            public string shopDisplayName;
            public string url;
            public string text;

            public RTItem(string shortname, ulong skin, int minQuantity, int maxQuantity, int minCost, int maxCost, string itemDisplayName, string shopDisplayName, string url, string text)
            {
                this.shortname = shortname;
                this.skin = skin;
                this.minQuantity = minQuantity;
                this.maxQuantity = maxQuantity;
                this.minCost = minCost;
                this.maxCost = maxCost;
                this.itemDisplayName = itemDisplayName;
                this.shopDisplayName = shopDisplayName;
                this.url = url;
                this.text = text;
            }
        }

        void AddMeals()
        {
            object[] generlSettingsOjb = new object[] { config.apiSettings.random_trader_api.copypaste_file_name, config.apiSettings.random_trader_api.shop_name, config.apiSettings.random_trader_api.shop_purchase_limit, config.apiSettings.random_trader_api.shop_display_amount };

            List<object[]> item_objects = Pool.Get<List<object[]>>();

            foreach (var kvp in config.mealSettings.meals)
            {
                int minQuantity = config.apiSettings.random_trader_api.min_stock_quantity;
                int maxQuantity = config.apiSettings.random_trader_api.max_stock_quantity;

                int minCost = config.apiSettings.random_trader_api.min_cost;
                int maxCost = config.apiSettings.random_trader_api.max_cost;

                var name = lang.GetMessage(kvp.Key.ToString(), this);

                string itemDisplayName = name;
                string shopDisplayName = name;
                var count = 0;
                foreach (var buff in kvp.Value.buffs)
                {
                    shopDisplayName += $"{(count > 0 ? " ":" [")}<color=#00ffb2>{lang.GetMessage(buff.Key.ToString() + "_ToText", this)}</color>{(count > 0 ? "," : null)}";
                    count++;
                }
                shopDisplayName += "]";

                item_objects.Add(new object[]
                {
                    kvp.Value.shortname,
                    kvp.Value.skin,
                    minQuantity,
                    maxQuantity,
                    minCost,
                    maxCost,
                    itemDisplayName,
                    shopDisplayName,
                    new KeyValuePair<string, string>()
                });
            }

            RandomTrader.Call("RTAddStore", this.Name, generlSettingsOjb, item_objects);
            Pool.FreeUnmanaged(ref item_objects);
        }

        void RandomTraderReady(List<string> current_stores)
        {
            if (current_stores == null || !current_stores.Contains(this.Name)) AddMeals();
        }

        #endregion

        #endregion

        #region commands

        enum CommandType
        {
            Chat,
            PlayerConsole,
            ServerConsole
        }

        void SendFeedbackToCommandUser(BasePlayer player, string message, CommandType commandType)
        {
            switch (commandType)
            {
                case CommandType.Chat:
                    PrintToChat(player, message);
                    return;
                case CommandType.PlayerConsole:
                    PrintToConsole(player, message);
                    return;
                case CommandType.ServerConsole:
                    Puts(message);
                    return;
            }
        }

        [ConsoleCommand("clearrecipes")]
        void ClearRecipes(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            foreach (var pi in pcdData.pEntity)
                pi.Value.learntRecipes.Clear();

            arg.ReplyWith("Cleared recipes for all players");
        }

        [ConsoleCommand("setcookingmarketstock")]
        void SetCookingMarketStockCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length <  1 || !arg.GetString(0).IsNumeric())
            {
                arg.ReplyWith("Usage: setcookingmarketstock <amount>");
                return;
            }

            var amount = Convert.ToInt32(arg.GetString(0));
            if (amount < 0)
            {
                arg.ReplyWith("Amount must be greater than or equal to 0.");
                return;
            }

            foreach (var kvp in config.ingredientSettings.ingredients)
                kvp.Value.marketInfo.availableStock = amount;

            arg.ReplyWith($"Set market quantities for each item to {amount}");
        }

        [ConsoleCommand("setcookingmarketstockfor")]
        void SetCookingMarketStockForCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (arg.Args.Length == null || arg.Args.Length < 2 || !arg.GetString(arg.Args.Length - 1).IsNumeric())
            {
                arg.ReplyWith("Usage: setcookingmarketstock <ingredient> <amount>");
                return;
            }

            var ingredient = string.Join(" ", arg.Args.Take(arg.Args.Length - 1));
            var amount = Convert.ToInt32(arg.Args.Last());
            if (amount < 0)
            {
                arg.ReplyWith("Amount must be greater than or equal to 0.");
                return;
            }

            IngredientsInfo ingredientData;
            if (!config.ingredientSettings.ingredients.TryGetValue(ingredient, out ingredientData))
            {
                arg.ReplyWith($"No match for ingredient: {ingredient}");
                return;
            }

            ingredientData.marketInfo.availableStock = amount;
            arg.ReplyWith($"Set available stock for {ingredient} to {amount}");
        }

        [ConsoleCommand("clearingredientbags")]
        void ClearIngredientBagsConsoleCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            ClearIngredientBags(player, player != null ? CommandType.PlayerConsole : CommandType.ServerConsole);
        }

        [ChatCommand("clearingredientbags")]
        void ClearIngredientBagsChatCMD(BasePlayer player)
        {
            ClearIngredientBags(player, CommandType.Chat);
        }

        void ClearIngredientBags(BasePlayer player, CommandType commandType)
        {
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            foreach (var p in BasePlayer.activePlayerList)
                if (IsLootingIngredientBag(p))
                    p.EndLooting();

            foreach (var kvp in pcdData.bag.Values)
            {
                kvp.Clear();
            }

            SendFeedbackToCommandUser(player, "Deleted ingredient bag data for all players.", commandType);
        }

        [ConsoleCommand("giverandommeal")]
        void GiveRandomMeal(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith("Usage: giverandommeal <userid>");
                return;
            }
            var target = BasePlayer.Find(arg.GetString(0));
            if (target == null)
            {
                arg.ReplyWith($"No player found with the ID: {arg.GetString(0)}");
                return;
            }

            List<KeyValuePair<string, MealInfo>> meals = Pool.Get<List<KeyValuePair<string, MealInfo>>>();
            meals.AddRange(config.mealSettings.meals.Where(x => x.Value.enabled));

            var meal = meals.GetRandom();

            GiveMeal(target, meal.Value.shortname, meal.Value.skin, meal.Key, meal.Value.spoilTime, 1);
            Pool.FreeUnmanaged(ref meals);
        }

        [ConsoleCommand("giverandomingredients")]
        void GiveRandomIngredients(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: giverandommeal <userid> <optional: amount>");
                return;
            }

            var target = BasePlayer.Find(arg.GetString(0));
            if (target == null)
            {
                arg.ReplyWith($"No player found with the ID: {arg.GetString(0)}");
                return;
            }

            int amount = 1;
            if (arg.Args.Length == 2) int.TryParse(arg.GetString(1), out amount);
            amount = Math.Max(amount, 1);

            List<KeyValuePair<string, IngredientsInfo>> ingredients = Pool.Get<List<KeyValuePair<string, IngredientsInfo>>>();
            ingredients.AddRange(config.ingredientSettings.ingredients.Where(x => x.Value.enabled));

            var ingredient = ingredients.GetRandom();
            GiveMeal(target, ingredient.Value.shortname, ingredient.Value.skin, ingredient.Key, ingredient.Value.spoilTime, amount);

            Pool.FreeUnmanaged(ref ingredients);
        }

        #region Giverecipe

        [ConsoleCommand("giverecipe")]
        void GiveRecipeConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            GiveRecipeCommand(player, arg.FullString.ToString().Split(' '), player != null ? CommandType.PlayerConsole : CommandType.ServerConsole);
        }

        [ChatCommand("giverecipe")]
        void GiveRecipeChatCommand(BasePlayer player, string command, string[] args)
        {
            GiveRecipeCommand(player, args, CommandType.Chat);
        }

        void GiveRecipeCommand(BasePlayer player, string[] args, CommandType commandType)
        {
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (args.IsNullOrEmpty() || args.Length < 2)
            {
                SendFeedbackToCommandUser(player, $"Usage: giverecipe <target> <recipe>\nValid recipes:\n{string.Join("\n- ", config.mealSettings.meals.Keys)}", commandType);
                return;
            }

            var target = FindPlayer(args[0]);
            if (target == null)
            {
                SendFeedbackToCommandUser(player, $"Could not find an online player that matched {args[0]}", commandType);
                return;
            }

            var meal = string.Join(" ", args.Skip(1));
            MealInfo mealData;
            if (!config.mealSettings.meals.TryGetValue(meal, out mealData))
            {
                SendFeedbackToCommandUser(player, $"{meal} is not a valid meal.", commandType);
                return;
            }

            var item = ItemManager.CreateByName(config.mealSettings.cookbookSettings.blueprint.shortname, 1, config.mealSettings.cookbookSettings.blueprint.skin);
            if (!string.IsNullOrEmpty(config.mealSettings.cookbookSettings.blueprint.displayName)) item.name = string.Format(config.mealSettings.cookbookSettings.blueprint.displayName, meal).ToLower();
            item.text = meal.ToLower();

            SendFeedbackToCommandUser(target, $"You found a recipe for <color=#ffae00>{lang.GetMessage(meal, this, target.UserIDString)}</color>.", commandType);
            target.GiveItem(item);
        }

        #endregion

        #region Givemeal

        [ChatCommand("givemeal")]
        void GiveMealChatCMD(BasePlayer player, string command, string[] args)
        {
            GiveMealFromCommand(player, args, CommandType.Chat);
        }

        [ConsoleCommand("givemeal")]
        void GiveMealConsoleCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            GiveMealFromCommand(player, arg.FullString.ToString().Split(' '), player != null ? CommandType.PlayerConsole : CommandType.ServerConsole);
        }
        
        void GiveMealFromCommand(BasePlayer player, string[] args, CommandType commandType)
        {
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (args.IsNullOrEmpty())
            {
                SendFeedbackToCommandUser(player, "Usage: givemeal <target> <meal name> <amount>", commandType);
                return;
            }

            var target = FindPlayer(args[0]);
            if (target == null)
            {
                SendFeedbackToCommandUser(player, $"Could not find an online player that matched {args[0]}", commandType);
                return;
            }

            string meal;
            int amount = 1;
            if (args.Last().IsNumeric())
            {
                amount = Math.Max(1, Convert.ToInt32(args.Last()));
                meal = string.Join(" ", args.Skip(1).Take(args.Length - 2));
            }
            else meal = string.Join(" ", args.Skip(1));

            MealInfo mealData;
            if (!config.mealSettings.meals.TryGetValue(meal, out mealData))
            {
                SendFeedbackToCommandUser(player, $"{meal} is not a valid meal.", commandType);
                return;
            }

            GiveMeal(target, mealData.shortname, mealData.skin, meal.ToLower(), mealData.spoilTime, amount);
            SendFeedbackToCommandUser(player, $"You gave {amount}x {meal.TitleCase()} to {target.displayName}.", commandType);
        }

        #endregion

        #region Giveingredient

        [ChatCommand("giveingredient")]
        void GiveIngredientChatCMD(BasePlayer player, string command, string[] args)
        {
            GiveIngredientFromCMD(player, args, CommandType.Chat);
        }

        [ConsoleCommand("giveingredient")]
        void GiveIngredientConsoleCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            GiveIngredientFromCMD(player, arg.FullString.ToString().Split(' '), player != null ? CommandType.PlayerConsole : CommandType.ServerConsole);
        }

        void GiveIngredientFromCMD(BasePlayer player, string[] args, CommandType commandType)
        {
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (args.IsNullOrEmpty())
            {
                SendFeedbackToCommandUser(player, "Usage: giveingredient <target> <ingredient name> <amount>", commandType);
                return;
            }

            var target = FindPlayer(args[0]);
            if (target == null)
            {
                SendFeedbackToCommandUser(player, $"Could not find an online player that matched {args[0]}", commandType);
                return;
            }

            string ingredient;
            int amount = 1;
            if (args.Last().IsNumeric())
            {
                amount = Math.Max(1, Convert.ToInt32(args.Last()));
                ingredient = string.Join(" ", args.Skip(1).Take(args.Length - 2));
            }
            else ingredient = string.Join(" ", args.Skip(1));

            IngredientsInfo ingredientData;
            if (!config.ingredientSettings.ingredients.TryGetValue(ingredient, out ingredientData))
            {
                SendFeedbackToCommandUser(player, $"{ingredient} is not a valid meal.", commandType);
                return;
            }

            GiveMeal(target, ingredientData.shortname, ingredientData.skin, ingredient, ingredientData.spoilTime, amount);
            SendFeedbackToCommandUser(player, $"You gave {amount}x {ingredient} to {target.displayName}.", commandType);
        }

        #endregion

        #region Setmarketquantity

        [ConsoleCommand("setglobalmarketquantity")]
        void SetGlobalMarketQuantity(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length == 0 || !int.TryParse(arg.GetString(0), out var amount))
            {
                arg.ReplyWith("Usage: setglobalmarketquantity <amount>");
                return;
            }

            foreach (var kvp in config.ingredientSettings.ingredients)
            {
                kvp.Value.marketInfo.availableStock = amount;
            }
            ConfigRequiresSave = true;
            arg.ReplyWith($"Updated market quantity to {amount}");
        }

        [ChatCommand("setmarketquantity")]
        void SetMarketQuantityChat(BasePlayer player, string command, string[] args)
        {
            SetMarketQuantity(player, args, CommandType.Chat);
        }

        [ConsoleCommand("setmarketquantity")]
        void SetmarketQuantityConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            SetMarketQuantity(player, arg.FullString.ToString().Split(' '), player != null ? CommandType.PlayerConsole : CommandType.ServerConsole);
        }

        void SetMarketQuantity(BasePlayer player, string[] args, CommandType commandType)
        {
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (args.IsNullOrEmpty() || !args.Last().IsNumeric())
            {
                SendFeedbackToCommandUser(player, "Usage: setmarketquantity <ingredient> <amount>", commandType);
                return;
            }

            var amount = Convert.ToInt32(args.Last());
            var ingredient = string.Join(" ", args.Take(args.Length - 1));

            bool isAll = ingredient.Equals("all", StringComparison.OrdinalIgnoreCase);

            if (isAll)
            {
                foreach (var kvp in config.ingredientSettings.ingredients)
                    kvp.Value.marketInfo.availableStock = amount;

                SendFeedbackToCommandUser(player, $"Update the availble stock for all ingredients to {amount}", commandType);
                SaveConfig();
                return;
            }

            IngredientsInfo ingredientData;
            if (!config.ingredientSettings.ingredients.TryGetValue(ingredient, out ingredientData))
            {
                SendFeedbackToCommandUser(player, $"{ingredient} is not a valid ingredient.", commandType);
                return;
            }

            ingredientData.marketInfo.availableStock = amount;
            SendFeedbackToCommandUser(player, $"Update the availble stock for {ingredient} to {amount}", commandType);
            SaveConfig();
        }

        #endregion

        #endregion        

        #region SkillTree integration

        Dictionary<string, float> SkillTreePermission_BuffDuration = new Dictionary<string, float>();
        Dictionary<string, float> SkillTreePermission_CookingTime = new Dictionary<string, float>();
        Dictionary<string, float> SkillTreePermission_IngredientChance = new Dictionary<string, float>();

        const string SkillTree_BuffDuration = "cooking.buffdurationmodifier.";
        const string SkillTree_CookingTime = "cooking.cookingtimemodifier.";
        const string SkillTree_IngredientChance = "cooking.ingredientchancemodifier.";

        void HandleSkillTreeIntegration()
        {
            if (!config.apiSettings.skilltree_settings.enabled) return;

            if (SkillTree == null || !SkillTree.IsLoaded)
            {
                Puts("Attempted to add SkillTree nodes, but SkillTree is not loaded.");
                return;
            }

            foreach (var node in config.apiSettings.skilltree_settings.Skills)
            {
                for (int i = 1; i < node.Value.max_level + 1; i++)
                {
                    string permString = string.Empty;
                    switch (node.Value.buffType)
                    {
                        case SkillTreeBuffTypes.BuffDuration:
                            permString = SkillTree_BuffDuration + $"{node.Value.value_per_level * i}";
                            if (!permission.PermissionExists(permString))
                            {
                                permission.RegisterPermission(permString, this);
                                SkillTreePermission_BuffDuration.Add(permString, node.Value.value_per_level * i);
                            }
                            break;

                        case SkillTreeBuffTypes.CookingTime:
                            permString = SkillTree_CookingTime + $"{node.Value.value_per_level * i}";
                            if (!permission.PermissionExists(permString))
                            {
                                permission.RegisterPermission(permString, this);
                                SkillTreePermission_CookingTime.Add(permString, node.Value.value_per_level * i);
                            }
                            break;

                        case SkillTreeBuffTypes.IngredientChance:
                            permString = SkillTree_IngredientChance + $"{node.Value.value_per_level * i}";
                            if (!permission.PermissionExists(permString))
                            {
                                permission.RegisterPermission(permString, this);
                                SkillTreePermission_IngredientChance.Add(permString, node.Value.value_per_level * i);
                            }
                            break;
                    }
                }
            }

            AddNodeToSkillTree();
        }

        void AddNodeToSkillTree()
        {
            foreach (var node in config.apiSettings.skilltree_settings.Skills)
            {
                try
                {
                    Dictionary<int, Dictionary<string, string>> _perms = new Dictionary<int, Dictionary<string, string>>();
                    for (int i = 1; i < node.Value.max_level + 1; i++)
                    {
                        switch (node.Value.buffType)
                        {
                            case SkillTreeBuffTypes.BuffDuration:
                                _perms.Add(i, new Dictionary<string, string>() { [SkillTree_BuffDuration + $"{node.Value.value_per_level * i}"] = $"BuffDuration + {Math.Round((node.Value.value_per_level * i) * 100, 2)}%" });
                                break;

                            case SkillTreeBuffTypes.CookingTime:
                                _perms.Add(i, new Dictionary<string, string>() { [SkillTree_CookingTime + $"{node.Value.value_per_level * i}"] = $"CookingTime - {Math.Round((node.Value.value_per_level * i) * 100, 2)}%" });
                                break;

                            case SkillTreeBuffTypes.IngredientChance:
                                _perms.Add(i, new Dictionary<string, string>() { [SkillTree_IngredientChance + $"{node.Value.value_per_level * i}"] = $"IngredientChance + {Math.Round((node.Value.value_per_level * i) * 100, 2)}%" });
                                break;
                        }
                    }

                    var description = string.Format(node.Value.description, node.Value.value_per_level * 100);

                    object[] perms = new object[]
                    {
                    description,
                    _perms
                    };

                    string Tree = "Cooking";
                    string Node = node.Key;
                    bool StartOn = node.Value.enabled;
                    int Max_Level = node.Value.max_level;
                    int Tier = node.Value.tier;
                    float Value_per_Level = node.Value.value_per_level;
                    string Buff = "Permission";
                    string BuffType = "Permission";
                    string URL = node.Value.icon_url;
                    ulong skin = node.Value.icon_skin_id;

                    SkillTree.Call("AddNode", Tree, Node, StartOn, Max_Level, Tier, Value_per_Level, Buff, BuffType, URL, perms, skin, true);
                }
                catch(Exception ex)
                {
                    Puts($"Encountered an error while adding the {node.Key} buff to SkillTree. Error: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
 