using System;
using System.Collections.Generic;

namespace GrimmCuiHarmony
{
    /// <summary>Common CUI registration patterns used across Grimm Harmony mods.</summary>
    public static class GrimmCuiPatterns
    {
        /// <summary>Bridge original console commands in JSON to cui.endtest MARKER cmd …</summary>
        public static GrimmCuiJsonRewriter CreateCommandBridgeRewriter(string marker, Func<IReadOnlyList<string>> getCommands)
        {
            return json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("\"command\":", StringComparison.Ordinal) < 0)
                    return json;

                var cmds = getCommands?.Invoke();
                if (cmds == null || cmds.Count == 0) return json;

                for (int i = 0; i < cmds.Count; i++)
                {
                    string cmd = cmds[i];
                    if (string.IsNullOrEmpty(cmd)) continue;

                    // Compact JSON (RustCui) and spaced JSON ("command": "…").
                    string withArgs = "\"command\":\"" + cmd + " ";
                    string alone = "\"command\":\"" + cmd + "\"";
                    string spacedWithArgs = "\"command\": \"" + cmd + " ";
                    string spacedAlone = "\"command\": \"" + cmd + "\"";
                    string bridgeArgs = "\"command\":\"cui.endtest " + marker + " " + cmd + " ";
                    string bridgeAlone = "\"command\":\"cui.endtest " + marker + " " + cmd + "\"";
                    string bridgeSpacedArgs = "\"command\": \"cui.endtest " + marker + " " + cmd + " ";
                    string bridgeSpacedAlone = "\"command\": \"cui.endtest " + marker + " " + cmd + "\"";

                    // Prefer exact end-quote / space-after-cmd matches so short names (e.g. "st")
                    // are not applied as substrings of longer commands already rewritten.
                    if (json.IndexOf(withArgs, StringComparison.Ordinal) >= 0)
                        json = json.Replace(withArgs, bridgeArgs);
                    if (json.IndexOf(alone, StringComparison.Ordinal) >= 0)
                        json = json.Replace(alone, bridgeAlone);
                    if (json.IndexOf(spacedWithArgs, StringComparison.Ordinal) >= 0)
                        json = json.Replace(spacedWithArgs, bridgeSpacedArgs);
                    if (json.IndexOf(spacedAlone, StringComparison.Ordinal) >= 0)
                        json = json.Replace(spacedAlone, bridgeSpacedAlone);
                }

                return json;
            };
        }

        public static GrimmCuiJsonRewriter CreateSimpleReplaceRewriter(string from, string to)
        {
            return json => string.IsNullOrEmpty(json) ? json : json.Replace(from, to);
        }

        public static void RegisterConsoleRouter(string marker, int commandArgIndex = 1)
        {
            GrimmCui.RegisterEndtest(marker, (player, src) =>
            {
                if (src.Args == null || src.Args.Length <= commandArgIndex) return;
                string cmdName = src.GetString(commandArgIndex) ?? string.Empty;
                if (!GrimmCui.InvokeRegisteredConsoleCommand(cmdName, src, commandArgIndex + 1))
                    UnityEngine.Debug.LogWarning($"[0GrimmCUI] {marker}: command not registered: {cmdName}");
            });
        }

        public static void RegisterRebuiltCommand(string marker, string commandPrefix, Action<ConsoleSystem.Arg> handler, int argStartIndex = 1)
        {
            GrimmCui.RegisterEndtest(marker, (player, src) =>
                GrimmCui.InvokeRebuiltCommand(commandPrefix, src, handler, argStartIndex));
        }

        /// <summary>
        /// Bridge Chaos <see cref="Ext.Chaos.UIFramework.CommandCallbackHandler"/> commands
        /// (e.g. adminmenu.callback) through cui.endtest for ConsoleGen clients.
        /// </summary>
        public static GrimmCuiJsonRewriter CreateChaosCallbackRewriter(string marker, string callbackCommandPrefix)
        {
            if (string.IsNullOrEmpty(callbackCommandPrefix)) return json => json;

            string withArgs = "\"command\":\"" + callbackCommandPrefix + " ";
            string alone = "\"command\":\"" + callbackCommandPrefix + "\"";
            string spacedArgs = "\"command\": \"" + callbackCommandPrefix + " ";
            string spacedAlone = "\"command\": \"" + callbackCommandPrefix + "\"";
            string bridgeArgs = "\"command\":\"cui.endtest " + marker + " " + callbackCommandPrefix + " ";
            string bridgeAlone = "\"command\":\"cui.endtest " + marker + " " + callbackCommandPrefix + "\"";
            string bridgeSpacedArgs = "\"command\": \"cui.endtest " + marker + " " + callbackCommandPrefix + " ";
            string bridgeSpacedAlone = "\"command\": \"cui.endtest " + marker + " " + callbackCommandPrefix + "\"";

            return json =>
            {
                if (string.IsNullOrEmpty(json)) return json;
                json = json.Replace(withArgs, bridgeArgs);
                json = json.Replace(alone, bridgeAlone);
                json = json.Replace(spacedArgs, bridgeSpacedArgs);
                json = json.Replace(spacedAlone, bridgeSpacedAlone);
                return json;
            };
        }
    }
}
