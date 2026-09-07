using System;
using GrimmCuiHarmony;

namespace IndustrialTransferSpeed
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtestPrefix("ITS_PLANTER_", (_, src) => { var player = src.Connection?.player as BasePlayer ?? src.Player(); if (player != null) IndustrialTransferSpeedMod.Instance?.HandlePlanterCuiCommand(player, src); });
        }
    }
}

