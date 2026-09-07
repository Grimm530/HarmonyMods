using System;
using GrimmCuiHarmony;

namespace AirbourneSpawnHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("AIRBOURNESPAWN", (_, src) => AirbourneSpawnMod.Instance?.HandleCuiCallback(src));
        }
    }
}
