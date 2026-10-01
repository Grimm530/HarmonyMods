using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using CustomGenerator.Shared;
using LZ4;
using ProtoBuf;
using UnityEngine;

namespace CustomGenerator.Custom;

internal sealed class CustomMonumentData
{
	public struct Item
	{
		public string Category;

		public uint Id;

		public Vector3 Local;

		public Quaternion Rotation;

		public Vector3 Scale;
	}

	private sealed class PrefabTerrain
	{
		private const float HeightUnit = 0.03051851f;

		public float Size;

		public Grid<float> Heights;

		public Grid<float> Alpha;

		public Grid<Vector4>[] Splat;

		public Grid<int> Topology;

		public static PrefabTerrain Load(string path, float size)
		{
			PrefabTerrain prefabTerrain = new PrefabTerrain
			{
				Size = size
			};
			prefabTerrain.Heights = Png.Read(path + ".heights", (byte[] p) => (float)(short)(p[0] | (p[2] << 8)) * 0.03051851f);
			prefabTerrain.Alpha = Png.Read(path + ".alpha", (byte[] p) => (float)(int)p[3] / 255f);
			prefabTerrain.Topology = Png.Read(path + ".topology", (byte[] p) => p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24));
			Grid<Vector4> grid = Png.Read(path + ".splat0", ToVector);
			Grid<Vector4> grid2 = Png.Read(path + ".splat1", ToVector);
			if (grid != null && grid2 != null)
			{
				prefabTerrain.Splat = new Grid<Vector4>[2] { grid, grid2 };
			}
			return prefabTerrain;
			static Vector4 ToVector(byte[] p)
			{
				//IL_0010: Unknown result type (might be due to invalid IL or missing references)
				//IL_001a: Unknown result type (might be due to invalid IL or missing references)
				return new Vector4((float)(int)p[0], (float)(int)p[1], (float)(int)p[2], (float)(int)p[3]) / 255f;
			}
		}

		public bool ToPixel<T>(Grid<T> grid, Vector2 offset, out float x, out float y)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			float num = offset.x / Size + 0.5f;
			float num2 = offset.y / Size + 0.5f;
			x = num * (float)grid.Width - 0.5f;
			y = (1f - num2) * (float)grid.Height - 0.5f;
			if (num >= 0f && num <= 1f && num2 >= 0f)
			{
				return num2 <= 1f;
			}
			return false;
		}
	}

	private sealed class Grid<T>
	{
		public int Width;

		public int Height;

		public T[] Values;

		public T this[int x, int y] => Values[Mathf.Clamp(y, 0, Height - 1) * Width + Mathf.Clamp(x, 0, Width - 1)];
	}

	private static class Png
	{
		public static Grid<T> Read<T>(string path, Func<byte[], T> convert)
		{
			if (!File.Exists(path))
			{
				return null;
			}
			byte[] array = File.ReadAllBytes(path);
			int num = 0;
			int num2 = 0;
			int i = 8;
			using MemoryStream memoryStream = new MemoryStream();
			int num3;
			for (; i + 8 <= array.Length; i += 12 + num3)
			{
				num3 = (array[i] << 24) | (array[i + 1] << 16) | (array[i + 2] << 8) | array[i + 3];
				string text = Encoding.ASCII.GetString(array, i + 4, 4);
				if (text == "IHDR")
				{
					num = (array[i + 8] << 24) | (array[i + 9] << 16) | (array[i + 10] << 8) | array[i + 11];
					num2 = (array[i + 12] << 24) | (array[i + 13] << 16) | (array[i + 14] << 8) | array[i + 15];
					if (array[i + 16] != 8 || array[i + 17] != 6)
					{
						throw new InvalidDataException(path + ": only 8-bit RGBA PNG is supported");
					}
				}
				else if (text == "IDAT")
				{
					memoryStream.Write(array, i + 8, num3);
				}
			}
			memoryStream.Position = 2L;
			byte[] array2;
			using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Decompress))
			{
				using MemoryStream memoryStream2 = new MemoryStream();
				deflateStream.CopyTo(memoryStream2);
				array2 = memoryStream2.ToArray();
			}
			int num4 = num * 4;
			int num5 = 0;
			byte[] array3 = new byte[num4];
			Grid<T> grid = new Grid<T>
			{
				Width = num,
				Height = num2,
				Values = new T[num * num2]
			};
			byte[] array4 = new byte[4];
			for (int j = 0; j < num2; j++)
			{
				byte b = array2[num5++];
				byte[] array5 = new byte[num4];
				Buffer.BlockCopy(array2, num5, array5, 0, num4);
				num5 += num4;
				for (int k = 0; k < num4; k++)
				{
					int num6 = ((k >= 4) ? array5[k - 4] : 0);
					int num7 = array3[k];
					int num8 = ((k >= 4) ? array3[k - 4] : 0);
					switch (b)
					{
					case 1:
						array5[k] = (byte)(array5[k] + num6);
						break;
					case 2:
						array5[k] = (byte)(array5[k] + num7);
						break;
					case 3:
						array5[k] = (byte)(array5[k] + (num6 + num7) / 2);
						break;
					case 4:
					{
						int num9 = num6 + num7 - num8;
						int num10 = Math.Abs(num9 - num6);
						int num11 = Math.Abs(num9 - num7);
						int num12 = Math.Abs(num9 - num8);
						array5[k] = (byte)(array5[k] + ((num10 <= num11 && num10 <= num12) ? num6 : ((num11 <= num12) ? num7 : num8)));
						break;
					}
					}
				}
				for (int l = 0; l < num; l++)
				{
					Buffer.BlockCopy(array5, l * 4, array4, 0, 4);
					grid.Values[j * num + l] = convert(array4);
				}
				array3 = array5;
			}
			return grid;
		}
	}

	private struct ProtoReader
	{
		private readonly byte[] _buffer;

		private int _position;

		private readonly int _end;

		public ProtoReader(byte[] buffer, int start, int end)
		{
			_buffer = buffer;
			_position = start;
			_end = end;
		}

		public bool Next(out int field, out int wire)
		{
			field = (wire = 0);
			if (_position >= _end)
			{
				return false;
			}
			ulong num = Varint();
			field = (int)(num >> 3);
			wire = (int)(num & 7);
			return true;
		}

		public ulong Varint()
		{
			ulong num = 0uL;
			for (int i = 0; i < 64; i += 7)
			{
				byte b = _buffer[_position++];
				num |= (ulong)((long)(b & 0x7F) << i);
				if ((b & 0x80) == 0)
				{
					break;
				}
			}
			return num;
		}

		public float Fixed32()
		{
			float result = BitConverter.ToSingle(_buffer, _position);
			_position += 4;
			return result;
		}

		public ProtoReader Message()
		{
			int num = (int)Varint();
			ProtoReader result = new ProtoReader(_buffer, _position, _position + num);
			_position += num;
			return result;
		}

		public string String()
		{
			int num = (int)Varint();
			string result = Encoding.UTF8.GetString(_buffer, _position, num);
			_position += num;
			return result;
		}

		public Vector3 Vector(Vector3 value)
		{
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			int field;
			int wire;
			while (Next(out field, out wire))
			{
				if (wire != 5)
				{
					Skip(wire);
					continue;
				}
				float num = Fixed32();
				switch (field)
				{
				case 1:
					value.x = num;
					break;
				case 2:
					value.y = num;
					break;
				case 3:
					value.z = num;
					break;
				}
			}
			return value;
		}

		public void Skip(int wire)
		{
			switch (wire)
			{
			case 0:
				Varint();
				break;
			case 1:
				_position += 8;
				break;
			case 2:
			{
				int num = (int)Varint();
				_position += num;
				break;
			}
			case 5:
				_position += 4;
				break;
			default:
				throw new InvalidDataException($"Unsupported protobuf wire type {wire}");
			}
		}
	}

	public const uint SpawnPointId = 2749405185u;

	private const float HeightOffset = -500f;

	private const float HeightScale = 1000f;

	public readonly List<Item> Items = new List<Item>();

	public readonly List<IOEntityInfo> IO = new List<IOEntityInfo>();

	public float AutoRadius;

	public int UnknownPrefabs;

	public int BrokenPrefabs;

	private float _size;

	private Vector3 _anchorPos;

	private Quaternion _anchorRot;

	private int _heightRes;

	private int _splatRes;

	private int _topologyRes;

	private int _alphaRes;

	private short[] _heights;

	private byte[] _splat;

	private int[] _topology;

	private byte[] _alpha;

	private PrefabTerrain _prefabTerrain;

	public bool HasHeights
	{
		get
		{
			if (_heights == null)
			{
				return _prefabTerrain?.Heights != null;
			}
			return true;
		}
	}

	public bool HasSplat
	{
		get
		{
			if (_splat == null)
			{
				return _prefabTerrain?.Splat != null;
			}
			return true;
		}
	}

	public bool HasTopology
	{
		get
		{
			if (_topology == null)
			{
				return _prefabTerrain?.Topology != null;
			}
			return true;
		}
	}

	public bool HasAlpha
	{
		get
		{
			if (_alpha == null)
			{
				return _prefabTerrain?.Alpha != null;
			}
			return true;
		}
	}

	public float AnchorGround { get; private set; }

	public static CustomMonumentData Load(string path)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0201: Unknown result type (might be due to invalid IL or missing references)
		//IL_0206: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0274: Unknown result type (might be due to invalid IL or missing references)
		//IL_0282: Unknown result type (might be due to invalid IL or missing references)
		//IL_0287: Unknown result type (might be due to invalid IL or missing references)
		//IL_0295: Unknown result type (might be due to invalid IL or missing references)
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02db: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_033f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0341: Unknown result type (might be due to invalid IL or missing references)
		//IL_0349: Unknown result type (might be due to invalid IL or missing references)
		//IL_0350: Unknown result type (might be due to invalid IL or missing references)
		//IL_0355: Unknown result type (might be due to invalid IL or missing references)
		//IL_035a: Unknown result type (might be due to invalid IL or missing references)
		//IL_035f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0364: Unknown result type (might be due to invalid IL or missing references)
		//IL_036d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0372: Unknown result type (might be due to invalid IL or missing references)
		//IL_0377: Unknown result type (might be due to invalid IL or missing references)
		//IL_0384: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0392: Unknown result type (might be due to invalid IL or missing references)
		//IL_0397: Unknown result type (might be due to invalid IL or missing references)
		if (string.Equals(Path.GetExtension(path), ".prefab", StringComparison.OrdinalIgnoreCase))
		{
			return LoadPrefab(path);
		}
		WorldSerialization val = new WorldSerialization();
		val.Load(path);
		if (val.world.prefabs == null || val.world.prefabs.Count == 0)
		{
			throw new InvalidDataException(path + " has no prefabs (or failed to load)");
		}
		CustomMonumentData data = new CustomMonumentData
		{
			_size = val.world.size
		};
		PrefabData val2 = val.world.prefabs[0];
		data._anchorPos = ToVector(val2.position);
		data._anchorRot = Quaternion.Euler(ToVector(val2.rotation));
		data._heights = ToArray<short>(val.GetMap("height")?.data, 2, out data._heightRes);
		data._splat = ToArray<byte>(val.GetMap("splat")?.data, 1, out data._splatRes, 8);
		data._topology = ToArray<int>(val.GetMap("topology")?.data, 4, out data._topologyRes);
		data._alpha = ToArray<byte>(val.GetMap("alpha")?.data, 1, out data._alphaRes);
		data.AnchorGround = (data.HasHeights ? data.SourceHeight(Vector2.zero) : data._anchorPos.y);
		if (float.IsNaN(data.AnchorGround))
		{
			data.AnchorGround = data._anchorPos.y;
		}
		Quaternion inverse = Quaternion.Inverse(data._anchorRot);
		float num = 0f;
		for (int i = 0; i < val.world.prefabs.Count; i++)
		{
			PrefabData val3 = val.world.prefabs[i];
			if (i != 0 || val3.id != 2749405185u)
			{
				if (string.IsNullOrEmpty(StringPool.Get(val3.id)))
				{
					data.UnknownPrefabs++;
					continue;
				}
				if (!IsFinite(ToVector(val3.position)) || !IsFinite(ToVector(val3.rotation)) || !IsFinite(ToVector(val3.scale)))
				{
					data.BrokenPrefabs++;
					continue;
				}
				Vector3 val4 = inverse * (ToVector(val3.position) - data._anchorPos);
				val4.y += data._anchorPos.y - data.AnchorGround;
				data.Items.Add(new Item
				{
					Category = val3.category,
					Id = val3.id,
					Local = val4,
					Rotation = inverse * Quaternion.Euler(ToVector(val3.rotation)),
					Scale = ToVector(val3.scale)
				});
				float num2 = num;
				Vector2 val5 = new Vector2(val4.x, val4.z);
				num = Mathf.Max(num2, ((Vector2)(ref val5)).magnitude);
			}
		}
		data.AutoRadius = Mathf.Max(20f, num + 10f);
		foreach (IOEntityInfo item in MapExtrasWriter.ReadRustEditIO(val, path))
		{
			data.IO.Add(MapExtrasWriter.Transform(item, delegate(Vector3 position)
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				//IL_000d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0012: Unknown result type (might be due to invalid IL or missing references)
				//IL_0017: Unknown result type (might be due to invalid IL or missing references)
				//IL_001c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0044: Unknown result type (might be due to invalid IL or missing references)
				Vector3 result = inverse * (position - data._anchorPos);
				result.y += data._anchorPos.y - data.AnchorGround;
				return result;
			}));
		}
		return data;
	}

	private static CustomMonumentData LoadPrefab(string path)
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_0277: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_020f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0214: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0230: Unknown result type (might be due to invalid IL or missing references)
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		//IL_0280: Unknown result type (might be due to invalid IL or missing references)
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0300: Unknown result type (might be due to invalid IL or missing references)
		byte[] array;
		using (FileStream fileStream = File.OpenRead(path))
		{
			byte[] buffer = new byte[4];
			if (fileStream.Read(buffer, 0, 4) != 4)
			{
				throw new InvalidDataException(path + " is too short");
			}
			LZ4Stream val = new LZ4Stream((Stream)fileStream, (LZ4StreamMode)1, (LZ4StreamFlags)0, 1048576);
			try
			{
				using MemoryStream memoryStream = new MemoryStream();
				((Stream)(object)val).CopyTo((Stream)memoryStream);
				array = memoryStream.ToArray();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		CustomMonumentData customMonumentData = new CustomMonumentData
		{
			_anchorRot = Quaternion.identity
		};
		float num = 0f;
		ProtoReader protoReader = new ProtoReader(array, 0, array.Length);
		float num2 = 0f;
		int field;
		int wire;
		while (protoReader.Next(out field, out wire))
		{
			if (field == 1 && wire == 2)
			{
				ProtoReader protoReader2 = protoReader.Message();
				int field2;
				int wire2;
				while (protoReader2.Next(out field2, out wire2))
				{
					if (field2 == 1 && wire2 == 0)
					{
						num2 = protoReader2.Varint();
					}
					else
					{
						protoReader2.Skip(wire2);
					}
				}
				continue;
			}
			if (field == 5 && wire == 2)
			{
				ProtoReader protoReader3 = protoReader.Message();
				int field3;
				int wire3;
				while (protoReader3.Next(out field3, out wire3))
				{
					if (field3 == 1 && wire3 == 2)
					{
						customMonumentData.IO.Add(ReadIOEntity(protoReader3.Message()));
					}
					else
					{
						protoReader3.Skip(wire3);
					}
				}
				continue;
			}
			if (field != 3 || wire != 2)
			{
				protoReader.Skip(wire);
				continue;
			}
			ProtoReader protoReader4 = protoReader.Message();
			string category = "";
			uint num3 = 0u;
			Vector3 val2 = Vector3.zero;
			Vector3 val3 = Vector3.zero;
			Vector3 val4 = Vector3.one;
			int field4;
			int wire4;
			while (protoReader4.Next(out field4, out wire4))
			{
				switch (field4)
				{
				case 1:
					if (wire4 == 2)
					{
						category = protoReader4.String();
						continue;
					}
					break;
				case 2:
					if (wire4 == 0)
					{
						num3 = (uint)protoReader4.Varint();
						continue;
					}
					break;
				case 3:
					if (wire4 == 2)
					{
						val2 = protoReader4.Message().Vector(Vector3.zero);
						continue;
					}
					break;
				case 4:
					if (wire4 == 2)
					{
						val3 = protoReader4.Message().Vector(Vector3.zero);
						continue;
					}
					break;
				case 5:
					if (wire4 == 2)
					{
						val4 = protoReader4.Message().Vector(Vector3.zero);
						continue;
					}
					break;
				}
				protoReader4.Skip(wire4);
			}
			if (num3 != 2749405185u)
			{
				if (string.IsNullOrEmpty(StringPool.Get(num3)))
				{
					customMonumentData.UnknownPrefabs++;
					continue;
				}
				if (!IsFinite(val2) || !IsFinite(val3) || !IsFinite(val4))
				{
					customMonumentData.BrokenPrefabs++;
					continue;
				}
				customMonumentData.Items.Add(new Item
				{
					Category = category,
					Id = num3,
					Local = val2,
					Rotation = Quaternion.Euler(val3),
					Scale = val4
				});
				float num4 = num;
				Vector2 val5 = new Vector2(val2.x, val2.z);
				num = Mathf.Max(num4, ((Vector2)(ref val5)).magnitude);
			}
		}
		if (customMonumentData.Items.Count == 0 && customMonumentData.UnknownPrefabs == 0)
		{
			throw new InvalidDataException(path + " has no prefabs");
		}
		customMonumentData.AnchorGround = 0f;
		customMonumentData.AutoRadius = Mathf.Max(20f, num + 10f);
		if (num2 > 0f)
		{
			customMonumentData._prefabTerrain = PrefabTerrain.Load(path, num2);
		}
		return customMonumentData;
	}

	private static IOEntityInfo ReadIOEntity(ProtoReader m)
	{
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		IOEntityInfo iOEntityInfo = new IOEntityInfo
		{
			DoorEffect = 0,
			Floors = 0
		};
		int field;
		int wire;
		while (m.Next(out field, out wire))
		{
			switch (field)
			{
			case 1:
				if (wire == 2)
				{
					iOEntityInfo.Prefab = m.String();
					continue;
				}
				break;
			case 2:
				if (wire == 2)
				{
					iOEntityInfo.Position = MapExtrasWriter.ToArray(m.Message().Vector(Vector3.zero));
					continue;
				}
				break;
			case 3:
				if (wire == 2)
				{
					iOEntityInfo.Inputs.Add(ReadIOConnection(m.Message()));
					continue;
				}
				break;
			case 4:
				if (wire == 2)
				{
					iOEntityInfo.Outputs.Add(ReadIOConnection(m.Message()));
					continue;
				}
				break;
			case 5:
				if (wire == 0)
				{
					iOEntityInfo.AccessLevel = (int)m.Varint();
					continue;
				}
				break;
			case 6:
				if (wire == 0)
				{
					iOEntityInfo.DoorEffect = (int)m.Varint();
					continue;
				}
				break;
			case 7:
				if (wire == 5)
				{
					iOEntityInfo.TimerLength = m.Fixed32();
					continue;
				}
				break;
			case 8:
				if (wire == 0)
				{
					iOEntityInfo.Frequency = (int)m.Varint();
					continue;
				}
				break;
			case 9:
				if (wire == 0)
				{
					iOEntityInfo.UnlimitedAmmo = m.Varint() != 0;
					continue;
				}
				break;
			case 10:
				if (wire == 0)
				{
					iOEntityInfo.PeaceKeeper = m.Varint() != 0;
					continue;
				}
				break;
			case 11:
				if (wire == 2)
				{
					iOEntityInfo.AutoTurretWeapon = m.String();
					continue;
				}
				break;
			case 12:
				if (wire == 0)
				{
					iOEntityInfo.BranchAmount = (int)m.Varint();
					continue;
				}
				break;
			case 13:
				if (wire == 0)
				{
					iOEntityInfo.TargetCounterNumber = (int)m.Varint();
					continue;
				}
				break;
			case 14:
				if (wire == 2)
				{
					iOEntityInfo.RcIdentifier = m.String();
					continue;
				}
				break;
			case 15:
				if (wire == 0)
				{
					iOEntityInfo.CounterPassthrough = m.Varint() != 0;
					continue;
				}
				break;
			case 16:
				if (wire == 0)
				{
					iOEntityInfo.Floors = (int)m.Varint();
					continue;
				}
				break;
			case 17:
				if (wire == 2)
				{
					iOEntityInfo.PhoneName = m.String();
					continue;
				}
				break;
			}
			m.Skip(wire);
		}
		return iOEntityInfo;
	}

	private static IOConnectionInfo ReadIOConnection(ProtoReader m)
	{
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		IOConnectionInfo iOConnectionInfo = new IOConnectionInfo();
		int field;
		int wire;
		while (m.Next(out field, out wire))
		{
			switch (field)
			{
			case 1:
				if (wire == 2)
				{
					iOConnectionInfo.Prefab = m.String();
					continue;
				}
				break;
			case 2:
				if (wire == 2)
				{
					iOConnectionInfo.Position = MapExtrasWriter.ToArray(m.Message().Vector(Vector3.zero));
					continue;
				}
				break;
			case 4:
				if (wire == 0)
				{
					iOConnectionInfo.Slot = (int)m.Varint();
					continue;
				}
				break;
			case 5:
				if (wire == 0)
				{
					iOConnectionInfo.Type = (int)m.Varint();
					continue;
				}
				break;
			}
			m.Skip(wire);
		}
		if (!string.IsNullOrEmpty(iOConnectionInfo.Prefab))
		{
			return iOConnectionInfo;
		}
		return null;
	}

	public float SourceHeight(Vector2 offset)
	{
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		if (_prefabTerrain != null)
		{
			Grid<float> heights = _prefabTerrain.Heights;
			if (heights == null || !_prefabTerrain.ToPixel(heights, offset, out var x, out var y))
			{
				return float.NaN;
			}
			int num = Mathf.FloorToInt(x);
			int num2 = Mathf.FloorToInt(y);
			float num3 = x - (float)num;
			float num4 = y - (float)num2;
			return AnchorGround + Mathf.Lerp(Mathf.Lerp(heights[num, num2], heights[num + 1, num2], num3), Mathf.Lerp(heights[num, num2 + 1], heights[num + 1, num2 + 1], num3), num4);
		}
		if (!ToSourceUV(offset, out var u, out var v))
		{
			return float.NaN;
		}
		float num5 = u * (float)(_heightRes - 1);
		float num6 = v * (float)(_heightRes - 1);
		int num7 = Mathf.Clamp((int)num5, 0, _heightRes - 2);
		int num8 = Mathf.Clamp((int)num6, 0, _heightRes - 2);
		float num9 = num5 - (float)num7;
		float num10 = num6 - (float)num8;
		float num11 = H(num7, num8);
		float num12 = H(num7 + 1, num8);
		float num13 = H(num7, num8 + 1);
		float num14 = H(num7 + 1, num8 + 1);
		return Mathf.Lerp(Mathf.Lerp(num11, num12, num9), Mathf.Lerp(num13, num14, num9), num10);
		float H(int num15, int z)
		{
			return -500f + (float)_heights[z * _heightRes + num15] / 32767f * 1000f;
		}
	}

	public bool SourceSplat(Vector2 offset, out Vector4 first, out Vector4 second)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		first = (second = Vector4.zero);
		if (_prefabTerrain != null)
		{
			Grid<Vector4>[] splat = _prefabTerrain.Splat;
			if (splat == null || !_prefabTerrain.ToPixel(splat[0], offset, out var x, out var y))
			{
				return false;
			}
			int x2 = Mathf.RoundToInt(x);
			int y2 = Mathf.RoundToInt(y);
			first = splat[0][x2, y2];
			second = splat[1][x2, y2];
			return true;
		}
		if (!ToSourceUV(offset, out var u, out var v))
		{
			return false;
		}
		int x3 = Mathf.Clamp((int)(u * (float)_splatRes), 0, _splatRes - 1);
		int z = Mathf.Clamp((int)(v * (float)_splatRes), 0, _splatRes - 1);
		first = new Vector4(S(0), S(1), S(2), S(3));
		second = new Vector4(S(4), S(5), S(6), S(7));
		return true;
		float S(int i)
		{
			return (float)(int)_splat[(i * _splatRes + z) * _splatRes + x3] / 255f;
		}
	}

	public int SourceTopology(Vector2 offset)
	{
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		if (_prefabTerrain != null)
		{
			Grid<int> topology = _prefabTerrain.Topology;
			if (topology == null || !_prefabTerrain.ToPixel(topology, offset, out var x, out var y))
			{
				return 0;
			}
			return topology[Mathf.RoundToInt(x), Mathf.RoundToInt(y)];
		}
		if (!ToSourceUV(offset, out var u, out var v))
		{
			return 0;
		}
		int num = Mathf.Clamp((int)(u * (float)_topologyRes), 0, _topologyRes - 1);
		int num2 = Mathf.Clamp((int)(v * (float)_topologyRes), 0, _topologyRes - 1);
		return _topology[num2 * _topologyRes + num];
	}

	public float SourceAlpha(Vector2 offset)
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		if (_prefabTerrain != null)
		{
			Grid<float> alpha = _prefabTerrain.Alpha;
			if (alpha == null || !_prefabTerrain.ToPixel(alpha, offset, out var x, out var y))
			{
				return float.NaN;
			}
			return alpha[Mathf.RoundToInt(x), Mathf.RoundToInt(y)];
		}
		if (_alpha == null || !ToSourceUV(offset, out var u, out var v))
		{
			return float.NaN;
		}
		int num = Mathf.Clamp((int)(u * (float)_alphaRes), 0, _alphaRes - 1);
		int num2 = Mathf.Clamp((int)(v * (float)_alphaRes), 0, _alphaRes - 1);
		return (float)(int)_alpha[num2 * _alphaRes + num] / 255f;
	}

	private bool ToSourceUV(Vector2 offset, out float u, out float v)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = _anchorPos + _anchorRot * new Vector3(offset.x, 0f, offset.y);
		u = (val.x + _size / 2f) / _size;
		v = (val.z + _size / 2f) / _size;
		if (u >= 0f && u <= 1f && v >= 0f)
		{
			return v <= 1f;
		}
		return false;
	}

	private static T[] ToArray<T>(byte[] bytes, int elementSize, out int res, int channels = 1) where T : struct
	{
		res = 0;
		if (bytes == null || bytes.Length == 0)
		{
			return null;
		}
		int num = bytes.Length / elementSize;
		res = (int)Math.Round(Math.Sqrt((double)num / (double)channels));
		if (res * res * channels != num)
		{
			res = 0;
			return null;
		}
		T[] array = new T[num];
		Buffer.BlockCopy(bytes, 0, array, 0, num * elementSize);
		return array;
	}

	private static bool IsFinite(Vector3 v)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		if (!float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y))
		{
			return !float.IsInfinity(v.z);
		}
		return false;
	}

	private static Vector3 ToVector(VectorData v)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		return new Vector3(v.x, v.y, v.z);
	}
}
