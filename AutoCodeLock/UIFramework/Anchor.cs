using System;

namespace Oxide.Ext.Chaos.UIFramework;

public struct Anchor
{
	public enum Enum
	{
		TopLeft,
		TopCenter,
		TopRight,
		CenterLeft,
		Center,
		CenterRight,
		BottomLeft,
		BottomCenter,
		BottomRight,
		FullStretch,
		TopStretch,
		HorizontalCenterStretch,
		BottomStretch,
		LeftStretch,
		VerticalCenterStretch,
		RightStretch
	}

	public Bounds Min;

	public Bounds Max;

	public static Anchor TopLeft = new Anchor(0f, 1f, 0f, 1f);

	public static Anchor TopCenter = new Anchor(0.5f, 1f, 0.5f, 1f);

	public static Anchor TopRight = new Anchor(1f, 1f, 1f, 1f);

	public static Anchor CenterLeft = new Anchor(0f, 0.5f, 0f, 0.5f);

	public static Anchor Center = new Anchor(0.5f, 0.5f, 0.5f, 0.5f);

	public static Anchor CenterRight = new Anchor(1f, 0.5f, 1f, 0.5f);

	public static Anchor BottomLeft = new Anchor(0f, 0f, 0f, 0f);

	public static Anchor BottomCenter = new Anchor(0.5f, 0f, 0.5f, 0f);

	public static Anchor BottomRight = new Anchor(1f, 0f, 1f, 0f);

	public static Anchor FullStretch = new Anchor(0f, 0f, 1f, 1f);

	public static Anchor TopStretch = new Anchor(0f, 1f, 1f, 1f);

	[Obsolete("Use HorizontalCenterStretch")]
	public static Anchor HoriztonalCenterStretch = new Anchor(0f, 0.5f, 1f, 0.5f);

	public static Anchor HorizontalCenterStretch = new Anchor(0f, 0.5f, 1f, 0.5f);

	public static Anchor BottomStretch = new Anchor(0f, 0f, 1f, 0f);

	public static Anchor LeftStretch = new Anchor(0f, 0f, 0f, 1f);

	public static Anchor VerticalCenterStretch = new Anchor(0.5f, 0f, 0.5f, 1f);

	public static Anchor RightStretch = new Anchor(1f, 0f, 1f, 1f);

	public Anchor(float xMin, float yMin, float xMax, float yMax)
	{
		Min = new Bounds(xMin, yMin);
		Max = new Bounds(xMax, yMax);
	}

	public Anchor(Bounds min, Bounds max)
	{
		Min = min;
		Max = max;
	}

	public override string ToString()
	{
		return Min.ToString() + " " + Max.ToString();
	}

	public static Anchor EnumToAnchor(Enum @enum)
	{
		return @enum switch
		{
			Enum.TopLeft => TopLeft, 
			Enum.TopCenter => TopCenter, 
			Enum.TopRight => TopRight, 
			Enum.CenterLeft => CenterLeft, 
			Enum.Center => Center, 
			Enum.CenterRight => CenterRight, 
			Enum.BottomLeft => BottomLeft, 
			Enum.BottomCenter => BottomCenter, 
			Enum.BottomRight => BottomRight, 
			Enum.FullStretch => FullStretch, 
			Enum.TopStretch => TopStretch, 
			Enum.HorizontalCenterStretch => HorizontalCenterStretch, 
			Enum.BottomStretch => BottomStretch, 
			Enum.LeftStretch => LeftStretch, 
			Enum.VerticalCenterStretch => VerticalCenterStretch, 
			Enum.RightStretch => RightStretch, 
			_ => throw new ArgumentOutOfRangeException("enum", @enum, null), 
		};
	}
}
