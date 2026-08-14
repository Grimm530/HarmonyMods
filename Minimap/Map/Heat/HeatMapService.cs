using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oxide.Ext.Chaos.Data;
using Oxide.Ext.Chaos.Map;
using Oxide.Ext.Chaos.UIFramework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using Time = UnityEngine.Time;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;
using UILayer = Oxide.Ext.Chaos.UIFramework.Layer;

namespace MinimapHarmony
{
    internal sealed class HeatMapService
    {
        public const string OverlayName = "heatmap";

        private readonly Minimap _plugin;
        private WorldGrid<HeatCell> _grid;
        private NativeArray<Color32> _heatColors;
        private Datafile<HeatWorldData> _stored;
        private Coroutine _routine;
        private string _overlayImage;
        private string _worldId;
        private bool _running;

        public HeatMapService(Minimap plugin)
        {
            _plugin = plugin;
        }

        public static bool IsEnabled
        {
            get
            {
                var heat = Minimap.Configuration?.Heat;
                return heat != null && (heat.EnablePvp || heat.EnablePve);
            }
        }

        public void Start()
        {
            if (_running || !IsEnabled || ServerMgr.Instance == null)
                return;

            var heat = Minimap.Configuration.Heat;
            EnsureColors(heat);

            float cellSize = TerrainMeta.Size.x / Mathf.CeilToInt(TerrainMeta.Size.x / Mathf.Max(10f, heat.GridSize));
            _grid = new WorldGrid<HeatCell>(TerrainMeta.Size.x + 1, cellSize);
            _worldId = ChaosMapPluginWorldId();

            if (heat.EnablePvp && heat.StoreData)
            {
                _stored = new Datafile<HeatWorldData>("heatmap");
                LoadStoredPvp();
            }

            _plugin.RegisterHeatOverlay();
            _running = true;
            _routine = ServerMgr.Instance.StartCoroutine(UpdateLoop());
            Debug.Log("[Minimap] Heat map started (PVP=" + heat.EnablePvp + ", PVE=" + heat.EnablePve + ")");
        }

        public void Stop()
        {
            if (!_running)
                return;

            if (_routine != null && ServerMgr.Instance != null)
                ServerMgr.Instance.StopCoroutine(_routine);
            _routine = null;

            SavePvp();
            _plugin.UnregisterHeatOverlay();

            if (_heatColors.IsCreated)
                _heatColors.Dispose();

            HeatCell.All.Clear();
            _grid = null;
            _overlayImage = null;
            _running = false;
        }

        public void PrepareUser(BaseMapUser mapUser)
        {
            if (mapUser == null || !IsEnabled)
                return;
            if (!mapUser.Overlays.ContainsKey(OverlayName))
                mapUser.Overlays[OverlayName] = true;
        }

        public void OnPvpDeath(BasePlayer victim, HitInfo info)
        {
            var heat = Minimap.Configuration?.Heat;
            if (!_running || heat == null || !heat.EnablePvp)
                return;
            if (victim == null || victim.IsDestroyed || victim.IsNpc)
                return;

            BasePlayer killer = info?.InitiatorPlayer;
            if (killer == null || killer.IsDestroyed || killer.IsNpc || killer == victim)
                return;

            RegisterPvp(victim.transform.position, Time.time);
        }

        public void OnServerSave()
        {
            SavePvp();
        }

        private IEnumerator UpdateLoop()
        {
            yield return CoroutineEx.waitForSeconds(3f);
            while (_running)
            {
                ExpirePvp();
                yield return RenderOverlay();
                float wait = Mathf.Max(15f, Minimap.Configuration?.Heat?.UpdateRate ?? 60f);
                yield return CoroutineEx.waitForSeconds(wait);
            }
        }

        private void RegisterPvp(Vector3 position, float time)
        {
            if (_grid == null || TerrainMeta.OutOfBounds(position))
                return;

            int2 coords = _grid.WorldToGridCoords(position);
            HeatCell cell = _grid[coords];
            if (cell == null)
                cell = _grid[coords] = new HeatCell(coords);
            cell.Events.Add(new HeatEvent(position, time));
        }

        private void ExpirePvp()
        {
            var heat = Minimap.Configuration?.Heat;
            if (heat == null || !heat.EnablePvp || _grid == null)
                return;

            float now = Time.time;
            for (int i = HeatCell.All.Count - 1; i >= 0; i--)
            {
                HeatCell cell = HeatCell.All[i];
                for (int e = cell.Events.Count - 1; e >= 0; e--)
                {
                    if (now - cell.Events[e].Time >= heat.ExpireTime)
                        cell.Events.RemoveAt(e);
                }
            }
        }

        private IEnumerator RenderOverlay()
        {
            var heat = Minimap.Configuration?.Heat;
            var map = Minimap.Configuration?.Map;
            if (heat == null || map == null || _grid == null)
                yield break;

            List<EventWithCell> eventList = new List<EventWithCell>();
            int cellCount = _grid.CellCount;

            if (heat.EnablePvp)
            {
                for (int i = 0; i < HeatCell.All.Count; i++)
                {
                    HeatCell cell = HeatCell.All[i];
                    if (cell == null || cell.Events.Count == 0)
                        continue;
                    int cellKey = cell.Coordinates.x + cell.Coordinates.y * cellCount;
                    for (int j = 0; j < cell.Events.Count; j++)
                    {
                        eventList.Add(new EventWithCell
                        {
                            cellKey = cellKey,
                            heatEvent = cell.Events[j]
                        });
                    }
                }
            }

            if (heat.EnablePve)
                CollectNpcEvents(eventList, cellCount);

            if (eventList.Count == 0)
            {
                _overlayImage = string.Empty;
                _plugin.NotifyHeatOverlayChanged();
                yield break;
            }

            eventList.Sort((a, b) => a.cellKey.CompareTo(b.cellKey));

            int totalCells = cellCount * cellCount;
            CellRange[] lookup = new CellRange[totalCells];
            for (int i = 0; i < totalCells; i++)
            {
                lookup[i].startIndex = -1;
                lookup[i].count = 0;
            }

            for (int i = 0; i < eventList.Count; i++)
            {
                int key = eventList[i].cellKey;
                if (key < 0 || key >= totalCells)
                    continue;
                if (lookup[key].startIndex == -1)
                {
                    lookup[key].startIndex = i;
                    lookup[key].count = 1;
                }
                else
                    lookup[key].count++;
            }

            int renderRes = Mathf.Max(256, map.RenderResolution / 2);
            HeatEvent[] sortedEvents = new HeatEvent[eventList.Count];
            CellRange[] cellLookup = lookup;
            Color32[] dst = new Color32[renderRes * renderRes];

            for (int i = 0; i < eventList.Count; i++)
                sortedEvents[i] = eventList[i].heatEvent;

            Color32[] colors = new Color32[_heatColors.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = _heatColors[i];

            HeatRenderArgs args = new HeatRenderArgs
            {
                heatColors = colors,
                dst = dst,
                sortedEvents = sortedEvents,
                cellLookup = cellLookup,
                invRadius = 1f / _grid.CellSize,
                radius = _grid.CellSize,
                terrainSize = TerrainMeta.Size,
                imageResolution = renderRes,
                maxCellValue = Mathf.Max(0.01f, heat.MaxCellValue),
                maxCellOpacity = Mathf.Clamp01(heat.MaxCellOpacity),
                fadeOpacity = heat.FadeOpacity,
                heatValue = heat.HeatValue,
                expireTime = Mathf.Max(1f, heat.ExpireTime),
                time = Time.time,
                cellCount = cellCount,
                cellSize = _grid.CellSize,
                margin = map.OceanMargin
            };

            GenerateHeatImage(args);
            yield return CoroutineEx.waitForEndOfFrame;

            Texture2D texture = new Texture2D(renderRes, renderRes, TextureFormat.RGBA32, false);
            _plugin.StoreHeatImage("minimap.heat." + _worldId, dst, texture, png =>
            {
                _overlayImage = png;
                _plugin.NotifyHeatOverlayChanged();
            });

            UnityEngine.Object.Destroy(texture);
        }

        private void CollectNpcEvents(List<EventWithCell> eventList, int cellCount)
        {
            if (_grid == null)
                return;

            float now = Time.time;
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                if (!TryGetNpcPosition(entity, out Vector3 position))
                    continue;
                if (TerrainMeta.OutOfBounds(position))
                    continue;

                int2 coords = _grid.WorldToGridCoords(position);
                int cellKey = coords.x + coords.y * cellCount;
                eventList.Add(new EventWithCell
                {
                    cellKey = cellKey,
                    heatEvent = new HeatEvent(position, now)
                });
            }
        }

        private static bool TryGetNpcPosition(BaseNetworkable entity, out Vector3 position)
        {
            position = default;
            if (entity == null || entity.IsDestroyed)
                return false;

            if (entity is BasePlayer player)
            {
                if (!player.IsNpc || player.IsDead())
                    return false;
                position = player.transform.position;
                return true;
            }

            if (entity is BaseNpc npc)
            {
                if (npc.IsDead())
                    return false;
                position = npc.transform.position;
                return true;
            }

            if (entity is SimpleShark shark)
            {
                if (shark.IsDead())
                    return false;
                position = shark.transform.position;
                return true;
            }

            return false;
        }

        private static void GenerateHeatImage(HeatRenderArgs args)
        {
            int res = args.imageResolution;
            int colorLen = args.heatColors.Length;
            Parallel.For(0, res * res, index =>
            {
                int x = index % res;
                int y = index / res;
                float2 normalized = new float2(x / (float)res, y / (float)res);
                float totalWidth = args.terrainSize.x + 2 * args.margin;
                float totalHeight = args.terrainSize.z + 2 * args.margin;
                float2 worldPos = new float2(
                    (normalized.x - 0.5f) * totalWidth,
                    (normalized.y - 0.5f) * totalHeight);

                int cellX = (int)math.ceil(worldPos.x * (1f / args.cellSize)) + (args.cellCount / 2);
                int cellY = (int)math.ceil(worldPos.y * (1f / args.cellSize)) + (args.cellCount / 2);
                cellX = (int)math.clamp(cellX, 0, args.cellCount - 1);
                cellY = (int)math.clamp(cellY, 0, args.cellCount - 1);

                float heat = 0f;
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        int nX = cellX + offsetX;
                        int nY = cellY + offsetY;
                        if (nX < 0 || nX >= args.cellCount || nY < 0 || nY >= args.cellCount)
                            continue;
                        int neighborKey = nX + nY * args.cellCount;
                        CellRange range = args.cellLookup[neighborKey];
                        if (range.startIndex < 0 || range.count == 0)
                            continue;

                        for (int i = range.startIndex; i < range.startIndex + range.count; i++)
                        {
                            HeatEvent evt = args.sortedEvents[i];
                            float2 evtPos = new float2(evt.WorldPosition.x, evt.WorldPosition.z);
                            float dist = math.distance(worldPos, evtPos);
                            if (dist >= args.radius)
                                continue;
                            heat += (1f - math.saturate(dist * args.invRadius)) *
                                    evt.Intensity(args.time, args.fadeOpacity, args.expireTime, args.heatValue);
                        }
                    }
                }

                args.dst[index] = GetHeatColor(heat, args, colorLen);
            });
        }

        private static Color32 GetHeatColor(float t, HeatRenderArgs args, int colorLen)
        {
            if (t <= 0f || colorLen == 0)
                return colorLen > 0 ? args.heatColors[0] : default;

            t = math.saturate(t / args.maxCellValue);
            int last = colorLen - 1;
            if (t >= 1f)
                return ClampOpacity(args.heatColors[last], args.maxCellOpacity);

            float frac = t * last;
            int lower = (int)math.clamp(math.floor(frac), 0, last);
            int upper = (int)math.clamp(math.ceil(frac), 0, last);
            Color32 from = args.heatColors[lower];
            Color32 to = args.heatColors[upper];
            float v = lower == upper ? 0f : math.saturate((t - lower / (float)last) / (upper / (float)last - lower / (float)last));
            return new Color32(
                (byte)(from.r + (to.r - from.r) * v),
                (byte)(from.g + (to.g - from.g) * v),
                (byte)(from.b + (to.b - from.b) * v),
                (byte)math.min(from.a + (to.a - from.a) * v, args.maxCellOpacity * 255f));
        }

        private static Color32 ClampOpacity(Color32 color, float maxOpacity)
        {
            color.a = (byte)math.min(color.a, maxOpacity * 255f);
            return color;
        }

        public void RenderOverlayUi(BaseMapUser mapUser, string name, string parent, bool isActive)
        {
            bool render = isActive && !string.IsNullOrEmpty(_overlayImage);
            RawImageContainer overlay = RawImageContainer.Create(name, UILayer.HudMenu, UIAnchor.FullStretch, Offset.zero);
            overlay.WithParent(parent);
            overlay.WithPNG(render ? _overlayImage : string.Empty);
            overlay.WithColor(render ? Oxide.Ext.Chaos.UIFramework.Color.White : Oxide.Ext.Chaos.UIFramework.Color.Clear);
            overlay.DestroyExisting();
            ChaosUI.Show(mapUser.Player, overlay);
        }

        public UpdateComponent UpdateOverlayUi(BaseMapUser mapUser, string name, bool isActive)
        {
            bool render = isActive && !string.IsNullOrEmpty(_overlayImage);
            UpdateComponent<RawImageComponent> update = ChaosUI.PrepareUpdate<RawImageComponent>(name);
            update.Component.PNG = render ? _overlayImage : string.Empty;
            update.Component.Color = render ? Oxide.Ext.Chaos.UIFramework.Color.White : Oxide.Ext.Chaos.UIFramework.Color.Clear;
            return update;
        }

        private void EnsureColors(Minimap.ConfigData.HeatSettings heat)
        {
            if (heat.HeatColors == null || heat.HeatColors.Length == 0)
                heat.HeatColors = Minimap.ConfigData.HeatSettings.DefaultColors();

            if (_heatColors.IsCreated)
                _heatColors.Dispose();

            _heatColors = new NativeArray<Color32>(heat.HeatColors.Length, Allocator.Persistent);
            for (int i = 0; i < heat.HeatColors.Length; i++)
                _heatColors[i] = heat.HeatColors[i];
        }

        private void LoadStoredPvp()
        {
            if (_stored?.Data == null || _grid == null)
                return;
            if (!string.Equals(_stored.Data.worldId, _worldId, StringComparison.Ordinal))
            {
                _stored.Data = new HeatWorldData { worldId = _worldId };
                return;
            }

            float now = Time.time;
            var heat = Minimap.Configuration.Heat;
            for (int i = 0; i < _stored.Data.events.Count; i++)
            {
                HeatEventRecord rec = _stored.Data.events[i];
                if (rec.age >= heat.ExpireTime)
                    continue;
                RegisterPvp(new Vector3(rec.x, 0f, rec.z), now - rec.age);
            }
        }

        private void SavePvp()
        {
            var heat = Minimap.Configuration?.Heat;
            if (!_running || heat == null || !heat.EnablePvp || !heat.StoreData)
                return;

            _stored ??= new Datafile<HeatWorldData>("heatmap");
            _stored.Data ??= new HeatWorldData();
            _stored.Data.worldId = _worldId;
            _stored.Data.events.Clear();
            float now = Time.time;
            for (int i = 0; i < HeatCell.All.Count; i++)
            {
                HeatCell cell = HeatCell.All[i];
                for (int e = 0; e < cell.Events.Count; e++)
                {
                    HeatEvent ev = cell.Events[e];
                    _stored.Data.events.Add(new HeatEventRecord
                    {
                        x = ev.WorldPosition.x,
                        z = ev.WorldPosition.z,
                        age = now - ev.Time
                    });
                }
            }
            _stored.Save();
        }

        private static string ChaosMapPluginWorldId()
        {
            try
            {
                return Minimap.CurrentWorldIdForHeat(Minimap.Configuration.Map.RenderResolution);
            }
            catch
            {
                return World.Size + "_" + World.Seed;
            }
        }

        private struct HeatEvent
        {
            public float3 WorldPosition;
            public float Time;

            public HeatEvent(Vector3 position, float time)
            {
                WorldPosition = position;
                Time = time;
            }

            public float Intensity(float time, bool fadeIntensity, float expireTime, float heatValue)
            {
                float remaining = (Time + expireTime) - time;
                if (remaining <= 0f)
                    return 0f;
                return fadeIntensity ? (remaining / expireTime) * heatValue : heatValue;
            }
        }

        private struct EventWithCell
        {
            public int cellKey;
            public HeatEvent heatEvent;
        }

        private struct CellRange
        {
            public int startIndex;
            public int count;
        }

        private struct HeatRenderArgs
        {
            public Color32[] heatColors;
            public Color32[] dst;
            public HeatEvent[] sortedEvents;
            public CellRange[] cellLookup;
            public float invRadius;
            public float radius;
            public float3 terrainSize;
            public int imageResolution;
            public float maxCellValue;
            public float maxCellOpacity;
            public bool fadeOpacity;
            public float expireTime;
            public float heatValue;
            public float time;
            public int cellCount;
            public float cellSize;
            public int margin;
        }

        private class HeatCell
        {
            public static readonly List<HeatCell> All = new List<HeatCell>();
            public readonly int2 Coordinates;
            public readonly List<HeatEvent> Events = new List<HeatEvent>();

            public HeatCell(int2 coordinates)
            {
                Coordinates = coordinates;
                All.Add(this);
            }
        }

        private class HeatWorldData
        {
            public string worldId;
            public List<HeatEventRecord> events = new List<HeatEventRecord>();
        }

        private class HeatEventRecord
        {
            public float x;
            public float z;
            public float age;
        }
    }
}
