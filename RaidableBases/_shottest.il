
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit NPCPlayer
       extends BasePlayer
{
  .method public hidebysig newslot virtual 
          instance bool  ShotTest(float32 targetDist) cil managed
  {
    // Code size       326 (0x146)
    .maxstack  6
    .locals init (class AttackEntity V_0,
             class BaseProjectile V_1,
             class FlameThrower V_2,
             valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4> V_3)
    IL_0000:  ldarg.0
    IL_0001:  call       instance class HeldEntity BasePlayer::GetHeldEntity()
    IL_0006:  isinst     AttackEntity
    IL_000b:  stloc.0
    IL_000c:  ldloc.0
    IL_000d:  ldnull
    IL_000e:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Equality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                      class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0013:  brfalse.s  IL_0017

    IL_0015:  ldc.i4.0
    IL_0016:  ret

    IL_0017:  ldloc.0
    IL_0018:  isinst     BaseProjectile
    IL_001d:  stloc.1
    IL_001e:  ldloc.1
    IL_001f:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Implicit(class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0024:  brfalse.s  IL_004b

    IL_0026:  ldloc.1
    IL_0027:  ldfld      class BaseProjectile/Magazine BaseProjectile::primaryMagazine
    IL_002c:  ldfld      int32 BaseProjectile/Magazine::contents
    IL_0031:  ldc.i4.0
    IL_0032:  bgt.s      IL_003c

    IL_0034:  ldloc.1
    IL_0035:  callvirt   instance void AttackEntity::ServerReload()
    IL_003a:  ldc.i4.0
    IL_003b:  ret

    IL_003c:  ldloc.1
    IL_003d:  callvirt   instance float32 AttackEntity::get_NextAttackTime()
    IL_0042:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0047:  ble.un.s   IL_006c

    IL_0049:  ldc.i4.0
    IL_004a:  ret

    IL_004b:  ldloc.0
    IL_004c:  isinst     FlameThrower
    IL_0051:  stloc.2
    IL_0052:  ldloc.2
    IL_0053:  ldnull
    IL_0054:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0059:  brfalse.s  IL_006c

    IL_005b:  ldloc.2
    IL_005c:  ldfld      int32 FlameThrower::ammo
    IL_0061:  ldc.i4.0
    IL_0062:  bgt.s      IL_006c

    IL_0064:  ldloc.2
    IL_0065:  callvirt   instance void AttackEntity::ServerReload()
    IL_006a:  ldc.i4.0
    IL_006b:  ret

    IL_006c:  ldloc.0
    IL_006d:  ldfld      float32 AttackEntity::attackLengthMin
    IL_0072:  ldc.r4     -1.
    IL_0077:  call       bool [UnityEngine.CoreModule]UnityEngine.Mathf::Approximately(float32,
                                                                                       float32)
    IL_007c:  brtrue     IL_0118

    IL_0081:  ldarg.0
    IL_0082:  ldarg.0
    IL_0083:  dup
    IL_0084:  ldvirtftn  instance void NPCPlayer::TriggerDown()
    IL_008a:  newobj     instance void [mscorlib]System.Action::.ctor(object,
                                                                      native int)
    IL_008f:  call       instance bool [Facepunch.UnityEngine]FacepunchBehaviour::IsInvoking(class [mscorlib]System.Action)
    IL_0094:  brfalse.s  IL_0098

    IL_0096:  ldc.i4.1
    IL_0097:  ret

    IL_0098:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_009d:  ldarg.0
    IL_009e:  ldfld      float32 NPCPlayer::nextTriggerTime
    IL_00a3:  bge.un.s   IL_00a7

    IL_00a5:  ldc.i4.1
    IL_00a6:  ret

    IL_00a7:  ldarg.0
    IL_00a8:  ldarg.0
    IL_00a9:  dup
    IL_00aa:  ldvirtftn  instance void NPCPlayer::TriggerDown()
    IL_00b0:  newobj     instance void [mscorlib]System.Action::.ctor(object,
                                                                      native int)
    IL_00b5:  ldc.r4     0.0
    IL_00ba:  ldc.r4     9.9999998e-03
    IL_00bf:  call       instance void [Facepunch.UnityEngine]FacepunchBehaviour::InvokeRepeating(class [mscorlib]System.Action,
                                                                                                  float32,
                                                                                                  float32)
    IL_00c4:  ldarg.1
    IL_00c5:  ldarg.0
    IL_00c6:  ldfld      float32 NPCPlayer::shortRange
    IL_00cb:  bgt.un.s   IL_00f3

    IL_00cd:  ldarg.0
    IL_00ce:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_00d3:  ldloc.0
    IL_00d4:  ldfld      float32 AttackEntity::attackLengthMin
    IL_00d9:  ldloc.0
    IL_00da:  ldfld      float32 AttackEntity::attackLengthMax
    IL_00df:  ldarg.0
    IL_00e0:  ldfld      float32 NPCPlayer::attackLengthMaxShortRangeScale
    IL_00e5:  mul
    IL_00e6:  call       float32 [UnityEngine.CoreModule]UnityEngine.Random::Range(float32,
                                                                                   float32)
    IL_00eb:  add
    IL_00ec:  stfld      float32 NPCPlayer::triggerEndTime
    IL_00f1:  br.s       IL_0110

    IL_00f3:  ldarg.0
    IL_00f4:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_00f9:  ldloc.0
    IL_00fa:  ldfld      float32 AttackEntity::attackLengthMin
    IL_00ff:  ldloc.0
    IL_0100:  ldfld      float32 AttackEntity::attackLengthMax
    IL_0105:  call       float32 [UnityEngine.CoreModule]UnityEngine.Random::Range(float32,
                                                                                   float32)
    IL_010a:  add
    IL_010b:  stfld      float32 NPCPlayer::triggerEndTime
    IL_0110:  ldarg.0
    IL_0111:  callvirt   instance void NPCPlayer::TriggerDown()
    IL_0116:  ldc.i4.1
    IL_0117:  ret

    IL_0118:  ldloc.0
    IL_0119:  ldarg.0
    IL_011a:  ldfld      float32 NPCPlayer::damageScale
    IL_011f:  ldc.r4     1.
    IL_0124:  ldloca.s   V_3
    IL_0126:  initobj    valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4>
    IL_012c:  ldloc.3
    IL_012d:  ldc.i4.1
    IL_012e:  ldc.i4.0
    IL_012f:  newobj     instance void HeldEntityServerUseParams::.ctor(float32,
                                                                        float32,
                                                                        valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4>,
                                                                        bool,
                                                                        bool)
    IL_0134:  callvirt   instance void HeldEntity::ServerUse(valuetype HeldEntityServerUseParams)
    IL_0139:  ldarg.0
    IL_013a:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_013f:  stfld      float32 NPCPlayer::lastGunShotTime
    IL_0144:  ldc.i4.1
    IL_0145:  ret
  } // end of method NPCPlayer::ShotTest

} // end of class NPCPlayer

// *********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file c:\!2XRUST\.cursor\HarmonyMods\RaidableBases\_shottest.res
