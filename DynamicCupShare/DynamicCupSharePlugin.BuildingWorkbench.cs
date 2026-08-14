using System.Collections;
using System.Collections.Generic;
using Facepunch;
using UnityEngine;

namespace DynamicCupShareHarmony
{
    public class BuildingWorkbenchTrigger : TriggerBase
    {
        public override void OnEntityLeave(BaseEntity ent)
        {
            base.OnEntityLeave(ent);
            if (ent is not BasePlayer player || player.IsNpc)
                return;

            Interface.NextTick(() =>
            {
                if (player && this)
                    player.EnterTrigger(this);
            });
        }
    }

    public class BuildingWorkbenchTicker : FacepunchBehaviour
    {
        public void Tick()
        {
            DynamicCupSharePlugin.Instance?.StartBuildingWorkbenchUpdate();
        }
    }

    public partial class DynamicCupSharePlugin
    {
        private GameObject _buildingWorkbenchGo;
        private BuildingWorkbenchTicker _buildingWorkbenchTicker;
        private BuildingWorkbenchTrigger _buildingWorkbenchTrigger;
        private PhysicsScene _buildingWorkbenchPhysics;
        private float _buildingWorkbenchScanRange;
        private float _buildingWorkbenchHalfScanRange;
        private readonly Dictionary<ulong, BuildingWorkbenchPlayerState> _buildingWorkbenchPlayers = new Dictionary<ulong, BuildingWorkbenchPlayerState>();
        private readonly Dictionary<uint, BuildingWorkbenchBuildingState> _buildingWorkbenchBuildings = new Dictionary<uint, BuildingWorkbenchBuildingState>();
        private readonly List<ulong> _buildingWorkbenchNotified = new List<ulong>();
        private readonly RaycastHit[] _buildingWorkbenchHits = new RaycastHit[256];
        private readonly List<uint> _buildingWorkbenchProcessed = new List<uint>();

        private sealed class BuildingWorkbenchPlayerState
        {
            public Vector3 Position;
            public readonly Dictionary<uint, BuildingWorkbenchBuildingState> Buildings = new Dictionary<uint, BuildingWorkbenchBuildingState>();
            public byte WorkbenchLevel;
        }

        internal sealed class BuildingWorkbenchBuildingState
        {
            public readonly uint BuildingId;
            public Workbench BestWorkbench;
            public readonly List<BasePlayer> Players = new List<BasePlayer>();
            public readonly List<Workbench> Workbenches = new List<Workbench>();

            public BuildingWorkbenchBuildingState(uint buildingId)
            {
                BuildingId = buildingId;
                BuildingManager.Building building = BuildingManager.server?.GetBuilding(buildingId);
                if (building?.decayEntities != null)
                {
                    foreach (DecayEntity decay in building.decayEntities)
                    {
                        if (decay is Workbench bench)
                            Workbenches.Add(bench);
                    }
                }
                UpdateBestBench();
            }

            public void EnterBuilding(BasePlayer player)
            {
                if (player != null && !Players.Contains(player))
                    Players.Add(player);
            }

            public void LeaveBuilding(BasePlayer player)
            {
                Players.Remove(player);
            }

            public void OnBenchBuilt(Workbench workbench)
            {
                if (workbench != null && !Workbenches.Contains(workbench))
                    Workbenches.Add(workbench);
                UpdateBestBench();
            }

            public void OnBenchKilled(Workbench workbench)
            {
                Workbenches.Remove(workbench);
                UpdateBestBench();
            }

            public byte GetWorkbenchLevel()
            {
                return BestWorkbench ? (byte)BestWorkbench.Workbenchlevel : (byte)0;
            }

            public void UpdateBestBench()
            {
                BestWorkbench = null;
                for (int i = 0; i < Workbenches.Count; i++)
                {
                    Workbench workbench = Workbenches[i];
                    if (!workbench) continue;
                    if (!BestWorkbench || BestWorkbench.Workbenchlevel < workbench.Workbenchlevel)
                        BestWorkbench = workbench;
                }
            }
        }

        internal bool BuildingWorkbenchFeatureEnabled
            => Configuration?.BuildingWorkbench != null && Configuration.BuildingWorkbench.Enabled;

        internal bool CanUseBuildingWorkbench(BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || player == null || player.IsNpc)
                return false;

            string perm = Configuration.Permission?.BuildingWorkbenchUse;
            if (!string.IsNullOrEmpty(perm) && !player.HasPermission(perm))
                return false;

            StoredData.PlayerData data = storedData?.SetupPlayer(player.GetUserId());
            return data == null || data.BuildingWorkbenchEnabled;
        }

        internal void StartBuildingWorkbench()
        {
            if (!BuildingWorkbenchFeatureEnabled)
                return;

            if (Configuration.BuildingWorkbench.BaseDistance < 3f)
            {
                Configuration.BuildingWorkbench.BaseDistance = 3f;
                Debug.LogWarning("[DynamicCupShare] Building workbench distance cannot be less than 3 meters.");
            }

            _buildingWorkbenchPhysics = Physics.defaultPhysicsScene;
            _buildingWorkbenchScanRange = Configuration.BuildingWorkbench.BaseDistance;
            _buildingWorkbenchHalfScanRange = _buildingWorkbenchScanRange / 2f;

            if (_buildingWorkbenchGo == null)
            {
                _buildingWorkbenchGo = new GameObject("DynamicCupShare_BuildingWorkbench");
                Object.DontDestroyOnLoad(_buildingWorkbenchGo);
                _buildingWorkbenchTicker = _buildingWorkbenchGo.AddComponent<BuildingWorkbenchTicker>();
                _buildingWorkbenchTrigger = _buildingWorkbenchGo.AddComponent<BuildingWorkbenchTrigger>();
            }

            float rate = Configuration.BuildingWorkbench.UpdateRate;
            if (rate < 0.25f)
                rate = 0.25f;

            _buildingWorkbenchTicker.CancelInvoke(nameof(BuildingWorkbenchTicker.Tick));
            _buildingWorkbenchTicker.InvokeRepeating(nameof(BuildingWorkbenchTicker.Tick), 1f, rate);
        }

        internal void StopBuildingWorkbench()
        {
            if (_buildingWorkbenchTicker != null)
            {
                _buildingWorkbenchTicker.CancelInvoke(nameof(BuildingWorkbenchTicker.Tick));
                _buildingWorkbenchTicker.StopAllCoroutines();
            }

            if (_buildingWorkbenchGo != null)
            {
                Object.Destroy(_buildingWorkbenchGo);
                _buildingWorkbenchGo = null;
            }

            _buildingWorkbenchTicker = null;
            _buildingWorkbenchTrigger = null;
            _buildingWorkbenchPlayers.Clear();
            _buildingWorkbenchBuildings.Clear();
            _buildingWorkbenchNotified.Clear();
        }

        internal void StartBuildingWorkbenchUpdate()
        {
            if (!BuildingWorkbenchFeatureEnabled || _buildingWorkbenchTicker == null)
                return;
            if (BasePlayer.activePlayerList == null || BasePlayer.activePlayerList.Count == 0)
                return;
            _buildingWorkbenchTicker.StartCoroutine(HandleBuildingWorkbenchUpdate());
        }

        private IEnumerator HandleBuildingWorkbenchUpdate()
        {
            float frameWait = 0;
            int count = BasePlayer.activePlayerList.Count;
            for (int i = 0; i < count; i++)
            {
                if (i >= BasePlayer.activePlayerList.Count)
                    break;

                BasePlayer player = BasePlayer.activePlayerList[i];
                if (!player) continue;

                if (!CanUseBuildingWorkbench(player))
                {
                    if (player.nextCheckTime == float.MaxValue)
                    {
                        player.nextCheckTime = 0;
                        player.cachedCraftLevel = 0;
                    }
                    continue;
                }

                BuildingWorkbenchPlayerState data = GetBuildingWorkbenchPlayerState(player.GetUserId());
                if (Vector3.Distance(player.transform.position, data.Position) < Configuration.BuildingWorkbench.RequiredDistance)
                    continue;

                if (player.triggers == null && _buildingWorkbenchTrigger)
                    player.EnterTrigger(_buildingWorkbenchTrigger);

                data.Position = player.transform.position;
                UpdatePlayerBuildings(player, data);
                UpdatePlayerWorkbenchLevel(player);

                float waitForFrames = Performance.report.frameRate * Configuration.BuildingWorkbench.UpdateRate / BasePlayer.activePlayerList.Count * 0.9f;
                if (waitForFrames >= 1)
                {
                    yield return null;
                    continue;
                }

                frameWait += waitForFrames;
                if (frameWait >= 1)
                {
                    frameWait -= 1f;
                    yield return null;
                }
            }
        }

        internal void OnBuildingWorkbenchPlayerConnected(BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || player == null || player.IsNpc)
                return;

            player.nextCheckTime = float.MaxValue;
            if (_buildingWorkbenchTrigger)
                player.EnterTrigger(_buildingWorkbenchTrigger);
        }

        internal void OnBuildingWorkbenchPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;

            player.nextCheckTime = 0;
            player.cachedCraftLevel = 0;

            ulong playerId = player.GetUserId();
            if (_buildingWorkbenchPlayers.Remove(playerId, out BuildingWorkbenchPlayerState playerData))
            {
                foreach (BuildingWorkbenchBuildingState data in playerData.Buildings.Values)
                    data.LeaveBuilding(player);
            }

            if (_buildingWorkbenchTrigger)
                player.LeaveTrigger(_buildingWorkbenchTrigger);
        }

        internal void OnBuildingWorkbenchSpawned(Workbench bench)
        {
            if (!BuildingWorkbenchFeatureEnabled || !bench) return;

            Interface.NextTick(() =>
            {
                if (!bench) return;
                BuildingWorkbenchBuildingState data = GetBuildingWorkbenchBuilding(bench.buildingID);
                data.OnBenchBuilt(bench);
                UpdateBuildingPlayers(data);

                if (!Configuration.BuildingWorkbench.BuiltNotification)
                    return;

                BasePlayer player = BasePlayer.FindByID(bench.OwnerID);
                if (!player || !CanUseBuildingWorkbench(player))
                    return;

                ulong playerId = player.GetUserId();
                if (_buildingWorkbenchNotified.Contains(playerId))
                    return;

                _buildingWorkbenchNotified.Add(playerId);
                Message(player, "BW.Notification");
            });
        }

        internal void OnBuildingWorkbenchKilled(Workbench bench)
        {
            if (!BuildingWorkbenchFeatureEnabled || !bench) return;
            BuildingWorkbenchBuildingState data = GetBuildingWorkbenchBuilding(bench.buildingID);
            data.OnBenchKilled(bench);
            UpdateBuildingPlayers(data);
        }

        internal void OnBuildingWorkbenchCupboardAuthorized(uint buildingId, BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || player == null) return;
            OnPlayerEnterBuilding(player, buildingId);
            UpdatePlayerWorkbenchLevel(player);
        }

        internal void OnBuildingWorkbenchCupboardDeauthorized(uint buildingId, BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || player == null) return;
            OnPlayerLeftBuilding(player, buildingId);
            UpdatePlayerWorkbenchLevel(player);
        }

        internal void OnBuildingWorkbenchCupboardCleared(uint buildingId)
        {
            if (!BuildingWorkbenchFeatureEnabled) return;
            BuildingWorkbenchBuildingState data = GetBuildingWorkbenchBuilding(buildingId);
            for (int i = data.Players.Count - 1; i >= 0; i--)
            {
                BasePlayer player = data.Players[i];
                OnPlayerLeftBuilding(player, buildingId);
                UpdatePlayerWorkbenchLevel(player);
            }
        }

        internal void OnBuildingWorkbenchBoatAuthorized(PlayerBoatPrivilege privilege, BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || privilege == null || player == null) return;
            if (privilege.ParentVehicle is not PlayerBoat boat) return;
            BuildingWorkbenchBuildingState data = GetBuildingWorkbenchBuilding(boat);
            if (data == null) return;
            OnPlayerEnterBuilding(player, data.BuildingId);
            UpdatePlayerWorkbenchLevel(player);
        }

        internal void OnBuildingWorkbenchBoatDeauthorized(PlayerBoatPrivilege privilege, BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || privilege == null || player == null) return;
            if (privilege.ParentVehicle is not PlayerBoat boat) return;
            BuildingWorkbenchBuildingState data = GetBuildingWorkbenchBuilding(boat);
            if (data == null) return;
            OnPlayerLeftBuilding(player, data.BuildingId);
            UpdatePlayerWorkbenchLevel(player);
        }

        internal void OnBuildingWorkbenchBoatCleared(PlayerBoatPrivilege privilege)
        {
            if (!BuildingWorkbenchFeatureEnabled || privilege == null) return;
            if (privilege.ParentVehicle is not PlayerBoat boat) return;
            BuildingWorkbenchBuildingState data = GetBuildingWorkbenchBuilding(boat);
            if (data == null) return;
            for (int i = data.Players.Count - 1; i >= 0; i--)
            {
                BasePlayer player = data.Players[i];
                OnPlayerLeftBuilding(player, data.BuildingId);
                UpdatePlayerWorkbenchLevel(player);
            }
        }

        internal void OnWorkbenchTriggerChanged(BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || player == null || player.IsNpc)
                return;
            UpdatePlayerWorkbenchLevel(player);
        }

        internal void ToggleBuildingWorkbench(BasePlayer player)
        {
            if (!BuildingWorkbenchFeatureEnabled || player == null)
                return;

            string perm = Configuration.Permission?.BuildingWorkbenchUse;
            if (!string.IsNullOrEmpty(perm) && !player.HasPermission(perm))
            {
                Message(player, "Error.NoPermissions");
                return;
            }

            StoredData.PlayerData data = storedData.SetupPlayer(player.GetUserId());
            if (data == null) return;

            bool next = !data.BuildingWorkbenchEnabled;
            data.buildingWorkbenchEnabled = next;
            Message(player, next ? "BW.ToggleOn" : "BW.ToggleOff");

            if (next)
                OnBuildingWorkbenchPlayerConnected(player);
            else
            {
                OnBuildingWorkbenchPlayerDisconnected(player);
                player.nextCheckTime = 0;
                player.cachedCraftLevel = 0;
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, false);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench2, false);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, false);
                player.SendNetworkUpdateImmediate();
            }
        }

        private void UpdatePlayerBuildings(BasePlayer player, BuildingWorkbenchPlayerState data)
        {
            List<uint> currentBuildings = Facepunch.Pool.Get<List<uint>>();

            if (Configuration.BuildingWorkbench.FastBuildingCheck)
                GetNearbyAuthorizedBuildingsFast(player, currentBuildings);
            else
                GetNearbyAuthorizedBuildings(player, currentBuildings);

            List<uint> leftBuildings = Facepunch.Pool.Get<List<uint>>();
            foreach (uint buildingId in data.Buildings.Keys)
            {
                if (!currentBuildings.Contains(buildingId))
                    leftBuildings.Add(buildingId);
            }

            for (int i = 0; i < leftBuildings.Count; i++)
                OnPlayerLeftBuilding(player, leftBuildings[i]);

            for (int i = 0; i < currentBuildings.Count; i++)
            {
                uint currentBuilding = currentBuildings[i];
                if (!data.Buildings.ContainsKey(currentBuilding))
                    OnPlayerEnterBuilding(player, currentBuilding);
            }

            Facepunch.Pool.FreeUnmanaged(ref currentBuildings);
            Facepunch.Pool.FreeUnmanaged(ref leftBuildings);
        }

        private void OnPlayerEnterBuilding(BasePlayer player, uint buildingId)
        {
            BuildingWorkbenchBuildingState building = GetBuildingWorkbenchBuilding(buildingId);
            building.EnterBuilding(player);
            GetBuildingWorkbenchPlayerState(player.GetUserId()).Buildings[buildingId] = building;
        }

        private void OnPlayerLeftBuilding(BasePlayer player, uint buildingId)
        {
            if (player == null) return;

            BuildingWorkbenchBuildingState building = GetBuildingWorkbenchBuilding(buildingId);
            building.LeaveBuilding(player);

            BuildingWorkbenchPlayerState playerState = GetBuildingWorkbenchPlayerState(player.GetUserId());
            if (!playerState.Buildings.Remove(buildingId))
                return;

            if (player.inventory?.crafting == null || player.inventory.crafting.queue.Count == 0)
                return;

            string cancelPerm = Configuration.Permission?.BuildingWorkbenchCancelCraft;
            if (!string.IsNullOrEmpty(cancelPerm) && !player.HasPermission(cancelPerm))
                return;

            bool canceled = false;
            List<int> toCancel = Facepunch.Pool.Get<List<int>>();
            foreach (ItemCraftTask task in player.inventory.crafting.queue)
            {
                if (task?.blueprint != null && player.cachedCraftLevel < task.blueprint.workbenchLevelRequired)
                    toCancel.Add(task.taskUID);
            }

            for (int i = 0; i < toCancel.Count; i++)
            {
                player.inventory.crafting.CancelTask(toCancel[i]);
                canceled = true;
            }

            Facepunch.Pool.FreeUnmanaged(ref toCancel);

            if (canceled && Configuration.BuildingWorkbench.CancelCraftNotification)
                Message(player, "BW.CraftCanceled");
        }

        private void UpdateBuildingPlayers(BuildingWorkbenchBuildingState building)
        {
            for (int i = 0; i < building.Players.Count; i++)
                UpdatePlayerWorkbenchLevel(building.Players[i]);
        }

        private void UpdatePlayerWorkbenchLevel(BasePlayer player)
        {
            if (!player) return;

            byte level = 0;
            Workbench workbench = null;

            BuildingWorkbenchPlayerState playerData = GetBuildingWorkbenchPlayerState(player.GetUserId());
            foreach (BuildingWorkbenchBuildingState building in playerData.Buildings.Values)
            {
                byte buildingLevel = building.GetWorkbenchLevel();
                if (buildingLevel > level)
                {
                    level = buildingLevel;
                    workbench = building.BestWorkbench;
                }
            }

            if (level != 3 && player.triggers != null)
            {
                for (int i = 0; i < player.triggers.Count; i++)
                {
                    if (player.triggers[i] is not TriggerWorkbench trigger || !trigger.parentBench)
                        continue;

                    byte workbenchLevel = (byte)trigger.parentBench.Workbenchlevel;
                    if (workbenchLevel > level)
                    {
                        level = workbenchLevel;
                        workbench = trigger.parentBench;
                    }
                }
            }

            if ((byte)player.cachedCraftLevel == level && playerData.WorkbenchLevel == level && player._cachedWorkbench == workbench)
                return;

            player.nextCheckTime = float.MaxValue;
            player.cachedCraftLevel = level;
            player._cachedWorkbench = workbench;
            playerData.WorkbenchLevel = level;
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, level == 1);
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench2, level == 2);
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, level == 3);
            player.SendNetworkUpdateImmediate();
            player.SendActiveWorkbenchIfChanged();
        }

        private BuildingWorkbenchPlayerState GetBuildingWorkbenchPlayerState(ulong playerId)
        {
            if (!_buildingWorkbenchPlayers.TryGetValue(playerId, out BuildingWorkbenchPlayerState data))
                _buildingWorkbenchPlayers[playerId] = data = new BuildingWorkbenchPlayerState();
            return data;
        }

        private BuildingWorkbenchBuildingState GetBuildingWorkbenchBuilding(uint buildingId)
        {
            if (!_buildingWorkbenchBuildings.TryGetValue(buildingId, out BuildingWorkbenchBuildingState data))
                _buildingWorkbenchBuildings[buildingId] = data = new BuildingWorkbenchBuildingState(buildingId);
            return data;
        }

        private BuildingWorkbenchBuildingState GetBuildingWorkbenchBuilding(PlayerBoat boat)
        {
            if (!boat || boat.BoatBuildingBlocks == null || boat.BoatBuildingBlocks.Cached == null || boat.BoatBuildingBlocks.Cached.Count == 0)
                return null;
            return GetBuildingWorkbenchBuilding(boat.BoatBuildingBlocks.Cached[0].buildingID);
        }

        private void GetNearbyAuthorizedBuildingsFast(BasePlayer player, List<uint> authorizedPrivs)
        {
            OBB obb = player.WorldSpaceBounds();
            float baseDistance = _buildingWorkbenchScanRange;
            int amount = _buildingWorkbenchPhysics.Raycast(
                player.transform.position + Vector3.down * _buildingWorkbenchHalfScanRange,
                Vector3.up,
                _buildingWorkbenchHits,
                baseDistance,
                Rust.Layers.Construction,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < amount; index++)
            {
                BuildingBlock block = _buildingWorkbenchHits[index].GetEntity() as BuildingBlock;
                if (!block) continue;
                if (_buildingWorkbenchProcessed.Contains(block.buildingID) || obb.Distance(block.WorldSpaceBounds()) > baseDistance)
                    continue;

                _buildingWorkbenchProcessed.Add(block.buildingID);
                BuildingPrivlidge priv = block.GetBuilding()?.GetDominatingBuildingPrivilege();
                if (!priv || !priv.IsAuthed(player))
                    continue;

                authorizedPrivs.Add(priv.buildingID);
            }

            _buildingWorkbenchProcessed.Clear();
        }

        private void GetNearbyAuthorizedBuildings(BasePlayer player, List<uint> authorizedPrivs)
        {
            OBB obb = player.WorldSpaceBounds();
            float baseDistance = Configuration.BuildingWorkbench.BaseDistance;
            int amount = _buildingWorkbenchPhysics.OverlapSphere(
                obb.position,
                baseDistance + obb.extents.magnitude,
                Vis.colBuffer,
                Rust.Layers.Construction | Rust.Layers.VehiclesLarge,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < amount; index++)
            {
                Collider collider = Vis.colBuffer[index];
                BuildingBlock block = collider ? collider.ToBaseEntity() as BuildingBlock : null;
                if (!block) continue;
                if (_buildingWorkbenchProcessed.Contains(block.buildingID) || obb.Distance(block.WorldSpaceBounds()) > baseDistance)
                    continue;

                _buildingWorkbenchProcessed.Add(block.buildingID);

                if (block is BoatBuildingBlock boatBlock)
                {
                    if (!Configuration.BuildingWorkbench.EnableBoatCheck)
                        continue;

                    PlayerBoat boat = PlayerBoat.GetParentPlayerBoat(boatBlock);
                    if (boat && boat.IsAuthedForBuilding(player))
                        authorizedPrivs.Add(boatBlock.buildingID);
                }
                else
                {
                    BuildingPrivlidge priv = block.GetBuilding()?.GetDominatingBuildingPrivilege();
                    if (priv && priv.IsAuthed(player))
                        authorizedPrivs.Add(priv.buildingID);
                }
            }

            _buildingWorkbenchProcessed.Clear();
        }
    }
}
