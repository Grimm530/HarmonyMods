using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using CustomGenerator.Shared;
using CustomGenerator.Utility;
using Newtonsoft.Json;
using ProtoBuf;
using UnityEngine;

namespace CustomGenerator.Custom;

internal static class MapExtrasWriter
{
	private static readonly string[] TerrainLayers = new string[8] { "terrain", "height", "splat", "biome", "alpha", "topology", "water", "buildingblocks" };

	public static readonly List<IOEntityInfo> GeneratedIO = new List<IOEntityInfo>();

	public static List<IOEntityInfo> ReadRustEditIO(WorldSerialization world, string source)
	{
		List<IOEntityInfo> list = new List<IOEntityInfo>();
		foreach (MapData item in world.world.maps ?? new List<MapData>())
		{
			if (TerrainLayers.Contains(item.name) || item.name == "customgenerator" || !IsXml(item.data))
			{
				continue;
			}
			try
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.LoadXml(Encoding.UTF8.GetString(item.data).TrimStart('\ufeff'));
				if (xmlDocument.DocumentElement?.Name != "SerializedIOData")
				{
					continue;
				}
				foreach (XmlElement item2 in xmlDocument.DocumentElement.SelectNodes("entities/SerializedIOEntity"))
				{
					list.Add(ReadEntity(item2));
				}
			}
			catch (Exception ex)
			{
				Logging.Warning(source + ": can't read RustEdit layer " + item.name + ": " + ex.Message);
			}
		}
		return list;
	}

	private static bool IsXml(byte[] data)
	{
		if (data != null && data.Length > 5 && data[0] == 60)
		{
			return data[1] == 63;
		}
		return false;
	}

	private static IOEntityInfo ReadEntity(XmlElement e)
	{
		return new IOEntityInfo
		{
			Prefab = Text(e, "fullPath"),
			Position = ReadVector(e["position"]),
			Inputs = ReadConnections(e["inputs"]),
			Outputs = ReadConnections(e["outputs"]),
			AccessLevel = Int(e, "accessLevel"),
			DoorEffect = Int(e, "doorEffect", -1),
			TimerLength = Float(e, "timerLength"),
			Frequency = Int(e, "frequency"),
			UnlimitedAmmo = Bool(e, "unlimitedAmmo"),
			PeaceKeeper = Bool(e, "peaceKeeper"),
			AutoTurretWeapon = Text(e, "autoTurretWeapon"),
			BranchAmount = Int(e, "branchAmount"),
			TargetCounterNumber = Int(e, "targetCounterNumber"),
			RcIdentifier = Text(e, "rcIdentifier"),
			CounterPassthrough = Bool(e, "counterPassthrough"),
			Floors = Int(e, "floors", 1),
			PhoneName = Text(e, "phoneName")
		};
	}

	private static List<IOConnectionInfo> ReadConnections(XmlElement list)
	{
		List<IOConnectionInfo> list2 = new List<IOConnectionInfo>();
		if (list == null)
		{
			return list2;
		}
		foreach (XmlElement item in list.ChildNodes.OfType<XmlElement>())
		{
			string text = Text(item, "fullPath");
			list2.Add(string.IsNullOrEmpty(text) ? null : new IOConnectionInfo
			{
				Prefab = text,
				Position = ReadVector(item["position"]),
				Slot = Int(item, "connectedTo"),
				Type = Int(item, "type")
			});
		}
		return list2;
	}

	private static string Text(XmlElement e, string name)
	{
		string text = e[name]?.InnerText;
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		return null;
	}

	private static int Int(XmlElement e, string name, int fallback = 0)
	{
		if (!int.TryParse(Text(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static float Float(XmlElement e, string name)
	{
		if (!float.TryParse(Text(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0f;
		}
		return result;
	}

	private static bool Bool(XmlElement e, string name)
	{
		return Text(e, name) == "true";
	}

	private static float[] ReadVector(XmlElement v)
	{
		if (v != null)
		{
			return new float[3]
			{
				Float(v, "x"),
				Float(v, "y"),
				Float(v, "z")
			};
		}
		return new float[3];
	}

	public static IOEntityInfo Transform(IOEntityInfo source, Func<Vector3, Vector3> transform)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		IOEntityInfo iOEntityInfo = JsonConvert.DeserializeObject<IOEntityInfo>(JsonConvert.SerializeObject((object)source));
		iOEntityInfo.Position = ToArray(transform(ToVector(source.Position)));
		iOEntityInfo.Inputs = source.Inputs.Select((IOConnectionInfo c) => MapConnection(c, transform)).ToList();
		iOEntityInfo.Outputs = source.Outputs.Select((IOConnectionInfo c) => MapConnection(c, transform)).ToList();
		return iOEntityInfo;
	}

	private static IOConnectionInfo MapConnection(IOConnectionInfo c, Func<Vector3, Vector3> transform)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		if (c != null)
		{
			return new IOConnectionInfo
			{
				Prefab = c.Prefab,
				Position = ToArray(transform(ToVector(c.Position))),
				Slot = c.Slot,
				Type = c.Type
			};
		}
		return null;
	}

	public static Vector3 ToVector(float[] v)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		if (v != null && v.Length >= 3)
		{
			return new Vector3(v[0], v[1], v[2]);
		}
		return Vector3.zero;
	}

	public static float[] ToArray(Vector3 v)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		return new float[3] { v.x, v.y, v.z };
	}

	public static void Write(WorldSerialization world, IEnumerable<IOEntityInfo> io, string what)
	{
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Expected O, but got Unknown
		List<IOEntityInfo> list = io.ToList();
		if (list.Count != 0)
		{
			MapData val = world.world.maps.FirstOrDefault((MapData x) => x.name == "customgenerator");
			MapExtras mapExtras;
			try
			{
				mapExtras = MapExtras.FromBytes(val?.data);
			}
			catch (Exception ex)
			{
				Logging.Warning("Map extras layer is broken, rewriting it: " + ex.Message);
				mapExtras = new MapExtras();
			}
			mapExtras.IO.AddRange(list);
			if (val != null)
			{
				world.world.maps.Remove(val);
			}
			world.world.maps.Add(new MapData
			{
				name = "customgenerator",
				data = mapExtras.ToBytes()
			});
			Logging.Info($"{what}: {list.Count} IO entities saved to the map for CustomGenerator.Server ({mapExtras.IO.Count} in total)");
		}
	}
}
