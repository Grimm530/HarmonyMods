using System.Collections.Generic;

namespace TruePVEHarmony;

internal static class TruePVECompat
{
    public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
        => dict != null && dict.TryGetValue(key, out var value) ? value : defaultValue;
}
