using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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
        static readonly FieldInfo DungeonGridCellsField = Field("DungeonGridCells");
        static readonly FieldInfo RiversField = Field("Rivers");
        static readonly FieldInfo OceanPatrolCloseField = Field("OceanPatrolClose");
        static readonly FieldInfo OceanPatrolFarField = Field("OceanPatrolFar");
        static readonly FieldInfo LandmarksField = Field("Landmarks");
        static readonly FieldInfo WiresField = Field("wires");

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

        public static List<DungeonGridCell> GetDungeonGridCells(TerrainPath path) =>
            path == null ? null : (List<DungeonGridCell>)DungeonGridCellsField?.GetValue(path);

        public static List<PathList> GetRivers(TerrainPath path) =>
            path == null ? null : (List<PathList>)RiversField?.GetValue(path);

        public static List<Vector3> GetOceanPatrolClose(TerrainPath path) =>
            path == null ? null : (List<Vector3>)OceanPatrolCloseField?.GetValue(path);

        public static List<Vector3> GetOceanPatrolFar(TerrainPath path) =>
            path == null ? null : (List<Vector3>)OceanPatrolFarField?.GetValue(path);

        public static List<LandmarkInfo> GetLandmarks(TerrainPath path) =>
            path == null ? null : (List<LandmarkInfo>)LandmarksField?.GetValue(path);

        /// <summary>
        /// PowerlineNode.Awake registers into TerrainPath.wires. Destroying a monument without
        /// scrubbing leaves dead nodes; CreateWires then NREs on GetComponent.
        /// </summary>
        public static int ScrubWires(TerrainPath path, GameObject ownedRoot = null)
        {
            if (path == null || WiresField == null)
                return 0;
            var wires = WiresField.GetValue(path) as System.Collections.IDictionary;
            if (wires == null || wires.Count == 0)
                return 0;

            int removed = 0;
            var emptyKeys = new List<object>();
            foreach (System.Collections.DictionaryEntry entry in wires)
            {
                if (!(entry.Value is System.Collections.IList list))
                    continue;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var node = list[i] as PowerlineNode;
                    bool drop = node == null;
                    if (!drop && ownedRoot != null)
                    {
                        try
                        {
                            Transform t = node.transform;
                            drop = t == null || t.root == ownedRoot.transform || t.IsChildOf(ownedRoot.transform);
                        }
                        catch
                        {
                            drop = true;
                        }
                    }
                    if (!drop)
                        continue;
                    list.RemoveAt(i);
                    removed++;
                }
                if (list.Count == 0)
                    emptyKeys.Add(entry.Key);
            }
            foreach (object key in emptyKeys)
                wires.Remove(key);
            return removed;
        }

        public static int ScrubLandmarksOwnedBy(TerrainPath path, GameObject ownedRoot)
        {
            var landmarks = GetLandmarks(path);
            if (landmarks == null || ownedRoot == null)
                return 0;
            int removed = 0;
            for (int i = landmarks.Count - 1; i >= 0; i--)
            {
                LandmarkInfo lm = landmarks[i];
                if (lm == null)
                {
                    landmarks.RemoveAt(i);
                    removed++;
                    continue;
                }
                try
                {
                    Transform t = lm.transform;
                    if (t == null || t.root == ownedRoot.transform || t.IsChildOf(ownedRoot.transform))
                    {
                        landmarks.RemoveAt(i);
                        removed++;
                    }
                }
                catch
                {
                    landmarks.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
    }
}
