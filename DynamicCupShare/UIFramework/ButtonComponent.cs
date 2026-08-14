using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class ButtonComponent : BaseCuiComponent, ICuiColorComponent, ICuiCommandComponent, ICuiGraphicComponent, IStyleComponent
{
	public Color Color { get; set; } = Color.DEFAULT;

	public string Command { get; set; }

	public bool Close { get; set; }

	public string Sprite { get; set; } = Sprites.DEFAULT;

	public string Material { get; set; } = Materials.DEFAULT;

	public Image.Type ImageType { get; set; }

	public float FadeIn { get; set; }

	public void SetCommand(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, string identifier = "")
	{
		Command = commandCallbackHandler.RegisterCommand(callback, null, identifier);
	}

	void ICuiCommandComponent.SetCommand(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, string identifier)
	{
		SetCommand(commandCallbackHandler, callback, identifier);
	}

	public void SetSecureCommand(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, ulong userId, string identifier = "")
	{
		Command = commandCallbackHandler.RegisterSecureCommand(callback, userId, identifier);
	}

	public BaseCuiComponent WithStyle(Style style)
	{
		Material = style.Material;
		Color = style.ImageColor;
		Sprite = style.Sprite;
		ImageType = style.ImageType;
		return this;
	}

	BaseCuiComponent IStyleComponent.WithStyle(Style style)
	{
		return WithStyle(style);
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is ButtonComponent buttonComponent)
		{
			Color = buttonComponent.Color;
			Command = buttonComponent.Command;
			Close = buttonComponent.Close;
			Sprite = buttonComponent.Sprite;
			Material = buttonComponent.Material;
			ImageType = buttonComponent.ImageType;
			FadeIn = buttonComponent.FadeIn;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.Button");
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
		if (!string.IsNullOrEmpty(Command) || IsFieldDirty("Command", dirtyFields))
		{
			jsonWriter.WritePropertyName("command");
			jsonWriter.WriteValue(Command);
		}
		if (Close || IsFieldDirty("Close", dirtyFields))
		{
			jsonWriter.WritePropertyName("close");
			jsonWriter.WriteValue(Close);
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
		Command = null;
		Close = false;
		Sprite = Sprites.DEFAULT;
		Material = Materials.DEFAULT;
		ImageType = Image.Type.Simple;
		FadeIn = 0f;
	}
}
