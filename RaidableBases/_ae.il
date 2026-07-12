
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit AttackEntity
       extends HeldEntity
{
  .field public float32 deployDelay
  .custom instance void [UnityEngine.CoreModule]UnityEngine.HeaderAttribute::.ctor(string) = ( 01 00 0D 41 74 74 61 63 6B 20 45 6E 74 69 74 79   // ...Attack Entity
                                                                                               00 00 ) 
  .field public float32 repeatDelay
  .field public float32 animationDelay
  .field public float32 effectiveRange
  .custom instance void [UnityEngine.CoreModule]UnityEngine.HeaderAttribute::.ctor(string) = ( 01 00 08 4E 50 43 55 73 61 67 65 00 00 )          // ...NPCUsage..
  .field public float32 npcDamageScale
  .field public float32 attackLengthMin
  .field public float32 attackLengthMax
  .field public float32 attackSpacing
  .field public float32 aiAimSwayOffset
  .field public float32 aiAimCone
  .field public bool aiOnlyInRange
  .field public float32 CloseRangeAddition
  .field public float32 MediumRangeAddition
  .field public float32 LongRangeAddition
  .field public bool CanUseAtMediumRange
  .field public bool CanUseAtLongRange
  .field public class SoundDefinition[] reloadSounds
  .field public class SoundDefinition thirdPersonMeleeSound
  .field public float32 recoilCompDelayOverride
  .custom instance void [UnityEngine.CoreModule]UnityEngine.HeaderAttribute::.ctor(string) = ( 01 00 13 52 65 63 6F 69 6C 20 43 6F 6D 70 65 6E   // ...Recoil Compen
                                                                                               73 61 74 69 6F 6E 00 00 )                         // sation..
  .field public bool wantsRecoilComp
  .field public bool showCrosshairOnTutorial
  .field public bool noHeadshots
  .field private valuetype EncryptedValue`1<float32> nextAttackTime
  .method public hidebysig newslot virtual 
          instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 
          GetInheritedVelocity(class BasePlayer player,
                               valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 direction) cil managed
  {
    // Code size       6 (0x6)
    .maxstack  8
    IL_0000:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Vector3::get_zero()
    IL_0005:  ret
  } // end of method AttackEntity::GetInheritedVelocity

  .method public hidebysig newslot virtual 
          instance float32  AmmoFraction() cil managed
  {
    // Code size       6 (0x6)
    .maxstack  8
    IL_0000:  ldc.r4     0.0
    IL_0005:  ret
  } // end of method AttackEntity::AmmoFraction

  .method public hidebysig newslot virtual 
          instance bool  CanReload() cil managed
  {
    // Code size       2 (0x2)
    .maxstack  8
    IL_0000:  ldc.i4.0
    IL_0001:  ret
  } // end of method AttackEntity::CanReload

  .method public hidebysig newslot virtual 
          instance bool  ServerIsReloading() cil managed
  {
    // Code size       2 (0x2)
    .maxstack  8
    IL_0000:  ldc.i4.0
    IL_0001:  ret
  } // end of method AttackEntity::ServerIsReloading

  .method public hidebysig newslot virtual 
          instance void  ServerReload() cil managed
  {
    // Code size       1 (0x1)
    .maxstack  8
    IL_0000:  ret
  } // end of method AttackEntity::ServerReload

  .method public hidebysig newslot virtual 
          instance bool  ServerTryReload(class IAmmoContainer ammoSource) cil managed
  {
    // Code size       2 (0x2)
    .maxstack  8
    IL_0000:  ldc.i4.1
    IL_0001:  ret
  } // end of method AttackEntity::ServerTryReload

  .method public hidebysig newslot virtual 
          instance void  TopUpAmmo() cil managed
  {
    // Code size       1 (0x1)
    .maxstack  8
    IL_0000:  ret
  } // end of method AttackEntity::TopUpAmmo

  .method public hidebysig newslot virtual 
          instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 
          ModifyAIAim(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 eulerInput,
                      [opt] float32 swayModifier) cil managed
  {
    .param [2] = float32(1.)
    // Code size       2 (0x2)
    .maxstack  8
    IL_0000:  ldarg.1
    IL_0001:  ret
  } // end of method AttackEntity::ModifyAIAim

  .method public hidebysig specialname instance float32 
          get_NextAttackTime() cil managed
  {
    // Code size       12 (0xc)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_0006:  call       !0 valuetype EncryptedValue`1<float32>::op_Implicit(valuetype EncryptedValue`1<!0>)
    IL_000b:  ret
  } // end of method AttackEntity::get_NextAttackTime

  .method public hidebysig newslot virtual 
          instance void  GetAttackStats(class HitInfo info) cil managed
  {
    // Code size       1 (0x1)
    .maxstack  8
    IL_0000:  ret
  } // end of method AttackEntity::GetAttackStats

  .method family hidebysig instance void 
          StartAttackCooldownRaw(float32 cooldown) cil managed
  {
    // Code size       19 (0x13)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0006:  ldarg.1
    IL_0007:  add
    IL_0008:  call       valuetype EncryptedValue`1<!0> valuetype EncryptedValue`1<float32>::op_Implicit(!0)
    IL_000d:  stfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_0012:  ret
  } // end of method AttackEntity::StartAttackCooldownRaw

  .method public hidebysig instance void 
          StartAttackCooldown(float32 cooldown) cil managed
  {
    // Code size       32 (0x20)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldarg.0
    IL_0002:  ldarg.0
    IL_0003:  ldfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_0008:  call       !0 valuetype EncryptedValue`1<float32>::op_Implicit(valuetype EncryptedValue`1<!0>)
    IL_000d:  ldarg.1
    IL_000e:  ldc.i4.1
    IL_000f:  ldc.i4.0
    IL_0010:  call       instance float32 AttackEntity::CalculateCooldownTime(float32,
                                                                              float32,
                                                                              bool,
                                                                              bool)
    IL_0015:  call       valuetype EncryptedValue`1<!0> valuetype EncryptedValue`1<float32>::op_Implicit(!0)
    IL_001a:  stfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_001f:  ret
  } // end of method AttackEntity::StartAttackCooldown

  .method public hidebysig instance void 
          ResetAttackCooldown() cil managed
  {
    // Code size       17 (0x11)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldc.r4     -inf
    IL_0006:  call       valuetype EncryptedValue`1<!0> valuetype EncryptedValue`1<float32>::op_Implicit(!0)
    IL_000b:  stfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_0010:  ret
  } // end of method AttackEntity::ResetAttackCooldown

  .method public hidebysig instance bool 
          HasAttackCooldown() cil managed
  {
    // Code size       19 (0x13)
    .maxstack  8
    IL_0000:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0005:  ldarg.0
    IL_0006:  ldfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_000b:  call       !0 valuetype EncryptedValue`1<float32>::op_Implicit(valuetype EncryptedValue`1<!0>)
    IL_0010:  clt
    IL_0012:  ret
  } // end of method AttackEntity::HasAttackCooldown

  .method family hidebysig instance float32 
          GetAttackCooldown() cil managed
  {
    // Code size       28 (0x1c)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_0006:  call       !0 valuetype EncryptedValue`1<float32>::op_Implicit(valuetype EncryptedValue`1<!0>)
    IL_000b:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0010:  sub
    IL_0011:  ldc.r4     0.0
    IL_0016:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Max(float32,
                                                                                float32)
    IL_001b:  ret
  } // end of method AttackEntity::GetAttackCooldown

  .method family hidebysig instance float32 
          GetAttackIdle() cil managed
  {
    // Code size       28 (0x1c)
    .maxstack  8
    IL_0000:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0005:  ldarg.0
    IL_0006:  ldfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_000b:  call       !0 valuetype EncryptedValue`1<float32>::op_Implicit(valuetype EncryptedValue`1<!0>)
    IL_0010:  sub
    IL_0011:  ldc.r4     0.0
    IL_0016:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Max(float32,
                                                                                float32)
    IL_001b:  ret
  } // end of method AttackEntity::GetAttackIdle

  .method family hidebysig instance float32 
          CalculateCooldownTime(float32 nextTime,
                                float32 cooldown,
                                bool catchup,
                                bool unscaledTime) cil managed
  {
    // Code size       163 (0xa3)
    .maxstack  3
    .locals init (float32 V_0,
             float32 V_1,
             class BasePlayer V_2)
    IL_0000:  ldarg.s    unscaledTime
    IL_0002:  brtrue.s   IL_000b

    IL_0004:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_time()
    IL_0009:  br.s       IL_0010

    IL_000b:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_unscaledTime()
    IL_0010:  stloc.0
    IL_0011:  ldc.r4     0.0
    IL_0016:  stloc.1
    IL_0017:  ldarg.0
    IL_0018:  call       instance bool BaseNetworkable::get_isServer()
    IL_001d:  brfalse.s  IL_0062

    IL_001f:  ldarg.0
    IL_0020:  call       instance class BasePlayer HeldEntity::GetOwnerPlayer()
    IL_0025:  stloc.2
    IL_0026:  ldloc.1
    IL_0027:  ldc.r4     0.1
    IL_002c:  add
    IL_002d:  stloc.1
    IL_002e:  ldloc.1
    IL_002f:  ldarg.2
    IL_0030:  ldc.r4     0.1
    IL_0035:  mul
    IL_0036:  add
    IL_0037:  stloc.1
    IL_0038:  ldloc.1
    IL_0039:  ldloc.2
    IL_003a:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Implicit(class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_003f:  brtrue.s   IL_0048

    IL_0041:  ldc.r4     0.1
    IL_0046:  br.s       IL_004e

    IL_0048:  ldloc.2
    IL_0049:  callvirt   instance float32 BasePlayer::get_desyncTimeClamped()
    IL_004e:  add
    IL_004f:  stloc.1
    IL_0050:  ldloc.1
    IL_0051:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_deltaTime()
    IL_0056:  call       float32 [UnityEngine.CoreModule]UnityEngine.Time::get_smoothDeltaTime()
    IL_005b:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Max(float32,
                                                                                float32)
    IL_0060:  add
    IL_0061:  stloc.1
    IL_0062:  ldarg.1
    IL_0063:  ldc.r4     0.0
    IL_0068:  bge.un.s   IL_007d

    IL_006a:  ldc.r4     0.0
    IL_006f:  ldloc.0
    IL_0070:  ldarg.2
    IL_0071:  add
    IL_0072:  ldloc.1
    IL_0073:  sub
    IL_0074:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Max(float32,
                                                                                float32)
    IL_0079:  starg.s    nextTime
    IL_007b:  br.s       IL_00a1

    IL_007d:  ldloc.0
    IL_007e:  ldarg.1
    IL_007f:  sub
    IL_0080:  ldloc.1
    IL_0081:  bgt.un.s   IL_0092

    IL_0083:  ldarg.1
    IL_0084:  ldarg.2
    IL_0085:  add
    IL_0086:  ldloc.0
    IL_0087:  ldarg.2
    IL_0088:  add
    IL_0089:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Min(float32,
                                                                                float32)
    IL_008e:  starg.s    nextTime
    IL_0090:  br.s       IL_00a1

    IL_0092:  ldarg.1
    IL_0093:  ldarg.2
    IL_0094:  add
    IL_0095:  ldloc.0
    IL_0096:  ldarg.2
    IL_0097:  add
    IL_0098:  ldloc.1
    IL_0099:  sub
    IL_009a:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Max(float32,
                                                                                float32)
    IL_009f:  starg.s    nextTime
    IL_00a1:  ldarg.1
    IL_00a2:  ret
  } // end of method AttackEntity::CalculateCooldownTime

  .method public hidebysig newslot virtual 
          instance int32  GetImpactEffectNumberValue(class HitInfo info) cil managed
  {
    // Code size       2 (0x2)
    .maxstack  8
    IL_0000:  ldc.i4.0
    IL_0001:  ret
  } // end of method AttackEntity::GetImpactEffectNumberValue

  .method public hidebysig newslot virtual 
          instance valuetype Effect/Type 
          GetEffectType(class HitInfo info) cil managed
  {
    // Code size       2 (0x2)
    .maxstack  8
    IL_0000:  ldc.i4.0
    IL_0001:  ret
  } // end of method AttackEntity::GetEffectType

  .method public hidebysig newslot virtual 
          instance valuetype Rust.DamageType 
          GetDamageTypeForEffect(class HitInfo info) cil managed
  {
    // Code size       12 (0xc)
    .maxstack  8
    IL_0000:  ldarg.1
    IL_0001:  ldfld      class Rust.DamageTypeList HitInfo::damageTypes
    IL_0006:  callvirt   instance valuetype Rust.DamageType Rust.DamageTypeList::GetMajorityDamageType()
    IL_000b:  ret
  } // end of method AttackEntity::GetDamageTypeForEffect

  .method family hidebysig instance bool 
          VerifyClientRPC(class BasePlayer player) cil managed
  {
    // Code size       541 (0x21d)
    .maxstack  5
    .locals init (class BasePlayer V_0,
             class Item V_1)
    IL_0000:  ldarg.1
    IL_0001:  ldnull
    IL_0002:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Equality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                      class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0007:  brfalse.s  IL_0015

    IL_0009:  ldstr      "Received RPC from null player"
    IL_000e:  call       void [UnityEngine.CoreModule]UnityEngine.Debug::LogWarning(object)
    IL_0013:  ldc.i4.0
    IL_0014:  ret

    IL_0015:  ldarg.0
    IL_0016:  call       instance class BasePlayer HeldEntity::GetOwnerPlayer()
    IL_001b:  stloc.0
    IL_001c:  ldloc.0
    IL_001d:  ldnull
    IL_001e:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Equality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                      class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0023:  brfalse.s  IL_005b

    IL_0025:  ldarg.1
    IL_0026:  ldc.i4.7
    IL_0027:  ldstr      "Owner not found ("
    IL_002c:  ldarg.0
    IL_002d:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_0032:  ldstr      ")"
    IL_0037:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_003c:  ldc.i4.1
    IL_003d:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0042:  ldarg.1
    IL_0043:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0048:  ldfld      class CombatLog PlayerStatistics::combat
    IL_004d:  ldarg.1
    IL_004e:  ldarg.0
    IL_004f:  ldstr      "owner_missing"
    IL_0054:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0059:  ldc.i4.0
    IL_005a:  ret

    IL_005b:  ldloc.0
    IL_005c:  ldarg.1
    IL_005d:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0062:  brfalse.s  IL_009a

    IL_0064:  ldarg.1
    IL_0065:  ldc.i4.7
    IL_0066:  ldstr      "Player mismatch ("
    IL_006b:  ldarg.0
    IL_006c:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_0071:  ldstr      ")"
    IL_0076:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_007b:  ldc.i4.1
    IL_007c:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0081:  ldarg.1
    IL_0082:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0087:  ldfld      class CombatLog PlayerStatistics::combat
    IL_008c:  ldarg.1
    IL_008d:  ldarg.0
    IL_008e:  ldstr      "player_mismatch"
    IL_0093:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0098:  ldc.i4.0
    IL_0099:  ret

    IL_009a:  ldarg.1
    IL_009b:  callvirt   instance bool BaseCombatEntity::IsDead()
    IL_00a0:  brfalse.s  IL_00d8

    IL_00a2:  ldarg.1
    IL_00a3:  ldc.i4.7
    IL_00a4:  ldstr      "Player dead ("
    IL_00a9:  ldarg.0
    IL_00aa:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_00af:  ldstr      ")"
    IL_00b4:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_00b9:  ldc.i4.1
    IL_00ba:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_00bf:  ldarg.1
    IL_00c0:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_00c5:  ldfld      class CombatLog PlayerStatistics::combat
    IL_00ca:  ldarg.1
    IL_00cb:  ldarg.0
    IL_00cc:  ldstr      "player_dead"
    IL_00d1:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_00d6:  ldc.i4.0
    IL_00d7:  ret

    IL_00d8:  ldarg.1
    IL_00d9:  callvirt   instance bool BasePlayer::IsWounded()
    IL_00de:  brfalse.s  IL_0116

    IL_00e0:  ldarg.1
    IL_00e1:  ldc.i4.7
    IL_00e2:  ldstr      "Player down ("
    IL_00e7:  ldarg.0
    IL_00e8:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_00ed:  ldstr      ")"
    IL_00f2:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_00f7:  ldc.i4.1
    IL_00f8:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_00fd:  ldarg.1
    IL_00fe:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0103:  ldfld      class CombatLog PlayerStatistics::combat
    IL_0108:  ldarg.1
    IL_0109:  ldarg.0
    IL_010a:  ldstr      "player_down"
    IL_010f:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0114:  ldc.i4.0
    IL_0115:  ret

    IL_0116:  ldarg.1
    IL_0117:  callvirt   instance bool BasePlayer::IsSleeping()
    IL_011c:  brfalse.s  IL_0154

    IL_011e:  ldarg.1
    IL_011f:  ldc.i4.7
    IL_0120:  ldstr      "Player sleeping ("
    IL_0125:  ldarg.0
    IL_0126:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_012b:  ldstr      ")"
    IL_0130:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_0135:  ldc.i4.1
    IL_0136:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_013b:  ldarg.1
    IL_013c:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0141:  ldfld      class CombatLog PlayerStatistics::combat
    IL_0146:  ldarg.1
    IL_0147:  ldarg.0
    IL_0148:  ldstr      "player_sleeping"
    IL_014d:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0152:  ldc.i4.0
    IL_0153:  ret

    IL_0154:  ldarg.1
    IL_0155:  callvirt   instance float32 BasePlayer::get_desyncTimeRaw()
    IL_015a:  ldsfld     float32 ConVar.AntiHack::maxdesync
    IL_015f:  ble.un.s   IL_019d

    IL_0161:  ldarg.1
    IL_0162:  ldc.i4.7
    IL_0163:  ldstr      "Player stalled ({0} with {1}s)"
    IL_0168:  ldarg.0
    IL_0169:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_016e:  ldarg.1
    IL_016f:  callvirt   instance float32 BasePlayer::get_desyncTimeRaw()
    IL_0174:  box        [mscorlib]System.Single
    IL_0179:  call       string [mscorlib]System.String::Format(string,
                                                                object,
                                                                object)
    IL_017e:  ldc.i4.1
    IL_017f:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0184:  ldarg.1
    IL_0185:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_018a:  ldfld      class CombatLog PlayerStatistics::combat
    IL_018f:  ldarg.1
    IL_0190:  ldarg.0
    IL_0191:  ldstr      "player_stalled"
    IL_0196:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_019b:  ldc.i4.0
    IL_019c:  ret

    IL_019d:  ldarg.0
    IL_019e:  call       instance class Item HeldEntity::GetOwnerItem()
    IL_01a3:  stloc.1
    IL_01a4:  ldloc.1
    IL_01a5:  brtrue.s   IL_01dd

    IL_01a7:  ldarg.1
    IL_01a8:  ldc.i4.7
    IL_01a9:  ldstr      "Item not found ("
    IL_01ae:  ldarg.0
    IL_01af:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_01b4:  ldstr      ")"
    IL_01b9:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_01be:  ldc.i4.1
    IL_01bf:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_01c4:  ldarg.1
    IL_01c5:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_01ca:  ldfld      class CombatLog PlayerStatistics::combat
    IL_01cf:  ldarg.1
    IL_01d0:  ldarg.0
    IL_01d1:  ldstr      "item_missing"
    IL_01d6:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_01db:  ldc.i4.0
    IL_01dc:  ret

    IL_01dd:  ldloc.1
    IL_01de:  callvirt   instance bool Item::get_isBroken()
    IL_01e3:  brfalse.s  IL_021b

    IL_01e5:  ldarg.1
    IL_01e6:  ldc.i4.7
    IL_01e7:  ldstr      "Item broken ("
    IL_01ec:  ldarg.0
    IL_01ed:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_01f2:  ldstr      ")"
    IL_01f7:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_01fc:  ldc.i4.1
    IL_01fd:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0202:  ldarg.1
    IL_0203:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0208:  ldfld      class CombatLog PlayerStatistics::combat
    IL_020d:  ldarg.1
    IL_020e:  ldarg.0
    IL_020f:  ldstr      "item_broken"
    IL_0214:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0219:  ldc.i4.0
    IL_021a:  ret

    IL_021b:  ldc.i4.1
    IL_021c:  ret
  } // end of method AttackEntity::VerifyClientRPC

  .method family hidebysig newslot virtual 
          instance bool  VerifyClientAttack(class BasePlayer player) cil managed
  {
    // Code size       82 (0x52)
    .maxstack  5
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  call       instance bool AttackEntity::VerifyClientRPC(class BasePlayer)
    IL_0007:  brtrue.s   IL_000b

    IL_0009:  ldc.i4.0
    IL_000a:  ret

    IL_000b:  ldarg.0
    IL_000c:  call       instance bool AttackEntity::HasAttackCooldown()
    IL_0011:  brfalse.s  IL_0050

    IL_0013:  ldarg.1
    IL_0014:  ldc.i4.s   9
    IL_0016:  ldstr      "T-{0}s ({1})"
    IL_001b:  ldarg.0
    IL_001c:  call       instance float32 AttackEntity::GetAttackCooldown()
    IL_0021:  box        [mscorlib]System.Single
    IL_0026:  ldarg.0
    IL_0027:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_002c:  call       string [mscorlib]System.String::Format(string,
                                                                object,
                                                                object)
    IL_0031:  ldc.i4.1
    IL_0032:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0037:  ldarg.1
    IL_0038:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_003d:  ldfld      class CombatLog PlayerStatistics::combat
    IL_0042:  ldarg.1
    IL_0043:  ldarg.0
    IL_0044:  ldstr      "attack_cooldown"
    IL_0049:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_004e:  ldc.i4.0
    IL_004f:  ret

    IL_0050:  ldc.i4.1
    IL_0051:  ret
  } // end of method AttackEntity::VerifyClientAttack

  .method family hidebysig instance bool 
          ValidateEyePos(class BasePlayer player,
                         valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 eyePos,
                         [opt] bool checkLineOfSight) cil managed
  {
    .param [3] = bool(true)
    // Code size       958 (0x3be)
    .maxstack  11
    .locals init (bool V_0,
             string V_1,
             float32 V_2,
             float32 V_3,
             float32 V_4,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_5,
             string V_6,
             float32 V_7,
             float32 V_8,
             string V_9,
             int32 V_10,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_11,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_12,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_13,
             string V_14,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_15,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_16,
             float32 V_17,
             class [UnityEngine.PhysicsModule]UnityEngine.Collider V_18,
             string V_19,
             string V_20)
    IL_0000:  ldc.i4.1
    IL_0001:  stloc.0
    IL_0002:  ldarg.2
    IL_0003:  call       bool [Rust.Global]UnityEngine.Vector3Ex::IsNaNOrInfinity(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_0008:  brfalse.s  IL_0042

    IL_000a:  ldarg.0
    IL_000b:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_0010:  stloc.1
    IL_0011:  ldarg.1
    IL_0012:  ldc.i4.6
    IL_0013:  ldstr      "Contains NaN ("
    IL_0018:  ldloc.1
    IL_0019:  ldstr      ")"
    IL_001e:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_0023:  ldc.i4.1
    IL_0024:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0029:  ldarg.1
    IL_002a:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_002f:  ldfld      class CombatLog PlayerStatistics::combat
    IL_0034:  ldarg.1
    IL_0035:  ldarg.0
    IL_0036:  ldstr      "eye_nan"
    IL_003b:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0040:  ldc.i4.0
    IL_0041:  stloc.0
    IL_0042:  ldsfld     int32 ConVar.AntiHack::eye_protection
    IL_0047:  ldc.i4.0
    IL_0048:  ble        IL_03bc

    IL_004d:  ldsfld     int32 ConVar.AntiHack::eye_protection
    IL_0052:  ldc.i4.1
    IL_0053:  blt        IL_0107

    IL_0058:  ldarg.1
    IL_0059:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 BaseEntity::GetParentVelocity()
    IL_005e:  stloc.s    V_5
    IL_0060:  ldloca.s   V_5
    IL_0062:  call       instance float32 [UnityEngine.CoreModule]UnityEngine.Vector3::get_magnitude()
    IL_0067:  ldarg.1
    IL_0068:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 BasePlayer::GetMountVelocity()
    IL_006d:  stloc.s    V_5
    IL_006f:  ldloca.s   V_5
    IL_0071:  call       instance float32 [UnityEngine.CoreModule]UnityEngine.Vector3::get_magnitude()
    IL_0076:  add
    IL_0077:  ldarg.1
    IL_0078:  callvirt   instance bool BaseNetworkable::HasParent()
    IL_007d:  brtrue.s   IL_008e

    IL_007f:  ldarg.1
    IL_0080:  callvirt   instance bool BasePlayer::get_isMounted()
    IL_0085:  brtrue.s   IL_008e

    IL_0087:  ldc.r4     0.0
    IL_008c:  br.s       IL_0093

    IL_008e:  ldsfld     float32 ConVar.AntiHack::eye_distance_parented_mounted_forgiveness
    IL_0093:  ldarg.1
    IL_0094:  callvirt   instance float32 BasePlayer::get_estimatedSpeed()
    IL_0099:  ldc.r4     0.0
    IL_009e:  bgt.s      IL_00a7

    IL_00a0:  ldc.r4     0.0
    IL_00a5:  br.s       IL_00ac

    IL_00a7:  ldsfld     float32 ConVar.AntiHack::eye_forgiveness
    IL_00ac:  add
    IL_00ad:  stloc.2
    IL_00ae:  ldloc.2
    IL_00af:  add
    IL_00b0:  stloc.3
    IL_00b1:  ldarg.1
    IL_00b2:  ldfld      class TickHistory BasePlayer::tickHistory
    IL_00b7:  ldarg.1
    IL_00b8:  ldarg.2
    IL_00b9:  callvirt   instance float32 TickHistory::Distance(class BasePlayer,
                                                                valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_00be:  stloc.s    V_4
    IL_00c0:  ldloc.s    V_4
    IL_00c2:  ldloc.3
    IL_00c3:  ble.un.s   IL_0107

    IL_00c5:  ldarg.0
    IL_00c6:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_00cb:  stloc.s    V_6
    IL_00cd:  ldarg.1
    IL_00ce:  ldc.i4.6
    IL_00cf:  ldstr      "Distance ({0} on attack with {1}m > {2}m)"
    IL_00d4:  ldloc.s    V_6
    IL_00d6:  ldloc.s    V_4
    IL_00d8:  box        [mscorlib]System.Single
    IL_00dd:  ldloc.3
    IL_00de:  box        [mscorlib]System.Single
    IL_00e3:  call       string [mscorlib]System.String::Format(string,
                                                                object,
                                                                object,
                                                                object)
    IL_00e8:  ldc.i4.1
    IL_00e9:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_00ee:  ldarg.1
    IL_00ef:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_00f4:  ldfld      class CombatLog PlayerStatistics::combat
    IL_00f9:  ldarg.1
    IL_00fa:  ldarg.0
    IL_00fb:  ldstr      "eye_distance"
    IL_0100:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0105:  ldc.i4.0
    IL_0106:  stloc.0
    IL_0107:  ldsfld     int32 ConVar.AntiHack::eye_protection
    IL_010c:  ldc.i4.3
    IL_010d:  blt        IL_019d

    IL_0112:  ldarg.1
    IL_0113:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 BasePlayer::GetMountVelocity()
    IL_0118:  ldfld      float32 [UnityEngine.CoreModule]UnityEngine.Vector3::y
    IL_011d:  ldarg.1
    IL_011e:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 BaseEntity::GetParentVelocity()
    IL_0123:  ldfld      float32 [UnityEngine.CoreModule]UnityEngine.Vector3::y
    IL_0128:  add
    IL_0129:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Abs(float32)
    IL_012e:  call       float32 BasePlayer::GetJumpHeight()
    IL_0133:  add
    IL_0134:  stloc.s    V_7
    IL_0136:  ldarg.1
    IL_0137:  callvirt   instance class PlayerEyes BasePlayer::get_eyes()
    IL_013c:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 PlayerEyes::get_position()
    IL_0141:  ldfld      float32 [UnityEngine.CoreModule]UnityEngine.Vector3::y
    IL_0146:  ldarg.2
    IL_0147:  ldfld      float32 [UnityEngine.CoreModule]UnityEngine.Vector3::y
    IL_014c:  sub
    IL_014d:  call       float32 [UnityEngine.CoreModule]UnityEngine.Mathf::Abs(float32)
    IL_0152:  stloc.s    V_8
    IL_0154:  ldloc.s    V_8
    IL_0156:  ldloc.s    V_7
    IL_0158:  ble.un.s   IL_019d

    IL_015a:  ldarg.0
    IL_015b:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_0160:  stloc.s    V_9
    IL_0162:  ldarg.1
    IL_0163:  ldc.i4.6
    IL_0164:  ldstr      "Altitude ({0} on attack with {1}m > {2}m)"
    IL_0169:  ldloc.s    V_9
    IL_016b:  ldloc.s    V_8
    IL_016d:  box        [mscorlib]System.Single
    IL_0172:  ldloc.s    V_7
    IL_0174:  box        [mscorlib]System.Single
    IL_0179:  call       string [mscorlib]System.String::Format(string,
                                                                object,
                                                                object,
                                                                object)
    IL_017e:  ldc.i4.1
    IL_017f:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0184:  ldarg.1
    IL_0185:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_018a:  ldfld      class CombatLog PlayerStatistics::combat
    IL_018f:  ldarg.1
    IL_0190:  ldarg.0
    IL_0191:  ldstr      "eye_altitude"
    IL_0196:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_019b:  ldc.i4.0
    IL_019c:  stloc.0
    IL_019d:  ldarg.3
    IL_019e:  brfalse    IL_0386

    IL_01a3:  ldc.i4     0x210000
    IL_01a8:  stloc.s    V_10
    IL_01aa:  ldsfld     bool ConVar.AntiHack::eye_terraincheck
    IL_01af:  brfalse.s  IL_01bb

    IL_01b1:  ldloc.s    V_10
    IL_01b3:  ldc.i4     0x800000
    IL_01b8:  or
    IL_01b9:  stloc.s    V_10
    IL_01bb:  ldsfld     bool ConVar.AntiHack::eye_vehiclecheck
    IL_01c0:  brfalse.s  IL_01cc

    IL_01c2:  ldloc.s    V_10
    IL_01c4:  ldc.i4     0x8000000
    IL_01c9:  or
    IL_01ca:  stloc.s    V_10
    IL_01cc:  ldsfld     int32 ConVar.AntiHack::eye_protection
    IL_01d1:  ldc.i4.2
    IL_01d2:  blt        IL_0279

    IL_01d7:  ldarg.1
    IL_01d8:  callvirt   instance class PlayerEyes BasePlayer::get_eyes()
    IL_01dd:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 PlayerEyes::get_center()
    IL_01e2:  stloc.s    V_11
    IL_01e4:  ldarg.1
    IL_01e5:  callvirt   instance class PlayerEyes BasePlayer::get_eyes()
    IL_01ea:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 PlayerEyes::get_position()
    IL_01ef:  stloc.s    V_12
    IL_01f1:  ldarg.2
    IL_01f2:  stloc.s    V_13
    IL_01f4:  ldloc.s    V_11
    IL_01f6:  ldloc.s    V_12
    IL_01f8:  ldloc.s    V_10
    IL_01fa:  ldsfld     float32 ConVar.AntiHack::eye_losradius
    IL_01ff:  ldnull
    IL_0200:  call       bool GamePhysics::LineOfSightRadius(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                             int32,
                                                             float32,
                                                             class BaseEntity)
    IL_0205:  brfalse.s  IL_021a

    IL_0207:  ldloc.s    V_12
    IL_0209:  ldloc.s    V_13
    IL_020b:  ldloc.s    V_10
    IL_020d:  ldsfld     float32 ConVar.AntiHack::eye_losradius
    IL_0212:  ldnull
    IL_0213:  call       bool GamePhysics::LineOfSightRadius(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                             int32,
                                                             float32,
                                                             class BaseEntity)
    IL_0218:  br.s       IL_021b

    IL_021a:  ldc.i4.0
    IL_021b:  brtrue.s   IL_0279

    IL_021d:  ldarg.0
    IL_021e:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_0223:  stloc.s    V_14
    IL_0225:  ldarg.1
    IL_0226:  ldc.i4.6
    IL_0227:  ldstr      "Line of sight ({0} on attack) {1} {2} {3}"
    IL_022c:  ldc.i4.4
    IL_022d:  newarr     [mscorlib]System.Object
    IL_0232:  dup
    IL_0233:  ldc.i4.0
    IL_0234:  ldloc.s    V_14
    IL_0236:  stelem.ref
    IL_0237:  dup
    IL_0238:  ldc.i4.1
    IL_0239:  ldloc.s    V_11
    IL_023b:  box        [UnityEngine.CoreModule]UnityEngine.Vector3
    IL_0240:  stelem.ref
    IL_0241:  dup
    IL_0242:  ldc.i4.2
    IL_0243:  ldloc.s    V_12
    IL_0245:  box        [UnityEngine.CoreModule]UnityEngine.Vector3
    IL_024a:  stelem.ref
    IL_024b:  dup
    IL_024c:  ldc.i4.3
    IL_024d:  ldloc.s    V_13
    IL_024f:  box        [UnityEngine.CoreModule]UnityEngine.Vector3
    IL_0254:  stelem.ref
    IL_0255:  call       string [mscorlib]System.String::Format(string,
                                                                object[])
    IL_025a:  ldc.i4.1
    IL_025b:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0260:  ldarg.1
    IL_0261:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0266:  ldfld      class CombatLog PlayerStatistics::combat
    IL_026b:  ldarg.1
    IL_026c:  ldarg.0
    IL_026d:  ldstr      "eye_los"
    IL_0272:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0277:  ldc.i4.0
    IL_0278:  stloc.0
    IL_0279:  ldsfld     int32 ConVar.AntiHack::eye_protection
    IL_027e:  ldc.i4.4
    IL_027f:  blt        IL_0386

    IL_0284:  ldarg.1
    IL_0285:  callvirt   instance bool BaseNetworkable::HasParent()
    IL_028a:  brtrue     IL_0386

    IL_028f:  ldarg.1
    IL_0290:  callvirt   instance class PlayerEyes BasePlayer::get_eyes()
    IL_0295:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 PlayerEyes::get_position()
    IL_029a:  stloc.s    V_15
    IL_029c:  ldarg.2
    IL_029d:  stloc.s    V_16
    IL_029f:  ldloc.s    V_15
    IL_02a1:  ldloc.s    V_16
    IL_02a3:  call       float32 [UnityEngine.CoreModule]UnityEngine.Vector3::Distance(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_02a8:  stloc.s    V_17
    IL_02aa:  ldloc.s    V_17
    IL_02ac:  ldsfld     float32 ConVar.AntiHack::eye_noclip_cutoff
    IL_02b1:  ble.un.s   IL_031d

    IL_02b3:  ldarg.1
    IL_02b4:  ldloc.s    V_15
    IL_02b6:  ldloc.s    V_16
    IL_02b8:  ldsfld     float32 ConVar.AntiHack::eye_noclip_margin
    IL_02bd:  call       float32 BasePlayer::NoClipRadius(float32)
    IL_02c2:  ldsfld     float32 ConVar.AntiHack::eye_noclip_backtracking
    IL_02c7:  ldloca.s   V_18
    IL_02c9:  ldc.i4.0
    IL_02ca:  ldnull
    IL_02cb:  ldc.i4.1
    IL_02cc:  ldc.i4.0
    IL_02cd:  ldc.i4.0
    IL_02ce:  call       bool AntiHack::TestNoClipping(class BasePlayer,
                                                       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                       float32,
                                                       float32,
                                                       class [UnityEngine.PhysicsModule]UnityEngine.Collider&,
                                                       bool,
                                                       class BaseEntity,
                                                       bool,
                                                       bool,
                                                       bool)
    IL_02d3:  brfalse    IL_0386

    IL_02d8:  ldarg.0
    IL_02d9:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_02de:  stloc.s    V_19
    IL_02e0:  ldarg.1
    IL_02e1:  ldc.i4.6
    IL_02e2:  ldstr      "NoClip (cutoff) ({0} on attack) {1} {2}"
    IL_02e7:  ldloc.s    V_19
    IL_02e9:  ldloc.s    V_15
    IL_02eb:  box        [UnityEngine.CoreModule]UnityEngine.Vector3
    IL_02f0:  ldloc.s    V_16
    IL_02f2:  box        [UnityEngine.CoreModule]UnityEngine.Vector3
    IL_02f7:  call       string [mscorlib]System.String::Format(string,
                                                                object,
                                                                object,
                                                                object)
    IL_02fc:  ldc.i4.1
    IL_02fd:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_0302:  ldarg.1
    IL_0303:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0308:  ldfld      class CombatLog PlayerStatistics::combat
    IL_030d:  ldarg.1
    IL_030e:  ldarg.0
    IL_030f:  ldstr      "eye_noclip_cutoff"
    IL_0314:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0319:  ldc.i4.0
    IL_031a:  stloc.0
    IL_031b:  br.s       IL_0386

    IL_031d:  ldloc.s    V_17
    IL_031f:  ldc.r4     9.9999998e-03
    IL_0324:  ble.un.s   IL_0386

    IL_0326:  ldarg.1
    IL_0327:  ldloc.s    V_15
    IL_0329:  ldloc.s    V_16
    IL_032b:  ldc.r4     0.1
    IL_0330:  ldsfld     float32 ConVar.AntiHack::eye_noclip_backtracking
    IL_0335:  ldloca.s   V_18
    IL_0337:  ldc.i4.0
    IL_0338:  ldnull
    IL_0339:  ldc.i4.1
    IL_033a:  ldc.i4.0
    IL_033b:  ldc.i4.0
    IL_033c:  call       bool AntiHack::TestNoClipping(class BasePlayer,
                                                       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                       float32,
                                                       float32,
                                                       class [UnityEngine.PhysicsModule]UnityEngine.Collider&,
                                                       bool,
                                                       class BaseEntity,
                                                       bool,
                                                       bool,
                                                       bool)
    IL_0341:  brfalse.s  IL_0386

    IL_0343:  ldarg.0
    IL_0344:  call       instance string BaseNetworkable::get_ShortPrefabName()
    IL_0349:  stloc.s    V_20
    IL_034b:  ldarg.1
    IL_034c:  ldc.i4.6
    IL_034d:  ldstr      "NoClip ({0} on attack) {1} {2}"
    IL_0352:  ldloc.s    V_20
    IL_0354:  ldloc.s    V_15
    IL_0356:  box        [UnityEngine.CoreModule]UnityEngine.Vector3
    IL_035b:  ldloc.s    V_16
    IL_035d:  box        [UnityEngine.CoreModule]UnityEngine.Vector3
    IL_0362:  call       string [mscorlib]System.String::Format(string,
                                                                object,
                                                                object,
                                                                object)
    IL_0367:  ldc.i4.1
    IL_0368:  call       void AntiHack::Log(class BasePlayer,
                                            valuetype AntiHackType,
                                            string,
                                            bool)
    IL_036d:  ldarg.1
    IL_036e:  ldfld      class PlayerStatistics BasePlayer::stats
    IL_0373:  ldfld      class CombatLog PlayerStatistics::combat
    IL_0378:  ldarg.1
    IL_0379:  ldarg.0
    IL_037a:  ldstr      "eye_noclip"
    IL_037f:  callvirt   instance void CombatLog::LogInvalid(class BasePlayer,
                                                             class AttackEntity,
                                                             string)
    IL_0384:  ldc.i4.0
    IL_0385:  stloc.0
    IL_0386:  ldloc.0
    IL_0387:  brtrue.s   IL_0398

    IL_0389:  ldarg.1
    IL_038a:  ldc.i4.6
    IL_038b:  ldsfld     float32 ConVar.AntiHack::eye_penalty
    IL_0390:  ldnull
    IL_0391:  call       void AntiHack::AddViolation(class BasePlayer,
                                                     valuetype AntiHackType,
                                                     float32,
                                                     class [UnityEngine.CoreModule]UnityEngine.GameObject)
    IL_0396:  br.s       IL_03bc

    IL_0398:  ldsfld     int32 ConVar.AntiHack::eye_protection
    IL_039d:  ldc.i4.5
    IL_039e:  blt.s      IL_03bc

    IL_03a0:  ldarg.1
    IL_03a1:  callvirt   instance bool BaseNetworkable::HasParent()
    IL_03a6:  brtrue.s   IL_03bc

    IL_03a8:  ldarg.1
    IL_03a9:  callvirt   instance bool BasePlayer::get_isMounted()
    IL_03ae:  brtrue.s   IL_03bc

    IL_03b0:  ldarg.1
    IL_03b1:  ldfld      class [Facepunch.System]Deque`1<valuetype [UnityEngine.CoreModule]UnityEngine.Vector3> BasePlayer::eyeHistory
    IL_03b6:  ldarg.2
    IL_03b7:  callvirt   instance void class [Facepunch.System]Deque`1<valuetype [UnityEngine.CoreModule]UnityEngine.Vector3>::PushBack(!0)
    IL_03bc:  ldloc.0
    IL_03bd:  ret
  } // end of method AttackEntity::ValidateEyePos

  .method public hidebysig virtual instance void 
          OnHeldChanged() cil managed
  {
    // Code size       25 (0x19)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance void HeldEntity::OnHeldChanged()
    IL_0006:  ldarg.0
    IL_0007:  ldarg.0
    IL_0008:  ldfld      float32 AttackEntity::deployDelay
    IL_000d:  ldc.r4     0.89999998
    IL_0012:  mul
    IL_0013:  call       instance void AttackEntity::StartAttackCooldown(float32)
    IL_0018:  ret
  } // end of method AttackEntity::OnHeldChanged

  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    // Code size       103 (0x67)
    .maxstack  2
    IL_0000:  ldarg.0
    IL_0001:  ldc.r4     1.
    IL_0006:  stfld      float32 AttackEntity::deployDelay
    IL_000b:  ldarg.0
    IL_000c:  ldc.r4     0.5
    IL_0011:  stfld      float32 AttackEntity::repeatDelay
    IL_0016:  ldarg.0
    IL_0017:  ldc.r4     1.
    IL_001c:  stfld      float32 AttackEntity::effectiveRange
    IL_0021:  ldarg.0
    IL_0022:  ldc.r4     1.
    IL_0027:  stfld      float32 AttackEntity::npcDamageScale
    IL_002c:  ldarg.0
    IL_002d:  ldc.r4     -1.
    IL_0032:  stfld      float32 AttackEntity::attackLengthMin
    IL_0037:  ldarg.0
    IL_0038:  ldc.r4     -1.
    IL_003d:  stfld      float32 AttackEntity::attackLengthMax
    IL_0042:  ldarg.0
    IL_0043:  ldc.i4.1
    IL_0044:  stfld      bool AttackEntity::CanUseAtMediumRange
    IL_0049:  ldarg.0
    IL_004a:  ldc.i4.1
    IL_004b:  stfld      bool AttackEntity::CanUseAtLongRange
    IL_0050:  ldarg.0
    IL_0051:  ldc.r4     -inf
    IL_0056:  call       valuetype EncryptedValue`1<!0> valuetype EncryptedValue`1<float32>::op_Implicit(!0)
    IL_005b:  stfld      valuetype EncryptedValue`1<float32> AttackEntity::nextAttackTime
    IL_0060:  ldarg.0
    IL_0061:  call       instance void HeldEntity::.ctor()
    IL_0066:  ret
  } // end of method AttackEntity::.ctor

  .property instance float32 NextAttackTime()
  {
    .get instance float32 AttackEntity::get_NextAttackTime()
  } // end of property AttackEntity::NextAttackTime
} // end of class AttackEntity

// *********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file c:\!2XRUST\.cursor\HarmonyMods\RaidableBases\_ae.res
