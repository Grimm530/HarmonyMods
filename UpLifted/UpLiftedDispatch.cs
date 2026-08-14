using System;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class UpLifted
    {
        internal static UpLifted Instance { get; private set; }
        internal static void SetInstance(UpLifted inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static UpLifted GetModInstance() => Instance;

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] Init failed: " + ex.Message); }
            try { OverlayLanguageFile(); } catch { }
        }

        public void CallOnServerInitialized()
        {
            try { ResolvePluginReferences(); } catch { }
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[UpLifted] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] Unload failed: " + ex.Message); }
        }

        public static void Dispatch_OnTick()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTick))) return;
            try { inst.OnTick(); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnTick: " + ex.Message); }
        }

        public static void Dispatch_OnServerSave()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnServerSave))) return;
            try { inst.OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnServerSave: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerConnected))) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnPlayerConnected: " + ex.Message); }
        }

        public static void Dispatch_OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnMeleeAttack))) return;
            try { inst.OnMeleeAttack(player, info); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnMeleeAttack: " + ex.Message); }
        }

        public static object Dispatch_OnServerCommand(ConsoleSystem.Arg arg)
        {
            var inst = Instance;
            if (inst == null || arg == null || !inst.IsSubscribed(nameof(OnServerCommand))) return null;
            try { return inst.OnServerCommand(arg); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnServerCommand: " + ex.Message); return null; }
        }

        public static object Dispatch_OnPlayerCommand(ConsoleSystem.Arg arg)
        {
            var inst = Instance;
            if (inst == null || arg == null || !inst.IsSubscribed(nameof(OnPlayerCommand))) return null;
            try { return inst.OnPlayerCommand(arg); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnPlayerCommand: " + ex.Message); return null; }
        }

        public static object Dispatch_CanDeployItem(BasePlayer player, Deployer deployer, NetworkableId entityId)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanDeployItem))) return null;
            try { return inst.CanDeployItem(player, deployer, entityId); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] CanDeployItem: " + ex.Message); return null; }
        }

        public static object Dispatch_CanBuild(Planner plan, Construction prefab, Construction.Target target)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanBuild))) return null;
            try { return inst.CanBuild(plan, prefab, target); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] CanBuild: " + ex.Message); return null; }
        }

        public static void Dispatch_OnDoorKnocked(Door door, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnDoorKnocked))) return;
            try { inst.OnDoorKnocked(door, player); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnDoorKnocked: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerSleep(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerSleep))) return;
            try { inst.OnPlayerSleep(player); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnPlayerSleep: " + ex.Message); }
        }

        public static object Dispatch_OnLiftUse(ProceduralLift lift, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLiftUse))) return null;
            try { return inst.OnLiftUse(lift, player); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnLiftUse: " + ex.Message); return null; }
        }

        public static object Dispatch_OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityTakeDamage))) return null;
            try { return inst.OnEntityTakeDamage(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnEntityTakeDamage: " + ex.Message); return null; }
        }

        public static void Dispatch_OnEntityKill(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityKill))) return;
            try { inst.OnEntityKill(entity); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnEntityKill: " + ex.Message); }
        }

        public static void Dispatch_OnButtonPress(PressButton button, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnButtonPress))) return;
            try { inst.OnButtonPress(button, player); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnButtonPress: " + ex.Message); }
        }

        public static object Dispatch_OnEntityStabilityCheck(StabilityEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityStabilityCheck))) return null;
            try { return inst.OnEntityStabilityCheck(entity); }
            catch (Exception ex) { Debug.LogWarning("[UpLifted] OnEntityStabilityCheck: " + ex.Message); return null; }
        }
    }
}
