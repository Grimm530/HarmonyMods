using HarmonyLib;

namespace BetterBackpack;

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.DropBackpackOnDeath))]
internal class PlayerInventory_DropBackpackOnDeath_Patch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerInventory __instance, bool wounded)
    {
        var player = __instance?.baseEntity;
        if (!LootDebug.ShouldLog(player)) return;
        LootDebug.DumpInventory(player, wounded ? "DropBackpackOnDeath wounded" : "DropBackpackOnDeath");
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance)
    {
        var player = __instance?.baseEntity;
        if (!LootDebug.ShouldLog(player)) return;
        var bag = __instance.GetBackpackWithInventory();
        LootDebug.Log(player, bag == null
            ? "DropBackpackOnDeath after: worn backpack gone (dropped or never equipped)"
            : $"DropBackpackOnDeath after: still wearing {bag.info?.shortname} uid={bag.uid.Value}");
    }
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.TryDropBackpack))]
internal class PlayerInventory_TryDropBackpack_Patch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerInventory __instance)
    {
        var player = __instance?.baseEntity;
        if (!LootDebug.ShouldLog(player)) return;
        var bag = __instance.GetBackpackWithInventory();
        if (bag == null)
        {
            LootDebug.Log(player, "TryDropBackpack: no backpack");
            return;
        }
        LootDebug.Log(player, $"TryDropBackpack: dropping {bag.info?.shortname} uid={bag.uid.Value} | {LootDebug.ContentsList(bag.contents)}");
    }
}
