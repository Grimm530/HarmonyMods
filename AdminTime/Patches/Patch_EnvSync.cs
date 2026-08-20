using HarmonyLib;
using Network;

namespace AdminTime.Patches
{
    [HarmonyPatch(typeof(EnvSync), nameof(EnvSync.ServerInit))]
    public static class Patch_EnvSync_ServerInit
    {
        [HarmonyPostfix]
        public static void Postfix(EnvSync __instance)
        {
            AdminTimeMod.Instance?.BindEnvSync(__instance);
        }
    }

    /// <summary>
    /// EnvSync.UpdateNetwork queues a snapshot to every subscriber every 5s.
    /// That queued send is flushed after our override snapshot, so the client
    /// briefly applies real server daytime and flashes. Skip the queue for
    /// players who already have a /mytime override; they get their own snapshots.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.QueueUpdate))]
    public static class Patch_BasePlayer_QueueUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, BaseNetworkable ent)
        {
            if (!(ent is EnvSync)) return true;
            if (__instance == null) return true;
            return !AdminTimeMod.HasTimeOverride(__instance);
        }
    }

    /// <summary>
    /// ToStreamForNetwork calls the virtual CanUseNetworkCache on BaseEntity.
    /// Override players must not receive the shared daytime cache.
    /// </summary>
    [HarmonyPatch(typeof(BaseEntity), nameof(BaseEntity.CanUseNetworkCache))]
    public static class Patch_BaseEntity_CanUseNetworkCache
    {
        [HarmonyPostfix]
        public static void Postfix(BaseEntity __instance, Connection connection, ref bool __result)
        {
            if (!__result) return;
            if (!(__instance is EnvSync)) return;
            var player = connection?.player as BasePlayer;
            if (player != null && AdminTimeMod.HasTimeOverride(player))
                __result = false;
        }
    }

    /// <summary>
    /// Rewrite this connection's sky time when they have a /mytime override.
    /// </summary>
    [HarmonyPatch(typeof(EnvSync), nameof(EnvSync.Save))]
    public static class Patch_EnvSync_Save
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable.SaveInfo info)
        {
            if (info.forDisk || info.forConnection == null || info.msg?.environment == null) return;
            var player = info.forConnection.player as BasePlayer;
            if (player == null) return;
            float hour = AdminTimeMod.GetPlayerTime(player);
            if (hour < 0f) return;
            System.DateTime current = System.DateTime.FromBinary(info.msg.environment.dateTime);
            info.msg.environment.dateTime = current.Date.AddHours(hour).ToBinary();
        }
    }
}
