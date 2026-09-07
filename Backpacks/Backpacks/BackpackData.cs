using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Backpacks
{
    /// <summary>
    /// Serializable item entry for JSON persistence.
    /// </summary>
    public class BackpackItemEntry
    {
        [JsonProperty("itemid")]
        public int ItemId { get; set; }

        [JsonProperty("amount")]
        public int Amount { get; set; }

        [JsonProperty("slot")]
        public int Slot { get; set; }

        [JsonProperty("condition")]
        public float Condition { get; set; } = 100f;

        [JsonProperty("maxCondition")]
        public float MaxCondition { get; set; } = 100f;

        [JsonProperty("blueprint")]
        public bool Blueprint { get; set; }

        [JsonProperty("skin")]
        public ulong Skin { get; set; }

        [JsonProperty("contents")]
        public List<BackpackItemEntry> Contents { get; set; }
    }

    /// <summary>
    /// Per-player backpack data: one list of items per page (e.g. 3 pages × 48 slots).
    /// </summary>
    public class BackpackPagesData
    {
        [JsonProperty("Page0")]
        public List<BackpackItemEntry> Page0 { get; set; } = new List<BackpackItemEntry>();

        [JsonProperty("Page1")]
        public List<BackpackItemEntry> Page1 { get; set; } = new List<BackpackItemEntry>();

        [JsonProperty("Page2")]
        public List<BackpackItemEntry> Page2 { get; set; } = new List<BackpackItemEntry>();

        public List<BackpackItemEntry> GetPage(int index)
        {
            if (index == 0) return Page0 ?? (Page0 = new List<BackpackItemEntry>());
            if (index == 1) return Page1 ?? (Page1 = new List<BackpackItemEntry>());
            if (index == 2) return Page2 ?? (Page2 = new List<BackpackItemEntry>());
            return new List<BackpackItemEntry>();
        }

        public void SetPage(int index, List<BackpackItemEntry> entries)
        {
            var list = entries ?? new List<BackpackItemEntry>();
            if (index == 0) Page0 = list;
            else if (index == 1) Page1 = list;
            else if (index == 2) Page2 = list;
        }
    }
}
