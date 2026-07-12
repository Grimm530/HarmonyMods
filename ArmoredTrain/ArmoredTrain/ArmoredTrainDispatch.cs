using System;

namespace Oxide.Plugins
{
    /// <summary>
    /// Harmony glue for the ported plugin: exposes the Oxide-style lifecycle + hook methods (which are
    /// private instance members of ArmoredTrain) to the Harmony patches and console commands. Each
    /// dispatcher early-outs when no event/instance is active, replacing Oxide Subscribe/Unsubscribe.
    /// </summary>
    public partial class ArmoredTrain
    {
        private static bool Ready => _ins != null && _ins._eventController != null;

        // ----- lifecycle wrappers (called by ArmoredTrainMod) -----
        public void CallInit() => Init();
        public void CallOnServerInitialized() => OnServerInitialized();
        public void CallUnload() => Unload();

        // ----- command wrappers (called by ArmoredTrainMod) -----
        public static void CmdStart(BasePlayer player, string preset, int? overrideUnderground)
        {
            float ou = overrideUnderground ?? -1;
            EventLauncher.DelayStartEvent(false, player, string.IsNullOrEmpty(preset) ? "" : preset, ou);
        }

        public static void CmdStop() => EventLauncher.StopEvent();

        public static void CmdPoint(BasePlayer player)
        {
            _ins?.ChatCustomPointCommand(player, "atrainpoint", Array.Empty<string>());
        }

        public static void CmdSaveCustomWagon(string presetName, string wagonShortPrefabName)
        {
            WagonCustomizer.MapSaver.CreateOrAddNewWagonToData(presetName, wagonShortPrefabName);
        }

        public static bool IsEventActive() => EventLauncher.IsEventActive();

        // ----- damage (BaseCombatEntity.Hurt) -----
        public static object Dispatch_Hurt(BaseCombatEntity entity, HitInfo info)
        {
            if (!Ready || entity == null || info == null) return null;
            if (entity is TrainCar tc) return _ins.OnEntityTakeDamage(tc, info);
            if (entity is PatrolHelicopter ph) return _ins.OnEntityTakeDamage(ph, info);
            if (entity is BradleyAPC b) return _ins.OnEntityTakeDamage(b, info);
            if (entity is AutoTurret at) return _ins.OnEntityTakeDamage(at, info);
            if (entity is SamSite ss) return _ins.OnEntityTakeDamage(ss, info);
            if (entity is ElectricSwitch es) return _ins.OnEntityTakeDamage(es, info);
            if (entity is PowerCounter pc) return _ins.OnEntityTakeDamage(pc, info);
            if (entity is BasePlayer bp) return _ins.OnEntityTakeDamage(bp, info);
            return null;
        }

        // ----- death (BaseCombatEntity.Die) -----
        public static void Dispatch_Die(BaseCombatEntity entity, HitInfo info)
        {
            if (!Ready || entity == null) return;
            if (entity is PatrolHelicopter ph) _ins.OnEntityDeath(ph, info);
            else if (entity is AutoTurret at) _ins.OnEntityDeath(at, info);
            else if (entity is BradleyAPC b) _ins.OnEntityDeath(b, info);
            else if (entity is ScientistNPC sn) _ins.OnEntityDeath(sn, info);
        }

        // ----- spawn (BaseNetworkable.Spawn) -----
        public static void Dispatch_Spawned(BaseNetworkable entity)
        {
            if (_ins == null || _ins._eventController == null || entity == null) return;
            if (entity is HelicopterDebris hd) _ins.OnEntitySpawned(hd);
            else if (entity is LootContainer lc) _ins.OnEntitySpawned(lc);
        }

        // ----- mount (BaseMountable.AttemptMount) -----
        public static object Dispatch_CanMount(BaseMountable mountable, BasePlayer player)
        {
            if (!Ready || mountable == null || player == null) return null;
            if (mountable is BaseVehicleSeat seat) return _ins.CanMountEntity(player, seat);
            return null;
        }

        // ----- loot (PlayerLoot.StartLootingEntity) -----
        public static object Dispatch_CanLoot(BasePlayer player, BaseEntity target)
        {
            if (!Ready || player == null || target == null) return null;
            if (target is LootContainer lc) return _ins.CanLootEntity(player, lc);
            if (target is SamSite ss) return _ins.CanLootEntity(player, ss);
            return null;
        }

        public static void Dispatch_OnLootEntity(BasePlayer player, BaseEntity target)
        {
            if (!Ready || player == null || target == null) return;
            if (target is StorageContainer sc) _ins.OnLootEntity(player, sc);
        }

        public static void Dispatch_OnLootEntityEnd(BasePlayer player, BaseEntity target)
        {
            if (!Ready || player == null || target == null) return;
            if (target is StorageContainer sc) _ins.OnLootEntityEnd(player, sc);
        }

        // ----- hack (HackableLockedCrate.RPC_Hack) -----
        public static object Dispatch_CanHack(BasePlayer player, HackableLockedCrate crate)
        {
            if (!Ready || player == null || crate == null) return null;
            return _ins.CanHackCrate(player, crate);
        }

        // ----- sleep (BasePlayer.StartSleeping) -----
        public static void Dispatch_OnPlayerSleep(BasePlayer player)
        {
            if (_ins == null || player == null) return;
            _ins.OnPlayerSleep(player);
        }

        // ----- kill (BaseNetworkable kill) : block event wagon destruction -----
        public static bool Dispatch_ShouldBlockKill(BaseNetworkable entity)
        {
            if (!Ready || entity == null) return false;
            if (entity is TrainCar tc) return _ins.OnEntityKill(tc) != null;
            return false;
        }

        // ----- switch toggle -----
        public static object Dispatch_OnSwitchToggle(ElectricSwitch sw, BasePlayer player)
        {
            if (!Ready || sw == null || player == null) return null;
            return _ins.OnSwitchToggle(sw, player);
        }

        public static void Dispatch_OnSwitchToggled(ElectricSwitch sw, BasePlayer player)
        {
            if (!Ready || sw == null || player == null) return;
            _ins.OnSwitchToggled(sw, player);
        }

        // ----- turret authorize / samsite mode -----
        public static object Dispatch_OnTurretAuthorize(AutoTurret turret, BasePlayer player)
        {
            if (!Ready || turret == null || player == null) return null;
            return _ins.OnTurretAuthorize(turret, player);
        }

        public static object Dispatch_OnSamSiteModeToggle(SamSite samSite, BasePlayer player, bool isEnable)
        {
            if (!Ready || samSite == null || player == null) return null;
            return _ins.OnSamSiteModeToggle(samSite, player, isEnable);
        }

        // ----- train couple / uncouple -----
        public static object Dispatch_OnTrainCarUncouple(TrainCar trainCar, BasePlayer player)
        {
            if (!Ready || trainCar == null || player == null) return null;
            return _ins.OnTrainCarUncouple(trainCar, player);
        }

        public static object Dispatch_CanTrainCarCouple(TrainCar a, TrainCar b)
        {
            if (!Ready || a == null || b == null) return null;
            return _ins.CanTrainCarCouple(a, b);
        }

        // ----- collision destroy wagons ahead -----
        public static object Dispatch_OnEntityEnter(TriggerTrainCollisions trigger, TrainCar trainCar)
        {
            if (!Ready || trigger == null || trainCar == null) return null;
            return _ins.OnEntityEnter(trigger, trainCar);
        }

        // ----- pickup block -----
        public static object Dispatch_CanPickup(BasePlayer player, BaseEntity entity)
        {
            if (!Ready || player == null || entity == null) return null;
            if (entity is ElectricSwitch es) return _ins.CanPickupEntity(player, es);
            if (entity is PowerCounter pc) return _ins.CanPickupEntity(player, pc);
            return null;
        }

        // ----- corpse populate -----
        public static void Dispatch_OnCorpsePopulate(ScientistNPC scientist, NPCPlayerCorpse corpse)
        {
            if (!Ready || scientist == null || corpse == null) return;
            _ins.OnCorpsePopulate(scientist, corpse);
        }

        // ----- bradley target filter -----
        public static object Dispatch_CanBradleyApcTarget(BradleyAPC bradley, BaseEntity entity)
        {
            if (!Ready || bradley == null || entity == null) return null;
            return _ins.CanBradleyApcTarget(bradley, entity);
        }

        // ----- heli target filter -----
        public static object Dispatch_CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player)
        {
            if (!Ready || heli == null || player == null) return null;
            return _ins.CanHelicopterTarget(heli, player);
        }

        // ----- custom npc target -----
        public static object Dispatch_OnCustomNpcTarget(ScientistNPC npc, BasePlayer player)
        {
            if (!Ready || npc == null || player == null) return null;
            return _ins.OnCustomNpcTarget(npc, player);
        }

        // ----- counter UI -----
        public static object Dispatch_OnCounterModeToggle(PowerCounter counter, BasePlayer player, bool mode)
        {
            if (!Ready || counter == null || player == null) return null;
            return _ins.OnCounterModeToggle(counter, player, mode);
        }

        public static object Dispatch_OnCounterTargetChange(PowerCounter counter, BasePlayer player, int targetNumber)
        {
            if (!Ready || counter == null || player == null) return null;
            return _ins.OnCounterTargetChange(counter, player, targetNumber);
        }
    }
}
