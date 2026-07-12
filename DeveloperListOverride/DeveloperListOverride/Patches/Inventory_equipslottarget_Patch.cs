using HarmonyLib;
using ConVar;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// F1 command: inventory.equipslottarget loot
    /// When first argument is "loot", open the inventory of the player you're looking at (admin/developer only).
    /// Client doesn't send RPC_LootPlayer for standing players, so this is the way to access their inventory.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.equipslottarget))]
    public static class Inventory_equipslottarget_Patch
    {
        static bool Prefix(Arg arg)
        {
            BasePlayer basePlayer = arg.Player();
            if (basePlayer == null) return true;
            string first = arg.GetString(0, "").Trim().ToLowerInvariant();
            if (first != "loot") return true;

            if (!basePlayer.IsAdmin && !basePlayer.IsDeveloper && !DeveloperListOverrideConfig.IsOverrideDeveloper(basePlayer.UserIDString))
            {
                arg.ReplyWith("Only admins/developers can use inventory.equipslottarget loot");
                return false;
            }

            BasePlayer target = RelationshipManager.GetLookingAtPlayer(basePlayer);
            if (target == null || target == basePlayer)
            {
                arg.ReplyWith("Look at a player to open their inventory.");
                return false;
            }

            if (!target.CanBeLooted(basePlayer) || !basePlayer.inventory.loot.StartLootingEntity(target))
            {
                arg.ReplyWith("Could not open that player's inventory.");
                return false;
            }

            basePlayer.inventory.loot.AddContainer(target.inventory.containerMain);
            basePlayer.inventory.loot.AddContainer(target.inventory.containerWear);
            basePlayer.inventory.loot.AddContainer(target.inventory.containerBelt);
            basePlayer.inventory.loot.SendImmediate();
            basePlayer.RadioactiveLootCheck(basePlayer.inventory.loot.containers);
            basePlayer.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", basePlayer), "player_corpse");
            arg.ReplyWith($"Opened inventory of {target.displayName}");
            return false; // skip original equipslottarget
        }
    }
}
