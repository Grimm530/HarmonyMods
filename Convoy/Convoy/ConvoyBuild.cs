using System;
using System.Reflection;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Entity spawn / parenting / rigidbody helpers ported from the Oxide Convoy BuildManager.
    /// Kept minimal: the Harmony port drives vehicles kinematically, so we only need spawn + parent + health + rigidbody access.
    /// </summary>
    public static class ConvoyBuild
    {
        public static BaseEntity CreateEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId, bool enableSaving)
        {
            if (string.IsNullOrEmpty(prefabName) || GameManager.server == null) return null;
            BaseEntity entity = GameManager.server.CreateEntity(prefabName, position, rotation);
            if (entity == null) return null;
            entity.enableSaving = enableSaving;
            entity.skinID = skinId;
            return entity;
        }

        public static BaseEntity SpawnRegularEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0, bool enableSaving = false)
        {
            BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, enableSaving);
            if (entity == null) return null;
            entity.Spawn();
            return entity;
        }

        public static BaseEntity SpawnChildEntity(BaseEntity parentEntity, string prefabName, Vector3 localPosition, Vector3 localEuler, ulong skinId = 0)
        {
            if (parentEntity == null) return null;
            BaseEntity entity = CreateEntity(prefabName, parentEntity.transform.position, Quaternion.identity, skinId, false);
            if (entity == null) return null;

            SetParent(parentEntity, entity, localPosition, localEuler);
            DestroyGroundComponents(entity);
            entity.Spawn();
            return entity;
        }

        public static void SetParent(BaseEntity parentEntity, BaseEntity childEntity, Vector3 localPosition, Vector3 localEuler)
        {
            childEntity.SetParent(parentEntity, true);
            childEntity.transform.localPosition = localPosition;
            childEntity.transform.localEulerAngles = localEuler;
        }

        public static void UpdateEntityMaxHealth(BaseCombatEntity entity, float maxHealth)
        {
            if (entity == null || maxHealth <= 0f) return;
            entity.startHealth = maxHealth;
            entity.InitializeHealth(maxHealth, maxHealth);
        }

        /// <summary>Prevent auto-kill of a parented deployable (crate/turret) when off ground.</summary>
        public static void DestroyGroundComponents(BaseEntity entity)
        {
            if (entity == null) return;
            DestroyComponent<GroundWatch>(entity);
            DestroyComponent<DestroyOnGroundMissing>(entity);
        }

        public static void DestroyComponent<T>(BaseEntity entity) where T : Component
        {
            if (entity == null) return;
            var comp = entity.GetComponent<T>();
            if (comp != null)
                UnityEngine.Object.DestroyImmediate(comp);
        }

        /// <summary>Find the drive rigidbody of a vehicle: BaseVehicle.rigidBody / BradleyAPC.myRigidBody, else search children.</summary>
        public static Rigidbody GetRigidbody(BaseEntity entity)
        {
            if (entity == null) return null;

            var bv = entity as BaseVehicle;
            if (bv != null && bv.rigidBody != null) return bv.rigidBody;

            var apc = entity as BradleyAPC;
            if (apc != null && apc.myRigidBody != null) return apc.myRigidBody;

            var field = entity.GetType().GetField("rigidBody", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? entity.GetType().GetField("myRigidBody", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var rb = field.GetValue(entity) as Rigidbody;
                if (rb != null) return rb;
            }

            return entity.GetComponentInChildren<Rigidbody>();
        }

        /// <summary>Put the vehicle on rails: kinematic rigidbody so we can move the transform without physics fighting.</summary>
        public static void SetKinematic(BaseEntity entity, bool kinematic)
        {
            var rb = GetRigidbody(entity);
            if (rb == null) return;
            try
            {
                if (kinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = kinematic;
                rb.interpolation = kinematic ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            }
            catch { }
        }

        public static object GetPrivateField(object instance, string name)
        {
            if (instance == null) return null;
            var f = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f?.GetValue(instance);
        }
    }
}
