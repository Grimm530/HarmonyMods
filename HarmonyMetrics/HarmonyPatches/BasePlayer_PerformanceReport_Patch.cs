using System;
using Facepunch;
using HarmonyLib;
using Newtonsoft.Json;
using ProtoBuf;
using UnityEngine;

namespace HarmonyMetrics.HarmonyPatches;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PerformanceReport))]
public static class BasePlayer_PerformanceReport_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(BasePlayer __instance, BaseEntity.RPCMessage msg)
    {
        PerformanceReport report = null;
        try
        {
            var format = msg.read.String();
            report = msg.read.Proto<PerformanceReport>();
            if (report == null)
            {
                return false;
            }

            if (report.user_id != __instance.UserIDString)
            {
                DebugEx.Log("Client performance report from " + __instance + " has incorrect user_id (" + __instance.UserIDString + ")");
                return false;
            }

            MetricsLogger.TryHandleClientPerformanceReport(report);

            // Facepunch's "legacy" path DebugEx.Logs a client FPS/MB line every request.
            // HarmonyMetrics asks for reports on a timer; never print those. Keep json/rcon for admin.clientperf.
            if (format == "json")
            {
                DebugEx.Log(ToJson(report));
            }
            else if (format == "rcon")
            {
                RCon.Broadcast(RCon.LogType.ClientPerf, ToJson(report));
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError("[HarmonyMetrics] PerformanceReport prefix: " + ex.Message);
            return false;
        }
        finally
        {
            report?.Dispose();
        }
    }

    private static string ToJson(PerformanceReport report)
    {
        return JsonConvert.SerializeObject(new ClientPerformanceReport
        {
            request_id = report.request_id,
            user_id = report.user_id,
            fps_average = report.fps_average,
            fps = report.fps,
            frame_id = report.frame_id,
            frame_time = report.frame_time,
            frame_time_average = report.frame_time_average,
            memory_system = report.memory_system,
            memory_collections = report.memory_collections,
            memory_managed_heap = report.memory_managed_heap,
            realtime_since_startup = report.realtime_since_startup,
            streamer_mode = report.streamer_mode,
            ping = report.ping,
            tasks_invokes = report.tasks_invokes,
            tasks_load_balancer = report.tasks_load_balancer,
            workshop_skins_queued = report.workshop_skins_queued
        });
    }
}
