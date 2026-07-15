using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ZombieHorde
{
    /// <summary>
    /// Spawns horde NPCs via GrimmNPC (NpcSpawn Harmony port) using SpawnNpc + NpcConfig.
    /// No project reference to GrimmNPC.dll — resolve via AppDomain SetData / assembly scan.
    /// </summary>
    public static class GrimmNpcBridge
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

        public static bool Available => _available;

        public static void Bind()
        {
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
                    UnityEngine.Debug.LogWarning("[ZombieHorde] GrimmNPC type not found. Load GrimmNPC before ZombieHorde (harmony.load GrimmNPC).");
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
                    UnityEngine.Debug.LogWarning("[ZombieHorde] GrimmNPC found but NpcConfig/NpcWear/NpcBelt nested types missing. Nested=" +
                        string.Join(",", GetNestedNames(_grimmType)));
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
                    UnityEngine.Debug.LogWarning("[ZombieHorde] GrimmNPC.SpawnNpc(Vector3, object) not found.");
                    return;
                }

                TryResolveInstance();
                _available = true;
                UnityEngine.Debug.Log("[ZombieHorde] GrimmNPC SpawnNpc integration bound (" + _grimmType.Assembly.GetName().Name + ").");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ZombieHorde] GrimmNPC bind failed: " + ex);
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
                string name = asm.GetName().Name ?? "";
                if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Facepunch", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Rust.", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("ZombieHorde", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("ZombieHorde_", StringComparison.OrdinalIgnoreCase))
                    continue;

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

        private static bool IsRaidWeapon(string shortName)
        {
            return shortName == "rocket.launcher" || shortName == "explosive.timed";
        }

        private static bool IsMeleeWeapon(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return false;
            ItemDefinition def = ItemManager.FindItemDefinition(shortName);
            if (def == null) return false;
            return def.GetComponent<ItemModEntity>() != null && shortName.Contains("knife")
                || shortName.Contains("machete")
                || shortName.Contains("sword")
                || shortName.Contains("bone.club")
                || shortName.Contains("salvaged.sword")
                || shortName.Contains("salvaged.cleaver")
                || shortName.Contains("longsword")
                || shortName.Contains("paddle")
                || shortName.Contains("pitchfork")
                || shortName.Contains("spear")
                || shortName.Contains("sickle")
                || shortName.Contains("mace")
                || shortName.Contains("hammer")
                || shortName.Contains("hatchet")
                || shortName.Contains("axe")
                || shortName.Contains("pickaxe")
                || shortName.Contains("torch")
                || shortName.Contains("candy.cane")
                || shortName == "rock"
                || shortName.Contains("chainsaw");
        }

        public static object BuildNpcConfig(ConfigData.MemberOptions.Loadout loadout, Horde horde)
        {
            if (loadout == null || _npcConfigType == null) return null;

            object npcCfg = Activator.CreateInstance(_npcConfigType);
            if (npcCfg == null) return null;

            Type wearSetType = typeof(HashSet<>).MakeGenericType(_npcWearType);
            object wearSet = Activator.CreateInstance(wearSetType);
            MethodInfo wearAdd = wearSetType.GetMethod("Add");
            if (loadout.WearItems != null)
            {
                foreach (var wear in loadout.WearItems)
                {
                    if (wear == null || string.IsNullOrWhiteSpace(wear.Shortname)) continue;
                    object w = Activator.CreateInstance(_npcWearType);
                    SetProp(w, _npcWearType, "ShortName", wear.Shortname);
                    SetProp(w, _npcWearType, "SkinID", wear.SkinID);
                    wearAdd.Invoke(wearSet, new[] { w });
                }
            }

            if (ConfigData.Configuration != null && ConfigData.Configuration.Member.GiveGlowEyes)
            {
                object w = Activator.CreateInstance(_npcWearType);
                SetProp(w, _npcWearType, "ShortName", "gloweyes");
                SetProp(w, _npcWearType, "SkinID", 0UL);
                wearAdd.Invoke(wearSet, new[] { w });
            }

            Type beltSetType = typeof(HashSet<>).MakeGenericType(_npcBeltType);
            object beltSet = Activator.CreateInstance(beltSetType);
            MethodInfo beltAdd = beltSetType.GetMethod("Add");
            bool hasRaidWeapon = false;
            bool hasMelee = false;
            if (loadout.BeltItems != null)
            {
                foreach (var belt in loadout.BeltItems)
                {
                    if (belt == null || string.IsNullOrWhiteSpace(belt.Shortname)) continue;
                    if (IsRaidWeapon(belt.Shortname)) hasRaidWeapon = true;
                    if (IsMeleeWeapon(belt.Shortname)) hasMelee = true;
                    object b = Activator.CreateInstance(_npcBeltType);
                    SetProp(b, _npcBeltType, "ShortName", belt.Shortname);
                    SetProp(b, _npcBeltType, "Amount", belt.Amount > 0 ? belt.Amount : 1);
                    SetProp(b, _npcBeltType, "SkinID", belt.SkinID);
                    var mods = new HashSet<string>();
                    if (belt.SubSpawn != null)
                    {
                        foreach (var sub in belt.SubSpawn)
                            if (sub != null && !string.IsNullOrWhiteSpace(sub.Shortname))
                                mods.Add(sub.Shortname);
                    }
                    SetProp(b, _npcBeltType, "Mods", mods);
                    SetProp(b, _npcBeltType, "Ammo", string.Empty);
                    beltAdd.Invoke(beltSet, new[] { b });
                }
            }

            HashSet<string> states = new HashSet<string> { "RoamState", "ChaseState", "CombatState" };
            // Online base raiding (config) + melee/explosives: use GrimmNPC raid states (replaces ChaosNPC ZombieAttack building logic).
            bool raidBases = ConfigData.Configuration?.Horde?.RaidOnlinePlayersAtBases ?? false;
            if (hasRaidWeapon)
                states.Add(hasMelee ? "RaidStateMelee" : "RaidState");
            else if (raidBases && hasMelee)
                states.Add("RaidStateMelee");

            float sense = loadout.Sensory?.SenseRange > 0 ? loadout.Sensory.SenseRange : 30f;
            float listen = loadout.Sensory?.ListenRange > 0 ? loadout.Sensory.ListenRange : sense * 0.5f;
            float health = loadout.Vitals?.Health > 0 ? loadout.Vitals.Health : 200f;
            float speed = loadout.Movement?.Speed > 0 ? loadout.Movement.Speed : 6.2f;

            // Local roam: use horde MaximumRoamDistance (from Local roam distance / monument). Else wide roam.
            float roam;
            if (horde != null && horde.IsLocalHorde && horde.MaximumRoamDistance > 0)
                roam = horde.MaximumRoamDistance;
            else if (ConfigData.Configuration?.Horde?.LocalRoam == true && ConfigData.Configuration.Horde.RoamDistance > 0)
                roam = ConfigData.Configuration.Horde.RoamDistance;
            else
                roam = 100f;

            float chase = sense * 2f;
            string name = loadout.Names != null && loadout.Names.Length > 0
                ? loadout.Names[UnityEngine.Random.Range(0, loadout.Names.Length)]
                : "Zombie";

            var member = ConfigData.Configuration?.Member;
            Vector3 home = horde != null ? horde.InitialPosition : Vector3.zero;
            // Facepunch ToVector3 expects "x y z" (not Unity's "(x, y, z)")
            string homePos = home != Vector3.zero
                ? $"{home.x} {home.y} {home.z}"
                : string.Empty;

            SetProp(npcCfg, _npcConfigType, "Name", name);
            SetProp(npcCfg, _npcConfigType, "WearItems", wearSet);
            SetProp(npcCfg, _npcConfigType, "BeltItems", beltSet);
            SetProp(npcCfg, _npcConfigType, "Kit", string.Empty);
            SetProp(npcCfg, _npcConfigType, "Health", health);
            SetProp(npcCfg, _npcConfigType, "RoamRange", roam);
            SetProp(npcCfg, _npcConfigType, "ChaseRange", chase);
            SetProp(npcCfg, _npcConfigType, "SenseRange", sense);
            SetProp(npcCfg, _npcConfigType, "ListenRange", listen);
            SetProp(npcCfg, _npcConfigType, "AttackRangeMultiplier", loadout.Sensory?.AttackRangeMultiplier > 0 ? loadout.Sensory.AttackRangeMultiplier : 1.5f);
            SetProp(npcCfg, _npcConfigType, "CheckVisionCone", loadout.Sensory?.IgnoreNonVisionSneakers ?? true);
            SetProp(npcCfg, _npcConfigType, "VisionCone", loadout.Sensory?.VisionCone > 0 ? loadout.Sensory.VisionCone : 135f);
            SetProp(npcCfg, _npcConfigType, "HostileTargetsOnly", false);
            SetProp(npcCfg, _npcConfigType, "DamageScale", loadout.DamageMultiplier > 0 ? loadout.DamageMultiplier : 1f);
            SetProp(npcCfg, _npcConfigType, "TurretDamageScale", 1f);
            SetProp(npcCfg, _npcConfigType, "AimConeScale", loadout.AimConeScale > 0 ? loadout.AimConeScale : 2f);
            SetProp(npcCfg, _npcConfigType, "DisableRadio", true);
            SetProp(npcCfg, _npcConfigType, "CanRunAwayWater", !(member?.CanSwim ?? true));
            SetProp(npcCfg, _npcConfigType, "CanSwim", member?.CanSwim ?? true);
            // Never let GrimmNPC sleep zombies — ZombieHorde dormant + LocalRoam need Think() for RoamState
            SetProp(npcCfg, _npcConfigType, "CanSleep", false);
            SetProp(npcCfg, _npcConfigType, "SleepDistance", 0f);
            SetProp(npcCfg, _npcConfigType, "Speed", speed);
            SetProp(npcCfg, _npcConfigType, "AreaMask", 25);
            SetProp(npcCfg, _npcConfigType, "AgentTypeID", -1372625422);
            SetProp(npcCfg, _npcConfigType, "HomePosition", homePos);
            SetProp(npcCfg, _npcConfigType, "MemoryDuration", ConfigData.Configuration?.Horde?.ForgetTime > 0 ? ConfigData.Configuration.Horde.ForgetTime : 10f);
            SetProp(npcCfg, _npcConfigType, "States", states);
            SetProp(npcCfg, _npcConfigType, "TrustSpawnPosition", true);
            SetProp(npcCfg, _npcConfigType, "IgnoreSleepingPlayers", member?.IgnoreSleepers ?? false);
            SetProp(npcCfg, _npcConfigType, "IgnoreSafeZonePlayers", loadout.Sensory?.IgnoreSafeZonePlayers ?? true);
            SetProp(npcCfg, _npcConfigType, "CanBeTargetedByAutoTurrets", member?.TargetedByTurrets ?? false);
            SetProp(npcCfg, _npcConfigType, "CanBeTargetedByGunTraps", member?.TargetedByTurrets ?? false);
            SetProp(npcCfg, _npcConfigType, "CanBeTargetedByFlameTurrets", member?.TargetedByTurrets ?? false);
            SetProp(npcCfg, _npcConfigType, "CanBeTargetedByAPC", member?.TargetedByAPC ?? false);
            SetProp(npcCfg, _npcConfigType, "InstantDeathIfHitHead", member?.HeadshotKills ?? false);
            SetProp(npcCfg, _npcConfigType, "HeadDamageScale", member?.HeadshotKills == true ? 100f : 1f);

            return npcCfg;
        }

        public static ZombieNPC Spawn(Vector3 position, ConfigData.MemberOptions.Loadout loadout, Horde horde)
        {
            if (loadout == null) return null;

            if (!TryGetInstance())
            {
                UnityEngine.Debug.LogWarning("[ZombieHorde] GrimmNPC.Instance not ready - cannot Spawn. Load GrimmNPC first.");
                return null;
            }

            object npcCfg = BuildNpcConfig(loadout, horde);
            if (npcCfg == null)
            {
                UnityEngine.Debug.LogWarning("[ZombieHorde] Failed to build GrimmNPC.NpcConfig.");
                return null;
            }

            ScientistNPC npc;
            try
            {
                npc = _spawnNpc.Invoke(_grimmInstance, new object[] { position, npcCfg }) as ScientistNPC;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ZombieHorde] GrimmNPC.SpawnNpc failed: " + ex);
                return null;
            }

            if (npc == null || npc.IsDestroyed)
            {
                UnityEngine.Debug.LogWarning("[ZombieHorde] GrimmNPC.SpawnNpc returned null.");
                return null;
            }

            try
            {
                if (npc.skinID != CustomNpcSkinId)
                    npc.skinID = CustomNpcSkinId;
            }
            catch { }

            ZombieNPC zombie = npc.gameObject.GetComponent<ZombieNPC>();
            if (zombie == null)
                zombie = npc.gameObject.AddComponent<ZombieNPC>();

            zombie.Initialize(npc, loadout, horde);

            // GrimmNPC already applies wear/belt from config; still apply loadout extras (ranges, glow eyes already in config)
            try
            {
                loadout.GiveToPlayer(zombie, applyInventory: false);
            }
            catch (Exception ex)
            {
                Compat.PrintWarning("GiveToPlayer post-spawn: " + ex.Message);
            }

            // GrimmNPC may finish brain/navigator init a tick later — apply when ready.
            BaseNavigator navigator = npc.Brain?.Navigator;
            if (navigator != null)
            {
                loadout.Movement?.ApplySettingsToNavigator(navigator);
                loadout.Sensory?.ApplySettingsToBrain(npc.Brain);
            }
            else
            {
                Compat.NextTick(() =>
                {
                    if (npc == null || npc.IsDestroyed) return;
                    loadout.Movement?.ApplySettingsToNavigator(npc.Brain?.Navigator);
                    loadout.Sensory?.ApplySettingsToBrain(npc.Brain);
                });
            }

            return zombie;
        }

        public static void SetBrainSleeping(ScientistNPC npc, bool sleep)
        {
            if (npc?.Brain == null) return;

            // Local roam needs Think/RoamState — never freeze the brain for dormant.
            if (sleep && ConfigData.Configuration?.Horde?.LocalRoam == true)
                sleep = false;

            try
            {
                var brain = npc.Brain;
                var field = brain.GetType().GetField("sleeping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? typeof(BaseAIBrain).GetField("sleeping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    field.SetValue(brain, sleep);

                npc.IsDormant = sleep;

                if (npc.NavAgent != null)
                    npc.NavAgent.enabled = !sleep;

                var nav = brain.Navigator;
                if (nav != null)
                {
                    if (sleep) nav.Pause();
                    else nav.Resume();
                }

                if (!sleep)
                {
                    try { brain.SwitchToState(AIState.Roam, 0); } catch { }
                }
            }
            catch { }
        }

        public static bool IsBrainSleeping(ScientistNPC npc)
        {
            if (npc?.Brain == null) return false;
            try
            {
                var brain = npc.Brain;
                var field = brain.GetType().GetField("sleeping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? typeof(BaseAIBrain).GetField("sleeping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    return (bool)field.GetValue(brain);
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Feed a TC / building into GrimmNPC raid AI (Foundations + CurrentRaidTarget).
        /// Does not AddState — GrimmNPC already owns AIState.Cooldown via RaidState/RaidStateMelee.
        /// </summary>
        public static bool AssignRaidTarget(ScientistNPC npc, BuildingPrivlidge priv)
        {
            if (npc == null || npc.IsDestroyed || priv == null || priv.IsDestroyed)
                return false;

            try
            {
                Type npcType = npc.GetType();
                HashSet<BuildingBlock> foundations = CollectBuildingBlocks(priv);

                PropertyInfo foundationsProp = npcType.GetProperty("Foundations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (foundationsProp != null && foundations.Count > 0)
                    foundationsProp.SetValue(npc, foundations);

                BaseCombatEntity raidTarget = null;
                if (foundations.Count > 0)
                {
                    float best = float.MaxValue;
                    Vector3 origin = npc.transform.position;
                    foreach (BuildingBlock block in foundations)
                    {
                        if (block == null || block.IsDestroyed) continue;
                        float d = (block.transform.position - origin).sqrMagnitude;
                        if (d < best)
                        {
                            best = d;
                            raidTarget = block;
                        }
                    }
                }
                if (raidTarget == null)
                    raidTarget = priv;

                PropertyInfo currentRaidProp = npcType.GetProperty("CurrentRaidTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                currentRaidProp?.SetValue(npc, raidTarget);

                // Fallback for RaidState when Foundations stayed empty (GetRaidTarget → PlayerTarget).
                if (foundations.Count == 0)
                {
                    PropertyInfo playerTargetProp = npcType.GetProperty("PlayerTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    playerTargetProp?.SetValue(npc, priv);
                }

                // Enter GrimmNPC's existing Cooldown slot (RaidState / RaidStateMelee) if present.
                if (npc.Brain != null && npc.Brain.states != null && npc.Brain.states.ContainsKey(AIState.Cooldown))
                {
                    try { npc.Brain.SwitchToState(AIState.Cooldown, 0); } catch { }
                }

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ZombieHorde] AssignRaidTarget failed: " + ex.Message);
                return false;
            }
        }

        public static bool HasGrimmRaidState(ScientistNPC npc)
        {
            if (npc?.Brain?.states == null) return false;
            return npc.Brain.states.ContainsKey(AIState.Cooldown);
        }

        private static HashSet<BuildingBlock> CollectBuildingBlocks(BuildingPrivlidge priv)
        {
            var result = new HashSet<BuildingBlock>();
            if (priv == null || priv.IsDestroyed) return result;

            try
            {
                BuildingManager.Building building = priv.GetBuilding();
                if (building?.buildingBlocks != null)
                {
                    foreach (BuildingBlock block in building.buildingBlocks)
                    {
                        if (block != null && !block.IsDestroyed)
                            result.Add(block);
                    }
                }
            }
            catch { }

            if (result.Count > 0) return result;

            try
            {
                List<BuildingBlock> list = Facepunch.Pool.Get<List<BuildingBlock>>();
                Vis.Entities(priv.transform.position, 25f, list, 1 << 21);
                for (int i = 0; i < list.Count; i++)
                {
                    BuildingBlock block = list[i];
                    if (block != null && !block.IsDestroyed)
                        result.Add(block);
                }
                Facepunch.Pool.FreeUnmanaged(ref list);
            }
            catch { }

            return result;
        }
    }
}
