using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Ext.Chaos.UIFramework;

public class RawImageComponent : BaseCuiComponent, ICuiColorComponent, ICuiGraphicComponent, IStyleComponent
{
	// Oxide default texture path used when omitting the property from CUI JSON.
	public const string DefaultTexture = "assets/icons/rust.png";

	public Color Color { get; set; } = Color.DEFAULT;

	public string Texture { get; set; } = DefaultTexture;

	public string Material { get; set; }

	public string URL { get; set; }

	public string PNG { get; set; }

	public ulong SteamId { get; set; }

	public float FadeIn { get; set; }

	public BaseCuiComponent WithStyle(Style style)
	{
		Color = style.ImageColor;
		Texture = style.Texture;
		Material = style.Material;
		return this;
	}

	BaseCuiComponent IStyleComponent.WithStyle(Style style)
	{
		return WithStyle(style);
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is RawImageComponent rawImageComponent)
		{
			Color = rawImageComponent.Color;
			Texture = rawImageComponent.Texture;
			Material = rawImageComponent.Material;
			URL = rawImageComponent.URL;
			PNG = rawImageComponent.PNG;
			SteamId = rawImageComponent.SteamId;
			FadeIn = rawImageComponent.FadeIn;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.RawImage");
		if (Color != Color.DEFAULT || IsFieldDirty("Color", dirtyFields))
		{
			jsonWriter.WritePropertyName("color");
			jsonWriter.WriteValue(Color.ToString("1 1 1 1"));
		}
		if ((!string.IsNullOrEmpty(Texture) && Texture != DefaultTexture) || IsFieldDirty("Texture", dirtyFields))
		{
			jsonWriter.WritePropertyName("texture");
			jsonWriter.WriteValue(Texture);
		}
		if (!string.IsNullOrEmpty(Material) || IsFieldDirty("Material", dirtyFields))
		{
			jsonWriter.WritePropertyName("material");
			jsonWriter.WriteValue(Material);
		}
		if (!string.IsNullOrEmpty(URL) || IsFieldDirty("URL", dirtyFields))
		{
			jsonWriter.WritePropertyName("url");
			jsonWriter.WriteValue(URL);
		}
		if (!string.IsNullOrEmpty(PNG) || IsFieldDirty("PNG", dirtyFields))
		{
			jsonWriter.WritePropertyName("png");
			jsonWriter.WriteValue(PNG);
		}
		if (SteamId != 0L || IsFieldDirty("SteamId", dirtyFields))
		{
			jsonWriter.WritePropertyName("steamid");
			jsonWriter.WriteValue(SteamId.ToString());
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
		Color = Color.DEFAULT;
		Texture = DefaultTexture;
		Material = null;
		URL = null;
		PNG = null;
		FadeIn = 0f;
		SteamId = 0uL;
	}
}
