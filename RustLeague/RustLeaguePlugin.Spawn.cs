using System;
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;
using Rust;
using Rust.Modular;
using UnityEngine;

namespace RustLeagueHarmony
{
    public partial class RustLeaguePlugin
    {
        private void closeEvent(string teamWIn = "none")
        {
            if (configData.ItemSettings.joinItemEnable) refundPlayer();
            zoneRed?.Destory();
            zoneBlue?.Destory();
            if (zoneRed != null) UnityEngine.Object.Destroy(zoneRed);
            if (zoneBlue != null) UnityEngine.Object.Destroy(zoneBlue);
            if (ballMono != null) UnityEngine.Object.Destroy(ballMono);
            if (arenaBounds != null) UnityEngine.Object.Destroy(arenaBounds);
            DestroyLeftovers();
            KillMapMarker();
            DespawnArena();

            eventOpen = false;
            eventRunning = false;
            carCount = 0;
            eventRoundOver = false;
            inEventRound = 0;
            soloTest = false;
            testing = configData.settings.testing;

            if (EventPlayerLastPos.Count > 0)
            {
                foreach (var key in EventPlayerLastPos)
                {
                    BasePlayer player = BasePlayer.FindByID(key.Key);
                    if (player == null) continue;
                    TeleportPlayerPosition(player, key.Value, null);
                    CuiHelper.DestroyUi(player, "ScoreBlocknameTimer");
                    CuiHelper.DestroyUi(player, "RtimerSBlocknameTimer");
                    CuiHelper.DestroyUi(player, "MessagesBlocknameTimer");
                    CuiHelper.DestroyUi(player, "TeamHudBlocknameTimer");
                    CuiHelper.DestroyUi(player, "waitingPlay");
                }
            }

            if (eventEntitys.Count > 0)
            {
                foreach (var keys in eventEntitys)
                {
                    var entity = FindEntity(keys);
                    if (entity == null) continue;
                    if (entity is ModularCar chasse)
                    {
                        foreach (BaseVehicleModule attachedModuleEntity in chasse.AttachedModuleEntities)
                        {
                            if (attachedModuleEntity is not VehicleModuleEngine engine) continue;
                            var container = engine.GetContainer() as EngineStorage;
                            if (container?.inventory == null) continue;
                            container.inventory.Clear();
                        }
                    }
                    timer.NextTick(() => { entity?.Kill(); });
                }
            }
            timer.NextTick(PurgeDestroyedFromSaveList);

            if (teamWIn != "none")
                AnnounceWinners(teamWIn);

            ClearJoinState();
            finalScore = "";
            AfterEventClosed();
        }

        private void AnnounceWinners(string teamWIn)
        {
            string winners = "";
            string losers = "";
            foreach (var key in RuningEventPlayer)
            {
                BasePlayer player = BasePlayer.FindByID(key.Key);
                if (player == null) continue;
                bool winnerSide = teamWIn == "tie"
                    ? key.Value == "red"
                    : key.Value == teamWIn;
                string color = key.Value == "red" ? "<color=#ce422b>" : "<color=#0000FF>";
                string entry = color + player.displayName + "</color>";
                if (winnerSide)
                    winners = winners.Length == 0 ? entry : winners + ", " + entry;
                else
                    losers = losers.Length == 0 ? entry : losers + ", " + entry;
                if (teamWIn != "tie" && key.Value == teamWIn && configData.ItemSettings.winItemEnable)
                {
                    var captured = player;
                    timer.Once(5f, () => giveWinItem(captured, configData.ItemSettings.winItem, configData.ItemSettings.winItemAmount, false));
                }
            }
            if (teamWIn == "tie") Broadcast("AnnounceTie", winners, losers, finalScore);
            else Broadcast("AnnounceWin", winners, losers, finalScore);
        }

        private bool checkPayment(BasePlayer player)
        {
            ItemDefinition def = ItemManager.FindItemDefinition(configData.ItemSettings.joinItem);
            ulong id = player.GetUserId();
            if (def == null || paiedPlayers.ContainsKey(id)) return true;
            int totals = player.inventory.GetAmount(configData.ItemSettings.joinItem);
            if (totals >= configData.ItemSettings.joinItemAmount)
            {
                player.inventory.Take(null, configData.ItemSettings.joinItem, configData.ItemSettings.joinItemAmount);
                paiedPlayers[id] = true;
                Reply(player, "charged", configData.ItemSettings.joinItemAmount, def.shortname);
                return true;
            }
            Reply(player, "Notcharged", configData.ItemSettings.joinItemAmount, def.shortname);
            return false;
        }

        private void refundPlayer()
        {
            var ids = new List<ulong>(paiedPlayers.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                ulong key = ids[i];
                BasePlayer player = BasePlayer.FindByID(key);
                if (player == null) continue;
                giveWinItem(player, configData.ItemSettings.joinItem, configData.ItemSettings.joinItemAmount, true);
                paiedPlayers.Remove(key);
            }
        }

        private void giveWinItem(BasePlayer player, int itemID, int ItemAmount, bool refund)
        {
            var winItem = ItemManager.CreateByItemID(itemID, ItemAmount, 0);
            if (winItem == null) return;
            if (winItem.MoveToContainer(player.inventory.containerBelt, -1, true) ||
                winItem.MoveToContainer(player.inventory.containerMain, -1, true))
            {
                Reply(player, refund ? "gaveJoinRefund" : "gaveWinItem", ItemAmount, winItem.info.shortname);
                return;
            }
            winItem.Drop(player.transform.position + new Vector3(0.5f, 1f, 0), Vector3.zero);
            Reply(player, refund ? "dropedJoinRefund" : "dropedWinItem", ItemAmount, winItem.info.shortname);
        }

        public void resetRound()
        {
            Rigidbody rb = ball?.GetComponent<Rigidbody>();
            if (ball != null)
            {
                ball.transform.position = configData.eventSettings.eventCenter + Vector3.up * 5f;
                ball.transform.hasChanged = true;
                if (rb != null) { rb.velocity = Vector3.zero; rb.useGravity = false; }
                ball.SendNetworkUpdateImmediate();
            }
            SnapAllCarsHome();
            timer.Once(0.25f, SnapAllCarsHome);
        }

        private void SnapAllCarsHome()
        {
            foreach (var key in RedEventCars)
                SnapCar(key.Key, key.Value.position);
            foreach (var key in BlueEventCars)
                SnapCar(key.Key, key.Value.position);
        }

        internal void SnapCar(ulong netId, Vector3 position)
        {
            var entity = FindEntity(netId) as ModularCar;
            if (entity == null || entity.IsDestroyed) return;
            var rb = entity.rigidBody;
            bool kinematic = false;
            if (rb != null)
            {
                kinematic = rb.isKinematic;
                if (rb.IsSleeping()) rb.WakeUp();
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            Vector3 lookTarget = position + Vector3.forward;
            if (ball != null)
            {
                lookTarget = ball.transform.position;
                lookTarget.y = position.y;
            }
            Vector3 dir = lookTarget - position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            entity.transform.SetPositionAndRotation(position, rot);
            if (rb != null)
            {
                rb.position = position;
                rb.rotation = rot;
                rb.isKinematic = kinematic;
            }
            entity.transform.hasChanged = true;
            entity.InvalidateNetworkCache();
            entity.UpdateNetworkGroup();
            entity.SendNetworkUpdateImmediate();
        }

        private void spawnEntitys()
        {
            eventRunning = true;
            foreach (var current in BasePlayer.activePlayerList)
            {
                if (current != null)
                    CuiHelper.DestroyUi(current, "theUIleagueMenu");
            }

            var redGo = new GameObject("RustLeague_RedGoal");
            zoneRed = redGo.AddComponent<golePostRed>();
            zoneRed.transform.position = configData.eventSettings.RedZone;
            zoneRed.transform.rotation = Quaternion.Euler(0f, configData.eventSettings.RedZoneRotation, 0f);

            var blueGo = new GameObject("RustLeague_BlueGoal");
            zoneBlue = blueGo.AddComponent<golePostBlue>();
            zoneBlue.transform.position = configData.eventSettings.BlueZone;
            zoneBlue.transform.rotation = Quaternion.Euler(0f, configData.eventSettings.BlueZoneRotation, 0f);

            var boundsGo = new GameObject("RustLeague_Arena");
            arenaBounds = boundsGo.AddComponent<ArenaBounds>();
            arenaBounds.transform.position = configData.eventSettings.eventCenter;
            float yaw = configData.eventSettings.RedZoneRotation;
            arenaBounds.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Vector3 center = configData.eventSettings.eventCenter;
            var ballEntity = GameManager.server.CreateEntity("assets/content/vehicles/ball/ball.entity.prefab", center + Vector3.up * 2f, Quaternion.identity);
            if (ballEntity == null)
            {
                closeEvent();
                return;
            }
            ballEntity.enableSaving = false;
            ballEntity.Spawn();
            DisablePersistence(ballEntity);
            ball = ballEntity;
            eventEntitys.Add(ballEntity.net.ID.Value);
            ballMono = ballEntity.GetOrAdd<rustLeague>();

            int totalPlayers = RuningEventPlayer.Count;
            float radius = configData.eventSettings.CarSpawnRadius > 0f ? configData.eventSettings.CarSpawnRadius : 20f;
            List<Vector3> carPlacement;
            if ((soloTest || testing) && totalPlayers == 1) carPlacement = GetCircumferencePositions(center, radius, 180f, center.y);
            else if (totalPlayers >= 2 && totalPlayers < 4) carPlacement = GetCircumferencePositions(center, radius, 180f, center.y);
            else if (totalPlayers >= 4 && totalPlayers < 6) carPlacement = GetCircumferencePositions(center, radius, 90f, center.y);
            else carPlacement = GetCircumferencePositions(center, radius, 60f, center.y);

            currentPlayers.Clear();
            eventPlayerList.Clear();
            foreach (var key in RuningEventPlayer)
                eventPlayerList.Add(key.Key);

            foreach (Vector3 pos in carPlacement)
            {
                BaseModularVehicle carEntity = SpawnChasse(Quaternion.LookRotation(center - pos), configData.CarSettings.carFrame, pos, ballEntity);
                if (carEntity == null)
                {
                    closeEvent();
                    return;
                }
                carEntity.UpdateNetworkGroup();
                carEntity.SendNetworkUpdateImmediate();
                eventEntitys.Add(carEntity.net.ID.Value);
            }

            if (configData.ItemSettings.joinItemEnable) refundPlayer();
            var keep = new List<ulong>(currentPlayers);
            var drop = new List<ulong>();
            foreach (var key in RuningEventPlayer)
            {
                if (!keep.Contains(key.Key)) drop.Add(key.Key);
            }
            for (int i = 0; i < drop.Count; i++)
                RuningEventPlayer.Remove(drop[i]);
        }

        public static bool IsOdd(int numb) => numb % 2 != 0;

        public List<Vector3> GetCircumferencePositions(Vector3 center, float radius, float next, float y)
        {
            var positions = new List<Vector3>();
            float degree = 0f;
            while (degree < 360f)
            {
                float angle = (2f * Mathf.PI / 360f) * degree;
                positions.Add(new Vector3(center.x + radius * Mathf.Cos(angle), center.y, center.z + radius * Mathf.Sin(angle)));
                degree += next;
            }
            return positions;
        }

        BaseModularVehicle SpawnChasse(Quaternion rotation, string PreFab, Vector3 Vector, BaseEntity ballEntity)
        {
            ModularCar entity = GameManager.server.CreateEntity(PreFab, Vector, rotation) as ModularCar;
            if (entity == null) return null;

            entity.enableSaving = false;
            entity.Spawn();
            entity.transform.LookAt(ballEntity.transform);
            ItemContainer storageBox = entity.Inventory.ModuleContainer;
            SpawnChasseItem(configData.CarSettings.carSlot0, entity, storageBox, 0);
            SpawnChasseItem(configData.CarSettings.carSlot1, entity, storageBox, 1);
            SpawnChasseItem(configData.CarSettings.carSlot2, entity, storageBox, 2);
            SpawnChasseItem(configData.CarSettings.carSlot3, entity, storageBox, 3);

            BasePlayer player = null;
            if (eventPlayerList.Count - 1 >= carCount)
                player = BasePlayer.FindByID(eventPlayerList[carCount]);

            var setup = new carLoc { position = entity.transform.position, rotation = entity.transform.rotation };
            rustLeagueCar car = entity.GetOrAdd<rustLeagueCar>();
            car.homePosition = entity.transform.position;
            car.homeRotation = entity.transform.rotation;
            if (IsOdd(carCount))
            {
                RedEventCars[entity.net.ID.Value] = setup;
                car.team = "red";
                if (player != null)
                {
                    RedEventCars[entity.net.ID.Value].name = player.displayName;
                    RedEventCars[entity.net.ID.Value].playerID = player.GetUserId();
                    if (!currentPlayers.Contains(player.GetUserId())) currentPlayers.Add(player.GetUserId());
                    car.driver = player;
                    jounEventArena(entity, player, "red", entity.transform.position);
                    paiedPlayers.Remove(player.GetUserId());
                }
            }
            else
            {
                BlueEventCars[entity.net.ID.Value] = setup;
                car.team = "blue";
                if (player != null)
                {
                    BlueEventCars[entity.net.ID.Value].name = player.displayName;
                    BlueEventCars[entity.net.ID.Value].playerID = player.GetUserId();
                    if (!currentPlayers.Contains(player.GetUserId())) currentPlayers.Add(player.GetUserId());
                    car.driver = player;
                    jounEventArena(entity, player, "blue", entity.transform.position);
                    paiedPlayers.Remove(player.GetUserId());
                }
            }
            carCount++;
            DisablePersistence(entity);
            return entity;
        }

        internal static void DisablePersistence(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed) return;
            if (entity.enableSaving)
                entity.EnableSaving(false);
            var vehicle = entity as BaseModularVehicle;
            if (vehicle != null)
            {
                List<BaseVehicleModule> modules = vehicle.AttachedModuleEntities;
                if (modules != null)
                {
                    for (int i = 0; i < modules.Count; i++)
                        DisablePersistence(modules[i]);
                }
            }
            List<BaseEntity> kids = entity.children;
            if (kids == null || kids.Count == 0) return;
            for (int i = 0; i < kids.Count; i++)
                DisablePersistence(kids[i]);
        }

        internal static void PurgeDestroyedFromSaveList()
        {
            BaseEntity.saveList.RemoveWhere(e => e == null || e.IsDestroyed);
        }

        bool SpawnChasseItem(int PreFabID, BaseModularVehicle Chasse, ItemContainer storage, int pos)
        {
            if (PreFabID == 0) return true;
            Item CarItem = ItemManager.CreateByItemID(PreFabID, 1, 0);
            if (CarItem != null)
                Chasse.Inventory.TryAddModuleItem(CarItem, pos);
            return true;
        }

        public void jounEventArena(BaseModularVehicle car, BasePlayer player, string team, Vector3 destination)
        {
            EventPlayerLastPos[player.GetUserId()] = player.transform.position;
            player.EnsureDismounted();
            RuningEventPlayer[player.GetUserId()] = team;
            NotifyPlayerTeam(player, team);
            timer.Once(2f, () => TeleportPlayerPosition(player, destination + Vector3.forward * 2f, car));
        }

        internal string GetPlayerTeam(BasePlayer player)
        {
            if (player == null) return null;
            ulong id = player.GetUserId();
            if (RuningEventPlayer.TryGetValue(id, out var team) && !string.IsNullOrEmpty(team))
                return team;
            if (ballMono != null)
            {
                if (ballMono.redPlayer.ContainsKey(id)) return "red";
                if (ballMono.bluePlayer.ContainsKey(id)) return "blue";
            }
            return null;
        }

        internal void NotifyPlayerTeam(BasePlayer player, string team)
        {
            if (player == null || string.IsNullOrEmpty(team)) return;
            bool red = team.Equals("red", StringComparison.OrdinalIgnoreCase);
            Reply(player, red ? "YourTeamRedChat" : "YourTeamBlueChat");
            ShowTeamHud(player, red ? "red" : "blue");
        }

        internal void ShowTeamHud(BasePlayer player, string team = null)
        {
            if (player == null) return;
            if (string.IsNullOrEmpty(team))
                team = GetPlayerTeam(player);
            CuiHelper.DestroyUi(player, "TeamHudBlocknameTimer");
            if (string.IsNullOrEmpty(team)) return;
            bool red = team.Equals("red", StringComparison.OrdinalIgnoreCase);
            var elements = new CuiElementContainer();
            var panel = elements.Add(new CuiPanel
            {
                Image = { Color = red ? "0.55 0.10 0.07 0.94" : "0.05 0.22 0.55 0.94" },
                RectTransform = { AnchorMin = "0.012 0.715", AnchorMax = "0.180 0.775" }
            }, "Hud", "TeamHudBlocknameTimer");
            elements.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.04 0", AnchorMax = "0.96 1" },
                Text = { Color = "1 1 1 1", Text = Lang(red ? "YourTeamRed" : "YourTeamBlue"), FontSize = 14, Align = TextAnchor.MiddleCenter, Font = "RobotoCondensed-Bold.ttf" }
            }, panel);
            CuiHelper.AddUi(player, elements);
        }

        private void TeleportPlayerPosition(BasePlayer player, Vector3 destination, BaseModularVehicle car)
        {
            if (player == null) return;
            player.GetMounted()?.DismountPlayer(player, true);
            player.PauseFlyHackDetection(60f);
            player.PauseSpeedHackDetection(60f);
            player.SetParent(null, true);
            player.Teleport(destination);
            player.ForceUpdateTriggers();
            player.ClientRPC(RpcTarget.Player("ForceViewAnglesTo", player), Quaternion.Euler(Vector3.zero) * Vector3.forward);
            player.UpdateNetworkGroup();
            player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            player.ClientRPC(RpcTarget.Player("StartLoading_Quick", player), true);
            if (car != null) timer.Once(2f, () => StartAwake(player, car));
        }

        private void StartAwake(BasePlayer player, BaseModularVehicle car)
        {
            if (player == null || car == null) return;
            if (!player.IsSleeping())
            {
                car.ClearOwnerEntry();
                foreach (BaseVehicle.MountPointInfo mountPoint in car.allMountPoints)
                {
                    if (mountPoint?.mountable == null || !mountPoint.isDriver) continue;
                    mountPoint.mountable.MountPlayer(player);
                    player.SendNetworkUpdateImmediate();
                    ShowTeamHud(player);
                    timer.Once(2f, () => VerifyMounted(player, mountPoint.mountable, car));
                    break;
                }
                return;
            }
            timer.Once(1f, () => StartAwake(player, car));
        }

        private void VerifyMounted(BasePlayer player, BaseMountable mount, BaseModularVehicle car)
        {
            if (car != null && player != null && !car.IsDriver(player))
            {
                player.GetMounted()?.DismountPlayer(player, true);
                timer.NextTick(() =>
                {
                    if (player != null && mount != null)
                        mount.MountPlayer(player);
                });
            }
        }

        private void SpawnMapMarker()
        {
            KillMapMarker();
            if (!configData.settings.ShowMapMarker) return;
            Vector3 pos = configData.eventSettings.eventCenter;
            _mapMarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", pos) as MapMarkerGenericRadius;
            if (_mapMarker != null)
            {
                _mapMarker.enableSaving = false;
                _mapMarker.Spawn();
                _mapMarker.radius = 0.4f;
                _mapMarker.alpha = 0.7f;
                _mapMarker.color1 = new Color(0.8f, 0.1f, 0.1f);
                _mapMarker.color2 = new Color(0.1f, 0.2f, 0.8f);
                _mapMarker.SendUpdate();
            }
            _shopMarker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", pos) as VendingMachineMapMarker;
            if (_shopMarker != null)
            {
                _shopMarker.enableSaving = false;
                _shopMarker.markerShopName = "RustLeague " + GridRef(pos);
                _shopMarker.Spawn();
            }
        }

        private void KillMapMarker()
        {
            if (_mapMarker != null && !_mapMarker.IsDestroyed) _mapMarker.Kill();
            if (_shopMarker != null && !_shopMarker.IsDestroyed) _shopMarker.Kill();
            _mapMarker = null;
            _shopMarker = null;
        }

        internal bool PointInGoal(Vector3 world, bool red, float pad = 0f)
        {
            Vector3 origin = red ? configData.eventSettings.RedZone : configData.eventSettings.BlueZone;
            float yaw = red ? configData.eventSettings.RedZoneRotation : configData.eventSettings.BlueZoneRotation;
            Vector3 size = red ? configData.eventSettings.RedZoneSize : configData.eventSettings.BlueZoneSize;
            if (origin == Vector3.zero || size == Vector3.zero) return false;
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)) * (world - origin);
            Vector3 half = size * 0.5f;
            half.x += pad;
            half.y += pad;
            half.z += pad;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        public void TryNegateEventDamage(BaseEntity theBall, HitInfo hitinfo)
        {
            if (!eventOpen && !eventRunning) return;
            if (theBall == null || hitinfo == null) return;
            bool negate = false;
            rustLeagueCar hitCar = FindEventCar(theBall);
            if (theBall == ball) negate = true;
            else if (theBall is BasePlayer bp && EventPlayerLastPos.ContainsKey(bp.GetUserId())) negate = true;
            else if (hitCar != null) negate = true;
            if (hitCar != null)
            {
                rustLeagueCar shooter = FindRocketShooter(hitinfo);
                if (shooter != null && !hitCar.IsSameCar(shooter))
                {
                    Vector3 blast = hitinfo.HitPositionWorld;
                    if (blast == Vector3.zero && hitinfo.Initiator != null)
                        blast = hitinfo.Initiator.transform.position;
                    hitCar.ApplyRocketBlast(blast, shooter);
                }
            }
            if (!negate) return;
            hitinfo.damageTypes = new DamageTypeList();
            hitinfo.HitEntity = null;
            hitinfo.HitMaterial = 0;
            hitinfo.PointStart = Vector3.zero;
        }

        private static rustLeagueCar FindEventCar(BaseEntity entity)
        {
            if (entity == null) return null;
            var car = entity.GetComponent<rustLeagueCar>();
            if (car != null) return car;
            var module = entity as BaseVehicleModule;
            if (module?.Vehicle != null)
            {
                car = module.Vehicle.GetComponent<rustLeagueCar>();
                if (car != null) return car;
            }
            var modular = entity.GetComponentInParent<ModularCar>();
            return modular != null ? modular.GetComponent<rustLeagueCar>() : null;
        }

        private static rustLeagueCar FindRocketShooter(HitInfo info)
        {
            if (info == null) return null;
            BaseEntity init = info.Initiator;
            if (init != null)
            {
                var tracker = init.GetComponent<LeagueRocket>();
                if (tracker != null) return tracker.Shooter;
            }
            if (info.WeaponPrefab != null)
            {
                var tracker = info.WeaponPrefab.GetComponent<LeagueRocket>();
                if (tracker != null) return tracker.Shooter;
            }
            if (info.InitiatorPlayer != null)
            {
                var cars = Instance.LiveCars;
                for (int i = 0; i < cars.Count; i++)
                {
                    var ride = cars[i];
                    if (ride != null && ride.driver == info.InitiatorPlayer)
                        return ride;
                }
            }
            return null;
        }

        public bool TryBlockDismount(BasePlayer player, BaseMountable entity)
        {
            if (!eventRunning || player == null) return false;
            if (!EventPlayerLastPos.ContainsKey(player.GetUserId())) return false;
            var ride = entity.VehicleParent()?.GetComponent<ModularCar>()?.GetComponent<rustLeagueCar>();
            if (ride == null) return false;
            ride.FireRocket();
            return true;
        }

        public bool TryHandleDismountFailed(BasePlayer player, BaseMountable entity)
        {
            if (!eventRunning || player == null) return false;
            if (!EventPlayerLastPos.ContainsKey(player.GetUserId())) return false;
            var ride = entity.VehicleParent()?.GetComponent<ModularCar>()?.GetComponent<rustLeagueCar>();
            if (ride == null) return false;
            ride.FireRocket();
            return true;
        }

        public bool TryBlockSeatSwap(BasePlayer player, ModularCarSeat carSeat)
        {
            if (!eventRunning || player == null) return false;
            if (!EventPlayerLastPos.ContainsKey(player.GetUserId())) return false;
            var ride = carSeat.associatedSeatingModule?.Vehicle?.GetComponent<BaseModularVehicle>()?.GetComponent<rustLeagueCar>();
            if (ride == null) return false;
            if (ride.car != null && !ride.car.IsDriver(player))
                return false;
            ride.flipOver();
            return true;
        }

        public void TryKeepEventOxygen(PlayerMetabolism metabolism, BaseCombatEntity ownerEntity)
        {
            BasePlayer player = ownerEntity as BasePlayer;
            if (player == null || !EventPlayerLastPos.ContainsKey(player.GetUserId()))
                return;
            if (metabolism.oxygen.value < 1f)
                metabolism.oxygen.value = 1f;
        }

        public void RunEffect(BasePlayer player, Vector3 position, string prefab)
        {
            var effect = new Effect();
            effect.Init(Effect.Type.Generic, position, Vector3.zero);
            effect.pooledString = prefab;
            if (player != null && player.net?.connection != null)
                EffectNetwork.Send(effect, player.net.connection);
            else
                EffectNetwork.Send(effect);
        }

        private Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Quaternion rotation) => rotation * (point - pivot) + pivot;

        private void drawZones(BasePlayer player, Vector3 center, float rot, Vector3 size)
        {
            if (center == Vector3.zero || player == null) return;
            float time = 60f;
            Quaternion rotation = Quaternion.Euler(0f, rot, 0f);
            Vector3 point1 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y + size.y, center.z + size.z), center, rotation);
            Vector3 point2 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y - size.y, center.z + size.z), center, rotation);
            Vector3 point3 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y + size.y, center.z - size.z), center, rotation);
            Vector3 point4 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y - size.y, center.z - size.z), center, rotation);
            Vector3 point5 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y + size.y, center.z + size.z), center, rotation);
            Vector3 point6 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y - size.y, center.z + size.z), center, rotation);
            Vector3 point7 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y + size.y, center.z - size.z), center, rotation);
            Vector3 point8 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y - size.y, center.z - size.z), center, rotation);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point1, point2);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point1, point3);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point1, point5);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point4, point2);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point4, point3);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point4, point8);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point5, point6);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point5, point7);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point6, point2);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point8, point6);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point8, point7);
            player.SendConsoleCommand("ddraw.line", time, Color.blue, point7, point3);
        }
    }
}
