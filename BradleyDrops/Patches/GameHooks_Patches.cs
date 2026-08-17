using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using BD = Oxide.Plugins.BradleyDrops;

namespace BradleyDropsHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || (!message.StartsWith("/") && !message.StartsWith("\\")))
                return true;
            var player = arg.Player();
            if (player == null) return true;
            try
            {
                if (BradleyDropsMod.Instance != null && BradleyDropsMod.Instance.OnChatCommand(player, message))
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] Chat.say: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;
            if (!string.Equals(a[0].ToString(), BradleyDropsMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;
            var mod = BradleyDropsMod.Instance;
            if (mod == null) return true;
            try { mod.HandleCuiEndtest(args, a); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] cui.endtest BRADLEYDROPS: " + ex); }
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemContainer), "Insert", new[] { typeof(Item) })]
    public static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result || __instance == null || item == null) return;
            try { BD.Dispatch_OnItemAddedToContainer(__instance, item); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnItemAddedToContainer: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ThrownWeapon), "SetUpThrownWeapon", new[] { typeof(BaseEntity), typeof(Item) })]
    public static class ThrownWeapon_SetUpThrownWeapon_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, BaseEntity ent, Item ownerItem)
        {
            if (__instance == null || ent == null) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try { BD.Dispatch_OnExplosiveThrown(player, ent, __instance, ownerItem); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnExplosiveThrown: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ThrownWeapon), nameof(ThrownWeapon.DoThrowImpl))]
    public static class ThrownWeapon_DoThrowImpl_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, BasePlayer owningPlayer, BaseEntity thrownEntity)
        {
            if (__instance == null || owningPlayer == null || thrownEntity == null) return;
            try { BD.Dispatch_OnExplosiveThrown(owningPlayer, thrownEntity, __instance, __instance.GetItem()); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnExplosiveThrown DoThrowImpl: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SupplySignal), nameof(SupplySignal.Explode))]
    public static class SupplySignal_Explode_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(SupplySignal __instance)
        {
            if (__instance == null) return true;
            try
            {
                ulong skin = __instance.skinID;
                if (skin == 0 || !BD.Dispatch_IsBradleyDropSkin(skin)) return true;
                var player = __instance.creatorEntity as BasePlayer;
                if (player != null)
                    BD.Dispatch_OnExplosiveThrown(player, __instance, null, null);
                return false;
            }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] SupplySignal.Explode: " + ex.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance == null || __instance is BasePlayer) return;
            try { BD.Dispatch_OnEntitySpawned(__instance); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnEntitySpawned: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (info?.InitiatorPlayer != null)
            {
                object attack = BD.Dispatch_OnPlayerAttack(info.InitiatorPlayer, info);
                if (attack != null) return false;
            }
            return BD.Dispatch_OnEntityTakeDamage(__instance, info) == null;
        }
    }

    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.OnAttacked))]
    public static class BradleyAPC_OnAttacked_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BradleyAPC __instance, HitInfo info)
        {
            if (__instance == null || info == null) return;
            try { BD.Dispatch_OnBradleyAttacked(__instance, info); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnBradleyAttacked: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            try { BD.Dispatch_OnEntityKill(__instance); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnEntityDestroy: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            if (__instance == null || targetEntity is not LockedByEntCrate crate) return true;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return true;
            if (BD.Dispatch_CanLootEntity(player, crate) != null) { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.SpawnLoot))]
    public static class LootContainer_SpawnLoot_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LootContainer __instance)
        {
            try { BD.Dispatch_OnLootSpawn(__instance); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnLootSpawn: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.Initialize))]
    public static class BradleyAPC_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BradleyAPC __instance)
        {
            if (__instance == null) return;
            try { BD.Dispatch_OnBradleyApcInitialize(__instance); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnBradleyApcInitialize: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BradleyAPC), "CanDeployScientists")]
    public static class BradleyAPC_CanDeployScientists_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BradleyAPC __instance, BaseEntity attacker, List<GameObjectRef> scientistPrefabs, List<Vector3> spawnPositions, ref bool __result)
        {
            var player = attacker as BasePlayer;
            object r = BD.Dispatch_CanDeployScientists(__instance, player, scientistPrefabs, spawnPositions);
            if (r == null) return true;
            __result = r is bool b && b;
            return false;
        }
    }

    [HarmonyPatch(typeof(HackableLockedCrate), nameof(HackableLockedCrate.StartHacking))]
    public static class HackableLockedCrate_StartHacking_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(HackableLockedCrate __instance)
        {
            return BD.Dispatch_OnCrateHack(__instance) == null;
        }
    }
}
