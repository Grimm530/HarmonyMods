using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oxide.Ext.Chaos.TextMeshPro;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using Color = UnityEngine.Color;

namespace Oxide.Ext.Chaos.Map;

public class MapRenderer : System.IDisposable
{
	private readonly MapImageUtility _utility;
	private readonly MapConfig _config;
	private readonly DeepSeaMapConfig _deepSeaConfig;
	private readonly PermanentMarkerFont _font;
	private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(0.95f, 2.87f, 2.37f));
	private const float SunPower = 0.65f;
	private const float Brightness = 1.05f;
	private const float Contrast = 0.94f;
	private const float TrainTunnelCellSize = 216f;
	private const float TrainTunnelHalfWidth = 8f;
	private const float TrainTunnelHubHalf = 14f;

	public MapRenderer(MapImageUtility utility, MapConfig config, DeepSeaMapConfig deepSeaConfig)
	{
		_utility = utility;
		_config = config ?? new MapConfig();
		_deepSeaConfig = deepSeaConfig ?? new DeepSeaMapConfig();
		_font = new PermanentMarkerFont();
	}

	public void Dispose()
	{
		_font?.Dispose();
	}

	public IEnumerator RenderOverworld(NativeArray<Color> buffer0, NativeArray<Color> buffer1 = default, bool drawMarkers = true)
	{
		int res = _config.RenderResolution;
		float scaledMargin = (float)_config.OceanMargin / (TerrainMeta.Size.x + 2f * _config.OceanMargin) * res;
		float invImageRes = 1f / (res - scaledMargin * 2f);
		float3[] splatColors = _config.SplatColors;
		TerrainHeightMap heights = TerrainMeta.HeightMap;
		TerrainSplatMap splat = TerrainMeta.SplatMap;
		TerrainTopologyMap topology = TerrainMeta.TopologyMap;
		TerrainTexturing texturing = TerrainMeta.Texturing;
		float maxDepth = Mathf.Max(Mathf.Abs(heights.GetHeight(0, 0)), 5f);
		float3 half = new float3(0.5f, 0.5f, 0.5f);

		Parallel.For(0, res, y =>
		{
			float normZ = ((float)y - scaledMargin) * invImageRes;
			for (int x = 0; x < res; x++)
			{
				float normX = ((float)x - scaledMargin) * invImageRes;
				Color pixel = SampleMainland(normX, normZ, splatColors, heights, splat, topology, texturing, maxDepth, half);
				buffer0[y * res + x] = pixel;
			}
		});

		BlendOverlay(res, buffer0, _config.Overworld.Overlay);

		if (buffer1.IsCreated)
			buffer0.CopyTo(buffer1);

		if (drawMarkers)
			yield return DrawMonumentMarkers(buffer1.IsCreated ? buffer1 : buffer0, false);
		else
			yield return CoroutineEx.waitForEndOfFrame;
	}

	public IEnumerator RenderDeepSea(NativeArray<Color> buffer0, NativeArray<Color> buffer1 = default, bool drawMarkers = true)
	{
		int res = _config.RenderResolution;
		Bounds bounds = DeepSeaManager.DeepSeaBounds;
		float3[] splatColors = _deepSeaConfig.SplatColors;
		TerrainHeightMap heights = TerrainMeta.HeightMap;
		TerrainSplatMap splat = TerrainMeta.SplatMap;
		float3 half = new float3(0.5f, 0.5f, 0.5f);
		float maxDepth = 50f;

		Parallel.For(0, res, y =>
		{
			float tz = (float)y / (res - 1);
			for (int x = 0; x < res; x++)
			{
				float tx = (float)x / (res - 1);
				Vector3 world = new Vector3(
					Mathf.Lerp(bounds.min.x, bounds.max.x, tx),
					0f,
					Mathf.Lerp(bounds.min.z, bounds.max.z, tz));
				Color pixel = SampleWorld(world, splatColors, heights, splat, maxDepth, half, useSand2: true);
				buffer0[y * res + x] = pixel;
			}
		});

		BlendOverlay(res, buffer0, _deepSeaConfig.DeepSea.Overlay);

		if (buffer1.IsCreated)
			buffer0.CopyTo(buffer1);

		if (drawMarkers)
			yield return DrawDeepSeaMonumentMarkers(buffer1.IsCreated ? buffer1 : buffer0);
		else
			yield return CoroutineEx.waitForEndOfFrame;
	}

	public void BlendOverlay(int imageRes, NativeArray<Color> dst, Color color)
	{
		Parallel.For(0, imageRes, y =>
		{
			for (int x = 0; x < imageRes; x++)
			{
				int index = y * imageRes + x;
				dst[index] = ImageUtility.BlendColors(dst[index], color);
			}
		});
	}

	public void BlendUnderworldOverlay(NativeArray<Color> src)
	{
		if (_config.Underworld == null)
			_config.Underworld = new MapConfig.UnderworldColors();
		BlendOverlay(_config.RenderResolution, src, _config.Underworld.Overlay);
	}

	public IEnumerator RenderTrainTunnels(NativeArray<Color> src, bool drawMarkers = true)
	{
		int res = _config.RenderResolution;
		Color tunnel = _config.Underworld?.MarkerForeground ?? new Color(0.58f, 0.58f, 0.62f, 1f);
		Color station = Color.Lerp(tunnel, Color.white, 0.22f);
		float cellSize = TrainTunnelCellSize;
		if (TerrainMeta.Path?.DungeonGridEntrances != null && TerrainMeta.Path.DungeonGridEntrances.Count > 0)
		{
			int configured = TerrainMeta.Path.DungeonGridEntrances[0].CellSize;
			if (configured > 0)
				cellSize = configured;
		}

		int cells = 0;
		if (TerrainMeta.Path?.DungeonGridCells != null)
		{
			List<DungeonGridCell> list = TerrainMeta.Path.DungeonGridCells;
			for (int i = 0; i < list.Count; i++)
			{
				DungeonGridCell cell = list[i];
				if (cell == null)
					continue;
				cells++;
				DrawTunnelGridCell(src, res, cell, cellSize, tunnel, station);
				if ((i & 7) == 7)
					yield return CoroutineEx.waitForEndOfFrame;
			}
		}

		if (TerrainMeta.Path?.DungeonGridEntrances != null)
		{
			List<DungeonGridInfo> entrances = TerrainMeta.Path.DungeonGridEntrances;
			for (int i = 0; i < entrances.Count; i++)
			{
				DungeonGridInfo entrance = entrances[i];
				if (entrance?.Links == null)
					continue;
				for (int j = 0; j < entrance.Links.Count; j++)
				{
					GameObject link = entrance.Links[j];
					if (link == null)
						continue;
					DrawWorldRectCentered(src, res, link.transform.position, 16f, 16f, station);
				}
			}
		}

		Debug.Log($"[Minimap] Tunnel layer: {cells} dungeon-grid cells at {cellSize}m");
		yield return CoroutineEx.waitForEndOfFrame;
	}

	public IEnumerator RenderUnderwaterLabs(NativeArray<Color> src, int level, bool drawMarkers = true)
	{
		int res = _config.RenderResolution;
		Color lab = _config.Underworld?.MarkerForeground ?? new Color(0.35f, 0.72f, 0.68f, 1f);
		lab = Color.Lerp(lab, new Color(0.35f, 0.72f, 0.68f, 1f), 0.45f);
		if (TerrainMeta.Path != null)
		{
			List<DungeonBaseInfo> dungeons = TerrainMeta.Path.DungeonBaseEntrances;
			for (int d = 0; d < dungeons.Count; d++)
			{
				DungeonBaseInfo dungeon = dungeons[d];
				if (dungeon == null || dungeon.Floors == null || level < 0 || level >= dungeon.Floors.Count)
					continue;
				DungeonBaseFloor floor = dungeon.Floors[level];
				if (floor?.Links == null)
					continue;
				for (int i = 0; i < floor.Links.Count; i++)
				{
					DungeonBaseLink link = floor.Links[i];
					if (link == null)
						continue;
					DrawLabLink(src, res, link, lab);
				}

				yield return CoroutineEx.waitForEndOfFrame;
			}
		}

		yield return CoroutineEx.waitForEndOfFrame;
	}

	public IEnumerator DrawMonumentMarkers(NativeArray<Color> dst, bool underworld)
	{
		int len = dst.Length;
		for (int i = 0; i < len; i++)
			dst[i] = Color.clear;
		int res = _config.RenderResolution;
		bool names = _config.RenderMonumentNames;
		bool tunnels = _config.RenderTunnelEntrances;
		Color bg = underworld
			? (_config.Underworld?.MarkerBackground ?? new Color(0.13f, 0.13f, 0.13f, 1f))
			: (Color)_config.Overworld.MarkerBackground;
		Color fg = underworld
			? (_config.Underworld?.MarkerForeground ?? new Color(0.58f, 0.58f, 0.58f, 1f))
			: (Color)_config.Overworld.MarkerForeground;
		Color text = underworld
			? (_config.Underworld?.Text ?? Color.white)
			: (Color)_config.Overworld.Text;

		if (TerrainMeta.Path != null)
		{
			if (names)
			{
				List<MonumentInfo> monuments = TerrainMeta.Path.Monuments;
				for (int i = 0; i < monuments.Count; i++)
				{
					MonumentInfo monument = monuments[i];
					if (monument == null || !monument.shouldDisplayOnMap)
						continue;
					bool isUnder = monument.MapLayer != global::MapLayer.Overworld;
					if (isUnder != underworld)
						continue;
					string label = monument.displayPhrase?.english;
					DrawMarker(dst, res, monument.transform.position, bg, fg, names ? label : null, text);
				}
			}

			if (tunnels && underworld)
			{
				List<DungeonGridInfo> entrances = TerrainMeta.Path.DungeonGridEntrances;
				for (int i = 0; i < entrances.Count; i++)
				{
					DungeonGridInfo entrance = entrances[i];
					if (entrance == null) continue;
					DrawMarker(dst, res, entrance.transform.position, bg, fg, null, text);
				}
			}
		}

		yield return CoroutineEx.waitForEndOfFrame;
	}

	public IEnumerator DrawDeepSeaMonumentMarkers(NativeArray<Color> dst)
	{
		int len = dst.Length;
		for (int i = 0; i < len; i++)
			dst[i] = Color.clear;
		int res = _config.RenderResolution;
		Color bg = _config.Overworld.MarkerBackground;
		Color fg = _config.Overworld.MarkerForeground;
		Color text = _config.Overworld.Text;

		if (TerrainMeta.Path?.Landmarks != null)
		{
			List<LandmarkInfo> landmarks = TerrainMeta.Path.Landmarks;
			for (int i = 0; i < landmarks.Count; i++)
			{
				LandmarkInfo landmark = landmarks[i];
				if (landmark == null || !landmark.shouldDisplayOnMap)
					continue;
				if (!DeepSeaManager.IsInsideDeepSea(landmark.transform.position))
					continue;
				string label = landmark.displayPhrase?.english;
				DrawDeepSeaMarker(dst, res, landmark.transform.position, bg, fg, label, text);
			}
		}

		yield return CoroutineEx.waitForEndOfFrame;
	}

	private Color SampleMainland(float normX, float normZ, float3[] splatColors, TerrainHeightMap heights,
		TerrainSplatMap splat, TerrainTopologyMap topology, TerrainTexturing texturing, float maxDepth, float3 half)
	{
		bool inMap = normX >= 0f && normX <= 1f && normZ >= 0f && normZ <= 1f;
		float3 start = splatColors[0];
		if (!inMap)
		{
			start = splatColors[10];
			return new Color(start.x, start.y, start.z, 1f);
		}

		float height = heights.GetHeight(normX, normZ);
		Vector3 normal = heights.GetNormal(normX, normZ);
		float shoreDist = texturing != null ? texturing.GetMainlandCoarseVectorToShore(normX, normZ).shoreDist : 0f;
		bool waterTopo = (topology.GetTopology(normX, normZ, 16f) & 0x180) != 0;

		start = math.lerp(start, splatColors[1], splat.GetSplat(normX, normZ, 128));
		start = math.lerp(start, splatColors[8], splat.GetSplat(normX, normZ, 64));
		start = math.lerp(start, splatColors[6], splat.GetSplat(normX, normZ, 8));
		start = math.lerp(start, splatColors[2], splat.GetSplat(normX, normZ, 1));
		start = math.lerp(start, splatColors[4], splat.GetSplat(normX, normZ, 16));
		start = math.lerp(start, splatColors[5], splat.GetSplat(normX, normZ, 32));
		start = math.lerp(start, splatColors[3], splat.GetSplat(normX, normZ, 4));
		start = math.lerp(start, splatColors[7], splat.GetSplat(normX, normZ, 2));

		float waterDepth = 0f;
		if (shoreDist > 0f)
		{
			waterDepth = 0f - height;
			if (waterDepth <= 0f || !waterTopo)
				waterDepth = Mathf.Max(waterDepth, 0.1f * shoreDist);
		}

		if (waterDepth > 0f)
		{
			start = math.lerp(start, splatColors[9], Mathf.Clamp(0.5f + waterDepth / 5f, 0f, 1f));
			start = math.lerp(start, splatColors[10], Mathf.Clamp(waterDepth / maxDepth, 0f, 1f));
		}
		else
		{
			float sun = Mathf.Max(Vector3.Dot(normal, SunDirection), 0f);
			start += (sun - 0.5f) * SunPower * start;
			start = (start - half) * Contrast + half;
		}

		start *= Brightness;
		return new Color(start.x, start.y, start.z, 1f);
	}

	private Color SampleWorld(Vector3 world, float3[] splatColors, TerrainHeightMap heights, TerrainSplatMap splat,
		float maxDepth, float3 half, bool useSand2)
	{
		float3 start = splatColors[0];
		float height = heights.GetHeight(world);
		Vector3 normal = heights.GetNormal(world);

		start = math.lerp(start, splatColors[1], splat.GetSplat(world, 128));
		start = math.lerp(start, splatColors[8], splat.GetSplat(world, 64));
		start = math.lerp(start, splatColors[6], splat.GetSplat(world, 8));
		start = math.lerp(start, splatColors[2], splat.GetSplat(world, 1));
		start = math.lerp(start, splatColors[4], splat.GetSplat(world, 16));
		start = math.lerp(start, splatColors[5], splat.GetSplat(world, 32));
		start = math.lerp(start, splatColors[3], splat.GetSplat(world, 4));
		if (useSand2)
			start = math.lerp(start, splatColors[7], splat.GetSplat(world, 2));
		else
			start = math.lerp(start, splatColors[7], splat.GetSplat(world, 2));

		if (height < 0f)
		{
			float waterDepth = -height;
			start = math.lerp(start, splatColors[9], Mathf.Clamp(0.5f + waterDepth / 5f, 0f, 1f));
			start = math.lerp(start, splatColors[10], Mathf.Clamp(waterDepth / maxDepth, 0f, 1f));
		}
		else
		{
			float sun = Mathf.Max(Vector3.Dot(normal, SunDirection), 0f);
			start += (sun - 0.5f) * SunPower * start;
			start = (start - half) * Contrast + half;
		}

		start *= Brightness;
		return new Color(start.x, start.y, start.z, 1f);
	}

	private void DrawTunnelGridCell(NativeArray<Color> dst, int res, DungeonGridCell cell, float cellSize, Color tunnel, Color station)
	{
		Vector3 center = GetTunnelCellCenter(cell.transform.position, cellSize);
		float half = cellSize * 0.5f;
		bool north = cell.North != DungeonGridConnectionType.None;
		bool south = cell.South != DungeonGridConnectionType.None;
		bool east = cell.East != DungeonGridConnectionType.None;
		bool west = cell.West != DungeonGridConnectionType.None;
		int connections = (north ? 1 : 0) + (south ? 1 : 0) + (east ? 1 : 0) + (west ? 1 : 0);
		float hub = connections >= 3 ? TrainTunnelHubHalf * 1.45f : TrainTunnelHubHalf;
		Color hubColor = connections >= 3 ? station : tunnel;
		DrawWorldRectCentered(dst, res, center, hub * 2f, hub * 2f, hubColor);
		if (north)
			DrawWorldRect(dst, res, center.x - TrainTunnelHalfWidth, center.x + TrainTunnelHalfWidth, center.z, center.z + half, tunnel);
		if (south)
			DrawWorldRect(dst, res, center.x - TrainTunnelHalfWidth, center.x + TrainTunnelHalfWidth, center.z - half, center.z, tunnel);
		if (east)
			DrawWorldRect(dst, res, center.x, center.x + half, center.z - TrainTunnelHalfWidth, center.z + TrainTunnelHalfWidth, tunnel);
		if (west)
			DrawWorldRect(dst, res, center.x - half, center.x, center.z - TrainTunnelHalfWidth, center.z + TrainTunnelHalfWidth, tunnel);
	}

	private static Vector3 GetTunnelCellCenter(Vector3 position, float cellSize)
	{
		Vector3 origin = WorldSpaceGrid.ClosestGridCell(position, TerrainMeta.Size.x, cellSize, WorldSpaceGrid.RoundingMode.Down);
		return origin + new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);
	}

	private void DrawLabLink(NativeArray<Color> dst, int res, DungeonBaseLink link, Color color)
	{
		List<DungeonVolume> volumes = link.Volumes;
		if (volumes != null && volumes.Count > 0)
		{
			for (int i = 0; i < volumes.Count; i++)
			{
				DungeonVolume volume = volumes[i];
				if (volume == null)
					continue;
				OBB obb = volume.GetBounds(link.transform.position, link.transform.rotation);
				DrawWorldRect(dst, res, obb.position.x - obb.extents.x, obb.position.x + obb.extents.x, obb.position.z - obb.extents.z, obb.position.z + obb.extents.z, color);
			}
			return;
		}

		DrawWorldRectCentered(dst, res, link.transform.position, 36f, 36f, color);
	}

	private void DrawWorldRectCentered(NativeArray<Color> dst, int res, Vector3 world, float sizeX, float sizeZ, Color color)
	{
		float hx = sizeX * 0.5f;
		float hz = sizeZ * 0.5f;
		DrawWorldRect(dst, res, world.x - hx, world.x + hx, world.z - hz, world.z + hz, color);
	}

	private void DrawWorldRect(NativeArray<Color> dst, int res, float minX, float maxX, float minZ, float maxZ, Color color)
	{
		WorldToPixel(new Vector3(minX, 0f, minZ), res, out int x0, out int y0);
		WorldToPixel(new Vector3(maxX, 0f, maxZ), res, out int x1, out int y1);
		int xmin = Mathf.Max(0, Mathf.Min(x0, x1));
		int xmax = Mathf.Min(res - 1, Mathf.Max(x0, x1));
		int ymin = Mathf.Max(0, Mathf.Min(y0, y1));
		int ymax = Mathf.Min(res - 1, Mathf.Max(y0, y1));
		if (xmax < xmin || ymax < ymin)
			return;
		for (int py = ymin; py <= ymax; py++)
		{
			int row = py * res;
			for (int px = xmin; px <= xmax; px++)
				dst[row + px] = color;
		}
	}

	private void WorldToPixel(Vector3 world, int res, out int px, out int py)
	{
		Vector3 n = _utility.NormalizePosition(world);
		px = Mathf.RoundToInt(n.x * (res - 1));
		py = Mathf.RoundToInt(n.z * (res - 1));
	}

	private void DrawMarker(NativeArray<Color> dst, int res, Vector3 world, Color bg, Color fg, string label, Color text)
	{
		if (!TryWorldToPixel(world, res, out int cx, out int cy))
			return;
		DrawFilledCircle(dst, res, cx, cy, 7, bg);
		DrawFilledCircle(dst, res, cx, cy, 4, fg);
		DrawMonumentLabel(dst, res, cx, cy, label, text);
	}

	private void DrawDeepSeaMarker(NativeArray<Color> dst, int res, Vector3 world, Color bg, Color fg, string label, Color text)
	{
		Bounds bounds = DeepSeaManager.DeepSeaBounds;
		int cx = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(bounds.min.x, bounds.max.x, world.x) * (res - 1)), 0, res - 1);
		int cy = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(bounds.min.z, bounds.max.z, world.z) * (res - 1)), 0, res - 1);
		DrawFilledCircle(dst, res, cx, cy, 7, bg);
		DrawFilledCircle(dst, res, cx, cy, 4, fg);
		DrawMonumentLabel(dst, res, cx, cy, label, text);
	}

	private void DrawMonumentLabel(NativeArray<Color> dst, int res, int cx, int cy, string label, Color text)
	{
		if (string.IsNullOrEmpty(label))
			return;
		int fontSize = Mathf.Max(8, _config.MonumentNameFontSize);
		ImageTextUtility.WriteTextToImage(_font, res, dst, new int2(cx, cy + fontSize), label, text, fontSize);
	}

	private bool TryWorldToPixel(Vector3 world, int res, out int px, out int py)
	{
		Vector3 n = _utility.NormalizePosition(world);
		px = Mathf.RoundToInt(n.x * (res - 1));
		py = Mathf.RoundToInt(n.z * (res - 1));
		if (px < 0 || px >= res || py < 0 || py >= res)
			return false;
		return true;
	}

	private static void DrawFilledCircle(NativeArray<Color> dst, int res, int cx, int cy, int radius, Color color)
	{
		int r2 = radius * radius;
		for (int dy = -radius; dy <= radius; dy++)
		{
			int py = cy + dy;
			if (py < 0 || py >= res) continue;
			for (int dx = -radius; dx <= radius; dx++)
			{
				if (dx * dx + dy * dy > r2) continue;
				int px = cx + dx;
				if (px < 0 || px >= res) continue;
				int index = py * res + px;
				Color existing = dst[index];
				float a = color.a;
				dst[index] = new Color(
					existing.r * (1f - a) + color.r * a,
					existing.g * (1f - a) + color.g * a,
					existing.b * (1f - a) + color.b * a,
					Mathf.Max(existing.a, a));
			}
		}
	}
}
