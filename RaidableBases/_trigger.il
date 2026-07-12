
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit NPCPlayer
       extends BasePlayer
{
  .method public hidebysig newslot virtual 
          instance void  TriggerDown() cil managed
  {
    // Code size       132 (0x84)
    .maxstack  6
    .locals init (class AttackEntity V_0,
             valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4> V_1)
    IL_0000:  ldarg.0
    IL_0001:  call       instance class HeldEntity BasePlayer::GetHeldEntity()
    IL_0006:  isinst     AttackEntity
    IL_000b:  stloc.0
    IL_000c:  ldloc.0
    IL_000d:  ldnull
    IL_000e:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0013:  brfalse.s  IL_0036

    IL_0015:  ldloc.0
    IL_0016:  ldarg.0
    IL_0017:  ldfld      float32 NPCPlayer::damageScale
    IL_001c:  ldc.r4     1.
    IL_0021:  ldloca.s   V_1
    IL_0023:  initobj    valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4>
    IL_0029:  ldloc.1
    IL_002a:  ldc.i4.1
    IL_002b:  ldc.i4.0
    IL_002c:  newobj     instance void HeldEntityServerUseParams::.ctor(float32,
                                                                        float32,
                                                                        valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4>,
                                                                        bool,
                                                                        bool)
    IL_0031:  callvirt   instance void HeldEntity::ServerUse(valuetype HeldEntityServerUseParams)
    IL_0036:  ldarg.0
    IL_0037:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_003c:  stfld      float32 NPCPlayer::lastGunShotTime
    IL_0041:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0046:  ldarg.0
    IL_0047:  ldfld      float32 NPCPlayer::triggerEndTime
    IL_004c:  ble.un.s   IL_0083

    IL_004e:  ldarg.0
    IL_004f:  ldarg.0
    IL_0050:  dup
    IL_0051:  ldvirtftn  instance void NPCPlayer::TriggerDown()
    IL_0057:  newobj     instance void [mscorlib]System.Action::.ctor(object,
                                                                      native int)
    IL_005c:  call       instance void [Facepunch.UnityEngine]FacepunchBehaviour::CancelInvoke(class [mscorlib]System.Action)
    IL_0061:  ldarg.0
    IL_0062:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0067:  ldloc.0
    IL_0068:  ldnull
    IL_0069:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_006e:  brtrue.s   IL_0077

    IL_0070:  ldc.r4     1.
    IL_0075:  br.s       IL_007d

    IL_0077:  ldloc.0
    IL_0078:  ldfld      float32 AttackEntity::attackSpacing
    IL_007d:  add
    IL_007e:  stfld      float32 NPCPlayer::nextTriggerTime
    IL_0083:  ret
  } // end of method NPCPlayer::TriggerDown

} // end of class NPCPlayer

// *********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file c:\!2XRUST\.cursor\HarmonyMods\RaidableBases\_trigger.res
