using System;
using System.Collections.Generic;

namespace CustomGenerator.Utility;

internal static class EnumParser
{
	public static Enum GetFilterEnum(string type, List<string> values)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		switch (type)
		{
		case "SplatType":
			return (Enum)(object)ParseEnum<Enum>(values);
		case "BiomeType":
			return (Enum)(object)ParseEnum<Enum>(values);
		case "TopologyAny":
		case "TopologyAll":
		case "TopologyNot":
			return (Enum)(object)ParseEnum<Enum>(values);
		default:
			throw new ArgumentException("Unknown type: " + type);
		}
	}

	private static T ParseEnum<T>(List<string> values) where T : struct, Enum
	{
		T val = default(T);
		foreach (string value in values)
		{
			if (Enum.TryParse<T>(value.Trim(), out var result))
			{
				val = (T)(object)((int)(object)val | (int)(object)result);
				continue;
			}
			throw new ArgumentException("Invalid value: " + value);
		}
		return val;
	}
}
