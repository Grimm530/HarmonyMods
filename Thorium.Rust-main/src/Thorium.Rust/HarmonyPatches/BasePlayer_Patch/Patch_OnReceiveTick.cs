using System.Collections.Generic;
using HarmonyLib;
using Thorium.Rust.HarmonyPatches.Utility;
using Thorium.Rust.Models;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust.HarmonyPatches.BasePlayer_Patch;

// DEPRECATED: This patch is replaced by ServerMgr_OnPlayerTick_Patch which provides more complete data
// ServerMgr.OnPlayerTick receives the raw Network.Message and can access the full PlayerTick protobuf
// whereas BasePlayer.OnReceiveTick only gets the already-parsed PlayerTick object with limited access

/*
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnReceiveTick), typeof(PlayerTick), typeof(bool))]
internal static class PatchOnReceiveTick
{
    private static bool _isConfigured;
    private static float _lastConfigCheck;

    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance, PlayerTick msg)
    {
        try
        {
            var now = Time.realtimeSinceStartup;
            if (now - _lastConfigCheck > 5f)
            {
                _isConfigured = DataHandler.IsConfigured;
                _lastConfigCheck = now;
            }

            if (!_isConfigured) return;
            if (__instance == null) return;
            if (msg == null) return;

            var steamId = Helpers.GetSteamIdOrZero(__instance);
            if (steamId == 0) return;

            var inputState = msg.inputState;
            var pos = msg.position;
            var velocity = __instance.estimatedVelocity;
            var viewAngles = __instance.viewAngles;
            var flags = __instance.playerFlags;
            var eyesPosition = __instance.eyes.position;

            var snapshot = PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.PlayerTick,
                CombatData.FromPlayer(__instance), velocity, __instance.IsOnGround(), inputState);
            
            snapshot.EyesViewMode = (flags & BasePlayer.PlayerFlags.EyesViewmode) != 0;
            snapshot.ThirdPersonViewMode = (flags & BasePlayer.PlayerFlags.ThirdPersonViewmode) != 0;
            snapshot.ViewAnglesX = viewAngles.x;
            snapshot.ViewAnglesY = viewAngles.y;
            snapshot.ViewAnglesZ = viewAngles.z;
            snapshot.EyesPositionX = eyesPosition.x;
            snapshot.EyesPositionY = eyesPosition.y;
            snapshot.EyesPositionZ = eyesPosition.z;
            
            AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
        }
        catch
        {
        }
    }
}
*/