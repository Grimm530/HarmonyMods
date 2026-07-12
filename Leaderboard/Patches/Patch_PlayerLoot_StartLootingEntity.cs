using System.Reflection;
using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
public static class Patch_PlayerLoot_StartLootingEntity
{
    static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
    {
        if (!__result || __instance == null || targetEntity == null) return;
        var player = GetPlayerFromPlayerLoot(__instance);
        if (player == null || !SteamIdHelper.IsSteamId(player.userID)) return;
        if (targetEntity is not StorageContainer storageContainer) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        mod.RecordStat(player.userID, LootType.Crate, storageContainer.ShortPrefabName ?? "crate", 1f);

        // LootItems are recorded when the player actually takes items (Patch_Item_MoveToContainer),
        // so opening crates/boxes and manually looting is counted there. Barrel loot is counted
        // via WorldItem.Pickup (InstantBarrel) and LootContainer.DropItems (vanilla break).
    }

    private static BasePlayer GetPlayerFromPlayerLoot(PlayerLoot loot)
    {
        if (loot == null) return null;
        var t = loot.GetType();
        while (t != null)
        {
            var f = t.GetField("baseEntity", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null)
            {
                return f.GetValue(loot) as BasePlayer;
            }
            t = t.BaseType;
        }
        return null;
    }
}
