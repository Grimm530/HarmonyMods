using HarmonyLib;
using Rust.Ai.Gen2;

namespace GrimmNPC2.Patches
{
    /// <summary>
    /// GEN2 scientists use <see cref="NpcShootingComponent.weaponItemDefinition"/> + <see cref="NpcShootingComponent.ServerInitPostNetworkGroupAssign"/>,
    /// not <see cref="PlayerInventory"/>. This runs before that method so boss JSON "first belt weapon" can replace the prefab default.
    /// Pending data must be peeked here because <see cref="BaseEntity.Spawn"/> postfix (which consumes pending) runs after component inits.
    /// </summary>
    [HarmonyPatch(typeof(NpcShootingComponent), nameof(NpcShootingComponent.ServerInitPostNetworkGroupAssign))]
    internal static class NpcShootingWeaponPatchGen2
    {
        private static void Prefix(NpcShootingComponent __instance)
        {
            BaseEntity ent = __instance?.baseEntity;
            if (ent == null) return;

            CustomNpcData2 data = null;
            if (!GrimmNPC2.TryPeekPendingNpcData(ent, out data) || data == null) return;
            if (string.IsNullOrEmpty(data.Gen2WeaponItemShortName)) return;

            ItemDefinition def = ItemManager.FindItemDefinition(data.Gen2WeaponItemShortName);
            if (def == null)
            {
                UnityEngine.Debug.LogWarning("[GrimmNPC2] Gen2WeaponItemShortName not found: " + data.Gen2WeaponItemShortName);
                return;
            }

            __instance.weaponItemDefinition = def;
        }
    }
}
