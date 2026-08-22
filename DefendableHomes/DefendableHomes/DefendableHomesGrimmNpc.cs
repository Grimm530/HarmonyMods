using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DefendableHomes
{
    /// <summary>
    /// Bridges the ported plugin's NpcSpawn.Call("SpawnNpc" / "AddTargetRaid" / "SetCurrentWeapon" / "SetParent")
    /// to the GrimmNPC Harmony mod (NpcSpawn replacement). SpawnNpc forwards the original JObject NPC config
    /// unchanged. Raid/weapon/parent APIs are resolved by name because GrimmNPC assemblies are renamed at load.
    /// </summary>
    public static class DefendableHomesGrimmNpc
    {
        private const string DataTypeKey = "GrimmNPC.Type";
        private const string DataInstanceKey = "GrimmNPC.Instance";

        private static bool _bound;
        private static Type _grimmType;
        private static MethodInfo _spawnNpc;
        private static MethodInfo _addTargetRaid;
        private static MethodInfo _setCurrentWeapon;
        private static MethodInfo _setParent;
        private static object _grimmInstance;

        public static bool Available => _spawnNpc != null && TryResolveInstance();

        public static void Bind()
        {
            if (_bound && _spawnNpc != null && TryResolveInstance()) return;
            _bound = true;
            _spawnNpc = null;
            _addTargetRaid = null;
            _setCurrentWeapon = null;
            _setParent = null;
            _grimmInstance = null;

            try
            {
                _grimmType = FindGrimmNpcType();
                if (_grimmType == null)
                {
                    Debug.LogWarning("[DefendableHomes] GrimmNPC type not found. Load 0GrimmNPC before DefendableHomes (harmony.load 0GrimmNPC). NPCs will not spawn.");
                    return;
                }

                _spawnNpc = FindMethod(_grimmType, "SpawnNpc", 2);
                _addTargetRaid = FindMethod(_grimmType, "AddTargetRaid", 2);
                _setCurrentWeapon = FindMethod(_grimmType, "SetCurrentWeapon", 2);
                _setParent = FindMethod(_grimmType, "SetParent", 4) ?? FindMethod(_grimmType, "SetParent", 3);

                if (_spawnNpc == null)
                {
                    Debug.LogWarning("[DefendableHomes] GrimmNPC.SpawnNpc(Vector3, object) not found.");
                    return;
                }

                if (!TryResolveInstance())
                    Debug.LogWarning("[DefendableHomes] GrimmNPC bound but Instance not ready yet; will retry on spawn.");

                Debug.Log("[DefendableHomes] GrimmNPC integration bound (" + _grimmType.Assembly.GetName().Name + ").");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DefendableHomes] GrimmNPC bind failed: " + ex);
            }
        }

        private static MethodInfo FindMethod(Type type, string name, int paramCount)
        {
            if (type == null) return null;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.Name != name) continue;
                if (m.GetParameters().Length == paramCount) return m;
            }
            return null;
        }

        private static Type FindGrimmNpcType()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(DataTypeKey) is Type fromData && fromData.Name == "GrimmNPC")
                    return fromData;
            }
            catch { }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType("GrimmNPC.GrimmNPC", false);
                    if (t != null) return t;
                }
                catch { }
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type[] exported;
                    try { exported = asm.GetExportedTypes(); }
                    catch { continue; }
                    foreach (Type type in exported)
                        if (type != null && type.Name == "GrimmNPC" && type.Namespace == "GrimmNPC")
                            return type;
                }
                catch { }
            }

            try
            {
                object inst = AppDomain.CurrentDomain.GetData(DataInstanceKey);
                if (inst != null) return inst.GetType();
            }
            catch { }

            return null;
        }

        private static bool TryResolveInstance()
        {
            if (_grimmType == null) return false;
            try
            {
                object fromData = AppDomain.CurrentDomain.GetData(DataInstanceKey);
                if (fromData != null && _grimmType.IsInstanceOfType(fromData))
                {
                    _grimmInstance = fromData;
                    return true;
                }
            }
            catch { }

            var p = _grimmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            _grimmInstance = p?.GetValue(null);
            return _grimmInstance != null;
        }

        public static object SpawnNpc(Vector3 position, object jObjectConfig)
        {
            if (_spawnNpc == null) Bind();
            if (_spawnNpc == null || !TryResolveInstance())
            {
                Debug.LogWarning("[DefendableHomes] GrimmNPC not available - cannot spawn NPC.");
                return null;
            }

            try
            {
                return _spawnNpc.Invoke(_grimmInstance, new[] { position, jObjectConfig });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DefendableHomes] GrimmNPC.SpawnNpc failed: " + ex);
                return null;
            }
        }

        public static void AddTargetRaid(ScientistNPC npc, HashSet<BuildingBlock> foundations)
        {
            if (npc == null || foundations == null) return;
            if (_addTargetRaid == null) Bind();
            if (_addTargetRaid == null || !TryResolveInstance()) return;
            try { _addTargetRaid.Invoke(_grimmInstance, new object[] { npc, foundations }); }
            catch (Exception ex) { Debug.LogWarning("[DefendableHomes] GrimmNPC.AddTargetRaid failed: " + ex.Message); }
        }

        public static void SetCurrentWeapon(ScientistNPC npc, Item weapon)
        {
            if (npc == null || weapon == null) return;
            if (_setCurrentWeapon == null) Bind();
            if (_setCurrentWeapon == null || !TryResolveInstance()) return;
            try { _setCurrentWeapon.Invoke(_grimmInstance, new object[] { npc, weapon }); }
            catch (Exception ex) { Debug.LogWarning("[DefendableHomes] GrimmNPC.SetCurrentWeapon failed: " + ex.Message); }
        }

        public static void SetParent(ScientistNPC npc, object parentObj, Vector3 localPos, float unusedPadding = 0f)
        {
            if (npc == null || parentObj == null) return;
            if (_setParent == null) Bind();
            if (_setParent == null || !TryResolveInstance()) return;
            try
            {
                var ps = _setParent.GetParameters();
                if (ps.Length >= 4)
                    _setParent.Invoke(_grimmInstance, new[] { npc, parentObj, localPos, unusedPadding });
                else
                    _setParent.Invoke(_grimmInstance, new[] { npc, parentObj, localPos });
            }
            catch (Exception ex) { Debug.LogWarning("[DefendableHomes] GrimmNPC.SetParent failed: " + ex.Message); }
        }
    }
}

namespace Oxide.Plugins
{
    /// <summary>
    /// NpcSpawn plugin stand-in. The ported plugin's NpcSpawn.Call(...) routes here, then to GrimmNPC.
    /// </summary>
    public class NpcSpawnBridge : Oxide.Core.Plugins.Plugin
    {
        public NpcSpawnBridge() { Name = "NpcSpawn"; IsLoaded = true; }

        public object SpawnNpc(UnityEngine.Vector3 position, object config)
        {
            return global::DefendableHomes.DefendableHomesGrimmNpc.SpawnNpc(position, config);
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook) || args == null) return null;

            if (string.Equals(hook, "SpawnNpc", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2 && args[0] is UnityEngine.Vector3 pos)
                return global::DefendableHomes.DefendableHomesGrimmNpc.SpawnNpc(pos, args[1]);

            if (string.Equals(hook, "AddTargetRaid", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2 && args[0] is ScientistNPC raidNpc)
            {
                var foundations = args[1] as HashSet<BuildingBlock>;
                if (foundations != null)
                    global::DefendableHomes.DefendableHomesGrimmNpc.AddTargetRaid(raidNpc, foundations);
                return null;
            }

            if (string.Equals(hook, "SetCurrentWeapon", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2 && args[0] is ScientistNPC weapNpc && args[1] is Item item)
            {
                global::DefendableHomes.DefendableHomesGrimmNpc.SetCurrentWeapon(weapNpc, item);
                return null;
            }

            if (string.Equals(hook, "SetParent", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 3 && args[0] is ScientistNPC parentNpc)
            {
                Vector3 local = args[2] is Vector3 v ? v : Vector3.zero;
                float pad = args.Length >= 4 && args[3] is float f ? f : 0f;
                global::DefendableHomes.DefendableHomesGrimmNpc.SetParent(parentNpc, args[1], local, pad);
                return null;
            }

            return null;
        }
    }
}
