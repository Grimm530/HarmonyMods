using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Facepunch.Harmony.Weaver;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    [HarmonyPatch( typeof( LootContainer ), "PopulateLoot" )]
    internal class LootContainer_PopulateLoot
    {
        [HarmonyPostfix]
        public static void Postfix( LootContainer __instance )
        {
            try
            {
                var args = Pool.Get<OnLootSpawnedArgs>();
                args.Entity = __instance;
                if (__instance.inventory != null)
                    args.Inventories.Add(__instance.inventory);

                GatherManagerMod.Instance.OnLootSpawned( args );

                Pool.Free( ref args );
            }
            catch ( Exception ex )
            {
                Debug.LogException( ex );
            }
        }
    }
}
