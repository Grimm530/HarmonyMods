using Oxide.Game.Rust.Cui;

namespace Oxide.Ext.Chaos.UIFramework;

public class RawImageContainer : BaseContainer
{
	private RawImageComponent m_Component;

	protected void Initialize(string name, BaseContainer parent, Style style, Anchor anchor, Offset offset)
	{
		Initialize(name, parent, anchor, offset);
		base.Element.Components.Add(m_Component = UIComponentPool.Get<RawImageComponent>());
		if (style == null)
		{
			return;
		}
		while (true)
		{
			int num = 1176316231;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x7D1AB6E1)) % 3)
				{
				case 0u:
					break;
				default:
					return;
				case 1u:
					goto IL_004f;
				case 2u:
					return;
				}
				break;
				IL_004f:
				WithStyle(style);
				num = (int)(num2 * 482575217) ^ -795403572;
			}
		}
	}

	protected void Initialize(string name, Layer layer, Style style, Anchor anchor, Offset offset)
	{
		Initialize(name, layer, anchor, offset);
		while (true)
		{
			int num = -61228872;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -1076056367)) % 4)
				{
				case 2u:
					break;
				default:
					return;
				case 1u:
				{
					base.Element.Components.Add(m_Component = UIComponentPool.Get<RawImageComponent>());
					int num3;
					int num4;
					if (style == null)
					{
						num3 = 451890128;
						num4 = num3;
					}
					else
					{
						num3 = 1881985859;
						num4 = num3;
					}
					num = num3 ^ (int)(num2 * 1842965701);
					continue;
				}
				case 3u:
					WithStyle(style);
					num = ((int)num2 * -886415771) ^ 0x2B67BB66;
					continue;
				case 0u:
					return;
				}
				break;
			}
		}
	}

	public new static RawImageContainer Create(BaseContainer parent)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, null, Anchor.FullStretch, Offset.Default);
		return rawImageContainer;
	}

	public new static RawImageContainer Create(BaseContainer parent, Anchor anchor)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, null, anchor, Offset.Default);
		return rawImageContainer;
	}

	public new static RawImageContainer Create(BaseContainer parent, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, null, Anchor.FullStretch, offset);
		return rawImageContainer;
	}

	public new static RawImageContainer Create(BaseContainer parent, Anchor anchor, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, null, anchor, offset);
		return rawImageContainer;
	}

	public static RawImageContainer Create(BaseContainer parent, Style style)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, style, Anchor.FullStretch, Offset.Default);
		return rawImageContainer;
	}

	public static RawImageContainer Create(BaseContainer parent, Style style, Anchor anchor)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, style, anchor, Offset.Default);
		return rawImageContainer;
	}

	public static RawImageContainer Create(BaseContainer parent, Style style, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, style, Anchor.FullStretch, offset);
		return rawImageContainer;
	}

	public static RawImageContainer Create(BaseContainer parent, Style style, Anchor anchor, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(CuiHelper.GetGuid(), parent, style, anchor, offset);
		return rawImageContainer;
	}

	public new static RawImageContainer Create(string name, Layer layer)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, null, Anchor.FullStretch, Offset.Default);
		return rawImageContainer;
	}

	public new static RawImageContainer Create(string name, Layer layer, Anchor anchor)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, null, anchor, Offset.Default);
		return rawImageContainer;
	}

	public new static RawImageContainer Create(string name, Layer layer, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, null, Anchor.FullStretch, offset);
		return rawImageContainer;
	}

	public new static RawImageContainer Create(string name, Layer layer, Anchor anchor, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, null, anchor, offset);
		return rawImageContainer;
	}

	public static RawImageContainer Create(string name, Layer layer, Style style)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, style, Anchor.FullStretch, Offset.Default);
		return rawImageContainer;
	}

	public static RawImageContainer Create(string name, Layer layer, Style style, Anchor anchor)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, style, anchor, Offset.Default);
		return rawImageContainer;
	}

	public static RawImageContainer Create(string name, Layer layer, Style style, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, style, Anchor.FullStretch, offset);
		return rawImageContainer;
	}

	public static RawImageContainer Create(string name, Layer layer, Style style, Anchor anchor, Offset offset)
	{
		RawImageContainer rawImageContainer = UIContainerPool.Get<RawImageContainer>();
		rawImageContainer.Initialize(name, layer, style, anchor, offset);
		return rawImageContainer;
	}

	public RawImageContainer WithMaterial(string material)
	{
		m_Component.Material = material;
		return this;
	}

	public RawImageContainer WithTexture(string texture)
	{
		m_Component.Texture = texture;
		return this;
	}

	public RawImageContainer WithColor(Color color)
	{
		m_Component.Color = color;
		return this;
	}

	public RawImageContainer WithPNG(string png)
	{
		m_Component.PNG = png;
		return this;
	}

	public RawImageContainer WithSteamId(ulong steamId)
	{
		m_Component.SteamId = steamId;
		return this;
	}

	public RawImageContainer WithURL(string url)
	{
		m_Component.URL = url;
		return this;
	}

	public RawImageContainer WithFadeIn(float fadeIn)
	{
		m_Component.FadeIn = fadeIn;
		return this;
	}

	public RawImageContainer WithStyle(Style style)
	{
		if (style != null)
		{
			while (true)
			{
				int num = -973095966;
				while (true)
				{
					uint num2;
					switch ((num2 = (uint)(num ^ -1854176293)) % 3)
					{
					case 0u:
						break;
					case 2u:
						m_Component.WithStyle(style);
						num = ((int)num2 * -1945010908) ^ -143747030;
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
