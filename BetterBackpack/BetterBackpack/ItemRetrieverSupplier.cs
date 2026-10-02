using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using ProtoBuf;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// Worn-backpack supplier for ItemRetriever — same contract as virtual Backpacks retrieve mode.
/// ItemRetriever already walks Flag.Backpack children on wear; we mark those bags unsearchable
/// so this supplier is the only path (gated by the player's Retrieval toggle).
/// </summary>
internal static class ItemRetrieverSupplier
{
    /// <summary>Must match ItemRetriever's UnsearchableItemFlag (bit 25).</summary>
    internal const Item.Flag UnsearchableForItemRetriever = (Item.Flag)(1 << 25);

    private const string SupplierName = "BetterBackpack";

    internal static void Register()
    {
        if (!ItemRetrieverBinder.IsReady)
            return;

        var spec = new Dictionary<string, object>
        {
            ["FindPlayerItems"] = new Action<BasePlayer, Dictionary<string, object>, List<Item>>(FindPlayerItems),
            ["SumPlayerItems"] = new Func<BasePlayer, Dictionary<string, object>, int>(SumPlayerItems),
            ["TakePlayerItems"] = new Func<BasePlayer, Dictionary<string, object>, int, List<Item>, int>(TakePlayerItems),
            ["SerializeForNetwork"] = new Action<BasePlayer, List<ProtoBuf.Item>>(SerializeForNetwork),
        };
        var findAmmo = CreateFindAmmoDelegate();
        if (findAmmo != null)
            spec["FindPlayerAmmo"] = findAmmo;

        ItemRetrieverBinder.CallApi("API_AddSupplier", SupplierName, spec);

        Debug.Log("[BetterBackpack] Registered worn-backpack supplier with ItemRetriever.");
    }

    private static Delegate CreateFindAmmoDelegate()
    {
        var ammoTypes = Type.GetType("Rust.AmmoTypes, Assembly-CSharp");
        if (ammoTypes == null)
        {
            Debug.LogWarning("[BetterBackpack] Rust.AmmoTypes not found; ammo retrieve will use item-id lookups only.");
            return null;
        }

        var method = typeof(ItemRetrieverSupplier).GetMethod(nameof(FindPlayerAmmoBoxed), BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null)
            return null;

        var playerParam = Expression.Parameter(typeof(BasePlayer), "player");
        var ammoParam = Expression.Parameter(ammoTypes, "ammo");
        var collectParam = Expression.Parameter(typeof(List<Item>), "collect");
        var body = Expression.Call(method, playerParam, Expression.Convert(ammoParam, typeof(object)), collectParam);
        var actionType = typeof(Action<,,>).MakeGenericType(typeof(BasePlayer), ammoTypes, typeof(List<Item>));
        return Expression.Lambda(actionType, body, playerParam, ammoParam, collectParam).Compile();
    }

    internal static void Unregister()
    {
        try
        {
            ItemRetrieverBinder.CallApi("API_RemoveSupplier", SupplierName);
        }
        catch { }
    }

    internal static void HideFromItemRetrieverWalk(Item backpack)
    {
        if (backpack == null || !backpack.IsBackpack())
            return;
        backpack.flags |= UnsearchableForItemRetriever;
    }

    internal static void ShowToItemRetrieverWalk(Item backpack)
    {
        if (backpack == null || !backpack.IsBackpack())
            return;
        backpack.flags &= ~UnsearchableForItemRetriever;
    }

    internal static void HideAllOnlineWornBackpacks()
    {
        foreach (var player in BasePlayer.activePlayerList)
            HideWornBackpack(player);
    }

    internal static void ShowAllOnlineWornBackpacks()
    {
        foreach (var player in BasePlayer.activePlayerList)
        {
            var backpack = player?.inventory?.GetBackpackWithInventory();
            ShowToItemRetrieverWalk(backpack);
        }
    }

    internal static void HideWornBackpack(BasePlayer player)
    {
        var backpack = player?.inventory?.GetBackpackWithInventory();
        HideFromItemRetrieverWalk(backpack);
    }

    private static bool CanRetrieve(BasePlayer player)
    {
        if (player == null || player.IsNpc)
            return false;
        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true))
            return false;
        var mod = BetterBackpackMod.Instance;
        if (mod == null)
            return false;
        var prefs = mod.GetOrCreatePrefs(player);
        return prefs != null && prefs.RetrievalEnabled;
    }

    private static ItemContainer GetContents(BasePlayer player)
    {
        return player?.inventory?.GetBackpackWithInventory()?.contents;
    }

    private static void FindPlayerItems(BasePlayer player, Dictionary<string, object> rawItemQuery, List<Item> collect)
    {
        if (!CanRetrieve(player) || collect == null)
            return;
        var contents = GetContents(player);
        if (contents?.itemList == null)
            return;

        var query = Query.Parse(rawItemQuery);
        var list = contents.itemList;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item != null && query.GetUsableAmount(item) > 0)
                collect.Add(item);
        }
    }

    private static void FindPlayerAmmoBoxed(BasePlayer player, object ammoType, List<Item> collect)
    {
        if (!CanRetrieve(player) || collect == null || ammoType == null)
            return;
        var contents = GetContents(player);
        if (contents == null)
            return;
        var findAmmo = typeof(ItemContainer).GetMethod("FindAmmo", new[] { typeof(List<Item>), ammoType.GetType() });
        findAmmo?.Invoke(contents, new[] { collect, ammoType });
    }

    private static int SumPlayerItems(BasePlayer player, Dictionary<string, object> rawItemQuery)
    {
        if (!CanRetrieve(player))
            return 0;
        var contents = GetContents(player);
        if (contents?.itemList == null)
            return 0;

        var query = Query.Parse(rawItemQuery);
        var sum = 0;
        var list = contents.itemList;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item != null)
                sum += query.GetUsableAmount(item);
        }
        return sum;
    }

    private static int TakePlayerItems(BasePlayer player, Dictionary<string, object> rawItemQuery, int amount, List<Item> collect)
    {
        if (!CanRetrieve(player) || amount <= 0)
            return 0;
        var contents = GetContents(player);
        if (contents?.itemList == null)
            return 0;

        var query = Query.Parse(rawItemQuery);
        if (query.ItemId.HasValue && query.IsItemIdOnly)
            return contents.Take(collect, query.ItemId.Value, amount);

        var taken = 0;
        var list = contents.itemList;
        for (int i = list.Count - 1; i >= 0 && taken < amount; i--)
        {
            var item = list[i];
            if (item == null)
                continue;
            var usable = query.GetUsableAmount(item);
            if (usable <= 0)
                continue;

            var need = Math.Min(amount - taken, usable);
            if (item.amount > need)
            {
                var split = item.SplitItem(need);
                if (split == null)
                    continue;
                if (collect != null)
                    collect.Add(split);
                else
                    split.Remove();
                taken += need;
            }
            else
            {
                taken += item.amount;
                item.RemoveFromContainer();
                if (collect != null)
                    collect.Add(item);
                else
                    item.Remove();
            }
        }

        return taken;
    }

    private static void SerializeForNetwork(BasePlayer player, List<ProtoBuf.Item> saveList)
    {
        if (!CanRetrieve(player) || saveList == null)
            return;
        // Dead-player snapshots (and death bags built from them) must not include
        // worn-bag copies. Real UIDs here also show up as unlootable dupes next to
        // the dropped backpack; ItemRetriever assigns fake UIDs when UID is 0.
        if (player.IsDead())
            return;
        var contents = GetContents(player);
        if (contents?.itemList == null)
            return;

        var list = contents.itemList;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item == null || !item.IsValid())
                continue;
            var data = item.Save();
            if (data == null)
                continue;
            ClearNetworkUids(data);
            saveList.Add(data);
        }
    }

    private static void ClearNetworkUids(ProtoBuf.Item data)
    {
        if (data == null) return;
        data.UID = default;
        var nested = data.contents?.contents;
        if (nested == null) return;
        for (int i = 0; i < nested.Count; i++)
            ClearNetworkUids(nested[i]);
    }

    private struct Query
    {
        public int? ItemId;
        public ulong? SkinId;
        public Item IgnoreItem;
        public bool IsItemIdOnly;

        public static Query Parse(Dictionary<string, object> raw)
        {
            var q = new Query();
            q.ItemId = GetInt(raw, "ItemId");
            q.SkinId = GetUlong(raw, "SkinId");
            if (raw != null && raw.TryGetValue("IgnoreItem", out var ignore) && ignore is Item item)
                q.IgnoreItem = item;
            q.IsItemIdOnly = q.ItemId.HasValue && !q.SkinId.HasValue && q.IgnoreItem == null
                && (raw == null || raw.Count <= 1);
            return q;
        }

        public int GetUsableAmount(Item item)
        {
            if (item?.info == null)
                return 0;
            if (IgnoreItem != null && item == IgnoreItem)
                return 0;
            if (ItemId.HasValue && ItemId.Value != item.info.itemid)
                return 0;
            if (SkinId.HasValue && SkinId.Value != item.skin)
                return 0;
            return item.amount;
        }

        private static int? GetInt(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null)
                return null;
            if (v is int i)
                return i;
            try { return Convert.ToInt32(v); }
            catch { return null; }
        }

        private static ulong? GetUlong(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null)
                return null;
            if (v is ulong u)
                return u;
            try { return Convert.ToUInt64(v); }
            catch { return null; }
        }
    }
}
