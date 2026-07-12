
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.


// warning : THIS IS A PARTIAL DISASSEMBLY, NOT SUITABLE FOR RE-ASSEMBLING



.class public auto ansi beforefieldinit BaseProjectile
       extends AttackEntity
{
  .method public hidebysig virtual instance void 
          ServerUse(valuetype HeldEntityServerUseParams parameters) cil managed
  {
    // Code size       1655 (0x677)
    .maxstack  10
    .locals init (class BaseProjectile/'<>c__DisplayClass93_0' V_0,
             class BasePlayer V_1,
             bool V_2,
             bool V_3,
             bool V_4,
             class BaseEntity V_5,
             bool V_6,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_7,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_8,
             class Projectile V_9,
             float32 V_10,
             bool V_11,
             class BaseEntity V_12,
             class Rust.Ai.Gen2.BaseNPC2 V_13,
             class NPCPlayer V_14,
             valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4 V_15,
             int32 V_16,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_17,
             float32 V_18,
             class [mscorlib]System.Collections.Generic.List`1<valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit> V_19,
             valuetype [UnityEngine.CoreModule]UnityEngine.Ray V_20,
             float32 V_21,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_22,
             int32 V_23,
             valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit V_24,
             class BaseEntity V_25,
             class BasePlayer V_26,
             class Shield V_27,
             class ColliderInfo V_28,
             class BaseCombatEntity V_29,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_30,
             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 V_31,
             class BaseProjectile/'<>c__DisplayClass93_1' V_32,
             float32 V_33)
    IL_0000:  newobj     instance void BaseProjectile/'<>c__DisplayClass93_0'::.ctor()
    IL_0005:  stloc.0
    IL_0006:  ldarg.0
    IL_0007:  call       instance bool BaseNetworkable::get_isClient()
    IL_000c:  brfalse.s  IL_000f

    IL_000e:  ret

    IL_000f:  ldarg.0
    IL_0010:  call       instance bool AttackEntity::HasAttackCooldown()
    IL_0015:  brfalse.s  IL_0018

    IL_0017:  ret

    IL_0018:  ldarg.0
    IL_0019:  call       instance class BasePlayer HeldEntity::GetOwnerPlayer()
    IL_001e:  stloc.1
    IL_001f:  ldloc.1
    IL_0020:  ldnull
    IL_0021:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_0026:  stloc.2
    IL_0027:  ldarg.0
    IL_0028:  ldfld      class BaseProjectile/Magazine BaseProjectile::primaryMagazine
    IL_002d:  ldfld      int32 BaseProjectile/Magazine::contents
    IL_0032:  ldc.i4.0
    IL_0033:  bgt.s      IL_0049

    IL_0035:  ldarg.0
    IL_0036:  ldc.i4.2
    IL_0037:  ldnull
    IL_0038:  call       instance void BaseEntity::SignalBroadcast(valuetype BaseEntity/Signal,
                                                                   class [Facepunch.Network]Network.Connection)
    IL_003d:  ldarg.0
    IL_003e:  ldc.r4     1.
    IL_0043:  call       instance void AttackEntity::StartAttackCooldownRaw(float32)
    IL_0048:  ret

    IL_0049:  ldarg.0
    IL_004a:  ldc.i4.m1
    IL_004b:  call       instance void BaseProjectile::ModifyAmmoCount(int32)
    IL_0050:  ldarg.0
    IL_0051:  ldfld      class BaseProjectile/Magazine BaseProjectile::primaryMagazine
    IL_0056:  ldfld      int32 BaseProjectile/Magazine::contents
    IL_005b:  ldc.i4.0
    IL_005c:  bge.s      IL_0065

    IL_005e:  ldarg.0
    IL_005f:  ldc.i4.0
    IL_0060:  call       instance void BaseProjectile::SetAmmoCount(int32)
    IL_0065:  ldloc.2
    IL_0066:  brfalse.s  IL_0070

    IL_0068:  ldloc.1
    IL_0069:  callvirt   instance bool BaseEntity::get_IsNpc()
    IL_006e:  br.s       IL_0071

    IL_0070:  ldc.i4.0
    IL_0071:  stloc.3
    IL_0072:  ldarg.0
    IL_0073:  ldloca.s   V_5
    IL_0075:  call       instance bool HeldEntity::TryGetOwner(class BaseEntity&)
    IL_007a:  brfalse.s  IL_0087

    IL_007c:  ldloc.s    V_5
    IL_007e:  ldloca.s   V_13
    IL_0080:  call       bool BaseNetworkableEx::Is<class Rust.Ai.Gen2.BaseNPC2>(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                 !!0&)
    IL_0085:  br.s       IL_0088

    IL_0087:  ldc.i4.0
    IL_0088:  stloc.s    V_4
    IL_008a:  ldloc.3
    IL_008b:  ldloc.s    V_4
    IL_008d:  or
    IL_008e:  stloc.s    V_6
    IL_0090:  ldloc.3
    IL_0091:  brfalse.s  IL_00c9

    IL_0093:  ldloc.1
    IL_0094:  callvirt   instance bool BasePlayer::get_isMounted()
    IL_0099:  brtrue.s   IL_00a9

    IL_009b:  ldloc.1
    IL_009c:  callvirt   instance class BaseEntity BaseNetworkable::GetParentEntity()
    IL_00a1:  ldnull
    IL_00a2:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_00a7:  brfalse.s  IL_00c9

    IL_00a9:  ldloc.1
    IL_00aa:  isinst     NPCPlayer
    IL_00af:  stloc.s    V_14
    IL_00b1:  ldloc.s    V_14
    IL_00b3:  ldnull
    IL_00b4:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_00b9:  brfalse.s  IL_00c9

    IL_00bb:  ldloc.s    V_14
    IL_00bd:  ldloc.s    V_14
    IL_00bf:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 NPCPlayer::GetAimDirection()
    IL_00c4:  callvirt   instance void NPCPlayer::SetAimDirection(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_00c9:  ldarg.0
    IL_00ca:  ldarg.0
    IL_00cb:  ldfld      float32 AttackEntity::repeatDelay
    IL_00d0:  call       instance void AttackEntity::StartAttackCooldownRaw(float32)
    IL_00d5:  ldloc.2
    IL_00d6:  brtrue.s   IL_00ea

    IL_00d8:  ldarg.0
    IL_00d9:  ldfld      class [UnityEngine.CoreModule]UnityEngine.Transform BaseProjectile::MuzzlePoint
    IL_00de:  callvirt   instance class [UnityEngine.CoreModule]UnityEngine.Transform [UnityEngine.CoreModule]UnityEngine.Component::get_transform()
    IL_00e3:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Transform::get_position()
    IL_00e8:  br.s       IL_00f5

    IL_00ea:  ldloc.1
    IL_00eb:  callvirt   instance class PlayerEyes BasePlayer::get_eyes()
    IL_00f0:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 PlayerEyes::get_position()
    IL_00f5:  stloc.s    V_7
    IL_00f7:  ldarg.0
    IL_00f8:  ldfld      class [UnityEngine.CoreModule]UnityEngine.Transform BaseProjectile::MuzzlePoint
    IL_00fd:  callvirt   instance class [UnityEngine.CoreModule]UnityEngine.Transform [UnityEngine.CoreModule]UnityEngine.Component::get_transform()
    IL_0102:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Transform::get_forward()
    IL_0107:  stloc.s    V_8
    IL_0109:  ldarga.s   parameters
    IL_010b:  ldflda     valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4> HeldEntityServerUseParams::originOverride
    IL_0110:  call       instance bool valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4>::get_HasValue()
    IL_0115:  brfalse.s  IL_014a

    IL_0117:  ldarga.s   parameters
    IL_0119:  ldflda     valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4> HeldEntityServerUseParams::originOverride
    IL_011e:  call       instance !0 valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4>::get_Value()
    IL_0123:  stloc.s    V_15
    IL_0125:  ldloca.s   V_15
    IL_0127:  call       instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Matrix4x4::GetPosition()
    IL_012c:  stloc.s    V_7
    IL_012e:  ldarga.s   parameters
    IL_0130:  ldflda     valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4> HeldEntityServerUseParams::originOverride
    IL_0135:  call       instance !0 valuetype [mscorlib]System.Nullable`1<valuetype [UnityEngine.CoreModule]UnityEngine.Matrix4x4>::get_Value()
    IL_013a:  stloc.s    V_15
    IL_013c:  ldloca.s   V_15
    IL_013e:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Vector3::get_forward()
    IL_0143:  call       instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Matrix4x4::MultiplyVector(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_0148:  stloc.s    V_8
    IL_014a:  ldloc.0
    IL_014b:  ldarg.0
    IL_014c:  ldfld      class BaseProjectile/Magazine BaseProjectile::primaryMagazine
    IL_0151:  ldfld      class ItemDefinition BaseProjectile/Magazine::ammoType
    IL_0156:  callvirt   instance !!0 [UnityEngine.CoreModule]UnityEngine.Component::GetComponent<class ItemModProjectile>()
    IL_015b:  stfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_0160:  ldarg.0
    IL_0161:  ldc.i4.0
    IL_0162:  ldsfld     string [mscorlib]System.String::Empty
    IL_0167:  ldnull
    IL_0168:  ldarg.0
    IL_0169:  call       instance string BaseProjectile::GetAttackEffect()
    IL_016e:  ldarg.0
    IL_016f:  ldfld      float32 BaseProjectile::maxAttackEffectDistance
    IL_0174:  call       instance void BaseEntity::SignalBroadcast(valuetype BaseEntity/Signal,
                                                                   string,
                                                                   class [Facepunch.Network]Network.Connection,
                                                                   string,
                                                                   float32)
    IL_0179:  ldloc.0
    IL_017a:  ldfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_017f:  ldfld      class GameObjectRef ItemModProjectile::projectileObject
    IL_0184:  callvirt   instance !0 class ResourceRef`1<class [UnityEngine.CoreModule]UnityEngine.GameObject>::Get()
    IL_0189:  callvirt   instance !!0 [UnityEngine.CoreModule]UnityEngine.GameObject::GetComponent<class Projectile>()
    IL_018e:  stloc.s    V_9
    IL_0190:  ldloc.0
    IL_0191:  ldfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_0196:  ldfld      float32 ItemModProjectile::projectileVelocity
    IL_019b:  ldarg.1
    IL_019c:  ldfld      float32 HeldEntityServerUseParams::speedModifier
    IL_01a1:  mul
    IL_01a2:  stloc.s    V_10
    IL_01a4:  ldarg.0
    IL_01a5:  call       instance class BaseEntity BaseNetworkable::GetParentEntity()
    IL_01aa:  isinst     BasePlayer
    IL_01af:  ldnull
    IL_01b0:  cgt.un
    IL_01b2:  stloc.s    V_11
    IL_01b4:  ldnull
    IL_01b5:  stloc.s    V_12
    IL_01b7:  ldloc.2
    IL_01b8:  brfalse.s  IL_01cf

    IL_01ba:  ldarg.0
    IL_01bb:  ldfld      bool HeldEntity::useOwnerForward
    IL_01c0:  brfalse.s  IL_01cf

    IL_01c2:  ldloc.1
    IL_01c3:  callvirt   instance class PlayerEyes BasePlayer::get_eyes()
    IL_01c8:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 PlayerEyes::BodyForward()
    IL_01cd:  stloc.s    V_8
    IL_01cf:  ldc.i4.0
    IL_01d0:  stloc.s    V_16
    IL_01d2:  br         IL_0664

    IL_01d7:  ldloc.3
    IL_01d8:  brfalse.s  IL_01ff

    IL_01da:  ldloc.0
    IL_01db:  ldfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_01e0:  ldfld      float32 ItemModProjectile::projectileSpread
    IL_01e5:  ldarg.0
    IL_01e6:  callvirt   instance float32 BaseProjectile::GetAimCone()
    IL_01eb:  add
    IL_01ec:  ldarg.0
    IL_01ed:  call       instance float32 BaseProjectile::GetAIAimcone()
    IL_01f2:  add
    IL_01f3:  ldloc.s    V_8
    IL_01f5:  ldc.i4.1
    IL_01f6:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 AimConeUtil::GetModifiedAimConeDirection(float32,
                                                                                                                        valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                                                        bool)
    IL_01fb:  stloc.s    V_17
    IL_01fd:  br.s       IL_0225

    IL_01ff:  ldloc.s    V_4
    IL_0201:  brfalse.s  IL_0209

    IL_0203:  ldloc.s    V_8
    IL_0205:  stloc.s    V_17
    IL_0207:  br.s       IL_0225

    IL_0209:  ldloc.0
    IL_020a:  ldfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_020f:  ldfld      float32 ItemModProjectile::projectileSpread
    IL_0214:  ldarg.0
    IL_0215:  callvirt   instance float32 BaseProjectile::GetAimCone()
    IL_021a:  add
    IL_021b:  ldloc.s    V_8
    IL_021d:  ldc.i4.1
    IL_021e:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 AimConeUtil::GetModifiedAimConeDirection(float32,
                                                                                                                        valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                                                        bool)
    IL_0223:  stloc.s    V_17
    IL_0225:  ldarg.1
    IL_0226:  ldfld      bool HeldEntityServerUseParams::useBulletThickness
    IL_022b:  brtrue.s   IL_0234

    IL_022d:  ldc.r4     0.0
    IL_0232:  br.s       IL_023c

    IL_0234:  ldarg.0
    IL_0235:  ldloc.s    V_9
    IL_0237:  callvirt   instance float32 BaseProjectile::GetOverrideProjectileThickness(class Projectile)
    IL_023c:  stloc.s    V_18
    IL_023e:  call       !!0 [Facepunch.System]Facepunch.Pool::Get<class [mscorlib]System.Collections.Generic.List`1<valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit>>()
    IL_0243:  stloc.s    V_19
    IL_0245:  ldloca.s   V_20
    IL_0247:  ldloc.s    V_7
    IL_0249:  ldloc.s    V_17
    IL_024b:  call       instance void [UnityEngine.CoreModule]UnityEngine.Ray::.ctor(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                      valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_0250:  ldloc.s    V_20
    IL_0252:  ldloc.s    V_18
    IL_0254:  ldloc.s    V_19
    IL_0256:  ldc.r4     300.
    IL_025b:  ldc.i4     0x48bb2b01
    IL_0260:  ldc.i4.1
    IL_0261:  ldloc.1
    IL_0262:  call       void GamePhysics::TraceAll(valuetype [UnityEngine.CoreModule]UnityEngine.Ray,
                                                    float32,
                                                    class [mscorlib]System.Collections.Generic.List`1<valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit>,
                                                    float32,
                                                    int32,
                                                    valuetype [UnityEngine.PhysicsModule]UnityEngine.QueryTriggerInteraction,
                                                    class BaseEntity)
    IL_0267:  ldc.r4     0.0
    IL_026c:  stloc.s    V_21
    IL_026e:  ldc.i4.0
    IL_026f:  stloc.s    V_23
    IL_0271:  br         IL_05ed

    IL_0276:  ldarg.1
    IL_0277:  ldfld      float32 HeldEntityServerUseParams::damageModifier
    IL_027c:  ldc.r4     0.0
    IL_0281:  beq        IL_05fb

    IL_0286:  ldloc.s    V_19
    IL_0288:  ldloc.s    V_23
    IL_028a:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit>::get_Item(int32)
    IL_028f:  stloc.s    V_24
    IL_0291:  ldloc.s    V_24
    IL_0293:  call       class BaseEntity RaycastHitEx::GetEntity(valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit)
    IL_0298:  stloc.s    V_25
    IL_029a:  ldloc.s    V_11
    IL_029c:  brfalse.s  IL_02c4

    IL_029e:  ldloc.s    V_25
    IL_02a0:  ldnull
    IL_02a1:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_02a6:  brfalse.s  IL_02db

    IL_02a8:  ldloc.s    V_25
    IL_02aa:  ldarg.0
    IL_02ab:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Equality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                      class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_02b0:  brtrue     IL_05e7

    IL_02b5:  ldloc.s    V_25
    IL_02b7:  ldarg.0
    IL_02b8:  callvirt   instance bool BaseNetworkable::EqualNetID(class BaseNetworkable)
    IL_02bd:  brfalse.s  IL_02db

    IL_02bf:  br         IL_05e7

    IL_02c4:  ldloc.s    V_25
    IL_02c6:  ldnull
    IL_02c7:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_02cc:  brfalse.s  IL_02db

    IL_02ce:  ldarg.0
    IL_02cf:  ldloc.s    V_25
    IL_02d1:  call       bool BaseEntityEx::HasEntityInParents(class BaseEntity,
                                                               class BaseEntity)
    IL_02d6:  brtrue     IL_05e7

    IL_02db:  ldloc.s    V_25
    IL_02dd:  isinst     BasePlayer
    IL_02e2:  stloc.s    V_26
    IL_02e4:  ldloc.s    V_26
    IL_02e6:  brfalse.s  IL_0343

    IL_02e8:  ldloc.s    V_26
    IL_02ea:  ldloca.s   V_27
    IL_02ec:  callvirt   instance bool BasePlayer::TryGetActiveShield(class Shield&)
    IL_02f1:  brfalse.s  IL_0343

    IL_02f3:  ldloc.s    V_27
    IL_02f5:  ldloc.s    V_20
    IL_02f7:  ldc.r4     300.
    IL_02fc:  callvirt   instance bool Shield::RaycastAgainstColliders(valuetype [UnityEngine.CoreModule]UnityEngine.Ray,
                                                                       float32)
    IL_0301:  brfalse.s  IL_0343

    IL_0303:  ldarg.0
    IL_0304:  call       instance class [UnityEngine.CoreModule]UnityEngine.Transform [UnityEngine.CoreModule]UnityEngine.Component::get_transform()
    IL_0309:  ldloc.s    V_27
    IL_030b:  callvirt   instance class [UnityEngine.CoreModule]UnityEngine.Transform [UnityEngine.CoreModule]UnityEngine.Component::get_transform()
    IL_0310:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Transform::get_position()
    IL_0315:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Transform::InverseTransformPoint(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_031a:  stloc.s    V_30
    IL_031c:  ldarg.0
    IL_031d:  call       instance class [UnityEngine.CoreModule]UnityEngine.Transform [UnityEngine.CoreModule]UnityEngine.Component::get_transform()
    IL_0322:  ldloc.s    V_26
    IL_0324:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 BaseEntity::CenterPoint()
    IL_0329:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Transform::InverseTransformPoint(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_032e:  stloc.s    V_31
    IL_0330:  ldloca.s   V_30
    IL_0332:  call       instance float32 [UnityEngine.CoreModule]UnityEngine.Vector3::get_sqrMagnitude()
    IL_0337:  ldloca.s   V_31
    IL_0339:  call       instance float32 [UnityEngine.CoreModule]UnityEngine.Vector3::get_sqrMagnitude()
    IL_033e:  blt        IL_05e7

    IL_0343:  ldloc.s    V_25
    IL_0345:  ldnull
    IL_0346:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_034b:  brfalse.s  IL_0359

    IL_034d:  ldloc.s    V_25
    IL_034f:  callvirt   instance bool BaseNetworkable::get_isClient()
    IL_0354:  brtrue     IL_05e7

    IL_0359:  ldloca.s   V_24
    IL_035b:  call       instance class [UnityEngine.PhysicsModule]UnityEngine.Collider [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_collider()
    IL_0360:  callvirt   instance !!0 [UnityEngine.CoreModule]UnityEngine.Component::GetComponent<class ColliderInfo>()
    IL_0365:  stloc.s    V_28
    IL_0367:  ldloc.s    V_28
    IL_0369:  ldnull
    IL_036a:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_036f:  brfalse.s  IL_037e

    IL_0371:  ldloc.s    V_28
    IL_0373:  ldc.i4.2
    IL_0374:  callvirt   instance bool ColliderInfo::HasFlag(valuetype ColliderInfo/Flags)
    IL_0379:  brfalse    IL_05e7

    IL_037e:  ldloc.s    V_25
    IL_0380:  isinst     BaseCombatEntity
    IL_0385:  stloc.s    V_29
    IL_0387:  ldloc.s    V_25
    IL_0389:  ldnull
    IL_038a:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_038f:  brfalse.s  IL_039a

    IL_0391:  ldloc.s    V_25
    IL_0393:  callvirt   instance bool BaseEntity::get_IsNpc()
    IL_0398:  br.s       IL_039b

    IL_039a:  ldc.i4.0
    IL_039b:  ldloc.s    V_6
    IL_039d:  and
    IL_039e:  brfalse.s  IL_03c3

    IL_03a0:  ldloc.s    V_29
    IL_03a2:  ldnull
    IL_03a3:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_03a8:  brfalse.s  IL_03c3

    IL_03aa:  ldloc.s    V_29
    IL_03ac:  callvirt   instance valuetype BaseCombatEntity/Faction BaseCombatEntity::GetFaction()
    IL_03b1:  ldc.i4.4
    IL_03b2:  beq.s      IL_03c3

    IL_03b4:  ldloc.s    V_25
    IL_03b6:  isinst     BasePet
    IL_03bb:  ldnull
    IL_03bc:  cgt.un
    IL_03be:  ldc.i4.0
    IL_03bf:  ceq
    IL_03c1:  br.s       IL_03c4

    IL_03c3:  ldc.i4.0
    IL_03c4:  brtrue     IL_05e7

    IL_03c9:  ldloc.s    V_25
    IL_03cb:  ldnull
    IL_03cc:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_03d1:  brfalse    IL_05e7

    IL_03d6:  ldloc.s    V_12
    IL_03d8:  ldnull
    IL_03d9:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Equality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                      class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_03de:  brtrue.s   IL_03f9

    IL_03e0:  ldloc.s    V_25
    IL_03e2:  ldloc.s    V_12
    IL_03e4:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Equality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                      class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_03e9:  brtrue.s   IL_03f9

    IL_03eb:  ldloc.s    V_25
    IL_03ed:  ldloc.s    V_12
    IL_03ef:  callvirt   instance bool BaseNetworkable::EqualNetID(class BaseNetworkable)
    IL_03f4:  brfalse    IL_05e7

    IL_03f9:  ldloc.s    V_25
    IL_03fb:  ldloc.s    V_7
    IL_03fd:  ldloca.s   V_24
    IL_03ff:  call       instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_point()
    IL_0404:  ldc.r4     300.
    IL_0409:  callvirt   instance bool BaseEntity::IsVisible(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                             valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                             float32)
    IL_040e:  brfalse    IL_05e7

    IL_0413:  newobj     instance void BaseProjectile/'<>c__DisplayClass93_1'::.ctor()
    IL_0418:  stloc.s    V_32
    IL_041a:  ldloc.s    V_32
    IL_041c:  ldloc.0
    IL_041d:  stfld      class BaseProjectile/'<>c__DisplayClass93_0' BaseProjectile/'<>c__DisplayClass93_1'::'CS$<>8__locals1'
    IL_0422:  ldloc.s    V_32
    IL_0424:  call       !!0 [Facepunch.System]Facepunch.Pool::Get<class HitInfo>()
    IL_0429:  stfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_042e:  ldarg.0
    IL_042f:  ldloc.s    V_32
    IL_0431:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0436:  call       instance void BaseProjectile::AssignInitiator(class HitInfo)
    IL_043b:  ldloc.s    V_32
    IL_043d:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0442:  ldarg.0
    IL_0443:  stfld      class AttackEntity HitInfo::Weapon
    IL_0448:  ldloc.s    V_32
    IL_044a:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_044f:  ldarg.0
    IL_0450:  call       instance class GameManager BaseNetworkable::get_gameManager()
    IL_0455:  ldarg.0
    IL_0456:  call       instance string BaseNetworkable::get_PrefabName()
    IL_045b:  callvirt   instance class [UnityEngine.CoreModule]UnityEngine.GameObject GameManager::FindPrefab(string)
    IL_0460:  callvirt   instance !!0 [UnityEngine.CoreModule]UnityEngine.GameObject::GetComponent<class AttackEntity>()
    IL_0465:  stfld      class BaseEntity HitInfo::WeaponPrefab
    IL_046a:  ldloc.s    V_32
    IL_046c:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0471:  ldc.i4.0
    IL_0472:  stfld      bool HitInfo::IsPredicting
    IL_0477:  ldloc.s    V_32
    IL_0479:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_047e:  ldloc.s    V_9
    IL_0480:  ldfld      bool Projectile::doHitEffects
    IL_0485:  stfld      bool HitInfo::DoHitEffects
    IL_048a:  ldloc.s    V_32
    IL_048c:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0491:  ldc.i4.1
    IL_0492:  stfld      bool HitInfo::DidHit
    IL_0497:  ldloc.s    V_32
    IL_0499:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_049e:  ldloc.s    V_17
    IL_04a0:  ldc.r4     300.
    IL_04a5:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Vector3::op_Multiply(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                                                                        float32)
    IL_04aa:  stfld      valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 HitInfo::ProjectileVelocity
    IL_04af:  ldloc.s    V_32
    IL_04b1:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_04b6:  ldarg.0
    IL_04b7:  ldfld      class [UnityEngine.CoreModule]UnityEngine.Transform BaseProjectile::MuzzlePoint
    IL_04bc:  callvirt   instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Transform::get_position()
    IL_04c1:  stfld      valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 HitInfo::PointStart
    IL_04c6:  ldloc.s    V_32
    IL_04c8:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_04cd:  ldloca.s   V_24
    IL_04cf:  call       instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_point()
    IL_04d4:  stfld      valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 HitInfo::PointEnd
    IL_04d9:  ldloc.s    V_32
    IL_04db:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_04e0:  ldloca.s   V_24
    IL_04e2:  call       instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_point()
    IL_04e7:  stfld      valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 HitInfo::HitPositionWorld
    IL_04ec:  ldloc.s    V_32
    IL_04ee:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_04f3:  ldloca.s   V_24
    IL_04f5:  call       instance valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_normal()
    IL_04fa:  stfld      valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 HitInfo::HitNormalWorld
    IL_04ff:  ldloc.s    V_32
    IL_0501:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0506:  ldloc.s    V_25
    IL_0508:  stfld      class BaseEntity HitInfo::HitEntity
    IL_050d:  ldloc.s    V_32
    IL_050f:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0514:  ldc.i4.1
    IL_0515:  stfld      bool HitInfo::UseProtection
    IL_051a:  ldloc.s    V_32
    IL_051c:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0521:  ldarg.1
    IL_0522:  ldfld      bool HeldEntityServerUseParams::useProtectionForNPCs
    IL_0527:  stfld      bool HitInfo::UseProtectionForNPCs
    IL_052c:  ldloca.s   V_24
    IL_052e:  call       instance float32 [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_distance()
    IL_0533:  stloc.s    V_21
    IL_0535:  ldloc.s    V_9
    IL_0537:  ldloc.s    V_32
    IL_0539:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_053e:  ldarg.0
    IL_053f:  call       instance valuetype Projectile/Modifier BaseProjectile::GetProjectileModifier()
    IL_0544:  ldc.r4     1.
    IL_0549:  callvirt   instance void Projectile::CalculateDamage(class HitInfo,
                                                                   valuetype Projectile/Modifier,
                                                                   float32)
    IL_054e:  ldloc.s    V_32
    IL_0550:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_0555:  ldfld      class Rust.DamageTypeList HitInfo::damageTypes
    IL_055a:  ldarg.0
    IL_055b:  ldc.i4.0
    IL_055c:  callvirt   instance float32 BaseProjectile::GetDamageScale(bool)
    IL_0561:  ldarg.1
    IL_0562:  ldfld      float32 HeldEntityServerUseParams::damageModifier
    IL_0567:  mul
    IL_0568:  ldloc.s    V_6
    IL_056a:  brtrue.s   IL_0574

    IL_056c:  ldarg.0
    IL_056d:  ldfld      float32 BaseProjectile::turretDamageScale
    IL_0572:  br.s       IL_057a

    IL_0574:  ldarg.0
    IL_0575:  ldfld      float32 AttackEntity::npcDamageScale
    IL_057a:  mul
    IL_057b:  callvirt   instance void Rust.DamageTypeList::ScaleAll(float32)
    IL_0580:  ldloc.s    V_10
    IL_0582:  ldc.r4     0.0
    IL_0587:  bgt.s      IL_0590

    IL_0589:  ldc.r4     0.0
    IL_058e:  br.s       IL_059a

    IL_0590:  ldloca.s   V_24
    IL_0592:  call       instance float32 [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_distance()
    IL_0597:  ldloc.s    V_10
    IL_0599:  div
    IL_059a:  stloc.s    V_33
    IL_059c:  ldloc.s    V_33
    IL_059e:  ldc.r4     0.2
    IL_05a3:  ble.un.s   IL_05bc

    IL_05a5:  ldarg.0
    IL_05a6:  ldloc.s    V_32
    IL_05a8:  ldftn      instance void BaseProjectile/'<>c__DisplayClass93_1'::'<ServerUse>b__0'()
    IL_05ae:  newobj     instance void [mscorlib]System.Action::.ctor(object,
                                                                      native int)
    IL_05b3:  ldloc.s    V_33
    IL_05b5:  call       instance void [Facepunch.UnityEngine]FacepunchBehaviour::Invoke(class [mscorlib]System.Action,
                                                                                         float32)
    IL_05ba:  br.s       IL_05d4

    IL_05bc:  ldloc.s    V_32
    IL_05be:  ldfld      class HitInfo BaseProjectile/'<>c__DisplayClass93_1'::info
    IL_05c3:  ldloc.s    V_32
    IL_05c5:  ldfld      class BaseProjectile/'<>c__DisplayClass93_0' BaseProjectile/'<>c__DisplayClass93_1'::'CS$<>8__locals1'
    IL_05ca:  ldfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_05cf:  call       void BaseProjectile::'<ServerUse>g__ProcessHit|93_1'(class HitInfo,
                                                                              class ItemModProjectile)
    IL_05d4:  ldloc.s    V_25
    IL_05d6:  ldnull
    IL_05d7:  call       bool [UnityEngine.CoreModule]UnityEngine.Object::op_Inequality(class [UnityEngine.CoreModule]UnityEngine.Object,
                                                                                        class [UnityEngine.CoreModule]UnityEngine.Object)
    IL_05dc:  brfalse.s  IL_05fb

    IL_05de:  ldloc.s    V_25
    IL_05e0:  callvirt   instance bool BaseEntity::ShouldBlockProjectiles()
    IL_05e5:  brtrue.s   IL_05fb

    IL_05e7:  ldloc.s    V_23
    IL_05e9:  ldc.i4.1
    IL_05ea:  add
    IL_05eb:  stloc.s    V_23
    IL_05ed:  ldloc.s    V_23
    IL_05ef:  ldloc.s    V_19
    IL_05f1:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit>::get_Count()
    IL_05f6:  blt        IL_0276

    IL_05fb:  ldloca.s   V_19
    IL_05fd:  call       void [Facepunch.System]Facepunch.Pool::FreeUnmanaged<valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit>(class [mscorlib]System.Collections.Generic.List`1<!!0>&)
    IL_0602:  ldloc.2
    IL_0603:  brfalse.s  IL_060d

    IL_0605:  ldloc.1
    IL_0606:  callvirt   instance bool BasePlayer::get_isMounted()
    IL_060b:  brtrue.s   IL_0614

    IL_060d:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Vector3::get_zero()
    IL_0612:  br.s       IL_0620

    IL_0614:  ldloc.s    V_17
    IL_0616:  ldc.r4     6.
    IL_061b:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Vector3::op_Multiply(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                                                                        float32)
    IL_0620:  stloc.s    V_22
    IL_0622:  ldarg.0
    IL_0623:  ldloc.0
    IL_0624:  ldfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_0629:  ldarg.0
    IL_062a:  callvirt   instance class GameObjectRef ItemModProjectile::GetOverrideProjectile(class BaseEntity)
    IL_062f:  callvirt   instance string class ResourceRef`1<class [UnityEngine.CoreModule]UnityEngine.GameObject>::get_resourcePath()
    IL_0634:  ldloc.s    V_7
    IL_0636:  ldloc.s    V_22
    IL_0638:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Vector3::op_Addition(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                                                                        valuetype [UnityEngine.CoreModule]UnityEngine.Vector3)
    IL_063d:  ldloc.s    V_17
    IL_063f:  ldloc.s    V_10
    IL_0641:  call       valuetype [UnityEngine.CoreModule]UnityEngine.Vector3 [UnityEngine.CoreModule]UnityEngine.Vector3::op_Multiply(valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                                                                        float32)
    IL_0646:  ldc.i4.1
    IL_0647:  ldc.i4.s   100
    IL_0649:  call       int32 [UnityEngine.CoreModule]UnityEngine.Random::Range(int32,
                                                                                 int32)
    IL_064e:  ldnull
    IL_064f:  ldarg.0
    IL_0650:  call       instance bool BaseProjectile::IsSilenced()
    IL_0655:  ldc.i4.1
    IL_0656:  ldnull
    IL_0657:  ldloc.s    V_21
    IL_0659:  call       instance void BaseProjectile::CreateProjectileEffectClientside(string,
                                                                                        valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                        valuetype [UnityEngine.CoreModule]UnityEngine.Vector3,
                                                                                        int32,
                                                                                        class [Facepunch.Network]Network.Connection,
                                                                                        bool,
                                                                                        bool,
                                                                                        class [mscorlib]System.Collections.Generic.List`1<class [Facepunch.Network]Network.Connection>,
                                                                                        float32)
    IL_065e:  ldloc.s    V_16
    IL_0660:  ldc.i4.1
    IL_0661:  add
    IL_0662:  stloc.s    V_16
    IL_0664:  ldloc.s    V_16
    IL_0666:  ldloc.0
    IL_0667:  ldfld      class ItemModProjectile BaseProjectile/'<>c__DisplayClass93_0'::ammoInfo
    IL_066c:  ldfld      int32 ItemModProjectile::numProjectiles
    IL_0671:  blt        IL_01d7

    IL_0676:  ret
  } // end of method BaseProjectile::ServerUse

} // end of class BaseProjectile

// *********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file c:\!2XRUST\.cursor\HarmonyMods\RaidableBases\_bpserveruse.res
