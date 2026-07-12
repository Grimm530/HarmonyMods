
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit BasePlayer
       extends BaseCombatEntity
       implements LootPanel/IHasLootPanel,
                  IIdealSlotEntity,
                  IInventoryProvider,
                  PlayerInventory/ICanMoveFrom,
                  IReceiveDeepSeaNotifications,
                  ISplashable,
                  IMedicalToolTarget
{
  .method public hidebysig instance void 
          ShowToast(valuetype GameTip/Styles style,
                    class [Rust.Localization]Translate/Phrase phrase,
                    [opt] bool overlay,
                    string[] arguments) cil managed
  {
    .param [3] = bool(false)
    .param [4]
    .custom instance void [mscorlib]System.ParamArrayAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       68 (0x44)
    .maxstack  6
    IL_0000:  ldarg.0
    IL_0001:  call       instance bool BaseNetworkable::get_isServer()
    IL_0006:  brfalse.s  IL_0043

    IL_0008:  ldarg.0
    IL_0009:  ldstr      "gametip.showtoast_translated"
    IL_000e:  ldc.i4.5
    IL_000f:  newarr     [mscorlib]System.Object
    IL_0014:  dup
    IL_0015:  ldc.i4.0
    IL_0016:  ldarg.1
    IL_0017:  box        [mscorlib]System.Int32
    IL_001c:  stelem.ref
    IL_001d:  dup
    IL_001e:  ldc.i4.1
    IL_001f:  ldarg.2
    IL_0020:  ldfld      string [Rust.Localization]Translate/Phrase::token
    IL_0025:  stelem.ref
    IL_0026:  dup
    IL_0027:  ldc.i4.2
    IL_0028:  ldarg.2
    IL_0029:  callvirt   instance string [Rust.Localization]Translate/Phrase::get_english()
    IL_002e:  stelem.ref
    IL_002f:  dup
    IL_0030:  ldc.i4.3
    IL_0031:  ldarg.3
    IL_0032:  box        [mscorlib]System.Boolean
    IL_0037:  stelem.ref
    IL_0038:  dup
    IL_0039:  ldc.i4.4
    IL_003a:  ldarg.s    arguments
    IL_003c:  stelem.ref
    IL_003d:  call       instance void BasePlayer::SendConsoleCommand(string,
                                                                      object[])
    IL_0042:  ret

    IL_0043:  ret
  } // end of method BasePlayer::ShowToast

} // end of class BasePlayer

// *********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file c:\!2XRUST\.cursor\HarmonyMods\RaidableBases\_showtoast.res
