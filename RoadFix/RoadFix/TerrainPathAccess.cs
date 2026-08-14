using System.Collections.Generic;
using System.Reflection;

namespace RoadFix;

/// <summary>
/// <see cref="TerrainPath"/> road/rail/river lists are <c>internal</c> on the live
/// Assembly-CSharp. Direct field access compiles against some reference layouts but
/// throws <see cref="System.FieldAccessException"/> at runtime and aborts procgen.
/// </summary>
internal static class TerrainPathAccess
{
    static readonly FieldInfo RoadsField = Field("Roads");
    static readonly FieldInfo RailsField = Field("Rails");
    static readonly FieldInfo RiversField = Field("Rivers");

    static FieldInfo Field(string name) =>
        typeof(TerrainPath).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static List<PathList> GetRoads(TerrainPath path) =>
        path == null ? null : RoadsField?.GetValue(path) as List<PathList>;

    public static List<PathList> GetRails(TerrainPath path) =>
        path == null ? null : RailsField?.GetValue(path) as List<PathList>;

    public static List<PathList> GetRivers(TerrainPath path) =>
        path == null ? null : RiversField?.GetValue(path) as List<PathList>;
}
