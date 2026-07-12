using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace NexusStaticPortals
{
    public sealed class NexusStaticPortalsMod : IHarmonyModHooks
    {
        /// <summary>Must match Oxide Portals.cs (<c>nexus_unlocks.json</c>) so hub/mod and leaf/plugin share one file.</summary>
        private const string UnlockFileName = "nexus_unlocks.json";

        /// <summary>Older Harmony builds wrote this name; merged into <see cref="UnlockFileName"/> on load.</summary>
        private const string LegacyUnlockFileName = "NexusStaticPortals_unlocks.json";

        private readonly Dictionary<ulong, PortalDefinition> _portalByEntityId = new();
        private readonly HashSet<ulong> _pendingTransfers = new();

        private NexusStaticPortalsConfigData _config;
        private UnlockData _unlockData;
        private bool _spawnScheduled;

        public static NexusStaticPortalsMod Instance { get; private set; }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            _config = NexusStaticPortalsConfig.LoadOrCreate();
            LoadUnlockDataInitial();
            Debug.Log("[NexusStaticPortals] Loaded. Config: HarmonyConfig/NexusStaticPortals.json.");
            ScheduleInitialSpawn();
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            DestroyAllPortals();
            _portalByEntityId.Clear();
            _pendingTransfers.Clear();
            Instance = null;
            Debug.Log("[NexusStaticPortals] Unloaded.");
        }

        internal void ScheduleInitialSpawn()
        {
            if (_spawnScheduled)
                return;

            var serverMgr = SingletonComponent<ServerMgr>.Instance;
            if (serverMgr == null)
            {
                Debug.Log("[NexusStaticPortals] ServerMgr not ready yet; portal spawn will wait for ServerMgr.Initialize.");
                return;
            }

            _spawnScheduled = true;
            Debug.Log("[NexusStaticPortals] Scheduling portal spawn pass.");
            InvokeHandler.Invoke(serverMgr, (Action)SpawnConfiguredPortals, 3f);
            InvokeHandler.Invoke(serverMgr, (Action)RetryMissingPortals, 15f);
        }

        internal bool TryHandlePortalUse(BasePlayer player, BasePortal portalEntity)
        {
            if (player == null || portalEntity == null)
                return false;

            var entity = portalEntity as BaseEntity;
            if (entity?.net == null)
                return false;

            if (!_portalByEntityId.TryGetValue(entity.net.ID.Value, out var portal))
                return false;

            HandleNexusPortalUse(player, portal);
            return true;
        }

        private void SpawnConfiguredPortals()
        {
            if (_config == null)
                _config = NexusStaticPortalsConfig.LoadOrCreate();

            _portalByEntityId.Clear();
            var spawned = 0;

            foreach (var portal in _config.Portals)
            {
                if (portal == null || !portal.IsNexusPortal)
                    continue;

                DestroyPortal(portal);
                spawned += SpawnPortalEntrances(portal);
            }

            Debug.Log("[NexusStaticPortals] Spawn pass complete. Spawned entrances: " + spawned + ".");
            LogSpawnCoverage();
        }

        private void RetryMissingPortals()
        {
            if (_config?.Portals == null)
                return;

            foreach (var portal in _config.Portals)
            {
                if (portal == null || !portal.IsNexusPortal)
                    continue;

                if (portal.SpawnedEntranceDoors.Count >= portal.ExpectedFixedWorldEntrances)
                    continue;

                Debug.Log("[NexusStaticPortals] Retrying fixed-world portal '" + portal.Name + "'.");
                DestroyPortal(portal);
                SpawnPortalEntrances(portal);
            }

            LogSpawnCoverage();
        }

        private int SpawnPortalEntrances(PortalDefinition portal)
        {
            if (portal.EntranceAnchors == null)
                return 0;

            var spawned = 0;

            for (var i = 0; i < portal.EntranceAnchors.Count; i++)
            {
                var anchor = portal.EntranceAnchors[i];
                if (anchor == null || (!anchor.UseFixedWorldTransform && !anchor.UseMonumentRelativeTransform))
                    continue;

                if (!TryResolveAnchorTransform(portal, anchor, out var worldPos, out var rotation, out var failureReason))
                {
                    Debug.LogWarning("[NexusStaticPortals] Portal '" + portal.Name + "' anchor [" + i + "] could not resolve transform: " + failureReason);
                    continue;
                }

                var entity = GameManager.server.CreateEntity(_config.ExitDoorPrefab, worldPos, rotation);
                if (entity == null)
                {
                    Debug.LogWarning("[NexusStaticPortals] CreateEntity failed for portal '" + portal.Name + "' at " + worldPos + ".");
                    continue;
                }

                entity.EnableSaving(false);
                var basePortal = entity.GetComponent<BasePortal>();
                if (basePortal != null)
                    basePortal.isUsablePortal = true;

                var scale = anchor.WorldScale.ToVector3();
                if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                    scale = Vector3.one;

                entity.transform.localScale = scale;
                entity.Spawn();

                if (entity.IsDestroyed || entity.net == null)
                {
                    Debug.LogWarning("[NexusStaticPortals] Spawn failed for portal '" + portal.Name + "'.");
                    continue;
                }

                entity.transform.position = worldPos;
                entity.transform.rotation = rotation;
                ApplyPostSpawnAdjustments(entity, anchor);
                entity.SendNetworkUpdateImmediate();

                portal.SpawnedEntranceDoors.Add(entity);
                _portalByEntityId[entity.net.ID.Value] = portal;
                spawned++;

                if (_config.DebugEnabled)
                {
                    Debug.Log("[NexusStaticPortals] Spawned '" + portal.Name + "' at " + entity.transform.position +
                        " rot=" + entity.transform.rotation.eulerAngles + " net=" + entity.net.ID.Value + ".");
                }
            }

            return spawned;
        }

        private bool TryResolveAnchorTransform(PortalDefinition portal, PortalAnchorDefinition anchor, out Vector3 worldPos, out Quaternion rotation, out string failureReason)
        {
            worldPos = Vector3.zero;
            rotation = Quaternion.identity;
            failureReason = null;

            if (anchor.UseMonumentRelativeTransform)
            {
                var monument = FindMatchingMonument(anchor.MonumentNameContains);
                if (monument == null)
                {
                    failureReason = "no matching monument found for '" + (anchor.MonumentNameContains ?? "compound") + "'";
                    return false;
                }

                rotation = monument.transform.rotation * Quaternion.Euler(anchor.LocalEulerAngles.ToVector3());
                worldPos = monument.transform.TransformPoint(anchor.LocalPosition.ToVector3());
                worldPos += anchor.WorldPositionOffset.ToVector3();
                worldPos += rotation * anchor.FixedWorldLocalOffset.ToVector3();

                if (_config.DebugEnabled)
                {
                    Debug.Log("[NexusStaticPortals] Resolved monument-relative anchor for '" + portal.Name + "' via monument '" +
                        GetMonumentDebugName(monument) + "' at " + monument.transform.position + " -> worldPos=" + worldPos +
                        " rot=" + rotation.eulerAngles + ".");
                }

                return true;
            }

            if (!anchor.UseFixedWorldTransform)
            {
                failureReason = "anchor has no supported transform mode enabled";
                return false;
            }

            worldPos = anchor.WorldPosition.ToVector3() + anchor.WorldPositionOffset.ToVector3();
            if (worldPos == Vector3.zero)
            {
                failureReason = "world position is zero";
                return false;
            }

            rotation = Quaternion.Euler(anchor.WorldEulerAngles.ToVector3());
            worldPos += rotation * anchor.FixedWorldLocalOffset.ToVector3();
            return true;
        }

        private static MonumentInfo FindMatchingMonument(string nameContains)
        {
            var monuments = UnityEngine.Object.FindObjectsOfType<MonumentInfo>();
            if (monuments == null || monuments.Length == 0)
                return null;

            var needle = string.IsNullOrWhiteSpace(nameContains) ? "compound" : nameContains.Trim();
            MonumentInfo best = null;
            var bestScore = float.MaxValue;

            foreach (var monument in monuments)
            {
                if (monument == null)
                    continue;

                if (!MonumentMatches(monument, needle))
                    continue;

                var score = monument.transform.position.sqrMagnitude;
                if (best == null || score < bestScore)
                {
                    best = monument;
                    bestScore = score;
                }
            }

            return best;
        }

        private static bool MonumentMatches(MonumentInfo monument, string needle)
        {
            if (monument == null || string.IsNullOrWhiteSpace(needle))
                return false;

            if (ContainsIgnoreCase(monument.name, needle))
                return true;

            if (monument.displayPhrase.IsValid())
            {
                if (ContainsIgnoreCase(monument.displayPhrase.token, needle))
                    return true;

                if (ContainsIgnoreCase(monument.displayPhrase.english, needle))
                    return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string value, string needle)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetMonumentDebugName(MonumentInfo monument)
        {
            if (monument == null)
                return "null";

            if (monument.displayPhrase.IsValid() && !string.IsNullOrWhiteSpace(monument.displayPhrase.token))
                return monument.displayPhrase.token;

            return monument.name ?? "(unnamed monument)";
        }

        private void ApplyPostSpawnAdjustments(BaseEntity entity, PortalAnchorDefinition anchor)
        {
            if (entity == null || anchor == null || !anchor.SnapPortalBottomToWorldY)
                return;

            var colliders = entity.GetComponentsInChildren<Collider>(true);
            var minY = entity.transform.position.y;
            var foundCollider = false;

            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled)
                    continue;

                if (!foundCollider || collider.bounds.min.y < minY)
                    minY = collider.bounds.min.y;

                foundCollider = true;
            }

            var delta = anchor.PortalBottomTargetWorldY - minY;
            entity.transform.position += Vector3.up * delta;
        }

        private void HandleNexusPortalUse(BasePlayer player, PortalDefinition portal)
        {
            if (player == null || portal == null)
                return;

            var zoneKey = (portal.NexusTransferTargetZoneKey ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(zoneKey))
                return;

            if (_pendingTransfers.Contains(player.userID))
            {
                player.ChatMessage("A portal transfer is already queued.");
                return;
            }

            if (!NexusServer.Started)
            {
                player.ChatMessage("Travel is unavailable right now (Nexus not connected).");
                return;
            }

            if (string.Equals(zoneKey, NexusServer.ZoneKey, StringComparison.OrdinalIgnoreCase))
            {
                player.ChatMessage("You are already on this zone.");
                return;
            }

            var cost = portal.NexusOneTimeScrapCost;
            var currency = string.IsNullOrWhiteSpace(portal.NexusUnlockCurrencyShortName)
                ? "scrap"
                : portal.NexusUnlockCurrencyShortName.Trim();

            ReloadUnlocks();

            if (cost > 0 && !HasUnlock(player.userID, portal.Name))
            {
                var prerequisite = portal.NexusPrerequisitePortalName?.Trim();
                if (!string.IsNullOrEmpty(prerequisite) && !HasUnlock(player.userID, prerequisite))
                {
                    player.ChatMessage("You must unlock '" + prerequisite + "' before using this portal.");
                    return;
                }

                if (CountCurrency(player, currency) < cost)
                {
                    player.ChatMessage("This portal requires a one-time payment of " + cost + " " + currency + ".");
                    return;
                }

                if (!ConsumeCurrency(player, currency, cost))
                {
                    player.ChatMessage("Could not process portal payment.");
                    return;
                }

                GrantUnlock(player.userID, portal.Name);
                player.ChatMessage("Paid " + cost + " " + currency + ". This portal is now permanently unlocked for you.");
            }

            void RunTransfer()
            {
                _pendingTransfers.Remove(player.userID);

                if (player == null || player.IsDestroyed || !player.IsConnected)
                    return;

                try
                {
                    _ = NexusServer.TransferEntity(player, zoneKey, "console", false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NexusStaticPortals] Transfer failed for '" + portal.Name + "': " + ex.Message);
                    player.ChatMessage("Travel failed. Try again.");
                }
            }

            _pendingTransfers.Add(player.userID);

            if (portal.TeleportationTime > 0)
            {
                player.ChatMessage("Transferring in " + portal.TeleportationTime + " seconds...");
                InvokeHandler.Invoke(player, (Action)RunTransfer, portal.TeleportationTime);
            }
            else
            {
                RunTransfer();
            }
        }

        private void LogSpawnCoverage()
        {
            if (_config?.Portals == null)
                return;

            foreach (var portal in _config.Portals)
            {
                if (portal == null || !portal.IsNexusPortal)
                    continue;

                var expected = portal.ExpectedFixedWorldEntrances;
                var actual = portal.SpawnedEntranceDoors.Count;
                if (expected > 0 && actual < expected)
                {
                    Debug.LogWarning("[NexusStaticPortals] Portal '" + portal.Name + "' spawned " + actual + "/" + expected +
                        " entrances. Check monument match / local position / world position settings.");
                }
            }
        }

        private void DestroyAllPortals()
        {
            if (_config?.Portals == null)
                return;

            foreach (var portal in _config.Portals)
                DestroyPortal(portal);
        }

        private void DestroyPortal(PortalDefinition portal)
        {
            if (portal?.SpawnedEntranceDoors == null)
                return;

            foreach (var entity in portal.SpawnedEntranceDoors)
            {
                if (entity == null || entity.IsDestroyed)
                    continue;

                if (entity.net != null)
                    _portalByEntityId.Remove(entity.net.ID.Value);

                entity.Kill();
            }

            portal.SpawnedEntranceDoors.Clear();
        }

        private string GetUnlockFilePath()
        {
            var customRoot = _config?.CustomPortalsDataDirectory;
            if (!string.IsNullOrWhiteSpace(customRoot))
            {
                var full = Path.GetFullPath(customRoot.Trim());
                Directory.CreateDirectory(full);
                return Path.Combine(full, UnlockFileName);
            }

            var defaultRoot = Path.Combine(Environment.CurrentDirectory, "HarmonyData");
            Directory.CreateDirectory(defaultRoot);
            return Path.Combine(defaultRoot, UnlockFileName);
        }

        /// <summary>
        /// Load canonical <see cref="UnlockFileName"/> and merge any legacy <see cref="LegacyUnlockFileName"/> once
        /// (same folder), then save if merged — matches Oxide Portals shared-unlocks layout.
        /// </summary>
        private void LoadUnlockDataInitial()
        {
            var path = GetUnlockFilePath();
            _unlockData = UnlockDataStore.Load(path);

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                return;

            var legacyPath = Path.Combine(dir, LegacyUnlockFileName);
            if (!File.Exists(legacyPath))
                return;

            try
            {
                var legacy = UnlockDataStore.Load(legacyPath);
                if (!UnlockDataStore.MergeInto(_unlockData, legacy))
                    return;

                UnlockDataStore.Save(path, _unlockData);
                Debug.Log("[NexusStaticPortals] Merged '" + LegacyUnlockFileName + "' into '" + UnlockFileName + "' and saved.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusStaticPortals] Legacy unlock merge failed: " + ex.Message);
            }
        }

        private void ReloadUnlocks()
        {
            _unlockData = UnlockDataStore.Load(GetUnlockFilePath());
        }

        private bool HasUnlock(ulong userId, string portalName)
        {
            if (userId == 0 || string.IsNullOrWhiteSpace(portalName))
                return false;

            if (_unlockData?.ByPortal == null)
                return false;

            return _unlockData.ByPortal.TryGetValue(portalName, out var users) &&
                   users != null &&
                   users.Contains(userId.ToString());
        }

        private void GrantUnlock(ulong userId, string portalName)
        {
            if (userId == 0 || string.IsNullOrWhiteSpace(portalName))
                return;

            _unlockData ??= new UnlockData();
            _unlockData.ByPortal ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (!_unlockData.ByPortal.TryGetValue(portalName, out var users) || users == null)
            {
                users = new List<string>();
                _unlockData.ByPortal[portalName] = users;
            }

            var steamId = userId.ToString();
            if (!users.Contains(steamId))
                users.Add(steamId);

            UnlockDataStore.Save(GetUnlockFilePath(), _unlockData);
        }

        private static int CountCurrency(BasePlayer player, string shortName)
        {
            if (player?.inventory == null || string.IsNullOrWhiteSpace(shortName))
                return 0;

            var total = 0;
            CountContainer(player.inventory.containerMain);
            CountContainer(player.inventory.containerBelt);
            return total;

            void CountContainer(ItemContainer container)
            {
                if (container?.itemList == null)
                    return;

                foreach (var item in container.itemList)
                {
                    if (item?.info?.shortname == shortName)
                        total += item.amount;
                }
            }
        }

        private static bool ConsumeCurrency(BasePlayer player, string shortName, int amount)
        {
            if (player?.inventory == null || string.IsNullOrWhiteSpace(shortName) || amount <= 0)
                return false;

            if (CountCurrency(player, shortName) < amount)
                return false;

            var remaining = amount;
            TakeFromContainer(player.inventory.containerMain);
            if (remaining > 0)
                TakeFromContainer(player.inventory.containerBelt);
            return remaining <= 0;

            void TakeFromContainer(ItemContainer container)
            {
                if (container?.itemList == null || remaining <= 0)
                    return;

                var snapshot = new List<Item>(container.itemList);
                foreach (var item in snapshot)
                {
                    if (item?.info?.shortname != shortName)
                        continue;

                    var take = Mathf.Min(item.amount, remaining);
                    item.UseItem(take);
                    remaining -= take;

                    if (remaining <= 0)
                        return;
                }
            }
        }
    }

    public sealed class UnlockData
    {
        [JsonProperty("ByPortal")]
        public Dictionary<string, List<string>> ByPortal { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static class UnlockDataStore
    {
        public static UnlockData Load(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(path))
                    return new UnlockData();

                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<UnlockData>(json) ?? new UnlockData();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusStaticPortals] Failed to load unlock data: " + ex.Message);
                return new UnlockData();
            }
        }

        public static void Save(string path, UnlockData data)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, JsonConvert.SerializeObject(data ?? new UnlockData(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusStaticPortals] Failed to save unlock data: " + ex.Message);
            }
        }

        /// <summary>Merges <paramref name="source"/> portal/user entries into <paramref name="target"/> (union). Returns whether anything was added.</summary>
        public static bool MergeInto(UnlockData target, UnlockData source)
        {
            if (target == null || source?.ByPortal == null || source.ByPortal.Count == 0)
                return false;

            target.ByPortal ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var changed = false;
            foreach (var kvp in source.ByPortal)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null)
                    continue;

                if (!target.ByPortal.TryGetValue(kvp.Key, out var users) || users == null)
                {
                    target.ByPortal[kvp.Key] = new List<string>(kvp.Value);
                    changed = true;
                    continue;
                }

                foreach (var id in kvp.Value)
                {
                    if (string.IsNullOrWhiteSpace(id) || users.Contains(id))
                        continue;
                    users.Add(id);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
