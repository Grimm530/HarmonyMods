using System.Collections.Generic;
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
    /// <summary>Metres of deck needed across parallel rails (0 = template native width).</summary>
    public float CoverWidth;
    /// <summary>Other rail spans sharing this river gap (snapped to the same gravel deck).</summary>
    public List<BridgeCrossing> ExtraSpans;
    /// <summary>Do not paste a .map tile (absorbed into a neighbouring combined bridge).</summary>
    public bool SkipPlace;
    /// <summary>Snap this rail onto the road template deck (road+rail combined crossing).</summary>
    public bool UseRoadDeck;
}
