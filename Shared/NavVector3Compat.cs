using Rust.Ai.Gen2.Nav;
using UnityEngine;

/// <summary>
/// NavVector3 replaced Vector3 on Gen2 RustNavMeshAgent; extension helpers mirror Vector3 conveniences.
/// </summary>
internal static class NavVector3Compat
{
    public static Vector3 ToVector3(this NavVector3 v) => new(v.x, v.y, v.z);

    public static float SqrMagnitude(this NavVector3 v) => v.x * v.x + v.y * v.y + v.z * v.z;

    public static float Magnitude(this NavVector3 v) => Mathf.Sqrt(v.SqrMagnitude());
}
