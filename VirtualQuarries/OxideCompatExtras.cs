// Extra Oxide shims not present in the CombatClasses OxideCompat copy:
// GetMod / RootPluginManager / Random / Time / Hash / Covalence / AddCovalenceCommand.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Core
{
    public static partial class Interface
    {
        public static OxideMod GetMod() => Oxide;
    }

    public partial class OxideMod
    {
        public Oxide.Core.Plugins.PluginManager RootPluginManager =>
            _rootPluginManager ??= new Oxide.Core.Plugins.PluginManager();
        private Oxide.Core.Plugins.PluginManager _rootPluginManager;

        public T GetLibrary<T>(string name = null) where T : class, new() => new T();
    }

    public static class Random
    {
        private static readonly System.Random Rng = new System.Random();
        public static int Range(int min, int max) => Rng.Next(min, max);
        public static float Range(float min, float max) => (float)(Rng.NextDouble() * (max - min) + min);
    }
}

namespace Oxide.Core.Libraries
{
    public class Time
    {
        public uint GetUnixTimestamp() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public DateTime GetCurrentTime() => DateTime.UtcNow;
    }

    public class Permission : Oxide.Plugins.PermissionLib { }
}

namespace Oxide.Core.Libraries.Covalence
{
    public interface IPlayerManager
    {
        IPlayer FindPlayer(string partialNameOrId);
        IPlayer FindPlayerById(string id);
        IEnumerable<IPlayer> FindPlayers(string partialNameOrId);
        IEnumerable<IPlayer> Connected { get; }
        IEnumerable<IPlayer> All { get; }
    }

    public class RustPlayerManager : IPlayerManager
    {
        private static IEnumerable<BasePlayer> AllBasePlayers()
        {
            var seen = new HashSet<ulong>();
            var active = BasePlayer.activePlayerList;
            if (active != null)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    var p = active[i];
                    if (p != null && seen.Add(p.userID)) yield return p;
                }
            }
            var sleepers = BasePlayer.sleepingPlayerList;
            if (sleepers != null)
            {
                for (int i = 0; i < sleepers.Count; i++)
                {
                    var p = sleepers[i];
                    if (p != null && seen.Add(p.userID)) yield return p;
                }
            }
        }

        public IEnumerable<IPlayer> All
        {
            get { foreach (var p in AllBasePlayers()) yield return new BasePlayerWrapper(p); }
        }

        public IEnumerable<IPlayer> Connected
        {
            get
            {
                var active = BasePlayer.activePlayerList;
                if (active == null) yield break;
                for (int i = 0; i < active.Count; i++)
                {
                    var p = active[i];
                    if (p != null && p.IsConnected) yield return new BasePlayerWrapper(p);
                }
            }
        }

        public IPlayer FindPlayerById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in AllBasePlayers())
                if (p.UserIDString == id) return new BasePlayerWrapper(p);
            return null;
        }

        public IPlayer FindPlayer(string partialNameOrId)
        {
            if (string.IsNullOrEmpty(partialNameOrId)) return null;
            var byId = FindPlayerById(partialNameOrId);
            if (byId != null) return byId;
            BasePlayer match = null;
            int matches = 0;
            foreach (var p in AllBasePlayers())
            {
                if (p.displayName != null && p.displayName.IndexOf(partialNameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    match = p;
                    matches++;
                    if (matches > 1) return null;
                }
            }
            return matches == 1 ? new BasePlayerWrapper(match) : null;
        }

        public IEnumerable<IPlayer> FindPlayers(string partialNameOrId)
        {
            var results = new List<IPlayer>();
            if (string.IsNullOrEmpty(partialNameOrId)) return results;
            foreach (var p in AllBasePlayers())
            {
                if (p.UserIDString == partialNameOrId ||
                    (p.displayName != null && p.displayName.IndexOf(partialNameOrId, StringComparison.OrdinalIgnoreCase) >= 0))
                    results.Add(new BasePlayerWrapper(p));
            }
            return results;
        }
    }

    public class Covalence
    {
        public IPlayerManager Players { get; } = new RustPlayerManager();
    }
}

namespace Oxide.Core.Plugins
{
    public partial class PluginManager
    {
        public Plugin GetPlugin(string name) => Find(name);
    }
}

namespace Oxide.Plugins
{
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

    public abstract partial class RustPlugin
    {
        public readonly Oxide.Core.Libraries.Covalence.Covalence covalence =
            new Oxide.Core.Libraries.Covalence.Covalence();

        protected void AddCovalenceCommand(string command, string callback)
        {
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(callback)) return;
            cmd.AddChatCommand(command, this, callback);
            cmd.AddConsoleCommand(command, this, callback);
        }

        protected void AddCovalenceCommand(string[] commands, string callback)
        {
            if (commands == null) return;
            foreach (var c in commands) AddCovalenceCommand(c, callback);
        }

        public void SendReply(BasePlayer player, string message, params object[] args)
        {
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message)) return;
            if (args != null && args.Length > 0)
            {
                try { message = string.Format(message, args); } catch { }
            }
            Player.Message(player, message);
        }

        public void SendReply(IPlayer user, string message, params object[] args)
        {
            if (user == null) return;
            if (args != null && args.Length > 0)
            {
                try { message = string.Format(message, args); } catch { }
            }
            user.Reply(message);
        }

        public void SendReply(ConsoleSystem.Arg arg, string message, params object[] args)
        {
            var player = arg?.Player();
            if (player != null) { SendReply(player, message, args); return; }
            Puts(message, args);
        }

        public void ResolvePluginReferences()
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in GetType().GetFields(bf))
            {
                if (!typeof(Plugin).IsAssignableFrom(f.FieldType)) continue;
                bool isRef = false;
                foreach (var a in f.GetCustomAttributes(typeof(PluginReferenceAttribute), false))
                { isRef = true; break; }
                if (!isRef) continue;
                try
                {
                    var found = plugins.Find(f.Name);
                    if (found != null) f.SetValue(this, found);
                }
                catch { }
            }
        }

        public void OverlayLanguageFile()
        {
            try
            {
                string path = Path.Combine(Oxide.Core.OxideMod.ResolveServerRoot(), "HarmonyLanguage", (Name ?? "plugin") + ".json");
                if (!File.Exists(path)) return;
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (dict == null || dict.Count == 0) return;
                lang.RegisterMessages(dict, this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[" + (Name ?? "plugin") + "] OverlayLanguageFile: " + ex.Message);
            }
        }

        public void SendWarning(ConsoleSystem.Arg arg, string message)
        {
            PrintWarning(message);
            if (arg?.Player() != null)
                arg.Player().ChatMessage(message);
        }
    }

    public partial class PermissionLib
    {
        public int GetGroupRank(string group) =>
            VirtualQuarriesHarmony.PermissionsBridge.GetGroupData(group)?.Rank ?? 0;

        public bool UserExists(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (VirtualQuarriesHarmony.PermissionsBridge.GetUserData(userId) != null) return true;
            return ulong.TryParse(userId, out _);
        }
    }

    public partial class ServerShim
    {
        public void Broadcast(string msg, string prefix, ulong chatIcon = 0UL)
        {
            if (!string.IsNullOrEmpty(prefix)) msg = prefix + " " + msg;
            var list = BasePlayer.activePlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || !p.IsConnected) continue;
                try { ConsoleNetwork.SendClientCommand(p.net.connection, "chat.add", 2, chatIcon, msg); }
                catch { p.ChatMessage(msg); }
            }
        }
    }

    public partial class PlayerLibrary
    {
        public void Message(BasePlayer player, string message, string prefix, ulong chatIcon = 0UL)
        {
            if (!string.IsNullOrEmpty(prefix)) message = prefix + " " + message;
            Message(player, message, chatIcon);
        }
    }

    public partial class WebRequests
    {
        public void Enqueue(string url, string body, Action<int, string> callback,
            object owner, Oxide.Core.Libraries.RequestMethod method,
            Dictionary<string, string> headers, float timeout)
        {
            Enqueue(url, body, callback, owner, method, headers);
        }
    }
}

namespace Oxide.Core
{
    public partial struct VersionNumber
    {
        public int CompareTo(VersionNumber other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            return Patch.CompareTo(other.Patch);
        }
        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;
    }
}

namespace Oxide.Core.Configuration
{
    public partial class DynamicConfigFile
    {
        public void Save(string filename)
        {
            if (!string.IsNullOrEmpty(filename)) Filename = filename;
            Save();
        }

        public object this[string key1, string key2]
        {
            get => Get(key1, key2);
            set
            {
                var obj = AsObject();
                var nested = obj[key1] as JObject;
                if (nested == null)
                {
                    nested = new JObject();
                    obj[key1] = nested;
                }
                nested[key2] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
            }
        }
    }
}
