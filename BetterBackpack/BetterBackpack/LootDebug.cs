using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// Short-lived loot trace for one (or few) Steam IDs. Hot path is a single bool check.
/// Auto-disables after the configured minutes and writes Loot Debug = false to config.
/// </summary>
internal static class LootDebug
{
    internal static volatile bool IsActive;
    private static HashSet<ulong> _steamIds = new HashSet<ulong>();
    private static bool _watchAll;
    private static float _untilRealtime;
    private static bool _expireStarted;

    internal static void ApplyFromConfig()
    {
        _expireStarted = false;
        IsActive = false;
        _steamIds = new HashSet<ulong>();
        _watchAll = false;
        _untilRealtime = 0f;

        var cfg = BetterBackpackConfig.Config;
        if (cfg == null || !cfg.LootDebug)
            return;

        ParseSteamIds(cfg.LootDebugSteamIds, _steamIds);
        _watchAll = _steamIds.Count == 0;

        IsActive = true;
        var mins = cfg.LootDebugDurationMinutes;
        _untilRealtime = mins > 0 ? Time.realtimeSinceStartup + mins * 60f : 0f;
        var until = _untilRealtime > 0 ? $"{mins:0.#} min (auto-off)" : "until config false + reload";
        var who = _watchAll ? "ALL players" : string.Join(", ", _steamIds);
        Debug.Log($"[BetterBackpack:Loot] ON for {who} — {until}. Grep logs for [BetterBackpack:Loot]");
        _nextStatusRealtime = Time.realtimeSinceStartup + 60f;
        LogWatchedOnlineStatus();
    }

    private static float _nextStatusRealtime;

    internal static void Tick()
    {
        if (!IsActive)
            return;
        if (_untilRealtime > 0f && Time.realtimeSinceStartup >= _untilRealtime)
        {
            Expire("duration elapsed");
            return;
        }
        if (Time.realtimeSinceStartup >= _nextStatusRealtime)
        {
            _nextStatusRealtime = Time.realtimeSinceStartup + 60f;
            LogWatchedOnlineStatus();
        }
    }

    internal static void Stop(string reason)
    {
        if (!IsActive)
            return;
        IsActive = false;
        Debug.Log($"[BetterBackpack:Loot] OFF ({reason}).");
    }

    internal static bool ShouldLog(BasePlayer player)
    {
        if (!IsActive || player == null || player.IsNpc)
            return false;
        if (_watchAll)
            return true;
        return _steamIds.Contains((ulong)player.userID);
    }

    internal static void Log(BasePlayer player, string message)
    {
        if (!ShouldLog(player))
            return;
        var name = player.displayName ?? "?";
        Debug.Log($"[BetterBackpack:Loot] {name} ({(ulong)player.userID}) {message}");
    }

    internal static void LogRaw(string message)
    {
        if (!IsActive)
            return;
        Debug.Log("[BetterBackpack:Loot] " + message);
    }

    internal static string ItemDesc(Item item)
    {
        if (item == null)
            return "null";
        var name = item.info != null ? item.info.shortname : "?";
        var valid = item.IsValid() ? "valid" : "INVALID";
        return $"{name} x{item.amount} uid={item.uid.Value} {valid}";
    }

    internal static string ContainerDesc(ItemContainer container, BasePlayer player = null)
    {
        if (container == null)
            return "world/none";

        var inv = player?.inventory ?? container.playerOwner?.inventory;
        if (inv != null)
        {
            if (container == inv.containerMain)
                return SlotSnap("main", container);
            if (container == inv.containerBelt)
                return SlotSnap("belt", container);
            if (container == inv.containerWear)
                return SlotSnap("wear", container);
            var bag = inv.GetBackpackWithInventory();
            if (bag != null && container == bag.contents)
                return SlotSnap("backpack", container);
        }

        if (container.parent != null && container.parent.IsBackpack())
            return SlotSnap("backpack", container);

        var entity = container.entityOwner;
        if (entity != null)
            return $"loot:{entity.ShortPrefabName} {SlotSnap("slots", container)}";

        return SlotSnap("container", container);
    }

    internal static string InvSnap(BasePlayer player)
    {
        if (player?.inventory == null)
            return "inv=null";
        var inv = player.inventory;
        var bag = inv.GetBackpackWithInventory()?.contents;
        var prefs = BetterBackpackMod.Instance?.GetOrCreatePrefs(player);
        var existing = prefs != null && prefs.ExistingEnabled;
        var retrieval = prefs != null && prefs.RetrievalEnabled;
        return $"{SlotSnap("main", inv.containerMain)} {SlotSnap("belt", inv.containerBelt)} {SlotSnap("bag", bag)} existing={(existing ? "on" : "off")} retrieval={(retrieval ? "on" : "off")}";
    }

    internal static void DumpInventory(BasePlayer player, string reason)
    {
        if (!ShouldLog(player) || player.inventory == null)
            return;

        var inv = player.inventory;
        Log(player, $"DUMP {reason} | {InvSnap(player)}");
        Log(player, "  main: " + ContentsList(inv.containerMain));
        Log(player, "  belt: " + ContentsList(inv.containerBelt));
        Log(player, "  wear: " + ContentsList(inv.containerWear, includeChildContainers: false));

        var bag = inv.GetBackpackWithInventory();
        if (bag == null)
        {
            Log(player, "  worn-backpack: none");
            return;
        }

        var dropWhen = false;
        var mod = bag.info != null ? bag.info.GetComponent<ItemModBackpack>() : null;
        if (mod != null)
            dropWhen = mod.DropWhenDowned;
        Log(player, $"  worn-backpack: {bag.info?.shortname} uid={bag.uid.Value} DropWhenDowned={dropWhen} | {ContentsList(bag.contents)}");
    }

    internal static string ContentsList(ItemContainer container, bool includeChildContainers = true)
    {
        if (container?.itemList == null || container.itemList.Count == 0)
            return "(empty)";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < container.itemList.Count; i++)
        {
            var item = container.itemList[i];
            if (item?.info == null) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(item.info.shortname).Append('x').Append(item.amount);
            if (includeChildContainers && item.contents?.itemList != null && item.contents.itemList.Count > 0)
                sb.Append('[').Append(ContentsList(item.contents)).Append(']');
            if (sb.Length > 1800)
            {
                sb.Append(", ...");
                break;
            }
        }
        return sb.ToString();
    }

    internal static bool IsPlayerBackpack(ItemContainer container, BasePlayer player)
    {
        if (container == null || player?.inventory == null)
            return false;
        var bag = player.inventory.GetBackpackWithInventory();
        return bag != null && container == bag.contents;
    }

    internal static bool IsExternalLoot(ItemContainer parent, BasePlayer player)
    {
        var inv = player?.inventory;
        if (inv == null)
            return true;
        if (parent == null)
            return false;
        if (parent == inv.containerMain || parent == inv.containerBelt || parent == inv.containerWear)
            return false;
        if (Item_MoveToContainer_Patch.IsPlayerManagedBackpackContainer(parent, player))
            return false;
        return true;
    }

    internal static BasePlayer ResolvePlayer(ItemContainer dest, BasePlayer sourcePlayer, Item item)
    {
        if (sourcePlayer != null && ShouldLog(sourcePlayer))
            return sourcePlayer;
        var fromDest = dest?.playerOwner;
        if (fromDest != null)
            return fromDest;
        var wear = dest?.parent?.parent?.playerOwner;
        if (wear != null)
            return wear;
        return item?.GetOwnerPlayer();
    }

    private static string SlotSnap(string name, ItemContainer container)
    {
        if (container == null)
            return $"{name}=none";
        var n = container.itemList != null ? container.itemList.Count : 0;
        var cap = container.capacity;
        var full = container.IsFull() ? " FULL" : "";
        return $"{name}={n}/{cap}{full}";
    }

    internal static void LogPlayerSpawn(BasePlayer player)
    {
        if (!ShouldLog(player))
            return;
        DumpInventory(player, "spawn/PlayerInit");
    }

    private static void LogWatchedOnlineStatus()
    {
        if (!IsActive)
            return;

        var remaining = "";
        if (_untilRealtime > 0f)
        {
            var minsLeft = Math.Max(0f, (_untilRealtime - Time.realtimeSinceStartup) / 60f);
            remaining = $", ~{minsLeft:0.0} min left";
        }

        if (_watchAll)
        {
            var n = 0;
            var list = BasePlayer.activePlayerList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p != null && !p.IsNpc && p.IsConnected)
                        n++;
                }
            }
            Debug.Log($"[BetterBackpack:Loot] watching ALL players ({n} online{remaining})");
            return;
        }

        if (_steamIds.Count == 0)
            return;
        foreach (var id in _steamIds)
        {
            var player = BasePlayer.FindByID(id);
            if (player != null && player.IsConnected)
                Debug.Log($"[BetterBackpack:Loot] watched player ONLINE: {player.displayName} ({id}){remaining}");
            else
                Debug.Log($"[BetterBackpack:Loot] watched player NOT connected: {id}{remaining}");
        }
    }

    private static void ParseSteamIds(List<string> raw, HashSet<ulong> dest)
    {
        if (raw == null)
            return;
        for (int i = 0; i < raw.Count; i++)
        {
            var s = raw[i];
            if (string.IsNullOrWhiteSpace(s))
                continue;
            if (ulong.TryParse(s.Trim(), out var id) && id != 0)
                dest.Add(id);
        }
    }

    private static void Expire(string reason)
    {
        if (_expireStarted)
            return;
        _expireStarted = true;
        IsActive = false;
        try
        {
            if (BetterBackpackConfig.Config != null)
            {
                BetterBackpackConfig.Config.LootDebug = false;
                BetterBackpackConfig.SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterBackpack:Loot] Failed to save auto-off: " + ex.Message);
        }
        Debug.Log($"[BetterBackpack:Loot] OFF ({reason}). Config Loot Debug set to false.");
    }
}
