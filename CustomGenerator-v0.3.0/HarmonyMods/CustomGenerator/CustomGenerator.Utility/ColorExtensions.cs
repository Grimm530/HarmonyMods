using System.Drawing;
using UnityEngine;

namespace CustomGenerator.Utility;

public static class ColorExtensions
{
	public static Color ToSystemDrawingColor(this Color unityColor)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		return Color.FromArgb(Mathf.Clamp(Mathf.FloorToInt(unityColor.a * 255f), 0, 255), Mathf.Clamp(Mathf.FloorToInt(unityColor.r * 255f), 0, 255), Mathf.Clamp(Mathf.FloorToInt(unityColor.g * 255f), 0, 255), Mathf.Clamp(Mathf.FloorToInt(unityColor.b * 255f), 0, 255));
	}
}
