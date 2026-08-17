using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Oxide.Plugins
{
    public partial class BradleyDrops
    {
        internal static void SetInstance(BradleyDrops inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static BradleyDrops GetModInstance() => Instance;

        public void CallInit()
        {
            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] LoadDefaultMessages failed: " + ex.Message); }
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] Init failed: " + ex.Message); }
            try { OverlayLanguageFile(); } catch { }
        }

        public void CallOnServerInitialized()
        {
            try { ResolvePluginReferences(); } catch { }
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[BradleyDrops] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] Unload failed: " + ex.Message); }
        }

        public static void Dispatch_OnItemAddedToContainer(ItemContainer container, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemAddedToContainer))) return;
            try { inst.OnItemAddedToContainer(container, item); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnItemAddedToContainer: " + ex.Message); }
        }

        public static object Dispatch_OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon item, Item ownerItem = null)
        {
            var inst = Instance;
            if (inst == null || entity is not SupplySignal signal) return null;
            if (!inst.IsSubscribed(nameof(OnExplosiveThrown))) return null;
            try { return inst.OnExplosiveThrown(player, signal, item, ownerItem); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnExplosiveThrown: " + ex.Message); return null; }
        }

        public static bool Dispatch_IsBradleyDropSkin(ulong skinId)
        {
            var inst = Instance;
            if (inst == null) return false;
            try { return inst.IsBradleyDropSkin(skinId); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] IsBradleyDropSkin: " + ex.Message); return false; }
        }

        public static object Dispatch_CanStackItem(Item item, Item targetItem)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanStackItem))) return null;
            try { return inst.CanStackItem(item, targetItem); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] CanStackItem: " + ex.Message); return null; }
        }

        public static object Dispatch_CanCombineDroppedItem(DroppedItem droppedItem, DroppedItem targetItem)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanCombineDroppedItem))) return null;
            try { return inst.CanCombineDroppedItem(droppedItem, targetItem); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] CanCombineDroppedItem: " + ex.Message); return null; }
        }

        public static void Dispatch_OnEntitySpawned(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntitySpawned))) return;
            try { inst.OnEntitySpawned(entity); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnEntitySpawned: " + ex.Message); }
        }

        public static object Dispatch_OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityTakeDamage))) return null;
            try
            {
                if (entity is BradleyAPC bradley)
                {
                    inst.TrySendAttackDifficulty(bradley, info);
                    return inst.OnEntityTakeDamage(bradley, info);
                }
                if (entity is CH47Helicopter ch47) return inst.OnEntityTakeDamage(ch47, info);
                if (entity is ScientistNPC npc) return inst.OnEntityTakeDamage(npc, info);
                if (entity is BasePlayer player) return inst.OnEntityTakeDamage(player, info);
                return inst.OnEntityTakeDamage(entity, info);
            }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnEntityTakeDamage: " + ex.Message); return null; }
        }

        public static void Dispatch_OnBradleyAttacked(BradleyAPC bradley, HitInfo info)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.TrySendAttackDifficulty(bradley, info); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnBradleyAttacked: " + ex.Message); }
        }

        public static object Dispatch_OnTurretTarget(AutoTurret turret, BaseCombatEntity target)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTurretTarget))) return null;
            try
            {
                if (target is BradleyAPC b) return inst.OnTurretTarget(turret, b);
                if (target is ScientistNPC n) return inst.OnTurretTarget(turret, n);
            }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnTurretTarget: " + ex.Message); }
            return null;
        }

        public static object Dispatch_OnEntityKill(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityDestroy))) return null;
            try { return inst.OnEntityDestroy(entity); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnEntityDestroy: " + ex.Message); return null; }
        }

        public static object Dispatch_CanLootEntity(BasePlayer player, LockedByEntCrate entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanLootEntity))) return null;
            try { return inst.CanLootEntity(player, entity); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] CanLootEntity: " + ex.Message); return null; }
        }

        public static object Dispatch_OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerAttack))) return null;
            try { return inst.OnPlayerAttack(attacker, info); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnPlayerAttack: " + ex.Message); return null; }
        }

        public static object Dispatch_OnLootSpawn(LootContainer lootContainer)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootSpawn))) return null;
            try { return inst.OnLootSpawn(lootContainer); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnLootSpawn: " + ex.Message); return null; }
        }

        public static object Dispatch_CanBradleyApcTarget(BradleyAPC bradley, BaseEntity target)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanBradleyApcTarget))) return null;
            try { return inst.CanBradleyApcTarget(bradley, target); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] CanBradleyApcTarget: " + ex.Message); return null; }
        }

        public static object Dispatch_OnBradleyApcInitialize(BradleyAPC bradley)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnBradleyApcInitialize))) return null;
            try { return inst.OnBradleyApcInitialize(bradley); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnBradleyApcInitialize: " + ex.Message); return null; }
        }

        public static object Dispatch_CanDeployScientists(BradleyAPC bradley, BasePlayer attacker, List<GameObjectRef> scientistPrefabs, List<Vector3> spawnPositions)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanDeployScientists))) return null;
            try { return inst.CanDeployScientists(bradley, attacker, scientistPrefabs, spawnPositions); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] CanDeployScientists: " + ex.Message); return null; }
        }

        public static void Dispatch_OnScientistInitialized(BradleyAPC bradley, ScientistNPC npc, Vector3 spawnPos)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnScientistInitialized))) return;
            try { inst.OnScientistInitialized(bradley, npc, spawnPos); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnScientistInitialized: " + ex.Message); }
        }

        public static object Dispatch_OnNpcTarget(NPCPlayer npc, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnNpcTarget))) return null;
            try { return inst.OnNpcTarget(npc, player); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnNpcTarget: " + ex.Message); return null; }
        }

        public static object Dispatch_CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanHackCrate))) return null;
            try { return inst.CanHackCrate(player, crate); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] CanHackCrate: " + ex.Message); return null; }
        }

        public static object Dispatch_OnCrateHack(HackableLockedCrate crate)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCrateHack))) return null;
            try { return inst.OnCrateHack(crate); }
            catch (Exception ex) { Debug.LogWarning("[BradleyDrops] OnCrateHack: " + ex.Message); return null; }
        }
    }
}
