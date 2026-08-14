using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class VirtualQuarries
    {
        internal static VirtualQuarries Instance { get; private set; }

        internal static void SetInstance(VirtualQuarries inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static VirtualQuarries GetModInstance() => Instance;

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] Init failed: " + ex.Message); }
            try { OverlayLanguageFile(); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OverlayLanguageFile: " + ex.Message); }
        }

        public void CallOnServerInitialized()
        {
            try { ResolvePluginReferences(); } catch { }
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[VirtualQuarries] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] Unload failed: " + ex.Message); }
        }

        public static void Dispatch_OnNewSave()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnNewSave))) return;
            try { inst.OnNewSave(); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnNewSave: " + ex.Message); }
        }

        public static void Dispatch_OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnDispenserBonus))) return;
            try { inst.OnDispenserBonus(dispenser, player, item); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnDispenserBonus: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerConnected))) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnPlayerConnected: " + ex.Message); }
        }

        public static void Dispatch_OnLootEntityEnd(BasePlayer player, BoxStorage storage)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntityEnd))) return;
            try { inst.OnLootEntityEnd(player, storage); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnLootEntityEnd: " + ex.Message); }
        }

        public static void Dispatch_OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnExplosiveThrown))) return;
            try { inst.OnExplosiveThrown(player, entity, item); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnExplosiveThrown: " + ex.Message); }
        }

        public static object Dispatch_OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityTakeDamage))) return null;
            if (entity is not BoxStorage box) return null;
            try { return inst.OnEntityTakeDamage(box); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnEntityTakeDamage: " + ex.Message); return null; }
        }

        public static void Dispatch_OnQuarryToggled(MiningQuarry quarry, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnQuarryToggled))) return;
            try { inst.OnQuarryToggled(quarry, player); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnQuarryToggled: " + ex.Message); }
        }

        public static object Dispatch_OnExcavatorResourceSet(ExcavatorArm arm, string type, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnExcavatorResourceSet))) return null;
            try { return inst.OnExcavatorResourceSet(arm, type, player); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnExcavatorResourceSet: " + ex.Message); return null; }
        }

        public static object Dispatch_OnExcavatorSuppliesRequest(ExcavatorSignalComputer comp, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnExcavatorSuppliesRequest))) return null;
            try { return inst.OnExcavatorSuppliesRequest(comp, player); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnExcavatorSuppliesRequest: " + ex.Message); return null; }
        }

        public static void Dispatch_OnLootEntity(BasePlayer player, BoxStorage storage)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntity))) return;
            try { inst.OnLootEntity(player, storage); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] OnLootEntity: " + ex.Message); }
        }

        public static object Dispatch_CanLootEntity(BasePlayer player, StorageContainer storage)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanLootEntity))) return null;
            try { return inst.CanLootEntity(player, storage); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] CanLootEntity: " + ex.Message); return null; }
        }
    }
}
