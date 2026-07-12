using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieHorde
{
    [Flags]
    public enum EntityType
    {
        Player = 1,
        BasePlayerNPC = 2,
        NPC = 4
    }

    public enum NPCType
    {
        HeavyScientist,
        Scientist,
        Scarecrow,
        BanditGuard,
        TunnelDweller,
        GingerBreadMan
    }

    public enum SensationType
    {
        Gunshot,
        ThrownWeapon,
        Explosion
    }

    public struct Sensation
    {
        public SensationType Type;
        public Vector3 Position;
        public float Radius;
        public float DamagePotential;
        public BaseEntity Initiator;
        public BasePlayer InitiatorPlayer;
        public BaseEntity UsedEntity;
    }

    public static class Sense
    {
        private static readonly BaseEntity[] Query = new BaseEntity[512];

        private static bool IsAbleToBeStimulated(BaseEntity entity)
        {
            if (entity == null) return false;
            return entity.GetComponent<ZombieNPC>() != null;
        }

        public static void Stimulate(Sensation sensation)
        {
            int inSphere = BaseEntity.Query.Server.GetInSphere(sensation.Position, sensation.Radius, Query, IsAbleToBeStimulated);
            float radiusSq = sensation.Radius * sensation.Radius;
            for (int i = 0; i < inSphere; i++)
            {
                ZombieNPC zombie = Query[i] != null ? Query[i].GetComponent<ZombieNPC>() : null;
                if (zombie == null || zombie.IsDestroyed) continue;
                Vector3 delta = zombie.transform.position - sensation.Position;
                if (delta.sqrMagnitude > radiusSq) continue;
                zombie.OnSensation(sensation);
            }
        }
    }

    public static class NavmeshSpawnPoint
    {
        public static bool Find(Vector3 position, float radius, out Vector3 result)
        {
            result = position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }

            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 sample = position + new Vector3(offset.x, 0f, offset.y);
                sample.y = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(sample) : sample.y;
                if (NavMesh.SamplePosition(sample, out hit, radius, NavMesh.AllAreas))
                {
                    result = hit.position;
                    return true;
                }
            }

            return false;
        }
    }

    public class Definitions
    {
        public LootContainer.LootSpawnSlot[] LootSpawns;
        public PlayerInventoryProperties[] Loadouts;

        public static Definitions FromScarecrow()
        {
            var defs = new Definitions();
            try
            {
                GameObject prefab = GameManager.server.FindPrefab("assets/prefabs/npc/scarecrow/scarecrow.prefab");
                ScarecrowNPC scarecrow = prefab != null ? prefab.GetComponent<ScarecrowNPC>() : null;
                if (scarecrow != null)
                {
                    defs.Loadouts = scarecrow.loadouts;
                    defs.LootSpawns = scarecrow.LootSpawnSlots;
                }
            }
            catch (Exception ex)
            {
                Compat.PrintWarning("Definitions.FromScarecrow: " + ex.Message);
            }
            return defs;
        }
    }

    [Serializable]
    public class NPCSettings
    {
        public class VitalStats
        {
            public float Health { get; set; } = 200f;
        }

        public class MovementStats
        {
            public float Speed { get; set; } = 6.2f;
            public float Acceleration { get; set; } = 12f;

            [JsonProperty(PropertyName = "Turn speed")]
            public float TurnSpeed { get; set; } = 120f;

            [JsonProperty(PropertyName = "Speed multiplier - Slowest")]
            public float SlowestSpeedFraction { get; set; } = 0.1f;

            [JsonProperty(PropertyName = "Speed multiplier - Slow")]
            public float SlowSpeedFraction { get; set; } = 0.3f;

            [JsonProperty(PropertyName = "Speed multiplier - Normal")]
            public float NormalSpeedFraction { get; set; } = 0.5f;

            [JsonProperty(PropertyName = "Speed multiplier - Fast")]
            public float FastSpeedFraction { get; set; } = 1f;

            [JsonProperty(PropertyName = "Speed multiplier - Low health")]
            public float LowHealthMaxSpeedFraction { get; set; } = 0.5f;

            [JsonIgnore]
            public bool CanSwim { get; set; }

            [JsonIgnore]
            public float SwimmingSpeedMultiplier { get; set; } = 0.4f;

            [JsonIgnore]
            public float MaxWaterDepth { get; set; } = 0.5f;

            public virtual void ApplySettingsToNavigator(BaseNavigator baseNavigator)
            {
                if (baseNavigator == null) return;
                baseNavigator.Acceleration = Acceleration;
                baseNavigator.FastSpeedFraction = FastSpeedFraction;
                baseNavigator.LowHealthMaxSpeedFraction = LowHealthMaxSpeedFraction;
                baseNavigator.NormalSpeedFraction = NormalSpeedFraction;
                baseNavigator.SlowestSpeedFraction = SlowestSpeedFraction;
                baseNavigator.SlowSpeedFraction = SlowSpeedFraction;
                baseNavigator.Speed = Speed;
                baseNavigator.TurnSpeed = TurnSpeed;
                if (CanSwim)
                {
                    baseNavigator.SwimmingSpeedMultiplier = SwimmingSpeedMultiplier;
                    baseNavigator.MaxWaterDepth = MaxWaterDepth;
                }
            }
        }

        public class SensoryStats
        {
            [JsonProperty(PropertyName = "Attack range multiplier")]
            public float AttackRangeMultiplier { get; set; } = 1.5f;

            [JsonProperty(PropertyName = "Sense range")]
            public float SenseRange { get; set; } = 30f;

            [JsonProperty(PropertyName = "Listen range")]
            public float ListenRange { get; set; } = 20f;

            [JsonProperty(PropertyName = "Target lost range")]
            public float TargetLostRange { get; set; } = 90f;

            [JsonProperty(PropertyName = "Target lost range time (seconds)")]
            public float TargetLostRangeTime { get; set; } = 5f;

            [JsonProperty(PropertyName = "Target lost LOS time (seconds)")]
            public float TargetLostLOSTime { get; set; } = 5f;

            [JsonProperty(PropertyName = "Ignore sneaking outside of vision range")]
            public bool IgnoreNonVisionSneakers { get; set; } = true;

            [JsonProperty(PropertyName = "Vision cone (0 - 180 degrees)")]
            public float VisionCone { get; set; } = 135f;

            [JsonProperty(PropertyName = "Ignore players in safe zone")]
            public bool IgnoreSafeZonePlayers { get; set; } = true;

            [JsonIgnore]
            public bool UseThrowableExplosives { get; set; }

            [JsonIgnore]
            public bool UseMedicalItems { get; set; }

            [JsonIgnore]
            public float HealChance { get; set; } = 0.5f;

            [JsonIgnore]
            public float HealBelowHealthFraction { get; set; } = 0.5f;

            public void ApplySettingsToBrain(BaseAIBrain brain)
            {
                if (brain == null) return;
                brain.MaxGroupSize = int.MaxValue;
                brain.AttackRangeMultiplier = 1f;
                brain.SenseRange = SenseRange;
                brain.ListenRange = ListenRange;
                brain.TargetLostRange = TargetLostRange;
                brain.CheckVisionCone = IgnoreNonVisionSneakers;
                brain.IgnoreNonVisionSneakers = IgnoreNonVisionSneakers;
                brain.IgnoreSafeZonePlayers = IgnoreSafeZonePlayers;
                brain.CanUseHealingItems = UseMedicalItems;
                brain.HealChance = HealChance;
                brain.HealBelowHealthFraction = HealBelowHealthFraction;
                brain.VisionCone = Vector3.Dot(Vector3.forward, Quaternion.Euler(0f, VisionCone, 0f) * Vector3.forward);
            }
        }

        [JsonProperty(PropertyName = "NPC types (HeavyScientist, Scientist, Scarecrow, BanditGuard, TunnelDweller, GingerBreadMan)")]
        public NPCType[] Types { get; set; } = new NPCType[1];

        [JsonProperty(PropertyName = "Display names (Chosen at random)")]
        public string[] DisplayNames { get; set; } = Array.Empty<string>();

        [JsonProperty(PropertyName = "Kits (Chosen at random)")]
        public string[] Kits { get; set; } = Array.Empty<string>();

        [JsonProperty(PropertyName = "Don't drop loot with corpse")]
        public bool StripCorpseLoot { get; set; }

        [JsonProperty(PropertyName = "Drop inventory as loot")]
        public bool DropInventoryOnDeath { get; set; }

        [JsonProperty(PropertyName = "Drop one of the specified AlphaLoot profiles as loot")]
        public string[] DropAlphaLootProfiles { get; set; } = Array.Empty<string>();

        [JsonProperty(PropertyName = "Max roam range")]
        public float RoamRange { get; set; } = -1f;

        [JsonProperty(PropertyName = "Max chase range")]
        public float ChaseRange { get; set; } = -1f;

        [JsonProperty(PropertyName = "Aim cone scale")]
        public float AimConeScale { get; set; } = 2f;

        [JsonProperty(PropertyName = "Kill in safe zone")]
        public bool KillInSafeZone { get; set; } = true;

        [JsonProperty(PropertyName = "Can be targeted by NPC auto turrets")]
        public bool TargetedByNPCTurrets { get; set; }

        [JsonProperty(PropertyName = "Despawn time (seconds)")]
        public float DespawnTime { get; set; }

        [JsonIgnore]
        public bool StartDead { get; set; }

        [JsonProperty(PropertyName = "Wounded chance (x out of 100)")]
        public float WoundedChance { get; set; }

        [JsonProperty(PropertyName = "Wounded duration min (seconds)")]
        public float WoundedDurationMin { get; set; }

        [JsonProperty(PropertyName = "Wounded duration max (seconds)")]
        public float WoundedDurationMax { get; set; }

        [JsonProperty(PropertyName = "Wounded recovery chance (x out of 100)")]
        public float WoundedRecoveryChance { get; set; } = 100f;

        [JsonProperty(PropertyName = "Prevent friendly fire")]
        public bool PreventFriendlyFire { get; set; } = true;

        [JsonIgnore]
        public bool EnableNavMesh { get; set; } = true;

        [JsonIgnore]
        public bool EquipWeapon { get; set; } = true;

        [JsonIgnore]
        public bool CanUseWeaponMounted { get; set; }

        [JsonProperty(PropertyName = "Kill if under water")]
        public bool KillUnderWater { get; set; } = true;

        public VitalStats Vitals { get; set; } = new VitalStats();
        public MovementStats Movement { get; set; } = new MovementStats();
        public SensoryStats Sensory { get; set; } = new SensoryStats();
    }

    /// <summary>
    /// Minimal spatial grid used when Facepunch Spatial.Grid is inaccessible.
    /// Prefer Spatial.Grid from Facepunch.System when available (see HordeGridWrapper).
    /// </summary>
    public class SimpleGrid<T> where T : class
    {
        private readonly Dictionary<long, List<T>> _cells = new Dictionary<long, List<T>>();
        private readonly Dictionary<T, long> _keys = new Dictionary<T, long>();
        private readonly float _cellSize;

        public SimpleGrid(int _, float worldSize)
        {
            _cellSize = Mathf.Max(1f, worldSize / 32f);
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private long CellKey(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / _cellSize);
            int cz = Mathf.FloorToInt(z / _cellSize);
            return Key(cx, cz);
        }

        public void Add(T item, float x, float z)
        {
            if (item == null) return;
            Remove(item);
            long key = CellKey(x, z);
            if (!_cells.TryGetValue(key, out var list))
                _cells[key] = list = new List<T>();
            list.Add(item);
            _keys[item] = key;
        }

        public void Move(T item, float x, float z)
        {
            if (item == null) return;
            long key = CellKey(x, z);
            if (_keys.TryGetValue(item, out long old) && old == key) return;
            Add(item, x, z);
        }

        public void Remove(T item)
        {
            if (item == null) return;
            if (!_keys.TryGetValue(item, out long key)) return;
            _keys.Remove(item);
            if (_cells.TryGetValue(key, out var list))
            {
                list.Remove(item);
                if (list.Count == 0) _cells.Remove(key);
            }
        }

        public int Query(float x, float z, float radius, T[] results, Func<T, bool> filter)
        {
            if (results == null || results.Length == 0) return 0;
            int count = 0;
            int minX = Mathf.FloorToInt((x - radius) / _cellSize);
            int maxX = Mathf.FloorToInt((x + radius) / _cellSize);
            int minZ = Mathf.FloorToInt((z - radius) / _cellSize);
            int maxZ = Mathf.FloorToInt((z + radius) / _cellSize);
            float r2 = radius * radius;
            for (int cx = minX; cx <= maxX; cx++)
            {
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    if (!_cells.TryGetValue(Key(cx, cz), out var list)) continue;
                    for (int i = 0; i < list.Count && count < results.Length; i++)
                    {
                        T item = list[i];
                        if (item == null) continue;
                        if (filter != null && !filter(item)) continue;
                        results[count++] = item;
                    }
                }
            }
            return count;
        }
    }
}
