using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TeleportGUI
{
    public partial class TeleportGUIMod
    {
        private const string FoundationShortname = "foundation";
        private const string FoundationTriangleShortname = "foundation.triangle";
        private const string FloorShortname = "floor";
        private const string FloorTriangleShortname = "floor.triangle";
        private const int InsideEntityLayers = 2097408; // Construction | Deployed
        private const int FoundationLayerMask = 1 << 21;

        private static readonly RaycastHit[] RayBuffer = new RaycastHit[64];
        private static readonly Collider[] InsideEntityResults = new Collider[64];
        private static readonly List<MonumentEntry> Monuments = new List<MonumentEntry>();
        private static readonly List<MonumentEntry> OilRigs = new List<MonumentEntry>();
        private static bool MonumentsDiscovered;

        private static readonly Dictionary<string, Bounds> MonumentBoundsOverrides = new Dictionary<string, Bounds>(StringComparer.OrdinalIgnoreCase)
        {
            ["airfield_1"] = new Bounds(new Vector3(0f, 0f, -25f), new Vector3(350f, 200f, 300f)),
            ["launch_site_1"] = new Bounds(Vector3.forward * -25f, new Vector3(600f, 200f, 350f)),
            ["trainyard_1"] = new Bounds(Vector3.zero, Vector3.one * 250f),
            ["water_treatment_plant_1"] = new Bounds(Vector3.forward * -50f, new Vector3(300f, 200f, 300f)),
            ["radtown_small_3"] = new Bounds(Vector3.forward * -25f, Vector3.one * 175f),
            ["harbor_2"] = new Bounds(Vector3.zero, new Vector3(250f, 200f, 300f)),
            ["train_tunnel_double_entrance"] = new Bounds(Vector3.zero, new Vector3(100f, 50f, 100f))
        };

        private int _topologyMaskCache = int.MaxValue;

        private enum InvalidBagReason
        {
            None,
            IsPublic,
            NotAssigned
        }

        #region Public condition entry points

        private bool MeetsPlayerConditions(BasePlayer player, BasePlayer target)
        {
            if (player == null || target == null)
                return false;

            TeleportGUIConfig.TeleportConditions c = EnsureConditions();
            if (player.IsWounded())
            {
                SendMessage(player, "You can not teleport whilst wounded");
                return false;
            }

            if (!CheckBleeding(c.WhilstBleeding, player, target)) return false;
            if (!CheckCrafting(c.WhenCrafting, player, target)) return false;
            if (!CheckMounted(c.Mounted, player, target)) return false;
            if (!CheckBuildingBlocked(c.BuildingBlocked, player, target)) return false;
            if (!CheckRaidBlocked(c.RaidBlocked, player, target)) return false;
            if (!CheckParented<CargoShip>(c.CargoShip, player, target,
                    "You can not teleport whilst on the cargo ship",
                    "You can not teleport when the target is on the cargo ship")) return false;
            if (!CheckParented<Tugboat>(c.TugBoat, player, target,
                    "You can not teleport whilst on a tug boat",
                    "You can not teleport when the target is on a tug boat")) return false;
            if (!CheckParented<HotAirBalloon>(c.HotAirBalloon, player, target,
                    "You can not teleport whilst in a hot air balloon",
                    "You can not teleport when the target is in a hot air balloon")) return false;
            if (!CheckOilRig(c.OilRig, player, target.transform.position, checkTargetPlayer: true, checkPosition: false)) return false;
            if (!CheckUnderwaterLabs(c.UnderwaterLabs, player, target.transform.position, checkTargetPlayer: true, checkPosition: false)) return false;
            if (!CheckTrainTunnels(c.TrainTunnels, player, target.transform.position, checkTargetPlayer: true, checkPosition: false)) return false;
            if (!CheckInWater(c.InWater, player, target)) return false;
            if (!CheckOnWater(c.OnWater, player, target)) return false;
            if (!CheckNoTpZone(c.NoTpZone, player, target)) return false;
            if (!CheckSafeZone(c.SafeZone, player, target)) return false;
            if (!(c.Hostile?.OnlyWarps ?? false) && !CheckHostile(c.Hostile, player, target)) return false;
            if (!CheckMonument(c.InMonument, player, target.transform.position, checkTargetPlayer: true, checkPosition: false)) return false;
            if (!CheckTopology(c.Topology, player, target.transform.position, checkTargetPlayer: true, checkPosition: false)) return false;

            return true;
        }

        private bool MeetsPositionConditions(BasePlayer player, Vector3 target, bool isWarp)
        {
            if (player == null)
                return false;

            TeleportGUIConfig.TeleportConditions c = EnsureConditions();
            if (player.IsWounded())
            {
                SendMessage(player, "You can not teleport whilst wounded");
                return false;
            }

            if (!CheckBleeding(c.WhilstBleeding, player, null)) return false;
            if (!CheckCrafting(c.WhenCrafting, player, null)) return false;
            if (!CheckMounted(c.Mounted, player, null)) return false;

            if (isWarp)
            {
                if (!CheckBuildingBlockedSelf(c.BuildingBlocked, player)) return false;
                if (!CheckRaidBlocked(c.RaidBlocked, player, null)) return false;
                if (!CheckParentedSelf<CargoShip>(c.CargoShip, player, "You can not teleport whilst on the cargo ship")) return false;
                if (!CheckParentedSelf<Tugboat>(c.TugBoat, player, "You can not teleport whilst on a tug boat")) return false;
                if (!CheckParentedSelf<HotAirBalloon>(c.HotAirBalloon, player, "You can not teleport whilst in a hot air balloon")) return false;
                if (!CheckOilRig(c.OilRig, player, target, checkTargetPlayer: false, checkPosition: false)) return false;
                if (!CheckUnderwaterLabs(c.UnderwaterLabs, player, target, checkTargetPlayer: false, checkPosition: false)) return false;
                if (!CheckTrainTunnels(c.TrainTunnels, player, target, checkTargetPlayer: false, checkPosition: false)) return false;
                if (!CheckInWater(c.InWater, player, null)) return false;
                if (!CheckOnWater(c.OnWater, player, null)) return false;
                if (!CheckSafeZone(c.SafeZone, player, null)) return false;
                if (!CheckNoTpZone(c.NoTpZone, player, null)) return false;
                if (!CheckTopology(c.Topology, player, target, checkTargetPlayer: false, checkPosition: false)) return false;
                if (!CheckHostile(c.Hostile, player, null)) return false;
                if (!CheckMonument(c.InMonument, player, target, checkTargetPlayer: false, checkPosition: false)) return false;
            }
            else
            {
                if (!CheckBuildingBlocked(c.BuildingBlocked, player, target)) return false;
                if (!CheckRaidBlocked(c.RaidBlocked, player, null)) return false;
                if (!CheckParentedSelf<CargoShip>(c.CargoShip, player, "You can not teleport whilst on the cargo ship")) return false;
                if (!CheckParentedSelf<Tugboat>(c.TugBoat, player, "You can not teleport whilst on a tug boat")) return false;
                if (!CheckParentedSelf<HotAirBalloon>(c.HotAirBalloon, player, "You can not teleport whilst in a hot air balloon")) return false;
                if (!CheckOilRig(c.OilRig, player, target, checkTargetPlayer: false, checkPosition: true)) return false;
                if (!CheckUnderwaterLabs(c.UnderwaterLabs, player, target, checkTargetPlayer: false, checkPosition: true)) return false;
                if (!CheckTrainTunnels(c.TrainTunnels, player, target, checkTargetPlayer: false, checkPosition: true)) return false;
                if (!CheckInWater(c.InWater, player, null)) return false;
                if (!CheckOnWater(c.OnWater, player, null)) return false;
                if (!CheckNoTpZone(c.NoTpZone, player, null)) return false;
                if (!CheckSafeZone(c.SafeZone, player, null)) return false;
                if (!(c.Hostile?.OnlyWarps ?? false) && !CheckHostile(c.Hostile, player, null)) return false;
                if (!CheckMonument(c.InMonument, player, target, checkTargetPlayer: false, checkPosition: true)) return false;
                if (!CheckTopology(c.Topology, player, target, checkTargetPlayer: false, checkPosition: true)) return false;
            }

            return true;
        }

        private bool CanSetHomeAtCurrentPosition(BasePlayer player, bool isOnTugBoat, TeleportGUIData.UserData user, out string error)
        {
            error = null;
            if (player == null)
            {
                error = "Invalid player";
                return false;
            }

            TeleportGUIConfig.HomeOptions home = _config?.Home ?? new TeleportGUIConfig.HomeOptions();

            if (isOnTugBoat && !home.AllowSetHomeOnTugboat)
            {
                error = "You can not set home on a tugboat";
                return false;
            }

            if (!home.AllowSetHomeInBuildBlocked && !player.CanBuild())
            {
                error = "You can not set home in a building blocked area";
                return false;
            }

            if (!isOnTugBoat && home.RequirePrivilegeSetHome)
            {
                BuildingPrivlidge privilege = player.GetBuildingPrivilege();
                if (privilege == null || !privilege.IsAuthed(player))
                {
                    error = "You must be authed on a tool cupboard to set a home";
                    return false;
                }
            }

            int maxHomes = AdminsBypassLimits && player.IsAdmin ? 0 : GetMaxHomes(player);
            if (maxHomes > 0 && user != null && user.Homes != null && user.Homes.Count >= maxHomes)
            {
                error = "You already have the maximum number of homes allowed";
                return false;
            }

            if (!isOnTugBoat)
            {
                if (home.MinimumHomeRadiusDistance > 0f && user?.Homes != null)
                {
                    Vector3 playerPos = player.transform.position;
                    foreach (TeleportGUIData.UserData.HomePoint existing in user.Homes.Values)
                    {
                        if (!TryGetHomeWorldPosition(existing, out Vector3 homePos))
                            continue;

                        float distance = Vector3.Distance(playerPos, homePos);
                        if (distance <= home.MinimumHomeRadiusDistance)
                        {
                            error = string.Format(
                                "You already have a home set {0}m away. The minimum distance for homes is {1}m",
                                distance.ToString("N1"),
                                home.MinimumHomeRadiusDistance.ToString("N1"));
                            return false;
                        }
                    }
                }

                if (home.MustSetHomeOnBuilding)
                {
                    Vector3 pos = player.transform.position;
                    if (!home.CanSetHomeOnFloor)
                    {
                        if (!CheckFoundation(pos))
                        {
                            error = "Homes can only be set on foundations";
                            return false;
                        }
                    }
                    else if (!CheckFoundation(pos) && !CheckFloor(pos))
                    {
                        error = "Homes can only be set on foundations and floors";
                        return false;
                    }
                }
            }

            if (TeleportGUIIntegrations.ZoneManager.IsLoaded &&
                TeleportGUIIntegrations.ZoneManager.PlayerHasFlag(player, "notp"))
            {
                error = "Homes can not be set in NoTP zones";
                return false;
            }

            return true;
        }

        private bool TryResolveHomePosition(TeleportGUIData.UserData.HomePoint home, BasePlayer player, out Vector3 position, out string error)
        {
            position = default;
            error = null;

            if (home == null)
            {
                error = "The home has been destroyed";
                return false;
            }

            if (!TryGetHomeWorldPosition(home, out position))
            {
                error = "The home has been destroyed";
                return false;
            }

            if (IsInvalidBagSpawn(home, player, out InvalidBagReason reason))
            {
                error = reason switch
                {
                    InvalidBagReason.IsPublic => "The home can not be used as it is a public bag/bed",
                    InvalidBagReason.NotAssigned => "The home can not be used as it is not assigned to you",
                    _ => "The home has been destroyed"
                };
                return false;
            }

            if (!IsHomePointValid(home))
            {
                error = "The home has been destroyed as the block it was placed on has been destroyed";
                return false;
            }

            Vector3 checkPos = position;
            if (home.EntityID != 0UL)
                checkPos += Vector3.up * 0.55f;

            if (IsInsideEntity(checkPos))
            {
                error = "The home is currently blocked by a deployed item";
                position = checkPos;
                return false;
            }

            if (home.EntityID != 0UL)
                position = checkPos;

            return true;
        }

        private bool IsInsideEntity(Vector3 position)
        {
            if (!(_config?.Home?.DisableHomeInEntity ?? true))
                return false;

            return Physics.OverlapSphereNonAlloc(position + (Vector3.up * 0.5f), 0.4f, InsideEntityResults, InsideEntityLayers) > 0;
        }

        #endregion

        #region Home helpers

        private bool TryGetHomeWorldPosition(TeleportGUIData.UserData.HomePoint home, out Vector3 position)
        {
            position = default;
            if (home == null)
                return false;

            if (home.EntityID == 0UL)
            {
                position = home.Position;
                return true;
            }

            BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(home.EntityID)) as BaseEntity;
            if (entity == null || entity.IsDestroyed)
                return false;

            position = entity.transform.TransformPoint(home.Offset);
            return true;
        }

        private bool IsHomePointValid(TeleportGUIData.UserData.HomePoint homePoint)
        {
            if (homePoint == null)
                return false;

            if (homePoint.EntityID == 0UL && (_config?.Home?.MustSetHomeOnBuilding ?? false))
            {
                if (!CheckFoundation(homePoint.Position) && !CheckFloor(homePoint.Position))
                    return false;
            }

            return true;
        }

        private bool IsInvalidBagSpawn(TeleportGUIData.UserData.HomePoint homePoint, BasePlayer player, out InvalidBagReason reason)
        {
            reason = InvalidBagReason.None;

            if (homePoint == null || homePoint.EntityID == 0UL)
                return false;

            BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(homePoint.EntityID)) as BaseEntity;
            if (entity == null || entity.IsDestroyed)
                return true;

            if (entity is SleepingBag sleepingBag && player != null)
            {
                if (sleepingBag.deployerUserID != (ulong)player.userID)
                {
                    reason = InvalidBagReason.NotAssigned;
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Condition checks

        private TeleportGUIConfig.TeleportConditions EnsureConditions()
        {
            _config ??= new TeleportGUIConfig();
            _config.Conditions ??= new TeleportGUIConfig.TeleportConditions();
            return _config.Conditions;
        }

        private bool CheckBleeding(TeleportGUIConfig.WhilstBleedingCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.WhilstBleedingCondition();
            if (!cond.CanTeleport && player.metabolism != null && player.metabolism.bleeding.value > 0f)
            {
                SendMessage(player, "You can not teleport whilst bleeding");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer &&
                target.metabolism != null && target.metabolism.bleeding.value > 0f)
            {
                SendMessage(player, "You can not teleport when the target is bleeding");
                return false;
            }

            return true;
        }

        private bool CheckCrafting(TeleportGUIConfig.WhenCraftingCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.WhenCraftingCondition();
            if (!cond.CanTeleport && player.inventory?.crafting?.queue != null && player.inventory.crafting.queue.Count > 0)
            {
                SendMessage(player, "You can not teleport whilst crafting");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer &&
                target.inventory?.crafting?.queue != null && target.inventory.crafting.queue.Count > 0)
            {
                SendMessage(player, "You can not teleport when the target is crafting");
                return false;
            }

            return true;
        }

        private bool CheckMounted(TeleportGUIConfig.MountedCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.MountedCondition();
            if (!cond.CanTeleport && player.isMounted)
            {
                SendMessage(player, "You can not teleport whilst mounted");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer && target.isMounted)
            {
                SendMessage(player, "You can not teleport when the target is mounted");
                return false;
            }

            return true;
        }

        private bool CheckBuildingBlockedSelf(TeleportGUIConfig.BuildingBlockedCondition cond, BasePlayer player)
        {
            cond ??= new TeleportGUIConfig.BuildingBlockedCondition();
            if (!cond.CanTeleport && !player.CanBuild())
            {
                SendMessage(player, "You can not teleport whilst building blocked");
                return false;
            }

            return true;
        }

        private bool CheckBuildingBlocked(TeleportGUIConfig.BuildingBlockedCondition cond, BasePlayer player, BasePlayer target)
        {
            if (!CheckBuildingBlockedSelf(cond, player))
                return false;

            cond ??= new TeleportGUIConfig.BuildingBlockedCondition();
            if (target != null && !cond.CanTeleportTargetPlayer && !target.CanBuild())
            {
                SendMessage(player, "You can not teleport when the target is building blocked");
                return false;
            }

            return true;
        }

        private bool CheckBuildingBlocked(TeleportGUIConfig.BuildingBlockedCondition cond, BasePlayer player, Vector3 position)
        {
            if (!CheckBuildingBlockedSelf(cond, player))
                return false;

            cond ??= new TeleportGUIConfig.BuildingBlockedCondition();
            if (!cond.CanTeleportTargetPosition &&
                player.IsBuildingBlocked(position, player.transform.rotation, player.bounds))
            {
                SendMessage(player, "You can not teleport as you are building blocked in the desired location");
                return false;
            }

            return true;
        }

        private bool CheckRaidBlocked(TeleportGUIConfig.RaidBlockedCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.RaidBlockedCondition();
            if (!cond.CanTeleport && IsRaidOrCombatBlocked(player))
            {
                SendMessage(player, "You can not teleport whilst raid/combat blocked");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer && IsRaidOrCombatBlocked(target))
            {
                SendMessage(player, "You can not teleport when the target is raid/combat blocked");
                return false;
            }

            return true;
        }

        private static bool IsRaidOrCombatBlocked(BasePlayer player)
        {
            if (player == null || !TeleportGUIIntegrations.RaidBlock.IsLoaded)
                return false;

            if (TeleportGUIIntegrations.RaidBlock.IsRaidBlocked(player))
                return true;

            try
            {
                Type type = TeleportGUIIntegrations.ResolveType(
                    "NoEscape", "RaidBlock", "RaidBlockHarmony.RaidBlockMod", "Oxide.Plugins.NoEscape");
                if (type == null)
                    return false;

                MethodInfo combat = type.GetMethod("IsCombatBlocked", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null, new[] { typeof(BasePlayer) }, null);
                if (combat == null)
                    return false;

                object instance = combat.IsStatic ? null : TeleportGUIIntegrations.ResolveInstance(type);
                object result = combat.Invoke(instance, new object[] { player });
                return result is bool blocked && blocked;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckParented<T>(TeleportGUIConfig.TargetTeleportCondition cond, BasePlayer player, BasePlayer target,
            string selfMessage, string targetMessage) where T : BaseEntity
        {
            if (!CheckParentedSelf<T>(cond, player, selfMessage))
                return false;

            if (cond != null && target != null && !cond.CanTeleportTargetPlayer && target.GetParentEntity() is T)
            {
                SendMessage(player, targetMessage);
                return false;
            }

            return true;
        }

        private bool CheckParentedSelf<T>(TeleportGUIConfig.TargetTeleportCondition cond, BasePlayer player, string selfMessage)
            where T : BaseEntity
        {
            // CanTeleport default false => block when parented, matching Oxide defaults.
            bool canTeleport = cond == null || cond.CanTeleport;
            if (!canTeleport && player.GetParentEntity() is T)
            {
                SendMessage(player, selfMessage);
                return false;
            }

            return true;
        }

        private bool CheckOilRig(TeleportGUIConfig.OilRigCondition cond, BasePlayer player, Vector3 target,
            bool checkTargetPlayer, bool checkPosition)
        {
            cond ??= new TeleportGUIConfig.OilRigCondition();
            EnsureMonumentsDiscovered();

            if (!cond.CanTeleport && IsNearOilRig(player.transform.position))
            {
                SendMessage(player, "You can not teleport whilst you are near the oil rig");
                return false;
            }

            if (checkTargetPlayer && !cond.CanTeleportTargetPlayer && IsNearOilRig(target))
            {
                SendMessage(player, "You can not teleport when the target is near the oil rig");
                return false;
            }

            if (checkPosition && !cond.CanTeleportTargetPosition && IsNearOilRig(target))
            {
                SendMessage(player, "You can not teleport as the desired location is too close to the oil rig");
                return false;
            }

            return true;
        }

        private bool CheckUnderwaterLabs(TeleportGUIConfig.UnderwaterLabsCondition cond, BasePlayer player, Vector3 target,
            bool checkTargetPlayer, bool checkPosition)
        {
            cond ??= new TeleportGUIConfig.UnderwaterLabsCondition();

            if (!cond.CanTeleport && IsInUnderwaterLab(player.transform.position))
            {
                SendMessage(player, "You can not teleport whilst you are in underwater labs");
                return false;
            }

            if (checkTargetPlayer && !cond.CanTeleportTargetPlayer && IsInUnderwaterLab(target))
            {
                SendMessage(player, "You can not teleport when the target is in underwater labs");
                return false;
            }

            if (checkPosition && !cond.CanTeleportTargetPosition && IsInUnderwaterLab(target))
            {
                SendMessage(player, "You can not teleport as the desired location is too close to underwater labs");
                return false;
            }

            return true;
        }

        private bool CheckTrainTunnels(TeleportGUIConfig.TrainTunnelsCondition cond, BasePlayer player, Vector3 target,
            bool checkTargetPlayer, bool checkPosition)
        {
            cond ??= new TeleportGUIConfig.TrainTunnelsCondition();

            if (!cond.CanTeleport && IsInTrainTunnels(player.transform.position))
            {
                SendMessage(player, "You can not teleport whilst you are in the train tunnels");
                return false;
            }

            if (checkTargetPlayer && !cond.CanTeleportTargetPlayer && IsInTrainTunnels(target))
            {
                SendMessage(player, "You can not teleport when the target is the train tunnels");
                return false;
            }

            if (checkPosition && !cond.CanTeleportTargetPosition && IsInTrainTunnels(target))
            {
                SendMessage(player, "You can not teleport as the desired location is too close to the train tunnels");
                return false;
            }

            return true;
        }

        private bool CheckInWater(TeleportGUIConfig.InWaterCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.InWaterCondition();
            if (!cond.CanTeleport && IsInWater(player))
            {
                SendMessage(player, "You can not teleport whilst in water");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer && IsInWater(target))
            {
                SendMessage(player, "You can not teleport when the target is in water");
                return false;
            }

            return true;
        }

        private bool CheckOnWater(TeleportGUIConfig.OnWaterCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.OnWaterCondition();
            float maxHeight = cond.MaxHeight > 0f ? cond.MaxHeight : 3f;

            if (!cond.CanTeleport && IsOnWater(player, maxHeight))
            {
                SendMessage(player, "You can not teleport whilst on water");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer && IsOnWater(target, maxHeight))
            {
                SendMessage(player, "You can not teleport when the target is on water");
                return false;
            }

            return true;
        }

        private bool CheckNoTpZone(TeleportGUIConfig.NoTPZoneCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.NoTPZoneCondition();
            if (!cond.CanTeleport &&
                TeleportGUIIntegrations.ZoneManager.IsLoaded &&
                TeleportGUIIntegrations.ZoneManager.PlayerHasFlag(player, "notp"))
            {
                SendMessage(player, "You can not teleport whilst in a NoTP zone");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer &&
                TeleportGUIIntegrations.ZoneManager.IsLoaded &&
                TeleportGUIIntegrations.ZoneManager.PlayerHasFlag(target, "notp"))
            {
                SendMessage(player, "You can not teleport when the target is in a NoTP zone");
                return false;
            }

            return true;
        }

        private bool CheckSafeZone(TeleportGUIConfig.SafeZoneCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.SafeZoneCondition();
            if (!cond.CanTeleport && player.InSafeZone())
            {
                SendMessage(player, "You can not teleport whilst in a safe zone");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer && target.InSafeZone())
            {
                SendMessage(player, "You can not teleport when the target is in a safe zone");
                return false;
            }

            return true;
        }

        private bool CheckHostile(TeleportGUIConfig.HostileCondition cond, BasePlayer player, BasePlayer target)
        {
            cond ??= new TeleportGUIConfig.HostileCondition();
            if (!cond.CanTeleport && player.IsHostile())
            {
                SendMessage(player, "You can not teleport whilst deemed hostile");
                return false;
            }

            if (target != null && !cond.CanTeleportTargetPlayer && target.IsHostile())
            {
                SendMessage(player, "You can not teleport when the target is deemed hostile");
                return false;
            }

            return true;
        }

        private bool CheckMonument(TeleportGUIConfig.InMonumentCondition cond, BasePlayer player, Vector3 target,
            bool checkTargetPlayer, bool checkPosition)
        {
            cond ??= new TeleportGUIConfig.InMonumentCondition();
            EnsureMonumentsDiscovered();

            bool ignoreSafe = cond.IgnoreSafeZones;
            string[] ignore = cond.IgnoreMonuments;

            if (!cond.CanTeleport && IsInMonument(player.transform.position, ignoreSafe, ignore))
            {
                SendMessage(player, "You can not teleport whilst you are in a monument");
                return false;
            }

            if (checkTargetPlayer && !cond.CanTeleportTargetPlayer && IsInMonument(target, ignoreSafe, ignore))
            {
                SendMessage(player, "You can not teleport when the target is in a monument");
                return false;
            }

            if (checkPosition && !cond.CanTeleportTargetPosition && IsInMonument(target, ignoreSafe, ignore))
            {
                SendMessage(player, "You can not teleport as the desired location is too close to a monument");
                return false;
            }

            return true;
        }

        private bool CheckTopology(TeleportGUIConfig.CustomTopologyCondition cond, BasePlayer player, Vector3 target,
            bool checkTargetPlayer, bool checkPosition)
        {
            cond ??= new TeleportGUIConfig.CustomTopologyCondition();
            int mask = GetTopologyMask(cond);
            if (mask == 0)
                return true;

            if (!cond.CanTeleport && ContainsTopologyAtPoint(player.transform.position, mask))
            {
                SendMessage(player, "You can not teleport from your current position");
                return false;
            }

            if (checkTargetPlayer && !cond.CanTeleportTargetPlayer && ContainsTopologyAtPoint(target, mask))
            {
                SendMessage(player, "You can not teleport to the targets current position");
                return false;
            }

            if (checkPosition && !cond.CanTeleportTargetPosition && ContainsTopologyAtPoint(target, mask))
            {
                SendMessage(player, "You can not teleport to the target position");
                return false;
            }

            return true;
        }

        #endregion

        #region World / building helpers

        private static bool IsInWater(BasePlayer player)
        {
            if (player == null)
                return false;

            ModelState modelState = player.modelState;
            return modelState != null && modelState.waterLevel > 0f;
        }

        private static bool IsOnWater(BasePlayer player, float maxHeight)
        {
            if (player == null || IsInWater(player))
                return false;

            // Deployed | World | Terrain | Construction
            const int layers = 1 << 8 | 1 << 16 | 1 << 21 | 1 << 23;
            Vector3 position = player.transform.position;

            if (Physics.Raycast(position + Vector3.up, Vector3.down, out _, 1f + maxHeight, layers))
                return false;

            WaterLevel.WaterInfo waterInfo = WaterLevel.GetWaterInfo(position, true, true, null);
            return waterInfo.overallDepth > 5f && waterInfo.surfaceLevel + maxHeight > position.y;
        }

        private static bool IsInUnderwaterLab(Vector3 position)
        {
            EnvironmentType environmentType = EnvironmentManager.Get(position, 0.01f);
            return (environmentType & EnvironmentType.UnderwaterLab) == EnvironmentType.UnderwaterLab;
        }

        private static bool IsInTrainTunnels(Vector3 position)
        {
            EnvironmentType environmentType = EnvironmentManager.Get(position, 0.01f);
            return (environmentType & EnvironmentType.TrainTunnels) == EnvironmentType.TrainTunnels;
        }

        private static bool IsNearOilRig(Vector3 position)
        {
            EnsureMonumentsDiscovered();
            for (int i = 0; i < OilRigs.Count; i++)
            {
                if (OilRigs[i].Contains(position))
                    return true;
            }

            return false;
        }

        private static bool IsInMonument(Vector3 position, bool ignoreSafeZone = false, string[] ignoreMonuments = null)
        {
            EnsureMonumentsDiscovered();
            for (int i = 0; i < Monuments.Count; i++)
            {
                MonumentEntry monument = Monuments[i];
                if (ignoreSafeZone && monument.IsSafeZone)
                    continue;

                if (ignoreMonuments != null && ignoreMonuments.Length > 0 &&
                    Array.Exists(ignoreMonuments, n => string.Equals(n, monument.Shortname, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (monument.Contains(position))
                    return true;
            }

            return false;
        }

        private static bool CheckFoundation(Vector3 position) =>
            FindBuildingBlock(position, FoundationShortname) || FindBuildingBlock(position, FoundationTriangleShortname);

        private static bool CheckFloor(Vector3 position) =>
            FindBuildingBlock(position, FloorShortname) || FindBuildingBlock(position, FloorTriangleShortname);

        private static bool FindBuildingBlock(Vector3 position, string shortname)
        {
            int num = Physics.RaycastNonAlloc(new Ray(position + (Vector3.up * 0.1f), Vector3.down), RayBuffer, 0.2f);
            if (num == 0)
                return false;

            for (int i = 0; i < num; i++)
            {
                BuildingBlock buildingBlock = RayBuffer[i].GetEntity() as BuildingBlock;
                if (buildingBlock != null && buildingBlock.ShortPrefabName == shortname)
                    return true;
            }

            return false;
        }

        private int GetTopologyMask(TeleportGUIConfig.CustomTopologyCondition cond)
        {
            if (_topologyMaskCache != int.MaxValue)
                return _topologyMaskCache;

            string[] topologies = cond?.Topologies;
            if (topologies == null || topologies.Length == 0)
            {
                _topologyMaskCache = 0;
                return 0;
            }

            int mask = 0;
            Type enumType = ResolveTerrainTopologyEnum();
            if (enumType != null && enumType.IsEnum)
            {
                string[] names = Enum.GetNames(enumType);
                for (int i = 0; i < names.Length; i++)
                {
                    if (Array.Exists(topologies, t => string.Equals(t, names[i], StringComparison.OrdinalIgnoreCase)))
                    {
                        object value = Enum.Parse(enumType, names[i]);
                        mask |= Convert.ToInt32(value);
                    }
                }
            }
            else
            {
                // Fallback bit positions matching TerrainTopology.Enum order.
                string[] fallback = {
                    "Field", "Cliff", "Summit", "Beachside", "Beach", "Forest", "Forestside", "Ocean", "Oceanside",
                    "Decor", "Monument", "Road", "Roadside", "Swamp", "River", "Riverside", "Lake", "Lakeside",
                    "Offshore", "Rail", "Railside", "Building", "Cliffside", "Mountain", "Clutter", "Alt",
                    "Tier0", "Tier1", "Tier2", "Mainland", "Hilltop"
                };
                for (int i = 0; i < fallback.Length; i++)
                {
                    if (Array.Exists(topologies, t => string.Equals(t, fallback[i], StringComparison.OrdinalIgnoreCase)))
                        mask |= 1 << i;
                }
            }

            _topologyMaskCache = mask;
            return mask;
        }

        private static Type ResolveTerrainTopologyEnum()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type nested = asm.GetType("TerrainTopology+Enum", false);
                    if (nested != null)
                        return nested;

                    Type outer = asm.GetType("TerrainTopology", false);
                    Type inner = outer?.GetNestedType("Enum");
                    if (inner != null)
                        return inner;
                }
                catch
                {
                    // ignored
                }
            }

            return null;
        }

        private static bool ContainsTopologyAtPoint(Vector3 position, int mask)
        {
            if (mask == 0 || TerrainMeta.TopologyMap == null)
                return false;

            return (TerrainMeta.TopologyMap.GetTopology(position) & mask) != 0;
        }

        private static void EnsureMonumentsDiscovered()
        {
            if (MonumentsDiscovered)
                return;

            MonumentsDiscovered = true;
            Monuments.Clear();
            OilRigs.Clear();

            // TerrainPath.Monuments is internal — resolve via the shared reflection helper.
            List<MonumentInfo> pathMonuments = GetTerrainMonuments();
            if (pathMonuments == null || pathMonuments.Count == 0)
                return;

            for (int i = 0; i < pathMonuments.Count; i++)
            {
                MonumentInfo monument = pathMonuments[i];
                if (monument == null)
                    continue;

                string shortname = Path.GetFileNameWithoutExtension(monument.name);
                if (string.IsNullOrEmpty(shortname))
                    continue;

                if (shortname.IndexOf("underwater_lab", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                Vector3 position = default;
                float radius = 0f;
                Bounds bounds = monument.Bounds;

                PreventBuildingMonumentTag[] tags = monument.GetComponentsInChildren<PreventBuildingMonumentTag>(true);
                if (tags != null)
                {
                    for (int t = 0; t < tags.Length; t++)
                    {
                        PreventBuildingMonumentTag tag = tags[t];
                        Collider collider = tag ? tag.GetComponent<Collider>() : null;
                        if (!tag || collider == null || collider.gameObject.layer != 29 || collider is MeshCollider)
                            continue;

                        if (collider is SphereCollider sphereCollider)
                        {
                            if (sphereCollider.radius > radius)
                            {
                                position = sphereCollider.transform.position;
                                radius = sphereCollider.radius;
                            }
                        }
                        else if (collider is BoxCollider boxCollider)
                        {
                            Vector3 localCenter = monument.transform.InverseTransformPoint(
                                boxCollider.transform.TransformPoint(boxCollider.center));
                            Vector3 localSize = Vector3.Scale(boxCollider.size, boxCollider.transform.lossyScale);
                            bounds.Encapsulate(new Bounds(localCenter, localSize));
                        }
                    }
                }

                if (radius == 0f && bounds.size == Vector3.zero &&
                    MonumentBoundsOverrides.TryGetValue(shortname, out Bounds @override))
                    bounds = @override;

                if (position == default)
                    position = monument.transform.position;

                var entry = new MonumentEntry(shortname, monument.IsSafeZone, monument.transform, position, radius, bounds, monument);

                bool isOilRig = false;
                try { isOilRig = monument.IsOilRig(); }
                catch { /* older assemblies */ }

                if (!isOilRig)
                    isOilRig = shortname.IndexOf("oilrig", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isOilRig)
                    OilRigs.Add(entry);
                else
                    Monuments.Add(entry);
            }
        }

        private sealed class MonumentEntry
        {
            public readonly string Shortname;
            public readonly bool IsSafeZone;
            private readonly Transform _transform;
            private readonly Vector3 _position;
            private readonly float _radius;
            private readonly Bounds _bounds;
            private readonly MonumentInfo _info;

            public MonumentEntry(string shortname, bool isSafeZone, Transform transform, Vector3 position, float radius,
                Bounds bounds, MonumentInfo info)
            {
                Shortname = shortname;
                IsSafeZone = isSafeZone;
                _transform = transform;
                _position = position;
                _radius = radius;
                _bounds = bounds;
                _info = info;
            }

            public bool Contains(Vector3 worldPos)
            {
                if (_radius > 0f)
                    return Vector3.Distance(_position, worldPos) < _radius;

                if (_bounds.size != Vector3.zero && _transform != null)
                {
                    Vector3 local = _transform.InverseTransformPoint(worldPos);
                    if (_bounds.Contains(local))
                        return true;
                }

                if (_info != null)
                {
                    try
                    {
                        if (_info.IsInBounds(worldPos))
                            return true;
                    }
                    catch
                    {
                        // ignored
                    }
                }

                return false;
            }
        }

        #endregion
    }
}
