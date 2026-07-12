using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Facepunch.Harmony.Weaver;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    [HarmonyPatch( typeof( CollectibleEntity ), "DoPickup" )]
    internal class CollectibleEntity_DoPickup : BaseTranspileHook
    {
        /// <summary>Oxide OnCollectiblePickup-style: modify itemList before items are created/given.</summary>
        static void Prefix( CollectibleEntity __instance )
        {
            try
            {
                if ( GatherManagerMod.Instance?.DebugGatherEnabled == true )
                    Debug.Log( $"[GatherManager] CollectibleEntity.DoPickup Prefix called prefab={__instance?.ShortPrefabName} Instance={(GatherManagerMod.Instance != null ? "ok" : "null")}" );
                if ( GatherManagerMod.Instance != null && __instance != null )
                    GatherManagerMod.Instance.ApplyPickupModifiersToCollectible( __instance );
            }
            catch ( Exception ex )
            {
                Debug.LogException( ex );
            }
        }

        static IEnumerable<CodeInstruction> Transpiler( IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase originalMethod )
        {
            return instructions; // Prefix handles itemList; no transpiler injection needed (avoids double-modification)
        }

        public override bool WeaveHook( CodeInstruction instruction )
        {
            return false; // unused – Prefix does the work
        }

        public static bool Hook( BasePlayer player, Item givenItem, CollectibleEntity entity )
        {
            try
            {
                var args = Pool.Get<OnGatherItemArgs>();
                args.Entity = entity;
                args.Player = player;
                args.GivenItem = givenItem;
                args.Source = GatherSource.Pickup;

                // In modloader this will call broadcast
                GatherManagerMod.Instance.OnGatherItem( args );

                bool result = !args.Cancel;

                Pool.Free( ref args );

                return result;
            }
            catch ( Exception ex )
            {
                Debug.LogException( ex );
                return true;
            }
        }
    }
}
