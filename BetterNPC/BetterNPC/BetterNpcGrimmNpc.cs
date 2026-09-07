using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterNpc
{
    /// <summary>
    /// Bridges the ported plugin's NpcSpawn.Call(...) hooks to the GrimmNPC Harmony mod
    /// (NpcSpawn replacement). SpawnNpc / SpawnPreset forward the original config unchanged.
    /// </summary>
    public static class BetterNpcGrimmNpc
    {
        private const string DataTypeKey = "GrimmNPC.Type";
        private const string DataInstanceKey = "GrimmNPC.Instance";

        private static bool _bound;
        private static Type _grimmType;
        private static MethodInfo _spawnNpc;
        private static MethodInfo _spawnPreset;
        private static MethodInfo _setParent;
        private static MethodInfo _isStationaryPreset;
        private static MethodInfo _getAreaMask;
        private static MethodInfo _getSpawnPoint;
        private static MethodInfo _getRoadSpawnPoint;
        private static MethodInfo _registerPresetUsage;
        private static MethodInfo _unregisterPresetUsage;
        private static MethodInfo _getJObject;
        private static object _grimmInstance;

        public static bool Available => _spawnNpc != null && TryResolveInstance();

        public static void Bind()
        {
            if (_bound && _spawnNpc != null && TryResolveInstance()) return;
            _bound = true;
            _spawnNpc = null;
            _spawnPreset = null;
            _setParent = null;
            _isStationaryPreset = null;
            _getAreaMask = null;
            _getSpawnPoint = null;
            _getRoadSpawnPoint = null;
            _registerPresetUsage = null;
            _unregisterPresetUsage = null;
            _getJObject = null;
            _grimmInstance = null;

            try
            {
                _grimmType = FindGrimmNpcType();
                if (_grimmType == null)
                {
                    Debug.LogWarning("[BetterNPC] GrimmNPC type not found. Load 0GrimmNPC before BetterNPC (harmony.load 0GrimmNPC). NPCs will not spawn.");
                    return;
                }

                _spawnNpc = FindMethod(_grimmType, "SpawnNpc", 2);
                _spawnPreset = FindMethod(_grimmType, "SpawnPreset", 2);
                _setParent = FindMethod(_grimmType, "SetParent", 4) ?? FindMethod(_grimmType, "SetParent", 3);
                _isStationaryPreset = FindMethod(_grimmType, "IsStationaryPreset", 1);
                _getAreaMask = FindMethod(_grimmType, "GetAreaMask", 1);
                _getSpawnPoint = FindMethod(_grimmType, "GetSpawnPoint", 1);
                _getRoadSpawnPoint = FindMethod(_grimmType, "GetRoadSpawnPoint", 1);
                _registerPresetUsage = FindMethod(_grimmType, "RegisterPresetUsage", 3);
                _unregisterPresetUsage = FindMethod(_grimmType, "UnregisterPresetUsage", 3);
                _getJObject = FindMethod(_grimmType, "GetJObject", 1);

                if (_spawnNpc == null)
                {
                    Debug.LogWarning("[BetterNPC] GrimmNPC.SpawnNpc(Vector3, object) not found.");
                    return;
                }

                if (!TryResolveInstance())
                    Debug.LogWarning("[BetterNPC] GrimmNPC bound but Instance not ready yet; will retry on spawn.");

                Debug.Log("[BetterNPC] GrimmNPC integration bound (" + _grimmType.Assembly.GetName().Name + ").");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC bind failed: " + ex);
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

        private static bool EnsureReady()
        {
            object live = null;
            try { live = AppDomain.CurrentDomain.GetData(DataInstanceKey); } catch { }
            if (_spawnNpc == null || _grimmType == null || live == null || !_grimmType.IsInstanceOfType(live))
            {
                _bound = false;
                Bind();
            }
            return _spawnNpc != null && TryResolveInstance();
        }

        private static void TagSpawned(object spawned)
        {
            if (spawned is BaseEntity entity)
                GrimmCoreBridge.TagCustomEntity(entity);
        }

        public static object SpawnNpc(Vector3 position, object jObjectConfig)
        {
            if (!EnsureReady())
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC not available - cannot spawn NPC.");
                return null;
            }

            try
            {
                object spawned = _spawnNpc.Invoke(_grimmInstance, new[] { position, jObjectConfig });
                TagSpawned(spawned);
                return spawned;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC.SpawnNpc failed: " + ex);
                return null;
            }
        }

        public static object SpawnPreset(Vector3 position, string presetName)
        {
            if (!EnsureReady() || _spawnPreset == null)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC not available - cannot spawn preset.");
                return null;
            }

            try
            {
                object spawned = _spawnPreset.Invoke(_grimmInstance, new object[] { position, presetName });
                TagSpawned(spawned);
                return spawned;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC.SpawnPreset failed: " + ex);
                return null;
            }
        }

        public static void SetParent(ScientistNPC npc, object parentObj, Vector3 localPos, float updateTime = 1f)
        {
            if (npc == null || parentObj == null) return;
            if (_setParent == null) Bind();
            if (_setParent == null || !TryResolveInstance()) return;
            try
            {
                var ps = _setParent.GetParameters();
                if (ps.Length >= 4)
                    _setParent.Invoke(_grimmInstance, new[] { npc, parentObj, localPos, updateTime });
                else
                    _setParent.Invoke(_grimmInstance, new[] { npc, parentObj, localPos });
            }
            catch (Exception ex) { Debug.LogWarning("[BetterNPC] GrimmNPC.SetParent failed: " + ex.Message); }
        }

        public static bool IsStationaryPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return false;
            if (_isStationaryPreset == null) Bind();
            if (_isStationaryPreset == null || !TryResolveInstance()) return false;
            try
            {
                object r = _isStationaryPreset.Invoke(_grimmInstance, new object[] { presetName });
                return r is bool b && b;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC.IsStationaryPreset failed: " + ex.Message);
                return false;
            }
        }

        public static int GetAreaMask(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return 1;
            if (_getAreaMask == null) Bind();
            if (_getAreaMask == null || !TryResolveInstance()) return 1;
            try
            {
                object r = _getAreaMask.Invoke(_grimmInstance, new object[] { presetName });
                return r is int i ? i : Convert.ToInt32(r);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC.GetAreaMask failed: " + ex.Message);
                return 1;
            }
        }

        public static object GetSpawnPoint(string biome)
        {
            if (string.IsNullOrEmpty(biome)) return null;
            if (_getSpawnPoint == null) Bind();
            if (_getSpawnPoint == null || !TryResolveInstance()) return null;
            try { return _getSpawnPoint.Invoke(_grimmInstance, new object[] { biome }); }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC.GetSpawnPoint failed: " + ex.Message);
                return null;
            }
        }

        public static object GetRoadSpawnPoint(string road)
        {
            if (string.IsNullOrEmpty(road)) return null;
            if (_getRoadSpawnPoint == null) Bind();
            if (_getRoadSpawnPoint == null || !TryResolveInstance()) return null;
            try { return _getRoadSpawnPoint.Invoke(_grimmInstance, new object[] { road }); }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC.GetRoadSpawnPoint failed: " + ex.Message);
                return null;
            }
        }

        public static void RegisterPresetUsage(string presetName, string sourcePlugin, string spawnPointName)
        {
            if (_registerPresetUsage == null) Bind();
            if (_registerPresetUsage == null || !TryResolveInstance()) return;
            try { _registerPresetUsage.Invoke(_grimmInstance, new object[] { presetName, sourcePlugin, spawnPointName }); }
            catch (Exception ex) { Debug.LogWarning("[BetterNPC] GrimmNPC.RegisterPresetUsage failed: " + ex.Message); }
        }

        public static void UnregisterPresetUsage(string presetName, string sourcePlugin, string spawnPointName)
        {
            if (_unregisterPresetUsage == null) Bind();
            if (_unregisterPresetUsage == null || !TryResolveInstance()) return;
            try { _unregisterPresetUsage.Invoke(_grimmInstance, new object[] { presetName, sourcePlugin, spawnPointName }); }
            catch (Exception ex) { Debug.LogWarning("[BetterNPC] GrimmNPC.UnregisterPresetUsage failed: " + ex.Message); }
        }

        public static object GetJObject(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return null;
            if (_getJObject == null) Bind();
            if (_getJObject == null || !TryResolveInstance()) return null;
            try { return _getJObject.Invoke(_grimmInstance, new object[] { presetName }); }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] GrimmNPC.GetJObject failed: " + ex.Message);
                return null;
            }
        }
    }
}

namespace Harmony.Plugins
{
    /// <summary>
    /// NpcSpawn plugin stand-in. The ported plugin's NpcSpawn.Call(...) routes here, then to GrimmNPC.
    /// </summary>
    public class NpcSpawnPluginBridge : Harmony.Core.Plugins.Plugin
    {
        public NpcSpawnPluginBridge() { Name = "NpcSpawn"; IsLoaded = true; }

        public override object Call(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook) || args == null) return null;

            if (string.Equals(hook, "SpawnNpc", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2 && args[0] is Vector3 pos)
                return global::BetterNpc.BetterNpcGrimmNpc.SpawnNpc(pos, args[1]);

            if (string.Equals(hook, "SpawnPreset", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2 && args[0] is Vector3 presetPos && args[1] is string presetName)
                return global::BetterNpc.BetterNpcGrimmNpc.SpawnPreset(presetPos, presetName);

            if (string.Equals(hook, "SetParent", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 3 && args[0] is ScientistNPC parentNpc)
            {
                Vector3 local = args[2] is Vector3 v ? v : Vector3.zero;
                float updateTime = args.Length >= 4 && args[3] is float f ? f : 1f;
                global::BetterNpc.BetterNpcGrimmNpc.SetParent(parentNpc, args[1], local, updateTime);
                return null;
            }

            if (string.Equals(hook, "IsStationaryPreset", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 1 && args[0] is string stationaryName)
                return global::BetterNpc.BetterNpcGrimmNpc.IsStationaryPreset(stationaryName);

            if (string.Equals(hook, "GetAreaMask", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 1 && args[0] is string maskName)
                return global::BetterNpc.BetterNpcGrimmNpc.GetAreaMask(maskName);

            if (string.Equals(hook, "GetSpawnPoint", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 1 && args[0] is string biome)
                return global::BetterNpc.BetterNpcGrimmNpc.GetSpawnPoint(biome);

            if (string.Equals(hook, "GetRoadSpawnPoint", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 1 && args[0] is string road)
                return global::BetterNpc.BetterNpcGrimmNpc.GetRoadSpawnPoint(road);

            if (string.Equals(hook, "RegisterPresetUsage", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 3)
            {
                global::BetterNpc.BetterNpcGrimmNpc.RegisterPresetUsage(args[0] as string, args[1] as string, args[2] as string);
                return null;
            }

            if (string.Equals(hook, "UnregisterPresetUsage", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 3)
            {
                global::BetterNpc.BetterNpcGrimmNpc.UnregisterPresetUsage(args[0] as string, args[1] as string, args[2] as string);
                return null;
            }

            if (string.Equals(hook, "GetJObject", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 1 && args[0] is string jObjectName)
                return global::BetterNpc.BetterNpcGrimmNpc.GetJObject(jObjectName);

            return null;
        }
    }

    /// <summary>Alias for NpcSpawnPluginBridge (NpcSpawn field assignment).</summary>
    public class NpcSpawnBridge : NpcSpawnPluginBridge
    {
    }
}
