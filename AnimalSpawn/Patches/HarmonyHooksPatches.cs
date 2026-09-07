using HarmonyLib;
using UnityEngine;

namespace AnimalSpawn
{
    /// <summary>
    /// Harmony hook replacements used by AnimalSpawn. Horse claim/spawn patches stay in Shop.
    /// </summary>
    internal static class HarmonyHooksPatches
    {
        private static AnimalSpawn Ins => AnimalSpawn.Instance;

        [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
        private static class Patch_OnEntityKill
        {
            private static void Prefix(BaseNetworkable __instance)
            {
                if (Ins == null) return;
                if (__instance is AnimalSpawn.CustomAnimalNpc animal)
                    Ins.OnEntityKill(animal);
            }
        }

        [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.GetBestTarget))]
        private static class Patch_OnNpcTarget_GetBestTarget
        {
            private static void Postfix(HumanNPC __instance, ref BaseEntity __result)
            {
                if (Ins == null || __result == null) return;
                if (__result is AnimalSpawn.CustomAnimalNpc victim && __instance is NPCPlayer attacker)
                {
                    object r = Ins.OnNpcTarget(attacker, victim);
                    if (r != null) __result = null;
                }
            }
        }

        [HarmonyPatch(typeof(BaseNpc), nameof(BaseNpc.GetWantsToAttack))]
        private static class Patch_OnNpcTarget_BaseNpc
        {
            private static bool Prefix(BaseNpc __instance, BaseEntity target, ref float __result)
            {
                if (Ins == null || target == null) return true;
                if (!(__instance is BaseAnimalNPC animal) || !(target is AnimalSpawn.CustomAnimalNpc victim))
                    return true;

                object r = Ins.OnNpcTarget(animal, victim);
                if (r != null)
                {
                    __result = 0f;
                    return false;
                }
                return true;
            }
        }
    }
}
