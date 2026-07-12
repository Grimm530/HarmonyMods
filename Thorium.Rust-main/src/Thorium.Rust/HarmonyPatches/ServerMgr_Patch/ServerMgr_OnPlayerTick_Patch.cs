using System;
using HarmonyLib;
using Network;
using Thorium.Rust.HarmonyPatches.Utility;
using Thorium.Rust.Models;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust.HarmonyPatches.ServerMgr_Patch;

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.OnPlayerTick), typeof(Message))]
internal static class ServerMgr_OnPlayerTick_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Message packet)
    {
        if (!DataHandler.IsConfigured) return;

        var player = packet.Player();
        if (player == null) return;

        var steamId = Helpers.GetSteamIdOrZero(player);
        if (steamId == 0) return;

        // Must restore read position even when Proto() throws — otherwise vanilla
        // OnPlayerTick desyncs the stream and kicks with "Invalid Packet: Player Tick".
        var savedPosition = packet.read.Position;
        try
        {
            var playerTick = packet.read.Proto(null as PlayerTick);
            if (playerTick == null) return;

            var inputState = playerTick.inputState;
            var modelState = playerTick.modelState;
            var pos = player.tickInterpolator.EndPoint;
            var eyePos = playerTick.eyePos;
            var velocity = player.estimatedVelocity;
            var viewAngles = player.viewAngles;
            var flags = player.playerFlags;

            var snapshot = PlayerSnapshot.Create(pos, player, SnapshotTypeEnums.PlayerTick,
                CombatData.FromPlayer(player), velocity, player.IsOnGround(), inputState);

            // Eyes and view mode flags
            snapshot.EyesViewMode = (flags & BasePlayer.PlayerFlags.EyesViewmode) != 0;
            snapshot.ThirdPersonViewMode = (flags & BasePlayer.PlayerFlags.ThirdPersonViewmode) != 0;
            snapshot.ViewAnglesX = viewAngles.x;
            snapshot.ViewAnglesY = viewAngles.y;
            snapshot.ViewAnglesZ = viewAngles.z;
            snapshot.EyesPositionX = eyePos.x;
            snapshot.EyesPositionY = eyePos.y;
            snapshot.EyesPositionZ = eyePos.z;

            // Additional PlayerTick data not available in BasePlayer.OnReceiveTick
            snapshot.ActiveItemId = playerTick.activeItem.Value;
            snapshot.ParentId = playerTick.parentID.Value;
            snapshot.DeltaMs = playerTick.deltaMs;

            // ModelState detailed fields - reuse local variables to avoid repeated field access
            snapshot.WaterLevel = modelState.waterLevel;
            var lookDir = modelState.lookDir;
            snapshot.LookDirX = lookDir.x;
            snapshot.LookDirY = lookDir.y;
            snapshot.LookDirZ = lookDir.z;
            snapshot.PoseType = modelState.poseType;
            var inheritedVel = modelState.inheritedVelocity;
            snapshot.InheritedVelocityX = inheritedVel.x;
            snapshot.InheritedVelocityY = inheritedVel.y;
            snapshot.InheritedVelocityZ = inheritedVel.z;

            AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
        }
        catch
        {
            // Silent fail to avoid server spam
        }
        finally
        {
            packet.read.Position = savedPosition;
        }
    }
}