using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomGenerator.Shared;
using CustomGenerator.Utility;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Custom;

internal static class CustomMonumentPlacer
{
	private struct Placed
	{
		public string Name;

		public Vector3 Position;

		public float Radius;
	}

	private const int BlockedTopology = 871552;

	private const int KeptTopology = 2080374784;

	private const int Attempts = 20000;

	private const int Candidates = 16;

	private static readonly FieldRef<TerrainPath, List<MonumentInfo>> _monuments = AccessTools.FieldRefAccess<TerrainPath, List<MonumentInfo>>("Monuments");

	private static bool _done;

	public static void Run(uint seed, string after)
	{
		//IL_01da: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0208: Unknown result type (might be due to invalid IL or missing references)
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0259: Unknown result type (might be due to invalid IL or missing references)
		//IL_0268: Unknown result type (might be due to invalid IL or missing references)
		ExtConfig.CustomMonumentSettings customMonuments = ExtConfig.Config.CustomMonuments;
		if (_done || World.Networked || customMonuments == null || !customMonuments.Enabled)
		{
			return;
		}
		_done = true;
		if (!customMonuments.List.Any((ExtConfig.CustomMonument x) => x.Enabled && x.Count > 0))
		{
			return;
		}
		Logging.Generation("Custom monuments: placing after '" + after + "'");
		List<Placed> list = new List<Placed>();
		foreach (ExtConfig.CustomMonument item in customMonuments.List.Where((ExtConfig.CustomMonument x) => x.Enabled && x.Count > 0))
		{
			string text = (string.IsNullOrEmpty(item.Name) ? Path.GetFileNameWithoutExtension(item.File) : item.Name);
			CustomMonumentData customMonumentData;
			try
			{
				customMonumentData = CustomMonumentData.Load(Path.Combine(Paths.Resolve(customMonuments.Folder), item.File));
			}
			catch (Exception ex)
			{
				Logging.Error("Custom monument '" + text + "': failed to load " + item.File, ex);
				GenerationReport.CustomMonument(text, 0, item.Count, "failed to load the file");
				continue;
			}
			if (customMonumentData.UnknownPrefabs > 0)
			{
				Logging.Warning($"Custom monument '{text}': {customMonumentData.UnknownPrefabs} prefabs are unknown to this Rust version and were skipped");
			}
			if (customMonumentData.BrokenPrefabs > 0)
			{
				Logging.Warning($"Custom monument '{text}': {customMonumentData.BrokenPrefabs} prefabs have NaN/infinite position, rotation or scale in the file and were skipped");
			}
			float num = ((item.Radius > 0f) ? item.Radius : customMonumentData.AutoRadius);
			int num2 = 0;
			for (int num3 = 0; num3 < item.Count; num3++)
			{
				if (!TryFindSpot(item, text, num, list, ref seed, out var best, out var bestHeight))
				{
					break;
				}
				float num4 = (item.RandomRotation ? SeedRandom.Range(ref seed, 0f, 360f) : 0f);
				Stamp(item, customMonumentData, best, bestHeight, num4, num);
				WritePrefabs(customMonumentData, best, bestHeight, num4);
				list.Add(new Placed
				{
					Name = text,
					Position = best,
					Radius = num
				});
				ExtConfig.tempData.customMonuments.Add(new KeyValuePair<string, Vector3>(text, best));
				num2++;
				Logging.Generation($"Custom monument '{text}' #{num2}: {best.x:0}, {best.z:0} (height {bestHeight:0.0}, rotation {num4:0})");
			}
			Logging.Generation($"Custom monument '{text}': placed {num2}/{item.Count}, radius {num:0}m, {customMonumentData.Items.Count} prefabs each" + ((customMonumentData.IO.Count > 0) ? $", {customMonumentData.IO.Count} IO entities each" : ""));
			GenerationReport.CustomMonument(text, num2, item.Count, (num2 < item.Count) ? "no room for the rest, see the log" : null);
		}
		MapExtrasWriter.Write(World.Serialization, MapExtrasWriter.GeneratedIO, "Custom monuments");
	}

	private static bool TryFindSpot(ExtConfig.CustomMonument cfg, string name, float radius, List<Placed> placed, ref uint seed, out Vector3 best, out float bestHeight)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		//IL_0322: Unknown result type (might be due to invalid IL or missing references)
		//IL_0327: Unknown result type (might be due to invalid IL or missing references)
		//IL_023c: Unknown result type (might be due to invalid IL or missing references)
		//IL_025a: Unknown result type (might be due to invalid IL or missing references)
		//IL_025f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0267: Unknown result type (might be due to invalid IL or missing references)
		//IL_0290: Unknown result type (might be due to invalid IL or missing references)
		best = Vector3.zero;
		bestHeight = 0f;
		TerrainHeightMap heightMap = TerrainMeta.HeightMap;
		TerrainTopologyMap topologyMap = TerrainMeta.TopologyMap;
		List<MonumentInfo> source = _monuments.Invoke(TerrainMeta.Path);
		float outer = radius + cfg.Blend;
		float num = outer + 50f;
		Vector3 position = TerrainMeta.Position;
		Vector3 size = TerrainMeta.Size;
		if (size.x <= num * 2f || size.z <= num * 2f)
		{
			return false;
		}
		int num2 = MaskOf(cfg.Filter, "TopologyNot");
		float num3 = float.MaxValue;
		int num4 = 0;
		for (int i = 0; i < 20000; i++)
		{
			if (num4 >= 16)
			{
				break;
			}
			Vector3 center = new Vector3(SeedRandom.Range(ref seed, position.x + num, position.x + size.x - num), 0f, SeedRandom.Range(ref seed, position.z + num, position.z + size.z - num));
			float height = heightMap.GetHeight(center);
			if (height < cfg.MinHeight || height > cfg.MaxHeight || !FilterAllows(cfg.Filter, center) || source.Any((MonumentInfo m) => (Object)(object)m != (Object)null && Distance2D(((Component)m).transform.position, center) < (float)cfg.MinDistanceToMonuments + radius) || placed.Any((Placed p) => Distance2D(p.Position, center) < p.Radius + outer || Distance2D(p.Position, center) < (float)((p.Name == name) ? cfg.MinDistanceSameType : cfg.MinDistanceToMonuments)))
			{
				continue;
			}
			float num5 = float.MaxValue;
			float num6 = float.MinValue;
			float num7 = 0f;
			int num8 = 0;
			bool flag = false;
			for (int num9 = 0; num9 <= 4; num9++)
			{
				if (flag)
				{
					break;
				}
				float num10 = ((num9 == 4) ? outer : (radius * (float)num9 / 3f));
				int num11 = ((num9 == 0) ? 1 : 12);
				for (int num12 = 0; num12 < num11; num12++)
				{
					float num13 = (float)num12 * (float)Math.PI * 2f / (float)num11;
					Vector3 worldPos = center + new Vector3(Mathf.Cos(num13) * num10, 0f, Mathf.Sin(num13) * num10);
					if ((topologyMap.GetTopology(worldPos) & ((num9 == 4) ? 17536 : (0xD4C80 | num2))) != 0)
					{
						flag = true;
						break;
					}
					if (num9 != 4)
					{
						float height2 = heightMap.GetHeight(worldPos);
						if (height2 < 0.5f)
						{
							flag = true;
							break;
						}
						num5 = Mathf.Min(num5, height2);
						num6 = Mathf.Max(num6, height2);
						num7 += height2;
						num8++;
					}
				}
			}
			if (!flag && !(num6 - num5 > cfg.MaxHeightDifference))
			{
				num4++;
				float num14 = num6 - num5;
				if (num14 < num3)
				{
					num3 = num14;
					best = center;
					bestHeight = num7 / (float)num8;
				}
			}
		}
		if (num4 == 0)
		{
			Logging.Warning("Custom monument '" + name + "': no suitable spot found (try lowering distances, MaxHeightDifference limits or the filter)");
		}
		best.y = bestHeight;
		return num4 > 0;
	}

	private static void Stamp(ExtConfig.CustomMonument cfg, CustomMonumentData data, Vector3 center, float baseHeight, float angle, float radius)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Unknown result type (might be due to invalid IL or missing references)
		float radius2 = radius + cfg.Blend;
		Quaternion inverse = Quaternion.Inverse(Quaternion.Euler(0f, angle, 0f));
		_ = TerrainMeta.Position;
		_ = TerrainMeta.Size;
		if (cfg.HeightMode != "None")
		{
			TerrainHeightMap map = TerrainMeta.HeightMap;
			int num = Res(map);
			bool stamp = cfg.HeightMode == "Stamp" && data.HasHeights;
			ForEachPixel(center, radius2, num, num - 1, delegate(int x, int z, Vector3 world, float distance)
			{
				//IL_0025: Unknown result type (might be due to invalid IL or missing references)
				//IL_0026: Unknown result type (might be due to invalid IL or missing references)
				float num5 = baseHeight;
				if (stamp)
				{
					float num6 = data.SourceHeight(Local(world));
					if (!float.IsNaN(num6))
					{
						num5 = baseHeight + (num6 - data.AnchorGround);
					}
				}
				map.SetHeight(x, z, TerrainMeta.NormalizeY(Mathf.Lerp(map.GetHeight(x, z), num5, Weight(distance))));
			});
		}
		if (cfg.CopySplat && data.HasSplat)
		{
			TerrainSplatMap map2 = TerrainMeta.SplatMap;
			int num2 = Res(map2);
			ForEachPixel(center, radius2, num2, num2, delegate(int x, int z, Vector3 world, float distance)
			{
				//IL_0011: Unknown result type (might be due to invalid IL or missing references)
				//IL_0012: Unknown result type (might be due to invalid IL or missing references)
				//IL_002a: Unknown result type (might be due to invalid IL or missing references)
				//IL_002b: Unknown result type (might be due to invalid IL or missing references)
				if (data.SourceSplat(Local(world), out var first, out var second))
				{
					map2.SetSplatRaw(x, z, first, second, Weight(distance));
				}
			});
		}
		if (cfg.CopyAlpha && data.HasAlpha)
		{
			TerrainAlphaMap map3 = TerrainMeta.AlphaMap;
			int num3 = Res(map3);
			ForEachPixel(center, radius2, num3, num3, delegate(int x, int z, Vector3 world, float distance)
			{
				//IL_0011: Unknown result type (might be due to invalid IL or missing references)
				//IL_0012: Unknown result type (might be due to invalid IL or missing references)
				float num5 = data.SourceAlpha(Local(world));
				if (!float.IsNaN(num5))
				{
					map3.SetAlpha(x, z, Mathf.Lerp(map3.GetAlpha(x, z), num5, Weight(distance)));
				}
			});
		}
		TerrainTopologyMap topologyMap = TerrainMeta.TopologyMap;
		int num4 = Res(topologyMap);
		ForEachPixel(center, radius, num4, num4, delegate(int x, int z, Vector3 world, float distance)
		{
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			int num5 = topologyMap.GetTopology(x, z);
			if (cfg.CopyTopology && data.HasTopology)
			{
				num5 = (num5 & 0x7C000000) | (data.SourceTopology(Local(world)) & -2080374785);
			}
			topologyMap.SetTopology(x, z, num5 | 0x400);
		});
		Vector2 Local(Vector3 world)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			Vector3 val = inverse * (world - center);
			return new Vector2(val.x, val.z);
		}
		float Weight(float distance)
		{
			if (!(distance <= radius))
			{
				if (!(cfg.Blend <= 0f))
				{
					return Mathf.SmoothStep(0f, 1f, 1f - (distance - radius) / cfg.Blend);
				}
				return 0f;
			}
			return 1f;
		}
	}

	private static void WritePrefabs(CustomMonumentData data, Vector3 center, float baseHeight, float angle)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
		Vector3 val2 = default(Vector3);
		foreach (CustomMonumentData.Item item in data.Items)
		{
			Vector3 val = rotation * new Vector3(item.Local.x, 0f, item.Local.z);
			((Vector3)(ref val2))._002Ector(center.x + val.x, baseHeight + item.Local.y, center.z + val.z);
			World.Serialization.AddPrefab(item.Category, item.Id, val2, rotation * item.Rotation, item.Scale);
		}
		foreach (IOEntityInfo item2 in data.IO)
		{
			MapExtrasWriter.GeneratedIO.Add(MapExtrasWriter.Transform(item2, delegate(Vector3 local)
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				//IL_0011: Unknown result type (might be due to invalid IL or missing references)
				//IL_0017: Unknown result type (might be due to invalid IL or missing references)
				//IL_001c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0021: Unknown result type (might be due to invalid IL or missing references)
				//IL_002d: Unknown result type (might be due to invalid IL or missing references)
				//IL_003a: Unknown result type (might be due to invalid IL or missing references)
				//IL_004c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0053: Unknown result type (might be due to invalid IL or missing references)
				Vector3 val3 = rotation * new Vector3(local.x, 0f, local.z);
				return new Vector3(center.x + val3.x, baseHeight + local.y, center.z + val3.z);
			}));
		}
	}

	private static void ForEachPixel(Vector3 center, float radius, int res, int cells, Action<int, int, Vector3, float> action)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		Vector3 position = TerrainMeta.Position;
		Vector3 size = TerrainMeta.Size;
		float num = ((cells == res) ? 0.5f : 0f);
		int num2 = Mathf.Clamp(Mathf.FloorToInt((center.x - radius - position.x) / size.x * (float)cells), 0, res - 1);
		int num3 = Mathf.Clamp(Mathf.CeilToInt((center.x + radius - position.x) / size.x * (float)cells), 0, res - 1);
		int num4 = Mathf.Clamp(Mathf.FloorToInt((center.z - radius - position.z) / size.z * (float)cells), 0, res - 1);
		int num5 = Mathf.Clamp(Mathf.CeilToInt((center.z + radius - position.z) / size.z * (float)cells), 0, res - 1);
		Vector3 val = default(Vector3);
		for (int i = num4; i <= num5; i++)
		{
			for (int j = num2; j <= num3; j++)
			{
				((Vector3)(ref val))._002Ector(position.x + ((float)j + num) / (float)cells * size.x, 0f, position.z + ((float)i + num) / (float)cells * size.z);
				float num6 = Distance2D(val, center);
				if (num6 <= radius)
				{
					action(j, i, val, num6);
				}
			}
		}
	}

	private static bool FilterAllows(ExtConfig.SpawnFilterCfg filter, Vector3 position)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		if (filter == null || !filter.Enabled)
		{
			return true;
		}
		if (filter.SplatType.Count > 0 && (TerrainMeta.SplatMap.GetSplatMaxType(position) & MaskOf(filter, "SplatType")) == 0)
		{
			return false;
		}
		if (filter.BiomeType.Count > 0 && (TerrainMeta.BiomeMap.GetBiomeMaxType(position) & MaskOf(filter, "BiomeType")) == 0)
		{
			return false;
		}
		int topology = TerrainMeta.TopologyMap.GetTopology(position);
		if (filter.TopologyAny.Count > 0 && (topology & MaskOf(filter, "TopologyAny")) == 0)
		{
			return false;
		}
		int num = MaskOf(filter, "TopologyAll");
		if (filter.TopologyAll.Count > 0 && (topology & num) != num)
		{
			return false;
		}
		return true;
	}

	private static int MaskOf(ExtConfig.SpawnFilterCfg filter, string field)
	{
		if (filter == null || !filter.Enabled)
		{
			return 0;
		}
		List<string> list = field switch
		{
			"SplatType" => filter.SplatType, 
			"BiomeType" => filter.BiomeType, 
			"TopologyAny" => filter.TopologyAny, 
			"TopologyAll" => filter.TopologyAll, 
			_ => filter.TopologyNot, 
		};
		if (list.Count != 0)
		{
			return Convert.ToInt32(EnumParser.GetFilterEnum(field, list));
		}
		return 0;
	}

	private static int Res(object map)
	{
		return (int)AccessTools.Field(map.GetType(), "res").GetValue(map);
	}

	private static float Distance2D(Vector3 a, Vector3 b)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = new Vector2(a.x - b.x, a.z - b.z);
		return ((Vector2)(ref val)).magnitude;
	}
}
