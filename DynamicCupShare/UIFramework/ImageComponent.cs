using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class ImageComponent : BaseCuiComponent, ICuiColorComponent, ICuiGraphicComponent, IStyleComponent
{
	public Color Color { get; set; } = Color.DEFAULT;

	public string Sprite { get; set; } = Sprites.DEFAULT;

	public string Material { get; set; } = Materials.DEFAULT;

	public Image.Type ImageType { get; set; }

	public string PNG { get; set; }

	public int ItemID { get; set; }

	public ulong SkinID { get; set; }

	public float FadeIn { get; set; }

	public BaseCuiComponent WithStyle(Style style)
	{
		Color = style.ImageColor;
		Sprite = style.Sprite;
		Material = style.Material;
		ImageType = style.ImageType;
		return this;
	}

	BaseCuiComponent IStyleComponent.WithStyle(Style style)
	{
		return WithStyle(style);
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is ImageComponent imageComponent)
		{
			Color = imageComponent.Color;
			Sprite = imageComponent.Sprite;
			Material = imageComponent.Material;
			ImageType = imageComponent.ImageType;
			PNG = imageComponent.PNG;
			ItemID = imageComponent.ItemID;
			SkinID = imageComponent.SkinID;
			FadeIn = imageComponent.FadeIn;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.Image");
		if (Color != Color.DEFAULT || IsFieldDirty("Color", dirtyFields))
		{
			jsonWriter.WritePropertyName("color");
			jsonWriter.WriteValue(Color.ToString("1 1 1 1"));
		}
		if ((!string.IsNullOrEmpty(Sprite) && Sprite != Sprites.DEFAULT) || IsFieldDirty("Sprite", dirtyFields))
		{
			jsonWriter.WritePropertyName("sprite");
			jsonWriter.WriteValue(Sprite);
		}
		if ((!string.IsNullOrEmpty(Material) && Material != Materials.DEFAULT) || IsFieldDirty("Material", dirtyFields))
		{
			jsonWriter.WritePropertyName("material");
			jsonWriter.WriteValue(Material);
		}
		if (ImageType != Image.Type.Simple || IsFieldDirty("ImageType", dirtyFields))
		{
			jsonWriter.WritePropertyName("imagetype");
			jsonWriter.WriteValue(EnumConverters.ToJson(ImageType));
		}
		if (!string.IsNullOrEmpty(PNG) || IsFieldDirty("PNG", dirtyFields))
		{
			jsonWriter.WritePropertyName("png");
			jsonWriter.WriteValue(PNG);
		}
		if (SkinID != 0L || IsFieldDirty("SkinID", dirtyFields))
		{
			jsonWriter.WritePropertyName("skinid");
			jsonWriter.WriteValue(SkinID);
		}
		if (ItemID != 0 || IsFieldDirty("ItemID", dirtyFields))
		{
			jsonWriter.WritePropertyName("itemid");
			jsonWriter.WriteValue(ItemID);
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
		PNG = null;
		ItemID = 0;
		SkinID = 0uL;
		Sprite = Sprites.DEFAULT;
		Material = Materials.DEFAULT;
		ImageType = Image.Type.Simple;
		FadeIn = 0f;
	}
}
