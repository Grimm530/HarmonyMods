using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins.JetPackExtensionMethods;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("JetPack", "Adem", "1.3.7")]
    public partial class JetPack : RustPlugin
    {
        #region Variables
        private const bool En = true;
        private static JetPack _ins;
        [PluginReference] private Plugin ZoneManager, Space;
        private readonly HashSet<string> _subscribeMethods = new HashSet<string>
        {
            "OnItemAddedToContainer",
            "OnItemRemovedFromContainer",
            "CanMoveItem",
            "OnPlayerDeath",
            "OnPlayerKicked",
            "OnLootSpawn",
            "CanLootEntity",
            "OnSamSiteTargetScan",
            "OnPlayerCommand",
            "OnEntitySpawned",
            "CanExplosiveStick",
            
            "OnEntityEnterZone",
            "OnSpaceEventStart",
            "OnSpaceEventStop"
          };
        
        private readonly HashSet<JetpackComponent> _jetpacks = new HashSet<JetpackComponent>();
        private readonly Dictionary<ulong, float> _unequipTime = new Dictionary<ulong, float>();
        private readonly HashSet<SupplyDrop> _supplyDrops = new HashSet<SupplyDrop>();
        
        private bool _isSpaceEventActive;
        private float _spaceHeight;
        #endregion Variables

        #region Hooks
        private void Init()
        {
            Unsubscribes();
        }

        private void OnServerInitialized()
        {
            _ins = this;
            LoadDefaultMessages();
            UpdateConfig();
            Subscribes();
            RegisterPermissions();

            if (plugins.Exists("Space") && (bool)Space.Call("IsEventActive"))
                OnSpaceEventStart();
        }

        private void Unload()
        {
            JetpackComponent.RemoveAllJetpacks();
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null)
                return;

            if (LootManager.IsJetpackItem(item))
            {
                BasePlayer player = container.playerOwner;

                if (player.IsRealPlayer() && container == player.inventory.containerWear)
                {
                    if (_config.WearWhenItemAdded)
                        JetpackComponent.TryAttachJetpackToPlayer(player);
                    else
                        NotifyManager.SendMessageToPlayer(player, "ActivateCommand", _config.Prefix);
                }
            }
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null)
                return;

            if (LootManager.IsJetpackItem(item))
            {
                BasePlayer player = container.playerOwner;

                if (player.IsRealPlayer() && container == player.inventory.containerWear)
                    JetpackComponent.RemoveJetpackFromPlayer(player.userID);
            }
        }

        private object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainer, int targetSlot, int amount)
        {
            if (item == null || playerLoot == null)
                return null;

            if (!_config.IsAirTakeOffAllowed)
            {
                BasePlayer player = playerLoot.baseEntity;

                if (LootManager.IsJetpackItem(item) && player.IsRealPlayer())
                {
                    JetpackComponent jetPack = JetpackComponent.GetJetpackComponentByUserId(player.userID);

                    if (jetPack != null && !jetPack.IsPlayerCanTakeOffJetpack())
                        return true;
                }
            }
            return null;
        }

        private void OnPlayerDeath(BasePlayer player)
        {
            if (player == null)
                return;

            JetpackComponent.RemoveJetpackFromPlayer(player.userID);
        }

        private void OnPlayerKicked(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            JetpackComponent.RemoveJetpackFromPlayer(player.userID);
        }

        private void OnLootSpawn(LootContainer container)
        {
            if (container == null)
                return;

            if (_config.IsSpawnInLootEnabled)
            {
                CrateConfig config = _config.Crates.FirstOrDefault(x => x.Prefab == container.PrefabName);

                if (config == null)
                    return;

                LootManager.TrySpawnItemInDefaultCrate(container, _config.ItemConfig, config.Chance);
            }
        }

        private object CanLootEntity(BasePlayer player, SupplyDrop container)
        {
            if (player == null)
                return null;

            if (!_config.IsAllowedLootAirdrop)
            {
                JetpackComponent jetPack = JetpackComponent.GetJetpackComponentByUserId(player.userID);

                if (jetPack != null)
                    return true;
            }

            return null;
        }

        private void OnSamSiteTargetScan(SamSite samSite, List<SamSite.ISamSiteTarget> targetList)
        {
            if (samSite == null || targetList == null)
                return;

            if (samSite.IsInDefenderMode())
                return;

            if (_config.IsPlayerSamAttack || _config.IsMonumentSamAttack)
                JetpackComponent.AddAllJetpacksToSamTargetList(samSite, targetList);
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return null;

            if (_config.BlockedCommands.Any(x => x.ToLower() == command.ToLower()))
            {
                JetpackComponent jetPackComponent = JetpackComponent.GetJetpackComponentByUserId(player.userID);

                if (jetPackComponent != null)
                {
                    NotifyManager.SendMessageToPlayer(player, "CommandBlock", _ins._config.Prefix);
                    return true;
                }
            }

            return null;
        }

        private void OnEntitySpawned(SupplyDrop supplyDrop)
        {
            if (supplyDrop == null)
                return;

            LootManager.OnSupplyDropSpawned(supplyDrop);
        }
        
        private object CanExplosiveStick(RFTimedExplosive rfTimedExplosive, BaseEntity entity)
        {
            if (rfTimedExplosive == null || entity == null || entity.net == null)
                return null;
            
            if (JetpackComponent.GetJetByMainEntityNetId(entity.net.ID.Value) != null)
                return false;
            
            if (JetpackComponent.GetJetByChildEntityNetId(entity.net.ID.Value) != null)
                return false;
            
            return null;
        }
        
        #region SupportedPlugins
        private void OnEntityEnterZone(string zoneID, BaseEntity entity)
        {
            if (entity == null || entity.net == null)
                return;

            if (_config.SupportedPluginsConfig.ZoneManagerConfig.Enable)
            {
                JetpackComponent jetPackComponent = JetpackComponent.GetJetByMainEntityNetId(entity.net.ID.Value);

                if (jetPackComponent != null && plugins.Exists("ZoneManager"))
                {
                    if (_config.SupportedPluginsConfig.ZoneManagerConfig.BlockIDs.Contains("ZoneID") || _config.SupportedPluginsConfig.ZoneManagerConfig.BlockFlags.Any(x => (bool)ZoneManager.Call("HasFlag", zoneID, x)))
                        jetPackComponent.RemoveJetpack();
                }
            }
        }

        private void OnSpaceEventStart()
        {
            _spaceHeight = (float)Space.Call("GetMinSpaceAltitude");
            _isSpaceEventActive = true;
        }

        private void OnSpaceEventStop()
        {
            _spaceHeight = 0;
            _isSpaceEventActive = false;
        }
        #endregion SupportedPlugins
        #endregion Hooks

        #region Commands
        [ChatCommand("jet")]
        private void JetChatCommand(BasePlayer player, string command, string[] arg)
        {
            if (player == null)
                return;

            JetpackComponent.OnPlayerEnterJetCommand(player);
        }

        [ConsoleCommand("jet")]
        private void ModuleGiveConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() == null)
                return;

            JetpackComponent.OnPlayerEnterJetCommand(arg.Player());
        }

        [ChatCommand("givejetpack")]
        private void GiveJetpackChatCommand(BasePlayer player, string command, string[] arg)
        {
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, _config.PermissionForGiveSelf))
            {
                PrintToChat(player, "You do not have permission to use this command!");
                return;
            }

            LootManager.GiveJetpackItemToPlayer(player);
            NotifyManager.SendMessageToPlayer(player, "GetJetpack", _ins._config.Prefix);
        }

        [ConsoleCommand("givejetpack")]
        private void GiveJetpackConsoleCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null)
            {
                if (arg.Args == null || arg.Args.Length < 1)
                    return;

                BasePlayer target = BasePlayer.FindByID(Convert.ToUInt64(arg.Args[0]));

                if (target == null)
                {
                    Puts("Player not found");
                    return;
                }

                LootManager.GiveJetpackItemToPlayer(target);
                NotifyManager.SendMessageToPlayer(target, "GetJetpack", _ins._config.Prefix);
                Puts($"A jetpack was given to {target.displayName}!");
            }
        }
        #endregion Commands

        #region Methods
        private void RegisterPermissions()
        {
            permission.RegisterPermission(_config.PermissionForUse, this);
            permission.RegisterPermission(_config.PermissionForGiveSelf, this);
            permission.RegisterPermission(_config.PermissionForNoFuel, this);
            permission.RegisterPermission(_config.PermissionJetpackCommand, this);
            permission.RegisterPermission(_config.PermissionForNoDelay, this);
        }

        private void Unsubscribes()
        {
            foreach (string hook in _subscribeMethods)
                Unsubscribe(hook);
        }

        private void Subscribes()
        {
            foreach (string hook in _subscribeMethods)
            {
                if (hook == "OnSamSiteTargetScan" && !_config.IsPlayerSamAttack && !_config.IsMonumentSamAttack)
                    continue;
                if (hook == "CanMoveItem" && _config.IsAirTakeOffAllowed)
                    continue;
                if (hook == "OnPlayerCommand" && _config.BlockedCommands.Count == 0)
                    continue;
                if (hook == "OnLootSpawn" && !_config.IsSpawnInLootEnabled)
                    continue;
                if (hook == "OnEntityEnterZone" && !_config.SupportedPluginsConfig.ZoneManagerConfig.Enable)
                    continue;

                Subscribe(hook);
            }
        }

        private static void Debug(params object[] arg)
        {
            string result = "";

            foreach (object obj in arg)
                if (obj != null)
                    result += obj.ToString() + " ";

            _ins.Puts(result);
        }
        #endregion Methods

        #region Classes
        private class JetpackComponent : FacepunchBehaviour
        {
            private BaseEntity _mainEntity;
            private Rigidbody _rigidbody;
            private BaseMountable _chair;
            private BasePlayer _player;
            private SamTargetComponent _samSiteComponent;
            private SeekerTargetComponent _seekerTargetComponent;
            private readonly List<BaseEntity> _fireEntities = new List<BaseEntity>();
            private bool _fireVisible;

            private bool _isGrounded;
            private float _lastSpeed;

            private bool _infinityFuel;
            private bool _haveFuel = true;
            private float _lastFuelConsumptionTime;
            private float _targetConsumptionAmount;

            private Coroutine _weaponHoldBlockCoroutine;

            public static void OnPlayerEnterJetCommand(BasePlayer player)
            {
                JetpackComponent jetPack = GetJetpackComponentByUserId(player.userID);

                if (jetPack != null)
                    jetPack.PlayerWantsTakeOffJetpack();
                else
                    TryAttachJetpackToPlayer(player);
            }

            public static void AddAllJetpacksToSamTargetList(SamSite samSite, List<SamSite.ISamSiteTarget> targetList)
            {
                foreach (JetpackComponent jetPackComponent in _ins._jetpacks)
                {
                    if (jetPackComponent == null || jetPackComponent._player == null)
                        continue;

                    BuildingPrivlidge buildingPrivileged = samSite.GetBuildingPrivilege();

                    if (buildingPrivileged != null && buildingPrivileged.IsAuthed(jetPackComponent._player))
                        continue;

                    if (samSite.ShortPrefabName == "sam_static")
                    {
                        if (_ins._config.IsMonumentSamAttack)
                            targetList.Add(jetPackComponent._samSiteComponent);
                    }
                    else if (_ins._config.IsPlayerSamAttack)
                    {
                        targetList.Add(jetPackComponent._samSiteComponent);
                    }
                }
            }

            public static JetpackComponent GetJetpackComponentByUserId(ulong userId)
            {
                return _ins._jetpacks.FirstOrDefault(x => x != null && x._player.userID == userId);
            }
            
            public static JetpackComponent GetJetByMainEntityNetId(ulong netId)
            {
                return _ins._jetpacks.FirstOrDefault(x => x != null && x._mainEntity.net.ID.Value == netId);
            }

            public static JetpackComponent GetJetByChildEntityNetId(ulong netId)
            {
                return _ins._jetpacks.FirstOrDefault(x => x != null && x._chair.children.Any(y => y != null && y.net.ID.Value == netId));
            }
            

            public static void RemoveJetpackFromPlayer(ulong playerUserId)
            {
                JetpackComponent jetPack = JetpackComponent.GetJetpackComponentByUserId(playerUserId);

                if (jetPack != null)
                    jetPack.RemoveJetpack();
            }

            public static void TryAttachJetpackToPlayer(BasePlayer player)
            {
                if (IsPlayerCanWearJetpack(player))
                    CreateJetpack(player);
            }

            private static bool IsPlayerCanWearJetpack(BasePlayer player)
            {
                if (player.HasPlayerFlag(BasePlayer.PlayerFlags.Wounded))
                    return false;

                if (player.isMounted)
                    return false;

                JetpackComponent jetPackComponent = GetJetpackComponentByUserId(player.userID);

                if (jetPackComponent != null)
                    return false;

                if (!_ins._config.DeepSeeAllow && DeepSeaManager.IsInsideDeepSea(player))
                {
                    NotifyManager.SendMessageToPlayer(player, "DeepSea_Restricted", _ins._config.Prefix);
                    return false;
                }

                if (!_ins._config.IsAirEquipIfAllowed && !player.IsOnGround())
                {
                    NotifyManager.SendMessageToPlayer(player, "AirEquipBlock", _ins._config.Prefix);
                    return false;
                }

                if (LootManager.IsPlayerHaveJetItem(player))
                {
                    if (!_ins.permission.UserHasPermission(player.UserIDString, _ins._config.PermissionForUse) && !_ins.permission.UserHasPermission(player.UserIDString, _ins._config.PermissionJetpackCommand))
                    {
                        NotifyManager.SendMessageToPlayer(player, "NoPermission", _ins._config.Prefix);
                        return false;
                    }
                }
                else if (!_ins.permission.UserHasPermission(player.UserIDString, _ins._config.PermissionJetpackCommand))
                {
                    return false;
                }

                if (_ins._config.WearDelay > 0)
                {
                    if (!_ins.permission.UserHasPermission(player.UserIDString, _ins._config.PermissionForNoDelay) && _ins._unequipTime.TryGetValue(player.userID, out float lastUnequipTime))
                    {
                        float timeScienceUse = Time.realtimeSinceStartup - lastUnequipTime;

                        if (timeScienceUse < _ins._config.WearDelay)
                        {
                            NotifyManager.SendMessageToPlayer(player, "Delay", _ins._config.Prefix, (int)(_ins._config.WearDelay - timeScienceUse));
                            return false;
                        }
                    }
                }

                if (IsPlayerAtHome(player))
                {
                    NotifyManager.SendMessageToPlayer(player, "AtHome", _ins._config.Prefix);
                    return false;
                }

                if (!SupportedPluginsController.IsZoneManagerAllowJetpackUse(player))
                {
                    NotifyManager.SendMessageToPlayer(player, "AreaBlock", _ins._config.Prefix);
                    return false;
                }

                return true;
            }

            private static bool IsPlayerAtHome(BasePlayer player)
            {
                if (SupportedPluginsController.CheckBySpacePlugin(player))
                    return true;

                Vector3 originPosition = player.transform.position + new Vector3(0, 1.5f, 0);

                if (IsBuildingsInDirection(originPosition, Vector3.up))
                    return true;
                else if (IsBuildingsInDirection(originPosition, player.eyes.BodyForward()) || IsBuildingsInDirection(originPosition, -player.eyes.BodyForward()))
                    return true;
                else if (IsBuildingsInDirection(originPosition, player.eyes.BodyRight()) || IsBuildingsInDirection(originPosition, -player.eyes.BodyRight()))
                    return true;

                return false;
            }

            private static bool IsBuildingsInDirection(Vector3 origin, Vector3 direction)
            {
                return Physics.Raycast(origin, direction, out RaycastHit _, 2, 1 << 21 | 1 << 8);
            }

            private static void CreateJetpack(BasePlayer player)
            {
                Vector3 jetpackPosition = player.transform.position + new Vector3(0, 1.3f, 0);
                Quaternion jetpackRotation = Quaternion.Euler(0, player.eyes.GetLookRotation().eulerAngles.y, 0) *  Quaternion.Euler(-90f, 0f, 0f);
                BaseEntity entity = BuildManager.SpawnJetpackBody(jetpackPosition, jetpackRotation);
                entity.SetFlag(BaseEntity.Flags.Locked, true);
                entity.SetFlag(BaseEntity.Flags.Busy, true);
                entity.globalBroadcast = true;
                JetpackComponent jetpackComponent = entity.gameObject.AddComponent<JetpackComponent>();
                jetpackComponent.Init(player, entity);
                _ins._jetpacks.Add(jetpackComponent);
                _ins._jetpacks.RemoveWhere(x => x == null);
            }

            private void Init(BasePlayer player, BaseEntity entity)
            {
                _player = player;
                _mainEntity = entity;

                GetAndUpdateRigidbody();
                BuildJetpack();

                Interface.CallHook("OnJetpackWear", new object[] { player });

                if (!_ins._config.Fuel.Fuel || _ins.permission.UserHasPermission(player.UserIDString, _ins._config.PermissionForNoFuel))
                    _infinityFuel = true;
                if (_ins._config.IsPlayerSamAttack || _ins._config.IsMonumentSamAttack)
                    _samSiteComponent = gameObject.AddComponent<SamTargetComponent>();

                if (_ins._config.IsWeaponAllowed)
                    _weaponHoldBlockCoroutine = ServerMgr.Instance.StartCoroutine(WeaponHoldBlockCoroutine());

                _seekerTargetComponent = SeekerTargetComponent.AttachSeekerTargetComponent(_mainEntity);
            }

            private void GetAndUpdateRigidbody()
            {
                _rigidbody = gameObject.GetComponent<Rigidbody>();
                if (_rigidbody == null)
                    _rigidbody = gameObject.AddComponent<Rigidbody>();

                _rigidbody.drag = _ins._config.Control.Drag;
                _rigidbody.centerOfMass = new Vector3(0, 0, -1.18f);
                _rigidbody.mass = 100;
                _rigidbody.useGravity = true;
                _rigidbody.isKinematic = false;
                _rigidbody.angularDrag = 3;
                _rigidbody.maxAngularVelocity = 7;
                _rigidbody.interpolation = RigidbodyInterpolation.None;
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            private void BuildJetpack()
            {
                SpawnChair();
                SpawnDecorEntities();
                SpawnFireEffects();
                CreateCollider();
            }

            private void SpawnChair()
            {
                string chairPrefabName = _ins._config.IsWeaponAllowed ? "assets/prefabs/vehicle/seats/testseat.prefab" : "assets/prefabs/vehicle/seats/standingdriver.prefab";
                Vector3 localPosition = new Vector3(0, -0.1f, -1.18f);
                Vector3 localRotation = new Vector3(90, 0, 0);

                _chair = MovableBaseMountable.CreateMovableBaseMountable(_mainEntity, chairPrefabName, localPosition, localRotation);
                _chair.MountPlayer(_player);

                if (_ins._config.IsThirdPersonView)
                    _player.SetPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode, true);
            }

            private void SpawnDecorEntities()
            {
                BuildManager.SpawnChildEntity(_chair, "assets/prefabs/weapons/rocketlauncher/rocket_launcher.entity.prefab", new Vector3(-0.210f, 0.96f, 0f), new Vector3(6.413f, 0.312f, 38.265f), string.Empty);
                BuildManager.SpawnChildEntity(_chair, "assets/prefabs/weapons/rocketlauncher/rocket_launcher.entity.prefab", new Vector3(0.342f, 0.96f, 0f), new Vector3(6.413f, 0.312f, 38.265f), string.Empty);
            }

            private void CreateCollider()
            {
                CapsuleCollider capsuleCollider = _chair.gameObject.AddComponent<CapsuleCollider>();
                capsuleCollider.gameObject.layer = 12;  
                capsuleCollider.direction = 1;

                capsuleCollider.center = new Vector3(0, 0.95f, 0);
                capsuleCollider.height = 2f;
                capsuleCollider.radius = 0.75f;

                capsuleCollider.material.staticFriction = 1;
                capsuleCollider.material.dynamicFriction = 1;
                capsuleCollider.material.frictionCombine = PhysicsMaterialCombine.Maximum;
            }

            private void OnCollisionExit(Collision collision)
            {
                OnTakeOff();
            }

            private void OnTakeOff()
            {
                if (_isGrounded)
                {
                    _isGrounded = false;
                    _rigidbody.maxAngularVelocity = 7;
                }
            }

            private void OnCollisionStay(Collision collision)
            {
                OnGrounded();
            }

            private void OnGrounded()
            {
                if (!_isGrounded)
                {
                    _isGrounded = true;
                    _rigidbody.maxAngularVelocity = 0.5f;
                }
            }

            private void FixedUpdate()
            {
                if (!CheckPassenger())
                {
                    RemoveJetpack();
                    return;
                }

                _player.PauseFlyHackDetection(2f);
                _player.PauseSpeedHackDetection(2f);
                _player.PauseVehicleNoClipDetection(2f);

                ControlEngine();
                ControlRotation();
                CheckFall();
                CheckWater();
                SpacePluginController();
            }

            private bool CheckPassenger()
            {
                return _chair.PlayerIsMounted(_player) && !_player.IsSleeping();
            }

            private void ControlEngine()
            {
                if (!_haveFuel)
                {
                    ControlFuelConsumption();
                    return;
                }

                if (_player.serverInput.IsDown(BUTTON.JUMP))
                {
                    ControlFuelConsumption();
                    EnableFireEffects();

                    if (transform.position.y < _ins._config.MaxHeight || SupportedPluginsController.IsPositionInSpace(transform.position))
                        _rigidbody.AddForce(transform.forward * (_ins._config.Control.Force * 100), ForceMode.Force);
                }
                else
                {
                    KillFireEntities();
                }
            }

            private void ControlRotation()
            {
                Quaternion rot = _rigidbody.rotation;

                if (_player.serverInput.IsDown(BUTTON.FORWARD))
                    rot = Quaternion.AngleAxis(_ins._config.Control.UpDown, rot * Vector3.right) * rot;
                else if (_player.serverInput.IsDown(BUTTON.BACKWARD))
                    rot = Quaternion.AngleAxis(-_ins._config.Control.UpDown, rot * Vector3.right) * rot;

                if (_player.serverInput.IsDown(BUTTON.RIGHT))
                    rot = Quaternion.AngleAxis(_ins._config.Control.Yaw, Vector3.up) * rot;
                else if (_player.serverInput.IsDown(BUTTON.LEFT))
                    rot = Quaternion.AngleAxis(-_ins._config.Control.Yaw, Vector3.up) * rot;

                if (_ins._config.Control.AutoRollHold)
                {
                    AutoHorizon();
                }
                else
                {
                    if (_player.serverInput.IsDown(BUTTON.DUCK))
                        rot = Quaternion.AngleAxis(_ins._config.Control.Roll, rot * Vector3.up) * rot;
                    else if (_player.serverInput.IsDown(BUTTON.SPRINT))
                        rot = Quaternion.AngleAxis(-_ins._config.Control.Roll, rot * Vector3.up) * rot;
                }

                _rigidbody.MoveRotation(rot);
            }

            private void CheckFall()
            {
                if (!_ins._config.IsFallDamageEnabled)
                    return;

                float deltaSpeed = Math.Abs(_rigidbody.velocity.magnitude - _lastSpeed);

                if (deltaSpeed > _ins._config.FallDamagePoint)
                    _player.Hurt((deltaSpeed - _ins._config.FallDamagePoint) * _ins._config.FallDamageMultiplicator, DamageType.Fall, null, false);

                _lastSpeed = _rigidbody.velocity.magnitude;
            }

            private void CheckWater()
            {
                if (_ins._config.IsTakeOffJetInWater && WaterLevel.Test(_mainEntity.transform.position, true, true))
                    RemoveJetpack();
            }

            private void ControlFuelConsumption()
            {
                if (_ins._config.Fuel.FuelPerTick <= 0 || _infinityFuel)
                    return;

                float timeScienceLastConsumption = Time.realtimeSinceStartup - _lastFuelConsumptionTime;

                if (timeScienceLastConsumption >= 2)
                {
                    List<Item> allItems = Pool.Get<List<Item>>();
                    _player.inventory.GetAllItems(allItems);
                    Item item = allItems.FirstOrDefault(x => x != null && LootManager.IsFuelItem(x.info.shortname));
                    Pool.FreeUnmanaged(ref allItems);

                    if (item == null)
                    {
                        if (_haveFuel)
                        {
                            NotifyManager.SendMessageToPlayer(_player, "NoFuel", _ins._config.Prefix);
                            _haveFuel = false;
                            KillFireEntities();
                        }
                        return;
                    }

                    _lastFuelConsumptionTime = Time.realtimeSinceStartup;
                    _targetConsumptionAmount += _ins._config.Fuel.FuelPerTick;
                    int consumptionInInt = (int)_targetConsumptionAmount;

                    if (consumptionInInt > 0)
                    {
                        _targetConsumptionAmount -= consumptionInInt;

                        if (item.amount > consumptionInInt)
                        {
                            item.amount = item.amount - consumptionInInt;
                            item.MarkDirty();
                            _haveFuel = true;
                        }
                        else
                        {
                            item.Remove();
                        }
                    }
                }
            }

            private void EnableFireEffects()
            {
                if (!_haveFuel)
                    return;

                SetFireVisible(true);
            }

            private void SpawnFireEffects()
            {
                CreateFireGun("assets/prefabs/npc/patrol helicopter/rocket_heli.prefab", new Vector3(0.278f, 0.25f, -0.15f), new Vector3(270, 0, 0));
                CreateFireGun("assets/prefabs/npc/patrol helicopter/rocket_heli.prefab", new Vector3(-0.278f, 0.25f, -0.15f), new Vector3(270, 0, 0));
            }

            private void CreateFireGun(string prefabName, Vector3 localPosition, Vector3 localRotation)
            {
                BaseEntity entity = BuildManager.SpawnChildEntity(_chair, prefabName, localPosition, localRotation, string.Empty);
                entity.SetFlag(BaseEntity.Flags.Reserved8, true);
                entity.syncPosition = false;
                entity.limitNetworking = true;
                _fireEntities.Add(entity);
            }

            private void SetFireVisible(bool visible)
            {
                if (_fireVisible == visible)
                    return;

                _fireVisible = visible;

                for (int i = 0; i < _fireEntities.Count; i++)
                {
                    BaseEntity entity = _fireEntities[i];
                    if (!entity.IsExists())
                        continue;

                    entity.limitNetworking = !visible;
                }
            }

            private void KillFireEntities()
            {
                SetFireVisible(false);
            }

            private void AutoHorizon()
            {
                if (_isGrounded)
                    return;

                float horizonAngle = Vector3.Angle(transform.right, new Vector3(transform.right.x, 0, transform.right.z));
                bool right = transform.right.y < 0;

                if (right)
                    horizonAngle *= -1;

                _rigidbody.AddTorque(transform.up * (horizonAngle * 5));
            }

            private void SpacePluginController()
            {
                if (_ins._isSpaceEventActive)
                {
                    if (SupportedPluginsController.IsPositionInSpace(_player.transform.position))
                    {
                        if (_rigidbody.useGravity)
                            _rigidbody.useGravity = false;
                    }
                    else if (!_rigidbody.useGravity)
                        _rigidbody.useGravity = true;
                }
                else if (!_rigidbody.useGravity)
                    _rigidbody.useGravity = true;
            }

            private IEnumerator WeaponHoldBlockCoroutine()
            {
                while (true)
                {
                    if (_isGrounded && LootManager.IsPositionNearToSupplyDrop(transform.position))
                        RemoveJetpack();

                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            public void PlayerWantsTakeOffJetpack()
            {
                if (IsPlayerCanTakeOffJetpack())
                    RemoveJetpack();
            }

            public bool IsPlayerCanTakeOffJetpack()
            {
                return _ins._config.IsAirTakeOffAllowed || IsJetpackOnTheGround() || IsJetpackOnCargoShip() || SupportedPluginsController.IsPositionInSpace(transform.position);
            }

            public bool IsJetpackOnTheGround()
            {
                return _isGrounded;
            }

            public bool IsJetpackOnCargoShip()
            {
                foreach (Collider collider in Physics.OverlapSphere(transform.position, 2f))
                {
                    BaseEntity entity = collider.ToBaseEntity();

                    if (entity == null)
                        continue;
                    
                    if (entity is CargoShip)
                        return true;
                }

                return false;
            }

            public static void RemoveAllJetpacks()
            {
                if (_ins?._jetpacks == null) return;
                foreach (JetpackComponent jetpack in _ins._jetpacks)
                    if (jetpack != null)
                        jetpack.RemoveJetpack(true);
            }

            public void RemoveJetpack(bool removeAllJetpacks = false)
            {
                _mainEntity.Kill();

                if (!removeAllJetpacks)
                    _ins._jetpacks.Remove(this);

                if (_ins._config.WearDelay > 0)
                    _ins._unequipTime[_player.userID] = Time.realtimeSinceStartup;
                
                if (_player != null)
                    _player.PauseFlyHackDetection(5f);
            }

            private void OnDestroy()
            {
                _chair.DismountPlayer(_player, true);

                if (_weaponHoldBlockCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_weaponHoldBlockCoroutine);

                if (_player != null)
                    _player.SetPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode, false);

                if (_seekerTargetComponent != null)
                    _seekerTargetComponent.KillComponent();

                Interface.CallHook("OnJetpackRemoved", new object[] { _player });
            }

            private class SamTargetComponent : FacepunchBehaviour, SamSite.ISamSiteTarget
            {
                private BaseEntity _baseEntity;
                private Rigidbody _rigidbody;

                private void Awake()
                {
                    _baseEntity = GetComponent<BaseEntity>();
                    _rigidbody = GetComponent<Rigidbody>();
                }

                public bool IsValidSamTarget() => true;

                public Vector3 Position => _baseEntity.transform.position;

                public SamSite.SamTargetType SAMTargetType => _ins._config.IsSamRadiusAndSpeedIncreased ? SamSite.targetTypeMissile : SamSite.targetTypeVehicle;

                public bool isClient => false;

                public bool IsValidSAMTarget(bool isStaticSamSite) => true;

                public Vector3 CenterPoint() => transform.position;

                public Vector3 GetWorldVelocity()
                {
                    return _rigidbody.velocity;
                }

                public bool IsVisible(Vector3 position, float distance) => _baseEntity.IsVisible(position, distance);
            }
        }

        private class SeekerTargetComponent : FacepunchBehaviour, SeekerTarget.ISeekerTargetOwner
        {
            private BaseEntity _movableDroppedItem;

            public static SeekerTargetComponent AttachSeekerTargetComponent(BaseEntity baseEntity)
            {
                if (!_ins._config.HomingLauncherConfig.IsEnable)
                    return null;

                GameObject gameObject = new GameObject("SeekerTargetComponent");
                gameObject.transform.SetParent(baseEntity.transform, false);
                SeekerTargetComponent seekerTargetComponent = gameObject.AddComponent<SeekerTargetComponent>();
                seekerTargetComponent.Init(baseEntity);
                return seekerTargetComponent;
            }

            private void Init(BaseEntity baseEntity)
            {
                _movableDroppedItem = baseEntity;
                SeekerTarget.SeekerStrength seekerStrength = _ins._config.HomingLauncherConfig.TargetCaptureDistance == 0 ? SeekerTarget.SeekerStrength.LOW : _ins._config.HomingLauncherConfig.TargetCaptureDistance == 1 ? SeekerTarget.SeekerStrength.MEDIUM : SeekerTarget.SeekerStrength.HIGH;
                SeekerTarget.SetSeekerTarget(this, seekerStrength);
            }

            public bool InSafeZone()
            {
                return _movableDroppedItem.InSafeZone();
            }

            public bool IsValidHomingTarget()
            {
                return true;
            }

            public void OnEntityMessage(BaseEntity from, string msg)
            {

            }

            public Vector3 CenterPoint()
            {
                return _movableDroppedItem.WorldSpaceBounds().position;
            }

            public bool IsVisible(Vector3 position, float maxDistance = float.PositiveInfinity)
            {
                return _movableDroppedItem.IsVisibleAndCanSee(position) || _movableDroppedItem.children.Any(x => x != null && x.IsVisibleAndCanSee(position));
            }

            public void KillComponent()
            {
                if (this.gameObject != null)
                    UnityEngine.GameObject.Destroy(this.gameObject);
            }
        }

        private sealed class JetpackBody : BaseEntity
        {
            public override bool PositionTickFixedTime => true;
        }

        private sealed class MovableBaseMountable : BaseMountable
        {
            public static MovableBaseMountable CreateMovableBaseMountable(BaseEntity parentEntity, string seatPrefab, Vector3 localPosition, Vector3 localRotation)
            {
                BaseMountable baseMountable = GameManager.server.CreateEntity(seatPrefab, parentEntity.transform.position) as BaseMountable;
                baseMountable.enableSaving = false;

                MovableBaseMountable movableBaseMountable = baseMountable.gameObject.AddComponent<MovableBaseMountable>();
                BuildManager.CopySerializableFields(baseMountable, movableBaseMountable);

                baseMountable.StopAllCoroutines();
                UnityEngine.GameObject.DestroyImmediate(baseMountable, true);
                movableBaseMountable.syncPosition = false;
                movableBaseMountable.enableSaving = false;
                BuildManager.SetParent(parentEntity, movableBaseMountable, localPosition, localRotation, string.Empty);
                movableBaseMountable.Spawn();
                return movableBaseMountable;
            }

            public override void DismountAllPlayers()
            {

            }

            public override bool GetDismountPosition(BasePlayer player, out Vector3 res, bool silent = false)
            {
                res = player.transform.position;
                return true;
            }
            
            public override float AntiHackPadding()
            {
                return 0;
            }

            public override float AntiHackVelocity()
            {
                return float.MaxValue;
            }

            public override float PositionTickRate => -1f;
        }

        private static class LootManager
        {
            public static void OnSupplyDropSpawned(SupplyDrop supplyDrop)
            {
                _ins._supplyDrops.RemoveWhere(x => !x.IsExists());
                _ins._supplyDrops.Add(supplyDrop);
            }

            public static bool IsPositionNearToSupplyDrop(Vector3 position)
            {
                return _ins._supplyDrops.Any(x => x.IsExists() && Vector3.Distance(position, x.transform.position) < 3);
            }

            public static bool IsFuelItem(string shortname)
            {
                return shortname == _ins._config.Fuel.ItemShortname;
            }

            public static bool IsJetpackItem(Item item)
            {
                if (item == null || item.info == null)
                    return false;

                return item.info.shortname == _ins._config.ItemConfig.Shortname && item.skin == _ins._config.ItemConfig.Skin;
            }

            public static bool IsPlayerHaveJetItem(BasePlayer player)
            {
                if (_ins._config.AllowOpenFromAnyContainer)
                {
                    List<Item> allItems = Pool.Get<List<Item>>();
                    player.inventory.GetAllItems(allItems);

                    if (allItems.Any(IsJetpackItem))
                    {
                        Pool.FreeUnmanaged(ref allItems);
                        return true;
                    }

                    Pool.FreeUnmanaged(ref allItems);
                    return false;
                }

                return player.inventory.containerWear.itemList.Any(IsJetpackItem);
            }

            public static void TrySpawnItemInDefaultCrate(LootContainer lootContainer, ItemConfig itemConfig, float chance, int removeItemIndex = 0)
            {
                _ins.NextTick(() =>
                {
                    if (lootContainer == null)
                        return;

                    if (UnityEngine.Random.Range(0f, 100f) <= chance)
                    {
                        Item item = CreateItem(itemConfig, 1);
                        if (lootContainer.inventory.itemList.Count > removeItemIndex)
                        {
                            Item removeItem = lootContainer.inventory.itemList[removeItemIndex];

                            if (removeItem != null)
                                lootContainer.inventory.Remove(removeItem);
                        }

                        if (!item.MoveToContainer(lootContainer.inventory))
                            item.Remove();
                    }
                });
            }

            public static void GiveJetpackItemToPlayer(BasePlayer player)
            {
                Item item = CreateItem(_ins._config.ItemConfig, 1);

                if (item != null)
                    GiveItemToPLayer(player, item);
            }

            private static void GiveItemToPLayer(BasePlayer player, Item item)
            {
                int spaceCountItem = PLayerInventory.GetSpaceCountItem(player, item.info.shortname, item.MaxStackable(), item.skin);
                int inventoryItemCount;

                if (spaceCountItem > item.amount)
                    inventoryItemCount = item.amount;
                else
                    inventoryItemCount = spaceCountItem;

                if (inventoryItemCount > 0)
                {
                    Item itemInventory = ItemManager.CreateByName(item.info.shortname, inventoryItemCount, item.skin);

                    if (item.skin != 0)
                        itemInventory.name = item.name;

                    item.amount -= inventoryItemCount;
                    PLayerInventory.MoveInventoryItem(player, itemInventory);
                }

                if (item.amount > 0)
                    PLayerInventory.DropExtraItem(player, item);
            }

            public static Item CreateItem(ItemConfig itemConfig, int amount)
            {
                Item item = ItemManager.CreateByName(itemConfig.Shortname, amount, itemConfig.Skin);

                if (itemConfig.Name != "")
                    item.name = itemConfig.Name;

                return item;
            }

            private static class PLayerInventory
            {
                public static int GetSpaceCountItem(BasePlayer player, string shortname, int stack, ulong skinID)
                {
                    int inventoryCapacity = player.inventory.containerMain.capacity + player.inventory.containerBelt.capacity;
                    int taken = player.inventory.containerMain.itemList.Count + player.inventory.containerBelt.itemList.Count;
                    int result = (inventoryCapacity - taken) * stack;

                    List<Item> allItems = Pool.Get<List<Item>>();
                    player.inventory.GetAllItems(allItems);

                    foreach (Item item in allItems)
                        if (item.info.shortname == shortname && item.skin == skinID && item.amount < stack)
                            result += stack - item.amount;

                    Pool.FreeUnmanaged(ref allItems);

                    return result;
                }

                public static void MoveInventoryItem(BasePlayer player, Item item)
                {
                    if (item.amount <= item.MaxStackable())
                    {
                        List<Item> allItems = Pool.Get<List<Item>>();
                        player.inventory.GetAllItems(allItems);

                        foreach (Item itemInv in allItems)
                        {
                            if (itemInv.info.shortname == item.info.shortname && itemInv.skin == item.skin && itemInv.amount < itemInv.MaxStackable())
                            {
                                if (itemInv.amount + item.amount <= itemInv.MaxStackable())
                                {
                                    itemInv.amount += item.amount;
                                    itemInv.MarkDirty();
                                    Pool.FreeUnmanaged(ref allItems);
                                    return;
                                }
                                else
                                {
                                    item.amount -= itemInv.MaxStackable() - itemInv.amount;
                                    itemInv.amount = itemInv.MaxStackable();
                                }
                            }
                        }

                        Pool.FreeUnmanaged(ref allItems);

                        if (item.amount > 0) player.inventory.GiveItem(item);
                    }
                    else
                    {
                        while (item.amount > item.MaxStackable())
                        {
                            Item thisItem = ItemManager.CreateByName(item.info.shortname, item.MaxStackable(), item.skin);
                            if (item.skin != 0) thisItem.name = item.name;
                            player.inventory.GiveItem(thisItem);
                            item.amount -= item.MaxStackable();
                        }
                        if (item.amount > 0) player.inventory.GiveItem(item);
                    }
                }

                public static void DropExtraItem(BasePlayer player, Item item)
                {
                    if (item.amount <= item.MaxStackable()) item.Drop(player.transform.position, Vector3.up);
                    else
                    {
                        while (item.amount > item.MaxStackable())
                        {
                            Item thisItem = ItemManager.CreateByName(item.info.shortname, item.MaxStackable(), item.skin);
                            if (item.skin != 0) thisItem.name = item.name;
                            thisItem.Drop(player.transform.position, Vector3.up);
                            item.amount -= item.MaxStackable();
                        }
                        if (item.amount > 0) item.Drop(player.transform.position, Vector3.up);
                    }
                }
            }
        }

        private static class BuildManager
        {
            public static void UpdateEntityScale(BaseEntity entity, Vector3 scale, bool hardUpdate)
            {
                entity.transform.localScale = scale;
                
                if (scale == Vector3.one)
                {
                    entity.networkEntityScale = true;
                    entity.SendNetworkUpdateImmediate();
                    entity.networkEntityScale = false;
                }
                else
                {
                    entity.networkEntityScale = true;
                    entity.SendNetworkUpdate();
                }

                if (hardUpdate)
                    HardEntityUpdate(entity);
            }

            public static void HardEntityUpdate(BaseEntity entity)
            {
                entity.limitNetworking = true;
                entity.limitNetworking = false;
            }
            
            public static BaseEntity SpawnJetpackBody(Vector3 position, Quaternion rotation)
            {
                BaseEntity entity = CreateJetpackBody(position, rotation);
                DestroyUnnecessaryComponents(entity);
                DestroyDecorComponents(entity);
                entity.Spawn();
                return entity;
            }

            public static BaseEntity SpawnDecorEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0)
            {
                BaseEntity entity = CreateDecorEntity(prefabName, position, rotation, skinId);
                DestroyUnnecessaryComponents(entity);
                DestroyDecorComponents(entity);
                entity.Spawn();
                return entity;
            }
            
            public static BaseEntity SpawnChildEntity(BaseEntity parentEntity, string prefabName, Vector3 localPosition, Vector3 localRotation, string bone, ulong skinId = 0, bool isDecor = true, bool enableSaving = false)
            {
                BaseEntity entity = isDecor ? CreateDecorEntity(prefabName, parentEntity.transform.position, Quaternion.identity, skinId) : CreateEntity(prefabName, parentEntity.transform.position, Quaternion.identity, skinId, enableSaving);
                SetParent(parentEntity, entity, localPosition, localRotation, bone);
                DestroyUnnecessaryComponents(entity);
                if (isDecor)
                    DestroyDecorComponents(entity);

                entity.Spawn();
                entity.syncPosition = false;
                return entity;
            }
            
            public static BaseEntity CreateEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId, bool enableSaving)
            {
                BaseEntity entity = GameManager.server.CreateEntity(prefabName, position, rotation);
                entity.enableSaving = enableSaving;
                entity.skinID = skinId;
                return entity;
            }

            public static JetpackBody CreateJetpackBody(Vector3 position, Quaternion rotation)
            {
                BaseEntity entity = CreateEntity("assets/prefabs/misc/item drop/item_drop_backpack.prefab", position, rotation, 0, false);
                JetpackBody body = entity.gameObject.AddComponent<JetpackBody>();
                CopySerializableFields<BaseEntity>(entity, body);
                UnityEngine.Object.DestroyImmediate(entity, true);
                body.SetFlag(BaseEntity.Flags.Busy, true);
                body.SetFlag(BaseEntity.Flags.Locked, true);
                body.syncPosition = true;
                body.enableSaving = false;
                return body;
            }

            public static BaseEntity CreateDecorEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0, bool enableSaving = false)
            {
                BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, enableSaving);

                BaseEntity trueBaseEntity = entity.gameObject.AddComponent<BaseEntity>();
                CopySerializableFields(entity, trueBaseEntity);
                UnityEngine.Object.DestroyImmediate(entity, true);
                trueBaseEntity.SetFlag(BaseEntity.Flags.Busy, true);
                trueBaseEntity.SetFlag(BaseEntity.Flags.Locked, true);
                trueBaseEntity.syncPosition = false;
                trueBaseEntity.enableSaving = enableSaving;

                return trueBaseEntity;
            }

            public static void SetParent(BaseEntity parentEntity, BaseEntity childEntity, Vector3 localPosition, Vector3 localRotation, string bone)
            {
                childEntity.transform.localPosition = localPosition;
                childEntity.transform.localEulerAngles = localRotation;
                if (string.IsNullOrEmpty(bone))
                    childEntity.SetParent(parentEntity);
                else
                    childEntity.SetParent(parentEntity, bone);
            }
            
            private static void DestroyDecorComponents(BaseEntity entity)
            {
                DestroyEntityComponents<TriggerParent>(entity);

                Component[] components = entity.GetComponentsInChildren<Component>();

                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    EntityCollisionMessage entityCollisionMessage = component as EntityCollisionMessage;

                    if (entityCollisionMessage || (component && component.name != entity.PrefabName))
                    {
                        Transform transform = component as Transform;
                        if (transform)
                            continue;

                        Collider collider = component as Collider;
                        if (collider && !collider.isTrigger && collider.gameObject.layer != 29)
                            continue;

                        if (component is Model)
                            continue;

                        UnityEngine.Object.DestroyImmediate(component);
                    }
                }
            }

            private static void DestroyUnnecessaryComponents(BaseEntity entity)
            {
                DestroyEntityComponent<GroundWatch>(entity);
                DestroyEntityComponent<DestroyOnGroundMissing>(entity);
                DestroyEntityComponent<TriggerHurtEx>(entity);

                if (entity.ShortPrefabName is "dpv.deployed" or "drone.deployed" or "item_drop" or "item_drop_backpack")
                    return;

                if (entity is not HotAirBalloon && entity is not FreeableLootContainer)
                {
                    DestroyEntityComponent<Rigidbody>(entity);
                }
            }

            public static void DestroyEntityComponent<TYpeForDestroy>(BaseEntity entity) where TYpeForDestroy : UnityEngine.Object
            {
                TYpeForDestroy component = entity.GetComponent<TYpeForDestroy>();
                if (component)
                    UnityEngine.Object.DestroyImmediate(component);
            }

            public static void DestroyEntityComponents<TYpeForDestroy>(BaseEntity entity) where TYpeForDestroy : UnityEngine.Object
            {
                TYpeForDestroy[] components = entity.GetComponentsInChildren<TYpeForDestroy>();

                for (int i = 0; i < components.Length; i++)
                {
                    TYpeForDestroy component = components[i];

                    if (component != null)
                        UnityEngine.Object.DestroyImmediate(component);
                }
            }

            public static void CopySerializableFields<T>(T src, T dst)
            {
                FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo field in srcFields)
                {
                    object value = field.GetValue(src);
                    field.SetValue(dst, value);
                }
            }
        }
        

        private static class NotifyManager
        {
            public static void SendMessageToPlayer(BasePlayer player, string langKey, params object[] args)
            {
                if (_ins._config.NotificationConfig.IsChat)
                    _ins.PrintToChat(player, GetMessage(langKey, player.UserIDString, args));
            }
        }

        private static class SupportedPluginsController
        {
            

            public static bool IsPositionInSpace(Vector3 position)
            {
                return _ins._isSpaceEventActive && _ins._spaceHeight > 0 && position.y > _ins._spaceHeight;
            }

            public static bool CheckBySpacePlugin(BasePlayer player)
            {
                if (IsPositionInSpace(player.transform.position))
                {
                    if (Physics.Raycast(player.eyes.transform.position, Vector3.up, out RaycastHit raycastHit, 10, 1 << 21) && Physics.Raycast(player.eyes.transform.position, -Vector3.up, out raycastHit, 10, 1 << 21) && !raycastHit.collider.name.Contains("floor.triangle.twig") && !raycastHit.collider.name.Contains("floor.frame"))
                        return true;

                    if (player.GetMounted() != null && player.GetMounted().PrefabName == "assets/prefabs/vehicle/seats/testseat.prefab")
                        player.GetMounted().Kill();
                }

                return false;
            }

            public static bool IsZoneManagerAllowJetpackUse(BasePlayer player)
            {
                if (!_ins._config.SupportedPluginsConfig.ZoneManagerConfig.Enable || !_ins.plugins.Exists("ZoneManager"))
                    return true;

                string[] playerZones = (string[])_ins.ZoneManager.Call("GetPlayerZoneIDs", player);

                if (playerZones == null || playerZones.Length == 0)
                    return true;

                if (playerZones.Any(zoneName => _ins._config.SupportedPluginsConfig.ZoneManagerConfig.BlockIDs.Contains(zoneName)))
                    return false;
                else if (playerZones.Any(zoneName => _ins._config.SupportedPluginsConfig.ZoneManagerConfig.BlockFlags.Any(flagName => (bool)_ins.ZoneManager.Call("HasFlag", zoneName, flagName))))
                    return false;

                return true;
            }
        }
        #endregion Classes

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["DeepSea_Restricted"] = "{0} You cannot use a <color=#b03b1e>jetpack</color> in deep sea!",
                
                ["GetJetpack"] = "{0} You <color=#738d43>got</color> a jetpack!",
                ["NoPermission"] = "{0} You <color=#b03b1e>do not have permission</color> to use a jetpack!",
                ["AtHome"] = "{0} You <color=#b03b1e>cannot</color> use a jetpack indoors!",
                ["AirEquipBlock"] = "{0} It is <color=#b03b1e>forbidden</color> to equip a jetpack in the air!",
                ["NoFuel"] = "{0} To use the jetpack, put fuel to <color=#738d43>inventory</color>!",
                ["CommandBlock"] = "{0} This command is <color=#b03b1e>not allowed</color> to be used with a jetpack!",
                ["AreaBlock"] = "{0} It is <color=#b03b1e>forbidden</color> to use a jetpack in this area!",
                ["ActivateCommand"] = "{0} To activate the jetpack, use the command <color=#738d43>/jet</color>",
                ["Delay"] = "{0} You will be able to use the jetpack in <color=#738d43>{1}</color> s.",
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["DeepSea_Restricted"] = "{0} Вы не можете использовать <color=#b03b1e>реактивный ранец</color> в глубоком море!",
                
                ["GetJetpack"] = "{0} Вы <color=#738d43>получили</color> реактивный ранец!",
                ["NoPermission"] = "{0} У вас <color=#b03b1e>нет разрешения</color> использовать реактивный ранец!",
                ["AtHome"] = "{0} <color=#b03b1e>Запрещено</color> использовать реактивный ранец в помещении!",
                ["AirEquipBlock"] = "{0} <color=#b03b1e>Запрещено</color> надевать джетпак в воздухе!",
                ["NoFuel"] = "{0} Для использования реактивного ранца положите топливо в <color=#2257b3>инвентарь</color>!",
                ["CommandBlock"] = "{0} Эту команду <color=#b03b1e>запрещено</color> использовать на джетпаке!",
                ["AreaBlock"] = "{0} <color=#b03b1e>Запрещено</color> использовать джетпак в этой области!",
                ["ActivateCommand"] = "{0} Для активации джетпака используйте команду <color=#738d43>/jet</color>!",
                ["Delay"] = "{0} Вы сможете использовать джетпак через <color=#738d43>{1}</color> с.",
            }, this, "ru");
        }

        private static string GetMessage(string langKey, string userID) => _ins.lang.GetMessage(langKey, _ins, userID);

        private static string GetMessage(string langKey, string userID, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userID) : string.Format(GetMessage(langKey, userID), args);
        #endregion Lang

        #region Config  
        private PluginConfig _config;
        
        private void UpdateConfig()
        {
            if (_config.Version != Version.ToString())
            {
                VersionNumber versionNumber;
                var versionArray = _config.Version.Split('.');
                versionNumber.Major = Convert.ToInt32(versionArray[0]);
                versionNumber.Minor = Convert.ToInt32(versionArray[1]);
                versionNumber.Patch = Convert.ToInt32(versionArray[2]);
                
                PluginConfig defaultConfig = PluginConfig.DefaultConfig();
                
                if (versionNumber.Minor == 2)
                {
                    if (versionNumber.Patch <= 3)
                    {
                        _config.HomingLauncherConfig = new HomingLauncherConfig
                        {
                            IsEnable = true,
                            TargetCaptureDistance = 1
                        };
                    }

                    if (versionNumber.Patch <= 8)
                    {
                        _config.PermissionForNoDelay = "jetpack.nodelay";
                    }
                    versionNumber = new VersionNumber(1, 3, 0);
                }

                if (versionNumber.Minor == 3)
                {
                    if (versionNumber.Patch == 0)
                    {
                        _config.HomingLauncherConfig = defaultConfig.HomingLauncherConfig;
                        _config.SupportedPluginsConfig.ZoneManagerConfig = defaultConfig.SupportedPluginsConfig.ZoneManagerConfig;
                    }

                    if (versionNumber.Patch <= 4)
                    {
                        _config.DeepSeeAllow = true;
                    }
                }

                _config.Version = Version.ToString();
                SaveConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = PluginConfig.DefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(_config, true);
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private class ItemConfig
        {
            [JsonProperty("Shortname")] 
            public string Shortname { get; set; }
            
            [JsonProperty("Skin")]
            public ulong Skin { get; set; }
            
            [JsonProperty("Name")]
            public string Name { get; set; }
        }

        private class FuelConfig
        {
            [JsonProperty(En ? "Use fuel" : "Использовать топливо?")]
            public bool Fuel { get; set; }
            
            [JsonProperty(En ? "Fuel consumption" : "Потребление топлива за период")] 
            public int FuelPerTick { get; set; }
            
            [JsonProperty(En ? "Item" : "Предмет")]
            public string ItemShortname { get; set; }
        }

        private class HomingLauncherConfig
        {
            [JsonProperty(En ? "Allow the Homing Launcher to attack the jetpack? [true/false]" : "Разрешить ПЗРК атаковать джетпак? [true/false]")]
            public bool IsEnable { get; set; }
            
            [JsonProperty(En ? "Target capture distance (0 - 100m, 1 - 200m, 2 - 1000m)" : "Дистанция для захвата джетпака (0 - 100м, 1 - 200м, 2 - 1000м)")]
            public int TargetCaptureDistance { get; set; }
        }

        private class ControlConfig
        {
            [JsonProperty(En ? "Air resistance" : "Сопротивление воздуха")]
            public float Drag { get; set; }
            
            [JsonProperty(En ? "Thrust" : "Тяга")]
            public float Force { get; set; }
            
            [JsonProperty(En ? "Pitch" : "Тангаж")]
            public float UpDown { get; set; }
            
            [JsonProperty(En ? "Roll" : "Крен")]
            public float Roll { get; set; }
            
            [JsonProperty(En ? "Yaw" : "Рыскание")]
            public float Yaw { get; set; }
            
            [JsonProperty(En ? "Control assistance" : "Помощь в управлении")]
            public bool AutoRollHold { get; set; }
        }

        private class CrateConfig
        {
            [JsonProperty(En ? "Prefab" : "Префаб ящика")] 
            public string Prefab { get; set; }
            
            [JsonProperty(En ? "Chance" : "Шанс")] 
            public float Chance { get; set; }
        }

        private class NotificationConfig
        {
            [JsonProperty(En ? "Use Chat Notifications? [true/false]" : "Использовать ли чат? [true/false]")] 
            public bool IsChat { get; set; }
        }

        private class SupportedPluginsConfig
        {
            [JsonProperty(En ? "ZoneManager setting" : "Настройка ZoneManager")] 
            public ZoneManagerConfig ZoneManagerConfig { get; set; }
        }

        private class ZoneManagerConfig
        {
            [JsonProperty(En ? "Do you use the ZoneManager? [true/false]" : "Использовать ли ZoneManager? [true/false]")] 
            public bool Enable { get; set; }
            
            [JsonProperty(En ? "List of zone flags that prohibit jetpack use inside" : "Список флагов зон, которые запрещают использование джетпака внутри")] 
            public HashSet<string> BlockFlags { get; set; }
            
            [JsonProperty(En ? "List of zone IDs that prohibit jetpack use inside" : "Список ID зон, которые запрещают использование джетпака внутри")] 
            public HashSet<string> BlockIDs { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Version" : "Версия")] 
            public string Version { get; set; }
            
            [JsonProperty(En ? "Prefix of chat messages" : "Префикс в чате")] 
            public string Prefix { get; set; }
            
            [JsonProperty(En ? "Permission to use" : "Разрешение для использования")] 
            public string PermissionForUse { get; set; }
            
            [JsonProperty(En ? "Permission to use a Jetpack without a Jetpack item in the inventory (chat command - /jet)" : "Разрешение для использования Джетпака без соответствующего предмета в инвентаре (команда - /jet)")] 
            public string PermissionJetpackCommand { get; set; }
            
            [JsonProperty(En ? "Permission to give to yourself" : "Разрешение для выдачи себе")] 
            public string PermissionForGiveSelf { get; set; }
            
            [JsonProperty(En ? "Permission to turn off fuel" : "Разрешение для отключения топлива")] 
            public string PermissionForNoFuel { get; set; }
            
            [JsonProperty(En ? "Permission to disable the delay between uses" : "Разрешение для отключения задержки между использованиями")] 
            public string PermissionForNoDelay { get; set; }
            
            [JsonProperty(En ? "A cooldown period during which the player cannot equip the jetpack after unequipping it [sec]" : "Задержка, в течение которой игрок не может надеть джетпак после его снятия [sec]")] 
            public float WearDelay { get; set; }
            
            [JsonProperty(En ? "Activate the jetpack when moving an item into a clothing container?" : "Активировать джетпак при перемещении предмета в контейнер одежды? ")] 
            public bool WearWhenItemAdded { get; set; }
            
            [JsonProperty(En ? "Allow the jetpack to be activated with the /jet command from any inventory container? (false - only from the clothing container)" : "Разрешить активировать джетпак командой /jet из любого контейнера инвентаря? (false - только из контейнера одежды)")] 
            public bool AllowOpenFromAnyContainer { get; set; }
            
            [JsonProperty(En ? "Maximum flight altitude" : "Максимальная высота полета")] 
            public float MaxHeight { get; set; }
            
            [JsonProperty(En ? "Allow the use of weapons on the jetpack?" : "Разрешить использование оружия на джетпаке?")] 
            public bool IsWeaponAllowed { get; set; }
            
            [JsonProperty(En ? "Take off a jetpack in the water?" : "Снимать джетпак при попадании в воду")] 
            public bool IsTakeOffJetInWater { get; set; }
            
            [JsonProperty(En ? "Allow to take off the jetpack in the air" : "Разрешить снимать джетпак в воздухе")] 
            public bool IsAirTakeOffAllowed { get; set; }
            
            [JsonProperty(En ? "Allow to equip a jetpack in the air" : "Разрешить одевать джетпак в воздухе")] 
            public bool IsAirEquipIfAllowed { get; set; }
            
            [JsonProperty(En ? "Allow jetpack in deep sea" : "Разрешение одевать джетпак в глубоком море")] 
            public bool DeepSeeAllow { get; set; }
            
            [JsonProperty(En ? "Third-person view" : "Вид от третьего лица")] 
            public bool IsThirdPersonView { get; set; }
            
            [JsonProperty(En ? "Enable collision damage" : "Включить урон от столкновений")] 
            public bool IsFallDamageEnabled { get; set; }
            
            [JsonProperty(En ? "Collision Damage Multiplier" : "Множитель урона от столкновений")] 
            public float FallDamageMultiplicator { get; set; }
            
            [JsonProperty(En ? "Collision acceleration threshold" : "Порог урона от столкновений")] 
            public float FallDamagePoint { get; set; }
            
            [JsonProperty(En ? "The SamSite will attack the jetpack" : "ПВО будет атаковать джетпак")] 
            public bool IsPlayerSamAttack { get; set; }
            
            [JsonProperty(En ? "The SamSite on the monuments will attack the jetpack" : "ПВО на рт будет атаковать джетпак")] 
            public bool IsMonumentSamAttack { get; set; }
            
            [JsonProperty(En ? "Increased missile speed and SamSite attack radius by jetpack" : "Увеличеные скорость ракет и радиус атаки ПВО по джетпаку")] 
            public bool IsSamRadiusAndSpeedIncreased { get; set; }
            
            [JsonProperty(En ? "Homing Launcher Config" : "Настройки ПЗРК")] 
            public HomingLauncherConfig HomingLauncherConfig { get; set; }
            
            [JsonProperty(En ? "Allow loot supply drop when using a jetpack" : "Разрешить лутать аирдроп при использовании джетпака")] 
            public bool IsAllowedLootAirdrop { get; set; }
            
            [JsonProperty(En ? "List of commands that are prohibited while using the jetpack" : "Список команд, которые запрещены во время использования джетпака")] 
            public HashSet<string> BlockedCommands { get; set; }
            
            [JsonProperty(En ? "Control" : "Управление")] 
            public ControlConfig Control { get; set; }
            
            [JsonProperty(En ? "Fuel" : "Топливо")] 
            public FuelConfig Fuel { get; set; }
            
            [JsonProperty(En ? "Item" : "Предмет")] 
            public ItemConfig ItemConfig { get; set; }
            
            [JsonProperty(En ? "Enable spawn in crates" : "Включить спавн в ящиках")] 
            public bool IsSpawnInLootEnabled { get; set; }
            
            [JsonProperty(En ? "Spawn setting" : "Настройка спавна в ящиках")] 
            public List<CrateConfig> Crates { get; set; }
            
            [JsonProperty(En ? "Notification Settings" : "Настройкa уведомлений")] 
            public NotificationConfig NotificationConfig { get; set; }
            
            [JsonProperty(En ? "Supported Plugins" : "Поддерживаемые плагины")] 
            public SupportedPluginsConfig SupportedPluginsConfig { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    Version = "1.3.7",
                    Prefix = "[JetPack]",
                    PermissionForUse = "jetpack.use",
                    PermissionJetpackCommand = "jetpack.command",
                    PermissionForGiveSelf = "jetpack.giveself",
                    PermissionForNoFuel = "jetpack.fuel",
                    PermissionForNoDelay = "jetpack.nodelay",
                    WearDelay = 10,
                    WearWhenItemAdded = false,
                    AllowOpenFromAnyContainer = false,
                    MaxHeight = 1000,
                    IsWeaponAllowed = true,
                    IsTakeOffJetInWater = true,
                    IsAirTakeOffAllowed = false,
                    IsAirEquipIfAllowed = false,
                    DeepSeeAllow = true,
                    IsThirdPersonView = false,
                    IsFallDamageEnabled = true,
                    FallDamageMultiplicator = 5f,
                    FallDamagePoint = 2.5f,
                    IsPlayerSamAttack = true,
                    IsSamRadiusAndSpeedIncreased = true,
                    HomingLauncherConfig = new HomingLauncherConfig
                    {
                        IsEnable = true,
                        TargetCaptureDistance = 1
                    },
                    BlockedCommands = new HashSet<string>
                    {
                        "home"
                    },
                    Control = new ControlConfig
                    {
                        Force = 20f,
                        Roll = 1.5f,
                        UpDown = 1.5f,
                        Yaw = 1.5f,
                        Drag = 0.7f,
                        AutoRollHold = true,
                    },
                    Fuel = new FuelConfig
                    {
                        Fuel = true,
                        FuelPerTick = 1,
                        ItemShortname = "lowgradefuel",

                    },
                    ItemConfig = new ItemConfig
                    {
                        Shortname = "burlap.gloves.new",
                        Skin = 2632956407,
                        Name = "Jetpack"
                    },
                    IsSpawnInLootEnabled = false,
                    Crates = new List<CrateConfig>
                    {
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            Chance = 5f
                        }
                    },
                    NotificationConfig = new NotificationConfig
                    {
                        IsChat = true,
                    },
                    SupportedPluginsConfig = new SupportedPluginsConfig
                    {
                        ZoneManagerConfig = new ZoneManagerConfig
                        {
                            Enable = false,
                            BlockFlags = new HashSet<string>
                            {
                                "eject",
                                "pvegod"
                            },
                            BlockIDs = new HashSet<string>
                            {
                                "Example"
                            }
                        }
                    }
                };
            }
        }
        #endregion Config
    }
}

namespace Oxide.Plugins.JetPackExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }
        
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return enumerator.Current;
            return default(TSource);
        }

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static bool IsRealPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();
    }
}