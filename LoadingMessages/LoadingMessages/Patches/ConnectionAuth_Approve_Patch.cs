using HarmonyLib;
using Network;

namespace LoadingMessages.Patches
{
    /// <summary>
    /// Oxide OnUserApprove equivalent — fires when ConnectionAuth.Approve runs
    /// (player authorized and entering join/queue).
    /// </summary>
    [HarmonyPatch(typeof(ConnectionAuth), nameof(ConnectionAuth.Approve))]
    internal static class ConnectionAuth_Approve_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Connection connection)
        {
            LoadingMessagesMod.Instance?.OnUserApprove(connection);
        }
    }
}
