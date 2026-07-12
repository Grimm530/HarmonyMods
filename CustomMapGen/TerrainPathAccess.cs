using System.Collections.Generic;
using System.Reflection;

namespace CustomMapGen
{
    /// <summary>
    /// TerrainPath stores monument/powerline/rail lists as <c>internal</c> fields (same as decompiled game code).
    /// External mods cannot use <c>TerrainMeta.Path.Monuments</c> etc. at compile time — only reflection works.
    /// CustomMapGenWorking's single-file tree did not include patch sources; this split project needs this helper to build.
    /// </summary>
    internal static class TerrainPathAccess
    {
        static readonly FieldInfo MonumentsField = Field("Monuments");
        static readonly FieldInfo PowerlinesField = Field("Powerlines");
        static readonly FieldInfo LakeObjsField = Field("LakeObjs");
        static readonly FieldInfo RailsField = Field("Rails");
        static readonly FieldInfo DungeonGridEntrancesField = Field("DungeonGridEntrances");
        static readonly FieldInfo RiversField = Field("Rivers");

        static FieldInfo Field(string name) =>
            typeof(TerrainPath).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static List<MonumentInfo> GetMonuments(TerrainPath path) =>
            path == null ? null : (List<MonumentInfo>)MonumentsField?.GetValue(path);

        public static List<PathList> GetPowerlines(TerrainPath path) =>
            path == null ? null : (List<PathList>)PowerlinesField?.GetValue(path);

        public static List<LakeInfo> GetLakeObjs(TerrainPath path) =>
            path == null ? null : (List<LakeInfo>)LakeObjsField?.GetValue(path);

        public static List<PathList> GetRails(TerrainPath path) =>
            path == null ? null : (List<PathList>)RailsField?.GetValue(path);

        public static List<DungeonGridInfo> GetDungeonGridEntrances(TerrainPath path) =>
            path == null ? null : (List<DungeonGridInfo>)DungeonGridEntrancesField?.GetValue(path);

        public static List<PathList> GetRivers(TerrainPath path) =>
            path == null ? null : (List<PathList>)RiversField?.GetValue(path);
    }
}
