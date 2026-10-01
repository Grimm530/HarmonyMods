namespace AnimalSpawn
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "AnimalSpawn";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 115, Prefix);
            GrimmCoreBridge.RegisterKillObserver(ModId, 115, Kill);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }

        private static void Kill(BaseNetworkable entity)
        {
            if (AnimalSpawn.Instance == null) return;
            if (entity is AnimalSpawn.CustomAnimalNpc animal)
                AnimalSpawn.Instance.OnEntityKill(animal);
        }

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (AnimalSpawn.Instance == null || entity == null || info == null) return null;
            if (!AnimalSpawn.IsCustomAnimal(entity) && !AnimalSpawn.IsCustomAnimal(info.Initiator)) return null;
            return GrimmCoreHurtSemantics.BlockIfHandled(AnimalSpawn.Instance.OnEntityTakeDamage(entity, info));
        }
    }
}
