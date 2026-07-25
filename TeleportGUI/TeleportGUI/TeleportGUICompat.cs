using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>Oxide-free shims used by the vendored Chaos UI framework port.</summary>
    public static class TeleportGUICompat
    {
        public static string StripTags(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return Regex.Replace(value, "<.*?>", string.Empty);
        }
    }

    /// <summary>Optional magnifying-glass image for search UI; degrades when no ImageLibrary mod is loaded.</summary>
    public static class ImageLibrary
    {
        public static bool IsLoaded => false;

        public static void AddImage(string url, string imageName, ulong skinId, Action callback = null)
        {
            try { callback?.Invoke(); } catch { }
        }

        public static string GetImage(string imageName, ulong skinId = 0UL) => string.Empty;
    }
}
