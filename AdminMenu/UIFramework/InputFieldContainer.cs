using System;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class InputFieldContainer : BaseContainer
{
	private InputFieldComponent m_Component;

	protected void Initialize(BaseContainer parent, string text, Style style, Anchor anchor, Offset offset)
	{
		Initialize(CuiHelper.GetGuid(), parent, anchor, offset);
		while (true)
		{
			int num = -1639581250;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -343455945)) % 6)
				{
				case 0u:
					break;
				default:
					return;
				case 4u:
					WithStyle(style);
					num = ((int)num2 * -120218455) ^ 0x334B498E;
					continue;
				case 2u:
				{
					int num3;
					int num4;
					if (style != null)
					{
						num3 = 1308703317;
						num4 = num3;
					}
					else
					{
						num3 = 1222697436;
						num4 = num3;
					}
					num = num3 ^ (int)(num2 * 291686342);
					continue;
				}
				case 1u:
					m_Component.Text = text;
					num = -512833564;
					continue;
				case 5u:
					base.Element.Components.Add(m_Component = UIComponentPool.Get<InputFieldComponent>());
					num = ((int)num2 * -327444529) ^ 0x27F67966;
					continue;
				case 3u:
					return;
				}
				break;
			}
		}
	}

	protected void Initialize(string name, Layer layer, string text, Style style, Anchor anchor, Offset offset)
	{
		Initialize(name, layer, anchor, offset);
		while (true)
		{
			int num = 473927336;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x21AA5DC)) % 5)
				{
				case 4u:
					break;
				case 1u:
					base.Element.Components.Add(m_Component = UIComponentPool.Get<InputFieldComponent>());
					num = ((int)num2 * -1637220868) ^ -430513563;
					continue;
				case 0u:
					WithStyle(style);
					num = (int)(num2 * 1462633613) ^ -1434172350;
					continue;
				case 2u:
				{
					int num3;
					int num4;
					if (style != null)
					{
						num3 = 584492293;
						num4 = num3;
					}
					else
					{
						num3 = 337694879;
						num4 = num3;
					}
					num = num3 ^ (int)(num2 * 2082077772);
					continue;
				}
				default:
					m_Component.Text = text;
					return;
				}
				break;
			}
		}
	}

	public new static InputFieldContainer Create(BaseContainer parent)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, null, Anchor.FullStretch, Offset.Default);
		return inputFieldContainer;
	}

	public new static InputFieldContainer Create(BaseContainer parent, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, null, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public new static InputFieldContainer Create(BaseContainer parent, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, null, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public new static InputFieldContainer Create(BaseContainer parent, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, null, anchor, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, string text)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, text, null, Anchor.FullStretch, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, string text, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, text, null, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, string text, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, text, null, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, string text, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, text, null, anchor, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, Style style)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, style, Anchor.FullStretch, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, Style style, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, style, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, Style style, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, style, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, Style style, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, string.Empty, style, anchor, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, string text, Style style, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, text, style, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, string text, Style style, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, text, style, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(BaseContainer parent, string text, Style style, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(parent, text, style, anchor, offset);
		return inputFieldContainer;
	}

	public new static InputFieldContainer Create(string name, Layer layer)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, null, Anchor.FullStretch, Offset.Default);
		return inputFieldContainer;
	}

	public new static InputFieldContainer Create(string name, Layer layer, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, null, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public new static InputFieldContainer Create(string name, Layer layer, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, null, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public new static InputFieldContainer Create(string name, Layer layer, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, null, anchor, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, string text)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, text, null, Anchor.FullStretch, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, string text, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, text, null, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, string text, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, text, null, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, string text, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, text, null, anchor, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, Style style)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, style, Anchor.FullStretch, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, Style style, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, style, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, Style style, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, style, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, Style style, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, string.Empty, style, anchor, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, string text, Style style, Anchor anchor)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, text, style, anchor, Offset.Default);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, string text, Style style, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, text, style, Anchor.FullStretch, offset);
		return inputFieldContainer;
	}

	public static InputFieldContainer Create(string name, Layer layer, string text, Style style, Anchor anchor, Offset offset)
	{
		InputFieldContainer inputFieldContainer = UIContainerPool.Get<InputFieldContainer>();
		inputFieldContainer.Initialize(name, layer, text, style, anchor, offset);
		return inputFieldContainer;
	}

	public InputFieldContainer WithColor(Color color)
	{
		m_Component.Color = color;
		return this;
	}

	public InputFieldContainer WithFont(Font font)
	{
		m_Component.Font = font;
		return this;
	}

	public InputFieldContainer WithSize(int size)
	{
		m_Component.FontSize = size;
		return this;
	}

	public InputFieldContainer WithText(string text)
	{
		m_Component.Text = text;
		return this;
	}

	public InputFieldContainer WithAlignment(TextAnchor alignment)
	{
		m_Component.Alignment = alignment;
		return this;
	}

	public InputFieldContainer WithLineType(InputField.LineType lineType)
	{
		m_Component.LineType = lineType;
		return this;
	}

	public InputFieldContainer WithCharacterLimit(int characterLimit)
	{
		m_Component.CharacterLimit = characterLimit;
		return this;
	}

	public new InputFieldContainer NeedsKeyboard()
	{
		m_Component.NeedsKeyboard = true;
		return this;
	}

	public InputFieldContainer InHudMenu()
	{
		m_Component.HudMenuInput = true;
		return this;
	}

	public InputFieldContainer Autofocus()
	{
		m_Component.AutoFocus = true;
		return this;
	}

	public InputFieldContainer AsPassword()
	{
		m_Component.IsPassword = true;
		return this;
	}

	public InputFieldContainer WithFadeIn(float fadeIn)
	{
		m_Component.FadeIn = fadeIn;
		return this;
	}

	public InputFieldContainer WithCommand(string command)
	{
		m_Component.Command = command;
		return this;
	}

	public InputFieldContainer WithCallback(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, string identifier = "")
	{
		m_Component.SetCommand(commandCallbackHandler, callback, identifier);
		return this;
	}

	public InputFieldContainer WithSecureCallback(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, ulong userId, string identifier = "")
	{
		m_Component.SetSecureCommand(commandCallbackHandler, callback, userId, identifier);
		return this;
	}

	public InputFieldContainer WithStyle(Style style)
	{
		if (style != null)
		{
			while (true)
			{
				int num = -32548136;
				while (true)
				{
					uint num2;
					switch ((num2 = (uint)(num ^ -2049922083)) % 3)
					{
					case 0u:
						break;
					case 1u:
						m_Component.WithStyle(style);
						num = ((int)num2 * -1207493152) ^ 0x63CFE73B;
						continue;
					default:
						goto end_IL_0003;
					}
					break;
				}
				continue;
				end_IL_0003:
				break;
			}
		}
		return this;
	}

	public override void OnEnterPool()
	{
		if (m_Component != null)
		{
			UIComponentPool.Free(ref m_Component);
			m_Component = null;
		}
		base.OnEnterPool();
	}
}
