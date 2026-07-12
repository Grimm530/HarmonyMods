using Oxide.Game.Rust.Cui;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class ImageContainer : BaseContainer
{
	private ImageComponent m_Component;

	protected void Initialize(BaseContainer parent, Style style, Anchor anchor, Offset offset)
	{
		Initialize(CuiHelper.GetGuid(), parent, anchor, offset);
		Element.Components.Add(m_Component = UIComponentPool.Get<ImageComponent>());
		if (style != null)
			WithStyle(style);
	}

	protected void Initialize(string name, Layer layer, Style style, Anchor anchor, Offset offset)
	{
		Initialize(name, layer, anchor, offset);
		Element.Components.Add(m_Component = UIComponentPool.Get<ImageComponent>());
		if (style != null)
			WithStyle(style);
	}

	public new static ImageContainer Create(BaseContainer parent)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, null, Anchor.FullStretch, Offset.Default);
		return imageContainer;
	}

	public new static ImageContainer Create(BaseContainer parent, Anchor anchor)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, null, anchor, Offset.Default);
		return imageContainer;
	}

	public new static ImageContainer Create(BaseContainer parent, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, null, Anchor.FullStretch, offset);
		return imageContainer;
	}

	public new static ImageContainer Create(BaseContainer parent, Anchor anchor, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, null, anchor, offset);
		return imageContainer;
	}

	public static ImageContainer Create(BaseContainer parent, Style style)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, style, Anchor.FullStretch, Offset.Default);
		return imageContainer;
	}

	public static ImageContainer Create(BaseContainer parent, Style style, Anchor anchor)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, style, anchor, Offset.Default);
		return imageContainer;
	}

	public static ImageContainer Create(BaseContainer parent, Style style, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, style, Anchor.FullStretch, offset);
		return imageContainer;
	}

	public static ImageContainer Create(BaseContainer parent, Style style, Anchor anchor, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(parent, style, anchor, offset);
		return imageContainer;
	}

	public new static ImageContainer Create(string name, Layer layer, Anchor anchor)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(name, layer, null, anchor, Offset.Default);
		return imageContainer;
	}

	public new static ImageContainer Create(string name, Layer layer, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(name, layer, null, Anchor.FullStretch, offset);
		return imageContainer;
	}

	public new static ImageContainer Create(string name, Layer layer, Anchor anchor, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(name, layer, null, anchor, offset);
		return imageContainer;
	}

	public static ImageContainer Create(string name, Layer layer, Style style)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(name, layer, style, Anchor.FullStretch, Offset.Default);
		return imageContainer;
	}

	public static ImageContainer Create(string name, Layer layer, Style style, Anchor anchor)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(name, layer, style, anchor, Offset.Default);
		return imageContainer;
	}

	public static ImageContainer Create(string name, Layer layer, Style style, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(name, layer, style, Anchor.FullStretch, offset);
		return imageContainer;
	}

	public static ImageContainer Create(string name, Layer layer, Style style, Anchor anchor, Offset offset)
	{
		ImageContainer imageContainer = UIContainerPool.Get<ImageContainer>();
		imageContainer.Initialize(name, layer, style, anchor, offset);
		return imageContainer;
	}

	public ImageContainer WithSprite(string sprite)
	{
		m_Component.Sprite = sprite;
		return this;
	}

	public ImageContainer WithMaterial(string material)
	{
		m_Component.Material = material;
		return this;
	}

	public ImageContainer WithColor(Color color)
	{
		m_Component.Color = color;
		return this;
	}

	public ImageContainer WithPNG(string png)
	{
		m_Component.PNG = png;
		return this;
	}

	public ImageContainer WithIcon(int itemId, ulong skinId = 0uL)
	{
		m_Component.ItemID = itemId;
		m_Component.SkinID = skinId;
		return this;
	}

	public ImageContainer WithImageType(Image.Type imageType)
	{
		m_Component.ImageType = imageType;
		return this;
	}

	public ImageContainer WithFadeIn(float fadeIn)
	{
		m_Component.FadeIn = fadeIn;
		return this;
	}

	public ImageContainer WithStyle(Style style)
	{
		if (style != null)
			m_Component.WithStyle(style);
		return this;
	}

	public override void OnEnterPool()
	{
		if (m_Component != null)
		{
			ImageComponent t = m_Component;
			UIComponentPool.Free(ref t);
			m_Component = null;
		}
		base.OnEnterPool();
	}
}
