using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class TextComponent : BaseCuiComponent, ICuiFontComponent, ICuiColorComponent, ICuiGraphicComponent, IStyleComponent
{
	public string Text { get; set; }

	public Font Font { get; set; }

	public int FontSize { get; set; } = 14;

	public TextAnchor Alignment { get; set; }

	public Color Color { get; set; } = Color.DEFAULT;

	public VerticalWrapMode VerticalOverflow { get; set; }

	public float FadeIn { get; set; }

	public BaseCuiComponent WithStyle(Style style)
	{
		Color = style.FontColor;
		Font = style.Font;
		FontSize = style.FontSize;
		Alignment = style.Alignment;
		VerticalOverflow = style.WrapMode;
		return this;
	}

	BaseCuiComponent IStyleComponent.WithStyle(Style style)
	{
		return WithStyle(style);
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is TextComponent textComponent)
		{
			Text = textComponent.Text;
			Font = textComponent.Font;
			FontSize = textComponent.FontSize;
			Alignment = textComponent.Alignment;
			Color = textComponent.Color;
			VerticalOverflow = textComponent.VerticalOverflow;
			FadeIn = textComponent.FadeIn;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.Text");
		jsonWriter.WritePropertyName("text");
		jsonWriter.WriteValue(Text);
		if (Font != Font.RobotoCondensedBold || IsFieldDirty("Font", dirtyFields))
		{
			jsonWriter.WritePropertyName("font");
			jsonWriter.WriteValue(EnumConverters.ToJson(Font));
		}
		if (FontSize != 14 || IsFieldDirty("FontSize", dirtyFields))
		{
			jsonWriter.WritePropertyName("fontSize");
			jsonWriter.WriteValue(FontSize);
		}
		if (Alignment != TextAnchor.UpperLeft || IsFieldDirty("Alignment", dirtyFields))
		{
			jsonWriter.WritePropertyName("align");
			jsonWriter.WriteValue(EnumConverters.ToJson(Alignment));
		}
		if (Color != Color.DEFAULT || IsFieldDirty("Color", dirtyFields))
		{
			jsonWriter.WritePropertyName("color");
			jsonWriter.WriteValue(Color.ToString("1 1 1 1"));
		}
		if (VerticalOverflow != VerticalWrapMode.Truncate || IsFieldDirty("VerticalOverflow", dirtyFields))
		{
			jsonWriter.WritePropertyName("verticalOverflow");
			jsonWriter.WriteValue(EnumConverters.ToJson(VerticalOverflow));
		}
		if (FadeIn > 0f || IsFieldDirty("FadeIn", dirtyFields))
		{
			jsonWriter.WritePropertyName("fadeIn");
			jsonWriter.WriteValue(FadeIn);
		}
		jsonWriter.WriteEndObject();
	}

	public override void OnEnterPool()
	{
		base.OnEnterPool();
		Text = null;
		Font = Font.RobotoCondensedBold;
		FontSize = 14;
		Alignment = TextAnchor.UpperLeft;
		Color = Color.DEFAULT;
		VerticalOverflow = VerticalWrapMode.Truncate;
		FadeIn = 0f;
	}
}
