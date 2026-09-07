using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    /// <summary>
    /// compat routes draggable UI moves through the OnCuiDraggableDrag hook via CommunityEntity.Hook_DragRPC.
    /// Harmony mods must patch that path explicitly (see CombatClasses Patch_CuiDrag.cs).
    /// </summary>
    [HarmonyPatch(typeof(CommunityEntity), nameof(CommunityEntity.Hook_DragRPC))]
    internal static class Cui_DragRPC_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type)
        {
            try
            {
                RaidableBasesHost.Instance?.ModInstance?.DispatchOnCuiDraggableDrag(player, name, position, type);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[RaidableBases] OnCuiDraggableDrag: " + ex.Message);
            }
        }
    }
}
