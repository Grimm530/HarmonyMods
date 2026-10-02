// GrimmBoss - encounter gameplay on GrimmNPC2 + GEN2 (Rust.Ai.Gen2). Does not own FSM/sense/nav stepping.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using Rust.Ai.Gen2;
using UnityEngine;
using UnityEngine.AI;

namespace Oxide.Plugins
{
    public enum GrimmBossGender { Random, Male, Female }

    public enum GrimmBossSkinTone { Random, Lightest, Light, Dark, Darkest }

    internal static class GrimmNpc2Interop
    {
        private const string GrimmMainTypeName = "GrimmNPC2.GrimmNPC2";

        private static Type _tData;
        private static Type _tGrimm;
        private static Type _tFsmKind;
        private static Type _tNpcKind;
        private static MethodInfo _registerPending;
        private static MethodInfo _getNpcData;
        private static MethodInfo _unregisterNpc;
        private static MethodInfo _ResolveEntity;
        private static MethodInfo _setHelperPhase;
        private static MethodInfo _Propagate;
        private static MethodInfo _SetDestination;
        private static MethodInfo _normalize;
        private static PropertyInfo _propOwnerNetId;
        private static PropertyInfo _instanceProp;
        private static Assembly _boundGrimmAssembly;
        private static bool _ready;
        private static bool _warnedAssemblyMissing;
        private static bool _warnedApiIncomplete;

        public static bool IsReady => _ready;

        /// <summary>Assembly + reflection are ready and GrimmNPC2.Instance is set (Harmony OnLoaded has run).</summary>
        public static bool IsGrimmRuntimeReady()
        {
            if (!_ready) return false;
            if (_instanceProp == null) return false;
            try
            {
                return _instanceProp.GetValue(null) != null;
            }
            catch
            {
                return false;
            }
        }

        static GrimmNpc2Interop()
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                if (args.LoadedAssembly.GetType(GrimmMainTypeName, false) != null)
                    Init();
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// Multiple GrimmNPC2.dll copies can exist (build output vs HarmonyMods, or stale + fresh after harmony.load).
        /// The loader sets <c>Instance</c> only on the live mod assembly; prefer that over an older copy left in the AppDomain.
        /// </summary>
        private static object TryGetGrimmInstanceFromAssembly(Assembly a)
        {
            if (a == null) return null;
            try
            {
                Type t = a.GetType(GrimmMainTypeName, false);
                if (t == null) return null;
                PropertyInfo p = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                return p?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static Assembly FindGrimmAssembly()
        {
            Assembly first = null;
            Assembly fromHarmonyMods = null;
            Assembly withInstance = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                try
                {
                    if (a.GetType(GrimmMainTypeName, false) == null) continue;
                    if (first == null) first = a;
                    if (TryGetGrimmInstanceFromAssembly(a) != null)
                        withInstance = a;
                    string loc = a.Location ?? "";
                    if (loc.IndexOf("HarmonyMods", StringComparison.OrdinalIgnoreCase) >= 0)
                        fromHarmonyMods = a;
                }
                catch
                {
                    // ignored
                }
            }

            // Prefer any copy where OnLoaded has run (do not require Location; it can be empty for some hosts).
            if (withInstance != null) return withInstance;
            return fromHarmonyMods ?? first;
        }

        private static void ClearBindings()
        {
            _tGrimm = null;
            _tData = null;
            _tFsmKind = null;
            _tNpcKind = null;
            _registerPending = null;
            _getNpcData = null;
            _unregisterNpc = null;
            _ResolveEntity = null;
            _setHelperPhase = null;
            _Propagate = null;
            _SetDestination = null;
            _normalize = null;
            _propOwnerNetId = null;
            _instanceProp = null;
            _boundGrimmAssembly = null;
            _ready = false;
        }

        internal static class Fsm
        {
            public const int Unknown = 0;
            public const int DefaultScientist = 1;
            public const int Heavy = 2;
            public const int Shotgun = 3;
        }

        internal static class Kind
        {
            public const int Unspecified = 0;
            public const int Primary = 1;
            public const int Helper = 2;
            public const int Minion = 3;
        }

        public static bool Init()
        {
            Assembly asm = FindGrimmAssembly();
            if (asm == null)
            {
                if (!_warnedAssemblyMissing)
                {
                    _warnedAssemblyMissing = true;
                    Debug.LogWarning(" GrimmNPC2 assembly not found yet (Harmony mod loads after Oxide). Will  when it loads. Ensure GrimmNPC2 is installed.");
                }

                return false;
            }

            // Same assembly already bound and runtime live; skip re-reflection.
            if (_ready && _boundGrimmAssembly != null && ReferenceEquals(_boundGrimmAssembly, asm) && IsGrimmRuntimeReady())
                return true;

            if (_ready)
                ClearBindings();

            _warnedAssemblyMissing = false;

            _tGrimm = asm.GetType(GrimmMainTypeName, false);
            _tData = asm.GetType("GrimmNPC2.CustomNpcData2", false);
            _tFsmKind = asm.GetType("GrimmNPC2.ScientistGen2FsmKind", false);
            _tNpcKind = asm.GetType("GrimmNPC2.CustomNpcKind", false);

            if (_tGrimm == null || _tData == null)
            {
                if (!_warnedApiIncomplete)
                {
                    _warnedApiIncomplete = true;
                    Debug.LogWarning(" GrimmNPC2 types GrimmNPC2 or CustomNpcData2 missing inside assembly.");
                }

                return false;
            }

            _registerPending = _tGrimm.GetMethod("RegisterPending", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(BaseEntity), _tData }, null);
            _getNpcData = _tGrimm.GetMethod("GetNpcData", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ulong) }, null);
            _unregisterNpc = _tGrimm.GetMethod("UnregisterNpc", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ulong) }, null);
            foreach (var m in _tGrimm.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "ResolveEntity") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(ulong) && ps[1].ParameterType.IsByRef && ps[1].ParameterType.GetElementType() == typeof(BaseEntity))
                {
                    _ResolveEntity = m;
                    break;
                }
            }

            _setHelperPhase = _tGrimm.GetMethod("SetRuntimeHelperPhaseActive", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ulong), typeof(bool) }, null);
            _Propagate = _tGrimm.GetMethod("PropagateTargetToAssistGroup", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(BaseEntity), typeof(BaseEntity), typeof(bool) }, null);
            _SetDestination = _tGrimm.GetMethod("SetDestinationRespectingHomeTether", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(BaseEntity), typeof(Vector3) }, null);
            _normalize = _tData.GetMethod("Normalize", Type.EmptyTypes);
            _propOwnerNetId = _tData.GetProperty("OwnerNetId");
            _instanceProp = _tGrimm.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

            _ready = _registerPending != null && _getNpcData != null && _normalize != null && _tFsmKind != null && _tNpcKind != null;
            if (!_ready)
            {
                if (!_warnedApiIncomplete)
                {
                    _warnedApiIncomplete = true;
                    Debug.LogWarning(" GrimmNPC2 API incomplete (RegisterPending, GetNpcData, Normalize, ScientistGen2FsmKind, CustomNpcKind).");
                }

                ClearBindings();
                return false;
            }

            _warnedApiIncomplete = false;
            _boundGrimmAssembly = asm;
            return true;
        }

        public static object CreateCustomNpcData2()
        {
            if (_tData == null) return null;
            return Activator.CreateInstance(_tData);
        }

        public static void Normalize(object data)
        {
            if (data == null || _normalize == null) return;
            _normalize.Invoke(data, null);
        }

        public static object ToFsmEnum(int value)
        {
            if (_tFsmKind == null) return value;
            return Enum.ToObject(_tFsmKind, value);
        }

        public static object ToKindEnum(int value)
        {
            if (_tNpcKind == null) return value;
            return Enum.ToObject(_tNpcKind, value);
        }

        public static void Set(object data, string propertyName, object value)
        {
            if (data == null || _tData == null) return;
            PropertyInfo p = _tData.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
                p.SetValue(data, value);
        }

        public static bool RegisterPending(BaseEntity entity, object data)
        {
            if (!_ready || entity == null || data == null) return false;
            return (bool)_registerPending.Invoke(null, new[] { entity, data });
        }

        public static object GetNpcData(ulong netId)
        {
            if (!_ready) return null;
            return _getNpcData.Invoke(null, new object[] { netId });
        }

        public static ulong GetOwnerNetId(object data)
        {
            if (data == null || _propOwnerNetId == null) return 0;
            object v = _propOwnerNetId.GetValue(data);
            return v is ulong u ? u : 0UL;
        }

        public static void UnregisterNpc(ulong netId)
        {
            if (!_ready) return;
            _unregisterNpc?.Invoke(null, new object[] { netId });
        }

        public static bool ResolveEntity(ulong netId, out BaseEntity entity)
        {
            entity = null;
            if (_ResolveEntity == null) return false;
            var args = new object[] { netId, null };
            bool ok = (bool)_ResolveEntity.Invoke(null, args);
            entity = args[1] as BaseEntity;
            return ok && entity != null;
        }

        public static void SetRuntimeHelperPhaseActive(ulong netId, bool active)
        {
            _setHelperPhase?.Invoke(null, new object[] { netId, active });
        }

        public static int PropagateTargetToAssistGroup(BaseEntity source, BaseEntity target, bool bypassCooldown)
        {
            if (_Propagate == null) return 0;
            return (int)_Propagate.Invoke(null, new object[] { source, target, bypassCooldown });
        }

        public static bool SetDestinationRespectingHomeTether(BaseEntity npc, Vector3 worldDestination)
        {
            if (_SetDestination == null) return false;
            return (bool)_SetDestination.Invoke(null, new object[] { npc, worldDestination });
        }
    }

    [Info("GrimmBoss", "Grimm530", "1.0.0")]
    internal class GrimmBoss : RustPlugin
    {
        #region Plugin config

        private PluginConfig _pluginConfig;
        private Timer _grimmLoadReTimer;
        private bool _loggedGrimmSpawnBlocked;
        private bool _loggedGrimmRuntimeNotReady;
        private bool _warnedGen2PrefabInvalid;
        private readonly Dictionary<string, string> _gen2HeavyPrefabResolveCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _gen2ResolveFailedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void DebugLog(string message)
        {
            if (_pluginConfig != null && _pluginConfig.Debug) Puts(message);
        }

        private static bool TrySnapToNavMesh(ref Vector3 pos, float maxDistance = 40f)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            {
                pos = hit.position;
                return true;
            }

            return false;
        }

        private static void TryKillEntity(BaseEntity ent)
        {
            if (ent == null || ent.IsDestroyed) return;
            try
            {
                ent.Kill();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(" Kill failed during failed-spawn cleanup: " + ex.Message);
            }
        }

        private void LoadCustomMapGlobal()
        {
            _customMapPositionsByBossName.Clear();
            string path = Path.Combine(Interface.Oxide.DataDirectory, "GrimmBoss", "CustomMap", "Global.json");
            if (!File.Exists(path)) return;

            try
            {
                var f = JsonConvert.DeserializeObject<CustomMapGlobalFile>(File.ReadAllText(path));
                if (f?.ListOfBosses == null) return;
                for (int i = 0; i < f.ListOfBosses.Count; i++)
                {
                    CustomMapGlobalBossEntry e = f.ListOfBosses[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.BossName)) continue;
                    if (e.ListOfPositions == null || e.ListOfPositions.Count == 0) continue;
                    string key = e.BossName.Trim();
                    var copy = new List<string>(e.ListOfPositions.Count);
                    for (int j = 0; j < e.ListOfPositions.Count; j++)
                    {
                        string s = e.ListOfPositions[j];
                        if (!string.IsNullOrWhiteSpace(s)) copy.Add(s.Trim());
                    }

                    if (copy.Count > 0) _customMapPositionsByBossName[key] = copy;
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"GrimmBoss: CustomMap/Global.json: {ex.Message}");
            }
        }

        protected override void LoadDefaultConfig()
        {
            _pluginConfig = PluginConfig.Default();
            _pluginConfig.PluginVersion = Version;
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _pluginConfig = Config.ReadObject<PluginConfig>();
            if (_pluginConfig == null)
            {
                Puts(" ERROR: config missing; restoring default.");
                LoadDefaultConfig();
                return;
            }

            _pluginConfig.GuiAnnouncements ??= PluginConfig.Default().GuiAnnouncements;
            _pluginConfig.Notify ??= PluginConfig.Default().Notify;
            _pluginConfig.Discord ??= PluginConfig.Default().Discord;
            if (string.IsNullOrWhiteSpace(_pluginConfig.Gen1LoadoutProxyPrefab))
                _pluginConfig.Gen1LoadoutProxyPrefab = PluginConfig.Default().Gen1LoadoutProxyPrefab;
            if (_pluginConfig.PluginVersion < Version) _pluginConfig.PluginVersion = Version;
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_pluginConfig);

        internal bool ValidateGen2ScientistPrefab(string configuredPath, out string manifestPath)
        {
            manifestPath = null;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                if (!_warnedGen2PrefabInvalid)
                {
                    _warnedGen2PrefabInvalid = true;
                    PrintError(" GEN2 scientist prefab path is empty. Set \"GEN2 scientist prefab\" in oxide/config/GrimmBoss.json or per-boss Gen2BossPrefab.");
                }

                return false;
            }

            string key = configuredPath.Trim();
            if (_gen2ResolveFailedKeys.Contains(key))
            {
                manifestPath = null;
                return false;
            }

            if (_gen2HeavyPrefabResolveCache.TryGetValue(key, out string cached))
            {
                GameObject goCached = GameManager.server.FindPrefab(cached);
                if (goCached != null && Gen2PrefabPrototypeHasScientistNpc2(goCached))
                {
                    manifestPath = cached;
                    _gen2ResolveFailedKeys.Remove(key);
                    return true;
                }

                _gen2HeavyPrefabResolveCache.Remove(key);
                _gen2ResolveFailedKeys.Remove(key);
            }

            foreach (string candidate in BuildGen2HeavyCandidateList(key))
            {
                GameObject go = GameManager.server.FindPrefab(candidate);
                if (go == null || !Gen2PrefabPrototypeHasScientistNpc2(go))
                    continue;
                manifestPath = candidate;
                _gen2HeavyPrefabResolveCache[key] = candidate;
                _gen2ResolveFailedKeys.Remove(key);
                if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                    DebugLog(" Resolved GEN2 Heavy prefab (configured path missing in this build): " + candidate);
                return true;
            }

            _gen2ResolveFailedKeys.Add(key);

            if (!_warnedGen2PrefabInvalid)
            {
                _warnedGen2PrefabInvalid = true;
                PrintError(" No ScientistNPC2 prefab resolved (config + pathToGuid \"scientistnpc2\" + GameManifest.entities paths containing \"scientist\", preferring \"heavy\"; prototype must include ScientistNPC2). Config: " + key + ". Reload after Rust update, or set \"GEN2 scientist prefab\" to a working path (try: find a spawned GEN2 scientist in-game and use its PrefabName). Gen1 scientistnpc_* under rust.ai/agents/... are not ScientistNPC2.");
            }

            return false;
        }

        private static List<string> BuildGen2HeavyCandidateList(string configuredTrimmed)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(configuredTrimmed) && seen.Add(configuredTrimmed))
                list.Add(configuredTrimmed);

            // Stock GEN2 scientists use paths like assets/prefabs/npc/scientist/scientistnpc2_heavy.prefab (manifest name "scientistnpc2").
            // Gen1 lists often show scientistnpc_* under rust.ai/agents/...; those are HumanNPC/legacy, not ScientistNPC2.
            var heavyFirst = new List<string>();
            var otherGen2 = new List<string>();
            try
            {
                GameManifest.Load();
                foreach (var kv in GameManifest.pathToGuid)
                {
                    string path = kv.Key;
                    if (string.IsNullOrEmpty(path)) continue;
                    string pl = path.ToLowerInvariant();
                    if (!pl.Contains("scientistnpc2") || !seen.Add(path))
                        continue;
                    if (pl.Contains("heavy"))
                        heavyFirst.Add(path);
                    else
                        otherGen2.Add(path);
                }
            }
            catch
            {
                // ignored
            }

            heavyFirst.Sort(StringComparer.OrdinalIgnoreCase);
            otherGen2.Sort(StringComparer.OrdinalIgnoreCase);
            list.AddRange(heavyFirst);
            list.AddRange(otherGen2);

            // Spawnable entity list (same as server entity.spawn). Some builds use filenames without the substring "scientistnpc2".
            var entHeavy = new List<string>();
            var entOther = new List<string>();
            try
            {
                string[] entities = GameManifest.Current?.entities;
                if (entities != null)
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        string path = entities[i];
                        if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                        string pl = path.ToLowerInvariant();
                        if (!pl.EndsWith(".prefab") || !pl.Contains("scientist")) continue;
                        if (pl.Contains("heavy"))
                            entHeavy.Add(path);
                        else
                            entOther.Add(path);
                    }
                }
            }
            catch
            {
                // ignored
            }

            entHeavy.Sort(StringComparer.OrdinalIgnoreCase);
            entOther.Sort(StringComparer.OrdinalIgnoreCase);
            list.AddRange(entHeavy);
            list.AddRange(entOther);
            return list;
        }

        private static bool Gen2PrefabPrototypeHasScientistNpc2(GameObject go)
        {
            if (go == null) return false;
            if (go.GetComponent<ScientistNPC2>() != null) return true;
            return go.GetComponentInChildren<ScientistNPC2>(true) != null;
        }

        private class PluginConfig
        {
            [JsonProperty("Prefix of chat messages")] public string Prefix { get; set; }
            [JsonProperty("Do you use the chat? [true/false]")] public bool IsChat { get; set; }
            [JsonProperty("Enable debug logging? [true/false]")] public bool Debug { get; set; }
            [JsonProperty("GUI Announcements setting")] public GuiAnnouncementsConfig GuiAnnouncements { get; set; }
            [JsonProperty("Notify setting")] public NotifyConfig Notify { get; set; }
            [JsonProperty("Discord setting (only for users DiscordMessages plugin)")] public DiscordConfig Discord { get; set; }
            [JsonProperty("Use the PVE mode of the plugin? (only for users PveMode plugin)")] public bool Pve { get; set; }
            [JsonProperty("NPC Turret Damage Multiplier (metadata / GrimmNPC2 incoming turret scale baseline)")] public float TurretDamageScale { get; set; }
            [JsonProperty("Total maintained number of bosses on the map at once")] public int AmountBosses { get; set; }
            [JsonProperty("GEN2 scientist prefab (Heavy default)")] public string BossPrefab { get; set; }
            /// <summary>Gen1 HumanNPC scientist used only as a child "mannequin" for wear/belt visuals (PlayerInventory requires BasePlayer). Default matches BossMonster / NpcSpawn.</summary>
            [JsonProperty("GEN1 scientist prefab for wear-belt loadout proxy (child of GEN2 boss)")] public string Gen1LoadoutProxyPrefab { get; set; }
            [JsonProperty("Configuration version")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig Default()
            {
                return new PluginConfig
                {
                    Prefix = "",
                    IsChat = true,
                    Debug = false,
                    GuiAnnouncements = new GuiAnnouncementsConfig
                    {
                        IsGuiAnnouncements = false,
                        BannerColor = "Orange",
                        TextColor = "White",
                        ApiAdjustVPosition = 0.03f
                    },
                    Notify = new NotifyConfig { IsNotify = false, Type = "0" },
                    Discord = new DiscordConfig
                    {
                        IsDiscord = false,
                        WebhookUrl = "",
                        EmbedColor = 13516583,
                        Keys = new HashSet<string> { "Start", "Finish" }
                    },
                    Pve = false,
                    TurretDamageScale = 0.5f,
                    AmountBosses = 5,
                    BossPrefab = "assets/prefabs/npc/scientist/scientistnpc2_heavy.prefab",
                    Gen1LoadoutProxyPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab",
                    PluginVersion = new VersionNumber()
                };
            }
        }

        private class GuiAnnouncementsConfig
        {
            [JsonProperty("Do you use the GUI Announcements plugin? [true/false]")] public bool IsGuiAnnouncements { get; set; }
            [JsonProperty("Banner color")] public string BannerColor { get; set; }
            [JsonProperty("Text color")] public string TextColor { get; set; }
            [JsonProperty("Adjust Vertical Position")] public float ApiAdjustVPosition { get; set; }
        }

        private class NotifyConfig
        {
            [JsonProperty("Do you use the Notify plugin? [true/false]")] public bool IsNotify { get; set; }
            [JsonProperty("Type")] public string Type { get; set; }
        }

        private class DiscordConfig
        {
            [JsonProperty("Do you use the Discord Messages plugin? [true/false]")] public bool IsDiscord { get; set; }
            [JsonProperty("Webhook URL")] public string WebhookUrl { get; set; }
            [JsonProperty("Embed Color (DECIMAL)")] public int EmbedColor { get; set; }
            [JsonProperty("Keys of required messages")] public HashSet<string> Keys { get; set; }
        }

        #endregion

        #region Boss data (BossMonster JSON compatible)

        public class NpcWear
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinID { get; set; }
        }

        public class NpcBelt
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("Amount")] public int Amount { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinID { get; set; }
            [JsonProperty("Mods")] public HashSet<string> Mods { get; set; }
            [JsonProperty("Ammo")] public string Ammo { get; set; }
        }

        public class ColorConfig
        {
            [JsonProperty("r")] public float R { get; set; }
            [JsonProperty("g")] public float G { get; set; }
            [JsonProperty("b")] public float B { get; set; }
        }

        public class MarkerConfig
        {
            [JsonProperty("Do you use the Marker? [true/false]")] public bool IsMarker { get; set; }
            [JsonProperty("Radius")] public float Radius { get; set; }
            [JsonProperty("Transparency")] public float Alpha { get; set; }
            [JsonProperty("Marker color")] public ColorConfig Color { get; set; }
        }

        public class NpcEconomic
        {
            [JsonProperty("Economics")] public double Economics { get; set; }
            [JsonProperty("Server Rewards (minimum 1)")] public int ServerRewards { get; set; }
            [JsonProperty("IQEconomic (minimum 1)")] public int IQEconomic { get; set; }
            [JsonProperty("XPerience")] public double XPerience { get; set; }
        }

        public class MonumentPositionsConfig
        {
            [JsonProperty("Name of monument")] public string Name { get; set; }
            /// <summary>JSON array order is preserved (HashSet was not; wrong spawn / ignored monuments).</summary>
            [JsonProperty("List of positions")] public List<string> Positions { get; set; }
        }

        public class ItemConfig
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("Minimum")] public int MinAmount { get; set; }
            [JsonProperty("Maximum")] public int MaxAmount { get; set; }
            [JsonProperty("Chance probability [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty("Is this a blueprint? [true/false]")] public bool IsBluePrint { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinID { get; set; }
            [JsonProperty("Text (empty - default)")] public string Text { get; set; }
            [JsonProperty("Name (empty - default)")] public string Name { get; set; }
        }

        public class LootTableConfig
        {
            [JsonProperty("Minimum number of items")] public int Min { get; set; }
            [JsonProperty("Maximum number of items")] public int Max { get; set; }
            [JsonProperty("Use minimum and maximum values? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty("List of items")] public List<ItemConfig> Items { get; set; }
        }

        public class PrefabConfig
        {
            [JsonProperty("Chance probability [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty("The path to the prefab")] public string PrefabDefinition { get; set; }
        }

        public class PrefabLootTableConfig
        {
            [JsonProperty("Minimum number of prefabs")] public int Min { get; set; }
            [JsonProperty("Maximum number of prefabs")] public int Max { get; set; }
            [JsonProperty("Use minimum and maximum values? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty("List of prefabs")] public List<PrefabConfig> Prefabs { get; set; }
        }

        public class MultiPointAOEConfig
        {
            [JsonProperty("Enable multi-point AOE patterns? [true/false]")] public bool EnableMultiPointAOE { get; set; }
            [JsonProperty("Number of AOE locations (3-16)")] public int AOELocationCount { get; set; }
            [JsonProperty("Warning time before damage [seconds]")] public float WarningTime { get; set; }
            [JsonProperty("Pattern spread radius")] public float PatternRadius { get; set; }
            [JsonProperty("Show visual warning circles? [true/false]")] public bool ShowWarningCircles { get; set; }
            [JsonProperty("Warning circle colors")] public Dictionary<string, string> WarningCircleColors { get; set; }
        }

        public class RadiusActionsConfig
        {
            [JsonProperty("Use only one ability at a time? [true/false]")] public bool UseOnlyOneAbility { get; set; }
            [JsonProperty("Radius (to disable all abilities, set the value to 0)")] public float Radius { get; set; }
            [JsonProperty("Multi-Point AOE Settings")] public MultiPointAOEConfig MultiPointAOE { get; set; }
            [JsonProperty("Spikes ability cooldown time (to disable the ability, set the value -1)")] public int TimeToSpikes { get; set; }
            [JsonProperty("Applied damage to player from Spikes")] public float DamageSpikes { get; set; }
            [JsonProperty("FireBall ability cooldown time (to disable the ability, set the value -1)")] public int TimeToFire { get; set; }
            [JsonProperty("Applied damage to player from FireBall")] public float DamageFire { get; set; }
            [JsonProperty("ElectricShock ability cooldown time (to disable the ability, set the value -1)")] public int TimeToElectricShock { get; set; }
            [JsonProperty("Applied damage to player from ElectricShock")] public float DamageElectricShock { get; set; }
            [JsonProperty("Wounded ability cooldown time (to disable the ability, set the value -1)")] public int TimeToWounded { get; set; }
            [JsonProperty("Freeze ability cooldown time (to disable the ability, set the value -1)")] public int TimeToFreeze { get; set; }
            [JsonProperty("Animal Ability Settings")] public AnimalAbility AnimalAbility { get; set; }
            [JsonProperty("NPC Ability Settings")] public NpcAbility NpcAbility { get; set; }
            [JsonProperty("Radiation")] public float Radiation { get; set; }
            [JsonProperty("Temperature")] public float Temperature { get; set; }
        }

        public class AnimalAbility
        {
            [JsonProperty("Ability Cooldown Time (to disable the ability, set the value -1)")] public int Time { get; set; }
            [JsonProperty("Type of animal or AnimalSpawn preset")] public string Type { get; set; }
            [JsonProperty("Number of animals")] public int Count { get; set; }
            [JsonProperty("Despawn time animals")] public float DespawnTime { get; set; }
        }

        public class NpcAbility
        {
            [JsonProperty("Ability Cooldown Time (to disable the ability, set the value -1)")] public int Time { get; set; }
            [JsonProperty("NPC Settings")] public AddNpcConfig ConfigNpc { get; set; }
            [JsonProperty("Number of NPCs")] public int Count { get; set; }
            [JsonProperty("Despawn time NPCs")] public float DespawnTime { get; set; }
        }

        public class AddNpcConfig
        {
            [JsonProperty("Names")] public List<string> Names { get; set; }
            [JsonProperty("Health")] public float Health { get; set; }
            [JsonProperty("Roam Range")] public float RoamRange { get; set; }
            [JsonProperty("Chase Range")] public float ChaseRange { get; set; }
            [JsonProperty("Attack Range Multiplier")] public float AttackRangeMultiplier { get; set; }
            [JsonProperty("Sense Range")] public float SenseRange { get; set; }
            [JsonProperty("Target Memory Duration [sec.]")] public float MemoryDuration { get; set; }
            [JsonProperty("Scale damage")] public float DamageScale { get; set; }
            [JsonProperty("Aim Cone Scale")] public float AimConeScale { get; set; }
            [JsonProperty("Detect the target only in the NPC's viewing vision cone? [true/false]")] public bool CheckVisionCone { get; set; }
            [JsonProperty("Vision Cone")] public float VisionCone { get; set; }
            [JsonProperty("Speed")] public float Speed { get; set; }
            [JsonProperty("Disable radio effects? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty("Wear items")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty("Belt items")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty("Kit")] public string Kit { get; set; }

            [JsonProperty("Kit (it is recommended to use the previous 2 settings to improve performance)")]
            public string KitBossMonster { get; set; }

            [JsonProperty("Gender (0=Random, 1=Male, 2=Female)")] public GrimmBossGender Gender { get; set; }
            [JsonProperty("Skin Tone (0=Random, 1=Lightest, 2=Light, 3=Dark, 4=Darkest)")] public GrimmBossSkinTone SkinTone { get; set; }
        }

        public class TakeDamageActionsConfig
        {
            [JsonProperty("Disable all abilities when applying damage? [true/false]")] public bool IsDisable { get; set; }
            [JsonProperty("Regeneration of health from the applied damage [%]")] public float Vampirism { get; set; }
            [JsonProperty("The amount of calories consumed")] public float CaloriesTarget { get; set; }
            [JsonProperty("The amount of water consumed")] public float HydrationTarget { get; set; }
            [JsonProperty("The amount of added radiation")] public float RadiationTarget { get; set; }
            [JsonProperty("The amount of added bleeding")] public float BleedingTarget { get; set; }
        }

        public class NpcConfig
        {
            [JsonProperty("Enabled? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty("Name")] public string Name { get; set; }
            [JsonProperty("Health")] public float Health { get; set; }
            [JsonProperty("Roam Range")] public float RoamRange { get; set; }
            [JsonProperty("Chase Range")] public float ChaseRange { get; set; }
            [JsonProperty("Attack Range Multiplier")] public float AttackRangeMultiplier { get; set; }
            [JsonProperty("Sense Range")] public float SenseRange { get; set; }
            [JsonProperty("Target Memory Duration [sec.]")] public float MemoryDuration { get; set; }
            [JsonProperty("Scale damage")] public float DamageScale { get; set; }
            [JsonProperty("Aim Cone Scale")] public float AimConeScale { get; set; }
            [JsonProperty("Detect the target only in the NPC's viewing vision cone? [true/false]")] public bool CheckVisionCone { get; set; }
            [JsonProperty("Vision Cone")] public float VisionCone { get; set; }
            [JsonProperty("Speed")] public float Speed { get; set; }
            [JsonProperty("Minimum time of appearance after death [sec.]")] public float MinTime { get; set; }
            [JsonProperty("Maximum time of appearance after death [sec.]")] public float MaxTime { get; set; }
            [JsonProperty("Disable automatic respawning of boss after death? (True to disable auto respawn) [true/false]")] public bool DisableTimer { get; set; }
            [JsonProperty("Disable radio effects? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty("Remove a corpse after death? (it is recommended to use the true value to improve performance) [true/false]")]
            public bool IsRemoveCorpse { get; set; }

            /// <summary>Alternate BossMonster key when the long form above is not used.</summary>
            [JsonProperty("Remove a corpse after death?")]
            public bool? IsRemoveCorpseShort { get; set; }

            [JsonProperty("Wear items")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty("Belt items")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty("Kit")] public string Kit { get; set; }

            [JsonProperty("Kit (it is recommended to use the previous 2 settings to improve performance)")]
            public string KitBossMonster { get; set; }
            [JsonProperty("Marker settings")] public MarkerConfig Marker { get; set; }
            [JsonProperty("The amount of economics that is given for killing the boss")] public NpcEconomic Economic { get; set; }
            [JsonProperty("List of monument locations")] public List<MonumentPositionsConfig> Monuments { get; set; }
            [JsonProperty("If the boss ends up below ocean sea level, should the boss return to it's place of appearance? [true/false]")] public bool CanRunAwayWater { get; set; }
            [JsonProperty("GrimmNPC: allow swimming in water (humanoid bosses; default true, set false for land-only) [true/false]")] public bool CanSwim { get; set; } = true;
            [JsonProperty("The distance at which you can apply damage to the boss (use 0 at any distance)")] public float PreventDamageRange { get; set; }
            [JsonProperty("Notify in a chat about actions with the boss? [true/false]")] public bool IsChat { get; set; }
            [JsonProperty("The path to the crate that appears at the place of death (empty - not used)")] public string CratePrefab { get; set; }
            [JsonProperty("Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)")] public int TypeLootTable { get; set; }
            [JsonProperty("Loot table from prefabs (if the loot table type is 4 or 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty("Own loot table (if the loot table type is 1 or 5)")] public LootTableConfig OwnLootTable { get; set; }
            [JsonProperty("All actions that occur with the player within the NPC radius")] public RadiusActionsConfig RadiusActions { get; set; }
            [JsonProperty("All actions that occur when applying NPC damage")] public TakeDamageActionsConfig TakeDamageActions { get; set; }
            [JsonProperty("Use the invisibility ability? (use only for bosses with melee weapons) [true/false]")] public bool UseInvisible { get; set; }
            [JsonProperty("Enable damage to the boss with melee weapons only? [true/false]")] public bool OnlyMeleeWeapon { get; set; }
            [JsonProperty("Return to spawn point during battle? [true/false]")] public bool ReturnToSpawnPoint { get; set; }
            [JsonProperty("Return to spawn point interval (seconds, 0 = disabled)")] public float ReturnToSpawnPointInterval { get; set; }
            [JsonProperty("Melee hold distance (m, 0 = default ~2.2; boss closes to this range when pressuring melee)")] public float MeleeHoldDistance { get; set; }
            [JsonProperty("AOE standoff distance (m, 0 = default ~11; boss backs off before radius AOEs)")] public float AoeStandoffDistance { get; set; }
            [JsonProperty("Gender (0=Random, 1=Male, 2=Female)")] public GrimmBossGender Gender { get; set; }
            [JsonProperty("Skin Tone (0=Random, 1=Lightest, 2=Light, 3=Dark, 4=Darkest)")] public GrimmBossSkinTone SkinTone { get; set; }
            [JsonProperty("Boss becomes immune while NPC helpers from ability are alive [true/false]")] public bool ImmuneWhileNpcHelpersActive { get; set; } = true;
            [JsonProperty("Assist propagation radius for helpers (0 = use GrimmNPC2 default on profile)")] public float AssistPropagationRadius { get; set; }
            [JsonProperty("GrimmNPC2 profile PresetId (optional, AutoApplyPresetFromRegistry)")] public string GrimmPresetId { get; set; }
            [JsonProperty("Auto-apply GrimmNPC2 preset from registry [true/false]")] public bool GrimmAutoApplyPreset { get; set; }
            [JsonProperty("GEN2 scientist prefab (optional override; empty = plugin BossPrefab)")] public string Gen2BossPrefab { get; set; }
        }

        private class CustomMapGlobalFile
        {
            [JsonProperty("ID")] public string Id { get; set; }
            [JsonProperty("List of bosses")] public List<CustomMapGlobalBossEntry> ListOfBosses { get; set; }
        }

        private class CustomMapGlobalBossEntry
        {
            [JsonProperty("Boss Name")] public string BossName { get; set; }
            [JsonProperty("List of positions")] public List<string> ListOfPositions { get; set; }
        }

        internal readonly HashSet<NpcConfig> BossConfigsEnabled = new HashSet<NpcConfig>();
        private readonly Dictionary<string, NpcConfig> _allBossConfigsByName = new Dictionary<string, NpcConfig>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _customMapPositionsByBossName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Module instances

        private BossSpawnService _spawnService;
        private BossProfileResolver _profileResolver;
        private HelperSpawnService _helperSpawn;
        private AttackCycleManager _attackCycles;
        private RadiusAbilitySystem _radiusAbilities;
        private RewardSystem _rewards;
        private MarkerNotificationSystem _markers;
        private CleanupManager _cleanup;

        private readonly Dictionary<ulong, BossController> _controllersByNetId = new Dictionary<ulong, BossController>(64);

        [PluginReference] private Plugin PveMode;
        [PluginReference] private Plugin AnimalSpawn;
        [PluginReference] private Plugin Economics;
        [PluginReference] private Plugin ServerRewards;
        [PluginReference] private Plugin IQEconomic;
        [PluginReference] private Plugin XPerience;

        #endregion

        /// <summary>BossMonster defers Gen1 NPCPlayer inventory until <c>timer.Once(0.2f)</c>. GEN2 <see cref="ScientistNPC2"/> is not an NPCPlayer and normally has no <see cref="PlayerInventory"/>; one delayed check remains for forward compatibility.</summary>
        private void ScheduleGen2BossInventoryEquip(ScientistNPC2 npc, NpcConfig cfg, string logName)
        {
            if (npc == null || cfg == null) return;
            ScientistNPC2 npcRef = npc;
            NpcConfig cfgRef = cfg;
            string name = logName ?? cfg.Name ?? "Boss";
            timer.Once(0.2f, () => TryGen2InventoryEquipDelayed(npcRef, cfgRef, name, 0));
        }

        private void ScheduleGen2HelperInventoryEquip(ScientistNPC2 npc, AddNpcConfig cfg, string logName)
        {
            if (npc == null || cfg == null) return;
            ScientistNPC2 npcRef = npc;
            AddNpcConfig cfgRef = cfg;
            string name = logName ?? "Helper";
            timer.Once(0.2f, () => TryGen2HelperInventoryEquipDelayed(npcRef, cfgRef, name, 0));
        }

        private void TryGen2InventoryEquipDelayed(ScientistNPC2 npc, NpcConfig cfg, string logName, int attempt)
        {
            if (npc == null || npc.IsDestroyed) return;
            PlayerInventory inv = ResolveGen2PlayerInventory(npc);
            // Stock ScientistNPC2 is not NPCPlayer: prefab has no PlayerInventory; one retry covers async attach if that ever changes.
            if (inv == null && attempt < 1)
            {
                timer.Once(0.2f, () => TryGen2InventoryEquipDelayed(npc, cfg, logName, attempt + 1));
                return;
            }

            bool hasWear = cfg.WearItems != null && cfg.WearItems.Count > 0;
            bool hasBelt = cfg.BeltItems != null && cfg.BeltItems.Count > 0;
            if (inv != null)
                EquipGen2WearAndBeltLikeBossMonster(npc, inv, cfg.WearItems, cfg.BeltItems, logName);
            else if (hasWear || hasBelt)
                TryAttachGen1LoadoutProxyChild(npc, cfg.WearItems, cfg.BeltItems, logName);
        }

        private void TryGen2HelperInventoryEquipDelayed(ScientistNPC2 npc, AddNpcConfig cfg, string logName, int attempt)
        {
            if (npc == null || npc.IsDestroyed) return;
            PlayerInventory inv = ResolveGen2PlayerInventory(npc);
            if (inv == null && attempt < 1)
            {
                timer.Once(0.2f, () => TryGen2HelperInventoryEquipDelayed(npc, cfg, logName, attempt + 1));
                return;
            }

            bool hasWear = cfg.WearItems != null && cfg.WearItems.Count > 0;
            bool hasBelt = cfg.BeltItems != null && cfg.BeltItems.Count > 0;
            if (inv != null)
                EquipGen2WearAndBeltLikeBossMonster(npc, inv, cfg.WearItems, cfg.BeltItems, logName);
            else if (hasWear || hasBelt)
                TryAttachGen1LoadoutProxyChild(npc, cfg.WearItems, cfg.BeltItems, logName);
        }

        /// <summary>GEN2 <see cref="ScientistNPC2"/> cannot host <see cref="PlayerInventory"/> (requires <see cref="BasePlayer"/>). BossMonster-style loadout is applied on a child Gen1 <see cref="ScientistNPC"/> (visual + belt proxy); combat remains GEN2 + GrimmNPC2 weapon.</summary>
        private void TryAttachGen1LoadoutProxyChild(ScientistNPC2 gen2, HashSet<NpcWear> wearItems, HashSet<NpcBelt> beltItems, string logName)
        {
            if (gen2 == null || gen2.IsDestroyed) return;
            string prefabPath = _pluginConfig?.Gen1LoadoutProxyPrefab?.Trim();
            if (string.IsNullOrEmpty(prefabPath))
            {
                PrintWarning(" GrimmBoss: Gen1LoadoutProxyPrefab is empty; cannot apply wear/belt. Set oxide/config/GrimmBoss.json.");
                return;
            }

            GameObject prefab = GameManager.server.FindPrefab(prefabPath);
            if (prefab == null)
            {
                PrintWarning(" GrimmBoss: Gen1 loadout proxy prefab not found: " + prefabPath);
                return;
            }

            if (prefab.GetComponent<ScientistNPC>() == null && prefab.GetComponentInChildren<ScientistNPC>(true) == null)
            {
                PrintWarning(" GrimmBoss: Gen1 loadout proxy prefab has no ScientistNPC (HumanNPC): " + prefabPath);
                return;
            }

            try
            {
                BaseEntity ent = GameManager.server.CreateEntity(prefabPath, gen2.transform.position, gen2.transform.rotation, false);
                ScientistNPC proxy = ent != null
                    ? (ent.GetComponent<ScientistNPC>() ?? ent.GetComponentInChildren<ScientistNPC>(true))
                    : null;
                if (proxy == null)
                {
                    TryKillEntity(ent);
                    PrintWarning(" GrimmBoss: CreateEntity did not yield ScientistNPC for loadout proxy.");
                    return;
                }

                proxy.enableSaving = false;
                PoolableEx.AwakeFromInstantiate(proxy.gameObject);
                proxy.Spawn();
                proxy.SetParent(gen2, true);
                proxy.transform.localPosition = Vector3.zero;
                proxy.transform.localRotation = Quaternion.identity;

                DisableGen1LoadoutAi(proxy);

                var beforeRenderers = new HashSet<Renderer>();
                foreach (Renderer r in proxy.GetComponentsInChildren<Renderer>(true))
                {
                    beforeRenderers.Add(r);
                    r.enabled = false;
                }

                EquipScientistNpcWearAndBelt(proxy, wearItems, beltItems, logName);
                foreach (Renderer r in proxy.GetComponentsInChildren<Renderer>(true))
                {
                    if (!beforeRenderers.Contains(r))
                        r.enabled = true;
                }

                foreach (Renderer r in beforeRenderers)
                    r.enabled = false;

                HideGen1ProxyHeldWeaponRenderers(proxy);
                DebugLog($"GrimmBoss: Gen1 loadout proxy attached for '{logName}' (wear/belt on child ScientistNPC).");
            }
            catch (Exception ex)
            {
                PrintWarning(" GrimmBoss: Gen1 loadout proxy failed for '" + logName + "': " + ex.Message);
            }
        }

        private static void DisableGen1LoadoutAi(ScientistNPC proxy)
        {
            if (proxy == null) return;
            foreach (Behaviour b in proxy.GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null) continue;
                string n = b.GetType().Name;
                if (n.IndexOf("Brain", StringComparison.Ordinal) >= 0 || n.IndexOf("Navigator", StringComparison.Ordinal) >= 0)
                    b.enabled = false;
            }

            UnityEngine.AI.NavMeshAgent agent = proxy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            foreach (Collider c in proxy.GetComponentsInChildren<Collider>(true))
            {
                if (c != null) c.enabled = false;
            }
        }

        private static void HideGen1ProxyHeldWeaponRenderers(ScientistNPC proxy)
        {
            if (proxy == null) return;
            foreach (AttackEntity atk in proxy.GetComponentsInChildren<AttackEntity>(true))
            {
                foreach (Renderer r in atk.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;
            }
        }

        /// <summary>Same as BossMonster <c>EquipNpcWearAndBelt</c> for Gen1 <see cref="ScientistNPC"/>.</summary>
        private void EquipScientistNpcWearAndBelt(ScientistNPC npc, HashSet<NpcWear> wearItems, HashSet<NpcBelt> beltItems, string logName)
        {
            if (npc == null || npc.inventory == null)
            {
                PrintWarning(" Failed to equip items for " + logName + ": inventory is null");
                return;
            }

            bool hasWear = wearItems != null && wearItems.Count > 0;
            bool hasBelt = beltItems != null && beltItems.Count > 0;

            if (hasWear && npc.inventory.containerWear != null)
                ClearGen2ItemContainer(npc.inventory.containerWear);
            if (hasBelt && npc.inventory.containerBelt != null)
                ClearGen2ItemContainer(npc.inventory.containerBelt);

            if (hasWear)
            {
                if (npc.inventory.containerWear == null)
                    PrintWarning(" Failed to equip wear items for " + logName + ": containerWear is null");
                else
                {
                    foreach (NpcWear wearItem in wearItems)
                    {
                        if (wearItem == null || string.IsNullOrEmpty(wearItem.ShortName)) continue;
                        Item item = ItemManager.CreateByName(wearItem.ShortName, 1, wearItem.SkinID);
                        if (item != null)
                        {
                            npc.inventory.GiveItem(item, npc.inventory.containerWear);
                            item.MarkDirty();
                        }
                    }
                }
            }

            if (hasBelt)
            {
                if (npc.inventory.containerBelt == null)
                    PrintWarning(" Failed to equip belt items for " + logName + ": containerBelt is null");
                else
                {
                    Item firstWeapon = null;
                    int slotIndex = 0;
                    foreach (NpcBelt beltItem in beltItems)
                    {
                        if (beltItem == null || string.IsNullOrEmpty(beltItem.ShortName)) continue;
                        Item item = ItemManager.CreateByName(beltItem.ShortName, beltItem.Amount, beltItem.SkinID);
                        if (item == null) continue;

                        if (beltItem.Mods != null && beltItem.Mods.Count > 0 && item.contents != null)
                        {
                            foreach (string mod in beltItem.Mods)
                            {
                                if (string.IsNullOrEmpty(mod)) continue;
                                Item modItem = ItemManager.CreateByName(mod, 1, 0);
                                if (modItem != null && modItem.info != null)
                                    item.contents.AddItem(modItem.info, 1);
                            }
                        }

                        string ammoType = beltItem.Ammo;
                        if (string.IsNullOrEmpty(ammoType) && beltItem.ShortName == "pistol.nailgun")
                            ammoType = "ammo.nailgun";

                        if (!string.IsNullOrEmpty(ammoType) && item.contents != null)
                        {
                            ItemDefinition ammoDef = ItemManager.FindItemDefinition(ammoType);
                            if (ammoDef != null)
                            {
                                int reserveAmmo = 100;
                                if (ammoDef.stackable > 0)
                                    reserveAmmo = Mathf.Min(ammoDef.stackable, 500);
                                item.contents.AddItem(ammoDef, reserveAmmo);
                            }
                        }

                        if (slotIndex == 0)
                        {
                            item.MoveToContainer(npc.inventory.containerBelt, 0);
                            firstWeapon = item;
                        }
                        else
                            npc.inventory.GiveItem(item, npc.inventory.containerBelt);

                        item.MarkDirty();
                        slotIndex++;
                    }

                    if (firstWeapon != null)
                    {
                        npc.UpdateActiveItem(firstWeapon.uid);
                        BaseEntity heldEntity = firstWeapon.GetHeldEntity();
                        if (heldEntity is BaseProjectile baseProjectile)
                            baseProjectile.TopUpAmmo();
                    }
                }
            }
        }

        /// <summary>GEN2 uses <see cref="Rust.Ai.Gen2.NpcShootingComponent"/> + one weapon def; pick first belt short name (HashSet order undefined).</summary>
        private static string FirstNonEmptyBeltWeaponShortName(HashSet<NpcBelt> beltItems)
        {
            if (beltItems == null || beltItems.Count == 0) return null;
            foreach (NpcBelt b in beltItems)
            {
                if (b == null) continue;
                string sn = b.ShortName;
                if (string.IsNullOrWhiteSpace(sn)) continue;
                return sn.Trim();
            }

            return null;
        }

        private static PlayerInventory ResolveGen2PlayerInventory(ScientistNPC2 npc)
        {
            if (npc == null) return null;
            PlayerInventory inv = npc.GetComponent<PlayerInventory>();
            if (inv != null) return inv;
            inv = npc.gameObject.GetComponentInChildren<PlayerInventory>(true);
            return inv;
        }

        private static void ClearGen2ItemContainer(ItemContainer container)
        {
            if (container == null || container.itemList == null) return;
            // Reverse index: safe removal while mutating itemList (no LINQ / no ToList snapshot).
            for (int i = container.itemList.Count - 1; i >= 0; i--)
            {
                Item existing = container.itemList[i];
                if (existing == null) continue;
                existing.RemoveFromContainer();
                existing.Remove();
            }
        }

        /// <summary>Parity with <c>BossMonster.EquipNpcWearAndBelt</c> (Gen1 ScientistNPC) for wear/belt/mods/ammo/active item.</summary>
        private void EquipGen2WearAndBeltLikeBossMonster(
            ScientistNPC2 npc,
            PlayerInventory inv,
            HashSet<NpcWear> wearItems,
            HashSet<NpcBelt> beltItems,
            string logName)
        {
            if (npc == null || inv == null)
            {
                PrintWarning($" Failed to equip items for {logName}: inventory is null");
                return;
            }

            bool hasWear = wearItems != null && wearItems.Count > 0;
            bool hasBelt = beltItems != null && beltItems.Count > 0;

            if (hasWear && inv.containerWear != null)
                ClearGen2ItemContainer(inv.containerWear);
            if (hasBelt && inv.containerBelt != null)
                ClearGen2ItemContainer(inv.containerBelt);

            if (hasWear)
            {
                if (inv.containerWear == null)
                    PrintWarning($" Failed to equip wear items for {logName}: containerWear is null");
                else
                {
                    foreach (NpcWear wearItem in wearItems)
                    {
                        if (wearItem == null || string.IsNullOrEmpty(wearItem.ShortName)) continue;
                        Item item = ItemManager.CreateByName(wearItem.ShortName, 1, wearItem.SkinID);
                        if (item != null)
                        {
                            inv.GiveItem(item, inv.containerWear);
                            item.MarkDirty();
                        }
                    }
                }
            }

            if (hasBelt)
            {
                if (inv.containerBelt == null)
                    PrintWarning($" Failed to equip belt items for {logName}: containerBelt is null");
                else
                {
                    Item firstWeapon = null;
                    int slotIndex = 0;
                    foreach (NpcBelt beltItem in beltItems)
                    {
                        if (beltItem == null || string.IsNullOrEmpty(beltItem.ShortName)) continue;
                        Item item = ItemManager.CreateByName(beltItem.ShortName, beltItem.Amount, beltItem.SkinID);
                        if (item == null) continue;

                        if (beltItem.Mods != null && beltItem.Mods.Count > 0 && item.contents != null)
                        {
                            foreach (string mod in beltItem.Mods)
                            {
                                if (string.IsNullOrEmpty(mod)) continue;
                                Item modItem = ItemManager.CreateByName(mod, 1, 0);
                                if (modItem != null && modItem.info != null)
                                    item.contents.AddItem(modItem.info, 1);
                            }
                        }

                        string ammoType = beltItem.Ammo;
                        if (string.IsNullOrEmpty(ammoType) && beltItem.ShortName == "pistol.nailgun")
                            ammoType = "ammo.nailgun";

                        if (!string.IsNullOrEmpty(ammoType) && item.contents != null)
                        {
                            ItemDefinition ammoDef = ItemManager.FindItemDefinition(ammoType);
                            if (ammoDef != null)
                            {
                                int reserveAmmo = 100;
                                if (ammoDef.stackable > 0)
                                    reserveAmmo = Mathf.Min(ammoDef.stackable, 500);
                                item.contents.AddItem(ammoDef, reserveAmmo);
                            }
                        }

                        if (slotIndex == 0)
                        {
                            item.MoveToContainer(inv.containerBelt, 0);
                            firstWeapon = item;
                        }
                        else
                            inv.GiveItem(item, inv.containerBelt);

                        item.MarkDirty();
                        slotIndex++;
                    }

                    if (firstWeapon != null)
                    {
                        // ScientistNPC2 : BaseNPC2; not a BasePlayer; only Gen1 NPCPlayer supports `npc as BasePlayer`.
                        BaseEntity ownerEntity = npc;
                        BasePlayer bp = ownerEntity as BasePlayer;
                        if (bp != null)
                        {
                            bp.UpdateActiveItem(firstWeapon.uid);
                            BaseEntity heldEntity = firstWeapon.GetHeldEntity();
                            if (heldEntity is BaseProjectile baseProjectile)
                                baseProjectile.TopUpAmmo();
                        }
                        else
                        {
                            BaseEntity heldEntity = firstWeapon.GetHeldEntity();
                            if (heldEntity is BaseProjectile baseProjectile)
                                baseProjectile.TopUpAmmo();
                        }
                    }
                }
            }
        }

        #region Oxide hooks

        private void Init()
        {
            _profileResolver = new BossProfileResolver(this);
            _helperSpawn = new HelperSpawnService(this, _profileResolver);
            _spawnService = new BossSpawnService(this, _profileResolver, _helperSpawn);
            _radiusAbilities = new RadiusAbilitySystem(this);
            _attackCycles = new AttackCycleManager(this, _radiusAbilities);
            _rewards = new RewardSystem(this);
            _markers = new MarkerNotificationSystem(this);
            _cleanup = new CleanupManager(this);
        }

        private void OnServerInitialized()
        {
            if (!GrimmNpc2Interop.Init())
            {
                Puts(" GrimmNPC2 not loaded yet (Harmony often loads after Oxide). Retrying every 5s until the mod is available.");
                _grimmLoadReTimer?.Destroy();
                _grimmLoadReTimer = timer.Every(5f, GrimmLoadTick);
            }
            else if (!GrimmNpc2Interop.IsGrimmRuntimeReady())
            {
                Puts(" GrimmNPC2 assembly found; waiting for Harmony mod runtime (Instance). Retrying every 5s.");
                _grimmLoadReTimer?.Destroy();
                _grimmLoadReTimer = timer.Every(5f, GrimmLoadTick);
            }
            else
            {
                Puts(" GrimmNPC2 API ready - boss spawning enabled.");
            }

            _spawnService.LoadBossDataFiles();
            _spawnService.InitializeSpawnQueue();
            _spawnService.ScheduleRespawnLoop();
            _attackCycles.StartGlobalTimers();
        }

        private void GrimmLoadTick()
        {
            // Always re-run Init while Instance is missing so we can rebind after harmony.load (stale assembly in AppDomain).
            if (!GrimmNpc2Interop.Init())
                return;
            if (!GrimmNpc2Interop.IsGrimmRuntimeReady())
                return;

            if (_grimmLoadReTimer != null)
                Puts(" GrimmNPC2 API ready - boss spawning enabled.");
            _loggedGrimmSpawnBlocked = false;
            _loggedGrimmRuntimeNotReady = false;
            _grimmLoadReTimer?.Destroy();
            _grimmLoadReTimer = null;
        }

        private void Unload()
        {
            _grimmLoadReTimer?.Destroy();
            _grimmLoadReTimer = null;
            _attackCycles?.StopGlobalTimers();
            int n = _controllersByNetId.Count;
            if (n > 0)
            {
                var snapshot = new BossController[n];
                int i = 0;
                foreach (BossController c in _controllersByNetId.Values)
                    snapshot[i++] = c;
                for (i = 0; i < snapshot.Length; i++)
                {
                    BossController c = snapshot[i];
                    c.MarkerUpdateTimer?.Destroy();
                    c.MarkerUpdateTimer = null;
                    _cleanup.DestroyBossController(c, silent: true);
                }
            }

            _controllersByNetId.Clear();
            _gen2HeavyPrefabResolveCache.Clear();
            _gen2ResolveFailedKeys.Clear();
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            if (entity is ScientistNPC2 boss && _controllersByNetId.TryGetValue(boss.net.ID.Value, out var ctrl))
            {
                if (ctrl.NpcHelpersActive && ctrl.Config.ImmuneWhileNpcHelpersActive)
                    return true;

                BasePlayer attacker = info.InitiatorPlayer;
                if (attacker != null)
                {
                    if (ctrl.Config.PreventDamageRange > 0f)
                    {
                        float d = Vector3.Distance(boss.transform.position, attacker.transform.position);
                        if (d > ctrl.Config.PreventDamageRange) return true;
                    }

                    if (ctrl.Config.OnlyMeleeWeapon && !info.damageTypes.IsMeleeType())
                        return true;

                    ctrl.OnBossDamagedByPlayer(attacker);
                    _radiusAbilities?.OnBossDamaged(ctrl, attacker, info);
                }

                return null;
            }

            if (entity is ScientistNPC2 helperNpc
                && GrimmNpc2Interop.GetNpcData(helperNpc.net.ID.Value) is object hd
                && hd != null)
            {
                ulong ownerId = GrimmNpc2Interop.GetOwnerNetId(hd);
                if (ownerId != 0 && _controllersByNetId.TryGetValue(ownerId, out var ownerCtrl))
                {
                    BasePlayer atk = info.InitiatorPlayer;
                    if (atk != null)
                        ownerCtrl.OnHelperDamagedByPlayer(helperNpc, atk);
                }
            }

            return null;
        }

        private void OnEntityDeath(ScientistNPC2 npc, HitInfo info)
        {
            if (npc == null) return;
            ulong id = npc.net?.ID.Value ?? 0;
            if (id == 0) return;

            if (_controllersByNetId.TryGetValue(id, out var ctrl))
            {
                _cleanup.HandleBossDeath(ctrl, npc, info);
                return;
            }

            object data = GrimmNpc2Interop.GetNpcData(id);
            if (data == null) return;
            ulong owner = GrimmNpc2Interop.GetOwnerNetId(data);
            if (owner != 0 && _controllersByNetId.TryGetValue(owner, out var bossCtrl))
                bossCtrl.OnHelperDied(id);
        }

        private bool RegisterController(BossController c)
        {
            if (_controllersByNetId.ContainsKey(c.BossNetId))
                return false;
            _controllersByNetId.Add(c.BossNetId, c);
            return true;
        }

        internal void UnregisterController(ulong netId) => _controllersByNetId.Remove(netId);

        #endregion

        #region BossProfileResolver

        private sealed class BossProfileResolver
        {
            private readonly GrimmBoss _p;

            public BossProfileResolver(GrimmBoss plugin) => _p = plugin;

            public object BuildBossProfile(NpcConfig cfg, Vector3 spawnPos, ulong squadId, int fsmKindHint)
            {
                float tether = Mathf.Max(cfg.RoamRange, 25f);
                float sense = cfg.SenseRange > 0f ? cfg.SenseRange : 50f;
                float listen = Mathf.Min(sense, 40f);

                object d = GrimmNpc2Interop.CreateCustomNpcData2();
                if (d == null) return null;
                GrimmNpc2Interop.Set(d, "Name", cfg.Name ?? "Boss");
                GrimmNpc2Interop.Set(d, "PresetId", string.IsNullOrWhiteSpace(cfg.GrimmPresetId) ? null : cfg.GrimmPresetId.Trim());
                GrimmNpc2Interop.Set(d, "AutoApplyPresetFromRegistry", cfg.GrimmAutoApplyPreset && !string.IsNullOrWhiteSpace(cfg.GrimmPresetId));
                GrimmNpc2Interop.Set(d, "FsmKindHint", GrimmNpc2Interop.ToFsmEnum(fsmKindHint));
                GrimmNpc2Interop.Set(d, "HomePosition", spawnPos);
                GrimmNpc2Interop.Set(d, "ForceHomeToSpawnPoint", true);
                GrimmNpc2Interop.Set(d, "SetInitialDestinationToHome", true);
                GrimmNpc2Interop.Set(d, "HomeTetherDistance", tether);
                GrimmNpc2Interop.Set(d, "RoamRange", cfg.RoamRange);
                GrimmNpc2Interop.Set(d, "ChaseRange", cfg.ChaseRange > 0f ? cfg.ChaseRange : 100f);
                GrimmNpc2Interop.Set(d, "SenseRange", sense);
                GrimmNpc2Interop.Set(d, "ListenRange", listen);
                GrimmNpc2Interop.Set(d, "TargetMemorySeconds", cfg.MemoryDuration > 0f ? cfg.MemoryDuration : 30f);
                GrimmNpc2Interop.Set(d, "DamageScale", cfg.DamageScale > 0f ? cfg.DamageScale : 1f);
                GrimmNpc2Interop.Set(d, "TurretDamageScaleIncoming", _p._pluginConfig.TurretDamageScale);
                GrimmNpc2Interop.Set(d, "AimConeScale", cfg.AimConeScale > 0f ? cfg.AimConeScale : 1f);
                GrimmNpc2Interop.Set(d, "CanSwim", cfg.CanSwim);
                GrimmNpc2Interop.Set(d, "IsBoss", true);
                GrimmNpc2Interop.Set(d, "Kind", GrimmNpc2Interop.ToKindEnum(GrimmNpc2Interop.Kind.Primary));
                GrimmNpc2Interop.Set(d, "OwnerNetId", 0UL);
                GrimmNpc2Interop.Set(d, "SquadId", squadId);
                GrimmNpc2Interop.Set(d, "AssistRadius", cfg.AssistPropagationRadius > 0f ? cfg.AssistPropagationRadius : 48f);
                GrimmNpc2Interop.Set(d, "PropagateTargetToAssistGroup", true);
                GrimmNpc2Interop.Set(d, "GroupAlertEnabled", false);
                GrimmNpc2Interop.Set(d, "ShortRangeVisionHalfAngleDegrees", cfg.VisionCone > 0f ? cfg.VisionCone : 0f);
                GrimmNpc2Interop.Set(d, "NavSpeedMultiplier", cfg.Speed > 0f ? cfg.Speed : 1f);
                GrimmNpc2Interop.Set(d, "CheckVisionCone", cfg.CheckVisionCone);
                GrimmNpc2Interop.Set(d, "VisionConeDegrees", cfg.VisionCone);
                GrimmNpc2Interop.Set(d, "AttackRangeMultiplier", cfg.AttackRangeMultiplier > 0f ? cfg.AttackRangeMultiplier : 1f);
                GrimmNpc2Interop.Set(d, "NpcSenseRange", cfg.SenseRange);
                GrimmNpc2Interop.Set(d, "CanRunAwayWater", cfg.CanRunAwayWater);
                GrimmNpc2Interop.Set(d, "CratePrefab", cfg.CratePrefab);
                GrimmNpc2Interop.Set(d, "RemoveCorpseOnDeath", cfg.IsRemoveCorpse);
                GrimmNpc2Interop.Set(d, "IgnoreSleepingPlayers", false);
                GrimmNpc2Interop.Set(d, "IgnoreWoundedPlayers", false);
                GrimmNpc2Interop.Set(d, "LootPreset", cfg.TypeLootTable.ToString());
                GrimmNpc2Interop.Set(d, "LootTableJsonHint", cfg.OwnLootTable != null ? JsonConvert.SerializeObject(cfg.OwnLootTable) : null);

                string gen2Weapon = FirstNonEmptyBeltWeaponShortName(cfg.BeltItems);
                if (!string.IsNullOrEmpty(gen2Weapon))
                    GrimmNpc2Interop.Set(d, "Gen2WeaponItemShortName", gen2Weapon);

                GrimmNpc2Interop.Normalize(d);
                return d;
            }

            public object BuildHelperProfile(
                AddNpcConfig cfg,
                Vector3 home,
                ulong ownerNetId,
                ulong squadId,
                int kindEnum,
                int fsmKindHint)
            {
                string nm = cfg.Names != null && cfg.Names.Count > 0 ? cfg.Names.GetRandom() : "Helper";
                float chase = cfg.ChaseRange > 0f ? cfg.ChaseRange : 50f;
                float roam = cfg.RoamRange > 0f ? cfg.RoamRange : Mathf.Max(25f, chase * 0.85f);
                float sense = cfg.SenseRange > 0f ? cfg.SenseRange : 50f;

                object d = GrimmNpc2Interop.CreateCustomNpcData2();
                if (d == null) return null;
                GrimmNpc2Interop.Set(d, "Name", nm);
                GrimmNpc2Interop.Set(d, "FsmKindHint", GrimmNpc2Interop.ToFsmEnum(fsmKindHint));
                GrimmNpc2Interop.Set(d, "HomePosition", home);
                GrimmNpc2Interop.Set(d, "ForceHomeToSpawnPoint", true);
                GrimmNpc2Interop.Set(d, "SetInitialDestinationToHome", true);
                GrimmNpc2Interop.Set(d, "HomeTetherDistance", Mathf.Max(roam, 25f));
                GrimmNpc2Interop.Set(d, "RoamRange", roam);
                GrimmNpc2Interop.Set(d, "ChaseRange", chase);
                GrimmNpc2Interop.Set(d, "SenseRange", sense);
                GrimmNpc2Interop.Set(d, "ListenRange", Mathf.Min(sense, 35f));
                GrimmNpc2Interop.Set(d, "TargetMemorySeconds", cfg.MemoryDuration > 0f ? cfg.MemoryDuration : 25f);
                GrimmNpc2Interop.Set(d, "DamageScale", cfg.DamageScale > 0f ? cfg.DamageScale : 1f);
                GrimmNpc2Interop.Set(d, "TurretDamageScaleIncoming", _p._pluginConfig.TurretDamageScale);
                GrimmNpc2Interop.Set(d, "AimConeScale", cfg.AimConeScale > 0f ? cfg.AimConeScale : 1f);
                GrimmNpc2Interop.Set(d, "CanSwim", true);
                GrimmNpc2Interop.Set(d, "IsBoss", false);
                GrimmNpc2Interop.Set(d, "Kind", GrimmNpc2Interop.ToKindEnum(kindEnum));
                GrimmNpc2Interop.Set(d, "OwnerNetId", ownerNetId);
                GrimmNpc2Interop.Set(d, "SquadId", squadId);
                GrimmNpc2Interop.Set(d, "AssistRadius", 36f);
                GrimmNpc2Interop.Set(d, "PropagateTargetToAssistGroup", true);
                GrimmNpc2Interop.Set(d, "ShortRangeVisionHalfAngleDegrees", cfg.VisionCone > 0f ? cfg.VisionCone : 0f);
                GrimmNpc2Interop.Set(d, "NavSpeedMultiplier", cfg.Speed > 0f ? cfg.Speed : 1f);
                GrimmNpc2Interop.Set(d, "CheckVisionCone", cfg.CheckVisionCone);
                GrimmNpc2Interop.Set(d, "AttackRangeMultiplier", cfg.AttackRangeMultiplier > 0f ? cfg.AttackRangeMultiplier : 1f);
                GrimmNpc2Interop.Set(d, "NpcSenseRange", sense);

                string gen2Weapon = FirstNonEmptyBeltWeaponShortName(cfg.BeltItems);
                if (!string.IsNullOrEmpty(gen2Weapon))
                    GrimmNpc2Interop.Set(d, "Gen2WeaponItemShortName", gen2Weapon);

                GrimmNpc2Interop.Normalize(d);
                return d;
            }
        }

        #endregion

        #region BossSpawnService

        private sealed class BossSpawnService
        {
            private readonly GrimmBoss _p;
            private readonly BossProfileResolver _resolver;
            private readonly HelperSpawnService _helpers;
            internal readonly List<string> SpawnQueue = new List<string>();

            public BossSpawnService(GrimmBoss plugin, BossProfileResolver resolver, HelperSpawnService helpers)
            {
                _p = plugin;
                _resolver = resolver;
                _helpers = helpers;
            }

            public void LoadBossDataFiles()
            {
                _p.BossConfigsEnabled.Clear();
                _p._allBossConfigsByName.Clear();

                string primary = Path.Combine(Interface.Oxide.DataDirectory, "GrimmBoss", "Bosses");
                string legacy = Path.Combine(Interface.Oxide.DataDirectory, "BossMonster", "Bosses");

                void ReadFolder(string folder)
                {
                    foreach (string path in Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var cfg = JsonConvert.DeserializeObject<NpcConfig>(File.ReadAllText(path));
                            if (cfg == null || string.IsNullOrWhiteSpace(cfg.Name)) continue;
                            _p._allBossConfigsByName[cfg.Name] = cfg;
                            EnsureDefaults(cfg);
                            if (cfg.Enabled) _p.BossConfigsEnabled.Add(cfg);
                        }
                        catch (Exception ex)
                        {
                            _p.PrintWarning($"GrimmBoss: failed reading {path}: {ex.Message}");
                        }
                    }
                }

                if (Directory.Exists(primary))
                    ReadFolder(primary);

                if (_p._allBossConfigsByName.Count == 0 && Directory.Exists(legacy))
                {
                    _p.PrintWarning($"GrimmBoss: no boss configs in '{primary}' - loading legacy folder '{legacy}'.");
                    ReadFolder(legacy);
                }
                else if (!Directory.Exists(primary) && Directory.Exists(legacy))
                {
                    _p.PrintWarning($"GrimmBoss: folder missing '{primary}' - loading legacy '{legacy}'.");
                    ReadFolder(legacy);
                }
                else if (!Directory.Exists(primary) && !Directory.Exists(legacy))
                {
                    _p.PrintWarning($"GrimmBoss: create boss JSON folder: {primary} (or legacy {legacy}).");
                }

                _p.LoadCustomMapGlobal();
                _p.Puts($"GrimmBoss: loaded {_p._allBossConfigsByName.Count} boss file(s), {_p.BossConfigsEnabled.Count} enabled.");
            }

            private static void EnsureDefaults(NpcConfig c)
            {
                if (c.IsRemoveCorpseShort.HasValue)
                    c.IsRemoveCorpse = c.IsRemoveCorpseShort.Value;
                if (string.IsNullOrEmpty(c.Kit) && !string.IsNullOrEmpty(c.KitBossMonster))
                    c.Kit = c.KitBossMonster;

                if (c.RadiusActions?.NpcAbility?.ConfigNpc != null)
                {
                    AddNpcConfig h = c.RadiusActions.NpcAbility.ConfigNpc;
                    if (string.IsNullOrEmpty(h.Kit) && !string.IsNullOrEmpty(h.KitBossMonster))
                        h.Kit = h.KitBossMonster;
                }

                c.OwnLootTable ??= new LootTableConfig { Min = 0, Max = 0, UseCount = false, Items = new List<ItemConfig>() };
                c.PrefabLootTable ??= new PrefabLootTableConfig { Min = 0, Max = 0, UseCount = false, Prefabs = new List<PrefabConfig>() };
                c.Marker ??= new MarkerConfig { IsMarker = false, Radius = 40f, Alpha = 0.5f, Color = new ColorConfig { R = 1f, G = 0.2f, B = 0.2f } };
                c.Economic ??= new NpcEconomic();
                c.RadiusActions ??= new RadiusActionsConfig
                {
                    Radius = 0f,
                    TimeToSpikes = -1,
                    TimeToFire = -1,
                    TimeToElectricShock = -1,
                    TimeToWounded = -1,
                    TimeToFreeze = -1,
                    MultiPointAOE = new MultiPointAOEConfig(),
                    AnimalAbility = new AnimalAbility { Time = -1 },
                    NpcAbility = new NpcAbility { Time = -1, ConfigNpc = new AddNpcConfig() }
                };
                c.TakeDamageActions ??= new TakeDamageActionsConfig { IsDisable = false };
                c.Monuments ??= new List<MonumentPositionsConfig>();
            }

            public void InitializeSpawnQueue()
            {
                SpawnQueue.Clear();
                foreach (NpcConfig c in _p.BossConfigsEnabled)
                {
                    if (c.DisableTimer) continue;
                    SpawnQueue.Add(c.Name);
                }
            }

            public void ScheduleRespawnLoop()
            {
                _p.timer.In(8f, CheckMaintainBossCount);
            }

            private void CheckMaintainBossCount()
            {
                try
                {
                    int want = _p._pluginConfig.AmountBosses;
                    int have = _p._controllersByNetId.Count;
                    for (int i = 0; i < want - have && SpawnQueue.Count > 0; i++)
                        TrySpawnOneFromQueue();
                }
                finally
                {
                    _p.timer.In(10f, CheckMaintainBossCount);
                }
            }

            private void TrySpawnOneFromQueue()
            {
                if (SpawnQueue.Count == 0) return;
                string name = SpawnQueue[0];
                SpawnQueue.RemoveAt(0);
                if (!_p._allBossConfigsByName.TryGetValue(name, out var cfg) || !cfg.Enabled)
                {
                    CheckMaintainBossCount();
                    return;
                }

                Vector3 pos = SelectSpawnPosition(cfg, out bool terrainFallback);
                if (pos == Vector3.zero)
                {
                    _p.timer.In(UnityEngine.Random.Range(cfg.MinTime, cfg.MaxTime), () => SpawnQueue.Add(name));
                    return;
                }

                object canSpawn = Interface.CallHook("CanBossSpawn", cfg.Name, pos);
                if (canSpawn is bool allow && !allow)
                {
                    _p.timer.In(UnityEngine.Random.Range(cfg.MinTime, cfg.MaxTime), () => SpawnQueue.Add(name));
                    return;
                }

                ulong squadId = (ulong)UnityEngine.Random.Range(100000, int.MaxValue);
                var boss = CreateBossEntity(cfg, pos, terrainFallback, squadId);
                if (boss == null)
                {
                    _p.timer.In(3f, () => SpawnQueue.Add(name));
                    return;
                }

                var ctrl = new BossController(_p, boss, cfg, pos, squadId, terrainFallback);
                if (!_p.RegisterController(ctrl))
                {
                    boss.Kill();
                    return;
                }

                _p._markers.AttachBossMarkers(ctrl);
                _p._markers.AnnounceSpawn(ctrl);
                Interface.CallHook("OnBossSpawn", boss);
                _p.DebugLog($"GrimmBoss spawned '{cfg.Name}' at {pos} (terrainFallback={terrainFallback})");
            }

            private ScientistNPC2 CreateBossEntity(NpcConfig cfg, Vector3 pos, bool terrainFallback, ulong squadId)
            {
                if (!GrimmNpc2Interop.IsReady && !GrimmNpc2Interop.Init())
                {
                    if (!_p._loggedGrimmSpawnBlocked)
                    {
                        _p._loggedGrimmSpawnBlocked = true;
                        _p.PrintWarning(" Boss spawn deferred until GrimmNPC2 is available (Harmony mod loads after Oxide).");
                    }

                    return null;
                }

                if (!GrimmNpc2Interop.IsGrimmRuntimeReady())
                {
                    if (!_p._loggedGrimmRuntimeNotReady)
                    {
                        _p._loggedGrimmRuntimeNotReady = true;
                        _p.PrintWarning(" Boss spawn deferred until GrimmNPC2 mod runtime is ready (GrimmNPC2.Instance).");
                    }

                    return null;
                }

                string configured = !string.IsNullOrWhiteSpace(cfg.Gen2BossPrefab)
                    ? cfg.Gen2BossPrefab.Trim()
                    : _p._pluginConfig.BossPrefab;
                if (!_p.ValidateGen2ScientistPrefab(configured, out string prefabPath))
                    return null;

                // Match SpawnGroup: inactive prefab, RegisterPending before Awake/Spawn so nav/agent init runs at a snapped position.
                var ent = GameManager.server.CreateEntity(prefabPath, pos, Quaternion.identity, false);
                var npc = ent as ScientistNPC2;
                if (npc == null)
                {
                    TryKillEntity(ent);
                    _p.Puts(" ERROR: BossPrefab is not ScientistNPC2.");
                    return null;
                }

                npc._health = cfg.Health;
                npc.startHealth = cfg.Health;

                object data = _resolver.BuildBossProfile(cfg, pos, squadId, GrimmNpc2Interop.Fsm.Heavy);
                if (data == null)
                {
                    _p.Puts(" ERROR: failed to build CustomNpcData2.");
                    TryKillEntity(npc);
                    return null;
                }

                GrimmNpc2Interop.Set(data, "HomePosition", pos);

                if (!GrimmNpc2Interop.RegisterPending(npc, data))
                {
                    _p.Puts(" ERROR: GrimmNPC2 RegisterPending failed (mod Instance null or invalid data).");
                    TryKillEntity(npc);
                    return null;
                }

                PoolableEx.AwakeFromInstantiate(((Component)npc).gameObject);
                npc.Spawn();
                _p.ScheduleGen2BossInventoryEquip(npc, cfg, cfg.Name);
                return npc;
            }

            /// <summary>Tries each position in profile order; skips failed NavMesh snaps instead of aborting the whole spawn.</summary>
            private bool TryPickMonumentSpawn(NpcConfig cfg, out Vector3 pos)
            {
                pos = Vector3.zero;
                if (cfg.Monuments == null || cfg.Monuments.Count == 0) return false;

                foreach (MonumentPositionsConfig m in cfg.Monuments)
                {
                    if (m?.Positions == null || m.Positions.Count == 0) continue;
                    string monLabel = string.IsNullOrEmpty(m.Name) ? "(unnamed)" : m.Name;
                    foreach (string s in m.Positions)
                    {
                        if (!ParseVector(s, out Vector3 v) || v == Vector3.zero) continue;
                        if (!TrySnapToNavMesh(ref v, 150f))
                        {
                            _p.DebugLog(" Monument '" + monLabel + "' position could not snap to NavMesh; trying next.");
                            continue;
                        }

                        pos = v;
                        return true;
                    }
                }

                return false;
            }

            private Vector3 SelectSpawnPosition(NpcConfig cfg, out bool terrainFallback)
            {
                terrainFallback = false;
                // 1) Per-boss JSON "List of monument locations" (profile intent) before CustomMap/Global.json overrides.
                if (TryPickMonumentSpawn(cfg, out Vector3 monumentPos))
                    return monumentPos;

                // 2) oxide/data/GrimmBoss/CustomMap/Global.json (custom map servers, optional).
                if (!string.IsNullOrEmpty(cfg.Name)
                    && _p._customMapPositionsByBossName.TryGetValue(cfg.Name, out var globalPosList)
                    && globalPosList != null
                    && globalPosList.Count > 0)
                {
                    int start = UnityEngine.Random.Range(0, globalPosList.Count);
                    for (int k = 0; k < globalPosList.Count; k++)
                    {
                        int idx = (start + k) % globalPosList.Count;
                        if (ParseVector(globalPosList[idx], out Vector3 v) && v != Vector3.zero)
                        {
                            if (!TrySnapToNavMesh(ref v, 150f))
                            {
                                _p.DebugLog(" Custom map position could not be snapped to NavMesh for '" + cfg.Name + "'; trying next.");
                                continue;
                            }

                            return v;
                        }
                    }
                }

                float half = TerrainMeta.Size.x * 0.45f;
                var p = new Vector3(UnityEngine.Random.Range(-half, half), 0f, UnityEngine.Random.Range(-half, half));
                p.y = TerrainMeta.HeightMap.GetHeight(p);
                if (!TrySnapToNavMesh(ref p, 150f))
                {
                    _p.DebugLog(" Random terrain position could not be snapped to NavMesh.");
                    return Vector3.zero;
                }

                terrainFallback = true;
                return p;
            }

            private static bool ParseVector(string raw, out Vector3 v)
            {
                v = Vector3.zero;
                if (string.IsNullOrEmpty(raw)) return false;
                string t = raw.Trim().Trim('(', ')');
                string[] p = t.Split(',');
                if (p.Length != 3) return false;
                if (!float.TryParse(p[0].Trim(), out float x)) return false;
                if (!float.TryParse(p[1].Trim(), out float y)) return false;
                if (!float.TryParse(p[2].Trim(), out float z)) return false;
                v = new Vector3(x, y, z);
                return true;
            }
        }

        #endregion

        #region HelperSpawnService

        private sealed class HelperSpawnService
        {
            private readonly GrimmBoss _p;
            private readonly BossProfileResolver _resolver;

            public HelperSpawnService(GrimmBoss plugin, BossProfileResolver resolver)
            {
                _p = plugin;
                _resolver = resolver;
            }

            public void SpawnNpcHelpers(BossController boss, NpcAbility ability, Vector3 near)
            {
                if (ability?.ConfigNpc == null || ability.Count <= 0) return;
                if (!GrimmNpc2Interop.IsGrimmRuntimeReady())
                    return;

                string configured = !string.IsNullOrWhiteSpace(boss.Config.Gen2BossPrefab)
                    ? boss.Config.Gen2BossPrefab.Trim()
                    : _p._pluginConfig.BossPrefab;
                if (!_p.ValidateGen2ScientistPrefab(configured, out string prefabPath))
                    return;

                for (int i = 0; i < ability.Count; i++)
                {
                    Vector3 pos = near + UnityEngine.Random.insideUnitSphere * 6f;
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                    if (!TrySnapToNavMesh(ref pos, 150f))
                        continue;

                    var ent = GameManager.server.CreateEntity(prefabPath, pos, Quaternion.identity, false);
                    var npc = ent as ScientistNPC2;
                    if (npc == null)
                    {
                        TryKillEntity(ent);
                        continue;
                    }

                    npc._health = ability.ConfigNpc.Health;
                    npc.startHealth = ability.ConfigNpc.Health;

                    object data = _resolver.BuildHelperProfile(
                        ability.ConfigNpc,
                        pos,
                        boss.BossNetId,
                        boss.SquadId,
                        GrimmNpc2Interop.Kind.Helper,
                        GrimmNpc2Interop.Fsm.Heavy);

                    if (!GrimmNpc2Interop.RegisterPending(npc, data))
                    {
                        TryKillEntity(npc);
                        continue;
                    }

                    PoolableEx.AwakeFromInstantiate(((Component)npc).gameObject);
                    npc.Spawn();
                    string helperLog = "Helper";
                    if (ability.ConfigNpc.Names != null && ability.ConfigNpc.Names.Count > 0)
                        helperLog = ability.ConfigNpc.Names[0];
                    _p.ScheduleGen2HelperInventoryEquip(npc, ability.ConfigNpc, helperLog);
                    boss.TrackHelper(npc.net.ID.Value, ability.DespawnTime);
                }
            }

            public void SpawnAnimals(BossController boss, AnimalAbility ability, Vector3 near)
            {
                if (_p.AnimalSpawn == null || ability == null || ability.Count <= 0 || string.IsNullOrWhiteSpace(ability.Type)) return;
                try
                {
                    for (int i = 0; i < ability.Count; i++)
                    {
                        object res = _p.AnimalSpawn.Call("SpawnAnimal", ability.Type, near + UnityEngine.Random.insideUnitSphere * 4f);
                        if (res is BaseEntity be)
                            boss.TrackAnimal(be, ability.DespawnTime);
                    }
                }
                catch (Exception ex)
                {
                    _p.PrintWarning($"GrimmBoss AnimalSpawn: {ex.Message}");
                }
            }
        }

        #endregion

        #region BossController

        private sealed class BossController
        {
            public readonly GrimmBoss Plugin;
            public readonly ScientistNPC2 Boss;
            public readonly NpcConfig Config;
            public readonly Vector3 SpawnPosition;
            public readonly ulong SquadId;
            public readonly bool TerrainFallbackSpawn;

            public ulong BossNetId => Boss != null && Boss.net != null ? Boss.net.ID.Value : 0;

            public bool NpcHelpersActive => _helperNetIds.Count > 0;

            private readonly HashSet<ulong> _helperNetIds = new HashSet<ulong>();
            private readonly Dictionary<ulong, float> _helperDespawnAt = new Dictionary<ulong, float>();
            private readonly List<(BaseEntity ent, float despawnAt)> _animals = new List<(BaseEntity, float)>();

            public float NextAssistPropagateTime;
            public float PhaseTimer;
            public readonly RadiusState RadiusState = new RadiusState();
            public Timer MarkerUpdateTimer;

            public BossController(GrimmBoss plugin, ScientistNPC2 boss, NpcConfig cfg, Vector3 spawn, ulong squad, bool terrainFb)
            {
                Plugin = plugin;
                Boss = boss;
                Config = cfg;
                SpawnPosition = spawn;
                SquadId = squad;
                TerrainFallbackSpawn = terrainFb;
            }

            public void TrackHelper(ulong netId, float despawnSeconds)
            {
                if (netId == 0) return;
                _helperNetIds.Add(netId);
                _helperDespawnAt[netId] = Time.realtimeSinceStartup + Mathf.Max(5f, despawnSeconds);
                GrimmNpc2Interop.SetRuntimeHelperPhaseActive(BossNetId, true);
            }

            public void TrackAnimal(BaseEntity ent, float despawnSeconds)
            {
                if (ent == null) return;
                _animals.Add((ent, Time.realtimeSinceStartup + Mathf.Max(5f, despawnSeconds)));
            }

            public void OnHelperDied(ulong netId)
            {
                _helperNetIds.Remove(netId);
                _helperDespawnAt.Remove(netId);
                if (_helperNetIds.Count == 0)
                    GrimmNpc2Interop.SetRuntimeHelperPhaseActive(BossNetId, false);
            }

            public void TickDespawns()
            {
                float now = Time.realtimeSinceStartup;
                int pairCount = _helperDespawnAt.Count;
                if (pairCount > 0)
                {
                    var snap = new KeyValuePair<ulong, float>[pairCount];
                    int si = 0;
                    foreach (KeyValuePair<ulong, float> kv in _helperDespawnAt)
                        snap[si++] = kv;
                    for (int j = 0; j < snap.Length; j++)
                    {
                        KeyValuePair<ulong, float> kv = snap[j];
                        if (now < kv.Value) continue;
                        if (GrimmNpc2Interop.ResolveEntity(kv.Key, out BaseEntity e) && e != null && !e.IsDestroyed)
                            e.Kill();
                        OnHelperDied(kv.Key);
                    }
                }

                for (int i = _animals.Count - 1; i >= 0; i--)
                {
                    var (ent, t) = _animals[i];
                    if (ent == null || ent.IsDestroyed || now >= t)
                    {
                        if (ent != null && !ent.IsDestroyed) ent.Kill();
                        _animals.RemoveAt(i);
                    }
                }
            }

            public void OnBossDamagedByPlayer(BasePlayer attacker)
            {
                if (attacker == null || Boss == null) return;
                if (Time.time < NextAssistPropagateTime) return;
                NextAssistPropagateTime = Time.time + 2f;
                GrimmNpc2Interop.PropagateTargetToAssistGroup(Boss, attacker, true);
            }

            public void OnHelperDamagedByPlayer(ScientistNPC2 helper, BasePlayer attacker)
            {
                if (attacker == null) return;
                if (Time.time < NextAssistPropagateTime) return;
                NextAssistPropagateTime = Time.time + 2f;
                GrimmNpc2Interop.PropagateTargetToAssistGroup(helper, attacker, true);
            }

            public void CopyHelperNetIdsTo(List<ulong> dest)
            {
                dest.Clear();
                foreach (ulong id in _helperNetIds)
                    dest.Add(id);
            }
        }

        #endregion

        #region AttackCycleManager

        private sealed class AttackCycleManager
        {
            private readonly GrimmBoss _p;
            private readonly RadiusAbilitySystem _radius;
            private Timer _tPhase;

            public AttackCycleManager(GrimmBoss plugin, RadiusAbilitySystem radius)
            {
                _p = plugin;
                _radius = radius;
            }

            public void StartGlobalTimers()
            {
                StopGlobalTimers();
                _tPhase = _p.timer.Every(1.25f, TickPhases);
            }

            public void StopGlobalTimers()
            {
                _tPhase?.Destroy();
                _tPhase = null;
            }

            private void TickPhases()
            {
                foreach (var c in _p._controllersByNetId.Values)
                {
                    if (c.Boss == null || c.Boss.IsDestroyed) continue;
                    c.TickDespawns();
                    c.PhaseTimer += 1.25f;

                    if (c.Config.ReturnToSpawnPoint && c.Config.ReturnToSpawnPointInterval > 0f
                        && c.PhaseTimer >= c.Config.ReturnToSpawnPointInterval)
                    {
                        c.PhaseTimer = 0f;
                        GrimmNpc2Interop.SetDestinationRespectingHomeTether(c.Boss, c.SpawnPosition);
                    }
                }

                _radius?.TickRadiusAbilitiesGlobal();
            }
        }

        #endregion

        #region RadiusAbilitySystem

        private sealed class RadiusAbilitySystem
        {
            private readonly GrimmBoss _p;

            public RadiusAbilitySystem(GrimmBoss plugin) => _p = plugin;

            public void OnBossDamaged(BossController ctrl, BasePlayer attacker, HitInfo info)
            {
                var td = ctrl.Config.TakeDamageActions;
                if (td == null || td.IsDisable) return;
                if (td.Vampirism > 0f && ctrl.Boss.health < ctrl.Boss._maxHealth)
                {
                    float add = info.damageTypes.Total() * td.Vampirism / 100f;
                    float nh = Mathf.Min(ctrl.Boss._maxHealth, ctrl.Boss.health + add);
                    NextTick(() => { if (ctrl.Boss != null && !ctrl.Boss.IsDestroyed) ctrl.Boss._health = nh; });
                }

                if (attacker == null) return;
                if (td.CaloriesTarget != 0f) attacker.metabolism.calories.Add(-td.CaloriesTarget);
                if (td.HydrationTarget != 0f) attacker.metabolism.hydration.Add(-td.HydrationTarget);
                if (td.RadiationTarget != 0f) attacker.metabolism.radiation_poison.Add(td.RadiationTarget);
                if (td.BleedingTarget != 0f) attacker.metabolism.bleeding.Add(td.BleedingTarget);
            }

            private void NextTick(Action a) => _p.NextTick(a);

            public void TickRadiusAbilitiesGlobal()
            {
                foreach (var ctrl in _p._controllersByNetId.Values)
                    TickOne(ctrl);
            }

            private void TickOne(BossController ctrl)
            {
                var ra = ctrl.Config.RadiusActions;
                if (ra == null || ra.Radius <= 0f) return;
                if (ctrl.NpcHelpersActive && ctrl.Config.ImmuneWhileNpcHelpersActive) return;

                var boss = ctrl.Boss;
                if (boss == null || boss.IsDestroyed) return;

                BasePlayer target = FindPriorityPlayerInRadius(boss, ra.Radius);
                if (target == null) return;

                float now = Time.time;
                if (!ctrl.RadiusState.EnsureInitialized(now, ra))
                    return;

                if (ctrl.RadiusState.UseAbility(now, ra, out int abi))
                {
                    switch (abi)
                    {
                        case 1: DoSpikes(ctrl, target, ra); break;
                        case 2: DoFire(ctrl, target, ra); break;
                        case 3: DoElectric(ctrl, target, ra); break;
                        case 4: DoWounded(ctrl, target, ra); break;
                        case 5: DoFreeze(ctrl, target, ra); break;
                        case 6: _p._helperSpawn.SpawnNpcHelpers(ctrl, ra.NpcAbility, target.transform.position); break;
                        case 7: _p._helperSpawn.SpawnAnimals(ctrl, ra.AnimalAbility, target.transform.position); break;
                    }
                }
            }

            private BasePlayer FindPriorityPlayerInRadius(ScientistNPC2 boss, float radius)
            {
                List<BasePlayer> list = Facepunch.Pool.Get<List<BasePlayer>>();
                try
                {
                    Vis.Entities(boss.transform.position, radius, list, Layers.Mask.Player_Server);
                    BasePlayer best = null;
                    float bestD = float.MaxValue;
                    foreach (var p in list)
                    {
                        if (p == null || p.IsSleeping() || p.IsWounded() || p.IsDead()) continue;
                        float d = Vector3.Distance(p.transform.position, boss.transform.position);
                        if (d <= radius && d < bestD)
                        {
                            bestD = d;
                            best = p;
                        }
                    }
                    return best;
                }
                finally
                {
                    Facepunch.Pool.FreeUnmanaged(ref list);
                }
            }

            private static void DoSpikes(BossController ctrl, BasePlayer target, RadiusActionsConfig ra)
            {
                Vector3 pos = target.transform.position;
                var spikes = GameManager.server.CreateEntity("assets/prefabs/deployable/floor spikes/spikes.floor.prefab", pos, Quaternion.identity) as BaseEntity;
                if (spikes == null) return;
                spikes.enableSaving = false;
                spikes.Spawn();
                foreach (var c in spikes.GetComponentsInChildren<Collider>())
                    UnityEngine.Object.DestroyImmediate(c);
                target.Hurt(ra.DamageSpikes, DamageType.Stab, ctrl.Boss, false);
                ctrl.Plugin.timer.In(5f, () => { if (spikes != null && !spikes.IsDestroyed) spikes.Kill(); });
            }

            private static void DoFire(BossController ctrl, BasePlayer target, RadiusActionsConfig ra)
            {
                target.Hurt(ra.DamageFire, DamageType.Heat, ctrl.Boss, false);
            }

            private static void DoElectric(BossController ctrl, BasePlayer target, RadiusActionsConfig ra)
            {
                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.shock.prefab", target.transform.position);
                float d = ra.DamageElectricShock > 0f ? ra.DamageElectricShock : 5f;
                target.Hurt(d, DamageType.ElectricShock, ctrl.Boss, false);
            }

            private static void DoWounded(BossController ctrl, BasePlayer target, RadiusActionsConfig ra)
            {
                target.metabolism.bleeding.Add(10f);
            }

            private static void DoFreeze(BossController ctrl, BasePlayer target, RadiusActionsConfig ra)
            {
                target.metabolism.temperature.SetValue(-50f);
            }
        }

        private sealed class RadiusState
        {
            private float _nextSpikes, _nextFire, _nextElec, _nextWound, _nextFreeze, _nextNpc, _nextAnimal;

            public bool EnsureInitialized(float now, RadiusActionsConfig ra)
            {
                if (_nextSpikes <= 0f && ra.TimeToSpikes > 0) _nextSpikes = now;
                if (_nextFire <= 0f && ra.TimeToFire > 0) _nextFire = now;
                if (_nextElec <= 0f && ra.TimeToElectricShock > 0) _nextElec = now;
                if (_nextWound <= 0f && ra.TimeToWounded > 0) _nextWound = now;
                if (_nextFreeze <= 0f && ra.TimeToFreeze > 0) _nextFreeze = now;
                if (_nextNpc <= 0f && ra.NpcAbility != null && ra.NpcAbility.Time > 0) _nextNpc = now + 15f;
                if (_nextAnimal <= 0f && ra.AnimalAbility != null && ra.AnimalAbility.Time > 0) _nextAnimal = now;
                return true;
            }

            public bool UseAbility(float now, RadiusActionsConfig ra, out int abilityId)
            {
                abilityId = 0;
                var candidates = new List<int>();
                void AddCandidate(float next, int id, int cdSec)
                {
                    if (cdSec < 0) return;
                    if (now >= next) candidates.Add(id);
                }

                AddCandidate(_nextSpikes, 1, ra.TimeToSpikes);
                AddCandidate(_nextFire, 2, ra.TimeToFire);
                AddCandidate(_nextElec, 3, ra.TimeToElectricShock);
                AddCandidate(_nextWound, 4, ra.TimeToWounded);
                AddCandidate(_nextFreeze, 5, ra.TimeToFreeze);
                AddCandidate(_nextNpc, 6, ra.NpcAbility?.Time ?? -1);
                AddCandidate(_nextAnimal, 7, ra.AnimalAbility?.Time ?? -1);

                if (candidates.Count == 0) return false;

                int pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                abilityId = pick;
                float Schedule(int cdSec) => now + Mathf.Max(1f, cdSec);

                switch (pick)
                {
                    case 1: _nextSpikes = Schedule(ra.TimeToSpikes); break;
                    case 2: _nextFire = Schedule(ra.TimeToFire); break;
                    case 3: _nextElec = Schedule(ra.TimeToElectricShock); break;
                    case 4: _nextWound = Schedule(ra.TimeToWounded); break;
                    case 5: _nextFreeze = Schedule(ra.TimeToFreeze); break;
                    case 6: _nextNpc = Schedule(ra.NpcAbility.Time); break;
                    case 7: _nextAnimal = Schedule(ra.AnimalAbility.Time); break;
                }

                if (ra.UseOnlyOneAbility)
                {
                    void Bump(ref float field, int cdSec)
                    {
                        if (cdSec < 0) return;
                        field = Schedule(cdSec);
                    }

                    Bump(ref _nextSpikes, ra.TimeToSpikes);
                    Bump(ref _nextFire, ra.TimeToFire);
                    Bump(ref _nextElec, ra.TimeToElectricShock);
                    Bump(ref _nextWound, ra.TimeToWounded);
                    Bump(ref _nextFreeze, ra.TimeToFreeze);
                    if (ra.NpcAbility != null) Bump(ref _nextNpc, ra.NpcAbility.Time);
                    if (ra.AnimalAbility != null) Bump(ref _nextAnimal, ra.AnimalAbility.Time);
                }

                return true;
            }
        }

        #endregion

        #region RewardSystem + Cleanup + Markers

        private sealed class RewardSystem
        {
            private readonly GrimmBoss _p;
            public RewardSystem(GrimmBoss plugin) => _p = plugin;

            public void ApplyKillRewards(BossController ctrl, BasePlayer killer)
            {
                var ec = ctrl.Config.Economic;
                if (ec == null || killer == null) return;
                if (_p.plugins.Exists("Economics") && ec.Economics > 0d)
                    _p.Economics?.Call("Deposit", killer.userID.ToString(), ec.Economics);
                if (_p.plugins.Exists("ServerRewards") && ec.ServerRewards > 0)
                    _p.ServerRewards?.Call("AddPoints", killer.userID, ec.ServerRewards);
                if (_p.plugins.Exists("IQEconomic") && ec.IQEconomic > 0)
                    _p.IQEconomic?.Call("API_SET_BALANCE", killer.userID, ec.IQEconomic);
                if (_p.plugins.Exists("XPerience") && ec.XPerience > 0d)
                    _p.XPerience?.Call("GiveXP", killer, ec.XPerience);
            }

            public void SpawnCrate(BossController ctrl)
            {
                string prefab = ctrl.Config.CratePrefab;
                if (string.IsNullOrEmpty(prefab)) return;
                var ent = GameManager.server.CreateEntity(prefab, ctrl.SpawnPosition, Quaternion.identity);
                if (ent == null) return;
                ent.enableSaving = false;
                ent.Spawn();
                if (_p._pluginConfig.Pve && _p.PveMode != null)
                    _p.PveMode.Call("CrateAddScientistPveMode", ent.net.ID.Value, ctrl.BossNetId);
            }
        }

        private sealed class MarkerNotificationSystem
        {
            private readonly GrimmBoss _p;
            public MarkerNotificationSystem(GrimmBoss plugin) => _p = plugin;

            public void AttachBossMarkers(BossController ctrl)
            {
                var m = ctrl.Config.Marker;
                if (m == null || !m.IsMarker || ctrl.Boss == null) return;

                MapMarkerGenericRadius radiusMarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", ctrl.Boss.transform.position) as MapMarkerGenericRadius;
                if (radiusMarker != null)
                {
                    radiusMarker.Spawn();
                    radiusMarker.radius = m.Radius;
                    radiusMarker.alpha = m.Alpha;
                    radiusMarker.color1 = new Color(m.Color.R, m.Color.G, m.Color.B);
                }

                VendingMachineMapMarker vend = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", ctrl.Boss.transform.position) as VendingMachineMapMarker;
                if (vend != null)
                {
                    vend.Spawn();
                    vend.SetVendingMachine(null, $"{ctrl.Config.Name}");
                }

                ctrl.MarkerUpdateTimer?.Destroy();
                ctrl.MarkerUpdateTimer = _p.timer.Every(1f, () =>
                {
                    if (ctrl.Boss == null || ctrl.Boss.IsDestroyed)
                    {
                        radiusMarker?.Kill();
                        vend?.Kill();
                        ctrl.MarkerUpdateTimer?.Destroy();
                        ctrl.MarkerUpdateTimer = null;
                        return;
                    }

                    if (radiusMarker != null)
                    {
                        radiusMarker.transform.position = ctrl.Boss.transform.position;
                        radiusMarker.SendUpdate();
                    }

                    if (vend != null)
                    {
                        vend.transform.position = ctrl.Boss.transform.position;
                        vend.SetVendingMachine(null, $"{ctrl.Config.Name} ({(int)ctrl.Boss.health} HP)");
                        vend.SendNetworkUpdate();
                    }
                });
            }

            public void AnnounceSpawn(BossController ctrl)
            {
                if (!_p._pluginConfig.IsChat || ctrl.Config == null || !ctrl.Config.IsChat) return;
                string grid = MapHelper.GridToString(MapHelper.PositionToGrid(ctrl.Boss.transform.position));
                _p.PrintToChat($"{_p._pluginConfig.Prefix} {ctrl.Config.Name} appeared near {grid}");
            }

            public void AnnounceKill(BossController ctrl, BasePlayer killer)
            {
                if (!_p._pluginConfig.IsChat || ctrl.Config == null || !ctrl.Config.IsChat) return;
                string grid = MapHelper.GridToString(MapHelper.PositionToGrid(ctrl.Boss.transform.position));
                _p.PrintToChat($"{_p._pluginConfig.Prefix} {killer.displayName} defeated {ctrl.Config.Name} near {grid}");
            }
        }

        private sealed class CleanupManager
        {
            private readonly GrimmBoss _p;
            private readonly List<ulong> _helperKillScratch = new List<ulong>(32);

            public CleanupManager(GrimmBoss plugin) => _p = plugin;

            public void HandleBossDeath(BossController ctrl, ScientistNPC2 npc, HitInfo info)
            {
                ctrl.MarkerUpdateTimer?.Destroy();
                ctrl.MarkerUpdateTimer = null;

                BasePlayer killer = info?.InitiatorPlayer;
                _p._markers.AnnounceKill(ctrl, killer);
                _p._rewards.ApplyKillRewards(ctrl, killer);
                _p._rewards.SpawnCrate(ctrl);

                ctrl.CopyHelperNetIdsTo(_helperKillScratch);
                for (int i = 0; i < _helperKillScratch.Count; i++)
                {
                    ulong hid = _helperKillScratch[i];
                    if (GrimmNpc2Interop.ResolveEntity(hid, out BaseEntity e) && e != null && !e.IsDestroyed)
                        e.Kill();
                }

                GrimmNpc2Interop.UnregisterNpc(ctrl.BossNetId);
                _p.UnregisterController(ctrl.BossNetId);

                if (!ctrl.Config.DisableTimer)
                {
                    _p.timer.In(UnityEngine.Random.Range(ctrl.Config.MinTime, ctrl.Config.MaxTime), () =>
                    {
                        _p._spawnService.SpawnQueue.Add(ctrl.Config.Name);
                    });
                }

                Interface.CallHook("OnBossKilled", npc, killer);
            }

            public void DestroyBossController(BossController ctrl, bool silent)
            {
                if (ctrl?.Boss != null && !ctrl.Boss.IsDestroyed)
                    ctrl.Boss.Kill();
                GrimmNpc2Interop.UnregisterNpc(ctrl.BossNetId);
                _p.UnregisterController(ctrl.BossNetId);
            }
        }

        #endregion

    }
}
