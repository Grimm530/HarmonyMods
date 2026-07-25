using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Oxide.Ext.Chaos.UIFramework;

public class OutlineComponent : BaseCuiComponent, ICuiColorComponent, IStyleComponent
{
	public static readonly Bounds DefaultDistance = new Bounds(1f, -1f);

	public Color Color { get; set; } = Color.DEFAULT;

	public Bounds EffectDistance { get; set; } = DefaultDistance;

	public bool UseGraphicAlpha { get; set; }

	public OutlineComponent()
	{
	}

	public OutlineComponent(Color color)
	{
		while (true)
		{
			int num = 587889133;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x53FCBB50)) % 4)
				{
				case 3u:
					break;
				default:
					return;
				case 1u:
					Color = color;
					num = (int)(num2 * 2011654162) ^ -1170814030;
					continue;
				case 0u:
					base.IsConstant = true;
					num = ((int)num2 * -1771778579) ^ 0x6D2E1D2E;
					continue;
				case 2u:
					return;
				}
				break;
			}
		}
	}

	public OutlineComponent(Color color, float2 effectDistance)
	{
		while (true)
		{
			int num = -1392261368;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -2138710317)) % 4)
				{
				case 0u:
					break;
				default:
					return;
				case 3u:
					Color = color;
					num = ((int)num2 * -1211732612) ^ 0x640A6316;
					continue;
				case 1u:
					EffectDistance = new Bounds(effectDistance);
					base.IsConstant = true;
					num = ((int)num2 * -1893996163) ^ -1208910540;
					continue;
				case 2u:
					return;
				}
				break;
			}
		}
	}

	public OutlineComponent(Color color, Bounds effectDistance)
	{
		while (true)
		{
			int num = -1579793100;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -430524906)) % 3)
				{
				case 0u:
					break;
				case 2u:
					goto IL_003e;
				default:
					base.IsConstant = true;
					return;
				}
				break;
				IL_003e:
				Color = color;
				EffectDistance = effectDistance;
				num = (int)(num2 * 1643134235) ^ -1777863660;
			}
		}
	}

	public OutlineComponent WithEffectDistance(float2 effectDistance)
	{
		EffectDistance = new Bounds(effectDistance);
		return this;
	}

	public OutlineComponent WithEffectDistance(Bounds effectDistance)
	{
		EffectDistance = effectDistance;
		return this;
	}

	public OutlineComponent WithGraphicAlpha()
	{
		UseGraphicAlpha = true;
		return this;
	}

	public BaseCuiComponent WithStyle(Style style)
	{
		Color = style.OutlineColor;
		EffectDistance = style.EffectDistance;
		while (true)
		{
			int num = -1054439178;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -451310179)) % 3)
				{
				case 2u:
					break;
				case 1u:
					goto IL_003a;
				default:
					return this;
				}
				break;
				IL_003a:
				UseGraphicAlpha = style.UseGraphicAlpha;
				num = (int)(num2 * 365429975) ^ -1044401089;
			}
		}
	}

	BaseCuiComponent IStyleComponent.WithStyle(Style style)
	{
		//ILSpy generated this explicit interface implementation from .override directive in WithStyle
		return this.WithStyle(style);
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is OutlineComponent outlineComponent)
		{
			Color = outlineComponent.Color;
			EffectDistance = outlineComponent.EffectDistance;
			UseGraphicAlpha = outlineComponent.UseGraphicAlpha;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.Outline");
		if (Color != Color.DEFAULT || IsFieldDirty("Color", dirtyFields))
		{
			jsonWriter.WritePropertyName("color");
			jsonWriter.WriteValue(Color.ToString("1 1 1 1"));
		}
		if (EffectDistance != DefaultDistance || IsFieldDirty("EffectDistance", dirtyFields))
		{
			jsonWriter.WritePropertyName("distance");
			jsonWriter.WriteValue(EffectDistance.ToString("1 -1"));
		}
		if (UseGraphicAlpha || IsFieldDirty("UseGraphicAlpha", dirtyFields))
		{
			jsonWriter.WritePropertyName("useGraphicAlpha");
			jsonWriter.WriteValue(UseGraphicAlpha);
		}
		jsonWriter.WriteEndObject();
	}

	public override void OnEnterPool()
	{
		base.OnEnterPool();
		Color = Color.DEFAULT;
		EffectDistance = DefaultDistance;
		UseGraphicAlpha = false;
	}
}
