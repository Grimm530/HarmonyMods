using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IndustrialTransferSpeed
{
    public static class ComposterStorageAdaptor
    {
        private const string AdapterPrefabPath = "assets/prefabs/deployable/playerioents/industrialadaptors/storageadaptor.deployed.prefab";
        private const string AdapterDeployEffectPath = "assets/prefabs/deployable/playerioents/industrialconveyor/effects/industrial-conveyor-deploy.prefab";

        // Current Rust keeps these caches private; clear via reflection after reparent.
        private static readonly FieldInfo CachedParentField = AccessTools.Field(typeof(IndustrialStorageAdaptor), "_cachedParent");
        private static readonly FieldInfo CachedContainerField = AccessTools.Field(typeof(IndustrialStorageAdaptor), "cachedContainer");

        private static readonly Dictionary<uint, SlotTransform> SlotTransforms = new Dictionary<uint, SlotTransform>
        {
            { 1162882237, new SlotTransform(new Vector3(0.0f, 0.20f, 0.32f), Quaternion.Euler(0f, 360f, 0f)) },
            { 467313155, new SlotTransform(new Vector3(0.0f, 0.20f, 0.32f), Quaternion.Euler(0f, 360f, 0f)) },
            { 375169930, new SlotTransform(new Vector3(0.34f, 0.28f, 0.0f), Quaternion.Euler(0f, 90f, 0f)) },
            { 115096413, new SlotTransform(new Vector3(0.0f, 0.26f, 0.33f), Quaternion.Euler(0f, 360f, 0f)) },
            { 3449130218, new SlotTransform(new Vector3(0.36f, 0.18f, 0.0f), Quaternion.Euler(0f, 90f, 0f)) },
            { 47518702, new SlotTransform(new Vector3(0.0f, 0.66f, 0.32f), Quaternion.Euler(0f, 0f, 0f)) },
            { 2846319393, new SlotTransform(new Vector3(0.0f, 0.46f, 0.39f), Quaternion.Euler(0f, 0f, 0f)) },
            { 2685133268, new SlotTransform(new Vector3(0.0f, 0.18f, -0.1f), Quaternion.Euler(90f, 0f, 0f)) },
            { 1921897480, new SlotTransform(new Vector3(0.0f, 0.7f, 0.62f), Quaternion.Euler(90f, 0f, 0f)) }
        };

        public static void EnsureAttached(Composter composter)
        {
            if (composter == null || composter.IsDestroyed)
            {
                return;
            }

            List<IndustrialStorageAdaptor> adaptors = GetAdaptors(composter);
            int targetCount = 1;

            for (int i = 0; i < adaptors.Count; i++)
            {
                if (i < targetCount)
                {
                    ApplyLayout(adaptors[i], composter);
                }
                else
                {
                    adaptors[i].Kill();
                }
            }

            for (int i = adaptors.Count; i < targetCount; i++)
            {
                CreateAdaptor(composter);
            }
        }

        public static void EnsureAttached(PlanterBox planter)
        {
            if (planter == null || planter.IsDestroyed)
            {
                return;
            }

            List<IndustrialStorageAdaptor> adaptors = GetAdaptors(planter);

            for (int i = 0; i < adaptors.Count; i++)
            {
                if (i == 0)
                {
                    ApplyLayout(adaptors[i], planter);
                }
                else
                {
                    adaptors[i].Kill();
                }
            }

            if (adaptors.Count == 0)
            {
                CreatePlanterAdaptor(planter);
            }

            EnsurePlanterHarvester(planter);
        }

        public static bool IsManagedComposterAdaptor(IndustrialStorageAdaptor adaptor)
        {
            return adaptor != null && adaptor.GetParentEntity() is Composter;
        }

        public static bool IsManagedAdaptor(IndustrialStorageAdaptor adaptor)
        {
            BaseEntity parent = adaptor?.GetParentEntity();
            return parent is Composter || parent is PlanterBox;
        }

        private static void CreateAdaptor(Composter composter)
        {
            IndustrialStorageAdaptor adaptor = GameManager.server.CreateEntity(AdapterPrefabPath, composter.transform.position, composter.transform.rotation, true) as IndustrialStorageAdaptor;
            if (adaptor == null)
            {
                Debug.LogWarning("[IndustrialTransferSpeed] Failed to create storage adaptor for composter.");
                return;
            }

            SpawnAndAttach(adaptor, composter);
        }

        private static void CreatePlanterAdaptor(PlanterBox planter)
        {
            IndustrialStorageAdaptor adaptor = GameManager.server.CreateEntity(AdapterPrefabPath, planter.transform.position, planter.transform.rotation, true) as IndustrialStorageAdaptor;
            if (adaptor == null)
            {
                Debug.LogWarning("[IndustrialTransferSpeed] Failed to create storage adaptor for planter.");
                return;
            }

            SpawnAndAttach(adaptor, planter);
        }

        private static void SpawnAndAttach(IndustrialStorageAdaptor adaptor, BaseEntity parent)
        {
            adaptor.enableSaving = true;
            adaptor.OwnerID = parent.OwnerID;
            adaptor.Spawn();
            DestroyGround(adaptor);
            adaptor.SetParent(parent, true, true);
            ApplyLayout(adaptor, parent);
            RunDeployEffect(adaptor);
        }

        private static void ApplyLayout(BaseEntity entity, BaseEntity parent)
        {
            SlotTransform slotTransform = GetSlotTransform(parent);

            entity.enableSaving = true;
            entity.OwnerID = parent.OwnerID;
            entity.SetParent(parent, true, true);
            DestroyGround(entity);
            entity.transform.localPosition = slotTransform.Position;
            entity.transform.localRotation = slotTransform.Rotation;
            if (entity is IOEntity ioEntity)
            {
                ApplySlotColours(ioEntity);
            }
            if (entity is IndustrialStorageAdaptor adaptor)
            {
                CachedParentField?.SetValue(adaptor, null);
                CachedContainerField?.SetValue(adaptor, null);
            }
            entity.SendNetworkUpdateImmediate();
        }

        private static SlotTransform GetSlotTransform(BaseEntity parent)
        {
            IndustrialTransferSpeedConfig config = IndustrialTransferSpeedConfig.Config;
            if (SlotTransforms.TryGetValue(parent.prefabID, out SlotTransform slotTransform))
            {
                return slotTransform;
            }

            if (parent is Composter && config.ComposterAdaptorLocalPosition?.Length == 3 && config.ComposterAdaptorLocalRotation?.Length == 3)
            {
                return new SlotTransform(
                    new Vector3(config.ComposterAdaptorLocalPosition[0], config.ComposterAdaptorLocalPosition[1], config.ComposterAdaptorLocalPosition[2]),
                    Quaternion.Euler(config.ComposterAdaptorLocalRotation[0], config.ComposterAdaptorLocalRotation[1], config.ComposterAdaptorLocalRotation[2]));
            }

            if (parent is PlanterBox && config.PlanterAdaptorLocalPosition?.Length == 3 && config.PlanterAdaptorLocalRotation?.Length == 3)
            {
                return new SlotTransform(
                    new Vector3(config.PlanterAdaptorLocalPosition[0], config.PlanterAdaptorLocalPosition[1], config.PlanterAdaptorLocalPosition[2]),
                    Quaternion.Euler(config.PlanterAdaptorLocalRotation[0], config.PlanterAdaptorLocalRotation[1], config.PlanterAdaptorLocalRotation[2]));
            }

            return new SlotTransform(Vector3.zero, Quaternion.identity);
        }

        private static void EnsurePlanterHarvester(PlanterBox planter)
        {
            PlanterIndustrialHarvester harvester = planter.gameObject.GetComponent<PlanterIndustrialHarvester>();
            if (harvester == null)
            {
                harvester = planter.gameObject.AddComponent<PlanterIndustrialHarvester>();
            }
            harvester.Init(planter);
        }

        private static void ApplySlotColours(IOEntity entity)
        {
            if (entity.inputs != null)
            {
                foreach (IOEntity.IOSlot input in entity.inputs)
                {
                    input.wireColour = WireTool.WireColour.Blue;
                }
            }

            if (entity.outputs != null)
            {
                foreach (IOEntity.IOSlot output in entity.outputs)
                {
                    output.wireColour = WireTool.WireColour.Orange;
                }
            }
        }

        private static void DestroyGround(BaseEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            Object.DestroyImmediate(entity.GetComponent<GroundWatch>());

            MeshCollider[] meshColliders = entity.GetComponentsInChildren<MeshCollider>();
            foreach (MeshCollider meshCollider in meshColliders)
            {
                Object.Destroy(meshCollider);
            }
        }

        private static void RunDeployEffect(BaseEntity entity)
        {
            if (entity != null)
            {
                Effect.server.Run(AdapterDeployEffectPath, entity.transform.position);
            }
        }

        private readonly struct SlotTransform
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            public SlotTransform(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }
        }

        private static List<IndustrialStorageAdaptor> GetAdaptors(Composter composter)
        {
            return GetAdaptors((BaseEntity)composter);
        }

        private static List<IndustrialStorageAdaptor> GetAdaptors(PlanterBox planter)
        {
            return GetAdaptors((BaseEntity)planter);
        }

        private static List<IndustrialStorageAdaptor> GetAdaptors(BaseEntity parent)
        {
            List<IndustrialStorageAdaptor> adaptors = new List<IndustrialStorageAdaptor>();
            if (parent.children == null)
            {
                return adaptors;
            }

            foreach (BaseEntity child in parent.children)
            {
                if (child is IndustrialStorageAdaptor adaptor)
                {
                    adaptors.Add(adaptor);
                }
            }

            return adaptors;
        }
    }
}
