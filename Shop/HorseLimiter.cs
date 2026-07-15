using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

namespace ShopHarmony
{
    /// <summary>
    /// Max owned horses (OwnerID) + NavMesh spawn helper for Shop Command products.
    /// Claim path: RidableHorse.SERVER_Claim; tracking on spawn/kill/claim.
    /// </summary>
    public sealed class HorseLimiter
    {
        public const string RidableHorsePrefabDefault = "assets/content/vehicles/horse/ridablehorse.prefab";
        public const string ConsoleCommand = "shop.horse";

        private readonly Dictionary<ulong, HashSet<RidableHorse>> _horsesByOwner =
            new Dictionary<ulong, HashSet<RidableHorse>>();
        private readonly Dictionary<RidableHorse, ulong> _trackedHorseOwners =
            new Dictionary<RidableHorse, ulong>();

        public HorseLimitSettings Settings { get; private set; } = HorseLimitSettings.CreateDefault();

        public bool Enabled => Settings != null && Settings.Enabled;

        public void Configure(HorseLimitSettings settings)
        {
            Settings = settings ?? HorseLimitSettings.CreateDefault();
        }

        public void ScanExistingHorses()
        {
            if (!Enabled) return;
            _horsesByOwner.Clear();
            _trackedHorseOwners.Clear();

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is RidableHorse horse)
                    TrackHorseOwner(horse);
            }

            Debug.Log($"[Shop Horse] Tracking initialized ({_trackedHorseOwners.Count} owned horse(s))");
        }

        public void Clear()
        {
            _horsesByOwner.Clear();
            _trackedHorseOwners.Clear();
        }

        public int GetPlayerHorseCount(BasePlayer player)
        {
            if (player == null || !IsSteamPlayer(player))
                return 0;

            ulong userId = player.userID;
            if (!_horsesByOwner.TryGetValue(userId, out HashSet<RidableHorse> horses))
                return 0;

            List<RidableHorse> stale = null;
            foreach (RidableHorse horse in horses)
            {
                if (horse == null || horse.IsDestroyed || horse.OwnerID != userId)
                {
                    if (stale == null) stale = new List<RidableHorse>();
                    stale.Add(horse);
                }
            }

            if (stale != null)
            {
                foreach (RidableHorse horse in stale)
                {
                    if (horse == null) horses.Remove(horse);
                    else if (horse.IsDestroyed) UntrackHorse(horse);
                    else TrackHorseOwner(horse);
                }
            }

            if (horses.Count == 0)
                _horsesByOwner.Remove(userId);

            return horses.Count;
        }

        public bool IsAtLimit(BasePlayer player)
        {
            if (!Enabled || player == null) return false;
            return GetPlayerHorseCount(player) >= Math.Max(0, Settings.MaxHorsesPerPlayer);
        }

        /// <summary>Prefix gate for RidableHorse.SERVER_Claim. Returns false to skip original.</summary>
        public bool AllowClaim(RidableHorse horse, BasePlayer player)
        {
            if (!Enabled || horse == null || player == null || !IsSteamPlayer(player))
                return true;

            if (!IsAtLimit(player))
                return true;

            int count = GetPlayerHorseCount(player);
            int max = Settings.MaxHorsesPerPlayer;
            player.ChatMessage(
                $"<color=red>You already own {count} horse(s). You can only own a maximum of {max} horses at a time.</color>");
            Debug.Log(
                $"[Shop Horse] Claim blocked for {player.displayName} ({player.userID}) count={count} max={max}");
            return false;
        }

        public void OnClaimed(RidableHorse horse, BasePlayer player)
        {
            if (horse == null || player == null || !IsSteamPlayer(player)) return;
            horse.OwnerID = player.userID;
            TrackHorseOwner(horse, player.userID);
        }

        public void OnHorseSpawned(RidableHorse horse) => TrackHorseOwner(horse);

        public void OnHorseKilled(RidableHorse horse) => UntrackHorse(horse);

        public void OnHorseMounted(RidableHorse horse) => TrackHorseOwner(horse);

        /// <summary>
        /// Console: shop.horse [name] [steamid] [refundAmount]
        /// Used by Shop ItemType.Command products after purchase charge.
        /// </summary>
        public void HandleConsoleCommand(ConsoleSystem.Arg arg, Action<BasePlayer, double> refund)
        {
            if (arg == null) return;

            BasePlayer player = arg.Player();
            string horseType = arg.GetString(0, "Horse");
            string playerId = arg.GetString(1, "");
            double refundAmount = arg.GetFloat(2, (float)Settings.RefundAmount);
            bool isShopPurchase = false;

            if (player == null)
            {
                if (!string.IsNullOrEmpty(playerId) && ulong.TryParse(playerId, out ulong steamId))
                {
                    player = BasePlayer.FindByID(steamId) ?? BasePlayer.FindSleeping(steamId);
                    if (player != null)
                        isShopPurchase = true;
                }

                if (player == null)
                {
                    Debug.Log("[Shop Horse] No player context for shop.horse");
                    return;
                }
            }
            else if (!player.IsAdmin)
            {
                isShopPurchase = true;
            }

            if (!Enabled)
            {
                SpawnHorseForPlayer(player, horseType);
                return;
            }

            if (IsAtLimit(player))
            {
                int count = GetPlayerHorseCount(player);
                int max = Settings.MaxHorsesPerPlayer;
                player.ChatMessage(
                    $"<color=red>You already own {count} horse(s). You can only own a maximum of {max} horses at a time.</color>");
                Debug.Log(
                    $"[Shop Horse] Spawn blocked for {player.displayName} ({player.userID}) count={count} max={max}");

                if (isShopPurchase && refundAmount > 0 && refund != null)
                {
                    refund(player, refundAmount);
                    player.ChatMessage($"<color=green>You have been refunded {refundAmount}.</color>");
                    Debug.Log($"[Shop Horse] Refunded {refundAmount} to {player.displayName}");
                }

                return;
            }

            SpawnHorseForPlayer(player, horseType);
        }

        public bool SpawnHorseForPlayer(BasePlayer player, string horseType)
        {
            if (player == null) return false;

            if (Enabled && IsAtLimit(player))
            {
                int count = GetPlayerHorseCount(player);
                int max = Settings.MaxHorsesPerPlayer;
                player.ChatMessage(
                    $"<color=red>You already own {count} horse(s). You can only own a maximum of {max} horses at a time.</color>");
                return false;
            }

            Vector3 spawnPos = FindValidSpawnPosition(player);
            if (spawnPos == Vector3.zero)
            {
                spawnPos = GetFallbackSpawnPosition(player);
                Debug.Log($"[Shop Horse] NavMesh miss - using fallback spawn at {spawnPos}");
            }

            if (spawnPos == Vector3.zero)
            {
                player.ChatMessage(
                    "<color=red>Could not find a valid location to spawn the horse. Please try in a different area.</color>");
                Debug.Log($"[Shop Horse] No valid spawn position for {player.displayName}");
                return false;
            }

            string prefab = string.IsNullOrEmpty(Settings.Prefab) ? RidableHorsePrefabDefault : Settings.Prefab;
            BaseEntity horseEntity = GameManager.server.CreateEntity(prefab, spawnPos);
            if (horseEntity == null)
            {
                player.ChatMessage("<color=red>Failed to spawn horse. Please try again.</color>");
                return false;
            }

            horseEntity.OwnerID = player.userID;
            horseEntity.Spawn();

            var ridableHorse = horseEntity as RidableHorse ?? horseEntity.GetComponent<RidableHorse>();
            if (ridableHorse != null)
            {
                ridableHorse.OwnerID = player.userID;
                TryApplyBreedFromName(ridableHorse, horseType);
                TrackHorseOwner(ridableHorse, player.userID);
            }

            string label = string.IsNullOrEmpty(horseType) ? "Horse" : horseType;
            Debug.Log($"[Shop Horse] Spawned {label} for {player.displayName} at {spawnPos}");
            player.ChatMessage($"<color=green>You have received a {label}!</color>");
            return true;
        }

        private static void TryApplyBreedFromName(RidableHorse horse, string horseType)
        {
            if (horse == null || horse.breeds == null || horse.breeds.Length == 0 || string.IsNullOrEmpty(horseType))
                return;

            string needle = horseType.Replace(" Horse", "", StringComparison.OrdinalIgnoreCase).Trim();
            for (int i = 0; i < horse.breeds.Length; i++)
            {
                HorseBreed breed = horse.breeds[i];
                if (breed == null || breed.breedName == null) continue;
                string english = breed.breedName.english ?? "";
                if (english.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    needle.IndexOf(english, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    horse.SetBreed(i);
                    return;
                }
            }
        }

        private void TrackHorseOwner(RidableHorse horse)
        {
            if (horse == null || horse.IsDestroyed) return;
            TrackHorseOwner(horse, horse.OwnerID);
        }

        private void TrackHorseOwner(RidableHorse horse, ulong ownerId)
        {
            if (horse == null || horse.IsDestroyed) return;

            if (_trackedHorseOwners.TryGetValue(horse, out ulong previousOwnerId) && previousOwnerId != ownerId)
                RemoveHorseFromOwner(horse, previousOwnerId);

            if (!ownerId.IsSteamId())
            {
                _trackedHorseOwners.Remove(horse);
                return;
            }

            if (!_horsesByOwner.TryGetValue(ownerId, out HashSet<RidableHorse> horses))
            {
                horses = new HashSet<RidableHorse>();
                _horsesByOwner[ownerId] = horses;
            }

            horses.Add(horse);
            _trackedHorseOwners[horse] = ownerId;
        }

        private void UntrackHorse(RidableHorse horse)
        {
            if (horse == null) return;
            if (_trackedHorseOwners.TryGetValue(horse, out ulong ownerId))
            {
                RemoveHorseFromOwner(horse, ownerId);
                _trackedHorseOwners.Remove(horse);
            }
        }

        private void RemoveHorseFromOwner(RidableHorse horse, ulong ownerId)
        {
            if (!_horsesByOwner.TryGetValue(ownerId, out HashSet<RidableHorse> horses)) return;
            horses.Remove(horse);
            if (horses.Count == 0)
                _horsesByOwner.Remove(ownerId);
        }

        private static bool IsSteamPlayer(BasePlayer player) =>
            player != null && ((ulong)player.userID).IsSteamId();

        private Vector3 FindValidSpawnPosition(BasePlayer player)
        {
            Vector3 playerPos = player.transform.position;
            Vector3 playerForward = player.transform.forward;

            Vector3[] testPositions =
            {
                playerPos + playerForward * 3f,
                playerPos + playerForward * 2f,
                playerPos + playerForward * 4f,
                playerPos + playerForward * 5f,
                playerPos + Vector3.right * 3f,
                playerPos + Vector3.left * 3f,
                playerPos + Vector3.back * 2f,
            };

            foreach (Vector3 testPos in testPositions)
            {
                Vector3 validPos = GetValidNavMeshPosition(testPos);
                if (validPos != Vector3.zero)
                    return validPos;
            }

            return GetValidNavMeshPosition(playerPos);
        }

        /// <summary>Last-resort spawn near the player when NavMesh sampling fails entirely.</summary>
        private static Vector3 GetFallbackSpawnPosition(BasePlayer player)
        {
            if (player == null) return Vector3.zero;
            Vector3 pos = player.transform.position + player.transform.forward * 3f;
            if (TerrainMeta.HeightMap != null)
                pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.25f;
            else
                pos.y += 0.25f;
            return pos;
        }

        private Vector3 GetValidNavMeshPosition(Vector3 testPosition)
        {
            float terrainHeight = TerrainMeta.HeightMap != null
                ? TerrainMeta.HeightMap.GetHeight(testPosition)
                : testPosition.y;
            Vector3 groundPos = new Vector3(testPosition.x, terrainHeight, testPosition.z);

            if (IsValidTerrainPosition(groundPos) &&
                NavMesh.SamplePosition(groundPos, out NavMeshHit navHit, 5f, NavMesh.AllAreas) &&
                IsValidTerrainPosition(navHit.position))
            {
                return navHit.position;
            }

            for (int i = 0; i < 10; i++)
            {
                Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * 3f;
                Vector3 randomPos = groundPos + new Vector3(randomOffset.x, 0f, randomOffset.y);
                if (TerrainMeta.HeightMap != null)
                    randomPos.y = TerrainMeta.HeightMap.GetHeight(randomPos);

                if (!IsValidTerrainPosition(randomPos)) continue;
                if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 2f, NavMesh.AllAreas) &&
                    IsValidTerrainPosition(hit.position))
                {
                    return hit.position;
                }
            }

            return Vector3.zero;
        }

        private static bool IsValidTerrainPosition(Vector3 position)
        {
            if (position.y < -100f || position.y > 1000f || position.y < 0f)
                return false;

            Vector3[] checkPoints =
            {
                position + Vector3.forward * 0.5f,
                position + Vector3.back * 0.5f,
                position + Vector3.right * 0.5f,
                position + Vector3.left * 0.5f
            };

            float maxHeightDiff = 0f;
            foreach (Vector3 checkPoint in checkPoints)
            {
                float checkHeight = TerrainMeta.HeightMap != null
                    ? TerrainMeta.HeightMap.GetHeight(checkPoint)
                    : position.y;
                float heightDiff = Mathf.Abs(position.y - checkHeight);
                if (heightDiff > maxHeightDiff)
                    maxHeightDiff = heightDiff;
            }

            return maxHeightDiff <= 2f;
        }
    }

    public sealed class HorseLimitSettings
    {
        [JsonProperty("Horse Limit Enabled")]
        public bool Enabled = true;

        [JsonProperty("Max Horses Per Player")]
        public int MaxHorsesPerPlayer = 4;

        [JsonProperty("Horse Prefab")]
        public string Prefab = HorseLimiter.RidableHorsePrefabDefault;

        [JsonProperty("Horse Refund Amount")]
        public double RefundAmount = 75.0;

        public static HorseLimitSettings CreateDefault() => new HorseLimitSettings();
    }
}
