using System;
using HarmonyLib;
using Rust.Ai.Gen2;
using UnityEngine;

namespace RustRewardsHarmony.Patches
{
    /// <summary>
    /// Maps Oxide reward hooks to game methods for GrimmRewards / RustRewards 3.2.5.
    /// </summary>
    internal static class RewardHooks
    {
        internal static RustRewards Plugin => RustRewardsHarmonyMod.Instance?.Plugin;
    }

    // ---- Harvest / gather ----

    /// <summary>
    /// Oxide OnDispenserGather(ResourceDispenser, BaseEntity, Item).
    /// Item arg is unused by reward logic (flesh/tree keyed off entity); pass null.
    /// </summary>
    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.GiveResourceFromItem))]
    internal static class ResourceDispenser_GiveResourceFromItem_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ResourceDispenser __instance, BasePlayer entity)
        {
            try
            {
                if (entity == null) return;
                RewardHooks.Plugin?.OnDispenserGather(__instance, entity, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnDispenserGather: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnDispenserBonus → ResourceDispenser.AssignFinishBonus (item unused; ore from prefab name).</summary>
    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.AssignFinishBonus))]
    internal static class ResourceDispenser_AssignFinishBonus_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ResourceDispenser __instance, BasePlayer player)
        {
            try
            {
                if (player == null) return;
                RewardHooks.Plugin?.OnDispenserBonus(__instance, player, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnDispenserBonus: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Oxide OnGrowableGathered → GrowableEntity.PickFruit.
    /// Prefix: PickFruit may Die() the plant on final harvest before a postfix runs.
    /// </summary>
    [HarmonyPatch(typeof(GrowableEntity), nameof(GrowableEntity.PickFruit), typeof(BasePlayer), typeof(bool))]
    internal static class GrowableEntity_PickFruit_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(GrowableEntity __instance, BasePlayer player)
        {
            try
            {
                RewardHooks.Plugin?.OnGrowableGathered(__instance, null, player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnGrowableGathered: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Oxide OnCollectiblePickup → CollectibleEntity.DoPickup.
    /// MUST be Prefix: DoPickup ends with Kill(), so postfix sees null net / destroyed entity.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleEntity), nameof(CollectibleEntity.DoPickup), typeof(BasePlayer), typeof(bool))]
    internal static class CollectibleEntity_DoPickup_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(CollectibleEntity __instance, BasePlayer reciever)
        {
            try
            {
                RewardHooks.Plugin?.OnCollectiblePickup(__instance, reciever);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnCollectiblePickup: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnItemAction → ItemMod.ServerCommand (unwrap / Gut / etc.).</summary>
    [HarmonyPatch(typeof(ItemMod), nameof(ItemMod.ServerCommand))]
    internal static class ItemMod_ServerCommand_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Item item, string command, BasePlayer player)
        {
            try
            {
                if (string.IsNullOrEmpty(command)) return;
                RewardHooks.Plugin?.OnItemAction(item, command, player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnItemAction: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnLootEntityEnd → StorageContainer.PlayerStoppedLooting (LootContainer calls base).</summary>
    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    internal static class StorageContainer_PlayerStoppedLooting_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(StorageContainer __instance, BasePlayer player)
        {
            try
            {
                if (__instance == null || player == null) return;
                RewardHooks.Plugin?.OnLootEntityEnd(player, __instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnLootEntityEnd: " + ex.Message);
            }
        }
    }

    // ---- Combat / death ----

    /// <summary>Oxide OnEntityTakeDamage → BaseCombatEntity.Hurt(HitInfo).</summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), typeof(HitInfo))]
    internal static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            try
            {
                if (__instance == null || info == null) return;
                RewardHooks.Plugin?.OnEntityTakeDamage(__instance, info);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnEntityTakeDamage: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnPlayerDeath → BasePlayer.Die(HitInfo).</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), typeof(HitInfo))]
    internal static class BasePlayer_Die_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance, HitInfo info)
        {
            try
            {
                if (__instance == null) return;
                RewardHooks.Plugin?.OnPlayerDeath(__instance, info);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnPlayerDeath: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Oxide OnEntityDeath overloads → BaseCombatEntity.Die(HitInfo).
    /// Dispatches to the most specific plugin overload (Oxide-style typed hooks).
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), typeof(HitInfo))]
    internal static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            try
            {
                var plugin = RewardHooks.Plugin;
                if (plugin == null || __instance == null) return;

                // Players are handled by BasePlayer_Die_Patch → OnPlayerDeath
                if (__instance is BasePlayer)
                    return;

                if (__instance is LootContainer barrel)
                {
                    plugin.OnEntityDeath(barrel, info);
                    return;
                }

                if (__instance is BaseVehicle vehicle)
                {
                    plugin.OnEntityDeath(vehicle, info);
                    return;
                }

                if (__instance is BaseNPC2 npc2)
                {
                    plugin.OnEntityDeath(npc2, info);
                    return;
                }

                if (__instance is BaseAnimalNPC animal)
                {
                    plugin.OnEntityDeath(animal, info);
                    return;
                }

                if (__instance is SimpleShark shark)
                {
                    plugin.OnEntityDeath(shark, info);
                    return;
                }

                if (__instance is SnakeHazard snake)
                {
                    plugin.OnEntityDeath(snake, info);
                    return;
                }

                plugin.OnEntityDeath(__instance, info);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnEntityDeath: " + ex.Message);
            }
        }
    }
}
