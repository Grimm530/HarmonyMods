using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Facepunch;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Rust;

namespace TCUpgrade;

/// <summary>
/// Full Harmony mod for TCUpgrade - upgrade, repair, reskin, wallpaper.
/// Loaded by HarmonyLoader from HarmonyMods/. No Oxide plugin required.
/// Config: HarmonyConfig/TCUpgrade.json or oxide/config/TCUpgrade.json
/// </summary>
public class TCUpgradeMod : IHarmonyModHooks
{
    public static TCUpgradeMod Instance { get; private set; }

    public bool ForceBothSides => TCUpgradeConfig.Config?.ForceBothSides ?? true;

    private const ulong HammerWallpaperSkin = 3494416562;
    private const int ClothItemId = -858312878;

    private readonly Dictionary<BuildingPrivlidge, TCConfig> _buildingCupboard = new();
    private readonly Dictionary<ulong, BuildingPrivlidge> _playerLootingTc = new();
    private static readonly Dictionary<string, string> _localImageIds = new();
    private readonly Dictionary<ulong, TCSkin> _playerSelectedSkins = new();
    private ConsoleSystem.Command _sendCmdCommand;
    private ConsoleSystem.Command _wphammerCommand;
    private ConsoleSystem.Command _addwpCommand;
    private TCUpgradeData _data;
    private int _maxGradeTier = 4;

    private static Type _cachedOxideModType;
    private static object _cachedOxideModInstance;

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        TCUpgradeConfig.LoadConfig();
        _data = TCUpgradeData.Load();

        try
        {
            _sendCmdCommand = new ConsoleSystem.Command
            {
                Name = "SENDCMD",
                FullName = "global.SENDCMD",
                Variable = true,
                ServerAdmin = false,
                Replicated = true,
                Call = HandleSendCmd
            };
            ConsoleSystem.Index.Server.Dict["global.SENDCMD"] = _sendCmdCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["SENDCMD"] = _sendCmdCommand;

            _wphammerCommand = new ConsoleSystem.Command { Name = "wphammer", FullName = "global.wphammer", Variable = true, ServerAdmin = false, Call = CmdWphammer };
            _addwpCommand = new ConsoleSystem.Command { Name = "addwp", FullName = "global.addwp", Variable = true, ServerAdmin = false, Call = CmdAddwp };
            ConsoleSystem.Index.Server.Dict["global.wphammer"] = _wphammerCommand;
            ConsoleSystem.Index.Server.Dict["global.addwp"] = _addwpCommand;
        }
        catch (Exception ex)
        {
            Log($"Command registration failed (some features may not work): {ex.Message}", force: true);
        }

        try
        {
            if (BaseNetworkable.serverEntities != null)
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    if (entity is BuildingPrivlidge tc && tc != null && tc.skinID == 0)
                        UpdateBlockedItems(tc);
                }
        }
        catch { }

        Log("Mod loaded.", force: true);
        Log($"Config path: HarmonyConfig/TCUpgrade.json (relative to server root). Debug={TCUpgradeConfig.Config?.Debug ?? false}", force: true);
        if (TCUpgradeConfig.Config?.Debug ?? false)
            Log($"Config: ItemsList count={TCUpgradeConfig.Config?.ItemsList?.Count ?? 0}");

        NextTick(LoadLocalImagesToFileStorage);
    }

    private static void LoadLocalImagesToFileStorage()
    {
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed)
        {
            if (BaseNetworkable.serverEntities != null)
                foreach (var e in BaseNetworkable.serverEntities)
                    if (e is CommunityEntity c && c != null && !c.IsDestroyed) { ce = c; break; }
        }
        if (ce == null || ce.IsDestroyed) { Log("LoadLocalImages: CommunityEntity not found"); return; }

        var imagesDir = !string.IsNullOrWhiteSpace(TCUpgradeConfig.Config?.ImagesPathOverride)
            ? TCUpgradeConfig.Config.ImagesPathOverride.Trim()
            : Path.Combine(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")), "HarmonyImages", "TCUpgrade");
        if (!Directory.Exists(imagesDir))
        {
            if (TCUpgradeConfig.Config?.Debug ?? false)
                Log($"LoadLocalImages: directory not found: {imagesDir}");
            return;
        }

        var toLoad = new Dictionary<string, string> {
            {"lock5", "lock5.png"}, {"upgrade2", "upgrade.png"}, {"nowp", "no.png"},
            {"wood", "wood.png"}, {"stone", "stone.png"}, {"metal", "metal.png"}, {"armored", "armored.png"},
            {"legacywood", "legacywood.png"}, {"gingerbread", "gingerbread.png"}, {"adobe", "adobe.png"},
            {"brick", "brick.png"}, {"brutalist", "brutalist.png"}, {"container", "container.png"},
            {"jungle", "jungle.png"}, {"spacetest", "spacetest.png"}
        };
        for (int i = 0; i <= 16; i++) toLoad["color_" + i] = Path.Combine("colours", i + ".png");

        lock (_localImageIds)
        {
            _localImageIds.Clear();
            foreach (var kv in toLoad)
            {
                var path = Path.Combine(imagesDir, kv.Value);
                if (!File.Exists(path)) continue;
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var crc = FileStorage.server.Store(bytes, FileStorage.Type.png, ce.net.ID);
                    var crcStr = crc.ToString();
                    _localImageIds[kv.Key] = crcStr;
                    var filename = Path.GetFileName(path);
                    if (TCUpgradeConfig.Config?.ItemsList != null)
                        foreach (var r in TCUpgradeConfig.Config.ItemsList)
                            if (!string.IsNullOrEmpty(r.Img) && r.Img.EndsWith(filename, StringComparison.OrdinalIgnoreCase))
                                _localImageIds[r.Img] = crcStr;
                }
                catch { }
            }
        }
        Log($"Loaded {_localImageIds.Count} local images from HarmonyImages/TCUpgrade/", force: false);
    }

    private static string GetLocalImage(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        lock (_localImageIds) return _localImageIds.TryGetValue(name, out var id) ? id : null;
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        var players = BasePlayer.activePlayerList;
        if (players == null) return;
        foreach (var p in players)
        {
            CUIHelper.DestroyUi(p, "TCUpgrade.buttons");
            CUIHelper.DestroyUi(p, "TCUpgrade.upgrade");
        }
        foreach (var kv in _buildingCupboard)
        {
            if (kv.Value.WorkUpgrade != null) ServerMgr.Instance?.StopCoroutine(kv.Value.WorkUpgrade);
            if (kv.Value.WorkRepair != null) ServerMgr.Instance?.StopCoroutine(kv.Value.WorkRepair);
            if (kv.Value.WorkReskin != null) ServerMgr.Instance?.StopCoroutine(kv.Value.WorkReskin);
            if (kv.Value.WorkWallpaper != null) ServerMgr.Instance?.StopCoroutine(kv.Value.WorkWallpaper);
            if (kv.Value.WorkUpwall != null) ServerMgr.Instance?.StopCoroutine(kv.Value.WorkUpwall);
        }
        _buildingCupboard.Clear();
        _playerLootingTc.Clear();

        try
        {
            if (_sendCmdCommand != null)
            {
                ConsoleSystem.Index.Server.Dict.Remove("global.SENDCMD");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("SENDCMD");
            }
            if (_wphammerCommand != null) ConsoleSystem.Index.Server.Dict.Remove("global.wphammer");
            if (_addwpCommand != null) ConsoleSystem.Index.Server.Dict.Remove("global.addwp");
        }
        catch { }
        Instance = null;
        Log("Mod unloaded.", force: true);
    }

    private static void Log(string msg, bool force = false)
    {
        var debug = TCUpgradeConfig.Config?.Debug ?? false;
        if (force || debug)
            UnityEngine.Debug.Log($"[TCUpgrade] {msg}");
    }

    /// <summary>Grant all TCUpgrade perms by default. Admin Steam IDs bypass raid blocks etc. No Oxide permission system.</summary>
    public bool HasPermission(string userId, string perm)
    {
        if (string.IsNullOrEmpty(perm)) return true;
        if (ulong.TryParse(userId, out var id) && (TCUpgradeConfig.Config?.AdminSteamIds?.Contains(id) ?? false))
            return true;
        return true;
    }

    public TCConfig GetOrCreateConfig(BuildingPrivlidge cup)
    {
        if (!_buildingCupboard.TryGetValue(cup, out var cfg))
        {
            cfg = new TCConfig();
            _buildingCupboard[cup] = cfg;
        }
        return cfg;
    }

    public void OnLootStarted(BasePlayer player, BuildingPrivlidge cup)
    {
        if (player == null || cup == null) return;
        GetOrCreateConfig(cup);
        _playerLootingTc[player.userID] = cup;
        Log($"OnLootStarted: player={player?.displayName} cup={cup?.net?.ID}");
        // Defer UI by 2 frames so TC loot panel is fully created first; avoids buttons being blocked by loot UI
        NextTick(() => NextTick(() => ShowButtonTC(player, cup)));
    }

    public void OnLootEnded(BasePlayer player)
    {
        if (player == null) return;
        _playerLootingTc.Remove(player.userID);
        CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
        CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
        CUIHelper.DestroyUi(player, "TCUpgrade.color");
        CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
        CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
    }

    /// <summary>Call from coroutine completion (upgrade/repair/reskin/wallpaper). Only shows buttons if player is still looting this TC; otherwise ensures UI is destroyed (avoids stuck cursor).</summary>
    private void ShowButtonTCIfStillLooting(BasePlayer player, BuildingPrivlidge cup)
    {
        if (player == null) return;
        if (_playerLootingTc.TryGetValue(player.userID, out var cached) && cached == cup)
            ShowButtonTC(player, cup);
        else
        {
            CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
            CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
            CUIHelper.DestroyUi(player, "TCUpgrade.color");
            CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
            CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
        }
    }

    private void ShowButtonTC(BasePlayer player, BuildingPrivlidge cup)
    {
        CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
        var cfg = GetOrCreateConfig(cup);

        var elements = new List<JObject>();
        var hasItems = false;
        if (TCUpgradeConfig.Config?.ItemsList != null)
            for (int idx = 0; idx < TCUpgradeConfig.Config.ItemsList.Count; idx++)
                if (TCUpgradeConfig.Config.ItemsList[idx].Enabled) { hasItems = true; break; }
        var btnColor = cfg.Work || cfg.Repair ? "0.90 0.20 0.20 0.50" : (hasItems ? "0.3 0.40 0.3 0.60" : "0.3 0.40 0.3 0.60");
        var offMin = TCUpgradeConfig.Config?.OffsetMin ?? "280 621";
        var offMax = TCUpgradeConfig.Config?.OffsetMax ?? "573 643";
        var ancMin = TCUpgradeConfig.Config?.AnchorMin ?? "0.5 0";
        var ancMax = TCUpgradeConfig.Config?.AnchorMax ?? "0.5 0";
        var btnDefault = TCUpgradeConfig.Config?.BtnTcColor ?? "0.3 0.40 0.3 0.60";
        var btnActive = TCUpgradeConfig.Config?.BtnTcColorActive ?? "0.90 0.20 0.20 0.50";
        btnColor = cfg.Work || cfg.Repair || cfg.Reskin || cfg.Upwall ? btnActive : btnDefault;
        // Use Container (no Image/raycast) so only buttons receive clicks; ButtonsParent config lets user try Hud/Overlay/OverlayNonScaled
        var parent = TCUpgradeConfig.Config?.ButtonsParent?.Trim();
        if (string.IsNullOrEmpty(parent)) parent = "Hud";
        elements.Add(CUIHelper.Container("TCUpgrade.buttons", parent, ancMin, ancMax, offMin, offMax, true));
        // Third button = AUTH (shows who is authed on TC). Wallpaper is in the Upgrade menu, not on the bar.
        elements.AddRange(CUIHelper.Button("btn_upgrade", "TCUpgrade.buttons", btnColor, LangHelper.Lang("UPGRADE"), 12, "0 0", "0.333 1", "cui.endtest SENDCMD MENU"));
        elements.AddRange(CUIHelper.Button("btn_repair", "TCUpgrade.buttons", HasPermission(player.UserIDString, "TCUpgrade.repair") ? btnDefault : "0.2 0.2 0.2 0.5", LangHelper.Lang("REPAIR"), 12, "0.333 0", "0.666 1", "cui.endtest SENDCMD REPAIR"));
        elements.AddRange(CUIHelper.Button("btn_auth", "TCUpgrade.buttons", btnDefault, LangHelper.Lang("AUTH"), 11, "0.666 0", "1 1", "cui.endtest SENDCMD AUTH 0"));

        Log($"ShowButtonTC: sending {elements.Count} elements to {player?.displayName}");
        CUIHelper.AddUi(player, elements);
    }

    private void ShowMenu(BasePlayer player, BuildingPrivlidge cup, int page = 0)
    {
        CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
        var items = new List<TCUpgradeConfig.ItemInfo>();
        if (TCUpgradeConfig.Config?.ItemsList != null)
            foreach (var x in TCUpgradeConfig.Config.ItemsList)
                if (x.Enabled) items.Add(x);
        if (items.Count == 0) return;

        var cfg = GetOrCreateConfig(cup);
        var elements = new List<JObject>();
        int perPage = 12;

        // BetterTC layout: overlay 2000x1600, title -450..450 x 230..260, content -450..450 x -190..230
        elements.Add(CUIHelper.Panel("TCUpgrade.upgrade", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-1000 -800", "1000 800", true));

        elements.Add(CUIHelper.Panel("TCUpgrade.title", "TCUpgrade.upgrade", "0.1 0.15 0.1 0.9", "0.5 0.5", "0.5 0.5", "-450 230", "450 260", false));
        elements.Add(CUIHelper.Label("title_lbl", "TCUpgrade.title", LangHelper.Lang("title1"), 16, "0.022 0.05", "0.8 0.95", "1 1 1 0.9", "MiddleLeft"));
        elements.AddRange(CUIHelper.Button("close", "TCUpgrade.title", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 13, "0.89 0", "0.999 0.982", "cui.endtest SENDCMD CLOSE"));

        elements.Add(CUIHelper.Panel("TCUpgrade.content", "TCUpgrade.upgrade", "0.2 0.23 0.2 0.4", "0.5 0.5", "0.5 0.5", "-450 -190", "450 230", false));

        // Bottom bar - BetterTC positions (normalized in content)
        elements.AddRange(CUIHelper.Button("effect", "TCUpgrade.content", cfg.Effect ? "0.2 0.6 0.2 0.6" : "0.4 0.4 0.4 0.6", cfg.Effect ? LangHelper.Lang("EffectON") : LangHelper.Lang("EffectOFF"), 10, "0.02 0.02", "0.10 0.08", $"cui.endtest SENDCMD EFFECT {page}"));
        elements.AddRange(CUIHelper.Button("downgrade", "TCUpgrade.content", cfg.Downgrade ? "0.2 0.6 0.2 0.6" : "0.4 0.4 0.4 0.6", cfg.Downgrade ? LangHelper.Lang("DowngradeON") : LangHelper.Lang("DowngradeOFF"), 10, "0.12 0.02", "0.20 0.08", $"cui.endtest SENDCMD DOWNGRADE {page}"));
        if (page > 0)
            elements.AddRange(CUIHelper.Button("prev", "TCUpgrade.content", "0.3 0.3 0.8 0.9", "< Back", 10, "0.22 0.02", "0.32 0.08", $"cui.endtest SENDCMD PAGE {page - 1}"));
        // Wallpaper is on each card, not in bottom bar. List Auth removed from upgrade screen (use TC button bar).
        if (HasPermission(player.UserIDString, "TCUpgrade.tcskinchange"))
            elements.AddRange(CUIHelper.Button("tcskin", "TCUpgrade.content", "0.8 1 0.5 0.1", LangHelper.Lang("TCSkin"), 14, "0.42 0.02", "0.58 0.08", $"cui.endtest SENDCMD TCSKIN {page}"));
        if (items.Count > (page + 1) * perPage)
            elements.AddRange(CUIHelper.Button("next", "TCUpgrade.content", "0.3 0.3 0.8 0.9", "Next >", 10, "0.76 0.02", "0.86 0.08", $"cui.endtest SENDCMD PAGE {page + 1}"));
        elements.AddRange(CUIHelper.Button("stop", "TCUpgrade.content", cfg.Work ? "0.9 0.2 0.2 0.5" : "0.3 0.3 0.3 0.6", LangHelper.Lang("STOP"), 11, "0.88 0.02", "0.98 0.08", $"cui.endtest SENDCMD STOP {page}"));

        // Grid: BetterTC layout - 135x135 cards, 6 cols, 2 rows, list_startX=-430 list_startY=190, spacing 10x35
        int listSizeX = 135, listSizeY = 135;
        int listStartX = -430, listStartY = 190;
        int listSpacingX = 10, listSpacingY = 35;
        int startIdx = page * perPage;
        int count = Math.Min(perPage, items.Count - startIdx);

        for (int i = 0; i < count; i++)
        {
            var item = items[startIdx + i];
            int col = i % 6, row = i / 6;
            int listX = listStartX + col * (listSizeX + listSpacingX);
            int listY = listStartY - row * (listSizeY + listSpacingY);

            var cardName = $"card_{item.ID}_{page}";
            elements.Add(CUIHelper.Panel(cardName, "TCUpgrade.content", "0.2 0.3 0.2 0.6", "0.5 0.5", "0.5 0.5", $"{listX} {listY - listSizeY - 25}", $"{listX + listSizeX} {listY}", false));

            // Icon: Full URLs always use RawImageUrl. Short keys: UseUrlForMenuImages picks URL vs FileStorage.
            JObject iconEl = null;
            if (!string.IsNullOrEmpty(item.Img))
            {
                if (item.Img.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || item.Img.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    iconEl = CUIHelper.RawImageUrl($"icon_{item.ID}_{page}", cardName, item.Img, "0.05 0.22", "0.95 0.92");
                else
                {
                    var useUrl = TCUpgradeConfig.Config?.UseUrlForMenuImages ?? true;
                    if (useUrl)
                        iconEl = CUIHelper.RawImageUrl($"icon_{item.ID}_{page}", cardName, CUIHelper.GetImageUrl(item.Img), "0.05 0.22", "0.95 0.92");
                    else
                    {
                        var localKey = item.Img.IndexOf("/", StringComparison.Ordinal) >= 0 ? System.IO.Path.GetFileNameWithoutExtension(item.Img) : item.Img;
                        var pngId = GetLocalImage(item.Img) ?? GetLocalImage(localKey);
                        if (!string.IsNullOrEmpty(pngId))
                            iconEl = CUIHelper.RawImage($"icon_{item.ID}_{page}", cardName, pngId, "0.05 0.22", "0.95 0.92");
                    }
                }
            }
            if (iconEl == null && !string.IsNullOrEmpty(TCUpgradeConfig.Config?.ImageUrlBase))
            {
                var baseUrl = TCUpgradeConfig.Config.ImageUrlBase.Trim().TrimEnd('/') + "/";
                var fallbackKey = item.Name?.ToLowerInvariant().Replace(" ", "") ?? "";
                if (!string.IsNullOrEmpty(fallbackKey))
                    iconEl = CUIHelper.RawImageUrl($"icon_{item.ID}_{page}", cardName, baseUrl + fallbackKey + ".png", "0.05 0.22", "0.95 0.92");
            }
            if (iconEl == null && item.ItemID != 0)
                iconEl = CUIHelper.Image($"icon_{item.ID}_{page}", cardName, item.ItemID, (ulong)item.SkinId, "0.05 0.22", "0.95 0.92");
            else if (iconEl == null && item.ItemID2 != 0)
                iconEl = CUIHelper.Image($"icon_{item.ID}_{page}", cardName, item.ItemID2, (ulong)item.SkinId, "0.05 0.22", "0.95 0.92");
            if (iconEl != null)
                elements.Add(iconEl);

            elements.Add(CUIHelper.Label($"lbl_{item.ID}_{page}", cardName, item.Name, 12, "0.05 0", "0.55 0.15", "0.7 0.7 0.7 1", "MiddleLeft"));

            var upgCmd = item.Color ? $"cui.endtest SENDCMD COLOR {item.ID} {item.Grade} {item.SkinId} {ColorIndexFromUint(cfg.Colour)} {page}" : $"cui.endtest SENDCMD UPGRADE {item.ID} {item.Grade} {item.SkinId} {page} 0";
            var upgColor = cfg.Work && cfg.Id == item.ID ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.1";
            elements.AddRange(CUIHelper.Button($"upg_{item.ID}_{page}", cardName, upgColor, cfg.Work && cfg.Id == item.ID ? LangHelper.Lang("STOP") : LangHelper.Lang("UPGRADE"), 10, "0.6 0", "0.99 0.15", cfg.Work && cfg.Id == item.ID ? $"cui.endtest SENDCMD STOP {page}" : upgCmd));

            elements.AddRange(CUIHelper.Button($"cost_{item.ID}_{page}", cardName, "0.4 0.4 0.4 0.3", "?", 10, "0.82 0.82", "0.95 0.95", $"cui.endtest SENDCMD COSTUPGRADE {item.ID} {item.Grade} {item.SkinId} {page}"));
            // Spraycan (reskin) and wallpaper: small buttons on each card with icons (item IDs from BetterTC)
            const int SpraycanItemId = -596876839;   // assets/prefabs/tools/spraycan
            const int ClothItemId = 1629564540;      // wallpaper/cloth icon
            if ((TCUpgradeConfig.Config?.Reskin ?? true) && HasPermission(player.UserIDString, "TCUpgrade.reskin"))
            {
                var reskinActive = cfg.Reskin && cfg.Id == item.ID;
                elements.Add(CUIHelper.Panel($"reskin_bg_{item.ID}_{page}", cardName, reskinActive ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.30", "0.82 0.66", "0.95 0.79", "0 0", "0 0", false));
                var reskinIcon = CUIHelper.Image($"reskin_icon_{item.ID}_{page}", $"reskin_bg_{item.ID}_{page}", SpraycanItemId, 0, "0.05 0.05", "0.95 0.95");
                if (reskinIcon != null) elements.Add(reskinIcon);
                var reskinCmd = $"cui.endtest SENDCMD RESKIN {item.ID} {item.Grade} {item.SkinId} {page}";
                if (item.Color) reskinCmd += $" {ColorIndexFromUint(cfg.Colour)}";
                elements.AddRange(CUIHelper.Button($"reskin_{item.ID}_{page}", cardName, "0 0 0 0", "", 10, "0.82 0.66", "0.95 0.79", reskinCmd));
            }
            if ((TCUpgradeConfig.Config?.Wallpaper ?? true) && HasPermission(player.UserIDString, "TCUpgrade.wallpaper") && !cup.HasParent())
            {
                var wpActive = cfg.WorkWallpaper != null && cfg.Id == item.ID;
                elements.Add(CUIHelper.Panel($"wall_bg_{item.ID}_{page}", cardName, wpActive ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.30", "0.82 0.50", "0.95 0.63", "0 0", "0 0", false));
                var wallIcon = CUIHelper.Image($"wall_icon_{item.ID}_{page}", $"wall_bg_{item.ID}_{page}", ClothItemId, 0, "0.05 0.05", "0.95 0.95");
                if (wallIcon != null) elements.Add(wallIcon);
                elements.AddRange(CUIHelper.Button($"wall_{item.ID}_{page}", cardName, "0 0 0 0", "", 10, "0.82 0.50", "0.95 0.63", $"cui.endtest SENDCMD WALLPAPER {item.ID} {item.Grade} {item.SkinId} {page} Wall"));
            }
        }

        CUIHelper.AddUi(player, elements);
    }

    /// <summary>Called when CUI button uses cui.endtest transport (cui.endtest SENDCMD MENU).</summary>
    public void HandleSendCmdFromCui(ConsoleSystem.Arg arg)
    {
        var a = arg?.Args;
        if (a == null || a.Length < 2 || a[0] != "SENDCMD") return;
        var args = new string[a.Length - 1];
        for (int i = 0; i < args.Length; i++) args[i] = a[i + 1];
        HandleSendCmdWithArgs(arg.Player(), args);
    }

    private void HandleSendCmd(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null) return;
        HandleSendCmdWithArgs(player, arg.Args);
    }

    private void HandleSendCmdWithArgs(BasePlayer player, string[] args)
    {
        if (player == null) return;

        var cup = _playerLootingTc.TryGetValue(player.userID, out var cached) && cached != null && _buildingCupboard.ContainsKey(cached)
            ? cached
            : GetPlayerTC(player);
        if (cup == null || !_buildingCupboard.ContainsKey(cup))
        {
            cup = player.GetBuildingPrivilege();
            if (cup == null || !_buildingCupboard.ContainsKey(cup))
            {
                if (TCUpgradeConfig.Config?.Debug ?? false)
                    Log($"HandleSendCmd: no TC for {player.displayName} (not looting? cup={cup?.net?.ID})");
                return;
            }
        }

        if (!cup.IsAuthed(player))
        {
            if (TCUpgradeConfig.Config?.Debug ?? false)
                Log($"HandleSendCmd: {player.displayName} not authed on TC");
            return;
        }

        if (args == null || args.Length < 1)
        {
            if (TCUpgradeConfig.Config?.Debug ?? false)
                Log($"HandleSendCmd: no args from {player.displayName}");
            return;
        }

        if (TCUpgradeConfig.Config?.Debug ?? false)
            Log($"HandleSendCmd: {player.displayName} -> {args[0]}");

        switch (args[0])
        {
            case "MENU":
                ShowMenu(player, cup, 0);
                break;
            case "PAGE":
                if (args.Length >= 2 && int.TryParse(args[1], out var page))
                    ShowMenu(player, cup, page);
                break;
            case "CLOSE":
                CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
                CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
                ShowButtonTC(player, cup);
                break;
            case "UPGRADE":
                if (args.Length < 6) return;
                if (IsRaidBlocked(player)) { TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RaidBlocked"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (!HasPermission(player.UserIDString, "TCUpgrade.upgrade")) { TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (!TCUpgradeHelpers.Unlock(_maxGradeTier, args[2])) { TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeBlock"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                StartUpgrade(player, cup, args);
                break;
            case "REPAIR":
                if (IsRaidBlocked(player)) { TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RaidBlocked"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (!HasPermission(player.UserIDString, "TCUpgrade.repair")) return;
                ToggleRepair(player, cup);
                break;
            case "STOP":
                if (args.Length >= 2 && int.TryParse(args[1], out var stopPage))
                    HandleStop(player, cup, stopPage);
                break;
            case "COSTUPGRADE":
                if (args.Length >= 5 && int.TryParse(args[1], out var costId) && int.TryParse(args[3], out var costSkin) && int.TryParse(args[4], out var costPage))
                    HandleCostUpgrade(player, cup, costId, args[2], costSkin, costPage);
                break;
            case "EFFECT":
                if (args.Length >= 2 && int.TryParse(args[1], out var effPage))
                {
                    var c = GetOrCreateConfig(cup);
                    c.Effect = !c.Effect;
                    ShowMenu(player, cup, effPage);
                }
                break;
            case "DOWNGRADE":
                if (args.Length >= 2 && int.TryParse(args[1], out var dgPage))
                {
                    var c = GetOrCreateConfig(cup);
                    c.Downgrade = !c.Downgrade;
                    ShowMenu(player, cup, dgPage);
                }
                break;
            case "RESKIN":
                if (IsRaidBlocked(player)) { TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RaidBlocked"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (!HasPermission(player.UserIDString, "TCUpgrade.reskin")) { TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (args.Length >= 6) HandleReskin(player, cup, args);
                break;
            case "UPWALL":
                if (IsRaidBlocked(player)) { TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RaidBlocked"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (!HasPermission(player.UserIDString, "TCUpgrade.upwall")) { TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (args.Length >= 5) HandleUpwall(player, cup, args);
                break;
            case "WALLPAPER":
                if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper")) return;
                if (args.Length >= 5) ShowMenuWallpaper(player, cup, int.Parse(args[4]), args.Length > 5 ? args[5] : "Wall");
                break;
            case "WALLPAPERSELECT":
                if (args.Length >= 6 && ulong.TryParse(args[5], out var wpId)) { GetOrCreateConfig(cup).WallpaperId = wpId; ShowMenuWallpaper(player, cup, int.Parse(args[4]), args.Length > 6 ? args[6] : "Wall"); }
                break;
            case "WALLPAPERON":
                if (IsRaidBlocked(player)) { TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RaidBlocked"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper")) { TCUpgradeHelpers.CreateGameTip(null, LangHelper.Lang("UpgradeLock"), player, TCUpgradeHelpers.FxError, 10, "danger"); return; }
                if (args.Length >= 7) HandleWallpaperOn(player, cup, args);
                break;
            case "WALLPAPERSIDES":
                if (args.Length >= 7) { var c = GetOrCreateConfig(cup); if (args.Length > 5) bool.TryParse(args[5], out c.WpExternal); if (args.Length > 6) bool.TryParse(args[6], out c.WpInternal); ShowMenuWallpaper(player, cup, int.Parse(args[4]), args.Length > 7 ? args[7] : "Wall"); }
                break;
            case "DELCUSTOMWP":
                if (HasPermission(player.UserIDString, "TCUpgrade.admin") && args.Length >= 4 && ulong.TryParse(args[1], out var delSkin) && _data.CustomWallpapers.TryGetValue(args[2], out var lst) && lst.Remove(delSkin)) { _data.Save(); ShowMenuWallpaper(player, cup, int.Parse(args[4]), args[2]); }
                break;
            case "AUTH":
                if (HasPermission(player.UserIDString, "TCUpgrade.authlist") && args.Length >= 2 && int.TryParse(args[1], out var authPage)) ShowMenuAuthlist(player, cup, authPage);
                break;
            case "REMOVEAUTH":
                if (HasPermission(player.UserIDString, "TCUpgrade.authlist") && args.Length >= 4 && ulong.TryParse(args[3], out var removeUid) && cup.IsAuthed(player)) { cup.authorizedPlayers.Remove(removeUid); cup.SendNetworkUpdate(); if (player.userID == removeUid) { CUIHelper.DestroyUi(player, "TCUpgrade.authlist"); return; } ShowMenuAuthlist(player, cup, int.Parse(args[1])); }
                break;
            case "TCSKIN":
                if (HasPermission(player.UserIDString, "TCUpgrade.tcskinchange") && args.Length >= 2 && int.TryParse(args[1], out var tcPage)) { ShowMenuTCSkin(player, cup, tcPage); CUIHelper.DestroyUi(player, "TCUpgrade.upgrade"); }
                break;
            case "TCSKINSELECT":
                if (args.Length >= 3)
                {
                    var skin = TCSkinFromShortName(args[1]);
                    SetPlayerSelectedSkin(player.userID, skin);
                    TCSkinReplace(cup, player, skin);
                    CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
                    CUIHelper.DestroyUi(player, "TCUpgrade.buttons");
                }
                break;
            case "COLOR":
                if (args.Length >= 6) ShowMenuColor(player, cup, args[1], args[2], args[3], args[4], int.Parse(args[5]));
                break;
            case "COLORSELECT":
                if (args.Length >= 6 && int.TryParse(args[4], out var colIdx)) { GetOrCreateConfig(cup).Colour = TCUpgradeHelpers.ColorIndexToUint(colIdx); ShowMenuColor(player, cup, args[1], args[2], args[3], args[4], int.Parse(args[5])); }
                break;
            case "CLOSE2":
                if (args.Length >= 2 && int.TryParse(args[1], out var closePage)) { CUIHelper.DestroyUi(player, "TCUpgrade.color"); CUIHelper.DestroyUi(player, "TCUpgrade.tcskin"); ShowMenu(player, cup, closePage); }
                break;
        }
    }

    private static int ColorIndexFromUint(uint c)
    {
        if (c == 0) return 0;
        for (int i = 1; i < TCUpgradeHelpers.Colors.Length; i++)
            if (TCUpgradeHelpers.ColorIndexToUint(i) == c) return i;
        return 0;
    }

    private static TCSkin TCSkinFromShortName(string shortName)
    {
        foreach (var kv in TCSkinMeta.Data)
            if (kv.Value.ShortName == shortName) return kv.Key;
        return TCSkin.Default;
    }

    private void HandleWallpaperOn(BasePlayer player, BuildingPrivlidge cup, string[] args)
    {
        if (!int.TryParse(args[1], out var id) || !int.TryParse(args[3], out var skinId) || !int.TryParse(args[4], out var page)) return;
        var gradeStr = args[2];
        var wallpall = args.Length > 5 && args[5] == "true";
        var category = args.Length > 6 ? args[6] : "Wall";
        var cfg = GetOrCreateConfig(cup);
        if (TCUpgradeHelpers.IsOnBarge(cup))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
            return;
        }
        if (cfg.WallpaperId > 1 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, (int)cfg.WallpaperId))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
            return;
        }
        var bg = gradeStr.ToLower() switch { "stone" => BuildingGrade.Enum.Stone, "metal" => BuildingGrade.Enum.Metal, "armored" => BuildingGrade.Enum.TopTier, _ => BuildingGrade.Enum.Wood };
        cfg.Id = id;
        cfg.Grade = bg;
        cfg.SkinId = skinId;
        cfg.Wallpall = wallpall;
        cfg.Work = !cfg.Work;
        cfg.Player = player.userID;

        if (cfg.Work)
            cfg.WorkWallpaper = ServerMgr.Instance.StartCoroutine(WallpaperProgress(player, cup, category));
        else if (cfg.WorkWallpaper != null)
        {
            ServerMgr.Instance.StopCoroutine(cfg.WorkWallpaper);
            cfg.WorkWallpaper = null;
        }
        CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
        CUIHelper.DestroyUi(player, "TCUpgrade.color");
        ShowButtonTC(player, cup);
    }

    private void HandleReskin(BasePlayer player, BuildingPrivlidge cup, string[] args)
    {
        if (!int.TryParse(args[1], out var id) || !int.TryParse(args[3], out var skinId) || !int.TryParse(args[4], out var page)) return;
        var gradeStr = args[2];
        var hasColor = args.Length > 5 && args[5] != "0";
        var itemInfo = TCUpgradeHelpers.GetItemInfo(id, gradeStr);
        if (itemInfo != null && itemInfo.DisableBarges && TCUpgradeHelpers.IsOnBarge(cup))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
            return;
        }
        if (skinId > 0 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, skinId))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
            return;
        }
        var bg = gradeStr.ToLower() switch { "stone" => BuildingGrade.Enum.Stone, "metal" => BuildingGrade.Enum.Metal, "armored" => BuildingGrade.Enum.TopTier, _ => BuildingGrade.Enum.Wood };

        var cfg = GetOrCreateConfig(cup);
        cfg.Id = id;
        cfg.Grade = bg;
        cfg.SkinId = skinId;
        cfg.Color = hasColor;
        cfg.Reskin = !cfg.Reskin;
        cfg.Player = player.userID;

        if (cfg.Reskin)
            cfg.WorkReskin = ServerMgr.Instance.StartCoroutine(ReskinProgress(player, cup));
        else if (cfg.WorkReskin != null)
        {
            ServerMgr.Instance.StopCoroutine(cfg.WorkReskin);
            cfg.WorkReskin = null;
        }
        CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
        ShowMenu(player, cup, page);
    }

    private void HandleUpwall(BasePlayer player, BuildingPrivlidge cup, string[] args)
    {
        if (!int.TryParse(args[1], out var id) || !int.TryParse(args[3], out var skinId) || !int.TryParse(args[4], out var page)) return;
        var gradeStr = args[2];
        var itemInfo = TCUpgradeHelpers.GetItemInfo(id, gradeStr);
        if (itemInfo != null && itemInfo.DisableBarges && TCUpgradeHelpers.IsOnBarge(cup))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
            return;
        }
        if (skinId > 0 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, skinId))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
            return;
        }
        var bg = gradeStr.ToLower() switch { "stone" => BuildingGrade.Enum.Stone, "metal" => BuildingGrade.Enum.Metal, "armored" => BuildingGrade.Enum.TopTier, _ => BuildingGrade.Enum.Wood };

        var cfg = GetOrCreateConfig(cup);
        cfg.Id = id;
        cfg.Grade = bg;
        cfg.SkinId = skinId;
        cfg.Upwall = !cfg.Upwall;
        cfg.Player = player.userID;

        if (cfg.Upwall)
            cfg.WorkUpwall = ServerMgr.Instance.StartCoroutine(ReskinProgressWall(player, cup));
        else if (cfg.WorkUpwall != null)
        {
            ServerMgr.Instance.StopCoroutine(cfg.WorkUpwall);
            cfg.WorkUpwall = null;
        }
        CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
        ShowButtonTC(player, cup);
    }

    private void HandleStop(BasePlayer player, BuildingPrivlidge cup, int page)
    {
        var cfg = GetOrCreateConfig(cup);
        cfg.Work = !cfg.Work;
        if (cfg.Work)
            cfg.WorkUpgrade = ServerMgr.Instance.StartCoroutine(UpdateProgress(player, cup));
        else
        {
            if (cfg.WorkUpgrade != null) { ServerMgr.Instance.StopCoroutine(cfg.WorkUpgrade); cfg.WorkUpgrade = null; }
            if (cfg.WorkWallpaper != null) { ServerMgr.Instance.StopCoroutine(cfg.WorkWallpaper); cfg.WorkWallpaper = null; }
            if (cfg.WorkUpwall != null) { ServerMgr.Instance.StopCoroutine(cfg.WorkUpwall); cfg.WorkUpwall = null; }
        }
        ShowMenu(player, cup, page);
    }

    private void HandleCostUpgrade(BasePlayer player, BuildingPrivlidge cup, int id, string gradeStr, int skinId, int page)
    {
        var bg = gradeStr.ToLower() switch { "stone" => BuildingGrade.Enum.Stone, "metal" => BuildingGrade.Enum.Metal, "armored" => BuildingGrade.Enum.TopTier, _ => BuildingGrade.Enum.Wood };
        var cfg = GetOrCreateConfig(cup);
        cfg.Id = id;
        cfg.Grade = bg;
        cfg.SkinId = skinId;
        ServerMgr.Instance.StartCoroutine(UpdateCost(player, cup));
        CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
        ShowButtonTC(player, cup);
    }

    private void StartUpgrade(BasePlayer player, BuildingPrivlidge cup, string[] args)
    {
        if (args.Length < 6) return;
        if (!int.TryParse(args[1], out var id) || !int.TryParse(args[3], out var skinId) || !int.TryParse(args[4], out var page)) return;
        var gradeStr = args[2];
        var hasColor = args[5] == "1";

        var itemInfo = TCUpgradeHelpers.GetItemInfo(id, gradeStr);
        if (itemInfo != null && itemInfo.DisableBarges && TCUpgradeHelpers.IsOnBarge(cup))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("DisableBarges"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.disablebarges", "Not available for Barges");
            return;
        }

        if (skinId > 0 && !TCUpgradeHelpers.IsSkinOwnedOrBypass(player, skinId))
        {
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoDLCPurchased"), player, TCUpgradeHelpers.FxError, 10, "danger");
            TCUpgradeHelpers.ShowToast(player, GameTip.Styles.Error, "tcupgrade.nodlc", "You don't have this DLC purchased.");
            return;
        }

        var bg = gradeStr.ToLower() switch
        {
            "stone" => BuildingGrade.Enum.Stone,
            "metal" => BuildingGrade.Enum.Metal,
            "armored" => BuildingGrade.Enum.TopTier,
            _ => BuildingGrade.Enum.Wood
        };

        var cfg = GetOrCreateConfig(cup);
        cfg.Id = id;
        cfg.Grade = bg;
        cfg.SkinId = skinId;
        cfg.Color = hasColor;
        cfg.Work = !cfg.Work;
        cfg.Player = player.userID;

        if (cfg.Work)
            cfg.WorkUpgrade = ServerMgr.Instance.StartCoroutine(UpdateProgress(player, cup));
        else if (cfg.WorkUpgrade != null)
        {
            ServerMgr.Instance.StopCoroutine(cfg.WorkUpgrade);
            cfg.WorkUpgrade = null;
        }

        CUIHelper.DestroyUi(player, "TCUpgrade.upgrade");
        ShowButtonTC(player, cup);
    }

    private void ToggleRepair(BasePlayer player, BuildingPrivlidge cup)
    {
        var cfg = GetOrCreateConfig(cup);
        cfg.Repair = !cfg.Repair;
        cfg.Player = player.userID;

        if (cfg.Repair)
            cfg.WorkRepair = ServerMgr.Instance.StartCoroutine(RepairProgress(player, cup));
        else if (cfg.WorkRepair != null)
        {
            ServerMgr.Instance.StopCoroutine(cfg.WorkRepair);
            cfg.WorkRepair = null;
        }
        ShowButtonTC(player, cup);
    }

    private IEnumerator UpdateCost(BasePlayer player, BuildingPrivlidge cup)
    {
        var building = cup?.GetBuilding();
        if (building?.buildingBlocks == null) yield break;

        var cfg = GetOrCreateConfig(cup);
        var teamMembers = (TCUpgradeConfig.Config?.TeamUpdate ?? false) ? GetTeamMembers(player) : new List<ulong> { player.userID };
        var totalCost = new Dictionary<ItemDefinition, int>();

        foreach (var block in building.buildingBlocks)
        {
            if (!teamMembers.Contains(block.OwnerID)) continue;
            if (cfg.Grade == block.grade) continue;
            var canDowngrade = (TCUpgradeConfig.Config?.Downgrade ?? true) && cfg.Downgrade;
            var isOwner = player.userID == block.OwnerID;
            if ((!canDowngrade || (TCUpgradeConfig.Config?.OnlyOwner ?? true) && !isOwner) && cfg.Grade < block.grade) continue;
            if ((TCUpgradeConfig.Config?.OnlyOwnerUp ?? true) && !isOwner && cfg.Grade > block.grade) continue;

            var costs = block.blockDefinition.GetGrade(cfg.Grade, 0).CostToBuild();
            foreach (var c in costs)
            {
                if (totalCost.TryGetValue(c.itemDef, out var amt)) totalCost[c.itemDef] = amt + (int)c.amount;
                else totalCost[c.itemDef] = (int)c.amount;
            }
        }

        var msg = new System.Text.StringBuilder();
        foreach (var kv in totalCost) msg.AppendLine($"{kv.Value} x {kv.Key.shortname}");
        var text = totalCost.Count == 0 ? LangHelper.Lang("NoUpgradeAvailable") : LangHelper.Lang("TotalCostUP", msg.ToString());
        TCUpgradeHelpers.CreateGameTip(cup, text, player, TCUpgradeHelpers.FxFinish, 10);
        yield break;
    }

    private IEnumerator UpdateProgress(BasePlayer player, BuildingPrivlidge cup)
    {
        var building = cup.GetBuilding();
        if (building?.buildingBlocks == null) yield break;

        yield return CoroutineEx.waitForSeconds(0.15f);

        var cfg = GetOrCreateConfig(cup);
        var teamMembers = (TCUpgradeConfig.Config?.TeamUpdate ?? false) ? GetTeamMembers(player) : new List<ulong> { player.userID };
        var cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyUpgrade);
        bool show = true;

        foreach (var block in building.buildingBlocks)
        {
            if (cup == null || !cfg.Work) { show = false; break; }
            if (!teamMembers.Contains(block.OwnerID)) continue;
            if (cfg.Grade == block.grade) continue;

            var canDowngrade = (TCUpgradeConfig.Config?.Downgrade ?? true) && cfg.Downgrade;
            var isOwner = player.userID == block.OwnerID;
            if ((!canDowngrade || (TCUpgradeConfig.Config?.OnlyOwner ?? true) && !isOwner) && cfg.Grade < block.grade) continue;
            if ((TCUpgradeConfig.Config?.OnlyOwnerUp ?? true) && !isOwner && cfg.Grade > block.grade) continue;

            UpgradeBlock(cup, block, cfg.Grade, player);
            yield return CoroutineEx.waitForSeconds(cd);
        }

        cfg.Work = false;
        cfg.WorkUpgrade = null;
        if (show) TCUpgradeHelpers.CreateGameTip(cup, teamMembers.Count <= 1 ? LangHelper.Lang("UpgradeFinishNoPlayer") : LangHelper.Lang("UpgradeFinish"), player, TCUpgradeHelpers.FxFinish, 10);
        ShowButtonTCIfStillLooting(player, cup);
    }

    private IEnumerator RepairProgress(BasePlayer player, BuildingPrivlidge cup)
    {
        var building = cup.GetBuilding();
        if (building?.buildingBlocks == null) yield break;

        yield return CoroutineEx.waitForSeconds(0.15f);

        var cfg = GetOrCreateConfig(cup);
        var costMult = TCUpgradeHelpers.ResourcesRepair(player.UserIDString);
        var cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyRepair);
        var allEntities = new List<BaseCombatEntity>(building.buildingBlocks);
        if (TCUpgradeConfig.Config?.Deployables ?? true && building.decayEntities != null)
            allEntities.AddRange(building.decayEntities);
        bool show = true;
        bool warned = false;
        var cooldown = TCUpgradeConfig.Config?.RepairCooldown ?? 30f;

        foreach (var entity in allEntities)
        {
            if (!cfg.Repair) { show = false; break; }
            if (entity.SecondsSinceAttacked < cooldown)
            {
                if (!warned) { warned = true; TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RepairBlockedRecentDamage", entity.ShortPrefabName, (cooldown - entity.SecondsSinceAttacked).ToString("0.0")), player, TCUpgradeHelpers.FxNoResources, 10, "warning"); }
                continue;
            }
            if (!RepairBlock(player, entity, cup, costMult)) continue;
            yield return CoroutineEx.waitForSeconds(cd);
        }

        cfg.Repair = false;
        cfg.WorkRepair = null;
        if (show) TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("RepairFinish"), player, TCUpgradeHelpers.FxFinish, 10);
        ShowButtonTCIfStillLooting(player, cup);
    }

    private bool RepairBlock(BasePlayer player, BaseCombatEntity entity, BuildingPrivlidge cup, float costMult)
    {
        if (entity == null || !entity.IsValid() || entity.IsDestroyed || !entity.repair.enabled || entity.health >= entity.MaxHealth()) return false;

        var missing = entity.MaxHealth() - entity.health;
        var pct = missing / entity.MaxHealth();
        if (missing <= 0 || pct <= 0) return false;

        var costs = entity.RepairCost(pct);
        float totalAmount = 0f;
        for (int ci = 0; ci < costs.Count; ci++) totalAmount += costs[ci].amount;
        if (totalAmount <= 0)
        {
            entity.health += missing;
            entity.SendNetworkUpdate();
            entity.OnRepairFinished(player);
            return true;
        }

        if (HasPermission(player.UserIDString, "TCUpgrade.repair.nocost"))
        {
            entity.health += missing;
            entity.SendNetworkUpdate();
            entity.OnRepairFinished(player);
            return true;
        }

        foreach (var c in costs)
        {
            var need = (int)(c.amount * costMult);
            if (cup.inventory.GetAmount(c.itemid, false) < need)
            {
                TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesRepair"), player, TCUpgradeHelpers.FxNoResources, 10, "danger");
                GetOrCreateConfig(cup).Repair = false;
                return false;
            }
        }
        foreach (var c in costs)
            cup.inventory.Take(null, c.itemid, (int)(c.amount * costMult));

        if ((TCUpgradeConfig.Config?.PlayFx ?? true) && GetOrCreateConfig(cup).Effect)
            Effect.server.Run(TCUpgradeHelpers.FxRepair, entity.transform.position);

        entity.health += missing;
        entity.SendNetworkUpdate();
        if (entity.health >= entity.MaxHealth()) entity.OnRepairFinished(player);
        else entity.OnRepair();
        return true;
    }

    private void UpgradeBlock(BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade, BasePlayer player)
    {
        var cfg = GetOrCreateConfig(cup);
        if (!HasPermission(player.UserIDString, "TCUpgrade.upgrade.nocost"))
        {
            if (!CanUpgrade(player, cup, block, grade))
            {
                cfg.Work = false;
                TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesUpgrade"), player, TCUpgradeHelpers.FxNoResources, 10, "danger");
                return;
            }
            var list = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
            foreach (var c in list)
                TCUpgradeHelpers.TakeResources(cup.inventory.itemList, c.itemDef.shortname, (int)c.amount);
        }

        // Skip CheckBlock for foundations - their DeployVolume often fails (objects above), but TC upgrade should include them
        var isFoundation = block.ShortPrefabName != null && block.ShortPrefabName.Contains("foundation");
        if (!isFoundation && TCUpgradeHelpers.CheckBlock(block)) return;

        block.skinID = (ulong)cfg.SkinId;
        if ((TCUpgradeConfig.Config?.PlayFx ?? true) && cfg.Effect)
        {
            var effect = grade switch
            {
                BuildingGrade.Enum.Wood => TCUpgradeHelpers.FxFramePlace,
                BuildingGrade.Enum.Stone => TCUpgradeHelpers.FxPromoteStone,
                BuildingGrade.Enum.Metal => TCUpgradeHelpers.FxPromoteMetal,
                _ => TCUpgradeHelpers.FxPromoteTopTier
            };
            block.ClientRPC(RpcTarget.NetworkGroup("DoUpgradeEffect"), (int)grade, block.skinID);
            Effect.server.Run(effect, block.transform.position);
        }

        block.SetGrade(grade);
        block.UpdateSkin();
        block.SetHealthToMax();
        if (cfg.Color) block.SetCustomColour(cfg.Colour);
        block.SendNetworkUpdateImmediate();
    }

    private bool CanUpgrade(BasePlayer player, BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade)
    {
        var list = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
        foreach (var c in list)
            if (cup.inventory.GetAmount(c.itemid, false) < c.amount) return false;
        return true;
    }

    private IEnumerator ReskinProgress(BasePlayer player, BuildingPrivlidge cup)
    {
        var building = cup.GetBuilding();
        if (building?.buildingBlocks == null) yield break;

        yield return CoroutineEx.waitForSeconds(0.15f);

        var cfg = GetOrCreateConfig(cup);
        var cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyReskin);
        bool show = true;

        foreach (var block in building.buildingBlocks)
        {
            if (cup == null || !cfg.Reskin) { show = false; break; }
            if (cfg.Grade != block.grade) continue;
            if ((ulong)cfg.SkinId == block.skinID && cfg.Colour == block.customColour) continue;
            ReskinBlock(cup, block, cfg.Grade, player);
            yield return CoroutineEx.waitForSeconds(cd);
        }

        cfg.Reskin = false;
        cfg.WorkReskin = null;
        if (show) TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("ReskinFinish"), player, TCUpgradeHelpers.FxFinish, 10);
        ShowButtonTCIfStillLooting(player, cup);
    }

    private void ReskinBlock(BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade, BasePlayer player)
    {
        var cfg = GetOrCreateConfig(cup);
        if (!HasPermission(player.UserIDString, "TCUpgrade.reskin.nocost") && !CanUpgrade(player, cup, block, grade))
        {
            cfg.Reskin = false;
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesReskin"), player, TCUpgradeHelpers.FxNoResources, 10, "danger");
            return;
        }
        var isFoundation = block.ShortPrefabName != null && block.ShortPrefabName.Contains("foundation");
        if (!isFoundation && TCUpgradeHelpers.CheckBlock(block)) return;

        if (!HasPermission(player.UserIDString, "TCUpgrade.reskin.nocost"))
        {
            var list = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
            foreach (var c in list)
                TCUpgradeHelpers.TakeResources(cup.inventory.itemList, c.itemDef.shortname, (int)c.amount);
        }

        block.skinID = (ulong)cfg.SkinId;
        block.UpdateSkin();
        if (cfg.Color) block.SetCustomColour(cfg.Colour);
        block.SendNetworkUpdateImmediate();

        if ((TCUpgradeConfig.Config?.PlayFx ?? true) && cfg.Effect)
        {
            Effect.server.Run(TCUpgradeHelpers.FxSpray, block.transform.position);
            Effect.server.Run(TCUpgradeHelpers.FxReskin, block.transform.position);
        }
    }

    private IEnumerator ReskinProgressWall(BasePlayer player, BuildingPrivlidge cup)
    {
        if (cup == null || player == null || !_buildingCupboard.ContainsKey(cup)) yield break;

        var center = cup.transform.position;
        var radius = TCUpgradeConfig.Config?.UpwallDis ?? 100f;
        var delay = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyReskin);
        var cfg = GetOrCreateConfig(cup);
        var validOwners = GetTeamMembers(player);
        bool show = false;

        var nearbyWalls = Facepunch.Pool.Get<List<BaseEntity>>();
        Vis.Entities(center, radius, nearbyWalls, UnityEngine.LayerMask.GetMask("Construction"));

        foreach (var wall in nearbyWalls)
        {
            if (!cfg.Upwall) { show = false; break; }
            if (wall == null || wall.ShortPrefabName == null) continue;
            if (!wall.ShortPrefabName.Contains("wall.external") && !wall.ShortPrefabName.Contains("gates.external.high")) continue;
            if (!validOwners.Contains(wall.OwnerID)) continue;

            var targetPrefab = TCUpgradeHelpers.GetTargetPrefab(wall.ShortPrefabName, cfg.SkinId);
            if (targetPrefab == null || targetPrefab == wall.PrefabName) continue;

            if (TCUpgradeConfig.Config?.SameWallGrade ?? true)
            {
                var from = TCUpgradeHelpers.GetWallType(wall.ShortPrefabName);
                var to = TCUpgradeHelpers.GetWallType(targetPrefab);
                if (!TCUpgradeHelpers.CanChangeWall(from, to)) continue;
            }

            ReskinWall(cup, wall, player);
            yield return CoroutineEx.waitForSeconds(delay);
            show = true;
        }

        Facepunch.Pool.FreeUnmanaged(ref nearbyWalls);
        cfg.Upwall = false;
        cfg.WorkUpwall = null;
        if (show) TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("ReskinWallFinish"), player, TCUpgradeHelpers.FxFinish, 10);
        ShowButtonTCIfStillLooting(player, cup);
    }

    private void ReskinWall(BuildingPrivlidge cup, BaseEntity wall, BasePlayer player)
    {
        var pos = wall.transform.position;
        var rot = wall.transform.rotation;
        var ownerId = wall.OwnerID;
        var newPrefab = TCUpgradeHelpers.GetTargetPrefab(wall.ShortPrefabName, GetOrCreateConfig(cup).SkinId);
        if (string.IsNullOrEmpty(newPrefab)) return;

        var newEntity = GameManager.server.CreateEntity(newPrefab, pos, rot, true);
        if (newEntity == null) return;

        if (newEntity.ShortPrefabName == "wall.external.high.legacy")
            newEntity.Invoke("PopulateVariants", 0f);
        newEntity.skinID = 0;
        newEntity.OwnerID = ownerId;
        newEntity.Spawn();

        if (newEntity is BaseCombatEntity bce && wall is BaseCombatEntity oldBce)
        {
            bce.health = oldBce.health;
            bce.lastAttackedTime = 0;
        }
        TCUpgradeHelpers.CopyLock(wall, newEntity);
        wall.Kill();

        if ((TCUpgradeConfig.Config?.PlayFx ?? true) && GetOrCreateConfig(cup).Effect)
        {
            Effect.server.Run(TCUpgradeHelpers.FxSpray, pos);
            Effect.server.Run(TCUpgradeHelpers.FxWall, pos);
        }
    }

    private List<ulong> GetTeamMembers(BasePlayer player)
    {
        var list = new List<ulong> { player.userID };
        if (player.currentTeam != 0)
        {
            var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
            if (team?.members != null) list.AddRange(team.members);
        }
        return list;
    }

    private BuildingPrivlidge GetPlayerTC(BasePlayer player)
    {
        if (UnityEngine.Physics.Raycast(player.transform.position, Vector3.down, out var hit, 2f))
            return hit.collider.GetComponentInParent<BuildingBlock>()?.GetBuildingPrivilege();
        return null;
    }

    public TCSkin GetPlayerSelectedSkin(ulong userId)
    {
        return _playerSelectedSkins.TryGetValue(userId, out var s) ? s : TCSkin.Default;
    }

    public void SetPlayerSelectedSkin(ulong userId, TCSkin skin)
    {
        _playerSelectedSkins[userId] = skin;
    }

    public void TCSkinReplace(BuildingPrivlidge tc, BasePlayer player, TCSkin skin)
    {
        if (!TCSkinMeta.Data.TryGetValue(skin, out var meta)) return;

        var pos = tc.transform.position;
        var rot = tc.transform.rotation;
        var newTc = GameManager.server.CreateEntity(meta.PrefabPath, pos, rot, true);
        if (newTc == null) return;

        newTc.OwnerID = tc.OwnerID;
        newTc.Spawn();

        NextTick(() =>
        {
            try
            {
                var building = newTc as BuildingPrivlidge;
                if (building == null) return;

                if (tc.HasParent())
                {
                    var parent = tc.GetParentEntity();
                    if (parent != null && !parent.IsDestroyed)
                        newTc.SetParent(parent, true);
                }

                foreach (var userId in tc.authorizedPlayers)
                    building.authorizedPlayers.Add(userId);

                building.AttachToBuilding(tc.buildingID);
                building.BuildingDirty();
                building.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                UpdateBlockedItems(building);

                if (tc.inventory != null && building.inventory != null)
                {
                    var itemsToMove = new List<Item>(tc.inventory.itemList);
                    for (int ii = 0; ii < itemsToMove.Count; ii++)
                    {
                        var item = itemsToMove[ii];
                        var newItem = ItemManager.CreateByItemID(item.info.itemid, item.amount, item.skin);
                        if (newItem != null)
                        {
                            newItem.condition = item.condition;
                            newItem.maxCondition = item.maxCondition;
                            newItem.MoveToContainer(building.inventory);
                        }
                    }
                }
                TCUpgradeHelpers.CopyLock(tc, building);
                Effect.server.Run(meta.EffectPath, newTc.transform.position);
                tc.inventory?.Clear();
                tc.Kill();

                newTc.UpdateNetworkGroup();
                newTc.SendNetworkUpdateImmediate();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[TCUpgrade] TCSkinReplace error: {ex}");
            }
        });
    }

    private void NextTick(Action action)
    {
        ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action));
    }

    private IEnumerator NextTickCoroutine(Action action)
    {
        yield return null;
        try { action?.Invoke(); } catch (Exception ex) { UnityEngine.Debug.LogError($"[TCUpgrade] NextTick error: {ex}"); }
    }

    private void CmdWphammer(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player != null)
        {
            if (!HasPermission(player.UserIDString, "TCUpgrade.admin")) { arg.ReplyWith("No permission."); return; }
            GiveWallpaperHammer(player);
            return;
        }
        if (arg.Args != null && arg.Args.Length >= 1)
        {
            var target = BasePlayer.Find(arg.Args[0]) ?? BasePlayer.FindAwakeOrSleeping(arg.Args[0]);
            if (target != null) { GiveWallpaperHammer(target); arg.ReplyWith($"Wallpaper hammer given to {target.displayName}."); }
            else arg.ReplyWith("Player not found.");
        }
    }

    private void CmdAddwp(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        var args = arg.Args ?? Array.Empty<string>();
        if (player == null) return;
        if (!HasPermission(player.UserIDString, "TCUpgrade.admin")) { player.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_NoPermission")); return; }
        if (args.Length != 2 || !ulong.TryParse(args[0], out var skinId))
        {
            player.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_Usage"));
            return;
        }
        var cat = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(args[1].Trim().ToLower());
        if (cat != "Wall" && cat != "Floor" && cat != "Ceiling")
        {
            player.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_InvalidCategory"));
            return;
        }
        if (!_data.CustomWallpapers.ContainsKey(cat)) _data.CustomWallpapers[cat] = new HashSet<ulong>();
        if (_data.CustomWallpapers[cat].Add(skinId)) { _data.Save(); player.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_Added", skinId, cat)); }
        else player.SendConsoleCommand("chat.add", 2, 0, LangHelper.Lang("AddWP_AlreadyExists"));
    }

    private void GiveWallpaperHammer(BasePlayer player)
    {
        var hammer = ItemManager.CreateByName("hammer", 1, HammerWallpaperSkin);
        if (hammer != null) player.GiveItem(hammer);
    }

    public void StopCoroutinesForPlayer(ulong userId)
    {
        foreach (var kv in _buildingCupboard)
        {
            if (kv.Value.Player != userId) continue;
            if (kv.Value.WorkUpgrade != null) { ServerMgr.Instance?.StopCoroutine(kv.Value.WorkUpgrade); kv.Value.WorkUpgrade = null; }
            if (kv.Value.WorkRepair != null) { ServerMgr.Instance?.StopCoroutine(kv.Value.WorkRepair); kv.Value.WorkRepair = null; }
            if (kv.Value.WorkReskin != null) { ServerMgr.Instance?.StopCoroutine(kv.Value.WorkReskin); kv.Value.WorkReskin = null; }
            if (kv.Value.WorkWallpaper != null) { ServerMgr.Instance?.StopCoroutine(kv.Value.WorkWallpaper); kv.Value.WorkWallpaper = null; }
            if (kv.Value.WorkUpwall != null) { ServerMgr.Instance?.StopCoroutine(kv.Value.WorkUpwall); kv.Value.WorkUpwall = null; }
            break;
        }
    }

    private bool IsRaidBlocked(BasePlayer player)
    {
        if (player == null) return false;
        if (!(TCUpgradeConfig.Config?.UseNoEscape ?? false) && !(TCUpgradeConfig.Config?.UseRaidBlock ?? false)) return false;
        try
        {
            var mod = _cachedOxideModType;
            if (mod == null)
            {
                mod = Type.GetType("Oxide.Core.OxideMod, Oxide.Core");
                if (mod == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        mod = asm.GetType("Oxide.Core.OxideMod");
                        if (mod != null) break;
                    }
                }
                if (mod != null) _cachedOxideModType = mod;
            }
            if (mod == null) return false;

            var instance = _cachedOxideModInstance;
            if (instance == null)
            {
                instance = mod.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance != null) _cachedOxideModInstance = instance;
            }
            if (instance == null) return false;

            var getPlugin = mod.GetMethod("GetPlugin", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (getPlugin == null) return false;
            var noEscape = (TCUpgradeConfig.Config?.UseNoEscape ?? false) ? getPlugin.Invoke(instance, new object[] { "NoEscape" }) : null;
            var raidBlock = (TCUpgradeConfig.Config?.UseRaidBlock ?? false) ? getPlugin.Invoke(instance, new object[] { "RaidBlock" }) : null;
            if (noEscape != null)
            {
                var call = noEscape.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                if (call != null && call.Invoke(noEscape, new object[] { "IsRaidBlocked", new object[] { player.UserIDString } }) is bool b && b) return true;
            }
            if (raidBlock != null)
            {
                var call = raidBlock.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                if (call != null && call.Invoke(raidBlock, new object[] { "IsRaidBlocked", new object[] { player.UserIDString } }) is bool b && b) return true;
            }
        }
        catch { }
        return false;
    }

    private IEnumerator WallpaperProgress(BasePlayer player, BuildingPrivlidge cup, string category)
    {
        var building = cup.GetBuilding();
        if (building?.buildingBlocks == null) yield break;

        yield return CoroutineEx.waitForSeconds(0.15f);

        var cfg = GetOrCreateConfig(cup);
        var teamMembers = (TCUpgradeConfig.Config?.TeamUpdate ?? false) ? GetTeamMembers(player) : new List<ulong> { player.userID };
        var cd = TCUpgradeHelpers.Frequency(player.UserIDString, TCUpgradeConfig.Config?.FrequencyWallpaper);
        bool show = true;
        var grade = cfg.Grade;
        var wallpaperId = cfg.WallpaperId;
        bool isCeiling = category == "Ceiling";
        bool isFloor = category == "Floor";

        foreach (var block in building.buildingBlocks)
        {
            if (cup == null || !cfg.Work) { show = false; break; }
            if (!teamMembers.Contains(block.OwnerID)) continue;

            if (category == "Wall")
            {
                var applyInternal = cfg.WpInternal;
                var applyExternal = cfg.WpExternal;
                if (!applyInternal && !applyExternal) applyInternal = true;
                var internalOk = !applyInternal || (block.wallpaperID == wallpaperId && block.wallpaperHealth != -1);
                var externalOk = !applyExternal || (block.wallpaperID2 == wallpaperId && block.wallpaperHealth2 != -1);
                if (internalOk && externalOk) continue;
            }
            else
            {
                var currentId = isCeiling ? block.wallpaperID2 : block.wallpaperID;
                var currentHealth = isCeiling ? block.wallpaperHealth2 : block.wallpaperHealth;
                if (currentId == wallpaperId && currentHealth != -1) continue;
            }
            if (grade != block.grade && !cfg.Wallpall) continue;
            if (category == "Wall" && (!block.ShortPrefabName.Contains("wall") || block.ShortPrefabName.Contains("wall.frame"))) continue;
            if (category == "Floor" && !block.ShortPrefabName.Contains("floor") && !block.ShortPrefabName.Contains("foundation")) continue;
            if (category == "Ceiling" && !block.ShortPrefabName.Contains("floor") && !block.ShortPrefabName.Contains("roof")) continue;
            if ((ulong)cfg.SkinId != block.skinID && !cfg.Wallpall) continue;

            WallpaperBlock(cup, block, player, category);
            yield return CoroutineEx.waitForSeconds(cd);
        }

        cfg.Work = false;
        cfg.WorkWallpaper = null;
        if (show) TCUpgradeHelpers.CreateGameTip(cup, teamMembers.Count <= 1 ? LangHelper.Lang("WallpaperFinishNoPlayer") : LangHelper.Lang("WallpaperFinish"), player, TCUpgradeHelpers.FxFinish, 10);
        ShowButtonTCIfStillLooting(player, cup);
    }

    private void WallpaperBlock(BuildingPrivlidge cup, BuildingBlock block, BasePlayer player, string category)
    {
        var cfg = GetOrCreateConfig(cup);
        if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper.nocost") && !CanWallpaper(player, cup))
        {
            cfg.Work = false;
            TCUpgradeHelpers.CreateGameTip(cup, LangHelper.Lang("NoResourcesWallpaper"), player, TCUpgradeHelpers.FxNoResources, 10, "danger");
            return;
        }
        var isFoundation = block.ShortPrefabName != null && block.ShortPrefabName.Contains("foundation");
        if (!isFoundation && TCUpgradeHelpers.CheckBlock(block)) return;

        var wallpaperId = cfg.WallpaperId;
        var wallRes = TCUpgradeConfig.Config?.WallResource ?? 5;

        if (wallpaperId == 1)
        {
            var removeInternal = cfg.WpInternal;
            var removeExternal = cfg.WpExternal;
            if (!removeInternal && !removeExternal) removeInternal = true;
            if (removeInternal) block.RemoveWallpaper(0);
            if (removeExternal) block.RemoveWallpaper(1);
        }
        else
        {
            if (!HasPermission(player.UserIDString, "TCUpgrade.wallpaper.nocost"))
                TCUpgradeHelpers.TakeResources(cup.inventory.itemList, "cloth", wallRes);

            if (category == "Wall")
            {
                var applyInternal = cfg.WpInternal;
                var applyExternal = cfg.WpExternal;
                if (!applyInternal && !applyExternal) applyInternal = true;
                if (applyInternal) block.SetWallpaper(wallpaperId, 0, 0f);
                if ((TCUpgradeConfig.Config?.BothSides ?? true) && applyExternal) block.SetWallpaper(wallpaperId, 1);
            }
            else if (category == "Floor")
            {
                if (block.ShortPrefabName.Contains("foundation")) block.SetWallpaper(wallpaperId, 0);
                else block.SetWallpaper(wallpaperId, 1);
            }
            else if (category == "Ceiling")
                block.SetWallpaper(wallpaperId, 0);
        }

        if (!(TCUpgradeConfig.Config?.WallpaperDamage ?? true))
        {
            if (block.wallpaperProtection == null) block.wallpaperProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
            if (block.wallpaperProtection.amounts == null || block.wallpaperProtection.amounts.Length < 26) block.wallpaperProtection.amounts = new float[26];
            for (int i = 0; i < block.wallpaperProtection.amounts.Length; i++)
                block.wallpaperProtection.amounts[i] = float.MaxValue;
        }

        if (TCUpgradeConfig.Config?.PlayFx ?? true && cfg.Effect)
            Effect.server.Run(TCUpgradeHelpers.FxCloth, block.transform.position);
    }

    private bool CanWallpaper(BasePlayer player, BuildingPrivlidge cup)
    {
        return cup.inventory.GetAmount(ClothItemId, false) >= (TCUpgradeConfig.Config?.WallResource ?? 5);
    }

    private (int itemId, List<ulong> skinIds) GetWallpaperItems(BasePlayer player, string category)
    {
        var list = new List<ulong> { 1 };
        int itemId = category switch { "Wall" => 553967074, "Floor" => -551431036, "Ceiling" => 1730664641, _ => 0 };
        var idef = category switch
        {
            "Wall" => WallpaperSettings.WallpaperItemDef,
            "Floor" => WallpaperSettings.FlooringItemDef,
            "Ceiling" => WallpaperSettings.CeilingItemDef,
            _ => null
        };
        if (idef?.skins != null)
        {
            foreach (var s in idef.skins)
            {
                var id = (ulong)s.id;
                if (!list.Contains(id)) list.Add(id);
            }
        }
        if (HasPermission(player.UserIDString, "TCUpgrade.wallpaper.custom") && _data.CustomWallpapers.TryGetValue(category, out var custom))
        {
            foreach (var id in custom)
                if (!list.Contains(id)) list.Add(id);
        }
        list.Add(0);
        return (itemId, list);
    }

    private void ShowMenuWallpaper(BasePlayer player, BuildingPrivlidge cup, int page, string category = "Wall")
    {
        CUIHelper.DestroyUi(player, "TCUpgrade.color");
        var cfg = GetOrCreateConfig(cup);
        var elements = new List<JObject>();
        // Main overlay - smaller (320x240), outer frame slightly larger than scroll area
        elements.Add(CUIHelper.Panel("TCUpgrade.color", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-320 -240", "320 240", true));
        // Content panel - outer container, slightly larger than scroll (the box inside red square)
        elements.Add(CUIHelper.Panel("TCUpgrade.color_content", "TCUpgrade.color", "0.2 0.23 0.2 0.4", "0.5 0.5", "0.5 0.5", "-190 -170", "190 210", false));
        elements.Add(CUIHelper.Label("wp_title", "TCUpgrade.color", LangHelper.Lang("title5"), 16, "0.05 0.90", "0.90 0.98", "1 1 1 0.9", "MiddleLeft"));
        elements.AddRange(CUIHelper.Button("wp_close", "TCUpgrade.color", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 14, "0.92 0.90", "0.99 0.98", $"cui.endtest SENDCMD CLOSE2 {page}"));

        // Category tabs
        float btnW = 0.30f, btnH = 0.06f, startX = 0.03f, startY = 0.88f, spacing = 0.02f;
        for (int i = 0; i < 3; i++)
        {
            var cat = new[] { "Wall", "Floor", "Ceiling" }[i];
            var isActive = cat == category;
            float xMin = startX + i * (btnW + spacing);
            float xMax = xMin + btnW;
            elements.AddRange(CUIHelper.Button($"wp_cat_{cat}", "TCUpgrade.color_content", isActive ? "0.8 1 0.5 0.6" : "0.2 0.3 0.2 0.6", cat.ToUpper(), 11, $"{xMin} {startY}", $"{xMax} {startY + btnH}", $"cui.endtest SENDCMD WALLPAPER 0 wood 0 {page} {cat}"));
        }

        // Scroll view for wallpaper grid (purple box) - CUI creates wp_scroll___Content for scrollable children
        var (itemId, skinList) = GetWallpaperItems(player, category);
        int itemsPerRow = 4;
        int loops = (int)System.Math.Ceiling((double)skinList.Count / itemsPerRow);
        int listSizeY = 70;
        int totalHeight = loops * (listSizeY + 10);
        elements.Add(CUIHelper.ScrollView("wp_scroll", "TCUpgrade.color_content", "0.04 0.14", "0.96 0.72", "0 0", "0 0", totalHeight));

        string scrollContent = "wp_scroll___Content";
        int listStartX = -30, listStartY = totalHeight / 2 - 10;
        int listSizeX = 70;
        int row = 0, col = 0;
        for (int i = 0; i < skinList.Count; i++)
        {
            var skinId = skinList[i];
            int listX = listStartX + col * (listSizeX + 10);
            int listY = listStartY - row * (listSizeY + 10);
            var sel = cfg.WallpaperId == skinId;
            elements.Add(CUIHelper.Panel($"wp_sel_{i}", scrollContent, sel ? "1 1 1 0.7" : "0.2 0.3 0.2 0.6", "0.5 0.5", "0.5 0.5", $"{listX} {listY - listSizeY}", $"{listX + listSizeX} {listY}", false));
            if (skinId > 1 && itemId != 0)
            {
                var img = CUIHelper.Image($"wp_img_{i}", $"wp_sel_{i}", itemId, skinId, "0.1 0.1", "0.9 0.9");
                if (img != null) elements.Add(img);
            }
            elements.AddRange(CUIHelper.Button($"wp_btn_{i}", $"wp_sel_{i}", "0 0 0 0", skinId == 1 ? "X" : skinId == 0 ? "?" : "", 10, "0 0", "1 1", $"cui.endtest SENDCMD WALLPAPERSELECT 0 wood 0 {page} {skinId} {category}"));
            col++;
            if (col >= itemsPerRow) { col = 0; row++; }
        }

        var up = cfg.Work;
        bool isWall = category == "Wall";
        bool showIntExt = (TCUpgradeConfig.Config?.BothSides ?? true) && isWall;

        if (showIntExt)
        {
            // WALL: Internal + External on left, Place Grade + Place All on right (BetterTC layout)
            elements.Add(CUIHelper.Panel("wp_int_bg", "TCUpgrade.color_content", "1 1 1 0.05", "0.04 0.04", "0.09 0.09", "0 0", "0 0", false));
            elements.AddRange(CUIHelper.Button("wp_int", "TCUpgrade.color_content", cfg.WpInternal ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", "", 10, "0.045 0.045", "0.085 0.083", $"cui.endtest SENDCMD WALLPAPERSIDES 0 wood 0 {page} {cfg.WpExternal.ToString().ToLower()} {(!cfg.WpInternal).ToString().ToLower()} {category}"));
            elements.Add(CUIHelper.Label("wp_int_lbl", "TCUpgrade.color_content", LangHelper.Lang(cfg.WpInternal ? "InternalON" : "InternalOFF"), 10, "0.10 0.04", "0.30 0.09", "0.7 0.7 0.7 1", "MiddleLeft"));
            elements.Add(CUIHelper.Panel("wp_ext_bg", "TCUpgrade.color_content", "1 1 1 0.05", "0.26 0.04", "0.31 0.09", "0 0", "0 0", false));
            elements.AddRange(CUIHelper.Button("wp_ext", "TCUpgrade.color_content", cfg.WpExternal ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", "", 10, "0.265 0.045", "0.305 0.083", $"cui.endtest SENDCMD WALLPAPERSIDES 0 wood 0 {page} {(!cfg.WpExternal).ToString().ToLower()} {cfg.WpInternal.ToString().ToLower()} {category}"));
            elements.Add(CUIHelper.Label("wp_ext_lbl", "TCUpgrade.color_content", LangHelper.Lang(cfg.WpExternal ? "ExternalON" : "ExternalOFF"), 10, "0.32 0.04", "0.52 0.09", "0.7 0.7 0.7 1", "MiddleLeft"));
            elements.AddRange(CUIHelper.Button("wp_go", "TCUpgrade.color_content", up ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", up ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERGRADE"), 12, "0.51 0.03", "0.73 0.10", up ? $"cui.endtest SENDCMD STOP 0 wood 0 {page} {category}" : $"cui.endtest SENDCMD WALLPAPERON 0 wood 0 {page} false {category}"));
            elements.AddRange(CUIHelper.Button("wp_all", "TCUpgrade.color_content", up ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", up ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERALL"), 12, "0.75 0.03", "0.95 0.10", up ? $"cui.endtest SENDCMD STOP 0 wood 0 {page} {category}" : $"cui.endtest SENDCMD WALLPAPERON 0 wood 0 {page} true {category}"));
        }
        else
        {
            // FLOOR / CEILING: Only Place Grade + Place All (no Internal/External)
            elements.AddRange(CUIHelper.Button("wp_go", "TCUpgrade.color_content", up ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", up ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERGRADE"), 12, "0.20 0.03", "0.45 0.10", up ? $"cui.endtest SENDCMD STOP 0 wood 0 {page} {category}" : $"cui.endtest SENDCMD WALLPAPERON 0 wood 0 {page} false {category}"));
            elements.AddRange(CUIHelper.Button("wp_all", "TCUpgrade.color_content", up ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", up ? LangHelper.Lang("STOP") : LangHelper.Lang("WALLPAPERALL"), 12, "0.55 0.03", "0.80 0.10", up ? $"cui.endtest SENDCMD STOP 0 wood 0 {page} {category}" : $"cui.endtest SENDCMD WALLPAPERON 0 wood 0 {page} true {category}"));
        }
        CUIHelper.AddUi(player, elements);
    }

    private void ShowMenuAuthlist(BasePlayer player, BuildingPrivlidge cup, int page)
    {
        CUIHelper.DestroyUi(player, "TCUpgrade.authlist");
        var authList = GetAuthPlayers(cup);
        var elements = new List<JObject>();
        elements.Add(CUIHelper.Panel("TCUpgrade.authlist", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-300 -250", "300 250", true));
        elements.Add(CUIHelper.Label("auth_title", "TCUpgrade.authlist", LangHelper.Lang("title3"), 16, "0.05 0.9", "0.95 0.98"));
        elements.AddRange(CUIHelper.Button("auth_close", "TCUpgrade.authlist", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 14, "0.8 0.88", "0.98 0.98", "cui.endtest SENDCMD CLOSE"));

        float y = 0.8f;
        foreach (var uid in authList)
        {
            var p = BasePlayer.FindAwakeOrSleepingByID(uid);
            var name = p?.displayName ?? (SingletonComponent<ServerMgr>.Instance?.persistance?.GetPlayerName(uid) ?? uid.ToString());
            if (TCUpgradeConfig.Config?.SteamIdShow ?? true) name = $"{name} ({uid})";
            elements.AddRange(CUIHelper.Button($"auth_rem_{uid}", "TCUpgrade.authlist", "0.8 0.2 0.2 0.6", "X " + name, 11, $"0.02 {y - 0.08f}", $"0.98 {y}", $"cui.endtest SENDCMD REMOVEAUTH {page} 0 {uid}"));
            y -= 0.1f;
        }
        CUIHelper.AddUi(player, elements);
    }

    private List<ulong> GetAuthPlayers(BuildingPrivlidge cup)
    {
        var list = new List<ulong>();
        if (cup?.authorizedPlayers == null) return list;
        foreach (var uid in cup.authorizedPlayers)
        {
            if (HasPermission(uid.ToString(), "TCUpgrade.admin") && !(TCUpgradeConfig.Config?.Adminshow ?? false)) continue;
            list.Add(uid);
        }
        return list;
    }

    private void ShowMenuTCSkin(BasePlayer player, BuildingPrivlidge cup, int page)
    {
        CUIHelper.DestroyUi(player, "TCUpgrade.tcskin");
        var elements = new List<JObject>();
        elements.Add(CUIHelper.Panel("TCUpgrade.tcskin", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-350 -200", "350 200", true));
        elements.Add(CUIHelper.Label("tcsk_title", "TCUpgrade.tcskin", LangHelper.Lang("title4"), 16, "0.05 0.85", "0.95 0.98"));
        elements.AddRange(CUIHelper.Button("tcsk_close", "TCUpgrade.tcskin", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 14, "0.8 0.88", "0.98 0.98", $"cui.endtest SENDCMD CLOSE2 {page}"));

        int col = 0;
        foreach (var kv in TCSkinMeta.Data)
        {
            float x = 0.1f + col * 0.35f;
            var label = kv.Value.ShortName.Replace("cupboard.tool.", "");
            elements.AddRange(CUIHelper.Button($"tcsk_{kv.Key}", "TCUpgrade.tcskin", "0.2 0.3 0.2 0.6", label, 12, $"{x} 0.15", $"{x + 0.3f} 0.75", $"cui.endtest SENDCMD TCSKINSELECT {kv.Value.ShortName} {page}"));
            col++;
        }
        CUIHelper.AddUi(player, elements);
    }

    private void ShowMenuColor(BasePlayer player, BuildingPrivlidge cup, string id, string grade, string skinId, string color, int page)
    {
        CUIHelper.DestroyUi(player, "TCUpgrade.color");
        var cfg = GetOrCreateConfig(cup);
        var elements = new List<JObject>();
        elements.Add(CUIHelper.Panel("TCUpgrade.color", "OverlayNonScaled", "0 0 0 0.8", "0.5 0.5", "0.5 0.5", "-250 -250", "250 250", true));
        elements.Add(CUIHelper.Label("col_title", "TCUpgrade.color", LangHelper.Lang("title2"), 16, "0.05 0.9", "0.95 0.98"));
        elements.AddRange(CUIHelper.Button("col_close", "TCUpgrade.color", "0.9 0.2 0.2 0.5", LangHelper.Lang("CLOSE"), 14, "0.8 0.88", "0.98 0.98", $"cui.endtest SENDCMD CLOSE2 {page}"));

        int col = 0, row = 0;
        for (int i = 0; i < TCUpgradeHelpers.Colors.Length; i++)
        {
            if (i == 0 && !(TCUpgradeConfig.Config?.EnableMultiColor ?? true)) continue;
            float x = 0.05f + (col % 4) * 0.22f;
            float y = 0.78f - row * 0.22f;
            var sel = cfg.Colour == TCUpgradeHelpers.ColorIndexToUint(i);
            var colorStr = i == 0 ? "0.5 0.5 0.5 0.5" : TCUpgradeHelpers.Colors[i];
            elements.AddRange(CUIHelper.Button($"col_{i}", "TCUpgrade.color", sel ? "1 1 1 0.7" : "0.2 0.3 0.2 0.6", "", 10, $"{x} {y - 0.18f}", $"{x + 0.2f} {y}", $"cui.endtest SENDCMD COLORSELECT {id} {grade} {skinId} {i} {page}"));
            col++;
            if (col >= 4) { col = 0; row++; }
        }
        elements.AddRange(CUIHelper.Button("col_upgrade", "TCUpgrade.color", cfg.Work ? "0.9 0.2 0.2 0.5" : "0.8 1 0.5 0.6", cfg.Work ? LangHelper.Lang("STOP") : LangHelper.Lang("UPGRADE"), 12, "0.35 0.02", "0.65 0.1", cfg.Work ? $"cui.endtest SENDCMD STOP {page}" : $"cui.endtest SENDCMD UPGRADE {id} {grade} {skinId} {page} 1"));
        CUIHelper.AddUi(player, elements);
    }

    public void UpdateBlockedItems(BuildingPrivlidge cupboard)
    {
        if (cupboard?.inventory?.blockedItems == null) return;

        var cfg = TCUpgradeConfig.Config;
        if (cfg?.AllowedItemsConfig == null) return;

        var newBlocked = new HashSet<ItemDefinition>(cupboard.inventory.blockedItems);
        HashSet<ItemDefinition> newAllowed;
        if (cupboard.inventory.onlyAllowedItems == null || cupboard.inventory.onlyAllowedItems.Length == 0)
        {
            newAllowed = new HashSet<ItemDefinition>();
            for (int i = 0; i < ItemManager.itemList.Count; i++)
            {
                var it = ItemManager.itemList[i];
                if (it.category == ItemCategory.Resources || it.category == ItemCategory.Construction)
                    newAllowed.Add(it);
            }
        }
        else
        {
            newAllowed = new HashSet<ItemDefinition>(cupboard.inventory.onlyAllowedItems);
        }

        foreach (var item in ItemManager.itemList)
        {
            if (cfg.AllowedItemsConfig.TryGetValue(item.shortname, out var allowed))
            {
                if (allowed) { newBlocked.Remove(item); newAllowed.Add(item); }
                else { newBlocked.Add(item); newAllowed.Remove(item); }
            }
        }

        cupboard.inventory.blockedItems = newBlocked;
        cupboard.inventory.MarkDirty();
    }
}
