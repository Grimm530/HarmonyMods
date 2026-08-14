using Facepunch;
using Network;
using ProtoBuf;
using Rust;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core.Plugins;
using static Facepunch.Pool;

namespace KaruzaVehicles
{
    [Info("BulletProjectile", "Karuza", "1.8.0")]
    public class BulletProjectile : RustPlugin
    {
        const string DEFAULT_PROJECTILE = "assets/prefabs/npc/autoturret/sentrybullet.prefab";

        private ProjectileSystem projectileSystem;
        static BulletProjectile instance;
        static int BULLET_LAYER = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Default), LayerMask.LayerToName((int)Layer.Water), LayerMask.LayerToName((int)Layer.Deployed), LayerMask.LayerToName((int)Layer.Ragdoll), LayerMask.LayerToName((int)Layer.AI), LayerMask.LayerToName((int)Layer.Vehicle_Detailed), LayerMask.LayerToName((int)Layer.World), LayerMask.LayerToName((int)Layer.Player_Server), LayerMask.LayerToName((int)Layer.Harvestable), LayerMask.LayerToName((int)Layer.Physics_Projectile), LayerMask.LayerToName((int)Layer.Construction), LayerMask.LayerToName((int)Layer.Terrain), LayerMask.LayerToName((int)Layer.Transparent), LayerMask.LayerToName((int)Layer.Vehicle_Large), LayerMask.LayerToName((int)Layer.Tree), LayerMask.LayerToName((int)Layer.Vehicle_World));

        internal void OnServerInitialized()
        {
            instance = this;
            Pool.FillBuffer<CustomProjectile>();
            var newGo = new GameObject();
            projectileSystem = newGo.AddComponent<ProjectileSystem>();
            GameTrace.RaycastList = new List<RaycastHit>();
        }

        internal void Unload()
        {
            Pool.Get<List<CustomProjectile>>().Clear();
            GameObject.Destroy(projectileSystem.gameObject);
            GameTrace.RaycastList.Clear();
            GameTrace.RaycastList = null;
        }

        [HookMethod("ShootProjectile")]
        public void ShootProjectile(BasePlayer owner, BaseEntity weaponPrefab, Vector3 muzzlePos, Vector3 velocity, List<DamageTypeEntry> damageTypeOverrides, bool forceGroups, string ammoPrefabPathOverride = "", string impactEffect = "")
        {
            HandleProjectileShoot(owner, weaponPrefab, muzzlePos, velocity, damageTypeOverrides, forceGroups, ammoPrefabPathOverride, impactEffect);
        }

        private void HandleProjectileShoot(BasePlayer owner, BaseEntity weaponPrefab, Vector3 muzzlePos, Vector3 velocity, List<DamageTypeEntry> damageTypeOverrides, bool forceGroups, string ammoPrefabPathOverride = "", string impactEffect = "")
        {
            var projectile = Pool.Get<CustomProjectile>();
            projectile.Owner = owner;
            projectile.WeaponPrefab = weaponPrefab;
            projectile.ForceGroups = forceGroups;
            projectile.CustomEffect = impactEffect;

            projectile.ProjectilePrefabPath = DEFAULT_PROJECTILE;
            if (!string.IsNullOrEmpty(ammoPrefabPathOverride))
            {
                projectile.ProjectilePrefabPath = ammoPrefabPathOverride;
            }

            projectile.InitializeBullet(muzzlePos);

            if (damageTypeOverrides.Count > 0)
            {
                for (int i = 0; i < damageTypeOverrides.Count; i++)
                {
                    var dt = damageTypeOverrides[i];
                    projectile.DamageTypes.Add(new DamageTypeEntry()
                    {
                        type = dt.type,
                        amount = dt.amount,
                    });
                }
            }
            else
            {
                projectile.DamageTypes.Add(new DamageTypeEntry() { type = DamageType.Bullet, amount = 25 });
            }

            projectile.InitializeVelocity(velocity);
            projectile.Launch();
        }

        #region ProjectileSystem

        public class ProjectileSystem : MonoBehaviour
        {
            private static ProjectileSystem instance = null;
            public List<CustomProjectile> CustomProjectiles = new List<CustomProjectile>();

            public static Dictionary<string, uint> StringPoolCache = new Dictionary<string, uint>();

            void Awake()
            {
                if (instance == null)
                {
                    instance = this;
                    DontDestroyOnLoad(this);
                }
                else if (this != instance)
                {
                    Destroy(this);
                }
            }

            void FixedUpdate()
            {
                for (int i = CustomProjectiles.Count - 1; i >= 0; i--)
                {
                    var cp = CustomProjectiles[i];
                    if (cp.Disabled)
                    {
                        CustomProjectiles.RemoveAt(i);
                        continue;
                    }

                    cp.FixedUpdate();
                }
            }

            void OnDestroy()
            {
                if (StringPoolCache != null)
                {
                    StringPoolCache.Clear();
                    StringPoolCache = null;
                }

                if (CustomProjectiles != null)
                {
                    CustomProjectiles.Clear();
                    CustomProjectiles = null;
                }
            }
        }

        public class CustomProjectile : IPooled
        {
            static MinMax damageDistances = new MinMax(10f, 100f);
            static MinMax damageMultipliers = new MinMax(1f, 0.8f);

            const float maxDistance = float.PositiveInfinity;

            public List<DamageTypeEntry> DamageTypes = new List<DamageTypeEntry>();
            public BasePlayer Owner;
            public BaseEntity WeaponPrefab;
            public string ProjectilePrefabPath = DEFAULT_PROJECTILE;
            public bool Disabled = true;
            public bool ForceGroups;
            public string CustomEffect;

            Projectile.Modifier modifier = Projectile.Modifier.Default;
            Vector3 currentVelocity;
            Vector3 currentPosition;
            float traveledDistance;
            float traveledTime;
            Vector3 sentPosition;
            Vector3 previousPosition;
            float previousTraveledTime;
            bool isRetiring;
            HitTest hitTest;
            bool clientsideEffect;
            bool clientsideAttack;
            Vector3 initialVelocity;
            float initialDistance;
            float integrity = 1f;

            public void InitializeBullet(Vector3 location)
            {
                this.initialDistance = 0;
                this.currentPosition = location;
                Disabled = false;
                clientsideEffect = true;
                clientsideAttack = true;
                integrity = 2;
                instance.projectileSystem.CustomProjectiles.Add(this);
            }

            public void CalculateDamage(HitInfo info, Projectile.Modifier mod, float scale)
            {
                if (info == null)
                {
                    return;
                }

                float damageMultiplier = damageMultipliers.Lerp(mod.distanceOffset + mod.distanceScale * damageDistances.x, mod.distanceOffset + mod.distanceScale * damageDistances.y, info.ProjectileDistance);
                float totalDamage = scale * (mod.damageOffset + mod.damageScale * damageMultiplier);

                foreach (DamageTypeEntry damageType in this.DamageTypes)
                {
                    info.damageTypes.Add(damageType.type, damageType.amount * totalDamage);
                }
            }

            public bool isAuthoritative
            {
                get
                {
                    if (!object.ReferenceEquals(this.Owner, null) && this.Owner.IsConnected)
                    {
                        return true;
                    }

                    return false;
                }
            }

            private bool isAlive
            {
                get
                {
                    if (this.Disabled)
                    {
                        return false;
                    }

                    if (this.integrity > 1.0 / 1000.0 && this.traveledDistance < maxDistance)
                    {
                        return this.traveledTime < 8.0;
                    }

                    return false;
                }
            }

            private void Retire()
            {
                if (this.isRetiring)
                {
                    return;
                }

                this.isRetiring = true;
                this.Cleanup();
            }

            private void Cleanup()
            {
                OnDisable();
                var toFree = this;
                Pool.Free(ref toFree);
            }

            public void InitializeVelocity(Vector3 overrideVel)
            {
                this.initialVelocity = overrideVel;
                this.currentVelocity = this.initialVelocity;
            }

            protected void OnDisable()
            {
                this.currentVelocity = Vector3.zero;
                this.currentPosition = Vector3.zero;
                this.traveledDistance = 0.0f;
                this.traveledTime = 0.0f;
                this.sentPosition = Vector3.zero;
                this.previousPosition = Vector3.zero;
                this.previousTraveledTime = 0.0f;
                this.isRetiring = false;
                this.Owner = null;
                this.WeaponPrefab = null;
                this.clientsideEffect = false;
                this.clientsideAttack = false;
                this.integrity = 1f;
                this.modifier = Projectile.Modifier.Default;
                this.Disabled = true;
                this.DamageTypes.Clear();
                this.ForceGroups = false;
                this.CustomEffect = null;
            }

            public void FixedUpdate()
            {
                if (Disabled)
                {
                    return;
                }

                if (!this.isAlive)
                {
                    this.Retire();
                    return;
                }

                this.UpdateVelocity(Time.fixedDeltaTime);
            }

            private void UpdateVelocity(float deltaTime)
            {
                if (this.traveledTime != 0.0f)
                {
                    this.previousPosition = this.currentPosition;
                    this.previousTraveledTime = this.traveledTime;
                }

                if (this.traveledTime == 0.0f)
                {
                    this.sentPosition = this.previousPosition = this.currentPosition;
                }

                deltaTime *= Time.timeScale;
                this.DoMovement(deltaTime);
                this.DoVelocityUpdate(deltaTime);
            }

            private void DoVelocityUpdate(float deltaTime)
            {
                this.currentVelocity += Physics.gravity * deltaTime;
                if (!this.isAuthoritative || GamePhysics.LineOfSight(this.sentPosition, this.currentPosition, 2162688, 0.0f))
                {
                    return;
                }

                using (PlayerProjectileUpdate update = Facepunch.Pool.Get<PlayerProjectileUpdate>())
                {
                    update.curPosition = this.previousPosition;
                    update.travelTime = this.previousTraveledTime;
                    var target = RpcTarget.SendInfo("OnProjectileUpdate", new SendInfo(this.Owner.Connection));
                    this.Owner.ClientRPC(target, update.ToProtoBytes());
                    this.sentPosition = this.previousPosition;
                }
            }

            private void DoMovement(float deltaTime)
            {
                Vector3 vector3_1 = this.currentVelocity * deltaTime;
                float magnitude1 = vector3_1.magnitude;
                float num1 = 1f / magnitude1;
                Vector3 direction = vector3_1 * num1;
                bool flag = false;
                Vector3 vPosB = this.currentPosition + direction * magnitude1;

                float num2 = this.traveledTime + deltaTime;
                if (this.hitTest == null)
                {
                    this.hitTest = new HitTest();
                }
                else
                {
                    this.hitTest.Clear();
                }

                this.hitTest.AttackRay = new Ray(this.currentPosition, direction);
                this.hitTest.MaxDistance = magnitude1;
                this.hitTest.ignoreEntity = this.Owner;
                this.hitTest.Radius = 0.0f;
                this.hitTest.Forgiveness = 1;
                this.hitTest.type = this.isAuthoritative ? HitTest.Type.Projectile : HitTest.Type.ProjectileEffect;

                List<TraceInfo> list = Pool.Get<List<TraceInfo>>();
                GameTrace.TraceAll(this.hitTest, list);

                for (int index = 0; index < list.Count && this.isAlive && !flag; index++)
                {
                    if (list[index].valid)
                    {
                        list[index].UpdateHitTest(this.hitTest);
                        Vector3 vector3_2 = this.hitTest.HitPointWorld();
                        Vector3 normal = this.hitTest.HitNormalWorld();

                        float magnitude2 = (vector3_2 - this.currentPosition).magnitude;
                        float num3 = magnitude2 * num1 * deltaTime;
                        this.traveledDistance += magnitude2;
                        this.traveledTime += num3;

                        this.currentPosition = vector3_2;
                        if (this.DoHit(this.hitTest, vector3_2, normal) || this.DoWaterHit(ref this.hitTest, vector3_2))
                            flag = true;
                    }
                }

                Pool.FreeUnmanaged(ref list);
                if (!this.isAlive)
                {
                    return;
                }

                if (flag && this.traveledTime < num2)
                {
                    this.DoMovement(num2 - this.traveledTime);
                }
                else
                {
                    float magnitude2 = (vPosB - this.currentPosition).magnitude;
                    this.traveledDistance += magnitude2;
                    this.traveledTime += magnitude2 * num1 * deltaTime;
                    this.currentPosition = vPosB;
                }
            }

            private bool DoWaterHit(ref HitTest test, Vector3 targetPosition)
            {
                float height = TerrainMeta.WaterMap.GetHeight(targetPosition);
                if (height < targetPosition.y)
                {
                    return false;
                }

                Vector3 point = targetPosition;
                point.y = height;
                Vector3 normal = TerrainMeta.WaterMap.GetNormal(targetPosition);
                test.DidHit = true;
                test.HitEntity = null;
                test.HitDistance = Vector3.Distance(test.AttackRay.origin, targetPosition);
                test.HitMaterial = "Water";
                test.HitPart = 0U;
                test.HitTransform = null;
                test.HitPoint = point;
                test.HitNormal = normal;
                test.collider = null;
                test.gameObject = null;
                this.DoHit(test, point, normal);
                this.integrity = 0.0f;
                return true;
            }

            public static Attack BuildAttackMessage(HitTest test)
            {
                uint hitBone = 0;
                if (!object.ReferenceEquals(test.HitTransform, null) && !object.ReferenceEquals(test.HitEntity, null) && test.HitEntity.transform != test.HitTransform)
                {
                    if (!ProjectileSystem.StringPoolCache.ContainsKey(test.HitTransform.name))
                    {
                        ProjectileSystem.StringPoolCache[test.HitTransform.name] = StringPool.Get(test.HitTransform.name);
                    }

                    hitBone = ProjectileSystem.StringPoolCache[test.HitTransform.name];
                }

                var attack = Pool.Get<Attack>();
                attack.pointStart = test.AttackRay.origin;
                attack.pointEnd = test.AttackRay.origin + test.AttackRay.direction * test.MaxDistance;

                if (!test.DidHit)
                {
                    return attack;
                }

                if (test.HitEntity.IsValid())
                {
                    Transform transform = test.HitTransform;
                    if (!transform)
                        transform = test.HitEntity.transform;
                    attack.hitID = test.HitEntity.net.ID;
                    attack.hitBone = hitBone;
                    attack.hitPartID = test.HitPart;
                    attack.hitPositionWorld = transform.localToWorldMatrix.MultiplyPoint(test.HitPoint);
                    attack.hitPositionLocal = test.HitPoint;
                    attack.hitNormalWorld = transform.localToWorldMatrix.MultiplyVector(test.HitNormal);
                    attack.hitNormalLocal = test.HitNormal;

                    return attack;
                }

                attack.hitID.Value = 0UL;
                attack.hitBone = 0U;
                attack.hitPositionWorld = test.HitPoint;
                attack.hitPositionLocal = test.HitPoint;
                attack.hitNormalWorld = test.HitNormal;
                attack.hitNormalLocal = test.HitNormal;

                return attack;
            }

            private bool DoHit(HitTest test, Vector3 point, Vector3 normal)
            {
                bool flag = false;
                using (PlayerProjectileAttack attack = Pool.Get<PlayerProjectileAttack>())
                {
                    using (attack.playerAttack = Pool.Get<PlayerAttack>())
                    {
                        attack.playerAttack.attack = BuildAttackMessage(test);

                        HitInfo info = Pool.Get<HitInfo>();
                        info.LoadFromAttack(attack.playerAttack.attack, true);
                        info.WeaponPrefab = this.WeaponPrefab;
                        info.Initiator = this.Owner;
                        info.ProjectileDistance = this.traveledDistance;
                        info.ProjectileVelocity = this.currentVelocity;
                        info.IsPredicting = true;
                        info.DoDecals = true;
                        this.CalculateDamage(info, this.modifier, this.integrity);

                        if (object.ReferenceEquals(info.HitEntity, null) && Projectile.IsWaterMaterial(test.HitMaterial))
                        {
                            this.currentVelocity *= 0.1f;
                            this.currentPosition += this.currentVelocity.normalized * (1f / 1000f);
                            this.integrity = Mathf.Clamp01(this.integrity - 0.1f);
                            flag = true;
                        }
                        else if (object.ReferenceEquals(info.HitEntity, null))
                        {
                            this.integrity = 0.0f;
                        }
                        else
                        {
                            float resistance = info.HitEntity.PenetrationResistance(info);
                            flag = this.Refract(point, normal, resistance);
                            this.integrity = Mathf.Clamp01(this.integrity - resistance);

                            if (info.HitEntity is BasePlayer)
                            {
                                info.HitMaterial = Projectile.FleshMaterialID();
                            }
                        }

                        if (this.isAuthoritative)
                        {
                            attack.hitVelocity = this.currentVelocity;
                            attack.hitDistance = this.traveledDistance;
                            attack.travelTime = this.traveledTime;

                            var target = RpcTarget.SendInfo("OnProjectileAttack", new SendInfo(this.Owner.Connection));
                            this.Owner.ClientRPC(target, attack.ToProtoBytes());
                            this.sentPosition = this.currentPosition;
                        }

                        if (this.clientsideAttack && !object.ReferenceEquals(info.HitEntity, null))
                        {
                            info.HitEntity.OnAttacked(info);
                        }

                        if (this.clientsideEffect)
                        {
                            Effect.server.ImpactEffect(info, CustomEffect);
                        }
                    }
                }

                return flag;
            }

            private bool Refract(Vector3 point, Vector3 normal, float resistance)
            {
                if (resistance >= 1.0)
                {
                    return false;
                }

                var maxResistance = Mathf.Lerp(1f, 0.5f, resistance);
                if (maxResistance <= 0.0f)
                {
                    return false;
                }

                this.currentVelocity = this.RefractVector(this.currentVelocity, (Quaternion.LookRotation(normal) * Vector3.forward).normalized, resistance) * maxResistance;
                this.currentPosition += this.currentVelocity.normalized * 0.001f;

                return true;
            }

            private Vector3 RefractVector(Vector3 v, Vector3 n, float f)
            {
                float magnitude = v.magnitude;
                return Vector3.Slerp(v / magnitude, -n, f) * magnitude;
            }

            internal void Launch()
            {
                var reusableInstance = Effect.reusableInstance;

                reusableInstance.Init(Effect.Type.Projectile, this.currentPosition, this.currentVelocity, null);
                reusableInstance.scale = 1f;

                if (ForceGroups)
                {
                    reusableInstance.targets = Owner.net.group.subscribers;
                }

                if (!ProjectileSystem.StringPoolCache.ContainsKey(ProjectilePrefabPath))
                {
                    ProjectileSystem.StringPoolCache[ProjectilePrefabPath] = StringPool.Get(ProjectilePrefabPath);
                }

                reusableInstance.pooledstringid = ProjectileSystem.StringPoolCache[ProjectilePrefabPath];
                EffectNetwork.Send(reusableInstance);

                while (this.isAlive && (this.traveledDistance < this.initialDistance || this.traveledTime < 0.100000001490116f))
                {
                    this.UpdateVelocity(Time.fixedDeltaTime);
                }
            }

            public void EnterPool()
            {
            }

            public void LeavePool()
            {
            }
        }

        public static class GameTrace
        {
            public static List<RaycastHit> RaycastList = new List<RaycastHit>();

            public static void TraceAll(HitTest test, List<TraceInfo> traces, int layerMask = -1)
            {
                if (layerMask == -1)
                {
                    layerMask = BULLET_LAYER;
                }

                var list = RaycastList;
                var origin = test.AttackRay.origin;
                var direction = test.AttackRay.direction;
                var maxDistance = test.MaxDistance;
                var radius = test.Radius;
                if ((layerMask & 16384) != 0)
                {
                    layerMask &= -16385;
                    GamePhysics.TraceAllUnordered(new Ray(origin - direction * 5f, direction), radius, list, maxDistance + 5f, 16384, QueryTriggerInteraction.UseGlobal);
                    for (int index = 0; index < list.Count; index++)
                    {
                        RaycastHit raycastHit = list[index];
                        raycastHit.distance -= 5f;
                        list[index] = raycastHit;
                    }
                }

                GamePhysics.TraceAllUnordered(new Ray(origin, direction), radius, list, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

                for (int index = 0; index < list.Count; index++)
                {
                    var hit = list[index];
                    var collider = hit.GetCollider();
                    if (object.ReferenceEquals(collider, null) || collider.isTrigger)
                    {
                        continue;
                    }

                    var colliderInfo = collider.GetComponent<ColliderInfo>();
                    if (hit.distance > 0.0f && (object.ReferenceEquals(colliderInfo, null) || colliderInfo.Filter(test)))
                    {
                        TraceInfo traceInfo = new TraceInfo()
                        {
                            valid = true,
                            distance = hit.distance,
                            partID = 0,
                            point = hit.point,
                            normal = hit.normal,
                            collider = collider,
                            material = collider.GetMaterialAt(hit.point),
                            entity = collider.gameObject.ToBaseEntity()
                        };

                        traceInfo.bone = GetTransform(collider.transform, hit.point, traceInfo.entity);

                        if (object.ReferenceEquals(traceInfo.entity, null) || traceInfo.entity != test.ignoreEntity)
                        {
                            traces.Add(traceInfo);
                        }
                    }
                }

                traces.Sort((a, b) => a.distance.CompareTo(b.distance));
                list.Clear();
            }

            public static Transform GetTransform(Transform bone, Vector3 position, BaseEntity entity)
            {
                if (!object.ReferenceEquals(bone.gameObject.GetComponentInParent<Model>(), null))
                {
                    return bone;
                }

                if (object.ReferenceEquals(entity, null))
                {
                    return null;
                }

                if (entity.model && entity.model.rootBone)
                {
                    return entity.model.FindClosestBone(position);
                }

                return entity.transform;
            }
        }

        public struct TraceInfo
        {
            public bool valid;
            public float distance;
            public BaseEntity entity;
            public Vector3 point;
            public Vector3 normal;
            public Transform bone;
            public PhysicsMaterial material;
            public uint partID;
            public Collider collider;

            public void UpdateHitTest(HitTest test)
            {
                test.DidHit = true;
                test.HitEntity = this.entity;
                test.HitDistance = this.distance;
                test.HitMaterial = this.material != null ? this.material.GetName() : "generic";
                test.HitPart = this.partID;
                test.HitTransform = this.bone;
                test.HitPoint = this.point;
                test.HitNormal = this.normal;
                test.collider = this.collider;
                test.gameObject = !object.ReferenceEquals(this.collider, null) ? this.collider.gameObject : test.HitTransform.gameObject;

                if (test.HitTransform == null)
                {
                    return;
                }

                test.HitPoint = test.HitTransform.InverseTransformPoint(this.point);
                test.HitNormal = test.HitTransform.InverseTransformDirection(this.normal);
            }
        }
        #endregion

    }
}
