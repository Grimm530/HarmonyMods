using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Configuration

        private const string en_ru_compressed_64 = "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";
        
        private Dictionary<string, Dictionary<string, string>> DecompressedLanguageMessages() => JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(Encoding.UTF8.GetString(Facepunch.Utility.Compression.Uncompress(Convert.FromBase64String(en_ru_compressed_64))));

        protected void RegisterLanguageMessages()
        {
            var m = DecompressedLanguageMessages();
            foreach (var mode in GetRaidableModes())
            {
                m["en"][$"Mode{mode}"] = mode;
                m["ru"][$"Mode{mode}"] = mode;
            }
            foreach (var (language, messages) in m)
            {
                lang.RegisterMessages(messages, this, language);
            }
            // User-editable HarmonyLanguage file wins over embedded defaults.
            lang.ReloadHarmonyLanguageOverrides();
        }

        public void TryMessage(BasePlayer player, string key, params object[] args)
        {
            if (player.IsValid() && !waiting.Contains(player.userID))
            {
                ulong userid = player.userID;

                waiting.Add(userid);
                QueueNotification(player, key, args);
                timer.Once(10f, () => waiting.Remove(userid));
            }
        }

        public void Message(BasePlayer player, string key, params object[] args)
        {
            if (player.IsNetworked())
            {
                QueueNotification(player, key, args);
            }
        }

        public void Message(IPlayer user, string key, params object[] args)
        {
            if (user != null)
            {
                user.Reply(mx(key, null, args));
            }
        }

        private void CheckNotifications()
        {
            if (_notifications.Count == 0)
                return;

            for (int i = 0; i < _notifications.Count; i++)
            {
                var (userid, notes) = _notifications.ElementAt(i);

                if (notes.Count > 0)
                {
                    var n = notes[0];
                    int take = 1;
                    int len = n.messageBare.Length;
                    using var sbBare = DisposableBuilder.Get();
                    using var sbFull = DisposableBuilder.Get();
                    sbBare.Append(n.messageBare);
                    sbFull.Append(n.messageFull);

                    for (int j = 1; j < notes.Count; j++)
                    {
                        if (len + 2 + notes[j].messageBare.Length > 140) break;

                        sbBare.AppendLine().Append(notes[j].messageBare);
                        sbFull.AppendLine().Append(notes[j].messageFull);
                        len += 2 + notes[j].messageBare.Length;
                        take++;
                    }

                    n.messageBare = sbBare.ToString();
                    n.messageFull = sbFull.ToString();
                    SendNotification(n);

                    for (int j = 0; j < take; j++)
                    {
                        var obj = notes[0];
                        notes.RemoveAt(0);
                        Pool.Free(ref obj);
                    }
                }

                if (notes.Count == 0)
                {
                    _notifications.Remove(userid);
                    Pool.Free(ref notes);
                    i--;
                }
            }
        }

        private void QueueNotification(IPlayer user, string key, params object[] args)
        {
            if (user.Object is BasePlayer player)
            {
                QueueNotification(player, key, args);
            }
            else user.Reply(mx(key, user.Id, args));
        }

        private void DebugNotify(string format, params object[] args)
        {
            if (config?.EventMessages == null || !config.EventMessages.Debug)
                return;
            Puts("[Notify] " + (args != null && args.Length > 0 ? string.Format(format, args) : format));
        }

        /// <summary>Lang-key path (enter/exit/etc).</summary>
        private void QueueNotification(BasePlayer player, string key, params object[] args)
        {
            if (player == null || !player.IsOnline())
            {
                DebugNotify("skip key={0}: player offline/null", key);
                return;
            }

            string message = m(key, player.UserIDString, args);

            if (string.IsNullOrWhiteSpace(message))
            {
                DebugNotify("skip key={0}: lang returned empty (missing string?)", key);
                return;
            }

            EnqueuePlayerNotification(player, message, mx(key, player.UserIDString, args), key);
        }

        /// <summary>Pre-rendered text path (opened-base announcements already formatted).</summary>
        private void QueueNotificationText(BasePlayer player, string message)
        {
            if (player == null || !player.IsOnline())
            {
                DebugNotify("skip text: player offline/null");
                return;
            }
            if (string.IsNullOrWhiteSpace(message))
            {
                DebugNotify("skip text: empty");
                return;
            }
            EnqueuePlayerNotification(player, message, message, "(raw)");
        }

        private void EnqueuePlayerNotification(BasePlayer player, string chatText, string tipFull, string debugKey)
        {
            string tipBare = rf(tipFull ?? chatText);

            // Immediate chat — assembly BasePlayer.ChatMessage → chat.add 2, 0, msg
            if (config.EventMessages.Message)
                player.ChatMessage(chatText);

            // Send tip immediately (do not wait for CheckNotifications timer).
            if (config.EventMessages.RustStyle != EventMessageSettings.NoRustStyle
                || config.GUIAnnouncement.Enabled || config.UI.AA.Enabled
                || config.EventMessages.NotifyType != -1)
            {
                var n = Pool.Get<Notification>();
                n.player = player;
                n.messageFull = tipFull ?? chatText;
                n.messageBare = tipBare;
                SendNotification(n);
                Pool.Free(ref n);
            }
        }

        private void SendNotification(Notification notification)
        {
            if (notification?.player == null || !notification.player.IsOnline())
                return;

            string tipText = config.EventMessages.StripRustTip ? notification.messageBare : notification.messageFull;
            if (string.IsNullOrWhiteSpace(tipText))
                tipText = notification.messageFull;

            bool messageWasSent = false;
            var p = notification.player;

            if (config.GUIAnnouncement.Enabled && GUIAnnouncements.CanCall())
            {
                GUIAnnouncements?.Call("CreateAnnouncement", tipText, config.GUIAnnouncement.TintColor, config.GUIAnnouncement.TextColor, p);
                messageWasSent = true;
            }

            if (config.UI.AA.Enabled && AdvancedAlerts.CanCall())
            {
                AdvancedAlerts?.Call("SpawnAlert", p, "hook", tipText, config.UI.AA.AnchorMin, config.UI.AA.AnchorMax, config.UI.AA.Time);
                messageWasSent = true;
            }

            if (config.EventMessages.NotifyType != -1 && Notify.CanCall())
            {
                Notify?.Call("SendNotify", p, config.EventMessages.NotifyType, tipText);
                messageWasSent = true;
            }

            // Assembly-RUST BasePlayer.ShowToast → gametip.showtoast_translated(style, token, english, overlay, args)
            if (config.EventMessages.RustStyle != EventMessageSettings.NoRustStyle)
            {
                string toast = ClearColorAndSize(tipText);
                if (string.IsNullOrWhiteSpace(toast))
                    toast = tipText ?? string.Empty;
                var style = config.EventMessages.RustStyle;

                try
                {
                    // Exact Facepunch path (same as TCUpgradeHelpers.ShowToast / BasePlayer.ShowToast).
                    p.ShowToast(style, new Translate.Phrase("raidablebases.tip", toast), false);
                    messageWasSent = true;
                }
                catch
                {
                    try
                    {
                        p.SendConsoleCommand("gametip.showtoast_translated", (int)style, "raidablebases.tip", toast, false, System.Array.Empty<string>());
                        messageWasSent = true;
                    }
                    catch { }
                }

                // TCUpgrade CreateGameTip fallback (plain gametip banner).
                try
                {
                    p.SendConsoleCommand("gametip.hidegametip");
                    p.SendConsoleCommand("gametip.showgametip", toast);
                    messageWasSent = true;
                    var mgr = ServerMgr.Instance;
                    if (mgr != null)
                        mgr.StartCoroutine(HideGameTipDelayed(p, 8f));
                }
                catch { }
            }

            if (!messageWasSent && !config.EventMessages.Message)
                Player.Message(p, tipText, config.Settings.ChatID);
        }

        /// <summary>Strip rich text — toast / showgametip reject color/size tags.</summary>
        private static string ClearColorAndSize(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;
            message = message.Replace("</color>", string.Empty);
            message = message.Replace("</size>", string.Empty);
            while (message.Contains("<color="))
            {
                int index = message.IndexOf("<color=", StringComparison.Ordinal);
                int end = message.IndexOf('>', index);
                if (index < 0 || end < 0) break;
                message = message.Remove(index, end - index + 1);
            }
            while (message.Contains("<size="))
            {
                int index = message.IndexOf("<size=", StringComparison.Ordinal);
                int end = message.IndexOf('>', index);
                if (index < 0 || end < 0) break;
                message = message.Remove(index, end - index + 1);
            }
            return message;
        }

        private static System.Collections.IEnumerator HideGameTipDelayed(BasePlayer player, float seconds)
        {
            yield return CoroutineEx.waitForSeconds(seconds);
            try
            {
                if (player != null && player.IsConnected)
                    player.SendConsoleCommand("gametip.hidegametip");
            }
            catch { }
        }

        public string m(string key, string id = null, params object[] args)
        {
            if (id == null || id == "server_console")
            {
                return mx(key, id, args);
            }

            using var _sb2 = DisposableBuilder.Get();

            if (config.EventMessages.Prefix)
            {
                _sb2.Append(lang.GetMessage("Prefix", this, id));
            }

            string message = lang.GetMessage(key, this, id);

            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            _sb2.Append(message);

            return args.Length > 0 ? string.Format(_sb2.ToString(), args) : _sb2.ToString();
        }

        public string mx(string key, string id = null, params object[] args)
        {
            using var _sb2 = DisposableBuilder.Get();

            string message = lang.GetMessage(key, this, id);

            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            _sb2.Append(id == null || id == "server_console" ? rf(message) : message);

            return args.Length > 0 ? string.Format(_sb2.ToString(), args) : _sb2.ToString();
        }

        public static Regex HtmlTagRegex;

        public static string rf(string source) => source.Contains('>') && HtmlTagRegex != null ? HtmlTagRegex.Replace(source, string.Empty) : source;

        public class Notification : Pool.IPooled
        {
            public BasePlayer player;
            public string messageBare;
            public string messageFull;
            public void Reset()
            {
                player = null;
                messageBare = null;
                messageFull = null;
            }
            public void EnterPool()
            {
                Reset();
            }
            public void LeavePool()
            {
            }
        }

        private Dictionary<ulong, List<Notification>> _notifications = new();

        private List<ulong> waiting = new();

        protected static void Puts(Exception ex)
        {
            UnityEngine.Debug.Log($"[{Name}] {ex}");
        }

        protected static void Puts(string format, params object[] args)
        {
            if (!string.IsNullOrWhiteSpace(format))
            {
                UnityEngine.Debug.Log($"[{Name}] {((args.Length != 0) ? string.Format(format, args) : format)}");
            }
        }

        private Configuration config;

        private static Dictionary<string, List<CustomCostOptions>> DefaultCustomCosts() => new()
        {
            [RaidableMode.Easy] = new() { new(50) },
            [RaidableMode.Medium] = new() { new(100) },
            [RaidableMode.Hard] = new() { new(150) },
            [RaidableMode.Expert] = new() { new(200) },
            [RaidableMode.Nightmare] = new() { new(250) }
        };

        private static AdditionalBaseOptions DefaultBaseOptions() => new()
        {
            Costs = DefaultCostOptions(),
            Options = DefaultPasteOptions(),
        };

        private static List<AdditionalBaseCosts> DefaultCostOptions() => new()
        {
            new() { currencyAmount = 16, currencyToUse = "explosive.satchel" },
            new() { currencyAmount = 256, currencyToUse = "ammo.rifle.explosive" },
        };

        private static List<PasteOption> DefaultPasteOptions() => new()
        {
            new() { Key = "stability", Value = "false" },
            new() { Key = "autoheight", Value = "false" },
            new() { Key = "height", Value = "1.0" },
        };

        private static Dictionary<string, BuildingOptions> DefaultBuildingOptions() => new()
        {
            ["Easy Bases"] = new(RaidableMode.Easy, 0) { NPC = new(15.0) },
            ["Medium Bases"] = new(RaidableMode.Medium, 1) { NPC = new(15.0) },
            ["Hard Bases"] = new(RaidableMode.Hard, 2) { NPC = new(20.0) },
            ["Expert Bases"] = new(RaidableMode.Expert, 3) { NPC = new(25.0) },
            ["Nightmare Bases"] = new(RaidableMode.Nightmare, 4) { NPC = new(30.0) }
        };

        private static List<LootItem> DefaultLoot() => new()
        {
            new("ammo.pistol", 40, 40),
            new("ammo.pistol.fire", 40, 40),
            new("ammo.pistol.hv", 40, 40),
            new("ammo.rifle", 60, 60),
            new("ammo.rifle.explosive", 60, 60),
            new("ammo.rifle.hv", 60, 60),
            new("ammo.rifle.incendiary", 60, 60),
            new("ammo.shotgun", 24, 24),
            new("ammo.shotgun.slug", 40, 40),
            new("surveycharge", 20, 20),
            new("bucket.helmet", 1, 1),
            new("cctv.camera", 1, 1),
            new("coffeecan.helmet", 1, 1),
            new("explosive.timed", 1, 1),
            new("metal.facemask", 1, 1),
            new("metal.plate.torso", 1, 1),
            new("pistol.m92", 1, 1),
            new("rifle.ak", 1, 1),
            new("rifle.bolt", 1, 1),
            new("rifle.lr300", 1, 1),
            new("shotgun.pump", 1, 1),
            new("shotgun.spas12", 1, 1),
            new("smg.2", 1, 1),
            new("smg.mp5", 1, 1),
            new("smg.thompson", 1, 1),
            new("supply.signal", 1, 1),
            new("targeting.computer", 1, 1),
            new("metal.refined", 150, 150),
            new("stones", 7500, 15000),
            new("sulfur", 2500, 7500),
            new("metal.fragments", 2500, 7500),
            new("charcoal", 1000, 5000),
            new("gunpowder", 1000, 3500),
            new("scrap", 100, 150)
        };

        public class DifficultyModeOptions : DifficultyModesInt
        {
            public DifficultyModeOptions() : base(null) { }
            public DifficultyModeOptions(string parent) : base(parent) { }
        }

        public class DifficultyModesInt : ConfigurationExtension<int>
        {
            public DifficultyModesInt() : base(null, RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }
            public DifficultyModesInt(string parent) : base(parent, RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }

            public virtual bool Any() => Dictionary.Exists(x => x.Value > 0);

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => Set(mode, 0));
                    return Any();
                }
                return false;
            }
        }

        public class DifficultyModesDouble : ConfigurationExtension<double>
        {
            public DifficultyModesDouble() : base(null, RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }
            public DifficultyModesDouble(string parent) : base(parent, RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }

            public bool Any() => Dictionary.Exists(x => x.Value > 0);

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => Set(mode, 0));
                    return Any();
                }
                return false;
            }
        }

        public class BuyableWipeTime : ConfigurationExtension<List<BuyableWipeTime.WipeInfo>>
        {
            public BuyableWipeTime() : base(en ? "Enable X Hours After Wipe (0 = immediately)" : "Включить через X часов после вайпа (0 = сразу)", RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }

            public class WipeInfo
            {
                [JsonProperty("Permission Or Group")]
                public string Value;

                [JsonProperty(en ? "Hours After Wipe" : "Часы после вайпа")]
                public double Hours;

                public WipeInfo(string value, double hours) => (Value, Hours) = (value, hours);

                public WipeInfo() { }
            }

            public static List<WipeInfo> Init(string value) => new() { new(value, 0), new("default", 0) };

            public bool Any() => Dictionary.Count > 0 && Dictionary.Values.Exists(x => x.Count > 0);

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => Set(mode, Init($"raidablebases.buyraid.{(en ? mode.ToLower() : mode).Replace(" ", "")}wipetime")));
                    return Any();
                }
                return false;
            }

            public HashSet<string> All()
            {
                HashSet<string> values = new();
                foreach (var wipeList in Dictionary.Values)
                {
                    foreach (var wipeTime in wipeList)
                    {
                        if (!string.IsNullOrWhiteSpace(wipeTime.Value))
                        {
                            values.Add(wipeTime.Value);
                        }
                    }
                }
                return values;
            }

            public double Get(string userid, string mode)
            {
                var wipeTimes = Get(mode);
                if (wipeTimes == null || wipeTimes.Count == 0)
                {
                    return 0;
                }

                double minHours = double.MaxValue;

                foreach (var wipeTime in wipeTimes)
                {
                    if (string.IsNullOrWhiteSpace(wipeTime.Value))
                    {
                        continue;
                    }

                    bool hasAccess = wipeTime.Value.Contains('.')
                        ? userid.HasPermission(wipeTime.Value)
                        : userid.BelongsToGroup(wipeTime.Value);

                    if (hasAccess && wipeTime.Hours < minHours)
                    {
                        minHours = wipeTime.Hours;
                    }
                }

                return minHours == double.MaxValue ? 0 : minHours;
            }
        }

        public class DayLimitSettings
        {
            [JsonProperty(PropertyName = en ? "Monday" : "Понедельник")]
            public bool Monday = true;

            [JsonProperty(PropertyName = en ? "Tuesday" : "Вторник")]
            public bool Tuesday = true;

            [JsonProperty(PropertyName = en ? "Wednesday" : "Среда")]
            public bool Wednesday = true;

            [JsonProperty(PropertyName = en ? "Thursday" : "Четверг")]
            public bool Thursday = true;

            [JsonProperty(PropertyName = en ? "Friday" : "Пятница")]
            public bool Friday = true;

            [JsonProperty(PropertyName = en ? "Saturday" : "Суббота")]
            public bool Saturday = true;

            [JsonProperty(PropertyName = en ? "Sunday" : "Воскресенье")]
            public bool Sunday = true;
        }

        public class BaseLockoutSettings : ConfigurationExtension<double>
        {
            public BaseLockoutSettings() : base(
                en ? "Player Lockouts (0 = ignore)" : "Блокировки Игроков (0 = игнорировать)",
                en ? "Time Between Raids In Minutes (Easy)" : "Время между рейдами в минутах (Легкий)",
                en ? "Time Between Raids In Minutes (Medium)" : "Время между рейдами в минутах (Средний)",
                en ? "Time Between Raids In Minutes (Hard)" : "Время между рейдами в минутах (Тяжело)",
                en ? "Time Between Raids In Minutes (Expert)" : "Время между рейдами в минутах (Эксперт)",
                en ? "Time Between Raids In Minutes (Nightmare)" : "Время между рейдами в минутах (Кошмарный)")
            { }

            [JsonProperty(PropertyName = en ? "Apply Lockouts To PVE" : "Применять блокировки к PVE")]
            public bool PVE = true;

            [JsonProperty(PropertyName = en ? "Apply Lockouts To PVP" : "Применять блокировки к PVP")]
            public bool PVP = true;

            [JsonProperty(PropertyName = en ? "Apply All Lockouts Everytime" : "Применять Все Блокировки при рейде Базы любого уровня")]
            public bool Global;

            [JsonProperty(PropertyName = en ? "Block Clans From Owning More Than One Raid" : "Запретить кланам владеть более чем одним рейдом")]
            public bool BlockClans;

            [JsonProperty(PropertyName = en ? "Block Friends From Owning More Than One Raid" : "Запретить друзьям владеть более чем одним рейдом")]
            public bool BlockFriends;

            [JsonProperty(PropertyName = en ? "Block Teams From Owning More Than One Raid" : "Запретить командам владеть более чем одним рейдом")]
            public bool BlockTeams;

            [JsonProperty(PropertyName = en ? "Block Players From Joining A Clan/Team To Exploit Restrictions" : "Запретить игрокам вступать в Клан/Команду для обхода ограничений")]
            public bool AllyExploit;

            public bool Any() => Dictionary.Exists(x => x.Value > 0);

            public bool IsBlocking() => BlockClans || BlockFriends || BlockTeams;

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => Set(en ? $"Time Between Raids In Minutes ({mode})" : $"Время между рейдами в минутах ({mode})", 0.0));
                    return Any();
                }
                return false;
            }
        }

        public class BaseAmountSettings : DifficultyModesInt
        {
            public BaseAmountSettings() : base(null) { }
            public BaseAmountSettings(string parent) : base(parent) { }

            [JsonProperty(PropertyName = en ? "Allow Max Amount Increase From Difficulties Disabled On A Specific Day Of The Week" : "Увеличивать Максимальное Количество Баз в определённый день недели на количество Баз Отключенных Сложностей")]
            public bool CanMerge;

            internal Dictionary<string, Merge> Merges = new(StringComparer.OrdinalIgnoreCase);

            internal class Merge
            {
                public int amount;
                public string mode;
            }

            public override bool Any() => Dictionary.Count > 0;

            public int Get(RaidableBases instance, RaidableType type, string mode)
            {
                int baseAmount = Get(mode);

                if (CanMerge && !Merges.ContainsKey(mode) && !instance.CanSpawnDifficultyToday(type, mode))
                {
                    List<string> modes = instance.GetRaidableModes().ToList();
                    modes.Reverse();

                    foreach (var otherMode in modes)
                    {
                        if (otherMode != mode && instance.CanSpawnDifficultyToday(type, otherMode))
                        {
                            int otherAmount = Get(otherMode);
                            if (otherAmount <= 0)
                                continue;

                            bool alreadyMerged = false;
                            foreach (var merge in Merges.Values)
                            {
                                if (merge.mode == otherMode)
                                {
                                    alreadyMerged = true;
                                    break;
                                }
                            }

                            if (!alreadyMerged)
                            {
                                Merges[mode] = new Merge { amount = otherAmount, mode = otherMode };
                                break;
                            }
                        }
                    }
                }

                if (baseAmount > 0 && Merges.TryGetValue(mode, out Merge mergeInfo))
                {
                    baseAmount += mergeInfo.amount;
                }

                return baseAmount;
            }
        }

        public class BaseChanceSettings : ConfigurationExtension<decimal>
        {
            public BaseChanceSettings() : base(en ? "Chance To Automatically Spawn Each Difficulty (-1 = ignore)" : "Вероятность автоматического спауна в каждой сложности (-1 = игнорировать)", RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }

            [JsonProperty(PropertyName = en ? "Use Cumulative Probability" : "Использовать кумулятивную систему вероятности (рандома)")]
            public bool Cumulative = true;

            public bool Any() => Dictionary.Exists(x => x.Value != 0m);

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => Set(mode, -1m));
                    return Any();
                }
                return false;
            }

            public string SelectRandomMode(RaidableBases m, List<string> modes)
            {
                if (Cumulative)
                {
                    return SelectCumulativeMode(m, modes);
                }
                return SelectNonCumulativeMode(m, modes);
            }

            // Enables the selection of raidable modes by progressively adding each mode's probability and choosing the first mode where a randomly generated value falls within the accumulated total.
            private string SelectCumulativeMode(RaidableBases m, List<string> modes)
            {
                using var weighted = DisposableList<(string mode, decimal chance)>();
                double totalChance = 0.0;

                foreach (var mode in modes)
                {
                    decimal chance = Get(mode);
                    weighted.Add((mode, chance));
                    totalChance += (double)chance;
                }

                if (totalChance > 0.0)
                {
                    decimal randomValue = (decimal)Core.Random.Range(0.0, totalChance);
                    decimal cumulative = 0m;

                    foreach (var (mode, chance) in weighted)
                    {
                        cumulative += chance;
                        if (randomValue < cumulative)
                        {
                            return mode;
                        }
                    }
                }

                return modes.GetRandom();
            }

            // Selects a raidable mode based on individual probabilities, with each mode having an independent chance of being chosen directly by comparing against a random value.
            private string SelectNonCumulativeMode(RaidableBases m, List<string> modes)
            {
                double totalChance = 0.0;

                foreach (var mode in modes)
                {
                    totalChance += (double)Get(mode);
                }

                if (totalChance > 0.0)
                {
                    decimal randomValue = (decimal)Core.Random.Range(0.0, totalChance);

                    foreach (var mode in m.GetRaidableModes())
                    {
                        if (modes.Contains(mode))
                        {
                            decimal modeChance = Get(mode);
                            if (randomValue <= modeChance)
                            {
                                return mode;
                            }
                        }
                    }
                }

                return modes.GetRandom();
            }
        }

        public class Color1Settings : ConfigurationExtension<string>
        {
            public Color1Settings() : base(en ? "Difficulty Colors (Border)" : "Цвета сложности (Обводка)", RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }

            public bool Any() => Dictionary.Count > 0 && Dictionary.Values.Exists(x => !string.IsNullOrWhiteSpace(x));

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => TryAdd(mode, "000000"));
                    return true;
                }
                return false;
            }

            public new string Get(string mode)
            {
                if (!Dictionary.TryGetValue(mode, out string hex) || string.IsNullOrEmpty(hex))
                {
                    return "#000000";
                }
                return hex.StartsWith('#') ? hex : $"#{hex}";
            }
        }

        public class Color2Settings : ConfigurationExtension<string>
        {
            public Color2Settings() : base(en ? "Difficulty Colors (Inner)" : "Цвета сложности (Заполнение)", RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }

            public bool Any() => Dictionary.Values.Exists(x => !string.IsNullOrWhiteSpace(x));

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => TryAdd(mode, mode switch
                    {
                        RaidableMode.Easy => "00FF00",
                        RaidableMode.Medium => "FFEB04",
                        RaidableMode.Hard => "FF0000",
                        RaidableMode.Expert => "0000FF",
                        RaidableMode.Nightmare => "000000",
                        _ => GetColor(Dictionary.Values)
                    }));
                    return Any();
                }
                return false;
            }

            public new string Get(string mode)
            {
                if (!Dictionary.TryGetValue(mode, out string hex) || string.IsNullOrEmpty(hex))
                {
                    return "#000000";
                }
                return hex.StartsWith('#') ? hex : $"#{hex}";
            }

            public static string GetColor(IEnumerable<string> values, int resolution = 360)
            {
                using var hues = DisposableList<float>();
                foreach (var value in values)
                {
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    Color c = HexToColor(value.TrimStart('#'));
                    Color.RGBToHSV(c, out float hue, out _, out _);
                    hues.Add(hue);
                }

                float candidateHue = 0f;
                if (hues.Count > 0)
                {
                    float bestMinDistance = -1f;
                    for (int i = 0; i < resolution; i++)
                    {
                        float candidate = i / (float)resolution;
                        float minDistance = float.MaxValue;
                        foreach (var hue in hues)
                        {
                            float diff = Math.Abs(candidate - hue);
                            if (diff > 0.5f) diff = 1f - diff;
                            if (diff < minDistance) minDistance = diff;
                        }

                        if (minDistance > bestMinDistance)
                        {
                            bestMinDistance = minDistance;
                            candidateHue = candidate;
                        }
                    }
                }

                Color color = Color.HSVToRGB(candidateHue, S: 0.6f, V: 0.95f);
                return $"{(int)(color.r * 255):X2}{(int)(color.g * 255):X2}{(int)(color.b * 255):X2}";
            }

            private static Color HexToColor(string hex) => new(byte.TryParse(hex[..2], NumberStyles.AllowHexSpecifier, CultureInfo.CurrentCulture, out byte r) ? r / 255f : 1, byte.TryParse(hex[2..4], NumberStyles.AllowHexSpecifier, CultureInfo.CurrentCulture, out byte g) ? g / 255f : 1, byte.TryParse(hex[4..6], NumberStyles.AllowHexSpecifier, CultureInfo.CurrentCulture, out byte b) ? b / 255f : 1);
        }

        public class ManagementMountableSettings
        {
            [JsonProperty(PropertyName = en ? "All Controlled Mounts" : "Весь управляемый транспорт")]
            public bool ControlledMounts;

            [JsonProperty(PropertyName = en ? "All Other Mounts" : "Весь остальной транспорт")]
            public bool Other;

            [JsonProperty(PropertyName = en ? "Attack Helicopters" : "Боевые вертолеты")]
            public bool AttackHelicopters;

            [JsonProperty(PropertyName = en ? "Bikes" : "велосипеды/мотоциклы")]
            public bool Bikes;

            [JsonProperty(PropertyName = en ? "Boats" : "Лодки")]
            public bool Boats;

            [JsonProperty(PropertyName = en ? "Campers" : "Кемперский модуль (на машине)")]
            public bool Campers = true;

            [JsonProperty(PropertyName = en ? "Cars (Basic)" : "Машины (Базовые)")]
            public bool BasicCars;

            [JsonProperty(PropertyName = en ? "Cars (Modular)" : "Машины (Модульные)")]
            public bool ModularCars;

            [JsonProperty(PropertyName = en ? "Chinook" : "Чинук")]
            public bool CH47;

            [JsonProperty(PropertyName = en ? "Drones" : "Дроны")]
            public bool Drones;

            [JsonProperty(PropertyName = en ? "RFExplosives Above Dome (experimental)" : "RFExplosives Above Dome (experimental)")]
            public bool RFExplosivesAboveDome;

            [JsonProperty(PropertyName = en ? "Flying Carpet" : "Flying Carpet (Plugin)")]
            public bool FlyingCarpet;

            [JsonProperty(PropertyName = en ? "Horses" : "Лошади")]
            public bool Hitchable;

            [JsonProperty(PropertyName = en ? "HotAirBalloon" : "Воздушные шары")]
            public bool HotAirBalloon = true;

            [JsonProperty(PropertyName = en ? "Invisible Chair" : "Chair (Invisible)")]
            public bool Invisible = true;

            [JsonProperty(PropertyName = en ? "Jetpacks" : "Jetpacks (Plugin)")]
            public bool Jetpacks = true;

            [JsonProperty(PropertyName = en ? "MiniCopters" : "Миникоптер")]
            public bool MiniCopters;

            [JsonProperty(PropertyName = en ? "Parachutes" : "Парашюты")]
            public bool Parachutes;

            [JsonProperty(PropertyName = en ? "Pianos" : "Пианино")]
            public bool Pianos = true;

            [JsonProperty(PropertyName = en ? "Siege" : "Siege")]
            public bool Siege;

            [JsonProperty(PropertyName = en ? "Scrap Transport Helicopters" : "Грузовой вертолет (Корова)")]
            public bool Scrap;

            [JsonProperty(PropertyName = en ? "Snowmobiles" : "Снегоходы")]
            public bool Snowmobile;

            [JsonProperty(PropertyName = en ? "Tugboats" : "Буксиры")]
            public bool Tugboats;
        }

        public class BuildingOptionsSetupSettings
        {
            [JsonProperty(PropertyName = en ? "Amount Of Entities To Spawn Per Batch" : "Количество объектов для спавна")]
            public int SpawnLimit = 1;

            [JsonProperty(PropertyName = en ? "Amount Of Entities To Despawn Per Batch" : "Количество объектов для удаления")]
            public int DespawnLimit = 1;

            [JsonProperty(PropertyName = en ? "Height Adjustment Applied To This Paste" : "Регулировка высота для данной базы")]
            public float PasteHeightAdjustment;

            [DefaultValue(-1f)]
            [JsonProperty(PropertyName = en ? "Force All Bases To Spawn At Height Level (0 = Water)" : "Статичная высота для всех баз (0 = Уровень Воды)", DefaultValueHandling = DefaultValueHandling.Include)]
            public float ForcedHeightValue = -1f;

            [JsonProperty(PropertyName = en ? "Enabled (Forced Height Level)" : "Использовать функцию настройки высоты для баз")]
            public bool EnableForcedHeight;

            internal float ForcedHeight => EnableForcedHeight ? ForcedHeightValue : -1f;

            [JsonProperty(PropertyName = en ? "Foundations Immune To Damage When Forced Height Is Applied" : "Запретить наносить урон фундаменту (при статичной высоте)")]
            public bool FoundationsImmuneForcedHeight;

            [JsonProperty(PropertyName = en ? "Foundations Immune To Damage" : "Запретить наносить урон фундаменту")]
            public bool FoundationsImmune;

            [JsonProperty(PropertyName = en ? "Kill These Prefabs After Paste" : "Список префабов для удаления после спавна базы", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlockedPrefabs = new();

            [JsonProperty(PropertyName = en ? "Marker Name (Override)" : "Название Маркера (переопределение)")]
            public string MarkerName = "";
        }

        public class PlayerAmountsEventTypeSettings
        {
            [JsonProperty(PropertyName = en ? "Buyable Events" : "Покупаемые События")]
            public int Buyable;

            [JsonProperty(PropertyName = en ? "Maintained Events" : "Поддерживаемых Событий")]
            public int Maintained;

            [JsonProperty(PropertyName = en ? "Manual Events" : "Ручные События")]
            public int Manual;

            [JsonProperty(PropertyName = en ? "Scheduled Events" : "Запланированные События")]
            public int Scheduled;
        }

        protected void ProcessExtensions(ExtOp op)
        {
            RaidableModes.Clear();
            var modes = GetRaidableModes().ToList();

            if (op == ExtOp.Validate || op == ExtOp.Init)
            {
                modes.RemoveAll(x => !Buildings.Profiles.Values.Exists(y => y.Options.Mode.Equals(x, StringComparison.OrdinalIgnoreCase) && y.Options.Enabled));
            }

            bool justCreated = false;
            foreach (var ext in _extensions)
            {
                if (ext == null)
                {
                    continue;
                }
                switch (op)
                {
                    case ExtOp.Validate:
                        if (profileErrors.Count != 0)
                        {
                            break;
                        }
                        if (!ext.Validate(modes))
                        {
                            Puts("Add profile JSON under HarmonyData/RaidableBases/Profiles (Difficulty must match config), or delete that folder and reload to generate defaults.");
                            return;
                        }
                        break;
                    case ExtOp.Invalidate:
                        ext.Invalidate();
                        break;
                    case ExtOp.Init:
                        justCreated |= ext.Create(modes);
                        break;
                }
            }

            if (justCreated && isInitialized)
            {
                SaveConfig();
                //Puts("Successfully updated configuration file with profile information.");
            }
        }

        private static List<IConfigExtension> _extensions = new();

        public enum ExtOp { Init, Validate, Invalidate }

        public interface IConfigExtension
        {
            bool Validate(List<string> modes);
            void Invalidate();
            bool Create(List<string> modes);
            bool ShouldProcessExtension();
        }

        public class ConfigurationExtension<T> : IConfigExtension
        {
            [JsonExtensionData]
            internal IDictionary<string, JToken> _extensionData { get; private set; }

            private Dictionary<string, T> _cache;
            private bool _initialized;
            private string _parent;

            private readonly string _easyKey;
            private readonly string _mediumKey;
            private readonly string _hardKey;
            private readonly string _expertKey;
            private readonly string _nightmareKey;

            public ConfigurationExtension(string parent, string easyKey, string mediumKey, string hardKey, string expertKey, string nightmareKey)
            {
                if (ShouldProcessExtension())
                {
                    _extensions.Add(this);
                }
                _parent = parent;
                _easyKey = easyKey;
                _mediumKey = mediumKey;
                _hardKey = hardKey;
                _expertKey = expertKey;
                _nightmareKey = nightmareKey;
                _extensionData = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            }

            public string GetParentName() => _parent ?? (_parent = base.GetType().Name);

            public T Get(string key) => Dictionary.TryGetValue(key, out T value) ? value : CreateDefault();

            public void Set(string key, T value)
            {
                Dictionary[key] = value;
                _extensionData[key] = JToken.FromObject(value);
            }

            public bool TryAdd(string key, T value)
            {
                if (Dictionary.TryGetValue(key, out var val) && val != null && !(val is string s && string.IsNullOrEmpty(s)))
                {
                    return false;
                }

                Dictionary[key] = value;
                _extensionData[key] = JToken.FromObject(value);
                return true;
            }

            public bool Remove(string key)
            {
                return Dictionary.Remove(key) | _extensionData.Remove(key);
            }

            public bool Validate(List<string> modes)
            {
                static bool IsMatch(string mode, string key) => mode switch
                {
                    RaidableMode.Easy when key.Contains("легких") => true,
                    RaidableMode.Medium when key.Contains("cредний") => true,
                    RaidableMode.Hard when key.Contains("сложных") => true,
                    RaidableMode.Expert when key.Contains("эксперт") => true,
                    RaidableMode.Nightmare when key.Contains("кошмарный") => true,
                    _ => Regex.IsMatch(key, $@"\b{Regex.Escape(mode)}\b", RegexOptions.IgnoreCase),
                };
                foreach (var mode in modes)
                {
                    if (!Dictionary.Keys.Exists(key => IsMatch(mode, key)))
                    {
                        Puts($"Difficulty '{mode}' is missing from '{string.Join(", ", Dictionary.Keys)}' in the configuration section '{GetParentName()}'");
                        return false;
                    }
                }
                foreach (var key in Dictionary.Keys)
                {
                    if (key != RaidableMode.Points && !modes.Exists(mode => IsMatch(mode, key)))
                    {
                        Puts($"Difficulty `{key}` is in the configuration file, but does not exist in any of the profiles.");
                        return false;
                    }
                }
                return true;
            }

            public void Clear()
            {
                _extensionData?.Clear();
                _cache = null;
            }

            public virtual bool ShouldProcessExtension() => true;

            public virtual void Invalidate() => _cache = null;

            public virtual bool Create(List<string> modes) => false;

            protected virtual T CreateDefault() => (T)(object)GetDefaultValue(typeof(T));

            private static object GetDefaultValue(Type type) => type switch
            {
                _ when type == typeof(string) => string.Empty,
                _ when type == typeof(bool) => false,
                _ when type == typeof(byte) => (byte)0,
                _ when type == typeof(char) => (char)0,
                _ when type == typeof(DateTime) => default(DateTime),
                _ when type == typeof(DateTimeOffset) => default(DateTimeOffset),
                _ when type == typeof(decimal) => 0m,
                _ when type == typeof(double) => 0.0,
                _ when type == typeof(Guid) => default(Guid),
                _ when type == typeof(short) => (short)0,
                _ when type == typeof(int) => 0,
                _ when type == typeof(long) => 0L,
                _ when type == typeof(ushort) => (ushort)0,
                _ when type == typeof(uint) => 0u,
                _ when type == typeof(ulong) => 0uL,
                _ when type == typeof(sbyte) => (sbyte)0,
                _ when type == typeof(float) => 0f,
                _ when type == typeof(TimeSpan) => default(TimeSpan),
                _ when type == typeof(NetworkableId) => default(NetworkableId),
                _ => Activator.CreateInstance(type)
            };

            private bool IsJTokenCompatible(JToken j)
            {
                if (j == null)
                {
                    return false;
                }

                Type t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

                return t switch
                {
                    _ when t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) || t == typeof(short) || t == typeof(ushort) => j.Type == JTokenType.Integer,
                    _ when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => j.Type == JTokenType.Float,
                    _ when t == typeof(DateTimeOffset) => j.Type == JTokenType.Integer || j.Type == JTokenType.String,
                    _ when t == typeof(DateTime) => j.Type == JTokenType.Date || j.Type == JTokenType.String,
                    _ when t == typeof(Guid) => j.Type == JTokenType.Guid || j.Type == JTokenType.String,
                    _ when t == typeof(TimeSpan) => j.Type == JTokenType.TimeSpan || j.Type == JTokenType.String,
                    _ when t == typeof(string) || t == typeof(char) => j.Type == JTokenType.String,
                    _ when t == typeof(bool) => j.Type == JTokenType.Boolean,
                    _ when t == typeof(byte) || t == typeof(sbyte) => j.Type == JTokenType.Bytes,
                    _ when t.IsEnum => j.Type == JTokenType.String || j.Type == JTokenType.Integer,
                    _ when typeof(IEnumerable).IsAssignableFrom(t) => j.Type == JTokenType.Array || j.Type == JTokenType.Object,
                    _ => j.Type == JTokenType.Object
                };
            }

            private void EnsureCached()
            {
                _cache = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

                foreach (var extension in _extensionData.ToList())
                {
                    try
                    {
                        _cache[extension.Key] = extension.Value.ToObject<T>();
                    }
                    catch (JsonSerializationException)
                    {
                        //Puts("Obsolete key removed from collection: {0}", extension.Key);
                        _extensionData.Remove(extension.Key);
                    }
                    catch (JsonReaderException ex)
                    {
                        if (!IsJTokenCompatible(extension.Value))
                        {
                            Puts($"[INFO] Key: '{extension.Key}' value ({extension.Value}) is '{extension.Value?.Type}' when expecting '{typeof(T)}'");
                        }
                        Puts("Json Error: Missing commas, unquoted keys, or improperly formatted values: {0}\n{1}", extension.Key, ex);
                    }
                    catch (JsonException ex)
                    {
                        Puts(ex);
                    }
                }
            }

            private void EnsureInitialized()
            {
                _initialized = true;
                if (_extensionData == null)
                {
                    _extensionData = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
                }

                if (_extensionData.Count != 0)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(_easyKey)) _extensionData.TryAdd(_easyKey, JToken.FromObject(CreateDefault()));
                if (!string.IsNullOrEmpty(_mediumKey)) _extensionData.TryAdd(_mediumKey, JToken.FromObject(CreateDefault()));
                if (!string.IsNullOrEmpty(_hardKey)) _extensionData.TryAdd(_hardKey, JToken.FromObject(CreateDefault()));
                if (!string.IsNullOrEmpty(_expertKey)) _extensionData.TryAdd(_expertKey, JToken.FromObject(CreateDefault()));
                if (!string.IsNullOrEmpty(_nightmareKey)) _extensionData.TryAdd(_nightmareKey, JToken.FromObject(CreateDefault()));
            }

            internal Dictionary<string, T> Dictionary
            {
                get
                {
                    if (_cache != null)
                    {
                        return _cache;
                    }

                    if (!_initialized)
                    {
                        EnsureInitialized();
                    }

                    EnsureCached();

                    return _cache;
                }
                set
                {
                    if (value != null)
                    {
                        foreach (var kvp in value)
                        {
                            if (kvp.Value == null) continue;
                            _extensionData[kvp.Key] = JToken.FromObject(kvp.Value);
                        }
                        _cache = null;
                    }
                }
            }

            [OnDeserialized]
            internal void OnDeserializedMethod(StreamingContext context)
            {
                if (_extensionData == null)
                {
                    _extensionData = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
                }

                EnsureCached();
                EnsureInitialized();
            }
        }

        public class ManagementPlayerAmountsSettings : ConfigurationExtension<PlayerAmountsEventTypeSettings>
        {
            public ManagementPlayerAmountsSettings(string easyKey, string mediumKey, string hardKey, string expertKey, string nightmareKey) : base(en ? "Max Amount Of Players Allowed To Enter Each Difficulty (0 = infinite, -1 = none)" : "Максимальное количество участников для каждой сложности (0 = бесконечно, -1 = никого)", easyKey, mediumKey, hardKey, expertKey, nightmareKey) { }

            [JsonProperty(PropertyName = en ? "Bypass For PVP Bases" : "Обход (Bypass) для PVP баз")]
            public bool BypassPVP;

            public int Get(string mode, RaidableType type)
            {
                if (Dictionary.TryGetValue(mode, out var setting) && setting != null)
                {
                    return type switch
                    {
                        RaidableType.Maintained => setting.Maintained,
                        RaidableType.Scheduled => setting.Scheduled,
                        RaidableType.Purchased => setting.Buyable,
                        _ => setting.Manual,
                    };
                }
                return 0;
            }

            public bool Any() => Dictionary.Count > 0 && Dictionary.All(x => x.Value != null);

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => TryAdd(mode, new()));
                    return Any();
                }
                return false;
            }
        }

        public class ManagementDropSettings
        {
            [JsonProperty(PropertyName = "SET", NullValueHandling = NullValueHandling.Ignore)]
            public bool? SET = null;

            [JsonProperty(PropertyName = en ? "Despawn These Dropped Loot Bags When Base Despawns" : "Исчезновение выпавших мешков с добычей при исчезновении базы")]
            public bool DespawnGreyWeaponBags;

            [JsonProperty(PropertyName = en ? "Auto Turrets" : "Автоматические турели")]
            public bool AUTOTURRET;

            [JsonProperty(PropertyName = en ? "Flame Turret" : "Пламенная турель")]
            public bool FLAMETURRET;

            [JsonProperty(PropertyName = en ? "Fog Machine" : "Туманная машина")]
            public bool FOGMACHINE;

            [JsonProperty(PropertyName = en ? "Gun Trap" : "Ловушка с дробовиком (гантрап)")]
            public bool GUNTRAP;

            [JsonProperty(PropertyName = en ? "SAM Site" : "Зенитная установка САМ")]
            public bool SAMSITE;

            public bool CanDespawnGreyWeaponBag(BaseEntity entity) => DespawnGreyWeaponBags && entity.OwnerID == 0 && (entity is AutoTurret or FlameTurret or FogMachine or GunTrap or SamSite);

            public bool Get(BaseEntity entity) => entity switch
            {
                AutoTurret _ => AUTOTURRET,
                FlameTurret _ => FLAMETURRET,
                FogMachine _ => FOGMACHINE,
                GunTrap _ => GUNTRAP,
                SamSite _ => SAMSITE,
                Fridge => true,
                _ => false
            };
        }

        public class ManagementSettingsLocations
        {
            [JsonProperty(PropertyName = "position")]
            public string _position;
            public float radius;
            public ManagementSettingsLocations() { }
            public ManagementSettingsLocations(Vector3 position, float radius)
            {
                (_position, this.radius) = (position.ToString(), radius);
            }
            internal Vector3 position { get { try { return _position.ToVector3(); } catch { Puts("Block Spawns At Positions: {0} is an invalid position in config file.", _position); return default; } } }
        }

        public class ManagementBiomeSettings
        {
            [JsonProperty(PropertyName = en ? "Arctic" : "Arctic")]
            public bool Arctic = true;

            [JsonProperty(PropertyName = en ? "Arid" : "Arid")]
            public bool Arid = true;

            [JsonProperty(PropertyName = en ? "Temperate" : "Temperate")]
            public bool Temperate = true;

            [JsonProperty(PropertyName = en ? "Tundra" : "Tundra")]
            public bool Tundra = true;

            [JsonProperty(PropertyName = en ? "Jungle" : "Jungle")]
            public bool Jungle = true;

            public bool IsBiomeEnabled(int? t, Vector3 a, out TerrainBiome.Enum biome)
            {
                if (!t.HasValue)
                {
                    biome = (TerrainBiome.Enum)0;
                    return true;
                }
                biome = (TerrainBiome.Enum)t.Value;
                return biome switch
                {
                    TerrainBiome.Enum.Arctic => Arctic,
                    TerrainBiome.Enum.Arid => Arid,
                    TerrainBiome.Enum.Temperate => Temperate,
                    TerrainBiome.Enum.Tundra => Tundra,
                    TerrainBiome.Enum.Jungle => Jungle,
                    _ => true
                };
            }
        }

        public class ManagementSettings : ConfigurationExtension<DayLimitSettings>
        {
            [JsonProperty(PropertyName = en ? "Block Grid On Spawns Database Positions" : "Не использовать сетку на позициях Spawns Database (Plugin)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public bool BlockAtSpawnsDatabase;

            [JsonProperty(PropertyName = en ? "Grids To Block Spawns At" : "Сетки для Блокировки Спауна", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlockedGrids = new();

            [JsonProperty(PropertyName = en ? "Blocked Monument Markers (* = everything)" : "Заблокированные маркеры памятников (* = все)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlockedMonumentMarkers = new();

            [JsonProperty(PropertyName = en ? "Block Spawns At Positions" : "Блокировать спаун в позиции", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ManagementSettingsLocations> BlockedPositions = new() { new(Vector3.zero, 200f) };

            [JsonProperty(PropertyName = en ? "Additional Map Prefabs To Block Spawns At" : "Дополнительные префабы для блокировки спауна", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> BlockedPrefabs = new(StringComparer.OrdinalIgnoreCase) { ["test_prefab"] = 150f, ["test_prefab_2"] = 125.25f };

            [JsonProperty(PropertyName = en ? "Eject Mounts" : "Не допускать транспортные средства", NullValueHandling = NullValueHandling.Ignore)]
            public ManagementMountableSettings _Mounts = null;

            [JsonProperty(PropertyName = en ? "Max Amount Of Players Allowed To Enter Each Difficulty (0 = infinite, -1 = none)" : "Максимальное количество участников для каждой сложности (0 = бесконечно, -1 = никого)")]
            public ManagementPlayerAmountsSettings Players = new(RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare);

            [JsonProperty(PropertyName = en ? "Max Amount Allowed To Automatically Spawn Per Difficulty (0 = infinite, -1 = disabled)" : "Максимальное количество Баз для автоматического появления для каждой сложности (0 = бесконечно, -1 = отключено)")]
            public BaseAmountSettings Amounts = new(en ? "Max Amount Allowed To Automatically Spawn Per Difficulty (0 = infinite, -1 = disabled)" : "Максимальное количество Баз для автоматического появления для каждой сложности (0 = бесконечно, -1 = отключено)");

            [JsonProperty(PropertyName = en ? "Chance To Automatically Spawn Each Difficulty (-1 = ignore)" : "Вероятность автоматического спауна в каждой сложности (-1 = игнорировать)")]
            public BaseChanceSettings Chances = new();

            [JsonProperty(PropertyName = en ? "Player Lockouts (0 = ignore)" : "Блокировки Игроков (0 = игнорировать)")]
            public BaseLockoutSettings Lockout = new();

            [JsonProperty(PropertyName = en ? "Additional Containers To Include As Boxes" : "Дополнительные контейнеры для распределения лута, как в ящиках", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Inherit = new();

            [JsonProperty(PropertyName = en ? "Difficulty Colors (Border)" : "Цвета сложности (Обводка)")]
            public Color1Settings Colors1 = new();

            [JsonProperty(PropertyName = en ? "Difficulty Colors (Inner)" : "Цвета сложности (Заполнение)")]
            public Color2Settings Colors2 = new();

            [JsonProperty(PropertyName = en ? "Entities Allowed To Drop Loot" : "Объекты с которых выпадает лут")]
            public ManagementDropSettings DropLoot = new();

            [JsonProperty(PropertyName = en ? "Additional Blocked Colliders" : "Коллайдеры для блокировок", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AdditionalBlockedColliders = new() { "cubes" };

            [JsonProperty(PropertyName = en ? "Allow Teleport" : "Разрешить телепортацию")]
            public bool AllowTeleport;

            [JsonProperty(PropertyName = en ? "Allow Teleport Ignores Respawning" : "Разрешить телепорт игнорировать возрождение")]
            public bool AllowRespawn;

            [JsonProperty(PropertyName = en ? "Allow Cupboard Loot To Drop" : "Разрешить выпадение лута из шкафов")]
            public bool AllowCupboardLoot = true;

            [JsonProperty(PropertyName = en ? "Allow Players To Build" : "Разрешить строить игрокам", NullValueHandling = NullValueHandling.Ignore)]
            public bool? _AllowBuilding = null;

            [JsonProperty(PropertyName = en ? "Allow Players To Build (Exclusions)" : "Разрешить строить игрокам (Исключительные объекты, даже если строить - false)", ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Ignore)]
            public List<string> _AllowedBuildingBlocks = null;

            [JsonProperty(PropertyName = en ? "Allow Players To Use Ladders" : "Разрешить использовать лестницы")]
            public bool AllowLadders = true;

            [JsonProperty(PropertyName = en ? "Allow Players To Upgrade Event Buildings" : "Разрешить улучшение строений")]
            public bool AllowUpgrade;

            [JsonProperty(PropertyName = en ? "Allow Player Bags To Be Lootable At PVP Bases" : "Разрешить лутать чужие рюкзаки на PVP Базах")]
            public bool PlayersLootableInPVP = true;

            [JsonProperty(PropertyName = en ? "Allow Player Bags To Be Lootable At PVE Bases" : "Разрешить лутать чужие рюкзаки на PVE Базах")]
            public bool PlayersLootableInPVE;

            [JsonProperty(PropertyName = en ? "Allow Players To Loot Traps" : "Разрешить лутать ловушки (турелли и т.д)")]
            public bool LootableTraps;

            [JsonProperty(PropertyName = en ? "Allow Npcs To Target Other Npcs" : "Разрешить НПС Нападать на Других НПС")]
            public bool TargetNpcs;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases Inland" : "Спавнить рейд-базы на островах")]
            public bool AllowInland = true;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases On Beaches" : "Спавнить рейд-базы на пляжах")]
            public bool AllowOnBeach = true;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases On Ice Sheets" : "Спавнить рейд-базы ледяных глыбах")]
            public bool AllowOnIceSheets;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases On Roads" : "Спавнить рейд-базы возле дорог")]
            public bool AllowOnRoads = true;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases On Rivers" : "Спавнить рейд-базы возле рек")]
            public bool AllowOnRivers = true;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases On Railroads" : "Спавнить рейд-базы возле ЖД-путей")]
            public bool AllowOnRailroads;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases On Building Topology" : "Разрешить Рейдовые Базы на Застроенной территории")]
            public bool AllowOnBuildingTopology = true;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases On Monument Topology" : "Спавнить рейд-базы на территории монументов (рт)")]
            public bool AllowOnMonumentTopology;

            [JsonProperty(PropertyName = en ? "Allow Raid Bases In Biomes" : "Спавнить рейд-базы по разным биомам", NullValueHandling = NullValueHandling.Ignore)]
            public ManagementBiomeSettings _Biomes = null;

            [JsonProperty(PropertyName = en ? "Amount Of Spawn Position Checks Per Frame (ADVANCED USERS ONLY)" : "Количество Проверок Позиций Спауна за Кадр (ТОЛЬКО ДЛЯ ОПЫТНЫХ ПОЛЬЗОВАТЕЛЕЙ)")]
            public int SpawnChecks = 25;

            [JsonProperty(PropertyName = en ? "Allow Vending Machines To Broadcast" : "Отображать торговые аппараты в базах на карте")]
            public bool AllowBroadcasting;

            [JsonProperty(PropertyName = en ? "Backpacks Can Be Opened At PVE Bases" : "Можно ли открыть рюкзак на PVE Базах")]
            public bool BackpacksOpenPVE = true;

            [JsonProperty(PropertyName = en ? "Backpacks Can Be Opened At PVP Bases" : "Можно ли открыть рюкзак на на PVP Базах")]
            public bool BackpacksOpenPVP = true;

            [JsonProperty(PropertyName = en ? "Rust Backpacks Drop At PVE Bases" : "(Rust) Рюкзаки выпадают на PVE базах")]
            public bool RustBackpacksPVE;

            [JsonProperty(PropertyName = en ? "Rust Backpacks Drop At PVP Bases" : "(Rust) Рюкзаки выпадают на PVP базах")]
            public bool RustBackpacksPVP;

            [JsonProperty(PropertyName = en ? "Backpacks Drop At PVE Bases" : "Рюкзаки выпадают на PVE базах")]
            public bool BackpacksPVE;

            [JsonProperty(PropertyName = en ? "Backpacks Drop At PVP Bases" : "Рюкзаки выпадают на PVP базах")]
            public bool BackpacksPVP;

            [JsonProperty(PropertyName = en ? "Block Custom Loot Plugin" : "Блокировать Custom Loot Plugin")]
            public bool BlockCustomLootNPC;

            [JsonProperty(PropertyName = en ? "Block AlphaLoot Plugin" : "Блокировать AlphaLoot Plugin")]
            public bool BlockAlphaLoot;

            [JsonProperty(PropertyName = en ? "Block BetterLoot Plugin" : "Блокировать BetterLoot Plugin")]
            public bool BlockBetterLoot = true;

            [JsonProperty(PropertyName = en ? "Block Npc Kits Plugin" : "Блокировать Npc Kits Plugin")]
            public bool BlockNpcKits;

            [JsonProperty(PropertyName = en ? "Block Helicopter Damage To Bases" : "Блокировать урон от вертолета по базам")]
            public bool BlockHelicopterDamage;

            [JsonProperty(PropertyName = en ? "Block Mounted Damage To Bases And Players" : "Блокировать урон от транспортных средств по Базам и Игрокам")]
            public bool BlockMounts;

            [JsonProperty(PropertyName = en ? "Block Damage From Siege Weapons To Bases" : "Блокировать урон от осадных орудий по базам")]
            public bool BlockSiegeMounts;

            [JsonProperty(PropertyName = en ? "Block Mini Collision Damage" : "Блокировать урон от столкновений по миникоптеру")]
            public bool MiniCollision;

            [JsonProperty(PropertyName = en ? "Block DoubleJump Plugin" : "Отключить DoubleJump Plugin")]
            public bool NoDoubleJump = true;

            [JsonProperty(PropertyName = en ? "Block RevivePlayer Plugin For PVP Bases" : "Блокировка Плагина RevivePlayer Для Баз PVP")]
            public bool BlockRevivePVP { get; set; }

            [JsonProperty(PropertyName = en ? "Block RevivePlayer Plugin For PVE Bases" : "Блокировка Плагина RevivePlayer Для Баз PVE")]
            public bool BlockRevivePVE { get; set; }

            [JsonProperty(PropertyName = en ? "Block RestoreUponDeath Plugin For PVP Bases" : "Отключить RestoreUponDeath Plugin для PVP баз")]
            public bool BlockRestorePVP;

            [JsonProperty(PropertyName = en ? "Block RestoreUponDeath Plugin For PVE Bases" : "Отключить RestoreUponDeath Plugin для PVE баз")]
            public bool BlockRestorePVE;

            [JsonProperty(PropertyName = en ? "Block LifeSupport Plugin" : "Отключить LifeSupport Plugin")]
            public bool NoLifeSupport = true;

            [JsonProperty(PropertyName = en ? "Block Rewards During Server Restart" : "Блокировать Награды Во Время Перезагрузки Сервера")]
            public bool Restart;

            [JsonProperty(PropertyName = en ? "Bypass Lock Treasure To First Attacker For PVE Bases" : "Обход Блокировки Сокровища для Первого Атакующего на PVE Базах")]
            public bool BypassUseOwnersForPVE;

            [JsonProperty(PropertyName = en ? "Bypass Lock Treasure To First Attacker For PVP Bases" : "Обход Блокировки Сокровища для Первого Атакующего на PVP Базах")]
            public bool BypassUseOwnersForPVP = true;

            [JsonProperty(PropertyName = en ? "Despawn Spawned Mounts" : "Удалять Транспортные Средства при Спавне")]
            public bool DespawnMounts = true;

            [JsonProperty(PropertyName = en ? "Do Not Destroy Player Built Deployables" : "Не Уничтожать Игровые Постройки Игроков")]
            public bool KeepDeployables = true;

            [JsonProperty(PropertyName = en ? "Do Not Destroy Player Built Structures" : "Не Уничтожать Структуры, Построенные Игроками")]
            public bool KeepStructures = true;

            [JsonProperty(PropertyName = en ? "Divide Rewards Among All Raiders" : "Делить Награды Между Всеми Рейдерами")]
            public bool DivideRewards = true;

            [JsonProperty(PropertyName = en ? "Draw Corpse Time (Seconds)" : "Время жизни трупа (Секунды)")]
            public float DrawTime = 300f;

            [JsonProperty(PropertyName = en ? "Destroy Boxes Clipped Too Far Into Terrain" : "Уничтожать ящики заспавненные под землей")]
            public bool ClippedBoxes = true;

            [JsonProperty(PropertyName = en ? "Destroy Turrets Clipped Too Far Into Terrain" : "Уничтожать турели заспавненные под землей")]
            public bool ClippedTurrets = true;

            [JsonProperty(PropertyName = en ? "Eject Sleepers Before Spawning Base" : "Изгнание Спящих Перед Появлением Базы")]
            public bool EjectSleepers = true;

            [JsonProperty(PropertyName = en ? "Eject Scavengers When Raid Is Completed" : "Выкидывать за купол Мародеров После Завершения Рейда")]
            public bool EjectScavengers = true;

            [JsonProperty(PropertyName = en ? "Eject Mountables Before Spawning A Base" : "Выкидывать за купол Транспортные Средства Перед Появлением Базы")]
            public bool EjectMountables;

            [JsonProperty(PropertyName = en ? "Kill Deployables Before Spawning A Base" : "Уничтожение Построек Перед Появлением Базы")]
            public bool KillDeployables;

            [JsonProperty(PropertyName = en ? "Eject Deployables Before Spawning A Base" : "Выкидывать за купол Размещённые Предметы Перед Появлением Базы")]
            public bool EjectDeployables;

            [JsonProperty(PropertyName = en ? "Extra Distance To Spawn From Monuments" : "Расстояние для Спауна от Монументов")]
            public float MonumentDistance = 25f;

            [JsonProperty(PropertyName = en ? "Move Weapons Onto Weapon Racks" : "Переместить оружие на оружейные стойки")]
            public bool Racks = true;

            [JsonProperty(PropertyName = en ? "Divide Weapon Rack Loot When Enabled" : "Разделять лут стойки для оружия при включении")]
            public bool DivideRackLoot = true;

            [JsonProperty(PropertyName = en ? "Move Cookables Into Ovens" : "Перемещать и плавить руду и нефть в печки и НПЗ")]
            public bool Cook = true;

            [JsonProperty(PropertyName = en ? "Move Food Into BBQ Or Fridge" : "Перемещать Еду в Мангалы или Холодильники")]
            public bool Food = true;

            [JsonProperty(PropertyName = en ? "Blacklist For BBQ And Fridge" : "Черный Список для Мангалов и Холодильников")]
            public HashSet<string> Foods = new() { "syrup", "pancakes" };

            [JsonProperty(PropertyName = en ? "Move Resources Into Tool Cupboard" : "Перемещать Ресурсы в Шкаф для Инструментов")]
            public bool Cupboard = true;

            [JsonProperty(PropertyName = en ? "Move Items Into Lockers" : "Перемещать Предметы в шкафы для переодевания")]
            public bool Lockers;

            [JsonProperty(PropertyName = en ? "Divide Locker Loot When Enabled" : "Разделить лут между шкафами для переодевания, если включено")]
            public bool DivideLockerLoot = true;

            [JsonProperty(PropertyName = en ? "Lock Treasure To First Attacker" : "Заблокировать Базу для Первого Атакующего")]
            public bool UseOwners = true;

            [JsonProperty(PropertyName = en ? "Lock Treasure Max Inactive Time (Minutes)" : "Время Неактивности до снятия блокировки с Базы (Минуты)")]
            public float LockTime = 20f;

            [JsonProperty(PropertyName = en ? "Assign Lockout When Lock Treasure Max Inactive Time Expires" : "Назначить Блокировку при Истечении Максимального Времени Неактивности Сокровища")]
            public bool SetLockout;

            [JsonProperty(PropertyName = en ? "Lock Players To Raid Base After Entering Zone" : "Заблокировать Базу на Игроков После Входа в Зону")]
            public bool LockToRaidOnEnter;

            [JsonProperty(PropertyName = en ? "Only Award First Attacker and Allies" : "Награждать Только Первого Атакующего и Его Союзников")]
            public bool OnlyAwardAllies;

            [JsonProperty(PropertyName = en ? "Only Award Owner Of Raid" : "Награждать Только Владельца Рейда")]
            public bool OnlyAwardOwner;

            [JsonProperty(PropertyName = en ? "Mounts Can Take Damage From Players" : "Транспортные Средства Могут Получать Урон от Игроков")]
            public bool MountDamageFromPlayers;

            [JsonProperty(PropertyName = en ? "Player Cupboard Detection Radius" : "Радиус Обнаружения Шкафов Игроков")]
            public float CupboardDetectionRadius = 125f;

            [JsonProperty(PropertyName = en ? "Players With PVP Delay Can Damage Anything Inside Zone" : "Игроки с Задержкой PVP Могут Наносить Урон Любому Объекту в Зоне")]
            public bool PVPDelayDamageInside;

            [JsonProperty(PropertyName = en ? "Players With PVP Delay Can Damage Other Players With PVP Delay Anywhere" : "Игроки с Задержкой PVP Могут Везде Наносить Урон Другим Игрокам с Задержкой PVP")]
            public bool PVPDelayAnywhere;

            [JsonProperty(PropertyName = en ? "PVP Delay Between Zone Hopping" : "Задержка PVP Между Перемещениями по Зонам")]
            public float PVPDelay = 10f;

            [JsonProperty(PropertyName = en ? "PVP Delay Between Zone Hopping Persists After Despawn" : "Задержка PVP между переходами по зонам сохраняется после исчезновения")]
            public bool PVPDelayPersists;

            [JsonProperty(PropertyName = en ? "PVP Delay Triggers When Entity Destroyed From Outside Zone" : "Задержка PVP Активируется При Уничтожении Объекта Извне Зоны")]
            public bool PVPDelayTrigger;

            [JsonProperty(PropertyName = en ? "Prevent Fire From Spreading" : "Предотвратить Распространение Огня")]
            public bool PreventFireFromSpreading = true;

            [JsonProperty(PropertyName = en ? "Prevent Players From Hogging Raids" : "Предотвратить Захват Рейдов Игроками")]
            public bool PreventHogging = true;

            [JsonProperty(PropertyName = en ? "Prevent Fall Damage When Base Despawns" : "Предотвратить Урон от Падения при Исчезновении Базы")]
            public bool PreventFallDamage;

            [JsonProperty(PropertyName = en ? "Require Cupboard To Be Looted Before Despawning" : "Требовать Ограбления Шкафа перед Исчезновением", NullValueHandling = NullValueHandling.Ignore)]
            public bool? _RequireCupboardLooted = null;

            [JsonProperty(PropertyName = en ? "Require Cupboard To Be Looted Before Completion" : "Требовать ограбления шкафа для инструментов перед завершением")]
            public bool RequireCupboardLooted;

            [JsonProperty(PropertyName = en ? "Destroying The Cupboard Completes The Raid" : "Уничтожение Шкафа Завершает Рейд")]
            public bool EndWhenCupboardIsDestroyed;

            [JsonProperty(PropertyName = en ? "Require All Bases To Spawn Before Respawning An Existing Base" : "Требовать Появления Всех Баз Перед повторным созданием такой же Базы")]
            public bool RequireAllSpawned = true;

            [JsonProperty(PropertyName = en ? "Require All Bases To Spawn For Individual Players" : "Требовать, чтобы все базы спавнились для индивидуальных игроков")]
            public bool RequireAllSpawnedBuyableEvents;

            [JsonProperty(PropertyName = en ? "Require All Bases To Spawn Persists On Restart" : "Требование о Появлении Всех Баз Сохраняется После Перезапуска")]
            public bool RequireAllSpawnsPersist;

            [JsonProperty(PropertyName = en ? "Turn Lights On At Night" : "Включать Освещение Ночью")]
            public bool Lights = true;

            [JsonProperty(PropertyName = en ? "Turn Lights On Indefinitely" : "Включать Освещение на Всегда")]
            public bool AlwaysLights;

            [JsonProperty(PropertyName = en ? "Turn Lights On Bypasses NightLantern" : "Включение Освещения переназначает настройку NightLantern")]
            public bool NightLantern;

            [JsonProperty(PropertyName = en ? "Ignore List For Turn Lights On" : "Список исключений для включения света", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> IgnoredLights = new() { "laserlight", "weaponrack", "lightswitch", "soundlight", "xmas" };

            [JsonProperty(PropertyName = en ? "Traps And Turrets Ignore Users Using NOCLIP" : "Ловушки и Турели Игнорируют Пользователей в Режиме NOCLIP")]
            public bool IgnoreFlying;

            [JsonProperty(PropertyName = en ? "Use Random Codes On Code Locks" : "Использовать Случайные Коды на Кодовых Замках")]
            public bool RandomCodes = true;

            [JsonProperty(PropertyName = en ? "Maximum Water Depth For All Npcs" : "Максимальная Глубина Воды для Всех НПС")]
            public float WaterDepth = 3f;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Looting (min: 1)" : "Минуты до исчезновения после разграбления (минимум: 1)")]
            public int DespawnMinutes = 15;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Looting Resets When Damaged" : "Минуты до исчезновения после разграбления сбрасываются при повреждении")]
            public bool DespawnMinutesReset;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Inactive (0 = disabled)" : "Минуты до исчезновения после бездействия (0 = отключено)")]
            public int DespawnMinutesInactive = 45;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Inactive Resets When Damaged" : "Минуты до исчезновения после бездействия сбрасываются при повреждении")]
            public bool DespawnMinutesInactiveReset = true;

            [JsonProperty(PropertyName = en ? "Wait To Start Despawn Timer When Base Takes Damage From Player" : "Ожидание Начала Таймера Исчезновения После Урона Базе от Игрока")]
            public bool Engaged;

            [JsonProperty(PropertyName = en ? "Wait To Start Despawn Timer Until Npc Is Killed By Player" : "Ожидать запуска таймера удаления, пока NPC не будет убит игроком")]
            public bool EngagedNpc;

            [JsonProperty(PropertyName = "Apply Title Case To Difficulty Name", NullValueHandling = NullValueHandling.Ignore)]
            public bool? TitleCase = en ? false : null;

            [JsonProperty(PropertyName = "Max Amount Of Players Allowed To Enter (0 = infinite, -1 = none)", NullValueHandling = NullValueHandling.Ignore)]
            public ManagementPlayerAmountsSettings _Players = null;
            
            public bool Any() => Dictionary.Count > 0 && Dictionary.All(x => x.Value != null);

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => TryAdd(en ? $"{mode} Raids Can Spawn On" : $"Дни спавна {mode} рейд-баз", new()));
                    return Any();
                }
                return false;
            }

            public ManagementSettings(string easyKey, string mediumKey, string hardKey, string expertKey, string nightmareKey) : base(en ? "Raid Management" : "Управление Рейдами", easyKey, mediumKey, hardKey, expertKey, nightmareKey) { }
        }

        public class MapMarkerSettings
        {
            [JsonProperty(PropertyName = en ? "Marker Name" : "Название Маркера")]
            public string MarkerName = "Raidable Base Event";

            [JsonProperty(PropertyName = en ? "Radius" : "Радиус")]
            public float Radius = 0.25f;

            [JsonProperty(PropertyName = en ? "Radius (Map Size 3600 Or Less)" : "Радиус (Размер Карты 3600 или Меньше)")]
            public float SubRadius = 0.5f;

            [JsonProperty(PropertyName = en ? "Use Vending Map Marker" : "Использовать Маркер Торгового Автомата на Карте")]
            public bool UseVendingMarker = true;

            [JsonProperty(PropertyName = en ? "Show Remaining Loot When No Owner (PVE)" : "Показать оставшуюся добычу, если нет владельца (PVE)")]
            public bool LootPVE = true;

            [JsonProperty(PropertyName = en ? "Show Remaining Loot When No Owner (PVP)" : "Показать оставшуюся добычу, если нет владельца (PVP)")]
            public bool LootPVP;

            [JsonProperty(PropertyName = en ? "Show Owners Name on Map Marker" : "Показывать Имя Владельца на Маркере Карты")]
            public bool ShowOwnersName = true;

            [JsonProperty(PropertyName = en ? "Show If Purchased On Map Marker" : "Показывать Покупная ли База на Маркере Карты")]
            public bool ShowPurchased;

            [JsonProperty(PropertyName = en ? "Use Explosion Map Marker" : "Использовать Маркер Взрыва на Карте")]
            public bool UseExplosionMarker;

            [JsonProperty(PropertyName = en ? "Create Markers For Buyable Events" : "Создавать Маркеры для Покупаемых Событий")]
            public bool Buyables = true;

            [JsonProperty(PropertyName = en ? "Create Markers For Maintained Events" : "Создавать Маркеры для Поддерживаемых Событий")]
            public bool Maintained = true;

            [JsonProperty(PropertyName = en ? "Create Markers For Scheduled Events" : "Создавать Маркеры для Запланированных Событий")]
            public bool Scheduled = true;

            [JsonProperty(PropertyName = en ? "Create Markers For Manual Events" : "Создавать Маркеры для Ручных Событий")]
            public bool Manual = true;
        }

        public class ExperimentalSettings
        {
            [JsonProperty(PropertyName = en ? "Apply Custom Auto Height To" : "Применить Пользовательскую Автоматическую Высоту к", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AutoHeight = new();

            [JsonProperty(PropertyName = en ? "Bunker Bases Or Profiles" : "Бункерные Базы или Профили", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Bunker = new();

            [JsonProperty(PropertyName = en ? "Multi Foundation Bases Or Profiles" : "Базы или Профили с Многоуровневым Фундаментом", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> MultiFoundation = new();

            public enum Type { AutoHeight, Bunker, MultiFoundation };

            public bool Contains(Type type, RandomBase rb) => type switch
            {
                Type.AutoHeight => Contains(AutoHeight, rb),
                Type.Bunker => Contains(Bunker, rb),
                _ => Contains(MultiFoundation, rb),
            };

            public bool Contains(List<string> m, RandomBase rb) => m.Contains("*") || m.Contains(rb.BaseName) || m.Contains(rb.Profile.ProfileName);
        }

        public class WipeSettings
        {
            [JsonProperty(PropertyName = "Wipe triggers when Rust protocol changes")]
            public bool Protocol = true;

            [JsonProperty(PropertyName = "Wipe triggers on detection of map wipe")]
            public bool Map = true;

            [JsonProperty(PropertyName = "Wipe includes current data")]
            public bool Current = true;

            [JsonProperty(PropertyName = "Wipe includes lifetime data (NOT recommended!)")]
            public bool Lifetime;

            [JsonProperty(PropertyName = "Manual wipe (command: rb wipe) revokes below permissions and groups from players")]
            public bool RemoveFromList = true;

            [JsonProperty(PropertyName = "Permissions and groups to revoke on wipe (command: rb revokepg)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Remove = new();
        }

        public class PluginSettings
        {
            [JsonProperty(PropertyName = "Wipe Management (/data/RaidableBases.json)")]
            public WipeSettings Wipe = new();

            [JsonProperty(PropertyName = en ? "Experimental [* = everything]" : "Экспериментальные Настройки [* = все]")]
            public ExperimentalSettings Experimental = new();

            [JsonProperty(PropertyName = en ? "Raid Management" : "Управление Рейдами")]
            public ManagementSettings Management = new(
                en ? "Easy Raids Can Spawn On" : "Дни спавна Легкий рейд-баз",
                en ? "Medium Raids Can Spawn On" : "Дни спавна Средний рейд-баз",
                en ? "Hard Raids Can Spawn On" : "Дни спавна Тяжело рейд-баз",
                en ? "Expert Raids Can Spawn On" : "Дни спавна Эксперт рейд-баз",
                en ? "Nightmare Raids Can Spawn On" : "Дни спавна Кошмарный рейд-баз");

            [JsonProperty(PropertyName = en ? "Map Markers" : "Маркеры на Карте")]
            public MapMarkerSettings Markers = new();

            [JsonProperty(PropertyName = en ? "Buyable Events" : "Покупаемые События")]
            public BuyableSettings Buyable = new();

            [JsonProperty(PropertyName = en ? "Maintained Events" : "Поддерживаемых Событий")]
            public MaintainedSettings Maintained = new();

            [JsonProperty(PropertyName = en ? "Manual Events" : "Ручные События")]
            public ManualSettings Manual = new();

            [JsonProperty(PropertyName = en ? "Scheduled Events" : "Запланированные События")]
            public ScheduledSettings Schedule = new();

            [JsonProperty(PropertyName = en ? "Allowed Zone Manager Zones" : "Разрешенные Зоны Управления Зонами", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AllowedZones = new() { "pvp", "99999999" };

            [JsonProperty(PropertyName = en ? "Buyable Event Costs" : "Стоимость Покупаемых Событий")]
            public RaidableBaseCostOptions Include = new();

            [JsonProperty(PropertyName = en ? "Economics Buy Raid Costs (0 = disabled)" : "Стоимость Покупки Рейда в Экономике (0 = отключено)")]
            public DifficultyModesDouble Economics = new(en ? "Economics Buy Raid Costs (0 = disabled)" : "Стоимость Покупки Рейда в Экономике (0 = отключено)");

            [JsonProperty(PropertyName = en ? "ServerRewards Buy Raid Costs (0 = disabled)" : "Стоимость Покупки Рейда в ServerRewards (0 = отключено)")]
            public DifficultyModeOptions ServerRewards = new(en ? "ServerRewards Buy Raid Costs (0 = disabled)" : "Стоимость Покупки Рейда в ServerRewards (0 = отключено)");

            [JsonProperty(PropertyName = en ? "Custom Buy Raid Cost" : "Пользовательская Стоимость Покупки Рейда", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<CustomCostOptions>> Custom = DefaultCustomCosts();

            [JsonProperty(PropertyName = en ? "ShoppyStock Buy Raid Cost" : "Стоимость Покупки Рейда в ShoppyStock", NullValueHandling = NullValueHandling.Ignore)]
            public CustomCostShoppyStock ShoppyStock = null;

            [JsonProperty(PropertyName = en ? "Use Grid Locations In Allowed Zone Manager Zones Only" : "Использовать Сеточные Расположения Только в Разрешенных Зонах Управления Зонами")]
            public bool UseZoneManagerOnly;

            [JsonProperty(PropertyName = en ? "Extended Distance To Spawn Away From Zone Manager Zones" : "Расширенное Расстояние для Спауна Вне Зон Управления Зонами")]
            public float ZoneDistance = 25f;

            [JsonProperty(PropertyName = en ? "Blacklisted Commands (PVE)" : "Черный Список Команд (PVE)", NullValueHandling = NullValueHandling.Ignore)]
            public List<string> _BlacklistedPVECommands = null;

            [JsonProperty(PropertyName = en ? "Blacklisted Commands (PVP)" : "Черный Список Команд (PVP)", NullValueHandling = NullValueHandling.Ignore)]
            public List<string> _BlacklistedPVPCommands = null;

            [JsonProperty(PropertyName = en ? "Automatically Teleport Admins To Their Map Marker Positions" : "Автоматически Телепортировать Администраторов к Их Маркерам на Карте")]
            public bool TeleportMarker = true;

            [JsonProperty(PropertyName = en ? "Automatically Destroy Markers That Admins Teleport To" : "Автоматически Уничтожать Маркеры, к Которым Телепортируются Администраторы")]
            public bool DestroyMarker;

            [JsonProperty(PropertyName = en ? "Block Archery Plugin At PVE Events" : "Блокировать Archery Plugin на PVE-мероприятиях")]
            public bool NoArcheryPVE;

            [JsonProperty(PropertyName = en ? "Block Archery Plugin At PVP Events" : "Блокировать Archery Plugin на PVP-мероприятиях")]
            public bool NoArcheryPVP;

            [JsonProperty(PropertyName = en ? "Block Wizardry Plugin At PVE Events" : "Блокировать Wizardry Plugin на PVE-мероприятиях")]
            public bool NoWizardryPVE;

            [JsonProperty(PropertyName = en ? "Block Wizardry Plugin At PVP Events" : "Блокировать Wizardry Plugin на PVP-мероприятиях")]
            public bool NoWizardryPVP;

            [JsonProperty(PropertyName = en ? "Block Weapons From Use" : "Блокировать оружие от использования", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlockedWeapons = new() { "toolgun" };

            [JsonProperty(PropertyName = en ? "Chat Steam64ID" : "Steam64ID Чата")]
            public ulong ChatID = 76561199564392767;

            [JsonProperty(PropertyName = en ? "Expansion Mode (Dangerous Treasures)" : "Режим Расширения (Dangerous Treasures)")]
            public bool ExpansionMode;

            [JsonProperty(PropertyName = en ? "Remove Admins From Raiders List" : "Удалить Администраторов из Списка Рейдеров")]
            public bool RemoveAdminRaiders;

            [JsonProperty(PropertyName = en ? "Show Direction To Coordinates" : "Показать Направление к Координатам")]
            public bool ShowDir;

            [JsonProperty(PropertyName = en ? "Show Grid Coordinates" : "Показать координаты сетки")]
            public bool ShowGrid = true;

            [JsonProperty(PropertyName = en ? "Show X Z Coordinates" : "Показать Координаты X Z")]
            public bool ShowXZ;

            [JsonProperty(PropertyName = en ? "Buy Raid Command" : "Команда Покупки Рейда")]
            public string BuyCommand = "buyraid";

            [JsonProperty(PropertyName = en ? "Event Command" : "Команда События")]
            public string EventCommand = "rbe";

            [JsonProperty(PropertyName = en ? "Hunter Command" : "Команда Охотника")]
            public string HunterCommand = "rb";

            [JsonProperty(PropertyName = en ? "Server Console Command" : "Команда Консоли Сервера")]
            public string ConsoleCommand = "rbevent";

            [JsonProperty(PropertyName = en ? "Remove Paid Content Command" : "Remove Paid Content Command")]
            public string EditCommand = null;

            internal bool AnyCustomCost()
            {
                foreach (var options in Custom.Values)
                {
                    foreach (var option in options)
                    {
                        if (option.isItem || option.isPlugin)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public class IQDronePatrolSettings
        {
            [JsonProperty(en ? "Use drone support" : "Использовать поддержку дронов")]
            public bool UseDronePatrol;

            [JsonProperty(en ? "How many drones will be spawned near the base?" : "Сколько дронов будет заспавнено на рейд-базе")]
            public int droneCountSpawned = 10;

            [JsonProperty(en ? "How many drones can attack simultaneously?" : "Какое количество дронов сможет атаковать одновременно")]
            public int droneAttackedCount = 2;

            [JsonProperty(en ? "Drone presets configuration [Drone preset key from the drone config] - chance" : "Настройка пресетов дронов [Ключ пресета дронов из конфига дронов] - шанс")]
            public Dictionary<string, int> keyDrones = new()
            {
                ["LITE_DRONE"] = 100, //Ключи дронов с их пресетами и шансом (ключи берутся из конфига дронов)
            };
        }

        public class EventMessageRewardSettings
        {
            [JsonProperty(PropertyName = en ? "Flying" : "Летающий")]
            public bool Flying;

            [JsonProperty(PropertyName = en ? "Vanished" : "Vanish")]
            public bool Vanished;

            [JsonProperty(PropertyName = en ? "Inactive" : "Неактивный")]
            public bool Inactive = true;

            [JsonProperty(PropertyName = en ? "Not An Ally" : "Не Союзник")]
            public bool NotAlly = true;

            [JsonProperty(PropertyName = en ? "Not The Owner" : "Не Владелец")]
            public bool NotOwner = true;

            [JsonProperty(PropertyName = en ? "Not A Participant" : "Не Участник")]
            public bool NotParticipant = true;

            [JsonProperty(PropertyName = en ? "Remove Admins From Raiders List" : "Удалить Администраторов из Списка Рейдеров")]
            public bool RemoveAdmin;
        }

        public class EventMessageSettings
        {
            [JsonProperty(PropertyName = en ? "Ineligible For Rewards" : "Не Имеет Права на Награды")]
            public EventMessageRewardSettings Rewards = new();

            [JsonProperty(PropertyName = en ? "Announce Raid Unlocked" : "Объявить о снятии блокировки с Рейда")]
            public bool AnnounceRaidUnlock;

            [JsonProperty(PropertyName = en ? "Announce Buy Base Messages" : "Объявить Сообщения о Покупке Базы")]
            public bool AnnounceBuy;

            [JsonProperty(PropertyName = en ? "Announce Thief Message" : "Объявить Сообщение когда забраны все предметы")]
            public bool AnnounceThief = true;

            [JsonProperty(PropertyName = en ? "Announce PVE/PVP Enter/Exit Messages" : "Объявить Сообщения о Входе/Выходе PVE/PVP")]
            public bool AnnounceEnterExit = true;

            [JsonProperty(PropertyName = en ? "Announce When Blocks Are Immune To Damage" : "Объявите, когда блоки невосприимчивы к повреждениям")]
            public bool BlocksImmune;

            [JsonProperty(PropertyName = en ? "Show Destroy Warning" : "Показать Предупреждение об Уничтожении")]
            public bool ShowWarning = true;

            [JsonProperty(PropertyName = en ? "Show Opened Message For PVE Bases" : "Показать Сообщение об Открытии для PVE Баз")]
            public bool OpenedPVE = true;

            [JsonProperty(PropertyName = en ? "Show Opened Message For PVP Bases" : "Показать Сообщение об Открытии для PVP Баз")]
            public bool OpenedPVP = true;

            [JsonProperty(PropertyName = en ? "Show Opened Message For Paid Bases" : "Показать Сообщение об Открытии для Оплаченных Баз")]
            public bool OpenedAndPaid = true;

            [JsonProperty(PropertyName = en ? "Show Message For Block Damage Outside Of The Dome To Players Inside" : "Показать Сообщение о Блокировке Урона Снаружи Купола Игрокам Внутри")]
            public bool NoDamageFromOutsideToPlayersInside;

            [JsonProperty(PropertyName = en ? "Show Message When Purchase Becomes Available" : "Показать Сообщение, Когда Покупка Станет Доступной")]
            public bool PurchaseAvailable = true;

            [JsonProperty(PropertyName = en ? "Show Prefix" : "Показать Префикс")]
            public bool Prefix = true;

            [JsonProperty(PropertyName = en ? "Notify Plugin - Type (-1 = disabled)" : "Notify Plugin - Тип (-1 = отключено)")]
            public int NotifyType = -1;

            [JsonProperty(PropertyName = en ? "Rust Game Tip Style (0 = blue norm, 1 = red norm, 2 = blue long, 3 = blue short, 4 = server)" : "Rust Game Tip Style (0 = blue normal, 1 = red normal, 2 = blue long, 3 = blue short, 4 = server event)")]
            public GameTip.Styles RustStyle = NoRustStyle;
            internal const GameTip.Styles NoRustStyle = (GameTip.Styles)(-1);

            [JsonProperty(PropertyName = en ? "Strip Colors From Rust Game Tip Messages" : "Strip Colors From Rust Game Tip Messages")]
            public bool StripRustTip;

            [JsonProperty(PropertyName = en ? "Notification Interval" : "Интервал Уведомлений")]
            public float Interval = 1f;

            [JsonProperty(PropertyName = en ? "Send Messages To Player" : "Отправлять Сообщения Игроку")]
            public bool Message = true;

            [JsonProperty(PropertyName = en ? "Debug Notifications (console)" : "Отладка уведомлений (консоль)")]
            public bool Debug;

            [JsonProperty(PropertyName = en ? "Save Thieves To Log File" : "Сохранить Воров в Лог-файл")]
            public bool LogThieves;

            [JsonProperty(PropertyName = en ? "Distance To Notify Players When Near An Event" : "Дистанция для уведомления игроков, находящихся рядом с событием")]
            public float Nearby;
        }

        public class GUIAnnouncementSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Banner Tint Color" : "Цвет Тонирования Баннера")]
            public string TintColor = "Grey";

            [JsonProperty(PropertyName = en ? "Maximum Distance" : "Максимальное Расстояние")]
            public float Distance = 300f;

            [JsonProperty(PropertyName = en ? "Text Color" : "Цвет текста")]
            public string TextColor = "White";
        }

        public class NpcSettingsInsideBaseSleepers
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Unwakeable" : "Не Просыпающийся")]
            public bool Unwakeable = true;

            [JsonProperty(PropertyName = en ? "Spawn Kit In Corpses Inventory" : "Создавать Комплект в Инвентаре Трупов")]
            public bool CopyKit;

            [JsonProperty(PropertyName = en ? "Spawn Loadout In Corpses Inventory" : "Создавать Снаряжение в Инвентаре Трупов")]
            public bool CopyLoadout;

            internal bool IsUnwakeable => Enabled && Unwakeable;
        }

        public class NpcSettingsInsideBase
        {
            [JsonProperty(PropertyName = en ? "Sleepers" : "Спящие")]
            public NpcSettingsInsideBaseSleepers Sleepers = new();

            [JsonProperty(PropertyName = en ? "Spawn On Floors" : "Создавать на полу")]
            public bool SpawnOnFloors;

            [JsonProperty(PropertyName = en ? "Spawn On Beds" : "Создавать на Кроватях")]
            public bool SpawnOnBeds;

            [JsonProperty(PropertyName = en ? "Spawn On Rugs" : "Создавать на Коврах")]
            public bool SpawnOnRugs;

            [JsonProperty(PropertyName = en ? "Spawn On Rugs With Skin Only" : "Создавать на Коврах Только с Скином")]
            public ulong SpawnOnRugsSkin = 1;

            [JsonProperty(PropertyName = en ? "Bed Health Multiplier" : "Множитель Здоровья Кровати")]
            public float BedHealthMultiplier = 1f;

            [JsonProperty(PropertyName = en ? "Rug Health Multiplier" : "Множитель Здоровья Ковра")]
            public float RugHealthMultiplier = 1f;

            [JsonProperty(PropertyName = en ? "Spawn Murderers Outside" : "Создавать Убийц Снаружи")]
            public bool SpawnMurderersOutside = true;

            [JsonProperty(PropertyName = en ? "Spawn Scientists Outside" : "Создавать Ученых Снаружи")]
            public bool SpawnScientistsOutside = true;

            [JsonProperty(PropertyName = en ? "Minimum Inside (-1 = ignore)" : "Минимум Внутри (-1 = игнорировать)")]
            public int Min = -1;

            [JsonProperty(PropertyName = en ? "Maximum Inside (-1 = ignore)" : "Максимум Внутри (-1 = игнорировать)")]
            public int Max = -1;
        }

        public class NpcKitSettings
        {
            [JsonProperty(PropertyName = en ? "Helm" : "Шлем", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Helm = new();

            [JsonProperty(PropertyName = en ? "Torso" : "Торс", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Torso = new();

            [JsonProperty(PropertyName = en ? "Pants" : "Штаны", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Pants = new();

            [JsonProperty(PropertyName = en ? "Gloves" : "Перчатки", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Gloves = new();

            [JsonProperty(PropertyName = en ? "Boots" : "Ботинки", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Boots = new();

            [JsonProperty(PropertyName = en ? "Shirt" : "Рубашка", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Shirt = new();

            [JsonProperty(PropertyName = en ? "Kilts" : "Килты", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Kilts = new();

            [JsonProperty(PropertyName = en ? "Weapon" : "Оружие", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Weapon = new();
        }

        public class ScientistLootSettings
        {
            [JsonProperty(PropertyName = en ? "Prefab ID List" : "Список префабов", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> IDs = new() { "cargo", "turret_any", "ch47_gunner", "excavator", "full_any", "heavy", "junkpile_pistol", "oilrig", "patrol", "peacekeeper", "roam", "roamtethered" };

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Disable All Prefab Loot Spawns" : "Отключить все выпадения добычи из префабов", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public bool None;

            [JsonProperty(PropertyName = en ? "Call OnCorpsePopulate Hook (some plugins require this)" : "Call OnCorpsePopulate Hook (some plugins require this)")]
            public bool CallHook;

            public uint GetRandom(List<string> ids) => ids.GetRandom() switch
            {
                "cargo" => 3623670799u,
                "turret_any" => 1639447304u,
                "ch47_gunner" => 1017671955u,
                "excavator" => 4293908444u,
                "full_any" => 1539172658u,
                "heavy" => 1536035819u,
                "junkpile_pistol" => 2066159302u,
                "cargo_turret" => 881071619u,
                "oilrig" => 548379897u,
                "patrol" => 4272904018u,
                "peacekeeper" => 2390854225u,
                "roam" => 4199494415u,
                "roamtethered" => 529928930u,
                "roamexcavator" => 529928930u,
                "scarecrow" => 3473349223u,
                "scarecrow_dungeon" => 3019050354u,
                "scarecrow_dungeonnoroam" => 70161046u,
                _ => 1536035819u
            };
        }

        public class NpcMultiplierSettings
        {
            [JsonProperty(PropertyName = en ? "Explosive Damage Multiplier" : "Множитель урона от взрывов")]
            public float ExplosiveDamageMultiplier = 1f;

            [JsonProperty(PropertyName = en ? "Gun Damage Multiplier" : "Множитель урона от огнестрельного оружия")]
            public float ProjectileDamageMultiplier = 1f;

            [JsonProperty(PropertyName = en ? "Melee Damage Multiplier" : "Множитель урона от ближнего боя")]
            public float MeleeDamageMultiplier = 1f;
        }

        public class NpcSettingsAccuracyDifficulty
        {
            [JsonProperty(PropertyName = "AK47")]
            public double AK47;

            [JsonProperty(PropertyName = "AK47 ICE")]
            public double AK47ICE;

            [JsonProperty(PropertyName = "Bolt Rifle")]
            public double BOLT_RIFLE;

            [JsonProperty(PropertyName = "Compound Bow")]
            public double COMPOUND_BOW;

            [JsonProperty(PropertyName = "Crossbow")]
            public double CROSSBOW;

            [JsonProperty(PropertyName = "Double Barrel Shotgun")]
            public double DOUBLE_SHOTGUN;

            [JsonProperty(PropertyName = "Eoka")]
            public double EOKA;

            [JsonProperty(PropertyName = "Glock")]
            public double GLOCK;

            [JsonProperty(PropertyName = "HMLMG")]
            public double HMLMG;

            [JsonProperty(PropertyName = "L96")]
            public double L96;

            [JsonProperty(PropertyName = "LR300")]
            public double LR300;

            [JsonProperty(PropertyName = "M249")]
            public double M249;

            [JsonProperty(PropertyName = "Minigun")]
            public double MINIGUN;

            [JsonProperty(PropertyName = "M39")]
            public double M39;

            [JsonProperty(PropertyName = "M92")]
            public double M92;

            [JsonProperty(PropertyName = "MP5")]
            public double MP5;

            [JsonProperty(PropertyName = "Nailgun")]
            public double NAILGUN;

            [JsonProperty(PropertyName = "Pump Shotgun")]
            public double PUMP_SHOTGUN;

            [JsonProperty(PropertyName = "Python")]
            public double PYTHON;

            [JsonProperty(PropertyName = "Revolver")]
            public double REVOLVER;

            [JsonProperty(PropertyName = "Semi Auto Pistol")]
            public double SEMI_AUTO_PISTOL;

            [JsonProperty(PropertyName = "Semi Auto Rifle")]
            public double SEMI_AUTO_RIFLE;

            [JsonProperty(PropertyName = "Spas12")]
            public double SPAS12;

            [JsonProperty(PropertyName = "Speargun")]
            public double SPEARGUN;

            [JsonProperty(PropertyName = "SMG")]
            public double SMG;

            [JsonProperty(PropertyName = "Snowball Gun")]
            public double SNOWBALL_GUN;

            [JsonProperty(PropertyName = "Thompson")]
            public double THOMPSON;

            [JsonProperty(PropertyName = "Waterpipe Shotgun")]
            public double WATERPIPE_SHOTGUN;

            public NpcSettingsAccuracyDifficulty(double accuracy)
            {
                AK47 = AK47ICE = BOLT_RIFLE = DOUBLE_SHOTGUN = EOKA = GLOCK = HMLMG = L96 = LR300 = M249 = MINIGUN = M39 = M92 = MP5 = NAILGUN = PUMP_SHOTGUN = PYTHON = REVOLVER = SEMI_AUTO_PISTOL = SEMI_AUTO_RIFLE = SPAS12 = SPEARGUN = SMG = SNOWBALL_GUN = THOMPSON = WATERPIPE_SHOTGUN = accuracy;
                COMPOUND_BOW = CROSSBOW = 50;
            }

            public double Get(HumanoidBrain brain) => brain.AttackName switch
            {
                "ak47u.entity" or "ak47u_med.entity" or "ak47u_diver.entity" or "sks.entity" => AK47,
                "ak47u_ice.entity" => AK47ICE,
                "bolt_rifle.entity" => BOLT_RIFLE,
                "compound_bow.entity" or "legacybow.entity" => COMPOUND_BOW,
                "crossbow.entity" or "bow_hunting.entity" or "mini_crossbow.entity" => CROSSBOW,
                "double_shotgun.entity" => DOUBLE_SHOTGUN,
                "glock.entity" or "hc_revolver.entity" => GLOCK,
                "hmlmg.entity" or "mgl.entity" => HMLMG,
                "l96.entity" => L96,
                "lr300.entity" => LR300,
                "m249.entity" => M249,
                "minigun.entity" => MINIGUN,
                "m39.entity" => M39,
                "m92.entity" => M92,
                "mp5.entity" => MP5,
                "nailgun.entity" => NAILGUN,
                "pistol_eoka.entity" => EOKA,
                "pistol_revolver.entity" => REVOLVER,
                "pistol_semiauto.entity" => SEMI_AUTO_PISTOL,
                "python.entity" => PYTHON,
                "semi_auto_rifle.entity" => SEMI_AUTO_RIFLE,
                "shotgun_pump.entity" or "blunderbuss.entity" or "m4_shotgun.entity" => PUMP_SHOTGUN,
                "shotgun_waterpipe.entity" => WATERPIPE_SHOTGUN,
                "spas12.entity" => SPAS12,
                "speargun.entity" or "blowpipe.entity" or "boomerang.entity" => SPEARGUN,
                "smg.entity" or "t1_smg" => SMG,
                "snowballgun.entity" => SNOWBALL_GUN,
                "thompson.entity" or _ => THOMPSON,
            };
        }

        public class NpcSettings
        {
            public NpcSettings() { }

            public NpcSettings(double accuracy)
            {
                Accuracy = new(accuracy);
            }

            public void SetAccuracy(string mode)
            {
                Accuracy ??= new(mode == RaidableMode.Easy || mode == RaidableMode.Medium ? 15.0 : mode == RaidableMode.Hard ? 20.0 : mode == RaidableMode.Expert ? 25.0 : 30.0);
            }

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = en ? "Weapon Accuracy (0 - 100)" : "Точность оружия (0 - 100)")]
            public NpcSettingsAccuracyDifficulty Accuracy;

            [JsonProperty(PropertyName = en ? "Damage Multipliers" : "Множители урона")]
            public NpcMultiplierSettings Multipliers = new();

            [JsonProperty(PropertyName = en ? "Spawn Inside Bases" : "Заселение внутри базы")]
            public NpcSettingsInsideBase Inside = new();

            [JsonProperty(PropertyName = en ? "Murderer Loadout" : "Набор убийцы")]
            public NpcKitSettings MurdererLoadout = new()
            {
                Helm = { "metal.facemask" },
                Torso = { "metal.plate.torso" },
                Pants = { "pants" },
                Gloves = { "tactical.gloves" },
                Boots = { "boots.frog" },
                Shirt = { "tshirt" },
                Weapon = { "machete" }
            };

            [JsonProperty(PropertyName = en ? "Scientist Loadout" : "Снаряжение ученых")]
            public NpcKitSettings ScientistLoadout = new()
            {
                Torso = { "hazmatsuit_scientist_peacekeeper" },
                Weapon = { "rifle.ak" }
            };

            [JsonProperty(PropertyName = en ? "Murderer Kits" : "Комплекты Убийц", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> MurdererKits = new() { "murderer_kit_1", "murderer_kit_2" };

            [JsonProperty(PropertyName = en ? "Scientist Kits" : "Комплекты Ученых", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ScientistKits = new() { "scientist_kit_1", "scientist_kit_2" };

            [JsonProperty(PropertyName = en ? "Murderer Items Dropped On Death" : "Предметы Убийц, Выпавшие При Смерти", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<LootItem> MurdererDrops = new() { new("ammo.pistol", 1, 30) };

            [JsonProperty(PropertyName = en ? "Scientist Items Dropped On Death" : "Предметы Ученых, Выпавшие При Смерти", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<LootItem> ScientistDrops = new() { new("ammo.rifle", 1, 30) };

            [JsonProperty(PropertyName = en ? "Spawn Alternate Default Scientist Loot" : "Спавн Альтернативных Предметов Ученых", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public ScientistLootSettings AlternateScientistLoot = new();

            [JsonProperty(PropertyName = en ? "Use Random Names" : "Использовать случайные имена")]
            public bool UseRandomNames = true;

            [JsonProperty(PropertyName = en ? "Use Capitalized Names" : "Использовать имена с заглавной буквы")]
            public bool Capitalize;

            [JsonProperty(PropertyName = en ? "Random Murderer Names" : "Случайные Murderer Имена", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RandomMurdererNames = new();

            [JsonProperty(PropertyName = en ? "Random Scientist Names" : "Случайные Scientist Имена", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RandomScientistNames = new();

            [JsonProperty(PropertyName = en ? "Amount That Can Throw Weapons" : "Количество, Которое Может Метать Оружие")]
            public int Thrown = 2;

            [JsonProperty(PropertyName = en ? "Amount Of Murderers To Spawn" : "Количество Спавна Убийц")]
            public int SpawnAmountMurderers = 1;

            [JsonProperty(PropertyName = en ? "Minimum Amount Of Murderers To Spawn" : "Минимальное Количество Спавна Убийц")]
            public int SpawnMinAmountMurderers = 1;

            [JsonProperty(PropertyName = en ? "Spawn Random Amount Of Murderers" : "Случайное Количество Спавна Убийц")]
            public bool SpawnRandomAmountMurderers;

            [JsonProperty(PropertyName = en ? "Amount Of Scientists To Spawn" : "Количество Спавна Ученых")]
            public int SpawnAmountScientists = 1;

            [JsonProperty(PropertyName = en ? "Minimum Amount Of Scientists To Spawn" : "Минимальное Количество Спавна Ученых")]
            public int SpawnMinAmountScientists = 1;

            [JsonProperty(PropertyName = en ? "Spawn Random Amount Of Scientists" : "Случайное Количество Спавна Ученых")]
            public bool SpawnRandomAmountScientists;

            [JsonProperty(PropertyName = en ? "Allow Npcs To Roofcamp" : "Разрешить НПС стрелять с крыши")]
            public bool Roofcampers;

            [JsonProperty(PropertyName = en ? "Allow Npcs To Counter Raid" : "Разрешить НПС контрнаступление")]
            public bool CounterRaid = true;

            [JsonProperty(PropertyName = en ? "Allow Npcs To Leave Dome When Attacking" : "Разрешить НПС покидать купол при атаке")]
            public bool CanLeave = true;

            [JsonProperty(PropertyName = en ? "Allow Npcs To Shoot Players Outside Of The Dome" : "Разрешить НПС стрелять в игроков снаружи купола")]
            public bool CanShoot = true;

            [JsonProperty(PropertyName = en ? "Allow Npcs To Play Catch When Equipped With Explosives" : "Разрешить НПС играть в подкидывание, когда у них есть взрывчатка")]
            public bool PlayCatch;

            [JsonProperty(PropertyName = en ? "Aggression Range" : "радиус агрессии")]
            public float AggressionRange = 70f;

            [JsonProperty(PropertyName = en ? "Decrease Damage Linearly From Npcs With A Maximum Effective Range Of" : "Постепенное уменьшение урона от НПС с максимальной эффективной дальностью")]
            public float NpcMaxEffectiveRange;

            [JsonProperty(PropertyName = en ? "Decrease Damage Linearly From Players With A Maximum Effective Range Of" : "Постепенное уменьшение урона от игроков с максимальной эффективной дальностью")]
            public float PlayerMaxEffectiveRange;

            [JsonProperty(PropertyName = en ? "Block Damage Outside To Npcs When Not Allowed To Leave Dome" : "Блокировать урон снаружи для НПС, когда им не разрешено покидать купол")]
            public bool BlockOutsideDamageOnLeave = true;

            [JsonProperty(PropertyName = en ? "Block Damage Outside Of The Dome To Npcs Inside" : "Блокировать урон снаружи купола по НПС внутри")]
            public bool BlockOutsideDamageToNpcsInside;

            [JsonProperty(PropertyName = en ? "Spawn Kit In Corpses Inventory" : "Создавать Комплект в Инвентаре Трупов")]
            public bool CopyKit;

            [JsonProperty(PropertyName = en ? "Spawn Loadout In Corpses Inventory" : "Создавать Снаряжение в Инвентаре Трупов")]
            public bool CopyLoadout;

            [JsonProperty(PropertyName = en ? "Health For Murderers" : "Здоровье для убийц")]
            public float MurdererHealth = 150f;

            [JsonProperty(PropertyName = en ? "Health For Scientists" : "Здоровье для ученых")]
            public float ScientistHealth = 150f;

            [JsonProperty(PropertyName = en ? "Kill Underwater Npcs" : "Убивать подводных НПС")]
            public bool KillUnderwater = true;

            [JsonProperty(PropertyName = en ? "Kits Are Unique When Applicable" : "Комплекты уникальны, когда это применимо")]
            public bool UniqueKits;

            [JsonProperty(PropertyName = en ? "Player Traps And Turrets Ignore Npcs" : "Ловушки и турели игроков игнорируют НПС")]
            public bool IgnorePlayerTrapsTurrets;

            [JsonProperty(PropertyName = en ? "Event Traps And Turrets Ignore Npcs" : "Ловушки и турели событий игнорируют НПС")]
            public bool IgnoreTrapsTurrets = true;

            [JsonProperty(PropertyName = en ? "Use Dangerous Treasures NPCs" : "Использовать НПС опасных сокровищ (Dangerous Treasures)")]
            public bool UseExpansionNpcs;
        }

        public class ProfileDespawnOptions
        {
            [JsonProperty(PropertyName = en ? "Override Global Config With These Options For This Profile" : "Переопределить глобальные настройки этими параметрами для этого профиля")]
            public bool OverrideConfig = false;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Looting (min: 1)" : "Минуты до исчезновения после разграбления (минимум: 1)")]
            public int DespawnMinutes = 15;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Looting Resets When Damaged" : "Минуты до исчезновения после разграбления сбрасываются при повреждении")]
            public bool DespawnMinutesReset;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Inactive (0 = disabled)" : "Минуты до исчезновения после бездействия (0 = отключено)")]
            public int DespawnMinutesInactive = 45;

            [JsonProperty(PropertyName = en ? "Minutes Until Despawn After Inactive Resets When Damaged" : "Минуты до исчезновения после бездействия сбрасываются при повреждении")]
            public bool DespawnMinutesInactiveReset = true;

            [JsonProperty(PropertyName = en ? "Wait To Start Despawn Timer When Base Takes Damage From Player" : "Ожидание Начала Таймера Исчезновения После Урона Базе от Игрока")]
            public bool Engaged;

            [JsonProperty(PropertyName = en ? "Wait To Start Despawn Timer Until Npc Is Killed By Player" : "Ожидать запуска таймера удаления, пока NPC не будет убит игроком")]
            public bool EngagedNpc;
        }

        public class PasteOption
        {
            [JsonProperty(PropertyName = en ? "Option" : "Опция")]
            public string Key;

            [JsonProperty(PropertyName = en ? "Value" : "Значение")]
            public string Value;
        }

        public class BuildingLevels
        {
            [JsonProperty(PropertyName = en ? "Level 2 - Final Death" : "Уровень 2 - Окончательная смерть")]
            public bool Level2;
        }

        public class DoorTypes
        {
            [JsonProperty(PropertyName = en ? "Wooden" : "Деревянные")]
            public bool Wooden;

            [JsonProperty(PropertyName = en ? "Metal" : "Металлические")]
            public bool Metal;

            [JsonProperty(PropertyName = en ? "HQM" : "МВК")]
            public bool HQM;

            [JsonProperty(PropertyName = en ? "Include Garage Doors" : "Включая гаражные двери")]
            public bool GarageDoor;

            public bool Any() => Wooden || Metal || HQM;
        }

        public class BuildingGradeLevels
        {
            [JsonProperty(PropertyName = en ? "Wooden" : "Деревянные")]
            public bool Wooden;

            [JsonProperty(PropertyName = en ? "Stone" : "Каменные")]
            public bool Stone;

            [JsonProperty(PropertyName = en ? "Metal" : "Металлические")]
            public bool Metal;

            [JsonProperty(PropertyName = en ? "HQM" : "МВК")]
            public bool HQM;

            public bool Any() => Wooden || Stone || Metal || HQM;
        }

        public class Mapping
        {
            [JsonProperty(PropertyName = en ? "Name" : "Имя", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string Name = "Example";

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Skin" : "Скин", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public ulong Skin;

            [JsonProperty(PropertyName = en ? "Grade" : "Класс", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public int Grade;

            public Mapping() { }
            public Mapping(string name, bool enabled, ulong skin, int grade)
            {
                Name = name;
                Enabled = enabled;
                Skin = skin;
                Grade = grade;
            }
        }

        public class BuildingGradeLevelsSkins : BuildingGradeLevels
        {
            [JsonProperty(PropertyName = en ? "Exclusions" : "Исключения", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Exclusions = new() { "raideasy100", "raideasy101" };

            [JsonProperty(PropertyName = en ? "Additional Rust Shop Building Skins" : "Дополнительные скины Rust Shop для зданий", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Mapping> AdditionalMappings = new()
            {
                new("Example", false, 0uL, 0)
            };

            [JsonProperty(PropertyName = en ? "Only Apply Skin When Material Is Enabled" : "Применять скин только при включенном материале")]
            public bool RequireMaterial;

            [JsonProperty(PropertyName = en ? "Use Adobe Skin" : "Использовать скин Adobe")]
            public bool Adobe;

            [JsonProperty(PropertyName = en ? "Use Shipping Container Skin" : "Используйте скин Shipping Container")]
            public bool Shipping;

            [JsonProperty(PropertyName = en ? "Use Brick Skin" : "Использовать скин кирпича")]
            public bool Brick;

            [JsonProperty(PropertyName = en ? "Use Frontier Skin" : "Использовать скин фронтира")]
            public bool Frontier;

            [JsonProperty(PropertyName = en ? "Use Gingerbread Skin" : "Использовать скин пряничного домика")]
            public bool Gingerbread;

            [JsonProperty(PropertyName = en ? "Use Brutalist Skin" : "Использовать скин брутализма")]
            public bool Brutalist;

            [JsonProperty(PropertyName = en ? "Use Random Colors" : "Использовать случайные цвета")]
            public bool RandomColour;

            [JsonProperty(PropertyName = en ? "Use Identical Colors" : "Использовать идентичные цвета")]
            public bool IdenticalColour;

            [JsonProperty(PropertyName = en ? "Use Random Skin For Whole Base" : "Использовать случайный скин для всей базы")]
            public bool RandomWhole;

            [JsonProperty(PropertyName = en ? "Use Random Skin On Every Block" : "Использовать случайный скин на каждом блоке")]
            public bool RandomEvery;

            [JsonProperty(PropertyName = en ? "Random Building Skin List" : "Список случайных скинов для здания", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> Skins = new() { 0, 2, 10220, 10221, 10223, 10225, 10232 };

            public ulong GetSkin(BuildingBlock block, BuildingGrade.Enum grade, ulong skinID)
            {
                List<ulong> skins = new();
                if (RandomEvery || RandomWhole)
                {
                    skins.AddRange(Skins.Where(skin => HasSkin(block, grade, skin)));
                    if (skins.Count > 0) return skins.GetRandom();
                }
                foreach (var map in GetMappings())
                {
                    if (map.Grade < 0 || map.Grade >= (int)BuildingGrade.Enum.Count)
                    {
                        continue;
                    }
                    bool material = map.Grade switch
                    {
                        (int)BuildingGrade.Enum.Wood => Wooden,
                        (int)BuildingGrade.Enum.Stone => Stone,
                        (int)BuildingGrade.Enum.Metal => Metal,
                        (int)BuildingGrade.Enum.TopTier => HQM,
                        _ => false
                    };
                    if (map.Enabled && (material || !RequireMaterial) && HasSkin(block, grade, map.Skin))
                    {
                        skins.Add(map.Skin);
                    }
                }
                return skins.Count > 0 ? skins.GetRandom() : skinID;
            }

            internal Construction construction = null;

            public List<Mapping> GetMappings()
            {
                List<Mapping> maps = new()
                {
                    new("Adobe", Adobe, 10220, (int)BuildingGrade.Enum.Stone),
                    new("Shipping Container", Shipping, 10221, (int)BuildingGrade.Enum.Metal),
                    new("Brick", Brick, 10223, (int)BuildingGrade.Enum.Stone),
                    new("Brutalist", Brutalist, 10225, (int)BuildingGrade.Enum.Stone),
                    new("Legacy Wood", Frontier, 10232, (int)BuildingGrade.Enum.Wood),
                    new("Gingerbread", Gingerbread, 2, (int)BuildingGrade.Enum.Wood),
                };
                construction ??= PrefabAttribute.server.Find<Construction>(870964632u);
                foreach (var map in AdditionalMappings)
                {
                    if (map.Skin == 0uL && !string.IsNullOrWhiteSpace(map.Name))
                    {
                        foreach (ConstructionGrade grade in construction.grades)
                        {
                            if (grade.gradeBase.upgradeMenu.name.english.EndsWith(map.Name))
                            {
                                map.Skin = grade.gradeBase.skin;
                                break;
                            }
                        }
                    }
                    maps.Add(map);
                }
                return maps;
            }

            public bool HasSkin(BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
            {
                ConstructionGrade constructionGrade = block.blockDefinition.GetGrade(grade, skin);
                if (constructionGrade == null || !constructionGrade.skinObject.isValid) return false;
                if (constructionGrade.gradeBase.type != grade || constructionGrade.gradeBase.skin != skin) return false;
                return GameManager.server.FindPrefab(constructionGrade.skinObject.resourcePath).HasComponent<ConstructionSkin>();
            }
        }

        public class BuildingOptionsAutoTurrets
        {
            [JsonProperty(PropertyName = en ? "Aim Cone" : "Угол прицеливания")]
            public float AimCone = 5f;

            [JsonProperty(PropertyName = en ? "Wait To Power On Until Event Starts" : "Ожидание включения до начала события")]
            public bool InitiateOnSpawn;

            [JsonProperty(PropertyName = en ? "Minimum Damage Modifier" : "Минимальный модификатор урона")]
            public float Min = 1f;

            [JsonProperty(PropertyName = en ? "Maximum Damage Modifier" : "Максимальный модификатор урона")]
            public float Max = 1f;

            [JsonProperty(PropertyName = en ? "Minimum Damage Modifier (NPC)" : "Минимальный модификатор урона (NPC)")]
            public float NpcMin = 1f;

            [JsonProperty(PropertyName = en ? "Maximum Damage Modifier (NPC)" : "Максимальный модификатор урона (NPC)")]
            public float NpcMax = 1f;

            [JsonProperty(PropertyName = en ? "Start Health" : "Начальное здоровье")]
            public float Health = 1000f;

            [JsonProperty(PropertyName = en ? "Sight Range" : "Дальность видимости")]
            public float SightRange = 30f;

            [JsonProperty(PropertyName = en ? "Double Sight Range When Shot" : "Двойная дальность видимости после выстрела")]
            public bool AutoAdjust;

            [JsonProperty(PropertyName = en ? "Set Hostile (False = Do Not Set Any Mode)" : "Установить враждебный режим (False = Не устанавливать никакой режим)")]
            public bool Hostile = true;

            [JsonProperty(PropertyName = en ? "Requires Power Source" : "Требуется источник питания")]
            public bool RequiresPower;

            [JsonProperty(PropertyName = en ? "Remove Equipped Weapon" : "Удалить экипированное оружие")]
            public bool RemoveWeapon;

            [JsonProperty(PropertyName = en ? "Random Weapons To Equip When Unequipped" : "Случайное оружие для экипировки при снятии", NullValueHandling = NullValueHandling.Ignore)]
            public List<string> _Shortnames = null;

            [JsonProperty(PropertyName = en ? "Random Weapons To Use When Unequipped" : "Случайное оружие, когда оно не экипировано", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<ulong>> Shortnames = new() { { "rifle.ak", new() { 0 } } };

            [JsonProperty(PropertyName = en ? "Remove Event Turrets For How Many Hours After Map Wipe" : "Сколько часов после вайпа удалять турели ивента?")]
            public double TurretHours;

            //[JsonProperty(PropertyName = en ? "Maximum Amount of Event Turrets" : "Максимальное количество турелей ивента")]
            //internal int MaxTurrets = -1;
        }

        public class BuildingOptionsPermissions
        {
            [JsonProperty(PropertyName = en ? "Buyable Events" : "Покупаемые События")]
            public string Buyable = "";

            [JsonProperty(PropertyName = en ? "Maintained Events" : "Поддерживаемых Событий")]
            public string Maintained = "";

            [JsonProperty(PropertyName = en ? "Scheduled Events" : "Запланированные События")]
            public string Scheduled = "";

            public void Register(RaidableBases instance, HarmonyPermissionHelper permission)
            {
                var buyable = Get(RaidableType.Purchased);

                if (!string.IsNullOrWhiteSpace(buyable) && !permission.PermissionExists(buyable))
                {
                    permission.RegisterPermission(buyable, instance);
                }

                var maintained = Get(RaidableType.Maintained);

                if (!string.IsNullOrWhiteSpace(maintained) && !permission.PermissionExists(maintained))
                {
                    permission.RegisterPermission(maintained, instance);
                }

                var scheduled = Get(RaidableType.Scheduled);

                if (!string.IsNullOrWhiteSpace(scheduled) && !permission.PermissionExists(scheduled))
                {
                    permission.RegisterPermission(scheduled, instance);
                }
            }

            public string Get(RaidableType type)
            {
                string permission = type switch
                {
                    RaidableType.Purchased => Buyable,
                    RaidableType.Maintained => Maintained,
                    RaidableType.Scheduled => Scheduled,
                    _ => null
                };

                return !string.IsNullOrWhiteSpace(permission) ? permission.Contains('.') ? permission : $"raidablebases.{permission}" : null;
            }

            private Dictionary<string, bool> _cache = new();

            public bool Has(BasePlayer player, RaidableType type)
            {
                if (player == null)
                {
                    return true;
                }

                string permission = Get(type);
                if (string.IsNullOrWhiteSpace(permission))
                {
                    return true;
                }

                string key = $"{player.UserIDString}_{type}";
                if (_cache.TryGetValue(key, out bool value))
                {
                    return value;
                }

                _cache[key] = player.HasPermission(permission.Contains('.') ? permission : $"raidablebases.{permission}");
                if (_cache.Count == 1) InvokeHandler.Instance.Invoke(_cache.Clear, 2f);
                return _cache[key];
            }
        }

        public class BuildingOptionsProtectionRadius
        {
            [JsonProperty(PropertyName = en ? "Buyable Events" : "Покупаемые События")]
            public float Buyable = 50f;

            [JsonProperty(PropertyName = en ? "Maintained Events" : "Поддерживаемых Событий")]
            public float Maintained = 50f;

            [JsonProperty(PropertyName = en ? "Manual Events" : "Ручные События")]
            public float Manual = 50f;

            [JsonProperty(PropertyName = en ? "Scheduled Events" : "Запланированные События")]
            public float Scheduled = 50f;

            [JsonProperty(PropertyName = en ? "Obstruction Distance Check" : "Проверка на препятствия")]
            public float Obstruction = -1f;

            public void Set(float value)
            {
                Buyable = Maintained = Manual = Scheduled = value;
            }

            public float Get(RaidableType type) => type switch
            {
                RaidableType.Purchased => Buyable,
                RaidableType.Maintained => Maintained,
                RaidableType.Scheduled => Scheduled,
                RaidableType.Manual => Manual,
                _ => Max()
            };

            public float Max() => Mathf.Max(Buyable, Maintained, Manual, Scheduled);

            public float Min() => Mathf.Min(Buyable, Maintained, Manual, Scheduled);

            public float Auto() => Mathf.Max(Maintained, Scheduled);
        }

        public class BuildingOptionsBradleySettings
        {
            [JsonProperty(PropertyName = en ? "Spawn Bradley When Base Spawns" : "Спаун Брэдли при создании базы")]
            public bool SpawnImmediately;

            [JsonProperty(PropertyName = en ? "Spawn Bradley When Base Is Completed" : "Спаун Брэдли, когда база завершена")]
            public bool SpawnCompleted;

            [JsonProperty(PropertyName = en ? "Chance To Spawn (Min)" : "Шанс спауна (мин)")]
            public float Min = 0.05f;

            [JsonProperty(PropertyName = en ? "Chance To Spawn (Max)" : "Шанс спауна (макс)")]
            public float Max = 0.1f;

            [JsonProperty(PropertyName = en ? "Health" : "Здоровье")]
            public float Health = 1000f;

            [JsonProperty(PropertyName = en ? "Bullet Damage" : "Урон от пули")]
            public float BulletDamage = 15f;

            [JsonProperty(PropertyName = en ? "Crates" : "Ящики")]
            public int Crates = 3;

            [JsonProperty(PropertyName = en ? "Sight Range" : "Дальность видимости")]
            public float SightRange = 100f;

            [JsonProperty(PropertyName = en ? "Double Sight Range When Shot" : "Двойная дальность видимости после выстрела")]
            public bool Vision = true;

            [JsonProperty(PropertyName = en ? "Splash Radius" : "Радиус взрыва")]
            public float Splash = 15f;
        }

        public class BuildingWaterOptions
        {
            [JsonProperty(PropertyName = en ? "Allow Bases To Float Above Water" : "Разрешить базам плавать над водой")]
            public bool AllowSubmerged;

            [JsonProperty(PropertyName = en ? "Chance For Underwater Bases To Spawn (0-100) (BETA - WORK IN PROGRESS)" : "Шанс появления подводных баз (0-100) (БЕТА - В РАЗРАБОТКЕ)")]
            public float Seabed;

            [JsonProperty(PropertyName = en ? "Spawn On The Surface Of Water" : "Нерест на поверхности воды")]
            public bool Surface;

            [JsonProperty(PropertyName = en ? "Ignore Land Level On Seabed" : "Игнорировать уровень земли на морском дне")]
            public bool IgnoreFlatTerrain;

            [JsonProperty(PropertyName = en ? "Prevent Bases From Floating Above Water By Also Checking Surrounding Area" : "Предотвращать плавание баз над водой, также проверяя окружающую область")]
            public bool SubmergedAreaCheck;

            [JsonProperty(PropertyName = en ? "Maximum Water Depth Level Used For Float Above Water Option" : "Максимальный уровень глубины воды, используемый для опции плавания над водой")]
            public float WaterDepth = 1f;

            [JsonProperty(PropertyName = en ? "Minimum Water Depth Level Used For Seabed Option" : "Минимальный уровень глубины воды, используемый для опции морского дна")]
            public float MinimumSeabedWaterDepth = -20f;

            [JsonProperty(PropertyName = en ? "Maximum Water Depth Level Used For Seabed Option" : "Максимальный уровень глубины воды, используемый для опции морского дна")]
            public float MaximumSeabedWaterDepth = -35f;

            [JsonProperty(PropertyName = en ? "Torpedo Damage Multiplier (Min)" : "Множитель урона торпеды (мин)")]
            public float TorpedoMin = 3f;

            [JsonProperty(PropertyName = en ? "Torpedo Damage Multiplier (Max)" : "Множитель урона торпеды (макс)")]
            public float TorpedoMax = 3f;

            internal float OceanLevel;
            internal bool IsWaterSpawn;

            internal CacheType FromCacheType => IsWaterSpawn ? CacheType.Seabed : CacheType.Generic;

            internal CacheType ToCacheType => IsWaterSpawn ? CacheType.Seabed2 : CacheType.Generic2;

            internal bool Random => Seabed > 0f && UnityEngine.Random.Range(0f, 100f) <= Seabed;
        }

        public class BuildingOptionsDifficultySpawns
        {
            [JsonProperty(PropertyName = en ? "Spawns Database File (Optional)" : "Файл базы данных спавнов (опционально)", NullValueHandling = NullValueHandling.Ignore)]
            public string _SpawnsFile = null;

            [JsonProperty(PropertyName = en ? "Buyable Spawns Database File (Optional)" : "События Файл базы данных спавнов (опционально)")]
            public string BuyableSpawnsFile = "none";

            [JsonProperty(PropertyName = en ? "Maintained Spawns Database File (Optional)" : "Поддерживаемых Файл базы данных спавнов (опционально)")]
            public string MaintainedSpawnsFile = "none";

            [JsonProperty(PropertyName = en ? "Scheduled Spawns Database File (Optional)" : "Запланированные Файл базы данных спавнов (опционально)")]
            public string ScheduledSpawnsFile = "none";

            [JsonProperty(PropertyName = en ? "Prevent Building Until Base Spawns" : "Запретить строительство до появления базы")]
            public bool PreventBuilding;

            [JsonProperty(PropertyName = en ? "Ignore Safe Checks" : "Игнорировать проверки безопасности")]
            public bool Ignore;

            [JsonProperty(PropertyName = en ? "Ignore Safe Checks In X Radius Only" : "Игнорировать проверки безопасности только в радиусе X")]
            public float SafeRadius;

            [JsonProperty(PropertyName = en ? "Ignore Player Entities At Custom Spawn Locations" : "Игнорировать игровые объекты игроков в пользовательских точках спавна")]
            public bool Skip;

            [JsonProperty(PropertyName = en ? "Kill Sleeping Bags" : "Уничтожать спальные мешки")]
            public bool KillSleepingBags = true;

            //[JsonProperty(PropertyName = en ? "Map Prefabs For Spawn Points" : "Префабы карты для точек спавна", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            //public List<string> SpawnPointPrefabs = new();

            [JsonProperty(PropertyName = en ? "Map Prefabs For Buyable Teleport" : "Префабы карты для покупного телепорта", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BuyableTeleportPrefabs = new();

            [JsonProperty(PropertyName = en ? "Time To Accept Buyable Teleport" : "Время для принятия покупного телепорта", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public float BuyableUiDuration = 60f;

            internal float BuyableTeleportRadius;

            internal List<Vector3> BuyableTeleportPositions = new();

            //internal List<Vector3> SpawnPointPositions = new();

            internal string Get(RaidableType type) => type switch { RaidableType.Purchased => BuyableSpawnsFile, RaidableType.Maintained => MaintainedSpawnsFile, RaidableType.Scheduled or _ => ScheduledSpawnsFile };

            public bool HasTeleportPositionAt(Vector3 from)
            {
                return BuyableTeleportRadius > 0f && BuyableTeleportPositions.Exists(v => InRange(from, v, BuyableTeleportRadius));
            }

            public bool GetBuyableTeleportPosition(Vector3 from, out Vector3 to)
            {
                return (to = BuyableTeleportPositions.FirstOrDefault(v => InRange(from, v, BuyableTeleportRadius))) != default;
            }

            public bool ShouldAdd(BaseProfile profile, ProtoBuf.PrefabData prefab, string fullname, Vector3 v)
            {
                if (BuyableTeleportPrefabs.Count > 0 && BuyableTeleportPrefabs.Exists(fullname.Contains))
                {
                    BuyableTeleportPositions.Add(v);
                    return true;
                }
                //if (SpawnPointPrefabs.Exists(fullname.Contains) || SpawnPointPrefabs.Exists(prefab.category.Contains))
                //{
                //    SpawnPointPositions.Add(v);
                //    profile.Spawns ??= new(profile.Instance, new());
                //    (v.y < WaterSystem.OceanLevel - 15f ? profile.Spawns.Seabed : profile.Spawns.Spawns).Add(new(v));
                //    return true;
                //}
                return false;
            }
        }

        public class BuildingOptionsRadiation
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Damage" : "Урон")]
            public float Damage = 1f;

            [JsonProperty(PropertyName = en ? "Rads" : "Радиация")]
            public float Rads = 2f;

            [JsonProperty(PropertyName = en ? "Protection Required" : "Требуется защита")]
            public float Protection = 6f;
        }

        public class BuildingOptionsEco
        {
            [JsonProperty(PropertyName = en ? "Allow Eco Raiding Only" : "Разрешить только эко-нападение")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Allow Flame Throwers" : "Разрешить огнеметы")]
            public bool FlameThrowers;

            [JsonProperty(PropertyName = en ? "Allow Bows" : "Разрешить луки")]
            public bool Bows = true;

            [JsonProperty(PropertyName = en ? "Allow Molotov Cocktails" : "Разрешить коктейли Молотова")]
            public bool Molotov = true;

            internal bool CanSpread(BaseEntity fireball) => fireball.ShortPrefabName switch
            {
                "flamethrower_fireball" when FlameThrowers => Enabled,
                "fireball_small_molotov" when Molotov => Enabled,
                "fireball_small_arrow" when Bows => Enabled,
                _ => false
            };
        }

        public class BuildingOptionsCommands
        {
            [JsonProperty(PropertyName = en ? "Commands" : "Команды", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Commands = new();

            [JsonProperty(PropertyName = en ? "Assign To Owner Of Raid Only" : "Начислять только владельцу рейда")]
            public bool Owner;

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled;

            public BuildingOptionsCommands()
            {
                Commands.Add("inventory.giveto {userid} apple 1");
                Commands.Add("o.usergroup add {userid} specialgroup");
            }
        }

        public class PlayerDamageMultiplier
        {
            [JsonProperty(PropertyName = en ? "Type" : "Тип")]
            public string Type;

            [JsonProperty(PropertyName = en ? "Min" : "Мин")]
            public float Min = 1f;

            [JsonProperty(PropertyName = en ? "Max" : "Макс")]
            public float Max = 1f;

            internal float amount => UnityEngine.Random.Range(Min, Max);

            internal DamageType[] _damageTypes;

            internal DamageType index => Array.Find(_damageTypes ??= (DamageType[])Enum.GetValues(typeof(DamageType)), type => type.ToString().Equals(Type, StringComparison.OrdinalIgnoreCase));

            public PlayerDamageMultiplier() { }

            public PlayerDamageMultiplier(string type, float min, float max)
            {
                (Type, Min, Max) = (type, min, max);
            }
        }

        public class AdditionalBaseOptions
        {
            [JsonProperty(PropertyName = en ? "CopyPaste Options" : "CopyPaste Options", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PasteOption> Options = new();

            [JsonProperty(PropertyName = en ? "Explosive Costs" : "Explosive Costs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AdditionalBaseCosts> Costs = new();

            internal bool Any => !Costs.IsNullOrEmpty() && Costs.Exists(x => x.Enabled);
        }

        public class AdditionalBaseCosts
        {
            [JsonProperty(PropertyName = en ? "Item Shortname" : "Сокращенное название предмета")]
            public string currencyToUse;

            [JsonProperty(PropertyName = en ? "Amount" : "Количество")]
            public int currencyAmount;

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled;
        }

        public class BuildingOptionsDrawContainers
        {
            [JsonProperty(PropertyName = en ? "Enabled (/rb hint)" : "Включено (/rb hint)")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Can bypass restrictions (vanish, noclip, raidablebases.canbypass)" : "Может обходить ограничения (vanish, noclip, raidablebases.canbypass)")]
            public bool CanBypass = true;

            [JsonProperty(PropertyName = en ? "Permission Required" : "Требуется разрешение")]
            public string Permission = "";

            [JsonProperty(PropertyName = en ? "Required Loot Percentage To See Loot Left" : "Требуемый процент добычи для отображения оставшегося лута")]
            public double RequiredLootPercentage = 85.0;

            [JsonProperty(PropertyName = en ? "Draw Time (Seconds)" : "Продолжительность розыгрыша (секунды)")]
            public float DrawTime = 15f;

            [JsonProperty(PropertyName = en ? "Max amount of containers to draw (0 = unlimited)" : "Максимальное количество контейнеров для отрисовки (0 = без ограничения)")]
            public int MaxContainersToDraw;

            [JsonProperty(PropertyName = en ? "Show Container Quantity" : "Показать количество в контейнере")]
            public bool ShowCupboardQuantity = true;

            [JsonProperty(PropertyName = en ? "Show Tool Cupboard In Yellow" : "Показать шкаф для инструментов желтым цветом")]
            public bool YellowCupboard = true;

            [JsonProperty(PropertyName = en ? "Show Tool Cupboard Only" : "Показывать только шкаф для инструментов")]
            public bool CupboardOnly;

            [JsonProperty(PropertyName = en ? "Cooldown (0 = No Cooldown)" : "Nерезарядки (0 = без перезарядки)")]
            public float Cooldown = 60f;

            [JsonProperty(PropertyName = en ? "Font Size" : "Размер шрифта")]
            public int FontSize = 32;

            public void Register(RaidableBases instance, HarmonyPermissionHelper permission)
            {
                if (!string.IsNullOrWhiteSpace(Permission) && Permission.StartsWith("raidablebases.") && !permission.PermissionExists(Permission))
                {
                    permission.RegisterPermission(Permission, instance);
                }
            }
        }

        public class SiegeSettings
        {
            [JsonProperty(PropertyName = en ? "Allow Siege Raiding Only" : "Разрешить осадный рейд только")]
            public bool Only;

            [JsonProperty(PropertyName = en ? "Damage Multiplier (Ballista)" : "Множитель урона (Ballista)")]
            public float BallistaMultiplier = 1f;

            [JsonProperty(PropertyName = en ? "Damage Multiplier (Cannon)" : "Множитель урона (Cannon)")]
            public float CannonMultiplier = 1f;
            
            [JsonProperty(PropertyName = en ? "Damage Multiplier (Catapult)" : "Множитель урона (Catapult)")]
            public float CatapultMultiplier = 1f;

            [JsonProperty(PropertyName = en ? "Damage Multiplier (Ram)" : "Множитель урона (Ram)")]
            public float RamMultiplier = 1f;

            internal bool Disabled;

            internal bool Any => BallistaMultiplier != 1 || CatapultMultiplier != 1 || RamMultiplier != 1 || CannonMultiplier != 1;

            public void Scale(BasePlayer attacker, HitInfo info, bool isHuman)
            {
                if (BallistaMultiplier != 1f && isHuman && !info.IsProjectile() && !(info.WeaponPrefab is TimedExplosive) && attacker.GetMounted() is BallistaGun)
                {
                    info.damageTypes.ScaleAll(BallistaMultiplier);
                }
                else if (CatapultMultiplier != 1f && info.WeaponPrefab != null && info.WeaponPrefab.ShortPrefabName.Contains("boulder_"))
                {
                    info.damageTypes.ScaleAll(CatapultMultiplier);
                }
                else if (RamMultiplier != 1f && info.WeaponPrefab is BatteringRam)
                {
                    info.damageTypes.ScaleAll(RamMultiplier);
                }
                else if (CannonMultiplier != 1f && info.WeaponPrefab is Cannon)
                {
                    info.damageTypes.ScaleAll(CannonMultiplier);
                }
            }

            public bool IsSiegeTool(BasePlayer attacker, HitInfo info, DamageType damageType)
            {
                if (info.WeaponPrefab is TimedExplosive te && te != null)
                {
                    return te.ShortPrefabName.Contains("boulder");
                }
                if (damageType.IsMeleeType() || info.WeaponPrefab is BaseSiegeWeapon or BallistaGun or Cannon)
                {
                    return true;
                }
                if (info.Weapon != null)
                {
                    Item weapon = info.Weapon.GetCachedItem();
                    if (weapon != null && weapon.info != null)
                    {
                        return weapon.info.IsAllowedInEra(EraRestriction.Default, Era.Primitive);
                    }
                }
                Item item = attacker.GetActiveItem();
                if (item != null && item.info != null)
                {
                    if (!item.info.IsAllowedInEra(EraRestriction.Default, Era.Primitive))
                    {
                        return false;
                    }
                    BaseProjectile projectile = item.GetHeldEntity() as BaseProjectile;
                    if (projectile == null || projectile.primaryMagazine == null || projectile.primaryMagazine.ammoType == null)
                    {
                        return true;
                    }
                    return projectile.primaryMagazine.ammoType.IsAllowedInEra(EraRestriction.Default, Era.Primitive);
                }
                return attacker.GetMounted() is BaseSiegeWeapon or BatteringRamSeat or BallistaGun;
            }

            public SiegeSettings() { }
        }

        public class BuildingOptions
        {
            public BuildingOptions() { }

            public BuildingOptions(string mode, int level)
            {
                string[] bases = new string[] { $"{mode}Base1", $"{mode}Base2", $"{mode}Base3", $"{mode}Base4", $"{mode}Base5" };
                (Mode, Level, PasteOptions, AdditionalBases) = (mode, level, DefaultPasteOptions(), bases.ToDictionary(value => value, _ => DefaultBaseOptions()));
            }

            [JsonProperty(PropertyName = en ? "Difficulty (0 = easy, 1 = medium, 2 = hard, 3 = expert, 4 = nightmare)" : "Сложность (0 = легкий, 1 = cредний, 2 = сложно, 3 = эксперт, 4 = кошмарный)", NullValueHandling = NullValueHandling.Ignore)]
            public string ObsoleteMode = null;

            [JsonProperty(PropertyName = en ? "Difficulty" : "Сложность")]
            public string Mode = string.Empty;

            [JsonProperty(PropertyName = en ? "Difficulty Level" : "Уровень сложности")]
            public int Level = -1;

            [JsonProperty(PropertyName = en ? "Allow Players To Build" : "Разрешить строить игрокам")]
            public bool AllowBuilding = true;

            [JsonProperty(PropertyName = en ? "Allow Players To Build (Exclusions)" : "Разрешить строить игрокам (Исключительные объекты, даже если строить - false)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AllowedBuildingBlockExceptions = new();

            [JsonProperty(PropertyName = en ? "Enable Profile For Buyable Bases Plugin" : "Включить профиль для плагина Buyable Bases")]
            public bool BuyableBase;

            [JsonProperty(PropertyName = en ? "Loot Hints" : "Loot Hints")]
            public BuildingOptionsDrawContainers DrawLoot = new();

            [JsonProperty(PropertyName = en ? "Allow Raid Bases In Biomes" : "Спавнить рейд-базы по разным биомам")]
            public ManagementBiomeSettings Biomes = null;

            [JsonProperty(PropertyName = en ? "Blacklisted Commands (PVE)" : "Черный Список Команд (PVE)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistedPVECommands = new();

            [JsonProperty(PropertyName = en ? "Blacklisted Commands (PVP)" : "Черный Список Команд (PVP)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistedPVPCommands = new();

            [JsonProperty(PropertyName = en ? "Commands To Run With Assign Rank After X Completions" : "Команды для выполнения с присвоением ранга после X завершений")]
            public BuildingOptionsCommands EventRankedAwards = new();

            [JsonProperty(PropertyName = en ? "Commands To Run On Event Completion" : "Команды для выполнения при завершении события")]
            public BuildingOptionsCommands EventCompletion = new();

            [JsonProperty(PropertyName = en ? "Permission Required To Enter" : "Требуется разрешение для входа")]
            public BuildingOptionsPermissions Permission = new();

            [JsonProperty(PropertyName = en ? "Advanced Protection Radius" : "Расширенный радиус защиты")]
            public BuildingOptionsProtectionRadius ProtectionRadii = new();

            [JsonProperty(PropertyName = en ? "Advanced Setup Settings" : "Расширенные настройки установки")]
            public BuildingOptionsSetupSettings Setup = new();

            [JsonProperty(PropertyName = en ? "Despawn Options Override" : "Переопределение параметров исчезновения")]
            public ProfileDespawnOptions DespawnOptions = new();

            [JsonProperty(PropertyName = en ? "Eject Mounts" : "Не допускать транспортные средства")]
            public ManagementMountableSettings Mounts = new();

            [JsonProperty(PropertyName = en ? "Elevators" : "Лифты")]
            public BuildingOptionsElevators Elevators = new();

            [JsonProperty(PropertyName = en ? "Entities Not Allowed To Be Damaged" : "Сущности, не подлежащие повреждению", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlockedEntityDamage = new();

            [JsonProperty(PropertyName = en ? "Entities Not Allowed To Be Picked Up" : "Сущности, не подлежащие поднятию", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistedPickupItems = new() { "generator.small", "generator.static", "autoturret_deployed" };

            [JsonProperty(PropertyName = en ? "Entities Allowed To Be Picked Up" : "Сущности, подлежащие поднятию", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> WhitelistedPickupItems = new() { "shutter" };

            [JsonProperty(PropertyName = en ? "Additional Bases For This Difficulty" : "Дополнительные базы для данной сложности", ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, List<PasteOption>> _AdditionalBases = null;

            [JsonProperty(PropertyName = en ? "Additional Bases" : "Дополнительные базы", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, AdditionalBaseOptions> AdditionalBases = new();

            [JsonProperty(PropertyName = en ? "Paste Options" : "Параметры вставки", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PasteOption> PasteOptions = new();

            [JsonProperty(PropertyName = en ? "Arena Walls" : "Стены арены")]
            public RaidableBaseWallOptions ArenaWalls = new();

            [JsonProperty(PropertyName = en ? "Eco Raiding" : "Эко-рейды")]
            public BuildingOptionsEco Eco = new();

            [JsonProperty(PropertyName = en ? "NPC Levels" : "Уровни NPC")]
            public BuildingLevels Levels = new();

            [JsonProperty(PropertyName = en ? "NPCs" : "NPC")]
            public NpcSettings NPC = new();

            [JsonProperty(PropertyName = en ? "Rewards" : "Награды")]
            public RewardSettings Rewards = new();

            [JsonProperty(PropertyName = en ? "Change Building Material Tier To" : "Изменить уровень материала здания на")]
            public BuildingGradeLevelsSkins Blocks = new();

            [JsonProperty(PropertyName = en ? "Change Door Type To" : "Изменить тип двери на")]
            public DoorTypes Doors = new();

            [JsonProperty(PropertyName = en ? "Player Damage To Base Multipliers" : "Множители урона игроков по базе", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PlayerDamageMultiplier> PlayerDamageMultiplier = new()
            {
                new("Arrow", 1f, 1f),
                new("Blunt", 1f, 1f),
                new("Bullet", 1f, 1f),
                new("Heat", 1f, 1f),
                new("Explosion", 1f, 1f),
                new("Slash", 1f, 1f),
                new("Stab", 1f, 1f),
            };

            [JsonProperty(PropertyName = "Siege")]
            public SiegeSettings Siege = new();

            [JsonProperty(PropertyName = en ? "Player Damage To Tool Cupboard Multiplier" : "Множитель Урона Игрока По Инструментальному Шкафу")]
            public float PlayerDamageMultiplierTC = 1f;

            [JsonProperty(PropertyName = en ? "Auto Turrets" : "Автоматические турели")]
            public BuildingOptionsAutoTurrets AutoTurret = new();

            [JsonProperty(PropertyName = en ? "Player Building Restrictions" : "Ограничения на строительство игроков")]
            public BuildingGradeLevels BuildingRestrictions = new();

            [JsonProperty(PropertyName = en ? "Water Settings" : "Настройки воды")]
            public BuildingWaterOptions Water = new();

            [JsonProperty(PropertyName = en ? "Spawns Database" : "База данных спавнов")]
            public BuildingOptionsDifficultySpawns CustomSpawns = new();

            [JsonProperty(PropertyName = en ? "Radiation" : "Радиация")]
            public BuildingOptionsRadiation Radiation = new();

            [JsonProperty(PropertyName = en ? "Sam Site" : "Зенитная установка САМ")]
            public WeaponSettingsSamSite SamSite = new();

            [JsonProperty(PropertyName = en ? "Sphere Colors (0 None, 1 Blue, 2 Cyan, 3 Green, 4 Magenta, 5 Purple, 6 Red, 7 Yellow)" : "Цвета сфер (0 Нет, 1 Синий, 2 Голубой, 3 Зеленый, 4 Пурпурный, 5 Фиолетовый, 6 Красный, 7 Желтый)")]
            public SphereColorSettings SphereColor = new();

            [JsonProperty(PropertyName = en ? "Tesla Coil" : "Тесла-катушка")]
            public WeaponSettingsTeslaCoil TeslaCoil = new();

            [JsonProperty(PropertyName = en ? "IQDronePatrol : Setting up for spawn drones on raid bases" : "IQDronePatrol : Настройка для спавна дронов на рейдбазах")]
            public IQDronePatrolSettings DronePatrols = new();

            [JsonProperty(PropertyName = en ? "Profile Enabled" : "Профиль включен")]
            public bool Enabled = true;

            [DefaultValue(2.5f)]
            [JsonProperty(PropertyName = en ? "Maximum Land Level" : "Максимальный уровень земли", DefaultValueHandling = DefaultValueHandling.Include)]
            public float LandLevel = 2.5f;

            internal float GetLandLevel => Mathf.Clamp(LandLevel, 0.5f, 3f);

            [JsonProperty(PropertyName = en ? "Allow Players To Use MLRS" : "Разрешить игрокам использовать МЛРС")]
            public bool MLRS = true;

            [JsonProperty(PropertyName = en ? "Allow Third-Party Npc Explosive Damage To Bases" : "Разрешить NPC сторонний взрывной урон по базам")]
            public bool RaidingNpcs;

            [JsonProperty(PropertyName = en ? "Add Code Lock To Unlocked Or KeyLocked Doors" : "Добавить кодовый замок к открытым или замкнутым дверям с ключом")]
            public bool CodeLockDoors = true;

            [JsonProperty(PropertyName = en ? "Add Key Lock To Unlocked Or CodeLocked Doors" : "Добавить ключевой замок к открытым или дверям с кодовым замком")]
            public bool KeyLockDoors;

            [JsonProperty(PropertyName = en ? "Add Code Lock To Tool Cupboards" : "Добавить кодовый замок к сундукам с инструментами")]
            public bool CodeLockPrivilege;

            [JsonProperty(PropertyName = en ? "Add Key Lock To Tool Cupboards" : "Добавить ключевой замок к сундукам с инструментами")]
            public bool KeyLockPrivilege;

            [JsonProperty(PropertyName = en ? "Add Code Lock To Boxes" : "Добавить кодовый замок к ящикам")]
            public bool CodeLockBoxes;

            [JsonProperty(PropertyName = en ? "Add Key Lock To Boxes" : "Добавить ключевой замок к ящикам")]
            public bool KeyLockBoxes;

            [JsonProperty(PropertyName = en ? "Add Code Lock To Lockers" : "Добавить кодовый замок к шкафам")]
            public bool CodeLockLockers = true;

            [JsonProperty(PropertyName = en ? "Add Key Lock To Lockers" : "Добавить ключевой замок к шкафам")]
            public bool KeyLockLockers;

            [JsonProperty(PropertyName = en ? "Close Open Doors With No Door Controller Installed" : "Закрыть открытые двери без установленного контроллера дверей")]
            public bool CloseOpenDoors = true;

            [JsonProperty(PropertyName = en ? "Allow Duplicate Items" : "Разрешить дублирование предметов")]
            public bool AllowDuplicates;

            [JsonProperty(PropertyName = en ? "Allow Players To Pickup Deployables" : "Разрешить игрокам поднимать размещаемые предметы")]
            public bool AllowPickup;

            [JsonProperty(PropertyName = en ? "Allow Players To Deploy A Cupboard" : "Разрешить игрокам размещать шкафы")]
            public bool AllowBuildingPriviledges = true;

            [JsonProperty(PropertyName = en ? "Allow Players To Deploy Barricades" : "Разрешить игрокам размещать баррикады")]
            public bool AllowBarricades = true;

            [JsonProperty(PropertyName = en ? "Allow PVP" : "Разрешить PVP")]
            public bool AllowPVP = true;

            [JsonProperty(PropertyName = en ? "Allow Self Damage" : "Разрешить наносить урон себе")]
            public bool AllowSelfDamage = true;

            [JsonProperty(PropertyName = en ? "Allow Friendly Fire (Teams)" : "Разрешить дружественный огонь (команды)")]
            public bool AllowFriendlyFire = true;

            [JsonProperty(PropertyName = en ? "Check Lower Probability Once Per Loot Item" : "Проверять более низкую вероятность один раз для каждого дропа")]
            public bool EnforceProbability;

            [JsonProperty(PropertyName = en ? "Amount Of Items To Spawn For Buyable Events (0 = Use Default Value)" : "Количество предметов для спавна для купленных событий (0 = использовать значение по умолчанию)")]
            public int MaxBuyableTreasure = 0;

            [JsonProperty(PropertyName = en ? "Minimum Amount Of Items To Spawn (0 = Use Max Value)" : "Минимальное количество предметов для спавна (0 = использовать максимальное значение)")]
            public int MinTreasure;

            [JsonProperty(PropertyName = en ? "Amount Of Items To Spawn" : "Количество предметов для спавна")]
            public int MaxTreasure = 30;

            [JsonProperty(PropertyName = en ? "Amount Of Items To Spawn Increased By Item Splits" : "Увеличивать количество предметов для спавна из-за разделения предметов")]
            public bool Dynamic;

            [JsonProperty(PropertyName = en ? "Use Primitive Loot From The Current Era" : "Использовать примитивный лут из текущей эпохи")]
            public bool Primitive;

            [JsonProperty(PropertyName = en ? "Flame Turret Health" : "Здоровье огненной турели")]
            public float FlameTurretHealth = 300f;

            [JsonProperty(PropertyName = en ? "Briefly Holster Weapon To Prevent Camping The Entrance Of Events" : "Кратковременно уберите оружие в кобуру, чтобы предотвратить кемпинг у входа на мероприятия")]
            public bool Holster { get; set; }

            [JsonProperty(PropertyName = en ? "Block Plugins Which Prevent Item Durability Loss" : "Блокировать плагины, которые предотвращают потерю прочности предметов", NullValueHandling = NullValueHandling.Ignore)]
            public bool? _EnforceDurability = null;

            [JsonProperty(PropertyName = en ? "Items Always Take Condition Loss And Cannot Be Repaired" : "Предметы всегда теряют прочность и не могут быть отремонтированы")]
            public bool EnforceConditionLoss;

            [JsonProperty(PropertyName = en ? "Block Damage To Players From Player Turrets Deployed Outside Of The Dome" : "Блокировать урон игрокам от турелей, расположенных за пределами купола")]
            public bool BlockOutsideTurrets;

            [JsonProperty(PropertyName = en ? "Block Damage Outside Of The Dome To Players Inside" : "Блокировать урон снаружи купола по игрокам внутри")]
            public bool BlockOutsideDamageToPlayersInside;

            [JsonProperty(PropertyName = en ? "Block Damage Outside Of The Dome To Bases Inside" : "Блокировать урон снаружи купола по базам внутри")]
            public bool BlockOutsideDamageToBaseInside;

            [JsonProperty(PropertyName = en ? "Block Damage Inside From Npcs To Players Outside" : "Блокировать урон снаружи купола от NPC по игрокам внутри")]
            public bool BlockNpcDamageToPlayersOutside;

            [JsonProperty(PropertyName = en ? "Building Blocks Are Immune To Damage" : "Строительные блоки устойчивы к урону")]
            public bool BlocksImmune;

            [JsonProperty(PropertyName = en ? "Building Blocks Are Immune To Damage (Twig Only)" : "Строительные блоки устойчивы к урону (только опора)")]
            public bool TwigImmune;

            [JsonProperty(PropertyName = en ? "Turrets Can Hurt Event Twig" : "Автоматические турели могут повредить ветку событий")]
            public bool TurretsHurtTwig;

            [JsonProperty(PropertyName = en ? "Boxes Are Invulnerable" : "Ящики неуязвимы")]
            public bool Invulnerable;

            [JsonProperty(PropertyName = en ? "Boxes Are Invulnerable Until Cupboard Is Destroyed" : "Ящики неуязвимы, пока не уничтожен шкаф")]
            public bool InvulnerableUntilCupboardIsDestroyed;

            [JsonProperty(PropertyName = en ? "Spawn Silently (No Notifcation, No Dome, No Map Marker)" : "Бесшумный спавн (нет уведомлений, нет купола, нет маркера на карте)")]
            public bool Silent;

            [JsonProperty(PropertyName = en ? "Hide Despawn Time On Map Marker (PVP)" : "Скрыть время деспауна на маркере карты (PvP)")]
            public bool HideDespawnTimePVP;

            [JsonProperty(PropertyName = en ? "Hide Despawn Time On Map Marker (PVE)" : "Скрыть время деспауна на маркере карты (PvE)")]
            public bool HideDespawnTimePVE;

            [JsonProperty(PropertyName = en ? "Use Simple Messaging" : "Использовать простые сообщения")]
            public bool Smart;

            [JsonProperty(PropertyName = en ? "Despawn Dropped Loot Bags From Raid Boxes When Base Despawns" : "Убирать сумки с добычей из ящиков при исчезновении базы")]
            public bool DespawnGreyBoxBags;

            [JsonProperty(PropertyName = en ? "Despawn Dropped Loot Bags From Npc When Base Despawns" : "Убирать сумки с добычей из NPC при исчезновении базы")]
            public bool DespawnGreyNpcBags;

            [JsonProperty(PropertyName = en ? "Protect Loot Bags From Raid Boxes For X Seconds After Base Despawns" : "Защищать сумки с добычей от ящиков при рейдах в течение X секунд после исчезновения базы")]
            public float PreventLooting;

            [JsonProperty(PropertyName = en ? "Divide Loot Into All Containers" : "Распределять добычу по всем контейнерам")]
            public bool DivideLoot = true;

            [JsonProperty(PropertyName = en ? "Drop Tool Cupboard Loot After Raid Is Completed" : "Выбрасывать добычу из шкафа инструментов после завершения рейда")]
            public bool DropPrivilegeLoot;

            [JsonProperty(PropertyName = en ? "Drop Container Loot X Seconds After It Is Looted" : "Выбрасывать добычу из контейнера через X секунд после его обчистки")]
            public float DropTimeAfterLooting;

            [JsonProperty(PropertyName = en ? "Drop Container Loot Applies Only To Boxes And Cupboards" : "Выбрасывать добычу из контейнеров только из ящиков и шкафов")]
            public bool DropOnlyBoxesAndPrivileges = true;

            [JsonProperty(PropertyName = en ? "Create Dome Around Event Using Spheres (0 = disabled, recommended = 5)" : "Создавать купол вокруг события с использованием сфер (0 = отключено, рекомендуется = 5)")]
            public int SphereAmount = 5;

            [JsonProperty(PropertyName = en ? "Empty All Containers Before Spawning Loot" : "Очищать все контейнеры перед появлением добычи")]
            public bool EmptyAll = true;

            [JsonProperty(PropertyName = en ? "Empty All Containers (Exclusions)" : "Очищать все контейнеры (исключения)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> EmptyExemptions = new() { "xmas_tree.deployed", "xmas_tree_a.deployed", "torchholder.deployed" };

            [JsonProperty(PropertyName = en ? "Eject Corpses From Enemy Raids (Advanced Users Only)" : "Изгонять трупы из рейдов врагов (только для опытных пользователей)")]
            public bool EjectBackpacks = true;

            [JsonProperty(PropertyName = en ? "Eject Corpses From PVE Instantly (Advanced Users Only)" : "Мгновенно изгонять трупы из PvE (только для опытных пользователей)")]
            public bool EjectBackpacksPVE;

            [JsonProperty(PropertyName = en ? "Eject Enemies From Purchased PVE Raids" : "Изгонять врагов из купленных PvE рейдов")]
            public bool EjectPurchasedPVE = true;

            [JsonProperty(PropertyName = en ? "Eject Enemies From Purchased PVP Raids" : "Изгонять врагов из купленных PvP рейдов")]
            public bool EjectPurchasedPVP;

            [JsonProperty(PropertyName = en ? "Eject Enemies From Locked PVE Raids" : "Изгонять врагов из закрытых PvE рейдов")]
            public bool EjectLockedPVE = true;

            [JsonProperty(PropertyName = en ? "Eject Enemies From Locked PVP Raids" : "Изгонять врагов из закрытых PvP рейдов")]
            public bool EjectLockedPVP;

            [JsonProperty(PropertyName = en ? "Eject Tree Radius When Spawning Base" : "Радиус изгнания деревьев при появлении базы")]
            public float TreeRadius;

            [JsonProperty(PropertyName = en ? "Delete Tree Radius When Spawning Base" : "Удалить радиус дерева при создании базы")]
            public float DeleteRadius;

            [JsonProperty(PropertyName = en ? "Respawn Deleted Trees After Despawning Base" : "Возродить удаленные деревья после деспауне базы")]
            public bool RespawnTrees = true;

            [JsonProperty(PropertyName = en ? "Explosion Damage Modifier (0-999)" : "Модификатор урона от взрыва (0-999)")]
            public float ExplosionModifier = 100f;

            [JsonProperty(PropertyName = en ? "Ignore Containers That Spawn With Loot Already" : "Игнорировать контейнеры, которые появляются уже с добычей")]
            public bool IgnoreContainedLoot;

            [JsonProperty(PropertyName = en ? "Loot Amount Multiplier" : "Множитель количества добычи")]
            public float Multiplier = 1f;

            [JsonProperty(PropertyName = en ? "Loot Amount Multiplier (raidablebases.buyable.vip.pve)" : "Множитель количества добычи (raidablebases.buyable.vip.pve)")]
            public float MultiplierPVE = 1f;

            [JsonProperty(PropertyName = en ? "Loot Amount Multiplier (raidablebases.buyable.vip.pvp)" : "Множитель количества добычи (raidablebases.buyable.vip.pvp)")]
            public float MultiplierPVP = 1f;

            [JsonProperty(PropertyName = en ? "Maximum Respawn Npc X Seconds After Death" : "Максимальное время возрождения NPC после смерти (в секундах)")]
            public float RespawnRateMax;

            [JsonProperty(PropertyName = en ? "Minimum Respawn Npc X Seconds After Death" : "Минимальное время возрождения NPC после смерти (в секундах)")]
            public float RespawnRateMin;

            [JsonProperty(PropertyName = en ? "No Item Input For Boxes And TC" : "Запрет складирования предметов в ящики")]
            public bool NoItemInput = true;

            [JsonProperty(PropertyName = en ? "Penalize Players On Death In PVE (ZLevels)" : "Наказывать игроков при смерти в PvE (ZLevels)")]
            public bool PenalizePVE = true;

            [JsonProperty(PropertyName = en ? "Penalize Players On Death In PVP (ZLevels)" : "Наказывать игроков при смерти в PvP (ZLevels)")]
            public bool PenalizePVP = true;

            [JsonProperty(PropertyName = en ? "Require Cupboard Access To Loot" : "Требовать доступ к шкафу для добычи")]
            public bool RequiresCupboardAccess;

            [JsonProperty(PropertyName = en ? "Require Cupboard Access To Place Ladders" : "Требовать доступ к шкафу для установки лестниц")]
            public bool RequiresCupboardAccessLadders;

            [JsonProperty(PropertyName = en ? "Skip Treasure Loot And Use Loot In Base Only" : "Пропускать добычу из сокровищ и использовать только добычу в базе")]
            public bool SkipTreasureLoot;

            [JsonProperty(PropertyName = en ? "Use Buoyant Boxes For Dropped Privilege Loot" : "Использовать плавучие ящики для выбрасываемой добычи из привилегированных шкафов")]
            public bool BuoyantPrivilege;

            [JsonProperty(PropertyName = en ? "Use Buoyant Boxes For Dropped Box Loot" : "Использовать плавучие ящики для выбрасываемой добычи из ящиков")]
            public bool BuoyantBox;

            [JsonProperty(PropertyName = en ? "Rearm Bear Traps When Damaged" : "Повторно вооружать капканы при повреждении")]
            public bool RearmBearTraps;

            [JsonProperty(PropertyName = en ? "Bear Traps Are Immune To Timed Explosives" : "Капканы устойчивы к взрывчатым устройствам с таймером")]
            public bool BearTrapsImmuneToExplosives;

            [JsonProperty(PropertyName = en ? "Force Time In Dome To (requires raidablebases.time)" : "Принудительно установить время в куполе на (требуется raidablebases.time)")]
            public int ForcedTime = -1;

            [JsonProperty(PropertyName = en ? "Remove Locks When Event Is Completed" : "Удалять замки после завершения события")]
            public bool UnlockEverything;

            [JsonProperty(PropertyName = en ? "Required Loot Percentage For Rewards" : "Конвертация Процента Необходимой Добычи Для Наград")]
            public double RequiredLootPercentage;

            [JsonProperty(PropertyName = en ? "Each Player Must Destroy An Entity For Reward Eligibility" : "Каждый игрок должен уничтожить объект для получения награды")]
            public bool RequiredDestroyEntity;

            [JsonProperty(PropertyName = en ? "Always Spawn Base Loot Table" : "Всегда генерировать базовую таблицу добычи")]
            public bool AlwaysSpawnBaseLoot;

            public BuildingOptions Clone() => MemberwiseClone() as BuildingOptions;

            public float ProtectionRadius(RaidableType type) => Mathf.Max(CELL_SIZE, ProtectionRadii.Get(type));

            public int GetLootAmount(RaidableType type)
            {
                int maxTreasure = type == RaidableType.Purchased && MaxBuyableTreasure > 0 ? MaxBuyableTreasure : MaxTreasure;
                return MinTreasure > 0 ? UnityEngine.Random.Range(MinTreasure, maxTreasure + 1) : maxTreasure;
            }
        }

        public class RaidableBaseSettingsEventTypeBase
        {
            [JsonProperty(PropertyName = en ? "Convert PVE To PVP" : "Преобразовать PVE в PVP")]
            public bool ConvertPVE;

            [JsonProperty(PropertyName = en ? "Convert PVP To PVE" : "Преобразовать PVP в PVE")]
            public bool ConvertPVP = true;

            [JsonProperty(PropertyName = en ? "Ignore Safe Checks" : "Игнорировать проверки безопасности")]
            public bool Ignore;

            [JsonProperty(PropertyName = en ? "Ignore Safe Checks In X Radius Only" : "Игнорировать проверки безопасности только в радиусе X")]
            public float SafeRadius;

            [JsonProperty(PropertyName = en ? "Ignore Player Entities At Custom Spawn Locations" : "Игнорировать игровые объекты игроков в пользовательских точках спавна")]
            public bool Skip;

            [JsonProperty(PropertyName = en ? "Spawn Bases X Distance Apart" : "Расстояние между спавнами баз (X)")]
            public float Distance = 100f;

            [JsonProperty(PropertyName = en ? "Spawns Database File (Optional)" : "Файл базы данных спавнов (опционально)")]
            public string SpawnsFile = "none";
        }

        public class EventTypeBaseExtendedSettings : RaidableBaseSettingsEventTypeBase
        {
            [JsonProperty(PropertyName = en ? "Enable X Hours After Wipe (0 = immediately)" : "Включить через X часов после вайпа (0 = сразу)")]
            public DifficultyModesDouble Wipe = new(en ? "Enable X Hours After Wipe (0 = immediately)" : "Включить через X часов после вайпа (0 = сразу)");

            [JsonProperty(PropertyName = en ? "Chance To Randomly Spawn PVP Bases (0 = Ignore Setting)" : "Шанс случайного спавна PvP баз (0 = Игнорировать настройку)")]
            public decimal Chance;

            [JsonProperty(PropertyName = en ? "Include PVE Bases" : "Включать PvE базы")]
            public bool IncludePVE = true;

            [JsonProperty(PropertyName = en ? "Include PVP Bases" : "Включать PvP базы")]
            public bool IncludePVP = true;

            [JsonProperty(PropertyName = en ? "Minimum Required Players Online" : "Минимальное количество игроков онлайн")]
            public int PlayerLimitMin = 0;

            [JsonProperty(PropertyName = en ? "Maximum Limit Of Players Online" : "Максимальное количество игроков онлайн")]
            public int PlayerLimitMax = 300;

            [JsonProperty(PropertyName = en ? "Permission To Ignore With Players Online Limit" : "Разрешение игнорировать с ограничением на количество игроков онлайн")]
            public string PlayerLimitPermission = "";

            [JsonProperty(PropertyName = en ? "Time To Wait Between Spawns" : "Время ожидания между спавнами")]
            public float Time = 15f;

            public int GetPlayerCount()
            {
                return string.IsNullOrWhiteSpace(PlayerLimitPermission) ? BasePlayer.activePlayerList.Count : BasePlayer.activePlayerList.Count(x => PlayerLimitPermission.Contains('.') ? !x.HasPermission(PlayerLimitPermission) : !x.BelongsToGroup(PlayerLimitPermission));
            }
        }

        public class ScheduledSettings : EventTypeBaseExtendedSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Every Min Seconds" : "Каждые минимальные секунды")]
            public double IntervalMin = 3600;

            [JsonProperty(PropertyName = en ? "Every Max Seconds" : "Каждые максимальные секунды")]
            public double IntervalMax = 7200;

            [JsonProperty(PropertyName = en ? "Max Scheduled Events" : "Максимальное количество запланированных событий")]
            public int MaxInt = 3;

            [JsonProperty(PropertyName = en ? "Max Scheduled Events (When Deep Sea Is Open)" : "Максимальное количество запланированных событий (When Deep Sea Is Open)")]
            public int MaxDeepSea = -1;

            [JsonProperty(PropertyName = en ? "Max To Spawn At Once (0 = Use Max Scheduled Events Amount)" : "Максимум для одновременного спавна (0 = Использовать максимальное количество запланированных событий)")]
            public int MaxOnce;

            internal int Max => MaxDeepSea == -1 ? MaxInt : (IsDeepSeaOpen() ? MaxDeepSea : MaxInt);
        }

        public class MaintainedSettings : EventTypeBaseExtendedSettings
        {
            [JsonProperty(PropertyName = en ? "Always Maintain Max Events" : "Всегда поддерживать максимальное количество событий")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Max Maintained Events" : "Максимальное количество поддерживаемых событий")]
            public int MaxInt = 3;

            [JsonProperty(PropertyName = en ? "Max Maintained Events (When Deep Sea Is Open)" : "Максимальное количество поддерживаемых событий (When Deep Sea Is Open)")]
            public int MaxDeepSea = -1;

            internal int Max => MaxDeepSea == -1 ? MaxInt : (IsDeepSeaOpen() ? MaxDeepSea : MaxInt);
        }

        public class BuyableCooldownDifficultySettings
        {
            [JsonProperty(PropertyName = en ? "VIP Permission: raidablebases.vipcooldown" : "VIP Разрешение: raidablebases.vipcooldown")]
            public double VIP = 1800;

            [JsonProperty(PropertyName = en ? "Admin Permission: raidablebases.allow" : "Админ Разрешение: raidablebases.allow")]
            public double Allow;

            [JsonProperty(PropertyName = en ? "Server Admins" : "Серверные администраторы")]
            public double Admin;

            [JsonProperty(PropertyName = en ? "Normal Users" : "Обычные пользователи")]
            public double Cooldown = 1800;
        }

        public class BuyableCooldownResetCosts
        {
            [JsonProperty(PropertyName = en ? "Custom Currency" : "Пользовательская валюта")]
            public CustomCostOptions Custom = new(0);

            [JsonProperty(PropertyName = en ? "Economics Money" : "Деньги Economics")]
            public double Money;

            [JsonProperty(PropertyName = en ? "ServerRewards Points" : "Очки ServerRewards")]
            public int Points;

            internal bool Any => Money > 0 || Points > 0 || Custom.isItem;
        }

        public class BuyableCooldownSettings : ConfigurationExtension<BuyableCooldownDifficultySettings>
        {
            public BuyableCooldownSettings() : base(en ? "Cooldowns (0 = No Cooldown)" : "Перезарядки (0 = без перезарядки)", RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }

            [JsonProperty(PropertyName = en ? "Reset Cooldown Costs" : "Стоимость сброса времени ожидания")]
            public BuyableCooldownResetCosts Costs = new();

            [JsonProperty(PropertyName = en ? "Apply Cooldown To Entire Clan And Team" : "Применить ограничение времени ожидания ко всему клану и команде")]
            public bool ApplyAlly;

            [JsonProperty(PropertyName = en ? "Apply All Cooldowns" : "Применить все ограничения времени ожидания")]
            public bool ApplyAll;

            [JsonProperty(PropertyName = en ? "Apply Cooldown When Rewards Are Given" : "Подавайте заявку, когда выдаются вознаграждения.")]
            public bool ApplyOnRewards = true;

            [JsonProperty(PropertyName = en ? "Cooldown Override Applied To All Other Difficulties" : "Переопределение перезарядки для всех остальных уровней сложности")]

            public float OtherOverrideCooldown;

            public bool Any() => Dictionary.Count > 0 && Dictionary.All(x => x.Value != null);

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    modes.ForEach(mode => TryAdd(mode, new()));
                    return Any();
                }
                return false;
            }

            public bool Has(StoredData data, ulong userid, string mode) => Process(data, userid, mode, false);

            public void Set(RaidableBases m, HashSet<ulong> alliance, ulong userid, string mode, bool set)
            {
                HashSet<ulong> members = new() { userid };
                if (ApplyAlly)
                {
                    members.UnionWith(alliance);
                    members.UnionWith(m.GetMembers(userid));
                }

                foreach (var member in members.ToList())
                {
                    using var modes = DisposableList<string>();
                    modes.AddRange(ApplyAll ? m.GetRaidableModes() : new[] { mode });

                    foreach (string other in modes)
                    {
                        if (set && other != mode && OtherOverrideCooldown > 0)
                        {
                            if (!m.data.BuyableCooldowns.TryGetValue(member, out var info))
                            {
                                m.data.BuyableCooldowns[member] = info = new();
                            }

                            var newExpiry = DateTime.Now.AddSeconds(OtherOverrideCooldown);
                            if (!info.Modes.TryGetValue(other, out var oldExpiry) || newExpiry > oldExpiry)
                            {
                                info.Modes[other] = newExpiry;
                            }
                            continue;
                        }

                        if (Process(m.data, member, other, set))
                        {
                            members.Add(member);
                            alliance.Add(member);
                        }
                    }
                }

                m.UpdateUI();
            }

            private Dictionary<string, bool> _cache = new();

            private bool Cache(string key, bool value)
            {
                _cache[key] = value;
                if (_cache.Count == 1) InvokeHandler.Instance.Invoke(_cache.Clear, 2f);
                return value;
            }

            private bool Process(StoredData data, ulong userid, string mode, bool set)
            {
                string key = $"{userid}_{mode}";
                if (_cache.TryGetValue(key, out bool value))
                {
                    return value;
                }

                if (userid.HasPermission("raidablebases.buyable.bypass.cooldown"))
                {
                    return Cache(key, false);
                }

                var diff = Get(mode);
                if (diff == null || diff.Cooldown <= 0)
                {
                    return Cache(key, false);
                }

                using var cooldowns = DisposableList<double>();
                cooldowns.Add(diff.Cooldown);

                BasePlayer player = BasePlayer.FindByID(userid);
                if (player != null)
                {
                    if (player.IsFlying || player.limitNetworking)
                    {
                        return Cache(key, false);
                    }

                    if (player.IsAdmin || player.IsDeveloper)
                    {
                        cooldowns.Add(diff.Admin);
                    }
                }

                if (userid.HasPermission("raidablebases.vipcooldown"))
                {
                    cooldowns.Add(diff.VIP);
                }

                if (userid.HasPermission("raidablebases.allow"))
                {
                    cooldowns.Add(diff.Allow);
                }

                double cooldown = double.MaxValue;

                foreach (double val in cooldowns)
                {
                    if (val < cooldown)
                    {
                        cooldown = val;
                    }
                    if (val <= 0)
                    {
                        return Cache(key, false);
                    }
                }

                if (set)
                {
                    if (!data.BuyableCooldowns.TryGetValue(userid, out var info))
                    {
                        data.BuyableCooldowns[userid] = info = new();
                    }

                    info.Modes[mode] = DateTime.Now.AddSeconds(cooldown);
                }

                return Cache(key, true);
            }
        }

        public class BuyableRefundsSettings
        {
            [JsonProperty(PropertyName = en ? "Refund Despawned Bases" : "Возврат средств при деспавне базы")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Base Becomes Ineligible For Rewards On Despawn" : "База становится неподходящей для вознаграждения при уничтожении")]
            public bool Ineligible = true;

            [JsonProperty(PropertyName = en ? "Block Refund If Base Is Damaged" : "Блокировать возврат, если база повреждена")]
            public bool Damaged = true;

            [JsonProperty(PropertyName = en ? "Block Despawn If Base Is Damaged" : "Блокировать деспавн, если база повреждена")]
            public bool Despawn = true;

            [JsonProperty(PropertyName = en ? "Block Despawn If Anything Is Looted" : "Блокировать деспавн, если что-либо было залутано")]
            public bool AnyLooted = true;

            [JsonProperty(PropertyName = en ? "Refund Percentage" : "Процент возврата")]
            public double Percentage = 100.0;

            [JsonProperty(PropertyName = en ? "Refund Resets Cooldown Timer" : "Возврат сбрасывает таймер времени ожидания")]
            public bool Reset;

            [JsonProperty(PropertyName = en ? "Cooldown (0 = No Cooldown)" : "Nерезарядки (0 = без перезарядки)")]
            public float Cooldown;

            [JsonProperty(PropertyName = en ? "Purchase Same Base While On Despawn Cooldown" : "Покупка той же базы во время кулдауна на удаление")]
            public bool Repeat;
        }

        public class BuyableSettings : RaidableBaseSettingsEventTypeBase
        {
            [JsonProperty(PropertyName = en ? "Enable X Hours After Wipe (0 = immediately)" : "Включить через X часов после вайпа (0 = сразу)")]
            public BuyableWipeTime Wipe = new();

            [JsonProperty(PropertyName = en ? "Max Amount Purchasable Per Difficulty (0 = infinite, -1 = disabled)" : "Максимальное количество, доступное к покупке на сложность (0 = бесконечно, -1 = отключено)")]
            public DifficultyModeOptions Limits = new(en ? "Max Amount Purchasable Per Difficulty (0 = infinite, -1 = disabled)" : "Максимальное количество, доступное к покупке на сложность (0 = бесконечно, -1 = отключено)");

            [JsonProperty(PropertyName = en ? "Cooldowns (0 = No Cooldown)" : "Перезарядки (0 = без перезарядки)")]
            public BuyableCooldownSettings Cooldowns = new();

            [JsonProperty(PropertyName = en ? "Refunds" : "Возвраты")]
            public BuyableRefundsSettings Refunds = new();

            [JsonProperty(PropertyName = en ? "Allow Players To Spawn Specified Base Files" : "Разрешить игрокам создавать базы из указанных файлов")]
            public bool FileMode;

            [JsonProperty(PropertyName = en ? "Allow Players To Buy PVP Raids" : "Разрешить игрокам покупать PVP рейды")]
            public bool AllowBuyPVP = true;

            [JsonProperty(PropertyName = en ? "Allow Ally With Lockouts To Enter" : "Разрешить союзникам с блокировками входить")]
            public bool AllowAlly = true;

            [JsonProperty(PropertyName = en ? "Lock Raid To Buyer And Friends" : "Заблокировать рейд для покупателя и его друзей")]
            public bool UsePayLock = true;

            [JsonProperty(PropertyName = en ? "Max Buyable Events" : "Максимальное количество покупаемых событий")]
            public int Max = 15;

            [JsonProperty(PropertyName = en ? "Prevent Players From Buying Until Previous Raid Despawns" : "Запретить игрокам покупать, пока предыдущий рейд не будет деспавнен")]
            public bool PreventNew;

            [JsonProperty(PropertyName = en ? "Prevent Players From Hogging Purchased Raids" : "Предотвращение захвата купленных рейдов игроками")]
            public bool PreventHogging;

            [JsonProperty(PropertyName = en ? "Use Permission (raidablebases.buyraid)" : "Использовать разрешение (raidablebases.buyraid)")]
            public bool UsePermission;

            [JsonProperty(PropertyName = en ? "Spawn At Closest Position From Player" : "Спавн в ближайшей позиции от игрока")]
            public bool Closest = true;

            [JsonProperty(PropertyName = en ? "Auto Close Buyable Ui When At Maximum Limit" : "Автоматически закрывать интерфейс покупки при достижении максимального лимита")]
            public bool AutoCloseUi;

            [JsonProperty(PropertyName = en ? "Add Personal Marker On Owners Map" : "Добавить личный маркер на карту мира владельца")]
            public bool PersonalMarker;

            [JsonProperty(PropertyName = "Use Raids Can Spawn On Options")]
            public bool UseCanSpawnOnOptions = true;
        }

        public class ManualSettings
        {
            [JsonProperty(PropertyName = en ? "Convert PVE To PVP" : "Преобразовать PVE в PVP")]
            public bool ConvertPVE;

            [JsonProperty(PropertyName = en ? "Convert PVP To PVE" : "Преобразовать PVP в PVE")]
            public bool ConvertPVP;

            [JsonProperty(PropertyName = en ? "Max Manual Events" : "Максимальное количество ручных событий")]
            public int MaxInt = 1;

            [JsonProperty(PropertyName = en ? "Max Manual Events (When Deep Sea Is Open)" : "Максимальное количество ручных событий (When Deep Sea Is Open)")]
            public int MaxDeepSea = -1;

            [JsonProperty(PropertyName = en ? "Spawns Database File (Optional)" : "Файл базы данных спавнов (опционально)")]
            public string SpawnsFile = "none";

            [JsonProperty(PropertyName = en ? "Bypass Lock Treasure To First Attacker For PVE Bases" : "Обход Блокировки Сокровища для Первого Атакующего на PVE Базах")]
            public bool BypassUseOwnersForPVE;

            [JsonProperty(PropertyName = en ? "Bypass Lock Treasure To First Attacker For PVP Bases" : "Обход Блокировки Сокровища для Первого Атакующего на PVP Базах")]
            public bool BypassUseOwnersForPVP = true;

            internal int Max => MaxDeepSea == -1 ? MaxInt : (IsDeepSeaOpen() ? MaxDeepSea : MaxInt);
        }

        public class RaidableBaseWallOptions
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = en ? "Stacks" : "Слои")]
            public int Stacks = 1;

            [JsonProperty(PropertyName = en ? "Ignore Stack Limit When Clipping Terrain" : "Игнорировать лимит слоев при обрезке террейна")]
            public bool IgnoreWhenClippingTerrain = true;

            [JsonProperty(PropertyName = en ? "Ignore Forced Height Option" : "Игнорировать насильно заданную высоту (опция)")]
            public bool IgnoreForcedHeight = true;

            [JsonProperty(PropertyName = en ? "Use Stone Walls" : "Использовать каменные стены")]
            public bool Stone = true;

            [JsonProperty(PropertyName = en ? "Use Iced Walls" : "Использовать ледяные стены")]
            public bool Ice;

            [JsonProperty(PropertyName = en ? "Use Frontier Walls" : "Использовать стену фронтира")]
            public bool Frontier;

            [JsonProperty(PropertyName = en ? "Use Adobe Walls" : "Использовать глинобитные стены")]
            public bool Adobe;

            [JsonProperty(PropertyName = en ? "Use Least Amount Of Walls" : "Использовать наименьшее количество стен")]
            public bool LeastAmount = true;

            [JsonProperty(PropertyName = en ? "Use UFO Walls" : "Использовать стены UFO")]
            public bool UseUFOWalls;

            [JsonProperty(PropertyName = en ? "Radius" : "Радиус")]
            public float Radius = 25f;
        }

        public class RaidableBaseCostOptions
        {
            [JsonProperty(PropertyName = en ? "Require Custom Costs" : "Требовать индивидуальные затраты")]
            public bool Custom = true;

            [JsonProperty(PropertyName = en ? "Require Economics Costs" : "Требовать затраты в экономике")]
            public bool Economics = true;

            [JsonProperty(PropertyName = en ? "Require Server Rewards Costs" : "Требовать затраты в Server Rewards")]
            public bool ServerRewards = true;

            internal bool Any => Custom || Economics || ServerRewards;
        }

        public class CustomCostShoppyStock
        {
            [JsonProperty(PropertyName = en ? "Item Shortname" : "Сокращенное название предмета")]
            public string ItemName = "";

            [JsonProperty(PropertyName = en ? "Item Skin" : "Скин предмета")]
            public ulong ItemSkin;

            [JsonProperty(PropertyName = en ? "Shop Name" : "Название магазина")]
            public string ShopName = "";

            [JsonProperty(PropertyName = en ? "Panel Family Name" : "Название семейства панели")]
            public string PanelName = "Legacy";

            public bool IsItem(CustomCostOptions option) => option.Shortname == ItemName && option.Skin == ItemSkin;
        }

        public class CustomCostPluginOptions
        {
            [JsonProperty(en ? "Plugin Name" : "Название плагина")] public string PluginName = "";
            [JsonProperty(en ? "Deposit Method (API)" : "Название метода(API)")] public string DepositHookName = "";
            [JsonProperty(en ? "Withdraw Method (API)" : "Метод вывода средств (API)")] public string WithdrawHookName = "";
            [JsonProperty(en ? "Balance Method (API)" : "Метод балансировки (API)")] public string BalanceHookName = "";
            [JsonProperty("ShoppyStock Shop Name")] public string ShoppyStockShopName = "";
            [JsonProperty(en ? "Currency Name" : "Название валюты")] public string CurrencyName = "";
            [JsonProperty(en ? "Amount" : "Сумма")] public double Amount;
            [JsonProperty(en ? "Amount Data Type (API) - [ 0 - double | 1 - float | 2 - int ]" : "Тип данных метода(Сумма API) - [ 0 - double | 1 - float | 2 - int ]")] public int AmountDataType;
            [JsonProperty(en ? "User Data Type (API) - [ 0 - ulong | 1 - string | 2 - player ]" : "Тип данных метода(User API) - [ 0 - ulong | 1 - string | 2 - player ]")] public int PlayerDataType;
        }

        public class CustomCostOptions
        {
            [JsonProperty(PropertyName = "Plugin")]
            public CustomCostPluginOptions Plugin = new();

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled;

            [JsonProperty(PropertyName = en ? "Item Shortname" : "Сокращенное название предмета")]
            public string Shortname = "scrap";

            [JsonProperty(PropertyName = en ? "Item Name" : "Название предмета")]
            public string Name = null;

            [JsonProperty(PropertyName = en ? "Amount" : "Количество")]
            public int Amount;

            [JsonProperty(PropertyName = en ? "Skin" : "Скин")]
            public ulong Skin;
            internal ItemDefinition _definition;

            internal ItemDefinition Definition => _definition ??= ItemManager.FindItemDefinition(Shortname);

            internal string GetCurrencyName() => !string.IsNullOrWhiteSpace(Plugin.CurrencyName) ? Plugin.CurrencyName : string.IsNullOrWhiteSpace(Name) ? Plugin.PluginName : Name;

            internal bool isItem => Enabled && !string.IsNullOrWhiteSpace(Shortname) && Amount > 0 && Definition != null;

            internal bool isPlugin => Enabled && Plugin != null && Plugin.Amount > 0 && !string.IsNullOrWhiteSpace(Plugin.PluginName) && !string.IsNullOrWhiteSpace(Plugin.WithdrawHookName) && !string.IsNullOrWhiteSpace(Plugin.DepositHookName) && !string.IsNullOrWhiteSpace(Plugin.BalanceHookName);

            public CustomCostOptions(int amount)
            {
                Amount = amount;
            }
        }

        public class RankedLadderSettings : ConfigurationExtension<RankedRecord>
        {
            [JsonProperty(PropertyName = en ? "Award Top X Players On Wipe" : "Наградить топ X игроков при вайпе")]
            public int Amount = 3;

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled = true;

            [DefaultValue(60d)]
            [JsonProperty(PropertyName = "Amount Of Days Before Removing Inactive User From The Lifetime Ladder?")]
            public double Days = 60;

            [JsonProperty(PropertyName = en ? "Show Top X Ladder" : "Показывать топ X лестницы")]
            public int Top = 10;

            [JsonProperty(PropertyName = en ? "Assign Rank After X Completions" : "Назначить ранг после X завершений")]
            public RaidableBaseSettingsRankedLadderPointOptions Assign = new(en ? "Assign Rank After X Completions" : "Назначить ранг после X завершений");

            [JsonProperty(PropertyName = en ? "Difficulty Points" : "Очки сложности")]
            public RaidableBaseSettingsRankedLadderPointOptions Points = new(en ? "Difficulty Points" : "Очки сложности");

            public RankedLadderSettings() : base(en ? "Ranked Ladder" : "Ранговая лестница", "default", "default", "default", "default", "default") { }

            public override bool Create(List<string> modes)
            {
                if (Dictionary.ContainsKey("default"))
                {
                    Clear();
                    modes.ForEach(mode =>
                    {
                        string str = mode.ToLower().Replace(" ", "");
                        Set(mode, new($"raidablebases.ladder.{str}", $"raid{str}", mode));
                    });
                    Set(RaidableMode.Points, new("raidablebases.th", "raidhunter", RaidableMode.Points));
                    return Dictionary.Count > 0;
                }
                return false;
            }

            public RankedRecord GetRecord(string mode)
            {
                foreach (var record in Dictionary.Values)
                {
                    if (record != null && record.IsValid && mode.Equals(record.Mode, StringComparison.CurrentCultureIgnoreCase))
                    {
                        return record;
                    }
                }
                return null;
            }

            public List<RankedRecord> GetRecords() => Dictionary.Values.Where(x => x != null && x.IsValid);
        }

        public class RaidableBaseSettingsRankedLadderPointOptions : DifficultyModesInt
        {
            [JsonProperty(PropertyName = en ? "Assign To Owner Of Raid Only" : "Начислять только владельцу рейда")]
            public bool Owner;

            public RaidableBaseSettingsRankedLadderPointOptions() : base(null) { }

            public RaidableBaseSettingsRankedLadderPointOptions(string parent) : base(parent) { }
        }

        public class RewardSettings
        {
            [JsonProperty(PropertyName = en ? "Custom Currency" : "Пользовательская валюта")]
            public CustomCostOptions Custom = new(0);

            [JsonProperty(PropertyName = en ? "Economics Money" : "Деньги Economics")]
            public double Money;

            [JsonProperty(PropertyName = en ? "ServerRewards Points" : "Очки ServerRewards")]
            public int Points;

            [JsonProperty(PropertyName = en ? "SkillTree XP" : "Опыт SkillTree")]
            public double SkillTree;

            [JsonProperty(PropertyName = en ? "XLevels XP" : "Опыт XLevels")]
            public double XLevels = -125;

            [JsonProperty(PropertyName = en ? "XPerience XP" : "Опыт XPerience")]
            public double XPerience = -125;

            [JsonProperty(PropertyName = en ? "Do Not Reward Buyable Events" : "Не награждать события, доступные для покупки")]
            public bool NoBuyableRewards;

            [JsonProperty(PropertyName = en ? "Double Rewards At Night Time Hours" : "В ночное время действуют двойные бонусы.")]
            public bool DoubleAtNighttime;

            internal bool IsDoubledAtNighttime() => DoubleAtNighttime && TOD_Sky.Instance?.IsNight == true;
        }

        public class SkinSettingsBoxes : SkinSettingsDefault
        {
            [JsonProperty(PropertyName = en ? "Preset Skins" : "Предустановленные скины", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> Skins = new();

            [JsonProperty(PropertyName = en ? "Ignore If Skinned Already" : "Игнорировать, если уже есть скин")]
            public bool IgnoreSkinned;

            [JsonProperty(PropertyName = en ? "Use Identical Skins" : "Использовать идентичные скины")]
            public bool Unique;
        }

        public class SkinSettingsLoot : SkinSettingsDefault
        {
            [JsonProperty(PropertyName = en ? "Use Identical Skins For Stackable Items" : "Использовать идентичные скины для стопок предметов")]
            public bool Stackable = true;

            [JsonProperty(PropertyName = en ? "Use Identical Skins For Non-Stackable Items" : "Использовать идентичные скины для нестопок предметов")]
            public bool NonStackable;
        }

        public class SkinSettingsNpcs : SkinSettingsDefault
        {
            [JsonProperty(PropertyName = en ? "Use Identical Skins" : "Использовать идентичные скины")]
            public bool Unique = true;

            [JsonProperty(PropertyName = en ? "Use Skins With Murderer Kits" : "Используйте скины с Murderer Kits")]
            public bool MurdererKits;

            [JsonProperty(PropertyName = en ? "Use Skins With Scientist Kits" : "Используйте скины с Scientist Kits")]
            public bool ScientistKits;

            [JsonProperty(PropertyName = en ? "Ignore Skinned Murderer Kits" : "Игнорировать скинированные Murderer Kits")]
            public bool IgnoreSkinnedMurderer;

            [JsonProperty(PropertyName = en ? "Ignore Skinned Scientist Kits" : "Игнорировать скинированные Scientist Kits")]
            public bool IgnoreSkinnedScientist;

            internal bool CanSkinKit(ulong skin, bool isMurderer) => (MurdererKits && isMurderer && (skin == 0uL || !IgnoreSkinnedMurderer)) || (ScientistKits && !isMurderer && (skin == 0uL || !IgnoreSkinnedScientist));
        }

        public class SkinSettingsDeployables : SkinSettingsDefault
        {
            [JsonProperty(PropertyName = en ? "Partial Names" : "Частичные названия", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> PartialNames = new()
            {
                "door", "barricade", "chair", "fridge", "furnace", "locker", "reactivetarget", "rug", "sleepingbag", "table", "vendingmachine", "waterpurifier", "skullspikes", "skulltrophy", "summer_dlc", "sled"
            };

            [JsonProperty(PropertyName = en ? "Preset Door Skins" : "Предустановленные скины для дверей", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> Doors = new();

            [JsonProperty(PropertyName = en ? "Skin Everything" : "наносить на всё скины")]
            public bool SkinEverything = true;

            [JsonProperty(PropertyName = en ? "Ignore If Skinned Already" : "Игнорировать, если уже есть скин")]
            public bool IgnoreSkinned;

            [JsonProperty(PropertyName = en ? "Use Identical Skins" : "Использовать идентичные скины")]
            public bool Unique;
        }

        public class SkinSettingsDefault
        {
            [JsonProperty(PropertyName = en ? "Use Random Skin" : "Использовать случайный скин")]
            public bool Random = true;

            [JsonProperty(PropertyName = en ? "Use Workshop Skins" : "Использовать скины из мастерской")]
            public bool Workshop = true;

            [JsonProperty(PropertyName = en ? "Use Imported Workshop Skins File" : "Использовать импортированные скины из мастерской")]
            public bool ImportedWorkshop = true;

            [JsonProperty(PropertyName = en ? "Use Approved Workshop Skins Only" : "Использовать только одобренные скины из мастерской")]
            public bool ApprovedOnly;
        }

        public class SkinSettings
        {
            [JsonProperty(PropertyName = en ? "Boxes" : "Ящики")]
            public SkinSettingsBoxes Boxes = new();

            [JsonProperty(PropertyName = en ? "Npcs" : "NPC")]
            public SkinSettingsNpcs Npc = new();

            [JsonProperty(PropertyName = en ? "Loot Items" : "предметы лута")]
            public SkinSettingsLoot Loot = new();

            [JsonProperty(PropertyName = en ? "Deployables" : "Размещаемые предметы")]
            public SkinSettingsDeployables Deployables = new();
        }

        public class SkinSettingsImportedWorkshop
        {
            [JsonProperty(PropertyName = "Imported Workshop Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<ulong>> SkinList = new()
            {
                ["jacket.snow"] = new() { 785868744, 939797621 },
                ["knife.bone"] = new() { 1228176194, 2038837066 }
            };
        }

        public class SkinsPlugin
        {
            [JsonProperty(PropertyName = "Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<SkinItem> Skins = new()
            {
                new() { Shortname = "jacket.snow", Skins = new() { 785868744, 939797621 } },
                new() { Shortname = "knife.bone", Skins = new() { 1228176194, 2038837066 } }
            };
        }

        public class SkinItem
        {
            [JsonProperty(PropertyName = "Item Shortname")]
            public string Shortname = "shortname";

            [JsonProperty(PropertyName = "Permission")]
            public string Permission = "";

            [JsonProperty(PropertyName = "Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> Skins = new() { 0 };
        }

        public class LootItem : IEquatable<LootItem>
        {
            public class ArmorSlots
            {
                [JsonProperty(PropertyName = en ? "min" : "мин")]
                public int min;
                [JsonProperty(PropertyName = en ? "max" : "макс")]
                public int max;
                internal int amount => max > 0 ? UnityEngine.Random.Range(min, max + 1) : 0;
                public void TryAdd(Item item)
                {
                    if (item == null || item.info == null || !item.info.TryGetComponent(out ItemModContainerArmorSlot slot))
                    {
                        return;
                    }
                    int cap = amount;
                    if (cap > 0)
                    {
                        slot.CreateAtCapacity(cap, item);
                        slot.OnItemCreated(item);
                    }
                }
            }

            public LootItem() { }

            public LootItem(string shortname, int amountMin = 1, int amount = 1, ulong skin = 0, bool isBlueprint = false, float probability = 1.0f, int stacksize = -1, string name = null, string text = null, bool hasPriority = false, ArmorSlots slots = null)
            {
                (this.shortname, this.amountMin, this.amount, this.skin, this.isBlueprint, this.probability, this.stacksize, this.name, this.text, this.hasPriority, this.slots) =
                    (shortname, amountMin, amount, skin, isBlueprint, probability, stacksize, name, text, hasPriority, slots);
            }

            internal void InitializeArmorSlots()
            {
                if (slots != null || definition == null || !definition.TryGetComponent(out ItemModContainerArmorSlot slot))
                {
                    return;
                }
                slots = new()
                {
                    min = slot.MinSlots,
                    max = slot.MaxSlots
                };
            }

            [JsonProperty(PropertyName = en ? "armor module slots" : "Слоты модулей брони", NullValueHandling = NullValueHandling.Ignore)]
            public ArmorSlots slots;

            [JsonProperty(PropertyName = en ? "shortname" : "краткое_название")]
            public string shortname;

            [JsonProperty(PropertyName = en ? "name" : "имя")]
            public string name = null;

            [JsonProperty(PropertyName = en ? "text" : "текст")]
            public string text = null;

            [JsonProperty(PropertyName = en ? "blueprint" : "чертёж")]
            public bool isBlueprint;

            [JsonProperty(PropertyName = en ? "skin" : "скин")]
            public ulong skin;

            [JsonProperty(PropertyName = en ? "amount" : "количество")]
            public int amount;

            [JsonProperty(PropertyName = en ? "amountMin" : "мин_количество")]
            public int amountMin;

            [JsonProperty(PropertyName = en ? "probability" : "вероятность")]
            public float probability = 1f;

            [JsonProperty(PropertyName = en ? "stacksize" : "размер_стека")]
            public int stacksize = -1;

            internal ItemDefinition definition => _def ??= ItemManager.FindItemDefinition(shortname);
            internal ItemDefinition _def;
            internal bool hasPriority;
            internal bool isSplit;

            public bool HasProbability() => UnityEngine.Random.value <= probability;

            public LootItem Clone() => new(shortname, amountMin, amount, skin, isBlueprint, probability, stacksize, name, text, hasPriority, slots);

            public bool Equals(LootItem other) => shortname == other.shortname && amount == other.amount && skin == other.skin && amountMin == other.amountMin && text == other.text;

            public override bool Equals(object obj) => obj is LootItem ti && Equals(ti);

            public override int GetHashCode() => base.GetHashCode();
        }

        #region Facepunch TOS Compliance

        private readonly HashSet<int> _dlcItemIds = new();
        private readonly HashSet<ulong> _ownershipIds = new();
        private bool _ownershipReady;

        public void LoadOwnership()
        {
            if (!config.BlockPaidContent)
            {
                _ownershipReady = true;
                return;
            }

            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0)
            {
                timer.In(3f, LoadOwnership);
                return;
            }

            foreach (var def in ItemManager.GetItemDefinitions())
            {
                if (RequiresOwnership(def))
                {
                    _dlcItemIds.Add(def.itemid);
                }

                if (def.skins != null)
                {
                    foreach (var sk in def.skins)
                    {
                        if (sk.id != 0) _ownershipIds.Add((ulong)sk.id);
                    }
                }

                if (def.skins2 != null)
                {
                    foreach (var sk2 in def.skins2)
                    {
                        if (sk2.WorkshopId != 0) _ownershipIds.Add(sk2.WorkshopId);
                    }
                }
            }

            _ownershipReady = true;
        }

        public bool RequiresOwnership(ItemDefinition def, ulong skin)
        {
            if (!config.BlockPaidContent) return false;
            if (skin != 0uL && !_ownershipReady) return true;
            if (skin != 0uL && _ownershipIds.Contains(skin)) return true;
            if (def != null && !_ownershipReady) return RequiresOwnership(def);
            return def != null && _dlcItemIds.Contains(def.itemid);
        }

        public bool RequiresOwnership(ItemDefinition def) => def switch
        {
            null => false,
            { steamItem: { id: not 0 } } => true,
            { steamDlc: { dlcAppID: not 0 } } => true,
            { Blueprint: { NeedsSteamDLC: true } } => true,
            { Parent: { Blueprint: { NeedsSteamDLC: true } } } => true,
            { isRedirectOf: { Blueprint: { NeedsSteamDLC: true } } } => true,
            { isRedirectOf: not null } => true,
            _ => false
        };

        public bool HasUnlocked(BasePlayer player, ItemDefinition def)
        {
            return false;
            //if (def == null || !config.BlockPaidContent || player.UnlockAllSkins) return true;
            //if (_ownershipReady ? !_dlcItemIds.Contains(def.itemid) : !RequiresOwnership(def)) return true;
            //return def.steamDlc != null && def.steamDlc.HasLicense(player.userID);
        }

        public bool HasUnlocked(BasePlayer player, ulong skin)
        {
            return false;
            //if (skin == 0 || !config.BlockPaidContent || player.UnlockAllSkins) return true;
            //if (!_ownershipReady) return false;
            //if (!_ownershipIds.Contains(skin)) return true;
            //return player.blueprints.CheckSkinOwnership((int)skin, player.userID);
        }

        #endregion Facepunch TOS Compliance

        private List<LootItem> GetPrefabLootFrom(LootContainer.LootSpawnSlot[] slots, LootSpawn lootSpawn, ItemContainer container, int maxToSpawn)
        {
            var items = new List<LootItem>();

            if (slots != null && slots.Length > 0)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    for (int n = 0; n < slot.numberToSpawn; n++)
                    {
                        if (UnityEngine.Random.Range(0f, 1f) <= slot.probability)
                        {
                            for (int k = 0; k < slot.definition.items.Length; k++)
                            {
                                var ia = slot.definition.items[k];
                                int amt = Mathf.FloorToInt(ia.GetAmount());
                                if (amt > 0)
                                {
                                    LootItem ti = new(ia.itemDef.shortname, amt, amt, 0, ia.isBP, slot.probability);
                                    ti.InitializeArmorSlots();
                                    items.Add(ti);
                                    if (items.Count >= maxToSpawn) return items;
                                }
                            }
                        }
                    }
                }
            }
            else if (lootSpawn != null)
            {
                SpawnFromDefinition(lootSpawn, maxToSpawn, items);
            }

            return items;
        }

        private void SpawnFromDefinition(LootSpawn lootSpawn, int maxToSpawn, List<LootItem> items)
        {
            if (items.Count >= maxToSpawn) return;
            if (lootSpawn.subSpawn != null && lootSpawn.subSpawn.Length > 0)
            {
                int totalWeight = 0;
                for (int i = 0; i < lootSpawn.subSpawn.Length; i++)
                {
                    totalWeight += lootSpawn.subSpawn[i].weight;
                }

                if (totalWeight > 0)
                {
                    int weight = 0, randomWeight = UnityEngine.Random.Range(0, totalWeight);
                    LootSpawn.Entry chosen = lootSpawn.subSpawn[0];

                    for (int i = 0; i < lootSpawn.subSpawn.Length; i++)
                    {
                        weight += lootSpawn.subSpawn[i].weight;
                        if (randomWeight < weight)
                        {
                            chosen = lootSpawn.subSpawn[i];
                            break;
                        }
                    }

                    if (chosen.category != null)
                    {
                        int times = 1 + chosen.extraSpawns;
                        for (int t = 0; t < times; t++)
                        {
                            if (items.Count >= maxToSpawn) return;
                            SpawnFromDefinition(chosen.category, maxToSpawn, items);
                        }
                        return;
                    }
                }
            }

            if (lootSpawn.items != null && lootSpawn.items.Length > 0)
            {
                for (int i = 0; i < lootSpawn.items.Length; i++)
                {
                    ItemAmountRanged ia = lootSpawn.items[i];
                    int amount = Mathf.FloorToInt(ia.GetAmount());
                    if (amount > 0)
                    {
                        LootItem ti = new(ia.itemDef.shortname, amount, amount, 0, ia.isBP, 1f);
                        ti.InitializeArmorSlots();
                        items.Add(ti);
                        if (items.Count >= maxToSpawn) return;
                    }
                }
            }
        }

        public class TreasureSettings
        {
            [JsonProperty(PropertyName = en ? "Resources Not Moved To Cupboards" : "Ресурсы, не перемещаемые в шкафы", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ExcludeFromCupboard = new()
            {
                "skull.human", "battery.small", "bone.fragments", "can.beans.empty", "can.tuna.empty", "water.salt", "water", "skull.wolf"
            };

            [JsonProperty(PropertyName = en ? "Use Day Of Week Loot" : "Использовать лут по дням недели")]
            public bool Daily;

            [JsonProperty(PropertyName = en ? "Do Not Duplicate Base Loot" : "Не дублировать базовый лут")]
            public bool Base;

            [JsonProperty(PropertyName = en ? "Do Not Duplicate Difficulty Loot" : "Не дублировать лут сложности")]
            public bool Difficulty;

            [JsonProperty(PropertyName = en ? "Do Not Duplicate Default Loot" : "Не дублировать лут по умолчанию")]
            public bool Default;

            [JsonProperty(PropertyName = en ? "Use Stack Size Limit For Spawning Items" : "Использовать ограничение размера стека для появления предметов")]
            public bool Stacks;
        }

        public class UIBaseSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено", Order = 1)]
            public bool Enabled = true;

            [JsonProperty(PropertyName = "Offset Min", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMin;

            [JsonProperty(PropertyName = "Offset Max", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMax;

            [JsonProperty(PropertyName = en ? "Panel Alpha" : "Прозрачность панели", NullValueHandling = NullValueHandling.Ignore, Order = 4)]
            public float? PanelAlpha = 0.98f;

            [JsonProperty(PropertyName = en ? "Background Color" : "Цвет фона", NullValueHandling = NullValueHandling.Ignore, Order = 5)]
            public string PanelColor = "#252121";

            [JsonProperty(PropertyName = en ? "Title Background Color" : "Цвет фона заголовка", NullValueHandling = NullValueHandling.Ignore, Order = 6)]
            public string TitlePanelColor = "#000000";
        }

        public class BuildingOptionsElevators : UIBaseSettings
        {
            public BuildingOptionsElevators()
            {
                (AnchorMin, AnchorMax, PanelAlpha) = ("0.406 0.915", "0.59 0.949", 0.98f);
            }

            [JsonProperty(PropertyName = "Anchor Min", Order = 2)]
            public string AnchorMin;

            [JsonProperty(PropertyName = "Anchor Max", Order = 3)]
            public string AnchorMax;

            [JsonProperty(PropertyName = en ? "Required Access Level" : "Требуемый уровень доступа", Order = 5)]
            public int RequiredAccessLevel;

            [JsonProperty(PropertyName = en ? "Required Access Level Grants Permanent Use" : "Уровень доступа предоставляет постоянное использование", Order = 6)]
            public bool RequiredAccessLevelOnce;

            [JsonProperty(PropertyName = en ? "Required Keycard Skin ID" : "ID Скина ключа доступа", Order = 7)]
            public ulong SkinID = 2690554489;

            [JsonProperty(PropertyName = en ? "Requires Building Permission" : "Требуется разрешение на строительство", Order = 8)]
            public bool RequiresBuildingPermission;

            [JsonProperty(PropertyName = en ? "Button Health" : "Прочность кнопки", Order = 9)]
            public float ButtonHealth = 1000f;

            [JsonProperty(PropertyName = en ? "Elevator Health" : "Прочность лифта", Order = 10)]
            public float ElevatorHealth = 600f;

            [JsonProperty(PropertyName = en ? "Requires Power Source" : "Требуется источник питания")]
            public bool RequiresPower;

            [JsonProperty(PropertyName = "Use Static Elevators Only (bmgjet)")]
            public bool BMGOnly;
        }

        public class UIDelaySettings : UIBaseSettings
        {
            public UIDelaySettings()
            {
                (OffsetMin, OffsetMax, PanelAlpha) = (new(-34.488f, 87.056f), new(179.631f, 124.804f), 0.98f);
            }

            [JsonProperty(PropertyName = en ? "Font Size" : "Размер шрифта", Order = 4)]
            public int FontSize = 14;

            [JsonProperty(PropertyName = en ? "Text Color" : "Цвет текста", Order = 5)]
            public string TextColor = "#FF0000";
        }

        public class UILockoutSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = en ? "Hidden While Buyable Events UI Is Closed" : "Скрыто, когда интерфейс событий покупки закрыт")]
            public bool BuyOnly;

            [JsonProperty(PropertyName = "Offset Min")]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMin = new(-117.966f, -149.658f);

            [JsonProperty(PropertyName = "Offset Max")]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMax = new(-17.834f, -106.342f);

            [JsonProperty(PropertyName = en ? "Panel Alpha" : "Прозрачность панели")]
            public float Alpha = 0.98f;

            [JsonProperty(PropertyName = en ? "Background Color" : "Цвет фона")]
            public string BackgroundColor = "#242020";

            [JsonProperty(PropertyName = en ? "Title Text Color" : "Цвет текста заголовка")]
            public string TitleColor = "#FFFFFF";

            [JsonProperty(PropertyName = en ? "Title Embed Color" : "Цвет внедренного заголовка")]
            public string TitleEmbedColor = "#242020";

            [JsonProperty(PropertyName = en ? "Title Panel Color" : "Цвет панели заголовка")]
            public string TitlePanelColor = "#000000";
        }

        public class UICooldownSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = en ? "Hidden While Buyable Events UI Is Closed" : "Скрыто, когда интерфейс событий покупки закрыт")]
            public bool BuyOnly;

            [JsonProperty(PropertyName = "Offset Min")]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMin = new(-117.966f, -77.055f);

            [JsonProperty(PropertyName = "Offset Max")]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMax = new(-17.834f, -33.74f);

            [JsonProperty(PropertyName = en ? "Panel Alpha" : "Прозрачность панели")]
            public float Alpha = 0.98f;

            [JsonProperty(PropertyName = en ? "Background Color" : "Цвет фона")]
            public string BackgroundColor = "#242020";

            [JsonProperty(PropertyName = en ? "Title Text Color" : "Цвет текста заголовка")]
            public string TitleColor = "#FFFFFF";

            [JsonProperty(PropertyName = en ? "Title Embed Color" : "Цвет внедренного заголовка")]
            public string TitleEmbedColor = "#242020";

            [JsonProperty(PropertyName = en ? "Title Panel Color" : "Цвет панели заголовка")]
            public string TitlePanelColor = "#000000";
        }

        public class UIBuyableSettings : ConfigurationExtension<string>
        {
            public UIBuyableSettings() : base(en ? "Buyable Events UI" : "События для покупки UI", RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare)
            {
                (OffsetMin, OffsetMax, PanelAlpha) = (new(-34.159f, 86.718f), new(179.959f, 254.682f), 0.98f);
            }

            public bool Any() => Dictionary.Count > 0 && Dictionary.All(x => !string.IsNullOrWhiteSpace(x.Value));

            public override bool Create(List<string> modes)
            {
                if (!Any())
                {
                    Clear();
                    modes.ForEach(mode =>
                    {
                        TryAdd(en ? $"{mode} Button Color" : $"Цвет кнопки '{mode}'", "#497CAF");
                        TryAdd(en ? $"{mode} Text Color" : $"Цвет текста '{mode}'", "#FFFFFF");
                    });
                    return Any();
                }
                return false;
            }

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = en ? "Sort By Price Instead Of Difficulty Level" : "Сортировать по цене вместо уровня трудности")]
            public bool Price;

            [JsonProperty(PropertyName = "Offset Min", NullValueHandling = NullValueHandling.Ignore)]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMin;

            [JsonProperty(PropertyName = "Offset Max", NullValueHandling = NullValueHandling.Ignore)]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 OffsetMax;

            [JsonProperty(PropertyName = en ? "Panel Alpha" : "Прозрачность панели", NullValueHandling = NullValueHandling.Ignore)]
            public float? PanelAlpha = 0.98f;

            [JsonProperty(PropertyName = en ? "Background Color" : "Цвет фона", NullValueHandling = NullValueHandling.Ignore)]
            public string PanelColor = "#252121";

            [JsonProperty(PropertyName = en ? "Title Background Color" : "Цвет фона заголовка", NullValueHandling = NullValueHandling.Ignore)]
            public string TitlePanelColor = "#000000";

            [JsonProperty(PropertyName = en ? "Cursor Enabled" : "Включение курсора")]
            public bool CursorEnabled;

            [JsonProperty(PropertyName = en ? "Button Alpha" : "Прозрачность кнопки")]
            public float ButtonAlpha = 1f;

            [JsonProperty(PropertyName = en ? "X Text Color" : "Цвет текста 'X'")]
            public string XTextColor = "#FFFFFF";

            [JsonProperty(PropertyName = en ? "Font Size" : "Размер шрифта")]
            public int FontSize = 14;

            [JsonProperty(PropertyName = en ? "Use Contrast Colors For Text Color" : "Использовать контрастные цвета для цвета текста")]
            public bool Contrast = true;

            [JsonProperty(PropertyName = en ? "Use Difficulty Colors For Buttons" : "Использовать цвета сложности для кнопок")]
            public bool Difficulty = true;

            [JsonProperty(PropertyName = en ? "X Button Color" : "Цвет кнопки 'X'")]
            public string CloseColor = "#497CAF";

            public string GetButton(string mode) => Get(en ? $"{mode} Button Color" : $"Цвет кнопки '{mode}'");

            public string GetText(string mode) => Get(en ? $"{mode} Text Color" : $"Цвет текста '{mode}'");
        }

        public class UIAdvancedAlertSettings : UIBaseSettings
        {
            [JsonProperty(PropertyName = en ? "Time Shown" : "Время отображения", Order = 5)]
            public float Time = 5f;

            [JsonProperty(PropertyName = "Anchor Min", Order = 2)]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 AnchorMin;

            [JsonProperty(PropertyName = "Anchor Max", Order = 3)]
            [JsonConverter(typeof(Vector2Converter))]
            public Vector2 AnchorMax;

            public UIAdvancedAlertSettings()
            {
                (AnchorMin, AnchorMax, OffsetMin, OffsetMax, PanelAlpha, PanelColor) = (new(0.35f, 0.85f), new(0.65f, 0.95f), default, default, null, null);
            }
        }

        public class UIStatusSettings : UIBaseSettings
        {
            public UIStatusSettings()
            {
                (OffsetMin, OffsetMax, PanelColor, PanelAlpha) = (new(191.957f, 17.056f), new(327.626f, 79.024f), "#252121", 0.98f);
            }

            [JsonProperty(PropertyName = en ? "Font Size" : "Размер шрифта")]
            public int FontSize = 12;

            [JsonProperty(PropertyName = en ? "PVP Color" : "Цвет PVP")]
            public string ColorPVP = "#FF0000";

            [JsonProperty(PropertyName = en ? "PVE Color" : "Цвет PVE")]
            public string ColorPVE = "#008000";

            [JsonProperty(PropertyName = en ? "No Owner Color" : "Цвет без владельца", Order = 7)]
            public string NoneColor = "#FFFFFF";

            [JsonProperty(PropertyName = en ? "Negative Color" : "Отрицательный цвет", Order = 7)]
            public string NegativeColor = "#FF0000";

            [JsonProperty(PropertyName = en ? "Positive Color" : "Положительный цвет", Order = 8)]
            public string PositiveColor = "#008000";

            [JsonProperty(PropertyName = en ? "Show Loot Left" : "Показывать оставшийся лут")]
            public bool ShowLootLeft = true;

            [JsonProperty(PropertyName = en ? "Hide Loot Left Number When There Is No Owner" : "Скрыть оставшуюся добычу, если нет владельца")]
            public bool HideWithoutOwner;
        }

        public class UISettings
        {
            [JsonProperty(PropertyName = en ? "Advanced Alerts UI" : "Расширенные оповещения UI")]
            public UIAdvancedAlertSettings AA = new();

            [JsonProperty(PropertyName = en ? "Buyable Events UI" : "События для покупки UI")]
            public UIBuyableSettings Buyable = new();

            [JsonProperty(PropertyName = en ? "Buyable Cooldowns UI" : "Перезарядки для покупки UI")]
            public UICooldownSettings BuyableCooldowns = new();

            [JsonProperty(PropertyName = en ? "Delay UI" : "Задержка UI")]
            public UIDelaySettings Delay = new();

            [JsonProperty(PropertyName = en ? "Lockouts UI" : "Блокировки UI")]
            public UILockoutSettings Lockout = new();

            [JsonProperty(PropertyName = en ? "Status UI" : "Статус UI")]
            public UIStatusSettings Status = new();
        }

        public class WeaponTypeStateSettings
        {
            [JsonProperty(PropertyName = en ? "AutoTurret" : "Автоматические турели")]
            public bool AutoTurret = true;

            [JsonProperty(PropertyName = en ? "FlameTurret" : "Пламенная турель")]
            public bool FlameTurret = true;

            [JsonProperty(PropertyName = en ? "FogMachine" : "Туманная машина")]
            public bool FogMachine = true;

            [JsonProperty(PropertyName = en ? "GunTrap" : "Ловушка с дробовиком (гантрап)")]
            public bool GunTrap = true;

            [JsonProperty(PropertyName = en ? "SamSite" : "Зенитная установка САМ")]
            public bool SamSite = true;
        }

        public class WeaponTypeAmountSettings
        {
            [JsonProperty(PropertyName = en ? "AutoTurret" : "Автоматические турели")]
            public int AutoTurret = 256;

            [JsonProperty(PropertyName = en ? "FlameTurret" : "Пламенная турель")]
            public int FlameTurret = 256;

            [JsonProperty(PropertyName = en ? "FogMachine" : "Туманная машина")]
            public int FogMachine = 5;

            [JsonProperty(PropertyName = en ? "GunTrap" : "Ловушка с дробовиком (гантрап)")]
            public int GunTrap = 128;

            [JsonProperty(PropertyName = en ? "SamSite" : "Зенитная установка САМ")]
            public int SamSite = 24;
        }

        public class WeaponSettingsSamSite
        {
            [JsonProperty(PropertyName = en ? "Repairs Every X Minutes (0.0 = disabled)" : "Восстановление каждые X минут (0.0 = отключено)")]
            public float Repair = 5f;

            [JsonProperty(PropertyName = en ? "Range (350.0 = Rust default)" : "Дальность (350.0 = значение по умолчанию в Rust)")]
            public float Range = 75f;

            [JsonProperty(PropertyName = en ? "Requires Power Source" : "Требуется источник питания")]
            public bool RequiresPower;

            [JsonProperty(PropertyName = en ? "Minimum Health" : "Минимальное здоровье")]
            public float Min = 1000f;

            [JsonProperty(PropertyName = en ? "Maximum Health" : "Максимальное здоровье")]
            public float Max = 1000f;
        }

        public class WeaponSettingsTeslaCoil
        {
            [JsonProperty(PropertyName = en ? "Requires A Power Source" : "Требуется источник питания")]
            public bool RequiresPower;

            [JsonProperty(PropertyName = en ? "Max Discharge Self Damage Seconds (0 = None, 120 = Rust default)" : "Максимальное время самоповреждения разряда (0 = Нет, 120 = значение по умолчанию в Rust)")]
            public float MaxDischargeSelfDamageSeconds;

            [JsonProperty(PropertyName = en ? "Max Damage Output" : "Максимальный урон")]
            public float MaxDamageOutput = 35f;

            [JsonProperty(PropertyName = en ? "Health" : "Здоровье")]
            public float Health = 250f;
        }

        public class WeaponSettings
        {
            [JsonProperty(PropertyName = en ? "Infinite Ammo" : "Бесконечные патроны")]
            public WeaponTypeStateSettings InfiniteAmmo = new();

            [JsonProperty(PropertyName = en ? "Ammo" : "Патроны")]
            public WeaponTypeAmountSettings Ammo = new();

            [JsonProperty(PropertyName = en ? "No Fuel Source" : "Нет источника топлива", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Burn = new() { "skull_fire_pit", "cursedcauldron.deployed" };

            [JsonProperty(PropertyName = en ? "Fog Machine Allows Motion Toggle" : "Туманная машина разрешает переключение движения")]
            public bool FogMotion = true;

            [JsonProperty(PropertyName = en ? "Fog Machine Requires A Power Source" : "Туманная машина требует источник питания")]
            public bool FogRequiresPower = true;

            [JsonProperty(PropertyName = en ? "Spooky Speakers Requires Power Source" : "Страшные динамики требуют источник питания")]
            public bool SpookySpeakersRequiresPower;

            [JsonProperty(PropertyName = en ? "Test Generator Power" : "Мощность тестового генератора")]
            public float TestGeneratorPower = 100f;

            [JsonProperty(PropertyName = en ? "Sprinkler Requires A Power Source" : "Для спринклера требуется источник питания")]
            public bool SprinklerRequiresPower = true;

            [JsonProperty(PropertyName = en ? "Furnace Starting Fuel" : "Начальное топливо печи")]
            public int Furnace = 1000;
        }

        public class SphereColorSettings
        {
            [JsonProperty(PropertyName = en ? "When Locked" : "Когда заблокировано")]
            public SphereColor Locked;

            [JsonProperty(PropertyName = en ? "When Unlocked" : "Когда разблокировано")]
            public SphereColor Unlocked;

            [JsonProperty(PropertyName = en ? "When PVP" : "Когда PVP")]
            public SphereColor PVPState;

            [JsonProperty(PropertyName = en ? "When PVE" : "Когда PVE")]
            public SphereColor PVEState;

            [JsonProperty(PropertyName = en ? "When Active" : "Когда активно")]
            public SphereColor Active;

            [JsonProperty(PropertyName = en ? "When Inactive" : "Когда неактивно")]
            public SphereColor Inactive;
        }

        public class Configuration
        {
            [JsonProperty(PropertyName = en ? "Settings" : "Настройки")]
            public PluginSettings Settings = new();

            [JsonProperty(PropertyName = en ? "Event Messages" : "Сообщения о событиях")]
            public EventMessageSettings EventMessages = new();

            [JsonProperty(PropertyName = en ? "GUIAnnouncements" : "Объявления GUI")]
            public GUIAnnouncementSettings GUIAnnouncement = new();

            [JsonProperty(PropertyName = en ? "Ranked Ladder" : "Ранговая лестница")]
            public RankedLadderSettings RankedLadder = new();

            [JsonProperty(PropertyName = en ? "Skins" : "Скины")]
            public SkinSettings Skins = new();

            [JsonProperty(PropertyName = en ? "Treasure" : "Сокровища")]
            public TreasureSettings Loot = new();

            [JsonProperty(PropertyName = en ? "UI" : "Интерфейс пользователя")]
            public UISettings UI = new();

            [JsonProperty(PropertyName = en ? "Weapons" : "Оружие")]
            public WeaponSettings Weapons = new();

            [JsonProperty(PropertyName = en ? "Log Debug To File" : "Запись отладочных сообщений в файл")]
            public bool LogToFile;

            [JsonProperty(PropertyName = "Block paid and restricted content to comply with Facepunch TOS")]
            public bool BlockPaidContent = true;

            [JsonProperty(PropertyName = en ? "Destroy DLC containers once looted" : "Уничтожать DLC-контейнеры после того, как они разграблены")]
            public bool? DestroyDlcContainerOnceLooted = null;

            internal bool DestroyLootedContainer => DestroyDlcContainerOnceLooted == true;
        }

        private bool BuoyantBox;
        private string _configFilePath;
        private bool isInitialized = true;
        private Exception exConf;
        private const bool en = true;
#pragma warning disable CS0649
        private bool InstallationError;
#pragma warning restore CS0649

        protected void LoadConfig()
        {
            if (RaidableBasesHost.Instance != null)
            {
                LoadConfigHarmony();
                return;
            }
            LoadConfigLegacy();
        }

        private void LoadConfigHarmony()
        {
            isInitialized = false;
            var path = HarmonyDataLayer.GetPreferredConfigPath();
            _configFilePath = path;
            var configDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            try
            {
                if (File.Exists(path))
                {
                    config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(path));
                    if (config == null)
                        UnityEngine.Debug.LogWarning("[RaidableBases] Config file exists but deserialized to null: " + path);
                }
                else
                    UnityEngine.Debug.Log("[RaidableBases] No config at " + path + " - creating defaults.");
                if (config == null)
                {
                    config = new Configuration();
                    LoadDefaultConfig();
                    isInitialized = true; // persist defaults to HarmonyConfig/RaidableBases.json
                }
                else isInitialized = true;
            }
            catch (Exception ex)
            {
                exConf = ex;
                UnityEngine.Debug.LogError("[RaidableBases] Config load failed, using defaults: " + ex);
                config = new Configuration();
                LoadDefaultConfig();
                isInitialized = true;
            }
            ProcessConfigAfterLoad();
            if (isInitialized) SaveConfig();
        }

        private void LoadConfigLegacy()
        {
            isInitialized = false;
            try
            {
                config = new Configuration();
                LoadDefaultConfig();
                isInitialized = true;
            }
            catch (Exception ex)
            {
                exConf = ex;
                LoadDefaultConfig();
                Puts(ex.ToString());
            }
            ProcessConfigAfterLoad();
        }

        private void ProcessConfigAfterLoad()
        {
            if (config.DestroyDlcContainerOnceLooted == null)
            {
                config.DestroyDlcContainerOnceLooted = config.BlockPaidContent;
            }
            if (config.Settings.Management._AllowBuilding.HasValue)
            {
                allowBuilding = config.Settings.Management._AllowBuilding.Value;
                config.Settings.Management._AllowBuilding = null;
            }
            if (config.Settings.Management._AllowedBuildingBlocks != null)
            {
                allowBuildingBlockExceptions = config.Settings.Management._AllowedBuildingBlocks.ToList();
                config.Settings.Management._AllowedBuildingBlocks = null;
            }
            if (config.UI.Status.OffsetMin == new Vector2(43.957f, 87.056f))
            {
                config.UI.Status.OffsetMin = new(191.957f, 17.056f);
                config.UI.Status.OffsetMax = new(327.626f, 79.024f);
            }
            if (config.Settings.Management._RequireCupboardLooted != null)
            {
                config.Settings.Management.RequireCupboardLooted = config.Settings.Management._RequireCupboardLooted.Value;
                config.Settings.Management._RequireCupboardLooted = null;
            }
            if (string.IsNullOrWhiteSpace(config.Settings.EditCommand))
            {
                const int len = 8;
                const string choices = "abcdefghijklmnopqrstuvwxyz";
                char[] buffer = new char[len];
                for (int i = 0; i < len; i++)
                    buffer[i] = choices[UnityEngine.Random.Range(0, choices.Length)];
                config.Settings.EditCommand = new string(buffer);
            }
            config.Settings.Management.Inherit.RemoveAll(string.IsNullOrWhiteSpace);
            UndoSettings = new(config.Settings.Management, config.LogToFile);
            config.Settings.Management._Players = null;
        }

        protected void SaveConfig()
        {
            if (!isInitialized) return;
            if (RaidableBasesHost.Instance != null && !string.IsNullOrEmpty(_configFilePath))
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_configFilePath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
        }

        protected void LoadDefaultConfig()
        {
            config = new();
            Puts("Loaded default configuration file. Writing HarmonyConfig/RaidableBases.json. Profiles go in HarmonyData/RaidableBases/Profiles/.");
        }

        private bool? allowBuilding = null;
        private List<string> allowBuildingBlockExceptions;

        public List<LootItem> TreasureLoot
        {
            get
            {
                if (!Buildings.DifficultyLootLists.TryGetValue(RaidableMode.Random, out var lootList))
                {
                    Buildings.DifficultyLootLists[RaidableMode.Random] = lootList = new();
                    Buildings.LootID[RaidableMode.Random] = DateTime.Now;
                }

                return lootList.ToList();
            }
        }

        public List<LootItem> WeekdayLoot
        {
            get
            {
                if (!config.Loot.Daily || !Buildings.WeekdayLootLists.TryGetValue(DateTime.Now.DayOfWeek, out var lootList))
                {
                    Buildings.WeekdayLootLists[DateTime.Now.DayOfWeek] = lootList = new();
                    Buildings.LootID[DateTime.Now.DayOfWeek.ToString()] = DateTime.Now;
                }

                return lootList.ToList();
            }
        }

        #endregion

    }
}
