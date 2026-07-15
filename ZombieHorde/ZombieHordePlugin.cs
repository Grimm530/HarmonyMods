using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Facepunch;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ZombieHorde
{
    public class ZombieHordePlugin
    {
        public static ZombieHordePlugin Instance { get; set; }
        public static readonly VersionNumber Version = new VersionNumber(0, 6, 351);

        public const string ADMIN_PERMISSION = "zombiehorde.admin";
        public const string IGNORE_PERMISSION = "zombiehorde.ignore";
        public const string IGNORE_UNTIL_HURT_PERMISSION = "zombiehorde.ignoreuntilhurt";

        public static readonly HashSet<string> IgnoreUntilHurtPlayers = new HashSet<string>();

        private static BaseNavigator.NavigationSpeed DefaultRoamSpeed;
        public static BaseNavigator.NavigationSpeed DefaultRoamSpeedPublic => DefaultRoamSpeed;
        private Compat.PluginRef Kits;
        private Compat.PluginRef Spawns;
        private List<Vector3> _spawnPoints;
        private SpawnSystem _spawnSystem = SpawnSystem.None;

        private const int SPAWN_RAYCAST_MASK = 1 << 0 | 1 << 8 | 1 << 15 | 1 << 17 | 1 << 21 | 1 << 29;
        private const TerrainTopology.Enum SPAWN_TOPOLOGY_MASK =
            TerrainTopology.Enum.Ocean | TerrainTopology.Enum.River | TerrainTopology.Enum.Lake |
            TerrainTopology.Enum.Cliff | TerrainTopology.Enum.Cliffside | TerrainTopology.Enum.Offshore |
            TerrainTopology.Enum.Summit | TerrainTopology.Enum.Decor | TerrainTopology.Enum.Monument;

        public void Init()
        {
            Instance = this;
            Compat.EnsureFolders();
            LoadConfig();
            Compat.Lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Notification.BeginSpawn"] = "<color=#ce422b>CAUTION!</color> Zombies have been spotted in the area",
                ["Notification.BeginDespawn"] = "<color=#ce422b>CAUTION!</color> The zombies appear to be dispersing"
            }, this);

            Compat.Permission.RegisterPermission(ADMIN_PERMISSION, this);
            Compat.Permission.RegisterPermission(IGNORE_PERMISSION, this);
            Compat.Permission.RegisterPermission(IGNORE_UNTIL_HURT_PERMISSION, this);

            Kits = new Compat.PluginRef("Kits");
            Spawns = new Compat.PluginRef("Spawns");

            GrimmNpcBridge.Bind();

            Compat.RegisterConsoleCommand("horde", OnConsoleHorde, true);
            Compat.RegisterConsoleCommand("hordeinfo", OnConsoleHordeInfo, false);

            // Defer world init until ServerMgr is ready
            Compat.Timer.Once(2f, OnServerInitialized);
        }

        public void Shutdown()
        {
            IgnoreUntilHurtPlayers.Clear();
            RaidingZombies.Shutdown();
            Compat.Timer.CancelAll();
            Horde.SpawnOrder.OnUnload();

            for (int i = Horde.AllHordes.Count - 1; i >= 0; i--)
                Horde.AllHordes[i].Destroy(true, true);
            Horde.AllHordes.Clear();

            ZombieNPC[] strays = UnityEngine.Object.FindObjectsOfType<ZombieNPC>();
            for (int i = 0; i < strays?.Length; i++)
            {
                if (strays[i] != null && !strays[i].IsDestroyed)
                    strays[i].Kill(BaseNetworkable.DestroyMode.None);
            }

            Compat.UnregisterCommands();
            ConfigData.Configuration = null;
            Instance = null;
        }

        private void OnServerInitialized()
        {
            GrimmNpcBridge.Bind();

            DefaultRoamSpeed = ParseType<BaseNavigator.NavigationSpeed>(ConfigData.Configuration.Horde.DefaultRoamSpeed);
            ValidateLoadoutProfiles();
            ValidateSpawnSystem();
            Horde.SpawnOrder.InitializeSpawnOrders();
            CreateMonumentHordeOrders();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);

            if (ConfigData.Configuration.Loot.DropAlphaLootProfiles?.Length > 0)
                AlphaLootHarmonyIntegration.Warmup();

            if (ConfigData.Configuration.Raiding == null)
                ConfigData.Configuration.Raiding = new ConfigData.RaidingZombiesOptions();
            RaidingZombies.Init();

            Compat.Puts("Initialized. GrimmNPC=" + GrimmNpcBridge.Available + " Hordes queued/active=" + Horde.AllHordes.Count);
        }

        #region Config

        public void LoadConfig()
        {
            try
            {
                string path = Compat.ConfigPath;
                if (!File.Exists(path) && File.Exists(Compat.OxideConfigPath))
                {
                    Directory.CreateDirectory(Compat.ConfigDirectory);
                    File.Copy(Compat.OxideConfigPath, path);
                    Compat.Puts("Migrated oxide/config/ZombieHorde.json -> HarmonyConfig/ZombieHorde.json");
                }

                if (!File.Exists(path))
                {
                    ConfigData.Configuration = GetBaseConfig();
                    SaveConfig();
                    Compat.Puts("Created default HarmonyConfig/ZombieHorde.json");
                    return;
                }

                ConfigData.Configuration = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
                if (ConfigData.Configuration == null)
                    ConfigData.Configuration = GetBaseConfig();

                if (ConfigData.Configuration.Version < Version)
                    UpdateConfigValues();

                SaveConfig();
            }
            catch (Exception ex)
            {
                Compat.PrintError("LoadConfig failed: " + ex);
                ConfigData.Configuration = GetBaseConfig();
            }
        }

        public void SaveConfig()
        {
            try
            {
                if (ConfigData.Configuration == null) return;
                ConfigData.Configuration.Version = Version;
                Directory.CreateDirectory(Compat.ConfigDirectory);
                File.WriteAllText(Compat.ConfigPath, JsonConvert.SerializeObject(ConfigData.Configuration, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Compat.PrintError("SaveConfig: " + ex.Message);
            }
        }

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                Horde = new ConfigData.HordeOptions
                {
                    InitialMemberCount = 3,
                    MaximumHordes = 5,
                    MaximumMemberCount = 10,
                    GrowthRate = 300,
                    CreateOnDeath = true,
                    ForgetTime = 10f,
                    MergeHordes = true,
                    RespawnTime = 900,
                    SpawnType = "Random",
                    SpawnFile = "",
                    DefaultRoamSpeed = BaseNavigator.NavigationSpeed.Slow.ToString(),
                    LocalRoam = false,
                    RoamDistance = 150,
                    UseProfiles = false,
                    RandomProfiles = new List<string>(),
                    UseSenses = true,
                    RaidOnlinePlayersAtBases = true,
                    OnlineBaseRaidScanRange = 250f,
                    OnlineBaseRaidScanInterval = 5f
                },
                Member = new ConfigData.MemberOptions
                {
                    IgnoreSleepers = false,
                    TargetAnimals = true,
                    TargetedByAnimals = true,
                    TargetedByNPCs = true,
                    TargetedByTurrets = false,
                    TargetedByNPCTurrets = true,
                    TargetedByAPC = false,
                    TargetNPCs = true,
                    TargetNPCsThatAttack = true,
                    TargetHumanNPCs = false,
                    GiveGlowEyes = true,
                    HeadshotKills = true,
                    MinimumHeadshotDamage = 25f,
                    Loadouts = new List<ConfigData.MemberOptions.Loadout>
                    {
                        new ConfigData.MemberOptions.Loadout("loadout-0")
                    },
                    KillUnderWater = true,
                    TargetedByPeaceKeeperTurrets = true,
                    EnableDormantSystem = true,
                    DormantUntilSensedOnly = false,
                    EnableZombieNoises = true,
                    CanSwim = true,
                    CanMountVehicles = true,
                    TargetInBuildings = true,
                    IgnoreBuildingMultiplierNotOwner = true,
                    ExplosiveBuildingDamageMultiplier = 1f,
                    MaxExplosiveThrowRange = 20f,
                    MeleeBuildingDamageMultiplier = 1f,
                    DespawnDudExplosives = true,
                    ExplodeDudExplosives = false
                },
                Loot = new ConfigData.LootTable
                {
                    DropInventory = false,
                    Random = new ConfigData.LootTable.RandomLoot(),
                    DropAlphaLootProfiles = Array.Empty<string>(),
                    DroppedBlacklist = new[] { "exampleitem.shortname1", "exampleitem.shortname2" }
                },
                TimedSpawns = new ConfigData.TimedSpawnOptions
                {
                    Enabled = false,
                    Despawn = true,
                    Start = 18f,
                    End = 6f
                },
                HordeProfiles = new Dictionary<string, List<string>>
                {
                    ["Profile1"] = new List<string> { "loadout-0" }
                },
                Monument = new ConfigData.MonumentSpawn
                {
                    ArcticResearch = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 120, HordeSize = 10, Profile = "" },
                    Airfield = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 85, HordeSize = 10, Profile = "" },
                    Dome = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 50, HordeSize = 10 },
                    Junkyard = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 100, HordeSize = 10, Profile = "" },
                    GasStation = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 40, HordeSize = 10, Profile = "" },
                    Ferry = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 90, HordeSize = 10, Profile = "" },
                    LargeHarbor = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 120, HordeSize = 10, Profile = "" },
                    Powerplant = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 120, HordeSize = 10, Profile = "" },
                    StoneQuarry = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 40, HordeSize = 10, Profile = "" },
                    SulfurQuarry = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 40, HordeSize = 10, Profile = "" },
                    HQMQuarry = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 40, HordeSize = 10, Profile = "" },
                    Radtown = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 60, HordeSize = 10, Profile = "" },
                    LegacyRadtown = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 60, HordeSize = 10, Profile = "" },
                    LaunchSite = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 140, HordeSize = 10, Profile = "" },
                    Satellite = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 60, HordeSize = 10, Profile = "" },
                    SmallHarbor = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 80, HordeSize = 10, Profile = "" },
                    Supermarket = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 40, HordeSize = 10, Profile = "" },
                    Trainyard = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 100, HordeSize = 10, Profile = "" },
                    Tunnels = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 80, HordeSize = 10, Profile = "" },
                    Warehouse = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 40, HordeSize = 10, Profile = "" },
                    WaterTreatment = new ConfigData.MonumentSpawn.MonumentSettings { Enabled = false, RoamDistance = 100, HordeSize = 10, Profile = "" },
                    Custom = new List<ConfigData.MonumentSpawn.CustomSpawnPoints>()
                },
                Raiding = new ConfigData.RaidingZombiesOptions(),
                Version = Version
            };
        }

        private void UpdateConfigValues()
        {
            Compat.PrintWarning("Config update detected! Updating config values...");
            ConfigData baseConfig = GetBaseConfig();
            if (ConfigData.Configuration.Monument == null)
                ConfigData.Configuration.Monument = baseConfig.Monument;
            if (ConfigData.Configuration.TimedSpawns == null)
                ConfigData.Configuration.TimedSpawns = baseConfig.TimedSpawns;
            if (ConfigData.Configuration.HordeProfiles == null)
                ConfigData.Configuration.HordeProfiles = baseConfig.HordeProfiles;
            if (ConfigData.Configuration.Loot?.DroppedBlacklist == null)
                ConfigData.Configuration.Loot.DroppedBlacklist = baseConfig.Loot.DroppedBlacklist;
            if (ConfigData.Configuration.Raiding == null)
                ConfigData.Configuration.Raiding = baseConfig.Raiding;
            ConfigData.Configuration.Version = Version;
        }

        #endregion

        #region Helpers

        private T ParseType<T>(string type)
        {
            try { return (T)Enum.Parse(typeof(T), type, true); }
            catch { return default; }
        }

        public static bool IsInOrOnBuilding(BaseEntity baseEntity)
        {
            if (!baseEntity || baseEntity.IsDestroyed) return false;
            const int CONSTRUCTION_LAYER = 1 << 21;
            Vector3 position = baseEntity.transform.position;
            if (Physics.Raycast(position, Vector3.up, out RaycastHit raycastHit, 40f, CONSTRUCTION_LAYER) ||
                Physics.Raycast(position, Vector3.down, out raycastHit, 20f, CONSTRUCTION_LAYER))
            {
                BaseEntity hitEntity = raycastHit.collider.ToBaseEntity();
                return hitEntity is BuildingBlock or SimpleBuildingBlock;
            }
            return false;
        }

        private static bool ContainsTopologyAtPoint(TerrainTopology.Enum mask, Vector3 position) =>
            (TerrainMeta.TopologyMap.GetTopology(position, 1f) & (int)mask) != 0;

        private void ValidateLoadoutProfiles()
        {
            Compat.Puts("Validating horde profiles...");
            bool hasChanged = false;
            if (ConfigData.Configuration.HordeProfiles == null) return;

            for (int i = ConfigData.Configuration.HordeProfiles.Count - 1; i >= 0; i--)
            {
                string key = ConfigData.Configuration.HordeProfiles.ElementAt(i).Key;
                for (int y = ConfigData.Configuration.HordeProfiles[key].Count - 1; y >= 0; y--)
                {
                    string loadoutId = ConfigData.Configuration.HordeProfiles[key][y];
                    if (ConfigData.Configuration.Member.Loadouts.All(x => x.LoadoutID != loadoutId))
                    {
                        Compat.Puts($"Loadout profile {loadoutId} does not exist. Removing from config");
                        ConfigData.Configuration.HordeProfiles[key].Remove(loadoutId);
                        hasChanged = true;
                    }
                }
                if (ConfigData.Configuration.HordeProfiles[key].Count <= 0)
                {
                    Compat.Puts($"Horde profile {key} does not have any valid loadouts. Removing from config");
                    ConfigData.Configuration.HordeProfiles.Remove(key);
                    hasChanged = true;
                }
            }
            if (hasChanged) SaveConfig();
        }

        private bool ValidateSpawnSystem()
        {
            _spawnSystem = ParseType<SpawnSystem>(ConfigData.Configuration.Horde.SpawnType);
            if (_spawnSystem == SpawnSystem.None)
            {
                Compat.PrintError("Invalid Spawn Type. Unable to spawn hordes!");
                return false;
            }

            if (_spawnSystem == SpawnSystem.SpawnsDatabase)
            {
                if (!Spawns.IsLoaded)
                {
                    Compat.PrintError("SpawnsDatabase selected but Spawns plugin not loaded.");
                    return false;
                }
                if (string.IsNullOrEmpty(ConfigData.Configuration.Horde.SpawnFile))
                {
                    Compat.PrintError("SpawnsDatabase selected but no spawn file specified.");
                    return false;
                }
                object success = Spawns.Call("LoadSpawnFile", ConfigData.Configuration.Horde.SpawnFile);
                if (success is List<Vector3> list && list.Count > 0)
                {
                    _spawnPoints = list;
                    return true;
                }
                Compat.PrintError("Spawn file invalid or empty.");
                return false;
            }
            return true;
        }

        public Vector3 GetSpawnPoint()
        {
            if (_spawnSystem == SpawnSystem.SpawnsDatabase && Spawns.IsLoaded && _spawnPoints != null && _spawnPoints.Count > 0)
            {
                Vector3 spawnPoint = _spawnPoints.GetRandom();
                _spawnPoints.Remove(spawnPoint);
                if (_spawnPoints.Count == 0)
                    _spawnPoints = Spawns.Call("LoadSpawnFile", ConfigData.Configuration.Horde.SpawnFile) as List<Vector3>;
                return spawnPoint;
            }

            float size = (World.Size / 2f) * 0.75f;
            for (int i = 0; i < 10; i++)
            {
                Vector2 randomInCircle = Random.insideUnitCircle * size;
                Vector3 position = new Vector3(randomInCircle.x, 0, randomInCircle.y);
                if (TerrainMeta.HeightMap != null)
                    position.y = TerrainMeta.HeightMap.GetHeight(position);
                if (NavmeshSpawnPoint.Find(position, 25f, out position))
                {
                    if (Physics.SphereCast(new Ray(position + Vector3.up * 5f, Vector3.down), 10f, 10f, SPAWN_RAYCAST_MASK))
                        continue;
                    if (ContainsTopologyAtPoint(SPAWN_TOPOLOGY_MASK, position))
                        continue;
                    if (WaterLevel.GetWaterDepth(position, true, false, null) <= 0.01f)
                        return position;
                }
            }

            try
            {
                var sp = ServerMgr.FindSpawnPoint();
                if (sp != null)
                    return sp.pos;
            }
            catch { }

            return Vector3.zero;
        }

        private void CreateMonumentHordeOrders()
        {
            var monuments = new List<(string, Vector3, ConfigData.MonumentSpawn.MonumentSettings)>
            {
                ("powerplant_1", new Vector3(-30.8f, 0.2f, -15.8f), ConfigData.Configuration.Monument.Powerplant),
                ("military_tunnel_1", new Vector3(-7.4f, 13.4f, 53.8f), ConfigData.Configuration.Monument.Tunnels),
                ("arctic_research_base_a", new Vector3(-3.6f, 0.729f, 28.86f), ConfigData.Configuration.Monument.ArcticResearch),
                ("ferry_terminal_1", new Vector3(-6.9f, 5.3f, 6.2f), ConfigData.Configuration.Monument.Ferry),
                ("harbor_1", new Vector3(54.7f, 5.1f, -39.6f), ConfigData.Configuration.Monument.LargeHarbor),
                ("harbor_2", new Vector3(-66.6f, 4.9f, 16.2f), ConfigData.Configuration.Monument.SmallHarbor),
                ("airfield_1", new Vector3(-12.4f, 0.2f, -28.9f), ConfigData.Configuration.Monument.Airfield),
                ("trainyard_1", new Vector3(35.8f, 0.2f, -0.8f), ConfigData.Configuration.Monument.Trainyard),
                ("water_treatment_plant_1", new Vector3(11.1f, 0.3f, -80.2f), ConfigData.Configuration.Monument.WaterTreatment),
                ("warehouse", new Vector3(16.6f, 0.1f, -7.5f), ConfigData.Configuration.Monument.Warehouse),
                ("satellite_dish", new Vector3(18.6f, 6.0f, -7.5f), ConfigData.Configuration.Monument.Satellite),
                ("sphere_tank", new Vector3(-44.6f, 5.8f, -3.0f), ConfigData.Configuration.Monument.Dome),
                ("radtown_small_3", new Vector3(-16.3f, -2.1f, -3.3f), ConfigData.Configuration.Monument.Radtown),
                ("radtown_1", new Vector3(0f, 0.166f, 0f), ConfigData.Configuration.Monument.LegacyRadtown),
                ("launch_site_1", new Vector3(222.1f, 3.3f, 0.0f), ConfigData.Configuration.Monument.LaunchSite),
                ("gas_station_1", new Vector3(-9.8f, 3.0f, 7.2f), ConfigData.Configuration.Monument.GasStation),
                ("supermarket_1", new Vector3(5.5f, 0.0f, -20.5f), ConfigData.Configuration.Monument.Supermarket),
                ("mining_quarry_c", new Vector3(15.8f, 4.5f, -1.5f), ConfigData.Configuration.Monument.HQMQuarry),
                ("mining_quarry_a", new Vector3(-0.8f, 0.6f, 11.4f), ConfigData.Configuration.Monument.SulfurQuarry),
                ("mining_quarry_b", new Vector3(-7.6f, 0.2f, 12.3f), ConfigData.Configuration.Monument.StoneQuarry),
                ("junkyard_1", new Vector3(-16.7f, 0.2f, 1.4f), ConfigData.Configuration.Monument.Junkyard)
            };

            int count = 0;
            GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            foreach (GameObject gobject in allObjects)
            {
                if (count >= ConfigData.Configuration.Horde.MaximumHordes) break;
                if (!gobject.name.Contains("autospawn/monument")) continue;
                Transform tr = gobject.transform;
                Vector3 position = tr.position;
                if (position == Vector3.zero) continue;

                foreach ((string name, Vector3 offset, ConfigData.MonumentSpawn.MonumentSettings settings) in monuments)
                {
                    if (gobject.name.Contains(name) && settings != null && settings.Enabled)
                    {
                        Horde.SpawnOrder.Create(tr.TransformPoint(offset), settings);
                        count++;
                        break;
                    }
                }
            }

            if (ConfigData.Configuration.Monument.Custom != null)
            {
                foreach (var custom in ConfigData.Configuration.Monument.Custom)
                {
                    if (custom.Enabled && custom.Location.IsValid)
                    {
                        Horde.SpawnOrder.Create(custom.Location, ConfigData.Configuration.Horde.InitialMemberCount,
                            custom.HordeSize, custom.RoamDistance, custom.Profile);
                        count++;
                    }
                }
            }

            if (count < ConfigData.Configuration.Horde.MaximumHordes)
                CreateRandomHordes();
        }

        private void CreateRandomHordes()
        {
            int amountToCreate = ConfigData.Configuration.Horde.MaximumHordes - Horde.AllHordes.Count;
            for (int i = 0; i < amountToCreate; i++)
            {
                float roamDistance = ConfigData.Configuration.Horde.LocalRoam ? ConfigData.Configuration.Horde.RoamDistance : -1;
                string profile = string.Empty;
                if (ConfigData.Configuration.Horde.RandomProfiles?.Count > 0)
                    profile = ConfigData.Configuration.Horde.RandomProfiles.GetRandom();
                else if (ConfigData.Configuration.Horde.UseProfiles && ConfigData.Configuration.HordeProfiles?.Count > 0)
                    profile = ConfigData.Configuration.HordeProfiles.Keys.ToArray().GetRandom();

                Horde.SpawnOrder.Create(GetSpawnPoint(), ConfigData.Configuration.Horde.InitialMemberCount,
                    ConfigData.Configuration.Horde.MaximumMemberCount, roamDistance, profile);
            }
        }

        #endregion

        #region Hook handlers

        public void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            if (Compat.Permission.UserHasPermission(player.UserIDString, IGNORE_UNTIL_HURT_PERMISSION))
                IgnoreUntilHurtPlayers.Add(player.UserIDString);
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null)
                IgnoreUntilHurtPlayers.Remove(player.UserIDString);
        }

        public void OnEntityTakeDamage(BaseCombatEntity baseCombatEntity, HitInfo hitInfo)
        {
            if (ConfigData.Configuration == null || hitInfo == null) return;

            ZombieNPC victimZombie = ZombieNPC.Get(baseCombatEntity as BasePlayer);
            if (victimZombie != null)
            {
                // CH47 scientists / other NPCs kill zombies even when GetBestTarget hooks miss —
                // hard-cancel damage when config forbids NPC targeting.
                BasePlayer initiatorPlayer = hitInfo.InitiatorPlayer;
                if (initiatorPlayer != null && initiatorPlayer.IsNpc && ZombieNPC.Get(initiatorPlayer) == null)
                {
                    if (!ConfigData.Configuration.Member.TargetedByNPCs)
                    {
                        hitInfo.damageTypes.ScaleAll(0f);
                        return;
                    }
                }

                if (hitInfo.Initiator is BaseNpc && !(hitInfo.Initiator is BasePlayer))
                {
                    if (!ConfigData.Configuration.Member.TargetedByAnimals)
                    {
                        hitInfo.damageTypes.ScaleAll(0f);
                        return;
                    }
                }

                if (initiatorPlayer && !initiatorPlayer.IsNpc && IgnoreUntilHurtPlayers.Contains(initiatorPlayer.UserIDString))
                {
                    IgnoreUntilHurtPlayers.Remove(initiatorPlayer.UserIDString);
                    Compat.Permission.RevokeUserPermission(initiatorPlayer.UserIDString, IGNORE_UNTIL_HURT_PERMISSION);
                }
                victimZombie.OnHurtNotify(hitInfo);
                RaidingZombies.OnEntityTakeDamage(baseCombatEntity, hitInfo);
                return;
            }

            ZombieNPC zombieNpc = ZombieNPC.Get(hitInfo.InitiatorPlayer);
            if (zombieNpc == null || !baseCombatEntity) return;

            bool hasExplosionDamage = hitInfo.damageTypes.Get(DamageType.Explosion) > 0;

            if (baseCombatEntity is BuildingBlock or SimpleBuildingBlock or Door)
            {
                if (hasExplosionDamage)
                {
                    if (ConfigData.Configuration.Member.IgnoreBuildingMultiplierNotOwner || ConfigData.Configuration.Member.DisableBuildingMultiplierNotOwner)
                    {
                        BasePlayer target = zombieNpc.CurrentTarget as BasePlayer;
                        if (!target) goto SCALE_DAMAGE;
                        if (baseCombatEntity.OwnerID == target.userID) goto SCALE_DAMAGE;
                        BuildingPrivlidge buildingPrivilege = (baseCombatEntity as DecayEntity)?.GetBuildingPrivilege();
                        if (buildingPrivilege && buildingPrivilege.IsAuthed(target.userID)) goto SCALE_DAMAGE;
                        if (ConfigData.Configuration.Member.DisableBuildingMultiplierNotOwner)
                            hitInfo.damageTypes.Scale(DamageType.Explosion, 0);
                        return;
                    }
                    SCALE_DAMAGE:
                    hitInfo.damageTypes.Scale(DamageType.Explosion, ConfigData.Configuration.Member.ExplosiveBuildingDamageMultiplier);
                    RaidingZombies.OnEntityTakeDamage(baseCombatEntity, hitInfo);
                    return;
                }

                AttackEntity attackEntity = zombieNpc.GetAttackEntity();
                if (attackEntity is BaseMelee)
                {
                    hitInfo.damageTypes.ScaleAll(ConfigData.Configuration.Member.MeleeBuildingDamageMultiplier);
                    return;
                }
            }

            if (hasExplosionDamage)
            {
                if (baseCombatEntity is BasePlayer player)
                {
                    if (player.IsNpc) goto APPLY_MULTIPLIER;
                    hitInfo.damageTypes.ScaleAll(ConVar.Halloween.scarecrow_beancan_vs_player_dmg_modifier);
                    return;
                }
                hitInfo.damageTypes.ScaleAll(0);
                return;
            }

            APPLY_MULTIPLIER:
            float damageMultiplier = zombieNpc.Loadout?.DamageMultiplier ?? 1f;
            if (!Mathf.Approximately(damageMultiplier, 1f))
                hitInfo.damageTypes.ScaleAll(damageMultiplier);

            // RaidingZombies explosive scale (Oxide RaidingZombies.OnEntityTakeDamage / CanEntityTakeDamage)
            RaidingZombies.OnEntityTakeDamage(baseCombatEntity, hitInfo);
        }

        public object OnExplosiveDud(DudTimedExplosive dudTimedExplosive)
        {
            if (dudTimedExplosive?.creatorEntity == null) return null;
            if (ZombieNPC.Get(dudTimedExplosive.creatorEntity as BasePlayer) == null) return null;

            if (ConfigData.Configuration.Member.ExplodeDudExplosives)
                return false;

            if (!ConfigData.Configuration.Member.DespawnDudExplosives)
                return null;

            Compat.NextTick(() =>
            {
                if (dudTimedExplosive && !dudTimedExplosive.IsDestroyed)
                    dudTimedExplosive.KillMessage();
            });
            return null;
        }

        public void OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            if (!player || hitInfo == null) return;

            ZombieNPC zombieNpc = ZombieNPC.Get(player);
            if (zombieNpc != null)
            {
                zombieNpc.Horde?.OnMemberKilled(zombieNpc, hitInfo.Initiator);
                return;
            }

            if (ConfigData.Configuration.Horde.CreateOnDeath)
            {
                ZombieNPC initiator = ZombieNPC.Get(hitInfo.InitiatorPlayer);
                initiator?.Horde?.OnPlayerKilled(player);
            }
        }

        public void OnEntityKill(BaseNetworkable entity)
        {
            if (entity is ScientistNPC scientist)
            {
                ZombieNPC zombie = ZombieNPC.Get(scientist);
                if (zombie != null && zombie.Horde is { isDespawning: false })
                    zombie.Horde.OnMemberKilled(zombie, null);
            }

            if (ConfigData.Configuration == null || !ConfigData.Configuration.Horde.UseSenses) return;

            if (entity is TimedExplosive timedExplosive)
            {
                Sense.Stimulate(new Sensation { Type = SensationType.Gunshot, Position = timedExplosive.transform.position, Radius = 80f });
            }
            else if (entity is TreeEntity treeEntity)
            {
                Sense.Stimulate(new Sensation { Type = SensationType.Gunshot, Position = treeEntity.transform.position, Radius = 30f });
            }
            else if (entity is OreResourceEntity ore)
            {
                Sense.Stimulate(new Sensation { Type = SensationType.Gunshot, Position = ore.transform.position, Radius = 30f });
            }
        }

        public object CanBeTargeted(BaseCombatEntity entity, MonoBehaviour turret)
        {
            ZombieNPC zombie = ZombieNPC.Get(entity as BasePlayer);
            if (zombie == null) return null;

            if (turret is GunTrap or FlameTurret)
                return ConfigData.Configuration.Member.TargetedByTurrets ? null : (object)false;

            if (turret is AutoTurret autoTurret)
            {
                if (ConfigData.Configuration.Member.TargetedByTurrets && !(autoTurret is NPCAutoTurret))
                    return null;

                if ((ConfigData.Configuration.Member.TargetedByPeaceKeeperTurrets || ConfigData.Configuration.Member.TargetedByTurrets)
                    && autoTurret is NPCAutoTurret)
                {
                    if (autoTurret.target == null && zombie.Npc != null)
                        autoTurret.SetTarget(zombie.Npc);
                    return null;
                }

                if (autoTurret.PeacekeeperMode() && ConfigData.Configuration.Member.TargetedByPeaceKeeperTurrets)
                    return null;

                return false;
            }
            return null;
        }

        public object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity entity)
        {
            if (ZombieNPC.Get(entity as BasePlayer) == null) return null;
            return ConfigData.Configuration.Member.TargetedByAPC ? null : (object)false;
        }

        public object CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player)
        {
            if (ZombieNPC.Get(player) == null) return null;
            return false;
        }

        public object OnNpcTarget(BaseEntity attacker, BaseEntity target)
        {
            ZombieNPC zombie = ZombieNPC.Get(target as BasePlayer);
            if (zombie == null) return null;

            if (attacker is NPCPlayer)
                return ConfigData.Configuration.Member.TargetedByNPCs && !zombie.IsBrainSleeping ? null : (object)true;

            if (attacker is BaseNpc)
                return ConfigData.Configuration.Member.TargetedByAnimals && !zombie.IsBrainSleeping ? null : (object)true;

            return null;
        }

        public object OnEntityEnterSafeZone(TriggerSafeZone trigger, BaseEntity entity)
        {
            ZombieNPC zombie = ZombieNPC.Get(entity as BasePlayer);
            if (trigger == null || zombie == null || zombie.Npc == null) return null;
            trigger.contents?.Remove(zombie.Npc.gameObject);
            try { zombie.Npc.LeaveTrigger(trigger); } catch { }
            return true;
        }

        public void OnDispenserGather(ResourceDispenser dispenser)
        {
            if (!ConfigData.Configuration.Horde.UseSenses || dispenser == null) return;
            Sense.Stimulate(new Sensation
            {
                Type = SensationType.Gunshot,
                Position = dispenser.transform.position,
                Radius = 20f
            });
        }

        public object OnCorpsePopulate(NPCPlayerCorpse corpse, ScientistNPC scientist)
        {
            ZombieNPC zombie = ZombieNPC.Get(scientist);
            if (zombie == null || corpse == null) return null;

            zombie.PrepareCorpseLoot();

            if (WantsToPopulateLoot(zombie, corpse))
                return corpse;

            string[] alphaLootProfiles = zombie.Loadout?.DropAlphaLootOverride?.Length > 0
                ? zombie.Loadout.DropAlphaLootOverride
                : ConfigData.Configuration.Loot.DropAlphaLootProfiles;

            if (alphaLootProfiles != null && alphaLootProfiles.Length > 0 &&
                AlphaLootHarmonyIntegration.TryPopulateProfile(corpse.containers[0], alphaLootProfiles.GetRandom()))
                return corpse;

            return null;
        }

        private bool WantsToPopulateLoot(ZombieNPC zombieNpc, NPCPlayerCorpse npcplayerCorpse)
        {
            if (ConfigData.Configuration.Loot.DropDefault || ConfigData.Configuration.Loot.DropInventory)
                return false;

            ConfigData.LootTable.RandomLoot randomLoot = ConfigData.Configuration.Loot.Random;
            if (zombieNpc.Loadout?.LootOverride?.List?.Count > 0)
                randomLoot = zombieNpc.Loadout.LootOverride;

            if (randomLoot?.List == null || randomLoot.List.Count == 0)
                return false;

            int count = Random.Range(randomLoot.Minimum, randomLoot.Maximum);
            int spawnedCount = 0;
            int loopCount = 0;

            while (true)
            {
                loopCount++;
                if (loopCount > 3) break;

                float probability = Random.Range(0f, 1f);
                var definitions = new List<ConfigData.LootTable.RandomLoot.LootDefinition>(randomLoot.List);
                for (int i = 0; i < randomLoot.List.Count; i++)
                {
                    var lootDefinition = definitions.GetRandom();
                    definitions.Remove(lootDefinition);
                    if (lootDefinition.Probability >= probability)
                    {
                        lootDefinition.Create(npcplayerCorpse.containers[0]);
                        spawnedCount++;
                        if (spawnedCount >= count) return true;
                    }
                }
            }
            return true;
        }

        #endregion

        #region Commands

        public bool TryHandleChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return false;
            if (!message.StartsWith("/")) return false;

            string[] parts = message.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            string cmd = parts[0].ToLowerInvariant();
            string[] args = parts.Skip(1).ToArray();

            if (cmd == "hordeinfo")
            {
                CmdHordeInfo(player);
                return true;
            }
            if (cmd == "horde")
            {
                CmdHorde(player, args);
                return true;
            }
            return false;
        }

        private void OnConsoleHorde(ConsoleSystem.Arg arg)
        {
            BasePlayer player = Compat.GetPlayer(arg);
            string[] args = arg.HasArgs() ? arg.Args.Select(a => a.ToString()).ToArray() : Array.Empty<string>();
            if (player != null)
                CmdHorde(player, args);
            else
                CmdHordeConsole(arg, args);
        }

        private void OnConsoleHordeInfo(ConsoleSystem.Arg arg)
        {
            BasePlayer player = Compat.GetPlayer(arg);
            if (player != null) CmdHordeInfo(player);
            else
            {
                int members = Horde.AllHordes.Sum(h => h.MemberCount);
                arg.ReplyWith($"Active hordes: {Horde.AllHordes.Count}, zombies: {members}");
            }
        }

        private void CmdHordeInfo(BasePlayer player)
        {
            int memberCount = 0;
            int hordeNumber = 0;
            foreach (Horde horde in Horde.AllHordes)
            {
                player.SendConsoleCommand("ddraw.text", 30, Color.green, horde.CentralLocation + new Vector3(0, 1.5f, 0),
                    $"<size=20>Zombie Horde {hordeNumber}</size>");
                memberCount += horde.MemberCount;
                hordeNumber++;
            }
            player.ChatMessage($"There are {Horde.AllHordes.Count} active zombie hordes with a total of {memberCount} zombies");
        }

        private void SendReply(BasePlayer player, string msg) => player.ChatMessage(msg);

        private void CmdHorde(BasePlayer player, string[] args)
        {
            if (!Compat.Permission.UserHasPermission(player.UserIDString, ADMIN_PERMISSION))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, "/horde info - Show position and information about active zombie hordes");
                SendReply(player, "/horde tpto <number> - Teleport to the specified zombie horde");
                SendReply(player, "/horde destroy <number> - Destroy the specified zombie horde");
                SendReply(player, "/horde create <opt:distance> <opt:profile> - Create a new zombie horde");
                SendReply(player, "/horde createspawn <opt:membercount> <opt:distance> <opt:profile> - Save custom spawn");
                SendReply(player, "/horde createloadout - Copy inventory to a new loadout");
                SendReply(player, "/horde hordecount <number> - Set maximum hordes");
                SendReply(player, "/horde membercount <number> - Set maximum members per horde");
                return;
            }

            switch (args[0].ToLower())
            {
                case "info":
                    CmdHordeInfo(player);
                    return;
                case "destroy":
                {
                    if (args.Length != 2 || !int.TryParse(args[1], out int number) || number < 0 || number >= Horde.AllHordes.Count)
                    {
                        SendReply(player, "Invalid horde number");
                        return;
                    }
                    Horde.AllHordes[number].Destroy(true, true);
                    SendReply(player, $"Destroyed zombie horde {number}");
                    return;
                }
                case "tpto":
                {
                    if (args.Length != 2 || !int.TryParse(args[1], out int number) || number < 0 || number >= Horde.AllHordes.Count)
                    {
                        SendReply(player, "Invalid horde number");
                        return;
                    }
                    player.Teleport(Horde.AllHordes[number].CentralLocation);
                    SendReply(player, $"Teleported to zombie horde {number}");
                    return;
                }
                case "create":
                {
                    float distance = -1;
                    if (args.Length >= 2 && !float.TryParse(args[1], out distance))
                    {
                        SendReply(player, "Invalid Syntax!");
                        return;
                    }
                    string profile = args.Length >= 3 && ConfigData.Configuration.HordeProfiles.ContainsKey(args[2]) ? args[2] : string.Empty;
                    if (NavmeshSpawnPoint.Find(player.transform.position, 5f, out Vector3 position)
                        && Horde.Create(new Horde.SpawnOrder(position, ConfigData.Configuration.Horde.InitialMemberCount,
                            ConfigData.Configuration.Horde.MaximumMemberCount, distance, profile)))
                    {
                        SendReply(player, distance > 0 ? $"Created horde with roam {distance}" : "Created zombie horde");
                        return;
                    }
                    SendReply(player, "Unable to spawn horde at this position");
                    return;
                }
                case "createspawn":
                {
                    int members = ConfigData.Configuration.Horde.InitialMemberCount;
                    if (args.Length >= 2 && !int.TryParse(args[1], out members)) { SendReply(player, "Invalid Syntax!"); return; }
                    float distance = -1;
                    if (args.Length >= 3 && !float.TryParse(args[2], out distance)) { SendReply(player, "Invalid Syntax!"); return; }
                    string profile = args.Length >= 4 && ConfigData.Configuration.HordeProfiles.ContainsKey(args[3]) ? args[3] : string.Empty;
                    ConfigData.Configuration.Monument.Custom.Add(new ConfigData.MonumentSpawn.CustomSpawnPoints
                    {
                        Enabled = true,
                        HordeSize = members,
                        Location = player.transform.position,
                        Profile = profile,
                        RoamDistance = distance
                    });
                    SaveConfig();
                    if (NavmeshSpawnPoint.Find(player.transform.position, 5f, out Vector3 position)
                        && Horde.Create(new Horde.SpawnOrder(position, ConfigData.Configuration.Horde.InitialMemberCount,
                            ConfigData.Configuration.Horde.MaximumMemberCount, distance, profile)))
                        SendReply(player, "Created custom horde spawn point");
                    else
                        SendReply(player, "Saved spawn but unable to spawn horde here");
                    return;
                }
                case "createloadout":
                {
                    var loadout = new ConfigData.MemberOptions.Loadout($"loadout-{ConfigData.Configuration.Member.Loadouts.Count}");
                    CopyContainer(player.inventory.containerBelt, loadout.BeltItems);
                    CopyContainer(player.inventory.containerMain, loadout.MainItems);
                    CopyContainer(player.inventory.containerWear, loadout.WearItems);
                    ConfigData.Configuration.Member.Loadouts.Add(loadout);
                    SaveConfig();
                    SendReply(player, $"Created loadout {loadout.LoadoutID}");
                    return;
                }
                case "hordecount":
                {
                    if (args.Length != 2 || !int.TryParse(args[1], out int n)) { SendReply(player, "Specify a number"); return; }
                    ConfigData.Configuration.Horde.MaximumHordes = n;
                    SaveConfig();
                    SendReply(player, $"Maximum hordes set to {n}");
                    return;
                }
                case "membercount":
                {
                    if (args.Length != 2 || !int.TryParse(args[1], out int n)) { SendReply(player, "Specify a number"); return; }
                    ConfigData.Configuration.Horde.MaximumMemberCount = n;
                    SaveConfig();
                    SendReply(player, $"Maximum members set to {n}");
                    return;
                }
            }
        }

        private static void CopyContainer(ItemContainer container, List<ConfigData.LootTable.InventoryItem> list)
        {
            if (container?.itemList == null) return;
            foreach (Item item in container.itemList)
            {
                if (item == null || item.amount == 0) continue;
                list.Add(new ConfigData.LootTable.InventoryItem
                {
                    Amount = item.amount,
                    Shortname = item.info.shortname,
                    SkinID = item.skin
                });
            }
        }

        private void CmdHordeConsole(ConsoleSystem.Arg arg, string[] args)
        {
            if (args.Length == 0)
            {
                arg.ReplyWith("horde info|create|destroy|hordecount|membercount");
                return;
            }
            // Minimal server console support
            if (args[0].Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                arg.ReplyWith($"Hordes={Horde.AllHordes.Count} Members={Horde.AllHordes.Sum(h => h.MemberCount)}");
            }
        }

        #endregion

        private static class AlphaLootHarmonyIntegration
        {
            private static bool _resolved;
            private static bool _available;
            private static Type _modType;
            private static PropertyInfo _instanceProperty;
            private static MethodInfo _tryGetNpcProfileMethod;
            private static readonly Type[] PopulateLootParameterTypes = { typeof(ItemContainer), typeof(string) };

            internal static void Warmup() => EnsureResolved();

            internal static bool TryPopulateProfile(ItemContainer container, string profileName)
            {
                if (container == null || string.IsNullOrEmpty(profileName)) return false;
                EnsureResolved();
                if (!_available) return false;
                object instance = _instanceProperty?.GetValue(null);
                if (instance == null) return false;
                object[] profileArgs = { profileName, null };
                if (_tryGetNpcProfileMethod == null || !(bool)_tryGetNpcProfileMethod.Invoke(instance, profileArgs))
                    return false;
                object profile = profileArgs[1];
                PropertyInfo enabledProperty = profile?.GetType().GetProperty("Enabled");
                if (enabledProperty != null && !(bool)enabledProperty.GetValue(profile)) return false;
                container.Clear();
                profile.GetType().GetMethod("PopulateLoot", PopulateLootParameterTypes)
                    ?.Invoke(profile, new object[] { container, profileName });
                return true;
            }

            private static void EnsureResolved()
            {
                if (_resolved) return;
                _resolved = true;
                try
                {
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!string.Equals(assembly.GetName().Name, "AlphaLoot", StringComparison.OrdinalIgnoreCase)
                            && !assembly.GetName().Name.StartsWith("AlphaLoot_", StringComparison.OrdinalIgnoreCase))
                            continue;
                        _modType = assembly.GetType("AlphaLoot.Harmony.AlphaLootMod");
                        if (_modType == null) continue;
                        _instanceProperty = _modType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        _tryGetNpcProfileMethod = _modType.GetMethod("TryGetNPCProfile");
                        _available = _instanceProperty != null && _tryGetNpcProfileMethod != null;
                        return;
                    }
                }
                catch { _available = false; }
            }
        }
    }
}
