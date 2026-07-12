using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// Deny real players from mounting convoy vehicles/seats (NPCs are still allowed).
    /// Port of the Oxide Convoy CanMountEntity hook.
    /// </summary>
    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.AttemptMount), new[] { typeof(BasePlayer), typeof(bool) })]
    public static class Patch_BaseMountable_AttemptMount
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return true;
            if (player is NPCPlayer) return true; // convoy NPCs mount freely

            var ec = EventController.Instance;
            if (ec == null) return true;

            if (__instance.net != null && ec.IsConvoyVehicle((ulong)__instance.net.ID.Value))
                return false;

            var parent = __instance.GetParentEntity();
            if (parent?.net != null && ec.IsConvoyVehicle((ulong)parent.net.ID.Value))
                return false;

            return true;
        }
    }
}
