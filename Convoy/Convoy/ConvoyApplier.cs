using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Applies Convoy NPC preset (health, wear, belt) and corpse loot from preset's Own loot table.
    /// </summary>
    public static class ConvoyApplier
    {
        private static PropertyInfo _inventoryProp;

        private static PlayerInventory GetInventory(BaseCombatEntity npc)
        {
            if (npc == null) return null;
            if (npc is BasePlayer bp) return bp.inventory;
            if (_inventoryProp == null)
                _inventoryProp = npc.GetType().GetProperty("inventory", BindingFlags.Public | BindingFlags.Instance);
            return _inventoryProp?.GetValue(npc) as PlayerInventory;
        }

        public static bool ApplyNpcPreset(BaseCombatEntity npc, string presetName)
        {
            if (npc == null || npc.net == null || string.IsNullOrEmpty(presetName)) return false;
            var mod = ConvoyMod.Instance;
            var preset = mod?.GetNpcPreset(presetName);
            if (preset == null) return false;

            ConvoyState.RegisterNpcPreset((ulong)npc.net.ID.Value, presetName);

            float health = preset.Health;
            if (health > 0f)
            {
                npc.health = health;
                var maxHealthProp = npc.GetType().GetProperty("MaxHealth", BindingFlags.Public | BindingFlags.Instance);
                if (maxHealthProp != null && maxHealthProp.CanWrite)
                    maxHealthProp.SetValue(npc, health);
            }

            var inventory = GetInventory(npc);
            if (inventory?.containerBelt == null && inventory?.containerWear == null) return true;

            if (preset.BeltItems != null && inventory.containerBelt != null)
            {
                foreach (var entry in preset.BeltItems)
                {
                    if (string.IsNullOrEmpty(entry.ShortName)) continue;
                    var def = ItemManager.FindItemDefinition(entry.ShortName);
                    if (def == null) continue;
                    int amount = entry.Amount > 0 ? entry.Amount : 1;
                    var item = ItemManager.Create(def, amount, entry.SkinId > 0 ? (ulong)entry.SkinId : 0UL);
                    if (item != null && entry.Mods != null && entry.Mods.Count > 0)
                    {
                        foreach (var modName in entry.Mods)
                        {
                            var modDef = ItemManager.FindItemDefinition(modName);
                            if (modDef != null)
                                item.contents?.AddItem(modDef, 1, 0UL);
                        }
                    }
                    if (item != null)
                        item.MoveToContainer(inventory.containerBelt);
                }
            }

            if (preset.WearItems != null && inventory?.containerWear != null)
            {
                foreach (var entry in preset.WearItems)
                {
                    if (string.IsNullOrEmpty(entry.ShortName)) continue;
                    var def = ItemManager.FindItemDefinition(entry.ShortName);
                    if (def == null) continue;
                    var item = ItemManager.Create(def, 1, entry.SkinId > 0 ? (ulong)entry.SkinId : 0UL);
                    if (item != null)
                        item.MoveToContainer(inventory.containerWear);
                }
            }

            return true;
        }

        public static void PopulateCorpseFromPreset(LootableCorpse corpse, ulong sourceNpcNetId)
        {
            if (corpse?.containers == null || sourceNpcNetId == 0) return;
            string presetName = ConvoyState.GetNpcPresetName(sourceNpcNetId);
            if (string.IsNullOrEmpty(presetName)) return;
            var mod = ConvoyMod.Instance;
            var preset = mod?.GetNpcPreset(presetName);
            if (preset?.OwnLootTable == null) return;

            var container = corpse.containers?[0];
            if (container == null) return;

            AddLootFromTable(container, preset.OwnLootTable);
        }

        public static void AddLootFromTable(ItemContainer container, LootTableEntry table)
        {
            if (container == null || table == null) return;

            if (table.EnableItemList && table.Items != null && table.Items.Count > 0)
            {
                int count = Mathf.Clamp(Random.Range(table.MinItems, table.MaxItems + 1), 1, 100);
                for (int i = 0; i < count; i++)
                {
                    foreach (var entry in table.Items)
                    {
                        if (Random.Range(0f, 100f) > entry.Chance) continue;
                        var def = ItemManager.FindItemDefinition(entry.ShortName);
                        if (def == null) continue;
                        int amount = Mathf.Clamp(Random.Range(entry.Minimum, entry.Maximum + 1), 1, def.stackable);
                        var item = ItemManager.Create(def, amount, 0UL);
                        if (item != null && !item.MoveToContainer(container))
                            item.Remove();
                    }
                }
            }
        }
    }
}
