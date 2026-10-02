using HarmonyLib;
using Network;
using UnityEngine;

namespace Radar.Patches;

/// <summary>
/// AdminRadar 5.4.4: Oxide <c>OnPlayerVoice(BasePlayer, ArraySegment&lt;byte&gt;)</c> (was <c>byte[]</c>).
/// Postfix only — Radio / ZoneManager / Cooking already prefix <c>ServerMgr.OnPlayerVoice</c>.
/// Do not skip the original or consume <c>packet.read</c>. Voice bytes are unused; the hook only marks who is speaking.
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
