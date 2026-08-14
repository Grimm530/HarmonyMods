using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Ext.Chaos.UIFramework;

public class NeedsKeyboardComponent : BaseCuiComponent
{
	public NeedsKeyboardComponent()
	{
		while (true)
		{
			int num = 432758649;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x66B29955)) % 3)
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
				num = (int)((num2 * 161603426) ^ 0x432D0070);
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
		jsonWriter.WriteValue("NeedsKeyboard");
		jsonWriter.WriteEndObject();
	}
}
