using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// After GenerateDungeonGrid runs, tunnel entrances exist in DungeonGridEntrances and rails exist in Path.Rails.
    /// This postfix adds rail segments from the nearest point on the rail network to each tunnel entrance so
    /// above-ground tracks connect visually to the entrance (fixes tracks running past without connecting).
    /// </summary>
    [HarmonyPatch(typeof(GenerateDungeonGrid), nameof(GenerateDungeonGrid.Process))]
    public static class GenerateDungeonGrid_Process_RailTunnelConnection_Postfix
    {
        private const int MaxPathDepth = 250000;
        private const float RailSegmentMinLengthSq = 25f * 25f; // skip if entrance is already very close to rail

        static void Postfix(uint seed)
        {
            if (World.Networked || World.Cached)
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config == null)
                return;

            var dungeonEntrances = TerrainPathAccess.GetDungeonGridEntrances(TerrainMeta.Path);
            if (config.DebugLogging)
                LogDungeonEntranceGridAlignment(dungeonEntrances);

            if (!config.ConnectRailsToTunnelEntrances)
                return;
            if (!World.Config.AboveGroundRails)
                return;
            var rails = TerrainPathAccess.GetRails(TerrainMeta.Path);
            if (rails == null || rails.Count == 0)
                return;
            if (dungeonEntrances == null || dungeonEntrances.Count == 0)
                return;

            int length = (int)((float)World.Size / 7.5f);
            int[,] costmap = TerrainPath.CreateRailCostmap(ref seed);
            var pathFinder = new PathFinder(costmap);
            var points = new List<Vector3>();
            int added = 0;

            foreach (var entrance in dungeonEntrances)
            {
                if (entrance == null || entrance.transform == null)
                    continue;

                Vector3 entrancePos = entrance.transform.position;
                entrancePos.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(entrancePos), 1f);

                if (!FindClosestRailPoint(rails, entrancePos, out Vector3 closestRailPoint, out _))
                    continue;

                if ((entrancePos - closestRailPoint).sqrMagnitude < RailSegmentMinLengthSq)
                    continue;

                PathFinder.Point startPoint = PathFinder.GetPoint(closestRailPoint, length);
                PathFinder.Point endPoint = PathFinder.GetPoint(entrancePos, length);

                PathFinder.Node startNode = pathFinder.FindClosestWalkable(startPoint, 5);
                PathFinder.Node endNode = pathFinder.FindClosestWalkable(endPoint, 5);
                if (startNode == null || endNode == null)
                    continue;

                PathFinder.Node path = pathFinder.FindPath(startNode.point, endNode.point, MaxPathDepth);
                if (path == null)
                    continue;

                points.Clear();
                points.Add(closestRailPoint);
                for (PathFinder.Node n = path; n != null; n = n.next)
                {
                    float normX = ((float)n.point.x + 0.5f) / length;
                    float normZ = ((float)n.point.y + 0.5f) / length;
                    float x = TerrainMeta.DenormalizeX(normX);
                    float z = TerrainMeta.DenormalizeZ(normZ);
                    float y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(normX, normZ), 1f);
                    points.Add(new Vector3(x, y, z));
                }
                if (points.Count < 2)
                    continue;

                PathList segment = CreateRailSegment(rails.Count + added, points.ToArray());
                segment.Start = false;
                segment.End = true;
                segment.ProcgenStartNode = null;
                segment.ProcgenEndNode = null;

                float Filter(int i)
                {
                    float a = Mathf.InverseLerp(0f, 8f, i);
                    float b = Mathf.InverseLerp(segment.Path.DefaultMaxIndex, segment.Path.DefaultMaxIndex - 8, i);
                    return Mathf.SmoothStep(0f, 1f, Mathf.Min(a, b));
                }
                segment.Path.Smoothen(32, new Vector3(1f, 0f, 1f), Filter);
                segment.Path.Smoothen(64, new Vector3(0f, 1f, 0f), Filter);
                segment.Path.Resample(7.5f);
                segment.Path.RecalculateTangents();
                segment.AdjustPlacementMap(20f);

                rails.Add(segment);
                added++;
            }

            if (config.DebugLogging && added > 0)
                UnityEngine.Debug.Log($"[CustomMapGen] Connected {added} rail segment(s) to tunnel entrances.");
        }

        private static void LogDungeonEntranceGridAlignment(List<DungeonGridInfo> dungeonEntrances)
        {
            if (dungeonEntrances == null || dungeonEntrances.Count == 0)
            {
                UnityEngine.Debug.Log("[CustomMapGen] Dungeon grid alignment: no DungeonGridEntrances after GenerateDungeonGrid.");
                return;
            }
            foreach (var entrance in dungeonEntrances)
            {
                if (entrance == null || entrance.transform == null)
                    continue;
                Vector3 pos = entrance.transform.position;
                Vector3 station = World_AddPrefab_Patch.ClosestDungeonGridStation(pos, TerrainMeta.Size.x);
                float dx = pos.x - station.x;
                float dz = pos.z - station.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                UnityEngine.Debug.Log($"[CustomMapGen] Dungeon entrance '{entrance.name}' at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) station=({station.x:F1},{station.z:F1}) stationDist={dist:F1}m");
            }

            var cells = TerrainPathAccess.GetDungeonGridCells(TerrainMeta.Path);
            DungeonGridInfo centerEntrance = FindCenterOutpostDungeonEntrance(dungeonEntrances);
            if (centerEntrance != null)
            {
                LogCenterOutpostTunnelLinkDiagnostics(centerEntrance, cells);
                bool debug = CustomMapGen.Instance?.GetConfig()?.DebugLogging ?? false;
                TryCloseCenterOutpostPathLinkGap(centerEntrance, cells, debug);
            }

            if (cells == null || cells.Count == 0)
                return;
            int logged = 0;
            foreach (var cell in cells)
            {
                if (cell == null || cell.transform == null)
                    continue;
                Vector3 pos = cell.transform.position;
                Vector3 station = World_AddPrefab_Patch.ClosestDungeonGridStation(pos, TerrainMeta.Size.x);
                UnityEngine.Debug.Log($"[CustomMapGen] DungeonGridCell '{cell.name}' at ({pos.x:F1},{pos.z:F1}) station=({station.x:F1},{station.z:F1}) dStation=({pos.x - station.x:F1},{pos.z - station.z:F1})");
                logged++;
                if (logged >= 3)
                    break;
            }
        }

        private static DungeonGridInfo FindCenterOutpostDungeonEntrance(List<DungeonGridInfo> dungeonEntrances)
        {
            if (dungeonEntrances == null || dungeonEntrances.Count == 0)
                return null;

            Vector3 target = World_AddPrefab_Patch.CenterOutpostEntranceIfCached
                ?? World_AddPrefab_Patch.CenterOutpostPositionIfCached
                ?? TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            DungeonGridInfo best = null;
            float bestDistSq = float.MaxValue;
            foreach (var entrance in dungeonEntrances)
            {
                if (entrance == null || entrance.transform == null)
                    continue;
                Vector3 pos = entrance.transform.position;
                float dx = pos.x - target.x;
                float dz = pos.z - target.z;
                float distSq = dx * dx + dz * dz;
                if (distSq >= bestDistSq)
                    continue;
                bestDistSq = distSq;
                best = entrance;
            }
            return best;
        }

        private static void LogCenterOutpostTunnelLinkDiagnostics(DungeonGridInfo entrance, List<DungeonGridCell> cells)
        {
            Vector3 pos = entrance.transform.position;
            Vector3 station = World_AddPrefab_Patch.ClosestDungeonGridStation(pos, TerrainMeta.Size.x);
            float dx = pos.x - station.x;
            float dz = pos.z - station.z;
            float stationDist = Mathf.Sqrt(dx * dx + dz * dz);

            TerrainPathConnect[] connects = entrance.GetComponentsInChildren<TerrainPathConnect>(true);
            int tunnelConnects = 0;
            if (connects != null)
            {
                foreach (var c in connects)
                {
                    if (c != null && c.Type == InfrastructureType.Tunnel)
                        tunnelConnects++;
                }
            }
            DungeonGridLink link = entrance.GetComponentInChildren<DungeonGridLink>(true);
            DungeonVolume volume = entrance.GetComponentInChildren<DungeonVolume>(true);
            string volumeSize = volume != null
                ? $"({volume.bounds.size.x:F1},{volume.bounds.size.y:F1},{volume.bounds.size.z:F1})"
                : "missing";

            UnityEngine.Debug.Log($"[CustomMapGen] Center outpost dungeon '{entrance.name}' at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) yaw={entrance.transform.eulerAngles.y:F0} station=({station.x:F1},{station.z:F1}) stationDist={stationDist:F1}m dx={dx:F1} dz={dz:F1} tunnelConnects={tunnelConnects} dungeonLink={(link != null ? "yes" : "NO")} dungeonVolume={volumeSize}.");
            if (stationDist < 6f)
                UnityEngine.Debug.LogWarning("[CustomMapGen] Center outpost dungeon entrance is on the station cell. GenerateDungeonGrid will skip station+link pieces (volume overlap) and paint a normal tunnel cell instead.");
            if (Mathf.Abs(dx) < 6f || Mathf.Abs(dz) < 6f)
                UnityEngine.Debug.LogWarning($"[CustomMapGen] Center outpost dungeon leftover is axis-aligned (dx={dx:F1} dz={dz:F1}). PathLink rejects pieces when one axis is under 6m, which leaves a hole in the monument-to-station corridor.");

            if (link != null && link.DownSocket != null)
            {
                Vector3 down = link.DownSocket.position;
                UnityEngine.Debug.Log($"[CustomMapGen] Center outpost DownSocket at ({down.x:F1},{down.y:F1},{down.z:F1}).");
            }

            int linkCount = 0;
            if (World.SpawnedPrefabs != null && World.SpawnedPrefabs.TryGetValue("Dungeon", out HashSet<GameObject> dungeonPrefabs) && dungeonPrefabs != null)
            {
                foreach (GameObject piece in dungeonPrefabs)
                {
                    if (piece == null)
                        continue;
                    Vector3 lp = piece.transform.position;
                    float ldx = lp.x - pos.x;
                    float ldz = lp.z - pos.z;
                    float sdx = lp.x - station.x;
                    float sdz = lp.z - station.z;
                    // Keep pieces on the monument-to-station run (near entrance or station).
                    if (ldx * ldx + ldz * ldz > 90f * 90f && sdx * sdx + sdz * sdz > 90f * 90f)
                        continue;
                    if (piece.GetComponentInChildren<DungeonGridLink>(true) == null
                        && (piece.name == null || piece.name.IndexOf("corridor", System.StringComparison.OrdinalIgnoreCase) < 0)
                        && (piece.name == null || piece.name.IndexOf("link", System.StringComparison.OrdinalIgnoreCase) < 0)
                        && (piece.name == null || piece.name.IndexOf("elevator", System.StringComparison.OrdinalIgnoreCase) < 0)
                        && (piece.name == null || piece.name.IndexOf("transition", System.StringComparison.OrdinalIgnoreCase) < 0))
                        continue;
                    UnityEngine.Debug.Log($"[CustomMapGen] PathLink '{piece.name}' at ({lp.x:F1},{lp.y:F1},{lp.z:F1})");
                    linkCount++;
                    if (linkCount >= 48)
                        break;
                }
            }
            UnityEngine.Debug.Log($"[CustomMapGen] Center outpost PathLink count={linkCount}.");
            if (linkCount == 0)
                UnityEngine.Debug.LogWarning("[CustomMapGen] No PathLink sections near the center outpost entrance — monument door is not connected to the station.");

            if (cells == null)
                return;
            int nearby = 0;
            bool sawStationCell = false;
            foreach (var cell in cells)
            {
                if (cell == null || cell.transform == null)
                    continue;
                Vector3 cellPos = cell.transform.position;
                float cdx = cellPos.x - station.x;
                float cdz = cellPos.z - station.z;
                if (cdx * cdx + cdz * cdz > 200f * 200f)
                    continue;
                string n = cell.name ?? "";
                bool isStation = n.IndexOf("station", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (isStation)
                    sawStationCell = true;
                UnityEngine.Debug.Log($"[CustomMapGen] Tunnel cell near center outpost: '{n}' at ({cellPos.x:F1},{cellPos.y:F1},{cellPos.z:F1}){(isStation ? " STATION" : "")}");
                nearby++;
            }
            if (nearby == 0)
                UnityEngine.Debug.LogWarning("[CustomMapGen] No dungeon grid cells within 200m of the center outpost station — tunnel network did not occupy that cell.");
            else if (!sawStationCell)
                UnityEngine.Debug.LogWarning("[CustomMapGen] Center outpost station cell has no station prefab (only regular tunnel pieces). Monument-to-tunnel link sections were not built.");
        }

        /// <summary>
        /// Vanilla PathLink stops after 8 segments/side. Stairwells eat most of that budget, then the
        /// last approach becomes axis-aligned (one leftover axis under 6m) and leaves a hole at the
        /// station — same gap you fix in RustEdit by deleting stubs and placing link-straight-a-18m.
        /// Keep stairwells; strip leftover straights/corners; pin 18/9/3m from the station UpSocket
        /// back to the stairwell landing.
        /// </summary>
        private static void TryCloseCenterOutpostPathLinkGap(DungeonGridInfo entrance, List<DungeonGridCell> cells, bool debugLogging)
        {
            if (entrance == null || World.Cached || World.Networked)
                return;

            Vector3 entrancePos = entrance.transform.position;
            Vector3 stationPos = World_AddPrefab_Patch.ClosestDungeonGridStation(entrancePos, TerrainMeta.Size.x);

            DungeonGridLink stationLink = FindStationDungeonLink(cells, stationPos);
            if (stationLink == null || stationLink.UpSocket == null)
            {
                if (debugLogging)
                    UnityEngine.Debug.LogWarning("[CustomMapGen] PathLink gap closer: no station UpSocket near center outpost.");
                return;
            }

            Transform stationUp = stationLink.UpSocket;
            DungeonGridLink landingLink = FindStairwellLandingLink(entrancePos, stationPos);
            if (landingLink == null || landingLink.DownSocket == null)
            {
                if (debugLogging)
                    UnityEngine.Debug.LogWarning("[CustomMapGen] PathLink gap closer: no stairwell landing DownSocket.");
                return;
            }

            Transform landingDown = landingLink.DownSocket;
            Vector3 gap = stationUp.position - landingDown.position;
            float gapXZ = new Vector2(gap.x, gap.z).magnitude;
            if (gapXZ <= 1.5f && Mathf.Abs(gap.y) <= 1.5f)
            {
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] PathLink gap closer: already closed (gapXZ={gapXZ:F1}m).");
                return;
            }

            int removed = StripLeftoverCorridorLinks(entrancePos, stationPos, landingDown.position, stationUp.position, debugLogging);
            // Re-read landing after strip (landing itself is a stairwell and was kept).
            landingLink = FindStairwellLandingLink(entrancePos, stationPos) ?? landingLink;
            landingDown = landingLink.DownSocket;

            int placed = FillGapFromStationToLanding(stationUp, landingDown, debugLogging);
            UnityEngine.Debug.Log($"[CustomMapGen] PathLink gap closer: removed={removed} placed={placed} gapXZ={gapXZ:F1}m stationUp=({stationUp.position.x:F1},{stationUp.position.y:F1},{stationUp.position.z:F1}) landing=({landingDown.position.x:F1},{landingDown.position.y:F1},{landingDown.position.z:F1}).");
        }

        private static DungeonGridLink FindStationDungeonLink(List<DungeonGridCell> cells, Vector3 stationPos)
        {
            DungeonGridLink best = null;
            float bestDist = float.MaxValue;
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    if (cell == null || cell.transform == null)
                        continue;
                    string n = cell.name ?? "";
                    if (n.IndexOf("station", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    float dx = cell.transform.position.x - stationPos.x;
                    float dz = cell.transform.position.z - stationPos.z;
                    float d = dx * dx + dz * dz;
                    if (d > 40f * 40f || d >= bestDist)
                        continue;
                    var link = cell.GetComponentInChildren<DungeonGridLink>(true);
                    if (link == null || link.UpSocket == null)
                        continue;
                    bestDist = d;
                    best = link;
                }
            }
            if (best != null)
                return best;

            // Fallback: any DungeonGridLink near the station cell.
            if (World.SpawnedPrefabs != null && World.SpawnedPrefabs.TryGetValue("Dungeon", out HashSet<GameObject> dungeon) && dungeon != null)
            {
                foreach (GameObject go in dungeon)
                {
                    if (go == null)
                        continue;
                    string n = go.name ?? "";
                    if (n.IndexOf("station", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    float dx = go.transform.position.x - stationPos.x;
                    float dz = go.transform.position.z - stationPos.z;
                    if (dx * dx + dz * dz > 40f * 40f)
                        continue;
                    var link = go.GetComponentInChildren<DungeonGridLink>(true);
                    if (link?.UpSocket == null)
                        continue;
                    return link;
                }
            }
            return null;
        }

        private static DungeonGridLink FindStairwellLandingLink(Vector3 entrancePos, Vector3 stationPos)
        {
            DungeonGridLink best = null;
            float bestY = float.MaxValue;
            if (World.SpawnedPrefabs == null || !World.SpawnedPrefabs.TryGetValue("Dungeon", out HashSet<GameObject> dungeon) || dungeon == null)
                return null;

            foreach (GameObject go in dungeon)
            {
                if (go == null)
                    continue;
                string n = go.name ?? "";
                if (n.IndexOf("stairwell", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Vector3 p = go.transform.position;
                // Must sit under the monument door, not over at the station.
                float edx = p.x - entrancePos.x;
                float edz = p.z - entrancePos.z;
                if (edx * edx + edz * edz > 40f * 40f)
                    continue;
                var link = go.GetComponentInChildren<DungeonGridLink>(true);
                if (link?.DownSocket == null)
                    continue;
                float y = link.DownSocket.position.y;
                if (y >= bestY)
                    continue;
                bestY = y;
                best = link;
            }
            return best;
        }

        private static int StripLeftoverCorridorLinks(Vector3 entrancePos, Vector3 stationPos, Vector3 landingPos, Vector3 stationUpPos, bool debugLogging)
        {
            if (World.SpawnedPrefabs == null || !World.SpawnedPrefabs.TryGetValue("Dungeon", out HashSet<GameObject> dungeon) || dungeon == null)
                return 0;

            var toRemove = new List<GameObject>();
            foreach (GameObject go in dungeon)
            {
                if (go == null)
                    continue;
                string n = go.name ?? "";
                bool isStraight = n.IndexOf("link-straight", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isCorner = n.IndexOf("link-corner", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isStraight && !isCorner)
                    continue;
                Vector3 p = go.transform.position;
                // Only corridor pieces on the monument↔station run.
                if (!IsBetweenCorridor(p, entrancePos, stationPos, 70f))
                    continue;
                // Never strip stairwells (name check above) or anything at the landing/station sockets.
                if ((p - landingPos).sqrMagnitude < 4f || (p - stationUpPos).sqrMagnitude < 4f)
                    continue;
                toRemove.Add(go);
            }

            int removed = 0;
            foreach (GameObject go in toRemove)
            {
                Vector3 p = go.transform.position;
                string n = go.name ?? "";
                RemoveDungeonPrefabFromSerialization(p, n);
                dungeon.Remove(go);
                UnityEngine.Object.Destroy(go);
                removed++;
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] PathLink gap closer removed leftover '{n}' at ({p.x:F1},{p.y:F1},{p.z:F1}).");
            }
            return removed;
        }

        private static bool IsBetweenCorridor(Vector3 p, Vector3 a, Vector3 b, float pad)
        {
            float minX = Mathf.Min(a.x, b.x) - pad;
            float maxX = Mathf.Max(a.x, b.x) + pad;
            float minZ = Mathf.Min(a.z, b.z) - pad;
            float maxZ = Mathf.Max(a.z, b.z) + pad;
            return p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;
        }

        private static int FillGapFromStationToLanding(Transform stationUp, Transform landingDown, bool debugLogging)
        {
            // Grow from the station UpSocket toward the stairwell landing DownSocket.
            Vector3 socketPos = stationUp.position;
            Quaternion socketRot = stationUp.rotation;
            Vector3 target = landingDown.position;
            int placed = 0;
            const int maxPieces = 12;

            string[] candidates =
            {
                "assets/bundled/prefabs/autospawn/tunnel-link/link-straight-a-18m.prefab",
                "assets/bundled/prefabs/autospawn/tunnel-link/link-straight-a-9m.prefab",
                "assets/bundled/prefabs/autospawn/tunnel-link/link-straight-a-3m.prefab",
                "assets/bundled/prefabs/autospawn/tunnel-link/link-corner-a-3m.prefab",
                "assets/bundled/prefabs/autospawn/tunnel-link/link-corner-b-3m.prefab"
            };

            for (int iter = 0; iter < maxPieces; iter++)
            {
                Vector3 remaining = target - socketPos;
                float remXZ = new Vector2(remaining.x, remaining.z).magnitude;
                if (remXZ <= 1.25f && Mathf.Abs(remaining.y) <= 1.5f)
                    break;

                Prefab bestPrefab = null;
                DungeonGridLink bestLink = null;
                Vector3 bestPos = Vector3.zero;
                Quaternion bestRot = Quaternion.identity;
                float bestScore = float.MaxValue;
                Vector3 toward = new Vector3(remaining.x, 0f, remaining.z);
                if (toward.sqrMagnitude > 0.0001f)
                    toward.Normalize();

                foreach (string path in candidates)
                {
                    Prefab[] loaded = Prefab.Load(path, null, null, useProbabilities: false, useWorldConfig: false);
                    if (loaded == null || loaded.Length == 0 || loaded[0]?.Object == null)
                        continue;
                    Prefab prefab = loaded[0];
                    DungeonGridLink link = prefab.Object.GetComponentInChildren<DungeonGridLink>(true);
                    if (link == null || link.DownSocket == null || link.UpSocket == null)
                        continue;

                    // Mate this piece's DownSocket onto the current free UpSocket (station side).
                    Quaternion rot = socketRot * Quaternion.Inverse(link.DownSocket.localRotation);
                    Vector3 pos = socketPos - rot * link.DownSocket.localPosition;
                    Vector3 newUp = pos + rot * link.UpSocket.localPosition;
                    float afterXZ = new Vector2(target.x - newUp.x, target.z - newUp.z).magnitude;
                    if (afterXZ >= remXZ - 0.05f)
                        continue; // must reduce the gap

                    Vector3 moved = new Vector3(newUp.x - socketPos.x, 0f, newUp.z - socketPos.z);
                    float score = afterXZ;
                    if (moved.sqrMagnitude > 0.0001f && toward.sqrMagnitude > 0.0001f
                        && Vector3.Dot(toward, moved.normalized) < 0.2f
                        && path.IndexOf("corner", StringComparison.OrdinalIgnoreCase) < 0)
                        score += 40f;

                    if (score >= bestScore)
                        continue;
                    bestScore = score;
                    bestPrefab = prefab;
                    bestLink = link;
                    bestPos = pos;
                    bestRot = rot;
                }

                if (bestPrefab == null || bestLink == null)
                {
                    if (debugLogging)
                        UnityEngine.Debug.LogWarning($"[CustomMapGen] PathLink gap closer stuck with remaining XZ={remXZ:F1}m at socket ({socketPos.x:F1},{socketPos.y:F1},{socketPos.z:F1}).");
                    break;
                }

                World.AddPrefab("Dungeon", bestPrefab, bestPos, bestRot, Vector3.one);
                placed++;
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] PathLink gap closer placed '{bestPrefab.Name}' at ({bestPos.x:F1},{bestPos.y:F1},{bestPos.z:F1}).");

                socketPos = bestPos + bestRot * bestLink.UpSocket.localPosition;
                socketRot = bestRot * bestLink.UpSocket.localRotation;
            }

            return placed;
        }

        private static void RemoveDungeonPrefabFromSerialization(Vector3 worldPos, string nameHint)
        {
            if (World.Serialization == null)
                return;
            object worldObj = PostSaveSwap.GetWorldFromSerialization(World.Serialization);
            var list = worldObj != null ? PostSaveSwap.GetPrefabsListFromWorld(worldObj) : null;
            if (list == null || list.Count == 0)
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                object row = list[i];
                if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                    continue;
                string path = StringPool.Get(id) ?? "";
                if (path.IndexOf("tunnel-link", StringComparison.OrdinalIgnoreCase) < 0
                    && path.IndexOf("link-straight", StringComparison.OrdinalIgnoreCase) < 0
                    && path.IndexOf("link-corner", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                float px = PostSaveSwap.GetPrefabPositionComponent(row, "x");
                float py = PostSaveSwap.GetPrefabPositionComponent(row, "y");
                float pz = PostSaveSwap.GetPrefabPositionComponent(row, "z");
                float dx = px - worldPos.x;
                float dy = py - worldPos.y;
                float dz = pz - worldPos.z;
                if (dx * dx + dy * dy + dz * dz > 2.5f * 2.5f)
                    continue;
                list.RemoveAt(i);
            }
        }

        private static bool FindClosestRailPoint(List<PathList> rails, Vector3 worldPos, out Vector3 closestPoint, out float closestDistSq)
        {
            closestPoint = worldPos;
            closestDistSq = float.MaxValue;
            foreach (var rail in rails)
            {
                if (rail?.Path?.Points == null)
                    continue;
                foreach (Vector3 p in rail.Path.Points)
                {
                    float d2 = (worldPos - p).sqrMagnitude;
                    if (d2 < closestDistSq)
                    {
                        closestDistSq = d2;
                        closestPoint = p;
                    }
                }
            }
            return closestDistSq < float.MaxValue;
        }

        private static PathList CreateRailSegment(int number, Vector3[] points)
        {
            var segment = new PathList("Rail " + number, points)
            {
                Spline = true,
                Width = 4f,
                InnerPadding = 1f,
                OuterPadding = 1f,
                InnerFade = 1f,
                OuterFade = 32f,
                RandomScale = 1f,
                MeshOffset = 0f,
                TerrainOffset = -0.125f,
                Topology = 524288,
                Splat = 128,
                Hierarchy = 1
            };
            return segment;
        }
    }
}
