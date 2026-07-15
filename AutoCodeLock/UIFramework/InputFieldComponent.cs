using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class InputFieldComponent : BaseCuiComponent, ICuiColorComponent, ICuiFontComponent, ICuiCommandComponent, ICuiGraphicComponent, IStyleComponent
{
	public Color Color { get; set; } = Color.White;

	public string Text { get; set; }

	public Font Font { get; set; }

	public int FontSize { get; set; } = 14;

	public TextAnchor Alignment { get; set; }

	public string Command { get; set; }

	public int CharacterLimit { get; set; }

	public bool ReadOnly { get; set; }

	public InputField.LineType LineType { get; set; }

	public bool IsPassword { get; set; }

	public bool NeedsKeyboard { get; set; }

	public bool HudMenuInput { get; set; }

	public bool AutoFocus { get; set; }

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
		Color = style.FontColor;
		Font = style.Font;
		FontSize = style.FontSize;
		Alignment = style.Alignment;
		LineType = style.LineType;
		return this;
	}

	BaseCuiComponent IStyleComponent.WithStyle(Style style)
	{
		//ILSpy generated this explicit interface implementation from .override directive in WithStyle
		return this.WithStyle(style);
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is InputFieldComponent inputFieldComponent)
		{
			Color = inputFieldComponent.Color;
			Text = inputFieldComponent.Text;
			Font = inputFieldComponent.Font;
			FontSize = inputFieldComponent.FontSize;
			Alignment = inputFieldComponent.Alignment;
			Command = inputFieldComponent.Command;
			CharacterLimit = inputFieldComponent.CharacterLimit;
			ReadOnly = inputFieldComponent.ReadOnly;
			LineType = inputFieldComponent.LineType;
			IsPassword = inputFieldComponent.IsPassword;
			NeedsKeyboard = inputFieldComponent.NeedsKeyboard;
			HudMenuInput = inputFieldComponent.HudMenuInput;
			AutoFocus = inputFieldComponent.AutoFocus;
			FadeIn = inputFieldComponent.FadeIn;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.InputField");
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
		if (!string.IsNullOrEmpty(Command) || IsFieldDirty("Command", dirtyFields))
		{
			jsonWriter.WritePropertyName("command");
			jsonWriter.WriteValue(Command);
		}
		if (CharacterLimit > 0 || IsFieldDirty("CharacterLimit", dirtyFields))
		{
			jsonWriter.WritePropertyName("characterLimit");
			jsonWriter.WriteValue(CharacterLimit);
		}
		if (LineType != InputField.LineType.SingleLine || IsFieldDirty("LineType", dirtyFields))
		{
			jsonWriter.WritePropertyName("lineType");
			jsonWriter.WriteValue(EnumConverters.ToJson(LineType));
		}
		if (ReadOnly || IsFieldDirty("ReadOnly", dirtyFields))
		{
			jsonWriter.WritePropertyName("readOnly");
			jsonWriter.WriteValue(ReadOnly);
		}
		if (IsPassword || IsFieldDirty("IsPassword", dirtyFields))
		{
			jsonWriter.WritePropertyName("password");
			jsonWriter.WriteValue(IsPassword);
		}
		if (NeedsKeyboard || IsFieldDirty("NeedsKeyboard", dirtyFields))
		{
			jsonWriter.WritePropertyName("needsKeyboard");
			jsonWriter.WriteValue(NeedsKeyboard);
		}
		if (HudMenuInput || IsFieldDirty("HudMenuInput", dirtyFields))
		{
			jsonWriter.WritePropertyName("hudMenuInput");
			jsonWriter.WriteValue(HudMenuInput);
		}
		if (AutoFocus || IsFieldDirty("AutoFocus", dirtyFields))
		{
			jsonWriter.WritePropertyName("autofocus");
			jsonWriter.WriteValue(AutoFocus);
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
		Text = null;
		Font = Font.RobotoCondensedBold;
		FontSize = 14;
		Alignment = TextAnchor.UpperLeft;
		CharacterLimit = 0;
		ReadOnly = false;
		LineType = InputField.LineType.SingleLine;
		IsPassword = false;
		NeedsKeyboard = false;
		HudMenuInput = false;
		AutoFocus = false;
		FadeIn = 0f;
	}
}
