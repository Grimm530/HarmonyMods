using System;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class ExcavatorRotationFeature
{
    public static void Initialize()
    {
        RustEditHub.OnServerInit += Fix;
    }

    public static void Shutdown()
    {
        RustEditHub.OnServerInit -= Fix;
    }

    private static void Fix()
    {
        var monuments = TerrainMeta.Path?.Monuments;
        if (monuments == null) return;

        for (int i = 0; i < monuments.Count; i++)
        {
            var m = monuments[i];
            if (m == null) continue;
            string name = m.name ?? string.Empty;
            if (name.IndexOf("excavator", StringComparison.OrdinalIgnoreCase) < 0) continue;

            float yaw = m.transform.eulerAngles.y;
            var arms = UnityEngine.Object.FindObjectsOfType<ExcavatorArm>();
            for (int a = 0; a < arms.Length; a++)
            {
                var arm = arms[a];
                if (arm == null) continue;
                if (Vector3.Distance(arm.transform.position, m.transform.position) > 60f) continue;
                arm.yaw1 = -4f + yaw;
                arm.yaw2 = 132.3f + yaw;
                Debug.Log($"[RustEditStandalone] ExcavatorArm yaw fixed for monument rotation {yaw:F1}");
            }
        }
    }
}
