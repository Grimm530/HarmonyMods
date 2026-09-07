using HarmonyLib;
using UnityEngine;

namespace GrimmCuiHarmony.Patches
{
    [HarmonyPatch(typeof(CommunityEntity), "Hook_DragRPC")]
    internal static class Cui_DragRPC_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type)
        {
            GrimmCui.RouteDrag(player, name, position, type);
        }
    }
}
