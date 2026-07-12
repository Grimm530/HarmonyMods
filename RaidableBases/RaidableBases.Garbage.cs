using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Garbage

        public void RemoveHeldEntities()
        {
            foreach (var raid in Raids)
            {
                foreach (var re in raid.Entities)
                {
                    if (re is IItemContainerEntity ice && ice != null && re.OwnerID == 0uL)
                    {
                        RaidableBase.ClearInventory(ice.inventory);
                    }
                }
            }
            ItemManager.DoRemoves();
        }

        public void DespawnAll(bool inactiveOnly)
        {
            var entities = new List<BaseEntity>();
            int undoLimit = 1;

            using var tmp = Raids.ToPooledList();

            foreach (RaidableBase raid in tmp)
            {
                if (raid == null || !raid.IsPasted || inactiveOnly && (raid.intruders.Count > 0 || raid.ownerId.IsSteamId()))
                {
                    continue;
                }

                foreach (var entity in raid.Entities)
                {
                    if (!entity.IsKilled() && !raid.DespawnExceptions.Contains(entity))
                    {
                        entities.Add(entity);
                    }
                }

                raid.Entities.Clear();

                if (raid.Options.Setup.DespawnLimit > undoLimit)
                {
                    undoLimit = raid.Options.Setup.DespawnLimit;
                }

                raid.Despawn();
            }

            if (entities.Count > 0)
            {
                UndoLoop(entities, undoLimit);
            }
        }

        private void KillEntity(BaseEntity entity, UndoLoopSettings us)
        {
            if (entity.IsNull())
            {
                return;
            }

            if (entity.ShortPrefabName == "item_drop_backpack")
            {
                var backpack = entity as DroppedItemContainer;
                if (backpack == null || backpack.skinID != RB_SKIN_ID)
                {
                    return;
                }
            }

            var corpse = entity as PlayerCorpse;
            if (corpse != null)
            {
                if (corpse.skinID != RB_SKIN_ID)
                {
                    return;
                }
                corpse.blockBagDrop = true;
            }

            if (!us.DespawnMounts)
            {
                var m = entity.GetParentEntity() as BaseMountable ?? entity as BaseMountable;
                if (m != null && RaidableBase.AnyMounted(m))
                {
                    if (m.skinID == RB_SKIN_ID) m.skinID = 0;
                    return;
                }
                if (IsCustomEntity(entity))
                {
                    return;
                }
            }

            if (entity.OwnerID.IsSteamId() && (entity.PrefabName.Contains("building") ? us.KeepStructures : us.KeepDeployables))
            {
                return;
            }

            if (!(entity is LiquidContainer))
            {
                IInventoryProvider provider = entity as IInventoryProvider;
                if (provider != null)
                {
                    using var containers = DisposableList<ItemContainer>();
                    provider.GetAllInventories(containers);
                    bool doRemoves = false;
                    foreach (var container in containers)
                    {
                        if (container?.itemList?.Count > 0)
                        {
                            if (entity.OwnerID.IsSteamId())
                            {
                                DropLoot(entity, container, BuoyantBox);
                            }
                            else
                            {
                                container.Clear();
                                doRemoves = true;
                            }
                        }
                    }
                    if (doRemoves)
                    {
                        ItemManager.DoRemoves();
                    }
                }
            }

            var io = entity as IOEntity;
            if (io != null)
            {
                var ss = io as SamSite;
                if (ss != null)
                {
                    ss.staticRespawn = false;
                }
                var turret = io as AutoTurret;
                if (turret != null)
                {
                    AutoTurret.interferenceUpdateList.Remove(turret);
                }
                try { io.ClearConnections(); } catch { }
            }
            
            entity.SafelyKill();
        }

        private DroppedItemContainer DropLoot(BaseEntity ent, ItemContainer container, bool buoyant)
        {
            try
            {
                string prefab = buoyant ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab";
                Vector3 position = ent.CenterPoint();
                if (ent.skinID == 102201)
                {
                    position.y = Mathf.Max(position.y, TerrainMeta.HeightMap.GetHeight(position)) + 0.02f;
                }
                return container.Drop(prefab, position, ent.transform.rotation, 0f);
            }
            catch
            {
                return null;
            }
        }

        private UndoLoopSettings UndoSettings = new();

        private UndoLoopComparer UndoComparer = new();

        private TreeLoopComparer TreeComparer = new();

        public class UndoLoopSettings
        {
            public bool LogToFile, DespawnMounts, KeepStructures, KeepDeployables;
            public UndoLoopSettings() { }
            public UndoLoopSettings(ManagementSettings ms, bool logToFile) => (LogToFile, DespawnMounts, KeepStructures, KeepDeployables) = (logToFile, ms.DespawnMounts, ms.KeepStructures, ms.KeepDeployables);
        }

        public class UndoLoopComparer : IComparer<BaseNetworkable>
        {
            public Dictionary<string, ItemDefinition> DeployableItems;
            public Func<BaseEntity, bool, bool> IsBox;

            private int Evaluate(BaseNetworkable entity) => entity switch
            {
                AutoTurret => 0,
                WeaponRack => -1,
                IceFence or SimpleBuildingBlock => 6,
                BuildingBlock => 5,
                _ when DeployableItems.ContainsKey(entity.PrefabName) => 4,
                StorageContainer sc when IsBox(sc, true) => 3,
                IOEntity io when !IsBox(io, true) => 2,
                BaseVehicle => 1,
                _ => 4
            };

            public int Compare(BaseNetworkable x, BaseNetworkable y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                return Evaluate(x).CompareTo(Evaluate(y));
            }
        }

        public class TreeLoopComparer : IComparer<BaseNetworkable>
        {
            private int Evaluate(BaseNetworkable entity) => entity switch
            {
                VineSwingingTree => 2,
                TreeEntity => 1,
                NaturalBeehive => 0,
                _ => 9
            };

            public int Compare(BaseNetworkable x, BaseNetworkable y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                return Evaluate(x).CompareTo(Evaluate(y));
            }
        }

        public void UndoLoop(List<BaseEntity> entities, int limit, object[] hookObjects = null)
        {
            if (entities != null && entities.Count > 0)
            {
                ServerMgr.Instance.StartCoroutine(UndoLoopCo(entities, limit, hookObjects));
            }
        }

        private IEnumerator UndoLoopCo(List<BaseEntity> entities, int limit, object[] hookObjects)
        {
            entities.RemoveAll(entity => entity.IsKilled() || (entity.HasParent() && entity.GetParentEntity() is Tugboat));

            entities.Sort(UndoComparer);

            WaitForSeconds instruction = CoroutineEx.waitForSeconds(0.1f);

            int threshold = limit;

            int checks = 0;

            while (entities.Count > 0)
            {
                if (++checks >= threshold)
                {
                    checks = 0;
                    threshold = Performance.report.frameRate < 15 ? 1 : limit;
                    yield return instruction;
                }

                BaseEntity entity = entities[0];

                entities.RemoveAt(0);

                KillEntity(entity, UndoSettings);
            }

            if (hookObjects != null && hookObjects.Length > 0)
            {
                if (UndoSettings.LogToFile)
                {
                    LogToFile("despawn", $"{DateTime.Now} Despawn completed {hookObjects[0]}", this, true);
                }
                Interface.CallHook("OnRaidableBaseDespawned", hookObjects);
            }
        }

        #endregion Garbage

    }
}
