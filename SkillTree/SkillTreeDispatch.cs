// SkillTreeDispatch.cs  --  partial class Oxide.Plugins.SkillTree
// Provides:
//   - static Instance property and lifecycle wrappers (CallInit, CallLoaded, etc.)
//   - public Dispatch_* methods called by Harmony patch files
//   - IsHookSubscribed helper
// Because this is the same partial class as SkillTreePlugin.cs, all private
// members of the plugin body are accessible here with no visibility changes.

using System;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class SkillTree
    {
        // ---- Instance management ------------------------------------------
        // NOTE: SkillTreePlugin.cs declares:  private static SkillTree Instance { get; set; }
        // We reuse that property here (same partial class) via SetInstance/ClearInstance.
        // External callers use GetModInstance() to avoid private-access restrictions.

        internal static void SetInstance(SkillTree inst) => Instance = inst;
        internal static void ClearInstance()             => Instance = null;

        /// <summary>Exposed so SkillTreeMod (different namespace) can read the instance.</summary>
        internal static SkillTree GetModInstance() => Instance;

        // ---- Lifecycle wrappers -------------------------------------------

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] Init failed: " + ex.Message); }
        }

        public void CallLoaded()
        {
            try { Loaded(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] Loaded failed: " + ex.Message); }
        }

        public void CallOnServerInitialized(bool initial = true)
        {
            try { OnServerInitialized(initial); }
            catch (Exception ex) { Debug.LogError("[SkillTree] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] Unload failed: " + ex.Message); }
        }

        /// <summary>
        /// Assigns [PluginReference] fields from PluginManager.Find (ImageLibrary stub, Economics, etc.).
        /// Safe to call multiple times; missing plugins stay null (plugin code null-checks most refs).
        /// </summary>
        public void ResolvePluginReferences()
        {
            // Explicit list matches SkillTreePlugin [PluginReference] declaration.
            string[] names =
            {
                "ImageLibrary", "Economics", "ServerRewards", "ShoppyStock", "EventManager", "BotReSpawn",
                "Cooking", "UINotify", "ZombieHorde", "EventHelper", "RaidableBases", "LootDefender",
                "SkillTreeXPEvent", "ZoneManager", "VirtualRecycler", "DeployableNature",
                "NotificationSystem", "MovementSpeed"
            };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                var field = GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) continue;
                try
                {
                    var found = plugins?.Find(name);
                    if (found != null)
                        field.SetValue(this, found);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[SkillTree] ResolvePluginReferences(" + name + "): " + ex.Message);
                }
            }
        }

        // ---- Hook subscription helper -------------------------------------

        /// <summary>
        /// Returns true when the hook should be dispatched.
        /// Default (no explicit Subscribe/Unsubscribe): true.
        /// Returns false only when the plugin explicitly called Unsubscribe(hookName).
        /// </summary>
        public static bool IsHookSubscribed(string hookName)
        {
            var inst = Instance;
            return inst == null || inst.IsSubscribed(hookName);
        }

        // ---- Dispatch helpers (called by Harmony patches) -----------------
        //
        // Convention:
        //   - void hooks:   return void, catch + log internally.
        //   - object hooks: return object (null = allow / not handled).
        //
        // All guards:
        //   1. Instance null check.
        //   2. IsSubscribed(hookName) check (respects Unsubscribe calls).
        //   3. try/catch with warning log.

        // ---- Damage / Death -----------------------------------------------

        public static object Dispatch_OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityTakeDamage))) return null;
            try { return inst.OnEntityTakeDamage(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityTakeDamage: " + ex.Message); return null; }
        }

        public static void Dispatch_OnEntityDeath(BaseEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityDeath))) return;
            try
            {
                // Route to typed overloads that the plugin declares.
                if (entity is BaseEntity be)
                    inst.OnEntityDeath(be, info);
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityDeath: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDeath))) return;
            try { inst.OnPlayerDeath(player, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerDeath(BasePlayer): " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDeathNpc(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDeath))) return;
            try
            {
                switch (entity)
                {
                    case ScarecrowNPC   sc:  inst.OnPlayerDeath(sc,  info); break;
                    case GingerbreadNPC gb:  inst.OnPlayerDeath(gb,  info); break;
                    case ScientistNPC   sn:  inst.OnPlayerDeath(sn,  info); break;
                    case TunnelDweller  td:  inst.OnPlayerDeath(td,  info); break;
                    case UnderwaterDweller ud: inst.OnPlayerDeath(ud, info); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerDeath(NPC): " + ex.Message); }
        }

        // ---- Player lifecycle --------------------------------------------

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerConnected))) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerConnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDisconnected(BasePlayer player, string reason = "")
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDisconnected))) return;
            try { inst.OnPlayerDisconnected(player, reason ?? ""); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerDisconnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerRespawned(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerRespawned))) return;
            try { inst.OnPlayerRespawned(player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerRespawned: " + ex.Message); }
        }

        // ---- Gather / collect -------------------------------------------

        public static object Dispatch_OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnDispenserGather))) return null;
            try { return inst.OnDispenserGather(dispenser, player, item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnDispenserGather: " + ex.Message); return null; }
        }

        public static object Dispatch_OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnDispenserBonus))) return null;
            try { return inst.OnDispenserBonus(dispenser, player, item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnDispenserBonus: " + ex.Message); return null; }
        }

        public static void Dispatch_OnCollectiblePickup(CollectibleEntity entity, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCollectiblePickup))) return;
            try { inst.OnCollectiblePickup(entity, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnCollectiblePickup: " + ex.Message); }
        }

        public static void Dispatch_OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnGrowableGathered))) return;
            try { inst.OnGrowableGathered(plant, item, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnGrowableGathered: " + ex.Message); }
        }

        public static object Dispatch_CanTakeCutting(BasePlayer player, GrowableEntity plant)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanTakeCutting))) return null;
            try { return inst.CanTakeCutting(player, plant); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanTakeCutting: " + ex.Message); return null; }
        }

        // ---- Crafting ---------------------------------------------------

        public static object Dispatch_OnItemCraft(ItemCraftTask task, BasePlayer player, Item fromTempBlueprint)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemCraft))) return null;
            try { return inst.OnItemCraft(task, player, fromTempBlueprint); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraft: " + ex.Message); return null; }
        }

        public static void Dispatch_OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemCraftFinished))) return;
            try { inst.OnItemCraftFinished(task, item, crafter); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraftFinished: " + ex.Message); }
        }

        public static void Dispatch_OnItemCraftCancelled(ItemCraftTask task)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemCraftCancelled))) return;
            try { inst.OnItemCraftCancelled(task); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraftCancelled: " + ex.Message); }
        }

        // ---- Building / repair / condition --------------------------------

        public static void Dispatch_OnEntityBuilt(Planner plan, GameObject go)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityBuilt))) return;
            try { inst.OnEntityBuilt(plan, go); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityBuilt: " + ex.Message); }
        }

        public static void Dispatch_OnLoseCondition(Item item, ref float amount)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLoseCondition))) return;
            try { inst.OnLoseCondition(item, ref amount); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnLoseCondition: " + ex.Message); }
        }

        public static object Dispatch_OnItemRepair(BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemRepair))) return null;
            try { return inst.OnItemRepair(player, item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemRepair: " + ex.Message); return null; }
        }

        public static object Dispatch_OnPayForUpgrade(BasePlayer player, BuildingBlock block, ConstructionGrade grade)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPayForUpgrade))) return null;
            try { return inst.OnPayForUpgrade(player, block, grade); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPayForUpgrade: " + ex.Message); return null; }
        }

        public static object Dispatch_OnResearchCostDetermine(Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnResearchCostDetermine))) return null;
            try { return inst.OnResearchCostDetermine(item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnResearchCostDetermine: " + ex.Message); return null; }
        }

        // ---- Weapon ------------------------------------------------------

        public static void Dispatch_OnWeaponFired(BaseProjectile proj, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot shoot)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnWeaponFired))) return;
            try { inst.OnWeaponFired(proj, player, mod, shoot); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponFired: " + ex.Message); }
        }

        public static object Dispatch_OnWeaponReload(BaseProjectile weapon, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnWeaponReload))) return null;
            try { return inst.OnWeaponReload(weapon, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponReload: " + ex.Message); return null; }
        }

        public static object Dispatch_OnWeaponModChange(BaseProjectile weapon, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnWeaponModChange))) return null;
            try { return inst.OnWeaponModChange(weapon, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnWeaponModChange: " + ex.Message); return null; }
        }

        // ---- Mount -------------------------------------------------------

        public static void Dispatch_OnEntityMounted(BaseMountable entity, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityMounted))) return;
            try { inst.OnEntityMounted(entity, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityMounted: " + ex.Message); }
        }

        public static void Dispatch_OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityDismounted))) return;
            try { inst.OnEntityDismounted(entity, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityDismounted: " + ex.Message); }
        }

        // ---- Loot -------------------------------------------------------

        public static void Dispatch_OnLootEntity(BasePlayer player, LootContainer entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntity))) return;
            try { inst.OnLootEntity(player, entity); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnLootEntity: " + ex.Message); }
        }

        public static void Dispatch_OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntityEnd))) return;
            try { inst.OnLootEntityEnd(player, container); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnLootEntityEnd: " + ex.Message); }
        }

        public static void Dispatch_CanLootEntity(BasePlayer player, LootContainer container)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanLootEntity))) return;
            try { inst.CanLootEntity(player, container); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanLootEntity: " + ex.Message); }
        }

        public static object Dispatch_CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanUseLockedEntity))) return null;
            try { return inst.CanUseLockedEntity(player, baseLock); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanUseLockedEntity: " + ex.Message); return null; }
        }

        // ---- Recycler ---------------------------------------------------

        public static void Dispatch_OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            // OnRecyclerToggle is private - call via reflection.
            try
            {
                var mi = typeof(SkillTree).GetMethod("OnRecyclerToggle",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                mi?.Invoke(inst, new object[] { recycler, player });
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnRecyclerToggle: " + ex.Message); }
        }

        // ---- Fuel / oven ------------------------------------------------

        public static void Dispatch_OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnFuelConsume))) return;
            try { inst.OnFuelConsume(oven, fuel, burnable); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnFuelConsume: " + ex.Message); }
        }

        public static void Dispatch_OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnOvenToggle))) return;
            try { inst.OnOvenToggle(oven, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnOvenToggle: " + ex.Message); }
        }

        // ---- Fishing ----------------------------------------------------

        public static void Dispatch_OnFishCatch(Item item, BaseFishingRod rod, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnFishCatch))) return;
            try { inst.OnFishCatch(item, rod, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnFishCatch: " + ex.Message); }
        }

        public static void Dispatch_CanCatchFish(BasePlayer player, BaseFishingRod rod, Item fish)
        {
            var inst = Instance;
            // CanCatchFish is void in this plugin - just call it.
            if (inst == null) return;
            try { inst.CanCatchFish(player, rod, fish); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] CanCatchFish: " + ex.Message); }
        }

        public static void Dispatch_OnFishingStopped(BaseFishingRod rod, BaseFishingRod.FailReason reason)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnFishingStopped))) return;
            try { inst.OnFishingStopped(rod, reason); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnFishingStopped: " + ex.Message); }
        }

        // ---- Melee / hammer ---------------------------------------------

        public static void Dispatch_OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnMeleeAttack))) return;
            try { inst.OnMeleeAttack(player, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMeleeAttack: " + ex.Message); }
        }

        public static object Dispatch_OnHammerHit(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnHammerHit))) return null;
            try { return inst.OnHammerHit(player, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnHammerHit: " + ex.Message); return null; }
        }

        // ---- Health / revival -------------------------------------------

        public static object Dispatch_OnHealingItemUse(MedicalTool tool, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnHealingItemUse))) return null;
            try { return inst.OnHealingItemUse(tool, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnHealingItemUse: " + ex.Message); return null; }
        }

        public static void Dispatch_OnPlayerHealthChange(BasePlayer player, float oldVal, float newVal)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerHealthChange))) return;
            try { inst.OnPlayerHealthChange(player, oldVal, newVal); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerHealthChange: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerRevive(BasePlayer reviver, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerRevive))) return;
            try { inst.OnPlayerRevive(reviver, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerRevive: " + ex.Message); }
        }

        public static object Dispatch_OnPlayerAddModifiers(BasePlayer player, Item item, ItemModConsumable consumable)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerAddModifiers))) return null;
            try { return inst.OnPlayerAddModifiers(player, item, consumable); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerAddModifiers: " + ex.Message); return null; }
        }

        public static object Dispatch_OnPlayerWound(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerWound))) return null;
            try { return inst.OnPlayerWound(player, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerWound: " + ex.Message); return null; }
        }

        // ---- Active item / input ----------------------------------------

        public static void Dispatch_OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnActiveItemChanged))) return;
            try { inst.OnActiveItemChanged(player, oldItem, newItem); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnActiveItemChanged: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerInput(BasePlayer player, InputState input)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerInput))) return;
            try { inst.OnPlayerInput(player, input); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerInput: " + ex.Message); }
        }

        // ---- Explosive --------------------------------------------------

        public static void Dispatch_OnExplosiveThrown(BasePlayer player, TimedExplosive explosive, ThrownWeapon weapon)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnExplosiveThrown))) return;
            try { inst.OnExplosiveThrown(player, explosive, weapon); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnExplosiveThrown: " + ex.Message); }
        }

        public static void Dispatch_OnExplosiveDropped(BasePlayer player, TimedExplosive explosive, ThrownWeapon weapon)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnExplosiveDropped))) return;
            try { inst.OnExplosiveDropped(player, explosive, weapon); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnExplosiveDropped: " + ex.Message); }
        }

        public static void Dispatch_OnRocketLaunched(BasePlayer player, TimedExplosive entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnRocketLaunched))) return;
            try { inst.OnRocketLaunched(player, entity); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnRocketLaunched: " + ex.Message); }
        }

        public static void Dispatch_OnTimedExplosiveExplode(TimedExplosive explosive)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTimedExplosiveExplode))) return;
            try { inst.OnTimedExplosiveExplode(explosive); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnTimedExplosiveExplode: " + ex.Message); }
        }

        // ---- Entity spawn / kill / save ----------------------------------

        public static void Dispatch_OnEntitySpawned(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntitySpawned))) return;
            try { inst.OnEntitySpawned(entity); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntitySpawned: " + ex.Message); }
        }

        public static void Dispatch_OnEntityKill_StorageContainer(StorageContainer c)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityKill(c); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityKill(Storage): " + ex.Message); }
        }

        public static void Dispatch_OnEntityKill_CollectibleEntity(CollectibleEntity c)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityKill(c); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityKill(Collectible): " + ex.Message); }
        }

        public static void Dispatch_OnEntityKill_Workbench(Workbench wb)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityKill(wb); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityKill(Workbench): " + ex.Message); }
        }

        public static void Dispatch_OnEntityKill_DudTimedExplosive(DudTimedExplosive dud)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityKill(dud); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityKill(DudExplosive): " + ex.Message); }
        }

        public static void Dispatch_OnServerSave()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnServerSave))) return;
            try { inst.OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnServerSave: " + ex.Message); }
        }

        public static void Dispatch_OnNewSave(string filename)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnNewSave))) return;
            try { inst.OnNewSave(filename ?? ""); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnNewSave: " + ex.Message); }
        }

        // ---- Misc -------------------------------------------------------

        public static void Dispatch_OnMixingTableToggle(MixingTable mt, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnMixingTableToggle))) return;
            try { inst.OnMixingTableToggle(mt, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMixingTableToggle: " + ex.Message); }
        }

        public static void Dispatch_OnMetalDetectorFlagRequest(BaseMetalDetector detector, Vector3 pos, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnMetalDetectorFlagRequest))) return;
            try { inst.OnMetalDetectorFlagRequest(detector, pos, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMetalDetectorFlagRequest: " + ex.Message); }
        }

        public static object Dispatch_OnTreeMarkerHit(TreeEntity tree, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTreeMarkerHit))) return null;
            try { return inst.OnTreeMarkerHit(tree, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnTreeMarkerHit: " + ex.Message); return null; }
        }

        public static object Dispatch_OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerViolation))) return null;
            try { return inst.OnPlayerViolation(player, type, amount); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerViolation: " + ex.Message); return null; }
        }

        public static object Dispatch_OnNpcTarget(ScientistNPC npc, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnNpcTarget))) return null;
            try { return inst.OnNpcTarget(npc, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnNpcTarget: " + ex.Message); return null; }
        }

        public static void Dispatch_OnMissionSucceeded(BaseMission mission, BaseMission.MissionInstance missionInstance, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnMissionSucceeded))) return;
            try { inst.OnMissionSucceeded(mission, missionInstance, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMissionSucceeded: " + ex.Message); }
        }

        public static object Dispatch_OnItemAction(Item item, string action, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemAction))) return null;
            try { return inst.OnItemAction(item, action, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemAction: " + ex.Message); return null; }
        }

        public static void Dispatch_OnItemAddedToContainer(ItemContainer container, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemAddedToContainer))) return;
            try { inst.OnItemAddedToContainer(container, item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemAddedToContainer: " + ex.Message); }
        }

        public static void Dispatch_OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemRemovedFromContainer))) return;
            try { inst.OnItemRemovedFromContainer(container, item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemRemovedFromContainer: " + ex.Message); }
        }

        public static object Dispatch_OnCardSwipe(CardReader reader, Keycard card, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCardSwipe))) return null;
            try
            {
                // OnCardSwipe is private - call via reflection.
                var mi = typeof(SkillTree).GetMethod("OnCardSwipe",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return mi?.Invoke(inst, new object[] { reader, card, player });
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnCardSwipe: " + ex.Message); return null; }
        }

        // ---- Zone integration (called from ZoneManager mod events) -------

        public static void Dispatch_OnEnterZone(string zoneId, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEnterZone(zoneId, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEnterZone: " + ex.Message); }
        }

        public static void Dispatch_OnExitZone(string zoneId, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnExitZone(zoneId, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnExitZone: " + ex.Message); }
        }
    }
}
