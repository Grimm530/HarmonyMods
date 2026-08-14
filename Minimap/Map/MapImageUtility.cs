using Oxide.Ext.Chaos.UIFramework;
using Unity.Mathematics;
using UnityEngine;

namespace Oxide.Ext.Chaos.Map;

public class MapImageUtility
{
	public Vector3 OneOverSize;
	public Vector3 Position;
	public Vector3 Size;

	private readonly int _renderResolution;

	public MapImageUtility(MapConfig mapConfig)
	{
		_renderResolution = mapConfig.RenderResolution;
		int num = (int)TerrainMeta.Size.x + mapConfig.OceanMargin * 2;
		Size = new Vector3(num, 1000f, num);
		Position = Size * -0.5f;
		OneOverSize = new Vector3(1f / Size.x, 1f / Size.y, 1f / Size.z);
	}

	public Vector3 NormalizePosition(Vector3 worldPos)
	{
		return new Vector3(
			(worldPos.x - Position.x) * OneOverSize.x,
			(worldPos.y - Position.y) * OneOverSize.y,
			(worldPos.z - Position.z) * OneOverSize.z);
	}

	public float2 WorldToImage(Offset mapOffset, Vector3 worldPosition)
	{
		Vector3 vector = NormalizePosition(worldPosition);
		return new float2((vector.x - 0.5f) * mapOffset.Width, (vector.z - 0.5f) * mapOffset.Height);
	}

	public Offset CalculateImageOffsetForPosition(float viewportSize, Vector3 position, int zoomLevel, int zoomLevels)
	{
		float num = Mathf.Lerp(viewportSize, _renderResolution, (float)zoomLevel / (float)Mathf.Max(zoomLevels, 1));
		float num2 = num * 0.5f;
		float2 @float = NormalizeWorldPosition(position) * num;
		float2 float2 = new float2(@float.x - num2, @float.y - num2);
		return new Offset(0f - num2 + float2.x, 0f - num2 + float2.y, num2 + float2.x, num2 + float2.y);
	}

	private float2 NormalizeWorldPosition(Vector3 worldPosition)
	{
		Vector3 vector = NormalizePosition(worldPosition);
		return new float2(1f - vector.x, 1f - vector.z);
	}
}
