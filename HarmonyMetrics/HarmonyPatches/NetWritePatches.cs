using HarmonyLib;
using Network;

namespace HarmonyMetrics.HarmonyPatches;

[HarmonyPatch(typeof(NetWrite), nameof(NetWrite.Send))]
public static class NetWrite_Send_Patch
{
    [HarmonyPrefix]
    public static void Prefix(NetWrite __instance, SendInfo info)
    {
        if (!MetricsLogger.IsReady)
        {
            return;
        }

        SingletonComponent<MetricsLogger>.Instance.OnNetWriteSend(__instance, info);
    }
}

[HarmonyPatch(typeof(NetWrite), nameof(NetWrite.PacketID))]
public static class NetWrite_PacketID_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Message.Type val)
    {
        if (!MetricsLogger.IsReady)
        {
            return;
        }

        SingletonComponent<MetricsLogger>.Instance.OnNetWritePacketID(val);
    }
}
