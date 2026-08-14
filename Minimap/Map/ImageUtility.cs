using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Oxide.Ext.Chaos.Map;

public static class ImageUtility
{
	public static Color SamplePixel(NativeArray<Color> colors, float normalizedX, float normalizedY, int width, int height)
	{
		if (!colors.IsCreated || colors.Length != width * height)
			return Color.clear;
		normalizedX = Mathf.Clamp01(normalizedX);
		normalizedY = Mathf.Clamp01(normalizedY);
		int num = Mathf.FloorToInt(normalizedX * (float)(width - 1));
		int num2 = Mathf.FloorToInt(normalizedY * (float)(height - 1)) * width + num;
		if (num2 < 0 || num2 >= colors.Length)
			return Color.clear;
		return colors[num2];
	}

	public static Color BilinearSample(NativeArray<Color> colors, float normalizedX, float normalizedY, int width, int height)
	{
		float num = normalizedX * (float)width - 0.5f;
		float num2 = normalizedY * (float)height - 0.5f;
		int num3 = (int)math.floor(num);
		int num4 = (int)math.floor(num2);
		int valueToClamp = num3 + 1;
		int valueToClamp2 = num4 + 1;
		float t = num - (float)num3;
		float t2 = num2 - (float)num4;
		num3 = math.clamp(num3, 0, width - 1);
		valueToClamp = math.clamp(valueToClamp, 0, width - 1);
		num4 = math.clamp(num4, 0, height - 1);
		valueToClamp2 = math.clamp(valueToClamp2, 0, height - 1);
		Color a = colors[num4 * width + num3];
		Color b = colors[num4 * width + valueToClamp];
		Color a2 = colors[valueToClamp2 * width + num3];
		Color b2 = colors[valueToClamp2 * width + valueToClamp];
		return Color.Lerp(Color.Lerp(a, b, t), Color.Lerp(a2, b2, t), t2);
	}

	public static Color BlendColors(Color original, Color overlay)
	{
		float num = Mathf.Clamp01(overlay.a);
		return new Color(
			overlay.r * num + original.r * (1f - num),
			overlay.g * num + original.g * (1f - num),
			overlay.b * num + original.b * (1f - num),
			1f);
	}

	public static void OverlayImageAsset(NativeArray<Color> dst, NativeArray<Color> src, int srcWidth, int srcHeight, int2 pixelCenter, float rotation, float scale, int renderRes, Color? color = null)
	{
		float num = (float)srcWidth * scale * 0.5f;
		float num2 = (float)srcHeight * scale * 0.5f;
		int maxExtent = Mathf.CeilToInt(Mathf.Sqrt(num * num + num2 * num2));
		float f = (0f - rotation) * (MathF.PI / 180f);
		float cosRot = Mathf.Cos(f);
		float sinRot = Mathf.Sin(f);
		float invScale = 1f / scale;
		Parallel.For(-maxExtent, maxExtent + 1, dy =>
		{
			for (int i = -maxExtent; i <= maxExtent; i++)
			{
				int num3 = pixelCenter.x + i;
				int num4 = pixelCenter.y + dy;
				if (num3 < 0 || num3 >= renderRes || num4 < 0 || num4 >= renderRes)
					continue;
				float num5 = (float)i * cosRot + (float)dy * sinRot;
				float num6 = (float)(-i) * sinRot + (float)dy * cosRot;
				float num7 = num5 * invScale + (float)srcWidth * 0.5f;
				float num8 = num6 * invScale + (float)srcHeight * 0.5f;
				float num9 = num7 / (float)srcWidth;
				float num10 = num8 / (float)srcHeight;
				if (num9 < 0f || num9 > 1f || num10 < 0f || num10 > 1f)
					continue;
				Color color2 = BilinearSample(src, num9, num10, srcWidth, srcHeight);
				if (color.HasValue)
					color2 = new Color(color.Value.r, color.Value.g, color.Value.b, color2.a);
				int index = num4 * renderRes + num3;
				Color color3 = dst[index];
				float a = color2.a;
				dst[index] = new Color(
					color3.r * (1f - a) + color2.r * a,
					color3.g * (1f - a) + color2.g * a,
					color3.b * (1f - a) + color2.b * a,
					Mathf.Max(color3.a, a));
			}
		});
	}
}
