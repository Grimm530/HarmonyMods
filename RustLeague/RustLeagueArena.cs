using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RustLeagueHarmony
{
    public class ArenaPart
    {
        public uint Id;
        public string Path;
        public Vector3 Pos;
        public Vector3 Rot;
        public Vector3 Scale = Vector3.one;
    }

    public class ArenaDefinition
    {
        public string Source;
        public int Count;
        public Vector3 Size;
        public Vector3 Center;
        public Vector3 RedGoal;
        public Vector3 BlueGoal;
        public List<ArenaPart> Parts = new List<ArenaPart>();
    }

    public static class ArenaCatalog
    {
        public static ArenaDefinition Definition { get; private set; }
        public static bool Ready => Definition != null && Definition.Parts != null && Definition.Parts.Count > 0;

        public static void Load(RustLeaguePlugin plugin)
        {
            Definition = null;
            string cache = Path.Combine(RustLeagueHost.Instance.DataDirectory, "Arena.json");
            string configured = plugin.configData.settings.ArenaPrefabPath;
            string mapPath = ResolveArenaFile(plugin, configured);

            if (File.Exists(cache) && mapPath != null)
            {
                try
                {
                    var cached = JsonConvert.DeserializeObject<ArenaDefinition>(File.ReadAllText(cache));
                    if (cached != null && cached.Parts != null && cached.Parts.Count > 0
                        && string.Equals(cached.Source, mapPath, StringComparison.OrdinalIgnoreCase)
                        && !ContainsEditorGizmos(cached))
                    {
                        Definition = cached;
                        ApplySizeToGrid(plugin);
                        Debug.Log($"[RustLeague] Loaded cached arena ({Definition.Parts.Count} parts) from HarmonyData/RustLeague/Arena.json");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RustLeague] Arena.json cache unreadable: " + ex.Message);
                }
            }

            if (mapPath == null)
            {
                Debug.LogWarning("[RustLeague] Arena prefab not found. Looked for maps/prefabs/RustLeagueArena.map and .prefab");
                return;
            }

            Definition = ExtractFromWorldMap(mapPath);
            if (!Ready)
            {
                Debug.LogWarning("[RustLeague] Failed to extract prefabs from " + mapPath);
                return;
            }

            try
            {
                File.WriteAllText(cache, JsonConvert.SerializeObject(Definition, Formatting.Indented));
                Debug.Log($"[RustLeague] Extracted {Definition.Parts.Count} arena parts -> HarmonyData/RustLeague/Arena.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustLeague] Could not write Arena.json: " + ex.Message);
            }

            ApplySizeToGrid(plugin);
        }

        private static void ApplySizeToGrid(RustLeaguePlugin plugin)
        {
            if (!Ready || plugin?.configData?.Grid == null) return;
            float radius = Mathf.Max(Definition.Size.x, Definition.Size.z) * 0.5f + 10f;
            if (radius > plugin.configData.Grid.ArenaRadius)
                plugin.configData.Grid.ArenaRadius = radius;
        }

        private static string ResolveArenaFile(RustLeaguePlugin plugin, string configured)
        {
            string root = RustLeagueHost.Instance.ServerRoot;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(configured))
            {
                candidates.Add(Path.IsPathRooted(configured) ? configured : Path.Combine(root, configured));
                string noExt = Path.Combine(root, Path.ChangeExtension(configured, null) ?? configured);
                candidates.Add(noExt + ".map");
                candidates.Add(noExt + ".prefab");
            }
            candidates.Add(Path.Combine(root, "maps", "prefabs", "RustLeagueArena.map"));
            candidates.Add(Path.Combine(root, "maps", "prefabs", "RustLeagueArena.prefab"));

            for (int i = 0; i < candidates.Count; i++)
            {
                if (File.Exists(candidates[i]))
                    return Path.GetFullPath(candidates[i]);
            }
            return null;
        }

        private static ArenaDefinition ExtractFromWorldMap(string path)
        {
            var world = new WorldSerialization();
            world.Load(path);
            var prefabs = world.world?.prefabs;
            if (prefabs == null || prefabs.Count == 0)
                return null;

            var def = new ArenaDefinition
            {
                Source = path,
                Count = prefabs.Count,
                Parts = new List<ArenaPart>(prefabs.Count)
            };

            Vector3 origin = new Vector3(prefabs[0].position.x, prefabs[0].position.y, prefabs[0].position.z);
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            int skipped = 0;

            for (int i = 0; i < prefabs.Count; i++)
            {
                var p = prefabs[i];
                string prefabPath = null;
                try { prefabPath = StringPool.Get(p.id); }
                catch { }

                Vector3 worldPos = p.position;
                Vector3 local = worldPos - origin;
                Quaternion stored = p.rotation;
                Vector3 euler = stored.eulerAngles;
                Vector3 scale = new Vector3(
                    p.scale.x == 0f ? 1f : p.scale.x,
                    p.scale.y == 0f ? 1f : p.scale.y,
                    p.scale.z == 0f ? 1f : p.scale.z);

                if (string.IsNullOrEmpty(prefabPath)
                    || prefabPath.IndexOf("ioslothandle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    skipped++;
                    continue;
                }

                if (local.x < minX) minX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.z < minZ) minZ = local.z;
                if (local.x > maxX) maxX = local.x;
                if (local.y > maxY) maxY = local.y;
                if (local.z > maxZ) maxZ = local.z;

                def.Parts.Add(new ArenaPart
                {
                    Id = p.id,
                    Path = prefabPath,
                    Pos = local,
                    Rot = euler,
                    Scale = scale
                });
            }

            def.Size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
            def.Center = new Vector3((minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f);
            def.RedGoal = new Vector3(def.Center.x, def.Center.y + 1f, maxZ);
            def.BlueGoal = new Vector3(def.Center.x, def.Center.y + 1f, minZ);
            def.Count = def.Parts.Count;

            if (skipped > 0)
                Debug.LogWarning($"[RustLeague] Skipped {skipped} arena prefabs with unknown IDs.");

            LogSample(def);
            return def;
        }

        private static bool ContainsEditorGizmos(ArenaDefinition def)
        {
            if (def?.Parts == null) return false;
            for (int i = 0; i < def.Parts.Count; i++)
            {
                if (def.Parts[i].Path != null
                    && def.Parts[i].Path.IndexOf("ioslothandle", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static void LogSample(ArenaDefinition def)
        {
            int n = Mathf.Min(12, def.Parts.Count);
            Debug.Log($"[RustLeague] Arena {def.Count} parts, size {def.Size.x:F0}x{def.Size.y:F0}x{def.Size.z:F0}");
            for (int i = 0; i < n; i++)
                Debug.Log($"[RustLeague]   {def.Parts[i].Path}");
        }
    }

    public partial class RustLeaguePlugin
    {
        internal readonly List<BaseEntity> arenaEntities = new List<BaseEntity>(16);
        internal readonly List<GameObject> arenaObjects = new List<GameObject>(16);
        private Coroutine _arenaSpawn;

        private const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
        private const string WallPrefab = "assets/prefabs/building core/wall/wall.prefab";
        private const float NativeBlock = 3f;
        internal const float PitchWallHeight = 10f;

        internal bool ArenaReady => ArenaCatalog.Ready;

        internal void ApplyArenaLayoutFromCatalog(Vector3 worldOrigin, float yaw)
        {
            worldOrigin = LiftToSky(worldOrigin);
            arenaOrigin = worldOrigin;
            arenaYaw = yaw;
            configData.eventSettings.ArenaOrigin = worldOrigin;
            configData.eventSettings.RedZoneRotation = yaw;
            configData.eventSettings.BlueZoneRotation = yaw + 180f;

            if (!ArenaCatalog.Ready)
            {
                ApplyArenaLayout(worldOrigin, yaw);
                return;
            }

            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            var def = ArenaCatalog.Definition;
            configData.eventSettings.eventCenter = worldOrigin + rot * def.Center;
            configData.eventSettings.RedZone = worldOrigin + rot * def.RedGoal;
            configData.eventSettings.BlueZone = worldOrigin + rot * def.BlueGoal;
            float sx = Mathf.Max(20f, def.Size.x + 10f);
            float sy = Mathf.Max(20f, PitchWallHeight + 10f);
            float sz = Mathf.Max(20f, def.Size.z + 10f);
            configData.eventSettings.ArenaBoundsSize = new Vector3(sx, sy, sz);
        }

        internal void StartArenaSpawn(Vector3 worldOrigin, float yaw)
        {
            StopArenaSpawn();
            DespawnArena();
            if (!ArenaCatalog.Ready)
            {
                Debug.LogWarning(Lang("arenaMissing"));
                return;
            }
            if (ServerMgr.Instance == null) return;
            _arenaSpawn = ServerMgr.Instance.StartCoroutine(SpawnArenaRoutine(worldOrigin, yaw));
        }

        internal void StopArenaSpawn()
        {
            if (_arenaSpawn != null && ServerMgr.Instance != null)
                ServerMgr.Instance.StopCoroutine(_arenaSpawn);
            _arenaSpawn = null;
        }

        internal void DespawnArena()
        {
            StopArenaSpawn();
            for (int i = 0; i < arenaEntities.Count; i++)
            {
                var ent = arenaEntities[i];
                if (ent != null && !ent.IsDestroyed)
                    ent.Kill();
            }
            arenaEntities.Clear();
            for (int i = 0; i < arenaObjects.Count; i++)
            {
                var go = arenaObjects[i];
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            }
            arenaObjects.Clear();
        }

        private IEnumerator SpawnArenaRoutine(Vector3 worldOrigin, float yaw)
        {
            var def = ArenaCatalog.Definition;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

            Broadcast("arenaSpawning", GridRef(worldOrigin));

            Vector3 floorCenter = worldOrigin + yawRot * def.Center;
            float width = Mathf.Max(24f, def.Size.x);
            float length = Mathf.Max(24f, def.Size.z);
            float hx = width * 0.5f;
            float hz = length * 0.5f;

            SpawnMetalBlock(FloorPrefab, floorCenter, yawRot, new Vector3(width / NativeBlock, 1f, length / NativeBlock));
            SpawnPitchWall(floorCenter + yawRot * new Vector3(0f, 0.05f, hz), yawRot, width);
            SpawnPitchWall(floorCenter + yawRot * new Vector3(0f, 0.05f, -hz), yawRot * Quaternion.Euler(0f, 180f, 0f), width);
            SpawnPitchWall(floorCenter + yawRot * new Vector3(hx, 0.05f, 0f), yawRot * Quaternion.Euler(0f, 90f, 0f), length);
            SpawnPitchWall(floorCenter + yawRot * new Vector3(-hx, 0.05f, 0f), yawRot * Quaternion.Euler(0f, -90f, 0f), length);

            SpawnArenaBeacon();
            _arenaSpawn = null;
            Debug.Log($"[RustLeague] Arena pitch spawned: {arenaEntities.Count} parts, open floor {width:F0}x{length:F0}, walls {PitchWallHeight:F0}m. TP {GetArenaTeleportPos()}");
            Broadcast("arenaReady", GridRef(configData.eventSettings.eventCenter));
            yield break;
        }

        private void SpawnPitchWall(Vector3 pos, Quaternion rot, float along)
        {
            Vector3 wallScale = new Vector3(along / NativeBlock, PitchWallHeight / NativeBlock, 1f);
            if (SpawnMetalBlock(WallPrefab, pos, rot, wallScale) != null)
                return;

            SpawnMetalBlock(
                FloorPrefab,
                pos + rot * Vector3.up * (PitchWallHeight * 0.5f),
                rot * Quaternion.Euler(90f, 0f, 0f),
                new Vector3(along / NativeBlock, 1f, PitchWallHeight / NativeBlock));
        }

        private BaseEntity SpawnMetalBlock(string prefab, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            BaseEntity entity = GameManager.server.CreateEntity(prefab, pos, rot);
            if (entity == null) return null;

            entity.enableSaving = false;
            entity.networkEntityScale = true;
            StripWorldOnlyComponents(entity.gameObject);
            entity.Spawn();

            if (entity is StabilityEntity stability)
                stability.grounded = true;
            if (entity is BuildingBlock block)
            {
                block.ChangeGradeAndSkin(BuildingGrade.Enum.Metal, 0);
                block.StopBeingDemolishable();
                block.SetHealth(block.MaxHealth());
            }
            if (entity is DecayEntity decay)
                decay.decay = null;
            if (entity is BaseCombatEntity combat)
                combat.pickup.enabled = false;

            entity.networkEntityScale = true;
            entity.transform.localScale = scale == Vector3.zero ? Vector3.one : scale;
            entity.SendNetworkUpdateImmediate();
            arenaEntities.Add(entity);
            return entity;
        }

        private static void StripWorldOnlyComponents(GameObject go)
        {
            if (go == null) return;
            var anchors = go.GetComponentsInChildren<TerrainAnchor>(true);
            for (int i = 0; i < anchors.Length; i++)
                UnityEngine.Object.DestroyImmediate(anchors[i]);
            var checks = go.GetComponentsInChildren<TerrainCheck>(true);
            for (int i = 0; i < checks.Length; i++)
                UnityEngine.Object.DestroyImmediate(checks[i]);
            var filters = go.GetComponentsInChildren<TerrainFilter>(true);
            for (int i = 0; i < filters.Length; i++)
                UnityEngine.Object.DestroyImmediate(filters[i]);
            var groundWatch = go.GetComponentsInChildren<GroundWatch>(true);
            for (int i = 0; i < groundWatch.Length; i++)
                UnityEngine.Object.DestroyImmediate(groundWatch[i]);
            var missing = go.GetComponentsInChildren<DestroyOnGroundMissing>(true);
            for (int i = 0; i < missing.Length; i++)
                UnityEngine.Object.DestroyImmediate(missing[i]);
        }

        private void SpawnArenaBeacon()
        {
            Vector3 pos = GetArenaTeleportPos();
            if (pos == Vector3.zero) return;
            BaseEntity ent = GameManager.server.CreateEntity(
                "assets/prefabs/deployable/search light/searchlight.deployed.prefab",
                pos,
                Quaternion.identity);
            if (ent == null) return;
            ent.enableSaving = false;
            ent.Spawn();
            arenaEntities.Add(ent);
        }
    }
}

