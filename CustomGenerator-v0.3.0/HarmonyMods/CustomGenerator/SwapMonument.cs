using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomGenerator;
using CustomGenerator.Custom;
using CustomGenerator.Shared;
using CustomGenerator.Utility;
using ProtoBuf;
using UnityEngine;

public class SwapMonument
{
	private class Monument
	{
		public string prefabShortname;

		public string path;

		public Monument(string prefabShortname, string path)
		{
			this.prefabShortname = prefabShortname;
			this.path = path;
		}
	}

	private static readonly string Folder = Paths.Get("maps", "prefabs");

	public static void Initiate(string path)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected O, but got Unknown
		WorldSerialization val = new WorldSerialization();
		val.Load(path);
		if (val.world?.prefabs == null || val.world.prefabs.Count == 0)
		{
			Logging.Error("Swap: failed to load the saved map " + path + ", swap skipped");
			return;
		}
		List<Monument> list = LoadMonuments();
		if (list.Count == 0)
		{
			Logging.Warning("Swap: no .map files in " + Path.GetFullPath(Folder) + ", nothing to swap");
			return;
		}
		List<IOEntityInfo> io = new List<IOEntityInfo>();
		int num = SwapMonuments(val, list, io);
		MapExtrasWriter.Write(val, io, "Swap");
		string text = (ExtConfig.Config.Swap.SaveBothMaps ? Path.ChangeExtension(path, ".swapped.map") : path);
		val.Save(text);
		Logging.Info($"Swap: {num} monuments replaced, saved to {text}");
		GenerationReport.SwapSaved(text);
	}

	private static int SwapMonuments(WorldSerialization mainMap, List<Monument> files, List<IOEntityInfo> io)
	{
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Expected O, but got Unknown
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		List<PrefabData> source = mainMap.world.prefabs.ToList();
		int num = 0;
		foreach (Monument monument in files)
		{
			string fileName = Path.GetFileName(monument.path);
			List<PrefabData> list = source.Where((PrefabData x) => (StringPool.Get(x.id) ?? "").ToLowerInvariant().Contains(monument.prefabShortname)).ToList();
			if (list.Count == 0)
			{
				Logging.Warning("Swap: " + fileName + ": no '" + monument.prefabShortname + "' on the map, skipped (is the file named <vanilla prefab>.prefab.map?)");
				GenerationReport.Swap(fileName, 0, "no such monument on the map");
				continue;
			}
			WorldSerialization val = new WorldSerialization();
			try
			{
				val.Load(monument.path);
			}
			catch (Exception ex)
			{
				Logging.Error("Swap: " + fileName + ": failed to load", ex);
				GenerationReport.Swap(fileName, 0, "failed to load the file");
				continue;
			}
			if (val.world?.prefabs == null || val.world.prefabs.Count == 0)
			{
				Logging.Error("Swap: " + fileName + ": no prefabs in the file");
				GenerationReport.Swap(fileName, 0, "no prefabs in the file");
				continue;
			}
			List<IOEntityInfo> list2 = MapExtrasWriter.ReadRustEditIO(val, fileName);
			VectorData anchor = val.world.prefabs[0].position;
			foreach (PrefabData prefab in list)
			{
				mainMap.world.prefabs.Remove(prefab);
				mainMap.world.prefabs.AddRange(MapHandler.CreatePrefabFromMap(prefab.position, prefab.rotation, val.world.prefabs));
				foreach (IOEntityInfo item in list2)
				{
					io.Add(MapExtrasWriter.Transform(item, (Vector3 point) => MapHandler.TransformPoint(prefab.position, anchor, prefab.rotation, point)));
				}
			}
			num += list.Count;
			Logging.Info($"Swap: {fileName}: replaced {list.Count} x '{monument.prefabShortname}' ({val.world.prefabs.Count} prefabs each" + ((list2.Count > 0) ? $", {list2.Count} IO entities" : "") + ")");
			GenerationReport.Swap(fileName, list.Count);
		}
		return num;
	}

	private static List<Monument> LoadMonuments()
	{
		if (!Directory.Exists(Folder))
		{
			Directory.CreateDirectory(Folder);
		}
		return (from file in Directory.GetFiles(Folder)
			where file.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
			select new Monument(Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), file)).ToList();
	}
}
