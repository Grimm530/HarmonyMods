using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PersonalNPCHarmony.Patches
{
    /// <summary>
    /// Shared plumbing for the hook patches: short accessors plus the reentrancy guards that
    /// Oxide got for free (its hook dispatcher never re-enters a hook from inside itself).
    /// </summary>
    internal static class Hooks
    {
        internal static PersonalNPC Core => PersonalNPCHarmonyMod.Instance?.Plugin;
        internal static PersonalNPCHelper Helper => PersonalNPCHarmonyMod.Instance?.Helper;
        internal static PNPCAddonBuilder Builder => PersonalNPCHarmonyMod.Instance?.Builder;

        internal static void Warn(string hook, Exception ex)
        {
            Debug.LogWarning("[PersonalNPC] " + hook + ": " + ex.Message);
        }

        /// <summary>
        /// PersonalNPC re-opens loot panels from inside CanLootEntity, which calls back into
        /// StartLootingEntity. Without this the prefix would recurse forever.
        /// </summary>
        [ThreadStatic] internal static bool InLootHook;
    }

    // ---------------------------------------------------------------- damage / targeting

    /// <summary>Oxide OnEntityTakeDamage. Core and the builder addon both veto damage.</summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null || info == null) return true;

            try
            {
                if (Hooks.Core?.OnEntityTakeDamage(__instance, info) != null) return false;
            }
            catch (Exception ex) { Hooks.Warn("OnEntityTakeDamage", ex); }

            try
            {
                if (Hooks.Builder?.OnEntityTakeDamage(__instance, info) != null) return false;
            }
            catch (Exception ex) { Hooks.Warn("Builder OnEntityTakeDamage", ex); }

            return true;
        }
    }

    /// <summary>Oxide CanBeTargeted for auto turrets.</summary>
    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.ShouldTarget))]
    public static class AutoTurret_ShouldTarget_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(AutoTurret __instance, BaseCombatEntity targ, ref bool __result)
        {
            if (__instance == null || targ == null) return true;
            try
            {
                if (Hooks.Core?.CanBeTargeted(targ, __instance) is bool allowed && !allowed)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanBeTargeted(turret)", ex); }
            return true;
        }
    }

    /// <summary>Oxide CanBeTargeted for the Gen2 NPC sense component (scientists, tunnel dwellers...).</summary>
    [HarmonyPatch(typeof(Rust.Ai.Gen2.SenseComponent), nameof(Rust.Ai.Gen2.SenseComponent.CanTarget))]
    public static class SenseComponent_CanTarget_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Rust.Ai.Gen2.SenseComponent __instance, BaseEntity entity, ref bool __result)
        {
            var combat = entity as BaseCombatEntity;
            if (__instance == null || combat == null) return true;
            try
            {
                if (Hooks.Core?.CanBeTargeted(combat, __instance) is bool allowed && !allowed)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanBeTargeted(sense)", ex); }
            return true;
        }
    }

    /// <summary>Oxide CanBradleyApcTarget.</summary>
    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.VisibilityTest))]
    public static class BradleyAPC_VisibilityTest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BradleyAPC __instance, BaseEntity ent, ref bool __result)
        {
            if (__instance == null || ent == null) return true;
            try
            {
                if (Hooks.Core?.CanBradleyApcTarget(__instance, ent) is bool allowed && !allowed)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanBradleyApcTarget", ex); }
            return true;
        }
    }

    // ---------------------------------------------------------------- items

    /// <summary>Oxide OnLoseCondition. Fires before the condition drop is applied.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.LoseCondition))]
    public static class Item_LoseCondition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item __instance, float amount)
        {
            if (__instance == null) return;
            try { Hooks.Core?.OnLoseCondition(__instance, amount); }
            catch (Exception ex) { Hooks.Warn("OnLoseCondition", ex); }
        }
    }

    /// <summary>Oxide OnItemAction (unload_ammo, drop, ...).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.ServerCommand))]
    public static class Item_ServerCommand_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, string command, BasePlayer player)
        {
            if (__instance == null || player == null) return true;
            try
            {
                if (Hooks.Core?.OnItemAction(__instance, command, player) != null) return false;
            }
            catch (Exception ex) { Hooks.Warn("OnItemAction", ex); }
            return true;
        }
    }

    /// <summary>Oxide CanAcceptItem.</summary>
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.CanAcceptItem))]
    public static class ItemContainer_CanAcceptItem_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ItemContainer __instance, BasePlayer player, Item item, int targetPos, ref ItemContainer.CanAcceptResult __result)
        {
            if (__instance == null || item == null) return true;
            try
            {
                if (Hooks.Core?.CanAcceptItem(__instance, item, targetPos) is ItemContainer.CanAcceptResult result)
                {
                    __result = result;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanAcceptItem", ex); }
            return true;
        }
    }

    /// <summary>
    /// Oxide CanMoveItem. Oxide injects this inside PlayerInventory.MoveItem's RPC body, which is
    /// not patchable without a transpiler, so we hook the move itself and rebuild the arguments.
    /// Internal moves have no source player, and the plugin bails out on a null player.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer))]
    public static class Item_MoveToContainer_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, ItemContainer newcontainer, int iTargetPos, BasePlayer sourcePlayer, ref bool __result)
        {
            if (__instance == null || sourcePlayer == null) return true;

            var inventory = sourcePlayer.inventory;
            if (inventory == null) return true;

            try
            {
                var targetId = newcontainer != null ? newcontainer.uid : default(ItemContainerId);
                object hook = Hooks.Core?.CanMoveItem(__instance, inventory, targetId, iTargetPos, __instance.amount, default(ItemMoveModifier));
                if (hook != null)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanMoveItem", ex); }
            return true;
        }
    }

    // ---------------------------------------------------------------- gather

    /// <summary>
    /// Oxide OnDispenserGather / OnDispenserBonus. Both end at BasePlayer.GiveItem with the
    /// ResourceHarvested reason, and the plugin applies gather rates plus owner redirection there.
    /// </summary>
    [HarmonyPatch]
    public static class BasePlayer_GiveItem_Patch
    {
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original) => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            foreach (var type in new[] { typeof(BasePlayer), typeof(BaseEntity) })
            {
                var m = AccessTools.GetDeclaredMethods(type)
                    .FirstOrDefault(x => x.Name == "GiveItem" && x.GetParameters().Length >= 2 &&
                                         x.GetParameters()[0].ParameterType == typeof(Item));
                if (m != null) return m;
            }
            return null;
        }

        [HarmonyPrefix]
        public static bool Prefix(BaseEntity __instance, Item item, BaseEntity.GiveItemReason reason)
        {
            if (reason != BaseEntity.GiveItemReason.ResourceHarvested) return true;

            var player = __instance as BasePlayer;
            if (player == null || item == null) return true;

            try
            {
                if (Hooks.Core?.OnDispenserGather(null, player, item) != null) return false;
            }
            catch (Exception ex) { Hooks.Warn("OnDispenserGather", ex); }
            return true;
        }
    }

    // ---------------------------------------------------------------- building

    /// <summary>Oxide OnEntityBuilt.</summary>
    [HarmonyPatch]
    public static class Planner_DoBuild_Patch
    {
        public static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(Planner))
                .FirstOrDefault(m => m.Name == "DoBuild" && m.ReturnType == typeof(BaseEntity));

        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__instance == null || __result == null) return;
            try { Hooks.Core?.OnEntityBuilt(__instance, __result.gameObject); }
            catch (Exception ex) { Hooks.Warn("OnEntityBuilt", ex); }
        }
    }

    /// <summary>Oxide OnStructureUpgrade (builder addon blocks upgrades on bot-owned bases).</summary>
    [HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.CanChangeToGrade))]
    public static class BuildingBlock_CanChangeToGrade_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingBlock __instance, BuildingGrade.Enum iGrade, BasePlayer player, ref bool __result)
        {
            if (__instance == null || player == null) return true;
            try
            {
                if (Hooks.Builder?.OnStructureUpgrade(__instance, player, iGrade) != null)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("OnStructureUpgrade", ex); }
            return true;
        }
    }

    // ---------------------------------------------------------------- entities

    /// <summary>Oxide OnEntitySpawned, narrowed to the collectibles the plugin tracks.</summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is not CollectibleEntity collectible) return;
            try { Hooks.Core?.OnEntitySpawned(collectible); }
            catch (Exception ex) { Hooks.Warn("OnEntitySpawned", ex); }
        }
    }

    /// <summary>Oxide OnEntityKill, narrowed to collectibles.</summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (__instance is not CollectibleEntity collectible) return;
            if (collectible.IsDestroyed) return;
            try { Hooks.Core?.OnEntityKill(collectible); }
            catch (Exception ex) { Hooks.Warn("OnEntityKill", ex); }
        }
    }

    /// <summary>Oxide OnEntityMounted.</summary>
    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.MountPlayer))]
    public static class BaseMountable_MountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { Hooks.Core?.OnEntityMounted(__instance, player); }
            catch (Exception ex) { Hooks.Warn("OnEntityMounted", ex); }
        }
    }

    /// <summary>Oxide CanUseGesture - the plugin uses the Point gesture as the bot command input.</summary>
    [HarmonyPatch]
    public static class BasePlayer_Server_StartGesture_Patch
    {
        public static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(BasePlayer))
                .FirstOrDefault(m => m.Name == "Server_StartGesture" &&
                                     m.GetParameters().Length > 0 &&
                                     m.GetParameters()[0].ParameterType == typeof(GestureConfig));

        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, GestureConfig toPlay)
        {
            if (__instance == null || toPlay == null) return true;
            try
            {
                if (Hooks.Core?.CanUseGesture(__instance, toPlay) is bool allowed && !allowed)
                    return false;
            }
            catch (Exception ex) { Hooks.Warn("CanUseGesture", ex); }
            return true;
        }
    }

    /// <summary>Oxide CanUseLockedEntity - lets an owner reach locks on their bot's vehicles.</summary>
    [HarmonyPatch]
    public static class BaseLock_OnTryToOpen_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in new[] { typeof(BaseLock), typeof(CodeLock), typeof(KeyLock) })
            {
                var m = AccessTools.DeclaredMethod(type, "OnTryToOpen", new[] { typeof(BasePlayer) });
                if (m != null) yield return m;
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(BaseLock __instance, BasePlayer player, ref bool __result)
        {
            if (__instance == null || player == null) return true;
            try
            {
                if (Hooks.Core?.CanUseLockedEntity(player, __instance) is bool allowed)
                {
                    __result = allowed;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanUseLockedEntity", ex); }
            return true;
        }
    }

    // ---------------------------------------------------------------- looting

    /// <summary>Oxide CanLootEntity + OnLootEntity for bot corpses and bot backpacks.</summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity))]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            if (Hooks.InLootHook) return true;

            var player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return true;

            var core = Hooks.Core;
            if (core == null) return true;

            Hooks.InLootHook = true;
            try
            {
                object hook = targetEntity switch
                {
                    LootableCorpse corpse => core.CanLootEntity(player, corpse),
                    DroppedItemContainer dropped => core.CanLootEntity(player, dropped),
                    _ => null
                };

                if (hook is bool allowed && !allowed)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Hooks.Warn("CanLootEntity", ex); }
            finally { Hooks.InLootHook = false; }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || Hooks.InLootHook) return;

            var player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return;

            try { Hooks.Core?.OnLootEntity(player, targetEntity); }
            catch (Exception ex) { Hooks.Warn("OnLootEntity", ex); }
        }
    }

    /// <summary>
    /// Oxide OnCorpsePopulate. LootableCorpse.TakeFrom has three overloads and the plugin schedules
    /// loot transfer on the next tick, so guard against the same corpse firing twice in one frame.
    /// </summary>
    [HarmonyPatch]
    public static class LootableCorpse_TakeFrom_Patch
    {
        private static BaseEntity _lastCorpse;
        private static int _lastFrame = -1;

        public static IEnumerable<MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(LootableCorpse))
                .Where(m => m.Name == "TakeFrom")
                .Cast<MethodBase>();

        [HarmonyPostfix]
        public static void Postfix(LootableCorpse __instance, BaseEntity fromEntity)
        {
            if (__instance == null || fromEntity == null) return;

            int frame = Time.frameCount;
            if (_lastFrame == frame && ReferenceEquals(_lastCorpse, __instance)) return;
            _lastFrame = frame;
            _lastCorpse = __instance;

            var core = Hooks.Core;
            if (core == null) return;

            try
            {
                if (__instance is NPCPlayerCorpse npcCorpse && fromEntity is NPCPlayer npc)
                    core.OnCorpsePopulate(npc, npcCorpse);
                else
                    core.OnCorpsePopulate(fromEntity, __instance);
            }
            catch (Exception ex) { Hooks.Warn("OnCorpsePopulate", ex); }
        }
    }

    // ---------------------------------------------------------------- save / wipe

    /// <summary>Oxide OnServerSave - the helper persists its unlocked-player list here.</summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), new[] { typeof(bool) })]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            try { Hooks.Helper?.OnServerSave(); }
            catch (Exception ex) { Hooks.Warn("OnServerSave", ex); }
        }
    }

    /// <summary>
    /// Oxide OnNewSave. SaveRestore.Load returning false means there was no save to restore, which
    /// is what Oxide uses to detect a wipe.
    /// </summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        internal static bool NewSaveDetected;

        [HarmonyPostfix]
        public static void Postfix(bool __result)
        {
            if (__result) return;
            NewSaveDetected = true;

            try { Hooks.Helper?.OnNewSave(); }
            catch (Exception ex) { Hooks.Warn("OnNewSave", ex); }
        }
    }
}
