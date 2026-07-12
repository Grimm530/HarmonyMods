using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Facepunch;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Radar;

/// <summary>
/// Attached to player when radar is on. Scans entities and sends ddraw commands.
/// Like AdminRadar: no clear each refresh — draw every tick with duration = refresh+0.1f so markers stay and move with the object until out of range.
/// </summary>
public class RadarBehaviour : MonoBehaviour
{
    private BasePlayer _player;
    private RadarMod _mod;
    private RadarState _state;
    private float _nextScan;
    private const float MaxVisScanRadius = 200f;
    /// <summary>Small box at item (little blueish-green style).</summary>
    private const float SmallBoxSize = 0.25f;
    private static readonly List<BaseEntity> _scanBuffer = new List<BaseEntity>(512);

    /// <summary>Same as <c>GrimmNPC.CUSTOM_NPC_SKIN_ID</c> — GrimmBoss/GrimmNPC custom NPCs.</summary>
    private const ulong GrimmCustomNpcSkinId = 11162132011012UL;

    private static MethodInfo _grimmGetNpcData;
    private static PropertyInfo _grimmCustomNpcDataName;
    private static bool _grimmReflectionResolved;
    private static Dictionary<string, string> _deployableItems;
    private static bool _deployableItemsInitialized;

    private void Awake()
    {
        _player = GetComponent<BasePlayer>();
        _mod = RadarMod.Instance;
    }

    private void OnDestroy()
    {
        if (_player != null && _player.IsConnected)
            _player.Command("ddraw.clear");
    }

    private void Update()
    {
        if (_player == null || !_player.IsConnected || _mod == null)
        {
            Destroy(gameObject.GetComponent<RadarBehaviour>());
            return;
        }
        if (!_player.IsAdmin && !_player.IsDeveloper)
        {
            _player.Command("ddraw.clear");
            _mod?.DestroyMoveUI(_player);
            _mod?.DestroyUI(_player);
            Destroy(gameObject.GetComponent<RadarBehaviour>());
            return;
        }
        _state = _mod.GetOrCreateState(_player);
        if (_state == null || !_state.Enabled) return;

        if (UnityEngine.Time.time < _nextScan) return;
        float refreshInterval = Mathf.Clamp(_state.RefreshInterval, RadarState.MinRefreshInterval, RadarState.MaxRefreshInterval);
        _nextScan = UnityEngine.Time.time + refreshInterval;
        float drawDuration = refreshInterval + 0.1f;

        var pos = _player.transform.position;
        var maxDist = _state.ViewDistance;

        // Per-type drawing distances (mirrors AdminRadar \"Drawing Distances\" section).
        float distPlayers = RadarConfig.GetDistanceFor(RadarEntityType.Players, maxDist);
        float distSleepers = RadarConfig.GetDistanceFor(RadarEntityType.Sleepers, maxDist);
        float distDead = RadarConfig.GetDistanceFor(RadarEntityType.Dead, maxDist);
        float distBags = RadarConfig.GetDistanceFor(RadarEntityType.Bags, maxDist);
        float distTc = RadarConfig.GetDistanceFor(RadarEntityType.TC, maxDist);
        float distStash = RadarConfig.GetDistanceFor(RadarEntityType.Stash, maxDist);
        float distBackpack = RadarConfig.GetDistanceFor(RadarEntityType.Backpack, maxDist);
        float distBox = RadarConfig.GetDistanceFor(RadarEntityType.Box, maxDist);
        float distLoot = RadarConfig.GetDistanceFor(RadarEntityType.Loot, maxDist);
        float distNpc = RadarConfig.GetDistanceFor(RadarEntityType.Npc, maxDist);
        float distOre = RadarConfig.GetDistanceFor(RadarEntityType.Ore, maxDist);
        float distTrap = RadarConfig.GetDistanceFor(RadarEntityType.Trap, maxDist);
        float distTurret = RadarConfig.GetDistanceFor(RadarEntityType.Turret, maxDist);
        float distCol = RadarConfig.GetDistanceFor(RadarEntityType.Col, maxDist);
        float distAirdrop = RadarConfig.GetDistanceFor(RadarEntityType.Airdrop, maxDist);
        float distCctv = RadarConfig.GetDistanceFor(RadarEntityType.CCTV, maxDist);
        float distMlrs = RadarConfig.GetDistanceFor(RadarEntityType.MLRS, maxDist);
        float distPrefab = RadarConfig.GetDistanceFor(RadarEntityType.Prefab, maxDist);

        float sqPlayers = distPlayers * distPlayers;
        float sqSleepers = distSleepers * distSleepers;
        float sqDead = distDead * distDead;
        float sqBags = distBags * distBags;
        float sqTc = distTc * distTc;
        float sqStash = distStash * distStash;
        float sqBackpack = distBackpack * distBackpack;
        float sqBox = distBox * distBox;
        float sqLoot = distLoot * distLoot;
        float sqNpc = distNpc * distNpc;
        float sqOre = distOre * distOre;
        float sqTrap = distTrap * distTrap;
        float sqTurret = distTurret * distTurret;
        float sqCol = distCol * distCol;
        float sqAirdrop = distAirdrop * distAirdrop;
        float sqCctv = distCctv * distCctv;
        float sqMlrs = distMlrs * distMlrs;
        float sqPrefab = distPrefab * distPrefab;

        var scanRadius = RadarConfig.GetScanRadius(maxDist);

        if (_state.IsEnabled(RadarEntityType.Players))
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || p.IsDead() || p == _player || p.IsNpc) continue;
                var v = p.transform.position - pos;
                if (v.sqrMagnitude > sqPlayers) continue;
                var color = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.OnlinePlayer, Color.green);
                var label = GetCheats(p) + (p.displayName ?? "");
                DrawPlayerMarker(p, label, color, drawDuration);
            }
        }

        // Gen1 humanoid NPCs: AdminRadar FillCache uses foreach (BaseNetworkable in serverEntities) + validNPC (BasePlayer, !IsKilled, !Steam id).
        // Harmony must draw ddraw every refresh; a coroutine that yields was stopped each tick before finishing — no markers. Scan synchronously here.
        // Gen2 scientists (ScientistNPC2) are BaseNPC2 — handled in Vis.Entities below.
        if (_state.IsEnabled(RadarEntityType.Npc))
        {
            var npcCol = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.NPC, Color.yellow);
            DrawNpcBasePlayersFromWorld(pos, sqNpc, npcCol, drawDuration);
        }

        if (_state.IsEnabled(RadarEntityType.Sleepers))
        {
            foreach (var p in BasePlayer.sleepingPlayerList)
            {
                if (p == null || p.IsDead()) continue;
                var v = p.transform.position - pos;
                if (v.sqrMagnitude > sqSleepers) continue;
                var baseHex = p.IsAlive()
                    ? RadarConfig.Config?.ColorHexCodes?.SleepingPlayer
                    : RadarConfig.Config?.ColorHexCodes?.SleepingDeadPlayer;
                var fallback = p.IsAlive() ? Color.cyan : new Color(0.5f, 0.3f, 0.3f);
                var col = RadarConfig.GetColorFromHex(baseHex, fallback);
                var label = GetCheats(p) + (p.displayName ?? "");
                DrawPlayerMarker(p, label, col, drawDuration);
            }
        }

        _scanBuffer.Clear();
        var visRadius = Mathf.Min(scanRadius, MaxVisScanRadius);
        Vis.Entities(pos, visRadius, _scanBuffer);

        foreach (var e in _scanBuffer)
        {
            if (e == null || e.IsDestroyed) continue;
            if (e == _player) continue; // never draw a box around the local player (e.g. Prefab)
            var v = e.transform.position - pos;
            var sqr = v.sqrMagnitude;

            // Non–BasePlayer NPCs (AdminRadar: TravellingVendor; Gen2 humanoids e.g. ScientistNPC2 are BaseNPC2 with IsAnimal == false).
            if (_state.IsEnabled(RadarEntityType.Npc) && sqr <= sqNpc)
            {
                if (e is TravellingVendor vendor)
                {
                    var npcCol = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.NPC, Color.yellow);
                    var vname = vendor.ShortPrefabName ?? "Vendor";
                    DrawEntity(vendor.transform.position + Vector3.up, npcCol, vname, SmallBoxSize, drawDuration);
                    TryDrawNpcTargetVictim(vendor, vendor.transform.position, npcCol, sqr, sqNpc, drawDuration);
                    continue;
                }
                if (e is BaseNPC2 gen2 && !gen2.IsAnimal)
                {
                    var npcCol = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.NPC, Color.yellow);
                    var gname = gen2.ShortPrefabName ?? "Npc";
                    DrawEntity(gen2.transform.position + Vector3.up, npcCol, gname, SmallBoxSize, drawDuration);
                    TryDrawNpcTargetVictim(gen2, gen2.transform.position, npcCol, sqr, sqNpc, drawDuration);
                    continue;
                }
            }

            if (_state.IsEnabled(RadarEntityType.Dead) && e is PlayerCorpse corpse)
            {
                if (sqr > sqDead) continue;
                var ow = corpse.playerSteamID.ToString();
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.DeadPlayer, Color.red);
                DrawEntity(corpse.transform.position + Vector3.up, col, $"Dead {ow}", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.Bags) && e is SleepingBag bag)
            {
                if (sqr > sqBags) continue;
                var ownerId = bag.deployerUserID.ToString();
                var owner = BasePlayer.FindByID(bag.deployerUserID);
                var name = owner != null ? owner.displayName : ownerId;
                var label = owner != null ? $"Bag {name} ({ownerId})" : $"Bag {ownerId}";
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.SleepingBags, Color.yellow);
                DrawEntity(bag.transform.position + Vector3.up, col, label, SmallBoxSize, drawDuration);
                continue;
            }

            if (e is BuildingPrivlidge tc)
            {
                if (_state.IsEnabled(RadarEntityType.TC) && sqr <= sqTc)
                {
                    var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.ToolCupboards, new Color(1f, 0.6f, 0f));
                    DrawEntity(tc.transform.position + Vector3.up * 2f, col, "TC", SmallBoxSize, drawDuration);
                }
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.Stash) && e is StashContainer stash)
            {
                if (sqr > sqStash) continue;
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Stash, new Color(0.8f, 0.4f, 0.8f));
                DrawEntity(stash.transform.position + Vector3.up, col, "Stash", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.Backpack) && e is DroppedItemContainer backpack)
            {
                if (sqr > sqBackpack) continue;
                var oid = backpack.playerSteamID.ToString();
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Backpacks, new Color(0.6f, 0.8f, 0.6f));
                DrawEntity(backpack.transform.position + Vector3.up, col, $"Backpack {oid}", SmallBoxSize, drawDuration);
                continue;
            }

            if (e is StorageContainer storage)
            {
                // Loot first (loot crates, LockedByEntCrate, LootContainer); then Box (Options -> Boxes).
                if (_state.IsEnabled(RadarEntityType.Loot) && (storage is LootContainer || storage is LockedByEntCrate || IsLoot(storage)) && sqr <= sqLoot)
                {
                    var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Loot, Color.yellow);
                    DrawEntity(storage.transform.position + Vector3.up, col, "Loot", SmallBoxSize, drawDuration);
                    continue;
                }
                if (_state.IsEnabled(RadarEntityType.Box) && IsBox(storage) && sqr <= sqBox)
                {
                    var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Box, Color.magenta);
                    DrawEntity(storage.transform.position + Vector3.up, col, "Box", SmallBoxSize, drawDuration);
                    continue;
                }
            }

            if (_state.IsEnabled(RadarEntityType.Ore) && e is OreResourceEntity ore && sqr <= sqOre)
            {
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Resources, Color.yellow);
                DrawEntity(ore.transform.position + Vector3.up, col, "Ore", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.Trap) && IsTrap(e) && sqr <= sqTrap)
            {
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Traps, Color.magenta);
                DrawEntity(e.transform.position + Vector3.up, col, "Trap", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.Turret) && e is AutoTurret turret && sqr <= sqTurret)
            {
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.AutoTurrets, Color.yellow);
                DrawEntity(turret.transform.position + Vector3.up * 1.5f, col, "Turret", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.Col) && e is CollectibleEntity colEnt && sqr <= sqCol)
            {
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Collectibles, Color.yellow);
                DrawEntity(colEnt.transform.position + Vector3.up, col, "Col", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.Airdrop) && e is SupplyDrop supplyDrop && sqr <= sqAirdrop)
            {
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Airdrops, Color.magenta);
                DrawEntity(supplyDrop.transform.position + Vector3.up * 2f, col, "Airdrop", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.CCTV) && sqr <= sqCctv && (e.GetType().Name == "CCTV_RC" || e.GetType().Name == "Drone"))
            {
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.CCTV, Color.magenta);
                DrawEntity(e.transform.position + Vector3.up, col, "CCTV", SmallBoxSize, drawDuration);
                continue;
            }

            if (_state.IsEnabled(RadarEntityType.MLRS) && e.GetType().Name == "MLRSRocket" && sqr <= sqMlrs)
            {
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.MLRS, Color.magenta);
                DrawEntity(e.transform.position + Vector3.up, col, "MLRS", SmallBoxSize, drawDuration);
                continue;
            }

            // Prefab: only building entities and deployables (no players, world items, ore, etc.).
            if (_state.IsEnabled(RadarEntityType.Prefab) && sqr <= sqPrefab && (e is BuildingBlock || e is DecayEntity))
            {
                var name = e.ShortPrefabName ?? "";
                if (string.IsNullOrEmpty(name)) name = e.prefabID.ToString();
                else name = name + " (" + e.prefabID + ")";
                var col = RadarConfig.GetColorFromHex(RadarConfig.Config?.ColorHexCodes?.Prefab, new Color(0f, 1f, 1f));
                DrawEntity(e.transform.position + Vector3.up, col, name, SmallBoxSize, drawDuration);
            }
        }
    }

    private static void EnsureDeployableItems()
    {
        if (_deployableItemsInitialized)
            return;
        _deployableItemsInitialized = true;
        _deployableItems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in ItemManager.GetItemDefinitions())
        {
            if (!def.TryGetComponent<ItemModDeployable>(out var imd))
                continue;
            var entity = imd.entityPrefab.Get()?.GetComponent<StorageContainer>();
            if (entity == null)
                continue;
            if (!string.Equals(entity.ShortPrefabName, def.shortname, StringComparison.Ordinal))
                _deployableItems[entity.ShortPrefabName] = def.shortname;
        }
    }

    private static bool IsBox(BaseEntity e)
    {
        var list = RadarConfig.Config?.Options?.Boxes;
        if (list == null || list.Count == 0)
            return false;

        EnsureDeployableItems();
        var shortName = e?.ShortPrefabName ?? "";
        for (int i = 0; i < list.Count; i++)
        {
            if (shortName.IndexOf(list[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        if (_deployableItems == null || !_deployableItems.TryGetValue(shortName, out var itemShortname))
            return false;

        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], itemShortname, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsLoot(BaseEntity e)
    {
        var shortName = e?.ShortPrefabName ?? "";
        return shortName.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0
            || shortName.IndexOf("crate_", StringComparison.OrdinalIgnoreCase) >= 0
            || shortName.IndexOf("trash", StringComparison.OrdinalIgnoreCase) >= 0
            || shortName.IndexOf("hackable", StringComparison.OrdinalIgnoreCase) >= 0
            || shortName.IndexOf("oil", StringComparison.OrdinalIgnoreCase) >= 0
            || shortName.IndexOf("foodbox", StringComparison.OrdinalIgnoreCase) >= 0
            || shortName.IndexOf("vehicle_parts", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Label for Gen1 NPC players. Prefab-name mode and <c>displayName</c> branch match <c>AdminRadar.CacheNpc</c> (~2437).
    /// NpcSpawn <c>Config.Name</c> / Grimm <c>GetNpcData</c> run before <c>displayName</c> so Harmony sees boss names when vanilla shows &quot;Scientist&quot;.
    /// </summary>
    private static string NpcBasePlayerLabel(BasePlayer p)
    {
        if (p == null) return "Npc";
        var opt = RadarConfig.Config?.Options;

        if (opt?.ShowNpcNameAsPrefabName == true)
            return p.ShortPrefabName ?? "Npc";

        var npcSpawnBoss = TryNpcSpawnBossDisplayName(p);
        if (!string.IsNullOrEmpty(npcSpawnBoss))
            return npcSpawnBoss;
        var grimm = TryGrimmRegisteredNpcName(p);
        if (!string.IsNullOrEmpty(grimm))
            return grimm;

        if (!string.IsNullOrEmpty(p.displayName) && p.displayName != p.UserIDString)
            return p.displayName;

        var shortName = p.ShortPrefabName ?? "";
        if (shortName == "scarecrow")
            return "scarecrow";

        var prefabName = p.PrefabName ?? "";
        if (prefabName.IndexOf("scientist", StringComparison.OrdinalIgnoreCase) >= 0)
            return "scientist";

        return string.IsNullOrEmpty(shortName) ? "Npc" : shortName;
    }

    /// <summary>Facepunch Steam account IDs are in the 76561197960265728+ range; NPC/bot user IDs are outside this (matches Oxide <c>userID.IsSteamId()</c> for filtering).</summary>
    private static bool IsSteamAccountId(ulong userId) => userId >= 76561197960265728UL;

    /// <summary>AdminRadar <c>IsAtView</c> depth filter when <c>Show NPC At World View</c> is on (~2478).</summary>
    private bool PassesNpcWorldViewDepth(Vector3 targetPosition)
    {
        var opt = RadarConfig.Config?.Options;
        if (opt == null || !opt.ShowNpcAtWorldView)
            return true;
        float py = _player.transform.position.y;
        float ty = targetPosition.y;
        if (py > 0f && ty < -3f)
            return false;
        if (ty > 0f && py < -3f)
            return false;
        return true;
    }

    /// <summary>
    /// AdminRadar <c>validNPC</c> (<c>FillCache</c> ~2849): <c>BasePlayer</c>, alive, <c>!userID.IsSteamId()</c> (Harmony: <c>IsDead()</c>, <c>IsSteamAccountId</c>).
    /// Same iteration as <c>foreach (BaseNetworkable net in BaseNetworkable.serverEntities)</c> (~2797).
    /// </summary>
    private void DrawNpcBasePlayersFromWorld(Vector3 observerPos, float sqNpc, Color npcCol, float drawDuration)
    {
        foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
        {
            var p = net as BasePlayer;
            if (p == null || p.IsDestroyed || p == _player || p.IsDead())
                continue;
            ulong uid = p.userID;
            if (IsSteamAccountId(uid))
                continue;
            var v = p.transform.position - observerPos;
            if (v.sqrMagnitude > sqNpc)
                continue;
            if (!PassesNpcWorldViewDepth(p.transform.position))
                continue;
            DrawEntity(p.transform.position + Vector3.up, npcCol, NpcBasePlayerLabel(p), SmallBoxSize, drawDuration);
            TryDrawNpcTargetVictim(p, p.transform.position, npcCol, v.sqrMagnitude, sqNpc, drawDuration);
        }
    }

    private void TryDrawNpcTargetVictim(BaseEntity entity, Vector3 entityPos, Color color, float sqrDist, float sqNpc, float drawDuration)
    {
        if (!RadarConfig.Config?.Options?.ShowNpcPlayerTarget == true)
            return;
        if (sqrDist > sqNpc)
            return;
        if (!entity.HasBrain)
            return;

        var players = GetPlayersFromBrain(entity);
        if (players == null || players.Count == 0)
            return;

        BasePlayer victim = null;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null)
            {
                victim = players[i];
                break;
            }
        }

        if (victim == null)
            return;

        float dist = Mathf.Sqrt(sqrDist);
        var offset = new Vector3(0f, 2f + dist * 0.03f, 0f);
        DrawNpcTargetVictim(victim, entityPos, offset, color, drawDuration);
    }

    private static List<BasePlayer> GetPlayersFromBrain(BaseEntity entity)
    {
        var players = new List<BasePlayer>();
        if (!entity.TryGetComponent(out BaseAIBrain brain))
            return players;
        if (brain.Senses?.Players == null)
            return players;

        foreach (var ent in brain.Senses.Players)
        {
            if (ent is BasePlayer player && IsSteamAccountId(player.userID))
                players.Add(player);
        }

        return players;
    }

    private void DrawNpcTargetVictim(BasePlayer victim, Vector3 from, Vector3 offset, Color color, float duration)
    {
        if (_player == null || !_player.IsConnected || victim == null)
            return;

        string victimColor;
        if (victim.IsSleeping())
            victimColor = RadarConfig.Config?.ColorHexCodes?.SleepingPlayer ?? "#00ffff";
        else if (victim.IsAlive())
            victimColor = "#00ff00";
        else
            victimColor = RadarConfig.Config?.ColorHexCodes?.OnlineDeadPlayer ?? "#ff0000";

        var text = $"<color={victimColor}>{victim.displayName}</color>";
        var pos = from + offset;
        _player.Command("ddraw.text", duration, color, pos, DdrawSizedPlayerName("T: " + text), 0, 0);
    }

    private static string GetCheats(BasePlayer target)
    {
        var track = RadarConfig.Config?.TrackAdminStatus;
        if (track == null || !track.Any || target == null)
            return string.Empty;

        var sb = new StringBuilder();
        if (track.Radar && !string.IsNullOrWhiteSpace(track.RadarText) && RadarMod.IsRadarActive(target.userID))
            sb.Append(track.RadarText).Append('|');
        if (track.Spectating && target.SpectatingTarget != null && !string.IsNullOrWhiteSpace(track.SpectatingText))
            sb.Append(track.SpectatingText).Append('|');
        if (track.God && !string.IsNullOrWhiteSpace(track.GodText) && target.IsGod())
            sb.Append(track.GodText).Append('|');
        if (track.GodPlugin && !string.IsNullOrWhiteSpace(track.GodPluginText) && target.metabolism?.calories?.min == 500)
            sb.Append(track.GodPluginText).Append('|');
        if (track.Vanish && !string.IsNullOrWhiteSpace(track.VanishText) && (target.limitNetworking || target.isInvisible))
            sb.Append(track.VanishText).Append('|');
        if (track.NoClip && !string.IsNullOrWhiteSpace(track.NoClipText) && target.IsFlying)
            sb.Append(track.NoClipText).Append('|');

        if (sb.Length == 0)
            return string.Empty;

        sb.Length -= 1;
        sb.Insert(0, '(');
        sb.Append(") ");
        return sb.ToString();
    }

    /// <summary>BossMonster / NpcSpawn: <c>CustomScientistNpc</c> exposes <c>Config.Name</c> (no Grimm registration).</summary>
    private static string TryNpcSpawnBossDisplayName(BasePlayer p)
    {
        if (p == null) return null;
        var t = p.GetType();
        if (t.Name != "CustomScientistNpc" && (t.FullName == null || t.FullName.IndexOf("CustomScientistNpc", StringComparison.Ordinal) < 0))
            return null;
        try
        {
            var configProp = t.GetProperty("Config", BindingFlags.Public | BindingFlags.Instance);
            object cfg = configProp?.GetValue(p);
            if (cfg == null) return null;
            var nameProp = cfg.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            var name = nameProp?.GetValue(cfg) as string;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resolves <c>Name</c> from GrimmNPC registration (matches GrimmBoss JSON <c>Name</c> / boss profile).</summary>
    private static string TryGrimmRegisteredNpcName(BasePlayer p)
    {
        if (p.skinID != GrimmCustomNpcSkinId)
            return null;
        ulong netId = p.net?.ID.Value ?? 0;
        if (netId == 0)
            return null;
        if (!TryResolveGrimmNpcApi())
            return null;
        try
        {
            object data = _grimmGetNpcData.Invoke(null, new object[] { netId });
            if (data == null)
                return null;
            return _grimmCustomNpcDataName?.GetValue(data) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryResolveGrimmNpcApi()
    {
        if (_grimmReflectionResolved)
            return _grimmGetNpcData != null && _grimmCustomNpcDataName != null;
        _grimmReflectionResolved = true;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type grimmType = asm.GetType("GrimmNPC.GrimmNPC", false);
                if (grimmType == null)
                    continue;
                MethodInfo m = grimmType.GetMethod("GetNpcData", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ulong) }, null);
                if (m == null)
                    continue;
                Type dataType = asm.GetType("GrimmNPC.CustomNpcData", false);
                PropertyInfo nameProp = dataType?.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                if (nameProp == null)
                    continue;
                _grimmGetNpcData = m;
                _grimmCustomNpcDataName = nameProp;
                return true;
            }
        }
        catch
        {
            // GrimmNPC not present or API changed
        }
        return false;
    }

    private static bool IsTrap(BaseEntity e)
    {
        if (e is BaseTrap) return true;
        var list = RadarConfig.Config?.Options?.AdditionalTraps;
        if (list == null || list.Count == 0) return false;
        var shortName = e?.ShortPrefabName ?? "";
        for (int i = 0; i < list.Count; i++)
            if (shortName.IndexOf(list[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>Team-style marker: small dot above head + name in green (like the game's teammate UI).</summary>
    private static string DdrawSizedPlayerName(string text)
    {
        var sz = RadarConfig.Config?.Settings?.PlayerNameTextSize ?? 24;
        if (sz <= 0 || string.IsNullOrEmpty(text))
            return text ?? "";
        return "<size=" + sz + ">" + text + "</size>";
    }

    private static string DdrawSizedEntityLabel(string text)
    {
        var sz = RadarConfig.Config?.Settings?.EntityNameTextSize ?? 24;
        if (sz <= 0 || string.IsNullOrEmpty(text))
            return text ?? "";
        return "<size=" + sz + ">" + text + "</size>";
    }

    private void DrawPlayerMarker(BasePlayer target, string displayName, Color color, float duration)
    {
        if (_player == null || !_player.IsConnected || target == null) return;
        float dotHeight = 2.2f;
        float nameHeight = 2.5f;
        if (target.IsSpectating())
        {
            dotHeight += 1f;
            nameHeight += 1f;
        }
        const float dotRadius = 0.15f;
        var feetPosition = target.transform.position;
        var dotPos = feetPosition + Vector3.up * dotHeight;
        var namePos = feetPosition + Vector3.up * nameHeight;
        _player.Command("ddraw.sphere", duration, color, dotPos, dotRadius, 0, 0);
        _player.Command("ddraw.text", duration, color, namePos, DdrawSizedPlayerName(displayName ?? ""), 0, 0);
    }

    private void DrawEntity(Vector3 position, Color color, string text, float boxSize, float duration)
    {
        if (_player == null || !_player.IsConnected) return;
        _player.Command("ddraw.box", duration, color, position, $"{boxSize} {boxSize} {boxSize}", Vector3.zero, 0, 0);
        _player.Command("ddraw.text", duration, color, position + Vector3.up * 1.5f, DdrawSizedEntityLabel(text ?? ""), 0, 0);
    }
}
