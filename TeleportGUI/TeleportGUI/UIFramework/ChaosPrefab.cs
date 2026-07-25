using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public static class ChaosPrefab
{
	public static BaseContainer Background(string name, Layer layer, Anchor anchor, Offset offset, StylePreset stylePreset = null)
	{
		return ImageContainer.Create(name, layer, anchor, offset)
			.WithStyle(stylePreset?.Background ?? ChaosStyle.Background)
			.WithStylePreset(stylePreset);
	}

	public static BaseContainer Background(string name, Layer layer, Anchor anchor, Offset offset, Style style)
	{
		return ImageContainer.Create(name, layer, anchor, offset)
			.WithStyle(style ?? ChaosStyle.Background);
	}

	public static BaseContainer Panel(BaseContainer parent, Anchor anchor, Offset offset, StylePreset stylePreset = null)
	{
		return ImageContainer.Create(parent, anchor, offset)
			.WithStyle(stylePreset?.Panel ?? parent.StylePreset?.Panel ?? ChaosStyle.Panel)
			.WithStylePreset(stylePreset);
	}

	public static BaseContainer Panel(BaseContainer parent, Anchor anchor, Offset offset, Style style)
	{
		return ImageContainer.Create(parent, anchor, offset)
			.WithStyle(style ?? parent.StylePreset?.Panel ?? ChaosStyle.Panel);
	}

	public static BaseContainer Title(BaseContainer parent, Anchor anchor, Offset offset, string text, StylePreset stylePreset = null)
	{
		return TextContainer.Create(parent, anchor, offset)
			.WithText(text)
			.WithStyle(stylePreset?.Title ?? parent.StylePreset?.Title ?? ChaosStyle.Title)
			.WithStylePreset(stylePreset);
	}

	public static BaseContainer Title(BaseContainer parent, Anchor anchor, Offset offset, string text, Style style)
	{
		return TextContainer.Create(parent, anchor, offset)
			.WithText(text)
			.WithStyle(style ?? parent.StylePreset?.Title ?? ChaosStyle.Title);
	}

	public static InputFieldContainer Input(BaseContainer parent, Anchor anchor, Offset offset, string value, StylePreset stylePreset = null)
	{
		InputFieldContainer field = null;
		Style style = stylePreset?.Button ?? parent.StylePreset?.Button ?? ChaosStyle.Button;

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(style)
			.WithStylePreset(stylePreset)
			.WithChildren(container =>
			{
				field = InputFieldContainer.Create(container, Anchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
					.WithText(value)
					.WithStyle(style)
					.WithAlignment(TextAnchor.MiddleLeft);
			});

		return field;
	}

	public static InputFieldContainer Input(BaseContainer parent, Anchor anchor, Offset offset, string value, Style style)
	{
		InputFieldContainer field = null;
		Style resolved = style ?? parent.StylePreset?.Button ?? ChaosStyle.Button;

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(resolved)
			.WithChildren(container =>
			{
				field = InputFieldContainer.Create(container, Anchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
					.WithText(value)
					.WithStyle(resolved)
					.WithAlignment(TextAnchor.MiddleLeft);
			});

		return field;
	}

	public static ButtonContainer Toggle(BaseContainer parent, Anchor anchor, Offset offset, bool value, StylePreset stylePreset = null)
	{
		ButtonContainer button = null;
		Style buttonStyle = stylePreset?.Button ?? parent.StylePreset?.Button ?? ChaosStyle.Button;
		Style toggleStyle = stylePreset?.Toggle ?? parent.StylePreset?.Toggle ?? ChaosStyle.Toggle;

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(buttonStyle)
			.WithStylePreset(stylePreset)
			.WithChildren(container =>
			{
				if (value)
				{
					ImageContainer.Create(container, Anchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
						.WithStyle(toggleStyle);
				}

				button = ButtonContainer.Create(container, Anchor.FullStretch, Offset.zero)
					.WithColor(Color.Clear);
			});

		return button;
	}

	public static ButtonContainer Toggle(BaseContainer parent, Anchor anchor, Offset offset, bool value, Style style)
	{
		return Toggle(parent, anchor, offset, value, style, null);
	}

	public static ButtonContainer Toggle(BaseContainer parent, Anchor anchor, Offset offset, bool value, Style style, Style toggleStyle)
	{
		ButtonContainer button = null;
		Style buttonStyle = style ?? parent.StylePreset?.Button ?? ChaosStyle.Button;
		Style resolvedToggle = toggleStyle ?? parent.StylePreset?.Toggle ?? ChaosStyle.Toggle;

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(buttonStyle)
			.WithChildren(container =>
			{
				if (value)
				{
					ImageContainer.Create(container, Anchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
						.WithStyle(resolvedToggle);
				}

				button = ButtonContainer.Create(container, Anchor.FullStretch, Offset.zero)
					.WithColor(Color.Clear);
			});

		return button;
	}

	public static ButtonContainer NextPage(BaseContainer parent, Anchor anchor, Offset offset, bool hasNextPage, StylePreset stylePreset = null)
	{
		ButtonContainer button = null;
		Style style = hasNextPage
			? (stylePreset?.Button ?? parent.StylePreset?.Button ?? ChaosStyle.Button)
			: (stylePreset?.DisabledButton ?? parent.StylePreset?.DisabledButton ?? ChaosStyle.DisabledButton);

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(style)
			.WithStylePreset(stylePreset)
			.WithChildren(back =>
			{
				TextContainer.Create(back, Anchor.FullStretch, Offset.zero)
					.WithText(">")
					.WithStyle(style);

				if (hasNextPage)
				{
					button = ButtonContainer.Create(back, Anchor.FullStretch, Offset.zero)
						.WithColor(Color.Clear);
				}
			});

		return button;
	}

	public static ButtonContainer NextPage(BaseContainer parent, Anchor anchor, Offset offset, bool hasNextPage, Style style)
	{
		ButtonContainer button = null;
		Style resolved = style ?? parent.StylePreset?.Button ?? ChaosStyle.Button;

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(resolved)
			.WithChildren(back =>
			{
				TextContainer.Create(back, Anchor.FullStretch, Offset.zero)
					.WithText(">")
					.WithStyle(resolved);

				if (hasNextPage)
				{
					button = ButtonContainer.Create(back, Anchor.FullStretch, Offset.zero)
						.WithColor(Color.Clear);
				}
			});

		return button;
	}

	public static ButtonContainer PreviousPage(BaseContainer parent, Anchor anchor, Offset offset, bool hasPreviousPage, StylePreset stylePreset = null)
	{
		ButtonContainer button = null;
		Style style = hasPreviousPage
			? (stylePreset?.Button ?? parent.StylePreset?.Button ?? ChaosStyle.Button)
			: (stylePreset?.DisabledButton ?? parent.StylePreset?.DisabledButton ?? ChaosStyle.DisabledButton);

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(style)
			.WithStylePreset(stylePreset)
			.WithChildren(back =>
			{
				TextContainer.Create(back, Anchor.FullStretch, Offset.zero)
					.WithText("<")
					.WithStyle(style);

				if (hasPreviousPage)
				{
					button = ButtonContainer.Create(back, Anchor.FullStretch, Offset.zero)
						.WithColor(Color.Clear);
				}
			});

		return button;
	}

	public static ButtonContainer PreviousPage(BaseContainer parent, Anchor anchor, Offset offset, bool hasPreviousPage, Style style)
	{
		ButtonContainer button = null;
		Style resolved = style ?? parent.StylePreset?.Button ?? ChaosStyle.Button;

		ImageContainer.Create(parent, anchor, offset)
			.WithStyle(resolved)
			.WithChildren(back =>
			{
				TextContainer.Create(back, Anchor.FullStretch, Offset.zero)
					.WithText("<")
					.WithStyle(resolved);

				if (hasPreviousPage)
				{
					button = ButtonContainer.Create(back, Anchor.FullStretch, Offset.zero)
						.WithColor(Color.Clear);
				}
			});

		return button;
	}

	public static ButtonContainer TextButton(BaseContainer parent, Anchor anchor, Offset offset, string text, bool enabled = true, StylePreset stylePreset = null, OutlineComponent outlineComponent = null)
	{
		ButtonContainer button = null;
		Style style = enabled
			? (stylePreset?.Button ?? parent.StylePreset?.Button ?? ChaosStyle.Button)
			: (stylePreset?.DisabledButton ?? parent.StylePreset?.DisabledButton ?? ChaosStyle.DisabledButton);

		BaseContainer panel = ImageContainer.Create(parent, anchor, offset)
			.WithStyle(style)
			.WithStylePreset(stylePreset)
			.WithChildren(container =>
			{
				TextContainer.Create(container, Anchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
					.WithText(text)
					.WithStyle(style);

				if (enabled)
				{
					button = ButtonContainer.Create(container, Anchor.FullStretch, Offset.zero)
						.WithColor(Color.Clear);
				}
			});

		if (outlineComponent != null)
			panel.WithOutline(outlineComponent);

		return button;
	}

	public static ButtonContainer TextButton(BaseContainer parent, Anchor anchor, Offset offset, string text, Style style, OutlineComponent outlineComponent = null)
	{
		ButtonContainer button = null;
		Style resolved = style ?? parent.StylePreset?.Button ?? ChaosStyle.Button;

		BaseContainer panel = ImageContainer.Create(parent, anchor, offset)
			.WithStyle(resolved)
			.WithChildren(container =>
			{
				TextContainer.Create(container, Anchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
					.WithText(text)
					.WithStyle(resolved);

				button = ButtonContainer.Create(container, Anchor.FullStretch, Offset.zero)
					.WithColor(Color.Clear);
			});

		if (outlineComponent != null)
			panel.WithOutline(outlineComponent);

		return button;
	}

	public static ButtonContainer CloseButton(BaseContainer parent, Anchor anchor, Offset offset, OutlineComponent outlineComponent = null)
	{
		ButtonContainer button = null;
		BaseContainer panel = ImageContainer.Create(parent, anchor, offset)
			.WithStyle(parent.StylePreset?.Button ?? ChaosStyle.Button)
			.WithChildren(container =>
			{
				TextContainer.Create(container, Anchor.FullStretch, Offset.zero)
					.WithText("✕")
					.WithStyle(parent.StylePreset?.Close ?? ChaosStyle.Close);

				button = ButtonContainer.Create(container, Anchor.FullStretch, Offset.zero)
					.WithColor(Color.Clear);
			});

		if (outlineComponent != null)
			panel.WithOutline(outlineComponent);

		return button;
	}

	public static ButtonContainer SpriteButton(BaseContainer parent, Anchor anchor, Offset offset, string sprite, Anchor imageAnchor, Offset imageOffset, StylePreset stylePreset = null, OutlineComponent outlineComponent = null)
	{
		ButtonContainer button = null;
		BaseContainer panel = ImageContainer.Create(parent, anchor, offset)
			.WithStyle(stylePreset?.Button ?? parent.StylePreset?.Button ?? ChaosStyle.Button)
			.WithStylePreset(stylePreset)
			.WithChildren(container =>
			{
				ImageContainer.Create(container, imageAnchor, imageOffset).WithSprite(sprite);
				button = ButtonContainer.Create(container, Anchor.FullStretch, Offset.zero)
					.WithColor(Color.Clear);
			});

		if (outlineComponent != null)
			panel.WithOutline(outlineComponent);

		return button;
	}

	public static ButtonContainer SpriteButton(BaseContainer parent, Anchor anchor, Offset offset, string sprite, Anchor imageAnchor, Offset imageOffset, Style style, OutlineComponent outlineComponent = null)
	{
		ButtonContainer button = null;
		BaseContainer panel = ImageContainer.Create(parent, anchor, offset)
			.WithStyle(style ?? parent.StylePreset?.Button ?? ChaosStyle.Button)
			.WithChildren(container =>
			{
				ImageContainer.Create(container, imageAnchor, imageOffset).WithSprite(sprite);
				button = ButtonContainer.Create(container, Anchor.FullStretch, Offset.zero)
					.WithColor(Color.Clear);
			});

		if (outlineComponent != null)
			panel.WithOutline(outlineComponent);

		return button;
	}
}
