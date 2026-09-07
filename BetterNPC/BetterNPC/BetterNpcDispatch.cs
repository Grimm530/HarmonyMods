using System;
using System.Collections.Generic;
using Harmony.Core.Plugins;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Harmony.Plugins
{
    /// <summary>
    /// Harmony glue for the ported BetterNpc plugin: compat-style lifecycle + hook dispatchers
    /// for Harmony patches. Dispatchers early-out when _ins is null, replacing Oxide Subscribe/Unsubscribe.
    /// </summary>
    public partial class BetterNpc
    {
        public void CallInit() => Init();

        public void CallOnServerInitialized()
        {
            BindOptionalPlugins();
            OnServerInitialized();
        }

        public void CallUnload() => Unload();

        private void BindOptionalPlugins()
        {
            NpcSpawn = new NpcSpawnBridge();
            if (PveModePluginBridge.IsApiLive())
                PveMode = new PveModePluginBridge();
            if (EconomicsPluginBridge.IsApiLive())
                Economics = new EconomicsPluginBridge();
        }

        public static void Dispatch_OnCorpsePopulate(ScientistNPC npc, NPCPlayerCorpse corpse)
            => _?.OnCorpsePopulate(npc, corpse);

        public static void Dispatch_Spawned(BaseNetworkable entity)
        {
            if (_ == null || entity == null) return;
            if (entity is global::HumanNPC human) _.OnEntitySpawned(human);
            else if (entity is ScientistNPC2 sn2) _.OnEntitySpawned(sn2);
            else if (entity is CargoShip cargo) _.OnEntitySpawned(cargo);
            else if (entity is HackableLockedCrate crate) _.OnEntitySpawned(crate);
        }

        public static void Dispatch_OnEntityKill(BaseNetworkable entity)
        {
            if (_ == null || entity == null) return;
            if (entity is CargoShip cargo) _.OnEntityKill(cargo);
            else if (entity is HackableLockedCrate crate) _.OnEntityKill(crate);
            else if (entity is SupplyDrop drop) _.OnEntityKill(drop);
            else if (entity is LockedByEntCrate locked) _.OnEntityKill(locked);
        }

        public static void Dispatch_Die(BaseCombatEntity entity, HitInfo info)
        {
            if (_ == null || entity == null) return;
            if (entity is BradleyAPC bradley) _.OnEntityDeath(bradley, info);
            else if (entity is PatrolHelicopter heli) _.OnEntityDeath(heli, info);
        }

        public static object Dispatch_OnCargoShipSpawnCrate(CargoShip cargo)
            => _?.OnCargoShipSpawnCrate(cargo);

        public static void Dispatch_OnCargoShipHarborArrived(CargoShip cargo)
            => _?.OnCargoShipHarborArrived(cargo);

        public static object Dispatch_CanDeployScientists(BradleyAPC bradley, BaseEntity attacker, List<GameObjectRef> prefs, List<Vector3> positions)
            => _?.CanDeployScientists(bradley, attacker, prefs, positions);

        public static object Dispatch_GrimmHook(string hook, object[] args)
        {
            if (_ == null || string.IsNullOrEmpty(hook)) return null;
            try
            {
                if (hook == "OnNpcSpawnInitialized")
                {
                    _.OnNpcSpawnInitialized();
                    return null;
                }
                if (hook == "OnNpcSpawnPresetRename" && args != null && args.Length >= 2)
                {
                    _.OnNpcSpawnPresetRename(args[0] as string, args[1] as string);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] Grimm hook " + hook + " failed: " + ex.Message);
            }
            return null;
        }
    }
}
