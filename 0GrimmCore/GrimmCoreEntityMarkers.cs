using System;
using Rust.Ai.Gen2;

namespace GrimmCoreHarmony
{
    /// <summary>
    /// Who is allowed to run AI after the vanilla HumanNPC/Scarecrow cull.
    /// nivex: disable map scientists; keep tagged custom NPCs. Wildlife is not part of that cull.
    /// </summary>
    public static class GrimmCoreEntityMarkers
    {
        /// <summary>Primary blank workshop skin for custom/mod entities.</summary>
        public const ulong CustomEntitySkinId = 3793751435UL;

        /// <summary>Legacy GrimmNPC scientist marker (invalid Steam id — kept for existing saves).</summary>
        public const ulong LegacyGrimmNpcSkinId = 11162132011012UL;

        /// <summary>RaidableBases raid entity marker (buildings, turrets, NPCs).</summary>
        public const ulong LegacyRaidableBasesSkinId = 3710562502UL;

        /// <summary>Legacy AnimalSpawn custom animal marker.</summary>
        public const ulong LegacyAnimalSpawnSkinId = 11491311214163UL;

        public static bool IsWildlife(BaseEntity entity)
        {
            if (entity == null)
                return false;
            if (entity is BaseNpc || entity is BaseAnimalNPC)
                return true;
            if (entity is BaseNPC2)
                return true;
            return false;
        }

        public static bool IsMarkedCustom(BaseEntity entity)
        {
            if (entity == null)
                return false;

            ulong skin = entity.skinID;
            if (skin == CustomEntitySkinId
                || skin == LegacyGrimmNpcSkinId
                || skin == LegacyRaidableBasesSkinId
                || skin == LegacyAnimalSpawnSkinId)
                return true;

            string typeName = entity.GetType().Name;
            return typeName.IndexOf("CustomScientist", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("HumanoidNPC", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("ZombieNPC", StringComparison.Ordinal) >= 0;
        }

        public static bool IsAiAllowed(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
                return false;

            // Animals / Gen2 wildlife never belong in the scientist cull.
            if (IsWildlife(entity))
                return true;

            if (entity is BasePlayer player && !player.IsNpc)
                return true;

            return IsMarkedCustom(entity);
        }
    }
}
