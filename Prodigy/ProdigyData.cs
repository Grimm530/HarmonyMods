using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace Prodigy;

public class ProdigyData
{
    public Dictionary<ulong, List<LogObject>> Blocks { get; set; } = new();
    public Dictionary<ulong, List<LogObject>> TC { get; set; } = new();
    public Dictionary<ulong, UiOffsets> Offsets { get; set; } = new();
    public string WipeId { get; set; }

    internal Dictionary<ulong, List<LogObject>> Get(BaseEntity ent) =>
        ent is BuildingPrivlidge ? TC : Blocks;

    internal bool Changed { get; set; }
}

public class LogObject
{
    public DateTime Date { get; set; }
    [JsonConverter(typeof(Vector3Converter))]
    public Vector3 Coordinates { get; set; }

    public LogObject() { }

    public LogObject(DateTime date, Vector3 coordinates)
    {
        Date = date;
        Coordinates = coordinates;
    }
}

public class Vector3Converter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
        writer.WriteValue(value?.ToString() ?? "0 0 0");

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var s = reader.Value?.ToString();
        return string.IsNullOrEmpty(s) ? Vector3.zero : ParseVector3(s);
    }

    public static Vector3 ParseVector3(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Vector3.zero;
        var parts = s.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return Vector3.zero;
        float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x);
        float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y);
        float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z);
        return new Vector3(x, y, z);
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(Vector3);
}

public class UiOffsets
{
    public const string DefaultLargeMin = "-246.581 74.284";
    public const string DefaultLargeMax = "227.581 264.116";
    public const string DefaultSmallMin = "8.204 -1.883";
    public const string DefaultSmallMax = "228.796 132.483";

    internal bool Changed { get; set; }
    public string Min { get; set; } = DefaultLargeMin;
    public string Max { get; set; } = DefaultLargeMax;
    public bool IsSmallUi { get; set; }
    public bool IsTimed { get; set; } = true;

    public UiOffsets() { }

    public UiOffsets(string min, string max)
    {
        Min = min;
        Max = max;
    }

    internal void MigratePanelSize()
    {
        if (!TryParseXy(Min, out float minX, out float minY) || !TryParseXy(Max, out float maxX, out float maxY))
            return;

        float height = maxY - minY;
        float oldHeight = IsSmallUi ? 101.366f : 143.832f;
        float extra = IsSmallUi ? 33f : 46f;
        if (Math.Abs(height - oldHeight) > 0.6f) return;

        float half = extra * 0.5f;
        Min = FormatXy(minX, minY - half);
        Max = FormatXy(maxX, maxY + half);
        Changed = true;
    }

    private static bool TryParseXy(string s, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }

    private static string FormatXy(float x, float y) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###}", x, y);
}
