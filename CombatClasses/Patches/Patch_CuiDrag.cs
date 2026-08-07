using HarmonyLib;
using UnityEngine;
using CCPlugin = Oxide.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
{
    [HarmonyPatch(typeof(CommunityEntity), "Hook_DragRPC")]
    public static class CommunityEntity_Hook_DragRPC_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type)
        {
            try { CCPlugin.Dispatch_OnCuiDraggableDrag(player, name, position, type); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnCuiDraggableDrag: " + ex.Message); }
        }
    }
}
