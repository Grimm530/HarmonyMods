using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoorFramesHarmony
{
    public sealed class DoorFramesPlugin
    {
        private readonly Dictionary<string, string> _doorTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"door.double.hinged.wood", "Wooden Double Door"},
            {"door.double.hinged.metal", "Metal Double Door"},
            {"door.double.hinged.toptier", "Armored Double Door"},
            {"wall.frame.garagedoor", "Garage Door"},
            {"medieval.door.double.hinged.metal", "Medieval Metal Door"},
            {"wall.frame.fence.gate", "Chainlink Fence Gate"},
            {"wall.frame.fence", "Chainlink Fence"},
            {"wall.frame.shopfront", "Wooden Shop Front"},
            {"wall.frame.shopfront.metal", "Metal Shop Front"},
            {"door.double.hinged.bardoors", "Wooden Bar Doors"},
            {"wall.frame.cell.gate", "Prison Cell Gate"},
            {"wall.frame.cell", "Prison Cell Wall"}
        };

        private static readonly string[] OccupancyShortnameKeywords =
        {
            "door.double.hinged.wood",
            "door.double.hinged.metal",
            "door.double.hinged.toptier",
            "wall.frame.garagedoor",
            "medieval.door.double.hinged.metal",
            "wall.frame.fence.gate",
            "wall.frame.fence",
            "wall.frame.shopfront",
            "wall.frame.shopfront.metal",
            "door.double.hinged.bardoors",
            "wall.frame.cell.gate",
            "wall.frame.cell"
        };

        private static readonly string[] AllPermissions =
        {
            "doorframes.all", "doorframes.fence", "doorframes.wood", "doorframes.metal",
            "doorframes.armored", "doorframes.garage", "doorframes.shopfront", "doorframes.bardoors",
            "doorframes.prison", "doorframes.rotate", "doorframes.rotate.all"
        };

        private const float RAYCAST_DISTANCE = 7f;
        private const float SPHERECAST_RADIUS = 1.3f;
        private const float FRAME_PERSIST_SECONDS = 4.0f;
        private const float FRAME_COOLDOWN_SECONDS = 0.5f;
        private const float INPUT_CHECK_INTERVAL = 0.2f;
        private const string AdminGroup = "admin";

        private readonly Dictionary<NetworkableId, float> _frameCooldowns = new Dictionary<NetworkableId, float>();
        private readonly Dictionary<ulong, float> _lastRaycastTime = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastInputCheck = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastIndicatorTime = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, BuildingBlock> _lastFrame = new Dictionary<ulong, BuildingBlock>();
        private readonly Dictionary<ulong, float> _lastFrameTime = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> _holdingDoorItem = new HashSet<ulong>();

        public bool IsHoldingDoorItem(ulong userId) => _holdingDoorItem.Contains(userId);

        public void RegisterPermissions()
        {
            for (int i = 0; i < AllPermissions.Length; i++)
                PermissionsBridge.RegisterPermission(AllPermissions[i]);

            if (!PermissionsBridge.IsAvailable) return;
            if (!PermissionsBridge.GroupExists(AdminGroup))
                PermissionsBridge.CreateGroup(AdminGroup, "Administrators", 0);
            for (int i = 0; i < AllPermissions.Length; i++)
                PermissionsBridge.GrantGroupPermission(AdminGroup, AllPermissions[i]);
        }

        public void CleanCooldowns()
        {
            float currentTime = Time.realtimeSinceStartup;
            var stale = new List<NetworkableId>();
            foreach (var kv in _frameCooldowns)
            {
                if (currentTime - kv.Value > FRAME_COOLDOWN_SECONDS * 5)
                    stale.Add(kv.Key);
            }
            for (int i = 0; i < stale.Count; i++)
                _frameCooldowns.Remove(stale[i]);
            _lastRaycastTime.Clear();
        }

        private bool CheckPermissions(BasePlayer player, string doorType)
        {
            string id = player.UserIDString;
            if (PermissionsBridge.UserHasPermission(id, "doorframes.all"))
                return true;

            switch (doorType)
            {
                case "door.double.hinged.wood":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.wood");
                case "door.double.hinged.metal":
                case "medieval.door.double.hinged.metal":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.metal");
                case "door.double.hinged.toptier":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.armored");
                case "wall.frame.garagedoor":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.garage");
                case "wall.frame.fence.gate":
                case "wall.frame.fence":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.fence");
                case "wall.frame.shopfront":
                case "wall.frame.shopfront.metal":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.shopfront");
                case "door.double.hinged.bardoors":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.bardoors");
                case "wall.frame.cell":
                case "wall.frame.cell.gate":
                    return PermissionsBridge.UserHasPermission(id, "doorframes.prison");
                default:
                    return false;
            }
        }

        private static bool IsFrameOccupied(BuildingBlock frame)
        {
            if (frame == null) return false;
            var children = frame.GetComponentsInChildren<BaseEntity>(true);
            for (int i = 0; i < children.Length; i++)
            {
                BaseEntity entity = children[i];
                if (entity == null || entity == frame) continue;
                string name = entity.ShortPrefabName;
                if (string.IsNullOrEmpty(name)) continue;
                for (int k = 0; k < OccupancyShortnameKeywords.Length; k++)
                {
                    if (name.IndexOf(OccupancyShortnameKeywords[k], StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        public void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            if (!input.IsDown(BUTTON.FIRE_PRIMARY)) return;

            ulong uid = (ulong)player.userID;
            float currentTime = Time.realtimeSinceStartup;
            if (_lastInputCheck.TryGetValue(uid, out float lastCheck) && currentTime - lastCheck < INPUT_CHECK_INTERVAL)
                return;
            _lastInputCheck[uid] = currentTime;

            var item = player.GetActiveItem();
            if (item == null || !_doorTypes.ContainsKey(item.info.shortname)) return;

            if (_lastRaycastTime.TryGetValue(uid, out float lastTime) && currentTime - lastTime < 0.1f)
                return;
            _lastRaycastTime[uid] = currentTime;

            RaycastHit hit;
            bool hitOk = Physics.Raycast(player.eyes.HeadRay(), out hit, RAYCAST_DISTANCE);
            if (!hitOk)
            {
                hitOk = Physics.SphereCast(player.eyes.HeadRay(), SPHERECAST_RADIUS, out hit, RAYCAST_DISTANCE);
                if (!hitOk)
                {
                    if (_lastFrame.TryGetValue(uid, out var cached) && cached != null)
                    {
                        if (_lastFrameTime.TryGetValue(uid, out var t) && Time.realtimeSinceStartup - t <= FRAME_PERSIST_SECONDS)
                            DrawFrameIndicators(player, cached);
                    }
                    return;
                }
            }

            var frame = hit.GetEntity() as BuildingBlock;
            if (frame == null || frame.ShortPrefabName.IndexOf("floor.frame", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            _lastFrame[uid] = frame;
            _lastFrameTime[uid] = Time.realtimeSinceStartup;

            if (!CheckPermissions(player, item.info.shortname))
            {
                player.ChatMessage("You don't have permission to place this door type.");
                return;
            }

            if (IsFrameOccupied(frame))
            {
                player.ChatMessage("This frame already has something installed.");
                return;
            }

            if (frame.net != null && _frameCooldowns.TryGetValue(frame.net.ID, out float lastProcessedTime) &&
                currentTime - lastProcessedTime < FRAME_COOLDOWN_SECONDS)
                return;

            if (!player.CanBuild()) return;

            string manualPrefabPath = null;
            switch (item.info.shortname)
            {
                case "wall.frame.shopfront.metal":
                    manualPrefabPath = "assets/prefabs/building/wall.frame.shopfront/wall.frame.shopfront.metal.prefab";
                    break;
                case "wall.frame.fence":
                    manualPrefabPath = "assets/prefabs/building/wall.frame.fence/wall.frame.fence.prefab";
                    break;
                case "wall.frame.cell":
                    manualPrefabPath = "assets/prefabs/building/wall.frame.cell/wall.frame.cell.prefab";
                    break;
            }

            string prefabPath = manualPrefabPath;
            if (prefabPath == null)
            {
                var deployable = item.info.GetComponent<ItemModDeployable>();
                prefabPath = deployable != null ? deployable.entityPrefab?.resourcePath : null;
            }

            var entity = GameManager.server.CreateEntity(prefabPath);
            if (entity == null) return;

            entity.SetParent(frame);
            entity.transform.position = frame.transform.position - frame.transform.right * 1.5f;
            entity.transform.rotation = frame.transform.rotation * Quaternion.Euler(0f, 180f, 90f);
            entity.OwnerID = (ulong)player.userID;
            entity.skinID = item.skin;
            entity.Spawn();

            Effect.server.Run("assets/bundled/prefabs/fx/building/metal_sheet_place.prefab", entity.transform.position, Vector3.up, null, false);

            if (frame.net != null)
                _frameCooldowns[frame.net.ID] = currentTime;

            item.UseItem(1);
            if (_doorTypes.TryGetValue(item.info.shortname, out string nice))
                player.ChatMessage($"{nice} placed successfully!");
        }

        public void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null) return;
            ulong uid = (ulong)player.userID;
            if (newItem != null && newItem.info != null && _doorTypes.ContainsKey(newItem.info.shortname))
                _holdingDoorItem.Add(uid);
            else
                _holdingDoorItem.Remove(uid);
        }

        public void TickPlacementIndicator(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            ulong uid = (ulong)player.userID;
            float now = Time.realtimeSinceStartup;
            if (_lastIndicatorTime.TryGetValue(uid, out float last) && now - last < 0.1f)
                return;
            _lastIndicatorTime[uid] = now;

            var item = player.GetActiveItem();
            if (item == null || item.info == null || !_doorTypes.ContainsKey(item.info.shortname))
                return;

            if (!Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, RAYCAST_DISTANCE))
                return;

            var frame = hit.GetEntity() as BuildingBlock;
            if (frame == null || frame.ShortPrefabName.IndexOf("floor.frame", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            DrawFrameIndicators(player, frame);
        }

        private void DrawFrameIndicators(BasePlayer player, BuildingBlock frame)
        {
            var item = player.GetActiveItem();
            bool canPlaceDoor = item != null && item.info != null && _doorTypes.ContainsKey(item.info.shortname) && CheckPermissions(player, item.info.shortname);
            bool hasDoor = IsFrameOccupied(frame);
            bool isValid = canPlaceDoor && !hasDoor && player.CanBuild();

            Color validColor = new Color(0.0f, 0.5f, 0.0f, 1.0f);
            Color invalidColor = new Color(0.5f, 0.0f, 0.0f, 1.0f);

            Vector3 position1 = frame.transform.position - frame.transform.right * 1.5f;
            Vector3 position2 = frame.transform.position + frame.transform.right * 1.5f;
            Vector3 position3 = frame.transform.position - frame.transform.forward * 1.5f;
            Vector3 position4 = frame.transform.position + frame.transform.forward * 1.5f;

            float ttl = 2.0f;
            float sizeValid = 0.5f;
            float sizeInvalid = hasDoor ? 0.6f : 0.5f;
            Color c = isValid ? validColor : invalidColor;
            float size = isValid ? sizeValid : sizeInvalid;

            player.SendConsoleCommand("ddraw.sphere", ttl, c, position1, size);
            player.SendConsoleCommand("ddraw.sphere", ttl, c, position2, size);
            player.SendConsoleCommand("ddraw.sphere", ttl, c, position3, size);
            player.SendConsoleCommand("ddraw.sphere", ttl, c, position4, size);

            if (item != null)
                DrawDoorOrientationLabels(player, frame);
        }

        private static void DrawDoorOrientationLabels(BasePlayer player, BuildingBlock frame)
        {
            Quaternion doorWorldRot = frame.transform.rotation * Quaternion.Euler(0f, 180f, 90f);
            Vector3 doorUpWorld = doorWorldRot * Vector3.up;
            Vector3 frameUp = frame.transform.up;
            Vector3 projected = Vector3.ProjectOnPlane(doorUpWorld, frameUp).normalized;
            Vector3 r = frame.transform.right;
            Vector3 f = frame.transform.forward;
            float dotR = Vector3.Dot(projected, r);
            float dotF = Vector3.Dot(projected, f);

            Vector3 topAxis;
            if (Mathf.Abs(dotR) >= Mathf.Abs(dotF))
                topAxis = (dotR >= 0f) ? r : -r;
            else
                topAxis = (dotF >= 0f) ? f : -f;

            Vector3 center = frame.transform.position;
            float half = 1.5f;
            Vector3 topPos = center + topAxis * half;
            Vector3 bottomPos = center - topAxis * half;
            float ttl = 2.0f;
            Color c = new Color(0.10f, 0.55f, 1f, 1f);

            DrawBigText(player, topPos + frameUp * 0.02f, "TOP", c, ttl, frame.transform.right, frameUp, 3f);
            DrawBigText(player, bottomPos + frameUp * 0.02f, "BOTTOM", c, ttl, frame.transform.right, frameUp, 3f);

            Vector3 arrowOffset = frameUp * 0.03f;
            float margin = 0.2f;
            Vector3 centerTop = center + arrowOffset;
            Vector3 endTop = center + topAxis * (half - margin) + arrowOffset;
            Vector3 centerBottom = center + arrowOffset;
            Vector3 endBottom = center - topAxis * (half - margin) + arrowOffset;

            player.SendConsoleCommand("ddraw.arrow", ttl, c, centerTop, endTop, 0.05f);
            player.SendConsoleCommand("ddraw.arrow", ttl, c, centerBottom, endBottom, 0.05f);
        }

        private static void DrawBigText(BasePlayer player, Vector3 pos, string text, Color color, float ttl, Vector3 basisRight, Vector3 basisUp, float scale)
        {
            float unit = 0.06f * scale;
            Vector3 r = basisRight.normalized * unit;
            Vector3 u = basisUp.normalized * unit;
            Vector3[] offsets =
            {
                Vector3.zero, r, -r, u, -u, r + u, r - u, -r + u, -r - u
            };
            for (int i = 0; i < offsets.Length; i++)
                player.SendConsoleCommand("ddraw.text", ttl, color, pos + offsets[i], text);
        }

        public void RotateDoor(BasePlayer player)
        {
            if (player == null) return;
            player.ChatMessage("Usage: Hit a door with a hammer while holding SHIFT or using SCROLLWHEEL to rotate.");
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            ulong uid = (ulong)player.userID;
            _lastRaycastTime.Remove(uid);
            _lastInputCheck.Remove(uid);
            _lastIndicatorTime.Remove(uid);
            _lastFrame.Remove(uid);
            _lastFrameTime.Remove(uid);
            _holdingDoorItem.Remove(uid);
        }
    }
}
