using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Oxide CanBradleyApcTarget — after bradley builds its target list, drop targets the event rejects
    /// (sleepers / safe-zone / non-players when not aggressive).
    /// </summary>
    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.UpdateTargetList))]
    public static class Patch_BradleyAPC_UpdateTargetList
    {
        [HarmonyPostfix]
        public static void Postfix(BradleyAPC __instance)
        {
            if (__instance == null || __instance.targetList == null || __instance.targetList.Count == 0)
                return;

            for (int i = __instance.targetList.Count - 1; i >= 0; i--)
            {
                var info = __instance.targetList[i];
                if (info == null || info.entity == null) continue;
                object result = ATPlugin.Dispatch_CanBradleyApcTarget(__instance, info.entity);
                if (result != null)
                    __instance.targetList.RemoveAt(i);
            }
        }
    }
}
