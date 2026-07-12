
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit NPCPlayer
       extends BasePlayer
{
  .method public hidebysig instance class AttackEntity 
          GetAttackEntity() cil managed
  {
    // Code size       12 (0xc)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance class HeldEntity BasePlayer::GetHeldEntity()
    IL_0006:  isinst     AttackEntity
    IL_000b:  ret
  } // end of method NPCPlayer::GetAttackEntity

} // end of class NPCPlayer

// *********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file c:\!2XRUST\.cursor\HarmonyMods\RaidableBases\_getae.res
