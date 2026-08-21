using HarmonyLib;
using Network;
using UnityEngine;

namespace Radar.Patches;

/// <summary>
/// AdminRadar 5.4.312: Oxide <c>OnPlayerVoice(BasePlayer, ArraySegment&lt;byte&gt;)</c> (was <c>byte[]</c>).
/// Harmony observes the same game method Oxide hooks. Postfix only — Radio / ZoneManager / Cooking
/// already prefix <c>ServerMgr.OnPlayerVoice</c>; do not skip original or consume <c>packet.read</c>.
/// Voice bytes are unused (AdminRadar ignores <c>data</c> except for the hook signature).
/// </summary>
[HarmonyPatch(typeof(ServerMgr), "OnPlayerVoice")]
public static class ServerMgr_OnPlayerVoice_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Message packet)
    {
        BasePlayer player = packet == null ? null : NetworkPacketEx.Player(packet);
        if (player == null || player.IsDestroyed)
            return;
        RadarMod.Instance?.OnPlayerVoice(player);
    }
}
