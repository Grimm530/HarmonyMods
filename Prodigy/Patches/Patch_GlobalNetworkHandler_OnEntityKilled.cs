using HarmonyLib;

namespace Prodigy.Patches;

[HarmonyPatch(typeof(GlobalNetworkHandler), nameof(GlobalNetworkHandler.OnEntityKilled))]
public static class Patch_GlobalNetworkHandler_OnEntityKilled
{
    static void Postfix(BaseNetworkable entity)
    {
        if (entity == null || !(entity is BuildingPrivlidge or BuildingBlock)) return;

        var mod = ProdigyMod.Instance;
        if (mod == null) return;

        mod.OnEntityKilled(entity as BaseEntity);
    }
}
