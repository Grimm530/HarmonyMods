// Compatibility for .NET Framework 4.8 (no Range/Index, no string.Contains(char), etc.)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RaidableBases
{
    internal static class Net48Compat
    {
        public static bool Contains(this string s, char c) => s != null && s.IndexOf(c) >= 0;
        public static bool Contains(this string[] arr, string value) => arr != null && value != null && arr.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        public static bool Remove<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, out TValue value)
        {
            value = default;
            if (dict == null || !dict.TryGetValue(key, out value)) return false;
            dict.Remove(key);
            return true;
        }
        public static bool TryPeek<T>(this Queue<T> queue, out T result)
        {
            result = default;
            if (queue == null || queue.Count == 0) return false;
            result = queue.Peek();
            return true;
        }
        public static bool Contains(this string s, string value, StringComparison comparisonType)
        {
            return s != null && value != null && s.IndexOf(value, comparisonType) >= 0;
        }
    }
}
