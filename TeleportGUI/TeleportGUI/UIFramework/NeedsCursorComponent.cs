using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Ext.Chaos.UIFramework;

public class NeedsCursorComponent : BaseCuiComponent
{
	public NeedsCursorComponent()
	{
		while (true)
		{
			int num = 1790613662;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x372F40CE)) % 3)
				{
				case 2u:
					break;
				default:
					return;
				case 1u:
					goto IL_0028;
				case 0u:
					return;
				}
				break;
				IL_0028:
				base.IsConstant = true;
				num = ((int)num2 * -1731800686) ^ -405112523;
			}
		}
	}

	public override void CopyFrom<T>(T other)
	{
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("NeedsCursor");
		jsonWriter.WriteEndObject();
	}
}
