using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

/// <summary>
/// Maps UIFramework enums to the string values written into CUI JSON.
/// </summary>
internal static class EnumConverters
{
	private static readonly Dictionary<TextAnchor, string> TextAnchors = new()
	{
		[TextAnchor.LowerCenter] = nameof(TextAnchor.LowerCenter),
		[TextAnchor.LowerLeft] = nameof(TextAnchor.LowerLeft),
		[TextAnchor.LowerRight] = nameof(TextAnchor.LowerRight),
		[TextAnchor.MiddleCenter] = nameof(TextAnchor.MiddleCenter),
		[TextAnchor.MiddleLeft] = nameof(TextAnchor.MiddleLeft),
		[TextAnchor.MiddleRight] = nameof(TextAnchor.MiddleRight),
		[TextAnchor.UpperCenter] = nameof(TextAnchor.UpperCenter),
		[TextAnchor.UpperLeft] = nameof(TextAnchor.UpperLeft),
		[TextAnchor.UpperRight] = nameof(TextAnchor.UpperRight)
	};

	private static readonly Dictionary<VerticalWrapMode, string> VerticalWrapModes = new()
	{
		[VerticalWrapMode.Overflow] = nameof(VerticalWrapMode.Overflow),
		[VerticalWrapMode.Truncate] = nameof(VerticalWrapMode.Truncate)
	};

	private static readonly Dictionary<Image.Type, string> ImageTypes = new()
	{
		[Image.Type.Filled] = nameof(Image.Type.Filled),
		[Image.Type.Simple] = nameof(Image.Type.Simple),
		[Image.Type.Sliced] = nameof(Image.Type.Sliced),
		[Image.Type.Tiled] = nameof(Image.Type.Tiled)
	};

	private static readonly Dictionary<InputField.LineType, string> LineTypes = new()
	{
		[InputField.LineType.SingleLine] = nameof(InputField.LineType.SingleLine),
		[InputField.LineType.MultiLineNewline] = nameof(InputField.LineType.MultiLineNewline),
		[InputField.LineType.MultiLineSubmit] = nameof(InputField.LineType.MultiLineSubmit)
	};

	// HudMenu must be "Hud.Menu" — enum ToString() would emit "HudMenu".
	private static readonly Dictionary<Layer, string> Layers = new()
	{
		[Layer.Overall] = "Overall",
		[Layer.Overlay] = "Overlay",
		[Layer.Hud] = "Hud",
		[Layer.HudMenu] = "Hud.Menu",
		[Layer.Under] = "Under",
		[Layer.Inventory] = "Inventory",
		[Layer.Crafting] = "Crafting",
		[Layer.Contacts] = "Contacts",
		[Layer.Clans] = "Clans",
		[Layer.TechTree] = "TechTree",
		[Layer.Map] = "Map"
	};

	private static readonly Dictionary<Font, string> Fonts = new()
	{
		[Font.RobotoCondensedBold] = "RobotoCondensed-Bold.ttf",
		[Font.RobotoCondensedRegular] = "RobotoCondensed-Regular.ttf",
		[Font.DroidSansMono] = "DroidSansMono.ttf",
		[Font.PermanentMarker] = "PermanentMarker.ttf",
		[Font.PressStart2PRegular] = "PressStart2P-Regular.ttf"
	};

	public static string ToJson(TextAnchor value) => TextAnchors[value];

	public static string ToJson(VerticalWrapMode value) => VerticalWrapModes[value];

	public static string ToJson(Image.Type value) => ImageTypes[value];

	public static string ToJson(InputField.LineType value) => LineTypes[value];

	public static string ToJson(Layer value) => Layers[value];

	public static string ToJson(Font value) => Fonts[value];
}
