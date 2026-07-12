using System;
using System.Collections.Generic;
using Network;
using UnityEngine;

namespace BetterBackpack;

public class BetterBackpackMod : IHarmonyModHooks
{
    public static BetterBackpackMod Instance { get; private set; }

    private static ConsoleSystem.Command _helpCmd;
    internal readonly Dictionary<ulong, PlayerPrefs> PlayerPrefsByUserId = new();

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        BetterBackpackConfig.LoadConfig();
        var cfg = BetterBackpackConfig.Config;
        UnityEngine.Debug.Log($"[BetterBackpack] Mod loaded. Use /existing and /retrieval in chat to toggle. Reminder every {cfg?.ReminderIntervalMinutes ?? 10f} min.");
        try
        {
            _helpCmd = new ConsoleSystem.Command
            {
                Name = "betterbackpack",
                FullName = "global.betterbackpack",
                Variable = false,
                ServerAdmin = false,
                ServerUser = true,
                Call = CmdHelp
            };
            ConsoleSystem.Index.Server.Dict["global.betterbackpack"] = _helpCmd;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["betterbackpack"] = _helpCmd;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterBackpack] betterbackpack command registration failed: " + ex.Message);
        }

        try
        {
            var harmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.BetterBackpack");
            PlayerInventory_FindAmmo_Patch.Patch(harmony);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterBackpack] Ammo patch failed: " + ex.Message);
        }

        StartReminderLoop();
        ForceMainInventorySyncForAllPlayers();
    }

    private void StartReminderLoop()
    {
        var cfg = BetterBackpackConfig.Config;
        if (cfg?.ChatNotifications != true || cfg?.ReminderEnabled != true || cfg.ReminderIntervalMinutes <= 0) return;
        var invokeHandler = SingletonComponent<InvokeHandler>.Instance;
        if (invokeHandler == null)
        {
            Debug.LogWarning("[BetterBackpack] InvokeHandler not ready; reminder loop not started. Restart server or wait for next load.");
            return;
        }
        var interval = cfg.ReminderIntervalMinutes * 60f;
        // Use instance method so delegate has non-null Target; static delegates can cause NullReferenceException in InvokeAction.GetHashCode (ListHashSet/InvokeHandler).
        InvokeHandler.InvokeRepeating(invokeHandler, ReminderTick, interval, interval);
    }

    private void ReminderTick() => SendReminderToAllPlayers();

    private static void SendReminderToAllPlayers()
    {
        var cfg = BetterBackpackConfig.Config;
        if (cfg?.ChatNotifications != true || cfg?.ReminderEnabled != true || string.IsNullOrWhiteSpace(cfg.ReminderMessage)) return;
        var msg = cfg.ReminderMessage.Trim();
        foreach (var player in BasePlayer.activePlayerList)
        {
            if (player != null && !player.IsDestroyed && !player.IsNpc && player.net?.connection != null)
                player.ChatMessage(msg);
        }
    }

    private static void CmdHelp(ConsoleSystem.Arg arg)
    {
        var msg = "[BetterBackpack] Toggle backpack: /existing (auto-stack on loot), /retrieval (craft from backpack)";
        var player = arg.Player();
        if (player != null && BetterBackpackConfig.Config?.ChatNotifications == true)
            player.ChatMessage(msg);
        else
            arg.ReplyWith(msg);
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        try
        {
            var invokeHandler = SingletonComponent<InvokeHandler>.Instance;
            if (invokeHandler != null)
                InvokeHandler.CancelInvoke(invokeHandler, ReminderTick);
        }
        catch { }
        PlayerPrefsByUserId.Clear();
        try
        {
            ConsoleSystem.Index.Server.Dict?.Remove("global.betterbackpack");
            ConsoleSystem.Index.Server.GlobalDict?.Remove("betterbackpack");
        }
        catch { }
        Instance = null;
    }

    /// <summary>On mod load/reload: sync backpack items to all online players so crafting/reload work.</summary>
    private static void ForceMainInventorySyncForAllPlayers()
    {
        if (Instance == null) return;
        foreach (var player in BasePlayer.activePlayerList)
        {
            if (player == null || player.IsDestroyed || player.IsNpc || player.net?.connection == null) continue;
            var prefs = Instance.GetOrCreatePrefs(player);
            if (prefs == null || !prefs.RetrievalEnabled) continue;
            var backpack = player.inventory?.GetBackpackWithInventory();
            if (backpack?.contents == null || backpack.contents.itemList == null || backpack.contents.itemList.Count == 0) continue;
            ForceMainInventorySync(player);
        }
    }

    /// <summary>Handle /existing and /retrieval chat commands. Returns true if handled.</summary>
    internal bool OnChatCommand(BasePlayer player, string cmd)
    {
        if (player == null) return false;
        var prefs = GetOrCreatePrefs(player);
        if (prefs == null) return false;

        if (cmd == "/existing")
        {
            prefs.ExistingEnabled = !prefs.ExistingEnabled;
            if (BetterBackpackConfig.Config?.ChatNotifications == true)
                player.ChatMessage($"[BetterBackpack] Existing (auto-stack to backpack): {(prefs.ExistingEnabled ? "ON" : "OFF")}");
            return true;
        }
        if (cmd == "/retrieval")
        {
            prefs.RetrievalEnabled = !prefs.RetrievalEnabled;
            if (BetterBackpackConfig.Config?.ChatNotifications == true)
                player.ChatMessage($"[BetterBackpack] Retrieval (craft from backpack): {(prefs.RetrievalEnabled ? "ON" : "OFF")}");
            if (prefs.RetrievalEnabled)
                ForceMainInventorySync(player);
            return true;
        }
        return false;
    }

    internal PlayerPrefs GetOrCreatePrefs(BasePlayer player)
    {
        if (player == null) return null;
        if (!PlayerPrefsByUserId.TryGetValue(player.userID, out var prefs))
        {
            var cfg = BetterBackpackConfig.Config;
            prefs = new PlayerPrefs
            {
                ExistingEnabled = cfg?.ExistingEnabled ?? true,
                RetrievalEnabled = cfg?.RetrievalEnabled ?? true
            };
            PlayerPrefsByUserId[player.userID] = prefs;
        }
        return prefs;
    }

    /// <summary>
    /// Force re-send of main inventory to client. When Retrieval is toggled or mod loads,
    /// items already in the backpack weren't in the client's last main sync - this pushes them.
    /// </summary>
    internal static void ForceMainInventorySync(BasePlayer player)
    {
        if (player?.inventory?.containerMain == null) return;
        player.inventory.containerMain.dirty = true;
        player.inventory.SendUpdatedInventory(PlayerInventory.Type.Main, player.inventory.containerMain);
    }

    public class PlayerPrefs
    {
        public bool ExistingEnabled;
        public bool RetrievalEnabled;
    }
}
