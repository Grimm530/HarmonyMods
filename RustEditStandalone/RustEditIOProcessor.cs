using System;
using UnityEngine;

namespace RustEditStandalone;

/// <summary>
/// Schedules ProcessIOEntities to run after world spawn (single use).
/// Can be triggered by first prefab (90s delay fallback) or by LoadingScreen "DONE" (2s delay).
/// </summary>
public class RustEditIOProcessor : MonoBehaviour
{
    private static RustEditIOProcessor _runner;

    /// <summary>Fallback delay when only first-prefab trigger is used (world may take 60s+ to fully spawn).</summary>
    private const float FallbackDelaySeconds = 90f;

    /// <summary>Delay when triggered from LoadingScreen.Update("DONE") - run soon after world finalization.</summary>
    private const float DoneDelaySeconds = 2f;

    /// <summary>Schedule ProcessIO. Use delaySeconds = DoneDelaySeconds when called from DONE hook.</summary>
    public static void ScheduleProcessIO(float delaySeconds = FallbackDelaySeconds)
    {
        if (_runner == null)
        {
            var go = new GameObject("RustEditStandalone_IOProcessor");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<RustEditIOProcessor>();
        }

        _runner.CancelInvoke(nameof(RunProcessIO));
        _runner.Invoke(nameof(RunProcessIO), delaySeconds);
    }

    private void RunProcessIO()
    {
        try
        {
            RustEditStandaloneMod.Instance?.ProcessIOEntities();
        }
        finally
        {
            if (gameObject != null)
                Destroy(gameObject);
            _runner = null;
        }
    }
}
