using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oxide.Ext.Chaos.TextMeshPro.Fonts;
using Unity.Collections;
using UnityEngine;

namespace Oxide.Ext.Chaos.TextMeshPro;

public sealed class PermanentMarkerFont : IDisposable
{
	public const int AtlasWidth = 512;
	public const int AtlasHeight = 512;
	public const int PointSize = 60;
	public const float FaceScale = 1f;

	public Dictionary<char, GlyphEntry> GlyphLookup { get; }
	public NativeArray<Color> AtlasPixels { get; private set; }

	public bool IsReady => AtlasPixels.IsCreated && GlyphLookup.Count > 0;

	public PermanentMarkerFont()
	{
		GlyphLookup = new Dictionary<char, GlyphEntry>();
		foreach (GlyphEntry glyph in PermanentMarkerGlyphs.Create())
			GlyphLookup[(char)glyph.unicode] = glyph;
		LoadAtlas();
	}

	public void Dispose()
	{
		if (AtlasPixels.IsCreated)
			AtlasPixels.Dispose();
	}

	private void LoadAtlas()
	{
		byte[] png = LoadEmbeddedAtlas();
		if (png == null || png.Length == 0)
		{
			Debug.LogWarning("[Minimap] PermanentMarker atlas missing — monument labels will use fallback bitmap text.");
			return;
		}

		var texture = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, mipChain: false)
		{
			filterMode = FilterMode.Bilinear,
			wrapMode = TextureWrapMode.Clamp
		};
		if (!texture.LoadImage(png))
		{
			UnityEngine.Object.Destroy(texture);
			Debug.LogWarning("[Minimap] PermanentMarker atlas failed to decode.");
			return;
		}

		Color[] pixels = texture.GetPixels();
		UnityEngine.Object.Destroy(texture);
		AtlasPixels = new NativeArray<Color>(pixels, Allocator.Persistent);
	}

	private static byte[] LoadEmbeddedAtlas()
	{
		var asm = Assembly.GetExecutingAssembly();
		foreach (string name in asm.GetManifestResourceNames())
		{
			if (name.IndexOf("permanentmarker-atlas.png", StringComparison.OrdinalIgnoreCase) < 0)
				continue;
			using Stream stream = asm.GetManifestResourceStream(name);
			if (stream == null)
				return null;
			using var ms = new MemoryStream();
			stream.CopyTo(ms);
			return ms.ToArray();
		}

		return null;
	}
}
