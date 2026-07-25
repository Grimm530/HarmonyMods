using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class Style
{
	public Color ImageColor;

	public Color FontColor;

	public string Texture;

	public string Sprite;

	public string Material;

	public Image.Type ImageType;

	public Font Font;

	public int FontSize;

	public TextAnchor Alignment;

	public VerticalWrapMode WrapMode;

	public InputField.LineType LineType;

	public Color OutlineColor;

	public Bounds EffectDistance;

	public bool UseGraphicAlpha;

	public static Style Default = new Style();

	public Style()
	{
		ImageColor = Color.DEFAULT;
		FontColor = Color.DEFAULT;
		Texture = "assets/content/textures/generic/fulltransparent.tga";
		Sprite = "assets/content/ui/ui.background.tile.psd";
		Material = "assets/icons/iconmaterial.mat";
		ImageType = Image.Type.Simple;
		Font = Font.RobotoCondensedBold;
		FontSize = 14;
		Alignment = TextAnchor.UpperLeft;
		WrapMode = VerticalWrapMode.Truncate;
		LineType = InputField.LineType.SingleLine;
		OutlineColor = Color.Black;
		EffectDistance = OutlineComponent.DefaultDistance;
		UseGraphicAlpha = false;
	}

	public Style(Style copyFrom)
	{
		ImageColor = copyFrom.ImageColor;
		FontColor = copyFrom.FontColor;
		Texture = copyFrom.Texture;
		Sprite = copyFrom.Sprite;
		Material = copyFrom.Material;
		ImageType = copyFrom.ImageType;
		Font = copyFrom.Font;
		FontSize = copyFrom.FontSize;
		Alignment = copyFrom.Alignment;
		WrapMode = copyFrom.WrapMode;
		LineType = copyFrom.LineType;
		OutlineColor = copyFrom.OutlineColor;
		EffectDistance = copyFrom.EffectDistance;
		UseGraphicAlpha = copyFrom.UseGraphicAlpha;
	}
}
