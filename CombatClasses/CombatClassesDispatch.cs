// CombatClassesDispatch.cs — partial class Oxide.Plugins.CombatClasses
// Instance management, lifecycle wrappers, Dispatch_* for Harmony patches.

using System;
using UnityEngine;
using ChatChannel = ConVar.Chat.ChatChannel;

namespace Oxide.Plugins
{
    public partial class CombatClasses
    {
        // Instance property is declared in CombatClassesPlugin.cs

        internal static void SetInstance(CombatClasses inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static CombatClasses GetModInstance() => Instance;

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] Init failed: " + ex.Message); }

            // Oxide auto-calls LoadDefaultMessages; Harmony must do it explicitly or UI shows raw keys.
            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] LoadDefaultMessages failed: " + ex.Message); }
        }

        public void CallLoaded()
        {
            // CombatClasses has no Loaded() hook.
        }

        public void CallOnServerInitialized()
        {
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[CombatClasses] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] Unload failed: " + ex.Message); }
        }

        public void ResolvePluginReferences() { }

        public static bool IsHookSubscribed(string hookName)
        {
            var inst = Instance;
            return inst == null || inst.IsSubscribed(hookName);
        }

        // ---- Damage / Death -----------------------------------------------

        public static object Dispatch_OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityTakeDamage))) return null;
            try { return inst.OnEntityTakeDamage(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnEntityTakeDamage: " + ex.Message); return null; }
        }

        public static void Dispatch_OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityDeath))) return;
            try { inst.OnEntityDeath(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnEntityDeath: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDeath))) return;
            try { inst.OnPlayerDeath(player, info); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerDeath: " + ex.Message); }
        }

        // ---- Lifecycle ----------------------------------------------------

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerConnected))) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerConnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDisconnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDisconnected))) return;
            try { inst.OnPlayerDisconnected(player); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerDisconnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerRespawn(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerRespawn))) return;
            try { inst.OnPlayerRespawn(player, null); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerRespawn: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerSleepEnded(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerSleepEnded))) return;
            try { inst.OnPlayerSleepEnded(player); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerSleepEnded: " + ex.Message); }
        }

        public static void Dispatch_OnServerSave()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnServerSave))) return;
            try { inst.OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnServerSave: " + ex.Message); }
        }

        // ---- Medical / revive ---------------------------------------------

        public static void Dispatch_OnPlayerRevive(BasePlayer reviver, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerRevive))) return;
            try { inst.OnPlayerRevive(reviver, player); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerRevive: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerRecovered(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerRecovered))) return;
            try { inst.OnPlayerRecovered(player); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerRecovered: " + ex.Message); }
        }

        public static object Dispatch_OnHealingItemUse(MedicalTool tool, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnHealingItemUse))) return null;
            try { return inst.OnHealingItemUse(tool, player); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnHealingItemUse: " + ex.Message); return null; }
        }

        // ---- Explosives ---------------------------------------------------

        public static void Dispatch_OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon weapon)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnExplosiveThrown))) return;
            try { inst.OnExplosiveThrown(player, entity, weapon); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnExplosiveThrown: " + ex.Message); }
        }

        public static object Dispatch_OnExplosiveDud(DudTimedExplosive explosive)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnExplosiveDud))) return null;
            try { return inst.OnExplosiveDud(explosive); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnExplosiveDud: " + ex.Message); return null; }
        }

        public static void Dispatch_OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnRocketLaunched))) return;
            try { inst.OnRocketLaunched(player, entity); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnRocketLaunched: " + ex.Message); }
        }

        // ---- Input / items / loot -----------------------------------------

        public static void Dispatch_OnPlayerInput(BasePlayer player, InputState input)
        {
            var inst = Instance;
            // Silent until OnServerInitialized — patches are live immediately on harmony.load.
            if (inst == null || !inst.IsReady || !inst.IsSubscribed(nameof(OnPlayerInput))) return;
            try { inst.OnPlayerInput(player, input); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerInput: " + ex.Message); }
        }

        public static void Dispatch_OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnActiveItemChanged))) return;
            try { inst.OnActiveItemChanged(player, oldItem, newItem); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnActiveItemChanged: " + ex.Message); }
        }

        public static void Dispatch_OnItemCreated(Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemCreated))) return;
            try { inst.OnItemCreated(item); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnItemCreated: " + ex.Message); }
        }

        public static void Dispatch_OnItemAddedToContainer(ItemContainer container, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemAddedToContainer))) return;
            try { inst.OnItemAddedToContainer(container, item); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnItemAddedToContainer: " + ex.Message); }
        }

        public static void Dispatch_OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntity))) return;
            try { inst.OnLootEntity(player, entity); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnLootEntity: " + ex.Message); }
        }

        public static void Dispatch_OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntityEnd))) return;
            try { inst.OnLootEntityEnd(player, entity); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnLootEntityEnd: " + ex.Message); }
        }

        public static object Dispatch_OnConstructionPlace(BaseEntity entity, Construction component, Construction.Target target, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnConstructionPlace))) return null;
            try { return inst.OnConstructionPlace(entity, component, target, player); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnConstructionPlace: " + ex.Message); return null; }
        }

        public static object Dispatch_OnPlayerChat(BasePlayer player, string message, ChatChannel channel)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerChat))) return null;
            try { return inst.OnPlayerChat(player, message, channel); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerChat: " + ex.Message); return null; }
        }

        public static void Dispatch_OnCuiDraggableDrag(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnCuiDraggableDrag(player, name, position, type); }
            catch (Exception ex) { Debug.LogWarning("[CombatClasses] OnCuiDraggableDrag: " + ex.Message); }
        }
    }
}
