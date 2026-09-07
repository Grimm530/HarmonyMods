using System;
using Newtonsoft.Json;
using Ext.Chaos.UIFramework;
using UnityEngine;

namespace Ext.Chaos.Json;

public class UIColorConverter : JsonConverter
{
	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
	{
		Ext.Chaos.UIFramework.Color color = (Ext.Chaos.UIFramework.Color)value;
		writer.WriteValue($"{color.R} {color.G} {color.B} {color.A}");
	}

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.String)
		{
			string[] array = reader.Value.ToString().Trim().Split(' ');
			return new Ext.Chaos.UIFramework.Color(Convert.ToSingle(array[0]), Convert.ToSingle(array[1]), Convert.ToSingle(array[2]), Convert.ToSingle(array[3]));
		}
		return Ext.Chaos.UIFramework.Color.Clear;
	}

	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(UnityEngine.Color);
	}
}
