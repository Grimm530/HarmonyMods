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
            timer.Once(5f, () =>
            {
                foreach (var key in RedEventCars)
                    SnapCar(key.Key, key.Value.position);
                foreach (var key in BlueEventCars)
                    SnapCar(key.Key, key.Value.position);
            });
        }

        private void SnapCar(ulong netId, Vector3 position)
        {
            var entity = FindEntity(netId);
            if (entity == null || ball == null) return;
            entity.transform.position = position;
            entity.transform.LookAt(ball.transform);
            entity.SendNetworkUpdate();
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
            var ballEntity = GameManager.server.CreateEntity("assets/content/vehicles/ball/ball.entity.prefab", center, Quaternion.identity);
            if (ballEntity == null)
            {
                closeEvent();
                return;
            }
            ballEntity.enableSaving = false;
            ballEntity.Spawn();
            ball = ballEntity;
            eventEntitys.Add(ballEntity.net.ID.Value);
            ballMono = ballEntity.GetOrAdd<rustLeague>();

            int totalPlayers = RuningEventPlayer.Count;
            float radius = configData.eventSettings.CarSpawnRadius > 0f ? configData.eventSettings.CarSpawnRadius : 20f;
            List<Vector3> carPlacement;
            if (testing && totalPlayers == 1) carPlacement = GetCircumferencePositions(center, radius, 180f, center.y);
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
                carEntity.SendNetworkUpdate();
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

            entity.Spawn();
            entity.enableSaving = false;
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
            return entity;
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
            timer.Once(2f, () => TeleportPlayerPosition(player, destination + Vector3.forward * 2f, car));
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

        public void TryNegateEventDamage(BaseEntity theBall, HitInfo hitinfo)
        {
            if (!eventOpen && !eventRunning) return;
            if (theBall == null || hitinfo == null) return;
            bool negate = false;
            if (theBall == ball) negate = true;
            else if (theBall is BasePlayer bp && EventPlayerLastPos.ContainsKey(bp.GetUserId())) negate = true;
            else if (theBall.GetComponent<rustLeagueCar>() != null) negate = true;
            else if (theBall is BaseVehicleModule module && module.Vehicle != null && module.Vehicle.GetComponent<rustLeagueCar>() != null)
                negate = true;
            if (!negate) return;
            hitinfo.damageTypes = new DamageTypeList();
            hitinfo.HitEntity = null;
            hitinfo.HitMaterial = 0;
            hitinfo.PointStart = Vector3.zero;
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
