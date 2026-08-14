namespace Oxide.Ext.Chaos.TextMeshPro;

public class GlyphMetricsInfo
{
	public float width;
	public float height;
	public float bearingX;
	public float bearingY;
	public float advance;
	public float scale;
}

public class GlyphEntry
{
	public uint unicode;
	public uint glyphIdx;
	public int atlasIndex;
	public int x;
	public int y;
	public int width;
	public int height;
	public GlyphMetricsInfo metrics;
}
