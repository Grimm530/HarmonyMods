
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit BaseProjectile
       extends AttackEntity
{
  .method public hidebysig virtual instance void 
          TopUpAmmo() cil managed
  {
    // Code size       18 (0x12)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.0
    IL_0002:  ldfld      class BaseProjectile/Magazine BaseProjectile::primaryMagazine
    IL_0007:  ldfld      int32 BaseProjectile/Magazine::capacity
    IL_000c:  call       instance void BaseProjectile::SetAmmoCount(int32)
    IL_0011:  ret
  } // end of method BaseProjectile::TopUpAmmo

} // end of class BaseProjectile

// *********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file c:\!2XRUST\.cursor\HarmonyMods\RaidableBases\_topup.res
