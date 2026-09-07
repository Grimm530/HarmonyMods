using System;
using GrimmCuiHarmony;

namespace InventoryShortcuts
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("INVSHORTCUTS", (_, src) =>
            {
                var a = src.Args; if (a == null || a.Length < 2) return;
                var mod = InventoryShortcutsMod.Instance; if (mod == null) return;
                var player = src.Connection?.player as BasePlayer; if (player == null) return;
                string action = a.GetValue(1)?.ToString()?.ToUpperInvariant();
                if (action == "GRIDCLOSE") { mod.DestroyGridOverlay(player); return; }
                string cmd = action switch
                {
                    "OUTPOST" => "/outpost", "PLAYERS" => "/tp",
                    "KITS" => "/kits", "SHOP" => "/s", "SKINS" => "/skinshop", "VEHICLES" => "/vehicles", _ => null
                };
                if (cmd == null) return;
                try { player.EndLooting(); } catch { }
                player.SendConsoleCommand("chat.say", cmd);
            });
        }
    }
}

