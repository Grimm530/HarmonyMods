using System.Collections.Generic;
using Facepunch;
using HarmonyLib;
using RaidableBases.RaidableBasesExtensionMethods;
using UnityEngine;

namespace RaidableBases
{
    /// <summary>
    /// Game-native hooks: elevators, condition wear, SAM scan, MLRS, cupboards, fireballs, teams/clans.
    /// </summary>

    // ── Elevators ──────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(ElevatorLift), nameof(ElevatorLift.Server_RaiseLowerFloor))]
    internal static class ElevatorLift_Server_RaiseLowerFloor_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ElevatorLift __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null)
                return true;

            var read = msg.read;
            int pos = (int)read.Position;
            int targetFloor = read.Int32();
            bool relative = read.Bool();
            read.Position = pos;

            bool fullTravel = targetFloor == int.MinValue || targetFloor == int.MaxValue;
            Elevator.Direction direction = Elevator.Direction.Up;
            Elevator owner = __instance.owner;
            if (owner != null)
            {
                int current = owner.LiftPositionToFloor();
                int resolved = targetFloor;
                if (targetFloor == int.MinValue) resolved = 0;
                else if (targetFloor == int.MaxValue) resolved = owner.Floor;
                else if (relative) resolved = current + targetFloor;
                direction = resolved > current ? Elevator.Direction.Up : Elevator.Direction.Down;
            }
            else if (fullTravel)
            {
                direction = targetFloor == int.MaxValue ? Elevator.Direction.Up : Elevator.Direction.Down;
            }
            else if (relative)
            {
                direction = targetFloor >= 0 ? Elevator.Direction.Up : Elevator.Direction.Down;
            }

            var block = Interface.CallHook("OnElevatorButtonPress", __instance, msg.player, direction, fullTravel);
            return block == null;
        }
    }

    [HarmonyPatch(typeof(Elevator), "RequestMoveLiftTo")]
    internal static class Elevator_RequestMoveLiftTo_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Elevator __instance, int targetFloor, out float timeToTravel, Elevator fromElevator, ref bool __result)
        {
            timeToTravel = 0f;
            if (__instance == null)
                return true;

            // BMG elevators block vanilla move/call and drive travel themselves.
            if (Interface.CallHook("OnElevatorMove", __instance, targetFloor) != null)
            {
                __result = false;
                return false;
            }
            if (fromElevator != null && Interface.CallHook("OnElevatorCall", __instance, fromElevator) != null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PressButton), nameof(PressButton.RPC_Press))]
    internal static class PressButton_RPC_Press_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PressButton __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null)
                return;
            Interface.CallHook("OnButtonPress", __instance, msg.player);
        }
    }

    // ── Condition wear (Item.LoseCondition — not ItemModConditionHasFlag) ──

    [HarmonyPatch(typeof(Item), nameof(Item.LoseCondition))]
    internal static class Item_LoseCondition_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Item __instance, ref float amount)
        {
            if (__instance == null || amount <= 0f)
                return;

            // OnNeverWear is an Oxide NeverWear-style hook; RB returns amount to force wear in raid zones.
            var never = Interface.CallHook("OnNeverWear", __instance, amount);
            if (never is float forced && forced > 0f)
                amount = forced;

            var args = new object[] { __instance, amount };
            Interface.CallHook("OnLoseCondition", args);
            if (args[1] is float updated)
                amount = updated;
        }
    }

    // ── SAM sites ──────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(SamSite), nameof(SamSite.TargetScan))]
    internal static class SamSite_TargetScan_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(SamSite __instance)
        {
            if (__instance == null || !__instance.IsPowered())
                return true;

            var scanList = Pool.Get<List<SamSite.ISamSiteTarget>>();
            try
            {
                var replaced = Interface.CallHook("OnSamSiteTargetScan", __instance, scanList);
                if (replaced == null)
                    return true;

                // Oxide: non-null (RB returns true) replaces default target gathering.
                if (UnityEngine.Time.time > __instance.lastTargetVisibleTime + 3f)
                    __instance.ClearTarget();

                if (!__instance.staticRespawn)
                {
                    int num = (__instance.ammoItem != null && __instance.ammoItem.parent == __instance.inventory) ? __instance.ammoItem.amount : 0;
                    bool flag = __instance.lastAmmoCount < __instance.lowAmmoThreshold;
                    bool flag2 = num < __instance.lowAmmoThreshold;
                    if (num != __instance.lastAmmoCount && flag != flag2)
                        __instance.MarkIODirty();
                    __instance.lastAmmoCount = num;
                }

                if (__instance.HasValidTarget() || __instance.IsDead())
                    return false;

                SamSite.ISamSiteTarget samSiteTarget = null;
                foreach (SamSite.ISamSiteTarget item in scanList)
                {
                    if (item == null || item.isClient)
                        continue;
                    if (item.CenterPoint().y < __instance.eyePoint.transform.position.y)
                        continue;
                    if (!item.IsVisible(__instance.eyePoint.transform.position, item.SAMTargetType.scanRadius * 2f))
                        continue;
                    if (!item.IsValidSAMTarget(__instance.staticRespawn))
                        continue;

                    // Optional per-target gate used by CanEntityBeTargeted(entity, SamSite).
                    if (item is BaseEntity be)
                    {
                        var can = Interface.CallHook("CanEntityBeTargeted", be, __instance);
                        if (can is bool allow && !allow)
                            continue;
                    }

                    samSiteTarget = item;
                    break;
                }

                if (!samSiteTarget.IsUnityNull() && __instance.currentTarget != samSiteTarget)
                    __instance.lockOnTime = UnityEngine.Time.time + 0.5f;

                __instance.SetTarget(samSiteTarget);
                if (!__instance.currentTarget.IsUnityNull())
                    __instance.lastTargetVisibleTime = UnityEngine.Time.time;

                if (__instance.WeaponTickCB == null)
                    __instance.WeaponTickCB = __instance.WeaponTick;

                if (__instance.currentTarget.IsUnityNull())
                    __instance.CancelInvoke(__instance.WeaponTickCB);
                else
                    __instance.InvokeRandomized(__instance.WeaponTickCB, 0f, 0.5f, 0.2f);

                return false;
            }
            finally
            {
                Pool.FreeUnmanaged(ref scanList);
            }
        }
    }

    // ── MLRS ───────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(MLRS), "Fire")]
    internal static class MLRS_Fire_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(MLRS __instance, BasePlayer owner)
        {
            if (__instance == null)
                return true;
            var block = Interface.CallHook("OnMlrsFire", __instance, owner);
            return block == null;
        }
    }

    // ── Cupboards ──────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.AddPlayer))]
    internal static class BuildingPrivlidge_AddPlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildingPrivlidge __instance, BasePlayer granter, ulong targetPlayerId)
        {
            if (__instance == null)
                return;
            Interface.CallHook("OnCupboardAuthorize", __instance, granter);
        }
    }

    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.GetProtectedMinutes))]
    internal static class BuildingPrivlidge_GetProtectedMinutes_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildingPrivlidge __instance, bool force)
        {
            if (__instance == null)
                return;
            Interface.CallHook("OnCupboardProtectionCalculated", __instance, __instance.cachedProtectedMinutes);
        }
    }

    // ── Fireballs ──────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.OnAttacked), typeof(HitInfo))]
    internal static class BaseCombatEntity_OnAttacked_FireBall_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null || info == null)
                return;
            // Prefer FireBall initiator; RB also rewrites null/FireBall initiator while fire is in event territory.
            FireBall fire = info.Initiator as FireBall;
            if (fire == null && info.WeaponPrefab is FireBall fb)
                fire = fb;
            if (fire != null)
                Interface.CallHook("OnFireBallDamage", fire, __instance, info);
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_FireBallSpread_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is not FireBall spread || spread.IsDestroyed || spread.generation <= 0f)
                return;
            var creator = spread.creatorEntity as FireBall ?? spread;
            Interface.CallHook("OnFireBallSpread", creator, spread);
        }
    }

    // ── Teams (RelationshipManager) ────────────────────────────────────────

    [HarmonyPatch(typeof(RelationshipManager.PlayerTeam), nameof(RelationshipManager.PlayerTeam.AcceptInvite))]
    internal static class PlayerTeam_AcceptInvite_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(RelationshipManager.PlayerTeam __instance, BasePlayer player)
        {
            if (__instance == null || player == null)
                return true;
            var block = Interface.CallHook("OnTeamAcceptInvite", __instance, player);
            return block == null;
        }
    }

    // ── Native clans (ClanManager) ─────────────────────────────────────────

    [HarmonyPatch(typeof(ClanManager), nameof(ClanManager.Server_AcceptInvitation))]
    internal static class ClanManager_Server_AcceptInvitation_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ClanManager __instance, BaseEntity.RPCMessage msg)
        {
            if (msg.player == null)
                return true;
            var read = msg.read;
            int pos = (int)read.Position;
            read.Int32(); // requestId
            long clanId = read.Int64();
            read.Position = pos;

            string tag = clanId != 0L ? clanId.ToString() : string.Empty;
            // Non-null blocks accept (hogging / ally exploit). Also notifies Oxide Clans leave path.
            return Interface.CallHook("OnClanMemberJoined", (ulong)msg.player.userID, tag) == null;
        }
    }

    // ── Chat command blacklist ─────────────────────────────────────────────

    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    internal static class Chat_Say_Blacklist_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null)
                return true;
            var player = arg.Player();
            if (player == null)
                return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || message[0] != '/')
                return true;

            string body = message.Substring(1).Trim();
            if (body.Length == 0)
                return true;

            int space = body.IndexOf(' ');
            string command = space < 0 ? body : body.Substring(0, space);
            string[] args = space < 0
                ? System.Array.Empty<string>()
                : body.Substring(space + 1).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            var block = Interface.CallHook("OnPlayerCommand", player, command, args);
            return block == null;
        }
    }

    // ── PvE: allow raid NPC / RB-turret damage when server.pve reflects player↔player ──

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Hurt), typeof(HitInfo))]
    [HarmonyPriority(Priority.First)]
    internal static class BasePlayer_Hurt_RaidNpcDamage_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, HitInfo info, ref bool __state)
        {
            __state = false;
            if (info == null || __instance == null || __instance.IsNpc)
                return;
            if (!ConVar.Server.pve)
                return;

            BaseEntity initiator = info.Initiator;
            if (initiator == null)
                return;

            bool raidAttacker =
                initiator is RaidableBases.HumanoidNPC
                || initiator.skinID == RaidTurretIds.Skin
                || (initiator is AutoTurret at && at.skinID == RaidTurretIds.Skin);

            if (!raidAttacker && initiator is BasePlayer bp)
                raidAttacker = bp.skinID == RaidTurretIds.Skin || (!((ulong)bp.userID).IsSteamId() && bp.IsNpc);

            if (!raidAttacker)
                return;

            ConVar.Server.pve = false;
            __state = true;
        }

        [HarmonyPostfix]
        private static void Postfix(bool __state)
        {
            if (__state)
                ConVar.Server.pve = true;
        }
    }
}
