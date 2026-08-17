using HarmonyLib;
using Network;

namespace LoadingMessages.Patches
{
    /// <summary>
    /// Oxide OnUserApprove equivalent.
    /// Oxide injects IOnUserApprove into ConnectionAuth.OnNewConnection (after basic
    /// reject checks, before Steam/EAC auth) — not ConnectionAuth.Approve.
    /// Approve runs only after auth, when the client has already left the connecting
    /// screen that displays Message.Type.Message.
    /// </summary>
    [HarmonyPatch(typeof(ConnectionAuth), nameof(ConnectionAuth.OnNewConnection), typeof(Connection))]
    internal static class ConnectionAuth_OnNewConnection_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Connection connection)
        {
            LoadingMessagesMod.Instance?.OnUserApprove(connection);
        }
    }
}
