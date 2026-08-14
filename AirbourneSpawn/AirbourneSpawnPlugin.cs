using System;
using System.Collections.Generic;
using System.IO;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ProtoBuf;
using Rust;
using UnityEngine;

namespace AirbourneSpawnHarmony
{
    public class AirbourneSpawnPlugin
    {
        public const string UsePermission = "airbournespawn.use";
        public const string IgnoreCooldownPermission = "airbournespawn.ignorecooldown";

        private const string CH47_PREFAB = "assets/prefabs/npc/ch47/ch47.entity.prefab";
        private const string CARGO_PLANE_PREFAB = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
        private const string F15_PREFAB = "assets/scripts/entity/misc/f15/f15e.prefab";

        private const string UI_ROOT = "airbournespawn.ui.root";
        private const string UI_BTN_BEACH = "airbournespawn.ui.beach";

        private AirbourneEntity m_AirborneEntity;
        private readonly TimerLib timer = new TimerLib();

        private static readonly HashSet<BasePlayer> m_RespawningPlayers = new HashSet<BasePlayer>();
        private static readonly HashSet<ulong> m_PlaneJumpParachutePlayers = new HashSet<ulong>();
        private static readonly Dictionary<ulong, Parachute> m_PlaneJumpParachutes = new Dictionary<ulong, Parachute>();
        private static readonly Dictionary<ulong, Timer> m_ParachutePhysicsTimers = new Dictionary<ulong, Timer>();
        private static readonly Dictionary<ulong, Timer> m_FlyhackProtectors = new Dictionary<ulong, Timer>();
        private static readonly HashSet<ulong> m_BeachIntent = new HashSet<ulong>();

        private static AirbourneSpawnPlugin s_Instance;

        public static ConfigData Configuration { get; private set; }

        public AirbourneEntity AirborneEntity => m_AirborneEntity;

        public void HarmonyInit()
        {
            s_Instance = this;
            LoadConfig();
            RegisterLangMessages();
            AirbourneSpawnHost.Instance?.ReloadLanguage();
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(UsePermission);
            PermissionsBridge.RegisterPermission(IgnoreCooldownPermission);
        }

        public void HarmonyServerInitialized()
        {
            Configuration.Spawn.PrepareAutoKits();

            m_AirborneEntity = Configuration.Flight.Mode switch
            {
                ConfigData.FlightOptions.FlightMode.CargoPlane => AirbourneEntity.Spawn<AirbourneCargoPlane>(CARGO_PLANE_PREFAB),
                ConfigData.FlightOptions.FlightMode.CH47 => AirbourneEntity.Spawn<AirbourneCH47>(CH47_PREFAB),
                ConfigData.FlightOptions.FlightMode.F15 => AirbourneEntity.Spawn<AirbourneF15>(F15_PREFAB),
                _ => AirbourneEntity.Spawn<AirbourneCargoPlane>(CARGO_PLANE_PREFAB)
            };

            Debug.Log("[AirbourneSpawn] OK: Spawned " + (m_AirborneEntity != null ? m_AirborneEntity.LocalizedName : "null") + " flight entity.");
        }

        public void HarmonyUnload()
        {
            s_Instance = null;
            foreach (BasePlayer p in BasePlayer.activePlayerList)
                DestroyBeachUi(p);

            timer.DestroyAll();
            m_ParachutePhysicsTimers.Clear();
            m_PlaneJumpParachutePlayers.Clear();
            m_PlaneJumpParachutes.Clear();
            m_FlyhackProtectors.Clear();
            m_BeachIntent.Clear();
            m_RespawningPlayers.Clear();

            if (m_AirborneEntity)
            {
                m_AirborneEntity.DismountAllPlayers(true);
                UnityEngine.Object.Destroy(m_AirborneEntity);
            }

            Configuration = null;
        }

        public static void MarkRespawning(BasePlayer player)
        {
            if (player) m_RespawningPlayers.Add(player);
        }

        public static void UnmarkRespawning(BasePlayer player)
        {
            if (player) m_RespawningPlayers.Remove(player);
        }

        public static bool ShouldSkipKitsAutoKit(BasePlayer player)
        {
            if (player == null || Configuration == null) return false;
            return m_RespawningPlayers.Contains(player) && Configuration.Spawn.AutoKits.Count > 0;
        }

        public static void ScheduleAutoKit(BasePlayer player)
        {
            s_Instance?.timer.Once(0.15f, () => s_Instance?.GiveAutoKit(player));
        }

        private void GiveAutoKit(BasePlayer player)
        {
            if (!player || !m_RespawningPlayers.Contains(player))
                return;

            m_RespawningPlayers.Remove(player);

            if (Configuration.Spawn.AutoKits.Count == 0)
                return;

            if (!KitsBridge.IsLoaded)
            {
                Debug.LogWarning("[AirbourneSpawn] Kits is not loaded — spawn kit skipped");
                return;
            }

            string kit = Configuration.Spawn.AutoKits[UnityEngine.Random.Range(0, Configuration.Spawn.AutoKits.Count)];
            if (!KitsBridge.IsKit(kit))
            {
                Debug.LogWarning("[AirbourneSpawn] The kit '" + kit + "' does not exist");
                return;
            }

            player.inventory.Strip();
            KitsBridge.GiveKit(player, kit);
        }

        public bool TryHandleKill(BasePlayer player)
        {
            if (!player || !player.IsConnected || !m_AirborneEntity)
                return false;
            if (player.IsSpectating() || player.IsDead())
                return false;
            if (!player.CanSuicide())
                return false;

            BaseMountable baseMountable = player.GetMounted();
            if (!baseMountable)
                return false;

            AirbourneEntity airbourneEntity = baseMountable.GetParentEntity() as AirbourneEntity;
            if (!airbourneEntity)
                return false;

            airbourneEntity.DismountPlayer(player, false);
            player.MarkSuicide();
            player.Hurt(1000f, DamageType.Suicide, player, false);
            return true;
        }

        public bool TryHandleRespawn(BasePlayer player)
        {
            if (!player || !player.IsConnected || !m_AirborneEntity)
                return false;
            if (!player.IsDead() && !player.IsSpectating())
                return false;

            if (m_BeachIntent.Contains(player.GetUserId()))
            {
                m_BeachIntent.Remove(player.GetUserId());
                return false;
            }

            if (!Configuration.Spawn.ForceRandomRespawns)
                return false;

            m_AirborneEntity.MountPlayer(player);
            return true;
        }

        public bool TryHandleRespawnBag(BasePlayer player, NetworkableId id)
        {
            if (!player || !m_AirborneEntity)
                return false;
            if (!player.IsDead() && !player.IsSpectating())
                return false;
            if (id != m_AirborneEntity.GetNetworkableId())
                return false;

            m_AirborneEntity.MountPlayer(player);
            return true;
        }

        public bool TryBlockRemoveBag(BasePlayer player, NetworkableId id)
        {
            if (!player || !m_AirborneEntity)
                return false;
            if (!player.IsDead() && !player.IsSpectating())
                return false;
            return id == m_AirborneEntity.GetNetworkableId();
        }

        public bool TryBlockMountedCommand(BasePlayer player)
        {
            if (!player)
                return false;

            BaseMountable baseMountable = player.GetMounted();
            if (!baseMountable)
                return false;

            AirbourneEntity airbourneEntity = baseMountable.GetParentEntity() as AirbourneEntity;
            if (!airbourneEntity)
                return false;

            player.ShowToast(GameTip.Styles.Blue_Long, GetLocalizedString(player, "Notification.CommandBlocked"));
            return true;
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (!player) return;
            ulong id = player.GetUserId();
            m_BeachIntent.Remove(id);
            m_PlaneJumpParachutePlayers.Remove(id);
            m_PlaneJumpParachutes.Remove(id);
            DestroyParachutePhysicsTimer(id);
            StopFlyhackProtection(id);
            DestroyBeachUi(player);

            BaseMountable baseMountable = player.GetMounted();
            if (!baseMountable)
                return;

            AirbourneEntity airbourneEntity = baseMountable.GetParentEntity() as AirbourneEntity;
            if (!airbourneEntity)
                return;

            airbourneEntity.DismountPlayer(player, true);
            player.Hurt(new HitInfo(airbourneEntity, player, DamageType.Fall, 1000f));
        }

        public void OnPlayerDeath(BasePlayer player)
        {
            if (!player || !player.IsConnected)
                return;
            if (!player.HasPermission(UsePermission))
                return;
            if (!m_AirborneEntity)
                return;
            timer.Once(0.2f, () => ShowBeachButton(player));
            timer.Once(0.8f, () => ShowBeachButton(player));
        }

        public void OnPlayerRespawned(BasePlayer player)
        {
            if (!player) return;
            m_BeachIntent.Remove(player.GetUserId());
            DestroyBeachUi(player);
        }

        public void OnEntityMounted(BaseMountable entity, BasePlayer player)
        {
            if (entity == null || player == null || entity.IsDestroyed)
                return;
            Parachute chute = entity.GetComponentInParent<Parachute>();
            if (chute == null)
                return;
            if (!ContainsParachute(chute))
                return;

            m_PlaneJumpParachutePlayers.Remove(player.GetUserId());
            StartFlyhackProtection(player, 35f);

            if (!Configuration.Parachute.UseCustomDescent)
                return;

            chute.SetToNonKinematic();
            Rigidbody rb = chute.rigidBody;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearDamping = 0.5f;
                rb.angularDamping = 0.5f;
                rb.mass = 0.5f;
            }

            ApplyParachuteConfig(chute);

            float descentForce = Configuration.Parachute.DescentForce > 0f ? Configuration.Parachute.DescentForce : 500f;
            float forwardForce = Configuration.Parachute.ForwardForce > 0f ? Configuration.Parachute.ForwardForce : 2500f;

            Timer physicsTimer = timer.Repeat(0.02f, 3000, () =>
            {
                if (player == null || !player.IsConnected || !player.isMounted || chute == null || chute.IsDestroyed)
                {
                    DestroyParachutePhysicsTimer(player != null ? player.GetUserId() : 0UL);
                    return;
                }
                if (player.IsOnGround())
                {
                    DestroyParachutePhysicsTimer(player.GetUserId());
                    return;
                }
                if (rb != null && !rb.isKinematic)
                {
                    rb.AddForce(Vector3.down * descentForce, ForceMode.Force);
                    if (player.serverInput.IsDown(BUTTON.FORWARD))
                        rb.AddForce(chute.transform.forward * forwardForce, ForceMode.Force);
                }
                else if (rb != null)
                    rb.isKinematic = false;
            });
            m_ParachutePhysicsTimers[player.GetUserId()] = physicsTimer;
        }

        public void OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            if (player == null || entity == null || entity.IsDestroyed)
                return;
            Parachute chute = entity.GetComponentInParent<Parachute>();
            if (chute != null)
                m_PlaneJumpParachutes.Remove(player.GetUserId());
            DestroyParachutePhysicsTimer(player.GetUserId());
        }

        public void OnItemRemove(Item item)
        {
            if (!Configuration.Parachute.DestroyOnLand)
                return;
            if (!AirbourneEntity.ParachuteItems.Contains(item))
                return;

            BasePlayer player = item.GetOwnerPlayer();
            if (player)
            {
                Parachute parachute = player.GetMountedVehicle() as Parachute;
                if (parachute)
                    parachute.ConditionLossPerUse = 1f;
            }

            AirbourneEntity.ParachuteItems.Remove(item);
        }

        public void OnRespawnInformationGiven(BasePlayer player, List<RespawnInformation.SpawnOptions> list)
        {
            if (!player || player.IsNpc || !player.IsConnected)
                return;
            if (Configuration.Spawn.ForceRandomRespawns)
                return;
            if (!m_AirborneEntity)
                return;
            if (!player.HasPermission(UsePermission))
                return;

            int cooldown = m_AirborneEntity.GetPlayerCooldown(player);
            RespawnInformation.SpawnOptions d = Pool.Get<RespawnInformation.SpawnOptions>();
            d.id = m_AirborneEntity.GetNetworkableId();
            d.name = GetLocalizedString(player, "Name." + m_AirborneEntity.LocalizedName);
            d.worldPosition = new Vector3(TerrainMeta.Size.x + 1000, 0, TerrainMeta.Size.x + 1000);
            d.type = RespawnInformation.SpawnOptions.RespawnType.Static;
            d.respawnState = RespawnInformation.SpawnOptions.RespawnState.OK;
            d.unlockSeconds = cooldown != 0 ? Mathf.Clamp((cooldown - AirbourneEntity.EpochNow()), 0, int.MaxValue) : 0;
            d.mobile = false;
            list.Add(d);
        }

        public bool TryBlockViolation(BasePlayer player, AntiHackType type)
        {
            if (player == null)
                return false;
            if (type != AntiHackType.FlyHack && type != AntiHackType.SpeedHack)
                return false;
            ulong id = player.GetUserId();
            if (!m_FlyhackProtectors.ContainsKey(id) && !m_PlaneJumpParachutes.ContainsKey(id))
                return false;
            ResetAntiHackCompat(player);
            return true;
        }

        internal static void ScheduleDeployParachute(BasePlayer player)
        {
            if (s_Instance == null || player == null)
                return;
            s_Instance.timer.Once(0.2f, () => s_Instance.DeployParachuteFromSlot(player));
        }

        internal static void StartFlyhackProtectionForJump(BasePlayer player)
        {
            s_Instance?.StartFlyhackProtection(player, 35f);
        }

        internal static void ResetAntiHackCompat(BasePlayer player)
        {
            if (player == null || player.ActivePlayerInd < 0)
                return;

            try
            {
                int ind = player.ActivePlayerInd;
                if (AntiHack.PlayerStates.IsCreated)
                    AntiHack.PlayerStates[ind] = default;
                if (AntiHack.PlayerNoclipStates.IsCreated)
                    AntiHack.PlayerNoclipStates[ind] = default;
                if (AntiHack.PlayerSpeedhackStates.IsCreated)
                    AntiHack.PlayerSpeedhackStates[ind] = default;
                if (AntiHack.PlayerFlyhackStates.IsCreated)
                    AntiHack.PlayerFlyhackStates[ind] = default;
                player.rpcHistory?.Clear();
            }
            catch { }
        }

        private void DeployParachuteFromSlot(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            player.SetServerFall(true);

            Item slot = player.inventory.containerWear.GetSlot(7);
            if (slot == null || !(slot.conditionNormalized > 0f) || slot.isBroken || !slot.info.TryGetComponent<ItemModParachute>(out ItemModParachute component))
                return;

            Parachute parachute = GameManager.server.CreateEntity(component.ParachuteVehiclePrefab.resourcePath, player.transform.position, player.eyes.rotation) as Parachute;
            if (parachute == null)
                return;

            parachute.enableSaving = false;
            parachute.skinID = slot.skin;
            parachute.Spawn();
            parachute.SetHealth(parachute.MaxHealth() * slot.conditionNormalized);

            parachute.SetToNonKinematic();
            Rigidbody rb = parachute.rigidBody;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearDamping = 0.5f;
                rb.angularDamping = 0.5f;
                rb.mass = 0.5f;
            }

            ApplyParachuteConfig(parachute);

            m_PlaneJumpParachutes[player.GetUserId()] = parachute;
            parachute.AttemptMount(player);

            if (player.isMounted)
            {
                slot.Remove();
                ItemManager.DoRemoves();
                player.SendNetworkUpdate();
            }
            else
            {
                m_PlaneJumpParachutes.Remove(player.GetUserId());
                parachute.Kill();
            }
        }

        private void ApplyParachuteConfig(Parachute chute)
        {
            if (chute == null) return;
            var cfg = Configuration.Parachute;
            float targetDrag = cfg.TargetDrag > 0f ? cfg.TargetDrag : 0.2f;
            float targetAngularDrag = cfg.TargetAngularDrag > 0f ? cfg.TargetAngularDrag : 0.2f;
            chute.TargetDrag = targetDrag;
            chute.TargetAngularDrag = targetAngularDrag;
            if (cfg.ConstantForwardForce > 0f)
                chute.ConstantForwardForce = cfg.ConstantForwardForce;
            if (cfg.TurnForce > 0f)
                chute.TurnForce = cfg.TurnForce;
            if (cfg.ForwardTiltAcceleration > 0f)
                chute.ForwardTiltAcceleration = cfg.ForwardTiltAcceleration;
            if (cfg.DeployAnimationLength >= 0f)
                chute.DeployAnimationLength = cfg.DeployAnimationLength;
            if (cfg.UprightLerpForce > 0f)
                chute.UprightLerpForce = cfg.UprightLerpForce;
            if (cfg.UseCustomDescent)
            {
                chute.DragCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
                float maxH = cfg.MaxHorizontalVelocity > 0f ? cfg.MaxHorizontalVelocity : 40f;
                chute.DamageHorizontalVelocityCurve = new AnimationCurve(new Keyframe(0f, 5f), new Keyframe(1f, maxH));
            }
        }

        private static void DestroyParachutePhysicsTimer(ulong userID)
        {
            if (m_ParachutePhysicsTimers.TryGetValue(userID, out Timer t))
            {
                t?.Destroy();
                m_ParachutePhysicsTimers.Remove(userID);
            }
        }

        private void StartFlyhackProtection(BasePlayer player, float seconds = 35f)
        {
            if (player == null)
                return;
            ulong id = player.GetUserId();
            if (m_FlyhackProtectors.TryGetValue(id, out Timer existing))
                existing?.Destroy();
            int repeats = Mathf.CeilToInt(seconds);
            Timer protectTimer = timer.Repeat(1f, repeats, () =>
            {
                if (player == null || !player.IsConnected)
                {
                    m_FlyhackProtectors.Remove(player != null ? player.GetUserId() : 0UL);
                    return;
                }
                if (player.IsOnGround() && !player.isMounted)
                {
                    StopFlyhackProtection(player.GetUserId());
                    return;
                }
                player.PauseFlyHackDetection(2f);
                player.PauseSpeedHackDetection(2f);
            });
            m_FlyhackProtectors[id] = protectTimer;
        }

        private void StopFlyhackProtection(ulong userID)
        {
            if (m_FlyhackProtectors.TryGetValue(userID, out Timer t))
            {
                t?.Destroy();
                m_FlyhackProtectors.Remove(userID);
            }
        }

        private bool ContainsParachute(Parachute chute)
        {
            foreach (var kv in m_PlaneJumpParachutes)
            {
                if (kv.Value == chute)
                    return true;
            }
            return false;
        }

        private void ShowBeachButton(BasePlayer player)
        {
            if (!player || !player.IsConnected)
                return;
            DestroyBeachUi(player);

            string json = "[" +
                "{\"name\":\"" + UI_ROOT + "\",\"parent\":\"Overlay\",\"destroyUi\":\"" + UI_ROOT + "\",\"components\":[" +
                    "{\"type\":\"UnityEngine.UI.Image\",\"color\":\"0 0 0 0\"}," +
                    "{\"type\":\"RectTransform\",\"anchormin\":\"0.84 0.11\",\"anchormax\":\"0.96 0.17\"}" +
                "]}," +
                "{\"name\":\"" + UI_BTN_BEACH + "\",\"parent\":\"" + UI_ROOT + "\",\"components\":[" +
                    "{\"type\":\"UnityEngine.UI.Button\",\"command\":\"cui.endtest AIRBOURNESPAWN beach\",\"color\":\"0.4 0.0 0.8 0.95\"}," +
                    "{\"type\":\"UnityEngine.UI.Text\",\"text\":\"<size=20><color=#9B30FF>SPAWN ON BEACH</color></size>\\n<size=14><color=#E6E6FA>Spawn on the ground now</color></size>\",\"align\":\"MiddleCenter\",\"color\":\"1 1 1 1\"}," +
                    "{\"type\":\"RectTransform\",\"anchormin\":\"0 0\",\"anchormax\":\"1 1\"}" +
                "]}" +
            "]";
            CuiUtil.AddUi(player, json);
        }

        private void DestroyBeachUi(BasePlayer player)
        {
            if (player == null) return;
            CuiUtil.DestroyUi(player, UI_BTN_BEACH);
            CuiUtil.DestroyUi(player, UI_ROOT);
        }

        public void CmdBeach(BasePlayer player)
        {
            if (!player || !player.IsConnected)
                return;
            if (!player.HasPermission(UsePermission))
                return;
            m_BeachIntent.Add(player.GetUserId());
            DestroyBeachUi(player);
            if (player.net?.connection != null)
                ConsoleNetwork.SendClientCommand(player.net.connection, "global.respawn");
            else
                player.Respawn();
        }

        public static string GetLocalizedString(BasePlayer player, string key)
        {
            string userId = player != null ? player.UserIDString : null;
            return AirbourneSpawnHost.Instance?.Lang.GetMessage(key, userId) ?? key;
        }

        private void RegisterLangMessages()
        {
            AirbourneSpawnHost.Instance?.Lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Name.CargoPlane"] = "Cargo Plane",
                ["Name.CH47"] = "CH47 Helicopter",
                ["Name.F15"] = "F15",
                ["Jump.Allowed"] = "Press jump when ready to leave the plane",
                ["Jump.Blocked"] = "You must wait until you are closer to the island to jump",
                ["Notification.JumpZone"] = "You must wait until you are closer to the island",
                ["Notification.CommandBlocked"] = "You can not use any commands whilst mounted",
            });
        }

        private void LoadConfig()
        {
            string path = AirbourneSpawnHost.Instance?.ConfigPath;
            if (string.IsNullOrEmpty(path))
            {
                Configuration = GenerateDefaultConfiguration();
                return;
            }

            try
            {
                if (!File.Exists(path))
                {
                    string oxidePath = Path.Combine(AirbourneSpawnHost.Instance.ServerRoot, "oxide", "config", "AirbourneSpawn.json");
                    if (File.Exists(oxidePath))
                    {
                        File.Copy(oxidePath, path);
                        Debug.Log("[AirbourneSpawn] Migrated oxide/config/AirbourneSpawn.json -> HarmonyConfig/AirbourneSpawn.json");
                    }
                    else
                    {
                        Configuration = GenerateDefaultConfiguration();
                        File.WriteAllText(path, JsonConvert.SerializeObject(Configuration, Formatting.Indented));
                        Debug.Log("[AirbourneSpawn] Wrote default config: " + path);
                        return;
                    }
                }

                Configuration = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
                if (Configuration == null)
                    Configuration = GenerateDefaultConfiguration();
                else
                    MergeMissingParachuteDefaults(Configuration);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] Config load failed, using defaults: " + ex.Message);
                Configuration = GenerateDefaultConfiguration();
            }
        }

        private static void MergeMissingParachuteDefaults(ConfigData config)
        {
            if (config.Flight == null) config.Flight = GenerateDefaultConfiguration().Flight;
            if (config.Spawn == null) config.Spawn = GenerateDefaultConfiguration().Spawn;
            if (config.Parachute == null) config.Parachute = GenerateDefaultConfiguration().Parachute;
        }

        public static ConfigData GenerateDefaultConfiguration()
        {
            return new ConfigData
            {
                Flight = new ConfigData.FlightOptions
                {
                    Altitude = 450,
                    Speed = 40f,
                    Mode = ConfigData.FlightOptions.FlightMode.CargoPlane
                },
                Spawn = new ConfigData.SpawnOptions
                {
                    ForceRandomRespawns = false,
                    Cooldown = 60,
                    AutoKit = string.Empty
                },
                Parachute = new ConfigData.ParachuteOptions
                {
                    Condition = 0.5f,
                    DestroyOnLand = true,
                    UseCustomDescent = true,
                    DescentForce = 500f,
                    ForwardForce = 2500f,
                    TargetDrag = 0.45f,
                    TargetAngularDrag = 0.2f,
                    ConstantForwardForce = 20f,
                    MaxHorizontalVelocity = 65f,
                    TurnForce = 12f,
                    ForwardTiltAcceleration = 18f,
                    DeployAnimationLength = 0f,
                    UprightLerpForce = 18f
                }
            };
        }

        public class ConfigData
        {
            [JsonProperty("Flight Options")]
            public FlightOptions Flight { get; set; }

            [JsonProperty("Spawn Options")]
            public SpawnOptions Spawn { get; set; }

            [JsonProperty("Parachute Options")]
            public ParachuteOptions Parachute { get; set; }

            public class FlightOptions
            {
                [JsonProperty("Altitude (150 - 450)")]
                public float Altitude { get; set; }

                [JsonProperty("Speed")]
                public float Speed { get; set; }

                [JsonProperty("Mode (See plugin overview for options)")]
                public FlightMode Mode { get; set; }

                [JsonConverter(typeof(StringEnumConverter))]
                public enum FlightMode { CH47, CargoPlane, F15 }
            }

            public class SpawnOptions
            {
                [JsonProperty("Force random respawns to be on the plane")]
                public bool ForceRandomRespawns { get; set; }

                [JsonProperty("Spawn cooldown (seconds)")]
                public int Cooldown { get; set; }

                [JsonProperty("Give kit on respawn (kit name, leave blank for none)")]
                public string AutoKit { get; set; } = string.Empty;

                [JsonIgnore]
                public List<string> AutoKits { get; set; } = new List<string>();

                public void PrepareAutoKits()
                {
                    AutoKits.Clear();
                    if (string.IsNullOrEmpty(AutoKit))
                        return;

                    string[] kits = AutoKit.Split(',');
                    for (int i = 0; i < kits.Length; i++)
                    {
                        string trimmedKit = kits[i].Trim();
                        if (string.IsNullOrEmpty(trimmedKit))
                            continue;
                        if (!AutoKits.Contains(trimmedKit))
                            AutoKits.Add(trimmedKit);
                    }
                }
            }

            public class ParachuteOptions
            {
                [JsonProperty("Parachute condition (0.0 - 1.0)")]
                public float Condition { get; set; }

                [JsonProperty("Destroy parachute after use")]
                public bool DestroyOnLand { get; set; }

                [JsonProperty("Use custom descent speed (faster fall after jump from plane)")]
                public bool UseCustomDescent { get; set; }

                [JsonProperty("Descent force (downward force when custom descent enabled)")]
                public float DescentForce { get; set; }

                [JsonProperty("Forward force when holding W (custom descent)")]
                public float ForwardForce { get; set; }

                [JsonProperty("Target drag (game uses this every frame; lower = faster fall, default 1)")]
                public float TargetDrag { get; set; }

                [JsonProperty("Target angular drag (lower = faster rotation)")]
                public float TargetAngularDrag { get; set; }

                [JsonProperty("Constant forward force (game default 2; higher = more forward pull, 0 = use game default)")]
                public float ConstantForwardForce { get; set; }

                [JsonProperty("Max horizontal velocity (game clamps to this; default ~20, higher = more glide distance)")]
                public float MaxHorizontalVelocity { get; set; }

                [JsonProperty("Turn force (game default 2; higher = snappier left/right steering)")]
                public float TurnForce { get; set; }

                [JsonProperty("Forward tilt acceleration (game default 2; higher = more forward when holding W)")]
                public float ForwardTiltAcceleration { get; set; }

                [JsonProperty("Seconds before steering is enabled (game default 3; 0 = steer immediately)")]
                public float DeployAnimationLength { get; set; }

                [JsonProperty("Upright lerp force (game default 5; higher = levels out faster when you release A/D)")]
                public float UprightLerpForce { get; set; }
            }
        }
    }
}
