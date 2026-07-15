using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using Building = BuildingManager.Building;
using Pool = Facepunch.Pool;

namespace LimitEntities
{
    public enum LogLevel : byte
    {
        Off,
        Error,
        Warning,
        Info,
        Debug,
    }

    public sealed class LimitEntitiesService
    {
        public const string PermissionAdmin = "limitentities.admin";
        public const string PermissionImmunity = "limitentities.immunity";
        public const float DefaultBuildingDetectionRange = 1.51f;

        private static readonly object FalseObj = false;
        private static readonly Regex Tags = new Regex(
            "<color=.+?>|</color>|<size=.+?>|</size>|<i>|</i>|<b>|</b>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly string _serverRoot;
        private readonly string _configPath;
        private readonly string _dataPath;
        private readonly Cache _cache = new Cache();
        private PluginConfig _config;
        private StoredData _storedData = new StoredData();
        private float _buildingDetectionRange = DefaultBuildingDetectionRange;
        private readonly Dictionary<string, string> _langEn = CreateDefaultLang();
        private bool _initialized;

        public PluginConfig Config => _config;
        public bool IsReady => _initialized;

        public LimitEntitiesService(string serverRoot)
        {
            _serverRoot = serverRoot;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "LimitEntities.json");
            _dataPath = Path.Combine(serverRoot, "HarmonyData", "LimitEntities.json");
        }

        #region Config / Data models

        public class PluginConfig
        {
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(LogLevel.Off)]
            [JsonProperty(PropertyName = "Log Level (Debug, Info, Warning, Error, Off)", Order = 4)]
            public LogLevel LoggingLevel { get; set; }

            [JsonProperty(PropertyName = "Enable GameTip notifications")]
            public bool GameTipNotificationsEnabled { get; set; }

            [JsonProperty(PropertyName = "Enable notifications in chat")]
            public bool ChatNotificationsEnabled { get; set; }

            [JsonProperty(PropertyName = "Chat steamID icon")]
            public ulong SteamIDIcon { get; set; }

            [JsonProperty(PropertyName = "Commands list")]
            public string[] Commands { get; set; }

            [JsonProperty(PropertyName = "Warn when more than %")]
            [DefaultValue(80f)]
            public float WarnPercent { get; set; }

            [JsonProperty(PropertyName = "Building detection range")]
            [DefaultValue(1.51f)]
            public float BuildingDetectionRange { get; set; }

            [JsonProperty(PropertyName = "Track growable entities")]
            public bool TrackGrowable { get; set; }

            [JsonProperty(PropertyName = "Track powered lights")]
            public bool TrackPowerLights { get; set; }

            [JsonProperty(PropertyName = "Enable ChestStacks plugin support")]
            public bool ChestStacksEnabled { get; set; }

            [JsonProperty(PropertyName = "Enable PlaceryExtended plugin support")]
            public bool PlaceryExtendedEnabled { get; set; }

            [JsonProperty(PropertyName = "Excluded list")]
            public string[] Excluded { get; set; }

            [JsonProperty(PropertyName = "Excluded skin IDs")]
            public ulong[] ExcludedSkinID { get; set; }

            [JsonProperty(PropertyName = "Use ZoneManager")]
            public bool UseZoneManager { get; set; }

            [JsonProperty(PropertyName = "ZoneManager include mode (true = include mode / false = exclude mode)")]
            public bool ZoneManagerIncludeMode { get; set; }

            [JsonProperty(PropertyName = "ZoneIDs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] ZoneManagerZoneIDs { get; set; }

            [JsonProperty(PropertyName = "Entity Groups")]
            public List<EntityGroup> EntityGroups { get; set; }

            [JsonProperty(PropertyName = "Permissions")]
            public PermissionEntry[] Permissions { get; set; }
        }

        public class PermissionEntry
        {
            [JsonProperty(PropertyName = "Permission")]
            public string Permission { get; set; }

            [JsonProperty(PropertyName = "Priority")]
            public int Priority { get; set; }

            [JsonProperty(PropertyName = "Limits Global")]
            public LimitsEntry LimitsGlobal { get; set; }

            [JsonProperty(PropertyName = "Limits Building")]
            public LimitsEntry LimitsBuilding { get; set; }

            [JsonProperty(PropertyName = "Limits Radius")]
            public LimitsRadius LimitsRadius { get; set; }

            [JsonProperty(PropertyName = "Limits Powered Lights")]
            public LimitsPoweredLights LimitsPoweredLights { get; set; }

            [JsonProperty(PropertyName = "Prevent excessive merging of buildings")]
            public bool MergingCheck { get; set; }

            [JsonConstructor]
            public PermissionEntry() { }

            public PermissionEntry(PermissionEntry entry)
            {
                Permission = entry?.Permission ?? string.Empty;
                Priority = entry?.Priority ?? 0;
                LimitsGlobal = entry?.LimitsGlobal ?? new LimitsEntry();
                LimitsBuilding = entry?.LimitsBuilding ?? new LimitsEntry();
                LimitsRadius = entry?.LimitsRadius ?? new LimitsRadius();
                LimitsPoweredLights = new LimitsPoweredLights(entry?.LimitsPoweredLights);
                MergingCheck = entry?.MergingCheck ?? false;
            }
        }

        public class LimitsEntry
        {
            [JsonProperty(PropertyName = "Limit Total")]
            public int LimitTotal { get; set; }

            [JsonProperty(PropertyName = "Limits Entities")]
            public SortedDictionary<string, int> LimitsEntities { get; set; } = new SortedDictionary<string, int>();

            [JsonIgnore] public readonly Dictionary<uint, int> LimitEntitiesCache = new Dictionary<uint, int>();

            public int GetEntityLimit(uint prefabID)
            {
                LimitEntitiesCache.TryGetValue(prefabID, out int limit);
                return limit;
            }
        }

        public class LimitsPoweredLights
        {
            [JsonProperty(PropertyName = "Maximum Point Count")]
            [DefaultValue(-1)]
            public int MaxPoints { get; set; } = -1;

            [JsonProperty(PropertyName = "Maximum Total Length")]
            [DefaultValue(-1f)]
            public float MaxLength { get; set; } = -1f;

            [JsonProperty(PropertyName = "Maximum Distance Between Points")]
            [DefaultValue(-1f)]
            public float MaxLengthPoint { get; set; } = -1f;

            [JsonConstructor]
            public LimitsPoweredLights() { }

            public LimitsPoweredLights(LimitsPoweredLights entry)
            {
                MaxPoints = entry?.MaxPoints ?? -1;
                MaxLength = entry?.MaxLength ?? -1f;
                MaxLengthPoint = entry?.MaxLengthPoint ?? -1f;
            }
        }

        public class LimitsRadius
        {
            [JsonProperty(PropertyName = "Radius")]
            public float Radius { get; set; }

            [JsonProperty(PropertyName = "Limits Entities")]
            public SortedDictionary<string, int> LimitsEntities { get; set; } = new SortedDictionary<string, int>();

            [JsonIgnore] public readonly Dictionary<uint, int> LimitEntitiesCache = new Dictionary<uint, int>();

            public int GetEntityLimit(uint prefabID)
            {
                LimitEntitiesCache.TryGetValue(prefabID, out int limit);
                return limit;
            }
        }

        public class EntityGroup
        {
            [JsonProperty(PropertyName = "Group name")]
            public string Name { get; set; }

            [JsonIgnore] public uint ID { get; set; }

            [JsonProperty(PropertyName = "Group Entities list")]
            public List<string> ListEntities { get; set; }

            [JsonIgnore] public readonly List<uint> ListEntitiesCache = new List<uint>();
        }

        private class StoredData
        {
            public Dictionary<uint, ulong> BuildingsOwners { get; set; } = new Dictionary<uint, ulong>();
        }

        private class Cache
        {
            public readonly Dictionary<string, Dictionary<uint, string>> DisplayNames = new Dictionary<string, Dictionary<uint, string>>();
            public readonly Dictionary<uint, BuildingEntities> Buildings = new Dictionary<uint, BuildingEntities>();
            public readonly Dictionary<uint, HashSet<Vector3>> RadiusPrefabPositions = new Dictionary<uint, HashSet<Vector3>>();
            public readonly Dictionary<ulong, PlayerData> PlayersData = new Dictionary<ulong, PlayerData>();
            public readonly PermissionData PermissionData = new PermissionData();
            public readonly Prefabs Prefabs = new Prefabs();
            public readonly HashSet<ulong> EntitiesTracked = new HashSet<ulong>();
        }

        private class Prefabs
        {
            public readonly Dictionary<uint, string> ShortNames = new Dictionary<uint, string>();
            public readonly Dictionary<uint, uint> Groups = new Dictionary<uint, uint>();
            public readonly HashSet<uint> BuildingBlocks = new HashSet<uint>();
            public readonly HashSet<uint> Tracked = new HashSet<uint>();
        }

        private class PermissionData
        {
            public PermissionEntry[] Descending { get; set; } = Array.Empty<PermissionEntry>();
            public string[] Registered { get; set; } = Array.Empty<string>();
        }

        public class BuildingEntities
        {
            public uint BuildingID { get; set; }
            public readonly Dictionary<uint, int> EntitiesCount = new Dictionary<uint, int>();
            public readonly HashSet<ulong> EntitiesIds = new HashSet<ulong>();

            public BuildingEntities(uint buildingID) => BuildingID = buildingID;

            public void AddEntity(BaseEntity entity)
            {
                EntitiesIds.Add(entity.net.ID.Value);
                uint prefabID = LimitEntitiesMod.Service.GetPrefabID(entity.prefabID);
                EntitiesCount[prefabID] = GetEntityCount(prefabID) + 1;
            }

            public void RemoveEntity(BaseEntity entity)
            {
                EntitiesIds.Remove(entity.net.ID.Value);
                uint prefabID = LimitEntitiesMod.Service.GetPrefabID(entity.prefabID);
                if (!EntitiesCount.TryGetValue(prefabID, out int count)) return;
                if (count < 2) EntitiesCount.Remove(prefabID);
                else EntitiesCount[prefabID]--;
            }

            public int GetEntityCount(uint prefabID)
            {
                EntitiesCount.TryGetValue(prefabID, out int count);
                return count;
            }
        }

        public class PlayerData
        {
            public readonly string PlayerIdString;
            public PermissionEntry Perms;
            public bool HasImmunity { get; private set; }
            public readonly PlayerEntities PlayerEntities = new PlayerEntities();

            public PlayerData(ulong playerId)
            {
                PlayerIdString = playerId.ToString();
                UpdatePerms();
            }

            public void UpdatePerms()
            {
                HasImmunity = PermissionsBridge.UserHasPermission(PlayerIdString, PermissionImmunity);
                Perms = LimitEntitiesMod.Service?.GetPlayerPermissions(this);
            }

            public void AddEntity(uint prefabID) => PlayerEntities.AddEntity(prefabID);
            public void RemoveEntity(uint prefabID) => PlayerEntities.RemoveEntity(prefabID);

            public bool CanBuild() => Perms == null || HasImmunity || Perms.LimitsGlobal.LimitTotal != 0;

            public bool IsGlobalLimit()
            {
                if (Perms == null) return false;
                return PlayerEntities.TotalCount >= Perms.LimitsGlobal.LimitTotal;
            }

            public bool IsGlobalLimit(uint prefabId)
            {
                if (Perms == null) return false;
                if (!Perms.LimitsGlobal.LimitEntitiesCache.TryGetValue(prefabId, out int limitGlobal)) return false;
                PlayerEntities.Entities.TryGetValue(prefabId, out int count);
                return count >= limitGlobal;
            }

            public bool IsBuildingLimit(BuildingEntities entities)
            {
                if (Perms == null) return false;
                return entities.EntitiesIds.Count >= Perms.LimitsBuilding.LimitTotal;
            }

            public bool IsBuildingLimit(BuildingEntities entities, uint prefabId)
            {
                if (Perms == null) return false;
                if (!Perms.LimitsBuilding.LimitEntitiesCache.TryGetValue(prefabId, out int limit)) return false;
                entities.EntitiesCount.TryGetValue(prefabId, out int count);
                return count >= limit;
            }

            public bool IsRadiusLimit(uint prefabId, Vector3 position, Dictionary<uint, HashSet<Vector3>> radiusPrefabPositions)
            {
                if (Perms == null || Perms.LimitsRadius.Radius <= 0f) return false;
                if (!Perms.LimitsRadius.LimitEntitiesCache.TryGetValue(prefabId, out int limitRadius)) return false;
                if (!radiusPrefabPositions.TryGetValue(prefabId, out HashSet<Vector3> positions) || positions.Count == 0) return false;

                int count = 0;
                float radiusSqr = Perms.LimitsRadius.Radius * Perms.LimitsRadius.Radius;
                foreach (Vector3 entityPos in positions)
                {
                    if (Vector3.SqrMagnitude(position - entityPos) <= radiusSqr && ++count >= limitRadius)
                        return true;
                }
                return false;
            }

            public float GetGlobalPercentage()
            {
                if (Perms == null || Perms.LimitsGlobal.LimitTotal <= 0) return 0f;
                return (float)PlayerEntities.TotalCount / Perms.LimitsGlobal.LimitTotal * 100f;
            }

            public float GetGlobalPercentage(uint prefabId)
            {
                if (Perms == null) return 0f;
                if (!Perms.LimitsGlobal.LimitEntitiesCache.TryGetValue(prefabId, out int limit) || limit <= 0) return 0f;
                if (!PlayerEntities.Entities.TryGetValue(prefabId, out int count)) return 0f;
                return (float)count / limit * 100f;
            }

            public float GetBuildingPercentage(BuildingEntities entities)
            {
                if (Perms == null || Perms.LimitsBuilding.LimitTotal <= 0) return 0f;
                return (float)entities.EntitiesIds.Count / Perms.LimitsBuilding.LimitTotal * 100f;
            }

            public float GetBuildingPercentage(BuildingEntities entities, uint prefabId)
            {
                if (Perms == null) return 0f;
                if (!Perms.LimitsBuilding.LimitEntitiesCache.TryGetValue(prefabId, out int limit) || limit <= 0) return 0f;
                if (!entities.EntitiesCount.TryGetValue(prefabId, out int count)) return 0f;
                return (float)count / limit * 100f;
            }
        }

        public class PlayerEntities
        {
            public int TotalCount;
            public readonly Dictionary<uint, int> Entities = new Dictionary<uint, int>();

            public void AddEntity(uint prefabID)
            {
                TotalCount++;
                Entities[prefabID] = GetEntityCount(prefabID) + 1;
            }

            public void RemoveEntity(uint prefabID)
            {
                if (TotalCount > 0) TotalCount--;
                if (!Entities.TryGetValue(prefabID, out int count)) return;
                if (count < 2) Entities.Remove(prefabID);
                else Entities[prefabID]--;
            }

            public int GetEntityCount(uint prefabID) => Entities.TryGetValue(prefabID, out int count) ? count : 0;
        }

        #endregion

        #region Lifecycle

        public void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                Debug.LogError("[LimitEntities] FAIL: Config not found at " + _configPath);
                _config = new PluginConfig
                {
                    Commands = new[] { "limits", "limit" },
                    Excluded = Array.Empty<string>(),
                    ExcludedSkinID = Array.Empty<ulong>(),
                    ZoneManagerZoneIDs = Array.Empty<string>(),
                    EntityGroups = new List<EntityGroup>(),
                    Permissions = Array.Empty<PermissionEntry>()
                };
                return;
            }

            string json = File.ReadAllText(_configPath);
            _config = JsonConvert.DeserializeObject<PluginConfig>(json) ?? new PluginConfig();
            NormalizeConfig(_config);
            Debug.Log("[LimitEntities] OK: Config loaded from HarmonyConfig/LimitEntities.json");
        }

        private void NormalizeConfig(PluginConfig config)
        {
            config.Commands ??= new[] { "limits", "limit" };
            config.Excluded ??= Array.Empty<string>();
            config.ExcludedSkinID ??= Array.Empty<ulong>();
            config.ZoneManagerZoneIDs ??= Array.Empty<string>();
            config.EntityGroups ??= new List<EntityGroup>();
            config.Permissions ??= Array.Empty<PermissionEntry>();

            for (int i = 0; i < config.Permissions.Length; i++)
                config.Permissions[i] = new PermissionEntry(config.Permissions[i]);

            if (config.BuildingDetectionRange < 0f)
                config.BuildingDetectionRange = 0f;
            _buildingDetectionRange = config.BuildingDetectionRange;
        }

        public void RegisterPermissions()
        {
            if (_config == null) return;

            PermissionsBridge.RegisterPermission(PermissionAdmin);
            PermissionsBridge.RegisterPermission(PermissionImmunity);

            var entries = new List<PermissionEntry>();
            var perms = new List<string> { PermissionImmunity };

            foreach (PermissionEntry entry in _config.Permissions)
            {
                if (string.IsNullOrWhiteSpace(entry.Permission))
                {
                    Log("Empty Permission in config — skipped", LogLevel.Error);
                    continue;
                }

                if (!entry.Permission.StartsWith("limitentities.", StringComparison.OrdinalIgnoreCase))
                    entry.Permission = "limitentities." + entry.Permission.ToLowerInvariant();

                PermissionsBridge.RegisterPermission(entry.Permission);
                if (!perms.Contains(entry.Permission))
                    perms.Add(entry.Permission);
                entries.Add(entry);
            }

            entries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _cache.PermissionData.Descending = entries.ToArray();
            perms.Sort();
            _cache.PermissionData.Registered = perms.ToArray();
        }

        public void InitializeCaches()
        {
            CacheGroupIds();
            CachePermissions();
            CachePrefabIds();
            _initialized = true;
        }

        public void Shutdown()
        {
            _initialized = false;
            _cache.PlayersData.Clear();
            _cache.Buildings.Clear();
            _cache.EntitiesTracked.Clear();
        }

        public void RefreshAllPlayerPerms()
        {
            foreach (var kv in _cache.PlayersData)
                kv.Value.UpdatePerms();
        }

        #endregion

        #region Stored data

        public void StoredDataLoad()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    string json = File.ReadAllText(_dataPath);
                    _storedData = JsonConvert.DeserializeObject<StoredData>(json) ?? new StoredData();
                    _storedData.BuildingsOwners ??= new Dictionary<uint, ulong>();
                }
                else
                {
                    _storedData = new StoredData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LimitEntities] FAIL: data load, creating new: " + ex.Message);
                _storedData = new StoredData();
            }

            if (_storedData.BuildingsOwners.Count > 0)
            {
                var serverBuildings = BuildingManager.server.buildingDictionary;
                var toRemove = new List<uint>();
                foreach (uint buildingId in _storedData.BuildingsOwners.Keys)
                {
                    if (!serverBuildings.Contains(buildingId))
                        toRemove.Add(buildingId);
                }
                foreach (uint id in toRemove)
                    _storedData.BuildingsOwners.Remove(id);

                Log($"{_storedData.BuildingsOwners.Count} buildings with owners loaded", LogLevel.Debug);
                return;
            }

            CollectBuildingOwnersFromWorld();
        }

        private void CollectBuildingOwnersFromWorld()
        {
            Log("Collecting building owners from world", LogLevel.Debug);
            var dict = BuildingManager.server.buildingDictionary;
            for (int i = 0; i < dict.Values.Count; i++)
            {
                Building building = dict.Values[i];
                if (!building.HasDecayEntities() || _storedData.BuildingsOwners.ContainsKey(building.ID))
                    continue;

                ulong netID = uint.MaxValue;
                ulong ownerId = 0;
                for (int index = 0; index < building.decayEntities.Count; index++)
                {
                    DecayEntity decayEntity = building.decayEntities[index];
                    if (decayEntity.IsValid() && decayEntity.OwnerID.IsSteamId() && decayEntity.net.ID.Value < netID)
                    {
                        netID = decayEntity.net.ID.Value;
                        ownerId = decayEntity.OwnerID;
                    }
                }

                if (ownerId != 0)
                    _storedData.BuildingsOwners[building.ID] = ownerId;
            }

            if (_storedData.BuildingsOwners.Count > 0)
                StoredDataSave();
        }

        public void StoredDataSave()
        {
            try
            {
                string dir = Path.GetDirectoryName(_dataPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(_storedData, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LimitEntities] FAIL: data save: " + ex.Message);
            }
        }

        public void StoredDataClear()
        {
            _storedData = new StoredData();
            StoredDataSave();
            Debug.Log("[LimitEntities] OK: Data cleared (new save).");
        }

        #endregion

        #region Caching

        public void CacheGroupIds()
        {
            uint groupID = StringPool.closest + 10000;
            foreach (EntityGroup entityGroup in _config.EntityGroups)
            {
                entityGroup.ListEntitiesCache.Clear();
                if (entityGroup.ListEntities == null) continue;

                foreach (string prefab in entityGroup.ListEntities)
                {
                    if (!StringPool.toNumber.TryGetValue(prefab, out uint prefabID))
                    {
                        Log($"Invalid entity '{prefab}' in group '{entityGroup.Name}'", LogLevel.Error);
                        continue;
                    }
                    entityGroup.ListEntitiesCache.Add(prefabID);
                }

                if (entityGroup.ListEntitiesCache.Count == 0)
                {
                    Log($"0 valid entities in '{entityGroup.Name}' group", LogLevel.Error);
                    continue;
                }

                do { groupID++; } while (StringPool.toString.ContainsKey(groupID));
                entityGroup.ID = groupID;

                foreach (uint prefabID in entityGroup.ListEntitiesCache)
                    _cache.Prefabs.Groups[prefabID] = groupID;

                _cache.Prefabs.ShortNames[groupID] = entityGroup.Name;
                if (!_langEn.ContainsKey(entityGroup.Name))
                    _langEn[entityGroup.Name] = entityGroup.Name;
            }
        }

        public void CachePermissions()
        {
            var groupStringPool = new Dictionary<string, uint>();
            foreach (EntityGroup entityGroup in _config.EntityGroups)
            {
                if (entityGroup.ID == 0) continue;
                groupStringPool[entityGroup.Name] = entityGroup.ID;
            }

            foreach (PermissionEntry entry in _config.Permissions)
            {
                CacheLimitEntities(entry.LimitsBuilding?.LimitsEntities, entry.LimitsBuilding?.LimitEntitiesCache, groupStringPool, entry.Permission, "LimitsBuilding");
                CacheLimitEntities(entry.LimitsGlobal?.LimitsEntities, entry.LimitsGlobal?.LimitEntitiesCache, groupStringPool, entry.Permission, "LimitsGlobal");
                if (entry.LimitsRadius?.LimitsEntities != null)
                {
                    CacheLimitEntities(entry.LimitsRadius.LimitsEntities, entry.LimitsRadius.LimitEntitiesCache, groupStringPool, entry.Permission, "LimitsRadius");
                    foreach (var kv in entry.LimitsRadius.LimitEntitiesCache)
                    {
                        if (!_cache.RadiusPrefabPositions.ContainsKey(kv.Key))
                            _cache.RadiusPrefabPositions[kv.Key] = new HashSet<Vector3>();
                    }
                }
            }
        }

        private void CacheLimitEntities(
            SortedDictionary<string, int> source,
            Dictionary<uint, int> cache,
            Dictionary<string, uint> groupStringPool,
            string perm,
            string section)
        {
            if (source == null || cache == null) return;
            cache.Clear();
            foreach (var entity in source)
            {
                if (!groupStringPool.TryGetValue(entity.Key, out uint prefabID)
                    && !StringPool.toNumber.TryGetValue(entity.Key, out prefabID))
                {
                    Log($"Invalid entity '{entity.Key}' in config ({perm}: {section})", LogLevel.Error);
                    continue;
                }
                cache[prefabID] = entity.Value;
            }
        }

        public void CachePrefabIds()
        {
            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                BaseEntity entity = null;
                var deployable = itemDefinition.GetComponent<ItemModDeployable>();
                if (deployable != null && deployable.entityPrefab != null && deployable.entityPrefab.isValid)
                {
                    var prefabObject = deployable.entityPrefab.Get();
                    if (prefabObject != null)
                        entity = prefabObject.GetComponent<BaseEntity>();
                    else
                    {
                        try
                        {
                            var found = GameManager.server.FindPrefab(deployable.entityPrefab.resourcePath);
                            if (found != null)
                                entity = found.GetComponent<BaseEntity>();
                        }
                        catch { }
                    }
                }

                if (entity == null) continue;
                if (!_config.TrackGrowable && entity is GrowableEntity) continue;
                if (_config.Excluded != null && Array.IndexOf(_config.Excluded, entity.PrefabName) >= 0) continue;

                _cache.Prefabs.Tracked.Add(entity.prefabID);
                if (!_cache.Prefabs.ShortNames.ContainsKey(entity.prefabID))
                    _cache.Prefabs.ShortNames[entity.prefabID] = entity.ShortPrefabName;
            }

            ProcessPlanner("building.planner");
            ProcessPlanner("boat.planner");
            Log($"Tracked {_cache.Prefabs.Tracked.Count} prefabs", LogLevel.Debug);
        }

        private void ProcessPlanner(string plannerShortName)
        {
            var itemDefinition = ItemManager.FindItemDefinition(plannerShortName);
            if (itemDefinition == null)
            {
                Log(plannerShortName + " not found", LogLevel.Error);
                return;
            }

            var plannerEntity = itemDefinition.GetComponent<ItemModEntity>();
            if (plannerEntity == null || plannerEntity.entityPrefab == null) return;

            GameObject plannerObject = plannerEntity.entityPrefab.Get();
            if (plannerObject == null && plannerEntity.entityPrefab.isValid)
            {
                try { plannerObject = GameManager.server.FindPrefab(plannerEntity.entityPrefab.resourcePath); }
                catch { }
            }

            if (plannerObject == null) return;
            var plannerComponent = plannerObject.GetComponent<Planner>();
            if (plannerComponent?.buildableList == null) return;

            for (int i = 0; i < plannerComponent.buildableList.Length; i++)
            {
                var entity = plannerComponent.buildableList[i];
                if (entity == null) continue;
                _cache.Prefabs.Tracked.Add(entity.prefabID);
                if (!_cache.Prefabs.ShortNames.ContainsKey(entity.prefabID))
                    _cache.Prefabs.ShortNames[entity.prefabID] = entity.ShortPrefabName;
                if (entity is BuildingBlock)
                    _cache.Prefabs.BuildingBlocks.Add(entity.prefabID);
            }
        }

        public void CacheEntities()
        {
            int i = 0;
            int count = BaseEntity.saveList.Count;
            foreach (BaseEntity entity in BaseEntity.saveList)
            {
                i++;
                if (!IsValidEntity(entity)) continue;
                AddBuildingEntity(entity);
                uint prefabID = GetPrefabID(entity.prefabID);
                GetPlayerData(entity.OwnerID).AddEntity(prefabID);
                if (_cache.RadiusPrefabPositions.TryGetValue(prefabID, out HashSet<Vector3> positions))
                    positions.Add(entity.transform.position);
                _cache.EntitiesTracked.Add(entity.net.ID.Value);
            }
            Log($"Cached entities for {_cache.PlayersData.Count} players ({i}/{count} scanned)", LogLevel.Debug);
        }

        #endregion

        #region Lookups

        public uint GetPrefabID(uint prefabID)
        {
            if (_cache.Prefabs.Groups.TryGetValue(prefabID, out uint group))
                return group;
            return prefabID;
        }

        public PlayerData GetPlayerData(ulong playerId)
        {
            if (!_cache.PlayersData.TryGetValue(playerId, out PlayerData playerData))
                _cache.PlayersData[playerId] = playerData = new PlayerData(playerId);
            return playerData;
        }

        public PermissionEntry GetPlayerPermissions(PlayerData player)
        {
            var descending = _cache.PermissionData.Descending;
            if (descending == null) return null;
            for (int i = 0; i < descending.Length; i++)
            {
                PermissionEntry entry = descending[i];
                if (PermissionsBridge.UserHasPermission(player.PlayerIdString, entry.Permission))
                    return entry;
            }
            return null;
        }

        public BuildingEntities GetBuildingData(uint buildingID)
        {
            if (!_cache.Buildings.TryGetValue(buildingID, out BuildingEntities buildingEntities))
                _cache.Buildings[buildingID] = buildingEntities = new BuildingEntities(buildingID);
            return buildingEntities;
        }

        public bool IsValidEntity(BaseEntity entity)
        {
            if (entity == null || !entity.IsValid() || !entity.OwnerID.IsSteamId()) return false;
            if (entity.skinID != 0UL && _config.ExcludedSkinID != null && Array.IndexOf(_config.ExcludedSkinID, entity.skinID) >= 0)
                return false;
            return _cache.Prefabs.Tracked.Contains(entity.prefabID);
        }

        #endregion

        #region HandleCanBuild

        /// <summary>
        /// Oxide CanBuild parity. Returns false object to block, null to allow.
        /// </summary>
        public object HandleCanBuild(BasePlayer player, Construction component, Construction.Target placement)
        {
            if (!_initialized || _config == null || component == null)
                return null;

            if (!player.IsValid()
                || !player.userID.IsSteamId()
                || !_cache.Prefabs.Tracked.Contains(component.prefabID))
                return null;

            // ZoneManager skipped while config UseZoneManager is false
            PlayerData playerData = GetPlayerData(player.userID);
            playerData.UpdatePerms();
            if (playerData.Perms == null || playerData.HasImmunity)
                return null;

            PlayerEntities entities = playerData.PlayerEntities;
            Vector3 position = placement.entity != null && placement.entity.IsValid() && placement.socket
                ? placement.GetWorldPosition()
                : placement.position;
            uint prefabID = GetPrefabID(component.prefabID);

            if (!playerData.CanBuild())
            {
                HandleNotification(player, Lang(LangKeys.Error.EntityIsNotAllowed, playerData.PlayerIdString, GetItemDisplayName(prefabID)), true);
                return FalseObj;
            }

            if (playerData.IsGlobalLimit())
            {
                HandleNotification(player, Lang(LangKeys.Error.LimitGlobal.Reached, playerData.PlayerIdString, entities.TotalCount, playerData.Perms.LimitsGlobal.LimitTotal), true);
                return FalseObj;
            }

            if (playerData.IsGlobalLimit(prefabID))
            {
                HandleNotification(player, Lang(LangKeys.Error.LimitGlobal.EntityReached, playerData.PlayerIdString, entities.GetEntityCount(prefabID), playerData.Perms.LimitsGlobal.GetEntityLimit(prefabID), GetItemDisplayName(prefabID)), true);
                return FalseObj;
            }

            if (playerData.IsRadiusLimit(prefabID, position, _cache.RadiusPrefabPositions))
            {
                HandleNotification(player, Lang(LangKeys.Error.LimitRadius.EntityReached, playerData.PlayerIdString, entities.GetEntityCount(prefabID), playerData.Perms.LimitsRadius.GetEntityLimit(prefabID), GetItemDisplayName(prefabID), Lang(LangKeys.Format.Meters, playerData.PlayerIdString, playerData.Perms.LimitsRadius.Radius)), true);
                return FalseObj;
            }

            uint buildingID;
            if (_config.TrackGrowable && placement.entity is PlanterBox planterBox)
                buildingID = planterBox.buildingID;
            else
                buildingID = GetBuildingID(placement);

            if (_cache.Buildings.TryGetValue(buildingID, out BuildingEntities building))
            {
                if (playerData.IsBuildingLimit(building))
                {
                    HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.Reached, playerData.PlayerIdString, building.EntitiesIds.Count, playerData.Perms.LimitsBuilding.LimitTotal), true);
                    return FalseObj;
                }

                if (playerData.IsBuildingLimit(building, prefabID))
                {
                    HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.EntityReached, playerData.PlayerIdString, building.GetEntityCount(prefabID), playerData.Perms.LimitsBuilding.GetEntityLimit(prefabID), GetItemDisplayName(prefabID)), true);
                    return FalseObj;
                }

                if (playerData.Perms.MergingCheck
                    && _cache.Prefabs.BuildingBlocks.Contains(component.prefabID)
                    && IsMergeBlocked(component, placement, player, playerData, building))
                    return FalseObj;
            }

            return null;
        }

        public bool IsMergeBlocked(Construction component, Construction.Target placement, BasePlayer player, PlayerData playerData, BuildingEntities buildingEntities)
        {
            var gameObject = GameManager.server.CreatePrefab(component.fullName, Vector3.zero, Quaternion.identity, false);
            if (gameObject == null) return false;

            component.UpdatePlacement(gameObject.transform, component, ref placement);
            BaseEntity baseEntity = gameObject.ToBaseEntity();
            OBB oBb = baseEntity.WorldSpaceBounds();

            if (!baseEntity.IsValid())
                GameManager.Destroy(gameObject);
            else
                baseEntity.KillAsMapEntity();

            bool mergeBlocked = false;
            var processedBuildings = Pool.Get<List<uint>>();
            processedBuildings.Add(buildingEntities.BuildingID);
            var adjoiningBlocks = Pool.Get<List<BuildingBlock>>();
            Vis.Entities(oBb.position, oBb.extents.magnitude + 1f, adjoiningBlocks);

            if (adjoiningBlocks.Count > 0)
            {
                Dictionary<uint, int> limitEntitiesCache = playerData.Perms.LimitsBuilding.LimitEntitiesCache;
                int allowedBuildingTotal = playerData.Perms.LimitsBuilding.LimitTotal - buildingEntities.EntitiesIds.Count;
                var allowedBuildingEntities = new Dictionary<uint, int>();

                foreach (BuildingBlock adjoiningBlock in adjoiningBlocks)
                {
                    if (processedBuildings.Contains(adjoiningBlock.buildingID)
                        || !_cache.Buildings.TryGetValue(adjoiningBlock.buildingID, out BuildingEntities adjoiningBuilding))
                        continue;

                    foreach (var adjoiningEntity in adjoiningBuilding.EntitiesCount)
                    {
                        allowedBuildingTotal -= adjoiningEntity.Value;
                        if (allowedBuildingTotal < 0)
                        {
                            HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.MergeBlocked, playerData.PlayerIdString, allowedBuildingTotal * -1), true);
                            mergeBlocked = true;
                            break;
                        }

                        if (!limitEntitiesCache.TryGetValue(adjoiningEntity.Key, out int limitEntity))
                            continue;

                        if (!allowedBuildingEntities.TryGetValue(adjoiningEntity.Key, out int allowedCount))
                        {
                            buildingEntities.EntitiesCount.TryGetValue(adjoiningEntity.Key, out int existingCount);
                            allowedBuildingEntities[adjoiningEntity.Key] = allowedCount = limitEntity - existingCount;
                        }

                        allowedBuildingEntities[adjoiningEntity.Key] = allowedCount -= adjoiningEntity.Value;
                        if (allowedCount < 0)
                        {
                            HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.EntityMergeBlocked, playerData.PlayerIdString, allowedCount * -1, GetItemDisplayName(adjoiningEntity.Key)), true);
                            mergeBlocked = true;
                            break;
                        }
                    }

                    if (mergeBlocked) break;
                    processedBuildings.Add(adjoiningBlock.buildingID);
                }
            }

            Pool.FreeUnmanaged(ref adjoiningBlocks);
            Pool.FreeUnmanaged(ref processedBuildings);
            return mergeBlocked;
        }

        #endregion

        #region Entity lifecycle

        public void OnEntitySpawned(BaseEntity entity)
        {
            if (!_initialized || !IsValidEntity(entity)) return;
            if (_cache.EntitiesTracked.Contains(entity.net.ID.Value)) return;

            Vector3 position = entity.transform.position;
            ulong ownerID = entity.OwnerID;
            uint prefabID = GetPrefabID(entity.prefabID);
            uint buildingID = AddBuildingEntity(entity);

            if (buildingID > 0 && !_storedData.BuildingsOwners.ContainsKey(buildingID))
            {
                _storedData.BuildingsOwners[buildingID] = ownerID;
                Log($"Added building owner {buildingID} -> {ownerID}", LogLevel.Debug);
            }

            PlayerData playerData = GetPlayerData(ownerID);
            playerData.AddEntity(prefabID);
            _cache.EntitiesTracked.Add(entity.net.ID.Value);

            if (_cache.RadiusPrefabPositions.TryGetValue(prefabID, out HashSet<Vector3> positions))
                positions.Add(position);

            if ((_config.ChatNotificationsEnabled || _config.GameTipNotificationsEnabled)
                && _config.WarnPercent > 0
                && playerData.Perms != null
                && !playerData.HasImmunity)
            {
                HandleEntityNotification(BasePlayer.FindByID(ownerID), playerData, buildingID, prefabID);
            }
        }

        public void OnEntityKill(BaseEntity entity)
        {
            if (!_initialized || entity == null || !entity.IsValid()) return;
            if (!_cache.EntitiesTracked.Contains(entity.net.ID.Value)) return;
            if (!entity.OwnerID.IsSteamId()) return;
            if (!_cache.Prefabs.Tracked.Contains(entity.prefabID)) return;

            uint prefabID = GetPrefabID(entity.prefabID);
            ulong ownerID = entity.OwnerID;
            uint buildingID = RemoveBuildingEntity(entity);
            Vector3 position = entity.transform.position;

            if (entity is BuildingBlock block)
            {
                if (BuildingManager.server.buildingDictionary.TryGetValue(buildingID, out Building building)
                    && building.decayEntities != null
                    && building.decayEntities.Count == 1
                    && building.decayEntities.Contains(block))
                {
                    _storedData.BuildingsOwners.Remove(buildingID);
                }
            }

            GetPlayerData(ownerID).RemoveEntity(prefabID);
            _cache.EntitiesTracked.Remove(entity.net.ID.Value);

            if (_cache.RadiusPrefabPositions.TryGetValue(prefabID, out HashSet<Vector3> positions))
                positions.Remove(position);
        }

        public void OnEntityReskinned(BaseEntity entity)
        {
            if (!_initialized || entity == null || !entity.IsValid()) return;
            if (_cache.EntitiesTracked.Contains(entity.net.ID.Value)) return;
            OnEntitySpawned(entity);
        }

        public void OnPlayerConnected(BasePlayer player)
        {
            if (player != null && player.userID.IsSteamId())
                GetPlayerData(player.userID);
        }

        #endregion

        #region Building merge / split

        public void OnBuildingMerge(uint fromId, uint toId)
        {
            LimitEntitiesMod.NextTick(() => HandleBuildingChange(fromId, toId, false));
        }

        public void OnBuildingSplit(uint oldId)
        {
            LimitEntitiesMod.NextTick(() => HandleBuildingSplit(oldId));
        }

        public void HandleBuildingChange(uint oldId, uint newId, bool split)
        {
            if (!_storedData.BuildingsOwners.TryGetValue(oldId, out ulong ownerId) || !ownerId.IsSteamId())
                return;
            if (!BuildingManager.server.buildingDictionary.Contains(newId))
                return;

            _storedData.BuildingsOwners[newId] = ownerId;
            TransferMovedEntities(oldId, ownerId);
            Log($"Building {(split ? "split" : "merge")} {oldId} -> {newId} owner {ownerId}", LogLevel.Debug);
        }

        public void HandleBuildingSplit(uint oldId)
        {
            if (!_storedData.BuildingsOwners.TryGetValue(oldId, out ulong ownerId) || !ownerId.IsSteamId())
                return;

            if (!_cache.Buildings.TryGetValue(oldId, out BuildingEntities entitiesOld))
                return;

            var destinationIds = new HashSet<uint>();
            var moved = Pool.Get<List<BaseEntity>>();
            foreach (ulong id in entitiesOld.EntitiesIds)
            {
                if (BaseNetworkable.serverEntities.Find(new NetworkableId(id)) is not BaseEntity entity || !entity.IsValid())
                    continue;
                uint currentBuildingId = GetBuildingID(entity);
                if (currentBuildingId == oldId || currentBuildingId == 0)
                    continue;
                moved.Add(entity);
                destinationIds.Add(currentBuildingId);
            }

            foreach (uint newId in destinationIds)
                _storedData.BuildingsOwners[newId] = ownerId;

            foreach (BaseEntity entity in moved)
            {
                if (!entity.IsValid()) continue;
                entitiesOld.RemoveEntity(entity);
                AddBuildingEntity(entity);
            }

            Pool.FreeUnmanaged(ref moved);
            Log($"Building split {oldId} -> {destinationIds.Count} buildings, owner {ownerId}", LogLevel.Debug);
        }

        private void TransferMovedEntities(uint oldId, ulong ownerId)
        {
            if (!_cache.Buildings.TryGetValue(oldId, out BuildingEntities entitiesOld))
                return;

            var moved = Pool.Get<List<BaseEntity>>();
            foreach (ulong id in entitiesOld.EntitiesIds)
            {
                if (BaseNetworkable.serverEntities.Find(new NetworkableId(id)) is not BaseEntity entity || !entity.IsValid())
                    continue;
                uint currentBuildingId = GetBuildingID(entity);
                if (currentBuildingId == oldId || currentBuildingId == 0)
                    continue;
                moved.Add(entity);
            }

            foreach (BaseEntity entity in moved)
            {
                if (!entity.IsValid()) continue;
                entitiesOld.RemoveEntity(entity);
                AddBuildingEntity(entity);
            }

            Pool.FreeUnmanaged(ref moved);
        }

        public uint AddBuildingEntity(BaseEntity entity)
        {
            uint buildingId = GetBuildingID(entity);
            if (buildingId == 0) return 0;
            GetBuildingData(buildingId).AddEntity(entity);
            return buildingId;
        }

        public uint RemoveBuildingEntity(BaseEntity entity)
        {
            uint buildingId = GetBuildingID(entity);
            if (buildingId == 0 || !_cache.Buildings.TryGetValue(buildingId, out BuildingEntities buildingEntities))
                return buildingId;
            buildingEntities.RemoveEntity(entity);
            return buildingId;
        }

        public uint GetBuildingID(Construction.Target target)
        {
            if (target.entity != null && target.entity.IsValid())
            {
                if (target.entity is DecayEntity decayEntity)
                    return decayEntity.buildingID;
                return GetBuildingID(target.socket ? target.GetWorldPosition() : target.position, _buildingDetectionRange);
            }
            return GetBuildingID(target.position, _buildingDetectionRange);
        }

        public uint GetBuildingID(BaseEntity entity)
        {
            if (entity is DecayEntity decayEntity)
                return decayEntity.buildingID;
            if (entity.GetParentEntity() is DecayEntity parentEntity)
                return parentEntity.buildingID;
            return GetBuildingID(entity.transform.position, _buildingDetectionRange);
        }

        public uint GetBuildingID(Vector3 position, float radius = 0)
        {
            if (radius <= 0f) return 0U;

            var entities = Pool.Get<List<Collider>>();
            GamePhysics.OverlapSphere(position, radius, entities, Rust.Layers.Construction);
            if (entities.Count == 0)
            {
                Pool.FreeUnmanaged(ref entities);
                return 0U;
            }

            BuildingBlock nearestBlock = null;
            float nearestDistanceSqr = float.MaxValue;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].ToBaseEntity() is not BuildingBlock currentBlock)
                    continue;
                float d = Vector3.SqrMagnitude(position - currentBlock.transform.position);
                if (nearestBlock == null || d < nearestDistanceSqr)
                {
                    nearestBlock = currentBlock;
                    nearestDistanceSqr = d;
                }
            }

            Pool.FreeUnmanaged(ref entities);
            return nearestBlock == null ? 0U : nearestBlock.buildingID;
        }

        #endregion

        #region Notifications / commands / lang

        public void HandleEntityNotification(BasePlayer player, PlayerData playerData, uint buildingID, uint prefabID)
        {
            if (!player.IsValid() || player.IsDead() || !player.IsConnected || _config.WarnPercent <= 0)
                return;

            if (_cache.Buildings.TryGetValue(buildingID, out BuildingEntities building)
                && playerData.GetBuildingPercentage(building, prefabID) >= _config.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitBuildingEntity, playerData.PlayerIdString, building.GetEntityCount(prefabID), playerData.Perms.LimitsBuilding.GetEntityLimit(prefabID), GetItemDisplayName(prefabID)));
                return;
            }

            if (building != null && playerData.GetBuildingPercentage(building) >= _config.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitBuilding, playerData.PlayerIdString, building.EntitiesIds.Count, playerData.Perms.LimitsBuilding.LimitTotal));
                return;
            }

            if (playerData.GetGlobalPercentage(prefabID) >= _config.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitGlobalEntity, playerData.PlayerIdString, playerData.PlayerEntities.GetEntityCount(prefabID), playerData.Perms.LimitsGlobal.GetEntityLimit(prefabID), GetItemDisplayName(prefabID)));
                return;
            }

            if (playerData.GetGlobalPercentage() >= _config.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitGlobal, playerData.PlayerIdString, playerData.PlayerEntities.TotalCount, playerData.Perms.LimitsGlobal.LimitTotal));
            }
        }

        public void HandleNotification(BasePlayer player, string message, bool isWarning = false)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;
            Log($"{player.displayName} {StripRustTags(message)}", isWarning ? LogLevel.Warning : LogLevel.Info);

            if (_config.ChatNotificationsEnabled)
            {
                string help = Lang(LangKeys.Info.Help, player.UserIDString, _config.Commands != null && _config.Commands.Length > 0 ? _config.Commands[0] : "limits");
                PlayerSendMessage(player, message + "\n\n" + help);
            }

            if (_config.GameTipNotificationsEnabled)
                PlayerSendGameTip(player, message, isWarning);
        }

        public void PlayerSendMessage(BasePlayer player, string message)
        {
            player.SendConsoleCommand("chat.add", 2, _config.SteamIDIcon, $"{Lang(LangKeys.Format.Prefix, player.UserIDString)}{message}");
        }

        public void PlayerSendGameTip(BasePlayer player, string message, bool isWarning = false)
        {
            player.SendConsoleCommand("showtoast", isWarning ? (int)GameTip.Styles.Error : (int)GameTip.Styles.Blue_Long, message, false);
        }

        public void CmdLimitEntities(BasePlayer player, string[] args)
        {
            if (player == null) return;
            BasePlayer target = player;
            if (args != null && args.Length > 0)
            {
                if (!IsPlayerAdmin(player))
                {
                    PlayerSendMessage(player, Lang(LangKeys.Error.NoPermission, player.UserIDString));
                    return;
                }

                string query = args[0];
                target = BasePlayer.FindAwakeOrSleeping(query);
                if (target == null)
                {
                    PlayerSendMessage(player, Lang(LangKeys.Error.PlayerNotFound, player.UserIDString, query));
                    return;
                }
            }

            PlayerSendMessage(player, GetPlayerLimitString(player, target));
        }

        public void CmdLimitEntitiesList(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg?.Player();
            if (player != null && player.IsValid() && !IsPlayerAdmin(player))
                return;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("All tracked entities list start");
            foreach (uint trackedPrefabID in _cache.Prefabs.Tracked)
            {
                sb.AppendLine();
                sb.Append("PrefabID: ").Append(trackedPrefabID).AppendLine();
                sb.Append("PrefabShortName: ");
                if (_cache.Prefabs.ShortNames.TryGetValue(trackedPrefabID, out string shortName))
                    sb.Append(shortName);
                sb.AppendLine();
                sb.Append("Prefab: ").Append(StringPool.Get(trackedPrefabID)).AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("All tracked entities list finish");
            Debug.Log("[LimitEntities] " + sb);
            Debug.Log($"[LimitEntities] OK: Listed {_cache.Prefabs.Tracked.Count} tracked entities");
            if (arg != null)
                arg.ReplyWith($"OK: Listed {_cache.Prefabs.Tracked.Count} tracked entities (see server log)");
        }

        public bool IsPlayerAdmin(BasePlayer player) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, PermissionAdmin));

        public string GetPlayerLimitString(BasePlayer player, BasePlayer target)
        {
            if (!_cache.PlayersData.TryGetValue(target.userID, out PlayerData playerData) || playerData.Perms == null || playerData.HasImmunity)
                return Lang(LangKeys.Info.Unlimited, player.UserIDString);

            PlayerEntities entities = playerData.PlayerEntities;
            PermissionEntry perms = playerData.Perms;
            var sb = new StringBuilder();

            sb.AppendLine(Lang(LangKeys.Info.TotalAmount, player.UserIDString, $"{entities.TotalCount} / {perms.LimitsGlobal.LimitTotal}"));
            foreach (var limitEntry in perms.LimitsGlobal.LimitEntitiesCache)
            {
                uint prefabID = GetPrefabID(limitEntry.Key);
                sb.AppendLine();
                sb.Append(GetItemDisplayName(prefabID));
                sb.Append("  ");
                sb.Append(entities.GetEntityCount(prefabID));
                sb.Append(" / ");
                sb.Append(limitEntry.Value);
            }
            sb.AppendLine();
            string globalLimits = sb.ToString();

            sb.Clear();
            sb.AppendLine(Lang(LangKeys.Info.TotalAmount, player.UserIDString, perms.LimitsBuilding.LimitTotal));
            foreach (var limitEntry in perms.LimitsBuilding.LimitEntitiesCache)
            {
                sb.AppendLine();
                sb.Append(GetItemDisplayName(GetPrefabID(limitEntry.Key)));
                sb.Append("  ");
                sb.Append(limitEntry.Value);
            }
            string buildingLimits = sb.ToString();

            string radiusLimits = string.Empty;
            if (perms.LimitsRadius.Radius > 0f && perms.LimitsRadius.LimitEntitiesCache.Count > 0)
            {
                sb.Clear();
                sb.Append(Lang(LangKeys.Format.Meters, player.UserIDString, perms.LimitsRadius.Radius));
                sb.AppendLine(":");
                foreach (var limitEntry in perms.LimitsRadius.LimitEntitiesCache)
                {
                    sb.AppendLine();
                    sb.Append(GetItemDisplayName(GetPrefabID(limitEntry.Key)));
                    sb.Append("  ");
                    sb.Append(limitEntry.Value);
                }
                radiusLimits = sb.ToString();
            }

            return Lang(LangKeys.Info.Limits, player.UserIDString, globalLimits, buildingLimits, radiusLimits);
        }

        public string Lang(string key, string userIDString = null, params object[] args)
        {
            if (!_langEn.TryGetValue(key, out string template))
                template = key;
            try
            {
                return args != null && args.Length > 0 ? string.Format(template, args) : template;
            }
            catch
            {
                return template;
            }
        }

        public string GetShortName(uint prefabId)
        {
            if (!_cache.Prefabs.ShortNames.TryGetValue(prefabId, out string shortName))
            {
                if (!StringPool.toString.TryGetValue(prefabId, out shortName))
                    return string.Empty;
                _cache.Prefabs.ShortNames[prefabId] = shortName = Path.GetFileNameWithoutExtension(shortName);
            }
            return shortName;
        }

        public string GetItemDisplayName(uint prefabID)
        {
            prefabID = GetPrefabID(prefabID);
            if (_cache.Prefabs.Groups.ContainsValue(prefabID))
            {
                string groupName = GetShortName(prefabID);
                return Lang(groupName);
            }
            return GetShortName(prefabID);
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (_config == null || _config.LoggingLevel < level) return;
            switch (level)
            {
                case LogLevel.Error:
                    Debug.LogError("[LimitEntities] " + message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning("[LimitEntities] " + message);
                    break;
                default:
                    Debug.Log("[LimitEntities] " + message);
                    break;
            }
        }

        private static string StripRustTags(string text) =>
            string.IsNullOrWhiteSpace(text) ? text : Tags.Replace(text, string.Empty);

        private static class LangKeys
        {
            public static class Error
            {
                private const string Base = nameof(Error) + ".";
                public const string EntityIsNotAllowed = Base + nameof(EntityIsNotAllowed);
                public const string NoPermission = Base + nameof(NoPermission);
                public const string PlayerNotFound = Base + nameof(PlayerNotFound);
                public static class LimitBuilding
                {
                    private const string SubBase = Base + nameof(LimitBuilding) + ".";
                    public const string EntityMergeBlocked = SubBase + nameof(EntityMergeBlocked);
                    public const string EntityReached = SubBase + nameof(EntityReached);
                    public const string MergeBlocked = SubBase + nameof(MergeBlocked);
                    public const string Reached = SubBase + nameof(Reached);
                }
                public static class LimitGlobal
                {
                    private const string SubBase = Base + nameof(LimitGlobal) + ".";
                    public const string EntityReached = SubBase + nameof(EntityReached);
                    public const string Reached = SubBase + nameof(Reached);
                }
                public static class LimitRadius
                {
                    private const string SubBase = Base + nameof(LimitRadius) + ".";
                    public const string EntityReached = SubBase + nameof(EntityReached);
                }
            }
            public static class Format
            {
                private const string Base = nameof(Format) + ".";
                public const string Meters = Base + nameof(Meters);
                public const string Prefix = Base + nameof(Prefix);
            }
            public static class Info
            {
                private const string Base = nameof(Info) + ".";
                public const string Help = Base + nameof(Help);
                public const string LimitBuilding = Base + nameof(LimitBuilding);
                public const string LimitBuildingEntity = Base + nameof(LimitBuildingEntity);
                public const string LimitGlobal = Base + nameof(LimitGlobal);
                public const string LimitGlobalEntity = Base + nameof(LimitGlobalEntity);
                public const string Limits = Base + nameof(Limits);
                public const string TotalAmount = Base + nameof(TotalAmount);
                public const string Unlimited = Base + nameof(Unlimited);
            }
        }

        private static Dictionary<string, string> CreateDefaultLang() => new Dictionary<string, string>
        {
            [LangKeys.Error.EntityIsNotAllowed] = "You are not allowed to build <color=#FFA500>{0}</color>",
            [LangKeys.Error.PlayerNotFound] = "Player <color=#FFA500>{0}</color> not found!",
            [LangKeys.Error.LimitBuilding.EntityMergeBlocked] = "You can't merge these buildings because the limit of <color=#FFA500>{1}</color> will be exceeded by <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.EntityReached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> in this building",
            [LangKeys.Error.LimitBuilding.MergeBlocked] = "You can't merge these buildings because the limit of entities will be exceeded by <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.Reached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities in this building",
            [LangKeys.Error.LimitGlobal.EntityReached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color>",
            [LangKeys.Error.LimitGlobal.Reached] = "You have reached the global limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities",
            [LangKeys.Error.LimitRadius.EntityReached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> {3}",
            [LangKeys.Error.NoPermission] = "You do not have permission to use this command!",
            [LangKeys.Format.Meters] = "within a radius of <color=#FFA500>{0}</color> meters",
            [LangKeys.Format.Prefix] = "<color=#00FF00>[Limit Entities]</color>: ",
            [LangKeys.Info.Help] = "Get current limits: <color=#FFFF00>/{0}</color>",
            [LangKeys.Info.LimitBuilding] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities in this building",
            [LangKeys.Info.LimitBuildingEntity] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> in this building",
            [LangKeys.Info.LimitGlobal] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities",
            [LangKeys.Info.LimitGlobalEntity] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color>",
            [LangKeys.Info.Limits] = "\nYour global limits are:\n<color=#FFA500>{0}</color>\nYour limits per building are:\n<color=#FFA500>{1}</color>\n{2}",
            [LangKeys.Info.TotalAmount] = "Total amount: <color=#FFA500>{0}</color>",
            [LangKeys.Info.Unlimited] = "Your ability to build is unlimited",
            ["Foundations"] = "Foundations",
            ["Furnace"] = "Furnace",
            ["PlanterBoxes"] = "PlanterBoxes",
            ["Quarries"] = "Quarries",
            ["Roof"] = "Roof",
            ["TC"] = "TC",
        };

        #endregion
    }
}
