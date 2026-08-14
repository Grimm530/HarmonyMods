using System;
using Network;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Hud
    {
        internal static Hud Instance;

        internal static void SetInstance(Hud inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static Hud GetModInstance() => Instance;

        public void CallInit()
        {
            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { Debug.LogWarning("[Hud] LoadDefaultMessages: " + ex.Message); }
        }

        public void CallOnServerInitialized()
        {
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[Hud] OnServerInitialized: " + ex); }
        }

        public void CallUnload()
        {
            IsLoaded = false;
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[Hud] Unload: " + ex.Message); }
        }

        public void HarmonyRegisterPermissions()
        {
            try
            {
                permission.RegisterPermission("hud.streamer", this);
                if (_config?.MainSetup?.AdditionalMenu?.Commands == null) return;
                foreach (var check in _config.MainSetup.AdditionalMenu.Commands)
                {
                    if (!string.IsNullOrEmpty(check.PermissionToSee))
                        permission.RegisterPermission(check.PermissionToSee, this);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Hud] HarmonyRegisterPermissions: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnPlayerConnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDisconnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerDisconnected(player); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnPlayerDisconnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerSleep(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerSleep(player); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnPlayerSleep: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerSleepEnded(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerSleepEnded(player); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnPlayerSleepEnded: " + ex.Message); }
        }

        public static void Dispatch_OnServerSave()
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnServerSave: " + ex.Message); }
        }

        public static void Dispatch_OnConnectionQueue(Network.Connection connection)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnConnectionQueue(connection); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnConnectionQueue: " + ex.Message); }
        }

        public static void Dispatch_OnEntityMounted(ComputerStation entity, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityMounted(entity, player); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnEntityMounted: " + ex.Message); }
        }

        public static void Dispatch_OnEntityDismounted(ComputerStation entity, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityDismounted(entity, player); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnEntityDismounted: " + ex.Message); }
        }

        public static void Dispatch_OnDeepSeaOpened(DeepSeaManager manager)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnDeepSeaOpened(manager); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnDeepSeaOpened: " + ex.Message); }
        }

        public static void Dispatch_OnDeepSeaClosed(DeepSeaManager manager)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnDeepSeaClosed(manager); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnDeepSeaClosed: " + ex.Message); }
        }

        public static void Dispatch_OnEntitySpawned(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null || entity == null) return;
            try
            {
                switch (entity)
                {
                    case BradleyAPC bradley: inst.OnEntitySpawned(bradley); break;
                    case PatrolHelicopter heli: inst.OnEntitySpawned(heli); break;
                    case CH47Helicopter ch47: inst.OnEntitySpawned(ch47); break;
                    case CargoShip cargo: inst.OnEntitySpawned(cargo); break;
                    case SupplyDrop drop: inst.OnEntitySpawned(drop); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnEntitySpawned: " + ex.Message); }
        }

        public static void Dispatch_OnEntityKill(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null || entity == null) return;
            try
            {
                switch (entity)
                {
                    case BradleyAPC bradley: inst.OnEntityKill(bradley); break;
                    case PatrolHelicopter heli: inst.OnEntityKill(heli); break;
                    case CH47Helicopter ch47: inst.OnEntityKill(ch47); break;
                    case CargoShip cargo: inst.OnEntityKill(cargo); break;
                    case SupplyDrop drop: inst.OnEntityKill(drop); break;
                    case HackableLockedCrate crate: inst.OnEntityKill(crate); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnEntityKill: " + ex.Message); }
        }

        public static void Dispatch_OnCargoShipHarborArrived(CargoShip ship)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnCargoShipHarborArrived(ship); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnCargoShipHarborArrived: " + ex.Message); }
        }

        public static void Dispatch_OnCargoShipHarborLeave(CargoShip ship)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnCargoShipHarborLeave(ship); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnCargoShipHarborLeave: " + ex.Message); }
        }

        public static void Dispatch_OnCrateHack(HackableLockedCrate crate)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnCrateHack(crate); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnCrateHack: " + ex.Message); }
        }

        public static void Dispatch_OnCrateHackEnd(HackableLockedCrate crate)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnCrateHackEnd(crate); }
            catch (Exception ex) { Debug.LogWarning("[Hud] OnCrateHackEnd: " + ex.Message); }
        }
    }
}
