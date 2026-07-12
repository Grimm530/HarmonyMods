using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Oxide CanHelicopterTarget — strip rejected players from the event heli target list.
    /// </summary>
    [HarmonyPatch(typeof(PatrolHelicopterAI), nameof(PatrolHelicopterAI.UpdateTargetList))]
    public static class Patch_PatrolHelicopterAI_UpdateTargetList
    {
        [HarmonyPostfix]
        public static void Postfix(PatrolHelicopterAI __instance)
        {
            if (__instance == null || __instance._targetList == null || __instance._targetList.Count == 0)
                return;

            for (int i = __instance._targetList.Count - 1; i >= 0; i--)
            {
                var entry = __instance._targetList[i];
                BasePlayer player = entry?.ply;
                if (player == null) continue;
                object result = ATPlugin.Dispatch_CanHelicopterTarget(__instance, player);
                if (result != null)
                    __instance._targetList.RemoveAt(i);
            }
        }
    }
}
