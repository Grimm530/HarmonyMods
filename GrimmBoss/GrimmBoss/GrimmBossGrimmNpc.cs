using System;
using System.Reflection;
using UnityEngine;

namespace GrimmBoss
{
    /// <summary>
    /// Bridges GrimmBoss NpcSpawn.Call / SpawnNpc reflection to 0GrimmNPC
    /// (Harmony port of Oxide NpcSpawn). Resolves GrimmNPC.GrimmNPC via AppDomain keys.
    /// </summary>
    public static class GrimmBossGrimmNpc
    {
        private const string DataTypeKey = "GrimmNPC.Type";
        private const string DataInstanceKey = "GrimmNPC.Instance";

        private static bool _bound;
        private static Type _grimmType;
        private static MethodInfo _spawnNpc;
        private static object _grimmInstance;

        public static bool Available => _spawnNpc != null && TryResolveInstance();

        public static bool TryBindQuiet()
        {
            Bind(false);
            return Available;
        }

        public static void Bind(bool log = true)
        {
            if (_bound && _spawnNpc != null && TryResolveInstance()) return;
            _bound = true;
            _spawnNpc = null;
            _grimmInstance = null;

            try
            {
                _grimmType = FindGrimmNpcType();
                if (_grimmType == null)
                {
                    if (log)
                        Debug.LogWarning("[GrimmBoss] GrimmNPC type not found. Load 0GrimmNPC before GrimmBoss (harmony.load 0GrimmNPC). Bosses will not spawn.");
                    return;
                }

                _spawnNpc = _grimmType.GetMethod("SpawnNpc", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Vector3), typeof(object) }, null);
                if (_spawnNpc == null)
                {
                    foreach (var m in _grimmType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name != "SpawnNpc") continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 2 && ps[0].ParameterType == typeof(Vector3))
                        {
                            _spawnNpc = m;
                            break;
                        }
                    }
                }

                if (_spawnNpc == null)
                {
                    if (log) Debug.LogWarning("[GrimmBoss] GrimmNPC.SpawnNpc(Vector3, object) not found.");
                    return;
                }

                if (!TryResolveInstance() && log)
                    Debug.LogWarning("[GrimmBoss] GrimmNPC bound but Instance not ready yet; will retry on spawn.");

                if (log)
                    Debug.Log("[GrimmBoss] GrimmNPC SpawnNpc integration bound (" + _grimmType.Assembly.GetName().Name + ").");
            }
            catch (Exception ex)
            {
                if (log) Debug.LogWarning("[GrimmBoss] GrimmNPC bind failed: " + ex);
            }
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
            if (_spawnNpc == null)
                Bind();
            if (_spawnNpc == null || !TryResolveInstance())
            {
                Debug.LogWarning("[GrimmBoss] GrimmNPC not available - cannot spawn NPC.");
                return null;
            }

            try
            {
                return _spawnNpc.Invoke(_grimmInstance, new[] { position, jObjectConfig });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] GrimmNPC.SpawnNpc failed: " + ex);
                return null;
            }
        }
    }
}

namespace Oxide.Plugins
{
    /// <summary>
    /// NpcSpawn stand-in. GrimmBoss resolves SpawnNpc via reflection on this type and/or Call.
    /// Both route to 0GrimmNPC through GrimmBossGrimmNpc.
    /// </summary>
    public class NpcSpawnBridge : Oxide.Core.Plugins.Plugin
    {
        public NpcSpawnBridge() { Name = "NpcSpawn"; IsLoaded = true; }

        public object SpawnNpc(UnityEngine.Vector3 position, object config)
        {
            return global::GrimmBoss.GrimmBossGrimmNpc.SpawnNpc(position, config);
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.Equals(hook, "SpawnNpc", StringComparison.OrdinalIgnoreCase) && args != null && args.Length >= 2 && args[0] is UnityEngine.Vector3 pos)
                return global::GrimmBoss.GrimmBossGrimmNpc.SpawnNpc(pos, args[1]);
            return null;
        }
    }
}
