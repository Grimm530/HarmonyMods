using UnityEngine;

namespace RoadFix.Bridge;

internal struct BridgeCrossing
{
    public PathList Path;
    public float StartDist;
    public float EndDist;
    public Vector3 Center;
    public Vector3 Tangent;
    public float SpanLength;
    public float RiverBedY;
    public int NodeCount;
    /// <summary>Average/legacy deck height.</summary>
    public float DeckY;
    /// <summary>Bank height at span start (node 0 side).</summary>
    public float StartDeckY;
    /// <summary>Bank height at span end (node N side).</summary>
    public float EndDeckY;
}
