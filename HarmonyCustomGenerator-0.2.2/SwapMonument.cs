using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomGenerator;
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

	private static WorldSerialization _mainMap = new WorldSerialization();

	private static WorldSerialization _swapMap = new WorldSerialization();

	private static List<Monument> monuments = new List<Monument>();

	private static string mapPath = string.Empty;

	public static void Initiate(string path)
	{
		mapPath = path;
		_mainMap.Load(mapPath);
		Log(_mainMap.world.prefabs.Count);
		LoadMonuments();
		SwapMonuments();
		if (!ExtConfig.Config.Swap.SaveBothMaps)
		{
			_mainMap.Save(mapPath);
		}
		else
		{
			_mainMap.Save(mapPath.Replace(".map", ".swapped.map"));
		}
	}

	private static void SwapMonuments()
	{
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		foreach (Monument monument in monuments)
		{
			List<PrefabData> list = _mainMap.world.prefabs.Where((PrefabData x) => StringPool.Get(x.id).Contains(monument.prefabShortname)).ToList();
			if (list.Count() == 0)
			{
				continue;
			}
			foreach (PrefabData item in list)
			{
				_swapMap.Load(monument.path);
				_mainMap.world.prefabs.Remove(item);
				_mainMap.world.prefabs.AddRange(MapHander.CreatePrefabFromMap(item.position, item.rotation, _swapMap.world.prefabs));
			}
		}
	}

	private static void LoadMonuments()
	{
		if (!Directory.Exists("maps/prefabs"))
		{
			Directory.CreateDirectory("maps/prefabs");
		}
		string[] files = Directory.GetFiles("maps/prefabs");
		foreach (string path in files)
		{
			if (Path.GetFileName(path).EndsWith(".map"))
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
				monuments.Add(new Monument(fileNameWithoutExtension, path));
			}
		}
	}

	private static void Log(object obj)
	{
		Debug.Log((object)("[SWAP MN] " + obj));
	}
}
