using System.Drawing;
using UnityEngine;

namespace CustomMapGen.Utility
{
    /// <summary>
    /// Unity Color to System.Drawing.Color conversion (for map image rendering).
    /// </summary>
    public static class ColorExtensions
    {
        public static System.Drawing.Color ToSystemDrawingColor(this UnityEngine.Color unityColor)
        {
            return System.Drawing.Color.FromArgb(
                Mathf.Clamp(Mathf.FloorToInt(unityColor.a * 255f), 0, 255),
                Mathf.Clamp(Mathf.FloorToInt(unityColor.r * 255f), 0, 255),
                Mathf.Clamp(Mathf.FloorToInt(unityColor.g * 255f), 0, 255),
                Mathf.Clamp(Mathf.FloorToInt(unityColor.b * 255f), 0, 255));
        }
    }
}
