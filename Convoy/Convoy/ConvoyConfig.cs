using System.Collections.Generic;
using Newtonsoft.Json;

namespace Convoy
{
    /// <summary>
    /// Config aligned with Convoy.json (Loot Settings, NPC Configurations, Crate presets). Same structure as Oxide Convoy plugin.
    /// </summary>
    public class ConvoyConfig
    {
        [JsonProperty(PropertyName = "Loot Settings")]
        public LootSettingsOptions LootSettings { get; set; } = new LootSettingsOptions();

        [JsonProperty(PropertyName = "NPC Configurations")]
        public List<NpcPresetEntry> NpcPresets { get; set; } = new List<NpcPresetEntry>();

        [JsonProperty(PropertyName = "Crate presets")]
        public List<CratePresetEntry> CratePresets { get; set; } = new List<CratePresetEntry>();

        [JsonProperty(PropertyName = "Prefix of chat messages")]
        public string Prefix { get; set; } = "[Convoy]";

        /// <summary>When true, log to server console: config load, command registration, command invocations, and map marker creation. Turn off in production.</summary>
        [JsonProperty(PropertyName = "Enable debug logging [true/false]")]
        public bool Debug { get; set; } = false;

        /// <summary>Main Setting from Convoy.json - timer-based auto start (no player required).</summary>
        [JsonProperty(PropertyName = "Main Setting")]
        public MainConfig MainConfig { get; set; } = new MainConfig();

        /// <summary>Default spawn position (x,y,z) when starting from server console or auto-timer. Used if no player.</summary>
        [JsonProperty(PropertyName = "Default event position (x,y,z)")]
        public float[] DefaultEventPosition { get; set; } = new float[] { 0f, 100f, 0f };

        /// <summary>How long auto-started event runs before auto-stopping and rescheduling timer [sec]. 0 = run until convoystop.</summary>
        [JsonProperty(PropertyName = "Event duration when auto-started [sec]")]
        public int EventDurationAutoSec { get; set; } = 3600;

        /// <summary>Map marker settings (shop + ring). Matches Convoy.json Marker Config.</summary>
        [JsonProperty(PropertyName = "Marker Config")]
        public MarkerConfig MarkerConfig { get; set; } = new MarkerConfig();
    }

    public class MarkerConfig
    {
        [JsonProperty(PropertyName = "Do you use the Marker? [true/false]")]
        public bool Enable { get; set; } = true;

        [JsonProperty(PropertyName = "Use a shop marker? [true/false]")]
        public bool UseShopMarker { get; set; } = true;

        [JsonProperty(PropertyName = "Use a circular marker? [true/false]")]
        public bool UseRingMarker { get; set; } = true;

        /// <summary>Ring radius on map. Use small values (Oxide default 0.2); large values (e.g. 50) fill the whole map.</summary>
        [JsonProperty(PropertyName = "Radius")]
        public float Radius { get; set; } = 0.2f;

        [JsonProperty(PropertyName = "Alpha")]
        public float Alpha { get; set; } = 0.6f;

        [JsonProperty(PropertyName = "Marker color")]
        public ColorConfig Color1 { get; set; } = new ColorConfig();

        [JsonProperty(PropertyName = "Outline color")]
        public ColorConfig Color2 { get; set; } = new ColorConfig();
    }

    public class ColorConfig
    {
        [JsonProperty("r")]
        public float R { get; set; }

        [JsonProperty("g")]
        public float G { get; set; }

        [JsonProperty("b")]
        public float B { get; set; }
    }

    public class MainConfig
    {
        [JsonProperty(PropertyName = "Enable automatic event holding [true/false]")]
        public bool IsAutoEvent { get; set; } = true;

        [JsonProperty(PropertyName = "Minimum time between events [sec]")]
        public int MinTimeBetweenEvents { get; set; } = 3600;

        [JsonProperty(PropertyName = "Maximum time between events [sec]")]
        public int MaxTimeBetweenEvents { get; set; } = 3600;
    }

    public class LootSettingsOptions
    {
        [JsonProperty(PropertyName = "When the car is destroyed, loot falls to the ground [true/false]")]
        public bool LootFallsOnDestroy { get; set; } = true;

        [JsonProperty(PropertyName = "Percentage of loot loss when destroying a сar [0.0-1.0]")]
        public float LootLossPercentOnDestroy { get; set; } = 0.5f;

        [JsonProperty(PropertyName = "Prohibit looting crates if the convoy is moving [true/false]")]
        public bool ProhibitLootingWhenMoving { get; set; } = false;

        [JsonProperty(PropertyName = "Prohibit looting crates if NPCs are alive [true/false]")]
        public bool ProhibitLootingWhenNpcsAlive { get; set; } = false;

        [JsonProperty(PropertyName = "Prohibit looting crates if Bradleys are alive [true/false]")]
        public bool ProhibitLootingWhenBradleyAlive { get; set; } = false;

        [JsonProperty(PropertyName = "Prohibit looting crates if Heli is alive [true/false]")]
        public bool ProhibitLootingWhenHeliAlive { get; set; } = false;

        /// <summary>When a player/team deals this much damage to convoy entities, the entire event locks to that team (only they can loot/hack). 0 = disabled.</summary>
        [JsonProperty(PropertyName = "Event lock: damage threshold to lock event to attacker team [0 = disabled]")]
        public float EventLockDamageThreshold { get; set; } = 500f;

        /// <summary>If the locked team deals no damage for this many seconds, the event unlocks. Seconds.</summary>
        [JsonProperty(PropertyName = "Event lock: unlock after no damage for (seconds)")]
        public int EventLockUnlockAfterSeconds { get; set; } = 900;
    }

    public class NpcPresetEntry
    {
        [JsonProperty(PropertyName = "Preset Name")]
        public string PresetName { get; set; } = "";

        [JsonProperty(PropertyName = "Name")]
        public string DisplayName { get; set; } = "";

        [JsonProperty(PropertyName = "Health")]
        public float Health { get; set; } = 100f;

        [JsonProperty(PropertyName = "Scale damage")]
        public float ScaleDamage { get; set; } = 1f;

        [JsonProperty(PropertyName = "Speed")]
        public float Speed { get; set; } = 5f;

        [JsonProperty(PropertyName = "Wear items")]
        public List<WearItemEntry> WearItems { get; set; } = new List<WearItemEntry>();

        [JsonProperty(PropertyName = "Belt items")]
        public List<BeltItemEntry> BeltItems { get; set; } = new List<BeltItemEntry>();

        [JsonProperty(PropertyName = "Own loot table")]
        public LootTableEntry OwnLootTable { get; set; } = new LootTableEntry();
    }

    public class WearItemEntry
    {
        [JsonProperty(PropertyName = "ShortName")]
        public string ShortName { get; set; } = "";

        [JsonProperty(PropertyName = "skinID (0 - default)")]
        public uint SkinId { get; set; }
    }

    public class BeltItemEntry
    {
        [JsonProperty(PropertyName = "ShortName")]
        public string ShortName { get; set; } = "";

        [JsonProperty(PropertyName = "Amount")]
        public int Amount { get; set; } = 1;

        [JsonProperty(PropertyName = "skinID (0 - default)")]
        public uint SkinId { get; set; }

        [JsonProperty(PropertyName = "Mods")]
        public List<string> Mods { get; set; } = new List<string>();

        [JsonProperty(PropertyName = "Ammo")]
        public string Ammo { get; set; } = "";
    }

    public class LootTableEntry
    {
        [JsonProperty(PropertyName = "Enable spawn loot from prefabs")]
        public bool EnablePrefabLoot { get; set; }

        [JsonProperty(PropertyName = "List of prefabs (one is randomly selected)")]
        public List<PrefabLootEntry> PrefabList { get; set; } = new List<PrefabLootEntry>();

        [JsonProperty(PropertyName = "Enable spawn of items from the list")]
        public bool EnableItemList { get; set; }

        [JsonProperty(PropertyName = "Minimum numbers of items")]
        public int MinItems { get; set; } = 1;

        [JsonProperty(PropertyName = "Maximum numbers of items")]
        public int MaxItems { get; set; } = 1;

        [JsonProperty(PropertyName = "List of items")]
        public List<LootItemEntry> Items { get; set; } = new List<LootItemEntry>();
    }

    public class PrefabLootEntry
    {
        [JsonProperty(PropertyName = "Prefab displayName")]
        public string PrefabName { get; set; } = "";

        [JsonProperty(PropertyName = "Minimum Loot multiplier")]
        public int MinMultiplier { get; set; } = 1;

        [JsonProperty(PropertyName = "Maximum Loot multiplier")]
        public int MaxMultiplier { get; set; } = 1;
    }

    public class LootItemEntry
    {
        [JsonProperty(PropertyName = "ShortName")]
        public string ShortName { get; set; } = "";

        [JsonProperty(PropertyName = "Minimum")]
        public int Minimum { get; set; }

        [JsonProperty(PropertyName = "Maximum")]
        public int Maximum { get; set; }

        [JsonProperty(PropertyName = "Chance [0.0-100.0]")]
        public float Chance { get; set; } = 100f;
    }

    public class CratePresetEntry
    {
        [JsonProperty(PropertyName = "Preset Name")]
        public string PresetName { get; set; } = "";

        [JsonProperty(PropertyName = "Prefab")]
        public string Prefab { get; set; } = "";

        [JsonProperty(PropertyName = "SkinID (0 - default)")]
        public uint SkinId { get; set; }

        [JsonProperty(PropertyName = "Own loot table")]
        public LootTableEntry OwnLootTable { get; set; } = new LootTableEntry();
    }
}
