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

    internal struct ArenaPose
    {
        public BaseEntity Entity;
        public Vector3 LocalPos;
        public Quaternion LocalRot;
        public Vector3 Scale;
    }

    public static class ArenaCatalog
    {
        public static ArenaDefinition Definition { get; private set; }
        public static bool Ready => HasUsableLayout(Definition);

        public static void Load(RustLeaguePlugin plugin)
        {
            if (plugin?.configData == null) return;
            string cache = Path.Combine(RustLeagueHost.Instance.DataDirectory, "Arena.json");
            string configured = plugin.configData.settings.ArenaPrefabPath;
            string mapPath = ResolveArenaFile(plugin, configured);
            ArenaDefinition cachedDef = TryReadCache(cache);

            if (CacheIsUsable(cachedDef, mapPath))
            {
                Definition = cachedDef;
                ApplySizeToGrid(plugin);
                Debug.Log($"[RustLeague] Loaded cached arena {Definition.Size.x:F0}x{Definition.Size.z:F0} ({Definition.Count} parts) from HarmonyData/RustLeague/Arena.json");
                return;
            }

            if (mapPath == null)
            {
                Debug.LogWarning("[RustLeague] Arena prefab not found. Looked for maps/prefabs/RustLeagueArena.map and .prefab");
                if (HasUsableLayout(cachedDef))
                {
                    Definition = cachedDef;
                    ApplySizeToGrid(plugin);
                    Debug.LogWarning("[RustLeague] Using Arena.json size without the .map file.");
                }
                return;
            }

            var extracted = ExtractFromWorldMap(mapPath);
            if (HasUsableLayout(extracted))
            {
                Definition = extracted;
                try
                {
                    File.WriteAllText(cache, JsonConvert.SerializeObject(Definition, Formatting.Indented));
                    Debug.Log($"[RustLeague] Extracted arena {Definition.Size.x:F0}x{Definition.Size.z:F0} ({Definition.Parts.Count} named parts) -> HarmonyData/RustLeague/Arena.json");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RustLeague] Could not write Arena.json: " + ex.Message);
                }
                ApplySizeToGrid(plugin);
                return;
            }

            if (HasUsableLayout(cachedDef))
            {
                Definition = cachedDef;
                ApplySizeToGrid(plugin);
                Debug.LogWarning("[RustLeague] Map extract failed (StringPool not ready or unknown IDs); using Arena.json size.");
                return;
            }

            Debug.LogWarning("[RustLeague] Failed to extract prefabs from " + mapPath);
        }

        public static void EnsureLayout()
        {
            if (Ready) return;
            Definition = new ArenaDefinition
            {
                Source = "default",
                Count = 0,
                Size = new Vector3(90f, 12f, 217f),
                Center = Vector3.zero,
                RedGoal = new Vector3(0f, 1f, 108.5f),
                BlueGoal = new Vector3(0f, 1f, -108.5f),
                Parts = new List<ArenaPart>()
            };
            Debug.LogWarning("[RustLeague] Using default 90x217 pitch (arena map/cache unavailable).");
        }

        private static bool HasUsableLayout(ArenaDefinition def)
        {
            if (def == null) return false;
            return def.Size.x >= 8f && def.Size.z >= 8f
                && !float.IsNaN(def.Size.x) && !float.IsInfinity(def.Size.x)
                && !float.IsNaN(def.Size.z) && !float.IsInfinity(def.Size.z);
        }

        private static bool CacheIsUsable(ArenaDefinition cached, string mapPath)
        {
            if (!HasUsableLayout(cached) || ContainsEditorGizmos(cached)) return false;
            if (string.IsNullOrEmpty(cached.Source) || string.IsNullOrEmpty(mapPath)) return true;
            return string.Equals(
                Path.GetFileName(cached.Source),
                Path.GetFileName(mapPath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static ArenaDefinition TryReadCache(string cache)
        {
            if (!File.Exists(cache)) return null;
            try
            {
                return JsonConvert.DeserializeObject<ArenaDefinition>(File.ReadAllText(cache));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustLeague] Arena.json cache unreadable: " + ex.Message);
                return null;
            }
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

                bool known = !string.IsNullOrEmpty(prefabPath);
                if (known && prefabPath.IndexOf("ioslothandle", StringComparison.OrdinalIgnoreCase) >= 0)
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

                if (!known)
                {
                    skipped++;
                    continue;
                }

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
        internal readonly List<BaseEntity> arenaEntities = new List<BaseEntity>(700);
        internal readonly List<GameObject> arenaObjects = new List<GameObject>(16);
        private readonly List<ArenaPose> _arenaPoses = new List<ArenaPose>(700);
        private Coroutine _arenaSpawn;
        private string _arenaProxyPrefab;
        private Vector3 _arenaProxyNative = Vector3.one;
        private BaseEntity _arenaRoot;
        private Vector3 _arenaWorldCenter;
        private Vector3 _redGoalWallLocal;
        private Vector3 _blueGoalWallLocal;
        private Vector3 _redGoalFloorLocal;
        private Vector3 _blueGoalFloorLocal;
        private bool _goalPartsFound;

        private const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
        private const string WallPrefab = "assets/prefabs/building core/wall/wall.prefab";
        private const string BoatFloorPrefab = "assets/prefabs/building boat/floor/floor.prefab";
        private const string BoatWallPrefab = "assets/prefabs/building boat/wall/wall.prefab";
        private const string ArenaRootPrefab = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";
        private static readonly Vector3 BoatFloorNative = new Vector3(3f, 0.15f, 3f);
        private const float NativeBlock = 3f;
        private const float PlateOverlap = 1.2f;
        internal const float PitchWallHeight = 10f;

        private static readonly string[] CubePrefabCandidates =
        {
            "assets/bundled/prefabs/modding/cubes/tiled/cube_tiled_glass_01.prefab",
            "assets/bundled/prefabs/modding/cubes/tiled/cube_tiled_glass_03.prefab",
            "assets/bundled/prefabs/modding/cubes/white_cube.prefab",
            "assets/bundled/prefabs/modding/cubes/black_cube.prefab",
            "assets/bundled/prefabs/modding/cubes/concrete_cube.prefab",
            "assets/bundled/prefabs/modding/cubes/tiled/cube_tiled_metal_01.prefab",
            FloorPrefab
        };

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
                ArenaCatalog.Load(this);
            if (!ArenaCatalog.Ready)
                ArenaCatalog.EnsureLayout();

            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            var def = ArenaCatalog.Definition;
            float lift = PlayfieldSpawnLift(def);
            Vector3 surface = Vector3.up * lift;
            configData.eventSettings.eventCenter = worldOrigin + rot * def.Center + surface;
            PlaceGoalVolumesFromCatalog(def, worldOrigin, rot);
            float sx = Mathf.Max(20f, def.Size.x + 10f);
            float sy = Mathf.Max(20f, PitchWallHeight + 10f);
            float sz = Mathf.Max(20f, def.Size.z + 10f);
            configData.eventSettings.ArenaBoundsSize = new Vector3(sx, sy, sz);
        }

        // Score at playfield height across the goal mouth, not up in the greenhouse
        // box behind the back wall. Depth covers the lip plus several meters of pitch.
        private void PlaceGoalVolumesFromCatalog(ArenaDefinition def, Vector3 worldOrigin, Quaternion rot)
        {
            Vector3 redLocal = def != null ? def.RedGoal : Vector3.zero;
            Vector3 blueLocal = def != null ? def.BlueGoal : Vector3.zero;
            Vector3 center = def != null ? def.Center : Vector3.zero;
            Vector3? redFloor = null, blueFloor = null, redWall = null, blueWall = null;
            _goalPartsFound = false;

            if (def?.Parts != null)
            {
                for (int i = 0; i < def.Parts.Count; i++)
                {
                    var part = def.Parts[i];
                    Vector3 s = part.Scale == Vector3.zero ? Vector3.one : part.Scale;
                    Vector3 p = AdjustGoalBoxLocalPos(part);
                    if (ScaleNear(s, 1f, 13.85f, 17.21f))
                    {
                        if (p.z >= center.z) redFloor = p;
                        else blueFloor = p;
                    }
                    else if (ScaleNear(s, 1f, 30.6f, 17.21f))
                    {
                        if (p.z >= center.z) redWall = p;
                        else blueWall = p;
                    }
                }
            }

            if (redFloor.HasValue) _redGoalFloorLocal = redFloor.Value;
            if (blueFloor.HasValue) _blueGoalFloorLocal = blueFloor.Value;
            if (redWall.HasValue) _redGoalWallLocal = redWall.Value;
            if (blueWall.HasValue) _blueGoalWallLocal = blueWall.Value;
            _goalPartsFound = redWall.HasValue && blueWall.HasValue;

            if (redFloor.HasValue)
                redLocal = redFloor.Value;
            else if (redWall.HasValue)
                redLocal = redWall.Value;

            if (blueFloor.HasValue)
                blueLocal = blueFloor.Value;
            else if (blueWall.HasValue)
                blueLocal = blueWall.Value;

            // Pull onto the pitch so the box covers the mouth, not only the pocket.
            redLocal = PullGoalTowardField(redLocal, center, 6f);
            blueLocal = PullGoalTowardField(blueLocal, center, 6f);

            float playY = center.y + PlayfieldSpawnLift(def);
            redLocal.y = playY;
            blueLocal.y = playY;

            configData.eventSettings.RedZone = worldOrigin + rot * redLocal;
            configData.eventSettings.BlueZone = worldOrigin + rot * blueLocal;
            configData.eventSettings.RedZoneSize = new Vector3(20f, 20f, 24f);
            configData.eventSettings.BlueZoneSize = new Vector3(20f, 20f, 24f);
        }

        private static Vector3 PullGoalTowardField(Vector3 goal, Vector3 center, float meters)
        {
            Vector3 along = center - goal;
            along.y = 0f;
            if (along.sqrMagnitude < 1f) return goal;
            return goal + along.normalized * meters;
        }

        // Catalog center is the middle of the 10m floor slab. Lift spawn points to the top plus car clearance.
        private static float PlayfieldSpawnLift(ArenaDefinition def)
        {
            float halfThick = 5f;
            if (def?.Parts == null) return halfThick + 1.25f;

            for (int i = 0; i < def.Parts.Count; i++)
            {
                var part = def.Parts[i];
                if (part.Path == null || part.Path.IndexOf("glass", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Vector3 sc = part.Scale == Vector3.zero ? Vector3.one : part.Scale;
                Quaternion r = Quaternion.Euler(part.Rot);
                Vector3 ex = r * new Vector3(sc.x, 0f, 0f);
                Vector3 ey = r * new Vector3(0f, sc.y, 0f);
                Vector3 ez = r * new Vector3(0f, 0f, sc.z);
                float xw = Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x);
                float zw = Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z);
                if (xw * zw < 2000f) continue;
                float yExtent = Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y);
                float half = yExtent * 0.5f;
                if (half > halfThick) halfThick = half;
            }

            return halfThick + 1.25f;
        }

        internal void StartArenaSpawn(Vector3 worldOrigin, float yaw)
        {
            StopArenaSpawn();
            DespawnArena();
            if (!ArenaCatalog.Ready)
                ArenaCatalog.Load(this);
            if (!ArenaCatalog.Ready)
                ArenaCatalog.EnsureLayout();
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
            _arenaPoses.Clear();
            if (_arenaRoot != null && !_arenaRoot.IsDestroyed)
            {
                _arenaRoot.Kill();
                _arenaRoot = null;
            }
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

            if (def.Parts == null || def.Parts.Count == 0)
            {
                SpawnFallbackPitch(worldOrigin, yawRot, def);
                SpawnArenaBeacon();
                _arenaSpawn = null;
                Broadcast("arenaReady", GridRef(configData.eventSettings.eventCenter));
                yield break;
            }

            EnsurePanelPrefabs();
            _arenaWorldCenter = worldOrigin + yawRot * def.Center;
            _arenaRoot = SpawnArenaRoot(worldOrigin, yawRot);
            float delay = Mathf.Max(0.001f, configData.settings.ArenaSpawnDelay);
            int spawned = 0;
            int skipped = 0;
            int failed = 0;

            for (int i = 0; i < def.Parts.Count; i++)
            {
                var part = def.Parts[i];
                if (ShouldSkipArenaPart(part.Path))
                {
                    skipped++;
                    continue;
                }

                Vector3 localPos = AdjustGoalBoxLocalPos(part);
                Vector3 pos = worldOrigin + yawRot * localPos;
                Quaternion rot = yawRot * Quaternion.Euler(part.Rot);
                Vector3 scale = part.Scale == Vector3.zero ? Vector3.one : part.Scale;

                if (PrefabIsNetworkEntity(part.Path))
                {
                    if (SpawnMetalBlock(part.Path, pos, rot, scale) != null)
                        spawned++;
                    else
                        failed++;
                }
                else if (part.Path.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
                    || part.Path.IndexOf("/cubes/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (SpawnOrientedPanel(pos, rot, scale))
                        spawned++;
                    else
                        failed++;
                }
                else
                {
                    skipped++;
                }

                if ((i + 1) % 8 == 0)
                    yield return CoroutineEx.waitForSeconds(delay);
            }

            yield return CoroutineEx.waitForSeconds(0.2f);
            ReapplyArenaPoses();
            SpawnGoalLipCovers(worldOrigin, yawRot);
            SpawnGoalTeamLights(worldOrigin, yawRot);
            yield return CoroutineEx.waitForSeconds(1f);
            ReapplyArenaPoses();

            SpawnArenaBeacon();
            _arenaSpawn = null;
            Debug.Log($"[RustLeague] Arena rebuilt: {spawned} solid boat-floor plates (skipped {skipped} volumes, failed {failed}). TP {GetArenaTeleportPos()}");
            Broadcast("arenaReady", GridRef(configData.eventSettings.eventCenter));
        }

        private const string RedGoalLamp = "assets/prefabs/misc/permstore/industriallight/industrial.wall.lamp.red.deployed.prefab";
        private const string BlueGoalLamp = "assets/prefabs/misc/permstore/industriallight/industrial.wall.lamp.blue.deployed.prefab";
        private const string RedGoalSiren = "assets/prefabs/io/electric/lights/sirenlightorange.prefab";
        private const string BlueGoalSiren = "assets/prefabs/io/electric/lights/sirenlightblue.prefab";

        private void SpawnGoalTeamLights(Vector3 worldOrigin, Quaternion yawRot)
        {
            SpawnGoalLightFrame(worldOrigin, yawRot, true);
            SpawnGoalLightFrame(worldOrigin, yawRot, false);
        }

        private void SpawnGoalLightFrame(Vector3 worldOrigin, Quaternion yawRot, bool red)
        {
            string bar = red ? RedGoalLamp : BlueGoalLamp;
            string post = red ? RedGoalSiren : BlueGoalSiren;

            Vector3 wall = red ? _redGoalWallLocal : _blueGoalWallLocal;
            Vector3 floor = red ? _redGoalFloorLocal : _blueGoalFloorLocal;
            Vector3 center = ArenaCatalog.Definition != null ? ArenaCatalog.Definition.Center : Vector3.zero;
            Vector3 towardField;
            if (_goalPartsFound)
            {
                towardField = center - wall;
                towardField.y = 0f;
                if (towardField.sqrMagnitude < 0.01f)
                    towardField = red ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, 1f);
                towardField.Normalize();
            }
            else
            {
                wall = red ? new Vector3(24.79f, -0.75f, 199.25f) : new Vector3(24.79f, -0.75f, 2.05f);
                floor = red ? new Vector3(24.79f, -16.55f, 205.73f) : new Vector3(24.79f, -16.55f, -4.43f);
                towardField = red ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, 1f);
            }

            // Sit on the field-facing face of the back wall (0.85m from wall center ≈ 0.35m off the face).
            Vector3 mount = wall + towardField * 0.85f;
            float cx = wall.x;
            float barY = floor.y + 1.6f;
            float postLow = floor.y - 0.9f;
            float postHigh = floor.y + 4.6f;
            Quaternion face = yawRot * Quaternion.LookRotation(towardField, Vector3.up);
            float[] xs = { -7f, -3.5f, 0f, 3.5f, 7f };
            for (int i = 0; i < xs.Length; i++)
                SpawnIoLight(bar, worldOrigin + yawRot * new Vector3(cx + xs[i], barY, mount.z), face);
            SpawnIoLight(post, worldOrigin + yawRot * new Vector3(cx - 8.4f, postLow, mount.z), face);
            SpawnIoLight(post, worldOrigin + yawRot * new Vector3(cx + 8.4f, postLow, mount.z), face);
            SpawnIoLight(post, worldOrigin + yawRot * new Vector3(cx, postHigh, mount.z), face);
        }

        private void SpawnIoLight(string prefab, Vector3 pos, Quaternion rot)
        {
            if (string.IsNullOrEmpty(prefab)) return;
            bool parented = _arenaRoot != null && !_arenaRoot.IsDestroyed;
            Vector3 localPos = pos;
            Quaternion localRot = rot;
            if (parented)
            {
                localPos = Quaternion.Inverse(_arenaRoot.transform.rotation) * (pos - _arenaRoot.transform.position);
                localRot = Quaternion.Inverse(_arenaRoot.transform.rotation) * rot;
            }

            BaseEntity entity = GameManager.server.CreateEntity(prefab, parented ? localPos : pos, parented ? localRot : rot);
            if (entity == null) return;
            entity.enableSaving = false;
            entity.globalBroadcast = true;
            StripWorldOnlyComponents(entity.gameObject);
            if (entity is DecayEntity decay)
                decay.decay = null;
            if (entity is BaseCombatEntity combat)
                combat.pickup.enabled = false;
            if (parented)
                entity.SetParent(_arenaRoot);
            entity.Spawn();
            if (entity == null || entity.IsDestroyed) return;
            StripWorldOnlyComponents(entity.gameObject);
            if (entity is DecayEntity decay2)
                decay2.decay = null;
            if (entity is BaseCombatEntity combat2)
                combat2.pickup.enabled = false;
            if (parented)
            {
                entity.transform.localPosition = localPos;
                entity.transform.localRotation = localRot;
            }
            var io = entity as IOEntity;
            if (io != null)
                io.UpdateFromInput(100, 0);
            entity.SetFlag(BaseEntity.Flags.On, true);
            entity.SendNetworkUpdateImmediate();
            arenaEntities.Add(entity);
            _arenaPoses.Add(new ArenaPose
            {
                Entity = entity,
                LocalPos = parented ? localPos : pos,
                LocalRot = parented ? localRot : rot,
                Scale = Vector3.one
            });
        }

        private void SpawnFallbackPitch(Vector3 worldOrigin, Quaternion yawRot, ArenaDefinition def)
        {
            Vector3 floorCenter = worldOrigin + yawRot * def.Center;
            float width = Mathf.Max(24f, def.Size.x);
            float length = Mathf.Max(24f, def.Size.z);
            if (SpawnMetalBlock(BoatFloorPrefab, floorCenter, yawRot, new Vector3(width / NativeBlock, 1f, length / NativeBlock)) == null)
                SpawnMetalBlock(FloorPrefab, floorCenter, yawRot, new Vector3(width / NativeBlock, 1f, length / NativeBlock));
            SpawnPitchWall(floorCenter + yawRot * new Vector3(0f, 0.05f, length * 0.5f), yawRot, width);
            SpawnPitchWall(floorCenter + yawRot * new Vector3(0f, 0.05f, -length * 0.5f), yawRot * Quaternion.Euler(0f, 180f, 0f), width);
            SpawnPitchWall(floorCenter + yawRot * new Vector3(width * 0.5f, 0.05f, 0f), yawRot * Quaternion.Euler(0f, 90f, 0f), length);
            SpawnPitchWall(floorCenter + yawRot * new Vector3(-width * 0.5f, 0.05f, 0f), yawRot * Quaternion.Euler(0f, -90f, 0f), length);
            Debug.Log($"[RustLeague] Arena fallback pitch {width:F0}x{length:F0} (catalog had no parts).");
        }

        private void ResolveArenaProxy()
        {
            if (!string.IsNullOrEmpty(_arenaProxyPrefab)) return;

            for (int i = 0; i < CubePrefabCandidates.Length; i++)
            {
                string path = CubePrefabCandidates[i];
                bool isFloor = string.Equals(path, FloorPrefab, StringComparison.OrdinalIgnoreCase);
                if (!isFloor && !PrefabIsNetworkEntity(path))
                    continue;
                if (isFloor && GameManager.server.FindPrefab(path) == null)
                    continue;

                _arenaProxyPrefab = path;
                _arenaProxyNative = GetPrefabNativeSize(path);
                Debug.Log($"[RustLeague] Arena visual proxy: {path} native {_arenaProxyNative.x:F2}x{_arenaProxyNative.y:F2}x{_arenaProxyNative.z:F2}");
                return;
            }

            _arenaProxyPrefab = FloorPrefab;
            _arenaProxyNative = new Vector3(3f, 0.15f, 3f);
            Debug.LogWarning("[RustLeague] Arena visual proxy fell back to metal floor. Rust Edit cubes are world-only and do not replicate.");
        }

        private static bool PrefabIsNetworkEntity(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            GameObject prefab = GameManager.server.FindPrefab(path);
            return prefab != null && prefab.GetComponent<BaseEntity>() != null;
        }

        private static Vector3 GetPrefabNativeSize(string path)
        {
            if (path != null && path.IndexOf("building boat/floor", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(3f, 0.15f, 3f);
            if (path != null && path.IndexOf("building boat/wall", StringComparison.OrdinalIgnoreCase) >= 0
                && path.IndexOf("wall.low", StringComparison.OrdinalIgnoreCase) < 0)
                return new Vector3(3f, 3f, 0.15f);
            if (path != null && path.IndexOf("building core/floor", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(3f, 0.15f, 3f);
            if (path != null && path.IndexOf("building core/wall", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(3f, 3f, 0.15f);
            if (path != null && path.IndexOf("/cubes/", StringComparison.OrdinalIgnoreCase) >= 0)
                return Vector3.one;

            GameObject prefab = GameManager.server.FindPrefab(path);
            if (prefab == null) return Vector3.one;

            Vector3 size = Vector3.zero;
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                Vector3 s = Vector3.Scale(mesh.bounds.size, filters[i].transform.lossyScale);
                size = Vector3.Max(size, s);
            }
            if (size.x < 0.05f) size.x = 1f;
            if (size.y < 0.05f) size.y = 1f;
            if (size.z < 0.05f) size.z = 1f;
            return size;
        }

        private Vector3 ToProxyScale(Vector3 sourceScale)
        {
            if (sourceScale == Vector3.zero) sourceScale = Vector3.one;
            Vector3 native = _arenaProxyNative;
            if (native.x < 0.01f) native.x = 1f;
            if (native.y < 0.01f) native.y = 1f;
            if (native.z < 0.01f) native.z = 1f;
            return new Vector3(sourceScale.x / native.x, sourceScale.y / native.y, sourceScale.z / native.z);
        }

        private static bool ShouldSkipArenaPart(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            if (path.IndexOf("ioslothandle", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (path.IndexOf("prevent_building", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (path.IndexOf("invisible_collider", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool ScaleNear(Vector3 scale, float x, float y, float z)
        {
            return Mathf.Abs(scale.x - x) < 0.2f
                && Mathf.Abs(scale.y - y) < 0.2f
                && Mathf.Abs(scale.z - z) < 0.2f;
        }

        private void SpawnGoalLipCovers(Vector3 worldOrigin, Quaternion yawRot)
        {
            Vector3 scale = new Vector3(15f / BoatFloorNative.x, 0.2f / BoatFloorNative.y, 5f / BoatFloorNative.z);
            SpawnGoalLipCover(worldOrigin, yawRot, new Vector3(24.75f, -25.85f, -1.84f), scale);
            SpawnGoalLipCover(worldOrigin, yawRot, new Vector3(24.75f, -25.85f, 203.59f), scale);
        }

        private void SpawnGoalLipCover(Vector3 worldOrigin, Quaternion yawRot, Vector3 local, Vector3 scale)
        {
            Vector3 pos = worldOrigin + yawRot * local;
            if (SpawnMetalBlock(BoatFloorPrefab, pos, yawRot, scale) == null)
                SpawnMetalBlock(FloorPrefab, pos, yawRot, scale);
        }

        // Goal pocket: raise the end floor to local Y -16.55 and sit the back wall on
        // its top edge. Same at both ends. Do not lift or flatten hull strips.
        private static Vector3 AdjustGoalBoxLocalPos(ArenaPart part)
        {
            Vector3 p = part.Pos;
            Vector3 s = part.Scale == Vector3.zero ? Vector3.one : part.Scale;

            if (ScaleNear(s, 1f, 13.85f, 17.21f) && Mathf.Abs(p.y - (-21.12f)) < 0.3f)
                p.y = -16.55f;
            else if (ScaleNear(s, 1f, 30.6f, 17.21f) && Mathf.Abs(p.y - (-6.2f)) < 0.3f)
                p.y = -0.75f;
            else if (ScaleNear(s, 0.76f, 17.73f, 0.76f) && Mathf.Abs(p.y - (-21.84f)) < 0.3f)
                p.y += 4.57f;
            else if (part.Path != null
                && part.Path.IndexOf("fluorescent", StringComparison.OrdinalIgnoreCase) >= 0
                && Mathf.Abs(p.y - (-21.67f)) < 0.3f)
                p.y += 4.57f;

            return p;
        }

        private bool SpawnOrientedPanel(Vector3 worldPos, Quaternion cubeRot, Vector3 cubeScale)
        {
            if (cubeScale == Vector3.zero) cubeScale = Vector3.one;

            Vector3 edgeX = cubeRot * new Vector3(cubeScale.x, 0f, 0f);
            Vector3 edgeY = cubeRot * new Vector3(0f, cubeScale.y, 0f);
            Vector3 edgeZ = cubeRot * new Vector3(0f, 0f, cubeScale.z);

            float ax = edgeX.magnitude;
            float ay = edgeY.magnitude;
            float az = edgeZ.magnitude;

            Vector3 thin = edgeX;
            Vector3 edgeA = edgeY;
            Vector3 edgeB = edgeZ;
            if (ay <= ax && ay <= az)
            {
                thin = edgeY;
                edgeA = edgeX;
                edgeB = edgeZ;
            }
            else if (az <= ax && az <= ay)
            {
                thin = edgeZ;
                edgeA = edgeX;
                edgeB = edgeY;
            }

            if (thin.sqrMagnitude < 0.0001f)
                return false;

            return SpawnBoatPlate(worldPos, thin, edgeA, edgeB);
        }

        private bool SpawnBoatPlate(Vector3 cubeCenter, Vector3 thin, Vector3 edgeA, Vector3 edgeB)
        {
            Vector3 up = thin.normalized;
            bool horizontal = Mathf.Abs(up.y) >= 0.5f;
            if (horizontal)
            {
                if (Vector3.Dot(up, Vector3.up) < 0f)
                    up = -up;
            }
            else
            {
                Vector3 toInside = _arenaWorldCenter - cubeCenter;
                if (toInside.sqrMagnitude > 0.01f && Vector3.Dot(up, toInside) < 0f)
                    up = -up;
            }

            Vector3 forward = edgeB.sqrMagnitude >= edgeA.sqrMagnitude ? edgeB : edgeA;
            forward = Vector3.ProjectOnPlane(forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.Cross(up, Vector3.right);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.Cross(up, Vector3.forward);
            if (forward.sqrMagnitude < 0.0001f)
                return false;
            forward.Normalize();

            Quaternion rot = Quaternion.LookRotation(forward, up);
            Vector3 right = Vector3.Cross(up, forward);
            Vector3 localSize = new Vector3(
                (Mathf.Abs(Vector3.Dot(edgeA, right)) + Mathf.Abs(Vector3.Dot(edgeB, right))) * PlateOverlap,
                Mathf.Max(thin.magnitude, 0.15f),
                (Mathf.Abs(Vector3.Dot(edgeA, forward)) + Mathf.Abs(Vector3.Dot(edgeB, forward))) * PlateOverlap);

            if (localSize.x < 0.05f) localSize.x = 0.05f;
            if (localSize.z < 0.05f) localSize.z = 0.05f;

            Vector3 scale = new Vector3(
                localSize.x / BoatFloorNative.x,
                localSize.y / BoatFloorNative.y,
                localSize.z / BoatFloorNative.z);

            if (SpawnMetalBlock(BoatFloorPrefab, cubeCenter, rot, scale) != null)
                return true;
            return SpawnMetalBlock(FloorPrefab, cubeCenter, rot, scale) != null;
        }

        private void EnsurePanelPrefabs()
        {
            if (PrefabIsNetworkEntity(BoatFloorPrefab))
                Debug.Log("[RustLeague] Arena hull: solid boat floors, 5x5 lip covers at both goals.");
            else
                Debug.LogWarning("[RustLeague] Boat floor missing — falling back to building-core floor.");
        }

        private BaseEntity SpawnArenaRoot(Vector3 origin, Quaternion yawRot)
        {
            BaseEntity root = GameManager.server.CreateEntity(ArenaRootPrefab, origin, yawRot);
            if (root == null)
            {
                Debug.LogWarning("[RustLeague] Arena root prefab failed — plates will spawn unparented.");
                return null;
            }

            root.enableSaving = false;
            root.globalBroadcast = true;
            StripWorldOnlyComponents(root.gameObject);
            root.Spawn();
            root.transform.position = origin;
            root.transform.rotation = yawRot;
            root.transform.localScale = Vector3.one;
            root.globalBroadcast = true;
            root.SendNetworkUpdateImmediate();
            arenaEntities.Add(root);
            return root;
        }

        private void ReapplyArenaPoses()
        {
            for (int i = 0; i < _arenaPoses.Count; i++)
            {
                ArenaPose pose = _arenaPoses[i];
                if (pose.Entity == null || pose.Entity.IsDestroyed)
                    continue;
                pose.Entity.networkEntityScale = true;
                pose.Entity.transform.localPosition = pose.LocalPos;
                pose.Entity.transform.localRotation = pose.LocalRot;
                pose.Entity.transform.localScale = pose.Scale;
                pose.Entity.SendNetworkUpdateImmediate();
            }
        }

        private void SpawnPitchWall(Vector3 pos, Quaternion rot, float along)
        {
            Vector3 wallScale = new Vector3(along / NativeBlock, 1f, PitchWallHeight / NativeBlock);
            Quaternion plateRot = rot * Quaternion.Euler(90f, 0f, 0f);
            Vector3 platePos = pos + rot * Vector3.up * (PitchWallHeight * 0.5f);
            if (SpawnMetalBlock(BoatFloorPrefab, platePos, plateRot, wallScale) != null)
                return;
            SpawnMetalBlock(FloorPrefab, platePos, plateRot, wallScale);
        }

        private BaseEntity SpawnMetalBlock(string prefab, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            if (scale == Vector3.zero) scale = Vector3.one;

            bool parented = _arenaRoot != null && !_arenaRoot.IsDestroyed;
            Vector3 localPos = pos;
            Quaternion localRot = rot;
            if (parented)
            {
                localPos = Quaternion.Inverse(_arenaRoot.transform.rotation) * (pos - _arenaRoot.transform.position);
                localRot = Quaternion.Inverse(_arenaRoot.transform.rotation) * rot;
            }

            BaseEntity entity = GameManager.server.CreateEntity(prefab, localPos, localRot);
            if (entity == null) return null;

            entity.enableSaving = false;
            entity.globalBroadcast = true;
            entity.networkEntityScale = true;
            StripWorldOnlyComponents(entity.gameObject);
            if (entity is StabilityEntity stability)
                stability.grounded = true;
            if (parented)
                entity.SetParent(_arenaRoot);
            entity.transform.localScale = scale;
            entity.Spawn();
            if (entity == null || entity.IsDestroyed)
                return null;

            StripWorldOnlyComponents(entity.gameObject);
            if (entity is StabilityEntity grounded)
                grounded.grounded = true;
            if (entity is BuildingBlock block)
            {
                block.StopBeingDemolishable();
                block.SetHealth(block.MaxHealth());
            }
            if (entity is DecayEntity decay)
                decay.decay = null;
            if (entity is BaseCombatEntity combat)
                combat.pickup.enabled = false;

            entity.networkEntityScale = true;
            entity.globalBroadcast = true;
            if (parented)
            {
                entity.transform.localPosition = localPos;
                entity.transform.localRotation = localRot;
                entity.transform.localScale = scale;
            }
            else
            {
                entity.transform.position = pos;
                entity.transform.rotation = rot;
                entity.transform.localScale = scale;
            }
            entity.SendNetworkUpdateImmediate();
            arenaEntities.Add(entity);
            _arenaPoses.Add(new ArenaPose
            {
                Entity = entity,
                LocalPos = parented ? localPos : pos,
                LocalRot = parented ? localRot : rot,
                Scale = scale
            });
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

