using System.Globalization;
using Newtonsoft.Json;
using Oxide.Ext.Chaos.Json;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

[JsonConverter(typeof(UIColorConverter))]
public struct Color
{
	public readonly float R;

	public readonly float G;

	public readonly float B;

	public readonly float A;

	private string m_String;

	public static Color DEFAULT = new Color(1, 1, 1, 1);

	public static Color Clear = new Color(0, 0, 0, 0);

	public static Color Black = new Color("010101");

	public static Color White = new Color("FFFFFF");

	public static Color Gray = new Color("808080");

	public static Color DarkGray = new Color("A9A9A9");

	public static Color LightGray = new Color("D3D3D3");

	public static Color VeryDarkGray = new Color("666666");

	public static Color Red = new Color("FF0000");

	public static Color DarkRed = new Color("7F0000");

	public static Color Green = new Color("00FF00");

	public static Color DarkGreen = new Color("007F00");

	public static Color Blue = new Color("0000FF");

	public static Color DarkBlue = new Color("00007F");

	public static Color Yellow = new Color("FFFF00");

	public static Color Cyan = new Color("00FFFF");

	public static Color Magenta = new Color("FF00FF");

	public static Color Teal = new Color("008080");

	public static Color Aquamarine = new Color("00FFBF");

	public static Color Gold = new Color("FFD700");

	public static Color Goldenrod = new Color("DAA520");

	public static Color Azure = new Color("007FFF");

	public static Color Rose = new Color("FF007F");

	public static Color SpringGreen = new Color("00FF7F");

	public static Color Chartreuse = new Color("7FFF00");

	public static Color Orange = new Color("FFA500");

	public static Color Purple = new Color("800080");

	public static Color Violet = new Color("EE82EE");

	public static Color Brown = new Color("A52A2A");

	public static Color HotPink = new Color("FF69B4");

	public static Color Lilac = new Color("C8A2C8");

	public static Color CornflowerBlue = new Color("6495ED");

	public static Color MidnightBlue = new Color("191970");

	public static Color Wheat = new Color("F5DEB3");

	public static Color IndianRed = new Color("CD5C5C");

	public static Color Turquoise = new Color("30D5C8");

	public static Color SapGreen = new Color("507D2A");

	public static Color PhthaloBlue = new Color("000F89");

	public static Color PhthaloGreen = new Color("123524");

	public static Color Sienna = new Color("882D17");

	public Color(float r, float g, float b, float a = 1f)
	{
		R = r;
		B = b;
		G = g;
		A = a;
		m_String = $"{R} {G} {B} {A}";
	}

	public Color(int r, int g, int b, int a = 255)
		: this((float)r / 255f, (float)g / 255f, (float)b / 255f, (float)a / 255f)
	{
	}

	public Color(UnityEngine.Color color)
		: this(color.r, color.g, color.b, color.a)
	{
	}

	public Color(string hex, float a = 1f)
	{
		if (hex.StartsWith("#"))
		{
			hex = hex.Substring(1);
		}
		if (hex.Length == 3)
		{
			R = (float)int.Parse(hex[0].ToString(), NumberStyles.AllowHexSpecifier) / 255f;
			G = (float)int.Parse(hex[1].ToString(), NumberStyles.AllowHexSpecifier) / 255f;
			B = (float)int.Parse(hex[2].ToString(), NumberStyles.AllowHexSpecifier) / 255f;
			A = a;
		}
		else if (hex.Length == 6)
		{
			R = (float)int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier) / 255f;
			G = (float)int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier) / 255f;
			B = (float)int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier) / 255f;
			A = a;
		}
		else if (hex.Length == 8)
		{
			R = (float)int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier) / 255f;
			G = (float)int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier) / 255f;
			B = (float)int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier) / 255f;
			A = (float)int.Parse(hex.Substring(6, 2), NumberStyles.AllowHexSpecifier) / 255f;
		}
		else
		{
			R = (G = (B = (A = 1f)));
		}
		m_String = $"{R} {G} {B} {A}";
	}

	public override bool Equals(object obj)
	{
		if (!(obj is Color color))
		{
			return false;
		}
		if (R.Equals(color.R) && G.Equals(color.G) && B.Equals(color.B))
		{
			return A.Equals(color.A);
		}
		return false;
	}

	public bool Equals(Color other)
	{
		if (R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B))
		{
			return A.Equals(other.A);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return (((((R.GetHashCode() * 397) ^ G.GetHashCode()) * 397) ^ B.GetHashCode()) * 397) ^ A.GetHashCode();
	}

	public string ToString(string defaultValue)
	{
		if (string.IsNullOrEmpty(m_String))
		{
			return defaultValue;
		}
		return m_String;
	}

	public static bool operator ==(Color lhs, Color rhs)
	{
		return lhs.Equals(rhs);
	}

	public static bool operator !=(Color lhs, Color rhs)
	{
		return !lhs.Equals(rhs);
	}

	public static implicit operator Color(string s)
	{
		return new Color(s);
	}

	public static implicit operator Color(UnityEngine.Color c)
	{
		return new Color(c);
	}

	public static implicit operator UnityEngine.Color(Color c)
	{
		return new UnityEngine.Color(c.R, c.G, c.B, c.A);
	}
}
