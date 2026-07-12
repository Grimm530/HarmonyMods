using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Facepunch.Harmony.Weaver;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    [HarmonyPatch( typeof( GrowableEntity ), "GiveFruit", typeof(BasePlayer), typeof(int), typeof(bool) )]
    internal class GrowableEntity_GiveFruit : BaseTranspileHook
    {
        //Copy paste into each transpile hook
        static IEnumerable<CodeInstruction> Transpiler( IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase originalMethod )
        {
            return DoTranspile( MethodBase.GetCurrentMethod().DeclaringType, instructions, generator, originalMethod );
        }

        public override bool WeaveHook( CodeInstruction instruction )
        {
            if ( !IsMethodCall( instruction ) ) return false;
            var method = instruction.operand as MethodInfo;
            if ( method == null || ( method.Name != "GiveItem" && method.Name != "AddItem" && method.Name != "Insert" ) )
                return false;

            MoveBeforeMethod();

            if ( SearchStoreLocal( SearchDirection.Before, typeof( Item ), out var itemLocal ) == null )
            {
                Debug.LogError( "Couldn't find Item local for OnGatherItem" );
                return false;
            }

            // Player
            LoadParameter( 0 );

            // Item
            LoadLocal( itemLocal.LocalIndex );

            // Growable
            LoadThis();

            CallHookMethod( GetType() );

            return true;
        }

        public static bool Hook( BasePlayer player, Item givenItem, GrowableEntity entity )
        {
            try
            {
                var args = Pool.Get<OnGatherItemArgs>();
                args.Entity = entity;
                args.Player = player;
                args.GivenItem = givenItem;
                args.Source = GatherSource.Growable;

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
