using HarmonyLib;

namespace InventoryShortcuts.Patches;

/// <summary>
/// Intercepts cui.endtest when INVSHORTCUTS is used. Handles QUEST, SKILLS, OUTPOST, PLAYERS, KITS, SHOP, SKINS.
/// Runs chat.say as the player so Oxide plugins receive the command. Closes loot/inventory when possible.
/// </summary>
[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Cui_Endtest_Patch
{
    private static string GetCommand(string action)
    {
        return action switch
        {
            "QUEST" => "/quest",
            "SKILLS" => "/st",
            "OUTPOST" => "/outpost",
            "PLAYERS" => "/tp",
            "KITS" => "/kits",
            "SHOP" => "/s",
            "SKINS" => "/skinshop",
            "VEHICLES" => "/vehicles",
            _ => null
        };
    }

    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg args)
    {
        var a = args?.Args;
        if (a == null || a.Length < 2) return true;
        if (a[0] != "INVSHORTCUTS") return true;

        var mod = InventoryShortcutsMod.Instance;
        if (mod == null) return true;

        var player = args.Connection?.player as BasePlayer;
        if (player == null || player.IsDestroyed || !player.IsConnected) return true;

        string action = a[1].ToString().ToUpperInvariant();
        if (action == "GRIDCLOSE")
        {
            mod.DestroyGridOverlay(player);
            return false;
        }

        string cmd = GetCommand(action);
        if (cmd == null) return true;

        // Close loot/inventory so the target UI can open cleanly (e.g. Quests, Skills, Shop, Kits, etc.)
        try { player.EndLooting(); } catch { /* ignore */ }

        // Send to client so it runs through normal chat pipeline (Oxide plugins receive it)
        player.SendConsoleCommand("chat.say", cmd);
        return false;
    }
}
