using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public struct Offset
{
	public Bounds Min;

	public Bounds Max;

	public static Bounds Reference = new Bounds(1280f, 720f);

	public static Offset Default = new Offset(0f, 0f, 1f, 1f);

	public static Offset zero = new Offset(0f, 0f, 0f, 0f);

	[JsonIgnore]
	public float Width => Mathf.Abs(Max.X - Min.X);

	[JsonIgnore]
	public float Height => Mathf.Abs(Max.Y - Min.Y);

	public Offset(float xMin, float yMin, float xMax, float yMax)
	{
		Min = new Bounds(xMin, yMin);
		Max = new Bounds(xMax, yMax);
	}

	public Offset(float width, float height)
	{
		float num = width * 0.5f;
		float num2 = height * 0.5f;
		Min = new Bounds(0f - num, 0f - num2);
		Max = new Bounds(num, num2);
	}

	public Offset(Bounds min, Bounds max)
	{
		Min = min;
		Max = max;
	}

	public static implicit operator Offset(Area area)
	{
		return new Offset(area.Left, area.Bottom, area.Right, area.Top);
	}

	public override string ToString()
	{
		return Min.ToString() + " " + Max.ToString();
	}

	public static Offset operator +(Offset a, Offset b)
	{
		return new Offset(a.Min + b.Min, a.Max + b.Max);
	}

	public static Offset operator -(Offset a, Offset b)
	{
		return new Offset(a.Min - b.Min, a.Max - b.Max);
	}

	public static Offset operator /(Offset a, Offset b)
	{
		return new Offset(a.Min / b.Min, a.Max / b.Max);
	}

	public static Offset operator *(Offset a, Offset b)
	{
		return new Offset(a.Min * b.Min, a.Max * b.Max);
	}

	public static Offset operator /(Offset a, float f)
	{
		return new Offset(a.Min / f, a.Max / f);
	}

	public static Offset operator *(Offset a, float f)
	{
		return new Offset(a.Min * f, a.Max * f);
	}
}
