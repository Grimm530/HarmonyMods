using System.Collections.Generic;
using System.Text.RegularExpressions;
using Oxide.Ext.Chaos.TextMeshPro;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Oxide.Ext.Chaos.Map;

public static class ImageTextUtility
{
	private static readonly List<PositionedGlyph> Layout = new List<PositionedGlyph>(64);
	private static readonly Regex TagRegex = new Regex("<.*?>", RegexOptions.Compiled);

	public static void WriteTextToImage(PermanentMarkerFont font, int srcRes, NativeArray<Color> src, int2 pixelCenter, string text, Color color, int fontSize)
	{
		if (font == null || !font.IsReady || string.IsNullOrEmpty(text) || !src.IsCreated)
			return;

		text = TagRegex.Replace(text, string.Empty);
		Layout.Clear();
		LayoutText(Layout, font, text, fontSize, out float totalWidth, out float totalHeight);
		if (Layout.Count == 0)
			return;

		for (int i = 0; i < Layout.Count; i++)
		{
			PositionedGlyph current = Layout[i];
			float2 charOffset = new float2(current.x - totalWidth * 0.5f, current.y - totalHeight * 0.5f);
			float2 size = new float2(current.glyph.width * current.scale, current.glyph.height * current.scale);
			OverlayGlyph(font, pixelCenter, charOffset, size, srcRes, src, color, current.glyph);
		}
	}

	private static void LayoutText(List<PositionedGlyph> results, PermanentMarkerFont font, string text, float fontSize, out float totalWidth, out float totalHeight)
	{
		totalWidth = 0f;
		totalHeight = 0f;
		float cursorX = 0f;
		float cursorY = 0f;
		float baseScale = fontSize / PermanentMarkerFont.PointSize * PermanentMarkerFont.FaceScale;
		float lineSpacing = fontSize * 1.1f;

		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '\n')
			{
				cursorX = 0f;
				cursorY += lineSpacing;
				continue;
			}

			if (!font.GlyphLookup.TryGetValue(c, out GlyphEntry glyph) || glyph.metrics == null)
				continue;

			float scale = baseScale * glyph.metrics.scale;
			float x = cursorX + glyph.metrics.bearingX * scale;
			float y = -cursorY;
			results.Add(new PositionedGlyph
			{
				x = x,
				y = y,
				scale = scale,
				glyph = glyph
			});

			cursorX += glyph.metrics.advance * scale;
			float right = x + glyph.metrics.width * scale;
			if (right > totalWidth)
				totalWidth = right;
			float top = -y + glyph.metrics.bearingY * scale;
			if (top > totalHeight)
				totalHeight = top;
		}
	}

	private static void OverlayGlyph(PermanentMarkerFont font, int2 pixelCenter, float2 charOffset, float2 size, int renderRes, NativeArray<Color> src, Color assetColor, GlyphEntry glyph)
	{
		if (glyph.width <= 0 || glyph.height <= 0)
			return;

		float2 origin = new float2(pixelCenter.x + charOffset.x, pixelCenter.y + charOffset.y);
		int destW = math.max(1, (int)math.round(size.x));
		int destH = math.max(1, (int)math.round(size.y));

		for (int row = 0; row < destH; row++)
		{
			int destY = (int)math.round(origin.y + row);
			if (destY < 0 || destY >= renderRes)
				continue;

			float v = (float)row / destH;
			float atlasY = glyph.y + v * glyph.height;

			for (int col = 0; col < destW; col++)
			{
				int destX = (int)math.round(origin.x + col);
				if (destX < 0 || destX >= renderRes)
					continue;

				float u = (float)col / destW;
				float atlasX = glyph.x + u * glyph.width;
				float a = ImageUtility.BilinearSample(
					font.AtlasPixels,
					atlasX / PermanentMarkerFont.AtlasWidth,
					atlasY / PermanentMarkerFont.AtlasHeight,
					PermanentMarkerFont.AtlasWidth,
					PermanentMarkerFont.AtlasHeight).a;
				float alpha = math.smoothstep(0.35f, 0.65f, a);
				if (alpha <= 0.1f)
					continue;

				int index = destY * renderRes + destX;
				Color existing = src[index];
				src[index] = new Color(
					assetColor.r * alpha + existing.r * (1f - alpha),
					assetColor.g * alpha + existing.g * (1f - alpha),
					assetColor.b * alpha + existing.b * (1f - alpha),
					math.max(existing.a, alpha));
			}
		}
	}

	private struct PositionedGlyph
	{
		public float x;
		public float y;
		public float scale;
		public GlyphEntry glyph;
	}
}
