using System;
using Oxide.Game.Rust.Cui;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class ButtonContainer : BaseContainer
{
	private ButtonComponent m_Component;

	protected void Initialize(BaseContainer parent, string command, Style style, Anchor anchor, Offset offset)
	{
		Initialize(CuiHelper.GetGuid(), parent, anchor, offset);
		Element.Components.Add(m_Component = UIComponentPool.Get<ButtonComponent>());
		if (style != null)
			WithStyle(style);
		m_Component.Command = command;
	}

	protected void Initialize(string name, Layer layer, string command, Style style, Anchor anchor, Offset offset)
	{
		Initialize(name, layer, anchor, offset);
		Element.Components.Add(m_Component = UIComponentPool.Get<ButtonComponent>());
		if (style != null)
			WithStyle(style);
		m_Component.Command = command;
	}

	public new static ButtonContainer Create(BaseContainer parent)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, null, Anchor.FullStretch, Offset.Default);
		return buttonContainer;
	}

	public new static ButtonContainer Create(BaseContainer parent, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, null, anchor, Offset.Default);
		return buttonContainer;
	}

	public new static ButtonContainer Create(BaseContainer parent, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, null, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public new static ButtonContainer Create(BaseContainer parent, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, null, anchor, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, string command)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, command, null, Anchor.FullStretch, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, string command, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, command, null, anchor, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, string command, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, command, null, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, string command, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, command, null, anchor, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, Style style)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, style, Anchor.FullStretch, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, Style style, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, style, anchor, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, Style style, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, style, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, Style style, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, string.Empty, style, anchor, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, string command, Style style, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, command, style, anchor, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, string command, Style style, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, command, style, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(BaseContainer parent, string command, Style style, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(parent, command, style, anchor, offset);
		return buttonContainer;
	}

	public new static ButtonContainer Create(string name, Layer layer)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, null, Anchor.FullStretch, Offset.Default);
		return buttonContainer;
	}

	public new static ButtonContainer Create(string name, Layer layer, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, null, anchor, Offset.Default);
		return buttonContainer;
	}

	public new static ButtonContainer Create(string name, Layer layer, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, null, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public new static ButtonContainer Create(string name, Layer layer, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, null, anchor, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, string command)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, command, null, Anchor.FullStretch, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, string command, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, command, null, anchor, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, string command, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, command, null, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, string command, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, command, null, anchor, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, Style style)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, style, Anchor.FullStretch, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, Style style, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, style, anchor, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, Style style, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, style, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, Style style, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, string.Empty, style, anchor, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, string command, Style style, Anchor anchor)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, command, style, anchor, Offset.Default);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, string command, Style style, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, command, style, Anchor.FullStretch, offset);
		return buttonContainer;
	}

	public static ButtonContainer Create(string name, Layer layer, string command, Style style, Anchor anchor, Offset offset)
	{
		ButtonContainer buttonContainer = UIContainerPool.Get<ButtonContainer>();
		buttonContainer.Initialize(name, layer, command, style, anchor, offset);
		return buttonContainer;
	}

	public ButtonContainer WithColor(Color color)
	{
		m_Component.Color = color;
		return this;
	}

	public ButtonContainer CloseOnPress()
	{
		m_Component.Close = true;
		return this;
	}

	public ButtonContainer WithSprite(string sprite)
	{
		m_Component.Sprite = sprite;
		return this;
	}

	public ButtonContainer WithImageType(Image.Type imageType)
	{
		m_Component.ImageType = imageType;
		return this;
	}

	public ButtonContainer WithMaterial(string material)
	{
		m_Component.Material = material;
		return this;
	}

	public ButtonContainer WithFadeIn(float fadeIn)
	{
		m_Component.FadeIn = fadeIn;
		return this;
	}

	public ButtonContainer WithStyle(Style style)
	{
		if (style != null)
			m_Component.WithStyle(style);
		return this;
	}

	public ButtonContainer WithCommand(string command)
	{
		m_Component.Command = command;
		return this;
	}

	public ButtonContainer WithCallback(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, string identifier = "")
	{
		m_Component.SetCommand(commandCallbackHandler, callback, identifier);
		return this;
	}

	public ButtonContainer WithSecureCallback(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, ulong userId, string identifier = "")
	{
		m_Component.SetSecureCommand(commandCallbackHandler, callback, userId, identifier);
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
