// TruePVEDispatch.cs -- partial class Oxide.Plugins.TruePVE
// Provides:
//   - static instance accessors (SetInstance/GetModInstance/ClearInstance)
//   - lifecycle wrappers (CallInit/CallOnServerInitialized/CallUnload)
//   - ResolvePluginReferences (soft PluginReference binding)
//   - public Dispatch_* methods invoked by Harmony patch files
// Same partial class as TruePVEPlugin.cs, so all private members are accessible.
// Convention: object-returning hooks return null = allow / not handled;
//             non-null = cancel (patch Prefix returns false).

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Rust.Ai.Gen2;

namespace Oxide.Plugins
{
    public partial class TruePVE
    {
        // ---- Instance management ------------------------------------------
        // TruePVEPlugin.cs declares: private static TruePVE Instance { get; set; }
        internal static void SetInstance(TruePVE inst) => Instance = inst;
        internal static void ClearInstance()          => Instance = null;
        internal static TruePVE GetModInstance()      => Instance;

        // ---- Lifecycle wrappers -------------------------------------------

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] Init failed: " + ex); }
        }

        public void CallOnServerInitialized(bool initial = true)
        {
            try { OnServerInitialized(initial); }
            catch (Exception ex) { Debug.LogError("[TruePVE] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] Unload failed: " + ex); }
        }

        /// <summary>
        /// Best-effort binding of [PluginReference] fields via PluginManager.Find (AppDomain *_ApiType / *_Plugin).
        /// Harmony first-class: Permissions, Economics, RustRewards, SkillTree, RaidableBases.
        /// </summary>
        public void ResolvePluginReferences()
        {
            string[] names =
            {
                "AbandonedBases", "BradleyDrops", "Clans", "Convoy", "CustomHelicopterTiers2",
                "DynamicPVP", "Economics", "Friends", "HeliSignals", "HelpfulSupply", "LiteZones",
                "NpcRandomRaids", "Permissions", "PersonalHeli", "RaidableBases", "RustRewards",
                "ShoppyStock", "SkillTree", "XLevels", "XPerience", "ZoneManager"
            };
            var linked = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                var field = GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) continue;
                try
                {
                    var found = plugins?.Find(name);
                    if (found != null)
                    {
                        field.SetValue(this, found);
                        linked.Add(name);
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[TruePVE] ResolvePluginReferences(" + name + "): " + ex.Message); }
            }
            if (linked.Count > 0)
                Debug.Log("[TruePVE] Linked Harmony plugins: " + string.Join(", ", linked));
        }

        // ---- Hook subscription helper -------------------------------------
        public static bool IsHookSubscribed(string hookName)
        {
            var inst = Instance;
            return inst != null && inst.IsSubscribed(hookName);
        }

        private static void Warn(string hook, Exception ex) => Debug.LogWarning("[TruePVE] " + hook + ": " + ex.Message);

        // ---- Damage / Death -----------------------------------------------

        public static object Dispatch_OnEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || entity == null || !inst.IsSubscribed(nameof(OnEntityTakeDamage))) return null;
            try
            {
                if (entity is ResourceEntity re) return inst.OnEntityTakeDamage(re, info);
                return inst.OnEntityTakeDamage(entity, info);
            }
            catch (Exception ex) { Warn(nameof(OnEntityTakeDamage), ex); return null; }
        }

        public static void Dispatch_OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || entity == null) return;
            try
            {
                switch (entity)
                {
                    case PatrolHelicopter heli: inst.OnEntityDeath(heli, info); break;
                    case BradleyAPC apc:        inst.OnEntityDeath(apc, info); break;
                    case BaseNpc npc:           inst.OnEntityDeath(npc, info); break;
                    case BaseNPC2 npc2:         inst.OnEntityDeath(npc2, info); break;
                }
            }
            catch (Exception ex) { Warn(nameof(OnEntityDeath), ex); }
        }

        public static object Dispatch_OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || attacker == null || !inst.IsSubscribed(nameof(OnPlayerAttack))) return null;
            try { return inst.OnPlayerAttack(attacker, info); }
            catch (Exception ex) { Warn(nameof(OnPlayerAttack), ex); return null; }
        }

        // ---- Loot ---------------------------------------------------------

        public static object Dispatch_CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || player == null || entity == null || !inst.IsSubscribed(nameof(CanLootEntity))) return null;
            try
            {
                switch (entity)
                {
                    case BuildingPrivlidge priv:   return inst.CanLootEntity(player, priv);
                    case LootableCorpse corpse:    return inst.CanLootEntity(player, corpse);
                    case DroppedItemContainer dic: return inst.CanLootEntity(player, dic);
                    case ModularCarGarage garage:  return inst.CanLootEntity(player, garage);
                    case StorageContainer sc:      return inst.CanLootEntity(player, sc);
                    default:                       return inst.CanLootEntity(player, entity);
                }
            }
            catch (Exception ex) { Warn(nameof(CanLootEntity), ex); return null; }
        }

        public static object Dispatch_CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            var inst = Instance;
            if (inst == null || target == null || looter == null || !inst.IsSubscribed(nameof(CanLootPlayer))) return null;
            try { return inst.CanLootPlayer(target, looter); }
            catch (Exception ex) { Warn(nameof(CanLootPlayer), ex); return null; }
        }

        public static void Dispatch_OnLootPlayer(BasePlayer target, BasePlayer looter)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootPlayer))) return;
            try { inst.OnLootPlayer(target, looter); }
            catch (Exception ex) { Warn(nameof(OnLootPlayer), ex); }
        }

        public static void Dispatch_OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntity))) return;
            try { inst.OnLootEntity(player, entity); }
            catch (Exception ex) { Warn(nameof(OnLootEntity), ex); }
        }

        public static object Dispatch_OnStartBeingLooted(DroppedItemContainer container, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnStartBeingLooted))) return null;
            try { return inst.OnStartBeingLooted(container, player); }
            catch (Exception ex) { Warn(nameof(OnStartBeingLooted), ex); return null; }
        }

        public static object Dispatch_CanPickupEntity(BasePlayer player, BaseCombatEntity ent)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanPickupEntity))) return null;
            try { return inst.CanPickupEntity(player, ent); }
            catch (Exception ex) { Warn(nameof(CanPickupEntity), ex); return null; }
        }

        public static object Dispatch_OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnOvenToggle))) return null;
            try { return inst.OnOvenToggle(oven, player); }
            catch (Exception ex) { Warn(nameof(OnOvenToggle), ex); return null; }
        }

        public static object Dispatch_OnItemPickup(Item item, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemPickup))) return null;
            try { return inst.OnItemPickup(item, player); }
            catch (Exception ex) { Warn(nameof(OnItemPickup), ex); return null; }
        }

        public static void Dispatch_OnItemDropped(Item item, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemDropped))) return;
            try { inst.OnItemDropped(item, entity); }
            catch (Exception ex) { Warn(nameof(OnItemDropped), ex); }
        }

        // ---- Targeting ----------------------------------------------------

        public static object Dispatch_OnTurretTarget(AutoTurret turret, BaseCombatEntity target)
        {
            var inst = Instance;
            if (inst == null || turret == null || target == null || !inst.IsSubscribed(nameof(OnTurretTarget))) return null;
            try
            {
                if (target is BasePlayer bp) return inst.OnTurretTarget(turret, bp);
                if (target is BradleyAPC apc) return inst.OnTurretTarget(turret, apc);
                return null;
            }
            catch (Exception ex) { Warn(nameof(OnTurretTarget), ex); return null; }
        }

        public static object Dispatch_OnSamSiteTarget(BaseEntity samsite, BaseEntity target)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnSamSiteTarget))) return null;
            try { return inst.OnSamSiteTarget(samsite, target); }
            catch (Exception ex) { Warn(nameof(OnSamSiteTarget), ex); return null; }
        }

        public static object Dispatch_OnTrapTrigger(BaseTrap trap, GameObject go)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTrapTrigger))) return null;
            try { return inst.OnTrapTrigger(trap, go); }
            catch (Exception ex) { Warn(nameof(OnTrapTrigger), ex); return null; }
        }

        public static object Dispatch_OnNpcTarget(BaseEntity npc, BasePlayer target)
        {
            var inst = Instance;
            if (inst == null || target == null || !inst.IsSubscribed(nameof(OnNpcTarget))) return null;
            try
            {
                if (npc is BaseNpc bn)  return inst.OnNpcTarget(bn, target);
                if (npc is BaseNPC2 b2) return inst.OnNpcTarget(b2, target);
                return null;
            }
            catch (Exception ex) { Warn(nameof(OnNpcTarget), ex); return null; }
        }

        public static object Dispatch_OnEntityEnter(TriggerBase trigger, BaseEntity target)
        {
            var inst = Instance;
            if (inst == null || trigger == null || target == null || !inst.IsSubscribed(nameof(OnEntityEnter))) return null;
            try
            {
                if (trigger is TargetTrigger tt && target is BasePlayer bp) return inst.OnEntityEnter(tt, bp);
                if (trigger is TriggerEnterTimer tet) return inst.OnEntityEnter(tet, target);
                return null;
            }
            catch (Exception ex) { Warn(nameof(OnEntityEnter), ex); return null; }
        }

        public static object Dispatch_CanHelicopterStrafeTarget(PatrolHelicopterAI ai, BasePlayer ply)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanHelicopterStrafeTarget))) return null;
            try { return inst.CanHelicopterStrafeTarget(ai, ply); }
            catch (Exception ex) { Warn(nameof(CanHelicopterStrafeTarget), ex); return null; }
        }

        public static object Dispatch_CanWaterBallSplash(ItemDefinition liquidDef, Vector3 position, float radius, int amount)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanWaterBallSplash))) return null;
            try { return inst.CanWaterBallSplash(liquidDef, position, radius, amount); }
            catch (Exception ex) { Warn(nameof(CanWaterBallSplash), ex); return null; }
        }

        // ---- Spawns / build ----------------------------------------------

        public static void Dispatch_OnEntitySpawned(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null || entity == null || !inst.IsSubscribed(nameof(OnEntitySpawned))) return;
            try
            {
                // Most-derived first: SupplyDrop/ContainerIOEntity/BaseOven derive from StorageContainer.
                switch (entity)
                {
                    case SupplyDrop drop:            inst.OnEntitySpawned(drop); break;
                    case CH47Helicopter heli:        inst.OnEntitySpawned(heli); break;
                    case MLRSRocket rocket:          inst.OnEntitySpawned(rocket); break;
                    case RidableHorse horse:         inst.OnEntitySpawned(horse); break;
                    case Door door:                  inst.OnEntitySpawned(door); break;
                    case BaseLock baseLock:          inst.OnEntitySpawned(baseLock); break;
                    case BaseOven oven:              inst.OnEntitySpawned(oven); break;
                    case ContainerIOEntity cio:      inst.OnEntitySpawned(cio); break;
                    case StorageContainer sc:        inst.OnEntitySpawned(sc); break;
                    case BaseEntity be:              inst.OnEntitySpawned(be); break;
                }
            }
            catch (Exception ex) { Warn(nameof(OnEntitySpawned), ex); }
        }

        public static void Dispatch_OnEntityBuilt(Planner plan, GameObject go)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityBuilt))) return;
            try { inst.OnEntityBuilt(plan, go); }
            catch (Exception ex) { Warn(nameof(OnEntityBuilt), ex); }
        }

        public static object Dispatch_CanChangeGrade(BasePlayer player, BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanChangeGrade))) return null;
            try { return inst.CanChangeGrade(player, block, grade, skin); }
            catch (Exception ex) { Warn(nameof(CanChangeGrade), ex); return null; }
        }

        public static object Dispatch_OnCupboardAuthorize(BuildingPrivlidge priv, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCupboardAuthorize))) return null;
            try { return inst.OnCupboardAuthorize(priv, player); }
            catch (Exception ex) { Warn(nameof(OnCupboardAuthorize), ex); return null; }
        }

        public static object Dispatch_OnCodeEntered(CodeLock codeLock, BasePlayer player, string code)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCodeEntered))) return null;
            try { return inst.OnCodeEntered(codeLock, player, code); }
            catch (Exception ex) { Warn(nameof(OnCodeEntered), ex); return null; }
        }

        public static object Dispatch_OnMlrsFire(MLRS mlrs, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnMlrsFire))) return null;
            try { return inst.OnMlrsFire(mlrs, player); }
            catch (Exception ex) { Warn(nameof(OnMlrsFire), ex); return null; }
        }

        public static object Dispatch_OnWallpaperRemove(BuildingBlock block, int side)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnWallpaperRemove))) return null;
            try { return inst.OnWallpaperRemove(block, side); }
            catch (Exception ex) { Warn(nameof(OnWallpaperRemove), ex); return null; }
        }

        public static void Dispatch_OnTimedExplosiveExplode(TimedExplosive explosive, Vector3 pos)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTimedExplosiveExplode))) return;
            try { inst.OnTimedExplosiveExplode(explosive, pos); }
            catch (Exception ex) { Warn(nameof(OnTimedExplosiveExplode), ex); }
        }

        public static object Dispatch_OnEntityMarkHostile(BasePlayer player, float duration)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityMarkHostile))) return null;
            try { return inst.OnEntityMarkHostile(player, duration); }
            catch (Exception ex) { Warn(nameof(OnEntityMarkHostile), ex); return null; }
        }

        public static object Dispatch_OnSprayCreate(SprayCan sc, Vector3 pos, Quaternion rot)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnSprayCreate))) return null;
            try { return inst.OnSprayCreate(sc, pos, rot); }
            catch (Exception ex) { Warn(nameof(OnSprayCreate), ex); return null; }
        }

        // ---- Player lifecycle --------------------------------------------

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerConnected))) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Warn(nameof(OnPlayerConnected), ex); }
        }

        public static void Dispatch_OnPlayerDisconnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDisconnected))) return;
            try { inst.OnPlayerDisconnected(player); }
            catch (Exception ex) { Warn(nameof(OnPlayerDisconnected), ex); }
        }

        public static void Dispatch_OnPlayerSleep(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerSleep))) return;
            try { inst.OnPlayerSleep(player); }
            catch (Exception ex) { Warn(nameof(OnPlayerSleep), ex); }
        }

        public static void Dispatch_OnPlayerSleepEnded(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerSleepEnded))) return;
            try { inst.OnPlayerSleepEnded(player); }
            catch (Exception ex) { Warn(nameof(OnPlayerSleepEnded), ex); }
        }

        // ---- Save ---------------------------------------------------------

        public static void Dispatch_OnServerSave()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnServerSave))) return;
            try { inst.OnServerSave(); }
            catch (Exception ex) { Warn(nameof(OnServerSave), ex); }
        }

        public static void Dispatch_OnNewSave(string filename)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnNewSave(); }
            catch (Exception ex) { Warn(nameof(OnNewSave), ex); }
        }

        // ---- Supply drop / explosives ------------------------------------

        public static void Dispatch_OnExplosiveThrown(BasePlayer player, SupplySignal ss, ThrownWeapon tw)
        {
            var inst = Instance;
            if (inst == null || player == null || ss == null) return;
            try { inst.OnExplosiveThrown(player, ss, tw); }
            catch (Exception ex) { Warn(nameof(OnExplosiveThrown), ex); }
        }

        public static void Dispatch_OnExplosiveDroppedTimed(BasePlayer player, TimedExplosive te, ThrownWeapon tw)
        {
            var inst = Instance;
            if (inst == null || player == null || te == null) return;
            try { inst.OnExplosiveDropped(player, te, tw); }
            catch (Exception ex) { Warn(nameof(OnExplosiveDropped), ex); }
        }

        public static void Dispatch_OnCargoPlaneSignaled(CargoPlane plane, SupplySignal ss)
        {
            var inst = Instance;
            if (inst == null || plane == null || ss == null) return;
            try { inst.OnCargoPlaneSignaled(plane, ss); }
            catch (Exception ex) { Warn(nameof(OnCargoPlaneSignaled), ex); }
        }

        /// <summary>
        /// When TruePVE is actively handling damage with the browser-tag hybrid, vanilla
        /// BasePlayer/BuildingBlock PVE early-reflect must be suppressed so RuleSets remain authoritative
        /// while ConVar.Server.pve stays true for Steam listing.
        /// </summary>
        public static bool ShouldSuppressVanillaPve()
        {
            var inst = Instance;
            if (inst?.config?.options == null) return false;
            if (!inst.config.options.handleDamage) return false;
            if (!inst.config.options.UseGamePveBrowserTag) return false;
            if (!inst.IsSubscribed(nameof(OnEntityTakeDamage))) return false;
            return inst.IsEnabled();
        }
    }
}
