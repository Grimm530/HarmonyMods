using System.Globalization;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Oxide.Ext.Chaos;

public abstract class HexColor
{
	public class Rgb : HexColor
	{
		protected override float Alpha => 1f;

		public Rgb()
		{
		}

		public Rgb(UnityEngine.Color color)
		{
			Hex = ColorUtility.ToHtmlStringRGB(new UnityEngine.Color(color.r, color.g, color.b));
		}

		public Rgb(string hex)
		{
			Hex = hex;
		}

		public static implicit operator float3(Rgb hexColor)
		{
			UnityEngine.Color color = hexColor;
			return new float3(color.r, color.g, color.b);
		}

		public static implicit operator Oxide.Ext.Chaos.UIFramework.Color(Rgb hexColor)
		{
			return new Oxide.Ext.Chaos.UIFramework.Color(hexColor.Hex);
		}

		public static implicit operator UnityEngine.Color(Rgb hexColor)
		{
			hexColor.Parse(out var r, out var g, out var b, out _);
			return new UnityEngine.Color(r, g, b, 1f);
		}

		public static implicit operator Color32(Rgb hexColor)
		{
			hexColor.Parse(out var r, out var g, out var b, out _);
			return new Color32(
				(byte)Mathf.Round(Mathf.Clamp01(r) * 255f),
				(byte)Mathf.Round(Mathf.Clamp01(g) * 255f),
				(byte)Mathf.Round(Mathf.Clamp01(b) * 255f),
				255);
		}
	}

	public class Rgba : HexColor
	{
		public float Opacity { get; set; } = 1f;

		protected override float Alpha => Opacity;

		public Rgba()
		{
		}

		public Rgba(UnityEngine.Color color)
		{
			Hex = ColorUtility.ToHtmlStringRGB(new UnityEngine.Color(color.r, color.g, color.b));
			Opacity = color.a;
		}

		public Rgba(string hex, float opacity = 1f)
		{
			Hex = hex;
			Opacity = opacity;
		}

		public static implicit operator float3(Rgba hexColor)
		{
			UnityEngine.Color color = hexColor;
			return new float3(color.r, color.g, color.b);
		}

		public static implicit operator float4(Rgba hexColor)
		{
			UnityEngine.Color color = hexColor;
			return new float4(color.r, color.g, color.b, color.a);
		}

		public static implicit operator Oxide.Ext.Chaos.UIFramework.Color(Rgba hexColor)
		{
			return new Oxide.Ext.Chaos.UIFramework.Color(hexColor.Hex, hexColor.Opacity);
		}

		public static implicit operator UnityEngine.Color(Rgba hexColor)
		{
			hexColor.Parse(out var r, out var g, out var b, out _);
			return new UnityEngine.Color(r, g, b, hexColor.Opacity);
		}

		public static implicit operator Color32(Rgba hexColor)
		{
			hexColor.Parse(out var r, out var g, out var b, out _);
			return new Color32(
				(byte)Mathf.Round(Mathf.Clamp01(r) * 255f),
				(byte)Mathf.Round(Mathf.Clamp01(g) * 255f),
				(byte)Mathf.Round(Mathf.Clamp01(b) * 255f),
				(byte)Mathf.Round(Mathf.Clamp01(hexColor.Opacity) * 255f));
		}
	}

	public string Hex { get; set; }

	[JsonIgnore]
	protected abstract float Alpha { get; }

	protected void Parse(out float r, out float g, out float b, out float a)
	{
		string hex = Hex ?? "";
		if (hex.StartsWith("#"))
			hex = hex.Substring(1);

		if (hex.Length == 3)
		{
			r = int.Parse(hex[0].ToString(), NumberStyles.AllowHexSpecifier) / 15f;
			g = int.Parse(hex[1].ToString(), NumberStyles.AllowHexSpecifier) / 15f;
			b = int.Parse(hex[2].ToString(), NumberStyles.AllowHexSpecifier) / 15f;
			a = Alpha;
		}
		else if (hex.Length == 6)
		{
			r = int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier) / 255f;
			g = int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier) / 255f;
			b = int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier) / 255f;
			a = Alpha;
		}
		else if (hex.Length == 8)
		{
			r = int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier) / 255f;
			g = int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier) / 255f;
			b = int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier) / 255f;
			a = int.Parse(hex.Substring(6, 2), NumberStyles.AllowHexSpecifier) / 255f;
		}
		else
		{
			r = g = b = 1f;
			a = Alpha;
		}
	}
}
