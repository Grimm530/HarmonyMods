using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InstantBarrel.Patches;

/// <summary>
/// Patches BaseCombatEntity.OnAttacked to intercept barrel/roadsign damage.
/// When conditions are met: give loot to player, destroy barrel, skip normal damage.
/// </summary>
[HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.OnAttacked), new Type[] { typeof(HitInfo) })]
public class LootContainer_OnAttacked_Patch
{
    private static readonly HashSet<string> LootBarrelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "loot_barrel_1", "loot_barrel_2", "loot-barrel-1", "loot-barrel-2",
        "oil_barrel",
        "roadsign1", "roadsign2", "roadsign3", "roadsign4", "roadsign5",
        "roadsign6", "roadsign7", "roadsign8", "roadsign9"
    };

    private const int ScrapItemId = -932201673;

    [HarmonyPrefix]
    private static bool Prefix(BaseCombatEntity __instance, HitInfo info)
    {
        if (__instance == null || info == null || info.Initiator == null)
            return true;

        var lootContainer = __instance as LootContainer;
        if (lootContainer == null)
            return true;

        if (lootContainer.IsDestroyed)
            return false;

        var cfg = InstantBarrelConfig.Config;
        if (cfg == null)
            return true;

        if (info.ProjectileDistance > cfg.MaxDistance)
            return true;

        if (!cfg.OneShot && info.damageTypes.Total() < lootContainer.health)
            return true;

        var shortName = lootContainer.ShortPrefabName;
        if (string.IsNullOrEmpty(shortName) || !LootBarrelNames.Contains(shortName))
            return true;

        var player = lootContainer.lastAttacker as BasePlayer ?? info.InitiatorPlayer;
        if (player == null || !InstantBarrelMod.HasPermission(player.UserIDString))
            return true;

        var itemContainer = lootContainer.inventory;
        if (itemContainer == null)
            return true;

        if ((player.transform.position - lootContainer.transform.position).magnitude > cfg.MaxDistance)
            return true;

        if (!cfg.EnableWeapon && info.IsProjectile())
            return true;

        try
        {
            GiveLootToPlayer(lootContainer, player);
            DestroyBarrel(lootContainer, info, cfg.Gibs);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[InstantBarrel] Error in patch: {ex}");
        }

        return false;
    }

    private static void GiveLootToPlayer(LootContainer lootContainer, BasePlayer player)
    {
        var itemContainer = lootContainer.inventory;
        if (itemContainer?.itemList == null) return;

        // Give loot directly so it always lands in inventory (shoot barrel from distance = instant grab).
        // Notify Leaderboard via reflection so scrap/barrel loot is counted.
        for (int i = itemContainer.itemList.Count - 1; i >= 0; i--)
        {
            var item = itemContainer.itemList[i];
            if (item == null) continue;

            if (item.info.itemid == ScrapItemId)
            {
                ApplyScrapTeaBonus(lootContainer, player, item);
            }

            NotifyLeaderboardLootItems(player, item);
            player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp, GiveItemOptions.BackpackOverflow);
        }
    }

    /// <summary>
    /// If Leaderboard mod is loaded, record this item as LootItems. Scans loaded assemblies so it works regardless of load order.
    /// </summary>
    private static void NotifyLeaderboardLootItems(BasePlayer player, Item item)
    {
        if (player == null || item?.info == null || string.IsNullOrEmpty(item.info.shortname)) return;
        try
        {
            Type leaderboardType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    leaderboardType = asm.GetType("Leaderboard.LeaderboardMod");
                    if (leaderboardType != null) break;
                }
                catch { /* ignore */ }
            }
            if (leaderboardType == null) return;

            var instanceProp = leaderboardType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp == null) return;
            var mod = instanceProp.GetValue(null);
            if (mod == null) return;

            var lootTypeEnum = leaderboardType.Assembly.GetType("Leaderboard.LootType");
            if (lootTypeEnum == null) return;
            var recordMethod = leaderboardType.GetMethod("RecordStat", new[] { typeof(ulong), lootTypeEnum, typeof(string), typeof(float) });
            if (recordMethod == null) return;

            var lootItems = Enum.ToObject(lootTypeEnum, 12); // LootType.LootItems = 12
            recordMethod.Invoke(mod, new object[] { player.userID, lootItems, item.info.shortname, (float)item.amount });
        }
        catch
        {
            // Leaderboard not present or different version; ignore
        }
    }

    private static void ApplyScrapTeaBonus(LootContainer lootContainer, BasePlayer player, Item item)
    {
        if (player.modifiers == null) return;

        float scrapYield = 1f + player.modifiers.GetValue(Modifier.ModifierType.Scrap_Yield, 0f);
        if (scrapYield <= 1f) return;

        float num2 = player.modifiers.GetVariableValue(Modifier.ModifierType.Scrap_Yield, 0f);
        float num3 = Mathf.Max((float)lootContainer.scrapAmount * scrapYield - (float)lootContainer.scrapAmount, 0f);
        num2 += num3;

        int bonusScrap = 0;
        if (num2 >= 1f)
        {
            bonusScrap = (int)num2;
            num2 -= (float)bonusScrap;
        }

        player.modifiers.SetVariableValue(Modifier.ModifierType.Scrap_Yield, num2);
        if (bonusScrap > 0)
        {
            item.amount += bonusScrap;
        }
    }

    private static void DestroyBarrel(LootContainer lootContainer, HitInfo hitInfo, bool gibs)
    {
        if (lootContainer == null || lootContainer.IsDestroyed)
            return;

        lootContainer.Kill(gibs ? BaseNetworkable.DestroyMode.Gib : BaseNetworkable.DestroyMode.None);
    }
}
