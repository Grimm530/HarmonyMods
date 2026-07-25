using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Ext.Chaos.UIFramework;

public class ScrollbarComponent : BaseCuiComponent
{
	public class Style
	{
		public bool Invert;
		public bool AutoHide;
		public float Size = 20f;
		public string HandleSprite = "assets/content/ui/ui.rounded.tga";
		public string TrackSprite = "assets/content/ui/ui.background.tile.psd";
		public Color HandleColor = new Color(0.15f, 0.15f, 0.15f);
		public Color HighlightColor = new Color(0.17f, 0.17f, 0.17f);
		public Color PressedColor = new Color(0.2f, 0.2f, 0.2f);
		public Color TrackColor = new Color(0.09f, 0.09f, 0.09f);
	}

	public bool Invert { get; set; }
	public bool AutoHide { get; set; }
	public float Size { get; set; } = 20f;
	public string HandleSprite { get; set; } = "assets/content/ui/ui.rounded.tga";
	public string TrackSprite { get; set; } = "assets/content/ui/ui.background.tile.psd";
	public Color HandleColor { get; set; } = new Color(0.15f, 0.15f, 0.15f);
	public Color HighlightColor { get; set; } = new Color(0.17f, 0.17f, 0.17f);
	public Color PressedColor { get; set; } = new Color(0.2f, 0.2f, 0.2f);
	public Color TrackColor { get; set; } = new Color(0.09f, 0.09f, 0.09f);

	public void WithStyle(Style style)
	{
		if (style == null) return;
		Invert = style.Invert;
		AutoHide = style.AutoHide;
		Size = style.Size;
		HandleSprite = style.HandleSprite;
		TrackSprite = style.TrackSprite;
		HandleColor = style.HandleColor;
		HighlightColor = style.HighlightColor;
		PressedColor = style.PressedColor;
		TrackColor = style.TrackColor;
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is ScrollbarComponent s)
		{
			Invert = s.Invert;
			AutoHide = s.AutoHide;
			Size = s.Size;
			HandleSprite = s.HandleSprite;
			TrackSprite = s.TrackSprite;
			HandleColor = s.HandleColor;
			HighlightColor = s.HighlightColor;
			PressedColor = s.PressedColor;
			TrackColor = s.TrackColor;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.Scrollbar");
		jsonWriter.WritePropertyName("invert");
		jsonWriter.WriteValue(Invert);
		jsonWriter.WritePropertyName("autohide");
		jsonWriter.WriteValue(AutoHide);
		jsonWriter.WritePropertyName("size");
		jsonWriter.WriteValue(Size);
		jsonWriter.WritePropertyName("handleSprite");
		jsonWriter.WriteValue(HandleSprite);
		jsonWriter.WritePropertyName("trackSprite");
		jsonWriter.WriteValue(TrackSprite);
		jsonWriter.WritePropertyName("handleColor");
		jsonWriter.WriteValue(HandleColor.ToString());
		jsonWriter.WritePropertyName("highlightColor");
		jsonWriter.WriteValue(HighlightColor.ToString());
		jsonWriter.WritePropertyName("pressedColor");
		jsonWriter.WriteValue(PressedColor.ToString());
		jsonWriter.WritePropertyName("trackColor");
		jsonWriter.WriteValue(TrackColor.ToString());
		jsonWriter.WriteEndObject();
	}

	public override void OnEnterPool()
	{
		Invert = false;
		AutoHide = false;
		Size = 20f;
		HandleSprite = "assets/content/ui/ui.rounded.tga";
		TrackSprite = "assets/content/ui/ui.background.tile.psd";
		HandleColor = new Color(0.15f, 0.15f, 0.15f);
		HighlightColor = new Color(0.17f, 0.17f, 0.17f);
		PressedColor = new Color(0.2f, 0.2f, 0.2f);
		TrackColor = new Color(0.09f, 0.09f, 0.09f);
	}
}
