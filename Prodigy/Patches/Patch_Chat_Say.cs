using System;
using ConVar;
using HarmonyLib;

namespace Prodigy.Patches;

/// <summary>
/// Intercepts chat so "/prod" (or "/prodigy") runs the prodigy command on the server.
/// Allows keybind: bind p "chat.say /prod"
/// </summary>
[HarmonyPatch(typeof(Chat), nameof(Chat.say))]
public static class Patch_Chat_Say
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null || arg.Connection == null) return true;
        var msg = arg.GetString(0, "text")?.Trim();
        if (string.IsNullOrEmpty(msg)) return true;
        // Resolve player (connection.player can be null e.g. when spectating)
        var player = arg.Connection.player as BasePlayer ?? BasePlayer.FindByID(arg.Connection.userid);
        if (player == null) return true;

        var mod = ProdigyMod.Instance;
        if (mod == null) return true;

        var lower = msg.ToLowerInvariant().Trim();
        bool isProd = lower == "/prod" || lower.StartsWith("/prod ") || lower == "prod" || lower.StartsWith("prod ");
        bool isProdigy = lower == "/prodigy" || lower.StartsWith("/prodigy ") || lower == "prodigy" || lower.StartsWith("prodigy ");
        if (!isProd && !isProdigy) return true;
        // Parse: "/prod reset", "prod components" -> args after first word
        var parts = msg.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
        for (int i = 1; i < parts.Length; i++) args[i - 1] = parts[i];

        mod.RunProdigyCommand(player, args);
        return false; // suppress chat message
    }
}
