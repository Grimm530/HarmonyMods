using HarmonyLib;

namespace NexusStaticPortals.Patches
{
    [HarmonyPatch(typeof(BasePortal), nameof(BasePortal.UsePortal))]
    internal static class BasePortal_UsePortal_Patch
    {
        private static bool Prefix(BasePortal __instance, BasePlayer player)
        {
            var mod = NexusStaticPortalsMod.Instance;
            if (mod == null)
                return true;

            return !mod.TryHandlePortalUse(player, __instance);
        }
    }
}
