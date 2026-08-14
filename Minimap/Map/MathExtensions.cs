using System;
using UnityEngine;

namespace Oxide.Ext.Chaos;

public static class MathExtensions
{
	public static int RoundToNearestDegrees(float x, float m)
	{
		x %= 360f;
		if (x < 0f)
			x += 360f;
		float rounded = (float)(Math.Round(x / m, MidpointRounding.AwayFromZero) * m) % 360f;
		if (rounded == 360f)
			rounded = 0f;
		return Mathf.RoundToInt(rounded);
	}
}
