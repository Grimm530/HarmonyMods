using System;
using System.Reflection;
using UnityEngine;

namespace ArmoredTrain
{
    /// <summary>
    /// Bridges the ported plugin's NpcSpawn.Call("SpawnNpc", pos, JObject) to the GrimmNPC Harmony mod
    /// (the NpcSpawn replacement). GrimmNPC.SpawnNpc(Vector3, object) accepts the same JObject the Oxide
    /// plugin builds, so the original NPC config JSON is forwarded unchanged.
    /// </summary>
    public static class ArmoredTrainGrimmNpc
    {
        private const string DataTypeKey = "GrimmNPC.Type";
        private const string DataInstanceKey = "GrimmNPC.Instance";

        private static bool _bound;
        private static Type _grimmType;
        private static MethodInfo _spawnNpc;
        private static object _grimmInstance;

        public static bool Available => _spawnNpc != null;

        public static void Bind()
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
                    Debug.LogWarning("[ArmoredTrain] GrimmNPC type not found. Load 0GrimmNPC before ArmoredTrain (harmony.load 0GrimmNPC). NPCs will not spawn.");
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
                    Debug.LogWarning("[ArmoredTrain] GrimmNPC.SpawnNpc(Vector3, object) not found.");
                    return;
                }

                if (!TryResolveInstance())
                    Debug.LogWarning("[ArmoredTrain] GrimmNPC bound but Instance not ready yet; will retry on spawn.");

                Debug.Log("[ArmoredTrain] GrimmNPC SpawnNpc integration bound (" + _grimmType.Assembly.GetName().Name + ").");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ArmoredTrain] GrimmNPC bind failed: " + ex);
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

        /// <summary>Forwards to GrimmNPC.SpawnNpc(Vector3, object). Returns the spawned ScientistNPC (as object) or null.</summary>
        public static object SpawnNpc(Vector3 position, object jObjectConfig)
        {
            if (_spawnNpc == null)
                Bind();
            if (_spawnNpc == null || !TryResolveInstance())
            {
                Debug.LogWarning("[ArmoredTrain] GrimmNPC not available - cannot spawn NPC.");
                return null;
            }

            try
            {
                return _spawnNpc.Invoke(_grimmInstance, new[] { position, jObjectConfig });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ArmoredTrain] GrimmNPC.SpawnNpc failed: " + ex);
                return null;
            }
        }
    }
}

namespace Oxide.Plugins
{
    /// <summary>
    /// NpcSpawn plugin stand-in. The ported NpcSpawnManager resolves SpawnNpc via reflection on this
    /// object's type and/or Call("SpawnNpc", pos, cfg); both route to the GrimmNPC bridge.
    /// </summary>
    public class NpcSpawnBridge : Oxide.Core.Plugins.Plugin
    {
        public NpcSpawnBridge() { Name = "NpcSpawn"; }

        public object SpawnNpc(UnityEngine.Vector3 position, object config)
        {
            return global::ArmoredTrain.ArmoredTrainGrimmNpc.SpawnNpc(position, config);
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.Equals(hook, "SpawnNpc", StringComparison.OrdinalIgnoreCase) && args != null && args.Length >= 2 && args[0] is UnityEngine.Vector3 pos)
                return global::ArmoredTrain.ArmoredTrainGrimmNpc.SpawnNpc(pos, args[1]);
            return null;
        }
    }
}
