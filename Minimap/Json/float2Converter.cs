using System;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Oxide.Ext.Chaos.Json;

public class float2Converter : JsonConverter
{
	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
	{
		float2 @float = (float2)value;
		writer.WriteValue($"{@float.x} {@float.y}");
	}

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.String)
		{
			string[] array = reader.Value.ToString().Trim().Split(' ');
			return new float2(Convert.ToSingle(array[0]), Convert.ToSingle(array[1]));
		}
		return float2.zero;
	}

	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(float2);
	}
}
