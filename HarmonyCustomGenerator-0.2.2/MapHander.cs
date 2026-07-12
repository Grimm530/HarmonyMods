using System;
using System.Collections.Generic;
using ProtoBuf;

public class MapHander
{
	private static PrefabData CreatePrefab(uint PrefabID, VectorData position, VectorData rotation, VectorData scale, string category = "Monument")
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		return new PrefabData
		{
			category = category,
			id = PrefabID,
			position = position,
			rotation = rotation,
			scale = scale
		};
	}

	private static VectorData CalculateLocalPos(VectorData placePos, VectorData globalPos, VectorData rotation)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		return RotateVector(new VectorData(globalPos.x - placePos.x, globalPos.y - placePos.y, globalPos.z - placePos.z), rotation);
	}

	private static VectorData RotateVector(VectorData vector, VectorData rotation)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		float num = rotation.x * (float)Math.PI / 180f;
		float num2 = rotation.y * (float)Math.PI / 180f;
		float num3 = rotation.z * (float)Math.PI / 180f;
		float num4 = (float)Math.Cos(num);
		float num5 = (float)Math.Sin(num);
		float num6 = (float)Math.Cos(num2);
		float num7 = (float)Math.Sin(num2);
		float num8 = (float)Math.Cos(num3);
		float num9 = (float)Math.Sin(num3);
		float y = vector.y * num4 - vector.z * num5;
		float z = vector.y * num5 + vector.z * num4;
		vector.y = y;
		vector.z = z;
		float x = vector.x * num6 + vector.z * num7;
		z = vector.z * num6 - vector.x * num7;
		vector.x = x;
		vector.z = z;
		x = vector.x * num8 - vector.y * num9;
		y = vector.x * num9 + vector.y * num8;
		vector.x = x;
		vector.y = y;
		return vector;
	}

	public static List<PrefabData> CreatePrefabFromMap(VectorData startPos, VectorData rotation, List<PrefabData> prefabs)
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		List<PrefabData> list = new List<PrefabData>();
		bool flag = true;
		foreach (PrefabData prefab in prefabs)
		{
			list.Add(CreatePrefab((prefab.id == 2749405185u) ? 504351302u : prefab.id, Calculate(startPos, prefab.position, prefab.scale, prefabs, rotation), flag ? rotation : CalculateRot(rotation, prefab.rotation), (VectorData)((prefab.id == 2749405185u) ? new VectorData(0f, 0f, 0f) : prefab.scale), prefab.category));
			flag = false;
		}
		return list;
	}

	private static VectorData Calculate(VectorData globalPos, VectorData position, VectorData scale, List<PrefabData> prefabs, VectorData firstPrefabRotation)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		VectorData val = CalculateLocalPos(prefabs[0].position, position, firstPrefabRotation);
		return new VectorData(globalPos.x + val.x, globalPos.y + val.y, globalPos.z + val.z);
	}

	private static VectorData CalculateRot(VectorData globalRot, VectorData localRot)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		return new VectorData(globalRot.x + localRot.x, globalRot.y + localRot.y, globalRot.z + localRot.z);
	}
}
