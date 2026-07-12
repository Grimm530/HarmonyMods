using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Facepunch.Harmony.Weaver;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager.Hooks
{
    // Target NPCPlayer.CreateCorpse (inherited by HumanNPC). Optional: skip if method not found (e.g. game update).
    [HarmonyPatch]
    internal class HumanNPC_CreateCorpse : BaseTranspileHook
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method( typeof( NPCPlayer ), "CreateCorpse" );
        }

        static bool Prepare( MethodBase original )
        {
            return original != null;
        }

        static IEnumerable<CodeInstruction> Transpiler( IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase originalMethod )
        {
            return DoTranspile( MethodBase.GetCurrentMethod().DeclaringType, instructions, generator, originalMethod );
        }

        public override bool WeaveHook( CodeInstruction instruction )
        {
            if ( SearchStoreLocal( SearchDirection.After, typeof( NPCPlayerCorpse ), out var corpseLocal ) == null )
            {
                Debug.LogError( $"Couldn't find local for {GetType().Name}" );
                return false;
            }

            MoveToEnd();

            LoadThis();

            LoadLocal( corpseLocal.LocalIndex );

            CallHookMethod( GetType() );

            return true;
        }

        public static bool Hook( NPCPlayer entity, NPCPlayerCorpse corpse )
        {
            try
            {
                var args = Pool.Get<OnLootSpawnedArgs>();
                args.Entity = corpse;
                args.Inventories.AddRange( corpse.containers );

                // In modloader this will call broadcast
                GatherManagerMod.Instance.OnLootSpawned( args );

                Pool.Free( ref args );
            }
            catch ( Exception ex )
            {
                Debug.LogException( ex );
            }

            return true;
        }
    }
}
