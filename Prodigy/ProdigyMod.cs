using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ConVar;
using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using UnityEngine;

namespace Prodigy;

public class ProdigyMod : IHarmonyModHooks
{
    public static ProdigyMod Instance { get; private set; }

    private const float ShowUiForSeconds = 5f;
    private const string UiPanel = "PRODIGY_BACKDROP";

    private ProdigyData _data;
    private ProdigyConfig _config;
    private string _configPath;
    private string _dataPath;
    private HarmonyLib.Harmony _harmony;
    private GameObject _tickObject;
    private ProdigyCloseScheduler _closeScheduler;
    private readonly object _dataLock = new();
    private float _saveTimer;
    private readonly Dictionary<string, int> _deployables = new();
    private static MethodInfo _playtimeTrackerGetLastSeen;

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        _harmony = new HarmonyLib.Harmony("com.prodigy.patches");
        _harmony.PatchAll(typeof(ProdigyMod).Assembly);

        _configPath = Path.Combine(Environment.CurrentDirectory, "HarmonyConfig", "Prodigy.json");
        LoadConfig();

        var dataDir = Path.Combine(Environment.CurrentDirectory, _config?.DataFolder ?? "HarmonyData/Prodigy");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        _dataPath = Path.Combine(dataDir, "ProdigyData.json");
        LoadData();

        _tickObject = new GameObject("ProdigyTick");
        UnityEngine.Object.DontDestroyOnLoad(_tickObject);
        _tickObject.AddComponent<ProdigyTickBehaviour>();
        _closeScheduler = _tickObject.AddComponent<ProdigyCloseScheduler>();

        RegisterCommands();
        UnityEngine.Debug.Log("[Prodigy] Loaded. Commands: /prod or prodigy (use while looking at entity)");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        if (_tickObject != null) { UnityEngine.Object.Destroy(_tickObject); _tickObject = null; }
        foreach (var player in BasePlayer.activePlayerList)
        {
            if (CanUse(player)) ProdigyUI.Destroy(player);
        }
        SaveData();
        _harmony?.UnpatchAll("com.prodigy.patches");
        UnregisterCommands();
        Instance = null;
        UnityEngine.Debug.Log("[Prodigy] Unloaded.");
    }

    private void LoadConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(_configPath))
                _config = JsonConvert.DeserializeObject<ProdigyConfig>(File.ReadAllText(_configPath));
            _config ??= new ProdigyConfig();
            File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
        }
        catch (Exception ex) { UnityEngine.Debug.LogWarning($"[Prodigy] Config: {ex.Message}"); _config = new ProdigyConfig(); }
    }

    private void LoadData()
    {
        lock (_dataLock)
        {
            try
            {
                if (File.Exists(_dataPath))
                    _data = JsonConvert.DeserializeObject<ProdigyData>(File.ReadAllText(_dataPath));
                _data ??= new ProdigyData();
                _data.Blocks ??= new Dictionary<ulong, List<LogObject>>();
                _data.TC ??= new Dictionary<ulong, List<LogObject>>();
                _data.Offsets ??= new Dictionary<ulong, UiOffsets>();
                foreach (var ui in _data.Offsets.Values)
                {
                    if (ui == null) continue;
                    ui.MigratePanelSize();
                    if (ui.Changed) _data.Changed = true;
                }

                var currentWipeId = SaveRestore.WipeId;
                if (!string.IsNullOrEmpty(currentWipeId) && _data.WipeId != currentWipeId)
                {
                    _data.Blocks.Clear();
                    _data.TC.Clear();
                    _data.WipeId = currentWipeId;
                    _data.Changed = true;
                }
            }
            catch { _data = new ProdigyData(); _data.Blocks = new Dictionary<ulong, List<LogObject>>(); _data.TC = new Dictionary<ulong, List<LogObject>>(); _data.Offsets = new Dictionary<ulong, UiOffsets>(); }
        }
    }

    internal void SaveData()
    {
        lock (_dataLock)
        {
            if (_data == null || !_data.Changed) return;
            _data.Changed = false;
            try { File.WriteAllText(_dataPath, JsonConvert.SerializeObject(_data, Formatting.Indented)); } catch { }
        }
    }

    public void Update(float deltaTime)
    {
        _saveTimer += deltaTime;
        if (_saveTimer >= 300f) { _saveTimer = 0f; SaveData(); }
    }

    public bool CanUse(BasePlayer player)
    {
        if (player == null) return false;
        if (player.IsAdmin) return true;
        if (_config?.AdminOnly == true) return false;
        return _config?.AllowedSteamIds != null && _config.AllowedSteamIds.Contains(player.userID);
    }

    public bool CanUseMlrs(BasePlayer player)
    {
        if (player == null) return false;
        if (player.IsAdmin) return true;
        return _config?.AllowedMlrsSteamIds != null && _config.AllowedMlrsSteamIds.Contains(player.userID);
    }

    public void OnEntityPlaced(BaseEntity entity, BasePlayer player)
    {
        if (entity == null || player == null) return;
        lock (_dataLock)
        {
            var dict = _data.Get(entity);
            if (!dict.TryGetValue(player.userID, out var logs)) dict[player.userID] = logs = new List<LogObject>();
            logs.Add(new LogObject(DateTime.Now, entity.transform.position));
            _data.Changed = true;
        }
    }

    public void OnEntityKilled(BaseEntity entity)
    {
        if (_data == null || entity == null || entity.OwnerID == 0) return;
        lock (_dataLock)
        {
            var dict = _data.Get(entity);
            foreach (var kv in dict.ToList())
            {
                var logs = kv.Value;
                int index = logs.FindIndex(log => (entity.transform.position - log.Coordinates).sqrMagnitude < 0.1f);
                if (index >= 0)
                {
                    _data.Changed = true;
                    logs.RemoveAt(index);
                    if (logs.Count == 0) dict.Remove(kv.Key);
                    break;
                }
            }
        }
    }

    private void RegisterCommands()
    {
        try
        {
            if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey("global.prodigy"))
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = "PRODIGY",
                    FullName = "global.prodigy",
                    Variable = true,
                    ServerAdmin = false,
                    Replicated = true,
                    Call = CmdProdigy
                };
                ConsoleSystem.Index.Server.Dict["global.prodigy"] = cmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null) ConsoleSystem.Index.Server.GlobalDict["prodigy"] = cmd;
            }
            if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey("global.prodigy_ui_move"))
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = "PRODIGY_UI_MOVE",
                    FullName = "global.prodigy_ui_move",
                    Variable = true,
                    ServerAdmin = false,
                    Replicated = true,
                    Call = CmdProdigyUiMove
                };
                ConsoleSystem.Index.Server.Dict["global.prodigy_ui_move"] = cmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null) ConsoleSystem.Index.Server.GlobalDict["prodigy_ui_move"] = cmd;
            }
        }
        catch (Exception ex) { UnityEngine.Debug.LogWarning($"[Prodigy] RegisterCommands: {ex.Message}"); }
    }

    private void UnregisterCommands()
    {
        try
        {
            if (ConsoleSystem.Index.Server.Dict != null) ConsoleSystem.Index.Server.Dict.Remove("global.prodigy");
            if (ConsoleSystem.Index.Server.GlobalDict != null) ConsoleSystem.Index.Server.GlobalDict.Remove("prodigy");
            if (ConsoleSystem.Index.Server.Dict != null) ConsoleSystem.Index.Server.Dict.Remove("global.prodigy_ui_move");
            if (ConsoleSystem.Index.Server.GlobalDict != null) ConsoleSystem.Index.Server.GlobalDict.Remove("prodigy_ui_move");
        }
        catch { }
    }

    private void CmdProdigy(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null) { arg.ReplyWith("Must be a player!"); return; }
        RunProdigyCommand(player, ToStringArray(arg.Args), arg);
    }

    /// <summary>Runs prodigy logic (used by console command and by chat /prod or /prodigy).</summary>
    public void RunProdigyCommand(BasePlayer player, string[] args, ConsoleSystem.Arg arg = null)
    {
        if (player == null) return;
        if (!CanUse(player))
        {
            player.ChatMessage("<color=#ff6600>[Prodigy]</color> You don't have permission. Add your Steam ID to AllowedSteamIds in HarmonyConfig/Prodigy.json, or set AdminOnly to false.");
            return;
        }

        bool hasArg(string v) => args != null && args.Length > 0 && args[0] == v;
        bool hasAnyArg(string v) => args != null && args.Length > 0 && args.Contains(v);
        void reply(string msg) { if (arg != null) arg.ReplyWith(msg); else player.ChatMessage(msg); }

        if (hasArg("reset"))
        {
            lock (_dataLock) _data.Offsets.Remove(player.userID);
            _data.Changed = true;
            player.ChatMessage("Reset UI");
            return;
        }

        if (!TryGetHit(player, out var hit, out var entity))
        {
            if (hit.collider != null)
            {
                var layerName = LayerMask.LayerToName(hit.collider.gameObject.layer);
                var position = hit.collider.transform.position;
                var text = $"{layerName} {position} {hit.collider.name} {hit.collider.bounds.size}";
                player.SendConsoleCommand("ddraw.text", 10f, Color.green, position, $"<size=24>{text}</size>");
                reply("<color=#ff0000>No entity found</color>.");
            }
            else player.ChatMessage("<color=#ff0000>Unable to find anything, try standing closer</color>.");
            return;
        }

        if (player.IsAdmin)
        {
            bool hasNavDebug = entity.TryGetComponent<BaseNavigator>(out _);
            // Layer/position/size text moved to UI top as "click to copy" bar (no world-space ddraw here)
            player.SendConsoleCommand("ddraw.text", 10f, Color.red, entity.ServerPosition, $"<size=24>X</size>");
            if (!hasNavDebug)
            {
                float radius = Mathf.Max(entity.bounds.extents.x, entity.bounds.extents.y, entity.bounds.extents.z) * 0.25f;
                player.SendConsoleCommand("ddraw.sphere", 10f, Color.green, entity.CenterPoint(), radius);
            }
        }

        if (hasAnyArg("components"))
        {
            var components = new Dictionary<Component, string>();
            foreach (var component in entity.GetComponents<Component>())
            {
                components[component] = "-P-";
                foreach (var childcomp in component.GetComponentsInChildren<Component>()) components[childcomp] = "-C-";
            }
            foreach (var kv in components) reply($"{kv.Value} {kv.Key.GetType().Name} | {kv.Key.name} | {kv.Key.gameObject.layer}");
        }

        Prod(hit.collider, entity, player);
    }

    private void CmdProdigyUiMove(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null || !CanUse(player)) return;
        var args = ToStringArray(arg.Args);
        RunProdigyUiMove(player, args.Length > 0 ? args[0] : null, args.Length > 1 ? args[1] : null);
    }

    private static string[] ToStringArray(StringView[] args)
    {
        if (args == null || args.Length == 0) return Array.Empty<string>();

        var result = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
            result[i] = args[i].ToString();
        return result;
    }

    /// <summary>Handles UI move/close (used by console command and by cui.endtest PRODIGY from button clicks).</summary>
    public void RunProdigyUiMove(BasePlayer player, string direction, string encodedArg)
    {
        if (player == null || !CanUse(player)) return;
        if (direction == "close")
        {
            _closeScheduler?.Cancel(player.userID);
            ProdigyUI.Destroy(player);
            return;
        }
        if (direction == "paste")
        {
            if (!string.IsNullOrEmpty(encodedArg))
            {
                var parts = encodedArg.Replace("_", " ").Split('|');
                if (parts.Length >= 13)
                {
                    string lastOnline = parts.Length >= 14 ? parts[13] : "N/A";
                    string msg = $"Entity: {parts[0]}\nOwner: {parts[1]}\nPosition: {parts[2]}\nPrefabId: {parts[3]}\nType: {parts[4]}\nHealth: {parts[5]}\nSize: {parts[6]}\nBuilding ID: {parts[7]}\nCollider: {parts[8]}\nSkin: {parts[9]}\nLast: {parts[10]}\nCode: {parts[11]}\nLast Online: {lastOnline}\nDetails: {parts[12]}";
                    player.ConsoleMessage(msg);
                }
            }
            return;
        }
        UiOffsets offsets;
        lock (_dataLock) offsets = _data.Offsets.TryGetValue(player.userID, out var o) ? o : null;
        if (offsets == null || string.IsNullOrEmpty(encodedArg)) return;

        var offsetMin = offsets.Min.Split(' ');
        var offsetMax = offsets.Max.Split(' ');
        int n = player.serverInput.IsDown(BUTTON.DUCK) ? 1 : player.serverInput.IsDown(BUTTON.SPRINT) ? 50 : 15;
        switch (direction)
        {
            case "left": offsets.Min = $"{Convert.ToSingle(offsetMin[0]) - n} {offsetMin[1]}"; offsets.Max = $"{Convert.ToSingle(offsetMax[0]) - n} {offsetMax[1]}"; break;
            case "right": offsets.Min = $"{Convert.ToSingle(offsetMin[0]) + n} {offsetMin[1]}"; offsets.Max = $"{Convert.ToSingle(offsetMax[0]) + n} {offsetMax[1]}"; break;
            case "up": offsets.Min = $"{offsetMin[0]} {Convert.ToSingle(offsetMin[1]) + n}"; offsets.Max = $"{offsetMax[0]} {Convert.ToSingle(offsetMax[1]) + n}"; break;
            case "down": offsets.Min = $"{offsetMin[0]} {Convert.ToSingle(offsetMin[1]) - n}"; offsets.Max = $"{offsetMax[0]} {Convert.ToSingle(offsetMax[1]) - n}"; break;
            default: return;
        }
        offsets.Changed = true;
        _data.Changed = true;
        var args = encodedArg.Replace("_", " ").Split('|');
        if (args.Length >= 13)
        {
            string lastOnline = args.Length >= 14 ? args[13] : "N/A";
            ProdigyUI.Show(player, args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], lastOnline, offsets.IsSmallUi, offsets.Min, offsets.Max, offsets.IsTimed);
            SetTimer(player);
        }
    }

    private static bool TryGetHit(BasePlayer player, out RaycastHit hit, out BaseEntity entity)
    {
        int layerMask = -5 | (1 << 15);
        if (GamePhysics.Trace(player.eyes.HeadRay(), 0f, out hit, 50f, layerMask, QueryTriggerInteraction.Collide, player))
        {
            entity = hit.GetEntity();
            return entity != null;
        }
        entity = null;
        return false;
    }

    private UiOffsets GetProdigyUi(BasePlayer player)
    {
        lock (_dataLock)
        {
            if (!_data.Offsets.TryGetValue(player.userID, out var ui))
            {
                _data.Offsets[player.userID] = ui = new UiOffsets();
                if (player.serverInput.IsDown(BUTTON.SPRINT)) { ui.IsTimed = false; player.ChatMessage("Timed UI disabled"); }
                if (player.serverInput.IsDown(BUTTON.DUCK)) { ui.IsSmallUi = true; ui.Min = UiOffsets.DefaultSmallMin; ui.Max = UiOffsets.DefaultSmallMax; player.ChatMessage("Small UI enabled"); }
            }
            return ui;
        }
    }

    private void SetTimer(BasePlayer player)
    {
        _closeScheduler?.ScheduleClose(player.userID, ShowUiForSeconds);
    }

    private void Prod(Collider collider, BaseEntity entity, BasePlayer player)
    {
        var userid = entity.OwnerID;
        var sb = new StringBuilder();
        var details = new List<string>();
        var entityShortname = $"{entity.ShortPrefabName} ({entity.gameObject.layer})";
        var targetName = GetPlayerName(entity.OwnerID);
        var actualName = GetPlayerName(entity, ref userid);
        var lastAttackerName = GetLastAttackerName(entity);
        var isHoldingHammer = player.IsHoldingEntity<Hammer>();
        var colliderName = collider.name.Substring(collider.name.LastIndexOf('/') + 1);

        if (targetName != actualName) details.Add($"Actual owner: {actualName} ({entity.OwnerID})");
        if (IsDeployableEntity(entity)) entityShortname += " [D]";

        switch (entity)
        {
            case DebrisEntity debrisEntity: AppendDebrisEntity(sb, debrisEntity); break;
            case LiquidContainer lc when isHoldingHammer: HandleLiquidContainer(lc, sb); break;
            case ScientistNPC scientist when isHoldingHammer: HandleNPC(scientist, sb, player); break;
            case ScarecrowNPC scarecrow when isHoldingHammer: HandleNPC(scarecrow, sb, player); break;
            case FrankensteinPet pet when isHoldingHammer: HandleNPC(pet, sb, player); break;
            case BaseEntity e when e.TryGetComponent<BaseNavigator>(out var nav): HandleNavigator(nav, sb, player); break;
            case BaseEntity _ when isHoldingHammer && player.IsAdmin && GetNPCVendingMachine(entity) is NPCVendingMachine vm: LootNPCVendingMachine(player, vm); break;
            case BuildingBlock block: entityShortname = AppendBuildingBlock(sb, details, entityShortname, block); break;
            case KeyLock keyLock: AppendKeyLock(sb, isHoldingHammer, keyLock); break;
            case BaseAnimalNPC animalNPC: AppendAnimalTarget(details, animalNPC); break;
            case BasePlayer target when SteamIdHelper.IsSteamId(target.userID): AppendTargetName(details, target); break;
            case DroppedItem _ when entity.GetComponentInChildren<Cassette>() is Cassette cassette: sb.AppendLine($"Cassette: {cassette.CreatorSteamId}"); break;
            case NPCPlayerCorpse npcCorpse when player.inventory.loot.StartLootingEntity(npcCorpse): LootNPCPlayerCorpse(player, npcCorpse); break;
            case PlayerCorpse corpse: details.Add($"Corpse: {corpse.playerName} ({corpse.playerSteamID})"); break;
            case DroppedItemContainer container: details.Add($"Backpack: {container.playerName} ({container.playerSteamID}) [Despawn: {container.CalculateRemovalTime()}s]"); break;
            case BuildingPrivlidge priv: AppendBuildingPrivilege(sb, details, priv); break;
            case SleepingBag bag: AppendSleepingBag(details, isHoldingHammer, bag); break;
            case RepairBench bench: sb.AppendLine($"Max condition lost on repair: {bench.maxConditionLostOnRepair:#0.##%}"); break;
            case AutoTurret turret: AppendAutoTurret(details, isHoldingHammer, turret); AppendIOEntity(details, turret); break;
            case SamSite ss: ss.SetFlag(BaseEntity.Flags.Reserved8, !ss.IsPowered(), false, true); AppendIOEntity(details, ss); break;
            case IOEntity ioEntity: AppendIOEntity(details, ioEntity); break;
            case StashContainer stash: GetStashContents(player, stash); break;
            case GunTrap gunTrap: AppendGunTrap(details, gunTrap); break;
            case LockedByEntCrate crate: AppendLockedCrate(details, isHoldingHammer, crate); break;
            case MLRS mlrs when CanUseMlrs(player): AppendMLRS(details, mlrs); break;
        }

        if (entity is LegacyShelter ls)
        {
            ls.GetEntityPrivilege().SetFlag(BaseEntity.Flags.Reserved5, false);
            ls.GetEntityPrivilege().AddPlayer(player);
        }

        string code = HandleEntityCode(entity, details);
        string info = details.Count > 0 ? $"NetID: {entity.net.ID.Value} : {string.Join(", ", details)}" : $"NetID: {entity.net.ID.Value} : Actual owner: {actualName} ({entity.OwnerID})";
        if (info.Length > 30) player.ChatMessage(info);
        if (sb.Length > 0) player.ChatMessage(sb.ToString());

        UiOffsets ui = GetProdigyUi(player);
        float size = Mathf.Max(Mathf.Max(entity.bounds.size.x, entity.bounds.size.y), entity.bounds.size.z);
        size = Mathf.Max(size, Mathf.Max(entity.transform.localScale.x, Mathf.Max(entity.transform.localScale.y, entity.transform.localScale.z)));
        string buildingID = entity is DecayEntity de ? de.buildingID.ToString() : (entity.GetBuildingPrivilege()?.buildingID ?? 0).ToString();
        string lastOnline = GetLastOnline(userid);
        ProdigyUI.Show(player, entityShortname, actualName, entity.transform.position.ToString(), entity.prefabID.ToString(), entity.GetType().Name, entity.Health().ToString(), size.ToString(), buildingID, colliderName, entity.skinID.ToString(), lastAttackerName, code, info, lastOnline, ui.IsSmallUi, ui.Min, ui.Max, ui.IsTimed);
        SetTimer(player);
        lock (_dataLock) _data.Changed = true;
    }

    private string HandleEntityCode(BaseEntity entity, List<string> details)
    {
        string code = GetCode(entity);
        if (code.Length > 4)
        {
            var split = code.Split('ƾ');
            details.Add(split.Length > 1 ? split[1] : string.Empty);
            code = split[0];
        }
        return code;
    }

    private static void AppendMLRS(List<string> details, MLRS mlrs)
    {
        if (mlrs.IsDead() || mlrs.IsFiringRockets) details.Add("MLRS cannot be repaired while it is firing rockets.");
        else { mlrs.AdminFixUp(); details.Add("MLRS has been repaired."); }
    }

    private static void AppendLockedCrate(List<string> details, bool isHoldingHammer, LockedByEntCrate crate)
    {
        foreach (var item in crate.inventory.itemList) details.Add($"{item.info.shortname} {item.amount}");
        if (isHoldingHammer) crate.lockingEnt?.Kill();
    }

    private static void AppendGunTrap(List<string> details, GunTrap gunTrap) { foreach (var item in gunTrap.inventory.itemList) details.Add($"{item.info.shortname} {item.amount}"); }

    private static void AppendIOEntity(List<string> details, IOEntity ioEntity)
    {
        details.Add(ioEntity is NPCAutoTurret ? $"Powered: {ioEntity.HasFlag(BaseEntity.Flags.On)}" : $"Powered: {ioEntity.IsPowered()}");
    }

    private static void AppendAutoTurret(List<string> details, bool isHoldingHammer, AutoTurret turret)
    {
        string turretAuth = string.Join(", ", turret.authorizedPlayers.Select(p => p.ToString()));
        details.Add($"Auth: {(string.IsNullOrEmpty(turretAuth) ? "NONE" : turretAuth)}, AimCone: {turret.aimCone}, SightRange: {turret.sightRange}, Target: {turret.target}");
        if (turret.target is BasePlayer target) details.Add("Target: " + (target.displayName ?? target.userID.ToString()));
        if (isHoldingHammer) turret.SetIsOnline(turret.IsOffline());
        var attachedWeapon = turret.GetAttachedWeapon();
        if (attachedWeapon != null)
        {
            details.Add($"Ammo: {attachedWeapon.primaryMagazine.contents}");
            foreach (var item in turret.inventory.itemList) details.Add($"{item.info.shortname} {item.amount}");
        }
    }

    private static void HandleLiquidContainer(LiquidContainer lc, StringBuilder sb)
    {
        sb.AppendFormat("{0} ({1} maxStackSize)", lc.ShortPrefabName, lc.maxStackSize);
        if (lc is WaterCatcher wc) sb.AppendLine().AppendFormat("baseRate: {0}, fogRate: {1}, rainRate: {2}, snowRate: {3}", wc.collectionRates.baseRate, wc.collectionRates.fogRate, wc.collectionRates.rainRate, wc.collectionRates.snowRate);
    }

    private const float MaxNavArrowDistance = 75f;
    private const float NavSphereRadius = 0.25f;
    private const float NavArrowHeadSize = 0.5f;
    private const float ActualMovementLineLength = 15f;

    private static void HandleNPC<T>(T npc, StringBuilder sb, BasePlayer player) where T : NPCPlayer
    {
        var nav = npc.GetComponent<BaseNavigator>();
        if (nav == null) return;
        sb.AppendFormat("eyes: {0}, ", player.eyes == null);
        sb.AppendFormat("agentTypeId: {0}, areaMask: {1}, Is paused: {2}, ", nav.Agent.agentTypeID, nav.Agent.areaMask, nav.CurrentNavigationType == BaseNavigator.NavigationType.None);
        sb.AppendFormat("Navmesh enabled: {0}, stopped: {1}, stuck: {2}, type: {3}, ", nav.Agent.enabled, nav.Agent.isOnNavMesh && nav.Agent.isStopped, nav.StuckOffNavmesh, nav.CurrentNavigationType);
        if (npc.TryGetComponent<BaseAIBrain>(out var brain)) sb.AppendFormat("SenseRange: {0}, ListenRange: {1}, TargetLostRange: {2}, StoppingDistance: {3}", brain.SenseRange, brain.ListenRange, brain.TargetLostRange, nav.StoppingDistance);
        sb.AppendLine();
        DrawNavDebug(player, npc.eyes.position, nav.Agent.destinationWS, nav.Destination, npc);
    }

    private static void HandleNavigator(BaseNavigator nav, StringBuilder sb, BasePlayer player)
    {
        sb.AppendFormat("agentTypeId: {0}, areaMask: {1}, Is paused: {2}, ", nav.Agent.agentTypeID, nav.Agent.areaMask, nav.CurrentNavigationType == BaseNavigator.NavigationType.None);
        sb.AppendFormat("Navmesh enabled: {0}, stopped: {1}, stuck: {2}, type: {3}, ", nav.Agent.enabled, nav.Agent.isOnNavMesh && nav.Agent.isStopped, nav.StuckOffNavmesh, nav.CurrentNavigationType);
        sb.AppendLine();
        var entity = nav.GetComponent<BaseEntity>();
        DrawNavDebug(player, nav.transform.position, nav.Agent.destinationWS, nav.Destination, entity);
    }

    /// <summary>Draws sphere on NPC, line to nav destination (max 75m), arrow at end; cyan = actual movement direction.</summary>
    private static void DrawNavDebug(BasePlayer player, Vector3 from, Vector3 agentDest, Vector3 navDest, BaseEntity entity = null)
    {
        player.SendConsoleCommand("ddraw.sphere", 15f, Color.green, from, NavSphereRadius);
        DrawNavLineAndArrow(player, from, agentDest, Color.red);
        DrawNavLineAndArrow(player, from, navDest, Color.yellow);
        if (entity != null)
        {
            Vector3 vel = entity.GetWorldVelocity();
            if (vel.sqrMagnitude > 0.01f)
            {
                Vector3 to = from + vel.normalized * ActualMovementLineLength;
                player.SendConsoleCommand("ddraw.line", 15f, Color.cyan, from, to);
                Vector3 arrowStart = to - vel.normalized * 0.5f;
                player.SendConsoleCommand("ddraw.arrow", 15f, Color.cyan, arrowStart, to, NavArrowHeadSize);
            }
        }
    }

    private static void DrawNavLineAndArrow(BasePlayer player, Vector3 from, Vector3 dest, Color color)
    {
        float sqMax = MaxNavArrowDistance * MaxNavArrowDistance;
        if ((dest - from).sqrMagnitude > sqMax) return;
        player.SendConsoleCommand("ddraw.line", 15f, color, from, dest);
        Vector3 dir = (dest - from).normalized;
        Vector3 arrowStart = dest - dir * 0.5f;
        player.SendConsoleCommand("ddraw.arrow", 15f, color, arrowStart, dest, NavArrowHeadSize);
    }

    private static void LootNPCVendingMachine(BasePlayer player, NPCVendingMachine vm)
    {
        player.inventory.loot.Clear();
        player.inventory.loot.entitySource = RelationshipManager.ServerInstance;
        player.inventory.loot.itemSource = null;
        player.inventory.loot.AddContainer(vm.inventory);
        player.inventory.loot.SendImmediate();
        player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), vm.lootPanelName);
    }

    private static void AppendDebrisEntity(StringBuilder sb, DebrisEntity debrisEntity) => sb.AppendLine($"{debrisEntity.ShortPrefabName}: {debrisEntity.GetRemovalTime()}");

    private string AppendBuildingBlock(StringBuilder sb, List<string> details, string entityShortname, BuildingBlock block)
    {
        lock (_dataLock)
        {
            if (_data.Blocks.TryGetValue(block.OwnerID, out var logs))
                foreach (var log in logs)
                    if ((block.transform.position - log.Coordinates).sqrMagnitude < 0.1f)
                    {
                        sb.AppendLine($"<color=#0093FF>OwnerID</color>: <color=#71F808>{block.OwnerID}</color>");
                        sb.AppendLine($"<color=#0093FF>Date Built</color>: <color=#FFF000>{log.Date}</color>");
                        sb.AppendLine($"<color=#0093FF>Coordinates</color>: <color=#FFFFFF>{log.Coordinates}</color>");
                    }
        }
        if (block.HasWallpaper(0)) details.Add("Wallpaper0: " + block.wallpaperHealth);
        if (block.HasWallpaper(1)) details.Add("Wallpaper1: " + block.wallpaperHealth2);
        return entityShortname + " [B]";
    }

    private static void AppendKeyLock(StringBuilder sb, bool isHoldingHammer, KeyLock keyLock)
    {
        if (isHoldingHammer) { keyLock.SetFlag(BaseEntity.Flags.Locked, !keyLock.IsLocked(), false, true); keyLock.SendNetworkUpdate(BasePlayer.NetworkQueue.Update); }
        sb.AppendLine($"Keylock {(keyLock.IsLocked() ? "Locked" : "Unlocked")}");
    }

    private static void AppendAnimalTarget(List<string> details, BaseAnimalNPC animalNPC)
    {
        string target = animalNPC.AttackTarget is BasePlayer pt ? (pt.displayName ?? pt.userID.ToString()) : "N/A";
        details.Add($"Target: {(string.IsNullOrEmpty(target) ? "NONE" : target)}");
    }

    private static void AppendTargetName(List<string> details, BasePlayer basePlayer)
    {
        details.Add($"Team ID: {(basePlayer.currentTeam == 0 ? "N/A" : basePlayer.currentTeam.ToString())}");
        details.Add("Clan Tag: N/A");
    }

    private static void LootNPCPlayerCorpse(BasePlayer player, NPCPlayerCorpse npcCorpse)
    {
        npcCorpse.SetFlag(BaseEntity.Flags.Open, true);
        foreach (var container in npcCorpse.containers) player.inventory.loot.AddContainer(container);
        player.inventory.loot.SendImmediate();
        npcCorpse.ClientRPC(RpcTarget.Player("RPC_ClientLootCorpse", player));
        npcCorpse.SendNetworkUpdate();
    }

    private void AppendBuildingPrivilege(StringBuilder sb, List<string> details, BuildingPrivlidge priv)
    {
        string authedPlayers = string.Join(", ", priv.authorizedPlayers.Select(p => GetPlayerName(p)));
        details.Add($"Auth: {(string.IsNullOrEmpty(authedPlayers) ? "NONE" : authedPlayers)}");
        lock (_dataLock)
        {
            if (_data.TC.TryGetValue(priv.OwnerID, out var logs))
                foreach (var log in logs)
                    if ((priv.transform.position - log.Coordinates).sqrMagnitude < 0.1f)
                    {
                        sb.AppendLine($"<color=#0093FF>OwnerID</color>: <color=#71F808>{priv.OwnerID}</color>");
                        sb.AppendLine($"<color=#0093FF>Date Built</color>: <color=#FFF000>{log.Date}</color>");
                        sb.AppendLine($"<color=#0093FF>Coordinates</color>: <color=#FFFFFF>{log.Coordinates}</color>");
                    }
        }
    }

    private static void AppendSleepingBag(List<string> details, bool isHoldingHammer, SleepingBag bag)
    {
        if (isHoldingHammer) { bag.SetUnlockTime(UnityEngine.Time.realtimeSinceStartup); bag.SendNetworkUpdate(); }
        details.Add($"Sleeping Bag: {GetPlayerName(bag.deployerUserID)} ({bag.deployerUserID}) [{bag.niceName}]");
    }

    private static NPCVendingMachine GetNPCVendingMachine(BaseEntity entity) => entity as NPCVendingMachine ?? (entity as NPCShopKeeper)?.GetVendingMachine();

    private static string GetLastAttackerName(BaseEntity entity)
    {
        if (entity is not BaseCombatEntity bce || bce.lastAttacker == null) return "N/A";
        return bce.lastAttacker is BasePlayer lp ? lp.displayName : bce.lastAttacker.ShortPrefabName;
    }

    private string GetPlayerName(BaseEntity entity, ref ulong userid)
    {
        if (entity is BaseEntity e && SteamIdHelper.IsSteamId(e.OwnerID)) { userid = e.OwnerID; return GetPlayerName(userid); }
        if (entity is DroppedItem di && SteamIdHelper.IsSteamId(di.DroppedBy)) { userid = di.DroppedBy; return GetPlayerName(userid); }
        if (entity is VehicleModuleCamper vmc && SteamIdHelper.IsSteamId(vmc.OwnerID)) { userid = vmc.OwnerID; return GetPlayerName(userid); }
        if (entity is VehicleModuleCamper vmc2 && vmc2.GetContainer() is BaseEntity c && SteamIdHelper.IsSteamId(c.OwnerID)) { userid = c.OwnerID; return GetPlayerName(userid); }
        if (entity is SprayCanSpray scs && SteamIdHelper.IsSteamId(scs.sprayedByPlayer)) { userid = scs.sprayedByPlayer; return GetPlayerName(userid); }
        if (entity is PlayerCorpse corpse && SteamIdHelper.IsSteamId(corpse.playerSteamID)) { userid = corpse.playerSteamID; return GetPlayerName(userid); }
        if (entity is BasePlayer p && SteamIdHelper.IsSteamId(p.userID)) { userid = p.userID; return GetPlayerName(userid); }
        if (entity.parentEntity.Get(true) is BaseEntity parent && SteamIdHelper.IsSteamId(parent.OwnerID)) { userid = parent.OwnerID; return GetPlayerName(userid); }
        if (entity.children != null)
            foreach (var child in entity.children)
            {
                if (child is SleepingBagCamper sbc && SteamIdHelper.IsSteamId(sbc.deployerUserID)) { userid = sbc.deployerUserID; return GetPlayerName(userid); }
                if (child is VehiclePrivilege vp && SteamIdHelper.IsSteamId(vp.OwnerID)) { userid = vp.OwnerID; return GetPlayerName(userid); }
                if (child is BaseEntity be && SteamIdHelper.IsSteamId(be.OwnerID)) { userid = be.OwnerID; return GetPlayerName(userid); }
            }
        return "Server";
    }

    private static string GetPlayerName(ulong playerId)
    {
        if (playerId == 0) return "Server";
        var name = Admin.GetPlayerName(playerId);
        if (name != null && name != "[unknown]") return name;
        var p = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
        return p?.displayName ?? playerId.ToString();
    }

    private static string GetLastOnline(ulong playerId)
    {
        if (!SteamIdHelper.IsSteamId(playerId)) return "N/A";

        var player = BasePlayer.FindAwakeOrSleepingByID(playerId);
        if (player != null && player.IsConnected) return "Online";

        var getLastSeen = GetPlaytimeTrackerLastSeen();
        if (getLastSeen != null)
        {
            try
            {
                object result = getLastSeen.Invoke(null, new object[] { playerId.ToString() });
                if (TryToUnixSeconds(result, out double epoch) && epoch > 0)
                    return FormatLastOnline(epoch);
            }
            catch
            {
                _playtimeTrackerGetLastSeen = null;
            }
        }

        return "N/A";
    }

    private static MethodInfo GetPlaytimeTrackerLastSeen()
    {
        if (_playtimeTrackerGetLastSeen != null) return _playtimeTrackerGetLastSeen;
        try
        {
            var apiType = AppDomain.CurrentDomain.GetData("PlaytimeTracker_ApiType") as Type;
            if (apiType == null) return null;
            _playtimeTrackerGetLastSeen = apiType.GetMethod("GetLastSeen", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        }
        catch { }
        return _playtimeTrackerGetLastSeen;
    }

    private static bool TryToUnixSeconds(object value, out double epoch)
    {
        epoch = 0;
        if (value == null) return false;
        if (value is double d) { epoch = d; return true; }
        if (value is float f) { epoch = f; return true; }
        if (value is long l) { epoch = l; return true; }
        if (value is int i) { epoch = i; return true; }
        return false;
    }

    private static string FormatLastOnline(double unixSeconds)
    {
        var utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool IsDeployableEntity(BaseEntity entity)
    {
        if (_deployables.Count == 0)
            foreach (var def in ItemManager.GetItemDefinitions())
                if (def.TryGetComponent<ItemModDeployable>(out var imd)) _deployables[imd.entityPrefab.resourcePath] = def.itemid;
        return _deployables.ContainsKey(entity.PrefabName);
    }

    private static void GetStashContents(BasePlayer player, StorageContainer stash)
    {
        if (stash.inventory.itemList.Count > 0) foreach (var item in stash.inventory.itemList) player.ChatMessage($"{item.info.shortname} ({item.amount})");
        else player.ChatMessage("No stash contents");
    }

    private static string GetCode(BaseEntity entity)
    {
        var sb = new StringBuilder();
        ModularCar car = entity as ModularCar ?? entity.parentEntity.Get(true) as ModularCar;
        if (car != null) sb.Append(car.IsLockable ? car.CarLock.Code : "NONE");
        if (!entity.HasSlot(BaseEntity.Slot.Lock)) return sb.ToString();
        if (entity.GetSlot(BaseEntity.Slot.Lock) is not CodeLock codeLock) return sb.ToString();
        sb.Append(codeLock.code);
        if (codeLock.whitelistPlayers.Count == 0) return sb.ToString();
        sb.Append('ƾ');
        var playerStatus = new Dictionary<string, List<string>> { { "Online", new List<string>() }, { "Offline", new List<string>() }, { "Dead", new List<string>() } };
        foreach (ulong uid in codeLock.whitelistPlayers)
        {
            var pe = BasePlayer.FindAwakeOrSleeping(uid.ToString());
            if (pe != null && pe.IsConnected) playerStatus["Online"].Add(pe.displayName);
            else if (pe != null) playerStatus["Offline"].Add(pe.displayName);
            else playerStatus["Dead"].Add(GetPlayerName(uid));
        }
        foreach (var kv in playerStatus) if (kv.Value.Count > 0) sb.AppendFormat("({0}: {1})", kv.Key, string.Join(", ", kv.Value));
        return sb.ToString();
    }
}
