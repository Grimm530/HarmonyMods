using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class TextContainer : BaseContainer
{
	private TextComponent m_Component;

	protected void Initialize(BaseContainer parent, string text, Style style, Anchor anchor, Offset offset)
	{
		Initialize(CuiHelper.GetGuid(), parent, anchor, offset);
		base.Element.Components.Add(m_Component = UIComponentPool.Get<TextComponent>());
		if (style != null)
		{
			while (true)
			{
				int num = 862959830;
				while (true)
				{
					uint num2;
					switch ((num2 = (uint)(num ^ 0x17DE4667)) % 3)
					{
					case 0u:
						break;
					case 2u:
						WithStyle(style);
						num = (int)(num2 * 427073026) ^ -310040098;
						continue;
					default:
						goto end_IL_0031;
					}
					break;
				}
				continue;
				end_IL_0031:
				break;
			}
		}
		m_Component.Text = text;
	}

	protected void Initialize(string name, Layer layer, string text, Style style, Anchor anchor, Offset offset)
	{
		Initialize(name, layer, anchor, offset);
		base.Element.Components.Add(m_Component = UIComponentPool.Get<TextComponent>());
		while (true)
		{
			int num = 462163540;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x42B9BC89)) % 5)
				{
				case 4u:
					break;
				default:
					return;
				case 1u:
				{
					int num3;
					int num4;
					if (style != null)
					{
						num3 = -402197239;
						num4 = num3;
					}
					else
					{
						num3 = -153149802;
						num4 = num3;
					}
					num = num3 ^ (int)(num2 * 281012686);
					continue;
				}
				case 3u:
					WithStyle(style);
					num = (int)(num2 * 1010035102) ^ -878794412;
					continue;
				case 2u:
					m_Component.Text = text;
					num = 1621953786;
					continue;
				case 0u:
					return;
				}
				break;
			}
		}
	}

	public new static TextContainer Create(BaseContainer parent)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, null, Anchor.FullStretch, Offset.Default);
		return textContainer;
	}

	public new static TextContainer Create(BaseContainer parent, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, null, anchor, Offset.Default);
		return textContainer;
	}

	public new static TextContainer Create(BaseContainer parent, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, null, Anchor.FullStretch, offset);
		return textContainer;
	}

	public new static TextContainer Create(BaseContainer parent, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, null, anchor, offset);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, string text)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, text, null, Anchor.FullStretch, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, string text, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, text, null, anchor, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, string text, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, text, null, Anchor.FullStretch, offset);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, string text, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, text, null, anchor, offset);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, Style style)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, style, Anchor.FullStretch, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, Style style, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, style, anchor, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, Style style, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, style, Anchor.FullStretch, offset);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, Style style, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, string.Empty, style, anchor, offset);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, string text, Style style, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, text, style, anchor, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, string text, Style style, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, text, style, Anchor.FullStretch, offset);
		return textContainer;
	}

	public static TextContainer Create(BaseContainer parent, string text, Style style, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(parent, text, style, anchor, offset);
		return textContainer;
	}

	public new static TextContainer Create(string name, Layer layer)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, null, Anchor.FullStretch, Offset.Default);
		return textContainer;
	}

	public new static TextContainer Create(string name, Layer layer, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, null, anchor, Offset.Default);
		return textContainer;
	}

	public new static TextContainer Create(string name, Layer layer, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, null, Anchor.FullStretch, offset);
		return textContainer;
	}

	public new static TextContainer Create(string name, Layer layer, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, null, anchor, offset);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, string text)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, text, null, Anchor.FullStretch, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, string text, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, text, null, anchor, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, string text, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, text, null, Anchor.FullStretch, offset);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, string text, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, text, null, anchor, offset);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, Style style)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, style, Anchor.FullStretch, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, Style style, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, style, anchor, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, Style style, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, style, Anchor.FullStretch, offset);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, Style style, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, string.Empty, style, anchor, offset);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, string text, Style style, Anchor anchor)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, text, style, anchor, Offset.Default);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, string text, Style style, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, text, style, Anchor.FullStretch, offset);
		return textContainer;
	}

	public static TextContainer Create(string name, Layer layer, string text, Style style, Anchor anchor, Offset offset)
	{
		TextContainer textContainer = UIContainerPool.Get<TextContainer>();
		textContainer.Initialize(name, layer, text, style, anchor, offset);
		return textContainer;
	}

	public TextContainer WithText(string text)
	{
		m_Component.Text = text;
		return this;
	}

	public TextContainer WithColor(Color color)
	{
		m_Component.Color = color;
		return this;
	}

	public TextContainer WithFont(Font font)
	{
		m_Component.Font = font;
		return this;
	}

	public TextContainer WithSize(int size)
	{
		m_Component.FontSize = size;
		return this;
	}

	public TextContainer WithAlignment(TextAnchor alignment)
	{
		m_Component.Alignment = alignment;
		return this;
	}

	public TextContainer WithWrapMode(VerticalWrapMode wrapMode)
	{
		m_Component.VerticalOverflow = wrapMode;
		return this;
	}

	public TextContainer WithFadeIn(float fadeIn)
	{
		m_Component.FadeIn = fadeIn;
		return this;
	}

	public TextContainer WithStyle(Style style)
	{
		if (style != null)
		{
			while (true)
			{
				int num = -2041384228;
				while (true)
				{
					uint num2;
					switch ((num2 = (uint)(num ^ -1542560651)) % 3)
					{
					case 0u:
						break;
					case 1u:
						m_Component.WithStyle(style);
						num = ((int)num2 * -1396583777) ^ -1505937091;
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
