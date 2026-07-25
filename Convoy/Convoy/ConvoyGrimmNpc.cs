using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Spawns convoy NPCs via GrimmNPC (NpcSpawn Harmony port) using SpawnNpc + NpcConfig.
    /// Mounted NPCs use IdleState + CombatStationaryState (same as Oxide Convoy to NpcSpawn).
    /// </summary>
    public static class ConvoyGrimmNpc
    {
        public const ulong CustomNpcSkinId = 11162132011012UL;
        private const string DataTypeKey = "GrimmNPC.Type";
        private const string DataInstanceKey = "GrimmNPC.Instance";

        private static bool _bound;
        private static bool _available;
        private static Type _grimmType;
        private static Type _npcConfigType;
        private static Type _npcWearType;
        private static Type _npcBeltType;
        private static MethodInfo _spawnNpc;
        private static object _grimmInstance;

        private static readonly HashSet<ulong> _liveNpcs = new HashSet<ulong>();

        public static bool Available => _available;

        public static int LiveNpcCount
        {
            get
            {
                int c = 0;
                foreach (var id in _liveNpcs) c++;
                return c;
            }
        }

        public static void Bind()
        {
            // Allow re-bind if GrimmNPC wasn't ready the first time (load-order).
            if (_bound && _available && TryResolveInstance()) return;
            _bound = true;
            _available = false;
            _grimmType = null;
            _npcConfigType = null;
            _npcWearType = null;
            _npcBeltType = null;
            _spawnNpc = null;
            _grimmInstance = null;

            try
            {
                _grimmType = FindGrimmNpcType();
                if (_grimmType == null)
                {
                    UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC type not found. Load 0GrimmNPC before Convoy (harmony.load 0GrimmNPC).");
                    return;
                }

                _npcConfigType = _grimmType.GetNestedType("NpcConfig", BindingFlags.Public | BindingFlags.NonPublic)
                    ?? Type.GetType(_grimmType.FullName + "+NpcConfig", false)
                    ?? FindNestedByName(_grimmType, "NpcConfig");
                _npcWearType = _grimmType.GetNestedType("NpcWear", BindingFlags.Public | BindingFlags.NonPublic)
                    ?? FindNestedByName(_grimmType, "NpcWear");
                _npcBeltType = _grimmType.GetNestedType("NpcBelt", BindingFlags.Public | BindingFlags.NonPublic)
                    ?? FindNestedByName(_grimmType, "NpcBelt");

                if (_npcConfigType == null || _npcWearType == null || _npcBeltType == null)
                {
                    UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC found but NpcConfig/NpcWear/NpcBelt nested types missing. Nested=" +
                        string.Join(",", GetNestedNames(_grimmType)));
                    return;
                }

                _spawnNpc = _grimmType.GetMethod("SpawnNpc", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Vector3), typeof(object) }, null);
                if (_spawnNpc == null)
                {
                    // Fallback: any SpawnNpc(Vector3, *)
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
                    UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC.SpawnNpc(Vector3, object) not found.");
                    return;
                }

                if (!TryResolveInstance())
                {
                    UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC type bound but Instance is null (mod not fully loaded yet). Will retry on spawn.");
                    // Still mark available so spawn path can retry Instance.
                }

                _available = true;
                UnityEngine.Debug.Log("[Convoy] GrimmNPC SpawnNpc integration bound (" + _grimmType.Assembly.GetName().Name + ").");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC bind failed: " + ex);
            }
        }

        private static Type FindGrimmNpcType()
        {
            // 1) Preferred: AppDomain SetData written by GrimmNPC.OnLoaded
            try
            {
                if (AppDomain.CurrentDomain.GetData(DataTypeKey) is Type fromData && fromData.Name == "GrimmNPC")
                    return fromData;
            }
            catch { }

            // 2) Direct GetType by full name (works with Harmony-renamed assemblies)
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType("GrimmNPC.GrimmNPC", false);
                    if (t != null) return t;
                }
                catch { }
            }

            // 3) Scan exported types only (cheaper / safer than GetTypes on every game assembly)
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name ?? "";
                // Skip obvious framework / game assemblies
                if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Facepunch", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Rust.", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Convoy", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Convoy_", StringComparison.OrdinalIgnoreCase))
                    continue;

                // HarmonyLoader renames to 0GrimmNPC_<guid> (legacy GrimmNPC_<guid> also matched)
                if (!name.StartsWith("0GrimmNPC", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("GrimmNPC", StringComparison.OrdinalIgnoreCase)
                    && name.IndexOf("GrimmNPC", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // Still try GetType — assembly name may not contain GrimmNPC if renamed oddly
                }

                try
                {
                    Type t = asm.GetType("GrimmNPC.GrimmNPC", false);
                    if (t != null) return t;

                    Type[] exported;
                    try { exported = asm.GetExportedTypes(); }
                    catch { continue; }

                    foreach (Type type in exported)
                    {
                        if (type != null && type.Name == "GrimmNPC" && type.Namespace == "GrimmNPC")
                            return type;
                    }
                }
                catch { }
            }

            // 4) Last resort: Instance SetData object.GetType()
            try
            {
                object inst = AppDomain.CurrentDomain.GetData(DataInstanceKey);
                if (inst != null) return inst.GetType();
            }
            catch { }

            return null;
        }

        private static Type FindNestedByName(Type parent, string name)
        {
            if (parent == null) return null;
            foreach (Type n in parent.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (n != null && n.Name == name) return n;
            }
            return null;
        }

        private static string GetNestedNames(Type parent)
        {
            if (parent == null) return "";
            var list = new List<string>();
            try
            {
                foreach (Type n in parent.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    if (n != null) list.Add(n.Name);
            }
            catch { }
            return string.Join(",", list);
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

        private static bool TryGetInstance()
        {
            if (!_available)
                Bind();
            if (!_available || _grimmType == null) return false;
            return TryResolveInstance() && _spawnNpc != null;
        }

        private static void SetProp(object instance, Type type, string name, object value)
        {
            if (instance == null || type == null) return;
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(instance, value);
                return;
            }
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            field?.SetValue(instance, value);
        }

        private static object BuildNpcConfig(ConvoyNpcConfig cfg, bool mounted)
        {
            object npcCfg = Activator.CreateInstance(_npcConfigType);
            if (npcCfg == null) return null;

            Type wearSetType = typeof(HashSet<>).MakeGenericType(_npcWearType);
            object wearSet = Activator.CreateInstance(wearSetType);
            MethodInfo wearAdd = wearSetType.GetMethod("Add");
            if (cfg.WearItems != null)
            {
                foreach (var wear in cfg.WearItems)
                {
                    if (wear == null || string.IsNullOrWhiteSpace(wear.ShortName)) continue;
                    object w = Activator.CreateInstance(_npcWearType);
                    SetProp(w, _npcWearType, "ShortName", wear.ShortName);
                    SetProp(w, _npcWearType, "SkinID", wear.SkinID);
                    wearAdd.Invoke(wearSet, new[] { w });
                }
            }

            Type beltSetType = typeof(HashSet<>).MakeGenericType(_npcBeltType);
            object beltSet = Activator.CreateInstance(beltSetType);
            MethodInfo beltAdd = beltSetType.GetMethod("Add");
            bool hasRaidWeapon = false;
            if (cfg.BeltItems != null)
            {
                foreach (var belt in cfg.BeltItems)
                {
                    if (belt == null || string.IsNullOrWhiteSpace(belt.ShortName)) continue;
                    if (belt.ShortName == "rocket.launcher" || belt.ShortName == "explosive.timed")
                        hasRaidWeapon = true;
                    object b = Activator.CreateInstance(_npcBeltType);
                    SetProp(b, _npcBeltType, "ShortName", belt.ShortName);
                    SetProp(b, _npcBeltType, "Amount", belt.Amount > 0 ? belt.Amount : 1);
                    SetProp(b, _npcBeltType, "SkinID", belt.SkinID);
                    SetProp(b, _npcBeltType, "Mods", belt.Mods != null ? new HashSet<string>(belt.Mods) : new HashSet<string>());
                    SetProp(b, _npcBeltType, "Ammo", belt.Ammo ?? string.Empty);
                    beltAdd.Invoke(beltSet, new[] { b });
                }
            }

            HashSet<string> states;
            if (mounted)
                states = new HashSet<string> { "IdleState", "CombatStationaryState" };
            else if (hasRaidWeapon)
                states = new HashSet<string> { "RaidState", "RoamState", "ChaseState", "CombatState" };
            else
                states = new HashSet<string> { "RoamState", "ChaseState", "CombatState" };

            SetProp(npcCfg, _npcConfigType, "Name", cfg.DisplayName ?? "Convoy NPC");
            SetProp(npcCfg, _npcConfigType, "WearItems", wearSet);
            SetProp(npcCfg, _npcConfigType, "BeltItems", beltSet);
            SetProp(npcCfg, _npcConfigType, "Kit", string.Empty);
            SetProp(npcCfg, _npcConfigType, "Health", cfg.Health > 0 ? cfg.Health : 100f);
            SetProp(npcCfg, _npcConfigType, "RoamRange", mounted ? 0f : (cfg.RoamRange > 0 ? cfg.RoamRange : 15f));
            SetProp(npcCfg, _npcConfigType, "ChaseRange", mounted ? 0f : (cfg.ChaseRange > 0 ? cfg.ChaseRange : 100f));
            SetProp(npcCfg, _npcConfigType, "SenseRange", cfg.SenseRange > 0 ? cfg.SenseRange : 60f);
            SetProp(npcCfg, _npcConfigType, "ListenRange", (cfg.SenseRange > 0 ? cfg.SenseRange : 60f) * 0.5f);
            SetProp(npcCfg, _npcConfigType, "AttackRangeMultiplier", cfg.AttackRangeMultiplier > 0 ? cfg.AttackRangeMultiplier : 1f);
            SetProp(npcCfg, _npcConfigType, "CheckVisionCone", mounted ? false : cfg.CheckVisionCone);
            SetProp(npcCfg, _npcConfigType, "VisionCone", cfg.VisionCone > 0 ? cfg.VisionCone : 135f);
            SetProp(npcCfg, _npcConfigType, "HostileTargetsOnly", false);
            SetProp(npcCfg, _npcConfigType, "DamageScale", cfg.DamageScale > 0 ? cfg.DamageScale : 1f);
            SetProp(npcCfg, _npcConfigType, "TurretDamageScale", cfg.TurretDamageScale > 0 ? cfg.TurretDamageScale : 1f);
            SetProp(npcCfg, _npcConfigType, "AimConeScale", cfg.AimConeScale > 0 ? cfg.AimConeScale : 1f);
            SetProp(npcCfg, _npcConfigType, "DisableRadio", cfg.DisableRadio);
            SetProp(npcCfg, _npcConfigType, "CanRunAwayWater", true);
            SetProp(npcCfg, _npcConfigType, "CanSwim", false);
            SetProp(npcCfg, _npcConfigType, "CanSleep", false);
            SetProp(npcCfg, _npcConfigType, "SleepDistance", 100f);
            SetProp(npcCfg, _npcConfigType, "Speed", mounted ? 0f : (cfg.Speed > 0 ? cfg.Speed : 5f));
            SetProp(npcCfg, _npcConfigType, "AreaMask", 1);
            SetProp(npcCfg, _npcConfigType, "AgentTypeID", -1372625422);
            SetProp(npcCfg, _npcConfigType, "HomePosition", string.Empty);
            SetProp(npcCfg, _npcConfigType, "MemoryDuration", cfg.MemoryDuration > 0 ? cfg.MemoryDuration : 10f);
            SetProp(npcCfg, _npcConfigType, "States", states);
            SetProp(npcCfg, _npcConfigType, "TrustSpawnPosition", true);

            return npcCfg;
        }

        public static ScientistNPC SpawnNpc(ConvoyNpcConfig cfg, Vector3 position, Quaternion rotation, bool mounted = true)
        {
            if (cfg == null) return null;

            if (!TryGetInstance())
            {
                UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC.Instance not ready - cannot SpawnNpc. Load GrimmNPC first.");
                return null;
            }

            object npcCfg = BuildNpcConfig(cfg, mounted);
            if (npcCfg == null)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Failed to build GrimmNPC.NpcConfig.");
                return null;
            }

            ScientistNPC npc;
            try
            {
                npc = _spawnNpc.Invoke(_grimmInstance, new object[] { position, npcCfg }) as ScientistNPC;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC.SpawnNpc failed: " + ex);
                return null;
            }

            if (npc == null || npc.IsDestroyed)
            {
                UnityEngine.Debug.LogWarning("[Convoy] GrimmNPC.SpawnNpc returned null for preset '" + cfg.PresetName + "'.");
                return null;
            }

            try
            {
                npc.transform.rotation = rotation;
                npc.ServerRotation = rotation;
                if (npc.eyes != null)
                    npc.eyes.rotation = rotation;
            }
            catch { }

            if (mounted)
                PauseNavigator(npc);

            if (npc.net != null)
            {
                ulong netId = (ulong)npc.net.ID.Value;
                _liveNpcs.Add(netId);
                ConvoyState.NpcsAlive = true;
            }

            return npc;
        }

        private static void PauseNavigator(ScientistNPC npc)
        {
            try
            {
                var brain = npc?.Brain;
                var nav = brain?.Navigator;
                if (nav == null) return;
                nav.CanUseNavMesh = false;
                nav.Pause();
            }
            catch { }
        }

        /// <summary>After combat dismount: enable navmesh + brain so NPCs fight on foot.</summary>
        public static void EnableGroundCombat(ScientistNPC npc, ConvoyNpcConfig cfg)
        {
            if (npc == null || npc.IsDestroyed) return;
            try
            {
                var brain = npc.Brain;
                var nav = brain?.Navigator;
                if (nav != null)
                {
                    nav.CanUseNavMesh = true;
                    nav.Resume();
                    float roam = cfg != null && cfg.RoamRange > 0 ? cfg.RoamRange : 20f;
                    nav.MaxRoamDistanceFromHome = roam;
                }

                if (brain != null && cfg != null)
                {
                    if (cfg.SenseRange > 0) brain.SenseRange = cfg.SenseRange;
                    brain.TargetLostRange = (cfg.SenseRange > 0 ? cfg.SenseRange : 50f) * 2f;
                }

                if (_available && npc.net != null && _grimmType != null)
                {
                    ulong netId = (ulong)npc.net.ID.Value;
                    object data = _grimmType.GetMethod("GetNpcData", BindingFlags.Public | BindingFlags.Static)
                        ?.Invoke(null, new object[] { netId });
                    if (data != null)
                    {
                        SetObjMember(data, "HomePosition", npc.transform.position);
                        SetObjMember(data, "RoamRange", cfg != null && cfg.RoamRange > 0 ? cfg.RoamRange : 30f);
                        SetObjMember(data, "ChaseRange", cfg != null && cfg.ChaseRange > 0 ? cfg.ChaseRange : 80f);
                        SetObjMember(data, "CanSwim", false);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] EnableGroundCombat: " + ex.Message);
            }
        }

        /// <summary>Immediate damage-aggro into GrimmNPC CurrentTarget + senses memory.</summary>
        public static void ForceAggro(ScientistNPC npc, BasePlayer attacker)
        {
            if (npc == null || npc.IsDestroyed || attacker == null || attacker.IsDestroyed) return;
            try
            {
                Type t = npc.GetType();
                MethodInfo setKnown = t.GetMethod("SetKnown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(BaseEntity) }, null);
                setKnown?.Invoke(npc, new object[] { attacker });

                PropertyInfo currentTarget = t.GetProperty("CurrentTarget", BindingFlags.Public | BindingFlags.Instance);
                currentTarget?.SetValue(npc, attacker);

                if (npc.Brain != null)
                {
                    var brain = npc.Brain;
                    var sleepField = brain.GetType().GetField("sleeping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? typeof(BaseAIBrain).GetField("sleeping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    sleepField?.SetValue(brain, false);
                    brain.Navigator?.Resume();
                    if (brain.Senses?.Memory != null)
                        brain.Senses.Memory.SetKnown(attacker, npc, brain.Senses);
                }

                npc.IsDormant = false;
                if (npc.NavAgent != null)
                    npc.NavAgent.enabled = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] ForceAggro: " + ex.Message);
            }
        }

        private static void SetObjMember(object instance, string name, object value)
        {
            if (instance == null) return;
            var t = instance.GetType();
            var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(instance, value);
                return;
            }
            var field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            field?.SetValue(instance, value);
        }

        /// <summary>Before remounting: freeze nav again.</summary>
        public static void PauseForMount(ScientistNPC npc)
        {
            PauseNavigator(npc);
        }

        public static bool IsTracked(ulong netId) => netId != 0 && _liveNpcs.Contains(netId);

        public static void Unregister(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return;
            Unregister((ulong)npc.net.ID.Value);
        }

        public static void Unregister(ulong netId)
        {
            if (netId == 0) return;
            _liveNpcs.Remove(netId);
            if (_liveNpcs.Count == 0)
                ConvoyState.NpcsAlive = false;
        }

        public static bool IsConvoyNpc(ulong netId) => _liveNpcs.Contains(netId);

        public static void ClearAll()
        {
            var ids = new List<ulong>(_liveNpcs);
            foreach (var id in ids)
                Unregister(id);
            _liveNpcs.Clear();
            ConvoyState.NpcsAlive = false;
        }
    }
}
