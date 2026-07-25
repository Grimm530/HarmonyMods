using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TeleportGUI
{
    /// <summary>
    /// Monument-generated warps + payment helpers (Oxide TeleportGUI 2.0.481).
    /// Generated warps live only in memory — never written to warpdata.
    /// </summary>
    public partial class TeleportGUIMod
    {
        private readonly List<MonumentInfoEntry> _monuments = new List<MonumentInfoEntry>();
        private readonly List<MonumentInfoEntry> _oilRigs = new List<MonumentInfoEntry>();
        private readonly Dictionary<string, GeneratedMonumentWarpPoint> _monumentWarps =
            new Dictionary<string, GeneratedMonumentWarpPoint>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _monumentWarpChatCommands = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _monumentRegisteredCommandNames = new List<string>();

        private static ItemDefinition _scrapItemDefinition;
        private static FieldInfo _terrainPathMonumentsField;

        /// <summary>Rust.Layer.Player_Server — avoid referencing Rust.Global.</summary>
        private const int PlayerServerLayerMask = 1 << 17;

        private static readonly Dictionary<string, Bounds> BoundsOverrides = new Dictionary<string, Bounds>(StringComparer.OrdinalIgnoreCase)
        {
            ["fishing_village_a"] = new Bounds(Vector3.up * 5f, Vector3.one * 85),
            ["fishing_village_b"] = new Bounds(Vector3.up * 5f, Vector3.one * 75),
            ["fishing_village_c"] = new Bounds(Vector3.up * 5f, Vector3.one * 50),
            ["lighthouse"] = new Bounds(Vector3.up * 20f, Vector3.one * 80),
            ["swamp_a"] = new Bounds(),
            ["swamp_b"] = new Bounds(),
            ["swamp_c"] = new Bounds(),
            ["ice_lake_4"] = new Bounds(Vector3.zero, new Vector3(40, 40, 40)),
            ["supermarket_1"] = new Bounds(Vector3.zero, Vector3.one * 75),
            ["powerplant_1"] = new Bounds(Vector3.zero, new Vector3(250, 200, 300)),
            ["launch_site_1"] = new Bounds(Vector3.forward * -25, new Vector3(600, 200, 350)),
            ["trainyard_1"] = new Bounds(Vector3.zero, Vector3.one * 250),
            ["water_treatment_plant_1"] = new Bounds(Vector3.forward * -50, new Vector3(300, 200, 300)),
            ["radtown_small_3"] = new Bounds(Vector3.forward * -25, Vector3.one * 175),
            ["harbor_2"] = new Bounds(Vector3.zero, new Vector3(250, 200, 300)),
            ["train_tunnel_double_entrance"] = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(100, 50, 100))
        };

        public enum TeleportPaymentKind
        {
            Teleport,
            Home,
            Warp
        }

        /// <summary>
        /// Exact charge captured at payment time so refunds ignore later config changes.
        /// </summary>
        public readonly struct PaymentReceipt
        {
            public bool WasCharged { get; }
            public int Amount { get; }
            public TeleportGUIConfig.PurchaseMode Currency { get; }

            public PaymentReceipt(bool wasCharged, int amount, TeleportGUIConfig.PurchaseMode currency)
            {
                WasCharged = wasCharged;
                Amount = amount;
                Currency = currency;
            }

            public static PaymentReceipt None => new PaymentReceipt(false, 0, TeleportGUIConfig.PurchaseMode.Scrap);
        }

        #region Monument lifecycle

        /// <summary>
        /// Discover monuments via TerrainMeta.Path.Monuments and generate enabled warps in memory.
        /// Does not mutate config or warpdata.
        /// </summary>
        public void InitializeMonumentWarps()
        {
            ShutdownMonumentWarps();

            List<MonumentInfo> monuments = GetTerrainMonuments();
            if (monuments == null || monuments.Count == 0)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] No monuments discovered; monument warps skipped.");
                return;
            }

            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            var monumentWarpsConfig = _config?.Warp?.MonumentWarps;

            foreach (MonumentInfo monument in monuments)
            {
                if (monument == null) continue;

                string shortname;
                try
                {
                    shortname = Path.GetFileNameWithoutExtension(monument.name);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(shortname)) continue;

                Bounds bounds = monument.Bounds;
                Vector3 position = default;
                float radius = 0f;

                try
                {
                    PreventBuildingMonumentTag[] monumentTags =
                        monument.GetComponentsInChildren<PreventBuildingMonumentTag>(true);

                    if (monumentTags != null && monumentTags.Length > 0)
                    {
                        foreach (PreventBuildingMonumentTag monumentTag in monumentTags)
                        {
                            Collider collider = monumentTag ? monumentTag.GetComponent<Collider>() : null;

                            if (!monumentTag || collider == null || collider.gameObject.layer != 29 || collider is MeshCollider)
                                continue;

                            if (collider is SphereCollider sphereCollider)
                            {
                                if (sphereCollider.radius > radius)
                                {
                                    position = sphereCollider.transform.position;
                                    radius = sphereCollider.radius;
                                }
                            }
                            else if (collider is BoxCollider boxCollider)
                            {
                                Vector3 localCenter = monument.transform.InverseTransformPoint(
                                    boxCollider.transform.TransformPoint(boxCollider.center));
                                Vector3 localSize = Vector3.Scale(boxCollider.size, boxCollider.transform.lossyScale);
                                bounds.Encapsulate(new Bounds(localCenter, localSize));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[TeleportGUI] Monument tag scan failed for " + shortname + ": " + ex.Message);
                }

                if (radius == 0f && bounds.size == Vector3.zero &&
                    BoundsOverrides.TryGetValue(shortname, out Bounds @override))
                    bounds = @override;

                bool isSafeZone = false;
                try { isSafeZone = monument.IsSafeZone; } catch { }

                if (shortname.IndexOf("oilrig", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _oilRigs.Add(new MonumentInfoEntry(shortname, isSafeZone, monument.transform, position, radius, bounds));
                    continue;
                }

                if (shortname.IndexOf("underwater_lab", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var entry = new MonumentInfoEntry(shortname, isSafeZone, monument.transform, position, radius, bounds);
                _monuments.Add(entry);

                if (monumentWarpsConfig == null ||
                    !monumentWarpsConfig.TryGetValue(shortname, out TeleportGUIConfig.WarpOptions.MonumentWarp monumentWarp) ||
                    monumentWarp == null ||
                    !monumentWarp.Enabled)
                    continue;

                string uniqueName = textInfo.ToTitleCase(
                    Regex.Replace(shortname.Replace("_", " "), @"[\d-]", string.Empty).Trim());

                string gridCoord;
                try { gridCoord = MapHelper.PositionToString(monument.transform.position); }
                catch { gridCoord = "?"; }

                uniqueName += $" ({gridCoord})";

                float maxRadius = monumentWarp.MaxRadius > 0f ? monumentWarp.MaxRadius : radius;
                bool safeZoneOnly = isSafeZone && monumentWarp.SafeZoneOnly;

                var generated = new GeneratedMonumentWarpPoint(
                    shortname, gridCoord, uniqueName,
                    monument.transform, bounds, position, maxRadius, safeZoneOnly);

                if (generated.SpawnCount <= 0)
                    continue;

                if (!string.IsNullOrEmpty(monumentWarp.Permission))
                {
                    string perm = EnsureWarpPermission(monumentWarp.Permission);
                    generated.Permission = perm;
                    PermissionsBridge.RegisterPermission(perm);
                }

                string monumentCmd = NormalizeWarpChatCommand(monumentWarp.Command);
                generated.Command = monumentCmd ?? string.Empty;

                // Representative position for list/UI (actual teleport uses ResolveWarpPosition).
                generated.Position = generated.PeekPosition();

                _monumentWarps[uniqueName] = generated;

                if (!string.IsNullOrEmpty(monumentCmd) &&
                    !_manualWarpChatCommands.Contains(monumentCmd) &&
                    !_monumentWarpChatCommands.Contains(monumentCmd))
                {
                    RegisterMonumentWarpChatCommand(monumentCmd, uniqueName);
                }
            }

            UnityEngine.Debug.Log($"[TeleportGUI] Monument warps ready: {_monumentWarps.Count} generated from {_monuments.Count} monuments.");
        }

        /// <summary>
        /// TerrainPath.Monuments is internal on current Rust builds — read via reflection,
        /// with FindObjectsByType fallback. Shared for other partials that need monument lists.
        /// </summary>
        internal static List<MonumentInfo> GetTerrainMonuments()
        {
            try
            {
                TerrainPath path = TerrainMeta.Path;
                if (path != null)
                {
                    if (_terrainPathMonumentsField == null)
                        _terrainPathMonumentsField = typeof(TerrainPath).GetField(
                            "Monuments",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (_terrainPathMonumentsField?.GetValue(path) is List<MonumentInfo> fromPath && fromPath.Count > 0)
                        return fromPath;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] TerrainPath.Monuments reflection failed: " + ex.Message);
            }

            try
            {
                MonumentInfo[] found = UnityEngine.Object.FindObjectsByType<MonumentInfo>(FindObjectsSortMode.None);
                if (found != null && found.Length > 0)
                    return new List<MonumentInfo>(found);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] FindObjectsByType<MonumentInfo> failed: " + ex.Message);
            }

            return null;
        }

        public void ShutdownMonumentWarps()
        {
            UnregisterMonumentWarpChatCommands();
            _monumentWarps.Clear();
            _monuments.Clear();
            _oilRigs.Clear();
            _monumentWarpChatCommands.Clear();
        }

        private void RegisterMonumentWarpChatCommand(string cmdName, string warpName)
        {
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = cmdName,
                    FullName = "global." + cmdName,
                    Variable = false,
                    ServerAdmin = false,
                    ServerUser = true,
                    Call = arg =>
                    {
                        var player = arg.Connection?.player as BasePlayer;
                        if (player == null) return;
                        CmdWarp(player, new[] { warpName });
                    }
                };

                if (ConsoleSystem.Index.Server.Dict != null &&
                    !ConsoleSystem.Index.Server.Dict.ContainsKey("global." + cmdName))
                {
                    ConsoleSystem.Index.Server.Dict["global." + cmdName] = cmd;
                    if (ConsoleSystem.Index.Server.GlobalDict != null)
                        ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
                    _registeredCommands[cmdName] = cmd;
                    _monumentRegisteredCommandNames.Add(cmdName);
                    _monumentWarpChatCommands.Add(cmdName);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] Failed to register monument warp command /" + cmdName + ": " + ex.Message);
            }
        }

        private void UnregisterMonumentWarpChatCommands()
        {
            foreach (string cmdName in _monumentRegisteredCommandNames)
            {
                try
                {
                    if (ConsoleSystem.Index.Server.Dict != null)
                        ConsoleSystem.Index.Server.Dict.Remove("global." + cmdName);
                    if (ConsoleSystem.Index.Server.GlobalDict != null)
                        ConsoleSystem.Index.Server.GlobalDict.Remove(cmdName);
                    _registeredCommands.Remove(cmdName);
                }
                catch { }
            }
            _monumentRegisteredCommandNames.Clear();
        }

        #endregion

        #region Warp lookup (manual + generated)

        /// <summary>
        /// Look up a manual warpdata entry or an in-memory generated monument warp.
        /// Generated warps are never written to warpdata.
        /// </summary>
        public bool TryGetAnyWarp(string name, out TeleportGUIData.WarpPoint warp)
        {
            warp = null;
            if (string.IsNullOrEmpty(name)) return false;

            if (_warpData != null && _warpData.TryGetValue(name, out warp) && warp != null)
                return true;

            if (_monumentWarps.TryGetValue(name, out GeneratedMonumentWarpPoint generated))
            {
                warp = generated;
                return true;
            }

            // Case-insensitive fallback for manual warps already covered by TeleportGUIWarpData comparer;
            // also try loose match on monument display names.
            if (_warpData != null)
            {
                foreach (KeyValuePair<string, TeleportGUIData.WarpPoint> kvp in _warpData)
                {
                    if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        warp = kvp.Value;
                        return true;
                    }
                }
            }

            foreach (KeyValuePair<string, GeneratedMonumentWarpPoint> kvp in _monumentWarps)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    warp = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Enumerate manual warpdata entries then generated monument warps (memory only).
        /// </summary>
        public IEnumerable<KeyValuePair<string, TeleportGUIData.WarpPoint>> EnumerateAllWarps()
        {
            if (_warpData != null)
            {
                foreach (KeyValuePair<string, TeleportGUIData.WarpPoint> kvp in _warpData)
                    yield return kvp;
            }

            foreach (KeyValuePair<string, GeneratedMonumentWarpPoint> kvp in _monumentWarps)
                yield return new KeyValuePair<string, TeleportGUIData.WarpPoint>(kvp.Key, kvp.Value);
        }

        public bool TryGetMonumentWarp(string name, out GeneratedMonumentWarpPoint warp) =>
            _monumentWarps.TryGetValue(name, out warp);

        public IEnumerable<KeyValuePair<string, GeneratedMonumentWarpPoint>> EnumerateMonumentWarps() =>
            _monumentWarps;

        public bool WarpPointHasPermission(BasePlayer player, TeleportGUIData.WarpPoint warp)
        {
            if (warp == null) return false;
            if (string.IsNullOrEmpty(warp.Permission)) return true;
            return HasPerm(player, EnsureWarpPermission(warp.Permission));
        }

        /// <summary>
        /// Destination for teleport: monument random local spawn (NPC radius rejection) or manual + vicinity.
        /// </summary>
        public Vector3 ResolveWarpPosition(TeleportGUIData.WarpPoint warp)
        {
            if (warp == null) return Vector3.zero;

            if (warp is GeneratedMonumentWarpPoint generated)
                return generated.GetPosition();

            Vector3 warpPosition = warp.Position;
            float vicinity = _config?.Warp?.VicinityTeleportRadius ?? 0f;
            if (vicinity > 0f)
            {
                Vector2 random = Random.insideUnitCircle * vicinity;
                warpPosition.x += random.x;
                warpPosition.z += random.y;
                try { warpPosition.y = TerrainMeta.HeightMap.GetHeight(warpPosition); }
                catch { }
            }

            return warpPosition;
        }

        public bool TryResolveAnyWarpPosition(string name, out Vector3 position, out TeleportGUIData.WarpPoint warp)
        {
            position = default;
            if (!TryGetAnyWarp(name, out warp))
                return false;
            position = ResolveWarpPosition(warp);
            return true;
        }

        #endregion

        #region Admin ddraw helpers

        public void ShowMonumentBounds(BasePlayer player, float time = 30f)
        {
            if (player == null || !player.IsAdmin) return;
            if (time <= 0f) time = 30f;

            foreach (MonumentInfoEntry monument in _monuments)
            {
                Bounds bounds = monument.Bounds;
                float radius = monument.Radius;

                player.SendConsoleCommand("ddraw.text", time, Color.white, monument.Transform.position, monument.Shortname);

                if (radius > 0f)
                {
                    player.SendConsoleCommand("ddraw.sphere", time, Color.blue, monument.Position, radius);
                }
                else if (bounds != default)
                {
                    Transform transform = monument.Transform;
                    Vector3 c1 = transform.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, bounds.min.z));
                    Vector3 c2 = transform.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, bounds.max.z));
                    Vector3 c3 = transform.TransformPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.min.z));
                    Vector3 c4 = transform.TransformPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.max.z));
                    Vector3 c5 = transform.TransformPoint(new Vector3(bounds.max.x, bounds.min.y, bounds.min.z));
                    Vector3 c6 = transform.TransformPoint(new Vector3(bounds.max.x, bounds.min.y, bounds.max.z));
                    Vector3 c7 = transform.TransformPoint(new Vector3(bounds.max.x, bounds.max.y, bounds.min.z));
                    Vector3 c8 = transform.TransformPoint(new Vector3(bounds.max.x, bounds.max.y, bounds.max.z));

                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c1, c2);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c1, c3);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c2, c4);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c3, c4);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c5, c6);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c5, c7);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c6, c8);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c7, c8);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c1, c5);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c2, c6);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c3, c7);
                    player.SendConsoleCommand("ddraw.line", time, Color.blue, c4, c8);
                }
            }
        }

        public void ShowGeneratedWarps(BasePlayer player)
        {
            if (player == null || !player.IsAdmin) return;

            Vector3 playerPos = player.transform.position;
            foreach (KeyValuePair<string, GeneratedMonumentWarpPoint> kvp in _monumentWarps)
            {
                if (kvp.Value?.Spawns?._spawnPoints == null) continue;
                foreach (Vector3 v in kvp.Value.Spawns._spawnPoints)
                {
                    if (Vector3.Distance(playerPos, v) >= 300f) continue;
                    player.SendConsoleCommand("ddraw.sphere", 30f, Color.blue, v, 0.5f);
                    player.SendConsoleCommand("ddraw.line", 30f, Color.blue, v, v + Vector3.up * 100f);
                }
            }
        }

        #endregion

        #region Payments

        public TeleportGUIConfig.PurchaseOptions GetPurchaseOptions(TeleportPaymentKind kind)
        {
            switch (kind)
            {
                case TeleportPaymentKind.Warp:
                    return _config?.Warp?.Purchase ?? new TeleportGUIConfig.PurchaseOptions();
                case TeleportPaymentKind.Home:
                    return _config?.Home?.Purchase ?? new TeleportGUIConfig.PurchaseOptions();
                default:
                    return _config?.Teleport?.Purchase ?? new TeleportGUIConfig.PurchaseOptions();
            }
        }

        public TeleportGUIConfig.LimitOptions GetLimitOptions(TeleportPaymentKind kind)
        {
            switch (kind)
            {
                case TeleportPaymentKind.Warp:
                    return _config?.Warp?.Limits ?? new TeleportGUIConfig.LimitOptions();
                case TeleportPaymentKind.Home:
                    return _config?.Home?.Limits ?? new TeleportGUIConfig.LimitOptions();
                default:
                    return _config?.Teleport?.Limits ?? new TeleportGUIConfig.LimitOptions();
            }
        }

        /// <summary>VIP-aware cost using prefixed permission keys (lowest matching VIP, else Default).</summary>
        public int GetTeleportCost(BasePlayer player, TeleportPaymentKind kind)
        {
            TeleportGUIConfig.PurchaseOptions purchase = GetPurchaseOptions(kind);
            return purchase.GetLowestOption(perm => HasVipPermission(player, perm));
        }

        public bool HasReachedDailyLimit(BasePlayer player, TeleportGUIData.UserData userData, TeleportPaymentKind kind)
        {
            if (player == null || userData == null) return false;

            TeleportGUIConfig.LimitOptions limits = GetLimitOptions(kind);
            if (limits.Default == 0)
                return false;

            int limit = limits.GetHighestOption(perm => HasVipPermission(player, perm));
            if (limit == 0)
                return false;

            switch (kind)
            {
                case TeleportPaymentKind.Home:
                    return (userData.HomeUsage?.UsesToday ?? userData.HomeUsesToday) >= limit;
                case TeleportPaymentKind.Warp:
                    return (userData.WarpUsage?.UsesToday ?? userData.WarpUsesToday) >= limit;
                default:
                    return (userData.TPUsage?.UsesToday ?? userData.TPUsesToday) >= limit;
            }
        }

        /// <summary>
        /// Whether payment is required given daily limits + PayAlways / PayAfterUsingDailyLimits.
        /// When at limit without PayAfter/PayAlways, returns false and sets blockedByLimit.
        /// </summary>
        public bool IsPaymentRequired(BasePlayer player, TeleportGUIData.UserData userData, TeleportPaymentKind kind, out bool blockedByLimit)
        {
            blockedByLimit = false;
            TeleportGUIConfig.PurchaseOptions purchase = GetPurchaseOptions(kind);
            bool atLimit = HasReachedDailyLimit(player, userData, kind);

            if (atLimit || purchase.PayAlways)
            {
                if (!purchase.PayAfterUsingDailyLimits && !purchase.PayAlways)
                {
                    blockedByLimit = true;
                    return false;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Charge for a teleport. Receipt stores the exact amount/currency charged for safe refunds.
        /// </summary>
        public bool TryPayForTeleport(BasePlayer player, TeleportPaymentKind kind, out PaymentReceipt receipt)
        {
            receipt = PaymentReceipt.None;
            if (player == null) return false;

            TeleportGUIConfig.PurchaseOptions purchase = GetPurchaseOptions(kind);
            int cost = GetTeleportCost(player, kind);
            TeleportGUIConfig.PurchaseMode mode = purchase.Mode;

            if (cost <= 0)
            {
                receipt = new PaymentReceipt(true, 0, mode);
                return true;
            }

            EnsureScrapDefinition();

            switch (mode)
            {
                case TeleportGUIConfig.PurchaseMode.ServerRewards:
                    TeleportGUIIntegrations.ServerRewards.Rebind();
                    if (!TeleportGUIIntegrations.ServerRewards.IsLoaded ||
                        TeleportGUIIntegrations.ServerRewards.CheckPoints(player.userID) < cost)
                    {
                        SendMessage(player, $"You do not have enough RP to purchase this teleport ({cost} RP)");
                        return false;
                    }
                    break;

                case TeleportGUIConfig.PurchaseMode.Economics:
                    TeleportGUIIntegrations.Economics.Rebind();
                    if (!TeleportGUIIntegrations.Economics.IsLoaded ||
                        Convert.ToInt32(TeleportGUIIntegrations.Economics.Balance(player.userID)) < cost)
                    {
                        SendMessage(player, $"You do not have enough coins to purchase this teleport ({cost} coins)");
                        return false;
                    }
                    break;

                case TeleportGUIConfig.PurchaseMode.Scrap:
                    if (_scrapItemDefinition == null ||
                        player.inventory.GetAmount(_scrapItemDefinition.itemid) < cost)
                    {
                        SendMessage(player, $"You do not have enough scrap to purchase this teleport ({cost} scrap)");
                        return false;
                    }
                    break;

                default:
                    UnityEngine.Debug.LogWarning("[TeleportGUI] Invalid currency type set in config!");
                    return false;
            }

            switch (mode)
            {
                case TeleportGUIConfig.PurchaseMode.ServerRewards:
                    TeleportGUIIntegrations.ServerRewards.TakePoints(player.userID, cost);
                    SendMessage(player, $"You have purchased this teleport for {cost} RP");
                    break;

                case TeleportGUIConfig.PurchaseMode.Economics:
                    TeleportGUIIntegrations.Economics.Withdraw(player.userID, cost);
                    SendMessage(player, $"You have purchased this teleport for {cost} coins");
                    break;

                case TeleportGUIConfig.PurchaseMode.Scrap:
                    player.inventory.Take(null, _scrapItemDefinition.itemid, cost);
                    SendMessage(player, $"You have purchased this teleport for {cost} scrap");
                    break;
            }

            receipt = new PaymentReceipt(true, cost, mode);
            return true;
        }

        /// <summary>Refund using the receipt's captured amount/currency (not live config).</summary>
        public void RefundPayment(BasePlayer player, PaymentReceipt receipt)
        {
            if (player == null || !receipt.WasCharged || receipt.Amount <= 0)
                return;

            int cost = receipt.Amount;
            switch (receipt.Currency)
            {
                case TeleportGUIConfig.PurchaseMode.ServerRewards:
                    TeleportGUIIntegrations.ServerRewards.Rebind();
                    TeleportGUIIntegrations.ServerRewards.AddPoints(player.userID, cost);
                    SendMessage(player, $"You were refunded {cost} RP");
                    break;

                case TeleportGUIConfig.PurchaseMode.Economics:
                    TeleportGUIIntegrations.Economics.Rebind();
                    TeleportGUIIntegrations.Economics.Deposit(player.userID, cost);
                    SendMessage(player, $"You were refunded {cost} coins");
                    break;

                case TeleportGUIConfig.PurchaseMode.Scrap:
                    EnsureScrapDefinition();
                    if (_scrapItemDefinition != null)
                    {
                        player.GiveItem(ItemManager.Create(_scrapItemDefinition, cost), BaseEntity.GiveItemReason.PickedUp);
                        SendMessage(player, $"You were refunded {cost} scrap");
                    }
                    break;
            }
        }

        /// <summary>
        /// Evaluate limit/payment gate. On success, if payment was required and charged, receipt is set.
        /// Returns false when blocked by limit or payment failure.
        /// </summary>
        public bool TryAuthorizePayment(BasePlayer player, TeleportGUIData.UserData userData, TeleportPaymentKind kind, out PaymentReceipt receipt, out bool playerIsPaying)
        {
            receipt = PaymentReceipt.None;
            playerIsPaying = false;

            if (!IsPaymentRequired(player, userData, kind, out bool blockedByLimit))
            {
                if (blockedByLimit)
                {
                    switch (kind)
                    {
                        case TeleportPaymentKind.Home:
                            SendMessage(player, "You have reached your max home teleports for today.");
                            break;
                        case TeleportPaymentKind.Warp:
                            SendMessage(player, "You have reached your max warp teleports for today.");
                            break;
                        default:
                            SendMessage(player, "You have reached your max teleports for today.");
                            break;
                    }
                    return false;
                }

                return true;
            }

            if (!TryPayForTeleport(player, kind, out receipt))
                return false;

            playerIsPaying = receipt.WasCharged && receipt.Amount > 0;
            return true;
        }

        private static void EnsureScrapDefinition()
        {
            if (_scrapItemDefinition != null) return;
            try { _scrapItemDefinition = ItemManager.FindItemDefinition("scrap"); }
            catch { _scrapItemDefinition = null; }
        }

        #endregion

        #region Nested types (Oxide Monument / MonumentWarpPoint / LocalSpawnGenerator)

        public sealed class MonumentInfoEntry
        {
            public string Shortname { get; }
            public bool IsSafeZone { get; }
            public Transform Transform { get; }
            public Vector3 Position { get; }
            public float Radius { get; }
            public Bounds Bounds { get; }

            public MonumentInfoEntry(string shortname, bool isSafeZone, Transform transform, Vector3 position, float radius, Bounds bounds)
            {
                Shortname = shortname;
                IsSafeZone = isSafeZone;
                Transform = transform;
                Position = position;
                Radius = radius;
                Bounds = bounds;
            }

            public bool IsInMonument(Vector3 position)
            {
                if (Bounds == default || Radius > 0f)
                    return Vector3.Distance(Position, position) < Radius;

                Vector3 local = Transform.InverseTransformPoint(position);
                return Bounds.Contains(local);
            }
        }

        /// <summary>
        /// In-memory generated monument warp. Subclasses WarpPoint so TryGetAnyWarp/EnumerateAllWarps
        /// can surface it alongside manual warpdata without persisting it.
        /// </summary>
        public sealed class GeneratedMonumentWarpPoint : TeleportGUIData.WarpPoint
        {
            internal LocalSpawnGenerator Spawns;

            public string Shortname { get; }
            public string MapGrid { get; }
            public string UniqueName { get; }

            public int SpawnCount => Spawns?.Count ?? 0;

            public GeneratedMonumentWarpPoint(
                string shortname,
                string grid,
                string uniqueName,
                Transform transform,
                Bounds bounds,
                Vector3 position,
                float radius,
                bool safeZoneOnly)
            {
                Shortname = shortname;
                MapGrid = grid;
                UniqueName = uniqueName;
                Permission = string.Empty;
                Command = string.Empty;

                Spawns = new LocalSpawnGenerator(shortname, transform, bounds, position, radius, safeZoneOnly);
                if (Spawns.Count == 0)
                {
                    UnityEngine.Debug.Log($"[TeleportGUI] Failed to generate spawn points in monument {uniqueName}");
                    Spawns = null;
                }
            }

            public bool IsEnabled() => Spawns != null && Spawns.Count > 0;

            public Vector3 GetPosition() => Spawns != null ? Spawns.GetRandom() : Position;

            public Vector3 PeekPosition() =>
                Spawns != null && Spawns._spawnPoints != null && Spawns._spawnPoints.Count > 0
                    ? Spawns._spawnPoints[0]
                    : Position;
        }

        public sealed class LocalSpawnGenerator
        {
            internal List<Vector3> _spawnPoints;
            private List<Vector3> _availablePoints;

            public int Count => _spawnPoints?.Count ?? 0;

            private const int TargetLayers = ~(1 << 2 | 1 << 10 | 1 << 11 | 1 << 18 | 1 << 28 | 1 << 29);

            private static readonly string[] ValidColliderNames =
            {
                "road", "carpark", "concrete_slabs", "pavement", "walkway", "cliff", "Cliff",
                "a_lighthouse_ext", "ice_lake"
            };

            public LocalSpawnGenerator(string shortname, Transform transform, Bounds bounds, Vector3 position, float radius, bool safeZoneOnly)
            {
                _spawnPoints = new List<Vector3>();

                float maxDistance = radius > 0f || bounds.size != Vector3.zero
                    ? Mathf.Max(radius * 2f, 400f)
                    : bounds.size.y;

                if (position != default && bounds.size != Vector3.zero && transform != null)
                    position = transform.TransformPoint(bounds.center);

                for (int i = 0; i < 500; i++)
                {
                    Vector3 p = radius > 0f
                        ? GetRandomPointInRadius(position, radius, bounds)
                        : GetRandomPointInBounds(transform, bounds);

                    if (Physics.SphereCast(new Ray(p, Vector3.down), 0.5f, out RaycastHit raycastHit, maxDistance, TargetLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (raycastHit.collider != null && raycastHit.collider.GetComponent<ProceduralObject>())
                            continue;

                        if (Mathf.Abs(Vector3.Dot(raycastHit.normal, Vector3.up)) < 0.9f)
                            continue;

                        float waterSurface = WaterLevel.GetWaterSurface(raycastHit.point, true, true, null);
                        if (waterSurface > raycastHit.point.y)
                            continue;

                        if (safeZoneOnly && !IsInSafeZone(raycastHit.point))
                            continue;

                        if (raycastHit.collider != null &&
                            string.Equals(raycastHit.collider.GetType().Name, "TerrainCollider", StringComparison.Ordinal))
                        {
                            _spawnPoints.Add(raycastHit.point);
                        }
                        else if (raycastHit.collider != null &&
                                 ValidColliderNames.Any(n => raycastHit.collider.name.IndexOf(n, StringComparison.Ordinal) >= 0))
                        {
                            _spawnPoints.Add(raycastHit.point);
                        }
                    }

                    if (Count >= 30)
                        break;
                }

                _availablePoints = new List<Vector3>(_spawnPoints);
            }

            private static bool IsInSafeZone(Vector3 position)
            {
                // Avoid Vis.* (pulls OBB from Rust.Global which this project does not reference).
                Collider[] hits = Physics.OverlapSphere(position, 0.05f, -1, QueryTriggerInteraction.Collide);
                if (hits == null) return false;
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider collider = hits[i];
                    if (collider != null && collider.GetComponent<TriggerSafeZone>())
                        return true;
                }

                return false;
            }

            private static Vector3 GetRandomPointInRadius(Vector3 position, float radius, Bounds bounds)
            {
                Vector2 random = Random.insideUnitCircle * radius;
                float y = bounds != default ? bounds.center.y + bounds.extents.y : 200f;
                return position + new Vector3(random.x, y, random.y);
            }

            private static Vector3 GetRandomPointInBounds(Transform transform, Bounds bounds)
            {
                Vector3 target = new Vector3(
                    Random.Range(bounds.min.x, bounds.max.x),
                    bounds.max.y,
                    Random.Range(bounds.min.z, bounds.max.z));
                Vector3 local = bounds.ClosestPoint(target);
                return transform != null ? transform.TransformPoint(local) : local;
            }

            public Vector3 GetRandom(int attempt = 0)
            {
                if (_availablePoints == null || _availablePoints.Count == 0)
                {
                    if (_spawnPoints == null || _spawnPoints.Count == 0)
                        return Vector3.zero;
                    _availablePoints = new List<Vector3>(_spawnPoints);
                }

                int index = Random.Range(0, _availablePoints.Count);
                Vector3 point = _availablePoints[index];
                _availablePoints.RemoveAt(index);

                float npcRadius = Instance?._config?.Warp?.MonumentWarpNPCRadius ?? 25f;

                if (attempt < 5)
                {
                    bool hasNpcsNear = HasNpcsNear(point, npcRadius);

                    if (_availablePoints.Count == 0 && _spawnPoints != null)
                        _availablePoints.AddRange(_spawnPoints);

                    if (hasNpcsNear)
                        return GetRandom(attempt + 1);
                }

                return point;
            }

            private static bool HasNpcsNear(Vector3 point, float radius)
            {
                // Avoid Vis.Entities / Rust.Layer (Rust.Global). Player_Server == layer 17.
                Collider[] hits = Physics.OverlapSphere(point, radius, PlayerServerLayerMask, QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0) return false;

                for (int i = 0; i < hits.Length; i++)
                {
                    Collider c = hits[i];
                    if (c == null) continue;
                    BasePlayer bp = c.GetComponentInParent<BasePlayer>();
                    if (bp != null && bp.IsNpc)
                        return true;
                }

                return false;
            }
        }

        #endregion
    }
}
