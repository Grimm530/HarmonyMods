using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GrimmCuiHarmony
{
    public delegate void GrimmCuiEndtestHandler(BasePlayer player, ConsoleSystem.Arg sourceArg);
    public delegate void GrimmCuiDragHandler(BasePlayer player, string panelName, Vector3 position, CommunityEntity.DraggablePositionSendType dragType);
    public delegate string GrimmCuiJsonRewriter(string json);

    /// <summary>
    /// Central CUI command + drag router. One Harmony patch dispatches to registered mod handlers.
    /// Registries live in AppDomain (BCL Action/Func) so consumer Register* calls from a
    /// pre-reload GrimmCUI type still populate the store the live endtest patch reads.
    /// </summary>
    public static class GrimmCui
    {
        public const string AppDomainApiKey = "GrimmCui_ApiType";
        public const string AppDomainGenerationKey = "GrimmCui_Generation";
        public const string AppDomainReadyCallbacksKey = "GrimmCui_ReadyCallbacks";
        public const string AppDomainEndtestKey = "GrimmCui_EndtestHandlers";
        public const string AppDomainEndtestPrefixKey = "GrimmCui_EndtestPrefixes";
        public const string AppDomainRewritersKey = "GrimmCui_JsonRewriters";
        public const string AppDomainDragPrefixKey = "GrimmCui_DragPrefixes";
        public const string AppDomainDragExactKey = "GrimmCui_DragExact";

        public static int Generation { get; private set; }

        internal static void ResetRegistrations()
        {
            GetEndtestMap().Clear();
            GetEndtestPrefixes().Clear();
            GetRewriters().Clear();
            GetDragPrefixes().Clear();
            GetDragExact().Clear();
        }

        public static void RegisterEndtest(string marker, GrimmCuiEndtestHandler handler)
        {
            if (string.IsNullOrWhiteSpace(marker) || handler == null) return;
            // Wrap as BCL Action so AppDomain storage is shared across Cecil-renamed GrimmCUI loads.
            Action<BasePlayer, ConsoleSystem.Arg> action = (player, arg) => handler(player, arg);
            GetEndtestMap()[marker.Trim()] = action;
        }

        public static void RegisterEndtestPrefix(string prefix, GrimmCuiEndtestHandler handler)
        {
            if (string.IsNullOrWhiteSpace(prefix) || handler == null) return;
            Action<BasePlayer, ConsoleSystem.Arg> action = (player, arg) => handler(player, arg);
            GetEndtestPrefixes().Add(Tuple.Create(prefix, action));
        }

        public static void RegisterJsonRewriter(GrimmCuiJsonRewriter rewriter)
        {
            if (rewriter == null) return;
            Func<string, string> func = json => rewriter(json);
            GetRewriters().Add(func);
        }

        public static void RegisterDragPrefix(string panelPrefix, GrimmCuiDragHandler handler)
        {
            if (string.IsNullOrWhiteSpace(panelPrefix) || handler == null) return;
            Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType> action =
                (player, name, pos, type) => handler(player, name, pos, type);
            GetDragPrefixes().Add(Tuple.Create(panelPrefix, action));
        }

        public static void RegisterDragExact(string panelName, GrimmCuiDragHandler handler)
        {
            if (string.IsNullOrWhiteSpace(panelName) || handler == null) return;
            Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType> action =
                (player, name, pos, type) => handler(player, name, pos, type);
            GetDragExact()[panelName] = action;
        }

        public static void RegisterReadyCallback(Action callback) => GrimmCUIMod.RegisterReadyCallback(callback);

        public static string ApplyRewrites(string json)
        {
            var rewriters = GetRewriters();
            if (string.IsNullOrEmpty(json) || rewriters.Count == 0) return json;
            for (int i = 0; i < rewriters.Count; i++)
            {
                try { json = rewriters[i](json) ?? json; }
                catch (Exception ex) { Debug.LogWarning("[0GrimmCUI] JsonRewriter: " + ex.Message); }
            }
            return json;
        }

        internal static bool TryRouteEndtest(ConsoleSystem.Arg args)
        {
            if (args?.Args == null || args.Args.Length < 1) return false;

            string marker = args.GetString(0) ?? string.Empty;
            if (string.IsNullOrEmpty(marker)) return false;

            BasePlayer player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return true;

            var handlers = GetEndtestMap();
            if (handlers.TryGetValue(marker, out Action<BasePlayer, ConsoleSystem.Arg> handler))
            {
                try { handler(player, args); }
                catch (Exception ex) { Debug.LogWarning($"[0GrimmCUI] endtest {marker}: " + ex.Message); }
                return true;
            }

            var prefixes = GetEndtestPrefixes();
            for (int i = 0; i < prefixes.Count; i++)
            {
                Tuple<string, Action<BasePlayer, ConsoleSystem.Arg>> entry = prefixes[i];
                if (entry == null || string.IsNullOrEmpty(entry.Item1) || entry.Item2 == null) continue;
                if (!marker.StartsWith(entry.Item1, StringComparison.OrdinalIgnoreCase)) continue;
                try { entry.Item2(player, args); }
                catch (Exception ex) { Debug.LogWarning($"[0GrimmCUI] endtest prefix {entry.Item1}: " + ex.Message); }
                return true;
            }

            return false;
        }

        internal static void RouteDrag(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type)
        {
            if (player == null || string.IsNullOrEmpty(name)) return;

            var exactMap = GetDragExact();
            if (exactMap.TryGetValue(name, out Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType> exact))
            {
                try { exact(player, name, position, type); }
                catch (Exception ex) { Debug.LogWarning("[0GrimmCUI] drag exact: " + ex.Message); }
                return;
            }

            var prefixes = GetDragPrefixes();
            for (int i = 0; i < prefixes.Count; i++)
            {
                Tuple<string, Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType>> entry = prefixes[i];
                if (entry == null || string.IsNullOrEmpty(entry.Item1) || entry.Item2 == null) continue;
                if (!name.StartsWith(entry.Item1, StringComparison.Ordinal)) continue;
                try { entry.Item2(player, name, position, type); }
                catch (Exception ex) { Debug.LogWarning("[0GrimmCUI] drag prefix: " + ex.Message); }
                return;
            }
        }

        internal static void BumpGeneration()
        {
            Generation++;
            try { AppDomain.CurrentDomain.SetData(AppDomainGenerationKey, Generation); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(GrimmCui)); } catch { }
        }

        /// <summary>Build a ConsoleSystem.Arg from endtest payload and invoke a registered global/server command.</summary>
        public static bool InvokeRegisteredConsoleCommand(string cmdName, ConsoleSystem.Arg sourceArg, int argStartIndex = 1)
        {
            if (string.IsNullOrEmpty(cmdName) || sourceArg?.Args == null) return false;

            var sb = new StringBuilder(cmdName);
            for (int i = argStartIndex; i < sourceArg.Args.Length; i++)
            {
                sb.Append(' ');
                string s = sourceArg.GetString(i) ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (sourceArg?.Connection != null)
                    opt = opt.FromConnection(sourceArg.Connection);

                var uiArg = new ConsoleSystem.Arg(opt, sb.ToString());
                string key = cmdName.Contains(".") ? cmdName : "global." + cmdName;
                if (ConsoleSystem.Index.Server.Dict != null &&
                    ConsoleSystem.Index.Server.Dict.TryGetValue(key, out var cmd) &&
                    cmd?.Call != null)
                {
                    cmd.Call(uiArg);
                    return true;
                }

                if (!cmdName.Contains(".") &&
                    ConsoleSystem.Index.Server.GlobalDict != null &&
                    ConsoleSystem.Index.Server.GlobalDict.TryGetValue(cmdName, out cmd) &&
                    cmd?.Call != null)
                {
                    cmd.Call(uiArg);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[0GrimmCUI] InvokeRegisteredConsoleCommand: " + ex.Message);
            }

            return false;
        }

        /// <summary>Rebuild an compat-style command line (e.g. UI_Kits) from endtest args and invoke handler.</summary>
        public static void InvokeRebuiltCommand(string commandPrefix, ConsoleSystem.Arg sourceArg, Action<ConsoleSystem.Arg> handler, int argStartIndex = 1)
        {
            if (handler == null || string.IsNullOrEmpty(commandPrefix) || sourceArg?.Args == null) return;

            var sb = new StringBuilder(commandPrefix);
            for (int i = argStartIndex; i < sourceArg.Args.Length; i++)
            {
                sb.Append(' ');
                string s = sourceArg.GetString(i) ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (sourceArg?.Connection != null)
                    opt = opt.FromConnection(sourceArg.Connection);
                handler(new ConsoleSystem.Arg(opt, sb.ToString()));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[0GrimmCUI] InvokeRebuiltCommand: " + ex.Message);
            }
        }

        private static Dictionary<string, Action<BasePlayer, ConsoleSystem.Arg>> GetEndtestMap()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainEndtestKey) is Dictionary<string, Action<BasePlayer, ConsoleSystem.Arg>> existing)
                    return existing;
            }
            catch { }

            var created = new Dictionary<string, Action<BasePlayer, ConsoleSystem.Arg>>(StringComparer.OrdinalIgnoreCase);
            try { AppDomain.CurrentDomain.SetData(AppDomainEndtestKey, created); } catch { }
            return created;
        }

        private static List<Tuple<string, Action<BasePlayer, ConsoleSystem.Arg>>> GetEndtestPrefixes()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainEndtestPrefixKey) is List<Tuple<string, Action<BasePlayer, ConsoleSystem.Arg>>> existing)
                    return existing;
            }
            catch { }

            var created = new List<Tuple<string, Action<BasePlayer, ConsoleSystem.Arg>>>();
            try { AppDomain.CurrentDomain.SetData(AppDomainEndtestPrefixKey, created); } catch { }
            return created;
        }

        private static List<Func<string, string>> GetRewriters()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainRewritersKey) is List<Func<string, string>> existing)
                    return existing;
            }
            catch { }

            var created = new List<Func<string, string>>();
            try { AppDomain.CurrentDomain.SetData(AppDomainRewritersKey, created); } catch { }
            return created;
        }

        private static List<Tuple<string, Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType>>> GetDragPrefixes()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainDragPrefixKey) is List<Tuple<string, Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType>>> existing)
                    return existing;
            }
            catch { }

            var created = new List<Tuple<string, Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType>>>();
            try { AppDomain.CurrentDomain.SetData(AppDomainDragPrefixKey, created); } catch { }
            return created;
        }

        private static Dictionary<string, Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType>> GetDragExact()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainDragExactKey) is Dictionary<string, Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType>> existing)
                    return existing;
            }
            catch { }

            var created = new Dictionary<string, Action<BasePlayer, string, Vector3, CommunityEntity.DraggablePositionSendType>>(StringComparer.OrdinalIgnoreCase);
            try { AppDomain.CurrentDomain.SetData(AppDomainDragExactKey, created); } catch { }
            return created;
        }
    }
}
