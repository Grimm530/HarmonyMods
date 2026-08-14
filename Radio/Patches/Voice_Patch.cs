using HarmonyLib;
using Network;
using UnityEngine;

namespace RadioHarmony.Patches
{
    [HarmonyPatch(typeof(ServerMgr), "OnPlayerVoice")]
    internal static class Voice_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Message packet)
        {
            try
            {
                BasePlayer player = NetworkPacketEx.Player(packet);
                if (player == null || packet?.read == null) return;

                var read = packet.read;
                long pos = read.Position;
                var seg = read.BytesSegmentWithSize(1048576u);
                read.Position = pos;
                if (seg.Count <= 0 || seg.Array == null) return;

                byte[] data = new byte[seg.Count];
                System.Buffer.BlockCopy(seg.Array, seg.Offset, data, 0, seg.Count);
                RadioMod.Instance?.OnPlayerVoice(player, data);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Radio] OnPlayerVoice: " + ex.Message);
            }
        }
    }
}
