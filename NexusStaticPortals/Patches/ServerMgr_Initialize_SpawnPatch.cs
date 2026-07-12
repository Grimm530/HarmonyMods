using System.Reflection;
using HarmonyLib;

namespace NexusStaticPortals.Patches
{
    [HarmonyPatch]
    internal static class ServerMgr_Initialize_SpawnPatch
    {
        private static bool _scheduled;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ServerMgr), nameof(ServerMgr.Initialize));
        }

        private static void Postfix()
        {
            if (_scheduled)
                return;

            _scheduled = true;
            NexusStaticPortalsMod.Instance?.ScheduleInitialSpawn();
        }
    }
}
